using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Verification;
using Xunit;
using static LogAnalyzer.Dfir.Tests.VerificationLab;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP4 verification layer: every check with a positive and a negative case, every verdict, the report.</summary>
public sealed class VerificationTests : IDisposable
{
    private readonly VerificationLab _lab = new();
    public void Dispose() => _lab.Dispose();

    private FindingVerdict Only(VerificationReport r) => Assert.Single(r.Findings);

    // ---------------------------------------------------------------- verdicts

    [Fact]
    public void Two_independent_artifact_kinds_that_agree_give_VERIFIED()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Verified, v.Verdict);
        Assert.All(v.Checks.Where(c => c.Effect is not null), c => Assert.Fail(c.CheckId));
        Assert.Contains("Prefetch", v.Reason); Assert.Contains("BAM", v.Reason);
        Assert.Contains("nu externă", v.Reason);   // says what kind of verification it is
    }

    [Fact]
    public void A_single_artifact_kind_gives_SUPPORTED_not_VERIFIED()
    {
        _lab.Evidence("pf", "prefetch");
        _lab.Event("Prefetch", "pf", "pf1", T0, path: @"C:\a\x.exe");
        _lab.Add("F-1", "EXEC-USERPATH", SemanticType.Execution, null, T0, _lab.Ref("pf", "pf1"));
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Supported, v.Verdict);
        Assert.Contains("VERIFIED cere cel puțin două", v.Reason);
    }

    [Fact]
    public void Two_records_of_the_same_artifact_kind_are_not_independent()
    {
        _lab.Evidence("pf1", "prefetch"); _lab.Evidence("pf2", "prefetch");
        _lab.Event("Prefetch", "pf1", "a", T0, path: @"C:\a\x.exe"); _lab.Event("Prefetch", "pf2", "b", T0, path: @"C:\a\x.exe");
        _lab.Add("F-1", "EXEC-USERPATH", SemanticType.Execution, null, T0, _lab.Ref("pf1", "a"), _lab.Ref("pf2", "b"));
        _lab.WriteAll();
        Assert.Equal(StandardState.Supported, Only(_lab.Verify()).Verdict);
    }

    [Fact]
    public void A_correlation_is_at_most_SUPPORTED_even_with_several_kinds()
    {
        _lab.ExecutionWithTwoKinds();
        _lab.Findings.Clear();
        _lab.Add("F-1", "EXEC-USERPATH-CORRELATED", SemanticType.Correlation, null, T0.AddSeconds(1), _lab.Ref("pf", "pf1"), _lab.Ref("bam", "bam1"));
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Supported, v.Verdict);
        Assert.Contains("corelația nu este dovadă", v.Reason);
    }

    [Fact]
    public void A_finding_without_semantic_type_is_NOT_ASSESSED_when_nothing_else_fails()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteTimeline(); _lab.WriteLegacyFindings(); _lab.WriteGraph(); _lab.WriteDependencies(); _lab.WriteParsing("pf", "bam");
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.NotAssessed, v.Verdict);
        Assert.Equal(CheckOutcome.NotApplicable, Check(v, CheckIds.Sufficiency).Outcome);
        Assert.Equal("NECUNOSCUT", v.SemanticType);
    }

    [Fact]
    public void The_most_severe_check_wins_REJECTED_over_CONTRADICTED_over_UNPROVEN_over_UNKNOWN()
    {
        // Prefetch-only execution, written with last < first (CONTRADICTED), evidence tampered (REJECTED), no graph (UNKNOWN), Amcache-only would be UNPROVEN.
        _lab.Evidence("am", "amcache");
        _lab.Event("Amcache", "am", "a1", T0);
        _lab.Add("F-1", "X", SemanticType.Execution, T0.AddMinutes(10), T0, _lab.Ref("am", "a1"));
        _lab.WriteTimeline(); _lab.WriteFindings(); _lab.WriteDependencies(); _lab.WriteParsing("am");   // no graph.json
        var before = _lab.Verify();
        Assert.Equal(StandardState.Contradicted, Only(before).Verdict);   // UNPROVEN + UNKNOWN + CONTRADICTED -> CONTRADICTED
        var v0 = Only(before);
        Assert.Equal(StandardState.Unproven, Check(v0, CheckIds.Sufficiency).Effect);
        Assert.Equal(StandardState.Unknown, Check(v0, CheckIds.Graph).Effect);
        _lab.Tamper("am");
        Assert.Equal(StandardState.Rejected, Only(_lab.Verify()).Verdict);   // + REJECTED wins
    }

    [Fact]
    public void UNPROVEN_beats_UNKNOWN()
    {
        _lab.Evidence("am", "amcache"); _lab.Event("Amcache", "am", "a1", T0);
        _lab.Add("F-1", "X", SemanticType.Execution, null, T0, _lab.Ref("am", "a1"));
        _lab.WriteTimeline(); _lab.WriteFindings(); _lab.WriteDependencies(); _lab.WriteParsing("am");   // graph.json missing -> UNKNOWN too
        Assert.Equal(StandardState.Unproven, Only(_lab.Verify()).Verdict);
    }

    [Fact]
    public void UNKNOWN_when_only_the_graph_cannot_be_confirmed()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteTimeline(); _lab.WriteFindings(); _lab.WriteDependencies(); _lab.WriteParsing("pf", "bam");
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Unknown, v.Verdict);
        Assert.Contains("graph.json lipsește", v.Reason);
    }

    // ---------------------------------------------------------------- R10.1 sufficiency

    [Fact]
    public void Execution_claim_with_only_presence_artifacts_is_UNPROVEN_and_lists_the_missing_kinds()
    {
        _lab.Evidence("am", "amcache"); _lab.Evidence("lnk", "lnk");
        _lab.Event("Amcache", "am", "a1", T0); _lab.Event("LNK", "lnk", "l1", T0);
        _lab.Add("F-1", "X", SemanticType.Execution, null, T0, _lab.Ref("am", "a1"), _lab.Ref("lnk", "l1"));
        _lab.WriteAll();
        var c = Check(Only(_lab.Verify()), CheckIds.Sufficiency);
        Assert.Equal(StandardState.Unproven, c.Effect);
        Assert.Contains("Prezența unui fișier nu dovedește execuția", c.Reason);
        foreach (var kind in new[] { "Prefetch", "BAM", "EventLog:Security 4688", "Sysmon 1" }) Assert.Contains(c.Details, d => d.Contains(kind));
    }

    [Theory]
    [InlineData("Prefetch", "", "prefetch")]
    [InlineData("BAM", "", "file")]
    [InlineData("EventLog:Security", "4688", "evtx")]
    [InlineData("EventLog:Microsoft-Windows-Sysmon/Operational", "1", "evtx")]
    public void Each_execution_artifact_satisfies_an_execution_claim(string source, string eventId, string type)
    {
        _lab.Evidence("e", type); _lab.Event(source, "e", "l1", T0, eventId);
        _lab.Add("F-1", "X", SemanticType.Execution, null, T0, _lab.Ref("e", "l1"));
        _lab.WriteAll();
        Assert.Equal(CheckOutcome.Pass, Check(Only(_lab.Verify()), CheckIds.Sufficiency).Outcome);
    }

    [Fact]
    public void An_event_log_record_that_is_not_4688_or_sysmon_1_is_not_an_execution_artifact()
    {
        _lab.Evidence("e"); _lab.Event("EventLog:Security", "e", "l1", T0, "4624");
        _lab.Add("F-1", "X", SemanticType.Execution, null, T0, _lab.Ref("e", "l1"));
        _lab.WriteAll();
        Assert.Equal(StandardState.Unproven, Check(Only(_lab.Verify()), CheckIds.Sufficiency).Effect);
    }

    [Fact]
    public void Execution_claim_whose_event_log_record_cannot_be_identified_is_UNKNOWN_not_UNPROVEN()
    {
        _lab.Evidence("e");   // evtx, but no timeline row for it
        _lab.Event("Prefetch", "e", "other", T0);
        _lab.Add("F-1", "X", SemanticType.Execution, null, T0, _lab.Ref("e", "missing-row"));
        _lab.WriteAll();
        var c = Check(Only(_lab.Verify()), CheckIds.Sufficiency);
        Assert.Equal(StandardState.Unknown, c.Effect);
    }

    [Fact]
    public void Configuration_claim_needs_a_configuration_artifact()
    {
        _lab.Evidence("pf", "prefetch"); _lab.Event("Prefetch", "pf", "p", T0);
        _lab.Add("F-1", "X", SemanticType.Configuration, null, T0, _lab.Ref("pf", "p"));
        _lab.Evidence("t", "task_xml"); _lab.Event("ScheduledTask", "t", "t1", T0);
        _lab.Add("F-2", "Y", SemanticType.Configuration, null, T0, _lab.Ref("t", "t1"));
        _lab.WriteAll();
        var r = _lab.Verify();
        Assert.Equal(StandardState.Unproven, Check(r.Of("F-1")!, CheckIds.Sufficiency).Effect);
        Assert.Equal(CheckOutcome.Pass, Check(r.Of("F-2")!, CheckIds.Sufficiency).Outcome);
    }

    [Fact]
    public void Correlation_needs_two_sources_inference_and_attribution_are_never_sufficient()
    {
        _lab.Evidence("pf", "prefetch"); _lab.Event("Prefetch", "pf", "p", T0);
        _lab.Evidence("b", "file"); _lab.Event("BAM", "b", "b1", T0);
        _lab.Add("F-1", "C1", SemanticType.Correlation, null, T0, _lab.Ref("pf", "p"));
        _lab.Add("F-2", "C2", SemanticType.Correlation, null, T0, _lab.Ref("pf", "p"), _lab.Ref("b", "b1"));
        _lab.Add("F-3", "I", SemanticType.Inference, null, T0, _lab.Ref("pf", "p"), _lab.Ref("b", "b1"));
        _lab.Add("F-4", "A", SemanticType.Attribution, null, T0, _lab.Ref("pf", "p"), _lab.Ref("b", "b1"));
        _lab.WriteAll();
        var r = _lab.Verify();
        Assert.Equal(StandardState.Unproven, Check(r.Of("F-1")!, CheckIds.Sufficiency).Effect);
        Assert.Equal(CheckOutcome.Pass, Check(r.Of("F-2")!, CheckIds.Sufficiency).Outcome);
        Assert.Equal(StandardState.Unproven, r.Of("F-3")!.Verdict);
        Assert.Equal(StandardState.Unproven, r.Of("F-4")!.Verdict);
    }

    [Fact]
    public void Observation_and_presence_need_one_identified_record()
    {
        _lab.Evidence("am", "amcache"); _lab.Event("Amcache", "am", "a", T0);
        _lab.Add("F-1", "P", SemanticType.Presence, null, T0, _lab.Ref("am", "a"));
        _lab.Add("F-2", "O", SemanticType.Observation, null, T0, _lab.Ref("am", "a"));
        _lab.WriteAll();
        var r = _lab.Verify();
        Assert.All(r.Findings, f => Assert.Equal(CheckOutcome.Pass, Check(f, CheckIds.Sufficiency).Outcome));
        Assert.All(r.Findings, f => Assert.Equal(StandardState.Supported, f.Verdict));
    }

    // ---------------------------------------------------------------- R10.2 provenance

    [Fact]
    public void Provenance_passes_for_indexed_intact_evidence_with_a_parser()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        Assert.Equal(CheckOutcome.Pass, Check(Only(_lab.Verify()), CheckIds.Provenance).Outcome);
    }

    [Fact]
    public void Provenance_rejects_evidence_that_is_not_in_the_index()
    {
        _lab.ExecutionWithTwoKinds();
        _lab.Findings[0].SupportingEvidence.Add(new EvidenceRef("EV-999999", "x", "ghost", new string('A', 64)));
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Contains("EV-999999 nu există în indexul probelor", Check(v, CheckIds.Provenance).Reason);
    }

    [Fact]
    public void Provenance_rejects_evidence_modified_after_acquisition()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        _lab.Tamper("bam");
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Contains("MODIFICATĂ", Check(v, CheckIds.Provenance).Reason);
    }

    [Fact]
    public void Provenance_rejects_evidence_that_is_missing_from_the_case()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        _lab.Remove("pf");
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Contains("LIPSEȘTE", Check(v, CheckIds.Provenance).Reason);
    }

    [Fact]
    public void Provenance_uses_the_latest_recheck_invalidations()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        _lab.Ws.Recheck();   // everything intact: no invalidation
        Assert.Equal(StandardState.Verified, Only(_lab.Verify()).Verdict);
        // The evidence changes AFTER the re-check; with this run's own hashing off, only the re-check files could tell, so also write the file by hand.
        var inv = new InvalidationsFile { CaseId = "CASE-V", Items = [new Invalidation("finding", "F-0001", Invalidation.Invalidated, "dovada EV-x MODIFICATĂ", [_lab.Ev["pf"].EvidenceId])] };
        Json.Write(_lab.Ws.InvalidationsPath, inv);
        var v = Only(_lab.Verify(new VerificationOptions { Clock = () => T0, RehashEvidence = false }));
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Contains("INVALIDATED", Check(v, CheckIds.Provenance).Reason);
    }

    [Fact]
    public void Provenance_rejects_a_finding_hash_that_differs_from_the_index()
    {
        _lab.ExecutionWithTwoKinds();
        _lab.Findings[0].SupportingEvidence[0] = _lab.Findings[0].SupportingEvidence[0] with { Sha256 = new string('B', 64) };
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Contains("hash-ul din constatare diferă", Check(v, CheckIds.Provenance).Reason);
    }

    [Fact]
    public void Provenance_rejects_evidence_no_parser_recorded_when_parsing_json_exists_but_is_UNKNOWN_when_it_does_not()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteTimeline(); _lab.WriteFindings(); _lab.WriteGraph(); _lab.WriteDependencies();
        _lab.WriteParsing("pf");   // BAM evidence has no parser record
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Contains("nicio înregistrare a unui parser", Check(v, CheckIds.Provenance).Reason);

        File.Delete(_lab.Analysis("parsing.json"));
        var v2 = Only(_lab.Verify());
        Assert.Equal(StandardState.Unknown, Check(v2, CheckIds.Provenance).Effect);
        Assert.Contains("parsing.json lipsește", Check(v2, CheckIds.Provenance).Reason);
    }

    // ---------------------------------------------------------------- R10.8 unsupported

    [Fact]
    public void A_finding_with_no_supporting_evidence_is_REJECTED()
    {
        _lab.Evidence("pf", "prefetch");
        _lab.Add("F-1", "X", SemanticType.Execution, null, T0);
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Rejected, v.Verdict);
        Assert.Equal(StandardState.Rejected, Check(v, CheckIds.Unsupported).Effect);
        Assert.Contains("nicio probă", v.Reason);
    }

    // ---------------------------------------------------------------- R10.4 temporal

    [Fact]
    public void Temporal_first_after_last_is_CONTRADICTED()
    {
        _lab.ExecutionWithTwoKinds(); _lab.Findings[0] = Clone(_lab.Findings[0], first: T0.AddHours(1), last: T0); _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains("prima apariție", Check(v, CheckIds.Temporal).Reason);
    }

    [Fact]
    public void Temporal_time_after_the_acquisition_of_the_case_is_CONTRADICTED()
    {
        _lab.ExecutionWithTwoKinds();
        var future = DateTimeOffset.UtcNow.AddDays(30);
        _lab.Findings[0] = Clone(_lab.Findings[0], first: future, last: future); _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains("după momentul achiziției", Check(v, CheckIds.Temporal).Reason);
    }

    [Fact]
    public void Temporal_an_event_after_the_acquisition_of_its_evidence_is_CONTRADICTED()
    {
        _lab.Evidence("pf", "prefetch");
        _lab.Event("Prefetch", "pf", "pf1", DateTimeOffset.UtcNow.AddHours(3));   // the evidence was acquired "now": the event cannot be later
        _lab.Add("F-1", "X", SemanticType.Execution, null, null, _lab.Ref("pf", "pf1"));
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains("după achiziția probei", Check(v, CheckIds.Temporal).Reason);
    }

    [Fact]
    public void Temporal_supporting_event_outside_the_claimed_window_is_CONTRADICTED_inside_is_fine()
    {
        _lab.ExecutionWithTwoKinds();   // events at T0 and T0+1s, window ends at T0+1s
        _lab.Findings[0] = Clone(_lab.Findings[0], first: T0.AddHours(1), last: T0.AddHours(2)); _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains("iese din fereastra revendicată", Check(v, CheckIds.Temporal).Reason);

        _lab.Findings[0] = Clone(_lab.Findings[0], first: T0.AddMinutes(-1), last: T0.AddMinutes(1)); _lab.WriteFindings();
        Assert.Equal(StandardState.Verified, Only(_lab.Verify()).Verdict);
    }

    private void WriteManipulatedWindow(DateTimeOffset start, DateTimeOffset end, string kind = "CLOCK_JUMP") =>
        File.WriteAllText(_lab.Analysis("policy_timeline.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["schema_version"] = "1.0",
            ["TimeWindows"] = new[] { new { Start = start, End = end, Kind = kind, Description = "ceasul a fost mutat", EvidenceId = "EV", Locator = "L1" } },
        }));

    [Fact]
    public void Temporal_inside_a_manipulated_clock_window_is_UNKNOWN_not_CONTRADICTED()
    {
        _lab.ExecutionWithTwoKinds(); _lab.Findings[0] = Clone(_lab.Findings[0], first: T0.AddHours(1), last: T0); _lab.WriteAll();
        Assert.Equal(StandardState.Contradicted, Only(_lab.Verify()).Verdict);   // without the window: a real ordering violation
        WriteManipulatedWindow(T0.AddMinutes(-30), T0.AddHours(2));
        var c = Check(Only(_lab.Verify()), CheckIds.Temporal);
        Assert.Equal(StandardState.Unknown, c.Effect);
        Assert.Contains("ceasul", c.Reason);
        Assert.Contains("CLOCK_JUMP", c.Reason);
    }

    [Fact]
    public void Temporal_a_manipulated_window_elsewhere_does_not_hide_a_real_contradiction()
    {
        _lab.ExecutionWithTwoKinds(); _lab.Findings[0] = Clone(_lab.Findings[0], first: T0.AddHours(1), last: T0); _lab.WriteAll();
        WriteManipulatedWindow(T0.AddDays(-20), T0.AddDays(-19));
        Assert.Equal(StandardState.Contradicted, Check(Only(_lab.Verify()), CheckIds.Temporal).Effect);
    }

    [Fact]
    public void Temporal_unknown_event_time_is_UNKNOWN_not_a_violation()
    {
        _lab.Evidence("pf", "prefetch"); _lab.Event("Prefetch", "pf", "pf1", null);
        _lab.Add("F-1", "X", SemanticType.Execution, T0, T0, _lab.Ref("pf", "pf1"));
        _lab.WriteAll();
        var c = Check(Only(_lab.Verify()), CheckIds.Temporal);
        Assert.Equal(StandardState.Unknown, c.Effect);
        Assert.Contains("oră necunoscută", c.Reason);
    }

    [Fact]
    public void Temporal_an_execution_claim_without_any_time_is_UNKNOWN()
    {
        _lab.Evidence("pf", "prefetch"); _lab.Event("Prefetch", "pf", "pf1", null);
        _lab.Add("F-1", "X", SemanticType.Execution, null, null, _lab.Ref("pf", "pf1"));
        _lab.WriteAll();
        Assert.Equal(StandardState.Unknown, Check(Only(_lab.Verify()), CheckIds.Temporal).Effect);
    }

    // ---------------------------------------------------------------- R10.5 graph

    [Fact]
    public void Graph_node_and_edges_that_exist_pass()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var c = Check(Only(_lab.Verify()), CheckIds.Graph);
        Assert.Equal(CheckOutcome.Pass, c.Outcome);
    }

    [Fact]
    public void Graph_missing_node_missing_file_and_dangling_edges_are_UNKNOWN_with_the_reason()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        // 1. the finding's node is not in the graph
        var gp = _lab.Analysis("graph.json");
        var original = File.ReadAllText(gp);
        File.WriteAllText(gp, original.Replace("Finding:f-0001", "Finding:f-other"));
        var c1 = Check(Only(_lab.Verify()), CheckIds.Graph);
        Assert.Equal(StandardState.Unknown, c1.Effect); Assert.Contains("lipsește din graf", c1.Reason);
        // 2. an edge to evidence that is not in the case
        File.WriteAllText(gp, original.Replace("\"EvidenceId\": \"" + _lab.Ev["pf"].EvidenceId + "\"", "\"EvidenceId\": \"EV-424242\""));
        var c2 = Check(Only(_lab.Verify()), CheckIds.Graph);
        Assert.Equal(StandardState.Unknown, c2.Effect); Assert.Contains("EV-424242", c2.Reason);
        // 3. an edge from a node that does not exist
        File.WriteAllText(gp, original.Replace("\"SourceEntity\": \"File:", "\"SourceEntity\": \"Ghost:"));
        Assert.Equal(StandardState.Unknown, Check(Only(_lab.Verify()), CheckIds.Graph).Effect);
        // 4. no graph at all
        File.Delete(gp);
        var c4 = Check(Only(_lab.Verify()), CheckIds.Graph);
        Assert.Equal(StandardState.Unknown, c4.Effect); Assert.Contains("graph.json lipsește", c4.Reason);
    }

    // ---------------------------------------------------------------- R10.6 contradictions (one test per rule: positive and negative)

    private const string Exe = @"C:\Users\u\AppData\Local\Temp\x.exe";

    private Finding ExecAt(DateTimeOffset when, string path = Exe)
    {
        _lab.Evidence("pf", "prefetch"); _lab.Event("Prefetch", "pf", "pf1", when, path: path);
        return _lab.Add("F-1", "EXEC-USERPATH", SemanticType.Execution, null, when, _lab.Ref("pf", "pf1"));
    }

    private void Usn(string locator, DateTimeOffset when, string reason, string path = Exe)
    {
        if (!_lab.Ev.ContainsKey("usn")) _lab.Evidence("usn", "usn_journal");
        _lab.Event("USN", "usn", locator, when, path: path, summary: $"USN: x.exe — {reason}", semantics: "change journal record (operation closed)");
    }

    [Fact]
    public void Rule_execution_before_creation_fires_when_the_file_was_created_after_the_claimed_run()
    {
        ExecAt(T0); Usn("USN=1", T0.AddHours(2), "File create | Close");
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains(v.ContradictingEvidence, c => c.StartsWith("CONTRA-EXEC-BEFORE-CREATION"));
        Assert.Equal(StandardState.Contradicted, Check(v, CheckIds.Contradictions).Effect);
    }

    [Fact]
    public void Rule_execution_before_creation_does_not_fire_when_created_before_or_never_recorded()
    {
        ExecAt(T0); Usn("USN=1", T0.AddHours(-2), "File create | Close");
        _lab.WriteAll();
        Assert.DoesNotContain(Only(_lab.Verify()).ContradictingEvidence, c => c.StartsWith("CONTRA-EXEC-BEFORE-CREATION"));
        // another file's creation after the run is irrelevant
        Usn("USN=2", T0.AddHours(3), "File create | Close", @"C:\Users\u\AppData\Local\Temp\other.exe");
        _lab.WriteTimeline();
        Assert.Empty(Only(_lab.Verify()).ContradictingEvidence);
    }

    [Fact]
    public void Rule_execution_before_creation_ignores_a_later_re_creation_when_an_earlier_creation_exists()
    {
        ExecAt(T0); Usn("USN=1", T0.AddHours(-5), "File create | Close"); Usn("USN=2", T0.AddHours(5), "File create | Close");
        _lab.WriteAll();
        Assert.Empty(Only(_lab.Verify()).ContradictingEvidence);
    }

    [Fact]
    public void Rule_execution_of_absent_file_fires_when_another_source_shows_it_deleted_before_the_run()
    {
        ExecAt(T0); Usn("USN=1", T0.AddHours(-3), "File delete | Close");
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains(v.ContradictingEvidence, c => c.StartsWith("CONTRA-EXEC-FILE-ABSENT"));
    }

    [Fact]
    public void Rule_execution_of_absent_file_does_not_fire_for_a_delete_after_the_run_or_when_re_created()
    {
        ExecAt(T0); Usn("USN=1", T0.AddHours(3), "File delete | Close");   // deleted AFTER it ran: normal clean-up
        _lab.WriteAll();
        Assert.Empty(Only(_lab.Verify()).ContradictingEvidence);
        Usn("USN=2", T0.AddHours(-3), "File delete | Close"); Usn("USN=3", T0.AddHours(-2), "File create | Close");   // deleted, then created again before the run
        _lab.WriteTimeline();
        Assert.Empty(Only(_lab.Verify()).ContradictingEvidence);
    }

    [Fact]
    public void Rules_match_a_reconstructed_partial_usn_path_by_its_tail()
    {
        ExecAt(T0); Usn("USN=1", T0.AddHours(2), "File create | Close", @"…\Users\u\AppData\Local\Temp\x.exe");
        _lab.WriteAll();
        Assert.Contains(Only(_lab.Verify()).ContradictingEvidence, c => c.StartsWith("CONTRA-EXEC-BEFORE-CREATION"));
    }

    [Fact]
    public void Rule_log_cleared_but_records_span_the_clear_time_fires()
    {
        _lab.Evidence("sec");
        _lab.Event("EventLog:Security", "sec", "EventRecordID=10", T0.AddHours(-3), "4624");
        _lab.Event("EventLog:Security", "sec", "EventRecordID=11", T0, "1102");
        _lab.Event("EventLog:Security", "sec", "EventRecordID=12", T0.AddHours(3), "4624");
        _lab.Add("F-1", "LOG-TAMPER", SemanticType.Observation, T0, T0, _lab.Ref("sec", "EventRecordID=11"));
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains(v.ContradictingEvidence, c => c.StartsWith("CONTRA-LOG-CLEAR-SPANNING-RECORDS"));
    }

    [Fact]
    public void Rule_log_cleared_does_not_fire_for_a_normal_clear_nor_for_records_of_another_log()
    {
        _lab.Evidence("sec"); _lab.Evidence("sys");
        _lab.Event("EventLog:Security", "sec", "EventRecordID=11", T0, "1102");               // the clear is the first record
        _lab.Event("EventLog:Security", "sec", "EventRecordID=12", T0.AddHours(3), "4624");   // only records after it
        _lab.Event("EventLog:System", "sys", "EventRecordID=1", T0.AddHours(-3), "7036");      // records of ANOTHER file, before
        _lab.Add("F-1", "LOG-TAMPER", SemanticType.Observation, T0, T0, _lab.Ref("sec", "EventRecordID=11"));
        _lab.WriteAll();
        Assert.Empty(Only(_lab.Verify()).ContradictingEvidence);
    }

    [Fact]
    public void A_custom_rule_can_be_added_and_its_finding_is_CONTRADICTED()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var v = Only(_lab.Verify(new VerificationOptions { Clock = () => T0, ExtraContradictionRules = [new AlwaysContradicts()] }));
        Assert.Equal(StandardState.Contradicted, v.Verdict);
        Assert.Contains("TEST-RULE: ceva nu se potrivește", v.ContradictingEvidence);
    }

    private sealed class AlwaysContradicts : IContradictionRule
    {
        public string RuleId => "TEST-RULE";
        public string Description => "test";
        public IEnumerable<string> Evaluate(RuleContext c) => ["TEST-RULE: ceva nu se potrivește"];
    }

    // ---------------------------------------------------------------- R10.7 missing evidence

    [Fact]
    public void Missing_evidence_merges_the_rules_own_list_with_expected_artifacts_absent_from_the_case()
    {
        _lab.Evidence("pf", "prefetch"); _lab.Event("Prefetch", "pf", "pf1", T0);
        var f = _lab.Add("F-1", "EXEC-USERPATH", SemanticType.Execution, null, T0, _lab.Ref("pf", "pf1"));
        f.MissingEvidence.Add("Semnătura și hash-ul programului.");
        _lab.WriteAll();
        var v = Only(_lab.Verify());
        Assert.Contains("Semnătura și hash-ul programului.", v.MissingEvidence);
        var computed = Assert.Single(v.MissingEvidence, m => m.StartsWith("Artefacte așteptate"));
        Assert.Contains("BAM", computed); Assert.Contains("EventLog:Security 4688", computed); Assert.Contains("Sysmon 1", computed);
        Assert.DoesNotContain("Prefetch,", computed);   // Prefetch IS in the case
        Assert.Equal(CheckOutcome.Info, Check(v, CheckIds.MissingEvidence).Outcome);
        Assert.Equal(StandardState.Supported, v.Verdict);   // missing evidence alone never lowers the verdict
    }

    // ---------------------------------------------------------------- report

    [Fact]
    public void The_report_counts_verdicts_names_the_banner_and_warns_on_CONTRADICTED_or_REJECTED()
    {
        _lab.ExecutionWithTwoKinds("F-1");                                                      // VERIFIED
        _lab.Evidence("am", "amcache"); _lab.Event("Amcache", "am", "a1", T0);
        _lab.Add("F-2", "X", SemanticType.Execution, null, T0, _lab.Ref("am", "a1"));          // UNPROVEN
        _lab.Add("F-3", "Y", SemanticType.Execution, null, T0);                                 // REJECTED
        _lab.WriteAll();
        var r = _lab.Verify();
        Assert.Equal(StandardState.Verified, r.Of("F-1")!.Verdict);
        Assert.Equal(StandardState.Unproven, r.Of("F-2")!.Verdict);
        Assert.Equal(StandardState.Rejected, r.Of("F-3")!.Verdict);
        Assert.Equal(1, r.Count(StandardState.Verified)); Assert.Equal(1, r.Count(StandardState.Unproven)); Assert.Equal(1, r.Count(StandardState.Rejected));
        Assert.Equal(0, r.Count(StandardState.Contradicted));
        Assert.Equal(7, r.Counts.Count);
        Assert.Equal("Verificare: 1 VERIFIED, 1 UNPROVEN, 1 REJECTED", r.Banner);
        Assert.StartsWith("ATENȚIE: 1 REJECTED", r.Warning);
        Assert.Contains("nu le exportați în Vault", r.Warning);
    }

    [Fact]
    public void The_banner_has_no_warning_without_CONTRADICTED_or_REJECTED_and_handles_an_empty_case()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var r = _lab.Verify();
        Assert.Equal("Verificare: 1 VERIFIED", r.Banner);
        Assert.Null(r.Warning);
        Assert.Equal("Verificare: nicio constatare de verificat", VerificationReport.Line(VerificationReport.CountVerdicts([])));
        Assert.Equal("Verificare: 3 VERIFIED, 5 SUPPORTED, 2 UNPROVEN, 1 CONTRADICTED",
            VerificationReport.Line(new Dictionary<string, int> { ["VERIFIED"] = 3, ["SUPPORTED"] = 5, ["UNPROVEN"] = 2, ["CONTRADICTED"] = 1 }));
    }

    [Fact]
    public void Verify_writes_versioned_json_registers_it_in_custody_audits_the_run_and_never_touches_findings_json()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var findingsBefore = Hashing.Sha256File(_lab.Analysis("findings.json"));
        var r = _lab.Verify();

        Assert.Equal(findingsBefore, Hashing.Sha256File(_lab.Analysis("findings.json")));
        var path = _lab.Analysis("verification.json");
        Assert.True(File.Exists(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(SchemaVersions.Verification, doc.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal("VERIFIED", doc.RootElement.GetProperty("findings")[0].GetProperty("verdict").GetString());
        Assert.Equal(7, doc.RootElement.GetProperty("counts").EnumerateObject().Count());
        Assert.True(doc.RootElement.GetProperty("findings")[0].GetProperty("checks").GetArrayLength() >= 8);   // every check result is there to drill into
        Assert.Contains("nu este o verificare externă", doc.RootElement.GetProperty("limits").GetString()!, StringComparison.OrdinalIgnoreCase);

        var back = VerificationReport.Read(path)!;
        Assert.Equal(r.Banner, back.Banner);
        Assert.Equal(StandardState.Verified, back.Of("F-0001")!.Verdict);

        var custody = File.ReadAllLines(_lab.Ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(e => e.GetProperty("action").GetString() == "output.written" && e.GetProperty("to").GetString() == "Analysis/verification.json").ToList();
        var entry = Assert.Single(custody);
        Assert.Equal(Hashing.Sha256File(path), entry.GetProperty("sha256").GetString());
        Assert.Contains("verification.run\t", File.ReadAllText(_lab.Ws.AppAuditLogPath).Replace("\r", ""));
        var audit = File.ReadAllLines(_lab.Ws.AppAuditLogPath).Last(l => l.Contains("\tverification.run\t"));
        Assert.Contains("VERIFIED=1", audit);
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, _lab.Ws.VerifyChains().Custody.Status);
    }

    [Fact]
    public void The_same_case_gives_byte_identical_reports()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var a = File.ReadAllText(_lab.Verify().Let(_ => _lab.Analysis("verification.json")));
        var b = File.ReadAllText(_lab.Verify().Let(_ => _lab.Analysis("verification.json")));
        Assert.Equal(a, b);
    }

    [Fact]
    public void The_verification_module_depends_on_Dfir_Core_only()
    {
        var refs = typeof(CaseVerifier).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("LogAnalyzer", StringComparison.Ordinal)).ToList();
        Assert.Equal(["LogAnalyzer.Dfir.Core"], refs);
        var all = typeof(CaseVerifier).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain("System.Net.Http", all); Assert.DoesNotContain("System.Diagnostics.Process", all); Assert.DoesNotContain("System.Net.Sockets", all);
    }

    // ---------------------------------------------------------------- old cases

    [Fact]
    public void An_old_case_without_graph_dependencies_timeline_and_parsing_files_gives_UNKNOWN_with_reasons_not_a_crash()
    {
        _lab.ExecutionWithTwoKinds();
        _lab.WriteFindings();   // only evidence + findings.json
        var r = _lab.Verify();
        var v = Only(r);
        Assert.Equal(StandardState.Unknown, v.Verdict);
        Assert.Contains("graph.json lipsește", v.Reason);
        Assert.Contains("dependencies.json lipsește", Check(v, CheckIds.Dependencies).Reason);
        Assert.Equal("lipsește", r.Inputs["Analysis/graph.json"]);
        Assert.Equal("lipsește", r.Inputs["Analysis/dependencies.json"]);
    }

    [Fact]
    public void A_case_with_no_analysis_at_all_gives_an_empty_report_with_a_note()
    {
        var r = _lab.Verify();
        Assert.Empty(r.Findings);
        Assert.Contains(r.Notes, n => n.Contains("findings.json lipsește"));
        Assert.True(File.Exists(_lab.Analysis("verification.json")));
    }

    [Fact]
    public void A_corrupt_or_newer_schema_input_is_a_note_not_a_crash()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        File.WriteAllText(_lab.Analysis("graph.json"), "{ not json");
        File.WriteAllText(_lab.Analysis("dependencies.json"), "{\"schema_version\":\"9.0\"}");
        var r = _lab.Verify();
        Assert.Contains(r.Notes, n => n.StartsWith("graph.json nu a putut fi citit"));
        Assert.Contains(r.Notes, n => n.StartsWith("dependencies.json nu a putut fi citit"));
        Assert.Equal(StandardState.Unknown, Only(r).Verdict);
    }

    // ---------------------------------------------------------------- AI statements

    private void WriteAi(params (string Text, string Cite, string Kind, string Ev)[] statements)
    {
        var accepted = statements.Select(s => new
        {
            Section = "hypotheses", Text = s.Text, Classification = "UNPROVEN",
            Citations = new[] { new { Item = s.Cite, Kind = s.Kind, Evidence = s.Ev.Length > 0 ? new[] { new { EvidenceId = s.Ev, Locator = "l", Description = "d", Sha256 = "" } } : [] } },
        });
        File.WriteAllText(_lab.Analysis("ai_reasoning.json"), JsonSerializer.Serialize(new { Model = "m", Accepted = accepted, Rejected = Array.Empty<object>() }));
    }

    [Fact]
    public void An_AI_statement_is_never_VERIFIED_even_when_it_cites_a_VERIFIED_finding()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        WriteAi(("Ar putea fi executat de utilizator.", "F-0001", "finding", _lab.Ev["pf"].EvidenceId));
        var r = _lab.Verify();
        Assert.Equal(StandardState.Verified, r.Of("F-0001")!.Verdict);
        var s = Assert.Single(r.AiStatements);
        Assert.Equal(StandardState.Inferred, s.Verdict);
        Assert.NotEqual(StandardState.Verified, s.Verdict);
        Assert.Contains("niciodată VERIFIED", s.Reason);
    }

    [Fact]
    public void An_AI_statement_citing_a_REJECTED_or_CONTRADICTED_finding_is_CONTRADICTED()
    {
        _lab.ExecutionWithTwoKinds("F-1");
        _lab.Add("F-2", "Y", SemanticType.Execution, null, T0);   // REJECTED
        _lab.Evidence("am", "amcache"); _lab.Event("Amcache", "am", "a1", T0);
        _lab.Add("F-3", "Z", SemanticType.Execution, T0.AddHours(1), T0, _lab.Ref("am", "a1"));   // CONTRADICTED (first after last)
        _lab.WriteAll();
        WriteAi(("A rulat.", "F-2", "finding", ""), ("A rulat și el.", "F-3", "finding", ""));
        var r = _lab.Verify();
        Assert.Equal(StandardState.Rejected, r.Of("F-2")!.Verdict);
        Assert.Equal(StandardState.Contradicted, r.Of("F-3")!.Verdict);
        Assert.All(r.AiStatements, s => Assert.Equal(StandardState.Contradicted, s.Verdict));
    }

    [Fact]
    public void An_AI_statement_keeps_the_weaker_verdict_of_what_it_cites_and_citing_only_events_is_UNPROVEN()
    {
        _lab.Evidence("am", "amcache"); _lab.Event("Amcache", "am", "a1", T0);
        _lab.Add("F-1", "Z", SemanticType.Execution, null, T0, _lab.Ref("am", "a1"));   // UNPROVEN
        _lab.WriteAll();
        WriteAi(("Ar putea fi relevant.", "F-1", "finding", ""), ("Un eveniment.", "E1", "event", _lab.Ev["am"].EvidenceId), ("Altă constatare.", "F-99", "finding", ""));
        var r = _lab.Verify();
        Assert.Equal(StandardState.Unproven, r.AiStatements[0].Verdict);
        Assert.Equal(StandardState.Unproven, r.AiStatements[1].Verdict);
        Assert.Equal(StandardState.Unknown, r.AiStatements[2].Verdict);   // not in the report
    }

    [Fact]
    public void An_AI_statement_citing_modified_evidence_is_REJECTED()
    {
        _lab.ExecutionWithTwoKinds(); _lab.Evidence("other", "file"); _lab.WriteAll();
        WriteAi(("Ar putea fi relevant.", "F-0001", "finding", _lab.Ev["other"].EvidenceId));
        _lab.Tamper("other");
        Assert.Equal(StandardState.Rejected, Assert.Single(_lab.Verify().AiStatements).Verdict);
    }

    [Fact]
    public void No_AI_output_means_no_AI_statements()
    {
        _lab.ExecutionWithTwoKinds(); _lab.WriteAll();
        var r = _lab.Verify();
        Assert.Empty(r.AiStatements);
        Assert.Equal("lipsește", r.Inputs["Analysis/ai_reasoning.json"]);
    }

    // ---------------------------------------------------------------- helpers

    private static Finding Clone(Finding f, DateTimeOffset? first, DateTimeOffset? last) => new()
    {
        FindingId = f.FindingId, RuleId = f.RuleId, Title = f.Title, Description = f.Description, Classification = f.Classification, Confidence = f.Confidence,
        FirstSeenUtc = first, LastSeenUtc = last, SemanticType = f.SemanticType, SupportingEvidence = f.SupportingEvidence, File = f.File,
    };
}

internal static class LetExtension
{
    public static TOut Let<TIn, TOut>(this TIn value, Func<TIn, TOut> f) => f(value);
}
