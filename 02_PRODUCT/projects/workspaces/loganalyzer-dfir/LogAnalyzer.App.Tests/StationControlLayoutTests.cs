using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using LogAnalyzer.UI.Views.Controls;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The station control page after a control (owner screenshot, wide monitor): the result section above the tabs took the whole page height and
/// the tab with the checks list collapsed to one text line. The tab area keeps a minimum height and the page grows (and scrolls through
/// <see cref="LazyPage"/>) instead; the checks DataGrid keeps its own scrolling and virtualization.
/// </summary>
public sealed class StationControlLayoutTests
{
    // main window viewport: screen minus taskbar (40), title bar (30), header (64), status bar (32) and the 240 px sidebar
    [Theory]
    [InlineData(1920, 1080, 300)]
    [InlineData(1280, 720, 200)]
    public void The_checks_list_fills_the_page_after_a_control(double screenW, double screenH, double minGridHeight)
    {
        var (gridHeight, tabsHeight, virtualizing, scrollsItself) = Wp18AccessibilityTests.OnSta(() =>
        {
            var vm = new StationControlViewModel();
            vm.Result = new ControlResultScreen("Au fost găsite probleme", "Da, 3 neconformități.", "Mediu.", "Da.", "30 de verificări.",
                ["Pasul 1", "Pasul 2", "Pasul 3"], "Acoperire: completă.", false);
            for (int i = 0; i < 30; i++)
                vm.Checks.Add(new ControlCheck($"C{i:00}", "Zona", $"Verificarea numărul {i}", i % 3 == 0 ? ControlStatus.Neconform : ControlStatus.Conform,
                    $"Detaliul verificării {i}", [], "Recomandare"));

            var page = new LazyPage { Page = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(StationControlView)) }, DataContext = vm };
            double w = screenW - 240, h = screenH - 40 - 30 - 64 - 32;
            var window = new Window { Content = new Border { Width = w, Height = h, Child = page }, Width = w, Height = h, Left = -20000, Top = -20000,
                                      ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            window.Show();
            for (int pass = 0; pass < 4; pass++) window.UpdateLayout();      // the page fit and the view's minimum height settle over a few passes
            var view = (StationControlView)page.View!;
            Assert.Same(vm, view.DataContext);
            var grid = Find<DataGrid>(view, g => AutomationProperties.GetName(g) == "Verificările controlului");
            var tabs = Find<TabControl>(view, _ => true);
            var inner = Find<ScrollViewer>(grid, _ => true);
            var result = (grid.ActualHeight, tabs.ActualHeight, VirtualizingPanel.GetIsVirtualizing(grid), inner.ExtentHeight > inner.ViewportHeight + 0.5);
            // every part of the page stays reachable: the host scrolls over the whole view
            Assert.True(page.Host!.ExtentHeight + 0.5 >= view.ActualHeight, $"extent {page.Host.ExtentHeight:0} < view {view.ActualHeight:0}");
            window.Close();
            return result;
        });
        Assert.True(gridHeight >= minGridHeight, $"checks list is {gridHeight:0} px high at {screenW}x{screenH} (tabs {tabsHeight:0} px)");
        Assert.True(virtualizing, "the checks list must keep row virtualization");
        Assert.True(scrollsItself, "30 checks must scroll inside the list, not stretch the list to its whole content");
    }

    /// <summary>Every page with a star-row area marked with PageFill (station control, investigation, policy, procedure profile, process containment)
    /// gives that area at least its minimum height on a wide and on a small monitor, and the page host scrolls over the grown page.</summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void Every_page_fill_area_keeps_its_minimum_height(double screenW, double screenH)
    {
        var (problems, marked) = Wp18AccessibilityTests.OnSta(() =>
        {
            var found = new List<string>(); var names = new List<string>();
            foreach (var type in typeof(LazyPage).Assembly.GetTypes()
                         .Where(t => typeof(UserControl).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null
                                     && t.Namespace is { } ns && !ns.EndsWith(".Controls", StringComparison.Ordinal))
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var page = new LazyPage { Page = new DataTemplate { VisualTree = new FrameworkElementFactory(type) } };
                double w = screenW - 240, h = screenH - 40 - 30 - 64 - 32;
                var window = new Window { Content = new Border { Width = w, Height = h, Child = page }, Width = w, Height = h, Left = -20000, Top = -20000,
                                          ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
                try
                {
                    window.Show();
                    for (int pass = 0; pass < 4; pass++) window.UpdateLayout();
                    if (page.View is not { } view) continue;
                    foreach (var fill in All<FrameworkElement>(view).Where(f => PageFill.GetMinHeight(f) > 0 && f.IsVisible))
                    {
                        names.Add(type.Name);
                        if (fill.ActualHeight + 0.5 < PageFill.GetMinHeight(fill)) found.Add($"{type.Name}: {fill.GetType().Name} is {fill.ActualHeight:0} px, minimum {PageFill.GetMinHeight(fill):0}");
                        if (page.Host!.ExtentHeight + 0.5 < view.ActualHeight) found.Add($"{type.Name}: page {view.ActualHeight:0} px not reachable (extent {page.Host.ExtentHeight:0})");
                    }
                }
                catch (Exception ex) { found.Add($"{type.Name}: {ex.GetType().Name} {ex.Message}"); }
                finally { window.Close(); }
            }
            return (found, names);
        });
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(["InvestigationView", "PolicyView", "ProcedureProfileView", "ProcessContainmentView", "StationControlView"], marked.Distinct().OrderBy(n => n, StringComparer.Ordinal));
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (d is T t) yield return t;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
    }

    private static T Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (d is T t && !ReferenceEquals(d, root) && match(t)) return t;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
        throw new InvalidOperationException($"no {typeof(T).Name} found");
    }
}
