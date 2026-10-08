using System.Diagnostics;
using System.Text;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Sigma and YARA patterns run over untrusted evidence. A pattern with catastrophic backtracking must end in a reported
/// "rule evaluation timed out" (the rule is neither matched nor silently cleared), never in a stall or a crash.
/// </summary>
public sealed class RuleRegexTimeoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_rxto_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);          // wall-clock bound for the tests: orders of magnitude below what the pattern would take unbounded

    /// <summary>"aaaa…a!" defeats (a+)+ : the engine tries about 2^n splits before it can fail.</summary>
    private static readonly string Hostile = new string('a', 48) + "!";
    private const string Catastrophic = "^(a+)+$";

    private static TimelineEvent E(string id, string value) => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 9, 19, 15, 0, 0, DateTimeKind.Utc), "", "t"), Source = "EventLog:System", EventId = id,
        EvidenceId = "EV-" + id, Locator = "EventRecordID=" + id, Summary = "s", Fields = { ["Image"] = value },
    };

    private static string SigmaRule(string condition) => $$"""
        title: catastrophic
        id: rx-1
        logsource: { product: windows, service: system }
        detection:
          sel:
            Image|re: '{{Catastrophic}}'
          benign:
            Image|contains: 'ok'
          condition: {{condition}}
        level: high
        """;

    private static T Within<T>(Func<T> run)
    {
        var sw = Stopwatch.StartNew();
        var task = Task.Run(run);
        try { Assert.True(task.Wait(Generous), "evaluarea nu s-a terminat (fără timeout efectiv)"); }
        catch (AggregateException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); }
        Assert.True(sw.Elapsed < Generous);
        return task.Result;
    }

    // ------------------------------------------------------------------ Sigma

    [Fact]
    public void Sigma_regex_timeout_is_reported_and_is_not_a_match()
    {
        var s = SigmaLite.Load(Short, ("rx.yml", SigmaRule("sel")));
        var timeouts = new List<RuleTimeout>();
        var hits = Within(() => s.Match([E("1", Hostile)], timeouts).ToList());
        Assert.Empty(hits);
        var t = Assert.Single(timeouts);
        Assert.Equal("SIGMA:rx-1", t.RuleId);
        Assert.Equal("EV-1", t.EvidenceId);
        Assert.Equal("EventRecordID=1", t.Locator);
        Assert.Contains("depășit", t.Detail);
    }

    [Fact]
    public void Sigma_negated_condition_does_not_turn_a_timeout_into_a_match()
    {
        // If a timeout counted as "pattern did not match", "not sel" would fire on exactly the input that defeated the rule.
        var s = SigmaLite.Load(Short, ("rx.yml", SigmaRule("not sel")));
        var timeouts = new List<RuleTimeout>();
        var hits = Within(() => s.Match([E("1", Hostile)], timeouts).ToList());
        Assert.Empty(hits);
        Assert.Single(timeouts);
        // The same rule still evaluates ordinary input normally.
        Assert.Single(SigmaLite.Load(Short, ("rx.yml", SigmaRule("not sel"))).Match([E("2", "bbb")]));
    }

    [Fact]
    public void Sigma_without_a_list_to_record_into_throws_instead_of_dropping_the_timeout()
    {
        var s = SigmaLite.Load(Short, ("rx.yml", SigmaRule("sel")));
        var ex = Assert.Throws<RuleEvaluationTimeoutException>(() => Within(() => s.Match([E("1", Hostile)]).ToList()));
        Assert.Equal("EV-1", ex.Timeout.EvidenceId);
        Assert.IsType<System.Text.RegularExpressions.RegexMatchTimeoutException>(ex.InnerException);
    }

    [Fact]
    public void Sigma_stops_a_timed_out_rule_for_the_run_and_says_how_many_events_it_skipped()
    {
        var s = SigmaLite.Load(Short, ("rx.yml", SigmaRule("sel or benign")));
        var timeouts = new List<RuleTimeout>();
        var events = new[] { E("1", Hostile), E("2", Hostile), E("3", "ok") };
        var hits = Within(() => s.Match(events, timeouts).ToList());
        Assert.Empty(hits);                                                          // the rule is not evaluated after its timeout, and that is reported:
        Assert.Equal(2, timeouts.Count);
        Assert.Contains("2 evenimente ulterioare", timeouts[1].Detail);
    }

    [Fact]
    public void Sigma_ordinary_regex_still_matches_and_an_invalid_one_fails_to_load()
    {
        var ok = SigmaLite.Load(("rx.yml", SigmaRule("sel").Replace(Catastrophic, "^evil[0-9]+$")));
        Assert.Single(ok.Match([E("1", "evil42"), E("2", "evilx")]));
        var bad = Assert.Throws<FormatException>(() => SigmaLite.Load(("bad.yml", SigmaRule("sel").Replace(Catastrophic, "(unclosed"))));
        Assert.Contains("expresie regulată invalidă", bad.Message);
    }

    // ------------------------------------------------------------------ YARA

    private static string YaraRule(string condition) => $"rule hostile {{ strings: $r = /{Catastrophic}/ $ok = \"ok\" condition: {condition} }}";

    [Fact]
    public void Yara_regex_timeout_is_reported_and_is_not_a_match()
    {
        var y = YaraLite.Parse(YaraRule("$r"), "t.yar", Short);
        var timeouts = new List<RuleTimeout>();
        var hits = Within(() => y.Scan(Encoding.ASCII.GetBytes(Hostile), timeouts, "EV-9"));
        Assert.Empty(hits);
        var t = Assert.Single(timeouts);
        Assert.Equal("YARA:hostile", t.RuleId);
        Assert.Equal("EV-9", t.EvidenceId);
        Assert.Contains("depășit", t.Detail);
    }

    [Fact]
    public void Yara_negated_condition_does_not_turn_a_timeout_into_a_match_and_other_rules_still_run()
    {
        var text = YaraRule("not $r") + "\nrule fine { strings: $a = \"aaa\" condition: $a }";
        var y = YaraLite.Parse(text, "t.yar", Short);
        var timeouts = new List<RuleTimeout>();
        var hits = Within(() => y.Scan(Encoding.ASCII.GetBytes(Hostile), timeouts));
        Assert.Equal("fine", Assert.Single(hits).Rule.Name);                        // "not $r" did not fire on the input that defeated it
        Assert.Single(timeouts);
    }

    [Fact]
    public void Yara_without_a_list_to_record_into_throws_instead_of_dropping_the_timeout()
    {
        var y = YaraLite.Parse(YaraRule("$r"), "t.yar", Short);
        Assert.Throws<RuleEvaluationTimeoutException>(() => Within(() => y.Scan(Encoding.ASCII.GetBytes(Hostile))));
    }

    [Fact]
    public void Yara_invalid_regex_is_a_load_error_not_an_unhandled_exception()
    {
        var ex = Assert.Throws<FormatException>(() => YaraLite.Parse("rule a { strings: $r = /(/ condition: $r }"));
        Assert.Contains("expresie regulată invalidă", ex.Message);
    }

    [Fact]
    public void Yara_ordinary_patterns_are_unaffected_by_the_timeout()
    {
        var y = YaraLite.Parse("rule a { strings: $r = /ab[0-9]{2}cd/ condition: $r }", "t.yar", Short);
        var timeouts = new List<RuleTimeout>();
        Assert.Single(y.Scan(Encoding.ASCII.GetBytes("xxab42cdxx"), timeouts));
        Assert.Empty(timeouts);
    }

    // ------------------------------------------------------------------ engine

    [Fact]
    public void Engine_turns_timeouts_into_failed_gaps_and_keeps_running_the_other_rules()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "yara"));
        Directory.CreateDirectory(Path.Combine(_dir, "sigma"));
        File.WriteAllText(Path.Combine(_dir, "yara", "hostile.yar"), YaraRule("$r"));
        File.WriteAllText(Path.Combine(_dir, "yara", "good.yar"), "rule mz { strings: $m = \"aaaa\" condition: $m }");
        File.WriteAllText(Path.Combine(_dir, "sigma", "hostile.yml"), SigmaRule("sel"));

        var engine = DetectionEngine.Load(Short, _dir);
        Assert.Empty(engine.LoadErrors);
        var evidence = new List<EvidenceItem> { new() { EvidenceId = "EV-1", CaseId = "C", Source = "s", SourceType = "file", OriginalName = "a.bin", Sha256 = "AA", Size = Hostile.Length } };
        var results = Within(() => engine.Run([E("1", Hostile)], evidence, _ => Encoding.ASCII.GetBytes(Hostile)));

        Assert.DoesNotContain(results, d => d.RuleId is "YARA:hostile" or "SIGMA:rx-1");     // a timeout is not a detection ...
        Assert.Contains(results, d => d.RuleId == "YARA:mz");                                // ... and does not stop the other rules
        Assert.Equal(2, engine.Timeouts.Count);
        Assert.Equal(2, engine.LoadErrors.Count(g => g.Status == EvidenceStatus.Failed && g.Reason.StartsWith("Timeout la evaluarea regulii")));   // ... and is never silent
        Assert.All(engine.LoadErrors, g => Assert.Contains("nu înseamnă că intrarea e curată", g.Impact));
    }
}
