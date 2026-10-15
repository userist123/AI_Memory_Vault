using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Independent-review findings on the posture work: "NU SE APLICĂ" rows are not counted in summaries and verdicts, a change to or from
/// "NU SE APLICĂ" is never an improvement or a regression, and a malformed proxy value never aborts the control.
/// </summary>
public class ControlReviewFindingsTests
{
    private static ControlCheck C(string id, ControlStatus s, string area = "Rețea") => new(id, area, "Verificarea " + id, s, "detaliu", [], "recomandare");

    private static StationFacts Facts() => new()
    {
        Host = "PC1", CollectedUtc = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), IsAdministrator = true,
        Coverage = { new ChannelCoverage("Security", null, 100, true, ""), new ChannelCoverage("System", null, 10, true, "") },
    };

    // ---- finding 2: only applicable checks are counted --------------------------------------------------------------------------

    [Fact]
    public void Headline_is_NEDETERMINAT_when_every_applicable_check_is_undetermined_even_with_not_applicable_rows()
    {
        var r = new ControlReport { Facts = Facts() };
        r.Checks.AddRange([C("N01", ControlStatus.Nedeterminat), C("N02", ControlStatus.Nedeterminat), C("N12", ControlStatus.NuSeAplica)]);
        var screen = ControlResultScreen.Build(r, []);
        Assert.StartsWith("NEDETERMINAT", screen.Headline);
        Assert.DoesNotContain("Nimic neconform", screen.Problem);
        Assert.False(screen.NothingFound);
    }

    [Fact]
    public void Only_not_applicable_rows_mean_no_evaluated_checks()
    {
        var r = new ControlReport { Facts = Facts() };
        r.Checks.AddRange([C("N12", ControlStatus.NuSeAplica), C("N11", ControlStatus.NuSeAplica)]);
        Assert.Equal(0, r.Applicable);
        Assert.Equal("Nu există verificări evaluate.", ControlConclusion.Build(r).Verdict);
        Assert.DoesNotContain("NIMIC NECONFORM", ControlResultScreen.Build(r, []).Headline);
    }

    [Fact]
    public void Conclusion_says_nothing_could_be_checked_when_every_applicable_check_is_undetermined()
    {
        var r = new ControlReport { Facts = Facts() };
        r.Checks.AddRange([C("N01", ControlStatus.Nedeterminat), C("N12", ControlStatus.NuSeAplica)]);
        var v = ControlConclusion.Build(r).Verdict;
        Assert.DoesNotContain("Nicio verificare neconformă", v);
        Assert.Contains("NEDETERMINAT", v);
    }

    [Fact]
    public void Previous_control_label_counts_only_applicable_checks()
    {
        var p = new PreviousControl("x", DateTimeOffset.UtcNow, "PC1", [C("N01", ControlStatus.Conform), C("N12", ControlStatus.NuSeAplica)]);
        Assert.EndsWith("· o verificare", p.Label);
    }

    // ---- finding 3: N/A is not ranked with CONFORM ------------------------------------------------------------------------------

    [Theory]
    [InlineData(ControlStatus.Neconform, ControlStatus.NuSeAplica, "nu se mai aplică (ediție diferită)")]
    [InlineData(ControlStatus.Conform, ControlStatus.NuSeAplica, "nu se mai aplică (ediție diferită)")]
    [InlineData(ControlStatus.NuSeAplica, ControlStatus.Neconform, "se aplică acum (ediție diferită)")]
    [InlineData(ControlStatus.NuSeAplica, ControlStatus.Conform, "se aplică acum (ediție diferită)")]
    public void A_change_to_or_from_not_applicable_is_neither_improvement_nor_regression(ControlStatus before, ControlStatus after, string kind)
    {
        var prev = new PreviousControl("x", DateTimeOffset.UtcNow.AddDays(-5), "PC1", [C("N11", before)]);
        var cmp = ControlComparison.Compare(prev, [C("N11", after)]);
        Assert.Empty(cmp.Worse);
        Assert.Empty(cmp.Better);
        Assert.Equal(0, cmp.Unchanged);
        var ch = Assert.Single(cmp.ApplicabilityChanged);
        Assert.Equal(kind, ch.Kind);
        Assert.Contains("1 cu aplicabilitate schimbată", cmp.Summary);
    }

    [Fact]
    public void Not_applicable_on_both_sides_is_unchanged()
    {
        var prev = new PreviousControl("x", DateTimeOffset.UtcNow.AddDays(-5), "PC1", [C("N12", ControlStatus.NuSeAplica)]);
        var cmp = ControlComparison.Compare(prev, [C("N12", ControlStatus.NuSeAplica)]);
        Assert.Equal(1, cmp.Unchanged);
        Assert.Empty(cmp.ApplicabilityChanged);
    }

    // ---- finding 4: malformed proxy values -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("[fe80::1")]
    [InlineData("[fe80::1:8080")]
    [InlineData("[]:8080")]
    [InlineData("http=[::1")]
    [InlineData("http=")]
    [InlineData("=")]
    [InlineData("http://")]
    [InlineData("10.0.0.5:notaport")]
    [InlineData("10.0.0.5:99999")]
    [InlineData("[fe80::1]junk")]
    [InlineData(":8080")]
    public void Malformed_proxy_values_never_throw_and_become_a_finding_for_review(string proxy)
    {
        var ex = Record.Exception(() => EgressPathCollector.ProxyIsInternal(proxy));
        Assert.Null(ex);
        Assert.Null(EgressPathCollector.ProxyIsInternal(proxy));
        var fact = EgressPathCollector.ProxyFact("WinINet (utilizatorul curent)", proxy);
        Assert.Equal("Proxy", fact.Kind);
        Assert.Null(fact.Internal);
        Assert.Contains("valoare proxy nevalidă: " + proxy, fact.Description);
    }

    [Fact]
    public void A_malformed_proxy_part_next_to_a_public_address_is_still_outside()
    {
        Assert.False(EgressPathCollector.ProxyIsInternal("http=[fe80::1;https=203.0.113.9:443"));
    }

    [Theory]
    [InlineData("10.0.0.5:3128", true)]
    [InlineData("[fe80::1]:8080", true)]
    [InlineData("http://10.0.0.5:3128/", true)]
    [InlineData("[2001:db8::1]:3128", false)]
    public void Well_formed_proxy_values_are_not_reported_as_invalid(string proxy, bool expectedInternal)
    {
        Assert.Equal(expectedInternal, EgressPathCollector.ProxyIsInternal(proxy));
        Assert.DoesNotContain("nevalidă", EgressPathCollector.ProxyFact("WinHTTP", proxy).Description);
    }

    [Fact]
    public void A_parsing_exception_in_one_source_becomes_a_gap_not_a_failed_run()
    {
        var f = Facts();
        var ex = Record.Exception(() => StationFactCollector.Try(f, "Căi spre internet (configurație locală)", () => throw new ArgumentOutOfRangeException("length")));
        Assert.Null(ex);
        Assert.Contains(f.Gaps, g => g.Artifact == "Căi spre internet (configurație locală)");
        Assert.Null(f.EgressPaths);   // N11 then reports NEDETERMINAT
        Assert.Equal(LogAnalyzer.Core.Services.Edition.StationPosture.Standalone, f.Posture);
        Assert.Equal(ControlStatus.Nedeterminat, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N11").Status);
    }
}
