using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>One group of findings of the same rule, severity, classification and reason (for example several vendor scheduled tasks); the lead is printed, the members are listed.</summary>
public sealed record FindingGroup(Finding Lead, IReadOnlyList<Finding> Members);

/// <summary>What the first page of the investigation report says (review 2026-10-12, A and D2): verdict, meaning for the reader, next steps, scope of the report.</summary>
public sealed record ReportSummary(
    string Verdict, IReadOnlyList<string> Serious, IReadOnlyList<string> NotChecked, string Meaning, IReadOnlyList<string> NextSteps,
    string CanProve, string CannotProve,
    int CriticalConfirmed, int CriticalIndicative, int HighConfirmed, int HighIndicative);

/// <summary>
/// The text decisions of the investigation report that are not layout: grouping of repeated errors and findings, which limitations
/// are general and which belong to one finding, the first-page summary, the legend of English codes. Pure functions over the result,
/// so they are tested without producing a PDF.
/// </summary>
public static class InvestigationReportText
{
    // ---- Repeated collector errors ----

    private static readonly Regex ChannelError = new(@"^(?<ch>[^:]{1,80}): (?<rest>.+)$", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>"A: x; B: x; C: y" → "x — 2 canale: A, B; C: y". Identical messages become one line with the list of channels.</summary>
    public static string GroupErrors(string errors)
    {
        if (string.IsNullOrWhiteSpace(errors)) return "";
        var order = new List<string>();
        var channels = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var part in errors.Split("; ", StringSplitOptions.RemoveEmptyEntries))
        {
            var m = ChannelError.Match(part);
            if (!m.Success) { if (!channels.ContainsKey("\0" + part)) { channels["\0" + part] = []; order.Add("\0" + part); } continue; }
            var rest = m.Groups["rest"].Value;
            if (!channels.TryGetValue(rest, out var l)) { channels[rest] = l = []; order.Add(rest); }
            l.Add(m.Groups["ch"].Value);
        }
        var lines = order.Select(k => k[0] == '\0' ? k[1..]
            : channels[k].Count == 1 ? $"{channels[k][0]}: {k}"
            : $"{k} — {RoText.Count(channels[k].Count, "canal", "canale")}: {string.Join(", ", channels[k])}");
        return string.Join("; ", lines);
    }

    // ---- Limitations: general once, specific per finding ----

    private static readonly string[] GenericPrefixes = ["Probe contradictorii: regula nu le evaluează", "Dovezi contradictorii: regula nu le evaluează", "Verificare independentă: neefectuată"];
    private static readonly Regex ParserLimit = new(@"^(?<p>.+?) nu (?:poate|pot) dovedi ", RegexOptions.Compiled);

    /// <summary>A limitation every finding of a kind carries (the semantic type's own limit, the unevaluated contradiction check, no independent verification).</summary>
    public static bool IsGenericLimitation(string limitation) =>
        GenericPrefixes.Any(p => limitation.StartsWith(p, StringComparison.Ordinal))
        || Enum.GetValues<SemanticType>().Any(t => FindingContract.SemanticLimitation(t) is { Length: > 0 } s && s == limitation);

    /// <summary>The user-activity part of the report (E7): counts, the strongest rows with the meaning of their time, the limits. Pure, so the tests read it without parsing the PDF.</summary>
    public static List<(string Text, bool Head)> UserActivityLines(InvestigationResult r) => UserActivityAnalysis.ReportLines(r.UserActivityView());

    /// <summary>The general limitations present in these findings, each once.</summary>
    public static IReadOnlyList<string> GenericLimitations(IEnumerable<Finding> findings) =>
        findings.SelectMany(f => f.Limitations).Where(IsGenericLimitation).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The limitations that belong to this finding: the rule's own and, among parser limits, only those of parsers that supplied this
    /// finding's evidence (at most <paramref name="maxParserLimits"/>; the rest is counted, not dropped silently).
    /// </summary>
    public static (List<string> Specific, int OmittedParserLimits) SpecificLimitations(Finding f, int maxParserLimits = 3)
    {
        var relevant = f.Provenance?.Parsers.Select(FindingContract.ParserLabel).ToHashSet(StringComparer.Ordinal);
        var specific = new List<string>();
        int parserShown = 0, omitted = 0;
        foreach (var l in f.Limitations.Where(l => l.Length > 0 && !IsGenericLimitation(l)).Distinct(StringComparer.Ordinal))
        {
            var m = ParserLimit.Match(l);
            if (!m.Success) { specific.Add(l); continue; }
            if (relevant is not null && !relevant.Contains(m.Groups["p"].Value)) { omitted++; continue; }
            if (parserShown++ < maxParserLimits) specific.Add(l); else omitted++;
        }
        return (specific, omitted);
    }

    // ---- Grouping findings of the same kind ----

    public static List<FindingGroup> GroupFindings(IEnumerable<Finding> findings)
    {
        var groups = new List<FindingGroup>();
        foreach (var g in findings.GroupBy(f => (f.RuleId, f.Severity, f.Classification, f.Category, Reason: f.ClassificationReason.Length > 0 ? f.ClassificationReason : f.Title)))
        {
            var ordered = g.ToList();
            if (g.Key.RuleId == "INCIDENT-CHAIN" || ordered.Count == 1) groups.AddRange(ordered.Select(f => new FindingGroup(f, [f])));
            else groups.Add(new FindingGroup(ordered[0], ordered));
        }
        return groups.OrderByDescending(g => g.Lead.Severity).ThenBy(g => g.Lead.FirstSeenUtc ?? g.Lead.LastSeenUtc).ThenBy(g => g.Lead.FindingId, StringComparer.Ordinal).ToList();
    }

    /// <summary>Evidence references of all members of a group, one per (evidence id, locator).</summary>
    public static List<EvidenceRef> GroupEvidence(FindingGroup g) =>
        g.Members.SelectMany(m => m.SupportingEvidence).GroupBy(e => (e.EvidenceId, e.Locator)).Select(x => x.First()).ToList();

    // ---- First-page summary ----

    private static bool Solid(Finding f) => f.Verification.State is StandardState.Verified or StandardState.Supported;

    public const string CanProveText = "Poate arăta ce urme există în elementele de probă colectate, de unde provin (cu amprenta fiecărui element) și cât de sigură este fiecare constatare.";
    public const string CannotProveText = "Nu poate dovedi intenția sau persoana care a acționat și nu spune nimic despre ce nu a fost colectat. Absența unei constatări nu înseamnă că sistemul este curat. " +
        "Verificarea din raport este automată (făcută de un alt modul al aplicației), nu externă. Nu este un raport de constatare tehnico-științifică și nici un raport de expertiză: " +
        "acestea se întocmesc de un specialist sau un expert numit (art. 172 din Codul de procedură penală) și pot folosi acest raport doar ca material de lucru.";

    public static ReportSummary Summarize(InvestigationResult r, ReportIntegrity.Result integrity)
    {
        var all = r.Findings.ToList();
        var graveF = all.Where(f => f.Severity >= Severity.High && f.RuleId != "INCIDENT-CHAIN").ToList();
        int h = all.Count(f => f.Severity >= Severity.High);
        int contradicted = all.Count(f => f.Verification.State is StandardState.Contradicted or StandardState.Rejected);
        int Conf(Severity s) => all.Count(f => f.Severity == s && Solid(f));
        int Ind(Severity s) => all.Count(f => f.Severity == s && !Solid(f));

        string verdict;
        if (!integrity.AllIntact) verdict = "Rezultatele nu pot fi folosite ca probă: " + integrity.Banner;
        else if (all.Count == 0) verdict = "Nu s-a constatat nimic în sursele analizate. Aceasta nu dovedește că sistemul este curat: vezi ce nu s-a putut verifica.";
        else if (h == 0) verdict = $"Nu s-a stabilit o compromitere: {RoText.Count(all.Count, "constatare", "constatări")}, niciuna de severitate ridicată sau critică.";
        else
        {
            int conf = all.Count(f => f.Severity >= Severity.High && Solid(f)), ind = h - conf;
            verdict = $"Există {RoText.CountFem(h, "constatare", "constatări")} de severitate ridicată sau critică: {RoText.Pronoun(conf, "confirmată", "confirmate")} de verificarea automată și {RoText.Pronoun(ind, "doar indicativă", "doar indicative")} (de confirmat). " +
                      "Un indiciu nu stabilește singur o compromitere.";
        }
        if (contradicted > 0) verdict += $" {RoText.Capitalize(RoText.CountFem(contradicted, "constatare", "constatări"))} {(contradicted == 1 ? "este contrazisă" : "sunt contrazise")} de alte probe.";

        var serious = new List<string>();
        foreach (var f in graveF.OrderByDescending(f => f.Severity).ThenByDescending(Solid).GroupBy(f => f.Title).Select(g => g.First()).Take(5))
            serious.Add($"{f.FindingId} · {RoText.StripVolumes(f.Title)} ({RoLabels.Severity(f.Severity).ToLowerInvariant()}, {(Solid(f) ? "confirmată" : "doar indicativă")})");
        int detected = r.AntiForensics.Count(a => a.Result == AntiForensicResult.Detected);
        if (detected > 0) serious.Add($"{RoText.Capitalize(RoText.CountFem(detected, "verificare", "verificări"))} de ascundere sau ștergere a urmelor {(detected == 1 ? "a găsit" : "au găsit")} o urmă observată (nu dovada intenției).");

        var notChecked = new List<string>();
        var gapNames = r.Gaps.Where(g => g.Status != EvidenceStatus.Success).Select(g => g.Artifact).Distinct(StringComparer.Ordinal).ToList();
        if (gapNames.Count > 0)
            notChecked.Add($"{RoText.Count(gapNames.Count, "sursă lipsă sau incompletă", "surse lipsă sau incomplete")}: " + string.Join(", ", gapNames.Take(5).Select(RoText.CleanPathsInText)) + (gapNames.Count > 5 ? $" și încă {gapNames.Count - 5}" : "") + ".");
        int undetermined = r.AntiForensics.Count(a => a.Result == AntiForensicResult.Undetermined);
        if (undetermined > 0) notChecked.Add($"{RoText.Capitalize(RoText.CountFem(undetermined, "verificare", "verificări"))} de ascundere a urmelor {(undetermined == 1 ? "este nedeterminată" : "sunt nedeterminate")} (sursa lipsește sau nu a putut fi citită).");
        if (integrity.HasNotices) notChecked.Add("Unele elemente de probă sunt goale sau de format neașteptat și nu au fost interpretate.");
        if (notChecked.Count == 0) notChecked.Add("Nicio sursă lipsă raportată pentru această analiză.");

        string meaning = !integrity.AllIntact
            ? "Nu folosiți constatările ca probă până nu sunt reachiziționate elementele de probă modificate sau lipsă."
            : h > 0
                ? "Constatările grave trebuie verificate de personalul de IT sau de securitate înainte de orice concluzie sau măsură. Până atunci nu reporniți, nu curățați și nu reinstalați stația, ca să nu se piardă urme."
                : "Pe baza surselor analizate nu este nevoie de o acțiune urgentă. Lipsa constatărilor nu dovedește lipsa activității, iar sursele lipsă limitează concluzia.";

        var steps = new List<string>();
        if (!integrity.AllIntact) steps.Add("Reachiziționați din sursa originală elementele de probă modificate sau lipsă și reluați analiza.");
        foreach (var s in all.Where(f => f.Severity >= Severity.High).OrderByDescending(f => f.Severity).ThenByDescending(Solid).SelectMany(f => f.RecommendedNextSteps).Distinct(StringComparer.Ordinal).Take(5))
            steps.Add(s);
        if (gapNames.Count > 0) steps.Add("Completați sursele lipsă sau incomplete (secțiunea „Surse lipsă sau incomplete”) și reluați analiza.");
        if (h > 0) steps.Add("Hotărâți măsurile (izolare, schimbarea parolelor, sesizare) doar după ce un specialist a verificat constatările.");
        if (steps.Count == 0) steps.Add("Nicio acțiune urgentă; păstrați cazul și lanțul de custodie.");

        return new ReportSummary(RoText.FixSentences(verdict), serious, notChecked, meaning, steps, CanProveText, CannotProveText,
            Conf(Severity.Critical), Ind(Severity.Critical), Conf(Severity.High), Ind(Severity.High));
    }

    // ---- Headline counters ----

    public static string DetectionsNoneText(int rulesApplied) =>
        $"Nicio potrivire din {RoText.Count(rulesApplied, "regulă aplicată", "reguli aplicate")}. Aceasta nu înseamnă „curat”: regulile caută doar tipare cunoscute.";

    // ---- Section numbering ----

    /// <summary>Numbers sections 1, 2, 3 … in the order they are printed, whatever sections the audience includes.</summary>
    public sealed class SectionCounter
    {
        private int _n;
        public string Next(string title) => $"{++_n}. {title}";
        public int Count => _n;
    }

    // ---- Legend of English codes ----

    public static IReadOnlyList<string> Legend()
    {
        var l = new List<string>
        {
            "Severitate: " + string.Join(", ", new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info }.Select(s => $"{RoLabels.Severity(s)} = {s.ToSpec()}")) + ".",
            "Stare: " + string.Join(", ", StateLabels.RomanianTable.Select(kv => $"{kv.Value} = {kv.Key.ToSpec()}")) + ".",
            "Clasificare: " + string.Join(", ", Enum.GetValues<Classification>().Select(c => $"{RoLabels.Classification(c)} = {c.ToSpec()}")) + ".",
            "Rezultat al verificărilor de ascundere a urmelor: " + string.Join(", ", Enum.GetValues<AntiForensicResult>().Select(a => $"{RoLabels.AntiForensic(a)} = {a.ToString().ToUpperInvariant().Replace("NOTDETECTED", "NOT_DETECTED")}")) + ".",
            "Stare a unei surse: " + string.Join(", ", Enum.GetValues<EvidenceStatus>().Select(s => $"{RoLabels.Source(s)} = {s.ToSpec()}")) + ".",
            "Starea unui parser: " + string.Join(", ", new[] { "VALIDATED", "TESTED", "EXPERIMENTAL" }.Select(c => $"{ParserStatusRomanian(c)} = {c}")) + ".",
            "Stare a unui element de probă la reverificare: " + string.Join(", ", new[] { "AVAILABLE", "UNVERIFIED", "NO_EVIDENCE", "READ_ERROR", "MUTATED" }.Select(c => $"{SpecStatusRomanian(c)} = {c}")) + ".",
            "Tip de afirmație: " + string.Join(", ", Enum.GetValues<SemanticType>().Select(t => $"{RoLabels.Semantic(t)} = {t.ToSpec()}")) + ".",
        };
        return l;
    }

    /// <summary>How an evidence item entered the case, in words: "importat la" / "achiziționat la" (the data keeps "imported" / "acquired").</summary>
    public static string AcquisitionRomanian(string method) => method switch
    {
        "imported" => "importat la", "acquired" => "achiziționat la", "" => "intrat în caz la", _ => method,
    };

    public static string ParserStatusRomanian(string status) => status.ToUpperInvariant() switch
    {
        "VALIDATED" => "validat pe date reale", "TESTED" => "testat doar pe date sintetice", "EXPERIMENTAL" => "experimental", _ => status,
    };

    public static string SpecStatusRomanian(string spec) => spec switch
    {
        "AVAILABLE" => "disponibil", "UNVERIFIED" => "neverificat", "NO_EVIDENCE" => "lipsă", "READ_ERROR" => "eroare de citire", "MUTATED" => "modificat", _ => spec,
    };
}
