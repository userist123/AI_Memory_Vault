using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.8: Sigma 2.x correlation rules (event_count, value_count, temporal, temporal_ordered) on base-rule matches.</summary>
public sealed class SigmaCorrelationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_sc_{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static readonly DateTime T0 = new(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);

    private static TimelineEvent E(string eventId, double sec, string user = "bob", string ip = "1.1.1.1", int rec = 0, bool unknownTime = false) => new()
    {
        Time = unknownTime ? Timestamp.Unknown("?") : Timestamp.FromUtc(T0.AddSeconds(sec), "", "t"), Source = "EventLog:Security", EventId = eventId,
        EvidenceId = "EV-000001", Locator = $"EventRecordID={(rec != 0 ? rec : (int)(sec * 10) + int.Parse(eventId))}", Summary = "x",
        Fields = { ["TargetUserName"] = user, ["IpAddress"] = ip },
    };

    private const string Base = """
        title: logon failed
        id: 11111111-0000-0000-0000-000000000001
        name: logon_failed
        logsource: { product: windows, service: security }
        detection:
          sel: { EventID: 4625 }
          condition: sel
        level: medium
        ---
        title: logon ok
        id: 11111111-0000-0000-0000-000000000002
        name: logon_ok
        logsource: { product: windows, service: security }
        detection:
          sel: { EventID: 4624 }
          condition: sel
        level: medium
        ---
        title: user created
        name: user_created
        logsource: { product: windows, service: security }
        detection:
          sel: { EventID: 4720 }
          condition: sel
        level: medium

        """;

    private static string Corr(string body, string title = "c1") => $"---\ntitle: {title}\nid: corr-{title}\nlevel: high\ncorrelation:\n{body}";

    private DetectionEngine Load(params string[] files)
    {
        var r = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(r, "sigma"));
        for (int i = 0; i < files.Length; i++) File.WriteAllText(Path.Combine(r, "sigma", $"f{i}.yml"), files[i]);
        return DetectionEngine.Load(r);
    }

    private static List<DetectionResult> Corrs(DetectionEngine e, params TimelineEvent[] events) =>
        e.Run(events, [], _ => []).Where(r => r.Kind == DetectionKind.Correlation).ToList();

    private const string Count3 = "  type: event_count\n  rules: [logon_failed]\n  group-by: [TargetUserName]\n  timespan: 60s\n  condition: { gte: 3 }\n";

    [Fact]
    public void Event_count_fires_when_enough_events_in_window_and_not_otherwise()
    {
        var e = Load(Base + Corr(Count3));
        Assert.Empty(e.LoadErrors);
        var hit = Assert.Single(Corrs(e, E("4625", 0), E("4625", 10), E("4625", 20)));
        Assert.Equal("EV-000001", hit.EvidenceId);
        Assert.Contains("EventRecordID=", hit.Match);
        Assert.Contains("3 evenimente în fereastră", hit.Match);
        Assert.Empty(Corrs(e, E("4625", 0), E("4625", 10), E("4625", 200)));
    }

    [Fact]
    public void Group_by_separates_users_case_insensitively()
    {
        var e = Load(Base + Corr(Count3));
        var res = Corrs(e, E("4625", 0, "Bob"), E("4625", 5, "alice"), E("4625", 10, "BOB"), E("4625", 15, "alice"), E("4625", 20, "bob"));
        Assert.Contains("TargetUserName=bob", Assert.Single(res).Match);   // alice has only 2
    }

    [Fact]
    public void Window_edge_is_inside_and_a_millisecond_past_is_outside()
    {
        var e = Load(Base + Corr(Count3));
        Assert.Single(Corrs(e, E("4625", 0), E("4625", 30), E("4625", 60)));
        Assert.Empty(Corrs(e, E("4625", 0), E("4625", 30), E("4625", 60.001)));
    }

    [Fact]
    public void Overlapping_windows_merge_into_one_result_with_all_supporting_events()
    {
        var e = Load(Base + Corr(Count3));
        var one = Assert.Single(Corrs(e, E("4625", 0), E("4625", 10), E("4625", 20), E("4625", 30), E("4625", 40)));
        Assert.Contains("5 evenimente:", one.Match);
    }

    [Fact]
    public void Value_count_counts_distinct_values_positive_and_negative()
    {
        var body = "  type: value_count\n  rules: [logon_failed]\n  group-by: [TargetUserName]\n  timespan: 5m\n  condition: { gte: 3, field: IpAddress }\n";
        var e = Load(Base + Corr(body));
        Assert.Empty(e.LoadErrors);
        var hit = Assert.Single(Corrs(e, E("4625", 0, ip: "1.1.1.1"), E("4625", 1, ip: "2.2.2.2"), E("4625", 2, ip: "3.3.3.3"), E("4625", 3, ip: "3.3.3.3")));
        Assert.Contains("3 valori distincte de IpAddress", hit.Match);
        Assert.Empty(Corrs(e, E("4625", 0, ip: "1.1.1.1"), E("4625", 1, ip: "1.1.1.1"), E("4625", 2, ip: "2.2.2.2"), E("4625", 3, ip: "2.2.2.2")));
    }

    private const string Temporal = "  type: temporal\n  rules: [logon_failed, user_created]\n  group-by: [TargetUserName]\n  timespan: 10m\n";

    [Fact]
    public void Temporal_needs_all_rules_in_any_order_per_group()
    {
        var e = Load(Base + Corr(Temporal));
        Assert.Single(Corrs(e, E("4720", 0), E("4625", 60)));
        Assert.Empty(Corrs(e, E("4720", 0), E("4625", 60, "alice")));          // different groups
        Assert.Empty(Corrs(e, E("4720", 0), E("4625", 700)));                  // outside the window
    }

    [Fact]
    public void Temporal_ordered_requires_the_listed_order()
    {
        var e = Load(Base + Corr(Temporal.Replace("temporal\n", "temporal_ordered\n")));
        Assert.Empty(e.LoadErrors);
        Assert.Single(Corrs(e, E("4625", 0), E("4720", 60)));
        Assert.Empty(Corrs(e, E("4720", 0), E("4625", 60)));                   // order violated
        Assert.Empty(Corrs(e, E("4625", 0, rec: 1), E("4720", 0, rec: 2)));    // equal times carry no order
    }

    [Fact]
    public void Base_rules_in_another_file_are_found_and_base_rules_still_report()
    {
        var e = Load(Base, Corr(Count3).Substring(4));
        Assert.Empty(e.LoadErrors);
        var all = e.Run([E("4625", 0), E("4625", 1), E("4625", 2)], [], _ => []);
        Assert.Equal(3, all.Count(r => r.Kind == DetectionKind.Sigma));
        Assert.Single(all, r => r.Kind == DetectionKind.Correlation);
        Assert.Contains(e.Rules, r => r.Kind == DetectionKind.Correlation);
    }

    [Theory]
    [InlineData("  type: event_count\n  rules: [nope]\n  timespan: 1m\n  condition: { gte: 2 }\n", "nu există")]
    [InlineData("  type: event_count\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { lt: 2 }\n", "condition.lt")]
    [InlineData("  type: event_count\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { lte: 2 }\n", "condition.lte")]
    [InlineData("  type: event_count\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { eq: 0 }\n", "eq 0")]
    [InlineData("  type: event_count\n  rules: [logon_failed]\n  condition: { gte: 2 }\n", "timespan")]
    [InlineData("  type: bogus\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { gte: 2 }\n", "bogus")]
    [InlineData("  type: event_count\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { gte: 2 }\n  aliases: {}\n", "aliases")]
    [InlineData("  type: event_count\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { between: 2 }\n", "between")]
    [InlineData("  type: value_count\n  rules: [logon_failed]\n  timespan: 1m\n  condition: { gte: 2 }\n", "condition.field")]
    [InlineData("  type: temporal\n  rules: [logon_failed]\n  timespan: 1m\n", "două reguli")]
    public void Unsupported_correlation_is_a_load_error_never_silent(string body, string expected)
    {
        var e = Load(Base + Corr(body));
        var gap = Assert.Single(e.LoadErrors);
        Assert.Equal(EvidenceStatus.Failed, gap.Status);
        Assert.Contains(expected, gap.Reason);
        Assert.DoesNotContain(e.Rules, r => r.Kind == DetectionKind.Correlation);
    }

    [Fact]
    public void Chaining_a_correlation_is_refused()
    {
        var e = Load(Base + Corr(Count3, "first") + Corr("  type: event_count\n  rules: [corr-first]\n  timespan: 1m\n  condition: { gte: 1 }\n", "second"));
        Assert.Contains("înlănțuirea", Assert.Single(e.LoadErrors).Reason);
        Assert.Single(e.Rules, r => r.Kind == DetectionKind.Correlation);
    }

    [Fact]
    public void Events_with_unknown_time_are_excluded_with_a_gap()
    {
        var e = Load(Base + Corr(Count3));
        Assert.Empty(Corrs(e, E("4625", 0), E("4625", 10), E("4625", 20, unknownTime: true)));
        Assert.Contains(e.LoadErrors, g => g.Reason.Contains("ora lor este necunoscută"));
    }

    [Fact]
    public void Same_input_twice_gives_same_output()
    {
        var events = new[] { E("4625", 0), E("4625", 10, "alice"), E("4625", 20), E("4625", 25, "alice"), E("4625", 30), E("4625", 35, "alice") };
        var a = Corrs(Load(Base + Corr(Count3)), events).Select(r => r.Match).ToList();
        var b = Corrs(Load(Base + Corr(Count3)), events.Reverse().ToArray()).Select(r => r.Match).ToList();
        Assert.Equal(a, b);
        Assert.Equal(2, a.Count);
    }

    [Fact]
    public void Refs_are_capped_and_flagged()
    {
        var e = Load(Base + Corr("  type: event_count\n  rules: [logon_failed]\n  timespan: 1h\n  condition: { gte: 2 }\n"));
        var res = Assert.Single(Corrs(e, Enumerable.Range(0, 300).Select(i => E("4625", i)).ToArray()));
        Assert.Contains("300 evenimente:", res.Match);
        Assert.Contains("listă tăiată la 200", res.Match);
    }

    [Fact]
    public void RuleLab_accepts_a_correlation_with_its_base_rules_and_returns_its_matches()
    {
        var run = RuleLab.Test(RuleLabKind.Sigma, "brute", Base + Corr(Count3), [E("4625", 0), E("4625", 1), E("4625", 2)], [], _ => []);
        Assert.True(run.Valid, run.Summary);
        Assert.Contains(run.Matches, m => m.RuleId.StartsWith("SIGMA-CORR:"));
        Assert.Equal(3, run.Matches.Count(m => m.RuleId.StartsWith("SIGMA:")));
        var bad = RuleLab.Test(RuleLabKind.Sigma, "bad", Base + Corr(Count3.Replace("gte", "lt")), [], [], _ => []);
        Assert.False(bad.Valid);
        Assert.Contains("condition.lt", bad.Summary);
    }
}
