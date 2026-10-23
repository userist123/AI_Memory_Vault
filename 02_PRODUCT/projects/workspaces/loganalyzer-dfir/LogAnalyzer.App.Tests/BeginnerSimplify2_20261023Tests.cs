using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>2026-10-23, second round: toolbars of Profil / Registru utilizatori / Politici and the first step of Control stație in „Simplu”. Display only; Expert is unchanged.</summary>
[Collection(GlobalStateCollection.Name)]
public sealed class BeginnerSimplify2_20261023Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-simplify2-" + Guid.NewGuid().ToString("N"));
    public BeginnerSimplify2_20261023Tests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void Level(bool expert) => LanguageLevelContext.Set(expert ? UiLanguageLevel.Expert : UiLanguageLevel.Simple);
    private static void Layout(FrameworkElement v) { v.Measure(new Size(1400, 1600)); v.Arrange(new Rect(0, 0, 1400, 1600)); v.UpdateLayout(); }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// <summary>Shown to the person: no collapsed ancestor and no closed expander above it.</summary>
    private static bool Shown(DependencyObject? e)
    {
        for (var d = e; d is not null; d = LogicalTreeHelper.GetParent(d) ?? (d is FrameworkElement fe ? fe.TemplatedParent : null))
        {
            if (d is UIElement u && u.Visibility != Visibility.Visible) return false;
            if (d is Expander x && !x.IsExpanded && !ReferenceEquals(d, e)) return false;
        }
        return e is not null;
    }

    private static void OpenAvansat(FrameworkElement view)
    {
        foreach (var x in Descendants(view).OfType<Expander>().Where(x => (x.Header as string)?.StartsWith("Avansat", StringComparison.Ordinal) == true)) x.IsExpanded = true;
        Layout(view);
    }

    private static List<string> Buttons(FrameworkElement view) =>
        Descendants(view).OfType<Button>().Where(b => b.Content is string && Shown(b)).Select(b => (string)b.Content).ToList();

    private static List<string> Boxes(FrameworkElement view) =>
        Descendants(view).OfType<CheckBox>().Where(b => b.Content is string && Shown(b)).Select(b => (string)b.Content).ToList();

    private static bool HasText(FrameworkElement view, string text) =>
        Descendants(view).OfType<TextBlock>().Any(t => Shown(t) && t.Text == text);

    private static readonly string[] ProfileMoved = ["Salvează ca…", "Reîncarcă", "Deschide…", "Validează", "Importă CSV în tabel…", "Lipește în tabel"];
    private const string ProfileReplace = "înlocuiește conținutul tabelului la import/lipire";

    [Fact]
    public void Profile_toolbar_in_Simplu_keeps_Salveaza_and_one_Importa_JSON_the_rest_is_under_Avansat()
    {
        var provider = new ProfileProvider(Path.Combine(_dir, "p.json"));
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: false);
            var view = new ProcedureProfileView { DataContext = new ProcedureProfileViewModel(provider) };
            Layout(view);
            var shown = Buttons(view);
            Assert.Equal(1, shown.Count(b => b == "Salvează"));
            Assert.Equal(1, shown.Count(b => b == "Importă JSON…"));
            foreach (var m in ProfileMoved) Assert.DoesNotContain(m, shown);
            Assert.DoesNotContain(ProfileReplace, Boxes(view));
            OpenAvansat(view);
            var all = Buttons(view);
            foreach (var m in ProfileMoved) Assert.Equal(1, all.Count(b => b == m));
            Assert.Equal(1, all.Count(b => b == "Importă JSON…"));
            Assert.Contains(ProfileReplace, Boxes(view));
            return 0;
        });
    }

    [Fact]
    public void Profile_toolbar_in_Expert_is_unchanged()
    {
        var provider = new ProfileProvider(Path.Combine(_dir, "p2.json"));
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: true);
            var view = new ProcedureProfileView { DataContext = new ProcedureProfileViewModel(provider) };
            Layout(view);
            var shown = Buttons(view);
            foreach (var m in ProfileMoved.Append("Salvează")) Assert.Equal(1, shown.Count(b => b == m));
            Assert.Equal(2, shown.Count(b => b == "Importă JSON…"));   // toolbar + empty-state card, as before
            Assert.Contains(ProfileReplace, Boxes(view));
            return 0;
        });
    }

    private static readonly string[] RegisterMoved = ["Reîncarcă", "Deschide…", "Validează", "Importă JSON…", "Importă CSV…", "Lipește în tabel"];
    private const string RegisterReplace = "înlocuiește conținutul la import/lipire";

    [Fact]
    public void Users_register_toolbar_in_Simplu_keeps_Preia_and_Salveaza_the_rest_is_under_Avansat()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: false);
            var view = new RegisterView { DataContext = new UsersRegisterViewModel(Path.Combine(_dir, "u.json")) };
            Layout(view);
            Assert.Equal(["Preia de pe stație", "Salvează"], Buttons(view));
            Assert.DoesNotContain(RegisterReplace, Boxes(view));
            OpenAvansat(view);
            var all = Buttons(view);
            foreach (var m in RegisterMoved) Assert.Equal(1, all.Count(b => b == m));
            Assert.Contains(RegisterReplace, Boxes(view));
            return 0;
        });
    }

    [Fact]
    public void Users_register_toolbar_in_Expert_is_unchanged()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: true);
            var view = new RegisterView { DataContext = new UsersRegisterViewModel(Path.Combine(_dir, "u2.json")) };
            Layout(view);
            var shown = Buttons(view);
            foreach (var m in RegisterMoved.Concat(["Preia de pe stație", "Salvează"])) Assert.Equal(1, shown.Count(b => b == m));
            Assert.Contains(RegisterReplace, Boxes(view));
            return 0;
        });
    }

    [Fact]
    public void Policy_in_Simplu_shows_one_open_button_and_the_empty_card_points_to_it()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: false);
            var view = new PolicyView { DataContext = new PolicyViewModel() };
            Layout(view);
            var shown = Buttons(view);
            Assert.Equal(1, shown.Count(b => b == "Deschide politica…"));
            Assert.DoesNotContain("Deschide fișier…", shown);
            Assert.DoesNotContain("Deschide folder GPO…", shown);
            Assert.False(HasText(view, "Operator:"));
            OpenAvansat(view);
            var all = Buttons(view);
            Assert.Equal(1, all.Count(b => b == "Deschide fișier…"));
            Assert.Equal(1, all.Count(b => b == "Deschide folder GPO…"));
            Assert.True(HasText(view, "Operator:"));
            return 0;
        });
        Assert.Contains("Deschide politica…", EmptyStates.PolicyStep);
        Assert.DoesNotContain("Deschide fișier", EmptyStates.PolicyStep);
    }

    [Fact]
    public void Policy_in_Expert_keeps_the_toolbar_open_buttons()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            Level(expert: true);
            var view = new PolicyView { DataContext = new PolicyViewModel() };
            Layout(view);
            var shown = Buttons(view);
            Assert.Equal(1, shown.Count(b => b == "Deschide fișier…"));
            Assert.Equal(1, shown.Count(b => b == "Deschide folder GPO…"));
            Assert.True(HasText(view, "Operator:"));
            return 0;
        });
    }

    [Fact]
    public void Station_control_step_1_speaks_plainly_in_Simplu_and_keeps_the_technical_lines_in_Expert()
    {
        Assert.Equal("Ediția: Standalone (stație izolată, fără rețea).", StationControlViewModel.PlainPostureLine(LogAnalyzer.Core.Services.Edition.StationPosture.Standalone));
        var unknown = LogAnalyzer.Dfir.Registers.StationName.Parse("WS-01");
        Assert.Equal("Numele stației nu respectă regula de denumire a unității", StationControlViewModel.PlainStationDescription(unknown));
        var good = LogAnalyzer.Dfir.Registers.StationName.Parse("1234DOC5143SV");
        Assert.Equal(good.Describe(), StationControlViewModel.PlainStationDescription(good));
        Assert.Contains("1234DOC5143SV", StationControlViewModel.NamingRuleHelp);
        Wp18AccessibilityTests.OnSta(() =>
        {
            foreach (var expert in new[] { false, true })
            {
                Level(expert);
                var vm = new StationControlViewModel();
                var view = new StationControlView { DataContext = vm };
                Layout(view);
                Assert.Equal(!expert, HasText(view, vm.PostureLineSimple));
                Assert.Equal(!expert, HasText(view, vm.StationLineSimple));
                Assert.Equal(expert, HasText(view, vm.PostureLine));
                Assert.Equal(expert, HasText(view, vm.StationLine));
                Assert.DoesNotContain("urmează convenția", vm.StationLineSimple);
            }
            return 0;
        });
    }

    [Fact]
    public void Idle_status_line_on_Home_is_unchanged()
    {
        Assert.Equal("Nicio analiză încă.", MainViewModel.NoAnalysisLine("", false, 0, onHome: true));
    }
}
