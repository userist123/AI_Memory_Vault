using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Patterns taken from rule lists and data packs (VSS command patterns, ClickFix patterns, approved-software paths) run over evidence text.
/// A match timeout must never read as "no match": padding a command with repeated tokens would otherwise hide it (independent review, finding 1).
/// They are therefore compiled with <see cref="RegexOptions.NonBacktracking"/>, whose run time is linear in the input. A pattern the linear engine
/// cannot run (backreference, lookaround, atomic group: possible only in an operator's own data pack; the shipped lists are checked by tests)
/// falls back to the backtracking engine with <see cref="RegexDefaults.RuleMatchTimeout"/>, and a timeout there is reported as "neevaluat"
/// (<see cref="NotEvaluatedRuleId"/>), never as false.
/// </summary>
public static class RulePatterns
{
    public const string NotEvaluatedRuleId = "RULE-NOT-EVALUATED";

    private const RegexOptions Base = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Compiles a rule-list pattern; an invalid pattern is an <see cref="ArgumentException"/> for the caller to handle.</summary>
    public static Regex Compile(string pattern)
    {
        try { return new Regex(pattern, Base | RegexOptions.NonBacktracking, RegexDefaults.RuleMatchTimeout); }
        catch (NotSupportedException) { return new Regex(pattern, Base, RegexDefaults.RuleMatchTimeout); }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary><see cref="Compile"/> through a small bounded cache, for patterns evaluated once per row (approved-software paths).</summary>
    public static Regex Cached(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var rx)) return rx;
        if (Cache.Count > 512) Cache.Clear();
        return Cache.GetOrAdd(pattern, Compile);
    }

    /// <summary>True when the pattern runs on the linear engine (no catastrophic backtracking possible).</summary>
    public static bool IsLinear(Regex rx) => (rx.Options & RegexOptions.NonBacktracking) != 0;

    /// <summary>true / false when evaluated; null when the match timed out: the input is "neevaluat" for this pattern, not a non-match.</summary>
    public static bool? Match(Regex rx, string text)
    {
        try { return rx.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>The finding that records evidence rows a rule could not evaluate because one of its patterns timed out.</summary>
    internal static Finding NotEvaluated(string findingId, string ruleId, string category, IReadOnlyList<TimelineEvent> rows)
    {
        var times = rows.Select(e => e.Time.Utc).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
        return new Finding
        {
            FindingId = findingId, RuleId = NotEvaluatedRuleId,
            Title = $"Regula {ruleId}: {LogAnalyzer.Dfir.Language.RoText.Count(rows.Count, "intrare neevaluată", "intrări neevaluate")} (timp de potrivire depășit)",
            Severity = Severity.Info, Category = category, Classification = Classification.Unproven, Confidence = Confidence.Low,
            SemanticType = SemanticType.Observation,
            FirstSeenUtc = times.Count > 0 ? times[0] : null, LastSeenUtc = times.Count > 0 ? times[^1] : null,
            Description = $"Pe {LogAnalyzer.Dfir.Language.RoText.Count(rows.Count, "intrare", "intrări")} cel puțin un tipar al regulii {ruleId} nu și-a terminat potrivirea în " +
                          $"{RegexDefaults.RuleMatchTimeout.TotalMilliseconds:0} ms. Rezultatul pentru ele este „neevaluat”, nu „curat”: lipsa constatării {ruleId} pe aceste " +
                          "intrări nu înseamnă că textul nu conține ce caută regula. Un text umplut intenționat cu repetiții poate produce această situație.",
            ClassificationReason = "Limită de evaluare a regulii, nu o concluzie despre probă.",
            SupportingEvidence = rows.Take(12).Select(e => new EvidenceRef(e.EvidenceId, e.Locator, $"{e.Source} {e.EventId}".Trim() + " neevaluat")).ToList(),
            Limitations = [$"Intrările listate nu au fost evaluate de regula {ruleId}; verificați-le manual."],
            RecommendedNextSteps = ["Citiți manual textul intrărilor listate.", "Înlocuiți tiparul din pachetul de date cu unul fără referințe înapoi sau lookaround (motorul liniar)."],
        };
    }
}
