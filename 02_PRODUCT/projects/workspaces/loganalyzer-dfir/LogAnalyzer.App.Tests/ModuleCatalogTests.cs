using System.IO;
using System.Linq;
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

    [Fact]
    public void The_mode_badge_never_says_connected_on_the_closed_network_of_Arhipelag()
    {
        // lab WS01 2026-11-07: Arhipelag on its domain LAN showed „Mod: conectat”, read as Internet access
        Assert.Equal("Mod: rețea locală, fără Internet", ModuleCatalog.ModeBadge(StationPosture.IsolatedNetwork, airGapped: false));
        Assert.Equal("Mod: izolat (fără rețea)", ModuleCatalog.ModeBadge(StationPosture.IsolatedNetwork, airGapped: true));
        Assert.Equal("Mod: izolat (fără rețea)", ModuleCatalog.ModeBadge(StationPosture.Standalone, airGapped: true));
        Assert.Equal("Mod: izolat (fără rețea)", ModuleCatalog.ModeBadge(StationPosture.Standalone, airGapped: false));   // Insula has no network code at all
        Assert.Equal("Mod: conectat", ModuleCatalog.ModeBadge(StationPosture.Internet, airGapped: false));
    }

    [Fact]
    public void A_menu_item_is_announced_by_its_title_not_by_the_record_text()
    {
        // evaluation 2026-11-05: UI Automation read "NavigationEntry { Title = Politici, Tab = 17, Enabled = True, Reason = }" for every menu item
        Assert.Equal("Politici", new NavigationEntry("Politici", 17, true, "").ToString());
        Assert.Equal("Politici: nu este disponibil", new NavigationEntry("Politici", 17, false, "nu este disponibil").ToString());
        var menu = ModuleCatalog.Menu(StationPosture.Standalone, AppMode.AirGapped, Enumerable.Range(0, 24).ToList());
        Assert.All(menu, s => Assert.Equal(s.Name, s.ToString()));
        Assert.All(menu.SelectMany(s => s.Pages), p => Assert.Equal(p.Title, p.ToString()));
    }

    [Fact]
    public void A_sign_in_identity_row_is_announced_as_label_and_value()
    {
        // lab evaluation 2026-11-06 (Windows 11 VM): UI Automation read "IdentityRow { Label = …, Value = … }" in the sign-in window
        Assert.Equal("Stația: LAB-PC", new LogAnalyzer.UI.Views.SignInWindow.IdentityRow("Stația", "LAB-PC").ToString());
    }

    [Fact]
    public void Enter_in_the_password_field_signs_in_and_while_setting_a_password_moves_to_the_confirmation_first()
    {
        // lab VM 2026-11-06: sign-in and unlock answered only to the mouse
        Assert.True(LogAnalyzer.UI.Views.SignInWindow.EnterSubmits(LogAnalyzer.UI.Views.SignInWindowMode.SignIn, fromConfirm: false));
        Assert.True(LogAnalyzer.UI.Views.SignInWindow.EnterSubmits(LogAnalyzer.UI.Views.SignInWindowMode.Unlock, fromConfirm: false));
        Assert.False(LogAnalyzer.UI.Views.SignInWindow.EnterSubmits(LogAnalyzer.UI.Views.SignInWindowMode.FirstRun, fromConfirm: false));
        Assert.False(LogAnalyzer.UI.Views.SignInWindow.EnterSubmits(LogAnalyzer.UI.Views.SignInWindowMode.Recovery, fromConfirm: false));
        Assert.True(LogAnalyzer.UI.Views.SignInWindow.EnterSubmits(LogAnalyzer.UI.Views.SignInWindowMode.FirstRun, fromConfirm: true));
    }

    [Fact]
    public void The_quick_jump_palette_finds_any_page_of_the_module_by_part_of_its_name_without_diacritics()
    {
        // lab VM 2026-11-06: the palette listed seven fixed old pages and could not reach Active Directory
        var menu = ModuleCatalog.Menu(StationPosture.IsolatedNetwork, AppMode.Network, [], _ => true);   // no main pages of a role, a licence with every right
        var pages = menu.SelectMany(s => s.Pages).Select(p => new NavigationEntry(p.Title, p.Tab, true, "")).ToList();
        Assert.Contains(LogAnalyzer.UI.ViewModels.MainViewModel.PaletteFilter(pages, "active"), p => p.Title == "Active Directory");
        Assert.All(LogAnalyzer.UI.ViewModels.MainViewModel.PaletteFilter(pages, "amenintari detectii"), p => Assert.Contains("Amenințări", p.Title));
        Assert.NotEmpty(LogAnalyzer.UI.ViewModels.MainViewModel.PaletteFilter(pages, "amenintari detectii"));
        Assert.Empty(LogAnalyzer.UI.ViewModels.MainViewModel.PaletteFilter(pages, "nu exista asa ceva"));
        Assert.True(LogAnalyzer.UI.ViewModels.MainViewModel.PaletteFilter(pages, "").Count <= LogAnalyzer.UI.ViewModels.MainViewModel.PaletteMaxMatches);
        // Insula (Standalone) has no Active Directory page to jump to
        var insula = ModuleCatalog.Menu(StationPosture.Standalone, AppMode.AirGapped, [], _ => true).SelectMany(s => s.Pages).Select(p => new NavigationEntry(p.Title, p.Tab, true, "")).ToList();
        Assert.Empty(LogAnalyzer.UI.ViewModels.MainViewModel.PaletteFilter(insula, "active directory"));
    }

    [Fact]
    public void A_home_button_is_announced_by_its_title_not_the_type_name()
    {
        // lab VM 2026-11-06: UI Automation read "LogAnalyzer.UI.ViewModels.HomeIntentView" for every Home button
        var p = LogAnalyzer.Core.Services.Edition.RoleProfiles.For(LogAnalyzer.Core.Services.Edition.StationRole.Control,
            new LogAnalyzer.Core.Services.Edition.ClassifiedEditionProfile(), AppMode.AirGapped);
        Assert.All(p.PrimaryIntents.Concat(p.MoreIntents).Select(i => new LogAnalyzer.UI.ViewModels.HomeIntentView(i)), v =>
        {
            Assert.StartsWith(v.Title, v.ToString());
            Assert.DoesNotContain("HomeIntentView", v.ToString());
        });
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
        // since 2026-11-05 the Avansat menu is generated from ModuleCatalog.Menu: every page it can list is a catalog page with a section
        foreach (var p in ModuleCatalog.Pages.Where(p => p.Tab != RoleProfiles.TabHome))
            Assert.True(ModuleCatalog.SectionOf(p.Key) is not null, $"pagina „{p.Key}” nu are secțiune în meniul Avansat");
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

    private static readonly int[] NoMainTabs = [];

    private static IEnumerable<StationPosture> Modules => [StationPosture.Standalone, StationPosture.IsolatedNetwork, StationPosture.Internet];

    [Fact]
    public void Menu_of_Insula_has_none_of_the_network_or_response_pages()
    {
        foreach (var mode in new[] { AppMode.AirGapped, AppMode.Network })
        {
            var tabs = ModuleCatalog.Menu(StationPosture.Standalone, mode, NoMainTabs).SelectMany(s => s.Pages).ToList();
            foreach (var t in new[] { 7, 11, 13, 15 }) Assert.DoesNotContain(tabs, p => p.Tab == t);
            Assert.DoesNotContain(tabs, p => p.Key == "8.ad");
        }
    }

    [Fact]
    public void Menu_never_repeats_a_page_and_never_repeats_a_main_page_of_the_role()
    {
        int[] role = [14, 19, 17, 4];
        foreach (var module in Modules)
            foreach (var mode in new[] { AppMode.AirGapped, AppMode.Network })
            {
                var pages = ModuleCatalog.Menu(module, mode, role).SelectMany(s => s.Pages).ToList();
                Assert.Equal(pages.Count, pages.Select(p => p.Key).Distinct().Count());
                Assert.Equal(pages.Count, pages.Select(p => p.Tab).Distinct().Count());
                Assert.DoesNotContain(pages, p => role.Contains(p.Tab));
            }
    }

    [Fact]
    public void Menu_leaves_out_empty_sections()
    {
        foreach (var mode in new[] { AppMode.AirGapped, AppMode.Network })
        {
            var names = ModuleCatalog.Menu(StationPosture.Standalone, mode, NoMainTabs).Select(s => s.Name).ToList();
            Assert.DoesNotContain("Rețea și domeniu", names);
            Assert.DoesNotContain("Amenințări", names);
            Assert.Contains("Examinare", names);
        }
        var control = ModuleCatalog.Menu(StationPosture.Standalone, AppMode.AirGapped, [14, 19]).Select(s => s.Name);
        Assert.DoesNotContain("Control stație", control);
        foreach (var module in Modules)
            Assert.All(ModuleCatalog.Menu(module, AppMode.Network, NoMainTabs), s => Assert.NotEmpty(s.Pages));
        var net = ModuleCatalog.Menu(StationPosture.IsolatedNetwork, AppMode.Network, NoMainTabs).Select(s => s.Name).ToList();
        Assert.Contains("Rețea și domeniu", net);
        Assert.Contains("Amenințări", net);
    }

    [Fact]
    public void Menu_sections_and_order_follow_the_flow_of_a_case()
    {
        var all = ModuleCatalog.Menu(StationPosture.Internet, AppMode.Network, NoMainTabs);
        Assert.Equal(["Examinare", "Probe și colectare", "Control stație", "Registre", "Rețea și domeniu", "Amenințări", "Administrare"], all.Select(s => s.Name));
        Assert.Equal([0, 4, 1, 2, 3, 5, 6, 10], all[0].Pages.Select(p => p.Tab));
        Assert.Equal([16, 12, 9], all[1].Pages.Select(p => p.Tab));
    }

    [Fact]
    public void Every_tab_except_Acasa_is_in_exactly_one_menu_section()
    {
        foreach (var p in ModuleCatalog.Pages)
        {
            if (p.Tab == RoleProfiles.TabHome) { Assert.Null(ModuleCatalog.SectionOf(p.Key)); continue; }
            Assert.NotNull(ModuleCatalog.SectionOf(p.Key));
        }
        // across the two modes of the biggest module every tab 0-23 except Acasă is listed, and within one menu no page twice
        var net = ModuleCatalog.Menu(StationPosture.Internet, AppMode.Network, NoMainTabs).SelectMany(s => s.Pages).Select(p => p.Tab).ToList();
        var air = ModuleCatalog.Menu(StationPosture.Internet, AppMode.AirGapped, NoMainTabs).SelectMany(s => s.Pages).Select(p => p.Tab).ToList();
        Assert.Equal(net.Count, net.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, RoleProfiles.TabTransferRegister + 1).Where(t => t != RoleProfiles.TabHome), net.Union(air).Order());
    }

    [Fact]
    public void The_window_has_no_static_advanced_radio_buttons_and_binds_the_generated_menu()
    {
        var xaml = MainWindow();
        Assert.DoesNotContain("AdvancedNavVisibility", xaml);
        Assert.Contains("ItemsSource=\"{Binding AdvancedMenu}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding Title}\" Style=\"{StaticResource SidebarTabStyle}\"", xaml);
        Assert.Contains("CommandParameter=\"{Binding TabText}\"", xaml);
    }

    [Fact]
    public void An_unknown_menu_key_is_never_shown() =>
        Assert.False(AdvancedNavVisibilityConverter.IsVisible(null, "99", StationPosture.Internet, AppMode.Network));
}
