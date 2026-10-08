using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Firewall rule changes from Firewall.evtx, and audit-coverage gaps inferred from what the Security log contains.</summary>
public sealed class CoverageAndFirewallTests
{
    private const string FirewallChannel = "EventLog:Microsoft-Windows-Windows Firewall With Advanced Security/Firewall";
    private static readonly DateTime T0 = new(2026, 9, 19, 15, 0, 0, DateTimeKind.Utc);

    private static TimelineEvent Fw(int id, string app, string action, string direction, int n = 1) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(n), "", "test"), Source = FirewallChannel, EventId = id.ToString(), EvidenceId = "EV-FW",
        Locator = $"EventRecordID={n}", Summary = "fw",
        Fields = { ["RuleName"] = $"rule{n}", ["ApplicationPath"] = app, ["Action"] = action, ["Direction"] = direction, ["ModifyingApplication"] = @"C:\x\setup.exe" },
    };

    [Fact]
    public void Allow_rule_for_a_program_in_a_user_writable_folder_is_a_finding()
    {
        var events = new List<TimelineEvent>
        {
            Fw(2097, @"C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe", "3", "1", 1),          // allow inbound
            Fw(2097, @"C:\ProgramData\agent\a.exe", "3", "2", 2),                                    // allow outbound
            Fw(2097, @"C:\Users\u\AppData\Local\x\blocked.exe", "2", "2", 3),                         // block: not a finding
            Fw(2097, @"%ProgramData%\Microsoft\Windows Defender\Platform\4.18\MsMpEng.exe", "3", "2", 4), // Microsoft-owned
            Fw(2097, @"C:\Program Files\Google\Chrome\Application\chrome.exe", "3", "1", 5),
        };
        var f = Correlation.Run(events).Where(x => x.RuleId == "FIREWALL-RULE-USERPATH").ToList();

        Assert.Equal(2, f.Count);
        var inbound = Assert.Single(f, x => x.File.EndsWith("pro_cmd_core.exe"));
        Assert.Equal(Severity.High, inbound.Severity);
        Assert.Equal("T1562.004", inbound.MitreTechniqueId);
        Assert.Contains("Inbound", inbound.Description);
        Assert.Equal(Severity.Medium, Assert.Single(f, x => x.File.EndsWith("a.exe")).Severity);
    }

    private static TimelineEvent Sec(int id, int n) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(n), "", "test"), Source = "EventLog:Security", EventId = id.ToString(), EvidenceId = "EV-SEC",
        Locator = $"EventRecordID={n}", Summary = "s",
    };

    [Fact]
    public void Missing_process_creation_and_wfp_events_are_reported_as_gaps_only_when_the_security_log_was_read()
    {
        var gaps = AuditCoverage.Gaps([Sec(4624, 1), Sec(4672, 2)]);
        Assert.Contains(gaps, g => g.Artifact.Contains("4688") && g.Status == EvidenceStatus.NotAvailable);
        Assert.Contains(gaps, g => g.Artifact.Contains("5156"));

        var withAudit = AuditCoverage.Gaps([Sec(4624, 1), Sec(4688, 2), Sec(5156, 3)]);
        Assert.DoesNotContain(withAudit, g => g.Artifact.Contains("4688") || g.Artifact.Contains("5156"));

        // 4688 present in 1 of 20 active hours: audited intermittently (PARTIAL), with the numbers in the reason.
        var hours = Enumerable.Range(0, 20).Select(h => Sec(4624, h * 60)).Append(Sec(4688, 0)).ToList();
        var partial = Assert.Single(AuditCoverage.Gaps(hours), g => g.Artifact.Contains("4688"));
        Assert.Equal(EvidenceStatus.Partial, partial.Status);
        Assert.Contains("1 din 20 ore", partial.Reason);

        Assert.Empty(AuditCoverage.Gaps([Fw(2097, "x", "3", "1")]));   // no Security log: nothing can be said about its audit
    }

    /// <summary>
    /// The station's Security log, parsed in full, against auditpol.txt captured on the same station (process creation and
    /// WFP connection: "No Auditing"). The log has no 5156/5157 at all (NOT_AVAILABLE) and 4688 only around boots — the
    /// boot processes smss, wininit, lsass… — so process creation is reported as intermittent (PARTIAL), not as absent.
    /// Firewall.evtx: the new rule must not fire on the station's legitimate rules (Defender, Chrome, Store apps).
    /// </summary>
    [CorpusFact("securityEvtx")]
    public void Real_security_log_coverage_matches_auditpol_and_firewall_rules_are_not_flagged()
    {
        var auditpol = File.ReadAllLines(Path.Combine(Corpus.Root, Corpus.S("securityEvtx", "auditpol")));
        Assert.Contains(auditpol, l => l.Trim().StartsWith("Process Creation", StringComparison.Ordinal) && l.Contains("No Auditing"));
        Assert.Contains(auditpol, l => l.Trim().StartsWith("Filtering Platform Connection", StringComparison.Ordinal) && l.Contains("No Auditing"));

        var sec = Corpus.File("securityEvtx");
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "EV-S", CaseId = "C", Source = "Security", SourceType = "EventLog:Security", StoredPath = sec };
        var r = new EvtxParser().Parse(item, sec, sink, default);
        Assert.True(r.Records > 10_000, $"{r.Records} evenimente Security");
        Assert.DoesNotContain(sink.Events, e => e.EventId is "5156" or "5157");
        var procs = sink.Events.Where(e => e.EventId == "4688").ToList();
        Assert.True(procs.Count > 500, $"{procs.Count} evenimente 4688");
        Assert.Contains(procs, e => e.Fields.GetValueOrDefault("NewProcessName", "").EndsWith(@"\wininit.exe", StringComparison.OrdinalIgnoreCase));
        var gaps = AuditCoverage.Gaps(sink.Events);
        Assert.Equal(EvidenceStatus.Partial, Assert.Single(gaps, g => g.Artifact.Contains("4688")).Status);
        Assert.Equal(EvidenceStatus.NotAvailable, Assert.Single(gaps, g => g.Artifact.Contains("5156")).Status);

        var fw = Corpus.S("securityEvtx", "firewall");
        var fwSink = new ListSink();
        var fwItem = new EvidenceItem { EvidenceId = "EV-F", CaseId = "C", Source = "Firewall", SourceType = "evtx", StoredPath = fw };
        Assert.True(new EvtxParser().Parse(fwItem, Path.Combine(Corpus.Root, fw), fwSink, default).Records > 500);
        Assert.Contains(fwSink.Events, e => e.EventId == "2097" && e.Fields.ContainsKey("ApplicationPath"));
        Assert.DoesNotContain(Correlation.Run(fwSink.Events), x => x.RuleId == "FIREWALL-RULE-USERPATH");
    }
}
