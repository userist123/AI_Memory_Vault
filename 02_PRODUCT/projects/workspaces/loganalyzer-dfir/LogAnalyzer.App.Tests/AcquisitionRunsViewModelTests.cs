using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>R7c: the "Colectări" tab of the case page.</summary>
[Collection("CollectAllState")]
public sealed class AcquisitionRunsViewModelTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { CollectAllState.Instance.Clear(); _k.Dispose(); }

    [Fact]
    public void An_opened_case_with_a_record_lists_it_and_says_how_many()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.Add("Col", "1", "tool", "2", EvidenceStatus.Success, [], [], ws.LoadEvidence(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        rec.Complete(OperationState.Completed, "");

        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        var loaded = CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult();
        Assert.True(loaded.Opened);
        vm.ShowLoaded(loaded.Case!);

        var row = Assert.Single(vm.AcquisitionRuns);
        Assert.Equal(rec.RunId, row.RunId);
        Assert.False(row.HasProblem);
        Assert.Equal("1 colectare înregistrată, 0 cu probleme", vm.AcquisitionRunsLine);
    }

    [Fact]
    public void A_case_without_records_says_so()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!);
        Assert.Empty(vm.AcquisitionRuns);
        Assert.Equal("Nicio fișă de colectare în acest caz", vm.AcquisitionRunsLine);
    }

    [Fact]
    public void R7c_6_a_read_error_is_not_shown_as_no_records()
    {
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.FillAcquisitionRuns([], "acces refuzat");
        Assert.Equal("Fișele de colectare nu au putut fi citite: acces refuzat", vm.AcquisitionRunsLine);
    }
}
