using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Stage 2 WP2: finding contract, standard states, anti-overclaim invariants, versioned outputs, raw time in the timeline.</summary>
public sealed class FindingContractTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_fc_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 9, 19, 15, 0, 0, DateTimeKind.Utc);

    public FindingContractTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static Finding F(string rule, Classification c = Classification.Direct, Severity s = Severity.High, Confidence conf = Confidence.High) => new()
    {
        FindingId = "F-1", RuleId = rule, Title = "t", Description = "d", Severity = s, Classification = c, Confidence = conf,
        ClassificationReason = "r", SupportingEvidence = [new EvidenceRef("EV-1", "L", "x", new string('a', 64))],
    };

    private static TimelineEvent Ev(string source, string path, string process = "a.exe", Action<TimelineEvent>? more = null, string task = "", string service = "")
    {
        var e = new TimelineEvent
        {
            Time = Timestamp.FromUtc(T0, "", "test"), Source = source, EvidenceId = "EV-1", Locator = "L", Summary = "s", Path = path, Process = process, Task = task, Service = service,
            Fields = { ["PrefetchHash"] = "H" + path, ["ReferencedFiles"] = path, ["RunCount"] = "3" },
        };
        more?.Invoke(e);
        return e;
    }

    // ---- 1. Classification -> standard state: one test per mapping ----

    [Theory]
    [InlineData(Classification.Direct, StandardState.Observed)]
    [InlineData(Classification.Correlated, StandardState.Correlated)]
    [InlineData(Classification.Candidate, StandardState.Inferred)]
    [InlineData(Classification.Unproven, StandardState.Unproven)]
    [InlineData(Classification.BenignKnown, StandardState.Observed)]
    public void Classification_maps_to_a_standard_state_beside_it(Classification c, StandardState expected)
    {
        var f = FindingContract.Enrich([F("DEF-DETECTION", c)]).Single();
        Assert.Equal(expected, f.Status);
        Assert.Equal(c, f.Classification);   // never replaced
    }

    [Fact]
    public void Mapping_never_produces_Supported_Verified_Contradicted_or_Rejected()
    {
        foreach (var c in Enum.GetValues<Classification>())
            Assert.DoesNotContain(FindingContract.StatusFor(c), new[] { StandardState.Supported, StandardState.Verified, StandardState.Contradicted, StandardState.Rejected });
    }

    [Fact]
    public void Unenriched_finding_has_honest_defaults()
    {
        var f = new Finding { FindingId = "x", RuleId = "R", Title = "t", Description = "d" };
        Assert.Equal(StandardState.NotAssessed, f.Status);
        Assert.Equal(StandardState.NotAssessed, f.Verification.State);
        Assert.Equal("", f.ContractVersion);
        Assert.Equal(SemanticType.Observation, f.SemanticType);
        Assert.False(f.HasSemanticType);
    }

    // ---- 2. Every rule: contract fields populated ----

    [Fact]
    public void Every_catalog_rule_yields_a_complete_contract_with_verification_not_assessed()
    {
        foreach (var rule in RuleContracts.All)
        {
            var f = FindingContract.Enrich([F(rule.RuleId)], new FindingContractContext { NowUtc = T0 }).Single();
            Assert.Equal("1.0", f.ContractVersion);
            Assert.NotEmpty(f.MissingEvidence);
            Assert.NotEmpty(f.RecommendedNextSteps);
            Assert.Equal($"finding.{rule.RuleId.ToLowerInvariant().Replace('-', '_')}.title", f.TitleKey);
            Assert.EndsWith(".summary", f.SummaryKey);
            Assert.Equal(StandardState.NotAssessed, f.Verification.State);
            Assert.NotEmpty(f.Verification.Reason);
            Assert.Contains(f.Limitations, l => l.StartsWith("Verificare independentă", StringComparison.Ordinal));
            Assert.Equal(rule.RuleId, f.Provenance!.RuleId);
            Assert.Equal(["EV-1"], f.Provenance.EvidenceIds);
            Assert.Equal([new string('a', 64)], f.Provenance.EvidenceSha256);
            Assert.Contains(f.AuditTrail, a => a.Step == "classification.mapped");
            Assert.Contains(f.AuditTrail, a => a.Step == "anti_overclaim.check" && a.Detail == "fără încălcări");
            Assert.NotEmpty(f.HumanSummary);
            Assert.Contains(f.RuleId, f.TechnicalSummary);
            // A rule without a contradiction test must say so; a rule with one must say what it tests.
            if (rule.ContradictionCheck is null) Assert.Contains(f.Limitations, l => l.StartsWith("Dovezi contradictorii: regula nu le evaluează", StringComparison.Ordinal));
            else Assert.DoesNotContain(f.Limitations, l => l.StartsWith("Dovezi contradictorii", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Unknown_rule_is_flagged_not_silently_defaulted()
    {
        var f = FindingContract.Enrich([F("NOT-IN-CATALOG")]).Single();
        Assert.Contains(f.Limitations, l => l.Contains("nu are intrare în catalogul contractului"));
        Assert.Equal("unknown", f.Provenance!.Producer);
    }

    [Fact]
    public void Producer_supplied_missing_evidence_and_steps_are_kept()
    {
        var f = F("DEF-DETECTION");
        f.MissingEvidence.Add("specific");
        f.RecommendedNextSteps.Add("pas specific");
        FindingContract.Enrich([f]);
        Assert.Equal(["specific"], f.MissingEvidence);
        Assert.Equal(["pas specific"], f.RecommendedNextSteps);
    }

    [Fact]
    public void Enrich_is_idempotent()
    {
        var f = F("EXEC-USERPATH");
        FindingContract.Enrich([f]);
        var lim = f.Limitations.Count; var audit = f.AuditTrail.Count;
        FindingContract.Enrich([f]);
        Assert.Equal(lim, f.Limitations.Count);
        Assert.Equal(audit, f.AuditTrail.Count);
    }

    [Fact]
    public void Parser_limits_are_attached_to_the_findings_that_rest_on_the_parser()
    {
        var f = FindingContract.Enrich([F("EXEC-USERPATH")], new FindingContractContext { ParsersOf = _ => ["AmcacheParser"] }).Single();
        Assert.Contains(f.Limitations, l => l == "AmcacheParser nu poate dovedi: Că fișierul a fost executat.");
        Assert.Equal(["AmcacheParser"], f.Provenance!.Parsers);
    }

    [Fact]
    public void Every_registered_parser_exposes_what_it_can_and_cannot_prove()
    {
        foreach (var d in WindowsParsers.Registry.Descriptors)
        {
            var c = d.Capabilities;
            Assert.True(c is not null, $"{d.ParserId} has no capabilities");
            Assert.NotEmpty(c!.CanProve); Assert.NotEmpty(c.CannotProve); Assert.NotEmpty(c.Limitations); Assert.NotEmpty(c.CorrelationSources);
        }
    }

    // ---- 3. Contradictions computed by the rules ----

    [Fact]
    public void Disabled_task_and_disabled_service_are_recorded_as_contradicting_evidence()
    {
        var events = new List<TimelineEvent>
        {
            Ev("ScheduledTask", @"C:\Users\u\AppData\Roaming\x\run.exe", "run.exe", e => { e.Fields["Enabled"] = "false"; e.Fields["Hidden"] = "false"; }, task: @"\Upd"),
            Ev("ScheduledTask", @"C:\Users\u\AppData\Roaming\y\run2.exe", "run2.exe", e => { e.Fields["Enabled"] = "true"; e.Fields["Hidden"] = "false"; }, task: @"\Upd2"),
            Ev("Service", @"C:\ProgramData\svc\s.exe", "s.exe", e => { e.Fields["StartMode"] = "Disabled"; }, service: "Svc"),
        };
        var found = Correlation.Run(events);
        var task = Assert.Single(found, f => f.RuleId == "PERSIST-TASK-CONFIG" && f.Title.Contains(@"\Upd") && !f.Title.Contains("Upd2"));
        Assert.Single(task.ContradictingEvidence);
        Assert.Same(task.ContradictingEvidence, task.Contradictions);
        Assert.Empty(Assert.Single(found, f => f.Title.Contains("Upd2")).ContradictingEvidence);
        Assert.Single(Assert.Single(found, f => f.RuleId == "PERSIST-SERVICE-CONFIG").ContradictingEvidence);

        FindingContract.Enrich(found);
        Assert.Contains(task.AuditTrail, a => a.Step == "contradiction.check" && a.Detail.EndsWith(": 1 găsite"));
        Assert.Equal(task.Status, FindingContract.StatusFor(task.Classification));   // a qualified finding is not CONTRADICTED by mapping
    }

    [Fact]
    public void An_incident_chain_inherits_the_contradictions_of_its_steps()
    {
        var step = F("PERSIST-TASK-CONFIG"); step.ContradictingEvidence.Add("taskul este dezactivat");
        var other = new Finding { FindingId = "F-2", RuleId = "DEF-DETECTION", Title = "t", Description = "d", Classification = Classification.Direct, SupportingEvidence = step.SupportingEvidence };
        var chain = new Finding { FindingId = "F-3", RuleId = "INCIDENT-CHAIN", Title = "t", Description = "d", Classification = Classification.Correlated, RelatedFindingIds = ["F-1", "F-2"], SupportingEvidence = step.SupportingEvidence };
        FindingContract.Enrich([step, other, chain]);
        Assert.Equal(["F-1: taskul este dezactivat"], chain.ContradictingEvidence);
        Assert.Equal(SemanticType.Correlation, chain.SemanticType);
    }

    // ---- 4. Semantic types: presence never becomes execution ----

    [Fact]
    public void Amcache_only_presence_is_never_labelled_as_execution_and_BAM_or_Prefetch_is()
    {
        var presence = Correlation.Run([Ev("Amcache", @"C:\Users\u\Downloads\a.exe")]).Single(f => f.RuleId == "EXEC-USERPATH");
        Assert.Equal(SemanticType.Presence, presence.SemanticType);
        Assert.DoesNotContain("au rulat din", presence.Title);
        FindingContract.Enrich([presence]);
        Assert.Equal(SemanticType.Presence, presence.SemanticType);
        Assert.Contains(presence.Limitations, l => l.Contains("nu dovedește execuția"));

        var executed = Correlation.Run([Ev("BAM", @"C:\Users\u\Downloads\b.exe", "b.exe"), Ev("Prefetch", @"C:\Users\u\Downloads\c.exe", "c.exe")]).Single(f => f.RuleId == "EXEC-USERPATH");
        Assert.Equal(SemanticType.Execution, executed.SemanticType);

        // A mix is as weak as its weakest member.
        var mixed = Correlation.Run([Ev("BAM", @"C:\Users\u\Downloads\b.exe", "b.exe"), Ev("Amcache", @"C:\Users\u\Downloads\a.exe")]).Single(f => f.RuleId == "EXEC-USERPATH");
        Assert.Equal(SemanticType.Presence, mixed.SemanticType);
    }

    [Theory]
    [InlineData("Prefetch", SemanticType.Execution)]
    [InlineData("BAM", SemanticType.Execution)]
    [InlineData("UserAssist", SemanticType.Execution)]
    [InlineData("Amcache", SemanticType.Presence)]
    [InlineData("ShimCache", SemanticType.Presence)]
    [InlineData("LNK", SemanticType.Presence)]
    [InlineData("JumpList", SemanticType.Presence)]
    [InlineData("Service", SemanticType.Configuration)]
    [InlineData("ScheduledTask", SemanticType.Configuration)]
    [InlineData("RunKey", SemanticType.Configuration)]
    [InlineData("EventLog:Security", SemanticType.Observation)]
    [InlineData("SRUM", SemanticType.Observation)]
    [InlineData("USB", SemanticType.Observation)]
    [InlineData("USN", SemanticType.Observation)]
    public void Timeline_sources_map_to_a_semantic_type(string source, SemanticType expected) =>
        Assert.Equal(expected, TimeFacts.SemanticOf(source, "recorded"));

    [Fact]
    public void A_row_whose_time_semantics_say_not_execution_is_never_execution() =>
        Assert.Equal(SemanticType.Presence, TimeFacts.SemanticOf("BAM", "file last modified (ShimCache) — not execution"));

    [Fact]
    public void Correlation_findings_carry_the_non_causation_limit_and_are_never_supported_or_verified()
    {
        foreach (var rule in new[] { "DOWNLOAD-THEN-EXEC", "EXEC-USERPATH-CORRELATED", "INCIDENT-CHAIN", "NET-LOLBIN-TRAFFIC", "NET-USERPATH-UPLOAD" })
        {
            var f = FindingContract.Enrich([F(rule, Classification.Correlated)]).Single();
            Assert.Equal(SemanticType.Correlation, f.SemanticType);
            Assert.Contains(f.Limitations, l => l.Contains("nu dovedește cauzalitate"));
            Assert.Equal(StandardState.Correlated, f.Status);
            Assert.Empty(AntiOverclaim.Violations(f));
        }
        // Single-source network volume is an inference, not a correlation.
        Assert.Equal(SemanticType.Inference, FindingContract.Enrich([F("NET-LOLBIN-TRAFFIC", Classification.Candidate)]).Single().SemanticType);
    }

    // ---- 5. Severity and Confidence stay separate (lessons learned §99) ----

    [Theory]
    [InlineData(Severity.High, Confidence.Low)]
    [InlineData(Severity.Info, Confidence.High)]
    [InlineData(Severity.Critical, Confidence.Medium)]
    public void Severity_and_confidence_are_independent_and_untouched(Severity s, Confidence c)
    {
        var f = FindingContract.Enrich([F("DEF-DETECTION", Classification.Direct, s, c)]).Single();
        Assert.Equal(s, f.Severity);
        Assert.Equal(c, f.Confidence);
        foreach (var other in Enum.GetValues<Confidence>())
            Assert.Equal(f.Status, FindingContract.Enrich([F("DEF-DETECTION", Classification.Direct, s, other)]).Single().Status);
        foreach (var other in Enum.GetValues<Severity>())
            Assert.Equal(f.Status, FindingContract.Enrich([F("DEF-DETECTION", Classification.Direct, other, c)]).Single().Status);
    }

    // ---- 6. Anti-overclaim invariants (lessons learned §100) ----

    [Theory]
    [InlineData("Amcache")] [InlineData("ShimCache")] [InlineData("LNK")] [InlineData("JumpList")] [InlineData("USB")] [InlineData("USN")] [InlineData("SRUM")] [InlineData("BrowserDownload")]
    public void Artifact_or_presence_never_maps_to_execution(string artifact) =>
        Assert.NotEqual(SemanticType.Execution, AntiOverclaim.SemanticForArtifact(artifact));

    [Fact]
    public void Correlation_and_inference_never_reach_proof_and_nothing_maps_to_verified()
    {
        foreach (var t in Enum.GetValues<SemanticType>())
            foreach (var s in Enum.GetValues<StandardState>())
            {
                var r = AntiOverclaim.Constrain(s, t);
                Assert.NotEqual(StandardState.Verified, r);
                if (t == SemanticType.Correlation) Assert.NotEqual(StandardState.Supported, r);
                if (t is SemanticType.Inference or SemanticType.Attribution) Assert.DoesNotContain(r, new[] { StandardState.Observed, StandardState.Supported, StandardState.Correlated });
            }
    }

    [Fact]
    public void Ai_output_is_never_verified_or_supported()
    {
        foreach (var s in Enum.GetValues<StandardState>())
            Assert.DoesNotContain(AntiOverclaim.Constrain(s, SemanticType.Observation, fromAi: true), new[] { StandardState.Verified, StandardState.Supported, StandardState.Observed, StandardState.Correlated });
    }

    [Fact]
    public void Missing_evidence_never_becomes_a_clean_result()
    {
        Assert.Equal(StandardState.Unknown, AntiOverclaim.NoFindingsConclusion([new EvidenceGap("Security.evtx", EvidenceStatus.NotAvailable, "r", "i", "a", "r")]));
        Assert.Equal(StandardState.Unknown, AntiOverclaim.NoFindingsConclusion([]));   // no detection is not no compromise
    }

    [Fact]
    public void A_configured_policy_is_never_effective_without_separate_proof()
    {
        Assert.Equal(StandardState.NotAssessed, AntiOverclaim.PolicyEffectiveness(configured: true, provenEffective: false));
        Assert.Equal(StandardState.Unknown, AntiOverclaim.PolicyEffectiveness(configured: false, provenEffective: false));
        Assert.Equal(StandardState.Supported, AntiOverclaim.PolicyEffectiveness(configured: true, provenEffective: true));
    }

    [Fact]
    public void Violations_flag_verified_without_a_verifier_correlation_as_proof_and_attribution_without_evidence()
    {
        var f = F("DEF-DETECTION"); f.Status = StandardState.Verified;
        Assert.Contains("VERIFIED fără verificator", AntiOverclaim.Violations(f));
        var c = F("INCIDENT-CHAIN"); c.SemanticType = SemanticType.Correlation; c.Status = StandardState.Supported;
        Assert.Contains("corelația tratată ca dovadă", AntiOverclaim.Violations(c));
        var a = F("X"); a.SemanticType = SemanticType.Attribution; a.Status = StandardState.Observed;
        Assert.Contains("atribuire fără dovezi separate", AntiOverclaim.Violations(a));
    }

    // ---- 7. Labels and vocabularies ----

    [Fact]
    public void Romanian_labels_cover_the_ten_states_exactly()
    {
        var expected = new Dictionary<StandardState, string>
        {
            [StandardState.Observed] = "Observat", [StandardState.Correlated] = "Corelat", [StandardState.Supported] = "Susținut de dovezi",
            [StandardState.Verified] = "Verificat", [StandardState.Inferred] = "Deducție", [StandardState.Unproven] = "Nedemonstrat",
            [StandardState.Contradicted] = "Contrazis", [StandardState.Rejected] = "Respins", [StandardState.Unknown] = "Necunoscut",
            [StandardState.NotAssessed] = "Neevaluat",
        };
        Assert.Equal(10, Enum.GetValues<StandardState>().Length);
        Assert.Equal(expected, StateLabels.RomanianTable.ToDictionary(k => k.Key, k => k.Value));
        Assert.Equal(7, StateLabels.RomanianOperation.Count);
    }

    [Fact]
    public void Spec_names_of_the_contract_enums_are_the_documented_ones()
    {
        Assert.Equal(["OBSERVED", "CORRELATED", "SUPPORTED", "VERIFIED", "INFERRED", "UNPROVEN", "CONTRADICTED", "REJECTED", "UNKNOWN", "NOT_ASSESSED"],
            Enum.GetValues<StandardState>().Select(s => s.ToSpec()));
        Assert.Equal(["OBSERVATION", "PRESENCE", "EXECUTION", "CONFIGURATION", "CORRELATION", "INFERENCE", "ATTRIBUTION"], Enum.GetValues<SemanticType>().Select(s => s.ToSpec()));
        Assert.Equal(["NOT_STARTED", "RUNNING", "COMPLETED", "PARTIAL", "FAILED", "CANCELLED", "BLOCKED"], Enum.GetValues<OperationState>().Select(s => s.ToSpec()));
        Assert.Equal(["AVAILABLE", "COLLECTED", "PARSED", "PARTIAL", "UNSUPPORTED", "INVALID", "ERROR", "BLOCKED", "NOT_PRESENT", "NOT_ENABLED"], Enum.GetValues<ParserHealth>().Select(s => s.ToSpec()));
        Assert.Equal(["AVAILABLE", "NOT_AVAILABLE", "NOT_ENABLED", "NOT_APPLICABLE", "NOT_COLLECTED"], Enum.GetValues<SourceAvailability>().Select(s => s.ToSpec()));
    }

    [Fact]
    public void Contract_enums_serialize_as_spec_names_and_read_back_including_legacy_numbers()
    {
        var f = FindingContract.Enrich([F("DEF-DETECTION")]).Single();
        var json = JsonSerializer.Serialize(f);
        Assert.Contains("\"Status\":\"OBSERVED\"", json);
        Assert.Contains("\"State\":\"NOT_ASSESSED\"", json);
        var back = JsonSerializer.Deserialize<Finding>(json)!;
        Assert.Equal(f.Status, back.Status); Assert.Equal(f.SemanticType, back.SemanticType); Assert.Equal(f.Provenance!.RuleId, back.Provenance!.RuleId);
        Assert.Equal(StandardState.Verified, JsonSerializer.Deserialize<StandardState>("3"));
    }

    // ---- 8. Old vocabularies -> new ----

    [Theory]
    [InlineData("ExecutionProven", "Prefetch", SemanticType.Execution, StandardState.Observed)]
    [InlineData("ExecutionProven", "BAM", SemanticType.Execution, StandardState.Observed)]
    [InlineData("ExecutionProven", "Amcache", SemanticType.Presence, StandardState.Observed)]
    [InlineData("ExecutionProven", "", SemanticType.Presence, StandardState.Observed)]
    [InlineData("ExecutionPossible", "ShimCache", SemanticType.Inference, StandardState.Inferred)]
    [InlineData("FileExistenceOnly", "MFT", SemanticType.Presence, StandardState.Observed)]
    [InlineData("ConfigurationOnly", "Service", SemanticType.Configuration, StandardState.Observed)]
    [InlineData("ContextOnly", "", SemanticType.Observation, StandardState.Observed)]
    [InlineData("SomethingNew", "", SemanticType.Observation, StandardState.NotAssessed)]
    public void Legacy_evidence_strength_maps_conservatively(string strength, string artifact, SemanticType t, StandardState s) =>
        Assert.Equal((t, s), LegacyVocabulary.FromEvidenceStrength(strength, artifact));

    // ---- 9. Operation state ----

    [Fact]
    public void Operation_state_is_computed_from_what_happened()
    {
        ParseResult P(EvidenceStatus st, string err = "") => new() { EvidenceId = "E", Parser = "p", ParserVersion = "1", Status = st, Error = err };
        Assert.Equal(OperationState.Completed, OperationStates.Summarize([EvidenceStatus.Success], [P(EvidenceStatus.Success), P(EvidenceStatus.SkippedByDesign)]).State);
        Assert.Equal(OperationState.Partial, OperationStates.Summarize([EvidenceStatus.Success, EvidenceStatus.Failed], [P(EvidenceStatus.Success)]).State);
        Assert.Equal(OperationState.Partial, OperationStates.Summarize([], [P(EvidenceStatus.Success), P(EvidenceStatus.Partial)]).State);
        Assert.Equal(OperationState.Failed, OperationStates.Summarize([], [P(EvidenceStatus.Failed, "boom")]).State);
        Assert.Equal(OperationState.Failed, OperationStates.Summarize([EvidenceStatus.Failed, EvidenceStatus.NotAvailable], []).State);
        Assert.Equal(OperationState.Blocked, OperationStates.Summarize([], [P(EvidenceStatus.Failed, "EVIDENCE_MUTATED x")]).State);
    }

    // ---- 10. Real pipeline: raw time, versioned outputs, contract on every finding, run state ----

    private const string DisabledTask = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Date>2026-09-19T17:58:12.5+03:00</Date>
            <Author>MARIUS-PC\u</Author>
            <URI>\orchestratormaintain</URI>
          </RegistrationInfo>
          <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>S-1-5-21-1-2-3-1001</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings><Hidden>true</Hidden><Enabled>false</Enabled></Settings>
          <Actions Context="Author"><Exec><Command>C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe</Command><Arguments>-run silent</Arguments></Exec></Actions>
        </Task>
        """;

    private string TaskFile()
    {
        var path = Path.Combine(_dir, "orchestratormaintain");
        File.WriteAllText(path, DisabledTask, System.Text.Encoding.Unicode);
        return path;
    }

    private (CaseWorkspace ws, InvestigationResult r) Run()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "contract", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [TaskFile()]);
        return (ws, new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false));
    }

    [Fact]
    public void Timeline_csv_exports_the_raw_source_time_beside_the_normalized_one()
    {
        var (_, r) = Run();
        var lines = File.ReadAllLines(r.TimelineCsv);
        var header = lines[0].TrimStart('\uFEFF').Split(',');
        foreach (var col in new[] { "TimeUtc", "TimeRaw", "TimeConversion", "TimeZoneBasis", "TimeUncertainty", "SemanticType" }) Assert.Contains(col, header);
        var e = r.Timeline.First(x => x.Source == "ScheduledTask");
        Assert.Equal("2026-09-19T17:58:12.5+03:00", e.Time.Raw);   // the source's own text, offset included
        Assert.Equal("2026-09-19T14:58:12.500Z", e.Time.UtcIso);   // normalized
        Assert.StartsWith("offset stated by the source", e.TimeZoneBasis);
        Assert.Contains("falsificată", e.TimeUncertainty);          // author-supplied date
        Assert.Equal(SemanticType.Configuration, e.SemanticType);
        Assert.Contains(lines.Skip(1), l => l.Contains("2026-09-19T17:58:12.5+03:00") && l.Contains("ISO 8601 with offset") && l.Contains("CONFIGURATION"));
    }

    [Fact]
    public void Every_pipeline_finding_has_the_contract_and_outputs_are_versioned()
    {
        var (ws, r) = Run();
        var task = Assert.Single(r.Findings, f => f.RuleId == "PERSIST-TASK-CONFIG");
        // WP4: every finding carries exactly the verdict the verification module gave it (no finding is left with the WP2 default).
        var verdicts = r.Verification!.ToContract();
        Assert.All(r.Findings, f =>
        {
            Assert.Equal("1.0", f.ContractVersion);
            Assert.True(verdicts.ContainsKey(f.FindingId), $"no verdict for {f.FindingId}");
            Assert.Equal(verdicts[f.FindingId].State, f.Verification.State);
            Assert.NotEmpty(f.Limitations); Assert.NotEmpty(f.MissingEvidence); Assert.NotEmpty(f.RecommendedNextSteps);
            Assert.NotEmpty(f.Provenance!.EvidenceSha256);
            Assert.Empty(AntiOverclaim.Violations(f));
        });
        Assert.Equal(SemanticType.Configuration, task.SemanticType);
        Assert.Equal(StandardState.Observed, task.Status);
        Assert.Single(task.ContradictingEvidence);   // the task is disabled: computed from the parsed XML, not assumed
        Assert.Contains("ScheduledTaskParser", task.Provenance!.Parsers);
        Assert.Contains(task.Limitations, l => l.StartsWith("ScheduledTaskParser nu poate dovedi: Că taskul a rulat"));

        var analysis = Path.Combine(ws.Root, "Analysis");
        Assert.Equal(SchemaVersions.Findings, SchemaVersions.ReadVersion(r.FindingsJson));
        Assert.Equal(SchemaVersions.Graph, SchemaVersions.ReadVersion(Path.Combine(analysis, "graph.json")));
        var file = FindingsFile.Read(r.FindingsJson);
        Assert.Equal(r.Findings.Count, file.Findings.Count);
        var back = file.Findings.First(f => f.RuleId == "PERSIST-TASK-CONFIG");
        Assert.Equal(StandardState.Observed, back.Status);
        Assert.Equal(task.Limitations, back.Limitations);
        var manifest = SchemaManifest.Read(analysis);
        Assert.Equal("2.0", manifest.VersionOf("findings.json"));
        Assert.Equal("1.1", manifest.VersionOf("parsers.json"));
        // parsers.json stays a bare array so existing consumers keep working, and now carries the capabilities.
        var inv = JsonDocument.Parse(File.ReadAllText(Path.Combine(analysis, "parsers.json"))).RootElement;
        Assert.Equal(JsonValueKind.Array, inv.ValueKind);
        Assert.All(inv.EnumerateArray(), d => Assert.NotEqual(JsonValueKind.Null, d.GetProperty("Capabilities").ValueKind));
        foreach (var line in File.ReadLines(Path.Combine(ws.Root, "Exports", "vault_proposals.jsonl")))
            Assert.Equal(SchemaVersions.VaultProposals, VaultExport.SchemaVersionOf(JsonSerializer.Deserialize<VaultProposal>(line)!));
    }

    [Fact]
    public void Vault_proposal_for_a_finding_carries_semantic_type_status_and_the_verification_verdict()
    {
        var (_, r) = Run();
        var p = VaultExport.Read(Path.Combine(r.Case.Root, "Exports", "vault_proposals.jsonl")).First(x => x.Title.Contains("PERSIST-TASK-CONFIG"));
        Assert.Contains("\"semantic_type\": \"CONFIGURATION\"", p.Body);
        Assert.Contains("\"status\": \"OBSERVED\"", p.Body);
        // WP4: the verdict of the verification module travels with the proposal (a single-source configuration claim is SUPPORTED, not VERIFIED).
        Assert.Equal(StandardState.Supported, r.Verification!.Findings.First(f => f.RuleId == "PERSIST-TASK-CONFIG").Verdict);
        Assert.Contains("\"state\": \"SUPPORTED\"", p.Body);
        Assert.DoesNotContain("NEVERIFICAT", p.Body);
        Assert.Contains("Limitări:", p.Body);
        Assert.Contains("Contradicții:", p.Body);
    }

    [Fact]
    public void Run_state_is_completed_for_a_clean_run_and_cancelled_for_a_cancelled_one()
    {
        var (ws, r) = Run();
        Assert.Equal(OperationState.Completed, r.State);
        var rs = JsonDocument.Parse(File.ReadAllText(Path.Combine(ws.Root, "Analysis", "run_state.json"))).RootElement;
        Assert.Equal("COMPLETED", rs.GetProperty("State").GetString());

        var ws2 = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases2"), "cancel", TestScopes.Valid());
        InvestigationPipeline.Import(ws2, [TaskFile()]);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new InvestigationPipeline().Run(ws2, CollectionProfile.Quick, false, null, cts.Token));
        var rs2 = JsonDocument.Parse(File.ReadAllText(Path.Combine(ws2.Root, "Analysis", "run_state.json"))).RootElement;
        Assert.Equal("CANCELLED", rs2.GetProperty("State").GetString());
    }

    // ---- 12. Unreliable time lowers confidence, never severity (lessons learned §84); graph vocabulary (§94) ----

    private static Finding TimedFinding(string loc, DateTime t) => new()
    {
        FindingId = "F-9", RuleId = "DEF-DETECTION", Title = "t", Description = "d", Severity = Severity.High, Confidence = Confidence.High,
        FirstSeenUtc = new DateTimeOffset(t, TimeSpan.Zero), LastSeenUtc = new DateTimeOffset(t, TimeSpan.Zero),
        SupportingEvidence = [new EvidenceRef("EV-1", loc, "x")],
    };

    [Fact]
    public void A_row_with_an_ambiguous_or_missing_time_lowers_confidence_one_level_and_leaves_severity()
    {
        var ambiguous = new TimelineEvent { Time = Timestamp.FromUtc(T0, "raw", "fsutil, ora locală convertită cu X (oră ambiguă la schimbarea orei)"), Source = "USN", EvidenceId = "EV-1", Locator = "amb", Summary = "s" };
        TimeFacts.Annotate(ambiguous);
        var unknown = new TimelineEvent { Time = Timestamp.Unknown("0"), Source = "USN", EvidenceId = "EV-1", Locator = "unk", Summary = "s" };
        TimeFacts.Annotate(unknown);
        var fine = new TimelineEvent { Time = Timestamp.FromUtc(T0, "", "prefetch FILETIME"), Source = "Prefetch", EvidenceId = "EV-1", Locator = "ok", Summary = "s" };
        TimeFacts.Annotate(fine);
        var list = new List<Finding> { TimedFinding("amb", T0), TimedFinding("unk", T0), TimedFinding("ok", T0) };
        Assert.Equal(2, TimeReliability.Apply(list, [ambiguous, unknown, fine]));
        Assert.Equal([Confidence.Medium, Confidence.Medium, Confidence.High], list.Select(f => f.Confidence));
        Assert.All(list, f => Assert.Equal(Severity.High, f.Severity));
        Assert.Contains(list[0].AuditTrail, a => a.Step == "confidence.lowered");
        Assert.Equal(0, TimeReliability.Apply(list, [ambiguous, unknown, fine]));   // idempotent
        list[0].Confidence = Confidence.Low;
        Assert.Equal(Confidence.Low, list[0].Confidence);
    }

    [Fact]
    public void A_clock_change_near_the_finding_lowers_confidence_but_a_distant_one_does_not()
    {
        TimelineEvent Clock(DateTime t) => new() { Time = Timestamp.FromUtc(t, "", "EVTX SystemTime"), Source = "EventLog:Security", EventId = "4616", EvidenceId = "EV-2", Locator = "c", Summary = "time changed" };
        var near = TimedFinding("none", T0);
        var far = TimedFinding("none", T0);
        TimeReliability.Apply([near], [Clock(T0.AddHours(3))]);
        TimeReliability.Apply([far], [Clock(T0.AddDays(30))]);
        Assert.Equal(Confidence.Medium, near.Confidence);
        Assert.Equal(Confidence.High, far.Confidence);
        Assert.Contains(near.Limitations, l => l.Contains("ceasul sistemului"));
    }

    [Fact]
    public void Graph_relation_vocabulary_includes_the_lessons_learned_set()
    {
        var names = Enum.GetNames<LogAnalyzer.Dfir.Graph.RelationType>();
        foreach (var n in new[] { "Accessed", "Created", "Modified", "Copied", "Deleted", "Executed", "ConnectedTo", "Authenticated", "Changed", "Transmitted", "Printed", "Mounted", "Unmounted" })
            Assert.Contains(n, names);
    }

    // ---- 11. Readers accept legacy files ----

    private const string LegacyFindingsJson = """
        {"Findings":[{"FindingId":"F-0001","RuleId":"DEF-DETECTION","Title":"t","Severity":3,"Category":"Execution","Classification":0,"Confidence":2,
        "FirstSeenUtc":null,"LastSeenUtc":null,"Host":"","User":"","Process":"","Pid":null,"File":"","Ip":"","Domain":"","Description":"d",
        "SupportingEvidence":[{"EvidenceId":"EV-1","Locator":"L","Description":"x","Sha256":""}],"RelatedFindingIds":[],"ContradictingEvidence":[],
        "AlternativeExplanations":[],"MissingEvidence":[],"RecommendedNextSteps":[],"MitreTechniqueId":"","ClassificationReason":"r"}],
        "Gaps":[],"Collection":[],"RejectedFindings":[]}
        """;

    [Fact]
    public void Legacy_findings_json_without_schema_version_is_read_with_honest_contract_defaults()
    {
        var p = Path.Combine(_dir, "findings.json");
        File.WriteAllText(p, LegacyFindingsJson);
        Assert.Equal("1.0", SchemaVersions.ReadVersion(p));
        var file = FindingsFile.Read(p);
        Assert.Equal("1.0", file.SchemaVersion);
        var f = Assert.Single(file.Findings);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Equal(StandardState.NotAssessed, f.Status);
        Assert.Equal("", f.ContractVersion);
        Assert.Empty(f.Limitations);
    }

    [Fact]
    public void A_newer_major_version_is_refused_not_guessed()
    {
        var p = Path.Combine(_dir, "findings.json");
        File.WriteAllText(p, "{\"schema_version\":\"3.0\",\"Findings\":[]}");
        Assert.Throws<InvalidDataException>(() => FindingsFile.Read(p));
        Assert.Equal("2.5", SchemaVersions.Accept("2.5", "x"));   // same major, newer minor: accepted
    }

    [Fact]
    public void Legacy_vault_proposals_and_a_case_without_manifest_are_read()
    {
        var legacy = new VaultProposal("t", "Date.\n\n```json\n{\n  \"object_kind\": \"Finding\"\n}\n```\n", "experience", new Dictionary<string, string> { ["source_type"] = "execution", ["source_ref"] = "r" });
        var file = Path.Combine(_dir, "vault_proposals.jsonl");
        File.WriteAllText(file, JsonSerializer.Serialize(legacy) + "\n");
        var read = Assert.Single(VaultExport.Read(file));
        Assert.Equal("1.0", VaultExport.SchemaVersionOf(read));
        var m = SchemaManifest.Read(_dir);   // no manifest in this folder
        Assert.Equal("1.0", m.VersionOf("findings.json"));
    }
}
