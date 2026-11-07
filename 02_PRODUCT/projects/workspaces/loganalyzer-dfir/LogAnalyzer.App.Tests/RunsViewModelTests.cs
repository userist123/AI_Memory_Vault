using System;
using System.Linq;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Xunit;
namespace LogAnalyzer.App.Tests;

/// <summary>Master plan 6.16, „Rulări și diferențe” tab: the recorded runs of an opened case and the comparison of the last two.</summary>
[Collection("CollectAllState")]
public sealed class RunsViewModelTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { CollectAllState.Instance.Clear(); _k.Dispose(); }

    private static Finding F(string id, Severity s) => new() { FindingId = id, RuleId = "R1", Title = "t", Severity = s, Description = "d", SupportingEvidence = [new EvidenceRef("EV-000001", "rec 1", "x")] };

    [Fact]
    public void An_opened_case_lists_its_runs_and_compares_the_last_two()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        AnalysisRuns.Record(ws, AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [F("F-0001", Severity.High)], [], [], 1, "COMPLETE", "1.0"));
        AnalysisRuns.Record(ws, AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [F("F-0007", Severity.Critical)], [], [], 1, "COMPLETE", "1.0"));

        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!);
        Assert.Equal(2, vm.AnalysisRunRows.Count);
        Assert.Equal("2 rulări, toate intacte", vm.RunsLine);
        Assert.Contains("NEEXPLICATĂ", vm.RunDiffSummary);   // same evidence, same tools, the severity moved: for review
        Assert.True(vm.RunDiffNeedsReview);
        Assert.Contains(vm.RunDiffLines, l => l.StartsWith("~ modificată: F-0001→F-0007", StringComparison.Ordinal) && l.Contains("severitate High → Critical"));
    }

    [Fact]
    public void A_case_without_runs_says_so()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!);
        Assert.StartsWith("Nicio rulare înregistrată", vm.RunsLine);
        Assert.Equal("Comparația cere cel puțin două rulări.", vm.RunDiffSummary);
    }
}
