using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Evaluation 2026-11-05: on the real case the attack-chain page said „Atacatorul a parcurs cu succes 5 stadii critice” and placed a normal
/// log-service stop at shutdown (informational) as the persistence stage. The grouping is now said to be what it is and uses only high/critical alerts.
/// </summary>
public sealed class AttackStorylineWordingTests
{
    private static DetectedIssue I(string title, string severity, string mitre) => new() { Title = title, Severity = severity, MitreTechniqueId = mitre };

    [Fact]
    public void Low_and_informational_alerts_are_not_attack_stages()
    {
        var s = new AttackStorylineEngine().GenerateStoryline([I("oprire normală", "Info", "T1190"), I("acces din Internet", "High", "T1190")]);
        Assert.All(s.Nodes, n => Assert.NotEqual("oprire normală", n.Title));
        Assert.Contains(s.Nodes, n => n.Title == "acces din Internet");
    }

    [Fact]
    public void The_summary_does_not_claim_a_verified_attack()
    {
        var s = new AttackStorylineEngine().GenerateStoryline([I("acces din Internet", "High", "T1190")]);
        Assert.DoesNotContain("cu succes", s.OverallSummary);
        Assert.Contains("nu o reconstituire verificată", s.OverallSummary);
        Assert.Equal("RIDICAT", s.RiskLevel);
        Assert.Contains("nevalidată", new AttackStoryline().IncidentTitle);
    }

    [Fact]
    public void Only_low_alerts_give_no_attack_chain() =>
        Assert.Empty(new AttackStorylineEngine().GenerateStoryline([I("x", "Low", "T1190"), I("y", "Medium", "T1059.001")]).Nodes);
}
