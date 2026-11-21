using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>Runs one external tool (exe, arguments, log folder, label, timeout, token) and returns its result; the default is <see cref="ToolRunner.Run"/>. Tests pass a fake.</summary>
public delegate ToolRun ToolRunFunc(string exe, IReadOnlyList<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct);

/// <summary>
/// The live SOFTWARE hive as an offline hive (reg save HKLM\SOFTWARE, same mechanism as the SYSTEM hive in
/// <see cref="ExecutionArtifactsCollector"/>), so the SOFTWARE parsers (Run/RunOnce, Winlogon, IFEO, TaskCache) run on a live station and not
/// only on VSS / Windows.old / remote packages. It is registered as "software_hive" with the origin "HKLM\SOFTWARE" and TemporalType
/// CurrentSnapshot; a Windows.old / VSS copy is a separate evidence item with its own origin and Historical time, as for SYSTEM.
/// Profiles: Standard and FullForensic only. The hive is typically 100-300 MB, which does not fit the Quick triage (minutes, small case).
/// Needs elevation; a failure is a gap with a reason, never an abort of the run.
/// </summary>
public sealed class SoftwareHiveCollector : ICollector
{
    public const string SourceType = "software_hive";
    private readonly ToolRunFunc _run;

    public SoftwareHiveCollector() : this(null) { }
    public SoftwareHiveCollector(ToolRunFunc? run) { _run = run ?? ((exe, args, logDir, label, timeout, ct) => ToolRunner.Run(exe, args, logDir, label, timeout, ct)); }

    public string Name => "SoftwareHiveCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("SOFTWARE: hive-ul live nu a fost salvat, necesită drepturi de administrator", "registry");
        var o = new CollectorOutcome { Source = @"HKLM\SOFTWARE", Tool = "reg.exe" };
        var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
        var dest = Path.Combine(ctx.Case.RawDir("Registry"), "SOFTWARE");
        ToolRun save;
        try { save = _run(ToolRunner.System32("reg.exe"), ["save", @"HKLM\SOFTWARE", dest, "/y"], logDir, "reg_save_software", TimeSpan.FromMinutes(10), ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            o.Errors.Add($"reg save SOFTWARE: nu a putut fi pornit sau scris ({ex.Message}); hive-ul SOFTWARE live lipsește din analiză");
            o.Status = EvidenceStatus.Failed;
            return o;
        }
        o.Commands.Add(save.CommandLine);
        o.ExitCode = save.ExitCode;
        if (save.ExitCode == 0 && File.Exists(dest))
            o.Evidence.Add(ctx.Case.RegisterStored(dest, @"HKLM\SOFTWARE", "Registry:SOFTWARE", SourceType, TemporalType.CurrentSnapshot, Name, Version));
        else o.Errors.Add($"reg save SOFTWARE: exit {save.ExitCode}{(save.TimedOut ? " (timp depășit)" : "")}; hive-ul SOFTWARE live lipsește din analiză (persistența Run/Winlogon/IFEO și activitățile planificate din registru nu sunt citite)");
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : EvidenceStatus.Success;
        return o;
    }
}
