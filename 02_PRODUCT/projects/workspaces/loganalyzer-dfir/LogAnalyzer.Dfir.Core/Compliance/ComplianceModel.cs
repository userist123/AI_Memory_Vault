using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Policy;
using YamlDotNet.RepresentationModel;

namespace LogAnalyzer.Dfir.Compliance;

/// <summary>One benchmark requirement and the policy controls that implement it (the mapping is owner-supplied, never guessed).</summary>
public sealed record BenchmarkControl(string Id, string Title, string Requirement, IReadOnlyList<string> PolicyControls);

/// <summary>A benchmark or baseline (name, version, source) as the owner maps it onto one policy.</summary>
public sealed record Benchmark(string Name, string Version, string Source, IReadOnlyList<BenchmarkControl> Controls, string Sha256, bool DerivedFromPolicy);

public static class BenchmarkLoader
{
    private static readonly string[] HeadKeys = ["name", "version", "source"];
    private static readonly string[] ControlKeys = ["id", "title", "requirement", "policy_controls"];

    /// <summary>
    /// benchmark: { name, version, source } and controls: [ { id, title, requirement, policy_controls: [...] } ].
    /// Unknown keys are rejected; a mapping to a control the policy does not have is an error.
    /// </summary>
    public static Benchmark Parse(string yaml, PolicyDocument policy)
    {
        var ys = new YamlStream();
        ys.Load(new StringReader(yaml));
        if (ys.Documents.Count == 0 || ys.Documents[0].RootNode is not YamlMappingNode root) throw new FormatException("Maparea nu este un document YAML.");
        var head = Child(root, "benchmark") as YamlMappingNode ?? throw new FormatException("Lipsește secțiunea „benchmark”.");
        Reject(head, HeadKeys, "benchmark");
        var list = Child(root, "controls") as YamlSequenceNode ?? throw new FormatException("Lipsește lista „controls”.");
        var known = policy.Controls.Select(c => c.Id).ToHashSet();
        var controls = new List<BenchmarkControl>();
        foreach (var (node, i) in list.Children.Select((n, i) => (n, i)))
        {
            var m = node as YamlMappingNode ?? throw new FormatException($"controls[{i}] nu este o hartă.");
            Reject(m, ControlKeys, $"controls[{i}]");
            var ids = Child(m, "policy_controls") switch
            {
                YamlSequenceNode s => s.Children.Select(x => ((YamlScalarNode)x).Value ?? "").ToList(),
                YamlScalarNode s when (s.Value ?? "").Length > 0 => [s.Value!],
                _ => [],
            };
            var id = Text(m, "id");
            if (id.Length == 0) throw new FormatException($"controls[{i}]: lipsește id.");
            foreach (var missing in ids.Where(x => !known.Contains(x)))
                throw new FormatException($"{id}: controlul de politică „{missing}” nu există în {policy.Id} {policy.Version}.");
            controls.Add(new BenchmarkControl(id, Text(m, "title"), Text(m, "requirement"), ids));
        }
        foreach (var dup in controls.GroupBy(c => c.Id).Where(g => g.Count() > 1)) throw new FormatException($"Control de benchmark duplicat: {dup.Key}.");
        var b = new Benchmark(Text(head, "name"), Text(head, "version"), Text(head, "source"), controls, PolicyDocument.Hash(yaml), false);
        if (b.Name.Length == 0 || b.Version.Length == 0) throw new FormatException("benchmark.name și benchmark.version sunt obligatorii.");
        return b;
    }

    /// <summary>Without a benchmark the policy is its own baseline: one requirement per policy control.</summary>
    public static Benchmark FromPolicy(PolicyDocument p) =>
        new($"Politica {p.Id}", p.Version, $"policy sha256 {p.Sha256}",
            p.Controls.Select(c => new BenchmarkControl(c.Id, c.Title, $"{c.Setting.Display} = {c.Desired.Display}", [c.Id])).ToList(), p.Sha256, true);

    private static YamlNode? Child(YamlMappingNode m, string key) => m.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v : null;
    private static string Text(YamlMappingNode m, string key) => Child(m, key) is YamlScalarNode s ? s.Value ?? "" : "";
    private static void Reject(YamlMappingNode m, string[] allowed, string where)
    {
        var unknown = m.Children.Keys.Select(k => ((YamlScalarNode)k).Value ?? "").Where(k => !allowed.Contains(k)).ToList();
        if (unknown.Count > 0) throw new FormatException($"{where}: chei necunoscute {string.Join(", ", unknown)}.");
    }
}

public enum ComplianceResult { Satisfied, NotSatisfied, NotAssessed }

/// <summary>What was read for one policy control: the observation a benchmark result rests on.</summary>
public sealed record ComplianceObservation(string PolicyControl, string Setting, string Expected, string? Observed, ControlState State, string Detail);

public sealed record ControlAssessment(string BenchmarkControl, string Title, string Requirement, ComplianceResult Result, string Reason, IReadOnlyList<ComplianceObservation> Observations);

/// <summary>
/// BENCHMARK → VERSION → CONTROL → REQUIREMENT → DETECTION (policy controls) → EVIDENCE (values read, plan hash) → RESULT.
/// A benchmark control is Satisfied only when it is mapped and every mapped policy control was read and conforms.
/// </summary>
public sealed record ComplianceAssessment(
    string AssessmentId, string BenchmarkName, string BenchmarkVersion, string BenchmarkSource, string BenchmarkSha256, bool BenchmarkDerivedFromPolicy,
    string PolicyId, string PolicyVersion, string PolicySha256, string PlanSha256, string Station, DateTimeOffset AssessedUtc,
    IReadOnlyList<ControlAssessment> Controls)
{
    [JsonIgnore] public int Satisfied => Controls.Count(c => c.Result == ComplianceResult.Satisfied);
    [JsonIgnore] public int NotSatisfied => Controls.Count(c => c.Result == ComplianceResult.NotSatisfied);
    [JsonIgnore] public int NotAssessed => Controls.Count(c => c.Result == ComplianceResult.NotAssessed);

    /// <summary>True only if every benchmark control was mapped, read and satisfied.</summary>
    [JsonIgnore] public bool FullyCompliant => Controls.Count > 0 && Satisfied == Controls.Count;

    /// <summary>The only sentence the product says about compliance. It names what was checked and never rounds up.</summary>
    [JsonIgnore]
    public string Statement => FullyCompliant
        ? $"Conform cu {BenchmarkName} {BenchmarkVersion}: toate cele {Controls.Count} controale mapate au fost citite și sunt satisfăcute pe {Station} ({AssessedUtc:yyyy-MM-dd HH:mm} UTC)."
        : $"NU se poate declara conformitatea cu {BenchmarkName} {BenchmarkVersion}: {Satisfied} satisfăcute, {NotSatisfied} nesatisfăcute, {NotAssessed} neevaluate din {Controls.Count} controale.";

    public static ComplianceAssessment Assess(Benchmark b, PolicyDocument policy, PolicyPlan plan)
    {
        if (!plan.PolicySha256.Equals(policy.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException("Citirile (planul) nu aparțin acestei politici: SHA-256 diferit.");
        var reads = plan.Controls.ToDictionary(c => c.ControlId);
        var list = new List<ControlAssessment>();
        foreach (var bc in b.Controls)
        {
            var obs = bc.PolicyControls.Select(id => reads[id]).Select(r => new ComplianceObservation(r.ControlId, r.Setting.Display, r.Desired, r.Current?.Display,
                r.State, r.State == ControlState.Unreadable ? r.ReadError ?? "" : "")).ToList();
            (ComplianceResult res, string why) = obs.Count == 0 ? (ComplianceResult.NotAssessed, "fără mapare pe controale de politică")
                : obs.Any(o => o.State == ControlState.NonCompliant) ? (ComplianceResult.NotSatisfied, string.Join("; ", obs.Where(o => o.State == ControlState.NonCompliant).Select(o => $"{o.Setting}: {o.Observed ?? "(nu există)"} ≠ {o.Expected}")))
                : obs.Any(o => o.State == ControlState.Unreadable) ? (ComplianceResult.NotAssessed, "necitit: " + string.Join("; ", obs.Where(o => o.State == ControlState.Unreadable).Select(o => $"{o.Setting}: {o.Detail}")))
                : (ComplianceResult.Satisfied, "toate setările mapate sunt conforme");
            list.Add(new ControlAssessment(bc.Id, bc.Title, bc.Requirement, res, why, obs));
        }
        return new ComplianceAssessment(Guid.NewGuid().ToString(), b.Name, b.Version, b.Source, b.Sha256, b.DerivedFromPolicy, policy.Id, policy.Version, policy.Sha256,
            plan.Sha256, plan.Station, plan.CreatedUtc, list);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>Writes the assessment and its OSCAL assessment-results next to each other, each with a .sha256 file.</summary>
    public (string AssessmentPath, string OscalPath) Save(string dir)
    {
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, $"{AssessedUtc:yyyyMMddTHHmmssZ}-{AssessmentId[..8]}");
        var a = basePath + ".assessment.json";
        var text = JsonSerializer.Serialize(this, Json);
        Write(a, text);
        var o = basePath + ".oscal-ar.json";
        Write(o, OscalAssessmentResults.Build(this, Path.GetFileName(a), Sha(text)).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return (a, o);
    }

    private static void Write(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(false));
        File.WriteAllText(path + ".sha256", Sha(text) + "\n");
    }

    internal static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}

/// <summary>
/// OSCAL assessment-results (1.1.2 model): one result; reviewed controls = the benchmark controls; one observation per policy
/// control read (method TEST, evidence = the assessment file and its SHA-256); one finding per assessed benchmark control with
/// status satisfied / not-satisfied and links to its observations. Not-assessed controls get no finding and are listed in the
/// result's remarks. There is no OSCAL assessment plan: import-ap points at the policy, which is stated in the remarks.
/// </summary>
public static class OscalAssessmentResults
{
    public const string OscalVersion = "1.1.2";

    public static JsonObject Build(ComplianceAssessment a, string evidenceHref, string evidenceSha256)
    {
        var observations = new JsonArray();
        var obsIds = new Dictionary<string, string>();
        foreach (var o in a.Controls.SelectMany(c => c.Observations).DistinctBy(o => o.PolicyControl))
        {
            var id = Uuid($"{a.AssessmentId}/obs/{o.PolicyControl}");
            obsIds[o.PolicyControl] = id;
            observations.Add(new JsonObject
            {
                ["uuid"] = id,
                ["title"] = $"{o.PolicyControl}: {o.Setting}",
                ["description"] = $"Așteptat: {o.Expected}. Observat: {o.Observed ?? (o.State == ControlState.Unreadable ? "necitit" : "(nu există)")}. Stare: {o.State}." +
                                  (o.Detail.Length > 0 ? $" {o.Detail}" : ""),
                ["methods"] = new JsonArray("TEST"),
                ["subjects"] = new JsonArray(new JsonObject { ["subject-uuid"] = Uuid("station/" + a.Station), ["type"] = "component", ["title"] = a.Station }),
                ["relevant-evidence"] = new JsonArray(new JsonObject { ["href"] = evidenceHref, ["description"] = $"Evaluarea LogAnalyzer, SHA-256 {evidenceSha256}; plan {a.PlanSha256}." }),
                ["collected"] = a.AssessedUtc.ToString("O"),
            });
        }
        var findings = new JsonArray();
        foreach (var c in a.Controls.Where(c => c.Result != ComplianceResult.NotAssessed))
            findings.Add(new JsonObject
            {
                ["uuid"] = Uuid($"{a.AssessmentId}/finding/{c.BenchmarkControl}"),
                ["title"] = $"{c.BenchmarkControl} {c.Title}".Trim(),
                ["description"] = c.Reason,
                ["target"] = new JsonObject
                {
                    ["type"] = "objective-id",
                    ["target-id"] = c.BenchmarkControl,
                    ["status"] = new JsonObject { ["state"] = c.Result == ComplianceResult.Satisfied ? "satisfied" : "not-satisfied" },
                },
                ["related-observations"] = new JsonArray(c.Observations.Select(o => (JsonNode)new JsonObject { ["observation-uuid"] = obsIds[o.PolicyControl] }).ToArray()),
            });
        var notAssessed = a.Controls.Where(c => c.Result == ComplianceResult.NotAssessed).Select(c => $"{c.BenchmarkControl} ({c.Reason})").ToList();
        var result = new JsonObject
        {
            ["uuid"] = Uuid($"{a.AssessmentId}/result"),
            ["title"] = $"{a.BenchmarkName} {a.BenchmarkVersion} pe {a.Station}",
            ["description"] = a.Statement,
            ["start"] = a.AssessedUtc.ToString("O"),
            ["end"] = a.AssessedUtc.ToString("O"),
            ["reviewed-controls"] = new JsonObject
            {
                ["control-selections"] = new JsonArray(new JsonObject
                {
                    ["include-controls"] = new JsonArray(a.Controls.Select(c => (JsonNode)new JsonObject { ["control-id"] = c.BenchmarkControl }).ToArray()),
                }),
            },
            ["observations"] = observations,
            ["remarks"] = (notAssessed.Count > 0 ? $"Neevaluate ({notAssessed.Count}): {string.Join("; ", notAssessed)}. " : "") +
                          $"Nu există un plan de evaluare OSCAL; import-ap indică politica {a.PolicyId} {a.PolicyVersion} (SHA-256 {a.PolicySha256}).",
        };
        if (findings.Count > 0) result["findings"] = findings;
        return new JsonObject
        {
            ["assessment-results"] = new JsonObject
            {
                ["uuid"] = Uuid(a.AssessmentId + "/ar"),
                ["metadata"] = new JsonObject
                {
                    ["title"] = $"Evaluare {a.BenchmarkName} {a.BenchmarkVersion}",
                    ["last-modified"] = a.AssessedUtc.ToString("O"),
                    ["version"] = a.AssessmentId,
                    ["oscal-version"] = OscalVersion,
                },
                ["import-ap"] = new JsonObject { ["href"] = $"policy:{a.PolicyId}@{a.PolicyVersion}#sha256={a.PolicySha256}" },
                ["results"] = new JsonArray(result),
            },
        };
    }

    /// <summary>Deterministic name-based UUID (SHA-1 over the name, version 5 and variant bits set; no namespace UUID), so the same assessment always yields the same ids.</summary>
    public static string Uuid(string name)
    {
        var h = SHA1.HashData(Encoding.UTF8.GetBytes(name));
        h[6] = (byte)((h[6] & 0x0F) | 0x50);
        h[8] = (byte)((h[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(h, 0, 16).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }
}
