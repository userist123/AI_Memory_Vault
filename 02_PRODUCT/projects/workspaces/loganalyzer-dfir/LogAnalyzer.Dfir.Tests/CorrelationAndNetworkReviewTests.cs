using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression tests for the independent review of the Sigma correlation engine and the network heuristics (2026-11-08).</summary>
public sealed class CorrelationAndNetworkReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_corr_" + Guid.NewGuid().ToString("N"));
    public CorrelationAndNetworkReviewTests() => Directory.CreateDirectory(Path.Combine(_dir, "sigma"));
    public void Dispose() => Directory.Delete(_dir, true);

    private const string Base = """
        title: logon failed
        id: b1
        name: fail
        logsource:
          product: windows
          service: security
        detection:
          sel:
            EventID: 4625
          condition: sel
        level: low
        """;

    private static string Corr(string timespan = "10m", string extra = "", string groupBy = "TargetUserName") => $"""
        title: many failures
        id: c1
        {extra}
        correlation:
          type: event_count
          rules:
            - fail
          group-by:
            - {groupBy}
          timespan: {timespan}
          condition:
            gte: 3
        level: high
        """;

    private static readonly DateTime T0 = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
    private static TimelineEvent Fail(int minute, string user)
    {
        var t = T0.AddMinutes(minute);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "t"), Source = "EventLog:Security", EvidenceId = "EV-1", EventId = "4625", Provider = "Microsoft-Windows-Security-Auditing", Summary = "4625", Locator = $"EventRecordID={minute}{user.Length}" };
        if (user.Length > 0) e.Fields["TargetUserName"] = user;
        return e;
    }

    private DetectionEngine Engine(string yaml)
    {
        File.WriteAllText(Path.Combine(_dir, "sigma", "r.yml"), yaml);
        return DetectionEngine.Load(_dir);
    }

    [Theory]
    [InlineData("999999999h")]
    [InlineData("5000000d")]
    [InlineData("367d")]
    public void A_huge_timespan_is_a_load_error_of_that_rule_and_never_stops_the_analysis(string span)
    {
        var e = Engine(Base + "\n---\n" + Corr(span));
        Assert.Contains(e.LoadErrors, g => g.Status == EvidenceStatus.Failed && g.Reason.Contains("timespan"));
        var r = e.Run([Fail(0, "ana"), Fail(1, "ana"), Fail(2, "ana")], [], _ => []);   // the base rule still runs
        Assert.Equal(3, r.Count(x => x.RuleId.StartsWith("SIGMA:", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_event_dated_near_year_9999_is_excluded_with_a_note_and_never_stops_the_run()
    {
        var e = Engine(Base + "\n...\n" + Corr("2d"));                              // a document end marker also separates
        var late = new TimelineEvent { Time = Timestamp.FromUtc(new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc), "x", "t"), Source = "EventLog:Security", EvidenceId = "EV-9", EventId = "4625", Summary = "4625", Locator = "EventRecordID=9" };
        late.Fields["TargetUserName"] = "ana";
        var r = e.Run([Fail(0, "ana"), Fail(1, "ana"), Fail(2, "ana"), late], [], _ => []);
        Assert.Contains(r, x => x.RuleId.StartsWith("SIGMA-CORR:", StringComparison.Ordinal));
        Assert.Contains(e.LoadErrors, g => g.Reason.Contains("9999"));
        Assert.Empty(Engine(Base + "\n---\n" + Corr()).Run([Fail(0, " "), Fail(1, " "), Fail(2, " ")], [], _ => []).Where(x => x.RuleId.StartsWith("SIGMA-CORR:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Events_without_the_group_by_value_are_not_grouped_together_and_this_is_said()
    {
        var e = Engine(Base + "\n---\n" + Corr());
        var r = e.Run([Fail(0, ""), Fail(1, ""), Fail(2, "")], [], _ => []);
        Assert.DoesNotContain(r, x => x.RuleId.StartsWith("SIGMA-CORR:", StringComparison.Ordinal));
        Assert.Contains(e.LoadErrors, g => g.Status == EvidenceStatus.Partial && g.Reason.Contains("group-by"));
    }

    [Fact]
    public void A_separator_with_a_comment_still_separates_and_an_unknown_top_level_key_is_refused()
    {
        var ok = Engine(Base + "\n--- # corelarea\n" + Corr());
        Assert.Empty(ok.LoadErrors);
        Assert.Contains(ok.Run([Fail(0, "ana"), Fail(1, "ana"), Fail(2, "ana")], [], _ => []), x => x.RuleId.StartsWith("SIGMA-CORR:", StringComparison.Ordinal));
        var bad = Engine(Base + "\n---\n" + Corr(extra: "action: global"));
        Assert.Contains(bad.LoadErrors, g => g.Reason.Contains("action"));
    }

    [Fact]
    public void The_rule_lab_reports_a_partial_correlation_as_a_note_not_a_refusal()
    {
        var tl = new List<TimelineEvent> { Fail(0, "ana"), Fail(1, "ana"), Fail(2, "ana"), Fail(3, "") };
        var run = RuleLab.Test(RuleLabKind.Sigma, "corr.yml", Base + "\n---\n" + Corr(), tl, [], _ => []);
        Assert.True(run.Valid, string.Join("; ", run.Errors));
        Assert.Contains(run.Notes, n => n.Contains("group-by"));
        Assert.Contains(run.Matches, m => m.RuleId.StartsWith("SIGMA-CORR:", StringComparison.Ordinal));
    }

    // ---- network ----

    private static int _n;
    private static TimelineEvent Zeek(double sec, string src, int sport, string dst, int dport)
    {
        var t = T0.AddSeconds(sec);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "t"), Source = "Zeek:conn", EvidenceId = "EV-Z", EventId = "conn", Summary = "c", Locator = $"line={++_n}" };
        e.Fields["OrigH"] = src; e.Fields["OrigP"] = sport.ToString(); e.Fields["RespH"] = dst; e.Fields["RespP"] = dport.ToString();
        return e;
    }
    private static TimelineEvent Sys3(double sec, string src, int sport, string dst, int dport, string image)
    {
        var t = T0.AddSeconds(sec);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "t"), Source = "EventLog:Microsoft-Windows-Sysmon/Operational", EvidenceId = "EV-S", EventId = "3", Summary = "n", Locator = $"EventRecordID={++_n}" };
        e.Fields["SourceIp"] = src; e.Fields["SourcePort"] = sport.ToString(); e.Fields["DestinationIp"] = dst; e.Fields["DestinationPort"] = dport.ToString(); e.Fields["Image"] = image;
        return e;
    }
    private static string NextId() => "F-" + Guid.NewGuid().ToString("N")[..4];

    [Fact]
    public void A_beacon_whose_connections_come_from_two_processes_names_none_of_them()
    {
        var tl = new List<TimelineEvent>();
        for (int i = 0; i < 12; i++)
        {
            tl.Add(Zeek(i * 300, "10.66.0.21", 50000 + i, "203.0.113.9", 8443));
            tl.Add(Sys3(i * 300, "10.66.0.21", 50000 + i, "203.0.113.9", 8443, i == 0 ? @"C:\chrome.exe" : @"C:\Users\Public\evil.exe"));
        }
        var f = Assert.Single(NetworkHeuristics.Run(tl, NextId).Findings, x => x.RuleId == "NET-BEACON");
        Assert.Equal("", f.Process);
        Assert.Contains("mai multe procese", f.Description);
    }

    [Fact]
    public void Mapped_ipv6_forms_link_a_public_dns_beacon_stays_medium_and_a_dot_255_host_is_kept()
    {
        var tl = new List<TimelineEvent>();
        for (int i = 0; i < 10; i++)
        {
            tl.Add(Zeek(i * 300, "10.66.0.21", 50000 + i, "198.51.100.7", 53));
            tl.Add(Sys3(i * 300 + 1, "::ffff:10.66.0.21", 50000 + i, "::FFFF:198.51.100.7", 53, @"C:\x\dnsbeacon.exe"));
        }
        var r = NetworkHeuristics.Run(tl, NextId);
        Assert.Equal(10, r.Links.Count);
        var f = Assert.Single(r.Findings, x => x.RuleId == "NET-BEACON");
        Assert.Equal(Severity.Medium, f.Severity);
        var host255 = Enumerable.Range(0, 10).Select(i => Zeek(i * 300, "10.66.0.21", 51000 + i, "172.217.3.255", 443)).ToList();
        Assert.Contains(NetworkHeuristics.Run(host255, NextId).Findings, x => x.RuleId == "NET-BEACON");
    }

    [Fact]
    public void Hitting_the_finding_cap_is_a_gap_and_a_large_scan_stays_linear()
    {
        var tl = new List<TimelineEvent>();
        for (int d = 0; d < 210; d++)
            for (int i = 0; i < 8; i++) tl.Add(Zeek(i * 60, "10.66.0.21", 20000 + d * 10 + i, $"203.0.{d / 200}.{d % 200 + 1}", 8443));
        var r = NetworkHeuristics.Run(tl, NextId);
        Assert.Equal(NetworkHeuristics.MaxPerRule, r.Findings.Count(x => x.RuleId == "NET-BEACON"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("limita"));

        var scan = Enumerable.Range(0, 150_000).Select(h => Zeek(h * 0.0004, "10.66.0.50", 1024 + h % 60000, $"10.{h / 65536 % 256}.{h / 256 % 256}.{h % 256 + 1}", 445)).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Contains(NetworkHeuristics.Run(scan, NextId).Findings, x => x.RuleId == "NET-SCAN");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"{sw.Elapsed}");
    }
}
