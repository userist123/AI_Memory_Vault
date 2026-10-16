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
        // PowerShell is not in the shared inventory: only the editions that ship the collection script register it, for that script alone
        Assert.False(CollectorExec.InInventory(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe").ToLowerInvariant()));
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
    public void A_powershell_record_that_does_not_run_the_applications_collection_script_is_refused()
    {
        var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var real = CollectorExec.FileSha256(ps);
        Assert.Equal(64, real.Length);
        var cmd = "\"" + ps + "\" -NoProfile -Command Get-Process";
        var execs = OperationAudit.ReadCollectorExecs(Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("p1", "run-A", T0.AddMinutes(1), T0.AddMinutes(2), exe: ps, cmd: cmd, exeSha: real))), chainValid: true);
        Assert.Empty(execs.Valid);
        var m = SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), image: ps, cmd: cmd), execs);
        Assert.Equal(SelfExecKind.Claimed, m.Kind);
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

    // ---- B5: commands started outside a station control get an action window; a script block split over several 4104 events is reassembled ----

    private static LogFact Part(DateTimeOffset t, string text, string blockId, int number, int total, int pid = ChildPid, long record = 1) =>
        new("Microsoft-Windows-PowerShell/Operational", 4104, t, record, "S-1-5-18", new Dictionary<string, string>
        { ["ScriptBlockText"] = text, ["ScriptBlockId"] = blockId, ["MessageNumber"] = number.ToString(), ["MessageTotal"] = total.ToString(), ["ProcessId"] = pid.ToString() });

    /// <summary>The abused script cut into three consecutive pieces; their concatenation is exactly the script the record's hash was made from.</summary>
    private static string[] ThreePieces()
    {
        var a = Abused.Length / 3;
        return [Abused[..a], Abused[a..(2 * a)], Abused[(2 * a)..]];
    }

    [Fact]
    public void A_script_block_split_over_several_events_is_reassembled_in_order_and_is_the_applications_own()
    {
        var p = ThreePieces();
        Assert.Equal(Abused, string.Concat(p));
        // delivered out of order, and mixed with an unrelated single block
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true),
            Part(T0.AddMinutes(2), p[2], "BLOCK-1", 3, 3, record: 13), Part(T0.AddMinutes(2), p[0], "BLOCK-1", 1, 3, record: 11), Part(T0.AddMinutes(2), p[1], "BLOCK-1", 2, 3, record: 12)));
        Assert.Equal(3, r.Facts.OwnActivity.Count);
        Assert.DoesNotContain(r.Facts.Logs, l => l.Id == 4104);
        Assert.Equal(ControlStatus.Conform, Assert.Single(r.Checks, c => c.Id == "A17").Status);
        Assert.DoesNotContain(r.Checks, c => c.Id == "U06");
    }

    [Fact]
    public void A_split_set_with_a_missing_part_is_not_confirmed_and_stays_DE_VERIFICAT_with_the_reason()
    {
        var p = ThreePieces();
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true), Part(T0.AddMinutes(2), p[0], "BLOCK-1", 1, 3, record: 11), Part(T0.AddMinutes(2), p[2], "BLOCK-1", 3, 3, record: 13)));
        Assert.Empty(r.Facts.OwnActivity);
        Assert.Equal(2, r.Facts.ClaimedActivity.Count);
        Assert.Contains("lipsesc părți", r.Facts.ClaimedActivity[0].Why);
        Assert.Equal(ControlStatus.DeVerificat, Assert.Single(r.Checks, c => c.Id == "A17").Status);
        Assert.Equal(2, r.Facts.Logs.Count(l => l.Id == 4104));       // they stay with the other checks
    }

    [Fact]
    public void A_split_set_with_a_duplicated_part_is_not_confirmed_even_when_every_number_is_present()
    {
        var p = ThreePieces();
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true),
            Part(T0.AddMinutes(2), p[0], "BLOCK-1", 1, 3, record: 11), Part(T0.AddMinutes(2), p[1], "BLOCK-1", 2, 3, record: 12),
            Part(T0.AddMinutes(2), p[1], "BLOCK-1", 2, 3, record: 14), Part(T0.AddMinutes(2), p[2], "BLOCK-1", 3, 3, record: 13)));
        Assert.Empty(r.Facts.OwnActivity);
        Assert.Equal(4, r.Facts.ClaimedActivity.Count);
        Assert.Contains("duplicat", r.Facts.ClaimedActivity[0].Why);
        Assert.Equal(ControlStatus.DeVerificat, Assert.Single(r.Checks, c => c.Id == "A17").Status);
    }

    [Fact]
    public void Parts_of_two_different_script_blocks_are_never_merged()
    {
        var p = ThreePieces();
        var head = p[0] + p[1];                     // X1
        var tail = p[2];                            // X2: X1 + X2 is the recorded script, but they belong to two different blocks
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true),
            Part(T0.AddMinutes(2), head, "BLOCK-A", 1, 2, record: 11), Part(T0.AddMinutes(2), "Write-Host unrelated", "BLOCK-A", 2, 2, record: 12),
            Part(T0.AddMinutes(2), "Write-Host other", "BLOCK-B", 1, 2, record: 13), Part(T0.AddMinutes(2), tail, "BLOCK-B", 2, 2, record: 14)));
        Assert.Empty(r.Facts.OwnActivity);
        // the same ScriptBlockId in two processes is two sets as well
        var r2 = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true),
            Part(T0.AddMinutes(2), head, "BLOCK-A", 1, 2, pid: ChildPid, record: 21), Part(T0.AddMinutes(2), tail, "BLOCK-A", 2, 2, pid: 9999, record: 22)));
        Assert.Empty(r2.Facts.OwnActivity);
    }

    [Fact]
    public void An_incomplete_split_set_outside_every_program_window_is_an_ordinary_event()
    {
        var p = ThreePieces();
        var r = ControlEvaluator.Evaluate(Station(ScriptRun(valid: true), Part(T0.AddHours(6), p[0], "BLOCK-1", 1, 3, record: 11)));
        Assert.Empty(r.Facts.OwnActivity);
        Assert.Empty(r.Facts.ClaimedActivity);
        Assert.Single(r.Facts.Logs, l => l.Id == 4104);
    }

    [Fact]
    public void A_command_started_outside_a_control_is_confirmed_through_its_own_sealed_action_window()
    {
        var exec = Exec("x1", "act-x1", T0.AddMinutes(1), T0.AddMinutes(2));
        var audit = Audit(Line(OperationAudit.ActionStarted, T0, OperationAudit.ActionStartedDetail("act-x1", AppSha, "reg.exe")), exec[0], exec[1],
                          Line(OperationAudit.ActionFinished, T0.AddMinutes(3), OperationAudit.ActionFinishedDetail("act-x1")));
        var execs = Read(audit, chainValid: true);
        var rec = Assert.Single(execs.Valid);
        Assert.Equal("act-x1", rec.RunId);
        // A17 can now confirm the process start
        var r = ControlEvaluator.Evaluate(Station(execs, Proc(T0.AddMinutes(1.5), Cmd)));
        Assert.Single(r.Facts.OwnActivity);
        Assert.Equal(ControlStatus.Conform, Assert.Single(r.Checks, c => c.Id == "A17").Status);
    }

    [Fact]
    public void A_run_id_cannot_be_forged_the_window_must_be_in_the_sealed_audit_and_cover_exactly_that_command()
    {
        var exec = Exec("x1", "act-x1", T0.AddMinutes(1), T0.AddMinutes(2));
        // no action.started / finished: a record that merely names an "act-" run id is refused
        Assert.Empty(Read(Audit(exec), chainValid: true).Valid);
        // an action window of another application build
        var other = Audit(Line(OperationAudit.ActionStarted, T0, OperationAudit.ActionStartedDetail("act-x1", new string('c', 64), "reg.exe")), exec[0], exec[1],
                          Line(OperationAudit.ActionFinished, T0.AddMinutes(3), OperationAudit.ActionFinishedDetail("act-x1")));
        Assert.Contains("altă versiune", Assert.Single(Read(other, chainValid: true).Refused).Why);
        // a record of another command inside the window of act-x1 (the window covers exactly one command)
        var e2 = Exec("x2", "act-x1", T0.AddMinutes(1), T0.AddMinutes(2));
        var wrong = Audit(Line(OperationAudit.ActionStarted, T0, OperationAudit.ActionStartedDetail("act-x1", AppSha, "reg.exe")), e2[0], e2[1],
                          Line(OperationAudit.ActionFinished, T0.AddMinutes(3), OperationAudit.ActionFinishedDetail("act-x1")));
        Assert.Contains("acoperă exact acea comandă", Assert.Single(Read(wrong, chainValid: true).Refused).Why);
        // the command ended after the window was closed
        var late = Audit(Line(OperationAudit.ActionStarted, T0, OperationAudit.ActionStartedDetail("act-x1", AppSha, "reg.exe")), exec[0], exec[1],
                         Line(OperationAudit.ActionFinished, T0.AddMinutes(1.5), OperationAudit.ActionFinishedDetail("act-x1")));
        Assert.Empty(Read(late, chainValid: true).Valid);
        // a broken chain
        var ok = Audit(Line(OperationAudit.ActionStarted, T0, OperationAudit.ActionStartedDetail("act-x1", AppSha, "reg.exe")), exec[0], exec[1],
                       Line(OperationAudit.ActionFinished, T0.AddMinutes(3), OperationAudit.ActionFinishedDetail("act-x1")));
        Assert.Empty(Read(ok, chainValid: false).Valid);
        // a process name or command line that spells the run id proves nothing
        Assert.Equal(SelfExecKind.None, SelfExecMatcher.Classify(Start4688(T0.AddMinutes(1.5), cmd: "\"C:\\Tools\\act-x1.exe\" run=act-x1"), Read(Audit(exec), chainValid: true)).Kind);
    }

    [Fact]
    public void Start_outside_a_control_writes_the_action_window_around_the_command_record()
    {
        var cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var marker = "ACTIONWINDOW" + Guid.NewGuid().ToString("N")[..8];
        var psi = new ProcessStartInfo(cmdExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "/d", "/c", "echo", marker }) psi.ArgumentList.Add(a);
        var events = new List<(string Ev, string Detail)>();
        var sink = CollectorExec.Sink;
        var run = CollectorExec.CurrentRunId;
        try
        {
            CollectorExec.Sink = (ev, detail) => { lock (events) events.Add((ev, detail)); return true; };
            CollectorExec.CurrentRunId = "";
            using var p = CollectorExec.Start(psi);
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            CollectorExec.Done(p);
            CollectorExec.Done(p);                                   // a second call writes nothing
            List<(string Ev, string Detail)> mine;
            lock (events)
            {
                var exec = events.Single(e => e.Ev == CollectorExec.Exec && e.Detail.Contains(marker, StringComparison.Ordinal));
                Assert.True(CollectorExec.TryParseExec(exec.Detail, T0, out var rec));
                Assert.Equal(CollectorExec.ActionRunPrefix + rec.ExecId, rec.RunId);
                mine = [.. events.Where(e => e.Detail.Contains("run=" + rec.RunId, StringComparison.Ordinal) || e.Detail.Contains("exec=" + rec.ExecId + ";", StringComparison.Ordinal))];
            }
            Assert.Equal([OperationAudit.ActionStarted, CollectorExec.Exec, CollectorExec.ExecDone, OperationAudit.ActionFinished], mine.Select(e => e.Ev));
            Assert.Contains("app_sha256=", mine[0].Detail);
            Assert.DoesNotContain(marker, mine[0].Detail, StringComparison.Ordinal);       // the window carries no command line
        }
        finally { CollectorExec.Sink = sink; CollectorExec.CurrentRunId = run; }
    }

    // ---- B9: MpCmdRun.exe of the Defender platform folder ----

    private static string Platform(string rest) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform") + Path.DirectorySeparatorChar + rest;

    [Theory]
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3-12\mpcmdrun.EXE")]
    [InlineData(@"10.0.1.2\MpCmdRun.exe")]
    public void The_defender_platform_MpCmdRun_at_its_exact_location_is_in_the_inventory(string rest)
    {
        var path = Platform(rest);
        Assert.True(CollectorExec.IsDefenderPlatformMpCmdRun(path));
        Assert.True(CollectorExec.InInventory(path));
        Assert.True(CollectorExec.InInventory(path.ToUpperInvariant()));
        Assert.True(CollectorExec.InInventory("\"" + path + "\""));
    }

    [Theory]
    [InlineData(@"4.18.26080.3-0\..\MpCmdRun.exe")]                      // traversal
    [InlineData(@"4.18.26080.3-0\..\..\Evil\MpCmdRun.exe")]
    [InlineData(@"4.18.1.1\..\4.18.2.2\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3-0\x\MpCmdRun.exe")]                       // extra segment
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe\x")]
    [InlineData(@"Evil\4.18.26080.3-0\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe:evil")]                    // alternate data stream
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe:$DATA")]
    [InlineData(@"4.18.26080.3-0:x\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe.")]                        // trailing dot / space
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe ")]
    [InlineData(@"4.18.26080.3-0\\MpCmdRun.exe")]                        // doubled separator
    [InlineData(@"4.18.26080.3-0/MpCmdRun.exe")]                         // alternate separator
    [InlineData(@"4.18\MpCmdRun.exe")]                                   // not a version
    [InlineData(@"4.18.26080\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3.1\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3-0-1\MpCmdRun.exe")]
    [InlineData(@"4.18.26080.3-\MpCmdRun.exe")]
    [InlineData(@"abc\MpCmdRun.exe")]
    [InlineData(@"٤.١٨.٢٦٠٨٠.٣\MpCmdRun.exe")]                           // non-ASCII digits
    [InlineData(@"4.18.26080.3-0\MsMpEng.exe")]                          // another program
    [InlineData(@"4.18.26080.3-0\MpCmdRun.exe.exe")]
    [InlineData(@"4.18.26080.3-0\MpCmdRunX.exe")]
    [InlineData(@"4.18.26080.3-0")]
    public void A_look_alike_of_the_defender_platform_MpCmdRun_is_not_in_the_inventory(string rest)
    {
        var path = Platform(rest);
        Assert.False(CollectorExec.IsDefenderPlatformMpCmdRun(path), path);
        Assert.False(CollectorExec.InInventory(path), path);
    }

    [Fact]
    public void Other_folders_and_path_forms_of_MpCmdRun_are_not_in_the_inventory()
    {
        var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string[] bad =
        [
            Path.Combine(pd, "Microsoft", "Windows Defender", "MpCmdRun.exe"),                                           // no platform folder
            Path.Combine(pd, "Microsoft", "Windows Defender", "Platform", "MpCmdRun.exe"),                                 // no version folder
            Path.Combine(pd, "Microsoft", "Windows Defender Advanced Threat Protection", "Platform", "4.18.1.1", "MpCmdRun.exe"),
            Path.Combine(pd, "Evil", "Microsoft", "Windows Defender", "Platform", "4.18.1.1", "MpCmdRun.exe"),
            Path.Combine(pd, "Microsoft", "Windows Defender", "Platform", "4.18.1.1", "MpCmdRun.exe").Replace(pd, Path.Combine(Path.GetTempPath(), "x")),   // other root
            @"C:\Users\Public\Platform\4.18.1.1\MpCmdRun.exe",
            @"\\LAB-PC\C$\ProgramData\Microsoft\Windows Defender\Platform\4.18.1.1\MpCmdRun.exe",                          // UNC
            @"\\?\" + Path.Combine(pd, "Microsoft", "Windows Defender", "Platform", "4.18.1.1", "MpCmdRun.exe"),             // device path
            @"Platform\4.18.1.1\MpCmdRun.exe",                                                                              // relative
            "MpCmdRun.exe",
            "",
        ];
        foreach (var path in bad)
        {
            Assert.False(CollectorExec.IsDefenderPlatformMpCmdRun(path), path);
            Assert.False(CollectorExec.InInventory(path), path);
        }
    }

    [Fact]
    public void A_record_for_the_defender_platform_MpCmdRun_passes_the_inventory_test_but_a_look_alike_is_refused_for_it()
    {
        var good = Platform(@"4.18.26080.3-0\MpCmdRun.exe");
        var traversal = Platform(@"4.18.26080.3-0\..\MpCmdRun.exe");
        string? Why(string exe)
        {
            var cmd = "\"" + exe + "\" -Scan -ScanType 3 -File C:\\Case\\x.bin";
            var audit = Audit(Control("run-A", T0, T0.AddMinutes(20), Exec("m1", "run-A", T0.AddMinutes(1), T0.AddMinutes(2), exe: exe, cmd: cmd)));
            var execs = OperationAudit.ReadCollectorExecs(audit, chainValid: true, inventory: [Reg], currentSha256: p => p.Equals(exe, StringComparison.OrdinalIgnoreCase) ? ExeSha : "");
            return execs.Refused.Select(r => r.Why).SingleOrDefault();
        }
        Assert.Null(Why(good));                                                          // valid: recorded hash equals the file hash, location accepted
        Assert.Contains("inventarul fix", Why(traversal));
    }

    // ---- B6: the collection-script command line is matched to its end ----

    [Fact]
    public void A_trailing_line_feed_or_a_second_command_does_not_match_the_collection_script_command_line()
    {
        var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = Path.Combine(AppContext.BaseDirectory, "Scripts", "AuditCollector.ps1");
        var psi = LogAnalyzer.Response.Collection.PowerShellAuditCollector.BuildStartInfo(ps, script, "PC", @"D:\Case 1\out", "HOST-1");
        var cmd = CollectorExec.CommandLineOf(psi);
        bool Runs(string c) => LogAnalyzer.Response.Collection.PowerShellAuditCollector.RunsTheCollectionScript(c, ps, script);
        Assert.True(Runs(cmd));
        Assert.False(Runs(cmd + "\n"));
        Assert.False(Runs(cmd + "\r\n"));
        Assert.False(Runs(cmd + "\nGet-Process"));
        Assert.False(Runs(cmd + " "));
        Assert.False(Runs(cmd + " -Hostname other"));
    }

    [Fact]
    public void A_trailing_line_feed_does_not_make_a_firewall_command_match()
    {
        var runner = new LogAnalyzer.Infrastructure.Services.ComFirewallRunner();
        var outcome = runner.Netsh("advfirewall firewall show rule name=\"x\"\n");
        Assert.Equal(-1, outcome.ExitCode);
        Assert.Contains("nerecunoscută", outcome.Output);
    }
}
