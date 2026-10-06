using System.Diagnostics;
using System.Management;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Dfir.Windows.Native;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>Exports event log channels with wevtutil (raw .evtx, never re-rendered). Security needs elevation.</summary>
public sealed class EventLogCollector : ICollector
{
    public static readonly string[] Channels =
    [
        "Security", "System", "Application",
        "Microsoft-Windows-PowerShell/Operational", "Windows PowerShell",
        "Microsoft-Windows-Windows Defender/Operational", "Microsoft-Windows-TaskScheduler/Operational",
        "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", "Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational",
        "Microsoft-Windows-Sysmon/Operational", "Microsoft-Windows-Bits-Client/Operational", "Microsoft-Windows-WMI-Activity/Operational",
        "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall", "Microsoft-Windows-Partition/Diagnostic",
        "Microsoft-Windows-NetworkProfile/Operational", "Microsoft-Windows-WLAN-AutoConfig/Operational", "Microsoft-Windows-DNS-Client/Operational",
    ];

    public string Name => "EventLogCollector";
    public string Version => "1.0";
    public bool RequiresElevation => false;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var o = new CollectorOutcome { Tool = "wevtutil.exe", ToolVersion = ToolRunner.FileVersion(ToolRunner.System32("wevtutil.exe")), Source = "event logs" };
        var dir = ctx.Case.RawDir("EventLogs");
        var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
        int done = 0;
        foreach (var ch in Channels)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Progress?.Report(new CollectorProgress(Name, ch, done++ * 100 / Channels.Length));
            if (ch == "Security" && !ctx.IsElevated) { o.Errors.Add("Security: necesită drepturi de administrator"); continue; }
            var dest = Path.Combine(dir, ch.Replace('/', '%') + ".evtx");
            var run = ToolRunner.Run(ToolRunner.System32("wevtutil.exe"), ["epl", ch, dest, "/ow:true"], logDir, "wevtutil_" + ch.Replace('/', '_'), TimeSpan.FromMinutes(10), ct);
            o.Commands.Add(run.CommandLine);
            if (run.ExitCode != 0 || !File.Exists(dest))
            {
                // 15007 = channel not found (e.g. Sysmon not installed): an absent source, not a failure.
                o.Errors.Add($"{ch}: wevtutil exit {run.ExitCode}");
                continue;
            }
            o.Evidence.Add(ctx.Case.RegisterStored(dest, ch, "EventLog:" + ch, "evtx", TemporalType.Historical, Name, Version));
        }
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }
}

/// <summary>Copies Prefetch files (execution history). Needs elevation.</summary>
public sealed class PrefetchCollector : ICollector
{
    public string Name => "PrefetchCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var src = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("Prefetch: necesită drepturi de administrator", src);
        string[] files;
        try { files = Directory.GetFiles(src, "*.pf"); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return CollectorOutcome.NotAvailable(ex.Message, src); }
        if (files.Length == 0) return new CollectorOutcome { Status = EvidenceStatus.Empty, Source = src, Errors = { "Prefetch dezactivat sau gol (frecvent pe SSD-uri cu anumite configurări)." } };
        var o = new CollectorOutcome { Source = src, Tool = "copy" };
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try { o.Evidence.Add(ctx.Case.ImportFile(f, "Prefetch", "prefetch", TemporalType.Historical, Name, Version)); }
            catch (IOException ex) { o.Errors.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        o.Status = o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }
}

/// <summary>SRUM database (per-application network bytes, ~1h buckets). The live file is locked: copied through a VSS snapshot by esentutl.</summary>
public sealed class SrumCollector : ICollector
{
    public string Name => "SrumCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var src = Path.Combine(Environment.SystemDirectory, "sru", "SRUDB.dat");
        if (!ctx.IsElevated) return CollectorOutcome.NotAvailable("SRUM: necesită drepturi de administrator", src);
        var dir = ctx.Case.RawDir("SRUM");
        var dest = Path.Combine(dir, "SRUDB.dat");
        var run = ToolRunner.Run(ToolRunner.System32("esentutl.exe"), ["/y", src, "/vss", "/d", dest], Path.Combine(ctx.Case.Root, "Logs", "tools"), "esentutl_srum", TimeSpan.FromMinutes(10), ct);
        var o = new CollectorOutcome { Tool = "esentutl.exe", ToolVersion = ToolRunner.FileVersion(ToolRunner.System32("esentutl.exe")), Source = src, ExitCode = run.ExitCode, Commands = { run.CommandLine } };
        if (run.ExitCode != 0 || !File.Exists(dest)) { o.Status = EvidenceStatus.Failed; o.Errors.Add($"esentutl exit {run.ExitCode}"); return o; }
        o.Evidence.Add(ctx.Case.RegisterStored(dest, src, "SRUM", "srum", TemporalType.Historical, Name, Version));
        return o;
    }
}

/// <summary>
/// Snapshot of the running state (CURRENT_SNAPSHOT): processes with parent, command line and signature, TCP connections,
/// services, scheduled tasks and autostart entries. Read-only.
/// </summary>
public sealed class LiveStateCollector : ICollector
{
    public string Name => "LiveStateCollector";
    public string Version => "1.0";
    public bool RequiresElevation => false;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    public sealed record ProcessRow(int Pid, int? Ppid, string Name, string Path, string CommandLine, string Signature, bool UserWritable);
    public sealed record ServiceRow(string Name, string DisplayName, string Path, string StartMode, string State, string Account, bool UserWritable);
    public sealed record TaskRow(string Path, string Command, bool UserWritable);
    public sealed record AutorunRow(string Location, string Name, string Value, bool UserWritable);
    public sealed record Snapshot(DateTimeOffset TakenUtc, string Host, List<ProcessRow> Processes, List<TcpConnection> Connections,
                                  List<ServiceRow> Services, List<TaskRow> Tasks, List<AutorunRow> Autoruns, List<string> Errors);

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var errors = new List<string>();
        var snap = new Snapshot(DateTimeOffset.UtcNow, Environment.MachineName, [], [], [], [], [], errors);
        Try(errors, "procese", () =>
        {
            using var s = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
            foreach (ManagementObject mo in s.Get())
            {
                var path = mo["ExecutablePath"] as string ?? "";
                bool writable = SuspiciousProcessDetector.IsUserWritableLocation(path);
                string sig = path.Length == 0 ? "" : writable || !path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)
                    ? Describe(Authenticode.Verify(path)) : "(Windows, neverificat)";
                snap.Processes.Add(new ProcessRow(Convert.ToInt32(mo["ProcessId"]), mo["ParentProcessId"] is uint pp ? (int)pp : null,
                    mo["Name"] as string ?? "", path, mo["CommandLine"] as string ?? "", sig, writable));
            }
        });
        Try(errors, "conexiuni", () => snap.Connections.AddRange(TcpTable.All()));
        Try(errors, "servicii", () =>
        {
            using var s = new ManagementObjectSearcher("SELECT Name, DisplayName, PathName, StartMode, State, StartName FROM Win32_Service");
            foreach (ManagementObject mo in s.Get())
            {
                var p = mo["PathName"] as string ?? "";
                snap.Services.Add(new ServiceRow(mo["Name"] as string ?? "", mo["DisplayName"] as string ?? "", p, mo["StartMode"] as string ?? "",
                    mo["State"] as string ?? "", mo["StartName"] as string ?? "", SuspiciousProcessDetector.IsUserWritableLocation(ExePath(p))));
            }
        });
        Try(errors, "task-uri", () =>
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Environment.SystemDirectory, "Tasks"), "*", SearchOption.AllDirectories))
            {
                string xml;
                try { xml = File.ReadAllText(f); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
                var cmd = Between(xml, "<Command>", "</Command>") + " " + Between(xml, "<Arguments>", "</Arguments>");
                snap.Tasks.Add(new TaskRow(f, cmd.Trim(), SuspiciousProcessDetector.IsUserWritableLocation(ExePath(Environment.ExpandEnvironmentVariables(cmd.Trim())))));
            }
        });
        Try(errors, "autostart", () =>
        {
            foreach (var (hive, key) in new[] { (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
                                                 (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
                                                 (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce") })
            {
                using var k = hive.OpenSubKey(key);
                if (k is null) continue;
                foreach (var n in k.GetValueNames())
                {
                    var v = k.GetValue(n)?.ToString() ?? "";
                    snap.Autoruns.Add(new AutorunRow($@"{hive.Name}\{key}", n, v, SuspiciousProcessDetector.IsUserWritableLocation(ExePath(Environment.ExpandEnvironmentVariables(v)))));
                }
            }
        });

        var dir = ctx.Case.RawDir("LiveState");
        var path = Path.Combine(dir, $"live_state_{snap.TakenUtc:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
        var o = new CollectorOutcome { Source = "live system", Tool = "WMI/IPHelper/registry", Status = errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success };
        o.Errors.AddRange(errors);
        o.Evidence.Add(ctx.Case.RegisterStored(path, "live system", "LiveState", "live_snapshot", TemporalType.CurrentSnapshot, Name, Version));
        return o;
    }

    private static string Describe(SignatureInfo s) => s.IsSigned ? (s.IsValid ? $"semnat ({s.Kind}) {s.Signer}" : $"semnătură invalidă: {s.Status}") : "NESEMNAT";

    /// <summary>Executable part of a command line or service ImagePath ("C:\a b\x.exe" -arg, or C:\x.exe -arg).</summary>
    public static string ExePath(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : cmd.Trim('"'); }
        int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? cmd[..(exe + 4)] : cmd.Split(' ')[0];
    }

    private static string Between(string s, string a, string b)
    {
        int i = s.IndexOf(a, StringComparison.OrdinalIgnoreCase), j = s.IndexOf(b, StringComparison.OrdinalIgnoreCase);
        return i >= 0 && j > i ? s[(i + a.Length)..j] : "";
    }

    private static void Try(List<string> errors, string what, Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { errors.Add($"{what}: {ex.Message}"); }
    }
}
