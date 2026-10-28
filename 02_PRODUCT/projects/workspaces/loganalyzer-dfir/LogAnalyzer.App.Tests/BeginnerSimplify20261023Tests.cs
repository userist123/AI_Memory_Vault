using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// 2026-10-23: dense pages simplified in „Simplu”; „Expert” keeps everything. Display only: the real views are loaded with the application's theme and the
/// visibility of columns, tabs, fields and expanders is read at both levels.
/// </summary>
[Collection(GlobalStateCollection.Name)]
public sealed class BeginnerSimplify20261023Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-simplify-" + Guid.NewGuid().ToString("N"));
    public BeginnerSimplify20261023Tests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void Level(bool expert) => LanguageLevelContext.Set(expert ? UiLanguageLevel.Expert : UiLanguageLevel.Simple);

    private static void Layout(FrameworkElement v)
    {
        v.Measure(new Size(1400, 1600)); v.Arrange(new Rect(0, 0, 1400, 1600)); v.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// <summary>Shown to the person: no collapsed ancestor, and no collapsed expander above it.</summary>
    private static bool Shown(DependencyObject? e)
    {
        for (var d = e; d is not null; d = LogicalTreeHelper.GetParent(d) ?? (d is FrameworkElement fe ? fe.TemplatedParent : null))
        {
            if (d is UIElement u && u.Visibility != Visibility.Visible) return false;
            if (d is Expander x && !x.IsExpanded && !ReferenceEquals(d, e)) return false;
        }
        return e is not null;
    }

    private static T Named<T>(DependencyObject root, string automationName) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(t => System.Windows.Automation.AutomationProperties.GetName(t) == automationName);

    private static TextBlock Text(DependencyObject root, string startsWith) =>
        Descendants(root).OfType<TextBlock>().First(t => t.Text.StartsWith(startsWith, StringComparison.Ordinal));

    private static bool HasAncestorExpander(DependencyObject e, string header)
    {
        for (var d = LogicalTreeHelper.GetParent(e); d is not null; d = LogicalTreeHelper.GetParent(d))
            if (d is Expander x && (string?)x.Header == header) return true;
        return false;
    }

    // ───────── the helper itself ─────────

    [Theory]
    [InlineData(true, true, Visibility.Collapsed)]
    [InlineData(false, true, Visibility.Visible)]
    [InlineData(true, false, Visibility.Visible)]
    [InlineData(false, false, Visibility.Collapsed)]
    public void SimpleLevel_gives_each_level_its_own_visibility(bool isSimple, bool hideInSimple, Visibility expected) =>
        Assert.Equal(expected, SimpleLevel.VisibilityFor(isSimple, hideInSimple));

    [Fact]
    public void An_element_collapsed_by_its_own_rule_stays_collapsed_at_both_levels()
    {
        Assert.Equal(Visibility.Collapsed, SimpleLevel.VisibilityFor(false, true, Visibility.Collapsed));
        Assert.Equal(Visibility.Collapsed, SimpleLevel.VisibilityFor(true, false, Visibility.Collapsed));
    }

    // ───────── 1. Monitor dispozitive ─────────

    private static readonly string[] SimpleColumns = ["Stare", "Nume", "Tip", "Serie", "Capacitate", "Ultima conectare", "Nr. de înregistrare", "Alertă", ""];

    [Fact]
    public void Device_monitor_in_Simplu_has_one_sentence_nine_columns_and_only_the_main_list()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: false);
            var view = new MediaCenterView();
            Layout(view);
            var grid = (DataGrid)view.FindName("DevicesGrid");
            Assert.Equal(SimpleColumns, grid.Columns.Where(c => c.Visibility == Visibility.Visible).Select(c => c.Header as string ?? "").ToArray());
            Assert.True(Shown(Text(view, "Dispozitivele externe conectate la stație, câte un rând pe dispozitiv. Roșu = cere atenție.")));
            Assert.False(Shown(Text(view, "Aici apar mediile de stocare")));
            Assert.False(Shown(Text(view, "Orice dispozitiv conectat")));
            var tabs = Descendants(view).OfType<TabControl>().First();
            Assert.Equal(["Monitor în timp real"], tabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => (string)t.Header).ToArray());
            return 0;
        });
    }

    [Fact]
    public void Device_monitor_in_Expert_keeps_every_column_tab_and_paragraph()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: true);
            var view = new MediaCenterView();
            Layout(view);
            var grid = (DataGrid)view.FindName("DevicesGrid");
            Assert.All(grid.Columns, c => Assert.Equal(Visibility.Visible, c.Visibility));
            Assert.Contains("Producător", grid.Columns.Select(c => c.Header as string));
            Assert.Contains("Ultima deconectare", grid.Columns.Select(c => c.Header as string));
            Assert.False(Shown(Text(view, "Dispozitivele externe conectate la stație, câte un rând")));
            Assert.True(Shown(Text(view, "Aici apar mediile de stocare")));
            Assert.True(Shown(Text(view, "Orice dispozitiv conectat")));
            var tabs = Descendants(view).OfType<TabControl>().First();
            var shown = tabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => (string)t.Header).ToArray();
            foreach (var h in new[] { "Monitor în timp real", "Acum", "Toate dispozitivele", "Protecțiile Windows", "Confruntare între stații" }) Assert.Contains(h, shown);
            return 0;
        });
    }

    [Fact]
    public void Switching_the_level_while_a_hidden_tab_is_selected_returns_to_the_first_tab()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: true);
            var view = new MediaCenterView();
            var host = new Window { Content = view, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                host.Show();
                var tabs = Descendants(view).OfType<TabControl>().First();
                System.Windows.Data.BindingOperations.ClearBinding(tabs, Selector.SelectedIndexProperty);   // no view model here: the page's own binding has no source
                tabs.SelectedIndex = 2;
                Level(expert: false);
                var done = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => done.Continue = false);
                System.Windows.Threading.Dispatcher.PushFrame(done);
                Assert.Equal(0, tabs.SelectedIndex);
            }
            finally { host.Close(); }
            return 0;
        });
    }

    // ───────── 2. Registrul de transferuri ─────────

    private static readonly string[] ExtraTransferFields = ["Stația sursă", "Stația destinație", "Funcția persoanei", "Aprobat de", "Numărul de copii transferate", "Observații la transfer"];
    private static readonly string[] CoreTransferFields = ["Direcția transferului", "Data transferului", "Mediul folosit", "Clasificarea conținutului", "Sursa", "Destinația", "Persoana", "Martorul"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Transfer_form_shows_nine_fields_and_the_rest_under_Mai_multe_detalii(bool expert)
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert);
            var view = new TransferRegisterView();
            Layout(view);
            foreach (var f in CoreTransferFields) Assert.True(Shown(Named<FrameworkElement>(view, f)), f);
            Assert.True(Shown(Text(view, "Fișierele transferate")));
            foreach (var f in ExtraTransferFields)
            {
                var el = Named<FrameworkElement>(view, f);
                Assert.True(HasAncestorExpander(el, "Mai multe detalii"), f);
                Assert.Equal(expert, Shown(el));                       // Simplu: collapsed; Expert: open
                if (expert) Assert.True(el.ActualHeight > 0, f + " is not laid out in Expert");
            }
            // the content check: behind „Mai multe detalii” in Simplu, as before in Expert; the PDF report controls stay in both
            var verify = Text(view, "Verifică transferul după conținut");
            Assert.True(HasAncestorExpander(verify, "Mai multe detalii"));
            Assert.Equal(expert, Shown(verify));
            Assert.True(Shown(Descendants(view).OfType<Button>().First(b => (b.Content as string) == "Raportul registrului (PDF)")));
            return 0;
        });
    }

    [Fact]
    public void Transfer_validation_messages_sit_under_the_register_button_in_the_form()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            foreach (var expert in new[] { false, true })
            {
                Level(expert);
                var view = new TransferRegisterView();
                Layout(view);
                var button = Descendants(view).OfType<Button>().First(b => (b.Content as string) == "Înregistrează transferul" && LogicalTreeHelper.GetParent(b) is StackPanel);
                var problems = Named<ItemsControl>(view, "Probleme găsite în registrul de transferuri");
                Assert.Same(LogicalTreeHelper.GetParent(button), LogicalTreeHelper.GetParent(problems));
                Assert.True(Shown(problems));
            }
            return 0;
        });
    }

    // ───────── 3. Autentificare și conturi ─────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Auth_keeps_password_users_and_own_cards_and_puts_the_rest_under_Setari_avansate(bool expert)
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert);
            var view = new AuthView();
            Layout(view);
            foreach (var t in new[] { "Parola administratorului principal", "Utilizatori și carduri", "Cardurile mele" })
                Assert.False(HasAncestorExpander(Text(view, t), "Setări avansate"), t);
            foreach (var t in new[] { "Actualizarea listelor de detecție", "Încredere (offline)", "Parametri", "Audit și integritate" })
            {
                var tb = Text(view, t);
                Assert.True(HasAncestorExpander(tb, "Setări avansate"), t);
                Assert.Equal(expert, Shown(tb));
            }
            Assert.True(Shown(Descendants(view).OfType<Button>().First(b => (b.Content as string) == "Încheie sesiunea și închide aplicația")));
            return 0;
        });
    }

    // ───────── 4. Profil de proceduri ─────────

    [Fact]
    public void Rarely_needed_profile_tables_are_the_five_named_ones()
    {
        var rare = ProfileTables.All.Where(ProfileTableViewModel.IsRareTable).Select(ProfileTables.Title).ToArray();
        Assert.Equal(["Sărbători legale", "Schimburi", "Procedura de rotire a jurnalelor", "Canale de transfer aprobate", "Destinații de rețea autorizate"], rare);
    }

    [Fact]
    public void Profile_selection_leaves_a_hidden_table_when_Simplu_turns_Mai_multe_sectiuni_off()
    {
        Level(expert: false);
        var vm = new ProcedureProfileViewModel(new ProfileProvider(Path.Combine(_dir, "p.json"))) { ShowMoreSections = true };
        vm.SelectedTable = vm.Tables.First(t => t.Table == ProfileTable.Holidays);
        Assert.Equal(ProfileTable.Holidays, vm.SelectedTable!.Table);   // shown while the toggle is on
        vm.ShowMoreSections = false;
        Assert.Same(vm.Tables[0], vm.SelectedTable);                    // hidden now: the first table takes over
        Level(expert: true);
        vm.SelectedTable = vm.Tables.First(t => t.Table == ProfileTable.Shifts);
        vm.EnsureSelectedTabVisible();
        Assert.Equal(ProfileTable.Shifts, vm.SelectedTable!.Table);     // Expert never hides one
    }

    [Theory]
    [InlineData(false, false, 7)]
    [InlineData(false, true, 12)]
    [InlineData(true, false, 12)]
    public void Profile_tabs_follow_the_level_and_the_Mai_multe_sectiuni_toggle(bool expert, bool more, int visibleTabs)
    {
        var provider = new ProfileProvider(Path.Combine(_dir, "p.json"));
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert);
            var vm = new ProcedureProfileViewModel(provider) { ShowMoreSections = more };
            var view = new ProcedureProfileView { DataContext = vm };
            Layout(view);
            var tabs = Named<TabControl>(view, "Tabelele profilului de proceduri");
            var shown = vm.Tables.Where(t => ((TabItem)tabs.ItemContainerGenerator.ContainerFromItem(t)).Visibility == Visibility.Visible).ToList();
            Assert.Equal(visibleTabs, shown.Count);
            if (!expert && !more) Assert.DoesNotContain(shown, t => t.IsRare);
            var toggle = Descendants(view).OfType<CheckBox>().First(c => ((string)c.Content).StartsWith("Mai multe secțiuni", StringComparison.Ordinal));
            Assert.Equal(!expert, Shown(toggle));
            return 0;
        });
    }

    // ───────── 5. Politici ─────────

    [Fact]
    public void Policy_in_Simplu_hides_the_states_line_the_manual_steps_and_the_extra_tabs()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            foreach (var expert in new[] { false, true })
            {
                Level(expert);
                var view = new PolicyView { DataContext = new PolicyViewModel() };
                Layout(view);
                var states = Descendants(view).OfType<TextBlock>().Where(t => t.Text.StartsWith("Stări:", StringComparison.Ordinal)).ToList();
                Assert.Equal(expert, states.Any(Shown));
                if (expert) Assert.Contains("(DRAFT)", states.Single(Shown).Text);
                var manual = Descendants(view).OfType<Expander>().First(x => ((string)x.Header).StartsWith("Toți pașii, manual", StringComparison.Ordinal));
                Assert.Equal(expert, Shown(manual));
                var tabs = Descendants(view).OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => (string)i.Header == "Rezultatul execuției"));
                var headers = tabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => (string)t.Header).ToArray();
                if (expert) Assert.Equal(5, headers.Length);
                else Assert.Equal(["Plan (curent / dorit / diferență)", "Rezultatul execuției"], headers);
            }
            return 0;
        });
    }

    // ───────── 6. Control stație ─────────

    [Fact]
    public void Station_control_hides_the_advanced_parameters_in_Simplu_only()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            foreach (var expert in new[] { false, true })
            {
                Level(expert);
                var view = new StationControlView { DataContext = new StationControlViewModel() };
                Layout(view);
                var adv = Descendants(view).OfType<Expander>().Single(x => (x.Header as string) == "Avansat (parametrii controlului)");
                Assert.Equal(expert, Shown(adv));
            }
            return 0;
        });
    }

    // ───────── 7. Bara de stare ─────────

    [Fact]
    public void Idle_status_line_stops_at_the_fact_on_Home_and_points_to_Home_elsewhere()
    {
        Assert.Equal("Nicio analiză în această sesiune.", MainViewModel.NoAnalysisLine("", true, 0, onHome: true));
        Assert.Equal("Nicio analiză în această sesiune. Cazurile anterioare: pagina Acasă.", MainViewModel.NoAnalysisLine("", true, 0, onHome: false));
        Assert.Equal("Nicio analiză în această sesiune. Cazurile anterioare: pagina Acasă.", MainViewModel.NoAnalysisLine("", false, 4));
        Assert.Equal("Nicio analiză încă.", MainViewModel.NoAnalysisLine("", false, 0, onHome: true));
        Assert.Equal("Control stație finalizat.", MainViewModel.NoAnalysisLine("Control stație finalizat.", true, 3, onHome: true));
        // the line follows the page when the person moves between Home and the other pages
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("partial void OnSelectedTabIndexChanged(int value)", vm);
        Assert.Contains("SelectedTabIndex == HomeViewModel.HomeTabIndex)", vm);
    }

    [Fact]
    public void A_broken_accounts_integrity_is_warned_outside_the_advanced_settings_expander()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "AuthView.xaml"));
        int warning = xaml.IndexOf("Atenție: integritatea conturilor nu se verifică", StringComparison.Ordinal);
        int expander = xaml.IndexOf("<Expander Header=\"Setări avansate\"", StringComparison.Ordinal);
        Assert.True(warning > 0 && expander > 0 && warning < expander, "the integrity warning must come before (outside) the „Setări avansate” expander");
        var head = xaml[..expander];
        Assert.Contains("Visibility=\"{Binding IntegrityBroken, Converter={StaticResource BooleanToVisibilityConverter}}\" BorderBrush=\"{StaticResource CriticalBrush}\"", head);
        Assert.DoesNotContain("HideInSimple", head[head.LastIndexOf("<Border", StringComparison.Ordinal)..]);
    }

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
}
