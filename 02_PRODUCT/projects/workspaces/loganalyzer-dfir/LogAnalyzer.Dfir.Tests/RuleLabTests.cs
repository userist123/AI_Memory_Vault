using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.8, rule lab: load as the engine loads, test on a case corpus, diff two versions, add to the case only what loads.</summary>
public sealed class RuleLabTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_rl_{Guid.NewGuid():N}");
    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private static TimelineEvent E(int id, string image) => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 11, 8, 3, 0, id, DateTimeKind.Utc), "", "t"), Source = "EventLog:System", EventId = "7045",
        EvidenceId = "EV-000001", Locator = "EventRecordID=" + id, Summary = "serviciu", Fields = { ["Image"] = image },
    };

    private static readonly TimelineEvent[] Corpus = [E(1, @"C:\Users\Public\updsvc.exe"), E(2, @"C:\Windows\System32\svchost.exe"), E(3, @"C:\Users\Public\tool.exe")];

    private static string Sigma(string contains) => $$"""
        title: serviciu din Public
        id: lab-public-1
        logsource: { product: windows, service: system }
        detection:
          sel:
            Image|contains: '{{contains}}'
          condition: sel
        level: high
        """;

    private static RuleLabRun Test(string text, RuleLabKind kind = RuleLabKind.Sigma, string name = "public_service") =>
        RuleLab.Test(kind, name, text, Corpus, [], _ => []);

    [Fact]
    public void A_valid_rule_is_tested_on_the_corpus_and_two_versions_are_compared_by_match()
    {
        var v1 = Test(Sigma(@"\Users\Public\updsvc"));
        Assert.True(v1.Valid, v1.Summary);
        Assert.Equal("EventRecordID=1", Assert.Single(v1.Matches).Locator);
        var v2 = Test(Sigma(@"\Users\Public\"));
        var d = RuleLab.Diff(v1, v2);
        Assert.Equal(("EventRecordID=3", 0, 1), (Assert.Single(d.Added).Locator, d.Removed.Count, d.Kept));
        Assert.Contains("1 potriviri noi", d.Summary);
    }

    [Fact]
    public void A_rule_the_engine_would_refuse_is_refused_with_the_parser_reason_and_correlations_are_named()
    {
        var bad = Test(Sigma("x").Replace("Image|contains", "Image|base64offset"));
        Assert.False(bad.Valid);
        Assert.Contains("base64offset", bad.Summary);
        var corr = Test("title: c\ncorrelation:\n  type: event_count\n  rules: [a]\n  timespan: 5m\n  condition: { gte: 2 }\n");
        Assert.False(corr.Valid);   // a correlation is now loaded like any rule (no longer refused as a kind); this one names a rule that does not exist
        Assert.Contains("nu există printre regulile Sigma", corr.Summary);
        Assert.Contains("numele regulii", Test(Sigma("x"), name: "../evil").Summary);
    }

    [Fact]
    public void An_ioc_list_is_tested_too()
    {
        var r = Test("Type,Value,Context\nfilename,tool.exe,unealtă lăsată în Public\n", RuleLabKind.Ioc, "public_tools");
        Assert.True(r.Valid, r.Summary);
        Assert.Equal("EventRecordID=3", Assert.Single(r.Matches).Locator);
    }

    [Fact]
    public void Only_a_rule_that_loads_is_added_to_the_case_never_over_an_existing_file_and_it_is_audited_and_in_custody()
    {
        var ws = CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "C-RL", Name = "rl", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "H", Scope = TestScopes.Valid() });
        var text = Sigma(@"\Users\Public\");
        var ok = Test(text);
        Assert.Contains("nu s-a încărcat", RuleLab.AddToCase(ws, RuleLabKind.Sigma, "public_service", "x", Test("nu e yaml: [")).Reason);
        Assert.True(RuleLab.AddToCase(ws, RuleLabKind.Sigma, "public_service", text, ok).Ok);
        Assert.Contains("există deja", RuleLab.AddToCase(ws, RuleLabKind.Sigma, "public_service", text, ok).Reason);
        Assert.True(File.Exists(Path.Combine(ws.Root, "Rules", "sigma", "public_service.yml")));
        Assert.Contains("rule.added_to_case", File.ReadAllText(ws.AppAuditLogPath));
        Assert.Contains("Rules/sigma/public_service.yml", File.ReadAllText(ws.CustodyJsonlPath));
        // the case's next analysis loads it: same folder the pipeline gives the engine
        Assert.Contains(DetectionEngine.Load(Path.Combine(ws.Root, "Rules")).Rules, r => r.RuleId.Contains("lab-public-1", StringComparison.OrdinalIgnoreCase) || r.Title.Contains("Public"));
    }

    [Fact]
    public void A_sigma_field_no_corpus_event_carries_is_named_instead_of_a_bare_zero()
    {
        var r = Test("""
            title: parole
            id: lab-4625
            logsource: { product: windows, service: system }
            detection:
              sel:
                EventID: 4625
                TargetUserName: Administrator
              condition: sel
            """);
        Assert.True(r.Valid, r.Summary);
        Assert.Empty(r.Matches);
        Assert.Contains("TargetUserName", r.Summary);
        Assert.Contains("reanalizați cazul", r.Summary);
        Assert.DoesNotContain("Image", string.Join(" ", Test(Sigma(@"\Users\Public\")).Notes));   // a field the corpus has is not named
    }
}
