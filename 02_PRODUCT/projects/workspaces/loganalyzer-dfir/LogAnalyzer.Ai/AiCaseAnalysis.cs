using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.AI;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Runs the evidence-constrained local model over a finished investigation (master spec §25). It starts only if every evidence
/// file still matches its acquisition hash. What the model wrote is then validated at case level (R22.5, <see cref="AiClaimValidator"/>):
/// every claim must cite items of THIS case that contain the values it states. Only then is anything written or shown: the validated result
/// (Analysis/ai_reasoning.json, where a NESUSTINUT statement is moved to the rejected list), the validation itself (Analysis/ai_validation.json,
/// bound to the case's evidence hashes), the exact catalog and raw model answer, with their SHA-256 in the hash-chained audit log.
/// Nothing leaves the station: the model client accepts only a loopback address.
/// </summary>
public static class AiCaseAnalysis
{
    public static async Task<AiReasoning> RunAsync(InvestigationResult r, string endpoint, string model, int maxItems = 80, int contextTokens = 24576,
                                                   CancellationToken ct = default)
    {
        var integrity = ReportIntegrity.Check(r);
        if (!integrity.AllIntact)
            throw new InvalidOperationException("Probele nu mai corespund hash-urilor de la achiziție: analiza AI nu pornește pe probe neverificate.");
        var (catalog, omitted) = EvidenceReasoner.BuildCatalog(r.Findings, r.Timeline, r.AntiForensics, r.Gaps, maxItems);
        var raw = await new LocalModelClient(endpoint, model).ReasonAsync(catalog, omitted, contextTokens, ct);
        return ValidateAndStore(r, raw, catalog);
    }

    /// <summary>
    /// Validates the model's statements against the case, then writes the case files. Separated from the model call so the validation path is the same
    /// for any source of statements (and testable with a fake model, no network).
    /// </summary>
    public static AiReasoning ValidateAndStore(InvestigationResult r, AiReasoning modelOutput, IReadOnlyList<CatalogItem> catalog, IAiCaseFacts? facts = null)
    {
        var ai = Validate(r, modelOutput, catalog, facts);

        var dir = Path.Combine(r.Case.Root, "Analysis");
        Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(Path.Combine(dir, "ai_catalog.txt"), EvidenceReasoner.CatalogText(catalog), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "ai_raw_response.json"), ai.RawResponse, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "ai_reasoning.json"), JsonSerializer.Serialize(ai with { RawResponse = "(Analysis/ai_raw_response.json)" }, opts), new UTF8Encoding(false));
        foreach (var f in new[] { "ai_catalog.txt", "ai_raw_response.json", "ai_reasoning.json" })
            r.Case.RecordOutput("Analysis/" + f, "LocalModel", ai.ModelDigest.Length >= 12 ? ai.ModelDigest[..12] : ai.ModelDigest);   // WP3b: outputs in custody
        r.Case.RecordTransformation("CASE", $"LocalModel {ai.Model}", ai.ModelDigest.Length >= 12 ? ai.ModelDigest[..12] : ai.ModelDigest, "Analysis/ai_reasoning.json",
            $"{ai.Accepted.Count} afirmații acceptate, {ai.Rejected.Count} respinse; catalog {ai.CatalogSha256}; răspuns {ai.ResponseSha256}");
        r.Case.Audit("ai.reasoning", $"model={ai.Model} digest={ai.ModelDigest} endpoint={ai.Endpoint} prompt={ai.PromptSha256} response={ai.ResponseSha256}");
        AiValidationStore.Write(r.Case, ai.Validation!);   // validation + evidence binding: custody output and hash-chained audit entry
        return ai;
    }

    private static int Rank(ClaimState s) => s switch { ClaimState.Nesustinut => 3, ClaimState.Neverificabil => 2, ClaimState.Partial => 1, _ => 0 };

    /// <summary>The pure part: statements in, statements with their case-level validation out (a NESUSTINUT statement moves to the rejected list).</summary>
    public static AiReasoning Validate(InvestigationResult r, AiReasoning modelOutput, IReadOnlyList<CatalogItem> catalog, IAiCaseFacts? facts = null)
    {
        facts ??= BuildFacts(r, catalog);
        var perStatement = modelOutput.Accepted
            .Select(s => (Statement: s, Inputs: AiClaimValidator.Split(s.Section, s.Text, s.Citations.Select(c => c.Item).ToList())))
            .ToList();
        // a statement that splits into nothing (empty text) is not a claim; one that is only a citation marker keeps its text as one claim
        var inputs = perStatement.SelectMany(p => p.Inputs).ToList();
        var refused = modelOutput.Rejected
            .Select(x => AiClaimValidator.Refused(x.Section, x.Text, x.Cites, "respinsă de verificările structurale: " + x.Reason)).ToList();
        var result = AiClaimValidator.Validate(inputs, facts, refused: refused);

        var accepted = new List<AiStatement>();
        var rejected = new List<RejectedStatement>(modelOutput.Rejected);
        int at = 0;
        foreach (var (st, ins) in perStatement)
        {
            var mine = result.Claims.Skip(at).Take(ins.Count).ToList();
            at += ins.Count;
            if (mine.Count == 0) continue;
            var worst = mine.OrderByDescending(c => Rank(c.State)).First().State;
            var reason = string.Join(" | ", mine.Where(c => c.State != ClaimState.Sustinut).Select(c => c.Reason).Where(x => x.Length > 0).Distinct());
            var agg = new ValidatedClaim(st.Section, st.Text, st.Citations.Select(c => c.Item).ToList(), worst, reason, mine.SelectMany(c => c.Facts).ToList());
            if (worst == ClaimState.Nesustinut) rejected.Add(new RejectedStatement(st.Section, st.Text, agg.Cites, "validare la nivel de caz: " + reason));
            else accepted.Add(st with { Validation = agg });
        }
        return modelOutput with { Accepted = accepted, Rejected = rejected, Validation = result };
    }

    /// <summary>The case's facts with the catalog's local ids (E3 …) tied to the case records they stand for.</summary>
    public static AiCaseFacts BuildFacts(InvestigationResult r, IReadOnlyList<CatalogItem> catalog)
    {
        var facts = AiCaseFactsFactory.ForResult(r);
        foreach (var item in catalog.Where(i => i.Kind == "event" && i.Evidence.Count > 0))
        {
            var key = AiCaseFacts.EventKey(item.Evidence[0].EvidenceId, item.Evidence[0].Locator);
            if (facts.Find(key) is not null && facts.Find(item.Id) is null) facts.Alias(item.Id, key);
        }
        return facts;
    }
}
