using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;

namespace LogAnalyzer.Dfir.AI;

/// <summary>
/// Analysis/ai_validation.json: the case-level validation of the AI text, written next to it, registered as an output that depends on every evidence
/// item (a later evidence change invalidates it through the existing exact-invalidation mechanism) and recorded in the hash-chained case audit.
/// </summary>
public static class AiValidationStore
{
    public const string FileName = "ai_validation.json";
    public const string RelativePath = "Analysis/ai_validation.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNameCaseInsensitive = true };

    public static string PathIn(string caseRoot) => Path.Combine(caseRoot, "Analysis", FileName);

    public static void Write(CaseWorkspace ws, AiValidationResult r)
    {
        var dir = Path.Combine(ws.Root, "Analysis");
        Directory.CreateDirectory(dir);
        File.WriteAllText(PathIn(ws.Root), JsonSerializer.Serialize(r, Options), new UTF8Encoding(false));
        var rec = ws.RecordOutput(RelativePath, "AiClaimValidator", AiValidationResult.ValidatorId, ws.LoadEvidence().Select(e => e.EvidenceId));
        var c = r.Counts;
        ws.Audit("ai.claim_validation",
            $"claims={r.Claims.Count} SUSTINUT={c["SUSTINUT"]} PARTIAL={c["PARTIAL"]} NESUSTINUT={c["NESUSTINUT"]} NEVERIFICABIL={c["NEVERIFICABIL"]} text={r.TextSha256} binding={r.EvidenceBindingSha256} file={rec.Sha256}");
    }

    /// <summary>The saved validation, or null when none exists; <paramref name="error"/> says why a file that exists could not be read.</summary>
    public static AiValidationResult? TryRead(string caseRoot, out string error)
    {
        error = "";
        var p = PathIn(caseRoot);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<AiValidationResult>(File.ReadAllText(p), Options) ?? throw new JsonException("fișier gol"); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }
}

/// <summary>One line of a report or screen about the AI analysis.</summary>
public sealed record AiReportLine(string Text, ClaimState? State, bool Struck, bool Warning, bool Heading = false);

/// <summary>
/// The only way AI text reaches a report: through a validation. A case with AI output but no readable validation prints no AI statement, only the notice.
/// Statements other than SUSTINUT carry their state and warning; NESUSTINUT is struck with its reason and listed as "nu este constatare".
/// </summary>
public static class AiValidationText
{
    public static List<AiReportLine> Lines(string caseRoot, IAiCaseFacts current)
    {
        var saved = AiValidationStore.TryRead(caseRoot, out var error);
        bool aiPresent = File.Exists(Path.Combine(caseRoot, "Analysis", "ai_reasoning.json"));
        if (saved is null)
        {
            if (error.Length > 0) return [new("Analiză AI: validarea afirmațiilor nu a putut fi citită (" + error + "); nicio afirmație AI nu este tipărită.", ClaimState.Neverificabil, false, true)];
            return aiPresent
                ? [new("Analiză AI: există rezultate ale modelului în caz, dar fără validare la nivel de caz (Analysis/ai_validation.json lipsește); nicio afirmație AI nu este tipărită.", ClaimState.Neverificabil, false, true)]
                : [new("Analiză AI: nerulată pentru acest caz.", null, false, false)];
        }
        return Render(saved.CheckAgainst(current));
    }

    public static List<AiReportLine> Render(AiValidationResult v)
    {
        var lines = new List<AiReportLine> { new(v.SummaryRomanian, v.AllSustained ? ClaimState.Sustinut : ClaimState.Neverificabil, false, !v.AllSustained, Heading: true) };
        lines.Add(new("Afirmațiile sunt ale unui model local, nu constatări ale aplicației; ele rămân nedovedite până când analistul verifică citările.", null, false, false));
        foreach (var c in v.Claims)
        {
            lines.Add(c.State switch
            {
                ClaimState.Sustinut => new AiReportLine($"[SUSTINUT] {c.Text} (citări: {string.Join(", ", c.Cites)})", c.State, false, false),
                ClaimState.Nesustinut => new AiReportLine($"[NESUSTINUT — nu este constatare] {c.Text} — motiv: {c.Reason}", c.State, true, true),
                _ => new AiReportLine($"[{c.Label} — avertisment] {c.Text} — {c.Reason}", c.State, false, true),
            });
        }
        return lines;
    }
}
