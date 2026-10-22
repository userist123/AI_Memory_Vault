using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.AI;

/// <summary>
/// SUSTINUT: every cited item exists in THIS case and contains every checkable fact the claim states. PARTIAL: citations exist and nothing contradicts them,
/// but the claim states nothing the validator can compare (only the citation is confirmed). NESUSTINUT: no citation, a citation to a missing / foreign /
/// self-referential item, a fact that does not match, or instruction-like text. NEVERIFICABIL: the validator could not run (the cause is given) or the
/// validation is stale. Only SUSTINUT may be shown without a warning; NESUSTINUT is never a finding.
/// </summary>
[JsonConverter(typeof(ClaimStateConverter))]
public enum ClaimState { Sustinut, Partial, Nesustinut, Neverificabil }

public static class ClaimStates
{
    public static string ToSpec(this ClaimState s) => s switch
    {
        ClaimState.Sustinut => "SUSTINUT", ClaimState.Partial => "PARTIAL", ClaimState.Nesustinut => "NESUSTINUT", _ => "NEVERIFICABIL",
    };

    public static ClaimState Parse(string? s) => s?.Trim().ToUpperInvariant() switch
    {
        "SUSTINUT" => ClaimState.Sustinut, "PARTIAL" => ClaimState.Partial, "NESUSTINUT" => ClaimState.Nesustinut, _ => ClaimState.Neverificabil,
    };
}

public sealed class ClaimStateConverter : JsonConverter<ClaimState>
{
    public override ClaimState Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) => ClaimStates.Parse(reader.GetString());
    public override void Write(Utf8JsonWriter writer, ClaimState v, JsonSerializerOptions o) => writer.WriteStringValue(v.ToSpec());
}

/// <summary>One statement of an AI answer with the case ids it cites.</summary>
public sealed record AiClaimInput(string Section, string Text, IReadOnlyList<string> Cites);

/// <summary>One fact found in a claim and whether the cited items contain it.</summary>
public sealed record ClaimFactCheck(string Kind, string Value, bool Matched, string Note = "");

/// <summary>The verdict on one claim. <see cref="Reason"/> is always filled for anything but SUSTINUT.</summary>
public sealed record ValidatedClaim(string Section, string Text, IReadOnlyList<string> Cites, ClaimState State, string Reason, IReadOnlyList<ClaimFactCheck> Facts)
{
    /// <summary>Only a SUSTINUT claim may be presented as a statement of the analysis; the others carry a warning or are struck.</summary>
    [JsonIgnore] public bool NeedsWarning => State != ClaimState.Sustinut;
    /// <summary>NESUSTINUT is shown struck through with its reason and is never a finding.</summary>
    [JsonIgnore] public bool IsStruck => State == ClaimState.Nesustinut;
    [JsonIgnore] public string Label => State.ToSpec();
    /// <summary>The state at validation time when a later evidence change made the validation stale (null otherwise).</summary>
    public ClaimState? StateAtValidation { get; init; }
}

/// <summary>The case-level validation of one AI answer (Analysis/ai_validation.json).</summary>
public sealed class AiValidationResult
{
    public const string Schema = "ai-claim-validation/1";
    public const string ValidatorId = "LogAnalyzer.Dfir.Core/AiClaimValidator/1";
    public string SchemaVersion { get; init; } = Schema;
    public string Validator { get; init; } = ValidatorId;
    public string CaseId { get; init; } = "";
    public DateTimeOffset ValidatedUtc { get; init; }
    /// <summary>SHA-256 of the claims as the model gave them (section, text, cites): the AI text this validation belongs to.</summary>
    public string TextSha256 { get; init; } = "";
    /// <summary>SHA-256 of the case's evidence set at validation time (ids, hashes, sizes). A later change makes the validation stale.</summary>
    public string EvidenceBindingSha256 { get; init; } = "";
    public bool Stale { get; init; }
    public string StaleReason { get; init; } = "";
    public List<ValidatedClaim> Claims { get; init; } = [];
    public string Method { get; init; } = MethodText;

    public const string MethodText =
        "Verificare deterministă față de cazul curent: citările trebuie să existe în acest caz, iar valorile citate (timp, cale, hash, adresă, număr) să apară exact " +
        "în elementele citate; numerele de tip „N evenimente / constatări / probe” sunt recalculate din caz. Fără model folosit ca judecător.";

    public Dictionary<string, int> Counts => Enum.GetValues<ClaimState>().ToDictionary(s => s.ToSpec(), s => Claims.Count(c => c.State == s));
    [JsonIgnore] public int Sustained => Claims.Count(c => c.State == ClaimState.Sustinut);
    [JsonIgnore] public bool AllSustained => Claims.Count > 0 && Claims.All(c => c.State == ClaimState.Sustinut) && !Stale;

    public string SummaryRomanian =>
        $"Validarea afirmațiilor AI față de caz: {Claims.Count} afirmații — {Sustained} SUSTINUT, {Claims.Count(c => c.State == ClaimState.Partial)} PARTIAL, " +
        $"{Claims.Count(c => c.State == ClaimState.Nesustinut)} NESUSTINUT, {Claims.Count(c => c.State == ClaimState.Neverificabil)} NEVERIFICABIL" +
        (Stale ? $". VALIDARE ÎNVECHITĂ: {StaleReason}" : "") + ".";

    /// <summary>
    /// The validation as it stands against the case NOW: when the evidence set differs from the one it was bound to (or the binding cannot be
    /// recomputed) it is marked stale and every claim becomes NEVERIFICABIL (the earlier state is kept in <see cref="ValidatedClaim.StateAtValidation"/>).
    /// </summary>
    public AiValidationResult CheckAgainst(IAiCaseFacts current)
    {
        string now, why = "";
        try { now = current.EvidenceBindingSha256; }
        catch (Exception ex) { now = ""; why = $"legătura cu probele nu a putut fi recalculată ({ex.GetType().Name})"; }
        if (why.Length == 0 && !string.Equals(CaseId, current.CaseId, StringComparison.Ordinal)) why = $"validarea aparține cazului {CaseId}, nu cazului {current.CaseId}";
        else if (why.Length == 0 && !string.Equals(now, EvidenceBindingSha256, StringComparison.OrdinalIgnoreCase)) why = "probele cazului s-au schimbat după validare";
        if (why.Length == 0) return this;
        return new AiValidationResult
        {
            CaseId = CaseId, ValidatedUtc = ValidatedUtc, TextSha256 = TextSha256, EvidenceBindingSha256 = EvidenceBindingSha256, Stale = true, StaleReason = why,
            Claims = Claims.Select(c => c with { State = ClaimState.Neverificabil, Reason = "validare învechită: " + why, StateAtValidation = c.StateAtValidation ?? c.State }).ToList(),
        };
    }
}

/// <summary>
/// Case-level validation of AI statements (R22.5). Deterministic, no model, no network: an AI statement is split into claims; each claim must cite items
/// that exist in THIS case, and the facts it states (quoted values, timestamps, paths, hashes, addresses, file names, counts) must appear exactly in the
/// cited items. Counts are recomputed from the case. Text inside evidence is only ever compared as a string, never interpreted.
/// </summary>
public static class AiClaimValidator
{
    // ---------- claim splitting ----------

    private static readonly Regex InlineCite = new(@"\[(?<id>[A-Za-z0-9_][A-Za-z0-9_\-#.:/ ]{0,120}?)\]", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s+(?=[\p{Lu}\d„""«(])", RegexOptions.Compiled);

    /// <summary>Splits an answer into sentences; a sentence with inline [ID] citations uses them, the others inherit <paramref name="inherited"/>.</summary>
    public static List<AiClaimInput> Split(string section, string text, IReadOnlyList<string> inherited)
    {
        var claims = new List<AiClaimInput>();
        foreach (var raw in SentenceEnd.Split(text ?? "").Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            var inline = InlineCite.Matches(raw).Select(m => m.Groups["id"].Value.Trim()).Where(i => i.Length > 0).ToList();
            var clean = InlineCite.Replace(raw, "").Trim();
            if (clean.Length == 0) continue;
            claims.Add(new AiClaimInput(section, clean, inline.Count > 0 ? inline.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : inherited));
        }
        return claims;
    }

    /// <summary>Free-form AI text (no structure): every sentence is a claim and cites only through inline [ID]. No citation, no support.</summary>
    public static List<AiClaimInput> FromFreeText(string section, string text) => Split(section, text, []);

    public static string TextHash(IEnumerable<AiClaimInput> claims) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", claims.Select(c => $"{c.Section}\t{c.Text}\t{string.Join(",", c.Cites)}")))));

    // ---------- validation ----------

    public static AiValidationResult Validate(IReadOnlyList<AiClaimInput> inputs, IAiCaseFacts facts, DateTimeOffset? now = null, IReadOnlyList<ValidatedClaim>? refused = null)
    {
        refused ??= [];
        var allForHash = inputs.Concat(refused.Select(x => new AiClaimInput(x.Section, x.Text, x.Cites))).ToList();
        string binding;
        string caseId = "";
        try { caseId = facts.CaseId; binding = facts.EvidenceBindingSha256; }
        catch (Exception ex)
        {
            return new AiValidationResult
            {
                CaseId = caseId, ValidatedUtc = now ?? DateTimeOffset.UtcNow, TextSha256 = TextHash(allForHash),
                Claims = inputs.Select(c => Make(c, ClaimState.Neverificabil, $"validatorul nu a putut rula: {ex.GetType().Name}: {ex.Message}", [])).Concat(refused).ToList(),
            };
        }
        var claims = new List<ValidatedClaim>();
        foreach (var c in inputs)
        {
            try { claims.Add(ValidateOne(c, facts)); }
            catch (Exception ex) { claims.Add(Make(c, ClaimState.Neverificabil, $"validatorul nu a putut rula: {ex.GetType().Name}: {ex.Message}", [])); }
        }
        claims.AddRange(refused);
        return new AiValidationResult { CaseId = caseId, ValidatedUtc = now ?? DateTimeOffset.UtcNow, TextSha256 = TextHash(allForHash), EvidenceBindingSha256 = binding, Claims = claims };
    }

    /// <summary>A claim the earlier structural checks (EvidenceReasoner) already refused: kept, struck, with that reason.</summary>
    public static ValidatedClaim Refused(string section, string text, IReadOnlyList<string> cites, string reason) =>
        new(section, text, cites, ClaimState.Nesustinut, reason, []);

    private static ValidatedClaim Make(AiClaimInput c, ClaimState s, string reason, IReadOnlyList<ClaimFactCheck> facts) =>
        new(c.Section, c.Text, c.Cites, s, reason, facts);

    private static readonly Regex SelfReference = new(@"^(ai[-_:#]|ai$)|ai_(reasoning|raw_response|catalog|validation)|Analysis[/\\]ai_|^(S|AI|MODEL|ANS|RASPUNS)[-_]?\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Injection = new(
        @"ignor\w*\s+(all\s+|toate\s+)?(the\s+)?(previous|prior|above|earlier|anterioare|precedente)|ignor\w*\s+.{0,20}(instruc|prompt)|disregard\s+.{0,30}(instruction|rule|above)|" +
        @"mark\s+(all|everything|the\s+case)\s+.{0,20}(clean|safe|benign|harmless)|marchează\s+.{0,30}(curat|inofensiv|benign|sigur)|" +
        @"(system|developer)\s+prompt|you\s+are\s+now\b|\bact\s+as\b|do\s+not\s+report|nu\s+raporta|override\s+(the\s+)?(rules|instructions|policy)|new\s+instructions?|instruc\w+\s+noi",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static ValidatedClaim ValidateOne(AiClaimInput c, IAiCaseFacts facts)
    {
        if (string.IsNullOrWhiteSpace(c.Text)) return Make(c, ClaimState.Nesustinut, "afirmație goală", []);
        if (Injection.IsMatch(c.Text))
            return Make(c, ClaimState.Nesustinut, "conține text de tip instrucțiune (posibilă injecție din probe sau din model); nu este tratat ca instrucțiune și nu este o constatare", []);
        var cites = c.Cites.Select(x => x?.Trim().Trim('[', ']') ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cites.Count == 0) return Make(c, ClaimState.Nesustinut, "nu citează nicio probă din caz", []);

        var cited = new List<AiCaseRecord>();
        var problems = new List<string>();
        foreach (var id in cites)
        {
            if (SelfReference.IsMatch(id)) { problems.Add($"{id}: citează răspunsul AI însuși, nu o probă"); continue; }
            var rec = facts.Find(id);
            if (rec is null) { problems.Add($"{id}: nu există în acest caz (inexistent sau al altui caz)"); continue; }
            if (rec.CaseId.Length > 0 && !string.Equals(rec.CaseId, facts.CaseId, StringComparison.Ordinal)) { problems.Add($"{id}: aparține altui caz ({rec.CaseId})"); continue; }
            cited.Add(rec);
        }
        if (problems.Count > 0) return Make(c, ClaimState.Nesustinut, "citare nevalidă — " + string.Join("; ", problems), []);

        var checks = ClaimFacts.Check(c.Text, cited);
        var bad = checks.Where(f => !f.Matched).ToList();
        if (bad.Count > 0)
            return Make(c, ClaimState.Nesustinut, "valori care nu apar exact în elementele citate — " + string.Join("; ", bad.Select(b => $"{b.Kind} „{b.Value}”" + (b.Note.Length > 0 ? " (" + b.Note + ")" : ""))), checks);
        if (checks.Count == 0)
            return Make(c, ClaimState.Partial, "citările există în caz, dar afirmația nu conține valori pe care validatorul să le poată compara determinist (timp, cale, hash, adresă, număr, valoare între ghilimele)", checks);
        return Make(c, ClaimState.Sustinut, "", checks);
    }
}

/// <summary>Extraction and exact comparison of the checkable facts of one claim.</summary>
internal static class ClaimFacts
{
    private static readonly Regex Timestamp = new(@"\b(?<d>\d{4}-\d{2}-\d{2})[T ](?<h>\d{2}):(?<m>\d{2})(?::(?<s>\d{2})(?:\.(?<f>\d{1,7}))?)?(?<z>Z|[+-]\d{2}:?\d{2})?(?![\d])", RegexOptions.Compiled);
    private static readonly Regex DateOnly = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex Hash = new(@"\b[0-9a-fA-F]{12,128}\b", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d]|\.\d)", RegexOptions.Compiled);
    private static readonly Regex WinPath = new(@"(?<![\w])[A-Za-z]:[\\/][^\s""'<>|*?„”“«»`]*[^\s""'<>|*?„”“«»`.,;:!)\]]|\\\\[\w.$-]+\\[^\s""'<>|*?„”“«»`]*[^\s""'<>|*?„”“«»`.,;:!)\]]", RegexOptions.Compiled);
    private static readonly Regex Quoted = new(@"„(?<a>[^”""]{1,300})[”""]|""(?<b>[^""]{1,300})""|“(?<c>[^”]{1,300})”|«(?<d>[^»]{1,300})»|`(?<e>[^`]{1,300})`", RegexOptions.Compiled);
    private static readonly Regex FileName = new(@"\b[\w\-.]+\.(?:exe|dll|sys|ps1|bat|cmd|vbs|js|hta|msi|zip|rar|7z|lnk|evtx|pf|scr|tmp|dat|log|txt|docx?|xlsx?|pdf|iso|vhdx?|jar|py|sh)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Domain = new(@"\b(?:[a-z0-9-]+\.)+(?:com|net|org|ro|io|info|biz|ru|cn|xyz|top|cyou|site|online|app|dev|co|uk|de|eu)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Count = new(
        @"(?<n>\d{1,3}(?:[.,]\d{3})+|\d+)\s+(?<w>evenimente|eveniment|înregistrări|înregistrare|ocurențe|apariții|events?|records?|constatări|constatare|findings?|probe|probă|elemente\s+de\s+probă|element\s+de\s+probă|fișiere\s+de\s+probă|evidence\s+items?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BareNumber = new(@"(?<![\w.,\-])(?:\d{1,3}(?:[.,]\d{3})+(?:[.,]\d+)?|\d+[.,]\d+|\d{3,})(?![\w]|[.,]\d)", RegexOptions.Compiled);

    public static List<ClaimFactCheck> Check(string text, IReadOnlyList<AiCaseRecord> cited)
    {
        var checks = new List<ClaimFactCheck>();
        var rest = new StringBuilder(text);
        void Blank(Match m) { for (int i = m.Index; i < m.Index + m.Length; i++) rest[i] = ' '; }
        bool InAny(string token, bool norm = false) => cited.Any(r => ContainsBounded(norm ? r.BlobNormalized : r.Blob, norm ? token.Replace('/', '\\') : token));

        // quoted values first (a quoted path or name is one value)
        foreach (Match m in Quoted.Matches(text))
        {
            var v = (m.Groups["a"].Success ? m.Groups["a"] : m.Groups["b"].Success ? m.Groups["b"] : m.Groups["c"].Success ? m.Groups["c"] : m.Groups["d"].Success ? m.Groups["d"] : m.Groups["e"]).Value.Trim();
            if (v.Length == 0) continue;
            checks.Add(new ClaimFactCheck("valoare citată", v, InAny(v, norm: true)));
            Blank(m);
        }
        // timestamps
        foreach (Match m in Timestamp.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("timp", m.Value, TimeMatches(m, cited) || InAny(m.Value)));
            Blank(m);
        }
        foreach (Match m in DateOnly.Matches(rest.ToString()))
        {
            bool ok = cited.Any(r => r.Times.Any(t => t.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == m.Value)) || InAny(m.Value);
            checks.Add(new ClaimFactCheck("dată", m.Value, ok));
            Blank(m);
        }
        foreach (Match m in WinPath.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("cale", m.Value, InAny(m.Value, norm: true)));
            Blank(m);
        }
        foreach (Match m in Hash.Matches(rest.ToString()))
        {
            var v = m.Value;
            bool looksHash = v.Length is 32 or 40 or 64 or 128 || v.Any(char.IsLetter) && v.Any(char.IsDigit);
            if (!looksHash) continue;
            bool ok = cited.Any(r => r.Hashes.Any(h => h.StartsWith(v, StringComparison.OrdinalIgnoreCase))) || InAny(v);
            checks.Add(new ClaimFactCheck("hash", v, ok));
            Blank(m);
        }
        foreach (Match m in Ipv4.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("adresă IP", m.Value, InAny(m.Value)));
            Blank(m);
        }
        foreach (Match m in FileName.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("nume de fișier", m.Value, InAny(m.Value)));
            Blank(m);
        }
        foreach (Match m in Domain.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("domeniu", m.Value, InAny(m.Value)));
            Blank(m);
        }
        foreach (Match m in Count.Matches(rest.ToString()))
        {
            var n = ParseInt(m.Groups["n"].Value);
            var cls = NounClass(m.Groups["w"].Value);
            var allowed = RecomputedCounts(cls, cited);
            bool ok = n is { } v && allowed.Contains(v);
            checks.Add(new ClaimFactCheck("număr", m.Value, ok, ok ? "" : $"în caz, pentru elementele citate: {string.Join(" sau ", allowed.OrderBy(x => x))}"));
            Blank(m);
        }
        foreach (Match m in BareNumber.Matches(rest.ToString()))
        {
            var plain = new string(m.Value.Where(char.IsDigit).ToArray());
            bool ok = InAny(m.Value) || InAny(plain) || (plain.Length > 0 && cited.Any(r => ContainsBounded(r.Blob, plain)));
            checks.Add(new ClaimFactCheck("număr", m.Value, ok));
        }
        return checks;
    }

    private static long? ParseInt(string s)
    {
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string NounClass(string w)
    {
        w = Regex.Replace(w.ToLowerInvariant(), @"\s+", " ");
        if (w.StartsWith("constat") || w.StartsWith("finding")) return "finding";
        if (w.StartsWith("prob") || w.Contains("de probă") || w.StartsWith("evidence")) return "evidence";
        return "event";
    }

    /// <summary>The numbers a count of that kind may legitimately have for the cited items, recomputed from the case records.</summary>
    private static HashSet<long> RecomputedCounts(string cls, IReadOnlyList<AiCaseRecord> cited)
    {
        var set = new HashSet<long>();
        switch (cls)
        {
            case "event": set.Add(cited.SelectMany(r => r.EventKeys).Distinct(StringComparer.Ordinal).Count()); break;
            case "finding": set.Add(cited.SelectMany(r => r.FindingIds).Distinct(StringComparer.Ordinal).Count()); break;
            default:
                set.Add(cited.SelectMany(r => r.EvidenceIds).Distinct(StringComparer.Ordinal).Count());
                set.Add(cited.Sum(r => (long)r.EvidenceRefCount));
                break;
        }
        return set;
    }

    private static bool TimeMatches(Match m, IReadOnlyList<AiCaseRecord> cited)
    {
        var z = m.Groups["z"].Value;
        var s = m.Groups["s"].Success ? m.Groups["s"].Value : "00";
        var frac = m.Groups["f"].Success ? m.Groups["f"].Value : "";
        var iso = $"{m.Groups["d"].Value}T{m.Groups["h"].Value}:{m.Groups["m"].Value}:{s}" + (frac.Length > 0 ? "." + frac : "") + (z.Length == 0 ? "Z" : z.Length == 5 && z[3] != ':' ? z[..3] + ":" + z[3..] : z);
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) return false;
        // Precision of the claim: minutes, seconds or n fractional digits. The record must have the same instant at that precision.
        long ticks = t.UtcTicks;
        long unit = !m.Groups["s"].Success ? TimeSpan.TicksPerMinute : frac.Length == 0 ? TimeSpan.TicksPerSecond : (long)Math.Pow(10, 7 - frac.Length);
        return cited.Any(r => r.Times.Any(rt => rt.UtcTicks / unit == ticks / unit));
    }

    /// <summary>Ordinal, case-insensitive containment; a token that starts or ends with a word character must not continue into one (a prefix of a path or a longer number is not a match).</summary>
    internal static bool ContainsBounded(string blob, string token)
    {
        if (token.Length == 0) return false;
        int from = 0;
        while (from <= blob.Length - token.Length)
        {
            int i = blob.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            bool leftOk = !IsWord(token[0]) || i == 0 || !(IsWord(blob[i - 1]) || ((token[0] is >= '0' and <= '9') && blob[i - 1] is '.' or ',' && i >= 2 && char.IsDigit(blob[i - 2])));
            int end = i + token.Length;
            bool rightOk = !IsWord(token[^1]) || end == blob.Length || !(IsWord(blob[end]) || blob[end] == '\\' ||
                           ((blob[end] is '.' or ',') && end + 1 < blob.Length && char.IsDigit(blob[end + 1]) && char.IsDigit(token[^1])) ||
                           (blob[end] == '.' && end + 1 < blob.Length && char.IsLetterOrDigit(blob[end + 1])));
            if (leftOk && rightOk) return true;
            from = i + 1;
        }
        return false;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}
