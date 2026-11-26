using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Evaluation 2026-11-05 on the owner's station: the live-state findings were numbered from the count of findings, while Correlation reserves
/// ids ahead (the cleared-log finding), so two findings got "F-0305" and EvidenceGraph.Build threw; the whole investigation stopped and the app closed.
/// </summary>
public sealed class FindingIdUniquenessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_fid_" + Guid.NewGuid().ToString("N"));
    public FindingIdUniquenessTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static Finding F(string id) => new() { FindingId = id, RuleId = "T", Title = "t", Description = "d" };

    [Fact]
    public void The_next_number_is_the_highest_id_not_the_count()
    {
        Assert.Equal(0, LiveStateAnalyzer.MaxFindingNo([]));
        Assert.Equal(305, LiveStateAnalyzer.MaxFindingNo([F("F-0001"), F("F-0305"), F("SEQ-1"), F("F-x")]));
    }

    [Fact]
    public void A_repeated_id_does_not_throw_in_the_evidence_graph()
    {
        var ex = Record.Exception(() => LogAnalyzer.Dfir.Graph.EvidenceGraph.Build([], [F("F-0305"), F("F-0305")], "PC1"));
        Assert.Null(ex);
    }

    [Fact]
    public void Live_state_findings_follow_a_reserved_id_instead_of_reusing_it()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "fid", TestScopes.Valid());
        var path = Path.Combine(ws.RawDir("fid"), "live.json");
        var snap = new LiveStateCollector.Snapshot(DateTimeOffset.UtcNow, "PC1",
            [new LiveStateCollector.ProcessRow(4242, 1, "evil.exe", @"C:\Users\user\AppData\Local\Temp\evil.exe", "evil.exe", "NESEMNAT", true)], [], [], [], [], []);
        File.WriteAllText(path, JsonSerializer.Serialize(snap));
        var item = ws.RegisterStored(path, "live", "fid", "live_snapshot", TemporalType.Live, "col", "1");

        // two findings already given, but one id reserved in between: F-0001 and F-0003 (count 2, highest 3)
        var found = new List<Finding> { F("F-0001"), F("F-0003") };
        found.AddRange(LiveStateAnalyzer.Analyze(item, path, LiveStateAnalyzer.MaxFindingNo(found)).ToList());

        Assert.Equal(3, found.Count);
        Assert.Equal("F-0004", found[^1].FindingId);
        Assert.Equal(found.Count, found.Select(x => x.FindingId).Distinct(StringComparer.Ordinal).Count());
        // the graph that crashed accepts them
        LogAnalyzer.Dfir.Graph.EvidenceGraph.Build([], found, "PC1");
    }
}
