using System.IO;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Home;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>WP5: the Home page's view model without a window or a dialog (these tests need Windows to run, like the rest of this project).</summary>
public sealed class HomeViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-homevm-" + Guid.NewGuid().ToString("N"));
    public HomeViewModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch (IOException) { }
    }

    private HomeViewModel New(out InvestigationViewModel inv, List<int>? navigated = null, Func<string?>? pick = null)
    {
        inv = new InvestigationViewModel();
        return new HomeViewModel(inv, t => navigated?.Add(t), new RecentCases(Path.Combine(_dir, "recent.json")), pick);
    }

    private string MakeCase()
    {
        var sample = Path.Combine(_dir, "sample.bin");
        File.WriteAllText(sample, "MZ not really a program");
        var scope = new CaseScope
        {
            Purpose = "test", PeriodFromUtc = DateTimeOffset.UtcNow.AddDays(-1), PeriodToUtc = DateTimeOffset.UtcNow.AddDays(1), SystemsInScope = ["H"], Approver = "a",
            LegalBasis = LegalBasis.Incident, Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
        };
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "caz", scope);
        InvestigationPipeline.Import(ws, [sample]);
        new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        return ws.Root;
    }

    [Fact]
    public void Without_a_case_home_is_undetermined_offers_the_role_intents_and_never_says_safe_or_normal()
    {
        var vm = New(out _);
        Assert.False(vm.HasCase);
        Assert.Equal(AttentionLevel.Undetermined, vm.Attention);
        // WP18 S2: the default profile is the role decided at startup (CONTROL when nothing was decided), at most five intents
        Assert.Equal(StationRole.Control, vm.Profile.Role);
        Assert.Equal(RoleProfiles.MaxPrimary, vm.Intents.Length);
        Assert.Contains("Verifică această stație", vm.Intents);
        Assert.Contains("Deschide un control anterior", vm.Intents);
        Assert.Contains("Raport pentru proces-verbal", vm.Intents);
        Assert.NotEmpty(vm.CoverageRows);
        Assert.All(vm.CoverageRows, r => Assert.NotEqual(CoverageState.Collected, r.State));
        foreach (var t in new[] { vm.AttentionLabel, vm.Problem, vm.Trust, vm.Found, string.Join(" ", vm.NextSteps) })
        {
            Assert.DoesNotContain("SAFE", t); Assert.DoesNotContain("NORMAL", t); Assert.DoesNotContain("SECURED", t);
        }
    }

    [Fact]
    public void The_intents_lead_to_the_investigation_page_with_the_right_source_selected()
    {
        var nav = new List<int>();
        var vm = New(out var inv, nav);
        vm.AnalyzeEvidenceCommand.Execute(null);
        Assert.False(inv.CollectFromThisStation);
        vm.CheckThisComputerCommand.Execute(null);
        Assert.True(inv.CollectFromThisStation);
        Assert.Equal([HomeViewModel.InvestigationTabIndex, HomeViewModel.InvestigationTabIndex], nav);
    }

    [Fact]
    public async Task Role_intents_navigate_with_their_source_and_a_disabled_intent_only_explains_itself()
    {
        var nav = new List<int>();
        var csirt = RoleProfiles.For(StationRole.Csirt, new UnclassifiedEditionProfile(), AppMode.AirGapped);
        var inv = new InvestigationViewModel();
        var vm = new HomeViewModel(inv, t => nav.Add(t), new RecentCases(Path.Combine(_dir, "recent.json")), () => null, csirt);
        Assert.Equal("Stație de sprijin răspuns la incidente", vm.RoleTitle);
        Assert.Equal(RoleProfiles.MaxPrimary, vm.PrimaryIntents.Count);
        Assert.NotEmpty(vm.MoreIntents);

        inv.CollectFromThisStation = true;
        await vm.RunIntentCommand.ExecuteAsync(vm.PrimaryIntents.Single(i => i.Key == "receive_evidence"));
        Assert.False(inv.CollectFromThisStation);
        Assert.Equal([RoleProfiles.TabInvestigation], nav);
        Assert.StartsWith("Primește probe de la o stație:", vm.OpenStatus);

        await vm.RunIntentCommand.ExecuteAsync(vm.MoreIntents.Single(i => i.Key == "check_this_computer"));
        Assert.True(inv.CollectFromThisStation);
        Assert.Equal([RoleProfiles.TabInvestigation, RoleProfiles.TabInvestigation], nav);

        var domain = vm.MoreIntents.Single(i => i.Key == "domain_mail");
        Assert.False(domain.Enabled);
        Assert.StartsWith("Nu este disponibil:", domain.Tooltip);
        await vm.RunIntentCommand.ExecuteAsync(domain);
        Assert.Equal(2, nav.Count);                                    // nothing navigated
        Assert.Contains("nu este disponibil pe această stație", vm.OpenStatus);
        Assert.Contains("izolată", vm.OpenStatus);

        // "open cases" asks for a folder; cancelled here, so nothing opens and the status says so
        await vm.RunIntentCommand.ExecuteAsync(vm.PrimaryIntents.Single(i => i.Key == "open_cases"));
        Assert.False(vm.HasCase);
        Assert.Contains("anulată", vm.OpenStatus);
    }

    [Fact]
    public void Control_role_offers_no_network_intent_and_every_intent_leads_to_a_page()
    {
        var control = RoleProfiles.For(StationRole.Control, new ClassifiedEditionProfile(), AppMode.AirGapped);
        var vm = new HomeViewModel(new InvestigationViewModel(), null, new RecentCases(Path.Combine(_dir, "recent.json")), () => null, control);
        Assert.Equal("Stație de control", vm.RoleTitle);
        Assert.All(vm.PrimaryIntents, i => Assert.True(i.Enabled, i.Reason));
        Assert.DoesNotContain(vm.PrimaryIntents.Concat(vm.MoreIntents), i => i.Availability.Intent.RequiresNetwork);
        Assert.All(vm.PrimaryIntents.Concat(vm.MoreIntents), i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
    }

    [Fact]
    public async Task Opening_an_existing_case_shows_it_on_home_and_on_the_investigation_page_and_remembers_it()
    {
        var root = MakeCase();
        var vm = New(out var inv, pick: () => root);
        await vm.OpenExistingCaseCommand.ExecuteAsync(null);
        Assert.True(vm.HasCase);
        Assert.Contains("CASE-", vm.CaseTitle);
        Assert.NotNull(inv.Loaded);
        Assert.False(vm.IsReadOnly);
        Assert.Contains("Caz deschis", vm.OpenStatus);
        Assert.Single(vm.RecentList);
        Assert.Equal(root, vm.RecentList[0].Entry.Path, ignoreCase: true);
        Assert.NotEmpty(vm.CoverageRows);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_case_is_refused_with_the_reason_and_nothing_is_shown()
    {
        var empty = Path.Combine(_dir, "empty"); Directory.CreateDirectory(empty);
        var vm = New(out var inv, pick: () => empty);
        await vm.OpenExistingCaseCommand.ExecuteAsync(null);
        Assert.False(vm.HasCase);
        Assert.Null(inv.Loaded);
        Assert.Contains("nu a fost deschis", vm.OpenStatus);
        Assert.Contains("case.json", vm.OpenStatus);
        Assert.Empty(vm.RecentList);
    }

    [Fact]
    public async Task A_tampered_case_opens_read_only_and_home_says_why()
    {
        var root = MakeCase();
        var raw = Directory.EnumerateFiles(Path.Combine(root, "Raw"), "*", SearchOption.AllDirectories).First();
        File.SetAttributes(raw, FileAttributes.Normal);
        File.AppendAllText(raw, "TAMPERED");
        var vm = New(out var inv, pick: () => root);
        await vm.OpenExistingCaseCommand.ExecuteAsync(null);
        Assert.True(vm.HasCase);
        Assert.True(vm.IsReadOnly);
        Assert.True(inv.IsReadOnlyCase);
        Assert.Contains("doar pentru citire", vm.ReadOnlyNotice);
        Assert.Contains("Nu folosiți rezultatele ca probă", vm.NextSteps[0]);
        inv.CloseCaseCommand.Execute(null);
        Assert.Contains("doar pentru citire", inv.ClosureText);
    }

    [Fact]
    public async Task A_recent_entry_whose_folder_is_gone_is_shown_as_missing_and_stays_until_the_operator_removes_it()
    {
        var root = MakeCase();
        var vm = New(out _, pick: () => root);
        await vm.OpenExistingCaseCommand.ExecuteAsync(null);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(root, true);
        vm.ReloadRecent();
        var entry = Assert.Single(vm.RecentList);
        Assert.False(entry.Exists);
        Assert.Contains("LIPSĂ", entry.StateText);
        await vm.OpenRecentCommand.ExecuteAsync(entry);
        Assert.Contains("nu mai există", vm.OpenStatus);
        Assert.Single(vm.RecentList);
        vm.ForgetRecentCommand.Execute(entry);
        Assert.Empty(vm.RecentList);
    }
}
