using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Compliance;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public sealed class ComplianceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_comp_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private const string PolicyYaml = """
        policy: { id: LA-BASE, version: '2.0', title: Bază, author: autor }
        controls:
          - id: R1
            title: Ecran blocat
            detection: { type: registry, hive: HKLM, key: 'SOFTWARE\Policies\X', value: Lock, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
          - id: R2
            title: Mesaj legal
            detection: { type: registry, hive: HKLM, key: 'SOFTWARE\Policies\X', value: Caption, value_type: string }
            desired_state: { equals: 'Aviz' }
            remediation: set
            verification: reread
          - id: A1
            title: Audit creare procese
            detection: { type: audit, name: Process Creation }
            desired_state: Success
            remediation: manual
            verification: reread
        """;

    private const string Mapping = """
        benchmark: { name: Benchmark intern, version: '1.0', source: 'document intern al proprietarului' }
        controls:
          - id: '1.1'
            title: Blocare ecran
            requirement: Ecranul se blochează.
            policy_controls: [R1]
          - id: '1.2'
            title: Mesaj la autentificare
            requirement: Mesajul legal este afișat.
            policy_controls: [R2]
          - id: '2.1'
            title: Audit procese
            requirement: Crearea proceselor se auditează.
            policy_controls: [A1, R1]
          - id: '3.1'
            title: Cerință fără mapare
            requirement: Nu are încă un control de politică.
        """;

    private static (PolicyDocument, PolicyPlan) Read(Action<FakeSettings>? setup = null)
    {
        var p = PolicyLoader.Parse(PolicyYaml);
        var fake = new FakeSettings();
        fake.Values[p.Controls[0].Setting.Display] = new SettingValue("1", "dword");
        fake.Values[p.Controls[1].Setting.Display] = new SettingValue("Altceva", "string");
        setup?.Invoke(fake);
        return (p, new PolicyExecutor([fake], Path.Combine(Path.GetTempPath(), "unused"), "STATIE-1").Plan(p)); // no audit provider: A1 unreadable
    }

    [Fact]
    public void Benchmark_controls_are_satisfied_only_when_mapped_read_and_conforming()
    {
        var (p, plan) = Read();
        var a = ComplianceAssessment.Assess(BenchmarkLoader.Parse(Mapping, p), p, plan);
        var r = a.Controls.ToDictionary(c => c.BenchmarkControl, c => c.Result);
        Assert.Equal(ComplianceResult.Satisfied, r["1.1"]);
        Assert.Equal(ComplianceResult.NotSatisfied, r["1.2"]);
        Assert.Equal(ComplianceResult.NotAssessed, r["2.1"]); // A1 could not be read: never counted as satisfied
        Assert.Equal(ComplianceResult.NotAssessed, r["3.1"]);
        Assert.Contains("Altceva (string) ≠ Aviz", a.Controls[1].Reason);
        Assert.Contains("niciun furnizor", a.Controls[2].Reason);
        Assert.False(a.FullyCompliant);
        Assert.StartsWith("NU se poate declara conformitatea cu Benchmark intern 1.0: 1 satisfăcute, 1 nesatisfăcute, 2 neevaluate din 4", a.Statement);
        Assert.Equal((p.Sha256, plan.Sha256, "STATIE-1"), (a.PolicySha256, a.PlanSha256, a.Station));
    }

    [Fact]
    public void Only_a_fully_mapped_and_satisfied_benchmark_is_called_compliant()
    {
        var (p, plan) = Read(f => f.Values[PolicyLoader.Parse(PolicyYaml).Controls[1].Setting.Display] = new SettingValue("Aviz", "string"));
        var mapping = Mapping.Split("  - id: '2.1'")[0];
        var a = ComplianceAssessment.Assess(BenchmarkLoader.Parse(mapping, p), p, plan);
        Assert.True(a.FullyCompliant);
        Assert.StartsWith("Conform cu Benchmark intern 1.0: toate cele 2 controale mapate", a.Statement);
    }

    [Fact]
    public void Policy_is_its_own_baseline_when_no_benchmark_is_given()
    {
        var (p, plan) = Read();
        var b = BenchmarkLoader.FromPolicy(p);
        Assert.True(b.DerivedFromPolicy);
        var a = ComplianceAssessment.Assess(b, p, plan);
        Assert.Equal(("Politica LA-BASE", 3), (a.BenchmarkName, a.Controls.Count));
        Assert.Equal((1, 1, 1), (a.Satisfied, a.NotSatisfied, a.NotAssessed));
    }

    [Fact]
    public void Bad_mappings_and_foreign_reads_are_refused()
    {
        var (p, plan) = Read();
        Assert.Throws<FormatException>(() => BenchmarkLoader.Parse(Mapping.Replace("[R2]", "[R9]"), p));
        Assert.Throws<FormatException>(() => BenchmarkLoader.Parse(Mapping.Replace("requirement: Ecranul", "nivel: 1\n    requirement: Ecranul"), p));
        Assert.Throws<FormatException>(() => BenchmarkLoader.Parse(Mapping.Replace("'1.2'", "'1.1'"), p));
        Assert.Throws<FormatException>(() => BenchmarkLoader.Parse(Mapping.Replace("version: '1.0', ", ""), p));
        var other = PolicyLoader.Parse(PolicyYaml.Replace("'2.0'", "'2.1'"));
        Assert.Throws<PolicyLifecycleException>(() => ComplianceAssessment.Assess(BenchmarkLoader.FromPolicy(other), other, plan));
    }

    private static readonly Regex OscalUuid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[45][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

    [Fact]
    public void Oscal_assessment_results_link_findings_to_observations_and_evidence()
    {
        var (p, plan) = Read();
        var a = ComplianceAssessment.Assess(BenchmarkLoader.Parse(Mapping, p), p, plan);
        var (assessmentPath, oscalPath) = a.Save(_dir);
        foreach (var f in new[] { assessmentPath, oscalPath })
            Assert.Equal(PolicyDocument.Hash(File.ReadAllText(f)), File.ReadAllText(f + ".sha256").Trim());

        var ar = JsonNode.Parse(File.ReadAllText(oscalPath))!["assessment-results"]!;
        Assert.Equal("1.1.2", (string?)ar["metadata"]!["oscal-version"]);
        var result = ar["results"]![0]!;
        Assert.Equal(4, result["reviewed-controls"]!["control-selections"]![0]!["include-controls"]!.AsArray().Count);
        var observations = result["observations"]!.AsArray();
        Assert.Equal(3, observations.Count); // R1, R2, A1 — each read once
        var obsIds = observations.Select(o => (string)o!["uuid"]!).ToHashSet();
        Assert.All(observations, o => Assert.Equal(Path.GetFileName(assessmentPath), (string?)o!["relevant-evidence"]![0]!["href"]));
        var findings = result["findings"]!.AsArray();
        Assert.Equal(new[] { ("1.1", "satisfied"), ("1.2", "not-satisfied") },
            findings.Select(f => ((string)f!["target"]!["target-id"]!, (string)f["target"]!["status"]!["state"]!)));
        Assert.All(findings, f => Assert.All(f!["related-observations"]!.AsArray(), r => Assert.Contains((string)r!["observation-uuid"]!, obsIds)));
        Assert.Contains("2.1", (string?)result["remarks"]);
        Assert.Contains("3.1", (string?)result["remarks"]);
        var uuids = new[] { (string)ar["uuid"]!, (string)result["uuid"]! }.Concat(obsIds).Concat(findings.Select(f => (string)f!["uuid"]!)).ToList();
        Assert.All(uuids, u => Assert.Matches(OscalUuid, u));
        Assert.Equal(uuids.Count, uuids.Distinct().Count());
        Assert.Equal(OscalAssessmentResults.Build(a, "x", "y")["assessment-results"]!["uuid"]!.ToString(), (string)ar["uuid"]!); // deterministic ids
    }
}
