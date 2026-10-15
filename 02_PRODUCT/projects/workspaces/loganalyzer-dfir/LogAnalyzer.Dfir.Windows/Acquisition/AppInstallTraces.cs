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
    public sealed record Result(List<AppTrace> Traces, List<string> Missing);

    public const int MaxPrefetchFiles = 20;
    public const long MaxAmcacheBytes = 256L * 1024 * 1024;

    public static Result Read(string? exePath = null)
    {
        var r = new Result([], []);
        exePath ??= Environment.ProcessPath ?? "";
        var exeName = Path.GetFileName(exePath);
        Prefetch(r);
        Amcache(r);
        if (exeName.Length > 0) Security4688(r, exePath);
        return r;
    }

    private static void Prefetch(Result r)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        string[] files;
        try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "LOGANALYZER*.pf").Take(MaxPrefetchFiles).ToArray() : []; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            r.Missing.Add($"sursă lipsă: Prefetch (LOGANALYZER*.pf), de ce lipsește: folderul nu poate fi citit ({ex.Message}), cum se pornește: rulați ca administrator");
            return;
        }
        if (files.Length == 0)
        {
            object? enabled = null;
            try { enabled = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", null); }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
            r.Missing.Add(enabled is int v && v == 0
                ? "sursă lipsă: Prefetch, de ce lipsește: este oprit pe stație (EnablePrefetcher = 0), cum se pornește: administratorul îl pornește (EnablePrefetcher = 3)"
                : "sursă lipsă: Prefetch al aplicației, de ce lipsește: nu există niciun fișier LOGANALYZER*.pf (curățat sau încă nescris), cum se pornește: nu se poate recrea pentru rulările trecute");
            return;
        }
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            try
            {
                r.Traces.Add(new AppTrace($"Prefetch {name} (creat la prima rulare înregistrată)", new DateTimeOffset(File.GetCreationTimeUtc(f), TimeSpan.Zero), AppTraceKind.WindowsRun));
                var pf = PrefetchParser.Read(f);
                foreach (var t in pf.RunTimesUtc) r.Traces.Add(new AppTrace($"Prefetch {name} (rulare)", t, AppTraceKind.WindowsRun));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
            {
                r.Missing.Add($"sursă lipsă: {name}, de ce lipsește: nu a putut fi citit ({ex.Message}), cum se pornește: rulați ca administrator");
            }
        }
    }

    private static void Amcache(Result r)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "appcompat", "Programs", "Amcache.hve");
        try
        {
            if (!File.Exists(path)) { r.Missing.Add("sursă lipsă: Amcache.hve, de ce lipsește: fișierul nu există pe stație, cum se pornește: nu se poate (Windows îl creează singur)"); return; }
            if (new FileInfo(path).Length > MaxAmcacheBytes) { r.Missing.Add($"sursă lipsă: Amcache.hve, de ce lipsește: fișierul depășește {MaxAmcacheBytes / 1_048_576} MB, cum se pornește: analiza copiei din investigație"); return; }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var reg = new RawRegistry(fs);
            int n = 0;
            foreach (var k in reg.SubKeys(@"Root\InventoryApplicationFile"))
            {
                if (++n > 50_000) break;
                var p = k.Value("LowerCaseLongPath")?.AsText ?? "";
                var file = Path.GetFileName(p);
                if (file.StartsWith("loganalyzer", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    r.Traces.Add(new AppTrace($"Amcache: {file} (intrare scrisă la prima apariție a fișierului)", new DateTimeOffset(k.LastWriteUtc, TimeSpan.Zero), AppTraceKind.WindowsSeen));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            r.Missing.Add($"sursă lipsă: Amcache.hve, de ce lipsește: fișierul este ținut deschis de registrul Windows sau nu poate fi citit ({ex.Message}), cum se pornește: investigația îl copiază prin VSS ca administrator");
        }
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
        return install.ToVerify.Any(t => t.StartsWith("urmele Windows", StringComparison.Ordinal)) ? install with { ToVerify = [.. install.ToVerify, .. w.Missing] } : install;
    }
}
