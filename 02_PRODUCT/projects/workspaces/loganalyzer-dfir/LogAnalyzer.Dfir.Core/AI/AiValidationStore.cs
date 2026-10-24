using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            $"claims={r.Claims.Count} VALORI_CONFIRMATE={c["VALORI_CONFIRMATE"]} PARTIAL={c["PARTIAL"]} NESUSTINUT={c["NESUSTINUT"]} NEVERIFICABIL={c["NEVERIFICABIL"]} text={r.TextSha256} binding={r.EvidenceBindingSha256} file={rec.Sha256}");
    }

    /// <summary>
    /// The saved validation, or null when none exists; <paramref name="error"/> says why a file that exists could not be read. The file is checked against
    /// the SHA-256 the custody chain recorded when it was written: a file that was changed afterwards (or has no custody entry) is returned with every claim
    /// NEVERIFICABIL ("fișierul de validare a fost modificat").
    /// </summary>
    public static AiValidationResult? TryRead(string caseRoot, out string error)
    {
        error = "";
        var p = PathIn(caseRoot);
        if (!File.Exists(p)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<AiValidationResult>(File.ReadAllText(p), Options) ?? throw new JsonException("fișier gol");
            var why = CustodyMismatch(caseRoot, p);
            return why.Length == 0 ? parsed : Tampered(parsed, why);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    internal const string TamperedText = "fișierul de validare a fost modificat";

    private static AiValidationResult Tampered(AiValidationResult r, string why) => new()
    {
        CaseId = r.CaseId, ValidatedUtc = r.ValidatedUtc, TextSha256 = r.TextSha256, EvidenceBindingSha256 = r.EvidenceBindingSha256, Stale = true,
        StaleReason = TamperedText + " (" + why + ")",
        Claims = r.Claims.Select(c => c with { State = ClaimState.Neverificabil, Reason = TamperedText + ": starea de la validare nu mai este de încredere", StateAtValidation = null }).ToList(),
    };

    /// <summary>"" when the file's SHA-256 equals the latest custody entry for it; otherwise why not.</summary>
    private static string CustodyMismatch(string caseRoot, string file)
    {
        var log = Path.Combine(caseRoot, "Logs", "chain_of_custody.jsonl");
        string? recorded = null;
        if (File.Exists(log))
            foreach (var line in File.ReadLines(log))
            {
                if (line.IndexOf("output.written", StringComparison.Ordinal) < 0) continue;
                try
                {
                    var o = JsonNode.Parse(line)?.AsObject();
                    if (o?["action"]?.GetValue<string>() == "output.written" && string.Equals(o["to"]?.GetValue<string>(), RelativePath, StringComparison.OrdinalIgnoreCase))
                        recorded = o["sha256"]?.GetValue<string>();
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
        if (string.IsNullOrEmpty(recorded)) return "fără intrare în lanțul de custodie";
        var actual = LogAnalyzer.Dfir.IO.Hashing.Sha256File(file);
        return string.Equals(actual, recorded, StringComparison.OrdinalIgnoreCase) ? "" : "SHA-256 diferit de cel din lanțul de custodie";
    }
}

/// <summary>One line of a report or screen about the AI analysis.</summary>
public sealed record AiReportLine(string Text, ClaimState? State, bool Struck, bool Warning, bool Heading = false);

/// <summary>
/// The only way AI text reaches a report: through a validation. A case with AI output but no readable validation prints no AI statement, only the notice.
/// Every statement carries its state and a caveat or warning (VALORI_CONFIRMATE: only the cited values were confirmed, not the meaning); NESUSTINUT is struck with its reason and listed as "nu este constatare".
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
        var lines = new List<AiReportLine> { new(v.SummaryRomanian, v.AllValuesConfirmed ? ClaimState.ValoriConfirmate : ClaimState.Neverificabil, false, true, Heading: true) };
        lines.Add(new("Afirmațiile sunt ale unui model local, nu constatări ale aplicației. Validarea compară doar valorile citate (timp, cale, hash, adresă, număr, utilizator, gazdă, tehnică, port) cu elementele citate; sensul nu este verificat. Ele rămân nedovedite până când analistul verifică citările.", null, false, false));
        foreach (var c in v.Claims)
        {
            lines.Add(c.State switch
            {
                ClaimState.ValoriConfirmate => new AiReportLine($"[VALORI_CONFIRMATE] {c.Text} (citări: {string.Join(", ", c.Cites)}) — {ClaimStates.Caveat}", c.State, false, true),
                ClaimState.Nesustinut => new AiReportLine($"[NESUSTINUT — nu este constatare] {c.Text} — motiv: {c.Reason}", c.State, true, true),
                _ => new AiReportLine($"[{c.Label} — avertisment] {c.Text} — {c.Reason}", c.State, false, true),
            });
        }
        return lines;
    }
}
