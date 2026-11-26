using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.5: beaconing and scan heuristics, and the process behind a sensor-observed connection (exact tuple and time, never a guess).</summary>
public sealed class NetworkHeuristicsTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
    private static int _n;

    private static TimelineEvent Zeek(double sec, string src, int sport, string dst, int dport)
    {
        var t = T0.AddSeconds(sec);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "test"), Source = "Zeek:conn", EvidenceId = "EV-Z", EventId = "conn", Summary = "conn", Locator = $"line={++_n}", RemoteIp = dst, RemotePort = dport };
        e.Fields["OrigH"] = src; e.Fields["OrigP"] = sport.ToString(); e.Fields["RespH"] = dst; e.Fields["RespP"] = dport.ToString();
        return e;
    }

    private static TimelineEvent Sysmon3(double sec, string src, int sport, string dst, int dport, string image, string host = "WS01")
    {
        var t = T0.AddSeconds(sec);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "test"), Source = "EventLog:Microsoft-Windows-Sysmon/Operational", EvidenceId = "EV-S", EventId = "3", Host = host, Summary = "net", Locator = $"EventRecordID={++_n}" };
        e.Fields["SourceIp"] = src; e.Fields["SourcePort"] = sport.ToString(); e.Fields["DestinationIp"] = dst; e.Fields["DestinationPort"] = dport.ToString();
        e.Fields["Image"] = image; e.Fields["ProcessGuid"] = "{abc}"; e.Fields["ProcessId"] = "4321"; e.Fields["Initiated"] = "true";
        return e;
    }

    private static int _id;
    private static string Next() => $"F-{++_id:D4}";

    [Fact]
    public void Regular_connections_to_one_destination_are_a_beacon_candidate_with_their_numbers_and_rows()
    {
        var tl = Enumerable.Range(0, 12).Select(i => Zeek(i * 300 + (i % 3) * 7, "10.66.0.21", 50000 + i, "203.0.113.9", 8443)).ToList();
        var r = NetworkHeuristics.Run(tl, Next);
        var f = Assert.Single(r.Findings, x => x.RuleId == "NET-BEACON");
        Assert.Equal((Severity.Medium, Classification.Candidate), (f.Severity, f.Classification));
        Assert.Contains("12 conexiuni", f.Description);
        Assert.Matches(@"~5(\.\d)? min", f.Title);
        Assert.Equal(12, f.SupportingEvidence.Count);
        Assert.Contains(r.Gaps, g => g.Artifact.Contains("proces"));          // no Sysmon: the process cannot be named
    }

    [Fact]
    public void Irregular_or_few_connections_are_not_a_beacon_and_infrastructure_ports_are_low()
    {
        var rnd = new Random(7);
        var irregular = Enumerable.Range(0, 15).Select(i => Zeek(i * 300 + rnd.Next(0, 250), "10.66.0.21", 50000 + i, "203.0.113.9", 8443)).ToList();
        Assert.DoesNotContain(NetworkHeuristics.Run(irregular, Next).Findings, f => f.RuleId == "NET-BEACON");
        var few = Enumerable.Range(0, 5).Select(i => Zeek(i * 300, "10.66.0.21", 50000 + i, "203.0.113.9", 8443)).ToList();
        Assert.Empty(NetworkHeuristics.Run(few, Next).Findings);
        var ntp = Enumerable.Range(0, 10).Select(i => Zeek(i * 600, "10.66.0.21", 50000 + i, "10.66.0.10", 123)).ToList();
        Assert.Equal(Severity.Low, Assert.Single(NetworkHeuristics.Run(ntp, Next).Findings).Severity);
    }

    [Fact]
    public void A_sensor_connection_is_linked_to_its_process_only_by_the_exact_tuple_within_the_window()
    {
        var tl = new List<TimelineEvent>();
        for (int i = 0; i < 10; i++)
        {
            tl.Add(Zeek(i * 60, "10.66.0.21", 51000 + i, "203.0.113.9", 443));
            tl.Add(Sysmon3(i * 60 + 1.5, "10.66.0.21", 51000 + i, "203.0.113.9", 443, @"C:\Users\Public\updsvc.exe"));
        }
        tl.Add(Zeek(5000, "10.66.0.21", 52000, "198.51.100.1", 443));
        tl.Add(Sysmon3(5000, "10.66.0.21", 52001, "198.51.100.1", 443, @"C:\x\other.exe"));   // different source port: no link
        tl.Add(Zeek(6000, "10.66.0.21", 53000, "198.51.100.2", 443));
        tl.Add(Sysmon3(6000 + 600, "10.66.0.21", 53000, "198.51.100.2", 443, @"C:\x\late.exe")); // same tuple, 10 minutes later: no link
        var r = NetworkHeuristics.Run(tl, Next);
        Assert.Equal(10, r.Links.Count);
        Assert.All(r.Links, l => Assert.Equal(@"C:\Users\Public\updsvc.exe", l.Image));
        Assert.All(r.Links, l => Assert.Contains(l.Reasons, x => x.Contains("aceeași pereche")));
        var beacon = Assert.Single(r.Findings, f => f.RuleId == "NET-BEACON");   // Zeek and Sysmon saw the same connections: one finding
        Assert.Equal(@"C:\Users\Public\updsvc.exe", beacon.Process);
        Assert.Contains("Sysmon", beacon.Description);
        Assert.DoesNotContain(r.Gaps, g => g.Artifact.Contains("proces"));
    }

    [Fact]
    public void Many_ports_or_many_hosts_in_a_minute_are_a_scan_candidate_and_a_slow_spread_is_not()
    {
        var vertical = Enumerable.Range(1, 30).Select(p => Zeek(p, "10.66.0.50", 40000 + p, "10.66.0.10", p * 10)).ToList();
        var f = Assert.Single(NetworkHeuristics.Run(vertical, Next).Findings, x => x.RuleId == "NET-SCAN");
        Assert.Contains("30 porturi", f.Title);
        var horizontal = Enumerable.Range(1, 25).Select(h => Zeek(h, "10.66.0.50", 40000 + h, $"10.66.1.{h}", 445)).ToList();
        Assert.Contains(NetworkHeuristics.Run(horizontal, Next).Findings, x => x.RuleId == "NET-SCAN" && x.Title.Contains("25 stații"));
        var slow = Enumerable.Range(1, 30).Select(p => Zeek(p * 120, "10.66.0.50", 40000 + p, "10.66.0.10", p * 10)).ToList();
        Assert.DoesNotContain(NetworkHeuristics.Run(slow, Next).Findings, x => x.RuleId == "NET-SCAN");
    }

    [Fact]
    public void The_result_is_deterministic_and_broadcast_or_unknown_time_rows_are_ignored()
    {
        var tl = Enumerable.Range(0, 12).Select(i => Zeek(i * 120, "10.66.0.21", 50000 + i, "203.0.113.9", 8443)).ToList();
        tl.AddRange(Enumerable.Range(0, 12).Select(i => Zeek(i * 120, "10.66.0.21", 50100 + i, "255.255.255.255", 137)));
        tl.Add(new TimelineEvent { Time = Timestamp.Unknown("x"), Source = "Zeek:conn", EvidenceId = "EV-Z", Summary = "c", Locator = "line=x" });
        string Run() { int n = 0; return System.Text.Json.JsonSerializer.Serialize(NetworkHeuristics.Run(tl, () => $"F-{++n:D4}").Findings.Select(f => (f.RuleId, f.Title, f.Description))); }
        Assert.Equal(Run(), Run());
        Assert.DoesNotContain("255.255.255.255", Run());
    }
}
