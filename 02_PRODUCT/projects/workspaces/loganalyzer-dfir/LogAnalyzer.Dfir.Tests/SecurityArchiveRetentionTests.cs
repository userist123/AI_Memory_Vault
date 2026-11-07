using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// A18 with the archives Windows leaves on the station (research 2026-11-07, plan P1.3; HG 585 art. 291: access records kept at least 3 years at secret and
/// strict secret, 10 at SSID). An archive extends the live log only when it joins it without a gap; archives moved to other media are still not seen.
/// </summary>
public sealed class SecurityArchiveRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    private static SecurityArchiveFact A(string name, DateTimeOffset from, DateTimeOffset to) => new(name, from, to, "");

    private static StationFacts Facts(DateTimeOffset liveOldest, params SecurityArchiveFact[] archives)
    {
        var f = new StationFacts
        {
            Host = "LAB-PC", CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, IsAdministrator = true, Posture = StationPosture.Standalone,
            StationLevel = "SECRET", Coverage = { new ChannelCoverage("Security", liveOldest, 100, true, "") },
        };
        f.SecurityArchives.AddRange(archives);
        return f;
    }

    private static ControlCheck A18(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "A18");

    [Fact]
    public void Archives_that_join_the_live_log_without_a_gap_extend_the_coverage_back()
    {
        var live = Now.AddMonths(-2);
        var (start, used, gap) = ControlEvaluator.ContinuousCoverage(live,
        [
            A("Archive-Security-2.evtx", Now.AddYears(-2), live.AddSeconds(-5)),
            A("Archive-Security-1.evtx", Now.AddYears(-4), Now.AddYears(-2).AddHours(-3)),
        ]);
        Assert.Equal(Now.AddYears(-4), start);
        Assert.Equal(2, used);
        Assert.Equal("", gap);
    }

    [Fact]
    public void A_break_stops_the_chain_and_is_reported_and_unreadable_archives_never_count()
    {
        var live = Now.AddMonths(-2);
        var (start, used, gap) = ControlEvaluator.ContinuousCoverage(live,
        [
            A("Archive-Security-2.evtx", Now.AddYears(-1), live.AddMinutes(-1)),
            A("Archive-Security-1.evtx", Now.AddYears(-5), Now.AddYears(-1).AddDays(-30)),   // a month missing before archive 2
            new("Archive-Security-0.evtx", null, null, "acces refuzat"),
        ]);
        Assert.Equal(Now.AddYears(-1), start);
        Assert.Equal(1, used);
        Assert.StartsWith("între ", gap);
    }

    [Fact]
    public void A18_is_conform_when_the_live_log_and_its_archives_cover_the_three_years_of_a_secret_station()
    {
        var live = Now.AddMonths(-2);
        var c = A18(Facts(live, A("Archive-Security-1.evtx", Now.AddYears(-3).AddDays(-1), live.AddSeconds(-2))));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("împreună cu arhivele lui", c.Detail);
        Assert.Contains(c.Evidence, e => e.StartsWith("Archive-Security-1.evtx"));
    }

    [Fact]
    public void A18_stays_to_be_checked_when_the_chain_is_broken_or_too_short_and_names_the_gap()
    {
        var live = Now.AddMonths(-2);
        var broken = A18(Facts(live,
            A("Archive-Security-2.evtx", Now.AddYears(-1), live.AddSeconds(-2)),
            A("Archive-Security-1.evtx", Now.AddYears(-4), Now.AddYears(-1).AddDays(-20))));
        Assert.Equal(ControlStatus.DeVerificat, broken.Status);
        Assert.Contains("lipsesc înregistrări între", broken.Detail);
        Assert.Contains("poate fi și o perioadă în care stația a fost oprită", broken.Detail);
        Assert.Contains("arhivele exportate", broken.Detail);

        var none = A18(Facts(live));
        Assert.Equal(ControlStatus.DeVerificat, none.Status);
        Assert.Contains("nu există arhive", none.Detail);
    }
}
