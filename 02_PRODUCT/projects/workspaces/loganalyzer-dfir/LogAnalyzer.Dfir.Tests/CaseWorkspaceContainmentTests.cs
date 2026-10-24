using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #17: <see cref="CaseWorkspace.FullPath"/> resolves a stored path only inside the case folder. A tampered or imported case
/// whose evidence index lists an absolute path or a <c>..\</c> path is refused (gap), never analysed or hashed as if it were evidence.
/// </summary>
public sealed class CaseWorkspaceContainmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_contain_" + Guid.NewGuid().ToString("N"));

    public CaseWorkspaceContainmentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void FullPath_accepts_paths_inside_the_case()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "contain", TestScopes.Valid());
        Assert.Equal(Path.Combine(ws.Root, "Raw", "x.txt"), ws.FullPath(@"Raw\x.txt"));
        Assert.Equal(Path.Combine(ws.Root, "Exports", "a.jsonl"), ws.FullPath("Exports/a.jsonl"));
    }

    [Theory]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"Raw\..\..\outside.txt")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"\\server\share\x.evtx")]
    public void FullPath_refuses_paths_outside_the_case(string stored)
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "contain", TestScopes.Valid());
        Assert.Throws<ArgumentException>(() => ws.FullPath(stored));
    }

    [Fact]
    public void FullPath_refuses_a_sibling_folder_sharing_the_case_name_prefix()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "contain", TestScopes.Valid());
        Assert.Throws<ArgumentException>(() => ws.FullPath(ws.Root + "_other" + Path.DirectorySeparatorChar + "x.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pipeline_reports_a_gap_for_a_stored_path_outside_the_case_and_does_not_parse_it(bool absolute)
    {
        var outside = Path.Combine(_dir, "outside.txt");
        File.WriteAllText(outside, "not evidence");
        var input = Path.Combine(_dir, "input.txt");
        File.WriteAllText(input, "evidence");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "contain", TestScopes.Valid());
        var ev = Assert.Single(InvestigationPipeline.Import(ws, [input]));

        var forged = absolute ? outside : Path.GetRelativePath(ws.Root, outside);
        Assert.True(forged.StartsWith("..") || Path.IsPathRooted(forged));
        var index = ws.EvidenceIndexPath;
        File.SetAttributes(index, FileAttributes.Normal);
        var lines = File.ReadAllLines(index).Select(l =>
        {
            var n = JsonNode.Parse(l)!;
            if ((string?)n["evidenceId"] == ev.EvidenceId || (string?)n["EvidenceId"] == ev.EvidenceId)
            {
                if (n["storedPath"] is not null) n["storedPath"] = forged; else n["StoredPath"] = forged;
            }
            return n.ToJsonString();
        }).ToArray();
        File.WriteAllLines(index, lines);
        Assert.Equal(forged, Assert.Single(ws.LoadEvidence()).StoredPath);

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        Assert.Contains(r.Gaps, g => g.Reason.Contains("iese din folderul cazului"));
        Assert.DoesNotContain(r.Parsing, p => p.EvidenceId == ev.EvidenceId && p.Status == Model.EvidenceStatus.Success);
    }
}
