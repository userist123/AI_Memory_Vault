using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner decision 2026-10-22 on the cold-start report (docs/utilizare/PORNIRE_INCEPATOR_2026-10-22.md, rows 4, 7, 8, 9, 12, 14, 16-20 and the investigation
/// wizard): the polish pass. View-model and XAML level, like <see cref="BeginnerPath20261022Tests"/>.
/// </summary>
[Collection(GlobalStateCollection.Name)]
public sealed class BeginnerPolish20261022Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-polish-" + Guid.NewGuid().ToString("N"));
    public BeginnerPolish20261022Tests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));
    private static XDocument Xaml(params string[] parts) => XDocument.Load(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));
    private static string Attr(XElement e, string name) => (string?)e.Attribute(name) ?? "";

    // ───────── 1: the investigation wizard asks where the evidence comes from ─────────

    [Fact]
    public void The_wizard_step_one_offers_this_station_or_a_brought_folder_and_the_folder_is_the_default()
    {
        var inv = new InvestigationViewModel();
        Assert.True(inv.IntakeFromFolder);
        Assert.False(inv.IntakeFromThisStation);
        inv.IntakeFromThisStation = true;
        Assert.False(inv.IntakeFromFolder);                // one choice: the two are never both on
        inv.IntakeFromFolder = true;
        Assert.False(inv.IntakeFromThisStation);
        var xaml = Src("Views", "InvestigationView.xaml");
        Assert.Contains("Această stație (colectez acum de aici)", xaml);
        Assert.Contains("Un folder adus de la stația afectată", xaml);
        Assert.Contains("IsChecked=\"{Binding IntakeFromThisStation}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding IntakeFromFolder}\"", xaml);
    }

    [Fact]
    public void Choosing_this_station_collects_here_without_a_folder_and_goes_straight_to_the_scope()
    {
        var inv = new InvestigationViewModel { IntakeFromThisStation = true };
        inv.IntakeNextCommand.Execute(null);                // no folder needed
        Assert.Equal("", inv.IntakeError);
        Assert.Equal(2, inv.IntakeStepIndex);               // the „what was found” step only exists for a folder
        Assert.Equal("Pasul 3 din 3", inv.IntakeProgress);
        Assert.True(inv.CollectFromThisStation);             // the same switch the Expert form has
        Assert.Empty(inv.ImportFiles);
        inv.IntakeBackCommand.Execute(null);                 // back skips the folder scan too
        Assert.Equal(0, inv.IntakeStepIndex);
    }

    [Fact]
    public void This_station_still_requires_every_scope_field_before_the_run()
    {
        var inv = new InvestigationViewModel { IntakeFromThisStation = true };
        inv.IntakeNextCommand.Execute(null);
        inv.IntakeNextCommand.Execute(null);                 // scope step with nothing filled in: refused, nothing runs
        Assert.Equal(2, inv.IntakeStepIndex);
        Assert.Contains("câmpuri lipsă", inv.IntakeError);
        Assert.False(inv.IsBusy);
    }

    [Fact]
    public void The_folder_row_of_step_one_is_shown_only_for_the_folder_choice()
    {
        var xaml = Src("Views", "InvestigationView.xaml");
        var at = xaml.IndexOf("Alege folderul…", StringComparison.Ordinal);
        Assert.True(at > 0);
        var before = xaml[..at];
        Assert.Contains("IntakeFromFolder", before[before.LastIndexOf("<StackPanel", StringComparison.Ordinal)..]);
    }

    // ───────── 2: the menu entry and the page title are one name ─────────

    private static readonly (int Tab, string Name, string File)[] PageTitles =
    [
        (0, "Panou general", "DashboardView.xaml"), (3, "Amenințări și detecții", "AlertsAndRulesView.xaml"), (4, "Cronologia evenimentelor", "IncidentTimelineView.xaml"),
        (5, "Analiza riscului", "RiskAnalysisView.xaml"), (6, "Firul atacului", "AttackStorylineView.xaml"), (7, "Informații despre amenințări", "ThreatIntelView.xaml"),
        (8, "Conturi locale și USB", "KerberosAndLateralMovementView.xaml"), (8, "Active Directory", "ActiveDirectoryAuditView.xaml"), (9, "Istoricul probelor", "ChainOfCustodyView.xaml"),
        (10, "Tehnici de atac (MITRE ATT&CK)", "MitreMatrixView.xaml"), (12, "Colectare date", "DataCollectionView.xaml"), (14, "Control stație", "StationControlView.xaml"),
        (16, "Investigație completă (caz)", "InvestigationView.xaml"), (17, "Politici", "PolicyView.xaml"), (18, "Profil de proceduri", "ProcedureProfileView.xaml"),
        (19, "Monitor dispozitive", "MediaCenterView.xaml"), (21, "Autentificare și conturi", "AuthView.xaml"), (23, "Registrul de transferuri", "TransferRegisterView.xaml"),
    ];

    private static string PageTitle(string file)
    {
        var title = Xaml("Views", file).Descendants().First(e => e.Name.LocalName == "TextBlock" && double.TryParse(Attr(e, "FontSize"), System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 20 && Attr(e, "Text").Length > 0);
        return Attr(title, "Text");
    }

    [Fact]
    public void Every_page_title_is_the_name_of_its_menu_entry()
    {
        foreach (var (tab, name, file) in PageTitles) Assert.True(name == PageTitle(file), $"tab {tab}: title of {file} is „{PageTitle(file)}”, the menu says „{name}”");
        Assert.Equal("Registru utilizatori", new UsersRegisterViewModel(Path.Combine(_dir, "u.json")).Title);
    }

    [Fact]
    public void The_menus_use_the_same_names_as_the_titles()
    {
        var main = Xaml("MainWindow.xaml");
        var avansat = main.Descendants().Where(e => e.Name.LocalName == "RadioButton").Select(e => (Tab: Attr(e, "CommandParameter"), Name: Attr(e, "Content"))).Where(x => x.Name.Length > 0 && !x.Name.StartsWith('{')).ToList();
        foreach (var (tab, name, _) in PageTitles)
        {
            var labels = avansat.Where(x => x.Tab == tab.ToString()).Select(x => x.Name).ToList();
            if (labels.Count > 0) Assert.Contains(name, labels);
        }
        foreach (var role in new[] { StationRole.Control, StationRole.Csirt })
            foreach (var entry in RoleProfiles.For(role, new UnclassifiedEditionProfile(), AppMode.Network).Navigation)
                foreach (var (tab, name, _) in PageTitles.Where(p => p.Tab == entry.Tab && p.Tab != 8))
                    Assert.True(name == entry.Title, $"{role}: menu „{entry.Title}” vs title „{name}”");
        Assert.Contains("Title=\"Investigație domeniu și e-mail\"", main.ToString());
        Assert.Contains("Title=\"Izolare procese suspecte\"", main.ToString());
    }

    [Fact]
    public void The_long_explanation_of_the_policy_page_is_the_subtitle_not_the_title()
    {
        var xaml = Src("Views", "PolicyView.xaml");
        Assert.DoesNotContain("Text=\"Politici — import, aprobare, aplicare verificată\" FontSize", xaml);
        Assert.Contains("Importați, aprobați și aplicați politici", xaml);
    }

    // ───────── 3: the status bar ─────────

    [Fact]
    public void The_status_bar_does_not_carry_the_permanent_standalone_containment_notice()
    {
        var xaml = Src("MainWindow.xaml");
        Assert.DoesNotContain("nu este disponibilă în ediția Standalone", xaml);
        var bar = xaml[xaml.IndexOf("32PX RESTRAINED BOTTOM STATUS BAR", StringComparison.Ordinal)..];
        bar = bar[..bar.IndexOf("GLOBAL OVERLAYS", StringComparison.Ordinal)];
        Assert.Contains("HasContainment", bar);              // the switch is shown only where the edition has it
        Assert.Contains("Collapsed", bar);
    }

    [Fact]
    public void No_analysis_yet_is_not_said_when_there_are_earlier_controls_or_recent_cases()
    {
        Assert.Equal("Control stație finalizat.", MainViewModel.NoAnalysisLine("Control stație finalizat.", true, 3));
        Assert.Equal("Nicio analiză încă.", MainViewModel.NoAnalysisLine("", false, 0));
        var withHistory = MainViewModel.NoAnalysisLine("", true, 0);
        Assert.DoesNotContain("Nicio analiză încă", withHistory);
        Assert.Contains("Acasă", withHistory);
        Assert.DoesNotContain("Nicio analiză încă", MainViewModel.NoAnalysisLine("", false, 2));
        var vm = Src("ViewModels", "MainViewModel.cs");
        Assert.Contains("NoAnalysisLine(", vm[vm.IndexOf("announce", StringComparison.Ordinal)..]);
    }

    // ───────── 4: the intake buttons only where a table is ─────────

    [Fact]
    public void The_load_and_export_buttons_are_for_the_pages_with_an_event_table_only()
    {
        foreach (var tab in new[] { 1, 3, 4 }) Assert.True(MainViewModel.IsEventTablePage(tab), $"tab {tab}");
        foreach (var tab in new[] { 22, 14, 17, 18, 19, 20, 21, 23, 16, 12, 13 }) Assert.False(MainViewModel.IsEventTablePage(tab), $"tab {tab}");
        var xaml = Xaml("MainWindow.xaml");
        foreach (var content in new[] { "Încarcă un folder de probe", "Încarcă jurnale Windows", "Exportă tabelul (CSV)", "Exportă raportul" })
        {
            var button = xaml.Descendants().First(e => e.Name.LocalName == "Button" && Attr(e, "Content") == content);
            Assert.True(button.AncestorsAndSelf().Any(a => Attr(a, "Visibility").Contains("ShowEventTableButtons")), content);
        }
    }

    // ───────── 5, 18: empty states, no English in „Simplu”, subtitle fixes ─────────

    [Fact]
    public void An_empty_users_register_is_empty_and_a_filled_one_is_not()
    {
        var vm = new UsersRegisterViewModel(Path.Combine(_dir, "users.json"));
        Assert.True(vm.IsEmpty);
        vm.Data.Rows.Add("Ion Popescu", @"DOM\ion", "", "", "", "", "", "");
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void An_empty_profile_is_empty_and_a_filled_one_is_not()
    {
        var vm = new ProcedureProfileViewModel(new ProfileProvider(Path.Combine(_dir, "p.json")));
        Assert.True(vm.IsEmpty);
        vm.Tables.First(t => t.Table == ProfileTable.Holidays).Data.Rows.Add("2026-12-25");
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Nothing_open_in_the_policy_page_is_a_state_of_its_own()
    {
        Assert.True(new PolicyViewModel().NothingOpen);
    }

    [Fact]
    public void Each_empty_state_says_what_it_is_why_it_matters_and_the_first_step()
    {
        foreach (var (what, why, step) in new[] { (EmptyStates.UsersWhat, EmptyStates.UsersWhy, EmptyStates.UsersStep), (EmptyStates.ProfileWhat, EmptyStates.ProfileWhy, EmptyStates.ProfileStep), (EmptyStates.PolicyWhat, EmptyStates.PolicyWhy, EmptyStates.PolicyStep) })
        {
            Assert.True(what.Length > 20); Assert.True(step.Length > 10);
            Assert.Contains("NEDETERMINAT", why);            // which checks stay undetermined while the page is empty
            Assert.DoesNotMatch(new Regex("[A-Za-z]+\\(|\\([A-Z][a-z]+\\)"), what + why + step);
        }
        Assert.Contains("Preia de pe stație", EmptyStates.UsersStep);
        Assert.Contains("Importă", EmptyStates.ProfileStep);
        Assert.Contains("Deschide fișier", EmptyStates.PolicyStep);
    }

    [Theory]
    [InlineData("RegisterView.xaml", "RegisterPath")]
    [InlineData("ProcedureProfileView.xaml", "ProfilePath")]
    [InlineData("PolicyView.xaml", "StoreRoot")]
    public void The_empty_panel_is_shown_and_the_file_path_moves_under_an_advanced_expander(string view, string pathProperty)
    {
        var doc = Xaml("Views", view);
        var card = doc.Descendants().First(e => e.Name.LocalName == "EmptyStateCard");
        Assert.Matches("IsEmpty|NothingOpen", card.Parent!.ToString());
        var box = doc.Descendants().First(e => e.Name.LocalName == "TextBox" && Attr(e, "Text").Contains(pathProperty));
        var expander = box.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Expander");
        Assert.NotNull(expander);
        Assert.Equal("Avansat", Attr(expander!, "Header"));
    }

    private static readonly string[] English = ["Person", "Accounts", "Sids", "Clearance", "NeedToKnow", "ZonesAllowed", "(Day)", "Mon..Sun", "Start/End", "(Name)", "(Date)", "(Account)", "(Kind)", "(Publisher)", "(PathPattern", "(PolicyPath)"];

    [Fact]
    public void In_simple_the_help_texts_have_no_english_field_names_and_expert_keeps_them()
    {
        var users = new UsersRegisterViewModel(Path.Combine(_dir, "h.json"));
        foreach (var e in English) Assert.DoesNotContain(e, users.Hint);
        foreach (var t in ProfileTables.All)
            foreach (var e in English) Assert.DoesNotContain(e, new ProfileTableViewModel(t).Hint);
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        Assert.Contains("NeedToKnow", users.Hint);
        Assert.Contains("Mon..Sun", new ProfileTableViewModel(ProfileTable.WorkingHours).Hint);
    }

    [Fact]
    public void The_users_register_subtitle_starts_with_a_capital_and_registru_nedefinit_is_said_once()
    {
        var vm = new UsersRegisterViewModel(Path.Combine(_dir, "n.json"));
        Assert.True(char.IsUpper(vm.Notice[0]), vm.Notice);
        var subtitle = Src("Views", "RegisterView.xaml");
        var all = vm.Status + " " + vm.Definition + " " + subtitle;
        Assert.Equal(1, Regex.Matches(all, "registru nedefinit", RegexOptions.IgnoreCase).Count);
    }

    [Fact]
    public void The_policy_subtitle_has_a_space_after_the_period_and_no_english_in_simple()
    {
        var doc = Xaml("Views", "PolicyView.xaml");
        var texts = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock" && Attr(e, "Text").Contains("CIORNĂ")).ToList();
        // 2026-10-23: Simplu has no states line any more (BeginnerSimplify20261023Tests); only the Expert line, with the English names, remains
        Assert.Single(texts);
        foreach (var t in texts) { Assert.DoesNotContain(").Aprobarea", Attr(t, "Text")); Assert.Matches("\\. Aprobarea", Attr(t, "Text")); }
        Assert.Single(texts, t => Attr(t, "Text").Contains("DRAFT"));
    }

    // ───────── 7: „Cont nou” and the CRL text ─────────

    [Fact]
    public void The_new_account_boxes_have_visible_labels_and_the_crl_text_is_plain()
    {
        var doc = Xaml("Views", "AuthView.xaml");
        var texts = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").Select(e => Attr(e, "Text")).ToList();
        Assert.Contains("Contul (ex. DOMENIU\\utilizator)", texts);
        Assert.Contains("Numele persoanei", texts);
        var crl = doc.Descendants().First(e => e.Name.LocalName == "CheckBox" && Attr(e, "IsChecked").Contains("MissingCrlRefuse"));
        Assert.DoesNotContain("Standalone refuză", Attr(crl, "Content"));
        Assert.DoesNotContain("neclasificată avertizează", Attr(crl, "Content"));
        Assert.Contains("lista cardurilor retrase", Attr(crl, "Content"));
        var root = doc.Descendants().First(e => e.Name.LocalName == "StackPanel");
        Assert.Matches("^24,24,24,\\d{2,}$", Attr(root, "Margin"));   // room under the last button, nothing cut at the bottom of the scroll
    }

    // ───────── 8: the Avansat menu ─────────

    [Fact]
    public void Avansat_lists_only_the_pages_that_are_not_already_main_pages()
    {
        var control = RoleProfiles.For(StationRole.Control, new UnclassifiedEditionProfile(), AppMode.AirGapped).Navigation;
        Assert.False(AdvancedNav.IsShown(control, RoleProfiles.TabStationControl));
        Assert.False(AdvancedNav.IsShown(control, RoleProfiles.TabPolicies));
        Assert.False(AdvancedNav.IsShown(control, RoleProfiles.TabMediaRegister));
        Assert.True(AdvancedNav.IsShown(control, 0));
        Assert.True(AdvancedNav.IsShown(control, 10));
        var csirt = RoleProfiles.For(StationRole.Csirt, new UnclassifiedEditionProfile(), AppMode.AirGapped).Navigation;
        Assert.False(AdvancedNav.IsShown(csirt, RoleProfiles.TabTimeline));
        Assert.True(AdvancedNav.IsShown(csirt, RoleProfiles.TabStationControl) == false);
        Assert.True(AdvancedNav.IsShown(csirt, RoleProfiles.TabPolicies));
    }

    [Fact]
    public void Every_advanced_entry_is_filtered_trimmed_with_a_tooltip_and_the_list_scrolls_inside()
    {
        var doc = Xaml("MainWindow.xaml");
        var expander = doc.Descendants().First(e => e.Name.LocalName == "Expander" && Attr(e, "Header").StartsWith("Avansat"));
        var buttons = expander.Descendants().Where(e => e.Name.LocalName == "RadioButton").ToList();
        Assert.True(buttons.Count >= 20);
        foreach (var b in buttons) Assert.Contains("AdvancedNav", Attr(b, "Visibility") + b.ToString(), StringComparison.Ordinal);
        Assert.Contains(expander.Descendants(), e => e.Name.LocalName == "ScrollViewer" && Attr(e, "MaxHeight").Length > 0);
        var nav = Src("Themes", "Components", "Navigation.xaml");
        Assert.Contains("TextTrimming", nav);
        Assert.Contains("CharacterEllipsis", nav);
        Assert.Contains("ToolTip", nav);
    }

    // ───────── 9: transfers register ─────────

    [Fact]
    public void Stored_transfer_dates_are_shown_as_dd_MM_yyyy_HH_mm_and_the_stored_value_is_untouched()
    {
        Assert.Equal("16.10.2026 18:08", DateDisplay.Transfer("2026-10-16 18:08"));
        Assert.Equal("16.10.2026 18:08", DateDisplay.Transfer("2026-10-16 18:08:30"));
        Assert.Equal("16.10.2026", DateDisplay.Transfer("2026-10-16"));
        Assert.Equal("nu e dată", DateDisplay.Transfer("nu e dată"));
        Assert.Equal("", DateDisplay.Transfer(""));
        var xaml = Src("Views", "TransferRegisterView.xaml");
        var column = xaml[xaml.IndexOf("Header=\"Data\"", StringComparison.Ordinal)..];
        Assert.Contains("Converter", column[..column.IndexOf("/>", StringComparison.Ordinal)]);
        var vm = Src("ViewModels", "TransferViewModel.cs");
        Assert.Contains("ToLocalTime():dd.MM.yyyy HH:mm", vm);
        Assert.DoesNotContain("Connected.ToLocalTime():yyyy-MM-dd", vm);
    }

    [Fact]
    public void A_storage_medium_is_not_called_a_stick_and_the_unregistered_box_does_not_cut_its_last_row()
    {
        Assert.Equal("Mediu de stocare USB", TransferViewModel.UsbMediumLabel);
        Assert.DoesNotContain("Stick / disc USB", Src("ViewModels", "TransferViewModel.cs").Replace("Mediu de stocare USB", ""));
        var xaml = Xaml("Views", "TransferRegisterView.xaml");
        var box = xaml.Descendants().First(e => e.Name.LocalName == "ItemsControl" && Attr(e, "ItemsSource").Contains("UnregisteredConnections"));
        Assert.True(double.Parse(Attr(box, "MaxHeight"), System.Globalization.CultureInfo.InvariantCulture) >= 220);
        Assert.Contains("ScrollViewer", box.ToString());
    }

    // ───────── 10: glossary ─────────

    [Theory]
    [InlineData("jurnal sigilat")]
    [InlineData("air-gap")]
    [InlineData("caz LIVE")]
    [InlineData("ediția Standalone")]
    [InlineData("martor")]
    [InlineData("structură")]
    [InlineData("postura ediției")]
    [InlineData("cod de ancorare")]
    public void The_glossary_explains_the_terms_a_beginner_meets(string term)
    {
        var entry = Glossary.Lookup(term);
        Assert.NotNull(entry);
        Assert.True(entry!.Explanation.Length > 40);
        Assert.Matches("[ăâîșț]|este|se ", entry.Explanation);
    }

    [Fact]
    public void Typing_in_the_glossary_box_filters_the_list_through_a_live_binding()
    {
        var search = new GlossarySearch();
        var all = search.Results.Count;
        Assert.True(all > 50);
        search.Query = "sigilat";
        Assert.InRange(search.Results.Count, 1, all - 1);
        Assert.All(search.Results, t => Assert.True(
            t.Romanian.Contains("sigilat", StringComparison.OrdinalIgnoreCase) || t.Explanation.Contains("sigilat", StringComparison.OrdinalIgnoreCase)
            || t.Spellings.Any(s => s.Contains("sigilat", StringComparison.OrdinalIgnoreCase)) || t.Technical.Contains("sigilat", StringComparison.OrdinalIgnoreCase) || t.English.Contains("sigilat", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("termen", search.Summary);
        search.Query = "";
        Assert.Equal(all, search.Results.Count);
        search.Query = "zzzz-nu-exista";
        Assert.Empty(search.Results);
        Assert.Contains("Niciun", search.Summary);
        var xaml = Src("Views", "GlossaryWindow.xaml");
        Assert.Contains("Text=\"{Binding Query, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding Results}\"", xaml);
    }

    // ───────── 11: monitor columns for non-storage devices ─────────

    [Fact]
    public void Registry_and_registration_cells_are_empty_for_devices_that_are_not_storage_media()
    {
        Assert.Equal("—", DeviceMonitorRow.RegistryCellTextFor(false, "Neînregistrat"));
        Assert.Equal("Neînregistrat", DeviceMonitorRow.RegistryCellTextFor(true, "Neînregistrat"));
        Assert.NotNull(typeof(DeviceMonitorRow).GetProperty("RegistryCellText"));
        var xaml = Src("Views", "MediaCenterView.xaml");
        Assert.Contains("Binding RegistryCellText", xaml);
        Assert.Contains("Nu se cere", Src("ViewModels", "DeviceMonitorViewModel.cs"));   // the tooltip
        Assert.Contains("IsStorageMedium", xaml);
        Assert.Contains("Header=\"Nr. de înregistrare\" Binding=\"{Binding RegistrationNumber", xaml);   // pinned by DeviceMonitorTests
    }
}
