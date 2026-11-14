using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.4: events per source and day, and silent days inside a source's active period reported as candidate gaps only.</summary>
public sealed class CoverageTimelineTests
{
    private static readonly DateTime D0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static TimelineEvent E(string source, int day, int i = 0) => new()
    {
        Time = Timestamp.FromUtc(D0.AddDays(day).AddMinutes(i), "", "t"), Source = source, EvidenceId = "EV-1", Summary = "x", Locator = $"{source}-{day}-{i}",
    };

    [Fact]
    public void Events_are_counted_per_source_and_day_and_a_silent_stretch_inside_activity_is_a_candidate_gap()
    {
        var t = new List<TimelineEvent>();
        foreach (var d in new[] { 0, 1, 2, 6, 7 }) for (int i = 0; i < 3; i++) t.Add(E("EventLog:Security", d, i));
        t.Add(E("USB", 4));
        t.Add(new TimelineEvent { Time = Timestamp.Unknown("??"), Source = "Prefetch", EvidenceId = "EV-1", Summary = "fără oră" });
        var c = CoverageTimeline.Build(t);
        Assert.Equal(8, c.Days);
        Assert.Equal(1, c.WithoutTime);
        var sec = c.Rows.Single(r => r.Source == "EventLog:Security");
        Assert.Equal(new[] { 3, 3, 3, 0, 0, 0, 3, 3 }, sec.PerDay);
        Assert.Equal((3, 3), Assert.Single(sec.Gaps));
        var line = Assert.Single(c.GapLines());
        Assert.Contains("2026-11-04 și 2026-11-06", line);
        Assert.Contains("POSIBILĂ (CANDIDATE)", line);
        Assert.Empty(c.Rows.Single(r => r.Source == "USB").Gaps);     // one day of activity: nothing before and after to compare with
    }

    [Fact]
    public void One_quiet_day_is_not_listed_and_the_edges_of_a_source_are_never_gaps()
    {
        var t = new[] { E("System", 2), E("System", 4), E("System", 9) }.ToList();
        var c = CoverageTimeline.Build(t, D0, D0.AddDays(12));
        var row = Assert.Single(c.Rows);
        Assert.Equal(13, c.Days);
        Assert.Equal((5, 4), Assert.Single(row.Gaps));            // days 5..8; day 3 alone is under the 2-day minimum; days 0-1 and 10-12 are edges
    }

    [Fact]
    public void A_very_long_period_keeps_the_most_recent_part_and_says_so()
    {
        var c = CoverageTimeline.Build([E("System", 0), E("System", 1000)]);
        Assert.True(c.Truncated);
        Assert.Equal(CoverageTimeline.MaxDays, c.Days);
        Assert.Equal(1, c.Rows[0].Total);
    }
}
