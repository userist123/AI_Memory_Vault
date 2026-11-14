using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Licence bound to the interface and to the commands (2026-11-05): a page whose capability the licence lacks is not listed, not a main page of the role and cannot
/// be opened by a command; LicenseService.Has answers from the licence file as it is now. Q1..Q5 of the task; keys are ephemeral, in memory.
/// </summary>
[Collection("Global authentication state")]
public sealed class LicenseBindingTests : IDisposable
{
    private const string Hwid = "ABCD1234EFAB5678";
    private static readonly DateTime Expiry = new(2099, 1, 1);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-licbind-" + Guid.NewGuid().ToString("N"));
    private string LicFile => Path.Combine(_dir, "license.lic");

    public LicenseBindingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private LicenseService Service(StationPosture p) => new([Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())], LicFile, () => Hwid, p);

    private string Issue(StationPosture p, params string[] features) =>
        LicenseService.SignV2(_key, Hwid, Expiry, LicenseFeatures.ModuleId(p), "Unitatea X", features, "LIC1");

    private static IEditionProfile EditionOf(StationPosture module) =>
        module == StationPosture.Standalone ? new ClassifiedEditionProfile() : new NetworkEditionProfile();

    // ---- Q1 ----

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void Q1_a_licence_without_registers_removes_pages_20_and_23_from_the_menu_and_from_the_main_pages(StationPosture module)
    {
        var rights = LicenseFeatures.DefaultFor(module).Where(f => f != LicenseFeatures.Registers).ToList();
        bool Has(string f) => rights.Contains(f);
        // control: with the full base set the pages are there (the test would be empty otherwise)
        var fullMenu = ModuleCatalog.Menu(module, AppMode.Network, []).SelectMany(s => s.Pages).Select(p => p.Tab).ToList();
        Assert.Contains(20, fullMenu); Assert.Contains(23, fullMenu);

        var menu = ModuleCatalog.Menu(module, AppMode.Network, [], Has).SelectMany(s => s.Pages).Select(p => p.Tab).ToList();
        Assert.DoesNotContain(20, menu); Assert.DoesNotContain(23, menu);
        Assert.Contains(14, menu); // only the missing capability is hidden

        foreach (var role in new[] { StationRole.Control, StationRole.Csirt })
        {
            var p = RoleProfiles.For(role, EditionOf(module), AppMode.Network, Has);
            var tabs = p.PrimaryIntents.Concat(p.MoreIntents).Select(i => i.TargetTab).Concat(p.Navigation.Select(n => n.Tab)).ToList();
            Assert.DoesNotContain(20, tabs); Assert.DoesNotContain(23, tabs);
            Assert.DoesNotContain(p.MoreIntents, i => i.Key == "users_register");
        }
        // control: the same role with the full base set does show both
        var control = RoleProfiles.For(StationRole.Control, EditionOf(module), AppMode.Network, f => LicenseFeatures.DefaultFor(module).Contains(f));
        Assert.Contains(control.Navigation, n => n.Tab == 23); Assert.Contains(control.Navigation, n => n.Tab == 20);
    }

    [Fact]
    public void Q1b_each_page_declares_the_capability_the_task_names()
    {
        var expected = new Dictionary<string, string?>
        {
            ["14"] = "control.station", ["19"] = "control.station", ["16"] = "case.local", ["12"] = "case.local", ["9"] = "case.local",
            ["23"] = "registers", ["20"] = "registers", ["8.ad"] = "auth.ad", ["11"] = "collect.lan", ["13"] = "response.host", ["15"] = "collect.lan",
            ["7"] = null, ["0"] = null, ["17"] = null, ["22"] = null,
        };
        foreach (var (key, cap) in expected) Assert.Equal(cap, ModuleCatalog.Find(key)!.Capability);
        Assert.All(ModuleCatalog.Pages.Where(p => p.Capability is not null), p => Assert.True(LicenseFeatures.IsKnown(p.Capability)));
    }

    // ---- Q5 (and the old signatures) ----

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void Q5_a_legacy_LA1_licence_gives_the_base_set_and_the_menu_stays_the_one_of_today(StationPosture module)
    {
        File.WriteAllText(LicFile, LicenseService.Sign(_key, Hwid, Expiry, "LEG1"));
        var svc = Service(module);
        var claims = svc.CurrentLicense();
        Assert.NotNull(claims);
        Assert.True(claims!.IsLegacy);
        Assert.Equal(LicenseFeatures.DefaultFor(module), claims.Features);

        static string Dump(IReadOnlyList<ModuleCatalog.MenuSection> m) => string.Join("|", m.Select(s => s.Name + ":" + string.Join(",", s.Pages.Select(p => p.Key))));
        foreach (var mode in new[] { AppMode.AirGapped, AppMode.Network })
        {
            // the menu driven by the legacy licence equals the old-signature menu (which uses DefaultFor(module))
            Assert.Equal(Dump(ModuleCatalog.Menu(module, mode, [])), Dump(ModuleCatalog.Menu(module, mode, [], svc.Has)));
            // and the old menu is the one with no licence restriction at all (every base capability present)
            Assert.Equal(Dump(ModuleCatalog.Menu(module, mode, [], _ => true)), Dump(ModuleCatalog.Menu(module, mode, [])));
        }
    }

    // ---- Q3 / Q4 ----

    [Fact]
    public void Q3_a_replaced_licence_file_with_fewer_rights_is_answered_from_the_new_file()
    {
        var p = StationPosture.Internet;
        File.WriteAllText(LicFile, Issue(p, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal, LicenseFeatures.Registers));
        var svc = Service(p);
        Assert.True(svc.Has(LicenseFeatures.Registers));
        File.WriteAllText(LicFile, Issue(p, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal));
        Assert.False(svc.Has(LicenseFeatures.Registers));   // no CurrentLicense() call in between: Has itself follows the file
        Assert.True(svc.Has(LicenseFeatures.CaseLocal));
    }

    [Fact]
    public void Q4_a_deleted_licence_file_grants_nothing_not_even_the_base_set()
    {
        // Choice: Has is false for EVERYTHING when the file is gone (fail closed in the service). The running window keeps the rights it read at start
        // (MainViewModel) and, if it could not read any, the base set of the module, so the application is not blocked.
        var p = StationPosture.Internet;
        File.WriteAllText(LicFile, Issue(p, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal));
        var svc = Service(p);
        Assert.True(svc.Has(LicenseFeatures.ControlStation));
        File.Delete(LicFile);
        Assert.All(LicenseFeatures.All, f => Assert.False(svc.Has(f), f));
    }

    // ---- Q2 ----

    [Fact]
    public void Q2_NavigateCommand_to_tab_23_without_registers_does_not_change_the_page_and_says_why()
    {
        var p = StationPostureContext.Current;
        File.WriteAllText(LicFile, Issue(p, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal));
        var appData = Path.Combine(_dir, "appdata");
        Wp18AccessibilityTests.OnSta(() =>
        {
            var required = LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired;
            try
            {
                var services = LogAnalyzer.UI.AppComposition.BuildServices(appData);
                services.RemoveAll<LicenseService>();
                var licence = Service(p);
                Assert.Equal(LicenseCheck.Valid, licence.CheckSaved());   // the start-up check of App.xaml.cs, before the main window (review of R2b)
                services.AddSingleton(licence);
                var provider = services.BuildServiceProvider();
                provider.GetRequiredService<LogAnalyzer.Core.Interfaces.IDatabaseService>().InitializeDatabase();
                LogAnalyzer.UI.Services.AuthApp.Initialize(Path.Combine(_dir, "auth"));
                var vm = provider.GetRequiredService<LogAnalyzer.UI.ViewModels.MainViewModel>();

                vm.NavigateCommand.Execute("14");
                Assert.Equal(14, vm.SelectedTabIndex);
                vm.StatusMessage = "";
                vm.NavigateCommand.Execute("23");
                Assert.Equal(14, vm.SelectedTabIndex);
                Assert.Equal("Această funcție nu este inclusă în licența acestei stații.", vm.StatusMessage);
                vm.NavigateCommand.Execute("20");
                Assert.Equal(14, vm.SelectedTabIndex);
                Assert.DoesNotContain(vm.AdvancedMenu.SelectMany(s => s.Pages), pg => pg.Tab is 20 or 23);
                Assert.DoesNotContain(vm.RoleNavigation, n => n.Tab is 20 or 23);
            }
            finally { LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired = required; }
            return 0;
        });
    }
    // ---- review of R2b ----

    [Fact]
    public void R1_the_window_keeps_the_rights_verified_at_start_when_the_licence_expires_or_disappears_before_sign_in_ends()
    {
        var svc = Service(StationPosture.Internet);
        File.WriteAllText(LicFile, Issue(StationPosture.Internet, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal));
        Assert.Equal(LicenseCheck.Valid, svc.CheckSaved());          // the start-up check (App.xaml.cs)
        File.Move(LicFile, LicFile + ".moved");                       // renamed (or expired) while the sign-in dialog is open
        Assert.Null(svc.CurrentLicense());
        var rights = LogAnalyzer.UI.ViewModels.MainViewModel.RightsFrom(svc);
        Assert.Equal([LicenseFeatures.CaseLocal, LicenseFeatures.ControlStation], rights.OrderBy(r => r, StringComparer.Ordinal));
        Assert.DoesNotContain(LicenseFeatures.ResponseHost, rights);   // never the module's full set
    }

    [Fact]
    public void R1_a_licence_service_that_verified_nothing_grants_no_capability()
    {
        File.WriteAllText(LicFile, "garbage");
        var svc = Service(StationPosture.Internet);
        Assert.NotEqual(LicenseCheck.Valid, svc.CheckSaved());
        Assert.Empty(LogAnalyzer.UI.ViewModels.MainViewModel.RightsFrom(svc));
    }

    [Fact]
    public void R2_the_tab_start_argument_goes_through_the_licence_gate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.slnx"))) dir = dir.Parent;
        var app = File.ReadAllText(Path.Combine(dir!.FullName, "LogAnalyzer.App", "App.xaml.cs"));
        Assert.Contains("mvm.OpenTab(tab)", app);
        Assert.DoesNotContain("mvm.SelectedTabIndex = tab", app);
    }

    [Theory]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void R3_tab_8_in_network_mode_is_active_directory_and_needs_auth_ad(StationPosture module)
    {
        var rights = LicenseFeatures.DefaultFor(module).Where(f => f != LicenseFeatures.AuthAd).ToList();
        bool Has(string f) => rights.Contains(f);
        Assert.False(ModuleCatalog.CanOpenTab(8, module, Has, AppMode.Network));
        Assert.True(ModuleCatalog.CanOpenTab(8, module, Has, AppMode.AirGapped));                       // local accounts need no capability
        Assert.True(ModuleCatalog.CanOpenTab(8, module, f => LicenseFeatures.DefaultFor(module).Contains(f), AppMode.Network));
    }
}