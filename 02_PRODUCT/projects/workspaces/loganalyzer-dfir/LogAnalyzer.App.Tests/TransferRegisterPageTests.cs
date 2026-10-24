using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Edition;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Owner 2026-10-11: the transfer register is its own page, separate from the storage-media page.</summary>
public sealed class TransferRegisterPageTests
{
    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    [Fact]
    public void The_transfer_register_is_the_last_main_window_tab_and_has_a_sidebar_entry()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        var pages = Regex.Matches(xaml, @"<TabItem><ctrl:LazyPage>").Count;
        Assert.Equal(RoleProfiles.TabTransferRegister, pages - 1);
        Assert.Contains("<views:TransferRegisterView DataContext=\"{Binding Transfer}\"/>", xaml);
        Assert.Contains($"CommandParameter=\"{RoleProfiles.TabTransferRegister}\"", xaml);
    }

    [Fact]
    public void The_media_page_no_longer_holds_the_transfer_tab_and_links_to_the_new_page()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "MediaCenterView.xaml"));
        Assert.DoesNotContain("TabItem Header=\"Registrul de transferuri\"", xaml);
        Assert.Contains("Deschide registrul de transferuri", xaml);
    }

    [Fact]
    public void The_controller_navigation_lists_the_transfer_register_next_to_the_media_register()
    {
        var nav = RoleProfiles.For(StationRole.Control, new ClassifiedEditionProfile(), LogAnalyzer.Core.Services.Connectivity.AppMode.AirGapped).Navigation.Select(n => n.Tab).ToList();
        Assert.Equal(nav.IndexOf(RoleProfiles.TabMediaRegister) + 1, nav.IndexOf(RoleProfiles.TabTransferRegister));
    }
}
