using System.Linq;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Xunit;
namespace LogAnalyzer.App.Tests;

/// <summary>Master plan 6.1, „Flux de lucru” tab: the page shows what CaseWorkflow answered and refreshes from the chained log.</summary>
[Collection("CollectAllState")]
public sealed class WorkflowViewModelTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { CollectAllState.Instance.Clear(); _k.Dispose(); }

    [Fact]
    public void Without_a_case_the_tab_says_so_and_refuses_actions()
    {
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.WorkflowAddParticipantCommand.Execute(null);
        Assert.Equal("Deschideți sau creați întâi un caz.", vm.WorkflowStatus);
    }

    [Fact]
    public void An_operator_starts_the_workflow_moves_the_case_and_sees_the_checklist()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(CaseLoader.OpenAsync(ws.Root).GetAwaiter().GetResult().Case!);
        Assert.Contains("flux nepornit", vm.WorkflowStageText);

        vm.NewParticipantRole = 0;                                    // investigator, own account
        vm.WorkflowAddParticipantCommand.Execute(null);
        Assert.Contains("rolul de investigator", vm.WorkflowStatus);
        Assert.Single(vm.WorkflowParticipants);
        Assert.Equal("Triere", vm.SelectedNextStage);

        vm.WorkflowMoveCommand.Execute(null);
        Assert.Contains("Etapa: Triere", vm.WorkflowStageText);
        Assert.Contains("jurnal înlănțuit, intact", vm.WorkflowStageText);

        vm.WorkflowCloseCommand.Execute(null);                        // not an approver
        Assert.StartsWith("Refuzat:", vm.WorkflowStatus);
        Assert.Contains(vm.WorkflowChecklist, i => i.Id == "approver" && i.Status == ChecklistStatus.Fail);
        Assert.Contains(vm.WorkflowHistory, h => h.What.Contains("Nou → Triere"));
        Assert.Equal(OperatorIdentity.Who, new CaseWorkflow(ws).Load().History.First().Actor);
    }
}
