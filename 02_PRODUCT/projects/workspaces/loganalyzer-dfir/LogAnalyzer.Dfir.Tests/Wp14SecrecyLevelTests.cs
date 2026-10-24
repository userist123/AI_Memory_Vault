using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP14a item 1 (owner decision 21): classification levels as data, with an equivalence table used only for comparison.</summary>
public class Wp14SecrecyLevelTests
{
    [Fact]
    public void There_are_exactly_twelve_levels_four_per_scheme_and_none_is_unclassified()
    {
        Assert.Equal(12, SecrecyLevels.All.Count);
        foreach (var s in Enum.GetValues<SecrecyScheme>()) Assert.Equal(4, SecrecyLevels.All.Count(l => l.Scheme == s));
        Assert.DoesNotContain(SecrecyLevels.All, l => l.Name.Contains("UNCLASSIFIED", StringComparison.OrdinalIgnoreCase) || l.Name.Contains("NECLASIFICAT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(SecrecyLevels.All.Count, SecrecyLevels.All.Select(l => l.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("COSMIC TOP SECRET", "NATO_COSMIC_TOP_SECRET")]
    [InlineData("cts", "NATO_COSMIC_TOP_SECRET")]
    [InlineData("NATO SECRET", "NATO_SECRET")]
    [InlineData("NATO CONFIDENTIAL", "NATO_CONFIDENTIAL")]
    [InlineData("NATO RESTRICTED", "NATO_RESTRICTED")]
    [InlineData("TRÈS SECRET UE", "EU_TOP_SECRET")]
    [InlineData("EU TOP SECRET", "EU_TOP_SECRET")]
    [InlineData("SECRET UE/EU SECRET", "EU_SECRET")]
    [InlineData("CONFIDENTIEL UE", "EU_CONFIDENTIAL")]
    [InlineData("RESTREINT UE / EU RESTRICTED", "EU_RESTRICTED")]
    [InlineData("strict secret de importanță deosebită", "RO_STRICT_SECRET_DE_IMPORTANTA_DEOSEBITA")]
    [InlineData("STRICT SECRET DE IMPORTANTA DEOSEBITA", "RO_STRICT_SECRET_DE_IMPORTANTA_DEOSEBITA")]
    [InlineData("strict secret", "RO_STRICT_SECRET")]
    [InlineData("Secret", "RO_SECRET")]
    [InlineData("secret de serviciu", "RO_SECRET_DE_SERVICIU")]
    public void Every_level_is_recognised_by_its_marking(string text, string id)
    {
        Assert.True(SecrecyLevels.TryParse(text, out var level));
        Assert.Equal(id, level!.Id);
    }

    [Fact]
    public void Every_level_round_trips_through_its_id_and_its_name()
    {
        foreach (var l in SecrecyLevels.All)
        {
            Assert.Same(l, SecrecyLevels.Parse(l.Id));
            Assert.Same(l, SecrecyLevels.Parse(l.Name));
        }
    }

    [Theory]
    [InlineData("UNCLASSIFIED")]
    [InlineData("NATO UNCLASSIFIED")]
    [InlineData("neclasificat")]
    [InlineData("necunoscut")]
    [InlineData("TOP SECRET")]
    public void Unclassified_and_unknown_markings_are_not_levels(string text)
    {
        Assert.False(SecrecyLevels.TryParse(text, out _));
    }

    [Theory]
    [InlineData("UNCLASSIFIED")]
    [InlineData("NATO UNCLASSIFIED")]
    [InlineData("Neclasificat")]
    public void The_unclassified_markings_are_named_as_refused_by_decision_21(string text) => Assert.True(SecrecyLevels.IsUnclassifiedMarking(text));

    [Fact]
    public void An_empty_marking_is_nemarcat_not_a_level()
    {
        Assert.False(SecrecyLevels.TryParse("", out _));
        Assert.True(SecrecyLevels.IsUnmarked(""));
        Assert.True(SecrecyLevels.IsUnmarked("  nemarcat "));
        Assert.False(SecrecyLevels.IsUnmarked("NATO SECRET"));
        Assert.Equal("nemarcat", SecrecyLevels.Describe(""));
    }

    [Fact]
    public void Equivalence_table_pairs_nato_eu_and_national_by_rank_and_is_used_only_for_comparison()
    {
        var cts = SecrecyLevels.Parse("COSMIC TOP SECRET");
        var eq = SecrecyLevels.Equivalents(cts).Select(l => l.Id).OrderBy(x => x).ToList();
        Assert.Equal(["EU_TOP_SECRET", "NATO_COSMIC_TOP_SECRET", "RO_STRICT_SECRET_DE_IMPORTANTA_DEOSEBITA"], eq);
        Assert.Equal(SecrecyLevels.Parse("NATO SECRET").Rank, SecrecyLevels.Parse("SECRET UE").Rank);
        Assert.Equal(SecrecyLevels.Parse("NATO SECRET").Rank, SecrecyLevels.Parse("STRICT SECRET").Rank);
        Assert.Equal(SecrecyLevels.Parse("NATO CONFIDENTIAL").Rank, SecrecyLevels.Parse("SECRET").Rank);
        Assert.Equal(SecrecyLevels.Parse("NATO RESTRICTED").Rank, SecrecyLevels.Parse("SECRET DE SERVICIU").Rank);
        Assert.True(SecrecyLevels.AtLeast(SecrecyLevels.Parse("NATO SECRET"), SecrecyLevels.Parse("RESTREINT UE")));
        Assert.False(SecrecyLevels.AtLeast(SecrecyLevels.Parse("SECRET DE SERVICIU"), SecrecyLevels.Parse("NATO CONFIDENTIAL")));
        Assert.True(SecrecyLevels.EquivalenceSource.Contains("585", StringComparison.Ordinal));
    }
}
