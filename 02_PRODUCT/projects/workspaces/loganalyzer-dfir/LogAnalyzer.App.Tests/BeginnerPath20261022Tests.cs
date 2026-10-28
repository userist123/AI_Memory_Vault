using System.IO;
using System.Linq;
using System.Xml.Linq;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner decisions of 2026-10-22 after the cold start as a beginner (docs/utilizare/PORNIRE_INCEPATOR_2026-10-22.md): one main path on Home (A), the anchor code
/// as an optional wizard step (B), „ultimul control” with one meaning (C), the investigation wizard without the parallel form in „Simplu” (D) and the
/// three-screen first-run guide (E). View-model and XAML-binding level, like the UiWalkthrough tests.
/// </summary>
[Collection(GlobalStateCollection.Name)]
public sealed class BeginnerPath20261022Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-beginner-" + Guid.NewGuid().ToString("N"));
    public BeginnerPath20261022Tests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

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

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    // ───────── A: one main path on Home ─────────

    [Fact]
    public void The_language_level_state_follows_the_level_and_raises_the_change()
    {
        var state = LanguageLevelState.Instance;
        var raised = new List<string?>();
        void On(object? s, System.ComponentModel.PropertyChangedEventArgs e) => raised.Add(e.PropertyName);
        state.PropertyChanged += On;
        try
        {
            Assert.True(state.IsSimple); Assert.False(state.IsExpert);
            LanguageLevelContext.Set(UiLanguageLevel.Expert);
            Assert.False(state.IsSimple); Assert.True(state.IsExpert);
            Assert.Contains(nameof(LanguageLevelState.IsSimple), raised);
            Assert.Contains(nameof(LanguageLevelState.IsExpert), raised);
        }
        finally { state.PropertyChanged -= On; }
    }

    [Fact]
    public void The_top_bar_collect_all_button_is_shown_only_in_expert()
    {
        var main = XDocument.Load(Path.Combine(AppDir(), "MainWindow.xaml"));
        var button = main.Descendants().Single(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Content") == "Colectează tot");
        var visibility = (string?)button.Attribute("Visibility");
        Assert.NotNull(visibility);
        Assert.Contains("IsExpertLevel", visibility);
        Assert.Contains("Converter", visibility);
    }

    [Fact]
    public void The_empty_offer_is_hidden_in_simple_on_home_and_the_control_page_only()
    {
        var panel = Src("Views", "Controls", "NoDataPanel.xaml");
        Assert.Contains("HideInSimpleLevel", panel);
        Assert.Contains("LanguageLevelState", panel);
        Assert.Contains("HideInSimpleLevel=\"True\"", Src("Views", "HomeView.xaml"));
        Assert.Contains("HideInSimpleLevel=\"True\"", Src("Views", "StationControlView.xaml"));
        Assert.Contains("HideInSimpleLevel=\"True\"", Src("Views", "InvestigationView.xaml"));   // round 4 2026-10-23: the investigation wizard is its path in Simplu
        Assert.DoesNotContain("HideInSimpleLevel", Src("Views", "MediaCenterView.xaml"));
        Assert.DoesNotContain("HideInSimpleLevel", Src("MainWindow.xaml"));
        Assert.NotNull(typeof(LogAnalyzer.UI.Views.Controls.NoDataPanel).GetProperty("HideInSimpleLevel"));
    }

    [Fact]
    public void Earlier_controls_on_disk_hide_the_empty_offer_like_a_control_of_this_session()
    {
        var state = CollectAllState.Instance;
        var had = state.HasEarlierControls;
        try
        {
            state.Clear();
            state.HasEarlierControls = false;
            Assert.False(state.OfferHiddenOnHome);
            state.HasEarlierControls = true;
            Assert.True(state.OfferHiddenOnHome);      // „14 controale anterioare” and „Nu există încă date” no longer sit side by side
            Assert.True(state.IsEmpty);                // the other pages (Investigation, Media) still have no data from it
            state.HasEarlierControls = false;
            state.RecordControl("Control stație finalizat.");
            Assert.True(state.OfferHiddenOnHome);
            state.Clear();
            Assert.False(state.OfferHiddenOnHome);
        }
        finally { state.HasEarlierControls = had; state.Clear(); }
        Assert.Contains("OfferHiddenOnHome", Src("Views", "Controls", "NoDataPanel.xaml"));
    }

    [Fact]
    public void Earlier_controls_come_from_the_same_list_as_the_control_page()
    {
        var vm = Src("ViewModels", "StationControlViewModel.cs");
        var body = vm[vm.IndexOf("private void LoadPreviousControls()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("/// <summary>", StringComparison.Ordinal)];
        Assert.Contains("ControlArchive.List(", body);
        Assert.Contains("CollectAllState.Instance.HasEarlierControls = list.Count > 0;", body);
    }

    private sealed class FakeQuick : IStationQuickControl
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public bool IsBusy => false;
        public string Status => "";
        public string Summary => "";
        public bool HasResult => false;
        public string AnchorLine => "";
        public string PlannedPeriod => "";
        public IReadOnlyList<StatusCount> ResultCounts => [];
        public string PreviousAnchorCode { get; set; } = "";
        public Task RunQuickAsync() => Task.CompletedTask;
        public void OpenReport() { }
        public void SignAsOfficer() { }
        public void ExportForOfficer() { }
        public void ExportSignedMediaHistory() { }
    }

    private HomeViewModel Home(IStationQuickControl? quick)
    {
        var control = RoleProfiles.For(StationRole.Control, new UnclassifiedEditionProfile(), AppMode.AirGapped);
        return new HomeViewModel(new InvestigationViewModel(), _ => { }, new RecentCases(Path.Combine(_dir, "recent.json")), () => null, control, () => null, quick);
    }

    [Fact]
    public void Home_in_simple_has_one_station_control_button_and_no_second_card_for_the_same_action()
    {
        var vm = Home(new FakeQuick());
        Assert.True(vm.ShowQuickControl);
        Assert.DoesNotContain(vm.ShownPrimaryIntents, i => i.Key == "check_station");
        Assert.Contains(vm.PrimaryIntents, i => i.Key == "check_station");   // the role data is unchanged
        Assert.Contains("Verifică un suport (USB, CD/DVD)", vm.ShownPrimaryIntents.Select(i => i.Title));
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        Assert.Contains(vm.ShownPrimaryIntents, i => i.Key == "check_station");   // Expert: as today
        LanguageLevelContext.Set(UiLanguageLevel.Simple);
        Assert.DoesNotContain(vm.ShownPrimaryIntents, i => i.Key == "check_station");
    }

    [Fact]
    public void Home_without_the_quick_control_keeps_the_card_because_it_is_then_the_only_way_to_the_control()
    {
        var vm = Home(null);
        Assert.False(vm.ShowQuickControl);
        Assert.Contains(vm.ShownPrimaryIntents, i => i.Key == "check_station");
    }

    [Fact]
    public void Home_view_binds_the_shown_intents_and_says_in_one_line_what_each_action_does()
    {
        var xaml = Src("Views", "HomeView.xaml");
        Assert.Contains("ItemsSource=\"{Binding ShownPrimaryIntents}\"", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding PrimaryIntents}\"", xaml);
        Assert.Contains("Text=\"{Binding WhatHappensQuickControl}\"", xaml);               // under the big button
        var more = xaml[xaml.IndexOf("Header=\"Alte acțiuni\"", StringComparison.Ordinal)..];
        Assert.Contains("Text=\"{Binding Description}\"", more[..more.IndexOf("</Expander>", StringComparison.Ordinal)]);   // „Alte acțiuni” too
        Assert.Contains("Ce se întâmplă", HomeViewModel.WhatHappensQuickControlText);
        Assert.Contains("doar citește", HomeViewModel.WhatHappensQuickControlText);
    }

    // ───────── B: the anchor code is an optional wizard step ─────────

    [Fact]
    public void Home_no_longer_asks_for_the_anchor_code_first()
    {
        var xaml = Src("Views", "HomeView.xaml");
        Assert.DoesNotContain("PreviousAnchorCode", xaml);
        Assert.DoesNotContain("Codul de ancorare de pe procesul-verbal anterior", xaml);
        Assert.DoesNotContain("anchor_code", xaml);
    }

    [Fact]
    public void The_wizard_asks_for_the_previous_report_in_an_optional_step_before_the_period()
    {
        var vm = new StationControlViewModel();
        Assert.Equal("Pasul 1 din 4", vm.FlowProgress);
        vm.FlowNextCommand.Execute(null);                       // station -> previous report
        Assert.Equal(1, vm.FlowStepIndex);
        Assert.Equal("Aveți procesul-verbal al controlului anterior?", vm.FlowQuestion);
        Assert.Equal("", vm.PreviousAnchorCode);
        vm.FlowNextCommand.Execute(null);                       // left empty: allowed
        Assert.Equal(2, vm.FlowStepIndex);
        Assert.Equal("", vm.FlowError);
        Assert.Contains("perioad", vm.FlowStepTitle, StringComparison.OrdinalIgnoreCase);
        vm.FlowBackCommand.Execute(null);
        Assert.Equal(1, vm.FlowStepIndex);
        vm.PreviousAnchorCode = "AAAAA-BBBBB-CCCCC-DDDDD";
        vm.FlowNextCommand.Execute(null);
        Assert.Equal(2, vm.FlowStepIndex);
        Assert.Equal("AAAAA-BBBBB-CCCCC-DDDDD", vm.PreviousAnchorCode);   // the code is kept for the period and for A11/A13, exactly as before
        Assert.False(vm.PreviousCodeFromStation);
        Assert.Contains("ancor", StationControlViewModel.AnchorWhy);
        Assert.Contains("gol", StationControlViewModel.AnchorWhy);
    }

    [Fact]
    public void The_anchor_step_view_has_the_field_the_why_and_the_help_and_the_other_steps_moved_one_place()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "StationControlView.xaml"));
        var text = doc.ToString().Replace("\r", "");
        Assert.Contains("local:HelpButton Term=\"anchor_code\"", Src("Views", "StationControlView.xaml"));
        Assert.Contains("{Binding AnchorWhyText}", text);
        // the field sits in the step with FlowStepIndex 1
        var field = doc.Descendants().Single(e => e.Name.LocalName == "TextBox" && ((string?)e.Attribute("Text"))?.Contains("{Binding PreviousAnchorCode") == true
                                                  && e.Ancestors().Any(a => a.Name.LocalName == "StackPanel" && a.ToString().Contains("FlowStepIndex")));
        var step = field.Ancestors().First(a => a.Name.LocalName == "StackPanel" && a.ToString().Contains("FlowStepIndex"));
        Assert.Contains("Binding FlowStepIndex}\" Value=\"1\"", step.ToString().Replace("\r", ""));
        Assert.Contains("Binding FlowStepIndex}\" Value=\"2\"", text);
        Assert.Contains("Binding FlowStepIndex}\" Value=\"3\"", text);
    }

    // ───────── C: „ultimul control” has one meaning ─────────

    private static readonly DateTimeOffset Finished = new(2026, 10, 22, 18, 44, 0, TimeSpan.Zero);

    [Fact]
    public void The_control_page_line_shows_the_last_finished_control_in_the_home_format()
    {
        var line = StationControlViewModel.PreviousNoteText(14, Finished, Finished.AddMinutes(-5), 0);
        var expected = ControlPeriodStart.Describe(Finished);
        Assert.Matches(@"\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}, ora stației", expected);
        Assert.Equal($"14 controale anterioare; ultimul control terminat: {expected}.", line);
        Assert.DoesNotContain("cel mai recent", line);
        Assert.DoesNotContain("2026-10", line);
        Assert.DoesNotContain("ultimul raport salvat", line);   // the same control: said once
    }

    [Fact]
    public void A_saved_report_that_differs_from_the_last_finished_control_is_said_separately()
    {
        var saved = Finished.AddHours(-1);
        var line = StationControlViewModel.PreviousNoteText(3, Finished, saved, 0);
        Assert.Contains($"ultimul control terminat: {ControlPeriodStart.Describe(Finished)}", line);
        Assert.Contains($"ultimul raport salvat: {ControlPeriodStart.Describe(saved)}", line);
        Assert.DoesNotContain("2026-10", line);
    }

    [Fact]
    public void Without_a_trusted_finished_control_only_the_saved_report_is_named_and_unreadable_folders_are_counted()
    {
        var line = StationControlViewModel.PreviousNoteText(2, null, Finished, 1);
        Assert.Contains("ultimul raport salvat: " + ControlPeriodStart.Describe(Finished), line);
        Assert.Contains("Jurnalul sigilat nu are un control terminat verificabil", line);
        Assert.Contains("Foldere necitite: 1", line);
        Assert.Equal("Niciun control anterior salvat pe această stație.", StationControlViewModel.PreviousNoteText(0, null, null, 0));
        var finishedOnly = StationControlViewModel.PreviousNoteText(0, Finished, null, 0);
        Assert.Contains("ultimul control terminat: " + ControlPeriodStart.Describe(Finished), finishedOnly);
    }

    [Fact]
    public void The_control_page_reads_the_last_finished_control_from_the_same_trusted_source_as_home()
    {
        var vm = Src("ViewModels", "StationControlViewModel.cs");
        Assert.Contains("ControlPeriodStart.LastFinished(", vm);
        Assert.Contains("PreviousNoteText(", vm);
    }

    // ───────── D: the investigation page ─────────

    private static XElement Wizard(XDocument doc) =>
        doc.Descendants().First(e => e.Name.LocalName == "Border" && e.Elements().Any(c => c.Name.LocalName == "StackPanel") && e.Descendants().Any(d => (string?)d.Attribute("Text") == "{Binding IntakeProgress}"));

    [Fact]
    public void In_simple_the_investigation_page_shows_only_the_wizard_and_expert_keeps_the_full_form()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "InvestigationView.xaml"));
        var form = doc.Descendants().Single(e => e.Name.LocalName == "Border" && e.Elements().Any(c => c.Name.LocalName == "Grid") && e.Descendants().Any(d => (string?)d.Attribute("Text") is { } t && t.Contains("{Binding CaseName")));
        var formXml = form.ToString();
        Assert.Contains("LanguageLevelState", formXml);
        Assert.Contains("IsSimple", formXml);
        Assert.Contains("Collapsed", formXml);
        // the wizard card stays in both levels
        var wizardOwnStyle = string.Concat(Wizard(doc).Elements().Where(e => e.Name.LocalName == "Border.Style").Select(e => e.ToString()));
        Assert.DoesNotContain("IsSimple", wizardOwnStyle);
    }

    [Fact]
    public void The_wizard_last_step_has_every_required_scope_field_in_simple_and_the_form_pointer_only_in_expert()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "InvestigationView.xaml"));
        var wizard = Wizard(doc);
        var bindings = string.Concat(wizard.DescendantsAndSelf().SelectMany(e => e.Attributes()).Select(a => a.Value + "|"));
        foreach (var field in new[] { "ScopePurpose", "ScopeApprover", "ScopeSystems", "ScopeFrom", "ScopeTo", "LegalBasisIndex", "NetworkIndex", "ClassificationIndex", "CaseName" })
            Assert.Contains("{Binding " + field, bindings);
        // everything CaseScope.MissingFields requires is offered in the wizard: purpose, period from/to, systems, approver, legal basis, network, classification
        Assert.Equal(8, new LogAnalyzer.Dfir.Model.CaseScope().MissingFields().Count);
        // the running analysis can be stopped from the wizard too (the form's „Oprește” is hidden in Simplu)
        Assert.Contains("{Binding CancelCommand}", bindings);
        // the pointer to the form below is Expert-only
        var pointer = wizard.Descendants().Single(e => ((string?)e.Attribute("Text"))?.Contains("formularul de mai jos") == true);
        Assert.Contains("IsExpert", pointer.ToString());
    }

    [Fact]
    public void Empty_panels_before_a_run_are_replaced_by_a_short_sentence_in_simple()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "InvestigationView.xaml"));
        Assert.Contains("Rezultatele apar aici după ce porniți investigația.", Src("Views", "InvestigationView.xaml"));
        var tabsXml = doc.Descendants().Single(e => e.Name.LocalName == "TabControl").ToString();
        Assert.Contains("HasResult", tabsXml);
        Assert.Contains("IsSimple", tabsXml);
        // the log box: the form's own (hidden in Simplu with the form) and one under the wizard that shows only with content
        var logBoxes = doc.Descendants().Where(e => e.Name.LocalName == "TextBox" && (string?)e.Attribute("Text") is { } t && t.Contains("{Binding Log")).ToList();
        Assert.Equal(2, logBoxes.Count);
        Assert.Contains(logBoxes, b => b.ToString().Contains("IsSimple"));
        Assert.False(new InvestigationViewModel().HasResult);
    }

    [Fact]
    public void The_wizard_names_the_missing_scope_fields_in_romanian_not_in_english()
    {
        var all = new LogAnalyzer.Dfir.Model.CaseScope().MissingFields();
        foreach (var f in all)
        {
            var ro = InvestigationViewModel.ScopeFieldRo(f);
            Assert.NotEqual(f, ro);
            Assert.Matches("^[a-zăâîșț ()]+$", ro);
        }
    }

    // ───────── the real views, loaded with the application's theme ─────────

    private static bool EffectivelyVisible(System.Windows.DependencyObject? e)
    {
        for (var d = e; d is not null; d = System.Windows.LogicalTreeHelper.GetParent(d) ?? (d is System.Windows.FrameworkElement fe ? fe.TemplatedParent : null))
            if (d is System.Windows.UIElement u && u.Visibility != System.Windows.Visibility.Visible) return false;
        return e is not null;
    }

    private static IEnumerable<System.Windows.DependencyObject> LogicalDescendants(System.Windows.DependencyObject root)
    {
        foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            yield return c;
            foreach (var d in LogicalDescendants(c)) yield return d;
        }
    }

    private static T? FindByName<T>(System.Windows.DependencyObject root, string automationName) where T : System.Windows.FrameworkElement =>
        LogicalDescendants(root).OfType<T>().FirstOrDefault(t => System.Windows.Automation.AutomationProperties.GetName(t) == automationName);

    private static void Layout(System.Windows.FrameworkElement v)
    {
        v.Measure(new System.Windows.Size(1400, 900)); v.Arrange(new System.Windows.Rect(0, 0, 1400, 900)); v.UpdateLayout();
    }

    [Fact]
    public void The_real_investigation_view_shows_the_wizard_and_one_sentence_in_simple_and_the_full_form_in_expert()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            foreach (var expert in new[] { false, true })
            {
                LanguageLevelContext.Set(expert ? UiLanguageLevel.Expert : UiLanguageLevel.Simple);
                var view = new LogAnalyzer.UI.Views.InvestigationView { DataContext = new InvestigationViewModel() };
                Layout(view);
                var formLog = FindByName<System.Windows.Controls.TextBox>(view, "Jurnalul investigației");
                var wizardLog = FindByName<System.Windows.Controls.TextBox>(view, "Jurnalul investigației (progres)");
                var tabs = FindByName<System.Windows.Controls.TabControl>(view, "Rezultatele investigației");
                var emptyState = LogicalDescendants(view).OfType<System.Windows.Controls.TextBlock>().Single(t => t.Text == "Rezultatele apar aici după ce porniți investigația.");
                Assert.NotNull(formLog); Assert.NotNull(wizardLog); Assert.NotNull(tabs);
                Assert.Equal(expert, EffectivelyVisible(formLog));          // the full form (its log box is part of it): Expert only
                Assert.False(EffectivelyVisible(wizardLog));                // nothing to say before a run, in both levels
                Assert.Equal(expert, EffectivelyVisible(tabs));              // empty result tabs: Expert only
                Assert.Equal(!expert, EffectivelyVisible(emptyState));       // Simplu: one sentence instead
                var wizardTitle = LogicalDescendants(view).OfType<System.Windows.Controls.TextBlock>().First(t => t.Text == "De unde vin probele?");
                Assert.True(EffectivelyVisible(wizardTitle));                // the wizard stays in both levels
            }
            return 0;
        });
    }

    [Fact]
    public void The_real_home_view_hides_the_empty_offer_in_simple_and_when_earlier_controls_exist()
    {
        var state = CollectAllState.Instance;
        var had = state.HasEarlierControls;
        try
        {
            Wp18AccessibilityTests.OnSta(() =>
            {
                state.Clear();
                var control = RoleProfiles.For(StationRole.Control, new UnclassifiedEditionProfile(), AppMode.AirGapped);
                foreach (var (expert, earlier, shown) in new[] { (false, false, false), (true, false, true), (true, true, false) })
                {
                    LanguageLevelContext.Set(expert ? UiLanguageLevel.Expert : UiLanguageLevel.Simple);
                    state.HasEarlierControls = earlier;
                    var vm = new HomeViewModel(new InvestigationViewModel(), _ => { }, new RecentCases(Path.Combine(_dir, "recent.json")), () => null, control, () => null, new FakeQuick());
                    var view = new LogAnalyzer.UI.Views.HomeView { DataContext = vm };
                    Layout(view);
                    var panel = LogicalDescendants(view).OfType<LogAnalyzer.UI.Views.Controls.NoDataPanel>().Single();
                    var offer = LogicalDescendants(panel).OfType<System.Windows.Controls.Button>().Single();
                    Assert.Equal(shown, EffectivelyVisible(offer));
                    // the one main action is there in both levels, with its one-line explanation
                    Assert.True(EffectivelyVisible(FindByName<System.Windows.Controls.Button>(view, "Fă controlul stației")));
                    Assert.True(EffectivelyVisible(LogicalDescendants(view).OfType<System.Windows.Controls.TextBlock>().Single(t => t.Text == HomeViewModel.WhatHappensQuickControlText)));
                    // the anchor code is not on Home any more
                    Assert.DoesNotContain(LogicalDescendants(view).OfType<System.Windows.Controls.TextBox>(), t => System.Windows.Automation.AutomationProperties.GetName(t).Contains("controlul anterior"));
                }
                return 0;
            });
        }
        finally { state.HasEarlierControls = had; state.Clear(); }
    }

    [Fact]
    public void The_real_control_page_shows_the_anchor_question_only_on_its_step_and_hides_the_offer_in_simple()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            LanguageLevelContext.Set(UiLanguageLevel.Simple);
            CollectAllState.Instance.Clear();
            var vm = new StationControlViewModel();
            var view = new LogAnalyzer.UI.Views.StationControlView { DataContext = vm };
            Layout(view);
            var field = FindByName<System.Windows.Controls.TextBox>(view, "Codul de ancorare de la controlul anterior, de pe procesul-verbal semnat");
            Assert.NotNull(field);
            Assert.False(EffectivelyVisible(field));              // step 1 of 4 is the station
            vm.FlowNextCommand.Execute(null); Layout(view);
            Assert.True(EffectivelyVisible(field));               // step 2: the optional previous proces-verbal
            Assert.True(EffectivelyVisible(LogicalDescendants(view).OfType<System.Windows.Controls.TextBlock>().First(t => t.Text == StationControlViewModel.AnchorWhy)));
            vm.FlowNextCommand.Execute(null); Layout(view);
            Assert.False(EffectivelyVisible(field));              // step 3: the period
            Assert.True(EffectivelyVisible(FindByName<System.Windows.Controls.ComboBox>(view, "Perioada controlului")));
            var offer = LogicalDescendants(view).OfType<LogAnalyzer.UI.Views.Controls.NoDataPanel>().Single();
            Assert.False(EffectivelyVisible(LogicalDescendants(offer).OfType<System.Windows.Controls.Button>().Single()));   // Simplu: no „Colectează acum” here
            return 0;
        });
    }

    [Fact]
    public void The_first_run_guide_window_loads_with_the_theme_and_binds_the_first_screen()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            var w = new LogAnalyzer.UI.Views.FirstRunGuideWindow();
            var vm = Assert.IsType<FirstRunGuideViewModel>(w.DataContext);
            Assert.Equal("Ce face aplicația", vm.Title);
            Assert.Single(w.InputBindings.OfType<System.Windows.Input.KeyBinding>(), k => k.Key == System.Windows.Input.Key.Escape);
            w.Measure(new System.Windows.Size(620, 440));
            return 0;
        });
    }

    // ───────── E: the first-run guide ─────────

    [Fact]
    public void The_guide_has_three_screens_with_the_content_the_owner_asked_for()
    {
        var s = FirstRunGuideViewModel.Screens;
        Assert.Equal(3, s.Count);
        Assert.Contains("doar citește", string.Join(" ", s[0].Lines));
        Assert.Contains("nu modifică", string.Join(" ", s[0].Lines));
        foreach (var w in new[] { "control", "raport", "procesul-verbal", "cod de ancorare" }) Assert.Contains(w, string.Join(" ", s[1].Lines), StringComparison.OrdinalIgnoreCase);
        foreach (var w in new[] { "Ce înseamnă?", "Simplu", "Expert" }) Assert.Contains(w, string.Join(" ", s[2].Lines));
        Assert.All(s, x => Assert.False(string.IsNullOrWhiteSpace(x.Title)));
    }

    [Fact]
    public void The_guide_walks_forward_and_back_and_the_last_screen_closes()
    {
        var vm = new FirstRunGuideViewModel(new LanguagePreferences(_dir));
        Assert.Equal(0, vm.Index); Assert.False(vm.CanGoBack); Assert.False(vm.IsLast);
        Assert.Equal("Ecranul 1 din 3", vm.Progress);
        Assert.Equal("Înainte", vm.NextText);
        vm.Next(); vm.Next();
        Assert.Equal(2, vm.Index); Assert.True(vm.IsLast); Assert.Equal("Am înțeles", vm.NextText);
        vm.Back();
        Assert.Equal(1, vm.Index);
        vm.Next();
        var closed = 0; vm.CloseRequested += () => closed++;
        vm.Next();                        // „Am înțeles”
        Assert.Equal(1, closed); Assert.True(vm.Closed);
    }

    [Fact]
    public void The_seen_flag_is_kept_per_windows_user_in_the_existing_preferences_store()
    {
        var prefs = new LanguagePreferences(_dir);
        Assert.False(prefs.FirstRunGuideSeen());
        Assert.True(prefs.MarkFirstRunGuideSeen());
        Assert.True(new LanguagePreferences(_dir).FirstRunGuideSeen());          // a new instance (a new start) reads it back
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LanguagePreferences.DefaultDirectory());   // per Windows user
        // the language level of an account is untouched by the flag
        Assert.True(prefs.Save("administrator.principal", UiLanguageLevel.Expert));
        Assert.True(prefs.FirstRunGuideSeen());
        Assert.Equal(UiLanguageLevel.Expert, prefs.Load("administrator.principal"));
    }

    [Fact]
    public void The_guide_is_offered_once_and_does_not_block_when_it_cannot_be_shown()
    {
        var prefs = new LanguagePreferences(_dir);
        var shown = 0;
        var old = FirstRunGuideLauncher.Show;
        FirstRunGuideLauncher.Show = () => shown++;
        try
        {
            Assert.True(FirstRunGuideLauncher.ShowIfFirstTime(prefs));
            Assert.Equal(1, shown);
            Assert.False(FirstRunGuideLauncher.ShowIfFirstTime(prefs));          // not again at the next start
            Assert.Equal(1, shown);
            Assert.True(prefs.FirstRunGuideSeen());
            // a failure to show never stops the start: it is swallowed and the guide stays reachable from „Ce înseamnă?”
            var other = new LanguagePreferences(Path.Combine(_dir, "second"));
            FirstRunGuideLauncher.Show = () => throw new InvalidOperationException("no window");
            Assert.False(FirstRunGuideLauncher.ShowIfFirstTime(other));
        }
        finally { FirstRunGuideLauncher.Show = old; }
    }

    [Fact]
    public void Dont_show_again_closes_without_blocking_and_escape_closes_the_modeless_window()
    {
        var vm = new FirstRunGuideViewModel(new LanguagePreferences(_dir));
        var closed = 0; vm.CloseRequested += () => closed++;
        vm.DontShowAgain();
        Assert.Equal(1, closed);
        Assert.True(new LanguagePreferences(_dir).FirstRunGuideSeen());
        var xaml = Src("Views", "FirstRunGuideWindow.xaml");
        Assert.Contains("Key=\"Escape\"", xaml);
        Assert.Contains("Nu mai arăta", xaml);
        var code = Src("Views", "FirstRunGuideWindow.xaml.cs");
        Assert.DoesNotContain("ShowDialog", code);    // modeless: it never blocks the window behind it
        Assert.Contains(".Show()", code);
    }

    [Fact]
    public void The_guide_can_be_reopened_from_the_glossary_window_and_from_home_and_starts_with_the_main_window()
    {
        Assert.Contains("FirstRunGuideLauncher.Show", Src("Views", "GlossaryWindow.xaml.cs"));
        Assert.Contains("Click=\"Guide_Click\"", Src("Views", "GlossaryWindow.xaml"));
        Assert.Contains("{Binding OpenGuideCommand}", Src("Views", "HomeView.xaml"));
        Assert.Contains("FirstRunGuideLauncher.Show", Src("ViewModels", "HomeViewModel.cs"));
        Assert.Contains("FirstRunGuideLauncher.ShowIfFirstTime", Src("MainWindow.xaml.cs"));
    }
}
