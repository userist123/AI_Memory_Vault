using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.AI;

/// <summary>
/// VALORI_CONFIRMATE: every cited item exists in THIS case and the cited record(s) contain every checkable VALUE the claim states. It never confirms the
/// MEANING of the claim (a deterministic check compares values, not sense) and is never shown without <see cref="ClaimStates.Caveat"/>. PARTIAL: citations
/// exist and nothing contradicts them, but the claim states nothing comparable, cites only an evidence file (not an event or finding), or contains a
/// negation or an overall verdict whose sense cannot be checked. NESUSTINUT: no citation, a citation to a missing / foreign / self-referential item, a
/// value that does not match, or instruction-like text. NEVERIFICABIL: the validator could not run (the cause is given) or the validation is stale or
/// was modified. NESUSTINUT is never a finding.
/// </summary>
[JsonConverter(typeof(ClaimStateConverter))]
public enum ClaimState { ValoriConfirmate, Partial, Nesustinut, Neverificabil }

public static class ClaimStates
{
    /// <summary>Shown next to every state, in particular next to the top one: what was and was not checked.</summary>
    public const string Caveat = "afirmație a modelului, nu constatare; s-au confirmat doar valorile citate, nu sensul";

    public static string ToSpec(this ClaimState s) => s switch
    {
        ClaimState.ValoriConfirmate => "VALORI_CONFIRMATE", ClaimState.Partial => "PARTIAL", ClaimState.Nesustinut => "NESUSTINUT", _ => "NEVERIFICABIL",
    };

    /// <summary>The wording for screens: the top state reads "valori confirmate", the others keep their spec word.</summary>
    public static string ToDisplay(this ClaimState s) => s == ClaimState.ValoriConfirmate ? "valori confirmate" : s.ToSpec();

    public static ClaimState Parse(string? s) => s?.Trim().ToUpperInvariant() switch
    {
        "VALORI_CONFIRMATE" => ClaimState.ValoriConfirmate, "PARTIAL" => ClaimState.Partial, "NESUSTINUT" => ClaimState.Nesustinut, _ => ClaimState.Neverificabil,
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

/// <summary>The verdict on one claim. <see cref="Reason"/> is always filled for anything but VALORI_CONFIRMATE (which carries <see cref="ClaimStates.Caveat"/> instead).</summary>
public sealed record ValidatedClaim(string Section, string Text, IReadOnlyList<string> Cites, ClaimState State, string Reason, IReadOnlyList<ClaimFactCheck> Facts)
{
    /// <summary>Every state carries a caveat or warning: even VALORI_CONFIRMATE is a model statement whose values were compared, not a finding.</summary>
    [JsonIgnore] public bool NeedsWarning => true;
    /// <summary>The text shown next to the state: the reason, or for VALORI_CONFIRMATE the caveat.</summary>
    [JsonIgnore] public string Notice => State == ClaimState.ValoriConfirmate ? ClaimStates.Caveat : Reason;
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
        "Verificare deterministă față de cazul curent: citările trebuie să existe în acest caz, iar valorile citate (timp, cale, hash, adresă, număr, utilizator, gazdă, " +
        "tehnică MITRE, port) să apară exact în evenimentul sau constatarea citată (o citare doar la fișier confirmă cel mult parțial); numerele de tip " +
        "„N evenimente / constatări / probe” sunt recalculate din caz. Se confirmă VALORI, niciodată sensul; negațiile și concluziile globale nu pot fi confirmate. " +
        "Fără model folosit ca judecător.";

    public Dictionary<string, int> Counts => Enum.GetValues<ClaimState>().ToDictionary(s => s.ToSpec(), s => Claims.Count(c => c.State == s));
    [JsonIgnore] public int ValuesConfirmed => Claims.Count(c => c.State == ClaimState.ValoriConfirmate);
    [JsonIgnore] public bool AllValuesConfirmed => Claims.Count > 0 && Claims.All(c => c.State == ClaimState.ValoriConfirmate) && !Stale;

    public string SummaryRomanian =>
        $"Validarea afirmațiilor AI față de caz: {Claims.Count} afirmații — {ValuesConfirmed} VALORI_CONFIRMATE (valori citate confirmate, nu sensul), {Claims.Count(c => c.State == ClaimState.Partial)} PARTIAL, " +
        $"{Claims.Count(c => c.State == ClaimState.Nesustinut)} NESUSTINUT, {Claims.Count(c => c.State == ClaimState.Neverificabil)} NEVERIFICABIL" +
        (Stale ? $". VALIDARE ÎNVECHITĂ: {StaleReason}" : "") + ".";

    /// <summary>
    /// The validation as it stands against the case NOW: when the evidence set differs from the one it was bound to (or the binding cannot be
    /// recomputed) it is marked stale and every claim becomes NEVERIFICABIL (the earlier state is kept in <see cref="ValidatedClaim.StateAtValidation"/>).
    /// </summary>
    public AiValidationResult CheckAgainst(IAiCaseFacts current)
    {
        if (Stale) return this;   // already stale (or modified): keep the first cause
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

    // Imperative instructions aimed at the assistant. "act as" alone is ordinary language ("can act as a host"); only "act as" + an assistant/role word counts.
    private static readonly Regex Injection = new(
        @"ignor\w*\s+(all\s+|toate\s+)?(the\s+)?(previous|prior|above|earlier|anterioare|precedente)|ignor\w*\s+.{0,20}(instruc|prompt)|disregard\s+.{0,30}(instruction|rule|above)|" +
        @"mark\s+(all|everything|the\s+case)\s+.{0,20}(clean|safe|benign|harmless)|marchează\s+.{0,30}(curat|inofensiv|benign|sigur)|" +
        @"(system|developer)\s+prompt|you\s+are\s+now\b|\bact\s+as\s+(an?\s+|the\s+)?(assistant|ai\b|model|llm|chatbot|admin|administrator|root|jailbr|unrestricted|dan\b)|" +
        @"do\s+not\s+report|nu\s+raporta|override\s+(the\s+)?(rules|instructions|policy)|new\s+instructions?|instruc\w+\s+noi",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A negation or an overall verdict changes the MEANING of a claim; the validator compares values only, so such a claim cannot reach VALORI_CONFIRMATE.
    private static readonly Regex NegationOrVerdict = new(
        @"(?<![\p{L}\p{N}_])(nu|niciodat[aă]|f[aă]r[aă]|nimic|nici|nicio|nicun[a]?|not|never|no|none|without|cannot|neither|nor|" +
        @"benign\w*|curat\w*|sigur\w*|compromis\w*|compromised|clean|malicious|mali[tț]io\w*|safe|harmless|inofensiv\w*)(?![\p{L}\p{N}_])|n['’]t\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal const string NegationReason = "conține o negație sau o concluzie: sensul nu poate fi verificat automat";
    internal const string EvidenceOnlyReason = "citează doar fișierul, nu evenimentul sau constatarea: valorile nu sunt legate de un singur înregistrare";
    internal const string InjectionGeneric = "conține text de tip instrucțiune (posibilă injecție din probe sau din model); nu este tratat ca instrucțiune și nu este o constatare";
    internal const string InjectionQuoted = "citatul există în probă, dar conține text de tip instrucțiune; afișat ca citat, nu ca afirmație confirmată";

    private static string InjectionReason(AiClaimInput c, IReadOnlyList<string> cites, IAiCaseFacts facts)
    {
        var recs = new List<AiCaseRecord>();
        foreach (var id in cites)
        {
            if (SelfReference.IsMatch(id)) continue;
            var r = facts.Find(id);
            if (r is not null && (r.CaseId.Length == 0 || string.Equals(r.CaseId, facts.CaseId, StringComparison.Ordinal))) recs.Add(r);
        }
        if (recs.Count == 0) return InjectionGeneric;
        var candidates = ClaimFacts.QuotedValues(c.Text).Where(q => Injection.IsMatch(q)).ToList();
        if (candidates.Count == 0) candidates = Injection.Matches(c.Text).Select(m => m.Value.Trim()).ToList();
        bool present = candidates.Any(q => q.Length > 0 && recs.Any(r => r.Blob.Contains(q, StringComparison.OrdinalIgnoreCase)));
        return present ? InjectionQuoted : InjectionGeneric;
    }

    private static ValidatedClaim ValidateOne(AiClaimInput c, IAiCaseFacts facts)
    {
        if (string.IsNullOrWhiteSpace(c.Text)) return Make(c, ClaimState.Nesustinut, "afirmație goală", []);
        var cites = c.Cites.Select(x => x?.Trim().Trim('[', ']') ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Injection.IsMatch(c.Text)) return Make(c, ClaimState.Nesustinut, InjectionReason(c, cites, facts), []);
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

        // Values are compared against the specific cited records (events, findings, ...). An evidence FILE holds every event of that file, so a value found "somewhere in
        // the file" says nothing about the event the claim talks about: such a claim is compared against the file but can reach PARTIAL at most.
        var specific = cited.Where(r => r.Kind != "evidence").ToList();
        var basis = specific.Count > 0 ? specific : cited;
        var checks = ClaimFacts.Check(c.Text, basis);
        var bad = checks.Where(f => !f.Matched).ToList();
        if (bad.Count > 0)
            return Make(c, ClaimState.Nesustinut, "valori care nu apar exact în elementele citate — " + string.Join("; ", bad.Select(b => $"{b.Kind} „{b.Value}”" + (b.Note.Length > 0 ? " (" + b.Note + ")" : ""))), checks);
        if (checks.Count == 0)
            return Make(c, ClaimState.Partial, "citările există în caz, dar afirmația nu conține valori pe care validatorul să le poată compara determinist (timp, cale, hash, adresă, număr, valoare între ghilimele)", checks);
        var caps = new List<string>();
        if (specific.Count == 0) caps.Add(EvidenceOnlyReason);
        if (NegationOrVerdict.IsMatch(c.Text)) caps.Add(NegationReason);
        if (caps.Count > 0) return Make(c, ClaimState.Partial, string.Join("; ", caps), checks);
        return Make(c, ClaimState.ValoriConfirmate, "", checks);
    }
}

/// <summary>Extraction and exact comparison of the checkable facts of one claim.</summary>
internal static class ClaimFacts
{
    private static readonly Regex Timestamp = new(@"\b(?<d>\d{4}-\d{2}-\d{2})[T ](?<h>\d{2}):(?<m>\d{2})(?::(?<s>\d{2})(?:\.(?<f>\d{1,7}))?)?(?<z>Z|[+-]\d{2}:?\d{2})?(?![\d])", RegexOptions.Compiled);
    private static readonly Regex DateOnly = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex Hash = new(@"\b[0-9a-fA-F]{12,128}\b", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"(?<![\d.])(?<ip>(?:\d{1,3}\.){3}\d{1,3})(?![\d]|\.\d)(?::(?<port>\d{1,5})(?![\d\w]))?", RegexOptions.Compiled);
    private static readonly Regex EmailLike = new(@"(?<![\w.\-])[\w.\-]{1,64}@[A-Za-z0-9][A-Za-z0-9.\-]{0,100}[A-Za-z0-9](?![\w@])", RegexOptions.Compiled);
    private static readonly Regex DomainUser = new(@"(?<![\w\\])(?<d>[A-Za-z][\w\-]{0,30})\\(?<u>[A-Za-z0-9][\w.$\-]{0,40})(?![\w\\])", RegexOptions.Compiled);
    private static readonly Regex Mitre = new(@"(?<![\w.])T\d{4}(?:\.\d{3})?(?![\w]|\.\d)", RegexOptions.Compiled);
    private static readonly Regex Fqdn = new(@"(?<![\w.\-])(?=[A-Za-z0-9\-]*[A-Za-z])[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+\.[A-Za-z]{2,}(?![\w\-])", RegexOptions.Compiled);
    private static readonly Regex UserKeyword = new(@"(?<![\p{L}\d])(?:utilizatorul|utilizatorului|utilizator|username|user|contul|account)\s*[:=]?\s*(?<v>[\p{L}\d][\w.$\-]{0,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HostKeyword = new(@"(?<![\p{L}\d])(?:hostul|host|hostname|stația|statia|calculatorul|computerul|computer|serverul|server|mașina|masina|machine|workstation)\s*[:=]?\s*(?<v>[A-Za-z0-9][\w\-]{1,62}(?:\.[A-Za-z0-9][\w\-]*)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PortKeyword = new(@"(?<![\p{L}\d])port(?:ul|uri|urile|s)?\s*[:=]?\s*(?<p>\d{1,5})(?![\d\w])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Times = new(
        @"(?<n>\d{1,3}(?:[.,]\d{3})+|\d+)\s+(?:de\s+)?(?<w>ori|times|sesiuni|sesiune|conexiuni|conexiune|logări|logare|autentificări|autentificare|încercări|încercare|accesări|accesare|procese|proces|fișiere|fișier|utilizatori|attempts?|connections?|sessions?|logons?|logins?|processes|files|users|hosts?|gazde|hosturi)(?![\p{L}\d])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Words that follow "user" / "host" in ordinary prose and are not names (verbs, articles, prepositions); best effort, a name that is one of them is not compared.</summary>
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "au", "ai", "am", "are", "as", "este", "sunt", "era", "fost", "fi", "va", "să", "sa", "se", "si", "și", "sau", "dar", "nu", "cu", "de", "din", "în", "in", "la", "pe", "prin", "pentru", "către",
        "care", "ce", "un", "o", "unui", "unei", "the", "is", "was", "were", "be", "been", "has", "had", "have", "did", "does", "do", "can", "could", "will", "would", "may", "might", "must", "should",
        "and", "or", "on", "of", "to", "for", "from", "by", "with", "at", "in", "into", "that", "who", "which", "also", "then", "not", "never", "this", "these", "those", "its", "their",
        "s-a", "și-a", "ul", "ului", "lui", "lor", "respectiv", "însuși", "insusi", "local", "remote", "name", "nume", "numele", "id", "ip", "account", "cont", "host", "user", "server", "computer", "machine",
    };

    private static bool LooksLikeNoun(string v) =>
        !NotNames.Contains(v) && !(v.Length > 4 && (v.EndsWith("ed", StringComparison.OrdinalIgnoreCase) || v.EndsWith("ing", StringComparison.OrdinalIgnoreCase) ||
                                                       v.EndsWith("ează", StringComparison.OrdinalIgnoreCase) || v.EndsWith("ește", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The quoted values of a text (the same extraction the fact check uses).</summary>
    internal static List<string> QuotedValues(string text) =>
        Quoted.Matches(text).Select(m => (m.Groups["a"].Success ? m.Groups["a"] : m.Groups["b"].Success ? m.Groups["b"] : m.Groups["c"].Success ? m.Groups["c"] : m.Groups["d"].Success ? m.Groups["d"] : m.Groups["e"]).Value.Trim())
              .Where(v => v.Length > 0).ToList();
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
        void Blank(Group m) { for (int i = m.Index; i < m.Index + m.Length; i++) rest[i] = ' '; }
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
        foreach (Match m in EmailLike.Matches(rest.ToString()))
        {
            var at = m.Value.IndexOf('@');
            bool ok = InAny(m.Value) || InAny(m.Value[..at]) && InAny(m.Value[(at + 1)..]);
            checks.Add(new ClaimFactCheck("utilizator", m.Value, ok));
            Blank(m);
        }
        foreach (Match m in Ipv4.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("adresă IP", m.Groups["ip"].Value, InAny(m.Groups["ip"].Value)));
            if (m.Groups["port"].Success) checks.Add(new ClaimFactCheck("port", m.Groups["port"].Value, InAny(m.Groups["port"].Value)));
            Blank(m);
        }
        foreach (Match m in Mitre.Matches(rest.ToString()))
        {
            bool ok = InAny(m.Value) || !m.Value.Contains('.') && cited.Any(r => Regex.IsMatch(r.Blob, Regex.Escape(m.Value) + @"\.\d{3}(?![\w])", RegexOptions.IgnoreCase));   // a technique id matches its sub-technique
            checks.Add(new ClaimFactCheck("tehnică MITRE", m.Value, ok));
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
        foreach (Match m in Fqdn.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("gazdă", m.Value, InAny(m.Value)));
            Blank(m);
        }
        foreach (Match m in DomainUser.Matches(rest.ToString()))
        {
            bool ok = InAny(m.Value, norm: true) || InAny(m.Groups["d"].Value) && InAny(m.Groups["u"].Value);
            checks.Add(new ClaimFactCheck("utilizator", m.Value, ok));
            Blank(m);
        }
        foreach (Match m in PortKeyword.Matches(rest.ToString()))
        {
            checks.Add(new ClaimFactCheck("port", m.Groups["p"].Value, InAny(m.Groups["p"].Value)));
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
        // counts of things the case does not recompute ("de 50 de ori", "3 conexiuni"): the number itself must occur in the cited record
        foreach (Match m in Times.Matches(rest.ToString()))
        {
            var digits = new string(m.Groups["n"].Value.Where(char.IsDigit).ToArray());
            bool ok = InAny(m.Groups["n"].Value) || InAny(digits);
            checks.Add(new ClaimFactCheck("număr", m.Value, ok, ok ? "" : "numărul nu apare în elementul citat"));
            Blank(m);
        }
        foreach (Match m in UserKeyword.Matches(rest.ToString()))
        {
            var v = m.Groups["v"].Value.TrimEnd('.', '-', '$');
            if (v.Length < 2 || !LooksLikeNoun(v)) continue;
            checks.Add(new ClaimFactCheck("utilizator", v, InAny(v)));
            Blank(m.Groups["v"]);
        }
        foreach (Match m in HostKeyword.Matches(rest.ToString()))
        {
            var v = m.Groups["v"].Value.TrimEnd('.', '-');
            if (v.Length < 2 || !LooksLikeNoun(v)) continue;
            checks.Add(new ClaimFactCheck("gazdă", v, InAny(v)));
            Blank(m.Groups["v"]);
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
