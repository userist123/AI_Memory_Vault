using System.IO;
using System.Xml.Linq;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>The shared result and the handler are process-wide: these tests run alone, never in parallel with a test that opens a case or builds the main window.</summary>
[CollectionDefinition("CollectAllState", DisableParallelization = true)]
public sealed class CollectAllStateCollection { }

/// <summary>G3: one „Colectează tot” for the role, one result reused by every page, and the empty-page offer „Nu există încă date — Colectează acum”.</summary>
[Collection("CollectAllState")]
public sealed class CollectAllTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { CollectAllState.Instance.Clear(); CollectAllState.Handler = null; _k.Dispose(); }

    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    [Fact]
    public void The_plan_depends_on_the_role()
    {
        var csirt = CollectAllState.Plan(StationRole.Csirt);
        Assert.Equal(2, csirt.ProfileIndex);          // „Complet”
        Assert.False(csirt.RunStationControl);
        var control = CollectAllState.Plan(StationRole.Control);
        Assert.True(control.RunStationControl);
        Assert.Contains("controlul stației", control.Description);
        Assert.All(new[] { csirt, control }, p => Assert.Contains("arhivate și Windows.old", p.Description));
    }

    [Fact]
    public async Task The_single_command_runs_the_handler_and_a_published_result_is_shared()
    {
        CollectAllState.Instance.Clear();
        Assert.True(CollectAllState.Instance.IsEmpty);
        Assert.Equal(CollectAllState.EmptyText, CollectAllState.Instance.Status);
        int runs = 0;
        CollectAllState.Handler = () => { runs++; return Task.CompletedTask; };
        Assert.True(CollectAllState.Command.CanExecute(null));
        await CollectAllState.RunAsync();
        Assert.Equal(1, runs);

        var ws = _k.Case(_k.File("a.bin", "alpha"));
        InvestigationResult? seen = null;
        CollectAllState.Instance.Published += r => seen = r;
        var result = new InvestigationResult { Case = ws };
        CollectAllState.Instance.Publish(result);
        Assert.True(CollectAllState.Instance.HasData);
        Assert.Same(result, seen);
        Assert.Contains("Toate paginile folosesc acest rezultat", CollectAllState.Instance.Status);
    }

    [Fact]
    public void Without_a_handler_the_command_says_so_instead_of_doing_nothing()
    {
        CollectAllState.Handler = null;
        CollectAllState.Command.Execute(null);   // completes synchronously: there is nothing to await without a handler
        Assert.Contains("nu este disponibilă", CollectAllState.Instance.Status);
        CollectAllState.Handler = () => Task.CompletedTask;
        CollectAllState.Instance.IsRunning = true;
        CollectAllState.Command.Execute(null);
        Assert.Contains("rulează deja", CollectAllState.Instance.Status);
        CollectAllState.Instance.IsRunning = false;
    }

    [Fact]
    public void The_investigation_page_publishes_its_result_for_every_page()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        var loaded = CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult();
        Assert.True(loaded.Opened);
        vm.ShowLoaded(loaded.Case!);
        Assert.Same(vm.CurrentResult, CollectAllState.Instance.Current);
    }

    [Fact]
    public void Every_case_page_shows_the_empty_offer_and_the_window_has_the_collect_all_button()
    {
        var dir = AppDir();
        foreach (var view in new[] { "HomeView.xaml", "InvestigationView.xaml", "MediaCenterView.xaml", "StationControlView.xaml" })
            Assert.Contains("NoDataPanel", File.ReadAllText(Path.Combine(dir, "Views", view)));
        var panel = XDocument.Load(Path.Combine(dir, "Views", "Controls", "NoDataPanel.xaml")).ToString();
        Assert.Contains("CollectAllState.EmptyText", panel);
        Assert.Contains("CollectAllState.Command", panel);
        Assert.Contains("IsEmpty", panel);
        Assert.Equal("Nu există încă date — Colectează acum", CollectAllState.EmptyText);
        var main = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml"));
        Assert.Contains("Content=\"Colectează tot\" Command=\"{x:Static services:CollectAllState.Command}\"", main);
    }
}
