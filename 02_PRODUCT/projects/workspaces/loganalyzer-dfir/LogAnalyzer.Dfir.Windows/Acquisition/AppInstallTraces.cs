using System.Diagnostics.Eventing.Reader;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The traces Windows keeps of the application, which the application does not own and which are harder to rewrite consistently than its own file times
/// (review of PR #290): Prefetch (the .pf creation time and the run times inside it: the application RAN), Amcache (Root\InventoryApplicationFile entries for
/// the executable: the file was SEEN) and Security 4688 (the earliest retained process creation of the executable: it RAN). Every read is read-only and
/// bounded; a source that cannot be read is named in <see cref="Result.Missing"/>. The USN journal is not used: the application reads it only from an export.
/// </summary>
public static class AppInstallTraces
{
    public sealed record Result(List<AppTrace> Traces, List<string> Missing)
    {
        /// <summary>DE VERIFICAT notes (a run from another folder, a missing Prefetch file while Prefetch is active).</summary>
        public List<string> Notes { get; } = [];
    }

    /// <summary>What one Prefetch file says: its name, creation time, run times, the executable name and the full path of the executable among its referenced files.</summary>
    public sealed record PrefetchSeen(string FileName, DateTimeOffset CreatedUtc, IReadOnlyList<DateTimeOffset> Runs, string ExeName, string? ExeFullPath);

    /// <summary>
    /// The executables of the application itself (the three editions and the two older names). LogAnalyzer.LicenseManager, KeyGen and any other program never count.
    /// </summary>
    public static readonly IReadOnlyList<string> EditionExeNames =
        ["LOGANALYZER.STANDALONE.EXE", "LOGANALYZER.RETEA.EXE", "LOGANALYZER.INTERNET.EXE", "LOGANALYZER.EXE", "LOGANALYZER.AIRGAPPED.EXE", "LOGANALYZER.NETWORK.EXE"];

    /// <summary>"\PROGRAM FILES\X\A.EXE" from "C:\Program Files\X\A.exe": Prefetch stores paths as \VOLUME{…}\…, so the drive is compared away.</summary>
    private static string PathTail(string path)
    {
        var p = path.Replace('/', '\\').ToUpperInvariant();
        if (p.Length >= 2 && p[1] == ':') return p[2..];
        if (p.StartsWith(@"\VOLUME{", StringComparison.Ordinal) && p.IndexOf('}') is var close and > 0) return p[(close + 1)..];
        return p;
    }

    /// <summary>
    /// Which Prefetch files are traces of the INSTALLED application (review of PR #290, round 2): only the installed edition's executable name, and when the
    /// .pf names the full path, only the installed path. A run of an edition executable from another folder does not set the date: it is DE VERIFICAT.
    /// With Prefetch active (and other .pf files present) a missing .pf of the installed executable is DE VERIFICAT: it may have been deleted.
    /// </summary>
    public static (List<AppTrace> Traces, List<string> Notes) ClassifyPrefetch(IReadOnlyList<PrefetchSeen> files, string installedExePath, bool prefetchEnabled, int otherPrefetchFiles)
    {
        var traces = new List<AppTrace>();
        var notes = new List<string>();
        var installedName = Path.GetFileName(installedExePath).ToUpperInvariant();
        var installedTail = PathTail(installedExePath);
        bool installedSeen = false;
        foreach (var f in files)
        {
            var exe = f.ExeName.ToUpperInvariant();
            if (!EditionExeNames.Contains(exe)) continue;                                   // LicenseManager, KeyGen, other programs
            bool installed = exe == installedName && (f.ExeFullPath is null || PathTail(f.ExeFullPath) == installedTail);
            if (!installed)
            {
                notes.Add($"aplicația a rulat și din altă locație: {f.ExeFullPath ?? exe} ({f.FileName}, prima urmă {f.CreatedUtc:yyyy-MM-dd HH:mm} UTC)");
                continue;
            }
            installedSeen = true;
            traces.Add(new AppTrace($"Prefetch {f.FileName} (creat la prima rulare înregistrată)", f.CreatedUtc, AppTraceKind.WindowsRun));
            foreach (var t in f.Runs) traces.Add(new AppTrace($"Prefetch {f.FileName} (rulare)", t, AppTraceKind.WindowsRun));
        }
        if (!installedSeen && prefetchEnabled && otherPrefetchFiles > 0)
            notes.Add($"lipsește urma Prefetch a aplicației deși Prefetch este activ — posibilă ștergere (nu există niciun fișier .pf pentru {installedName}, deși alte {otherPrefetchFiles} programe au)");
        return (traces, notes);
    }

    /// <summary>Amcache entries of the INSTALLED executable only. The time is the key's LAST WRITE (Windows rewrites it), not a first sighting.</summary>
    public static List<AppTrace> ClassifyAmcache(IEnumerable<(string LowerCaseLongPath, DateTime LastWriteUtc)> entries, string installedExePath)
    {
        var tail = PathTail(installedExePath);
        return entries.Where(e => PathTail(e.LowerCaseLongPath) == tail)
            .Select(e => new AppTrace($"Amcache: {Path.GetFileName(e.LowerCaseLongPath)} (ora ultimei scrieri a intrării; Windows o rescrie)", new DateTimeOffset(DateTime.SpecifyKind(e.LastWriteUtc, DateTimeKind.Utc)), AppTraceKind.WindowsSeen))
            .ToList();
    }

    /// <summary>
    /// All Windows traces together (review of PR #290, probe B: store recreated, application reinstalled in a new folder, old .pf deleted):
    /// <list type="bullet">
    /// <item>the installed executable's Prefetch, Amcache and 4688 traces set the date (see <see cref="ClassifyPrefetch"/>, <see cref="ClassifyAmcache"/>);</item>
    /// <item>an Amcache entry of an edition executable at ANOTHER path is DE VERIFICAT „aplicația a existat/rulat anterior în altă locație” with its path and
    /// last-write time; it does not set the date (a last-write time proves no run, so NECONFORM is not justified);</item>
    /// <item>a .pf of the installed executable created long after an older Windows trace of an edition executable (another-folder Amcache or Prefetch,
    /// Security 4688, the installed Amcache entry) is DE VERIFICAT „urmă Prefetch recreată”.</item>
    /// </list>
    /// </summary>
    public static (List<AppTrace> Traces, List<string> Notes) Classify(IReadOnlyList<PrefetchSeen> prefetch, IEnumerable<(string LowerCaseLongPath, DateTime LastWriteUtc)> amcache,
        IReadOnlyList<AppTrace> security4688, string installedExePath, bool prefetchEnabled, int otherPrefetchFiles)
    {
        var am = amcache.ToList();
        var (traces, notes) = ClassifyPrefetch(prefetch, installedExePath, prefetchEnabled, otherPrefetchFiles);
        var amInstalled = ClassifyAmcache(am, installedExePath);
        traces.AddRange(amInstalled);
        traces.AddRange(security4688);
        var tail = PathTail(installedExePath);
        var older = new List<DateTimeOffset>();
        foreach (var e in am.Where(e => PathTail(e.LowerCaseLongPath) != tail && EditionExeNames.Contains(Path.GetFileName(e.LowerCaseLongPath).ToUpperInvariant())).OrderBy(e => e.LastWriteUtc))
        {
            var t = new DateTimeOffset(DateTime.SpecifyKind(e.LastWriteUtc, DateTimeKind.Utc));
            older.Add(t);
            notes.Add($"aplicația a existat/rulat anterior în altă locație: {e.LowerCaseLongPath}, {t:yyyy-MM-dd HH:mm} UTC (intrare Amcache, ora ultimei scrieri; nu dovedește o rulare)");
        }
        // other-folder Prefetch files of an edition executable also show an older presence
        older.AddRange(prefetch.Where(f => EditionExeNames.Contains(f.ExeName.ToUpperInvariant()) && f.ExeFullPath is not null && PathTail(f.ExeFullPath) != tail).Select(f => f.CreatedUtc));
        older.AddRange(security4688.Select(t => t.Utc));
        older.AddRange(amInstalled.Select(t => t.Utc));
        var installedName = Path.GetFileName(installedExePath).ToUpperInvariant();
        foreach (var f in prefetch.Where(f => f.ExeName.Equals(installedName, StringComparison.OrdinalIgnoreCase) && (f.ExeFullPath is null || PathTail(f.ExeFullPath) == tail)))
            if (older.Count > 0 && older.Min() < f.CreatedUtc - RecreatedTolerance)
                notes.Add($"urmă Prefetch recreată: {f.FileName} a fost creat la {f.CreatedUtc:yyyy-MM-dd HH:mm} UTC, deși aplicația are urme Windows din {older.Min():yyyy-MM-dd HH:mm} UTC " +
                          "(fișierul .pf anterior a fost probabil șters)");
        return (traces, notes);
    }

    /// <summary>How much older an earlier Windows trace must be than the installed .pf for the .pf to count as recreated.</summary>
    public static readonly TimeSpan RecreatedTolerance = TimeSpan.FromDays(2);

    public const int MaxPrefetchFiles = 20;
    public const long MaxAmcacheBytes = 256L * 1024 * 1024;

    public static Result Read(string? exePath = null)
    {
        var r = new Result([], []);
        exePath ??= Environment.ProcessPath ?? "";
        if (exePath.Length == 0) { r.Missing.Add("sursă lipsă: calea executabilului aplicației, de ce lipsește: procesul nu o raportează, cum se pornește: nu se aplică"); return r; }
        var (pfs, enabled, enabledValue, others) = Prefetch(r);
        var am = Amcache(r);
        var sec = new Result([], []);
        Security4688(sec, exePath);
        r.Missing.AddRange(sec.Missing);
        var (traces, notes) = Classify(pfs, am, sec.Traces, exePath, enabled, others);
        r.Traces.AddRange(traces);
        r.Notes.AddRange(notes);
        if (!traces.Any(t => t.What.StartsWith("Prefetch", StringComparison.Ordinal)))
            r.Missing.Add(enabledValue is int v0 && v0 == 0
                ? "sursă lipsă: Prefetch, de ce lipsește: este oprit pe stație (EnablePrefetcher = 0), cum se pornește: administratorul îl pornește (EnablePrefetcher = 3)"
                : $"sursă lipsă: Prefetch al aplicației instalate ({Path.GetFileName(exePath)}), de ce lipsește: nu există fișierul .pf (curățat sau încă nescris), cum se pornește: nu se poate recrea pentru rulările trecute");
        if (am.Count > 0 && !traces.Any(t => t.What.StartsWith("Amcache", StringComparison.Ordinal)))
            r.Missing.Add($"sursă lipsă: intrarea Amcache a aplicației instalate ({exePath}), de ce lipsește: Amcache nu o conține (încă nescrisă sau ștearsă), cum se pornește: nu se poate recrea");
        return r;
    }

    private static (List<PrefetchSeen> Files, bool Enabled, object? EnabledValue, int Others) Prefetch(Result r)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        object? enabledValue = null;
        try { enabledValue = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", null); }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        bool enabled = enabledValue is int ev && ev > 0;
        string[] all;
        try { all = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.pf") : []; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            r.Missing.Add($"sursă lipsă: Prefetch, de ce lipsește: folderul nu poate fi citit ({ex.Message}), cum se pornește: rulați ca administrator");
            return ([], enabled, enabledValue, 0);
        }
        var ours = all.Where(f => EditionExeNames.Any(n => Path.GetFileName(f).StartsWith(n + "-", StringComparison.OrdinalIgnoreCase))).Take(MaxPrefetchFiles).ToList();
        var seen = new List<PrefetchSeen>();
        foreach (var f in ours)
        {
            var name = Path.GetFileName(f);
            try
            {
                var pf = PrefetchParser.Read(f);
                seen.Add(new PrefetchSeen(name, new DateTimeOffset(File.GetCreationTimeUtc(f), TimeSpan.Zero), pf.RunTimesUtc, pf.ExeName, pf.ExePathGuess));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
            {
                r.Missing.Add($"sursă lipsă: {name}, de ce lipsește: nu a putut fi citit ({ex.Message}), cum se pornește: rulați ca administrator");
            }
        }
        return (seen, enabled, enabledValue, all.Length - ours.Count);
    }

    private static List<(string, DateTime)> Amcache(Result r)
    {
        var entries = new List<(string, DateTime)>();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "appcompat", "Programs", "Amcache.hve");
        try
        {
            if (!File.Exists(path)) { r.Missing.Add("sursă lipsă: Amcache.hve, de ce lipsește: fișierul nu există pe stație, cum se pornește: nu se poate (Windows îl creează singur)"); return entries; }
            if (new FileInfo(path).Length > MaxAmcacheBytes) { r.Missing.Add($"sursă lipsă: Amcache.hve, de ce lipsește: fișierul depășește {MaxAmcacheBytes / 1_048_576} MB, cum se pornește: analiza copiei din investigație"); return entries; }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var reg = new RawRegistry(fs);
            foreach (var k in reg.SubKeys(@"Root\InventoryApplicationFile").Take(50_000))
                if (k.Value("LowerCaseLongPath")?.AsText is { Length: > 0 } p) entries.Add((p, k.LastWriteUtc));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            r.Missing.Add($"sursă lipsă: Amcache.hve, de ce lipsește: fișierul este ținut deschis de registrul Windows sau nu poate fi citit ({ex.Message}), cum se pornește: investigația îl copiază prin VSS ca administrator");
        }
        return entries;
    }

    private static void Security4688(Result r, string exePath)
    {
        try
        {
            var query = new EventLogQuery("Security", PathType.LogName,
                $"*[System[EventID=4688]] and *[EventData[Data[@Name='NewProcessName']='{exePath.Replace("'", "&apos;", StringComparison.Ordinal)}']]");
            using var reader = new EventLogReader(query);
            using var first = reader.ReadEvent();
            if (first?.TimeCreated is { } t)
                r.Traces.Add(new AppTrace($"Security 4688: cea mai veche pornire păstrată a {Path.GetFileName(exePath)} (RecordID {first.RecordId})", new DateTimeOffset(t.ToUniversalTime(), TimeSpan.Zero), AppTraceKind.WindowsRun));
            else r.Missing.Add("sursă lipsă: Security 4688 pentru aplicație, de ce lipsește: auditul „Process Creation” nu a înregistrat nicio pornire a ei în jurnalul păstrat, cum se pornește: administratorul activează auditul „Process Creation”");
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            r.Missing.Add($"sursă lipsă: Security 4688, de ce lipsește: jurnalul Security nu poate fi citit ({ex.Message}), cum se pornește: rulați ca administrator");
        }
    }

    /// <summary>The installation date of the application on this station, from its audit chain, its own files and the Windows traces above.</summary>
    public static AppInstall ReadInstall()
    {
        var w = Read();
        var install = AppInstallDate.Read(null, [.. AppInstallDate.DefaultTraces(), .. w.Traces]);
        // When no Windows trace was found, say which sources were missing and why (they are what makes the check weaker).
        // The Prefetch notes (a run from another folder, a missing .pf while Prefetch is active) are always DE VERIFICAT.
        return install with { ToVerify = [.. install.ToVerify, .. w.Notes, .. install.ToVerify.Any(t => t.StartsWith("urmele Windows", StringComparison.Ordinal)) ? w.Missing : []] };
    }
}
