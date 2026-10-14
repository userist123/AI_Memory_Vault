using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Infrastructure.Engines;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #11: the legacy YARA-like engine takes its regex patterns from its rule list. Each pattern runs with an explicit match timeout;
    /// a rule whose pattern times out is reported as not evaluated (never as a match, never silently clean) and is not run on the remaining events.
    /// An invalid pattern is reported, not thrown.
    /// </summary>
    public class YaraRuleEngineTimeoutTests
    {
        private static ParsedEvent Ev(int id, string message) => new() { EventId = id, Message = message, MachineName = "LAB-PC", TimeCreated = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc) };

        [Fact]
        public void A_catastrophic_rule_pattern_ends_within_the_timeout_and_is_reported_as_not_evaluated()
        {
            var engine = new YaraRuleEngine();
            engine.Rules.Add(new YaraRule { Name = "Hostile_Pattern", Description = "test", RegexPatterns = new List<string> { "^(a+)+$" } });
            var hostile = new string('a', 40) + "!";
            var events = Enumerable.Range(1, 20).Select(i => Ev(i, hostile)).ToList();

            var sw = Stopwatch.StartNew();
            var issues = engine.Evaluate(events);
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
            var notEvaluated = Assert.Single(issues, i => i.Title.Contains("Hostile_Pattern"));
            Assert.Contains("neevaluat", notEvaluated.Title);
            Assert.Equal(0, engine.Rules.Single(r => r.Name == "Hostile_Pattern").MatchCount);
        }

        [Fact]
        public void An_invalid_rule_pattern_is_reported_and_the_other_rules_still_run()
        {
            var engine = new YaraRuleEngine();
            engine.Rules.Add(new YaraRule { Name = "Broken_Pattern", Description = "test", RegexPatterns = new List<string> { "(unclosed" } });
            var issues = engine.Evaluate(new[] { Ev(1, "sekurlsa::logonpasswords") });
            Assert.Contains(issues, i => i.Title.Contains("Mimikatz"));
            Assert.Contains(issues, i => i.Title.Contains("Broken_Pattern") && i.Title.Contains("neevaluat"));
        }

        [Fact]
        public void Normal_regex_rules_still_match()
        {
            var issues = new YaraRuleEngine().Evaluate(new[] { Ev(1, @"pipe opened \\.\pipe\msse-1a2b-server") });
            Assert.Contains(issues, i => i.Title.Contains("CobaltStrike"));
        }
    }
}
