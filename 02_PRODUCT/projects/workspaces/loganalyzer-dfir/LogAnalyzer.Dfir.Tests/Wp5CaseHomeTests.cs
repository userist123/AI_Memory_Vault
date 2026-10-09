using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Home;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using LogAnalyzer.Verification;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP5 (R9.1, U3, U21, U22): open an existing case, coverage matrix, Home aggregator, recent cases.</summary>
public sealed class Wp5CaseHomeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_wp5_{Guid.NewGuid():N}");
    public Wp5CaseHomeTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static readonly IReadOnlyList<LogAnalyzer.Dfir.Parsing.ParserDescriptor> Parsers = WindowsParsers.Registry.Descriptors;

    // ---- a case written the way the pipeline writes it ----

    private sealed record Built(CaseWorkspace Ws, EvidenceItem Evidence, List<Finding> Findings, List<TimelineEvent> Timeline, List<EvidenceGap> Gaps, List<CollectionRow> Collection, List<ParseResult> Parsing);

    private static Finding F(string id, Severity sev, string evidenceId) => new()
    {
        FindingId = id, RuleId = "R-" + id, Title = "titlu " + id, Description = "descriere " + id, Severity = sev, Classification = Classification.Direct, Confidence = Confidence.Medium,
        SupportingEvidence = [new EvidenceRef(evidenceId, "EventRecordID=5", "x")],
    };

    private Built BuildCase(string name = "caz", bool withFindings = true, bool withVerification = false)
    {
        var ws = CaseWorkspace.Create(Path.Combine(_dir, name), new CaseInfo { CaseId = "CASE-" + name, Name = name, CreatedAtUtc = DateTimeOffset.UtcNow, Host = "AUDITED", Scope = TestScopes.Valid() });
        var p = Path.Combine(ws.RawDir("Import:System"), "System.evtx");
        File.WriteAllText(p, "not really an evtx");
        var ev = ws.RegisterStored(p, "System.evtx", "Import:System", "evtx", TemporalType.Historical, "unit", "1");
        var t0 = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var timeline = new List<TimelineEvent>
        {
            new() { Time = new Timestamp(t0, "raw1", "FILETIME"), Source = "EventLog:System", EvidenceId = ev.EvidenceId, EventId = "7045", Summary = "serviciu nou, cu virgulă, și \"ghilimele\"", Locator = "rec=1", Classification = Classification.Direct, SemanticType = SemanticType.Configuration, Pid = 44, Process = "svc.exe" },
            new() { Time = Timestamp.Unknown("0"), Source = "EventLog:System", EvidenceId = ev.EvidenceId, Summary = "fără oră", Locator = "rec=2" },
        };
        var findings = new List<Finding> { F("F-0001", Severity.High, ev.EvidenceId), F("F-0002", Severity.Low, ev.EvidenceId) };
        var gaps = new List<EvidenceGap> { new("SRUM", EvidenceStatus.NotAvailable, "SRUDB.dat lipsește", "Fără utilizarea rețelei", "PCAP", "Cu drepturi de administrator") };
        var collection = new List<CollectionRow> { new("SrumCollector", EvidenceStatus.NotAvailable, 0, "SRUDB.dat lipsește", t0, t0.AddSeconds(2)) };
        var parsing = new List<ParseResult>
        {
            new() { EvidenceId = ev.EvidenceId, Parser = "EvtxParser", ParserVersion = "1.0", ParserStatus = "VALIDATED", Status = EvidenceStatus.Success, Records = 2 },
        };
        if (withFindings)
        {
            var analysis = Path.Combine(ws.Root, "Analysis");
            Directory.CreateDirectory(analysis);
            TimelineCsv.Write(Path.Combine(analysis, "timeline.csv"), timeline);
            ws.RecordOutput("Analysis/timeline.csv", "test", "1", [ev.EvidenceId]);
            File.WriteAllText(Path.Combine(analysis, "findings.json"), SchemaVersions.WithVersion(new { Findings = findings, Gaps = gaps, Collection = collection, RejectedFindings = Array.Empty<object>() }, SchemaVersions.Findings));
            ws.RecordOutput("Analysis/findings.json", "test", "1", [ev.EvidenceId]);
            File.WriteAllText(Path.Combine(analysis, "parsing.json"), JsonSerializer.Serialize(parsing));
            ws.RecordOutput("Analysis/parsing.json", "test", "1", [ev.EvidenceId]);
            File.WriteAllText(Path.Combine(analysis, "run_state.json"), JsonSerializer.Serialize(new RunState(ws.Info.CaseId, OperationState.Partial, "Colectare: 1 indisponibilă", DateTimeOffset.UtcNow)));
            ws.RecordOutput("Analysis/run_state.json", "test", "1");
            ws.WriteDependencies(findings, []);
            if (withVerification)
            {
                var report = new VerificationReport { CaseId = ws.Info.CaseId, GeneratedUtc = DateTimeOffset.UtcNow, Counts = VerificationReport.CountVerdicts([StandardState.Supported, StandardState.Unproven]) };
                report.Findings.Add(new FindingVerdict { FindingId = "F-0001", RuleId = "R-F-0001", Title = "t", Verdict = StandardState.Supported, Reason = "ok" });
                report.Findings.Add(new FindingVerdict { FindingId = "F-0002", RuleId = "R-F-0002", Title = "t", Verdict = StandardState.Unproven, Reason = "slab" });
                File.WriteAllText(Path.Combine(analysis, "verification.json"), report.ToJson());
                ws.RecordOutput("Analysis/verification.json", "test", "1");
            }
        }
        return new Built(ws, ev, findings, timeline, gaps, collection, parsing);
    }

    private static CaseLoadResult Open(Built b, bool recheck = true) => CaseLoader.Open(b.Ws.Root, recheck: recheck);

    // ---- R9.1: open an existing case ----

    [Fact]
    public void A_case_written_by_the_pipeline_files_loads_back_with_the_same_findings_timeline_gaps_scope_and_verification()
    {
        var b = BuildCase(withVerification: true);
        var res = Open(b);
        Assert.True(res.Opened, res.RefusedReason);
        var c = res.Case!;
        Assert.Equal(b.Findings.Select(f => (f.FindingId, f.Title, f.Severity)), c.Result.Findings.Select(f => (f.FindingId, f.Title, f.Severity)));
        Assert.Equal(b.Gaps, c.Result.Gaps);
        Assert.Equal(b.Collection.Select(r => (r.Collector, r.Status, r.Errors)), c.Result.Collection.Select(r => (r.Collector, r.Status, r.Errors)));
        Assert.Equal(b.Parsing.Select(p => (p.EvidenceId, p.Parser, p.Status, p.Records)), c.Result.Parsing.Select(p => (p.EvidenceId, p.Parser, p.Status, p.Records)));
        Assert.Equal(2, c.Result.Timeline.Count);
        var t = c.Result.Timeline[0];
        Assert.Equal(b.Timeline[0].Time.Utc, t.Time.Utc);
        Assert.Equal(b.Timeline[0].Summary, t.Summary);
        Assert.Equal(("7045", 44, "svc.exe", Classification.Direct, SemanticType.Configuration), (t.EventId, t.Pid, t.Process, t.Classification, t.SemanticType));
        Assert.Null(c.Result.Timeline[1].Time.Utc);
        Assert.Equal(b.Ws.Info.Scope.Purpose, c.Workspace.Info.Scope.Purpose);
        Assert.Equal(b.Ws.Info.Scope.Approver, c.Workspace.Info.Scope.Approver);
        Assert.Equal(b.Ws.Info.Scope.SystemsInScope, c.Workspace.Info.Scope.SystemsInScope);
        Assert.NotNull(c.Result.Verification);
        Assert.Equal(StandardState.Supported, c.Result.Findings.Single(f => f.FindingId == "F-0001").Verification.State);
        Assert.Equal(OperationState.Partial, c.Result.State);
        Assert.False(c.ReadOnly);
        Assert.Equal(CaseLifecycle.Active, c.Lifecycle);
        Assert.Equal(RecheckVerdict.Valid, c.Recheck!.Verdict);
        Assert.Contains(c.Notes, n => n.Contains("timeline.csv"));
    }

    [Fact]
    public void The_pipeline_output_of_a_real_run_loads_back()
    {
        var sample = Path.Combine(_dir, "sample.bin");
        File.WriteAllText(sample, "MZ not really a program");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "run", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [sample]);
        var run = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        var res = CaseLoader.Open(ws.Root);
        Assert.True(res.Opened, res.RefusedReason);
        var c = res.Case!;
        Assert.Equal(run.Findings.Select(f => f.FindingId), c.Result.Findings.Select(f => f.FindingId));
        Assert.Equal(run.Timeline.Count, c.Result.Timeline.Count);
        Assert.Equal(run.Parsing.Select(p => (p.EvidenceId, p.Parser, p.Status)), c.Result.Parsing.Select(p => (p.EvidenceId, p.Parser, p.Status)));
        Assert.Equal(run.State, c.Result.State);
        Assert.NotNull(c.Result.Verification);
        Assert.Equal(run.Verification!.Counts, c.Result.Verification!.Counts);
        Assert.Equal(ws.Info.Scope.Purpose, c.Workspace.Info.Scope.Purpose);
    }

    [Fact]
    public void A_tampered_evidence_file_opens_read_only_with_the_reason_and_the_workspace_refuses_changes()
    {
        var b = BuildCase();
        var full = b.Ws.FullPath(b.Evidence.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.AppendAllText(full, "TAMPERED");
        var c = Open(b).Case!;
        Assert.True(c.ReadOnly);
        Assert.Equal(RecheckVerdict.Modified, c.Recheck!.Verdict);
        Assert.Contains(c.ReadOnlyReasons, r => r.Contains("reverificarea integrității a eșuat"));
        Assert.True(c.Workspace.IsReadOnly);
        var src = Path.Combine(_dir, "x.txt"); File.WriteAllText(src, "x");
        Assert.Throws<InvalidOperationException>(() => c.Workspace.ImportFile(src, "s", "txt", TemporalType.Historical, "c", "1"));
        Assert.Throws<InvalidOperationException>(() => c.Workspace.RecordOutput("Analysis/findings.json", "p", "1"));
        Assert.False(c.Workspace.TransitionState(b.Evidence.EvidenceId, EvidenceState.Verified, "me").Ok);
        Assert.Equal(TrustState.Compromised, c.Home.TrustState);
    }

    [Fact]
    public void A_tampered_custody_chain_opens_read_only()
    {
        var b = BuildCase();
        var lines = File.ReadAllLines(b.Ws.CustodyJsonlPath).ToList();
        lines[0] = lines[0].Replace("\"unit\"", "\"forged\"");
        File.WriteAllLines(b.Ws.CustodyJsonlPath, lines);
        var c = Open(b).Case!;
        Assert.True(c.ReadOnly);
        Assert.Equal(RecheckVerdict.ChainBroken, c.Recheck!.Verdict);
    }

    [Fact]
    public void A_sealed_case_opens_read_only()
    {
        var b = BuildCase();
        CaseClosure.Close(b.Ws, "operator");
        var c = Open(b).Case!;
        Assert.True(c.ReadOnly);
        Assert.Equal(CaseLifecycle.Sealed, c.Lifecycle);
        Assert.Contains(c.ReadOnlyReasons, r => r.Contains("închis"));
        Assert.Contains(File.ReadAllLines(c.Workspace.AuditChainPath), l => l.Contains("case.opened_readonly"));
    }

    [Fact]
    public void An_archived_case_opens_read_only()
    {
        var b = BuildCase();
        Assert.True(b.Ws.TransitionState(b.Evidence.EvidenceId, EvidenceState.Verified, "me").Ok);
        Assert.True(b.Ws.TransitionState(b.Evidence.EvidenceId, EvidenceState.InAnalysis, "me").Ok);
        Assert.True(b.Ws.TransitionState(b.Evidence.EvidenceId, EvidenceState.Archived, "me").Ok);
        var c = Open(b).Case!;
        Assert.True(c.ReadOnly);
        Assert.Equal(CaseLifecycle.Archived, c.Lifecycle);
    }

    [Fact]
    public void Evidence_that_findings_rest_on_being_modified_makes_the_case_invalidated_and_read_only()
    {
        var b = BuildCase();
        var full = b.Ws.FullPath(b.Evidence.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.AppendAllText(full, "x");
        var c = Open(b).Case!;
        Assert.Equal(CaseLifecycle.Invalidated, c.Lifecycle);
        Assert.True(c.ReadOnly);
        Assert.Contains(c.ReadOnlyReasons, r => r.Contains("INVALIDATED"));
    }

    [Fact]
    public void A_case_with_a_sound_recheck_that_is_not_sealed_stays_writable()
    {
        var b = BuildCase();
        var c = Open(b).Case!;
        Assert.False(c.ReadOnly);
        Assert.False(c.Workspace.IsReadOnly);
    }

    [Fact]
    public void A_findings_file_of_an_unknown_version_is_refused_with_the_reason_and_the_folder_is_not_written()
    {
        var b = BuildCase();
        var fp = Path.Combine(b.Ws.Root, "Analysis", "findings.json");
        File.WriteAllText(fp, File.ReadAllText(fp).Replace("\"schema_version\": \"2.0\"", "\"schema_version\": \"9.0\""));
        var before = File.ReadAllText(b.Ws.AppAuditLogPath);
        var res = Open(b);
        Assert.False(res.Opened);
        Assert.Contains("9.0", res.RefusedReason);
        Assert.Contains("findings.json", res.RefusedReason);
        Assert.Equal(before, File.ReadAllText(b.Ws.AppAuditLogPath));   // refused before anything was audited in that folder
    }

    [Fact]
    public void A_case_of_an_unknown_case_version_is_refused()
    {
        var b = BuildCase();
        var cj = Path.Combine(b.Ws.Root, "case.json");
        File.WriteAllText(cj, File.ReadAllText(cj).Replace("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"7.0\""));
        var res = Open(b);
        Assert.False(res.Opened);
        Assert.Contains("7.0", res.RefusedReason);
    }

    [Fact]
    public void A_missing_folder_or_a_folder_without_case_json_is_refused_with_the_reason()
    {
        Assert.Contains("nu există", CaseLoader.Open(Path.Combine(_dir, "nope")).RefusedReason);
        var empty = Path.Combine(_dir, "empty"); Directory.CreateDirectory(empty);
        Assert.Contains("case.json", CaseLoader.Open(empty).RefusedReason);
        var broken = Path.Combine(_dir, "broken"); Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "case.json"), "{ not json");
        Assert.False(CaseLoader.Open(broken).Opened);
    }

    [Fact]
    public void A_case_that_was_never_analysed_loads_as_not_started_and_says_so_never_as_nothing_found()
    {
        var b = BuildCase(withFindings: false);
        var c = Open(b).Case!;
        Assert.Equal(OperationState.NotStarted, c.Result.State);
        Assert.Empty(c.Result.Findings);
        Assert.Equal(AttentionLevel.Undetermined, c.Home.Attention);
        Assert.Contains("nu a fost rulată", c.Home.AttentionLabel);
        Assert.Contains(c.Notes, n => n.Contains("findings.json lipsește"));
    }

    [Fact]
    public void Opening_a_case_audits_it_as_the_identity_the_chains_use()
    {
        var b = BuildCase();
        using var _ = OperatorIdentity.Scope(null, authenticationRequired: true);
        var c = Open(b).Case!;
        Assert.Equal("(neautentificat)", OperatorIdentity.Who);
        var opened = File.ReadAllLines(c.Workspace.AppAuditLogPath).Last(l => l.Split('\t')[2] == "case.opened");
        Assert.Equal(OperatorIdentity.Who, opened.Split('\t')[1]);
        Assert.Contains(File.ReadAllLines(c.Workspace.AuditChainPath), l => l.Contains("case.opened") && l.Contains("(neautentificat)"));
    }

    [Fact]
    public async Task OpenAsync_runs_the_recheck_on_a_background_task()
    {
        var b = BuildCase();
        var res = await CaseLoader.OpenAsync(b.Ws.Root);
        Assert.True(res.Opened);
        Assert.NotNull(res.Case!.Recheck);
    }

    // ---- recent cases ----

    [Fact]
    public void Recent_cases_remember_the_path_title_time_and_state_newest_first_and_show_a_missing_folder_as_missing_without_removing_it()
    {
        var file = Path.Combine(_dir, "recent.json");
        var r = new RecentCases(file);
        var a = Path.Combine(_dir, "a"); var bb = Path.Combine(_dir, "b"); Directory.CreateDirectory(a); Directory.CreateDirectory(bb);
        r.Record(a, "Caz A", CaseLifecycle.Active, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        r.Record(bb, "Caz B", CaseLifecycle.Sealed, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        r.Record(a, "Caz A", CaseLifecycle.Archived, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));   // moved to the top, not duplicated
        var list = new RecentCases(file).Load();   // a new instance: it is stored
        Assert.Equal(["Caz A", "Caz B"], list.Select(x => x.Entry.Title));
        Assert.Equal("ARCHIVED", list[0].StateText);
        Assert.Equal("SEALED", list[1].StateText);
        Directory.Delete(bb);
        var after = new RecentCases(file).Load();
        Assert.Equal(2, after.Count);
        Assert.False(after[1].Exists);
        Assert.Contains("LIPSĂ", after[1].StateText);
        new RecentCases(file).Forget(bb);   // only an explicit action removes it
        Assert.Single(new RecentCases(file).Load());
    }

    [Fact]
    public void Opening_records_the_case_in_the_recent_list_with_its_state()
    {
        var b = BuildCase();
        CaseClosure.Close(b.Ws, "op");
        var recent = new RecentCases(Path.Combine(_dir, "recent2.json"));
        Assert.True(CaseLoader.Open(b.Ws.Root, recent).Opened);
        var e = Assert.Single(recent.Load());
        Assert.Equal("SEALED", e.Entry.State);
        Assert.Equal("caz", e.Entry.Title);
        Assert.False(CaseLoader.Open(Path.Combine(_dir, "ghost"), recent).Opened);
        Assert.Single(recent.Load());   // a refused open is not remembered
    }

    // ---- U21: coverage matrix ----

    private static EvidenceItem Ev(string id, string sourceType, string path = "") => new()
    { EvidenceId = id, CaseId = "C", Source = "s", SourceType = sourceType, StoredPath = path.Length > 0 ? path : "Raw/" + id };

    private static ParseResult Pr(string ev, string parser, EvidenceStatus st, int records = 1, string error = "") => new()
    { EvidenceId = ev, Parser = parser, ParserVersion = "1.0", Status = st, Records = records, Error = error };

    private static CoverageMatrix Cov(IEnumerable<EvidenceItem>? ev = null, IEnumerable<ParseResult>? pr = null, IEnumerable<CollectorRun>? runs = null, IEnumerable<EvidenceGap>? gaps = null) =>
        CoverageMatrix.Build(new CoverageInputs((ev ?? []).ToList(), (pr ?? []).ToList(), (runs ?? []).ToList(), (gaps ?? []).ToList(), Parsers));

    private static CoverageRow Row(CoverageMatrix m, string id) => m.Rows.Single(r => r.FamilyId == id);

    [Fact]
    public void An_empty_case_has_unknown_coverage_never_full_or_clean()
    {
        var m = Cov();
        Assert.Equal(OverallCoverage.Unknown, m.Overall);
        Assert.All(m.Rows.Where(r => r.State != CoverageState.NotSupported), r => Assert.Equal(CoverageState.NotCollected, r.State));
    }

    [Fact]
    public void Every_listed_family_is_present_and_network_profiles_are_not_supported_with_the_reason()
    {
        var m = Cov();
        foreach (var id in new[] { "evtx", "prefetch", "amcache", "shimcache", "bam", "srum", "registry", "usn", "lnk_jumplist", "usb", "network_profiles", "browser", "tasks", "services" })
            Assert.Contains(m.Rows, r => r.FamilyId == id);
        var np = Row(m, "network_profiles");
        Assert.Equal(CoverageState.NotSupported, np.State);
        Assert.False(string.IsNullOrWhiteSpace(np.Reason));
        Assert.Equal("fără parser", np.ParserStatus);
        Assert.Contains(m.NotSupported, r => r.FamilyId == "network_profiles");
    }

    [Fact]
    public void A_parsed_family_is_collected_and_the_parser_status_comes_from_the_descriptor()
    {
        var m = Cov([Ev("EV-1", "evtx", "Raw/System.evtx")], [Pr("EV-1", "EvtxParser", EvidenceStatus.Success, 10)]);
        var r = Row(m, "evtx");
        Assert.Equal(CoverageState.Collected, r.State);
        Assert.Equal(Parsers.Single(d => d.ParserId == "EvtxParser").StatusName, r.ParserStatus);
        Assert.Equal(CoverageState.NotCollected, Row(m, "prefetch").State);
    }

    [Fact]
    public void An_empty_parse_is_partial_and_says_it_does_not_prove_absence_of_activity()
    {
        var m = Cov([Ev("EV-1", "evtx", "Raw/Security.evtx")], [Pr("EV-1", "EvtxParser", EvidenceStatus.Empty, 0)]);
        var r = Row(m, "evtx");
        Assert.Equal(CoverageState.Partial, r.State);
        Assert.Contains("nu dovedește absența activității", r.Reason);
    }

    [Fact]
    public void A_failed_parse_makes_the_family_unavailable_and_a_mixed_one_partial()
    {
        var failed = Cov([Ev("EV-1", "prefetch", "Raw/A.pf")], [Pr("EV-1", "PrefetchParser", EvidenceStatus.Failed, 0, "format necunoscut")]);
        Assert.Equal(CoverageState.Unavailable, Row(failed, "prefetch").State);
        Assert.Contains("format necunoscut", Row(failed, "prefetch").Reason);
        var mixed = Cov([Ev("EV-1", "prefetch", "Raw/A.pf"), Ev("EV-2", "prefetch", "Raw/B.pf")],
                        [Pr("EV-1", "PrefetchParser", EvidenceStatus.Success), Pr("EV-2", "PrefetchParser", EvidenceStatus.Failed, 0, "x")]);
        Assert.Equal(CoverageState.Partial, Row(mixed, "prefetch").State);
    }

    [Fact]
    public void Evidence_present_but_never_parsed_is_partial_not_collected()
    {
        var m = Cov([Ev("EV-1", "amcache", "Raw/Amcache.hve")]);
        Assert.Equal(CoverageState.Partial, Row(m, "amcache").State);
    }

    [Fact]
    public void A_collector_that_failed_makes_its_family_unavailable_and_one_that_never_ran_leaves_it_not_collected()
    {
        var m = Cov(runs: [new CollectorRun("SrumCollector", EvidenceStatus.NotAvailable, 0, "SRUDB.dat blocat")]);
        Assert.Equal(CoverageState.Unavailable, Row(m, "srum").State);
        Assert.Contains("SRUDB.dat blocat", Row(m, "srum").Reason);
        Assert.Equal(CoverageState.NotCollected, Row(m, "prefetch").State);
        var ranEmpty = Cov(runs: [new CollectorRun("PrefetchCollector", EvidenceStatus.Success, 0, "")]);
        Assert.Equal(CoverageState.Unavailable, Row(ranEmpty, "prefetch").State);
    }

    [Fact]
    public void An_audit_gap_on_the_security_log_downgrades_an_otherwise_collected_event_log_family()
    {
        var gap = new EvidenceGap("Security 4688 (crearea proceselor)", EvidenceStatus.NotAvailable, "audit dezactivat", "i", "a", "r");
        var m = Cov([Ev("EV-1", "evtx", "Raw/Security.evtx")], [Pr("EV-1", "EvtxParser", EvidenceStatus.Success, 50)], gaps: [gap]);
        var r = Row(m, "evtx");
        Assert.Equal(CoverageState.Partial, r.State);
        Assert.Contains("Security 4688", r.Reason);
    }

    [Fact]
    public void Shared_system_hive_evidence_counts_for_each_family_its_parser_reads()
    {
        var m = Cov([Ev("EV-1", "system_hive", "Raw/SYSTEM")],
                    [Pr("EV-1", "SystemHiveExecutionParser", EvidenceStatus.Success), Pr("EV-1", "UsbDevicesParser", EvidenceStatus.Success), Pr("EV-1", "ServicesParser", EvidenceStatus.Failed, 0, "e")]);
        Assert.Equal(CoverageState.Collected, Row(m, "shimcache").State);
        Assert.Equal(CoverageState.Collected, Row(m, "bam").State);
        Assert.Equal(CoverageState.Collected, Row(m, "usb").State);
        Assert.Equal(CoverageState.Unavailable, Row(m, "services").State);
    }

    [Fact]
    public void Overall_is_full_only_when_every_supported_family_is_collected_and_still_reports_the_unsupported_ones()
    {
        var ev = new List<EvidenceItem>(); var pr = new List<ParseResult>(); int n = 0;
        foreach (var d in Parsers)
        {
            var type = d.SourceTypes[0].EndsWith('*') ? "evtx" : d.SourceTypes[0];
            var e = Ev("EV-" + ++n, type, "Raw/" + (d.FileNames.FirstOrDefault(f => !f.StartsWith('.') && !f.Contains('*')) ?? "f" + n));
            if (!ev.Any(x => x.EvidenceId == e.EvidenceId)) ev.Add(e);
        }
        ev.Add(Ev("EV-live", "live_snapshot"));
        foreach (var e in ev.Where(x => x.SourceType != "live_snapshot"))
            foreach (var d in Parsers.Where(d => d.Accepts(e))) pr.Add(Pr(e.EvidenceId, d.ParserId, EvidenceStatus.Success, 3));
        var m = Cov(ev, pr);
        Assert.Equal(OverallCoverage.Full, m.Overall);
        Assert.Contains("nesuportate", m.OverallReason);
        Assert.NotEmpty(m.NotSupported);
        Assert.Empty(m.Gaps);
    }

    [Fact]
    public void Overall_is_minimal_with_one_family_and_partial_with_about_half()
    {
        var one = Cov([Ev("EV-1", "evtx", "Raw/System.evtx")], [Pr("EV-1", "EvtxParser", EvidenceStatus.Success)]);
        Assert.Equal(OverallCoverage.Minimal, one.Overall);
        var ev = new[] { ("evtx", "EvtxParser"), ("prefetch", "PrefetchParser"), ("amcache", "AmcacheParser"), ("srum", "SrumNetworkParser"), ("usn_journal", "UsnJournalParser"), ("lnk", "LnkParser"), ("task_xml", "ScheduledTaskParser") };
        var evidence = ev.Select((x, i) => Ev("EV-" + i, x.Item1, "Raw/f" + i + (x.Item1 == "evtx" ? ".evtx" : ""))).ToList();
        var parsing = ev.Select((x, i) => Pr("EV-" + i, x.Item2, EvidenceStatus.Success)).ToList();
        var half = Cov(evidence, parsing);
        Assert.Equal(OverallCoverage.Partial, half.Overall);
    }

    [Fact]
    public void Coverage_of_a_loaded_case_reflects_its_collection_gaps_and_parsing()
    {
        var b = BuildCase();
        var c = Open(b).Case!;
        Assert.Equal(CoverageState.Collected, Row(c.Coverage, "evtx").State);
        Assert.Equal(CoverageState.Unavailable, Row(c.Coverage, "srum").State);
        Assert.Equal(OverallCoverage.Minimal, c.Coverage.Overall);
    }

    // ---- U3: Home aggregator ----

    private static HomeInputs Inputs(Dictionary<Severity, int>? findings = null, CoverageMatrix? cov = null, OperationState state = OperationState.Completed,
                                      RecheckVerdict? integrity = RecheckVerdict.Valid, Dictionary<string, int>? verification = null) => new()
    {
        CaseOpen = true, CaseName = "c", AnalysisState = state, FindingsBySeverity = findings ?? new(), Coverage = cov ?? FullCoverage(), Integrity = integrity,
        VerificationCounts = verification,
    };

    private static CoverageMatrix FullCoverage()
    {
        var ev = new List<EvidenceItem>(); var pr = new List<ParseResult>(); int n = 0;
        foreach (var d in Parsers)
        {
            var type = d.SourceTypes[0].EndsWith('*') ? "evtx" : d.SourceTypes[0];
            ev.Add(Ev("EV-" + ++n, type, "Raw/" + (d.FileNames.FirstOrDefault(f => !f.StartsWith('.') && !f.Contains('*')) ?? "f" + n)));
        }
        ev.Add(Ev("EV-live", "live_snapshot"));
        foreach (var e in ev.Where(x => x.SourceType != "live_snapshot"))
            foreach (var d in Parsers.Where(d => d.Accepts(e))) pr.Add(Pr(e.EvidenceId, d.ParserId, EvidenceStatus.Success, 3));
        return Cov(ev, pr);
    }

    private static void AssertNeverReassuring(HomeSummary h)
    {
        foreach (var text in new[] { h.AttentionLabel, h.Problem, h.Seriousness, h.Trust, h.Found, string.Join(" ", h.NextSteps), h.CoverageLine })
        {
            Assert.DoesNotContain("SAFE", text); Assert.DoesNotContain("NORMAL", text); Assert.DoesNotContain("SECURED", text);
            Assert.DoesNotContain("curat", text.Replace("nu înseamnă că sistemul este curat", ""), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Home_with_zero_findings_and_good_coverage_says_nothing_detected_in_the_analysed_sources_never_safe_or_normal()
    {
        var h = HomeAggregator.Build(Inputs());
        Assert.Equal(AttentionLevel.NothingDetected, h.Attention);
        Assert.Contains("Nimic detectat în sursele analizate", h.AttentionLabel);
        Assert.Contains("acoperire completă", h.AttentionLabel);
        Assert.Contains("nu înseamnă că sistemul este curat", h.Problem);
        AssertNeverReassuring(h);
    }

    [Fact]
    public void Home_with_zero_findings_and_minimal_or_unknown_coverage_is_undetermined()
    {
        var minimal = Cov([Ev("EV-1", "evtx", "Raw/System.evtx")], [Pr("EV-1", "EvtxParser", EvidenceStatus.Success)]);
        var h = HomeAggregator.Build(Inputs(cov: minimal));
        Assert.Equal(AttentionLevel.Undetermined, h.Attention);
        Assert.Contains("acoperirea este minimă", h.AttentionLabel);
        Assert.Equal(AttentionLevel.Undetermined, HomeAggregator.Build(Inputs(cov: Cov())).Attention);
        AssertNeverReassuring(h);
    }

    [Fact]
    public void Home_with_zero_findings_but_an_analysis_that_did_not_finish_is_undetermined_even_with_full_coverage()
    {
        foreach (var s in new[] { OperationState.NotStarted, OperationState.Failed, OperationState.Cancelled, OperationState.Running, OperationState.Blocked })
            Assert.Equal(AttentionLevel.Undetermined, HomeAggregator.Build(Inputs(state: s)).Attention);
    }

    [Fact]
    public void Home_severity_levels_follow_the_highest_finding()
    {
        Assert.Equal(AttentionLevel.Critical, HomeAggregator.Build(Inputs(new() { [Severity.Critical] = 1, [Severity.Low] = 4 })).Attention);
        Assert.Equal(AttentionLevel.High, HomeAggregator.Build(Inputs(new() { [Severity.High] = 2 })).Attention);
        Assert.Equal(AttentionLevel.Medium, HomeAggregator.Build(Inputs(new() { [Severity.Medium] = 2 })).Attention);
        Assert.Equal(AttentionLevel.Low, HomeAggregator.Build(Inputs(new() { [Severity.Info] = 3 })).Attention);
        var h = HomeAggregator.Build(Inputs(new() { [Severity.Critical] = 1, [Severity.Low] = 4 }));
        Assert.Contains("5 constatări", h.Found);
        Assert.Contains("critică", h.Seriousness, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_integrity_answers_follow_the_recheck_and_a_missing_recheck_is_undetermined_not_intact()
    {
        Assert.Equal(TrustState.Intact, HomeAggregator.Build(Inputs(integrity: RecheckVerdict.Valid)).TrustState);
        Assert.Equal(TrustState.Limited, HomeAggregator.Build(Inputs(integrity: RecheckVerdict.Legacy)).TrustState);
        Assert.Equal(TrustState.Limited, HomeAggregator.Build(Inputs(integrity: RecheckVerdict.Unverified)).TrustState);
        foreach (var v in new[] { RecheckVerdict.Modified, RecheckVerdict.Missing, RecheckVerdict.ChainBroken })
            Assert.Equal(TrustState.Compromised, HomeAggregator.Build(Inputs(integrity: v)).TrustState);
        var none = HomeAggregator.Build(Inputs(integrity: null));
        Assert.Equal(TrustState.Undetermined, none.TrustState);
        Assert.StartsWith("Nedeterminat", none.Trust);
    }

    [Fact]
    public void Home_with_compromised_integrity_says_so_in_the_problem_and_puts_it_first_in_the_next_steps()
    {
        var h = HomeAggregator.Build(Inputs(integrity: RecheckVerdict.Modified));
        Assert.Contains("integritatea probelor este compromisă", h.Problem);
        Assert.Contains("Nu folosiți rezultatele ca probă", h.NextSteps[0]);
    }

    [Fact]
    public void Home_verification_not_run_is_undetermined_and_counts_are_shown_when_present()
    {
        Assert.Contains("nedeterminat", HomeAggregator.Build(Inputs(new() { [Severity.High] = 1 })).Found);
        var counts = VerificationReport.CountVerdicts([StandardState.Supported, StandardState.Unproven, StandardState.Contradicted]);
        var h = HomeAggregator.Build(Inputs(new() { [Severity.High] = 3 }, verification: counts));
        Assert.Contains("1 susținute, 1 nedovedite sau neevaluate, 1 contrazise sau respinse", h.Found);
        Assert.Contains(h.NextSteps, s => s.Contains("contrazise"));
    }

    [Fact]
    public void Home_next_steps_are_short_data_driven_and_name_the_missing_sources()
    {
        var partial = Cov([Ev("EV-1", "evtx", "Raw/System.evtx"), Ev("EV-2", "prefetch", "Raw/A.pf")], [Pr("EV-1", "EvtxParser", EvidenceStatus.Success), Pr("EV-2", "PrefetchParser", EvidenceStatus.Success)],
                          runs: [new CollectorRun("SrumCollector", EvidenceStatus.NotAvailable, 0, "x")]);
        var h = HomeAggregator.Build(Inputs(new() { [Severity.High] = 1 }, cov: partial));
        Assert.InRange(h.NextSteps.Count, 1, HomeAggregator.MaxNextSteps);
        Assert.Contains(h.NextSteps, s => s.Contains("Completați sursele") && s.Contains("SRUM"));
        Assert.Contains(h.NextSteps, s => s.Contains("severitate ridicată"));
    }

    [Fact]
    public void Home_without_an_open_case_is_undetermined_and_offers_the_three_intents()
    {
        var h = HomeBuilder.NoCase();
        Assert.Equal(AttentionLevel.Undetermined, h.Attention);
        Assert.Contains("Niciun caz deschis".ToLowerInvariant(), h.AttentionLabel.ToLowerInvariant());
        Assert.Contains("deschideți un caz existent", h.NextSteps[0]);
        Assert.Equal(TrustState.Undetermined, h.TrustState);
        AssertNeverReassuring(h);
    }

    [Fact]
    public void Home_of_a_loaded_case_is_built_from_the_case_files()
    {
        var b = BuildCase(withVerification: true);
        var h = Open(b).Case!.Home;
        Assert.Equal(AttentionLevel.High, h.Attention);
        Assert.Equal(TrustState.Intact, h.TrustState);
        Assert.Contains("2 constatări", h.Found);
        Assert.Contains("titlu F-0001", h.Found);
        Assert.Contains("1 susținute, 1 nedovedite", h.Found);
        Assert.Contains("Acoperire: Minimă", h.CoverageLine);
    }
}
