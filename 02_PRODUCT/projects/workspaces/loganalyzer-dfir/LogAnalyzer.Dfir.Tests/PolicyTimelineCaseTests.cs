using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP15b on the production path: the pipeline builds the policy timeline from the case timeline, links the expected policy through the procedure profile and records the output in custody.</summary>
public sealed class PolicyTimelineCaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_wp15b_{Guid.NewGuid():N}");
    public PolicyTimelineCaseTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private CaseWorkspace NewCase(string name)
    {
        var sample = Path.Combine(_dir, name + ".bin");
        File.WriteAllText(sample, "MZ not really a program " + name);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases_" + name), name, TestScopes.Valid());
        InvestigationPipeline.Import(ws, [sample]);
        return ws;
    }

    private string PolicyFile()
    {
        var p = Path.Combine(_dir, "audit.lapolicy");
        File.WriteAllText(p, PolicyLifecycleTests.Sample);
        return p;
    }

    [Fact]
    public void Run_without_a_profile_writes_policy_timeline_json_with_the_expected_policy_nedefinit_and_records_it_in_custody()
    {
        var ws = NewCase("a");
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, procedureProfile: new ProcedureProfile());
        Assert.NotNull(r.PolicyTimeline);
        var path = ws.FullPath("Analysis/policy_timeline.json");
        Assert.True(File.Exists(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(PolicyTimeline.SchemaVersion, doc.RootElement.GetProperty("schema_version").GetString());
        Assert.Contains("nedefinit", doc.RootElement.GetProperty("ExpectedNote").GetString());
        var sha = Hashing.Sha256File(path);
        Assert.Contains(File.ReadLines(ws.CustodyJsonlPath), l => l.Contains("Analysis/policy_timeline.json") && l.Contains(sha));
        Assert.Equal(SchemaVersions.PolicyTimeline, SchemaManifest.Read(Path.Combine(ws.Root, "Analysis")).VersionOf("policy_timeline.json"));
    }

    [Fact]
    public void Run_with_an_expected_policy_in_the_profile_evaluates_its_settings_all_UNKNOWN_without_group_policy_evidence()
    {
        var ws = NewCase("b");
        var profile = new ProcedureProfile();
        profile.ExpectedPolicies.Add(new ExpectedPolicyRow { PolicyPath = PolicyFile() });
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, procedureProfile: profile);
        Assert.NotNull(r.PolicyTimeline);
        Assert.Equal(2, r.PolicyTimeline!.Levels.Count);
        Assert.Equal("", r.PolicyTimeline.ExpectedNote);
        var applied = r.PolicyTimeline.Levels[0].Applied;
        Assert.Equal(LevelState.Unknown, applied.State);
        Assert.Empty(r.PolicyTimeline.Gaps);   // UNKNOWN is not a gap
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "POLICY-CONTROL-GAP");
    }

    [Fact]
    public void A_policy_file_that_cannot_be_read_is_reported_not_dropped_silently()
    {
        var ws = NewCase("c");
        var profile = new ProcedureProfile();
        profile.ExpectedPolicies.Add(new ExpectedPolicyRow { PolicyPath = Path.Combine(_dir, "missing.lapolicy") });
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, procedureProfile: profile);
        Assert.Contains("nedefinit", r.PolicyTimeline!.ExpectedNote);
        Assert.Contains("missing.lapolicy", r.PolicyTimeline.ExpectedNote);
    }

    [Fact]
    public void Expected_settings_come_from_the_existing_policy_document_not_a_copy()
    {
        var doc = PolicyLoader.LoadFile(PolicyFile());
        var s = ExpectedSetting.From(doc);
        Assert.Equal(["AUD-01", "REG-01"], s.Select(x => x.Id).ToArray());
        Assert.Equal("audit:Process Creation", s[0].EffectiveKey);
        Assert.Equal("Success", s[0].DesiredValue);
    }

    [Fact]
    public void The_report_line_says_nedefinit_for_an_undefined_policy_timeline_and_names_gaps_when_present()
    {
        var none = new InvestigationResult { Case = NewCase("d") };
        Assert.Contains("nedefinit", none.PolicyTimelineLine);
        var withGap = new InvestigationResult { Case = NewCase("e"), PolicyTimeline = new PolicyTimelineResult { Gaps = [new ControlGap { Title = "t", BrokenLevel = "observed" }] } };
        Assert.Contains("1 decalaje", withGap.PolicyTimelineLine);
        Assert.DoesNotContain("conform", withGap.PolicyTimelineLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_investigation_pdf_renders_the_policy_timeline_section_without_failing_for_an_empty_and_a_full_result()
    {
        var ws = NewCase("pdf");
        var T0 = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var chain = new PolicyChain
        {
            Change = new PolicyChange { ChangeId = "PC-001", TimeUtc = T0, Kind = "DS_MODIFIED", EventId = "5136", Who = "CORP\\a", TargetKind = "GPO", TargetId = "{G}", Attribute = "versionNumber", OldValue = "5", NewValue = "6" },
            Behavior = BehaviorState.NotEvaluable, BehaviorReason = "SYSVOL",
        };
        var r = new InvestigationResult { Case = ws, PolicyTimeline = new PolicyTimelineResult { Chains = [chain], Gaps = [new ControlGap { Title = "t", BrokenLevel = "observed", Reason = "r" }] } };
        var pdf = Path.Combine(_dir, "pt.pdf");
        InvestigationReportPdf.Write(r, pdf, "test");
        Assert.True(new FileInfo(pdf).Length > 1000);
        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, Path.Combine(_dir, "empty.pdf"), "test");
        Assert.True(File.Exists(Path.Combine(_dir, "empty.pdf")));
    }
}
