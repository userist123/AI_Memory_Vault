using System.IO;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Home and investigation text/state defects seen on the owner's PC (2026-10-22, installed Standalone Release build).</summary>
public sealed class HomeTextFixes20261022Tests
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

    [Fact]
    public void A_finished_control_hides_the_empty_banner_and_reaches_the_status_bar()
    {
        var state = CollectAllState.Instance;
        state.Clear();
        string? heard = null;
        void On(string l) => heard = l;
        state.ActivityRecorded += On;
        try
        {
            Assert.True(state.IsEmpty);
            state.RecordControl("Control stație finalizat: NECONFORM 1.");
            Assert.False(state.IsEmpty);
            Assert.Equal("Control stație finalizat: NECONFORM 1.", heard);
            state.Clear();
            Assert.True(state.IsEmpty);
        }
        finally { state.ActivityRecorded -= On; state.Clear(); }
    }

    [Fact]
    public void The_main_view_model_puts_the_last_activity_in_the_status_bar()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("CollectAllState.Instance.ActivityRecorded += line => StatusMessage = line;", vm);
    }

    [Fact]
    public void Result_chips_carry_the_tone_of_the_pdf_tiles()
    {
        Assert.Equal("Critical", StatusCount.ToneOf(LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Neconform));
        Assert.Equal("Warning", StatusCount.ToneOf(LogAnalyzer.Dfir.Windows.Audit.ControlStatus.DeVerificat));
        Assert.Equal("Success", StatusCount.ToneOf(LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Conform));
        Assert.Equal("Muted", StatusCount.ToneOf(LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Nedeterminat));
        Assert.Equal("Muted", StatusCount.ToneOf(LogAnalyzer.Dfir.Windows.Audit.ControlStatus.NuSeAplica));
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "HomeView.xaml"));
        Assert.Contains("Binding Tone}\" Value=\"Critical\"", xaml);
        Assert.Contains("BadgeSuccessBgBrush", xaml);
    }

    [Fact]
    public void The_double_click_hint_stays_on_the_control_page_status_but_not_on_the_home_status()
    {
        var vm = new StationControlViewModel();
        vm.Status = "Control finalizat: 90 verificări, 62 acțiuni în cronologie." + StationControlViewModel.DoubleClickHint;
        Assert.Contains("Dublu-click", vm.Status);
        Assert.DoesNotContain("Dublu-click", vm.HomeStatus);
        Assert.Contains("62 acțiuni în cronologie.", vm.HomeStatus);
    }

    [Fact]
    public void The_report_scope_message_does_not_claim_that_nothing_was_saved()
    {
        Assert.DoesNotContain("Nu s-a salvat nimic", LiveCase.ReportScopeRequiredMessage);
        Assert.Contains("Raportul nu s-a generat", LiveCase.ReportScopeRequiredMessage);
        Assert.Contains("codul de ancorare", LiveCase.ReportScopeRequiredMessage);
        Assert.StartsWith("Nu s-a salvat nimic", LiveCase.ScopeRequiredMessage);
    }

    [Fact]
    public void The_scope_dialog_title_equals_its_heading()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "ScopeDialog.xaml"));
        Assert.Contains("Title=\"Scopul cazului stației\"", xaml);
        Assert.Contains("Text=\"Scopul cazului stației\"", xaml);
        Assert.DoesNotContain("cazului LIVE", xaml);
    }

    [Fact]
    public void The_investigation_texts_use_romanian_numbers_severity_words_and_lowercase_state()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "InvestigationViewModel.cs"));
        Assert.Contains("Timeline.Count.ToString(\"N0\", Ro)", vm);
        Assert.Contains("RomanianOperation[_result.State].ToLower(Ro)", vm);
        Assert.Contains("RoLabels.Severity(c.Severity).ToLower(Ro)", vm);
        Assert.DoesNotContain("[{c.Severity.ToSpec()}]", vm);
    }
}
