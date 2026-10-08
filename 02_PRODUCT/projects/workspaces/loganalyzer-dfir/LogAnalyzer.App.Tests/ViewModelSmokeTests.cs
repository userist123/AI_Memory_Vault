using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Smoke tests of the application's ViewModels: they construct without a WPF Application or a dispatcher, expose the
/// documented defaults, and the commands that can be decided without touching the machine (no dialog, no collection,
/// no firewall) behave. They do not render any view.
/// </summary>
public sealed class ViewModelSmokeTests
{
    [Fact]
    public void Every_public_viewmodel_of_the_app_is_an_observable_object()
    {
        var vms = typeof(InvestigationViewModel).Assembly.GetTypes()
            .Where(t => t.Namespace == "LogAnalyzer.UI.ViewModels" && t.IsPublic && t.Name.EndsWith("ViewModel", StringComparison.Ordinal)).ToList();
        Assert.True(vms.Count >= 6, "expected the six known ViewModels, found " + vms.Count);
        Assert.All(vms, t => Assert.True(typeof(ObservableObject).IsAssignableFrom(t), t.Name));
    }

    [Fact]
    public void Investigation_defaults_and_collections_are_empty_before_a_run()
    {
        var vm = new InvestigationViewModel();
        Assert.Equal(3, vm.Profiles.Length);
        Assert.Equal(1, vm.ProfileIndex);
        Assert.True(vm.CollectFromThisStation);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Findings);
        Assert.Empty(vm.Timeline);
        Assert.Empty(vm.ImportFiles);
        Assert.Empty(vm.GraphEntities);
        Assert.StartsWith("http://127.0.0.1", vm.AiEndpoint);          // the AI endpoint is loopback by default
        Assert.Contains("UNPROVEN", vm.AiStatus);                        // AI output is presented as unproven until verified
    }

    [Fact]
    public void Investigation_without_a_result_says_so_instead_of_inventing_a_path()
    {
        var vm = new InvestigationViewModel { PathFrom = "a", PathTo = "b" };
        vm.FindPathCommand.Execute(null);
        Assert.Contains("investigația", vm.GraphStatus);
        Assert.Empty(vm.GraphEdges);
        vm.GraphSearch = "anything";                                     // search over no graph must not throw
        Assert.Empty(vm.GraphEntities);
    }

    [Fact]
    public async Task Investigation_run_with_nothing_to_collect_or_import_refuses_and_stays_idle()
    {
        var vm = new InvestigationViewModel { CollectFromThisStation = false };
        await vm.RunCommand.ExecuteAsync(null);
        Assert.False(vm.IsBusy);
        Assert.Contains("adăugați probe", vm.Log);
        Assert.Empty(vm.Findings);
    }

    [Fact]
    public void Investigation_property_changes_are_announced_to_the_view()
    {
        var vm = new InvestigationViewModel();
        var seen = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        vm.CaseName = "caz de test";
        vm.TimelineFilter = "x";
        Assert.Contains(nameof(vm.CaseName), seen);
        Assert.Contains(nameof(vm.TimelineFilter), seen);
    }

    [Fact]
    public async Task Station_control_rejects_an_invalid_period_without_collecting_anything()
    {
        var vm = new StationControlViewModel { PeriodDays = 0 };
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Contains("Perioada", vm.Status);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Checks);
    }

    [Fact]
    public void Station_control_report_cannot_be_saved_before_the_control_ran()
    {
        var vm = new StationControlViewModel();
        Assert.Equal(90, vm.PeriodDays);
        Assert.Equal(5, vm.StatusFilters.Length);
        vm.SaveReportCommand.Execute(null);                              // no report yet: a message, no file, no dialog
        Assert.Contains("întâi", vm.Status);
        vm.StatusFilter = "NECONFORM";                                   // filtering an empty report must not throw
        Assert.Empty(vm.Checks);
    }

    [Fact]
    public void Policy_viewmodel_starts_with_no_policy_open_and_says_nothing_is_applied_without_approval()
    {
        var vm = new PolicyViewModel();
        Assert.Equal("Nicio politică deschisă.", vm.PolicyInfo);
        Assert.Contains("Nimic nu se aplică fără aprobare", vm.Status);
        Assert.Empty(vm.PlanRows);
        Assert.Empty(vm.Results);
    }
}
