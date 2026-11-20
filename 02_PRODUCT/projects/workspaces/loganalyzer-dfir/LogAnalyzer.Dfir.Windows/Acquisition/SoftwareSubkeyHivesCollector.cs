using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// Planner #13 D1: the QUICK profile cannot afford the whole SOFTWARE hive (<see cref="SoftwareHiveCollector"/>, about 214 MB on a real PC, Standard and Full only), but the
/// configuration audit (UAC, Microsoft Defender protection and exclusions) needs only three small keys. This collector saves exactly those with
/// <c>reg save &lt;subkey&gt; &lt;file&gt; /y</c>; the subkey becomes the ROOT of each file, so every file is registered as "software_subkey_hive" with OriginalPath = the subkey path
/// and TemporalType CurrentSnapshot. <see cref="LogAnalyzer.Dfir.Windows.Investigation.ConfigurationHiveReader"/> maps the full-path locators (SOFTWARE\Microsoft\...\EnableLUA) onto the file root.
/// Quick only: Standard and Full keep the full hive (the reader prefers it). A key that does not exist (the Defender policy key usually does not) is NOT saved: a JSON marker (type software_subkey_absent) is registered instead, so the absence is hashed evidence and the audit reads the values as absent, not as a gap. A key that exists but cannot be saved is an error
/// of the outcome, never an abort; the audit then reports the unread settings as gaps. Needs elevation.
/// </summary>
public sealed class SoftwareSubkeyHivesCollector : ICollector
{
    public const string SourceType = "software_subkey_hive";

    /// <summary>(subkey below HKLM\SOFTWARE, file name in Raw\Registry, log label). Exact commands are fixed by tests.</summary>
    public static readonly IReadOnlyList<(string Subkey, string File, string Label)> Subkeys =
    [
        (@"Microsoft\Windows\CurrentVersion\Policies\System", "SOFTWARE-Policies-System", "reg_save_software_uac"),
        (@"Microsoft\Windows Defender", "SOFTWARE-Windows-Defender", "reg_save_software_defender"),
        (@"Policies\Microsoft\Windows Defender", "SOFTWARE-Policies-Windows-Defender", "reg_save_software_defender_policy"),
    ];

    public const string AbsentSourceType = "software_subkey_absent";

    private readonly ToolRunFunc _run;
    private readonly Func<string, bool> _keyExists;
    public SoftwareSubkeyHivesCollector() : this(null, null) { }
    /// <param name="keyExists">Read-only existence check of a key below HKLM\SOFTWARE (argument = the subkey path); by default the 64-bit registry view. Tests pass a fake.</param>
    public SoftwareSubkeyHivesCollector(ToolRunFunc? run, Func<string, bool>? keyExists = null)
    {
        _run = run ?? ((exe, args, logDir, label, timeout, ct) => ToolRunner.Run(exe, args, logDir, label, timeout, ct));
        _keyExists = keyExists ?? DefaultKeyExists;
    }

    private static bool DefaultKeyExists(string sub)
    {
        if (!OperatingSystem.IsWindows()) return true;   // cannot prove absence: let reg save decide
        using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var k = baseKey.OpenSubKey(@"SOFTWARE\" + sub);
        return k is not null;
    }

    public string Name => "SoftwareSubkeyHivesCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.Quick;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("SOFTWARE (subchei UAC și Defender): nu au fost salvate, necesită drepturi de administrator", "registry");
        var o = new CollectorOutcome { Source = @"HKLM\SOFTWARE (subchei)", Tool = "reg.exe" };
        var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
        foreach (var (sub, file, label) in Subkeys)
        {
            ct.ThrowIfCancellationRequested();
            var key = @"HKLM\SOFTWARE\" + sub;
            var dest = Path.Combine(ctx.Case.RawDir("Registry"), file);
            bool exists;
            try { exists = _keyExists(sub); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { exists = true; }   // cannot tell: try the save, a failure is then an error
            if (!exists)
            {
                // proven absent: no reg save, no error; the absence itself is hashed evidence (a marker the configuration audit reads as "values absent", not as a gap)
                var marker = dest + ".absent.json";
                File.WriteAllText(marker, System.Text.Json.JsonSerializer.Serialize(new { key, exists = false, checkedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) }), new System.Text.UTF8Encoding(false));
                o.Evidence.Add(ctx.Case.RegisterStored(marker, key, @"Registry:SOFTWARE\" + sub, AbsentSourceType, TemporalType.CurrentSnapshot, Name, Version,
                    notes: "Cheia nu există pe stație (verificare doar-citire înainte de reg save); lipsa ei este proba."));
                continue;
            }
            ToolRun save;
            try { save = _run(ToolRunner.System32("reg.exe"), ["save", key, dest, "/y"], logDir, label, TimeSpan.FromMinutes(2), ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                o.Errors.Add($"reg save {key}: nu a putut fi pornit sau scris ({ex.Message}); setările din această cheie lipsesc din analiză");
                continue;
            }
            o.Commands.Add(save.CommandLine);
            o.ExitCode = save.ExitCode;
            if (save.ExitCode == 0 && File.Exists(dest))
                o.Evidence.Add(ctx.Case.RegisterStored(dest, key, @"Registry:SOFTWARE\" + sub, SourceType, TemporalType.CurrentSnapshot, Name, Version));
            else o.Errors.Add($"reg save {key}: exit {save.ExitCode}{(save.TimedOut ? " (timp depășit)" : "")}; cheia nu există pe stație sau nu a putut fi salvată, setările ei lipsesc din analiză");
        }
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : EvidenceStatus.Success;
        return o;
    }
}
