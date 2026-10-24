using LogAnalyzer.Dfir.Model;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>The case scope is asked at the start of a control or investigation; a complete scope is not asked again later (report time).</summary>
[Collection("LiveCaseStatic")]
public sealed class ScopeAtStartTests : IDisposable
{
    private static CaseScope Complete() => new()
    {
        Purpose = "control periodic", Approver = "sef structura", SystemsInScope = ["STATIE1"],
        PeriodFromUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), PeriodToUtc = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
        LegalBasis = LegalBasis.Control, Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified,
        AuthorizingAct = "Ordin 12/2026",
    };

    public ScopeAtStartTests() { LiveCase.ResetSessionScope(); LiveCase.ScopePrompt = null; }
    public void Dispose() { LiveCase.ResetSessionScope(); LiveCase.ScopePrompt = null; }

    [Fact]
    public void Scope_is_asked_once_at_start_and_not_again_at_report_time()
    {
        int prompts = 0;
        LiveCase.ScopePrompt = (_, _) => { prompts++; return Complete(); };

        Assert.True(LiveCase.EnsureScopeAtStart());
        Assert.Equal(1, prompts);

        // report time: a second start-style request and the confirmed state do not prompt again
        Assert.True(LiveCase.EnsureScopeAtStart());
        Assert.True(LiveCase.ScopeConfirmed);
        Assert.Equal(1, prompts);
        Assert.Equal("Ordin 12/2026", LiveCase.CurrentScope!.AuthorizingAct);
    }

    [Fact]
    public void Closing_the_dialog_without_a_complete_scope_confirms_nothing_and_asks_again_later()
    {
        int prompts = 0;
        LiveCase.ScopePrompt = (_, _) => { prompts++; return null; };

        Assert.False(LiveCase.EnsureScopeAtStart());
        Assert.False(LiveCase.ScopeConfirmed);
        Assert.False(LiveCase.EnsureScopeAtStart());
        Assert.Equal(2, prompts);
    }

    [Fact]
    public void An_incomplete_scope_from_the_dialog_is_not_accepted()
    {
        LiveCase.ScopePrompt = (_, _) => new CaseScope { Purpose = "x" };
        Assert.False(LiveCase.EnsureScopeAtStart());
        Assert.False(LiveCase.ScopeConfirmed);
    }

    [Fact]
    public void Headless_use_without_a_prompt_does_not_confirm_or_throw()
    {
        Assert.False(LiveCase.EnsureScopeAtStart());
    }
}
