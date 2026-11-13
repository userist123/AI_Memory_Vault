using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// $MFT, the boot sector and the $I30 ($INDEX_ALLOCATION) of chosen directories of the system volume, read RAW from ONE shadow copy made for this
/// collection and deleted after it. Threat model: (T1) the device is opened for reading only (FileAccess.Read, share ReadWrite), nothing is ever written to it;
/// (T2) only the device of the copy this collector created (DeviceObject returned by WMI for the ShadowID it got back) and only in the form
/// <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy&lt;N&gt;</c>; (T3) the volume structures are hostile input, see <see cref="NtfsVolumeReader"/>; (T4) the copy is deleted in
/// finally and the number of shadow copies before and after is compared; (T5) output only inside the case folder, with fixed generated names, SHA-256 on the bytes
/// written and cross-checked against the hash the case computes when it registers the file; (T6) a short read or I/O error is an error, never zero padding.
/// Files land in ONE folder so that <see cref="MftParser"/> and <see cref="I30Parser"/> find <c>$MFT</c> and <c>$Boot</c> beside each $I30. Needs elevation, like the
/// Windows Search index. Nothing here can be proven without a real shadow copy: the orchestration is tested with a fake volume.
/// </summary>
public sealed class NtfsMetadataCollector : ICollector
{
    public string Name => "NtfsMetadata";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

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

    public NtfsMetadataCollector() : this(new WmiShadowCopy(), WmiShadowCopy.Count, OpenDeviceReadOnly, Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", () => DateTimeOffset.UtcNow) { }

    /// <summary>Tests: another shadow copy provider, counter, device opener and volume root.</summary>
    internal NtfsMetadataCollector(WindowsSearchIndexCollector.ISnapshot snapshot, Func<int> count, Func<string, Stream> open, string volumeRoot, Func<DateTimeOffset>? clock = null, Func<NtfsVolumeReader, IEnumerable<string>>? plan = null)
    { _snapshot = snapshot; _count = count; _open = open; _volumeRoot = volumeRoot; _clock = clock ?? (() => DateTimeOffset.UtcNow); _plan = plan; }

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
                var item = ctx.Case.RegisterStored(file, orig, "NtfsMetadata", type, TemporalType.Historical, Name, Version, notes: note);
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
