using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.UI.Views.Controls;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Small monitors (1366x768 with the taskbar leaves about 1366x728; 1280x720): every page and window must be fully reachable. Pages of the main window
/// are hosted by <see cref="LazyPage"/>, which puts the view in a ScrollViewer and gives it the viewport size down to a minimum page size; every
/// window caps itself to the work area. These tests load each view at 1280x720 and list the ones with no scroll host.
/// </summary>
public sealed class SmallMonitorScrollTests
{
    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    /// <summary>Every page view of the application: a UserControl of the Views namespaces that is not a small reusable control.</summary>
    private static IEnumerable<Type> PageViews() =>
        typeof(LazyPage).Assembly.GetTypes()
            .Where(t => typeof(UserControl).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null
                        && t.Namespace is { } ns && !ns.EndsWith(".Controls", StringComparison.Ordinal) && !ns.Contains("Controls."))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    // 1280x720 minus taskbar (40), title bar (30), header (64) and status bar (32), and the 240 px sidebar
    private const double ViewportW = 1280 - 240, ViewportH = 720 - 40 - 30 - 64 - 32;

    [Fact]
    public void Every_page_view_is_reachable_at_1280x720_inside_a_scroll_host()
    {
        var found = Wp18AccessibilityTests.OnSta(() =>
        {
            var problems = new List<string>();
            foreach (var type in PageViews())
            {
                try
                {
                    var page = new LazyPage { Page = new DataTemplate { VisualTree = new FrameworkElementFactory(type) } };
                    var border = new Border { Width = ViewportW, Height = ViewportH, Child = page };
                    var window = new Window { Content = border, Width = ViewportW, Height = ViewportH, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
                    window.Show();
                    window.UpdateLayout();
                    var host = page.Host;
                    if (host is null) { problems.Add($"{type.Name}: not inside a ScrollViewer"); window.Close(); continue; }
                    var view = page.View!;
                    if (view.Height < LazyPage.MinPageHeight - 0.5 || view.Width < LazyPage.MinPageWidth - 0.5) problems.Add($"{type.Name}: laid out smaller than the minimum page size ({view.Width:0}x{view.Height:0})");
                    // everything of the view is reachable: its extent is what the host scrolls over
                    if (host.ExtentHeight + 0.5 < view.Height || host.ExtentWidth + 0.5 < view.Width) problems.Add($"{type.Name}: extent smaller than the view");
                    window.Close();
                }
                catch (Exception ex) { problems.Add($"{type.Name}: {ex.GetType().Name} {ex.Message}"); }
            }
            return problems;
        });
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void A_page_that_fits_the_viewport_does_not_scroll_and_a_smaller_viewport_scrolls_instead_of_squeezing()
    {
        var big = LazyPage.PageSize(1500, 900);
        Assert.Equal(1500, big.Width); Assert.Equal(900, big.Height);                         // the view gets the whole viewport: a DataGrid on a star row keeps its own scrolling
        var small = LazyPage.PageSize(700, 400);
        Assert.True(small.Width >= LazyPage.MinPageWidth && small.Height >= LazyPage.MinPageHeight);   // the page keeps its minimum size and the host scrolls
        Assert.True(LazyPage.PageSize(ViewportW, ViewportH).Height >= LazyPage.MinPageHeight);
    }

    [Fact]
    public void Every_window_caps_itself_to_the_work_area_and_the_shell_minimum_fits_1280x720()
    {
        var views = Path.Combine(AppDir(), "Views");
        var windows = Directory.GetFiles(views, "*.xaml").Concat([Path.Combine(AppDir(), "MainWindow.xaml")])
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"<Window\b")).ToList();
        Assert.True(windows.Count >= 8);
        foreach (var f in windows)
            Assert.True(File.ReadAllText(f).Contains("WindowFit.ToWorkArea=\"True\""), $"{Path.GetFileName(f)} does not cap itself to the work area");

        var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        Assert.InRange(double.Parse(Regex.Match(main, @"<Window[^>]*?\bMinWidth=""(\d+)""", RegexOptions.Singleline).Groups[1].Value), 1, 1280);
        Assert.InRange(double.Parse(Regex.Match(main, @"<Window[^>]*?\bMinHeight=""(\d+)""", RegexOptions.Singleline).Groups[1].Value), 1, 600);
    }

    [Fact]
    public void WindowFit_shrinks_a_window_declared_taller_than_the_screen()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            var w = new Window { Height = 760, Width = 680, MinHeight = 420 };
            WindowFit.Apply(w, new Size(1366, 728));
            Assert.Equal(728, w.MaxHeight); Assert.Equal(728, w.Height); Assert.Equal(680, w.Width);
            var small = new Window { Height = 500, Width = 600 };
            WindowFit.Apply(small, new Size(1366, 728));
            Assert.Equal(500, small.Height);                                                     // a window that already fits is untouched
            return 0;
        });
    }

    [Fact]
    public void Pages_hosted_by_the_main_window_all_go_through_LazyPage()
    {
        var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        var tabs = Regex.Matches(main, @"<TabItem>").Count;
        Assert.Equal(tabs, Regex.Matches(main, @"<TabItem><ctrl:LazyPage>").Count);          // a page added without LazyPage would have no scroll host
    }
}
