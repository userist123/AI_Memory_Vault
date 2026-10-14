using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Independent review, finding 1: patterns from rule lists (VSS command patterns, ClickFix patterns) run over evidence text. A timeout must
/// never read as "no match" (padding an input to make it time out would hide it): shipped patterns compile with the linear NonBacktracking
/// engine, and a pattern that can still time out records the input as "neevaluat" (RULE-NOT-EVALUATED), not as clean.
/// </summary>
public sealed class RulePatternTimeoutTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static TimelineEvent Proc4688(double m, string cmd) =>
        Sec(4688, m, ("NewProcessName", @"C:\Windows\System32\wbem\WMIC.exe"), ("CommandLine", cmd), ("SubjectUserName", "u"));

    private static T Within<T>(Func<T> run)
    {
        var task = Task.Run(run);
        Assert.True(task.Wait(Generous), "evaluarea nu s-a terminat");
        return task.Result;
    }

    [Fact]
    public void Shipped_rule_list_patterns_all_compile_with_the_linear_engine()
    {
        var vss = Wp11Data.Default.Lists.Vss.CommandPatterns;
        var click = Wp14Data.Default.Payload.ClickFixPatterns.Where(p => p.Regex.Length > 0).ToList();
        Assert.NotEmpty(vss); Assert.NotEmpty(click);
        foreach (var p in vss.Select(p => p.Regex).Concat(click.Select(p => p.Regex)))
        {
            var rx = RulePatterns.Compile(p);
            Assert.True(RulePatterns.IsLinear(rx), p);
            Assert.True((rx.Options & RegexOptions.NonBacktracking) != 0, p);
        }
    }

    [Fact]
    public void A_pattern_the_linear_engine_cannot_run_falls_back_with_a_timeout()
    {
        var rx = RulePatterns.Compile("^(?=a)(a+)+$");
        Assert.False(RulePatterns.IsLinear(rx));
        Assert.Equal(LogAnalyzer.Dfir.RegexDefaults.RuleMatchTimeout, rx.MatchTimeout);
        Assert.Null(Within(() => RulePatterns.Match(rx, new string('a', 48) + "!")));   // timed out = not evaluated, not false
        Assert.True(RulePatterns.Match(rx, "aaa"));
        Assert.False(RulePatterns.Match(rx, "b"));
    }

    [Fact]
    public void A_matching_command_padded_to_30_KB_still_matches()
    {
        // The real command first, then repeated tokens: the backtracking engine retries every later token before giving up on each one.
        var sb = new StringBuilder("wmic shadowcopy delete");
        while (sb.Length < 30_000) sb.Append(" shadowcopy");
        var sw = Stopwatch.StartNew();
        var found = Within(() => Run([Proc4688(0, sb.ToString())]));
        sw.Stop();
        One(found, "VSS-SNAPSHOT-DELETED");
        Assert.DoesNotContain(found, f => f.RuleId == RulePatterns.NotEvaluatedRuleId);
    }

    [Fact]
    public void A_forced_VSS_pattern_timeout_is_a_not_evaluated_gap_not_a_clean_result()
    {
        var data = Wp11Data.FromJson("""{"agents":[]}""", """{"tools":[]}""",
            """{"vss":{"commandPatterns":[{"kind":"delete","tool":"t","regex":"^(?=a)(a+)+$"}]}}""");
        var ev = Proc4688(0, new string('a', 48) + "!");
        var found = Within(() => Run([ev], data: data));
        Assert.DoesNotContain(found, f => f.RuleId == "VSS-SNAPSHOT-DELETED");
        var gap = One(found, RulePatterns.NotEvaluatedRuleId);
        Assert.Contains("neevaluat", gap.Title + gap.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VSS-SNAPSHOT-DELETED", gap.Description);
        Assert.Contains(gap.SupportingEvidence, r => r.Locator == ev.Locator);
        Assert.NotEmpty(gap.Limitations);
        Assert.NotNull(RuleContracts.For(RulePatterns.NotEvaluatedRuleId));
    }

    private static TimelineEvent RunMru(string command) =>
        Row("RunMRU", 10, "", "", "", ("Command", command), ("MruPosition", "0"), ("Account", "u"), ("ValueName", "a"));

    [Fact]
    public void A_forced_ClickFix_pattern_timeout_is_a_not_evaluated_gap()
    {
        var data = Wp14Data.FromJson("""{"payload":{"clickFixMinWeight":1,"clickFixPatterns":[{"regex":"^(?=a)(a+)+$","weight":1,"label":"x"}]}}""");
        var ev = RunMru(new string('a', 48) + "!");
        var found = Within(() => W14.Go([ev], data: data));
        Assert.DoesNotContain(found, f => f.RuleId == "CLICKFIX-RUNMRU");
        var gap = One(found, RulePatterns.NotEvaluatedRuleId);
        Assert.Contains("CLICKFIX-RUNMRU", gap.Description);
        Assert.Contains(gap.SupportingEvidence, r => r.Locator == ev.Locator);
    }

    [Fact]
    public void A_ClickFix_data_pattern_matches_a_padded_value_with_the_linear_engine()
    {
        var data = Wp14Data.FromJson("""{"payload":{"clickFixMinWeight":1,"clickFixPatterns":[{"regex":"alpha.*omega","weight":1,"label":"x"}]}}""");
        var sb = new StringBuilder("alpha omega");
        while (sb.Length < 30_000) sb.Append(" alpha");
        var found = Within(() => W14.Go([RunMru(sb.ToString())], data: data));
        One(found, "CLICKFIX-RUNMRU");
        Assert.DoesNotContain(found, f => f.RuleId == RulePatterns.NotEvaluatedRuleId);
    }
}
