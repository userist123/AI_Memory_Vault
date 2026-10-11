using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LogAnalyzer.UI.Views.Controls;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Owner 2026-10-11 ("the other app is faster"): main-window pages are built the first time they are shown, not all 23 at startup.</summary>
public sealed class LazyPageTests
{
    private static DataTemplate Template() => new() { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) };

    [Fact]
    public void Only_the_shown_page_is_built_and_it_inherits_the_window_data_context()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            var first = new LazyPage { Page = Template() };
            var second = new LazyPage { Page = Template() };
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Content = first });
            tabs.Items.Add(new TabItem { Content = second });
            var window = new Window { Content = tabs, DataContext = "context", Width = 300, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
            window.Show();
            Pump();
            Assert.True(first.IsRealized);
            Assert.False(second.IsRealized);                 // never shown: never built

            tabs.SelectedIndex = 1;
            Pump();
            Assert.True(second.IsRealized);
            Assert.Equal("context", ((FrameworkElement)second.Content).DataContext);
            var built = second.Content;
            tabs.SelectedIndex = 0; Pump(); tabs.SelectedIndex = 1; Pump();
            Assert.Same(built, second.Content);               // kept, not rebuilt on every switch
            window.Close();
            return 0;
        });
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new System.Action(() => { }));
}
