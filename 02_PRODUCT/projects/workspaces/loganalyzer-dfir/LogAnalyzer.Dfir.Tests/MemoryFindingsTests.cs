using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.6: MEM-HIDDEN, MEM-INJECT, MEM-NETPROC, MEM-PATH, MEM-NODISK and the gap when no disk source exists.</summary>
public sealed class MemoryFindingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_memf_" + Guid.NewGuid().ToString("N"));
    public MemoryFindingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static int _row;
    private static TimelineEvent Ev(string source, string process, int? pid, string eventId = "", string path = "", string remoteIp = "", params (string K, string V)[] fields)
    {
        var e = new TimelineEvent
        {
            Time = Timestamp.Unknown(""), Source = source, EvidenceId = "E-" + source.Replace(':', '_'), EventId = eventId, Process = process, Pid = pid, Path = path, RemoteIp = remoteIp,
            Summary = $"{source} {process} {path}", Locator = "row=" + Interlocked.Increment(ref _row), SourceSha256 = "abc",
        };
        foreach (var (k, v) in fields) e.Fields[k] = v;
        return e;
    }
    private static TimelineEvent PsList(string n, int pid) => Ev("Memory:pslist", n, pid, "pslist", fields: ("ExitTime", ""));
    private static TimelineEvent PsScan(string n, int pid, string exit = "") => Ev("Memory:psscan", n, pid, "psscan", fields: ("ExitTime", exit));
    private static TimelineEvent Cmd(string n, int pid, string args) => Ev("Memory:cmdline", n, pid, "cmdline", fields: ("CommandLine", args));
    private static TimelineEvent Malfind(string n, int pid, bool mz, string prot = "PAGE_EXECUTE_READWRITE") =>
        Ev("Memory:malfind", n, pid, "malfind", fields: [("Protection", prot), ("StartVpn", "0x1f0000"), ("MzHeader", mz ? "da" : "nu")]);
    private static TimelineEvent Net(string n, int pid, string remote) => Ev("Memory:netscan", n, pid, "netscan", remoteIp: remote);
    private static TimelineEvent Prefetch(string exe) => Ev("Prefetch", exe, null);

    private static MemoryFindingsResult Run(params TimelineEvent[] t) { int n = 0; return MemoryFindings.Run(t, () => $"F-{++n:D4}"); }
    private static IEnumerable<Finding> Of(MemoryFindingsResult r, string rule) => r.Findings.Where(f => f.RuleId == rule);

    private static void AssertCandidate(Finding f)
    {
        Assert.True(f.Classification is Classification.Unproven or Classification.Candidate);
        Assert.Contains("Candidat", f.ClassificationReason);
        Assert.NotEmpty(f.SupportingEvidence);
        Assert.All(f.SupportingEvidence, e => { Assert.NotEmpty(e.EvidenceId); Assert.NotEmpty(e.Locator); });
    }

    [Fact]
    public void Hidden_process_is_psscan_only_and_not_ended()
    {
        var r = Run(PsList("System", 4), PsScan("System", 4), PsScan("updsvc.exe", 6666), PsScan("old.exe", 7000, "2026-11-07T09:01:00+00:00"));
        var f = Assert.Single(Of(r, "MEM-HIDDEN"));
        Assert.Equal((Severity.Medium, "updsvc.exe", 6666), (f.Severity, f.Process, f.Pid ?? 0));
        Assert.Contains("psscan", f.Description); Assert.Contains("încheiate", f.Description);
        AssertCandidate(f);
    }

    [Fact]
    public void Hidden_is_not_reported_when_pslist_has_the_process()
    {
        var r = Run(PsList("a.exe", 10), PsScan("a.exe", 10));
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "MEM-HIDDEN");
    }

    [Fact]
    public void Inject_with_MZ_is_high_and_has_mitre_and_evidence()
    {
        var r = Run(Malfind("x.exe", 50, mz: true), Malfind("x.exe", 50, mz: true));
        var f = Assert.Single(Of(r, "MEM-INJECT"));
        Assert.Equal((Severity.High, "T1055"), (f.Severity, f.MitreTechniqueId));
        Assert.Equal(2, f.SupportingEvidence.Count);
        AssertCandidate(f);
    }

    [Fact]
    public void Inject_without_MZ_alone_gives_nothing_and_non_rwx_is_ignored()
    {
        Assert.Empty(Run(Malfind("chrome.exe", 60, mz: false)).Findings);
        Assert.Empty(Run(Malfind("a.exe", 61, mz: true, prot: "PAGE_EXECUTE_WRITECOPY")).Findings);
    }

    [Fact]
    public void Inject_without_MZ_is_low_when_the_process_has_another_memory_signal()
    {
        var r = Run(PsList("System", 4), PsScan("tool.exe", 70), Malfind("tool.exe", 70, mz: false));
        var f = Assert.Single(Of(r, "MEM-INJECT"));
        Assert.Equal(Severity.Low, f.Severity);
        AssertCandidate(f);
    }

    [Fact]
    public void Netproc_cites_both_signals_and_needs_a_remote_address()
    {
        var r = Run(PsList("System", 4), PsScan("updsvc.exe", 6666), Malfind("updsvc.exe", 6666, mz: true), Net("updsvc.exe", 6666, "10.66.0.10"), Net("updsvc.exe", 6666, ""), Net("other.exe", 7, "1.2.3.4"));
        var f = Assert.Single(Of(r, "MEM-NETPROC"));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(2, f.RelatedFindingIds.Count);
        Assert.Contains(f.SupportingEvidence, e => e.Description.Contains("psscan"));
        Assert.Contains(f.SupportingEvidence, e => e.Description.Contains("malfind"));
        Assert.Single(f.SupportingEvidence, e => e.Description.Contains("netscan"));
        AssertCandidate(f);
        Assert.DoesNotContain(Run(PsList("a.exe", 1), PsScan("a.exe", 1), Net("a.exe", 1, "1.2.3.4")).Findings, x => x.RuleId == "MEM-NETPROC");   // not hidden, not injected
    }

    [Theory]
    [InlineData(@"C:\Users\Public\updsvc.exe -svc", true)]
    [InlineData(@"""C:\Users\bob\AppData\Local\Temp\x.exe"" /s", true)]
    [InlineData(@"C:\Windows\Temp\a.exe", true)]
    [InlineData(@"C:\Users\bob\Downloads\setup.exe", true)]
    [InlineData(@"C:\$Recycle.Bin\S-1\a.exe", true)]
    [InlineData(@"C:\ProgramData\x\y.exe", true)]
    [InlineData(@"C:\Windows\System32\svchost.exe -k netsvcs", false)]
    [InlineData(@"C:\Program Files\App\app.exe", false)]
    public void Path_rule_matches_user_writable_locations(string args, bool fires)
    {
        var r = Run(Cmd("p.exe", 9, args));
        Assert.Equal(fires ? 1 : 0, Of(r, "MEM-PATH").Count());
        if (fires) { var f = Of(r, "MEM-PATH").Single(); Assert.Equal(Severity.Medium, f.Severity); AssertCandidate(f); }
    }

    [Fact]
    public void Nodisk_fires_for_a_process_with_no_trace_and_names_the_limit_honestly()
    {
        var r = Run(Prefetch("CMD.EXE"), PsList("cmd.exe", 1), PsList("updsvc.exe", 2), PsList("System", 4), PsList("svchost.exe", 5));
        var f = Assert.Single(Of(r, "MEM-NODISK"));
        Assert.Equal((Severity.Low, "updsvc.exe"), (f.Severity, f.Process));
        Assert.Contains("fără urmă pe disc în probele cazului", f.Title);
        Assert.DoesNotContain("fără fișier", f.Title);
        Assert.Empty(r.Gaps);
        AssertCandidate(f);
    }

    [Fact]
    public void Nodisk_uses_the_command_line_image_name_and_skips_ended_processes()
    {
        var r = Run(Prefetch("RUNME.EXE"), PsList("trunc", 3), Cmd("trunc", 3, @"C:\x\runme.exe -a"), PsScan("gone.exe", 8, "2026-11-07T09:01:00+00:00"));
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "MEM-NODISK");
    }

    [Fact]
    public void Nodisk_is_replaced_by_one_gap_when_there_is_no_disk_source()
    {
        var r = Run(PsList("updsvc.exe", 2), PsList("other.exe", 3));
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "MEM-NODISK");
        var g = Assert.Single(r.Gaps);
        Assert.Contains("MEM-NODISK", g.Reason);
    }

    [Fact]
    public void Nodisk_reads_process_creation_event_logs_as_disk_sources()
    {
        var r = Run(Ev("EventLog:Security", "a.exe", null, "4688"), Ev("EventLog:Security", "b.exe", null, "4624"), PsList("a.exe", 1), PsList("zzz.exe", 2));
        Assert.Equal("zzz.exe", Assert.Single(Of(r, "MEM-NODISK")).Process);
        Assert.Single(Run(Ev("EventLog:Security", "b.exe", null, "4624"), PsList("a.exe", 1)).Gaps);   // a logon event alone is not a disk source
    }

    [Fact]
    public void Cap_and_dedupe_per_rule_pid_and_process()
    {
        var t = new List<TimelineEvent> { PsList("System", 4) };
        for (int i = 0; i < 250; i++) { t.Add(PsScan($"h{i}.exe", 1000 + i)); t.Add(PsScan($"h{i}.exe", 1000 + i)); }
        var r = Run([.. t]);
        Assert.Equal(MemoryFindings.MaxPerRule, Of(r, "MEM-HIDDEN").Count());
    }

    [Fact]
    public void Running_twice_on_the_same_timeline_gives_identical_output()
    {
        var t = new[] { PsList("System", 4), PsScan("updsvc.exe", 6666), Malfind("updsvc.exe", 6666, true), Net("updsvc.exe", 6666, "10.0.0.1"), Cmd("updsvc.exe", 6666, @"C:\Users\Public\updsvc.exe"), Prefetch("A.EXE") };
        string Dump(MemoryFindingsResult r) => string.Join("\n", r.Findings.Select(f => $"{f.FindingId}|{f.RuleId}|{f.Title}|{f.Severity}|{string.Join(",", f.SupportingEvidence.Select(e => e.Locator))}")) + "\n" + string.Join("|", r.Gaps.Select(g => g.Reason));
        Assert.Equal(Dump(Run(t)), Dump(Run(t)));
    }

    [Fact]
    public void Memory_data_names_are_not_plain_text_in_the_assembly()
    {
        var bytes = File.ReadAllBytes(typeof(MemoryFindings).Assembly.Location);
        Assert.DoesNotContain("fontdrvhost", System.Text.Encoding.Unicode.GetString(bytes));
        Assert.DoesNotContain("fontdrvhost", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void The_pipeline_turns_volatility_output_into_memory_findings()
    {
        const string psList = """[ { "PID": 4, "PPID": 0, "ImageFileName": "System", "Offset(V)": 1, "Threads": 1, "CreateTime": "2026-11-07T08:00:00+00:00", "ExitTime": null, "__children": [] } ]""";
        const string psScan = """
            [ { "PID": 4, "PPID": 0, "ImageFileName": "System", "Offset(V)": 1, "Threads": 1, "CreateTime": "2026-11-07T08:00:00+00:00", "ExitTime": null, "__children": [] },
              { "PID": 6666, "PPID": 4, "ImageFileName": "updsvc.exe", "Offset(V)": 2, "Threads": 3, "CreateTime": "2026-11-07T09:30:00+00:00", "ExitTime": null, "__children": [] } ]
            """;
        const string net = """[ { "Offset": 1, "Proto": "TCPv4", "LocalAddr": "10.0.0.2", "LocalPort": 5000, "ForeignAddr": "10.0.0.9", "ForeignPort": 445, "State": "ESTABLISHED", "PID": 6666, "Owner": "updsvc.exe", "Created": "2026-11-07T09:31:00+00:00", "__children": [] } ]""";
        const string mal = """[ { "PID": 6666, "Process": "updsvc.exe", "Start VPN": "0x1f0000", "End VPN": "0x1fffff", "Protection": "PAGE_EXECUTE_READWRITE", "Hexdump": "4d 5a 90 00", "__children": [] } ]""";
        const string cmd = """[ { "PID": 6666, "Process": "updsvc.exe", "Args": "C:\\Users\\Public\\updsvc.exe -svc", "__children": [] } ]""";
        var files = new List<string>();
        foreach (var (n, c) in new[] { ("pslist.json", psList), ("psscan.json", psScan), ("netscan.json", net), ("malfind.json", mal), ("cmdline.json", cmd) })
        { var p = Path.Combine(_dir, n); File.WriteAllText(p, c); files.Add(p); }
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "memf", TestScopes.Valid());
        InvestigationPipeline.Import(ws, files);
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        foreach (var rule in new[] { "MEM-HIDDEN", "MEM-INJECT", "MEM-NETPROC", "MEM-PATH" })
            Assert.Contains(r.Findings, f => f.RuleId == rule && f.Process == "updsvc.exe");
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "MEM-NODISK");
        Assert.Contains(r.Gaps, g => g.Artifact.Contains("memorie") && g.Reason.Contains("MEM-NODISK"));
        Assert.Equal(r.Findings.Count, r.Findings.Select(f => f.FindingId).Distinct().Count());
    }
}
