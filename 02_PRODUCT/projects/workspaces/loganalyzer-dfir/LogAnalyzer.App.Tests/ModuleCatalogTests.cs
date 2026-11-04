using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Plan 2026-11-04 phase R1: one catalog decides which page exists in which module (Insula / Arhipelag / Continent); a page the module can never have is not in
/// its menu at all, and the old ";air" / ";net" suffixes are gone from the window.
/// </summary>
public sealed class ModuleCatalogTests
{
    private static string MainWindow()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return File.ReadAllText(Path.Combine(d.FullName, "LogAnalyzer.App", "MainWindow.xaml"));
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    [Theory]
    [InlineData(StationPosture.Standalone, "Insula — Stație izolată")]
    [InlineData(StationPosture.IsolatedNetwork, "Arhipelag — Rețea izolată")]
    [InlineData(StationPosture.Internet, "Continent — Rețea conectată")]
    public void Every_module_has_a_name_and_a_descriptor(StationPosture p, string expected) => Assert.Equal(expected, ModuleCatalog.DisplayName(p));

    [Fact]
    public void Every_tab_of_the_main_window_is_in_the_catalog_and_every_menu_entry_uses_a_catalog_key()
    {
        var xaml = MainWindow();
        Assert.DoesNotContain(";air}", xaml);
        Assert.DoesNotContain(";net}", xaml);
        var keys = Regex.Matches(xaml, @"AdvancedNavVisibility\}, ConverterParameter=([^}]+)\}").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(keys);
        foreach (var k in keys) Assert.True(ModuleCatalog.Find(k) is not null, $"cheia de meniu „{k}” lipsește din ModuleCatalog");
        for (var tab = 0; tab <= RoleProfiles.TabTransferRegister; tab++)
            Assert.Contains(ModuleCatalog.Pages, p => p.Tab == tab);
        Assert.Equal(ModuleCatalog.Pages.Count, ModuleCatalog.Pages.Select(p => p.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("7")]   // threat intelligence
    [InlineData("8.ad")]
    [InlineData("11")]  // live monitoring
    [InlineData("13")]  // process isolation
    [InlineData("15")]  // domain and e-mail
    public void Insula_never_lists_network_or_response_pages(string key)
    {
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, key, StationPosture.Standalone, AppMode.AirGapped));
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, key, StationPosture.Standalone, AppMode.Network));
    }

    [Fact]
    public void Tab_8_shows_local_accounts_when_isolated_and_active_directory_on_a_network()
    {
        Assert.True(AdvancedNavVisibilityConverter.IsVisible(null, "8.local", StationPosture.Standalone, AppMode.AirGapped));
        Assert.True(AdvancedNavVisibilityConverter.IsVisible(null, "8.ad", StationPosture.IsolatedNetwork, AppMode.Network));
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, "8.local", StationPosture.IsolatedNetwork, AppMode.Network));
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, "8.ad", StationPosture.IsolatedNetwork, AppMode.AirGapped));
    }

    [Fact]
    public void A_network_module_running_isolated_hides_the_pages_that_need_the_network_mode()
    {
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, "15", StationPosture.IsolatedNetwork, AppMode.AirGapped));
        Assert.True(AdvancedNavVisibilityConverter.IsVisible(null, "15", StationPosture.IsolatedNetwork, AppMode.Network));
        Assert.True(AdvancedNavVisibilityConverter.IsVisible(null, "7", StationPosture.IsolatedNetwork, AppMode.AirGapped));
    }

    [Theory]
    [InlineData(StationPosture.Standalone, true)]
    [InlineData(StationPosture.IsolatedNetwork, true)]
    [InlineData(StationPosture.Internet, false)]
    public void The_sign_in_notice_states_only_what_the_application_does(StationPosture p, bool classified)
    {
        var n = ModuleCatalog.DefaultNotice(p);
        Assert.Equal(classified, n.Contains("informații clasificate"));
        Assert.Contains("numai persoanelor autorizate", n);
        Assert.Contains("jurnalul sigilat", n);
        Assert.False(string.IsNullOrWhiteSpace(ModuleCatalog.Purpose(p)));
    }

    [Fact]
    public void The_sign_in_identity_lists_station_level_version_and_licence_in_order()
    {
        var rows = SignInWindow.IdentityRows("LAB-PC", "SECRET (S)", "0.1.0-dfir");
        Assert.Equal(["Stația", "Nivelul stației", "Versiunea", "Licența"], rows.Select(r => r.Label));
        Assert.Equal(["LAB-PC", "SECRET (S)", "0.1.0-dfir", "verificată la pornire"], rows.Select(r => r.Value));
    }

    [Fact]
    public void An_unknown_menu_key_is_never_shown() =>
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, "99", StationPosture.Internet, AppMode.Network));
}
