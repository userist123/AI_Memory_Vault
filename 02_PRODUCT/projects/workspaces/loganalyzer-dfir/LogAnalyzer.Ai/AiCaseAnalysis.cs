using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.AI;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Runs the evidence-constrained local model over a finished investigation (master spec §25). It starts only if every evidence
/// file still matches its acquisition hash, and writes into the case the validated result (Analysis/ai_reasoning.json), the
/// exact catalog and raw model answer, with their SHA-256 in the audit log. Nothing leaves the station: the model client
/// accepts only a loopback address.
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
        var ai = await new LocalModelClient(endpoint, model).ReasonAsync(catalog, omitted, contextTokens, ct);

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
        return ai;
    }
}
