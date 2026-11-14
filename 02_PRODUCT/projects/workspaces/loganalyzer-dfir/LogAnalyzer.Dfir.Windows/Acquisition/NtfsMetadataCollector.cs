using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// $MFT, the boot sector and the $I30 ($INDEX_ALLOCATION) of chosen directories of the system volume, read RAW from ONE shadow copy made for this
/// collection and deleted after it. Threat model: (T1) the device is opened for reading only (FileAccess.Read, share ReadWrite), nothing is ever written to it;
/// (T2) only the device of the copy this collector created (DeviceObject returned by WMI for the ShadowID it got back) and only in the form
/// <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy&lt;N&gt;</c>; (T3) the volume structures are hostile input, see <see cref="NtfsVolumeReader"/>; (T4) the copy is deleted in
/// finally and the number of shadow copies before and after is compared; (T5) output only inside the case folder, with fixed generated names, SHA-256 on the bytes
/// written and cross-checked against the hash the case computes when it registers the file; (T6) a short read or I/O error is an error, never zero padding.
/// Files land in ONE folder so that <see cref="MftParser"/> and <see cref="I30Parser"/> find <c>$MFT</c> and <c>$Boot</c> beside each $I30. Needs elevation, like the
/// Windows Search index. The change journal (USN) of the same volume is added to the same folder as <c>usn_&lt;LETTER&gt;.csv</c> (<see cref="UsnJournalNative"/>, read only), so
/// NTFS-TIMESTOMP can use its High path on the system volume. Order: the journal is read right after $MFT and $I30 were copied, while the copy still exists (it is deleted
/// afterwards): $MFT, $I30 and USN are then as close in time as the design allows (the journal is live, so it can run slightly past the copy's moment; the manifest states
/// both moments). A failed or capped journal read is a run problem (Partial), never a failure: $MFT and $I30 stay valid. <c>$LogFile</c> (MFT record 2) is copied right after $MFT, from the same shadow copy and before the $I30 reads: it is a ring buffer (about 4.8 h / 64 MiB measured), so the later it is read the more of the history is already overwritten, and its restart header values are reported from the copied bytes only (no parsing; a bad magic keeps the file and makes the run Partial). Nothing here can be proven without a real shadow copy: the orchestration is tested with a fake volume.
/// </summary>
public sealed class NtfsMetadataCollector : ICollector
{
    public string Name => "NtfsMetadata";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    public const string UsnSourceType = "usn_journal";
    public const string LogFileSourceType = "ntfs_logfile", LogFileName = "$LogFile";
    public const string BootSourceType = "ntfs_boot", ManifestSourceType = "ntfs_manifest";
    public const string MftFileName = "$MFT", BootFileName = "$Boot", ManifestFileName = "manifest.json";
    public const int MaxDirectories = 64;

    private static readonly string[] FixedDirectories = ["\\Windows\\Temp", "\\ProgramData", "\\Users\\Public"];
    private static readonly string[] ProfileSubdirectories = ["Desktop", "Documents", "Downloads", "AppData\\Local\\Temp"];
    private static readonly string[] NotProfiles = ["Default", "Default User", "All Users", "defaultuser0"];
    private static readonly Regex DevicePattern = new(@"\A\\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy[0-9]{1,9}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>T2: the only device path this collector opens.</summary>
    internal static bool IsShadowDevice(string? device) => device is not null && DevicePattern.IsMatch(device);

    private readonly WindowsSearchIndexCollector.ISnapshot _snapshot;
    private readonly Func<int> _count;
    private readonly Func<string, Stream> _open;
    private readonly string _volumeRoot;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<NtfsVolumeReader, IEnumerable<string>>? _plan;
    private readonly Func<char, CancellationToken, UsnJournalRead>? _readUsn;   // null = the journal is not read (tests that do not inject one never touch a real journal)

    public NtfsMetadataCollector() : this(new WmiShadowCopy(), WmiShadowCopy.Count, OpenDeviceReadOnly, Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", () => DateTimeOffset.UtcNow, null, (l, c) => UsnJournalNative.Read(l, c)) { }

    /// <summary>Tests: another shadow copy provider, counter, device opener, volume root and journal reader (never the real journal of the development station).</summary>
    internal NtfsMetadataCollector(WindowsSearchIndexCollector.ISnapshot snapshot, Func<int> count, Func<string, Stream> open, string volumeRoot, Func<DateTimeOffset>? clock = null, Func<NtfsVolumeReader, IEnumerable<string>>? plan = null, Func<char, CancellationToken, UsnJournalRead>? readUsn = null)   // readUsn null: journal not read
    { _readUsn = readUsn; _snapshot = snapshot; _count = count; _open = open; _volumeRoot = volumeRoot; _clock = clock ?? (() => DateTimeOffset.UtcNow); _plan = plan; }

    /// <summary>T1: read-only handle on the device, shared with everyone (the copy is immutable). Used only with a path accepted by <see cref="IsShadowDevice"/>.</summary>
    private static Stream OpenDeviceReadOnly(string device)
    {
        if (!IsShadowDevice(device)) throw new InvalidOperationException("calea dispozitivului nu este o copie de umbră");
        return new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0);
    }

    private sealed record DirRow(string Path, string Status, string? File, long? Size, string? Sha256, string Detail, long? ValidDataLength = null);

    /// <summary>One folder per run (UTC time + short id), so a second collection in the same case never meets the read-only files of the first.</summary>
    private string NewRunFolder(CollectorContext ctx)
    {
        var raw = ctx.Case.RawDir("NtfsMetadata");
        var stamp = _clock().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var d = Path.Combine(raw, stamp + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Removes the (empty) run folder when nothing was kept in it.</summary>
    private static void TryRemoveEmpty(string dir) { try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("NTFS ($MFT, $I30): necesită drepturi de administrator", _volumeRoot);
        var o = new CollectorOutcome { Tool = "Win32_ShadowCopy + cititor NTFS brut", ToolVersion = Version, Source = _volumeRoot };
        var outDir = NewRunFolder(ctx);   // chosen before the shadow copy exists: a failed run costs no second copy
        var written = new List<string>();   // every file THIS run created in outDir (deleted on cancellation or total failure)
        var notFound = new List<string>();
        var profilesSkipped = new List<string>();
        var gaps = new List<string>();

        int? before = null, after = null;
        try { before = _count(); } catch (Exception ex) when (ex is not OperationCanceledException) { o.Errors.Add($"numărul copiilor de umbră înainte nu a putut fi aflat ({ex.Message})"); }

        string? snapId = null, device = null;
        try { (snapId, device) = _snapshot.Create(_volumeRoot); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            o.Errors.Add($"copia de umbră nu a putut fi creată ({ex.Message}); nimic nu a fost copiat");
            o.Status = EvidenceStatus.Failed;
            TryRemoveEmpty(outDir);
            return o;
        }
        o.Commands.Add($"Win32_ShadowCopy.Create(Volume={_volumeRoot}, Context=ClientAccessible) -> {snapId} {device}");
        var createdUtc = _clock();
        var dirs = new List<DirRow>();
        var skipped = new List<string>();
        NtfsBootSector? boot = null;
        EvidenceItem? mftItem = null, bootItem = null;
        string? mftSha = null; long mftSize = 0; long mftValid = 0; int mftRuns = 0;
        string? bootSha = null;
        var evidencePending = new List<(string File, string Orig, string Type, string Sha, string Note)>();
        Dictionary<string, object?>? usnManifest = null;
        Dictionary<string, object?> logFileManifest = new() { ["status"] = "NotPresent" };
        bool deleted = false;
        try
        {
            if (!IsShadowDevice(device)) { o.Errors.Add($"dispozitivul „{device}” nu are forma \\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy<N>; nu a fost deschis"); }
            else
            {
                using var vol = _open(device!);
                var reader = NtfsVolumeReader.Open(vol);
                boot = reader.Boot;

                // $Boot: the 512 bytes read from the device
                var bootPath = SafeOutPath(outDir, BootFileName);
                bootSha = Convert.ToHexString(SHA256.HashData(reader.BootSectorBytes));
                written.Add(bootPath);
                File.WriteAllBytes(bootPath, reader.BootSectorBytes);
                evidencePending.Add((bootPath, _volumeRoot + BootFileName, BootSourceType, bootSha, "Sectorul de boot (512 octeți) al volumului, citit brut din copia de umbră. Îl citesc MftParser și I30Parser când stau alături de $MFT."));

                // $MFT
                var mftPath = SafeOutPath(outDir, MftFileName);
                NtfsCopyResult mft;
                written.Add(mftPath);
                try { using var f = new FileStream(mftPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); mft = reader.CopyMft(f, ct); }
                catch { TryDelete(mftPath); throw; }
                mftSha = mft.Sha256; mftSize = mft.Size; mftValid = mft.ValidDataLength; mftRuns = mft.Runs;
                evidencePending.Add((mftPath, _volumeRoot + MftFileName, MftParser.SourceTypeName, mft.Sha256, $"$MFT:$DATA ({mft.Size:N0} octeți, {mft.Runs} run-uri) citit brut din copia de umbră {snapId}; același moment ca $I30 din acest folder."));

                // $LogFile: any problem here is reported and the run goes on ($MFT, $I30 and USN stay valid); cancellation propagates
                logFileManifest = CollectLogFile(reader, outDir, written, evidencePending, gaps, o, snapId, ct);

                // $I30 of the chosen directories
                int taken = 0, index = 0;
                foreach (var dir in (_plan ?? (r => PlanDirectories(r, profilesSkipped)))(reader))
                {
                    ct.ThrowIfCancellationRequested();
                    if (taken >= MaxDirectories) { skipped.Add(dir); continue; }
                    index++;
                    var name = $"i30_{index:D3}_{Sanitize(dir)}.i30";
                    var path = SafeOutPath(outDir, name);
                    written.Add(path);
                    try
                    {
                        NtfsDirectoryResult r;
                        using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) r = reader.CopyDirectoryIndex(dir, f, ct);
                        if (r.Status == NtfsDirectoryStatus.NotFound) { TryDelete(path); written.Remove(path); index--; notFound.Add(dir); continue; }   // absent directories do not use up the cap
                        taken++;
                        if (r.Status != NtfsDirectoryStatus.Copied || r.Copy is null)
                        {
                            TryDelete(path);
                            dirs.Add(new DirRow(dir, r.Status.ToString(), null, null, null, r.Detail));
                            continue;
                        }
                        dirs.Add(new DirRow(dir, "Copied", name, r.Copy.Size, r.Copy.Sha256, "", r.Copy.ValidDataLength));
                        if (r.Copy.ValidDataLength < r.Copy.Size)
                        {
                            var gap = $"{dir}: $INDEX_ALLOCATION are valid data length {r.Copy.ValidDataLength:N0} din {r.Copy.Size:N0} octeți; octeții de după sunt zero-uri în copie";
                            gaps.Add(gap); o.Errors.Add(gap);
                        }
                        evidencePending.Add((path, _volumeRoot.TrimEnd('\\') + dir, I30Parser.SourceTypeName, r.Copy.Sha256, $"$INDEX_ALLOCATION „$I30” brut al directorului {dir} ({r.Copy.Size:N0} octeți), din copia de umbră {snapId}; $MFT și $Boot din același folder."));
                    }
                    catch (NtfsReadException ex)
                    {
                        TryDelete(path); taken++;
                        dirs.Add(new DirRow(dir, "Error", null, null, null, ex.Message));
                        o.Errors.Add($"{dir}: $I30 necopiat ({ex.Message})");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        TryDelete(path); taken++;
                        dirs.Add(new DirRow(dir, "Error", null, null, null, ex.Message));
                        o.Errors.Add($"{dir}: $I30 necopiat, scrierea în folderul cazului a eșuat ({ex.Message})");
                    }
                }
                usnManifest = _readUsn is null ? null : CollectUsn(outDir, written, evidencePending, gaps, o, ct);
                if (skipped.Count > 0) o.Errors.Add($"plafonul de {MaxDirectories} directoare a fost atins; {skipped.Count} directoare nu au fost copiate: {string.Join(", ", skipped.Take(10))}{(skipped.Count > 10 ? ", ..." : "")}");
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled: nothing of this run stays (nothing is registered yet); the copy is deleted in finally, the exception goes on
            foreach (var f in written) TryDelete(f);
            written.Clear();
            throw;
        }
        catch (Exception ex) when (ex is NtfsReadException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            o.Errors.Add($"citirea brută a volumului a eșuat ({ex.Message}); {(mftSha is null ? "nu s-a copiat nimic" : "probele deja copiate rămân")}");
            if (mftSha is null) { foreach (var f in written) TryDelete(f); evidencePending.Clear(); written.Clear(); }   // without $MFT the rest has no moment of reference
        }
        finally
        {
            // T4: the copy is deleted whatever happened above (an exception in the read, a cancellation)
            if (written.Count == 0) TryRemoveEmpty(outDir);
            try { _snapshot.Delete(snapId!); deleted = true; o.Commands.Add($"Win32_ShadowCopy {snapId}: șters"); }
            catch (Exception ex) { o.Errors.Add($"copia de umbră {snapId} nu a putut fi ștearsă ({ex.Message}); ștergeți-o manual (vssadmin delete shadows /shadow={snapId})"); }
            try { after = _count(); } catch (Exception ex) when (ex is not OperationCanceledException) { o.Errors.Add($"numărul copiilor de umbră după nu a putut fi aflat ({ex.Message})"); }
        }
        if (before is int b && after is int a && a != b) o.Errors.Add($"numărul copiilor de umbră diferă: {b} înainte, {a} după; verificați cu „vssadmin list shadows”");

        // register the evidence (hash re-computed by the case, compared with the hash of the bytes written)
        foreach (var (file, orig, type, sha, note) in evidencePending)
        {
            try
            {
                var item = ctx.Case.RegisterStored(file, orig, type == UsnSourceType ? "USN:" + UsnJournalCollector.Letter(_volumeRoot) : "NtfsMetadata", type, TemporalType.Historical, Name, Version, notes: note, timezone: type == UsnSourceType ? TimeZoneInfo.Local.Id : null);
                if (!string.Equals(item.Sha256, sha, StringComparison.OrdinalIgnoreCase)) { o.Errors.Add($"{Path.GetFileName(file)}: SHA-256 din caz ({item.Sha256}) diferă de cel al octeților scriși ({sha})"); continue; }
                o.Evidence.Add(item);
                if (type == MftParser.SourceTypeName) mftItem = item; else if (type == BootSourceType) bootItem = item;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { o.Errors.Add($"{Path.GetFileName(file)}: nu a putut fi înregistrat ({ex.Message})"); }
        }

        // manifest (after the deletion, so it states what really happened)
        if (mftItem is not null)
        {
            try
            {
                var tz = TimeZoneInfo.Local;
                var manifest = new
                {
                    schema = "loganalyzer.ntfs-metadata/1",
                    collector = Name, version = Version,
                    volume = _volumeRoot, shadowId = snapId, shadowDevice = device,
                    shadowCreatedUtc = createdUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    stationTimeZone = tz.Id, stationUtcOffset = tz.GetUtcOffset(createdUtc).ToString(),
                    shadowCopiesBefore = before, shadowCopiesAfter = after, shadowDeleted = deleted,
                    boot = boot is null ? null : new { bytesPerSector = boot.BytesPerSector, sectorsPerCluster = boot.SectorsPerCluster, clusterSize = boot.ClusterSize, totalSectors = boot.TotalSectors, mftLcn = boot.MftLcn, mftRecordSize = boot.MftRecordSize, volumeSerial = boot.VolumeSerial.ToString("X16") },
                    files = new object[] { new { name = BootFileName, size = 512, sha256 = bootSha }, new { name = MftFileName, size = mftSize, validDataLength = mftValid, runs = mftRuns, sha256 = mftSha } },
                    directories = dirs.Select(d => new { path = d.Path, status = d.Status, file = d.File, size = d.Size, validDataLength = d.ValidDataLength, sha256 = d.Sha256, detail = d.Detail }).ToArray(),
                    directoriesNotFound = notFound.ToArray(),
                    profilesSkipped = profilesSkipped.ToArray(),
                    usn = usnManifest,
                    logFile = logFileManifest,
                    gaps = gaps.ToArray(),
                    directoriesSkippedOverCap = skipped.ToArray(),
                    errors = o.Errors.ToArray(),
                };
                var mpath = SafeOutPath(outDir, ManifestFileName);
                File.WriteAllText(mpath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                o.Evidence.Add(ctx.Case.RegisterStored(mpath, _volumeRoot + "$MFT+$I30", "NtfsMetadata", ManifestSourceType, TemporalType.Historical, Name, Version, notes: "Manifest: SHA-256 ale fișierelor, ID-ul copiei de umbră, ora (UTC) și fusul stației."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { o.Errors.Add($"manifestul nu a putut fi scris ({ex.Message})"); }
        }

        bool core = mftItem is not null && bootItem is not null;
        if (!core && o.Evidence.Count == 0) { foreach (var f in written) TryDelete(f); TryRemoveEmpty(outDir); }   // a failed run leaves nothing that is not registered
        o.Status = !core ? EvidenceStatus.Failed : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }

    /// <summary>
    /// $LogFile into the fixed file <c>$LogFile</c> of the run folder. Never throws except on cancellation: a failure deletes the file and goes to <c>o.Errors</c> (Partial);
    /// a missing restart-page magic keeps the file (it is evidence) and goes to <c>o.Errors</c> and <paramref name="gaps"/>. All restart values come from the copied bytes.
    /// </summary>
    private Dictionary<string, object?> CollectLogFile(NtfsVolumeReader reader, string outDir, List<string> written, List<(string File, string Orig, string Type, string Sha, string Note)> pending, List<string> gaps, CollectorOutcome o, string? snapId, CancellationToken ct)
    {
        var m = new Dictionary<string, object?> { ["status"] = "Error" };
        string? path = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            path = SafeOutPath(outDir, LogFileName);
            written.Add(path);
            NtfsLogFileResult r;
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) r = reader.CopyLogFile(f, ct);
            var c = r.Copy;
            m["status"] = "Copied"; m["file"] = LogFileName; m["size"] = c.Size; m["validDataLength"] = c.ValidDataLength; m["runs"] = c.Runs; m["sha256"] = c.Sha256;
            m["systemPageSize"] = r.SystemPageSize; m["logPageSize"] = r.LogPageSize; m["version"] = $"{r.VersionMajor}.{r.VersionMinor}";
            m["restartPages"] = r.RestartPages.Select(p => new { offset = p.Offset, magic = p.Magic }).ToArray();
            m["error"] = null;
            pending.Add((path, _volumeRoot + LogFileName, LogFileSourceType, c.Sha256, $"$LogFile ({c.Size:N0} octeți, {c.Runs} run-uri) copiat brut din copia de umbră {snapId}, la același moment ca $MFT din acest folder; analiza vine ulterior."));
            foreach (var p in r.RestartPages.Where(p => !p.IsValid))
            {
                var gap = $"$LogFile nu are pagina de restart validă la offset {p.Offset} (magie {p.Magic})";
                gaps.Add(gap); o.Errors.Add(gap);
            }
            if (!r.SecondPageLocated)
            {
                var gap = $"$LogFile: a doua pagină de restart nu poate fi localizată (system_page_size {r.SystemPageSize} nu este o putere a lui 2 între 512 și 65536 sub mărimea fișierului)";
                gaps.Add(gap); o.Errors.Add(gap);
            }
            if (c.ValidDataLength < c.Size)
            {
                var gap = $"$LogFile are valid data length {c.ValidDataLength:N0} din {c.Size:N0} octeți; octeții de după sunt zero-uri în copie";
                gaps.Add(gap); o.Errors.Add(gap);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is NtfsReadException or IOException or UnauthorizedAccessException)
        {
            if (path is not null) { TryDelete(path); written.Remove(path); }
            m = new Dictionary<string, object?> { ["status"] = "Error", ["error"] = ex.Message };
            o.Errors.Add($"$LogFile necopiat ({ex.Message}); $MFT, $I30 și USN continuă");
        }
        return m;
    }

    /// <summary>
    /// The change journal of the system volume into <c>usn_&lt;L&gt;.csv</c> in the run folder (same CSV as <see cref="UsnJournalCollector"/>, with the exact UTC column).
    /// Never throws except on cancellation: the outcome is the manifest block, problems go to <c>o.Errors</c> (Partial) and, for a capped read, to <paramref name="gaps"/>.
    /// </summary>
    private Dictionary<string, object?> CollectUsn(string outDir, List<string> written, List<(string File, string Orig, string Type, string Sha, string Note)> pending, List<string> gaps, CollectorOutcome o, CancellationToken ct)
    {
        var letter = UsnJournalCollector.Letter(_volumeRoot);
        var m = new Dictionary<string, object?> { ["status"] = "NotRead", ["letter"] = letter.Length > 0 ? letter : null };
        if (letter.Length != 1) { var e = $"jurnalul USN nu a fost citit: litera volumului „{_volumeRoot}” nu poate fi stabilită"; m["error"] = e; o.Errors.Add(e); return m; }
        o.Commands.Add($"FSCTL_READ_USN_JOURNAL \\\\.\\{letter}: (doar citire)");
        string? dest = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var read = _readUsn!(letter[0], ct);
            var readAt = _clock();
            dest = SafeOutPath(outDir, $"usn_{letter}.csv");
            written.Add(dest);
            var tz = TimeZoneInfo.Local;
            using (var w = new StreamWriter(new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false)))
                UsnJournalNative.WriteCsv(w, read.Info, read.Records, tz, $"volumul de sistem {_volumeRoot}", read.Incomplete);
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest)));
            pending.Add((dest, $"{letter}: jurnalul USN al volumului de sistem", UsnSourceType, sha,
                $"Jurnalul USN al volumului {letter}:, citit direct (doar citire) imediat după copierea $MFT și $I30 din acest folder; coloana „Time stamp UTC” are ora exactă."));
            m["status"] = read.Complete ? "Read" : "Incomplete";
            m["file"] = Path.GetFileName(dest); m["sha256"] = sha;
            m["journalId"] = "0x" + read.Info.JournalId.ToString("x16"); m["firstUsn"] = read.Info.FirstUsn; m["nextUsn"] = read.Info.NextUsn;
            m["records"] = read.Records.Count; m["incomplete"] = !read.Complete; m["incompleteReason"] = read.Complete ? null : read.Incomplete;
            m["readUtc"] = readAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            if (!read.Complete)
            {
                var gap = $"jurnalul USN {letter}: este incomplet: {read.Incomplete}";
                gaps.Add(gap); o.Errors.Add(gap);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (dest is not null) { TryDelete(dest); written.Remove(dest); }
            var why = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1179 } ? "volumul nu are jurnal USN activ (nu s-a creat unul)" : $"{ex.GetType().Name}: {ex.Message}";
            var e = $"jurnalul USN {letter}: nu a putut fi citit ({why}); $MFT și $I30 rămân valabile, dar regula NTFS-TIMESTOMP nu poate folosi calea cu jurnal";
            m["status"] = "Error"; m["error"] = e; o.Errors.Add(e);
        }
        return m;
    }

    /// <summary>Fixed directories first, then each profile under \Users (links and service profiles left out), in a stable order. Existence is decided by the reader.</summary>
    internal static IEnumerable<string> PlanDirectories(NtfsVolumeReader reader, List<string>? skippedProfiles = null)
    {
        // a raw profile name with a separator or a control character would be split into other components: skipped and reported
        static bool Hostile(string n) => n.Any(c => c is '\\' or '/' || char.IsControl(c));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in FixedDirectories) if (seen.Add(d)) yield return d;
        var all = reader.ListChildDirectories("\\Users").ToList();
        foreach (var bad in all.Where(Hostile)) skippedProfiles?.Add(System.Text.Json.JsonSerializer.Serialize(bad));   // JSON-escaped: control characters stay visible
        var profiles = all.Where(p => !Hostile(p))
            .Where(p => !NotProfiles.Contains(p, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var p in profiles)
            foreach (var s in ProfileSubdirectories)
            {
                var d = "\\Users\\" + p + "\\" + s;
                if (seen.Add(d)) yield return d;
            }
    }

    private static string Sanitize(string path)
    {
        var s = Regex.Replace(path.Trim('\\'), @"[^A-Za-z0-9._-]+", "_");
        return s.Length > 80 ? s[..80] : s;
    }

    /// <summary>T5: the output path is the case folder plus a name this collector generated; anything else is refused.</summary>
    private static string SafeOutPath(string outDir, string name)
    {
        var full = Path.GetFullPath(Path.Combine(outDir, name));
        if (!full.StartsWith(Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || name.Contains('\\') || name.Contains('/'))
            throw new IOException("calea de ieșire iese din folderul cazului");
        return full;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
