using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Policy;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Findings of the real-PC walkthrough of 2026-10-23: level-dependent hint of the profile tabs, the policy library after a restart, the LIVE case in „Cazuri recente”.</summary>
[Collection(GlobalStateCollection.Name)]
public sealed class RealPc20261023Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-realpc-" + Guid.NewGuid().ToString("N"));
    public RealPc20261023Tests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // ---- F1 ----
    [Fact]
    public void Profile_tab_hint_follows_the_level_in_both_directions()
    {
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        var vm = new ProfileTableViewModel(ProfileTable.WorkingHours);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Contains("(Day)", vm.Hint);

        LanguageLevelContext.Set(UiLanguageLevel.Simple);
        Assert.Contains(nameof(ProfileTableViewModel.Hint), raised);
        Assert.StartsWith("Ziua: de luni până duminică", vm.Hint);
        Assert.DoesNotContain("(Day)", vm.Hint);

        raised.Clear();
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        Assert.Contains(nameof(ProfileTableViewModel.Hint), raised);
        Assert.Contains("(Day)", vm.Hint);
        GC.KeepAlive(vm);
    }

    // ---- F2 ----
    private PolicyWorkbench RegisterOnePolicy(string root)
    {
        var bench = new PolicyWorkbench(root, [], () => @"STATIE\operator");
        var src = Path.Combine(_dir, "in", "policy.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, "User\nSoftware\\LogAnalyzerRealPcTest\nScreenSaverIsSecure\nSZ:1\n");
        var opened = bench.Open(src);   // imports into the library, like the GPO backup of the walkthrough
        bench.Register(opened.Policy);
        return bench;
    }

    [Fact]
    public void A_registered_policy_is_listed_after_a_restart_and_can_be_reopened_and_the_empty_card_goes_away()
    {
        var root = Path.Combine(_dir, "Policies");
        RegisterOnePolicy(root);

        var vm = new PolicyViewModel { StoreRoot = root };   // a fresh view model = a restarted application
        var entry = Assert.Single(vm.Library);
        Assert.StartsWith("IMPORT-", entry.Id);
        Assert.Equal("1.0.0", entry.Version);
        Assert.False(string.IsNullOrWhiteSpace(entry.Title));
        Assert.Equal("CIORNĂ (DRAFT)", entry.StateText);
        Assert.NotEqual("", entry.DateText);
        Assert.True(vm.HasLibrary);
        Assert.False(vm.NothingOpenAndLibraryEmpty);
        Assert.True(vm.NothingOpen);

        vm.OpenLibraryEntryCommand.Execute(entry);
        Assert.False(vm.NothingOpen);
        Assert.Contains(entry.Id, vm.PolicyInfo);
        Assert.Contains("DRAFT", vm.PolicyInfo);
    }

    [Fact]
    public void With_an_empty_library_the_empty_card_shows()
    {
        var vm = new PolicyViewModel { StoreRoot = Path.Combine(_dir, "empty-store") };
        Assert.Empty(vm.Library);
        Assert.False(vm.HasLibrary);
        Assert.True(vm.NothingOpenAndLibraryEmpty);
    }

    // ---- F3 ----
    [Fact]
    public void A_finished_control_and_a_live_collection_put_the_live_case_in_recent_cases_even_without_case_json()
    {
        var live = Path.Combine(_dir, "Cases", "LIVE-TEST-PC");
        var recent = new RecentCases(Path.Combine(_dir, "recent.json"));
        var home = new HomeViewModel(new InvestigationViewModel(), _ => { }, recent, () => null) { LiveCaseRoot = live };
        Assert.Empty(home.RecentList);

        CollectAllState.Instance.RecordControl("Control stație finalizat: test.");
        var e = Assert.Single(home.RecentList);
        Assert.Equal(Path.GetFullPath(live), e.Entry.Path, ignoreCase: true);
        Assert.Contains("LIVE", e.Entry.Title);

        home.ForgetRecentCommand.Execute(e);
        Assert.Empty(home.RecentList);
        home.NoteLiveCaseUsed();   // the live triage collection
        Assert.Single(home.RecentList);
        GC.KeepAlive(home);
        CollectAllState.Instance.Clear();
    }
}
