using LogAnalyzer.Dfir.Flow;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Checks only the controller can do: every question is answered Da / NU / Nu am putut verifica; unanswered is „Neverificat”, never „în regulă”.</summary>
public sealed class ManualChecksTests
{
    [Fact]
    public void Questions_are_unique_and_explained()
    {
        Assert.Equal(ManualChecks.All.Count, ManualChecks.All.Select(c => c.Id).Distinct().Count());
        Assert.All(ManualChecks.All, c => { Assert.EndsWith("?", c.Question); Assert.True(c.Why.Length > 20 && c.How.Length > 20); });
    }

    [Fact]
    public void Summary_never_counts_unanswered_or_unverifiable_as_fine()
    {
        var s = ManualChecks.Summary([new("M01", ManualAnswer.Yes, ""), new("M02", ManualAnswer.No, "versiune veche"), new("M03", ManualAnswer.CannotCheck, "")]);
        Assert.Equal($"Verificări făcute de controlor: 1 cu răspuns NU · 1 nu au putut fi verificate · {ManualChecks.All.Count - 3} neverificate · 1 în regulă", s);
        Assert.Equal("Neverificat", ManualChecks.AnswerText(ManualAnswer.NotAnswered));
    }

    [Fact]
    public void Radio_buttons_set_one_answer()
    {
        var item = new ManualCheckItem(ManualChecks.All[0]) { IsNo = true };
        Assert.Equal(ManualAnswer.No, item.Answer);
        item.IsCannot = true;
        Assert.False(item.IsNo);
        Assert.Equal(ManualAnswer.CannotCheck, item.ToResult().Answer);
    }
}
