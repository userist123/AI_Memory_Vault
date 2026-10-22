using System.IO;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Home and investigation text/state defects seen on the owner's PC (2026-10-22, installed Standalone Release build).</summary>
[Collection("CollectAllState")]
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
            Assert.True(state.IsEmptyForHome);
            state.RecordControl("Control stație finalizat: NECONFORM 1.");
            Assert.False(state.IsEmptyForHome);   // Home and the control page hide the offer
            Assert.True(state.IsEmpty);           // Investigation and Media still have no data
            Assert.Equal("Control stație finalizat: NECONFORM 1.", heard);
            state.Clear();
            Assert.True(state.IsEmpty);
            Assert.True(state.IsEmptyForHome);
        }
        finally { state.ActivityRecorded -= On; state.Clear(); }
    }

    [Fact]
    public void The_main_view_model_puts_the_last_activity_in_the_status_bar()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("CollectAllState.Instance.ActivityRecorded += line => StatusMessage = ActivityStatusLine(line,", vm);
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
        Assert.DoesNotContain("ancorare", LiveCase.ReportScopeRequiredMessage);   // no claim about the anchor by default
        Assert.StartsWith("Nu s-a salvat nimic", LiveCase.ScopeRequiredMessage);
    }

    [Fact]
    public void The_report_scope_message_claims_the_anchor_is_saved_only_when_it_is()
    {
        Assert.Contains("codul de ancorare sunt deja salvate", StationControlViewModel.ReportScopeMessage(anchorIssued: true, finishAudited: true));
        Assert.DoesNotContain("ancorare", StationControlViewModel.ReportScopeMessage(anchorIssued: false, finishAudited: true));
        Assert.DoesNotContain("ancorare", StationControlViewModel.ReportScopeMessage(anchorIssued: true, finishAudited: false));
        Assert.DoesNotContain("ancorare", StationControlViewModel.ReportScopeMessage(false, false));
        Assert.Contains("Raportul nu s-a generat", StationControlViewModel.ReportScopeMessage(false, false));
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "StationControlViewModel.cs"));
        Assert.Contains("ReportScopeMessage(_report.Anchor is not null, _controlFinishAudited)", vm);
        Assert.Contains("_controlFinishAudited = auditedFinish;", vm);
    }

    [Fact]
    public void Empty_offer_scope_home_and_control_page_hide_it_after_a_control_the_others_do_not()
    {
        var dir = AppDir();
        Assert.Contains("HideAfterControl=\"True\"", File.ReadAllText(Path.Combine(dir, "Views", "HomeView.xaml")));
        Assert.Contains("HideAfterControl=\"True\"", File.ReadAllText(Path.Combine(dir, "Views", "StationControlView.xaml")));
        Assert.DoesNotContain("HideAfterControl", File.ReadAllText(Path.Combine(dir, "Views", "InvestigationView.xaml")));
        Assert.DoesNotContain("HideAfterControl", File.ReadAllText(Path.Combine(dir, "Views", "MediaCenterView.xaml")));
    }

    [Fact]
    public void Recording_the_live_case_after_a_control_has_no_side_effects_and_survives_a_corrupt_case_file()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "HomeViewModel.cs"));
        var body = vm.Substring(vm.IndexOf("private void RecordLiveCase()", StringComparison.Ordinal), 1100);
        Assert.DoesNotContain("LiveCase.Get()", body);
        Assert.Contains("JsonException", body);
        Assert.Contains("InvalidDataException", body);
    }

    [Fact]
    public void An_opened_case_does_not_say_investigation_finished()
    {
        var state = CollectAllState.Instance;
        state.Clear();
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "InvestigationViewModel.cs"));
        Assert.Contains("ShowResult(loaded.Result, opened: true);", vm);
        var svc = File.ReadAllText(Path.Combine(AppDir(), "Services", "CollectAll.cs"));
        Assert.Contains("? $\"Caz deschis:", svc);
    }

    [Fact]
    public void An_audit_health_warning_stays_in_the_status_bar_in_front_of_the_activity_line()
    {
        Assert.Equal("Control gata.", MainViewModel.ActivityStatusLine("Control gata.", null));
        Assert.Equal("Control gata.", MainViewModel.ActivityStatusLine("Control gata.", " "));
        var s = MainViewModel.ActivityStatusLine("Control gata.", "ATENȚIE: jurnalul de audit nu se poate scrie");
        Assert.StartsWith("ATENȚIE: jurnalul de audit", s);
        Assert.EndsWith("Control gata.", s);
    }

    [Fact]
    public void Recent_cases_are_displayed_in_romanian_local_time_with_the_full_path_in_a_tooltip()
    {
        var at = new DateTimeOffset(2026, 10, 22, 18, 40, 0, TimeSpan.Zero);
        var entry = new LogAnalyzer.Dfir.Case.RecentCase("D:/Cases/a", "Monitorizare live", at, "ACTIVE");
        var view = new LogAnalyzer.Dfir.Case.RecentCaseView(entry, true);
        Assert.Equal("ACTIVE", entry.State);                 // stored data unchanged
        Assert.Equal("activ", view.DisplayState);
        Assert.Equal("deschis ultima dată " + at.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), view.LastOpenedLocalText);
        Assert.Equal("sigilat (închis)", new LogAnalyzer.Dfir.Case.RecentCaseView(entry with { State = "SEALED" }, true).DisplayState);
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "HomeView.xaml"));
        Assert.Contains("ToolTip=\"{Binding Entry.Path}\"", xaml);
        Assert.Contains("DisplayState", xaml);
        Assert.DoesNotContain("UTC'", xaml);
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
