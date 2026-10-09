using LogAnalyzer.Dfir.Language;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP6a / R18.1-R18.12: the plain-language glossary is one table in Dfir.Core; the technical name stays reachable.</summary>
public class Wp6aGlossaryTests
{
    // The twelve terms of the audit rows R18.1-R18.12, in order, with the Romanian human phrase the owner approved.
    private static readonly (string Tech, string Ro, string En)[] Required =
    [
        ("EVTX", "Jurnale Windows", "Windows logs"),
        ("Prefetch", "Istoricul pornirii programelor", "Program start history"),
        ("BAM", "Activitatea programelor", "Program activity"),
        ("Amcache", "Istoricul aplicațiilor", "Application history"),
        ("Evidence Graph", "Legături între evenimente", "Connections between events"),
        ("Provenance", "De unde provine informația", "Where this information came from"),
        ("Chain of Custody", "Istoricul probei", "Evidence history"),
        ("IOC", "Indicator suspect", "Suspicious indicator"),
        ("Sigma", "Regulă de detecție de securitate", "Security detection rule"),
        ("YARA", "Regulă de detecție pe fișiere/conținut", "File/content detection rule"),
        ("Correlation", "Evenimente legate", "Related events"),
        ("Verification", "Verificarea probelor", "Evidence check"),
    ];

    [Fact]
    public void The_twelve_required_terms_exist_with_romanian_english_and_an_explanation()
    {
        Assert.True(Glossary.Terms.Count >= 12);
        foreach (var (tech, ro, en) in Required)
        {
            var t = Glossary.Find(tech);
            Assert.NotNull(t);
            Assert.Equal(tech, t!.Technical);
            Assert.Equal(ro, t.Romanian);
            Assert.Equal(en, t.English);
            Assert.False(string.IsNullOrWhiteSpace(t.Explanation), tech);
        }
    }

    [Fact]
    public void Every_term_is_complete_and_keys_and_technical_names_are_unique()
    {
        foreach (var t in Glossary.Terms)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Key));
            Assert.False(string.IsNullOrWhiteSpace(t.Technical));
            Assert.False(string.IsNullOrWhiteSpace(t.Romanian));
            Assert.False(string.IsNullOrWhiteSpace(t.English));
            Assert.False(string.IsNullOrWhiteSpace(t.Explanation));
            Assert.NotEqual(t.Technical, t.Romanian);   // a "human phrase" that equals the jargon is not a translation
        }
        Assert.Equal(Glossary.Terms.Count, Glossary.Terms.Select(t => t.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Glossary.Terms.Count, Glossary.Terms.Select(t => t.Technical).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Lookup_accepts_key_or_technical_name_in_any_case_and_unknown_terms_pass_through_unchanged()
    {
        Assert.Equal("Jurnale Windows", Glossary.Human("evtx"));
        Assert.Equal("Jurnale Windows", Glossary.Human("EVTX"));
        Assert.Equal("Istoricul probei", Glossary.Human("chain_of_custody"));
        Assert.Equal("Istoricul probei", Glossary.Human("Chain of Custody"));
        Assert.Equal("Windows logs", Glossary.Human("EVTX", AppLanguage.English));
        Assert.Equal("ShimCache", Glossary.Human("ShimCache"));   // not in the table: never invented, shown as is
        Assert.Null(Glossary.Find("ShimCache"));
    }

    [Fact]
    public void Normal_views_show_the_human_phrase_and_advanced_views_keep_the_technical_name()
    {
        Assert.Equal("Jurnale Windows", Glossary.Display("EVTX", advanced: false));
        var adv = Glossary.Display("EVTX", advanced: true);
        Assert.Contains("EVTX", adv);
        Assert.Contains("Jurnale Windows", adv);
        var tip = Glossary.Tooltip("EVTX");
        Assert.Contains("EVTX", tip);
        Assert.Contains("Jurnale Windows", tip);
        Assert.Contains(Glossary.Find("EVTX")!.Explanation, tip);
        Assert.Equal("", Glossary.Tooltip("ShimCache"));
    }

    [Fact]
    public void Coverage_rows_for_glossary_families_show_the_human_phrase_and_keep_the_technical_label()
    {
        var m = LogAnalyzer.Dfir.Coverage.CoverageMatrix.Unknown([]);
        var bam = m.Rows.First(r => r.FamilyId == "bam");
        Assert.Equal("Activitatea programelor", bam.HumanFamily);
        Assert.Equal("BAM", bam.Family);
        Assert.Contains("BAM", bam.FamilyTooltip);
        var srum = m.Rows.First(r => r.FamilyId == "srum");
        Assert.Equal(srum.Family, srum.HumanFamily);   // no glossary entry: shown unchanged
    }
}
