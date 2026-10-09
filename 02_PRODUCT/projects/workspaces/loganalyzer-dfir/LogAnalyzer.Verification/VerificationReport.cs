using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>Stable ids of the checks (they appear in Analysis/verification.json, in the docs and in the tests).</summary>
public static class CheckIds
{
    public const string Unsupported = "UNSUPPORTED";
    public const string Provenance = "PROVENANCE";
    public const string Dependencies = "DEPENDENCIES";
    public const string Sufficiency = "SUFFICIENCY";
    public const string Temporal = "TEMPORAL";
    public const string Graph = "GRAPH";
    public const string Contradictions = "CONTRADICTIONS";
    public const string MissingEvidence = "MISSING_EVIDENCE";
    public const string AiClaims = "AI_CLAIMS";
}

/// <summary>What one check found. <see cref="Info"/> never changes a verdict; <see cref="Unknown"/> means the check could not be completed.</summary>
[JsonConverter(typeof(SpecEnumConverter<CheckOutcome>))]
public enum CheckOutcome { Pass, Fail, Unknown, NotApplicable, Info }

/// <summary>
/// One check on one finding. <see cref="Effect"/> is the verdict this check pushes the finding toward (REJECTED, CONTRADICTED, UNPROVEN or
/// UNKNOWN), or null when it pushes nowhere. The reason is Romanian text, deterministic for the same case.
/// </summary>
public sealed record CheckResult(string CheckId, CheckOutcome Outcome, StandardState? Effect, string Reason, IReadOnlyList<string> Details)
{
    public static CheckResult Pass(string id, string reason, params string[] details) => new(id, CheckOutcome.Pass, null, reason, details);
    public static CheckResult Fail(string id, StandardState effect, string reason, IEnumerable<string>? details = null) => new(id, CheckOutcome.Fail, effect, reason, (details ?? []).ToList());
    public static CheckResult Unknown(string id, string reason, IEnumerable<string>? details = null) => new(id, CheckOutcome.Unknown, StandardState.Unknown, reason, (details ?? []).ToList());
    public static CheckResult NotApplicable(string id, string reason) => new(id, CheckOutcome.NotApplicable, null, reason, []);
    public static CheckResult Info(string id, string reason, IEnumerable<string>? details = null) => new(id, CheckOutcome.Info, null, reason, (details ?? []).ToList());
}

/// <summary>The verdict on one finding, with every check result so each one can be drilled into.</summary>
public sealed class FindingVerdict
{
    public string FindingId { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string SemanticType { get; set; } = "";
    public StandardState Verdict { get; set; } = StandardState.NotAssessed;
    public string Reason { get; set; } = "";
    public List<CheckResult> Checks { get; set; } = [];
    /// <summary>What the contradiction rules found. Mirrors <c>Finding.ContradictingEvidence</c> in the verification output; findings.json is never rewritten.</summary>
    public List<string> ContradictingEvidence { get; set; } = [];
    /// <summary>The rule's own missing evidence merged with the artifact kinds this claim expects and the case does not contain.</summary>
    public List<string> MissingEvidence { get; set; } = [];
    /// <summary>Artifact kinds the supporting evidence resolves to (e.g. "Prefetch", "EventLog:Security 4688").</summary>
    public List<string> EvidenceKinds { get; set; } = [];
}

/// <summary>The verdict on one statement of the AI analysis (Analysis/ai_reasoning.json). Never VERIFIED.</summary>
public sealed class AiStatementVerdict
{
    public string Section { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Cites { get; set; } = [];
    public StandardState Verdict { get; set; } = StandardState.Unproven;
    public string Reason { get; set; } = "";
}

/// <summary>Analysis/verification.json.</summary>
public sealed class VerificationReport
{
    public const string VerifierId = "LogAnalyzer.Verification/1.0";
    public const string FileName = "verification.json";
    public const string RelativePath = "Analysis/verification.json";

    /// <summary>Verdicts in the order the banner lists them.</summary>
    public static readonly StandardState[] VerdictOrder =
    [
        StandardState.Verified, StandardState.Supported, StandardState.Unproven, StandardState.Unknown, StandardState.NotAssessed,
        StandardState.Contradicted, StandardState.Rejected,
    ];

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.Verification;
    public string CaseId { get; set; } = "";
    public string Verifier { get; set; } = VerifierId;
    public DateTimeOffset GeneratedUtc { get; set; }
    /// <summary>Count of findings per verdict (SPEC names; all seven are always present).</summary>
    public Dictionary<string, int> Counts { get; set; } = [];
    public List<FindingVerdict> Findings { get; set; } = [];
    public List<AiStatementVerdict> AiStatements { get; set; } = [];
    /// <summary>Which inputs were present ("prezent" / "lipsește") and anything the run could not read.</summary>
    public Dictionary<string, string> Inputs { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    /// <summary>What this module is and is not (always the same text; see docs/dfir/VERIFICATION.md).</summary>
    public string Limits { get; set; } = LimitsText;

    public const string LimitsText =
        "Verificare automată deterministă, rulată de aceeași aplicație (modul separat, fără apeluri către cod producător). " +
        "Nu este o verificare externă sau umană. VERIFIED înseamnă doar: dovezi suficiente, intacte și consecvente, din cel puțin două tipuri de artefact; " +
        "nu înseamnă că activitatea descrisă este dovedită în sens juridic. Lipsa unei contradicții nu dovedește că nu există.";

    public static string Name(StandardState s) => s.ToSpec();

    public static Dictionary<string, int> CountVerdicts(IEnumerable<StandardState> verdicts)
    {
        var counts = VerdictOrder.ToDictionary(v => v.ToSpec(), _ => 0);
        foreach (var v in verdicts) counts[v.ToSpec()] = counts.GetValueOrDefault(v.ToSpec()) + 1;
        return counts;
    }

    public int Count(StandardState s) => Counts.GetValueOrDefault(s.ToSpec());

    /// <summary>One line for reports and the investigation view, e.g. "Verificare: 3 VERIFIED, 5 SUPPORTED, 2 UNPROVEN, 1 CONTRADICTED".</summary>
    public string Banner => Line(Counts);

    public static string Line(IReadOnlyDictionary<string, int> counts)
    {
        var parts = VerdictOrder.Where(v => counts.GetValueOrDefault(v.ToSpec()) > 0).Select(v => $"{counts[v.ToSpec()]} {v.ToSpec()}").ToList();
        return parts.Count == 0 ? "Verificare: nicio constatare de verificat" : "Verificare: " + string.Join(", ", parts);
    }

    /// <summary>Banner for the operator (WP6a, U7): the Romanian state label first, the spec name in parentheses, e.g. "Verificare: 3 Verificat (VERIFIED), 2 Nedemonstrat (UNPROVEN)". <see cref="Banner"/> stays the data form.</summary>
    public string BannerRomanian => LineRomanian(Counts);

    public static string LineRomanian(IReadOnlyDictionary<string, int> counts)
    {
        var parts = VerdictOrder.Where(v => counts.GetValueOrDefault(v.ToSpec()) > 0)
            .Select(v => $"{counts[v.ToSpec()]} {LogAnalyzer.Dfir.Analysis.StateLabels.Romanian(v)} ({v.ToSpec()})").ToList();
        return parts.Count == 0 ? "Verificare: nicio constatare de verificat" : "Verificare: " + string.Join(", ", parts);
    }

    /// <summary>Warning text when any finding is CONTRADICTED or REJECTED; null otherwise.</summary>
    public string? Warning => WarningFor(Counts);

    public static string? WarningFor(IReadOnlyDictionary<string, int> counts)
    {
        int c = counts.GetValueOrDefault(StandardState.Contradicted.ToSpec()), r = counts.GetValueOrDefault(StandardState.Rejected.ToSpec());
        if (c + r == 0) return null;
        var what = string.Join(" și ", new[] { c > 0 ? $"{c} CONTRADICTED" : "", r > 0 ? $"{r} REJECTED" : "" }.Where(x => x.Length > 0));
        return $"ATENȚIE: {what}. Aceste constatări sunt contrazise de alte dovezi sau nu au dovezi valide; nu le prezentați ca fapte stabilite și nu le exportați în Vault.";
    }

    public FindingVerdict? Of(string findingId) => Findings.FirstOrDefault(f => f.FindingId == findingId);

    /// <summary>The verdicts as the contract's <see cref="FindingVerification"/> (what the Vault gate and the reports consume).</summary>
    public Dictionary<string, FindingVerification> ToContract() =>
        Findings.GroupBy(f => f.FindingId, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
            new FindingVerification(g.First().Verdict, g.First().Reason, Verifier, GeneratedUtc), StringComparer.Ordinal);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public string ToJson() => SchemaVersions.WithVersion(this, SchemaVersion, JsonOptions);

    /// <summary>Reads a verification.json; null when the file is absent. A newer major schema throws (see <see cref="SchemaVersions.Accept"/>).</summary>
    public static VerificationReport? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var r = JsonSerializer.Deserialize<VerificationReport>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"Empty JSON: {path}");
        r.SchemaVersion = SchemaVersions.Accept(r.SchemaVersion, Path.GetFileName(path));
        return r;
    }
}
