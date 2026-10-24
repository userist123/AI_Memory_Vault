using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Owner 2026-10-11: the policy screen says the one next step and why; the settings are explained in plain words.</summary>
public class PolicyNextStepTests
{
    private static PolicyRecord Rec(PolicyStatus s, string by = "PC\\ion") => new() { Id = "P", Version = "1", Sha256 = "AB", Author = "autor", RegisteredBy = by, Status = s };

    [Fact]
    public void The_lifecycle_is_walked_one_explained_step_at_a_time()
    {
        Assert.Equal(PolicyStepKind.Open, PolicyNextStep.For(false, null, "PC\\ion", false, false, 0, true).Kind);
        Assert.Equal(PolicyStepKind.Register, PolicyNextStep.For(true, null, "PC\\ion", false, false, 0, true).Kind);
        Assert.Equal(PolicyStepKind.Validate, PolicyNextStep.For(true, Rec(PolicyStatus.Draft), "PC\\ion", false, false, 0, true).Kind);
        Assert.Equal(PolicyStepKind.DryRun, PolicyNextStep.For(true, Rec(PolicyStatus.Validated), "PC\\ion", false, false, 0, true).Kind);
        Assert.Equal(PolicyStepKind.ComputePlan, PolicyNextStep.For(true, Rec(PolicyStatus.Approved), "PC\\ion", false, false, 0, true).Kind);
        Assert.Equal(PolicyStepKind.Apply, PolicyNextStep.For(true, Rec(PolicyStatus.Approved), "PC\\ion", false, true, 3, true).Kind);
        Assert.Equal(PolicyStepKind.Assess, PolicyNextStep.For(true, Rec(PolicyStatus.Approved), "PC\\ion", false, true, 0, true).Kind);
        Assert.Equal(PolicyStepKind.Assess, PolicyNextStep.For(true, Rec(PolicyStatus.Deployed), "PC\\ion", false, true, 0, true).Kind);
    }

    [Fact]
    public void Whoever_registered_the_policy_is_told_that_another_account_must_approve_and_gets_no_button()
    {
        var n = PolicyNextStep.For(true, Rec(PolicyStatus.Tested), "pc\\ION", true, false, 0, true);
        Assert.Equal(PolicyStepKind.WaitForApprover, n.Kind);
        Assert.False(n.CanAct);
        Assert.Contains("alt cont Windows", n.Text);
    }

    [Fact]
    public void The_approver_is_told_to_write_the_reason_before_the_button_appears()
    {
        var noReason = PolicyNextStep.For(true, Rec(PolicyStatus.Tested), "PC\\maria", false, false, 0, true);
        Assert.False(noReason.CanAct);
        Assert.Contains("Justificare aprobare", noReason.Text);
        Assert.True(PolicyNextStep.For(true, Rec(PolicyStatus.Tested), "PC\\maria", true, false, 0, true).CanAct);
    }

    [Fact]
    public void The_classified_edition_is_offered_the_assessment_instead_of_apply()
    {
        var n = PolicyNextStep.For(true, Rec(PolicyStatus.Approved), "PC\\ion", false, true, 5, canApply: false);
        Assert.Equal(PolicyStepKind.Assess, n.Kind);
        Assert.Contains("nu modifică stația", n.Text);
    }

    [Fact]
    public void Settings_are_explained_by_kind_and_unknown_registry_values_are_not_given_a_meaning()
    {
        Assert.Contains("Bluetooth", CodeExplainer.Setting(new SettingRef("service", "", "", "bthserv", "")).What);
        Assert.Contains("0 = nu se blochează", CodeExplainer.Setting(new SettingRef("secpol", "", "", "LockoutBadCount", "")).Why);
        Assert.Contains("„Logon”", CodeExplainer.Setting(new SettingRef("audit", "", "", "Logon", "")).What);
        var usb = CodeExplainer.Setting(new SettingRef("registry", "HKLM", @"SYSTEM\CurrentControlSet\Services\USBSTOR", "Start", "REG_DWORD"));
        Assert.Contains("4 = blocat", usb.When);
        var unknown = CodeExplainer.Setting(new SettingRef("registry", "HKLM", @"Software\Policies\Microsoft\Internet Explorer\Main", "Xyz", "REG_DWORD"));
        Assert.Contains("impusă prin politica de grup", unknown.What);
        Assert.Contains("nu este în catalogul aplicației", unknown.When);
    }
}
