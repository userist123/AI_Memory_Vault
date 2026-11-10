using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Lab 2026-11-10: the sidebar menu and the open page must never disagree (keyboard / screen-reader selection, pages opened from Home cards).</summary>
public sealed class SidebarSyncTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static string Source()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return File.ReadAllText(Path.Combine(d.FullName, "LogAnalyzer.App", "MainWindow.xaml.cs"));
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    [Fact]
    public void SidebarTab_reads_the_command_parameter_as_a_tab_number()
    {
        Sta(() =>
        {
            Assert.Equal(22, MainWindow.SidebarTab(new RadioButton { CommandParameter = "22" }));
            Assert.Equal(5, MainWindow.SidebarTab(new RadioButton { CommandParameter = 5 }));
            Assert.Null(MainWindow.SidebarTab(new RadioButton { CommandParameter = null }));
            Assert.Null(MainWindow.SidebarTab(new RadioButton { CommandParameter = "abc" }));
            Assert.Null(MainWindow.SidebarTab(new RadioButton { CommandParameter = "" }));
        });
    }

    [Fact]
    public void The_window_source_keeps_both_directions_of_the_sidebar_sync()
    {
        var src = Source();
        // menu -> page: a checked sidebar item opens its page
        Assert.Matches(new Regex(@"AddHandler\(\s*System\.Windows\.Controls\.Primitives\.ToggleButton\.CheckedEvent"), src);
        // page -> menu: the item of the open page is checked when SelectedTabIndex changes
        Assert.Matches(new Regex(@"PropertyChanged\s*\+=.*nameof\(MainViewModel\.SelectedTabIndex\)"), src);
    }
}
