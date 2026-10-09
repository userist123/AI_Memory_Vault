using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>
/// WP4 verification layer v1. Reads a case's outputs from disk (the evidence index, Analysis/findings.json, timeline.csv, graph.json,
/// dependencies.json, parsing.json, invalidations.json, ai_reasoning.json), checks every finding and writes Analysis/verification.json.
/// It never edits findings.json, never calls producer code (parsers, correlation, rules) and starts no process and opens no connection.
/// It is a separate module of the same application: its verdicts are an automatic cross-check, not an external or human verification
/// (see docs/dfir/VERIFICATION.md).
/// </summary>
public static class CaseVerifier
{
    private static readonly StandardState[] Precedence = [StandardState.Rejected, StandardState.Contradicted, StandardState.Unproven, StandardState.Unknown];

    /// <summary>Runs the checks, writes Analysis/verification.json, registers it in custody and writes the audit entry <c>verification.run</c>.</summary>
    public static VerificationReport Verify(CaseWorkspace ws, VerificationOptions? options = null, CancellationToken ct = default)
    {
        var o = options ?? new VerificationOptions();
        var report = Evaluate(ws, o, ct);
        var dir = Path.Combine(ws.Root, "Analysis");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, VerificationReport.FileName), report.ToJson(), new System.Text.UTF8Encoding(false));
        var evidenceIds = ws.LoadEvidence().Select(e => e.EvidenceId);
        ws.RecordOutput(VerificationReport.RelativePath, "LogAnalyzer.Verification", VerificationReport.VerifierId, evidenceIds);
        ws.Audit("verification.run", $"{report.Findings.Count} constatări, {report.AiStatements.Count} afirmații AI; " +
                 string.Join(" ", VerificationReport.VerdictOrder.Select(v => $"{v.ToSpec()}={report.Count(v)}")));
        return report;
    }

    /// <summary>The same checks without writing anything (used by <see cref="Verify"/> and by tests).</summary>
    public static VerificationReport Evaluate(CaseWorkspace ws, VerificationOptions o, CancellationToken ct = default)
    {
        // Evidence to re-hash: what the findings cite and what the AI analysis cites.
        var findings0 = TryReadFindings(ws);
        var toHash = findings0.SelectMany(f => f.SupportingEvidence.Select(r => r.EvidenceId)).Concat(AiCitedEvidence(ws));
        var facts = CaseFacts.Load(ws, o, toHash, ct);
        var list = facts.FindingsFile?.Findings ?? [];
        var report = new VerificationReport { CaseId = ws.Info.CaseId, GeneratedUtc = o.Clock(), Inputs = facts.Inputs, Notes = facts.Notes };

        if (facts.FindingsFile is null)
            report.Notes.Add(facts.Inputs.GetValueOrDefault("Analysis/findings.json") == "prezent"
                ? "Analysis/findings.json nu a putut fi citit: nicio constatare verificată."
                : "Analysis/findings.json lipsește: nu există constatări de verificat (caz fără investigație).");

        foreach (var f in list)
        {
            ct.ThrowIfCancellationRequested();
            report.Findings.Add(VerifyOne(f, facts, o));
        }
        report.Counts = VerificationReport.CountVerdicts(report.Findings.Select(v => v.Verdict));
        report.AiStatements = AiClaims.Evaluate(facts, report.Findings.GroupBy(v => v.FindingId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
        facts.AiReasoning?.Dispose();
        return report;
    }

    private static List<Finding> TryReadFindings(CaseWorkspace ws)
    {
        var p = Path.Combine(ws.Root, "Analysis", "findings.json");
        if (!File.Exists(p)) return [];
        try { return LogAnalyzer.Dfir.IO.FindingsFile.Read(p).Findings; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Evidence ids the AI analysis cites (Analysis/ai_reasoning.json), so their integrity is checked too.</summary>
    private static List<string> AiCitedEvidence(CaseWorkspace ws)
    {
        var p = Path.Combine(ws.Root, "Analysis", "ai_reasoning.json");
        var ids = new List<string>();
        if (!File.Exists(p)) return ids;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
            void Walk(System.Text.Json.JsonElement e)
            {
                switch (e.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Object:
                        foreach (var prop in e.EnumerateObject())
                            if (prop.Name.Equals("evidenceId", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String) ids.Add(prop.Value.GetString()!);
                            else Walk(prop.Value);
                        break;
                    case System.Text.Json.JsonValueKind.Array:
                        foreach (var x in e.EnumerateArray()) Walk(x);
                        break;
                }
            }
            Walk(doc.RootElement);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException) { /* reported by CaseFacts.Load as a note */ }
        return ids;
    }

    internal static FindingVerdict VerifyOne(Finding f, CaseFacts facts, VerificationOptions o)
    {
        var refs = f.SupportingEvidence.GroupBy(r => (r.EvidenceId, r.Locator)).Select(g => ArtifactKinds.Resolve(g.First(), facts)).ToList();
        var v = new FindingVerdict
        {
            FindingId = f.FindingId, RuleId = f.RuleId, Title = f.Title,
            SemanticType = f.HasSemanticType ? f.SemanticType.ToSpec() : "NECUNOSCUT",
            EvidenceKinds = refs.Select(r => r.Kind).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
        };
        var unsupported = Checks.Unsupported(f);
        v.Checks.Add(unsupported);
        v.Checks.Add(Checks.Provenance(f, refs, facts));
        v.Checks.Add(Checks.Dependencies(f, facts));
        v.Checks.Add(Checks.Sufficiency(f, refs));
        v.Checks.Add(Checks.Temporal(f, refs, facts, o));
        v.Checks.Add(Checks.Graph(f, refs, facts));
        var (contra, found) = Checks.Contradictions(f, refs, facts, o);
        v.Checks.Add(contra);
        v.ContradictingEvidence = found;
        var (missing, missingList) = Checks.MissingEvidence(f, facts);
        v.Checks.Add(missing);
        v.MissingEvidence = missingList;

        (v.Verdict, v.Reason) = Decide(f, v, refs);
        return v;
    }

    /// <summary>Most severe check effect wins; with none, SUPPORTED, or VERIFIED when at least two independent artifact families agree.</summary>
    private static (StandardState, string) Decide(Finding f, FindingVerdict v, IReadOnlyList<ResolvedRef> refs)
    {
        foreach (var s in Precedence)
        {
            var hits = v.Checks.Where(c => c.Effect == s).ToList();
            if (hits.Count > 0) return (s, string.Join(" ", hits.Select(h => $"[{h.CheckId}] {h.Reason}")));
        }
        var suff = v.Checks.First(c => c.CheckId == CheckIds.Sufficiency);
        if (suff.Outcome == CheckOutcome.NotApplicable)
            return (StandardState.NotAssessed, $"[{CheckIds.Sufficiency}] {suff.Reason} Nicio altă verificare nu a găsit probleme, dar fără tipul semantic nu se poate da un verdict mai tare.");
        var families = refs.Where(r => r.IsResolved).Select(r => r.Family).Distinct(StringComparer.Ordinal).ToList();
        if (families.Count >= 2 && f.SemanticType != SemanticType.Correlation)
            return (StandardState.Verified, $"Probe suficiente, intacte și consecvente, din {families.Count} tipuri de artefact independente: {string.Join(", ", families)}. Verificare automată, nu externă.");
        var why = f.SemanticType == SemanticType.Correlation
            ? "o corelație nu devine VERIFIED (corelația nu este dovadă)"
            : $"un singur tip de artefact ({(families.Count == 1 ? families[0] : "neidentificat")}); VERIFIED cere cel puțin două tipuri independente";
        return (StandardState.Supported, $"Probe suficiente, intacte și consecvente; rămâne SUPPORTED: {why}.");
    }
}
