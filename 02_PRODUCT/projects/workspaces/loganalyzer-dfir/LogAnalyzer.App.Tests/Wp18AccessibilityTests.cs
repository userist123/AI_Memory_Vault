using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// WP18 S6: the five main screens of the two roles (Home, Control stație, Investigație, Profil de proceduri, Autentificare) are laid out at 150 %
/// text scale in a 1366-pixel window without horizontal overflow, every interactive element of theirs has an automation name, and the XAML itself
/// loads with the application's theme (missing resources throw here, not on the user's screen).
/// </summary>
public sealed class Wp18AccessibilityTests
{
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

    /// <summary>The five screens, as the prompt's §6.5 names them.</summary>
    private static readonly string[] Screens = ["Views/HomeView.xaml", "Views/StationControlView.xaml", "Views/InvestigationView.xaml", "Views/ProcedureProfileView.xaml", "Views/AuthView.xaml"];

    private static readonly HashSet<string> Interactive = ["Button", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "ComboBox", "DatePicker", "DataGrid", "ListBox", "ListView", "TabControl", "Expander", "Slider", "PasswordBox"];
    private static readonly HashSet<string> NamedByOwnText = ["Button", "ToggleButton", "CheckBox", "RadioButton"];
    private static bool Has(XElement e, string attr) => e.Attributes().Any(a => a.Name.LocalName == attr && a.Value.Trim().Length > 0);
    private static bool IsLiteral(XElement e, string attr) => e.Attributes().Any(a => a.Name.LocalName == attr && a.Value.Trim().Length > 0 && !a.Value.TrimStart().StartsWith('{'));

    [Fact]
    public void Every_interactive_element_of_the_five_main_screens_has_an_automation_name()
    {
        var missing = new List<string>();
        foreach (var rel in Screens)
        {
            var doc = XDocument.Parse(File.ReadAllText(Path.Combine(AppDir(), rel.Replace('/', Path.DirectorySeparatorChar))));
            foreach (var e in doc.Descendants().Where(e => Interactive.Contains(e.Name.LocalName)))
            {
                bool named = Has(e, "AutomationProperties.Name") || Has(e, "AutomationProperties.LabeledBy")
                             || (NamedByOwnText.Contains(e.Name.LocalName) && IsLiteral(e, "Content"))
                             || (e.Name.LocalName == "Expander" && IsLiteral(e, "Header"))
                             || (e.Name.LocalName == "TabControl" && e.Elements().Any(x => x.Name.LocalName == "TabItem"));   // tabs are named by their headers
                if (!named) missing.Add($"{rel}: <{e.Name.LocalName}> {string.Join(" ", e.Attributes().Take(3).Select(a => a.Name.LocalName + "=" + a.Value))}");
            }
        }
        Assert.True(missing.Count == 0, "Elements without an automation name:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_five_main_screens_have_no_fixed_tiny_fonts()
    {
        var tiny = new List<string>();
        foreach (var rel in Screens)
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(AppDir(), rel.Replace('/', Path.DirectorySeparatorChar))), @"FontSize=""([0-9.]+)"""))
                if (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < 11) tiny.Add($"{rel}: FontSize={m.Groups[1].Value}");
        Assert.True(tiny.Count == 0, "Fonts below 11:\n" + string.Join("\n", tiny));
    }

    /// <summary>Runs <paramref name="body"/> on an STA thread with the application's theme loaded, as the real application has it.</summary>
    private static T OnSta<T>(Func<T> body)
    {
        T result = default!; Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    // The application's theme, from the same source files and in the same order as App.xaml. In the test host the
                    // application-relative "/Themes/…" URIs of App.xaml resolve against the test assembly, so the files are read from disk
                    // and flattened into one dictionary (none of them merges another; one dictionary keeps the cross-file StaticResources).
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var appXaml = File.ReadAllText(Path.Combine(AppDir(), "App.xaml"));
                    XNamespace xx = "http://schemas.microsoft.com/winfx/2006/xaml";
                    var root = XElement.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                                              "xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"/>");
                    // A key defined again in a later dictionary replaces the earlier one, as with merged dictionaries (e.g. SidebarTabStyle).
                    string KeyOf(XElement e) => (string?)e.Attribute(xx + "Key") ?? "implicit:" + (string?)e.Attribute("TargetType");
                    foreach (Match m in Regex.Matches(appXaml, @"<ResourceDictionary Source=""/([^""]+)""\s*/>"))
                    {
                        var doc = XDocument.Load(Path.Combine(AppDir(), m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
                        foreach (var e in doc.Root!.Elements().ToList())
                        {
                            e.Attribute(xx + "Shared")?.Remove();   // accepted only in compiled dictionaries; irrelevant to layout
                            var key = KeyOf(e);
                            root.Elements().Where(o => KeyOf(o) == key).Remove();
                            root.Add(e);
                        }
                    }
                    root.Add(XElement.Parse("<BooleanToVisibilityConverter xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Key=\"BooleanToVisibilityConverter\"/>"));
                    var xaml = root.ToString();
                    app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                }
                result = body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
        return result;
    }

    [Fact]
    public void The_theme_loads_as_one_dictionary_and_resolves_its_tokens_and_styles()
    {
        var (dictionaries, brush, style, buttonOk, error) = OnSta(() =>
        {
            var app = Application.Current!;
            string? err = null;
            bool ok = false;
            try { var b = new Button { Content = "x" }; b.Measure(new Size(200, 50)); ok = b.DesiredSize.Width > 0; } catch (Exception ex) { err = ex.ToString(); }
            return (app.Resources.MergedDictionaries.Count, app.TryFindResource("TextPrimaryBrush") is not null, app.TryFindResource("AccentButtonStyle") is not null, ok, err);
        });
        Assert.Equal(0, dictionaries);   // flattened: one dictionary
        Assert.True(brush, "TextPrimaryBrush not found");
        Assert.True(style, "AccentButtonStyle not found");
        Assert.True(buttonOk, "a plain Button cannot be measured with the theme: " + error);
    }

    public static IEnumerable<object[]> ScreensWithViewModels() =>
    [
        ["HomeView", (Func<FrameworkElement>)(() => new HomeView { DataContext = new HomeViewModel(new InvestigationViewModel(), null, new LogAnalyzer.Dfir.Case.RecentCases(Path.Combine(Path.GetTempPath(), "la-a11y-recent.json")), () => null) })],
        ["StationControlView", (Func<FrameworkElement>)(() => new StationControlView { DataContext = new StationControlViewModel() })],
        ["InvestigationView", (Func<FrameworkElement>)(() => new InvestigationView { DataContext = new InvestigationViewModel() })],
        ["ProcedureProfileView", (Func<FrameworkElement>)(() => new ProcedureProfileView { DataContext = new ProcedureProfileViewModel() })],
        ["AuthView", (Func<FrameworkElement>)(() => new AuthView())],   // its view model needs the authentication store; the XAML and the theme are what is checked here
    ];

    [Theory]
    [MemberData(nameof(ScreensWithViewModels))]
    public void Screen_loads_with_the_theme_and_fits_a_1366_window_at_150_percent_scale(string name, Func<FrameworkElement> create)
    {
        // 1366 px window minus the 240 px sidebar; at 150 % the content gets 1126 / 1.5 device-independent pixels.
        const double contentWidth = 1366 - 240, scale = 1.5;
        var (desired, overflow) = OnSta(() =>
        {
            var view = create();
            view.LayoutTransform = new ScaleTransform(scale, scale);
            var host = new Border { Width = contentWidth, Child = view };
            host.Measure(new Size(contentWidth, 2000));
            host.Arrange(new Rect(0, 0, contentWidth, 2000));
            host.UpdateLayout();
            // every text block must fit its container after wrapping: a clipped label is a label nobody can read
            var clipped = new List<string>();
            foreach (var tb in Descendants(view).OfType<TextBlock>())
                if (tb.TextWrapping == TextWrapping.NoWrap && tb.TextTrimming == TextTrimming.None && tb.ActualWidth > 0 && tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right > tb.ActualWidth + 0.5 && tb.Visibility == Visibility.Visible)
                    clipped.Add($"\"{tb.Text}\" needs {tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right:0} but has {tb.ActualWidth:0}");   // DesiredSize includes the margin, ActualWidth does not
            return (view.DesiredSize.Width, clipped);   // DesiredSize already includes the LayoutTransform
        });
        Assert.True(desired <= contentWidth + 0.5, $"{name}: needs {desired:0} px of width at 150 %, the window gives {contentWidth} (horizontal scrolling for the whole page)");
        Assert.True(overflow.Count == 0, $"{name}: clipped labels at 150 %:\n" + string.Join("\n", overflow.Take(20)));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
