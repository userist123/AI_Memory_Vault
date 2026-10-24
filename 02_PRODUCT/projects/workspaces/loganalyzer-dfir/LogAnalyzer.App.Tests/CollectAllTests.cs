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
        Assert.Equal(CollectAllState.EmptySubtitle, CollectAllState.Instance.Status);   // was EmptyText: the subtitle now says what the button collects
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
        Assert.Contains("CollectAllState.EmptyTitle", panel);
        Assert.Contains("Text=\"{Binding Status}\"", panel);   // the subtitle is the status line, which starts as EmptySubtitle
        Assert.NotEqual(CollectAllState.EmptyTitle, CollectAllState.EmptySubtitle);
        Assert.Contains("Colectează acum", CollectAllState.EmptySubtitle);
        Assert.Contains("doar citire", CollectAllState.EmptySubtitle);
        Assert.Contains("CollectAllState.Command", panel);
        Assert.Contains("IsEmpty", panel);
        Assert.Equal("Nu există încă date — Colectează acum", CollectAllState.EmptyText);
        var main = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml"));
        Assert.Contains("Content=\"Colectează tot\" Command=\"{x:Static services:CollectAllState.Command}\"", main);
    }

    [Fact]
    public void The_older_pages_under_Avansat_get_the_offer_from_the_window_and_it_hides_when_the_page_has_its_own_events()
    {
        var dir = AppDir();
        var main = XDocument.Load(Path.Combine(dir, "MainWindow.xaml"));
        XNamespace ctrl = "clr-namespace:LogAnalyzer.UI.Views.Controls";
        var panel = Assert.Single(main.Descendants(ctrl + "NoDataPanel"));
        Assert.Equal("{Binding Events.Count}", (string?)panel.Attribute("PageItemCount"));
        Assert.Contains("IsLegacyDataPage", (string?)panel.Attribute("Visibility"));
        // the panel itself: visible only while there is no result AND the page holds nothing of its own
        var own = File.ReadAllText(Path.Combine(dir, "Views", "Controls", "NoDataPanel.xaml"));
        Assert.Contains("<Condition Binding=\"{Binding IsEmpty}\" Value=\"True\"/>", own);
        Assert.Contains("<Condition Binding=\"{Binding HasPageData, ElementName=Root}\" Value=\"False\"/>", own);
        // the pages that already carry their own panel are not in the list (no double offer)
        Assert.DoesNotContain(LogAnalyzer.UI.ViewModels.HomeViewModel.HomeTabIndex, LogAnalyzer.UI.ViewModels.MainViewModel.LegacyDataTabs);
        Assert.DoesNotContain(LogAnalyzer.UI.ViewModels.HomeViewModel.InvestigationTabIndex, LogAnalyzer.UI.ViewModels.MainViewModel.LegacyDataTabs);
        Assert.DoesNotContain(LogAnalyzer.Core.Services.Edition.RoleProfiles.TabMediaRegister, LogAnalyzer.UI.ViewModels.MainViewModel.LegacyDataTabs);
        Assert.DoesNotContain(14, LogAnalyzer.UI.ViewModels.MainViewModel.LegacyDataTabs); // Control stație
        Assert.Contains(1, LogAnalyzer.UI.ViewModels.MainViewModel.LegacyDataTabs);          // evenimente EVTX
    }
}
