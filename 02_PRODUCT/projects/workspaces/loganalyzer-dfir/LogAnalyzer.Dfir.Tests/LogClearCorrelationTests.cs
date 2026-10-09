using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public class LogClearCorrelationTests
{
    private static readonly DateTime T0 = new(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc);   // 03:00: night time must not matter

    private static TimelineEvent E(string source, string id, int minute, string provider = "Microsoft-Windows-Eventlog", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = source, EventId = id, Provider = provider, EvidenceId = "EV-1",
        Locator = $"EventRecordID={1000 + minute}", Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Clear1102(int minute, string user = "alice") =>
        E("EventLog:Security", "1102", minute, f: [("SubjectUserName", user), ("SubjectDomainName", "CORP")]);

    private static TimelineEvent Defender(int minute) =>
        E("EventLog:Microsoft-Windows-Windows Defender/Operational", "1116", minute, "Microsoft-Windows-Windows Defender",
          ("Threat Name", "Trojan:Win32/X"), ("Path", @"file:_C:\Users\U\Downloads\x.exe"), ("Action Name", "Not Applicable"));

    [Fact]
    public void Lone_clear_without_policy_is_Medium_not_High_and_not_Critical()
    {
        var f = Assert.Single(Correlation.Run([Clear1102(0)]), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("necesită verificare", f.Description);
        Assert.DoesNotContain("intenționat", f.Title + f.Description);
    }

    [Fact]
    public void Clear_next_to_a_Defender_detection_is_High()
    {
        var f = Assert.Single(Correlation.Run([Clear1102(0), Defender(20)]), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("±60 min", f.Description);
    }

    [Fact]
    public void Clear_far_from_other_findings_stays_Medium()
    {
        var f = Assert.Single(Correlation.Run([Clear1102(0), Defender(300)]), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Approved_account_in_window_is_Info_with_policy()
    {
        var policy = new LogMaintenancePolicy([@"CORP\alice"], [new MaintenanceWindow(new DateTimeOffset(T0).AddMinutes(-10), new DateTimeOffset(T0).AddMinutes(10))]);
        var f = Assert.Single(Correlation.Run([Clear1102(0)], policy), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.Info, f.Severity);
        var bad = Assert.Single(Correlation.Run([Clear1102(0, "mallory")], policy), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.High, bad.Severity);
    }

    [Fact]
    public void Audit_policy_change_alone_stays_Medium()
    {
        var f = Assert.Single(Correlation.Run([E("EventLog:Security", "4719", 0, "Microsoft-Windows-Security-Auditing")]), x => x.RuleId == "LOG-TAMPER");
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void AF01_stays_Detected_and_carries_the_lifecycle_text()
    {
        var checks = AntiForensics.Evaluate([Clear1102(0), E("EventLog:System", "104", 1, f: [("Channel", "Application"), ("SubjectUserName", "alice"), ("SubjectDomainName", "CORP")])], []);
        var af = checks.Single(c => c.Id == "AF01");
        Assert.Equal(AntiForensicResult.Detected, af.Result);
        Assert.Contains("Ciclu de viață", af.Reason);
        Assert.Contains("nu există profil de procedură", af.Reason);
        Assert.Contains(" Application de CORP\\alice", af.Reason);
    }
}
