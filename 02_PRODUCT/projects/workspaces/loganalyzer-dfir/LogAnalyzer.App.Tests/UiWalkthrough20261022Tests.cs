using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The findings of the real-PC walkthrough of the installed Standalone demo (2026-10-22, UI only): banner text, status bar, edition name, date
/// format, watermark, device-monitor layout and counts, transfer register wording, help texts, password labels, wizard hints, window title.
/// </summary>
public sealed class UiWalkthrough20261022Tests
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

    private static string Root() => Path.GetDirectoryName(AppDir())!;

    /// <summary>Production sources (not tests, not build output) of the shared UI, core and Windows libraries.</summary>
    private static IEnumerable<string> ProductionSources() =>
        new[] { "LogAnalyzer.App", "LogAnalyzer.Core", "LogAnalyzer.Dfir.Core", "LogAnalyzer.Dfir.Windows" }
            .SelectMany(p => Directory.GetFiles(Path.Combine(Root(), p), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_user_facing_xaml_contains_the_english_watermark_Select_a_date()
    {
        var xaml = ProductionSources().Where(f => f.EndsWith(".xaml", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(xaml);
        Assert.Empty(xaml.Where(f => File.ReadAllText(f).Contains("Select a date", StringComparison.OrdinalIgnoreCase)));
        var app = File.ReadAllText(Path.Combine(AppDir(), "App.xaml"));
        Assert.Contains("TargetType=\"DatePickerTextBox\"", app);
        Assert.Contains("Alegeți data", app);
    }

    [Fact]
    public void Dates_are_romanian_through_the_element_language_and_the_thread_culture_is_left_alone()
    {
        var app = File.ReadAllText(Path.Combine(AppDir(), "App.xaml.cs"));
        Assert.Contains("FrameworkElement.LanguageProperty.OverrideMetadata", app);
        Assert.Contains("XmlLanguage.GetLanguage(\"ro-RO\")", app);
        // evidence parsing, export and report text keep the current culture: a process-wide culture change is not part of the UI fix
        Assert.DoesNotContain("DefaultThreadCurrentCulture", app);
    }

    [Fact]
    public void The_no_data_banner_title_and_subtitle_differ_and_the_subtitle_says_what_collect_does()
    {
        Assert.NotEqual(CollectAllState.EmptyTitle, CollectAllState.EmptySubtitle);
        Assert.Contains("Colectează acum", CollectAllState.EmptySubtitle);
        Assert.Contains("doar citire", CollectAllState.EmptySubtitle);
        var panel = File.ReadAllText(Path.Combine(AppDir(), "Views", "Controls", "NoDataPanel.xaml"));
        Assert.Contains("CollectAllState.EmptyTitle", panel);
        Assert.Contains("{Binding Status}", panel);
    }

    [Fact]
    public void The_status_bar_shows_an_ai_label_only_where_the_edition_has_ai_and_no_summary_without_data()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("eventsForAnalysis.Count == 0", vm);
        Assert.Contains("\"Nicio analiză încă.\"", vm);
        Assert.Contains("Edition.Has(LogAnalyzer.Core.Services.Edition.EditionFeature.AiAnalysis)", vm);
        Assert.DoesNotContain("\"✅ Analiză AI finalizată: Risc", vm);
    }

    [Fact]
    public void The_standalone_edition_has_no_ai_tab_and_is_named_standalone()
    {
        Assert.Equal("Standalone", StationPostures.EditionLabel(StationPosture.Standalone));
        var inv = new InvestigationViewModel();
        Assert.Null(inv.AiAnalysis);
        Assert.False(inv.HasAiAnalysis);
        var investigation = File.ReadAllText(Path.Combine(AppDir(), "Views", "InvestigationView.xaml"));
        Assert.Matches("<TabItem Header=\"Analiză AI \\(locală\\)\"[^>]*Visibility=\"\\{Binding HasAiAnalysis", investigation);
        Assert.DoesNotContain("Run AI &amp; Heuristic", File.ReadAllText(Path.Combine(AppDir(), "Views", "RiskAnalysisView.xaml")));
    }

    [Fact]
    public void The_window_title_is_short_romanian_with_the_edition_suffix()
    {
        var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        Assert.Contains("Title=\"LogAnalyzer\"", main);
        Assert.DoesNotContain("Threat Triage", main);
        var code = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml.cs"));
        Assert.Contains("Title += \" — Ediția: \" + LogAnalyzer.Core.Services.Edition.StationPostures.EditionLabel(", code);
    }

    [Fact]
    public void No_production_source_still_calls_the_classified_edition_ediția_clasificată()
    {
        var hits = ProductionSources().Where(f => File.ReadAllText(f).Contains("ediția clasificată", StringComparison.Ordinal)).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void The_home_card_no_longer_mentions_the_driver_columns()
    {
        var hits = ProductionSources().Where(f => File.ReadAllText(f).Contains("cu driverul", StringComparison.Ordinal)).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void The_password_change_boxes_have_visible_labels_and_automation_names()
    {
        var auth = File.ReadAllText(Path.Combine(AppDir(), "Views", "AuthView.xaml"));
        foreach (var label in new[] { "Parola curentă", "Parola nouă", "Repetați parola nouă" })
        {
            Assert.Contains($"<TextBlock Text=\"{label}\"", auth);
            Assert.Contains($"AutomationProperties.Name=\"{label}\"", auth);
        }
    }

    [Fact]
    public void The_station_control_hint_is_set_for_each_wizard_step()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "StationControlViewModel.cs"));
        Assert.Contains("alegeți perioada. Controlul doar citește", vm);
        Assert.Contains("alegeți procedurile.", vm);
        Assert.Contains("Apăsați „Pornește controlul” ca să rulați verificarea", vm);
        Assert.Matches("FlowBack\\(\\) \\{ _flow\\.Back\\(\\); SetFlowHint\\(\\);", vm);
        Assert.Matches("if \\(!_flow\\.Next\\(\\)\\) \\{ RaiseFlow\\(\\); return; \\}\\s*SetFlowHint\\(\\);", vm);
    }

    [Fact]
    public void The_monitor_header_names_only_the_monitor_scope_and_each_count_says_what_it_counts()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "MediaCenterView.xaml"));
        Assert.DoesNotContain("controlere și componente interne", page);
        Assert.Contains("Componentele interne nu se arată", page);
        var monitor = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "DeviceMonitorViewModel.cs"));
        Assert.Contains("Conectate acum (stocare și externe)", monitor);
        Assert.Contains("văzute de monitor", monitor);
        var inventory = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MediaCenterViewModel.cs"));
        Assert.Contains("Inventarul Windows (fila „Toate dispozitivele”)", inventory);
        // the readable minimum widths: names and the alert wrap on words, never one character per line
        Assert.Matches("Header=\"Nume\"[^>]*MinWidth=\"\\d+\"", page);
        Assert.Matches("Header=\"Alertă\"[^>]*MinWidth=\"260\"", page);
        Assert.Matches("<TextBlock Text=\"\\{Binding Problem\\}\" Style=\"\\{StaticResource OptionalLine\\}\"", page);
    }

    [Fact]
    public void The_transfer_register_uses_the_monitor_page_name_and_neutral_medium_wording()
    {
        var reg = File.ReadAllText(Path.Combine(AppDir(), "Views", "TransferRegisterView.xaml"));
        Assert.DoesNotContain("„Medii de stocare”", reg);
        Assert.Contains("„Monitor dispozitive”", reg);
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "TransferViewModel.cs"));
        Assert.DoesNotContain("Niciun stick", vm);
        Assert.DoesNotContain("„Medii de stocare”", vm);
        foreach (var header in new[] { "Clasificare", "Destinație", "Mediu", "Conținut", "Martor", "Copii" })
            Assert.Matches($"Header=\"{header}\"[^>]*MinWidth=\"\\d+\"", reg);
        Assert.Matches("Header=\"Direcție\"[^>]*Width=\"100\"", reg);   // was 70: the header is readable now
    }
}
