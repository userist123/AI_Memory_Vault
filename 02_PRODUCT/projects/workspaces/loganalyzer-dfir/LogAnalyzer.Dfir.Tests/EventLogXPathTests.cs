using System.Diagnostics.Eventing.Reader;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression: the control read no Security events ("The specified query is invalid") because 25 IDs were OR-ed in one XPath.</summary>
public class EventLogXPathTests
{
    [Fact]
    public void Every_query_has_at_most_twenty_ids_and_together_they_cover_all()
    {
        var ids = StationFactCollector.Queries["Security"];
        Assert.True(ids.Length > 22);   // the case that failed
        var queries = EventLogXPath.ForIds(ids, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).ToList();
        Assert.Equal(2, queries.Count);
        Assert.All(queries, q => Assert.True(q.Split(" or ").Length <= EventLogXPath.MaxIdsPerQuery));
        foreach (var id in ids) Assert.Contains(queries, q => q.Contains($"EventID={id})") || q.Contains($"EventID={id} "));
        Assert.All(queries, q => Assert.Contains("@SystemTime>='2026-07-01T00:00:00.000Z'", q));
    }

    [Fact]
    public void Windows_accepts_every_generated_query()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ids = Enumerable.Range(7001, 45).ToArray();     // more than two chunks
        foreach (var xpath in EventLogXPath.ForIds(ids, DateTimeOffset.UtcNow.AddDays(-1)))
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));   // throws "invalid query" past 22 conditions
            reader.ReadEvent()?.Dispose();
        }
    }
}
