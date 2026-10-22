using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Real-PC walkthrough, round 2 (2026-10-22, UI): the device-monitor columns keep a readable width and the grid scrolls sideways, the scope text is said once,
/// the status bar gives no analysis summary right after sign-in, and the home page does not show the wizard's „Înainte” hint.
/// </summary>
public sealed class UiWalkthroughRound2Tests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }

    private static string MonitorGrid()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "MediaCenterView.xaml"));
        var grid = page[page.IndexOf("x:Name=\"DevicesGrid\"", StringComparison.Ordinal)..];
        return grid[..grid.IndexOf("</DataGrid.Columns>", StringComparison.Ordinal)];
    }

    [Fact]
    public void Every_monitor_column_has_a_minimum_width_that_fits_its_header_and_the_grid_scrolls_sideways()
    {
        var grid = MonitorGrid();
        var columns = Regex.Matches(grid, "<DataGrid(?:Text|Template)Column Header=\"([^\"]*)\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(19, columns.Count);
        foreach (var c in columns)
        {
            var header = Regex.Match(c, "Header=\"([^\"]*)\"").Groups[1].Value;
            var min = Regex.Match(c, "MinWidth=\"(\\d+)\"");
            Assert.True(min.Success, $"column „{header}” has no MinWidth (it collapses to two characters when the page is narrow)");
            // about 7 px a character of the header plus the sort arrow and padding; „Stare” must fit „Deconectat” next to its dot
            Assert.True(int.Parse(min.Groups[1].Value) >= Math.Max(header.Length * 7 + 20, 60), $"column „{header}” MinWidth {min.Groups[1].Value} is too small");
        }
        Assert.Matches("<DataGridTemplateColumn Header=\"Stare\"[^>]*MinWidth=\"170\"", grid);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", grid[..grid.IndexOf('>')]);
    }

    [Fact]
    public void The_scope_of_the_monitor_is_explained_once()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "MediaCenterView.xaml"));
        Assert.Single(Regex.Matches(page, "omponentele interne nu se arată"));
        Assert.Contains("cititor de carduri). Componentele interne nu se arată", page);
        Assert.Contains("Text=\"Orice dispozitiv conectat sau scos apare singur", page);
    }

    [Fact]
    public void A_row_named_after_a_drive_letter_shows_the_model_instead()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "DeviceMonitorViewModel.cs"));
        Assert.Contains("DeviceMonitorRules.FriendlyName([e.Name], e.Model.Length > 0 ? e.Model", vm);
    }

    [Fact]
    public void The_status_bar_announces_an_analysis_only_for_data_of_this_session()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        // start-up re-reads the stored data without announcing it; an import, a collection or the operator's button does
        Assert.Contains("_ = RunAnalysisAsync(announce: false);", vm);
        Assert.Contains("public Task RunAiAnalysis() => RunAnalysisAsync(announce: true);", vm);
        Assert.Contains("eventsForAnalysis.Count == 0 || !announce", vm);
        // no score without evidence (the engine's 5-point baseline is not a risk)
        Assert.Contains("AiRiskScore = evidence ? explainableRisk.TotalScore : 0;", vm);
        Assert.Single(Regex.Matches(vm, @"RunAnalysisAsync\(announce: false\)"));
    }

    [Fact]
    public void The_home_page_does_not_show_the_wizard_hint_about_a_button_it_does_not_have()
    {
        var home = File.ReadAllText(Path.Combine(AppDir(), "Views", "HomeView.xaml"));
        Assert.Contains("{Binding QuickControl.HomeStatus}", home);
        Assert.DoesNotContain("{Binding QuickControl.Status}", home);
        Assert.Contains("Fă controlul stației", StationControlViewModel.HomeHint);
        Assert.DoesNotContain("Înainte", StationControlViewModel.HomeHint);
    }
}
