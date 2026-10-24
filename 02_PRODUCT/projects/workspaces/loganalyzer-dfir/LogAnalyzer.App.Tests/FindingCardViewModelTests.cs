using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>WP6a: the finding card's view model and the selection on the investigation page (needs Windows to run, like the rest of this project).</summary>
public sealed class FindingCardViewModelTests
{
    private static Finding Sample() => new()
    {
        FindingId = "F-1", RuleId = "X", Title = "Titlu", Description = "Ce s-a întâmplat.", Severity = Severity.High, Classification = Classification.Direct,
        SupportingEvidence = [new("EV-1", "r=1", "o înregistrare", "")], ClassificationReason = "Motiv.",
    };

    [Fact]
    public void Each_action_opens_its_panel_and_a_second_click_closes_it()
    {
        var vm = new FindingCardViewModel(FindingCardModel.Build(Sample(), EvidenceContext.Empty));
        Assert.Equal("", vm.ActivePanel);

        vm.ShowWhyCommand.Execute(null);
        Assert.True(vm.IsWhyOpen);
        Assert.False(vm.IsEvidenceOpen);
        vm.ShowEvidenceCommand.Execute(null);
        Assert.True(vm.IsEvidenceOpen);
        Assert.False(vm.IsWhyOpen);
        vm.ShowVerifyCommand.Execute(null);
        Assert.True(vm.IsVerifyOpen);
        vm.ShowTodoCommand.Execute(null);
        Assert.True(vm.IsTodoOpen);
        vm.ShowTodoCommand.Execute(null);
        Assert.Equal("", vm.ActivePanel);
    }

    [Fact]
    public void The_why_text_is_the_plain_text_of_the_explanation_with_its_five_parts()
    {
        var vm = new FindingCardViewModel(FindingCardModel.Build(Sample(), EvidenceContext.Empty));
        foreach (var part in new[] { "Ce am observat", "Dovezi", "Raționament", "Limite", "Verificare" }) Assert.Contains(part, vm.WhyText);
    }

    [Fact]
    public void Selecting_a_finding_builds_its_card_and_clearing_the_selection_removes_it()
    {
        var inv = new InvestigationViewModel();
        Assert.Null(inv.SelectedCard);
        inv.SelectedFinding = Sample();
        Assert.NotNull(inv.SelectedCard);
        Assert.Equal("Titlu", inv.SelectedCard!.Card.Title);
        Assert.Equal("Ridicată", inv.SelectedCard.Card.SeverityText);
        inv.SelectedFinding = null;
        Assert.Null(inv.SelectedCard);
    }
}
