using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The Windows Search index of the live station, read by <see cref="WindowsSearchIndexParser"/>:
/// <c>%ProgramData%\Microsoft\Search\Data\Applications\Windows\Windows.db</c> with its -wal and -shm. The WSearch service keeps the database open, so all files are
/// copied from ONE Volume Shadow Copy made for this collection and deleted after it (the service is never stopped); without that copy only the database is
/// copied (esentutl /vss), never a -wal from another moment. Windows-gather.db (with -wal / -shm) is copied from the same shadow copy and only from it: without the
/// copy there is no guarantee that it matches the moment of Windows.db, so it is not copied at all (<see cref="WindowsSearchGatherParser"/>). Both databases get the same
/// moment key in their notes (the shadow copy id) so a rule can compare them. A Windows.edb (ESE, older systems) is copied only as evidence, without a parser. Files over
/// <see cref="MaxFileBytes"/> are noted, not copied. Reparse points are not followed; a missing folder is "not available", not an error. Needs elevation, like SRUM.
/// </summary>
public sealed class WindowsSearchIndexCollector : ICollector
{
    public string Name => WindowsSearchIndexParser.CollectedBy;
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;
    public const string WalSourceType = "windows_search_wal", ShmSourceType = "windows_search_shm", EdbSourceType = "windows_search_edb";

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Microsoft", "Search", "Data", "Applications", "Windows");

    /// <summary>One file to copy: name beside the database, evidence type and the note kept with the evidence.</summary>
    internal readonly record struct Planned(string Source, string FileName, string SourceType, string Note);

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var dir = _dir ?? DefaultDir;
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("Windows Search: necesită drepturi de administrator", dir);
        if (!Directory.Exists(dir)) return CollectorOutcome.NotAvailable("sursă lipsă: folderul indexului Windows Search nu există (serviciul de căutare neinstalat sau fără index)", dir);
        var o = new CollectorOutcome { Tool = "esentutl.exe", ToolVersion = ToolRunner.FileVersion(ToolRunner.System32("esentutl.exe")), Source = dir };
        var plan = Plan(dir, MaxFileBytes, o);
        if (plan.Count == 0)
        {
            o.Status = o.Errors.Count > 0 ? EvidenceStatus.Failed : EvidenceStatus.Empty;
            return o;
        }
        var outDir = ctx.Case.RawDir("WindowsSearch");
        var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
        bool dbCopied = false;
        // registered only after the loop: a database whose planned -wal was not copied must not keep the same-snapshot note and moment key
        var pending = new List<(string Dest, Planned P, string Note)>();
        var copiedTypes = new HashSet<string>(StringComparer.Ordinal);

        // Windows.db and its -wal must come from ONE snapshot: a -wal copied after a checkpoint in a later snapshot rebuilds a state that never
        // existed and still passes every SQLite check (review r1, reproduced). One ClientAccessible shadow copy is made, every file is read from it,
        // and it is deleted. Without it, only the database is copied (esentutl /vss) and the -wal / -shm are left out on purpose.
        string? snapId = null, snapDevice = null;
        try { (snapId, snapDevice) = _snapshot.Create(Path.GetPathRoot(dir)!); }
        catch (Exception ex) when (ex is not OperationCanceledException) { o.Errors.Add($"copia de umbră comună nu a putut fi creată ({ex.Message}); se copiază doar baza, fără jurnalul WAL"); }
        if (snapId is not null) o.Commands.Add($"Win32_ShadowCopy.Create(Volume={Path.GetPathRoot(dir)}, Context=ClientAccessible) -> {snapId} {snapDevice}");
        try
        {
            foreach (var p in plan)
            {
                ct.ThrowIfCancellationRequested();
                bool gather = IsGatherType(p.SourceType);
                bool side = p.SourceType is WalSourceType or ShmSourceType;
                if (snapDevice is null && gather) { o.Errors.Add($"{p.FileName}: necopiat, fiindcă nu există o copie de umbră comună cu Windows.db (nu se știe dacă baza gather este din același moment; compararea celor două baze nu ar fi sigură)"); continue; }
                if (snapDevice is null && side) { o.Errors.Add($"{p.FileName}: necopiat, fiindcă nu există o copie de umbră comună cu baza (un jurnal din alt moment ar amesteca stările)"); continue; }
                var dest = Path.Combine(outDir, p.FileName);
                string note = p.Note;
                if (snapDevice is not null)
                {
                    int rootLen = Path.GetPathRoot(p.Source)!.Length;   // "C:\" -> keep the leading separator of the rest
                    var inSnap = snapDevice.TrimEnd('\\') + p.Source[(rootLen - 1)..];
                    var err = CopyCapped(inSnap, dest, MaxFileBytes, ct);
                    if (err is not null) { o.Errors.Add($"{p.FileName}: {err}"); continue; }
                    note += $" Copiat din copia de umbră {snapId}, {(gather ? WindowsSearchGatherParser.GatherSameSnapshotNote : WindowsSearchIndexParser.SameSnapshotNote)}.";
                    // the common moment of the two databases: the same key in the notes of both, written by this collector only
                    if (p.SourceType is WindowsSearchIndexParser.SourceTypeName or WindowsSearchGatherParser.SourceTypeName) note += $" {WindowsSearchGatherParser.SnapshotKeyPrefix}{snapId}.";
                }
                else
                {
                    var run = ToolRunner.Run(ToolRunner.System32("esentutl.exe"), ["/y", p.Source, "/vss", "/d", dest], logDir, "esentutl_wsearch_" + p.FileName, TimeSpan.FromMinutes(10), ct);
                    o.Commands.Add(run.CommandLine);
                    if (run.ExitCode != 0 || !File.Exists(dest)) { o.Errors.Add($"{p.FileName}: esentutl exit {run.ExitCode}"); continue; }
                    if (p.SourceType == WindowsSearchIndexParser.SourceTypeName) note += " Copiat fără jurnalul WAL: ultimele modificări ale indexului pot lipsi.";
                }
                if (UserArtifactsCollector.ReparseOnPath(p.Source).Length > 0)
                {
                    try { File.Delete(dest); } catch (IOException) { }
                    o.Errors.Add($"{p.FileName}: a devenit o legătură (reparse point) în timpul copierii; copia a fost eliminată.");
                    continue;
                }
                pending.Add((dest, p, note));
                copiedTypes.Add(p.SourceType);
            }
            foreach (var (dest, p, note0) in pending)
            {
                var note = note0;
                // the journal was planned (it existed) but not copied: the database alone is from the snapshot, the pair is not; without the journal its
                // newest rows are missing, and comparing it with Windows-gather.db would report every file indexed since the last checkpoint as missing
                var walType = p.SourceType == WindowsSearchIndexParser.SourceTypeName ? WalSourceType : p.SourceType == WindowsSearchGatherParser.SourceTypeName ? WindowsSearchGatherParser.WalSourceType : null;
                if (walType is not null && plan.Any(x => x.SourceType == walType) && !copiedTypes.Contains(walType))
                {
                    note = note.Replace($" {WindowsSearchGatherParser.SnapshotKeyPrefix}{snapId}.", "", StringComparison.Ordinal)
                               .Replace($", {WindowsSearchIndexParser.SameSnapshotNote}.", ".", StringComparison.Ordinal)
                               .Replace($", {WindowsSearchGatherParser.GatherSameSnapshotNote}.", ".", StringComparison.Ordinal)
                           + " Jurnalul WAL nu a putut fi copiat: ultimele modificări ale bazei pot lipsi, iar baza nu se compară cu cealaltă bază a indexului.";
                }
                o.Evidence.Add(ctx.Case.RegisterStored(dest, p.Source, "WindowsSearch", p.SourceType, TemporalType.Historical, Name, Version, notes: note));
                if (p.SourceType == WindowsSearchIndexParser.SourceTypeName) dbCopied = true;
            }
        }
        finally
        {
            if (snapId is not null)
            {
                try { _snapshot.Delete(snapId); o.Commands.Add($"Win32_ShadowCopy {snapId}: șters"); }
                catch (Exception ex) { o.Errors.Add($"copia de umbră {snapId} nu a putut fi ștearsă ({ex.Message}); ștergeți-o manual (vssadmin delete shadows /shadow={snapId})"); }
            }
        }
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : o.Errors.Count > 0 || !dbCopied && plan.Any(x => x.SourceType == WindowsSearchIndexParser.SourceTypeName) ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }

    /// <summary>Copies at most <paramref name="max"/> bytes; null on success, else why not (the partial copy is removed).</summary>
    private static string? CopyCapped(string source, string dest, long max, CancellationToken ct)
    {
        try
        {
            bool tooBig;
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
            {
                var buf = new byte[1 << 20]; long total = 0; int n;
                while ((n = src.Read(buf, 0, (int)Math.Min(buf.Length, max + 1 - total))) > 0) { ct.ThrowIfCancellationRequested(); dst.Write(buf, 0, n); total += n; if (total > max) break; }
                tooBig = total > max;
            }
            if (tooBig) { File.Delete(dest); return $"peste plafonul de {max:N0} octeți în copia de umbră; necopiat"; }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch (IOException) { }
            return $"nu a putut fi copiat din copia de umbră ({ex.Message})";
        }
    }

    /// <summary>Creates and deletes one Volume Shadow Copy. Replaced in tests; the default uses WMI Win32_ShadowCopy (ClientAccessible).</summary>
    internal interface ISnapshot
    {
        (string Id, string DeviceObject) Create(string volumeRoot);
        void Delete(string id);
    }

    private readonly ISnapshot _snapshot;
    private readonly string? _dir;
    public WindowsSearchIndexCollector() : this(new WmiSnapshot()) { }
    /// <summary>Tests: another snapshot provider and another index folder.</summary>
    internal WindowsSearchIndexCollector(ISnapshot snapshot, string? dir = null) { _snapshot = snapshot; _dir = dir; }

    private sealed class WmiSnapshot : ISnapshot
    {
        public (string Id, string DeviceObject) Create(string volumeRoot)
        {
            using var cls = new System.Management.ManagementClass(@"\\.\root\cimv2", "Win32_ShadowCopy", null);
            using var args = cls.GetMethodParameters("Create");
            args["Volume"] = volumeRoot; args["Context"] = "ClientAccessible";
            using var res = cls.InvokeMethod("Create", args, null);
            var rc = Convert.ToUInt32(res["ReturnValue"]);
            if (rc != 0) throw new InvalidOperationException($"Win32_ShadowCopy.Create a întors {rc}");
            var id = (string)res["ShadowID"];
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(@"\\.\root\cimv2", $"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID='{id.Replace("'", "")}'");
                using var col = s.Get();
                foreach (System.Management.ManagementObject m in col) using (m) if (m["DeviceObject"] is string dev && dev.Length > 0) return (id, dev);
            }
            catch (Exception) { TryDeleteOrphan(id); throw; }
            // created but not found: delete it here, the caller never learns its id (a ClientAccessible copy is persistent and would stay behind)
            var deleted = TryDeleteOrphan(id);
            throw new InvalidOperationException($"copia de umbră {id} a fost creată, dar nu poate fi găsită" + (deleted ? "; a fost ștearsă" : $"; ștergeți-o manual (vssadmin delete shadows /shadow={id})"));
        }

        public void Delete(string id)
        {
            using var s = new System.Management.ManagementObjectSearcher(@"\\.\root\cimv2", $"SELECT * FROM Win32_ShadowCopy WHERE ID='{id.Replace("'", "")}'");
            using var col = s.Get();
            foreach (System.Management.ManagementObject m in col) using (m) m.Delete();
        }

        private bool TryDeleteOrphan(string id)
        {
            try { Delete(id); return true; } catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// The files to copy from <paramref name="dir"/>: Windows.db (and -wal, -shm only beside it), and Windows.edb as evidence. Refusals (link, over the size cap, unreadable)
    /// go to <paramref name="o"/>.Errors. Nothing is read or copied here. Internal for the tests.
    /// </summary>
    internal static List<Planned> Plan(string dir, long maxBytes, CollectorOutcome o)
    {
        var plan = new List<Planned>();
        var link = UserArtifactsCollector.ReparseOnPath(dir);
        if (link.Length > 0) { o.Errors.Add($"legătura „{link}” pe drumul spre indexul Windows Search nu este urmată"); return plan; }
        bool dbOk = Check(dir, "Windows.db", maxBytes, o, out var dbPath);
        if (dbOk)
        {
            plan.Add(new(dbPath, "Windows.db", WindowsSearchIndexParser.SourceTypeName, "Indexul Windows Search (SQLite), copiat prin VSS; serviciul WSearch nu a fost oprit."));
            if (Check(dir, "Windows.db-wal", maxBytes, o, out var wal)) plan.Add(new(wal, "Windows.db-wal", WalSourceType, "Jurnalul WAL al indexului Windows Search: parserul îl pune lângă copia de lucru."));
            if (Check(dir, "Windows.db-shm", maxBytes, o, out var shm)) plan.Add(new(shm, "Windows.db-shm", ShmSourceType, "Memoria partajată (-shm) a indexului Windows Search."));
        }
        // Windows-gather.db (Windows 11): planned like Windows.db, but copied only from the shadow copy (see Collect); absent on older systems, which is not an error
        if (Check(dir, WindowsSearchGatherParser.DatabaseFileName, maxBytes, o, out var gdb))
        {
            plan.Add(new(gdb, WindowsSearchGatherParser.DatabaseFileName, WindowsSearchGatherParser.SourceTypeName, "Baza gatherer a Windows Search (Windows-gather.db, SQLite), copiată din aceeași copie de umbră ca Windows.db."));
            if (Check(dir, "Windows-gather.db-wal", maxBytes, o, out var gwal)) plan.Add(new(gwal, "Windows-gather.db-wal", WindowsSearchGatherParser.WalSourceType, "Jurnalul WAL al bazei gather Windows Search: parserul îl pune lângă copia de lucru."));
            if (Check(dir, "Windows-gather.db-shm", maxBytes, o, out var gshm)) plan.Add(new(gshm, "Windows-gather.db-shm", WindowsSearchGatherParser.ShmSourceType, "Memoria partajată (-shm) a bazei gather Windows Search."));
        }
        // the old ESE index: kept as evidence only, no parser reads it
        if (Check(dir, "Windows.edb", maxBytes, o, out var edb)) plan.Add(new(edb, "Windows.edb", EdbSourceType, "Indexul Windows Search în format ESE (Windows.edb): păstrat doar ca probă, nu există parser pentru el."));
        return plan;
    }

    private static bool IsGatherType(string t) => t is WindowsSearchGatherParser.SourceTypeName or WindowsSearchGatherParser.WalSourceType or WindowsSearchGatherParser.ShmSourceType;

    /// <summary>True when the file exists, is not a link and is not over the cap; a missing file is not an error.</summary>
    private static bool Check(string dir, string name, long maxBytes, CollectorOutcome o, out string path)
    {
        path = Path.Combine(dir, name);
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return false;
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint)) { o.Errors.Add($"{name} este o legătură (reparse point), nu este urmată"); return false; }
            if (fi.Length > maxBytes) { o.Errors.Add($"{name}: {fi.Length:N0} octeți, peste plafonul de {maxBytes:N0}; notat, necopiat"); return false; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            o.Errors.Add($"{name}: nu poate fi citit ({ex.Message})");
            return false;
        }
    }
}
