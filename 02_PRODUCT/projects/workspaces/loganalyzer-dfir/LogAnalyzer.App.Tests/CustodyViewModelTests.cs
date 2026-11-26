using System.IO;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Xunit;
namespace LogAnalyzer.App.Tests;
/// <summary>Tab 9 "Istoricul probelor": the custody chain of the open case, not the in-memory analysis ledger.</summary>
[Collection("CollectAllState")]
public sealed class CustodyViewModelTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { CollectAllState.Instance.Clear(); _k.Dispose(); }

    [Fact]
    public void Without_a_case_the_page_says_how_to_get_one()
    {
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        Assert.Empty(vm.CustodyRows);
        Assert.Equal(LogAnalyzer.UI.ViewModels.InvestigationViewModel.NoCaseCustodyLine, vm.CustodyLine);
    }

    [Fact]
    public void An_opened_case_lists_its_custody_with_a_verified_chain()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!);
        var ev = Assert.Single(ws.LoadEvidence());
        Assert.Contains(vm.CustodyRows, r => r.EvidenceId == ev.EvidenceId && r.Sha256 == ev.Sha256);
        Assert.False(vm.CustodyProblem);
        Assert.StartsWith("Lanț de custodie verificat:", vm.CustodyLine);
    }

    [Fact]
    public void A_broken_chain_is_shown_as_a_problem_and_refresh_reads_it_again()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!);
        Assert.False(vm.CustodyProblem);
        File.AppendAllText(ws.CustodyJsonlPath, "nu este json\n");
        vm.RefreshCustodyCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.True(vm.CustodyProblem);
        Assert.Contains("linii ilizibile", vm.CustodyLine);
    }

    [Fact]
    public void A_read_error_is_not_shown_as_an_empty_chain()
    {
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.FillCustody(null, "acces refuzat");
        Assert.True(vm.CustodyProblem);
        Assert.Equal("Lanțul de custodie nu a putut fi citit: acces refuzat", vm.CustodyLine);
    }

    [Fact]
    public void The_page_binds_the_case_custody_and_keeps_the_session_ledger_apart()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "ChainOfCustodyView.xaml"));
        Assert.Contains("Investigation.CustodyRows", xaml);
        Assert.Contains("Investigation.CustodyLine", xaml);
        Assert.Contains("nu face parte din caz", xaml);
        Assert.DoesNotContain("ExportCsvCommand", xaml);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("LogAnalyzer.App"), "LogAnalyzer.App");
    }
}
