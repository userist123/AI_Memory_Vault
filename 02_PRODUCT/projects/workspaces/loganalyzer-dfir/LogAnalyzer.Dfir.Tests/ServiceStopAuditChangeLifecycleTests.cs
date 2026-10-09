using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Owner decision 27: Security 1100 (logging service stopped) and 4719 (audit policy changed) get the log-clear lifecycle.</summary>
public class ServiceStopAuditChangeLifecycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 14, 0, 0, TimeSpan.Zero);
    private static LogMaintenancePolicy Policy(string account = @"CORP\alice") => new([account], [new MaintenanceWindow(T0.AddMinutes(-30), T0.AddMinutes(30))]);
    private static SystemLifecycleEvent Sys(int id, int minute) => new(T0.AddMinutes(minute), "System", id);
    private static ServiceStopEvent Stop(string user = "", string domain = "") => new(T0, user, domain);
    private static AuditPolicyChangeEvent Change(string user, string changes, string sid = "", string domain = "CORP") => new(T0, user, domain, sid, changes);

    // ---- 1100 ----

    [Theory]
    [InlineData(1074, 3)]     // restart initiated 3 min after
    [InlineData(1074, -4)]    // ... or just before the service stops
    [InlineData(6006, 2)]     // event log service stopped
    [InlineData(6005, 9)]     // next boot
    [InlineData(6006, 10)]    // 10 min is inclusive
    public void Stop_followed_by_a_shutdown_record_within_10_minutes_is_Routine_Info_with_or_without_profile(int id, int minute)
    {
        foreach (var policy in new[] { null, Policy() })
        {
            var a = LogClearAssessment.AssessServiceStop(Stop(), policy, [Sys(id, minute)]);
            Assert.Equal(LogClearLifecycle.Routine, a.Lifecycle);
            Assert.Equal(Severity.Info, a.Severity);
            Assert.Contains($"System {id}", a.Reason);
        }
    }

    [Fact]
    public void Stop_with_shutdown_record_more_than_10_minutes_away_is_not_paired()
    {
        Assert.Equal(LogClearLifecycle.NotAssessed, LogClearAssessment.AssessServiceStop(Stop(), null, [Sys(1074, 11)]).Lifecycle);
        Assert.Equal(LogClearLifecycle.NotAssessed, LogClearAssessment.AssessServiceStop(Stop(), null, [Sys(6005, -2)]).Lifecycle);   // a 6005 BEFORE the stop is the previous boot, not the next one
    }

    [Fact]
    public void Stop_without_shutdown_and_without_profile_is_NotAssessed_Medium()
    {
        var a = LogClearAssessment.AssessServiceStop(Stop(), null, []);
        Assert.Equal(LogClearLifecycle.NotAssessed, a.Lifecycle);
        Assert.Equal(Severity.Medium, a.Severity);
        Assert.Contains("nu există profil de procedură", a.Reason);
    }

    [Fact]
    public void Stop_without_shutdown_with_profile_is_Unexpected_High()
    {
        var a = LogClearAssessment.AssessServiceStop(Stop(), Policy(), []);
        Assert.Equal(LogClearLifecycle.Unexpected, a.Lifecycle);
        Assert.Equal(Severity.High, a.Severity);
    }

    [Fact]
    public void Stop_by_an_approved_account_inside_a_window_is_Routine_even_without_a_shutdown()
    {
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.AssessServiceStop(Stop("alice", "CORP"), Policy(), []).Lifecycle);
        Assert.Equal(LogClearLifecycle.Unexpected, LogClearAssessment.AssessServiceStop(Stop("mallory", "CORP"), Policy(), []).Lifecycle);
        Assert.Equal(LogClearLifecycle.Unexpected, LogClearAssessment.AssessServiceStop(new ServiceStopEvent(T0.AddHours(3), "alice", "CORP"), Policy(), []).Lifecycle);
    }

    // ---- 4719 ----

    [Theory]
    [InlineData("SYSTEM", "", "NT AUTHORITY")]
    [InlineData("WS-01$", "", "CORP")]
    [InlineData("anyone", "S-1-5-18", "CORP")]
    public void Change_applied_by_group_policy_is_Routine_Info_even_when_it_removes_auditing(string user, string sid, string domain)
    {
        var a = LogClearAssessment.AssessAuditPolicyChange(Change(user, "%%8448, %%8450", sid, domain), null);
        Assert.Equal(LogClearLifecycle.Routine, a.Lifecycle);
        Assert.Equal(Severity.Info, a.Severity);
        Assert.Contains("Group Policy", a.Reason);
    }

    [Fact]
    public void Change_by_an_approved_account_inside_a_window_is_Routine()
    {
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.AssessAuditPolicyChange(Change("alice", "%%8448"), Policy()).Lifecycle);
        Assert.Equal(Severity.Info, LogClearAssessment.AssessAuditPolicyChange(Change("alice", "%%8448"), Policy()).Severity);
    }

    [Theory]
    [InlineData("%%8448")]            // success removed
    [InlineData("%%8450")]            // failure removed
    [InlineData("%%8449, %%8450")]    // success added but failure removed
    public void Non_routine_change_that_removes_success_or_failure_auditing_is_High(string changes)
    {
        Assert.Equal(Severity.High, LogClearAssessment.AssessAuditPolicyChange(Change("bob", changes), null).Severity);
        var withProfile = LogClearAssessment.AssessAuditPolicyChange(Change("bob", changes), Policy());
        Assert.Equal(Severity.High, withProfile.Severity);
        Assert.Equal(LogClearLifecycle.Unexpected, withProfile.Lifecycle);
        Assert.Contains("elimină", withProfile.Reason);
    }

    [Theory]
    [InlineData("%%8449")]            // success added
    [InlineData("%%8451")]            // failure added
    [InlineData("")]                  // no detail
    public void Other_non_routine_change_is_Medium(string changes)
    {
        Assert.Equal(Severity.Medium, LogClearAssessment.AssessAuditPolicyChange(Change("bob", changes), null).Severity);
        Assert.Equal(Severity.Medium, LogClearAssessment.AssessAuditPolicyChange(Change("bob", changes), Policy()).Severity);
        Assert.Equal(LogClearLifecycle.NotAssessed, LogClearAssessment.AssessAuditPolicyChange(Change("bob", changes), null).Lifecycle);
    }

    // ---- LOG-TAMPER uses it ----

    private static TimelineEvent E(string source, string id, int minute, string provider = "x", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.UtcDateTime.AddMinutes(minute), "", "test"), Source = source, EventId = id, Provider = provider, EvidenceId = "EV-1",
        Locator = $"EventRecordID={1000 + minute + 100}", Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void LOG_TAMPER_for_a_service_stop_during_a_restart_is_Info_and_says_why()
    {
        var f = Assert.Single(Correlation.Run([E("EventLog:Security", "1100", 0), E("EventLog:System", "1074", 2)]), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("System 1074", f.Description);
    }

    [Fact]
    public void LOG_TAMPER_for_a_lone_service_stop_is_Medium_without_profile_and_High_with_one()
    {
        var events = new[] { E("EventLog:Security", "1100", 0) };
        Assert.Equal(Severity.Medium, Assert.Single(Correlation.Run(events), x => x.RuleId == "LOG-TAMPER").Severity);
        Assert.Equal(Severity.High, Assert.Single(Correlation.Run(events, Policy()), x => x.RuleId == "LOG-TAMPER").Severity);
    }

    [Fact]
    public void LOG_TAMPER_for_a_gpo_applied_audit_change_is_Info_and_for_a_manual_removal_High()
    {
        var gpo = E("EventLog:Security", "4719", 0, "Microsoft-Windows-Security-Auditing", ("SubjectUserName", "WS-01$"), ("SubjectDomainName", "CORP"), ("AuditPolicyChanges", "%%8448"));
        Assert.Equal(Severity.Info, Assert.Single(Correlation.Run([gpo]), x => x.RuleId == "LOG-TAMPER").Severity);
        var manual = E("EventLog:Security", "4719", 0, "Microsoft-Windows-Security-Auditing", ("SubjectUserName", "bob"), ("SubjectDomainName", "CORP"), ("AuditPolicyChanges", "%%8448"));
        var f = Assert.Single(Correlation.Run([manual]), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("bob", f.Description);
    }

    [Fact]
    public void LOG_TAMPER_takes_the_highest_severity_across_clears_stops_and_changes()
    {
        var routineStop = new[] { E("EventLog:Security", "1100", 0), E("EventLog:System", "6006", 1) };
        var clear = E("EventLog:Security", "1102", 120, "Microsoft-Windows-Eventlog", ("SubjectUserName", "mallory"), ("SubjectDomainName", "CORP"));
        var f = Assert.Single(Correlation.Run(routineStop.Append(clear).ToList()), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.Medium, f.Severity);   // the clear is NotAssessed (no profile); the routine stop does not lower it
        var withProfile = Assert.Single(Correlation.Run(routineStop.Append(clear).ToList(), Policy()), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.High, withProfile.Severity);
    }
}
