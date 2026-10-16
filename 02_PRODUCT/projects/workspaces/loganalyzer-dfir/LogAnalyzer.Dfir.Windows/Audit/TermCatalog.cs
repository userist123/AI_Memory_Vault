using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// Usability 2026-10-16 (owner: „fiecare termen explicat acolo unde apare”): the single lookup behind every tooltip, every „?” button and the
/// „Ce înseamnă termenii” appendix of the reports. It joins the plain-language glossary (<see cref="Glossary.All"/>: jargon and explained words such as the
/// results and the severity words) with one entry per control check id, taken from <see cref="CheckGuides"/> so a check and its explanation never drift.
/// A word that is in neither is returned as not found: the catalogue never invents an explanation.
/// </summary>
public static class TermCatalog
{
    private static readonly Lazy<IReadOnlyList<GlossaryTerm>> Checks = new(() => CheckGuides.Ids.OrderBy(i => i, StringComparer.Ordinal).Select(CheckTerm).ToList());

    /// <summary>The glossary entry of a control check: its id, a short name and what is checked and why.</summary>
    private static GlossaryTerm CheckTerm(string id)
    {
        var g = CheckGuides.For(id);
        return new GlossaryTerm(id, id, $"Verificarea {id}", $"Check {id}", $"{g.What} De ce contează: {g.Why}");
    }

    /// <summary>Every entry: the glossary first, then the control checks in id order.</summary>
    public static IReadOnlyList<GlossaryTerm> All => Glossary.All.Concat(Checks.Value).ToList();

    /// <summary>The control-check entries only.</summary>
    public static IReadOnlyList<GlossaryTerm> CheckTerms => Checks.Value;

    /// <summary>The entry for a technical name, alias, check id or enum name (<c>DeVerificat</c>); null when there is none.</summary>
    public static GlossaryTerm? Find(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        var t = term.Trim();
        return Glossary.Lookup(t) ?? Checks.Value.FirstOrDefault(c => string.Equals(c.Key, t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What a tooltip shows: the technical name, the plain phrase and the explanation. Empty when the term is unknown (no tooltip at all).</summary>
    public static string Tooltip(string? term) => Find(term) is { } t ? $"{t.Technical} — {t.Romanian}. {t.Explanation}" : "";

    /// <summary>
    /// The entries whose technical name, alias or check id appears in <paramref name="text"/> as a whole word, in the order of the catalogue, each once.
    /// A spelling with capitals must match exactly (so „MEDIE” is not found in „în medie”); an all-lowercase spelling matches in any case.
    /// </summary>
    public static IReadOnlyList<GlossaryTerm> TermsIn(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var found = new List<GlossaryTerm>();
        foreach (var t in All)
            if (t.Spellings.Any(s => Appears(text, s))) found.Add(t);
        return found;
    }

    private static bool Appears(string text, string spelling)
    {
        if (spelling.Length < 2) return false;
        var options = spelling.Any(char.IsUpper) ? RegexOptions.CultureInvariant : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(spelling)}(?![\p{{L}}\p{{N}}])", options, TimeSpan.FromSeconds(1));
    }

    /// <summary>The appendix lines: one per term, „Termen — pe înțeles: explicație”. Empty when the text has no explained term.</summary>
    public static IReadOnlyList<string> AppendixLines(string? text) =>
        TermsIn(text).Select(t => $"{t.Technical} — {t.Romanian}: {t.Explanation}").ToList();

    /// <summary>Heading of the appendix printed in the reports.</summary>
    public const string AppendixTitle = "Ce înseamnă termenii din acest raport";
}
