namespace LogAnalyzer.Dfir.Language;

/// <summary>Language of a glossary phrase. Only the glossary uses it until the resource layer (WP6b) takes over the rest of the UI.</summary>
public enum GlossaryLanguage { Romanian, English }

/// <summary>One glossary entry (UX contract §17, audit rows R18.1-R18.12): the technical term, its plain-language phrase in each language, and a short explanation (Romanian).</summary>
public sealed record GlossaryTerm(string Key, string Technical, string Romanian, string English, string Explanation);

/// <summary>
/// The single plain-language glossary. Normal views show the human phrase; advanced/technical views keep the technical name (with the
/// human phrase beside it), and every place that shows the human phrase also offers <see cref="Tooltip"/> with the technical name.
/// A term that is not in the table is returned unchanged: the glossary never invents a translation.
/// </summary>
public static class Glossary
{
    public static IReadOnlyList<GlossaryTerm> Terms { get; } =
    [
        new("evtx", "EVTX", "Jurnale Windows", "Windows logs",
            "Fișierele în care Windows înregistrează evenimentele sistemului, ale securității și ale aplicațiilor."),
        new("prefetch", "Prefetch", "Istoricul pornirii programelor", "Program start history",
            "Urmele pe care Windows le păstrează când un program este pornit: arată ce program și cam când, nu cine l-a pornit."),
        new("bam", "BAM", "Activitatea programelor", "Program activity",
            "Evidența Windows a programelor rulate de fiecare utilizator și a momentului ultimei rulări."),
        new("amcache", "Amcache", "Istoricul aplicațiilor", "Application history",
            "Lista aplicațiilor cunoscute de Windows; prezența unei intrări nu dovedește că aplicația a rulat."),
        new("evidence_graph", "Evidence Graph", "Legături între evenimente", "Connections between events",
            "Harta legăturilor dintre fișiere, procese, conturi, adrese și constatări; fiecare legătură arată proba sau derivarea ei."),
        new("provenance", "Provenance", "De unde provine informația", "Where this information came from",
            "Sursa unei informații: proba, fișierul (cu amprenta SHA-256) și parserul care a produs-o."),
        new("chain_of_custody", "Chain of Custody", "Istoricul probei", "Evidence history",
            "Registrul cine a avut proba, când și ce s-a făcut cu ea, cu amprente care arată dacă a fost modificată."),
        new("ioc", "IOC", "Indicator suspect", "Suspicious indicator",
            "Un element (adresă, domeniu, fișier, amprentă) asociat cu activitate rău-intenționată; apariția lui nu dovedește singură un atac."),
        new("sigma", "Sigma", "Regulă de detecție de securitate", "Security detection rule",
            "Regulă scrisă într-un format comun, care caută în jurnale tipare de activitate suspectă."),
        new("yara", "YARA", "Regulă de detecție pe fișiere/conținut", "File/content detection rule",
            "Regulă care caută în fișiere sau în conținut tipare asociate cu programe rău-intenționate."),
        new("correlation", "Correlation", "Evenimente legate", "Related events",
            "Evenimente care apar împreună (în timp, în cale sau în aceeași sursă); apropierea lor nu dovedește că unul l-a provocat pe celălalt."),
        new("verification", "Verification", "Verificarea probelor", "Evidence check",
            "Controlul automat al fiecărei constatări față de probele din caz; nu este o verificare externă sau umană."),
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
    public static string Human(string term, GlossaryLanguage lang = GlossaryLanguage.Romanian) =>
        Find(term) is { } t ? (lang == GlossaryLanguage.English ? t.English : t.Romanian) : term;

    /// <summary>Normal views: the human phrase. Advanced/technical views: the technical name with the human phrase beside it.</summary>
    public static string Display(string term, bool advanced, GlossaryLanguage lang = GlossaryLanguage.Romanian) =>
        Find(term) is not { } t ? term : advanced ? $"{t.Technical} ({Human(term, lang)})" : Human(term, lang);

    /// <summary>Tooltip text: technical name, human phrase and the short explanation. Empty for a term that is not in the table.</summary>
    public static string Tooltip(string term, GlossaryLanguage lang = GlossaryLanguage.Romanian) =>
        Find(term) is { } t ? $"{t.Technical} — {Human(term, lang)}. {t.Explanation}" : "";
}
