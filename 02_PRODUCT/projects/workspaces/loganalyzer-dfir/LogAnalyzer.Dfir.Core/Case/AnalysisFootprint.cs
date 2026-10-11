using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// F10: what the application wrote or changed on the station it runs on while analysing it ("amprenta analizei pe țintă"). Derived from the case itself
/// (every acquired file is in the evidence index with how it was obtained) plus the fixed list of temporary folders and registry areas the code uses.
/// Paths are generalised (%USERPROFILE%, %LOCALAPPDATA%, %TEMP%, &lt;stație&gt;) so the section can be shared without the examiner's or target's names.
/// </summary>
public sealed record AnalysisFootprint(string CaseFolder, bool LiveCollection, int ExportedLogs, int SavedHives, int OtherCopies, int Imported, int ReportsWritten)
{
    /// <summary>Temporary working folders the parsers create (and remove) on the machine that runs the analysis.</summary>
    public static readonly string[] TempFolders = [@"%TEMP%\LogAnalyzer\evtx_repair", @"%TEMP%\LogAnalyzer\sqlite_work", @"%TEMP%\ladfir_srum_*"];

    /// <summary>Registry areas read during live collection. Read only: nothing is created, changed or deleted.</summary>
    public static readonly string[] RegistryReads =
    [
        @"HKLM\SYSTEM (USBSTOR, MountedDevices, Enum\USB; copie cu reg save în folderul cazului)",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList",
        @"HKLM\SOFTWARE\Microsoft\Windows Portable Devices\Devices",
        @"HKU\<profil>\...\Explorer\MountPoints2, UserAssist, BAM (din copiile hive-urilor)",
    ];

    public static AnalysisFootprint ForCase(CaseWorkspace ws)
    {
        var ev = ws.LoadEvidence();
        var acquired = ev.Where(e => e.AcquisitionMethod == "acquired").ToList();
        int logs = acquired.Count(e => e.SourceType == "evtx");
        int hives = acquired.Count(e => e.SourceType.EndsWith("_hive", StringComparison.OrdinalIgnoreCase) || e.Source.StartsWith("Registry:", StringComparison.OrdinalIgnoreCase));
        int reports = 0;
        try
        {
            if (File.Exists(ws.CustodyJsonlPath))
                reports = File.ReadLines(ws.CustodyJsonlPath).Count(l => l.Contains("\"output.written\"", StringComparison.Ordinal));
        }
        catch (IOException) { }
        return new AnalysisFootprint(Generalize(ws.Root), acquired.Count > 0, logs, hives, acquired.Count - logs - hives, ev.Count - acquired.Count, reports);
    }

    public IReadOnlyList<string> Lines()
    {
        var l = new List<string>
        {
            $"Folderul cazului (scris pe calculatorul care rulează aplicația): {CaseFolder}.",
            LiveCollection
                ? $"Colectare de pe stația analizată: {ExportedLogs} jurnale exportate (.evtx, EvtExportLog: copie, canalul nu este golit sau modificat), " +
                  $"{SavedHives} copii de hive (reg save: copie, registrul nu este modificat), {OtherCopies} alte copii de fișiere — toate în folderul cazului."
                : "Nicio colectare live: probele au fost aduse ca fișiere; pe stația de proveniență aplicația nu a scris nimic.",
            "Foldere temporare de lucru (create la nevoie, șterse la sfârșitul citirii): " + string.Join(", ", TempFolders) + ".",
            "Registry: doar citire — " + string.Join("; ", RegistryReads) + ". Aplicația nu creează, nu modifică și nu șterge chei sau valori pe țintă.",
            "Efecte secundare inevitabile: sistemul de operare poate înregistra singur pornirea aplicației și a uneltelor ei (de ex. 4688, Prefetch, jurnalul Application); " +
            "acestea aparțin analizei, nu incidentului, și au ora de după începerea cazului.",
        };
        if (Imported > 0) l.Add($"Fișiere aduse de operator și copiate în caz (originalele nu se modifică): {Imported}.");
        if (ReportsWritten > 0) l.Add($"Rapoarte și exporturi scrise în caz (înregistrate în custodie): {ReportsWritten}.");
        return l;
    }

    private static readonly Regex UsersRx = new(@"^[A-Za-z]:\\(?:Users|Documents and Settings)\\[^\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>Replaces the profile, temp and application-data roots and the machine name by placeholders.</summary>
    public static string Generalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string Sub(string p, Environment.SpecialFolder f, string token)
        {
            var root = Environment.GetFolderPath(f);
            return root.Length > 3 && p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? token + p[root.Length..] : p;
        }
        var temp = Path.GetTempPath().TrimEnd('\\');
        var s = temp.Length > 3 && path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ? "%TEMP%" + path[temp.Length..] : path;
        s = Sub(s, Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%");
        s = Sub(s, Environment.SpecialFolder.ApplicationData, "%APPDATA%");
        s = Sub(s, Environment.SpecialFolder.UserProfile, "%USERPROFILE%");
        s = UsersRx.Replace(s, "%USERPROFILE%");
        var machine = Environment.MachineName;
        if (machine.Length >= 2) s = Regex.Replace(s, Regex.Escape(machine), "<stație>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        return s;
    }
}
