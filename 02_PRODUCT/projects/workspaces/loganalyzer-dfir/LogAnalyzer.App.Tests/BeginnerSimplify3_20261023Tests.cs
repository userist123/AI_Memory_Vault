using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>2026-10-23, third round: device monitor buttons, the sidebar „Avansat” menu, users register header, transfer register warning and Home „Alte acțiuni” in „Simplu”. Display only; Expert is unchanged.</summary>
[Collection(GlobalStateCollection.Name)]
public sealed class BeginnerSimplify3_20261023Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-simplify3-" + Guid.NewGuid().ToString("N"));
    public BeginnerSimplify3_20261023Tests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void Level(bool expert) => LanguageLevelContext.Set(expert ? UiLanguageLevel.Expert : UiLanguageLevel.Simple);
    private static void Layout(FrameworkElement v) { v.Measure(new Size(1400, 1600)); v.Arrange(new Rect(0, 0, 1400, 1600)); v.UpdateLayout(); }
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found");
    }
    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static IEnumerable<DependencyObject> Visual(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Visual(c)) yield return d;
        }
    }

    private static bool Shown(DependencyObject? e)
    {
        for (var d = e; d is not null; d = LogicalTreeHelper.GetParent(d) ?? (d is FrameworkElement fe ? fe.TemplatedParent : null))
        {
            if (d is UIElement u && u.Visibility != Visibility.Visible) return false;
            if (d is Expander x && !x.IsExpanded && !ReferenceEquals(d, e)) return false;
        }
        return e is not null;
    }

    private static List<string> Buttons(FrameworkElement view) =>
        Descendants(view).OfType<Button>().Where(b => b.Content is string && Shown(b)).Select(b => (string)b.Content).ToList();

    private static bool HasText(FrameworkElement view, string startsWith) =>
        Descendants(view).OfType<TextBlock>().Any(t => Shown(t) && t.Text.StartsWith(startsWith, StringComparison.Ordinal));

    // ───────── 1. Monitor dispozitive ─────────

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Device_monitor_hides_Opreste_and_Export_CSV_in_Simplu_only(bool expert, bool live)
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert);
            var vm = new MediaCenterViewModel(new MediaRegisterViewModel(Path.Combine(_dir, "m.json"))) { IsLive = live };
            var view = new MediaCenterView { DataContext = vm };
            Layout(view);
            var shown = Buttons(view);
            Assert.Contains("Reîmprospătează", shown);
            Assert.Contains("Deschide registrul de transferuri", shown);
            Assert.Equal(expert, shown.Contains("Export CSV"));
            Assert.Equal(expert && live, shown.Contains("Oprește supravegherea"));
            Assert.Equal(!live, shown.Contains("Pornește supravegherea"));
            return 0;
        });
    }

    // ───────── 2. Sidebar ─────────

    [Fact]
    public void Switching_to_Simplu_on_an_Avansat_page_lands_on_Acasa_and_nothing_else_moves()
    {
        var nav = RoleProfiles.For(StationRole.Control, new UnclassifiedEditionProfile(), AppMode.AirGapped).Navigation;
        Assert.Equal(HomeViewModel.HomeTabIndex, AdvancedNav.TabAfterLevelChange(nav, 0, simple: true));      // Panou general is under Avansat
        Assert.Equal(HomeViewModel.HomeTabIndex, AdvancedNav.TabAfterLevelChange(nav, 13, simple: true));
        Assert.Equal(14, AdvancedNav.TabAfterLevelChange(nav, 14, simple: true));                             // Control stație is a main page
        Assert.Equal(19, AdvancedNav.TabAfterLevelChange(nav, 19, simple: true));
        Assert.Equal(HomeViewModel.HomeTabIndex, AdvancedNav.TabAfterLevelChange(nav, HomeViewModel.HomeTabIndex, simple: true));
        Assert.Equal(0, AdvancedNav.TabAfterLevelChange(nav, 0, simple: false));                              // Expert: nothing moves
    }

    [Fact]
    public void Sidebar_hides_the_Avansat_menu_in_Simplu_and_offers_a_link_that_switches_to_Expert_and_opens_it()
    {
        var xaml = Src("MainWindow.xaml");
        var start = xaml.IndexOf("<Expander Header=\"Avansat (toate paginile)\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var expander = xaml[start..];
        expander = expander[..expander.IndexOf('>')];
        Assert.Contains("views:SimpleLevel.HideInSimple=\"True\"", expander);
        Assert.Contains("IsExpanded=\"{Binding AdvancedPagesOpen, Mode=TwoWay}\"", expander);
        var at = xaml.IndexOf("Toate paginile (nivelul Expert)", StringComparison.Ordinal);
        Assert.True(at >= 0);
        var tag = xaml[xaml.LastIndexOf("<Button", at, StringComparison.Ordinal)..(at + 200)];
        Assert.Contains("views:SimpleLevel.OnlyInSimple=\"True\"", tag);
        Assert.Contains("Command=\"{Binding ShowAllPagesCommand}\"", tag);
        var vm = Src("ViewModels", "MainViewModel.cs");
        Assert.Contains("private void ShowAllPages()", vm);
        Assert.Contains("AdvancedNav.TabAfterLevelChange(", vm);
    }

    // ───────── 3. Registru utilizatori ─────────

    private const string UsersShort = "Lista persoanelor care lucrează pe stație.";

    [Fact]
    public void Users_register_has_one_accent_button_and_one_explanation_in_Simplu()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: false);
            var vm = new UsersRegisterViewModel(Path.Combine(_dir, "u.json"));
            var view = new RegisterView { DataContext = vm };
            Layout(view);
            var accent = view.FindResource("AccentButtonStyle");
            var outline = view.FindResource("OutlineButtonStyle");
            var shown = Descendants(view).OfType<Button>().Where(b => b.Content is string && Shown(b)).ToDictionary(b => (string)b.Content);
            Assert.Same(accent, shown["Preia de pe stație"].Style);
            Assert.Same(outline, shown["Salvează"].Style);
            Assert.Single(shown.Values.Where(b => ReferenceEquals(b.Style, accent)));
            // empty register: the card explains, so the short line is not repeated
            Assert.True(vm.IsEmpty);
            Assert.False(HasText(view, UsersShort));
            Assert.Contains(Descendants(view).OfType<LogAnalyzer.UI.Views.Controls.EmptyStateCard>(), Shown);
            // the permissions line is a small secondary line
            Assert.NotEqual("", vm.Notice);
            var notice = Descendants(view).OfType<TextBlock>().First(t => t.Text == vm.Notice);
            Assert.NotEqual(FontWeights.SemiBold, notice.FontWeight);
            Assert.True(notice.FontSize <= 12);
            return 0;
        });
    }

    [Fact]
    public void Users_register_short_line_shows_once_a_row_exists_and_Expert_is_unchanged()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: false);
            var vm = new UsersRegisterViewModel(Path.Combine(_dir, "u1.json"));
            var row = vm.Data.NewRow(); row[0] = "ion.popescu"; vm.Data.Rows.Add(row);
            var view = new RegisterView { DataContext = vm };
            Layout(view);
            Assert.False(vm.IsEmpty);
            Assert.True(HasText(view, UsersShort));

            Level(expert: true);
            var vm2 = new UsersRegisterViewModel(Path.Combine(_dir, "u2.json"));
            var v2 = new RegisterView { DataContext = vm2 };
            Layout(v2);
            var accent = v2.FindResource("AccentButtonStyle");
            var shown = Descendants(v2).OfType<Button>().Where(b => b.Content is string && Shown(b)).ToDictionary(b => (string)b.Content);
            Assert.Same(accent, shown["Salvează"].Style);
            Assert.Same(accent, shown["Preia de pe stație"].Style);
            Assert.False(HasText(v2, UsersShort));
            var notice = Descendants(v2).OfType<TextBlock>().First(t => t.Text == vm2.Notice);
            Assert.Equal(FontWeights.SemiBold, notice.FontWeight);
            return 0;
        });
    }

    // ───────── 4. Registrul de transferuri ─────────

    private static DeviceAssessment Unregistered() => new(new DeviceRecord { Kind = DeviceKind.UsbStorage, InstanceId = "USB\\X" }, RegisterMatch.NotRegistered, "", "v",
        "Deconectați-l și aflați al cui este; pe o stație izolată doar suporturile din registru sunt permise.");

    [Fact]
    public void Unregistered_medium_status_is_sentence_case_in_Simplu_and_unchanged_in_Expert()
    {
        var a = Unregistered();
        Assert.Equal("Atenție: mediul nu este în registru. Deconectați-l și aflați al cui este; pe o stație izolată doar suporturile din registru sunt permise.", TransferViewModel.MediumStatus(a, simple: true));
        Assert.Equal("ATENȚIE: NU este în registru. Deconectați-l și aflați al cui este; pe o stație izolată doar suporturile din registru sunt permise.", TransferViewModel.MediumStatus(a, simple: false));
        Assert.Equal("Mediu din registru: 12/2026.", TransferViewModel.MediumStatus(a with { Match = RegisterMatch.Registered, RegistrationNumber = "12/2026" }, simple: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Transfer_page_subtitle_and_warning_colour_follow_the_level(bool expert)
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert);
            var media = new MediaCenterViewModel(new MediaRegisterViewModel(Path.Combine(_dir, "m2.json")));
            var vm = new TransferViewModel(media) { SelectedMedium = Unregistered() };
            var view = new TransferRegisterView { DataContext = vm };
            Layout(view);
            const string one = "Fiecare transfer de date pe un mediu de stocare se înregistrează aici.";
            Assert.Equal(!expert, HasText(view, one));
            Assert.Equal(expert, HasText(view, "Transferurile de date pe medii de stocare: un singur formular"));
            Assert.True(vm.StatusIsWarning);
            Assert.StartsWith(expert ? "ATENȚIE: NU este în registru." : "Atenție: mediul nu este în registru.", vm.Status);
            var status = Descendants(view).OfType<TextBlock>().First(t => t.Text == vm.Status);
            var warn = (System.Windows.Media.SolidColorBrush)view.FindResource("WarningBrush");
            var fg = (System.Windows.Media.SolidColorBrush)status.Foreground;
            Assert.Equal(!expert, fg.Color == warn.Color);
            return 0;
        });
    }

    // ───────── 5. Acasă · Alte acțiuni ─────────

    private HomeViewModel Home(StationRole role) => new(new InvestigationViewModel(), _ => { }, new RecentCases(Path.Combine(_dir, "recent.json")), () => null,
        RoleProfiles.For(role, new UnclassifiedEditionProfile(), AppMode.AirGapped));

    [Theory]
    [InlineData(StationRole.Control, "analyze_evidence")]
    [InlineData(StationRole.Csirt, "check_this_computer")]
    public void Other_actions_in_Simplu_drop_menu_duplicates_and_expert_only_items_and_Expert_keeps_all(StationRole role, string kept)
    {
        var vm = Home(role);
        Assert.Equal([kept], vm.ShownMoreIntents.Select(i => i.Key).ToArray());
        Assert.True(vm.HasShownMoreIntents);
        Assert.Contains(vm.MoreIntents, i => i.Key != kept);                 // the role data is unchanged
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        Assert.Equal(vm.MoreIntents.Select(i => i.Key), vm.ShownMoreIntents.Select(i => i.Key));
        LanguageLevelContext.Set(UiLanguageLevel.Simple);
        Assert.Equal([kept], vm.ShownMoreIntents.Select(i => i.Key).ToArray());
    }

    [Fact]
    public void Home_view_lists_the_shown_other_actions_only()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            foreach (var expert in new[] { false, true })
            {
                Level(expert);
                var vm = Home(StationRole.Control);
                var view = new HomeView { DataContext = vm };
                Layout(view);
                var ex = Descendants(view).OfType<Expander>().Single(x => (x.Header as string) == "Alte acțiuni");
                ex.IsExpanded = true;
                Layout(view);
                var items = Visual(ex).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Equal(expert, items.Contains("Profilul de proceduri"));
                Assert.Contains("Analizează probe aduse de pe altă stație", items);
            }
            return 0;
        });
    }
}
