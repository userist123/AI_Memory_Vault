namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// One glossary entry (UX contract §17, audit rows R18.1-R18.12): the technical term (an identifier, never translated) and its key. The plain-language phrase and
/// the short explanation live in the resource layer (<c>term.{Key}</c> and <c>term.{Key}.explain</c> in <c>Strings.ro.json</c> / <c>Strings.en.json</c>), so there
/// is one text table for the whole interface (WP6b).
/// </summary>
public sealed record GlossaryTerm(string Key, string Technical)
{
    public string Romanian => Loc.T($"term.{Key}", AppLanguage.Romanian);
    public string English => Loc.T($"term.{Key}", AppLanguage.English);
    /// <summary>The human phrase in the current UI language (or in <paramref name="lang"/>).</summary>
    public string Human(AppLanguage? lang = null) => Loc.T($"term.{Key}", lang ?? Loc.Current);
    /// <summary>The short explanation in the current language.</summary>
    public string Explanation => Loc.T($"term.{Key}.explain");
    public string ExplanationIn(AppLanguage lang) => Loc.T($"term.{Key}.explain", lang);
}

/// <summary>
/// The single plain-language glossary. Normal views show the human phrase; advanced/technical views keep the technical name (with the
/// human phrase beside it), and every place that shows the human phrase also offers <see cref="Tooltip"/> with the technical name.
/// A term that is not in the table is returned unchanged: the glossary never invents a translation. Without an explicit language the current UI language is used.
/// </summary>
public static class Glossary
{
    public static IReadOnlyList<GlossaryTerm> Terms { get; } =
    [
        new("evtx", "EVTX"), new("prefetch", "Prefetch"), new("bam", "BAM"), new("amcache", "Amcache"),
        new("evidence_graph", "Evidence Graph"), new("provenance", "Provenance"), new("chain_of_custody", "Chain of Custody"),
        new("ioc", "IOC"), new("sigma", "Sigma"), new("yara", "YARA"), new("correlation", "Correlation"), new("verification", "Verification"),
    ];

    /// <summary>The entry whose key or technical name matches (case-insensitive; spaces and underscores are equivalent), or null.</summary>
    public static GlossaryTerm? Find(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        var n = Norm(term);
        return Terms.FirstOrDefault(t => Norm(t.Key) == n || Norm(t.Technical) == n);
    }

    private static string Norm(string s) => s.Trim().Replace('_', ' ').ToLowerInvariant();

    /// <summary>The human phrase in the chosen language; the term itself when it is not in the table.</summary>
    public static string Human(string term, AppLanguage? lang = null) =>
        Find(term) is { } t ? Loc.T($"term.{t.Key}", lang ?? Loc.Current) : term;

    /// <summary>Normal views: the human phrase. Advanced/technical views: the technical name with the human phrase beside it.</summary>
    public static string Display(string term, bool advanced, AppLanguage? lang = null) =>
        Find(term) is not { } t ? term : advanced ? $"{t.Technical} ({Human(term, lang)})" : Human(term, lang);

    /// <summary>Tooltip text: technical name, human phrase and the short explanation. Empty for a term that is not in the table.</summary>
    public static string Tooltip(string term, AppLanguage? lang = null) =>
        Find(term) is { } t ? $"{t.Technical} — {Human(term, lang)}. {t.ExplanationIn(lang ?? Loc.Current)}" : "";
}
