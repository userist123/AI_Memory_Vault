using System.Diagnostics;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner rule 2026-10-16: the programs the application itself starts (reg.exe, esentutl, bcdedit, auditpol, the collection script ...) are not abuse. They are
/// recognised only through the sealed "collector.exec" records, never by folder or file name; a look-alike stays an ordinary event.
/// </summary>
public sealed class CollectorExecTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly string AppSha = new('a', 64), ExeSha = new('b', 64);
    private const string Reg = @"C:\Windows\System32\reg.exe";
    private const string Cmd = "\"C:\\Windows\\System32\\reg.exe\" save HKLM\\SYSTEM \"D:\\Case 1\\SYSTEM.hiv\" /y";
    private const int AppPid = 4100, ChildPid = 5200;
    private const string Script = "param($TargetType)\nGet-WinEvent -LogName Security -MaxEvents 10\n";
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "collector-exec-" + Guid.NewGuid().ToString("N")[..8]);

    public CollectorExecTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Line(string ev, DateTimeOffset when, string detail) =>
        System.Text.Json.JsonSerializer.Serialize(new { Event = ev, WhenUtc = when, Detail = detail });

    private static string[] Exec(string id, string run, DateTimeOffset start, DateTimeOffset? end, string exe = Reg, string cmd = Cmd, string script = "", string appSha = "", string exeSha = "") =>
    [
        Line(CollectorExec.Exec, start, CollectorExec.ExecDetail(id, run, AppPid, appSha.Length > 0 ? appSha : AppSha, exeSha.Length > 0 ? exeSha : ExeSha, script, exe, cmd)),
        .. end is { } e ? new[] { Line(CollectorExec.ExecDone, e, CollectorExec.DoneDetail(id, run, ChildPid, 0, e)) } : [],
    ];

    private static string[] Control(string run, DateTimeOffset start, DateTimeOffset end, params string[][] inside) =>
    [
        Line(OperationAudit.ControlStarted, start, OperationAudit.ControlStartedDetail("Standalone", "Standalone", true, 30, 0, run, AppSha)),
        .. inside.SelectMany(x => x),
        Line(OperationAudit.ControlFinished, end, OperationAudit.ControlFinishedDetail("WS", true, 1, 0, 0, 0, run)),
    ];

    private string Audit(params string[] lines)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N")[..6] + ".jsonl");
        File.WriteAllLines(p, lines);
        return p;
    }

    /// <summary>The synthetic reg.exe of these tests stands for an inventory program whose file has the recorded hash (the real check: the two inventory tests below).</summary>
    private static OperationAudit.CollectorExecs Read(string path, bool chainValid) =>
        OperationAudit.ReadCollectorExecs(path, chainValid, inventory: [Reg], currentSha256: p => p.Equals(Reg, StringComparison.OrdinalIgnoreCase) ? ExeSha : "");

    private OperationAudit.CollectorExecs Honest() =>
        Read(Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("x1", "run-A", T0.AddMinutes(1), T0.AddMinutes(2)))), chainValid: true);

    private static ProcessObservation Start4688(DateTimeOffset t, int pid = ChildPid, int parent = AppPid, string image = Reg, string cmd = Cmd, string sha = "") =>
        new(t, "4688", pid, parent, image, cmd, sha);

    [Fact]
    public void A_process_start_matching_a_sealed_record_is_the_applications_own_activity()
    {
        var execs = Honest();
        var r = Assert.Single(execs.Valid);
        Assert.Equal((ChildPid, 0), (r.ChildPid, r.ExitCode));
        Assert.Equal(Cmd, r.CommandLine);
        var m = SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1).AddSeconds(1)), execs);
        Assert.Equal(SelfExecKind.Own, m.Kind);
        // the parent alone (child id not in the event) is enough inside the window; the child alone too
        Assert.Equal(SelfExecKind.Own, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), pid: 0), execs).Kind);
        Assert.Equal(SelfExecKind.Own, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), parent: 0), execs).Kind);
        // Sysmon 1 carries the hash: it must be the recorded one
        Assert.Equal(SelfExecKind.Own, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), sha: ExeSha.ToUpperInvariant()) with { Kind = "Sysmon 1" }, execs).Kind);
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), sha: new string('d', 64)) with { Kind = "Sysmon 1" }, execs).Kind);
    }

    [Fact]
    public void A_reused_process_id_outside_the_record_window_is_not_the_application()
    {
        var execs = Honest();
        // the same process id, the same program and command line, an hour later: a reused id, not the application's run
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddHours(1)), execs).Kind);
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(-1)), execs).Kind);
        // and neither the same command from another process tree inside the window
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), pid: 9999, parent: 8888), execs).Kind);
    }

    [Fact]
    public void A_different_command_line_or_program_is_not_the_application_even_from_its_process()
    {
        var execs = Honest();
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), cmd: Cmd.Replace("save", "delete")), execs).Kind);
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), cmd: "vssadmin delete shadows /all /quiet"), execs).Kind);
        // a copy of reg.exe elsewhere, with the same arguments: not recognised by name
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), image: @"C:\Users\Public\reg.exe"), execs).Kind);
    }

    [Fact]
    public void A_forged_record_outside_a_control_run_is_only_claimed_and_never_hides_the_event()
    {
        string[] lines =
        [
            .. Control("run-A", T0, T0.AddMinutes(20)),
            .. Exec("f1", "run-A", T0.AddHours(2), T0.AddHours(2).AddMinutes(1)),     // claims run-A, written after its end
            .. Exec("f2", "", T0.AddHours(3), T0.AddHours(3).AddMinutes(1)),          // no control at all
            .. Exec("f3", "run-Z", T0.AddHours(4), T0.AddHours(4).AddMinutes(1)),     // a control that was never started
        ];
        var execs = Read(Audit(lines), chainValid: true);
        Assert.Empty(execs.Valid);
        Assert.Equal(3, execs.Refused.Count);
        var m = SelfExecMatcher.Classify(Start4688(T0.AddHours(2).AddSeconds(10)), execs);
        Assert.Equal(SelfExecKind.Claimed, m.Kind);
        Assert.Contains("nu este în interiorul controlului ei", m.Why);
        Assert.Contains("nu numește controlul", SelfExecMatcher.Classify(Start4688(T0.AddHours(3).AddSeconds(10)), execs).Why);
        Assert.Contains("început și sfârșit", SelfExecMatcher.Classify(Start4688(T0.AddHours(4).AddSeconds(10)), execs).Why);
    }

    [Fact]
    public void Records_without_an_end_running_too_long_from_another_build_or_from_a_broken_chain_are_refused()
    {
        string[] lines = [.. Control("run-A", T0, T0.AddHours(1),
            Exec("n1", "run-A", T0.AddMinutes(1), null),
            Exec("n3", "run-A", T0.AddMinutes(3), T0.AddMinutes(4), appSha: new string('e', 64)),
            Exec("n4", "run-A", T0.AddMinutes(5), T0.AddMinutes(6), exe: "reg.exe"),
            Exec("ok", "run-A", T0.AddMinutes(7), T0.AddMinutes(8)),
            Exec("n2", "run-A", T0.AddMinutes(9), T0.AddMinutes(20)))];
        var path = Audit(lines);
        var execs = Read(path, chainValid: true);
        Assert.Equal("ok", Assert.Single(execs.Valid).ExecId);
        var why = execs.Refused.ToDictionary(x => x.Record.ExecId, x => x.Why);
        Assert.Contains("sfârșit", why["n1"]);
        Assert.Contains("10 minute", why["n2"]);
        Assert.Contains("altă versiune", why["n3"]);
        Assert.Contains("calea lui completă", why["n4"]);
        var broken = Read(path, chainValid: false);
        Assert.Empty(broken.Valid);
        Assert.All(broken.Refused, x => Assert.Contains("nu se verifică", x.Why));
    }

    [Fact]
    public void A_backdated_record_after_a_clock_regression_is_refused()
    {
        string[] lines = [.. Control("run-A", T0, T0.AddMinutes(20)), .. Control("run-B", T0.AddDays(-2), T0.AddDays(-2).AddMinutes(20), Exec("b1", "run-B", T0.AddDays(-2).AddMinutes(1), T0.AddDays(-2).AddMinutes(2)))];
        var execs = Read(Audit(lines), chainValid: true);
        Assert.Empty(execs.Valid);
        Assert.Contains("oră în urmă", Assert.Single(execs.Refused).Why);
    }

    [Fact]
    public void A_script_block_is_the_applications_only_when_its_hash_is_the_recorded_script_hash()
    {
        var sha = CollectorExec.ScriptSha256(Script);
        var execs = Read(Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("s1", "run-A", T0.AddMinutes(1), T0.AddMinutes(3), script: sha))), chainValid: true);
        ProcessObservation Block(string text, int pid = ChildPid, double min = 2) => new(T0.AddMinutes(min), "4104", pid, 0, "", "", "", text);
        Assert.Equal(SelfExecKind.Own, SelfExecMatcher.Classify(Block(Script.Replace("\n", "\r\n")), execs).Kind);   // line endings do not matter
        Assert.Equal(SelfExecKind.Own, SelfExecMatcher.Classify(Block(Script, pid: 0), execs).Kind);
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Block(Script + "Invoke-Expression $x\n"), execs).Kind);   // one changed character
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Block(Script, pid: 777), execs).Kind);
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Block(Script, min: 30), execs).Kind);
    }

    // ---- the station control: own events leave every check, look-alikes stay, claimed ones are DE VERIFICAT ----

    private const string Abused = "$b = [Convert]::FromBase64String($x)\nGet-ItemProperty HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa\n";

    private static StationFacts Station(OperationAudit.CollectorExecs? execs, params LogFact[] logs)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = T0.AddDays(-30), PeriodEndUtc = T0.AddDays(1), CollectedUtc = T0.AddDays(1), IsAdministrator = true,
                                   Posture = LogAnalyzer.Core.Services.Edition.StationPosture.Standalone, CollectorExecs = execs };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, T0.AddDays(-90), 100, true, ""));
        f.Logs.AddRange(logs);
        return f;
    }

    private static LogFact Block(DateTimeOffset t, string text, long record = 1) =>
        new("Microsoft-Windows-PowerShell/Operational", 4104, t, record, "S-1-5-18", new Dictionary<string, string> { ["ScriptBlockText"] = text });

    private static LogFact Proc(DateTimeOffset t, string cmd, int pid = ChildPid, int parent = AppPid, long record = 2) =>
        new("Security", 4688, t, record, "S-1-5-18", new Dictionary<string, string>
        { ["NewProcessId"] = "0x" + pid.ToString("x"), ["ProcessId"] = "0x" + parent.ToString("x"), ["NewProcessName"] = Reg, ["CommandLine"] = cmd, ["SubjectUserName"] = "inspector" });

    private OperationAudit.CollectorExecs ScriptRun(bool valid) =>
        Read(Audit(valid
            ? Control("run-A", T0, T0.AddMinutes(20), Exec("s1", "run-A", T0.AddMinutes(1), T0.AddMinutes(3), script: CollectorExec.ScriptSha256(Abused)), Exec("r1", "run-A", T0.AddMinutes(4), T0.AddMinutes(5)))
            : [.. Control("run-A", T0, T0.AddMinutes(20)), .. Exec("s1", "run-A", T0.AddHours(1), T0.AddHours(1).AddMinutes(2), script: CollectorExec.ScriptSha256(Abused))]), chainValid: true);

    [Fact]
    public void The_applications_own_script_block_is_listed_apart_and_is_never_a_finding()
    {
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true), Block(T0.AddMinutes(2), Abused), Proc(T0.AddMinutes(4.5), Cmd)));
        Assert.DoesNotContain(r.Checks, c => c.Id == "U06");
        var a17 = Assert.Single(r.Checks, c => c.Id == "A17");
        Assert.Equal(ControlStatus.Conform, a17.Status);
        Assert.Contains("Activitate proprie a aplicației: 2 comenzi, toate consemnate", a17.Detail);
        Assert.Equal(2, a17.Evidence.Count);
        Assert.Equal(2, r.Facts.OwnActivity.Count);
        Assert.DoesNotContain(r.Facts.Logs, l => l.Id is 4104 or 4688);
    }

    [Fact]
    public void A_look_alike_script_block_stays_a_finding()
    {
        // the same abused text an hour later (no record covers it): an ordinary event
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true), Block(T0.AddHours(5), Abused)));
        Assert.Equal(ControlStatus.DeVerificat, Assert.Single(r.Checks, c => c.Id == "U06").Status);
        var a17 = Assert.Single(r.Checks, c => c.Id == "A17");
        Assert.Equal(ControlStatus.Conform, a17.Status);
        Assert.Contains("nicio comandă a aplicației", a17.Detail);
    }

    [Fact]
    public void A_script_block_claimed_by_a_refused_record_stays_a_finding_and_is_DE_VERIFICAT()
    {
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: false), Block(T0.AddHours(1).AddMinutes(1), Abused)));
        Assert.Equal(ControlStatus.DeVerificat, Assert.Single(r.Checks, c => c.Id == "U06").Status);
        var a17 = Assert.Single(r.Checks, c => c.Id == "A17");
        Assert.Equal(ControlStatus.DeVerificat, a17.Status);
        Assert.Contains(SelfExecMatcher.ClaimedLabel, a17.Detail);
        Assert.Contains(a17.Evidence, e => e.Contains("nu este în interiorul controlului ei"));
    }

    [Fact]
    public void Without_the_sealed_audit_nothing_is_recognised_and_A17_is_not_emitted()
    {
        var r = ControlEvaluator.Evaluate(Station(null, Block(T0.AddMinutes(2), Abused)));
        Assert.Equal(ControlStatus.DeVerificat, Assert.Single(r.Checks, c => c.Id == "U06").Status);
        Assert.DoesNotContain(r.Checks, c => c.Id == "A17");
    }

    // ---- review of PR #296: only the programs of the application's fixed inventory, with the hash their file has now ----

    [Fact]
    public void A_record_naming_a_program_outside_the_fixed_inventory_is_refused_and_its_event_is_only_claimed()
    {
        const string exfil = @"C:\Tools\exfil.exe";
        const string cmd = "\"C:\\Tools\\exfil.exe\" E:\\";
        var path = Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("x9", "run-A", T0.AddMinutes(1), T0.AddMinutes(2), exe: exfil, cmd: cmd)));
        var execs = OperationAudit.ReadCollectorExecs(path, chainValid: true);
        Assert.Empty(execs.Valid);
        Assert.Contains("inventar", Assert.Single(execs.Refused).Why);
        var ev = new LogFact("Security", 4688, T0.AddMinutes(1.5), 9, "S-1-5-18", new Dictionary<string, string>
            { ["NewProcessId"] = "0x" + ChildPid.ToString("x"), ["ProcessId"] = "0x" + AppPid.ToString("x"), ["NewProcessName"] = exfil, ["CommandLine"] = cmd });
        var r = ControlEvaluator.Evaluate(Station(execs, ev));
        var a17 = Assert.Single(r.Checks, c => c.Id == "A17");
        Assert.Equal(ControlStatus.DeVerificat, a17.Status);
        Assert.Contains(SelfExecMatcher.ClaimedLabel, a17.Detail);
        Assert.Contains(r.Facts.Logs, l => l.RecordId == 9);      // stays with the other checks
        Assert.Empty(r.Facts.OwnActivity);
    }

    [Fact]
    public void An_inventory_program_is_accepted_only_while_its_file_has_the_recorded_hash()
    {
        var reg = Path.Combine(Environment.SystemDirectory, "reg.exe");
        var real = CollectorExec.FileSha256(reg);
        Assert.Equal(64, real.Length);
        var cmd = "\"" + reg + "\" query HKLM";
        var ok = OperationAudit.ReadCollectorExecs(Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("g1", "run-A", T0.AddMinutes(1), T0.AddMinutes(2), exe: reg.ToUpperInvariant(), cmd: cmd, exeSha: real))), chainValid: true);
        Assert.Single(ok.Valid);
        // the same program with another hash (the file was replaced, or the record lies about it)
        var bad = OperationAudit.ReadCollectorExecs(Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("g2", "run-A", T0.AddMinutes(1), T0.AddMinutes(2), exe: reg, cmd: cmd, exeSha: new string('f', 64)))), chainValid: true);
        Assert.Empty(bad.Valid);
        Assert.Contains("amprent", Assert.Single(bad.Refused).Why);
        // the fixed inventory: by location, not by name
        Assert.True(CollectorExec.InInventory(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe").ToLowerInvariant()));
        Assert.True(CollectorExec.InInventory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe")));
        Assert.False(CollectorExec.InInventory(@"C:\Users\Public\reg.exe"));
        Assert.False(CollectorExec.InInventory(Path.Combine(Environment.SystemDirectory, "cmd.exe")));
        Assert.False(CollectorExec.InInventory(Path.Combine(Environment.SystemDirectory, "..", "Temp", "reg.exe")));
        Assert.False(CollectorExec.InInventory("reg.exe"));
        // the firewall tool is in the inventory only of the editions that ship its runner (they add it at composition)
        var fw = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        CollectorExec.AddToInventory(fw);
        Assert.True(CollectorExec.InInventory(fw.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => CollectorExec.AddToInventory("netsh.exe"));
    }

    [Fact]
    public void The_detail_survives_paths_and_commands_with_separators()
    {
        var cmd = "\"C:\\Tools; x\\a.exe\" /k \"v; w=1\"";
        var d = CollectorExec.ExecDetail("e1", "run-A", 12, AppSha, ExeSha, "", @"C:\Tools; x\a.exe", cmd);
        Assert.True(CollectorExec.TryParseExec(d, T0, out var r));
        Assert.Equal((@"C:\Tools; x\a.exe", cmd, 12, "run-A"), (r.Exe, r.CommandLine, r.AppPid, r.RunId));
        Assert.True(CollectorExec.TryParseDone(CollectorExec.DoneDetail("e1", "run-A", 99, 3, T0), out var done));
        Assert.Equal((99, 3, T0), (done.ChildPid, done.ExitCode, done.EndUtc));
    }

    [Fact]
    public void Every_start_is_recorded_before_it_happens_with_the_command_line_windows_receives()
    {
        var cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var psi = new ProcessStartInfo(cmdExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "/d", "/c", "echo", "%CMDCMDLINE%", "a b", "" }) psi.ArgumentList.Add(a);
        var events = new List<(string Ev, string Detail, bool Running)>();
        Process? started = null;
        var sink = CollectorExec.Sink;
        var run = CollectorExec.CurrentRunId;
        try
        {
            // only this test's start and its end (another test may start a tool at the same time)
            CollectorExec.Sink = (ev, detail) =>
            {
                lock (events)
                    if (ev == CollectorExec.Exec ? detail.Contains("%CMDCMDLINE%", StringComparison.Ordinal)
                                                 : events.Count == 1 && CollectorExec.TryParseExec(events[0].Detail, T0, out var mine) && detail.StartsWith("exec=" + mine.ExecId + ";", StringComparison.Ordinal))
                        events.Add((ev, detail, started is not null));
                return true;
            };
            CollectorExec.CurrentRunId = "run-T";
            using var p = started = CollectorExec.Start(psi);
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            CollectorExec.Done(p);
            // cmd prints its own command line: the one recorded is exactly what Windows received (and writes in 4688)
            // (echo prints the expanded command line followed by the arguments after it)
            Assert.Equal(CollectorExec.CommandLineOf(psi) + " \"a b\" \"\"", output, ignoreCase: true);
            Assert.Equal(2, events.Count);
            Assert.Equal((CollectorExec.Exec, false), (events[0].Ev, events[0].Running));     // written before the start
            Assert.True(CollectorExec.TryParseExec(events[0].Detail, T0, out var rec));
            Assert.Equal((cmdExe, CollectorExec.CommandLineOf(psi), Environment.ProcessId, "run-T"), (rec.Exe, rec.CommandLine, rec.AppPid, rec.RunId));
            Assert.Equal(CollectorExec.FileSha256(cmdExe), rec.ExeSha256);
            Assert.Equal(64, rec.ExeSha256.Length);
            Assert.True(CollectorExec.TryParseDone(events[1].Detail, out var done));
            Assert.Equal((rec.ExecId, p.Id, 0), (done.ExecId, done.ChildPid, done.ExitCode));
        }
        finally { CollectorExec.Sink = sink; CollectorExec.CurrentRunId = run; }
    }
}
