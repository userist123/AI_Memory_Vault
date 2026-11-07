using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// A DFIR screen never shows evidence that was not computed: the timeline panel once printed "CRITICAL (3 ALERTS)", fixed times and
/// "EDR Shield active: C2 beacon isolated" whatever the data. The panel is now computed (TimelineSummary) and the views are scanned for such text.
/// </summary>
public sealed class NoFixedSampleEvidenceTests
{
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    private static readonly Regex Forbidden = new(
        @"EDR Shield|C2 beacon|CRITICAL \([1-9]|Text=""\d{2}:\d{2}:\d{2}( UTC)?""|processes contained|FallbackValue='(CRITICAL|HIGH|T1\d{3}[^']*|Live EDR[^']*)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void No_view_prints_fixed_sample_findings_or_times()
    {
        var hits = Directory.EnumerateFiles(Path.Combine(Root(), "LogAnalyzer.App", "Views"), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => Forbidden.IsMatch(x.l))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.l.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, "Text demo fix în ecrane:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void The_timeline_summary_says_nothing_is_loaded_when_there_are_no_events()
    {
        var s = TimelineSummary.From([]);
        Assert.Equal((TimelineSummary.NoEvents, TimelineSummary.NoEvents, TimelineSummary.NoEvents, 0), (s.First, s.Last, s.Peak, s.Count));
        Assert.False(s.PeakIsCritical);
    }

    [Fact]
    public void The_timeline_summary_is_computed_from_the_events_shown()
    {
        var s = TimelineSummary.From(
        [
            new TimelineItem { Timestamp = new DateTime(2026, 11, 4, 10, 0, 5, DateTimeKind.Utc), Severity = "Informativ" },
            new TimelineItem { Timestamp = new DateTime(2026, 11, 4, 9, 1, 2, DateTimeKind.Utc), Severity = "Avertizare" },
            new TimelineItem { Timestamp = new DateTime(2026, 11, 4, 11, 30, 0, DateTimeKind.Utc), Severity = "warning" },
        ]);
        Assert.Equal("04.11.2026 09:01:02 UTC", s.First);
        Assert.Equal("04.11.2026 11:30:00 UTC", s.Last);
        Assert.Equal("MEDIU (2 evenimente)", s.Peak);
        Assert.Equal(3, s.Count);
    }
}
