using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public class LogClearAssessmentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 14, 0, 0, TimeSpan.Zero);

    private static LogClearEvent Clear(int minute, string channel = "Security", string user = "alice", string domain = "CORP") =>
        new(channel, T0.AddMinutes(minute), user, domain);

    private static LogMaintenancePolicy Policy(string account = @"CORP\alice", int fromMin = -30, int toMin = 30) =>
        new([account], [new MaintenanceWindow(T0.AddMinutes(fromMin), T0.AddMinutes(toMin))]);

    [Fact]
    public void Without_policy_a_lone_clear_is_not_assessed_and_Medium_never_High()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(0)], null, []));
        Assert.Equal(LogClearLifecycle.NotAssessed, a.Lifecycle);
        Assert.Equal(Severity.Medium, a.Severity);
        Assert.Empty(a.Factors);
        Assert.Contains("necesită verificare", a.Reason);
        Assert.Contains("nu există profil de procedură", a.Reason);
    }

    [Fact]
    public void Approved_account_inside_window_is_Routine_Info()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(0)], Policy(), []));
        Assert.Equal(LogClearLifecycle.Routine, a.Lifecycle);
        Assert.Equal(Severity.Info, a.Severity);
    }

    [Fact]
    public void Account_match_is_case_insensitive_and_accepts_bare_user_name()
    {
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.Assess([Clear(0, user: "ALICE")], Policy(@"corp\Alice"), [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.Assess([Clear(0)], Policy("alice"), [])[0].Lifecycle);
    }

    [Fact]
    public void Policy_defined_but_account_not_approved_is_Unexpected_High()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(0, user: "mallory")], Policy(), []));
        Assert.Equal(LogClearLifecycle.Unexpected, a.Lifecycle);
        Assert.Equal(Severity.High, a.Severity);
    }

    [Fact]
    public void Policy_defined_but_outside_window_is_Unexpected_High()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(120)], Policy(), []));
        Assert.Equal(LogClearLifecycle.Unexpected, a.Lifecycle);
        Assert.Equal(Severity.High, a.Severity);
    }

    [Fact]
    public void Window_boundaries_are_inclusive()
    {
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.Assess([Clear(30)], Policy(), [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.Assess([Clear(-30)], Policy(), [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.Unexpected, LogClearAssessment.Assess([Clear(31)], Policy(), [])[0].Lifecycle);
    }

    [Fact]
    public void Empty_policy_object_means_nothing_is_approved_so_Unexpected()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(0)], new LogMaintenancePolicy([], []), []));
        Assert.Equal(LogClearLifecycle.Unexpected, a.Lifecycle);
    }

    [Fact]
    public void Several_channels_by_same_subject_within_10_minutes_is_a_factor_and_High()
    {
        var items = LogClearAssessment.Assess([Clear(0, "Security"), Clear(8, "System")], null, []);
        Assert.All(items, i => { Assert.Equal(Severity.High, i.Severity); Assert.Contains(i.Factors, x => x.Contains("canale")); });
    }

    [Fact]
    public void Several_channels_further_apart_or_by_different_subjects_is_no_factor()
    {
        Assert.All(LogClearAssessment.Assess([Clear(0, "Security"), Clear(11, "System")], null, []), i => Assert.Empty(i.Factors));
        Assert.All(LogClearAssessment.Assess([Clear(0, "Security"), Clear(2, "System", user: "bob")], null, []), i => Assert.Empty(i.Factors));
        // same channel twice is not "several channels"
        Assert.All(LogClearAssessment.Assess([Clear(0, "Security"), Clear(2, "Security")], null, []), i => Assert.Empty(i.Factors));
    }

    [Fact]
    public void Clear_near_another_High_finding_is_a_factor_and_High()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(0)], null, [T0.AddMinutes(60)]));
        Assert.Equal(Severity.High, a.Severity);
        Assert.Contains(a.Factors, x => x.Contains("60 min"));
        var far = Assert.Single(LogClearAssessment.Assess([Clear(0)], null, [T0.AddMinutes(61)]));
        Assert.Equal(Severity.Medium, far.Severity);
    }

    [Fact]
    public void A_factor_escalates_even_a_Routine_clear()
    {
        var a = Assert.Single(LogClearAssessment.Assess([Clear(0)], Policy(), [T0.AddMinutes(5)]));
        Assert.Equal(LogClearLifecycle.Routine, a.Lifecycle);
        Assert.Equal(Severity.High, a.Severity);
    }

    [Fact]
    public void A_clear_alone_is_never_Critical()
    {
        var all = LogClearAssessment.Assess([Clear(0, "Security"), Clear(1, "System"), Clear(2, "Application", user: "x")], Policy("nobody"), [T0]);
        Assert.All(all, i => Assert.True(i.Severity <= Severity.High));
        Assert.Equal(Severity.High, LogClearAssessment.Overall(all));
    }

    [Fact]
    public void Overall_is_the_maximum_and_Info_for_none()
    {
        Assert.Equal(Severity.Info, LogClearAssessment.Overall([]));
        Assert.Equal(Severity.Medium, LogClearAssessment.Overall(LogClearAssessment.Assess([Clear(0)], null, [])));
    }

    [Fact]
    public void Time_of_day_never_changes_severity()
    {
        var night = new LogClearEvent("Security", new DateTimeOffset(2026, 9, 19, 3, 0, 0, TimeSpan.Zero), "alice", "CORP");
        var noon = new LogClearEvent("Security", new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero), "alice", "CORP");
        Assert.Equal(LogClearAssessment.Assess([noon], null, [])[0].Severity, LogClearAssessment.Assess([night], null, [])[0].Severity);
    }

    [Fact]
    public void Reason_does_not_assert_intent()
    {
        var items = LogClearAssessment.Assess([Clear(0, user: "mallory")], Policy(), [T0]);
        Assert.All(items, i => Assert.DoesNotContain("intenționat", i.Reason + string.Join(" ", i.Factors)));
    }
}
