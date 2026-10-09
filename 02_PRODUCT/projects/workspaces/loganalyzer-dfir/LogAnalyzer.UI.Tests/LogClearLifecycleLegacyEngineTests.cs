using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Core.Services.Network;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Infrastructure;
using LogAnalyzer.Infrastructure.Engines;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>WP1b: a cleared log is not "Critical / intentional" by itself, and the hour of a logon is context, not a penalty.</summary>
    public class LogClearLifecycleLegacyEngineTests
    {
        private static ParsedEvent Ev(int id, string msg = "", int hour = 12) => new()
        {
            EventId = id, MachineName = "SRV-01", Message = msg, TimeCreated = new DateTime(2026, 9, 19, hour, 0, 0, DateTimeKind.Utc), ProviderName = "Microsoft-Windows-Eventlog",
        };

        [Theory]
        [InlineData(1102)]
        [InlineData(104)]
        public void AnalysisEngine_clear_is_Medium_needs_verification_and_does_not_claim_intent(int id)
        {
            var issue = Assert.Single(new AnalysisEngine().AnalyzeEvents([Ev(id)]));
            Assert.Equal("Medium", issue.Severity);
            Assert.Contains("necesită verificare", issue.Explanation);
            Assert.DoesNotContain("intenționat", issue.Title + issue.Explanation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("indicator puternic", issue.Explanation);
        }

        [Theory]
        [InlineData(1102, "")]
        [InlineData(104, "")]
        [InlineData(4688, "Process CommandLine: wevtutil cl Security")]
        public void LiveMonitoring_clear_is_Medium_never_Critical_and_does_not_claim_intent(int id, string msg)
        {
            var alert = new LiveSecurityMonitoringEngine().EvaluateLiveEvent(Ev(id, msg));
            Assert.NotNull(alert);
            Assert.Equal("Medium", alert!.Severity);
            Assert.Equal("T1070.001", alert.MitreTechniqueId);
            Assert.DoesNotContain("intenționat", alert.Title + alert.Explanation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CRITICĂ", alert.Title);
            Assert.Contains("necesită verificare", alert.Explanation);
        }

        [Fact]
        public void Sigma_event_log_cleared_rule_is_Medium()
        {
            var engine = new SigmaRuleEngine();
            var issue = Assert.Single(engine.EvaluateEvents([Ev(1102)]), i => i.MitreTechniqueId == "T1070.001");
            Assert.Equal("Medium", issue.Severity);
            Assert.DoesNotContain("intenționat", issue.Explanation, StringComparison.OrdinalIgnoreCase);
            var rule = engine.Rules.Single(r => r.MitreTechnique == "T1070.001");
            Assert.Equal("Medium", rule.Severity);
            Assert.Contains("level: medium", rule.YamlContent);
        }

        [Fact]
        public void Legacy_engines_use_the_shared_classifier_severity()
        {
            var shared = LogClearAssessment.Assess([new LogClearEvent("Security", DateTimeOffset.UtcNow, "")], null, [])[0].Severity.ToString();
            Assert.Equal(shared, new AnalysisEngine().AnalyzeEvents([Ev(1102)]).Single().Severity);
        }

        [Fact]
        public void ExplainableRisk_off_hours_logons_add_no_points_and_are_shown_as_context()
        {
            var engine = new ExplainableAiRiskEngine();
            var baseline = engine.Evaluate([], 0, 0, 0, 0);
            var night = engine.Evaluate([], 0, 0, 7, 0);
            Assert.Equal(baseline.TotalScore, night.TotalScore);
            var factor = Assert.Single(night.Factors, f => f.Category.Contains("Off-Hours"));
            Assert.Equal(0, factor.WeightPoints);
            Assert.Contains("neevaluat: programul de lucru nu este definit", factor.Description);
            Assert.DoesNotContain(baseline.Factors, f => f.Category.Contains("Off-Hours"));
        }

        [Fact]
        public void ExplainableRisk_with_working_hours_defined_compares_but_still_does_not_penalise()
        {
            var engine = new ExplainableAiRiskEngine();
            var baseline = engine.Evaluate([], 0, 0, 0, 0);
            var night = engine.Evaluate([], 0, 0, 7, 0, new WorkingHours(new TimeOnly(8, 0), new TimeOnly(18, 0)));
            Assert.Equal(baseline.TotalScore, night.TotalScore);
            var factor = Assert.Single(night.Factors, f => f.Category.Contains("Off-Hours"));
            Assert.Equal(0, factor.WeightPoints);
            Assert.Contains("08:00", factor.Description);
            Assert.DoesNotContain("neevaluat", factor.Description);
        }

        private static ParsedEvent Logon(int hour) => new()
        {
            EventId = 4624, MachineName = "WS-01", Message = "TargetUserName: bob", TimeCreated = new DateTime(2026, 9, 19, hour, 30, 0, DateTimeKind.Utc),
        };

        [Fact]
        public void Uba_off_hours_logon_is_Info_context_with_zero_risk()
        {
            var item = Assert.Single(new UserBehaviorAnalyticsEngine().Evaluate([Logon(2), Logon(3)]));
            Assert.Equal("Info", item.Severity);
            Assert.Equal(0.0, item.RiskWeight);
            Assert.Contains("neevaluat: programul de lucru nu este definit", item.Description);
        }

        [Fact]
        public void Uba_with_working_hours_defined_reports_comparison_only()
        {
            var hours = new WorkingHours(new TimeOnly(8, 0), new TimeOnly(18, 0));
            var item = Assert.Single(new UserBehaviorAnalyticsEngine().Evaluate([Logon(2), Logon(12)], hours));
            Assert.Equal("Info", item.Severity);
            Assert.Equal(0.0, item.RiskWeight);
            Assert.Contains("08:00", item.Description);
        }

        [Fact]
        public void Anomaly_engine_night_logon_is_Info_context()
        {
            var issue = Assert.Single(new AnomalyDetectionEngine().DetectAnomalies([Logon(2)]), i => i.Title.Contains("Nocturnă"));
            Assert.Equal("Info", issue.Severity);
            Assert.DoesNotContain("atacator", issue.Explanation);
        }
    }
}
