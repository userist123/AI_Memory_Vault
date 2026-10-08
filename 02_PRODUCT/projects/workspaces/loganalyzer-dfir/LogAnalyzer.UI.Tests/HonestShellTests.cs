using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Core.Services.Network;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Stage 2 WP1 (honest shell): nothing in the shell or the legacy compliance matrix may state safety or conformity
    /// that no evidence supports (UX contract section 3, owner decision 3).
    /// </summary>
    public class HonestShellTests
    {
        // ---- ComplianceAuditEngine: no CONFORM without evidence ----

        [Fact]
        public void ComplianceCheckResult_defaults_to_not_assessed_not_conform()
        {
            Assert.Equal(ComplianceStatus.NotAssessed, new ComplianceCheckResult().Status);
            Assert.Equal("NEEVALUAT", ComplianceStatus.NotAssessed);
        }

        [Fact]
        public void No_events_and_no_summaries_yield_only_not_assessed()
        {
            var results = new ComplianceAuditEngine().Evaluate(new List<ParsedEvent>(), new AdAuditSummary(), new StandaloneSamSummary(), 0, 0);

            Assert.Equal(5, results.Count);
            Assert.All(results, r => Assert.Equal(ComplianceStatus.NotAssessed, r.Status));
            Assert.DoesNotContain(results, r => r.RequiredAction.Contains("Conformitate validată") || r.RequiredAction.Contains("asigurat criptografic"));
        }

        [Fact]
        public void Null_summaries_do_not_become_conform()
        {
            var results = new ComplianceAuditEngine().Evaluate(null!, null!, null!, 0, 0);

            Assert.DoesNotContain(results, r => r.Status == ComplianceStatus.Conform);
        }

        [Fact]
        public void EvaluateCompliance_overload_never_returns_conform()
        {
            var results = new ComplianceAuditEngine().EvaluateCompliance(
                new AdAuditSummary(), new StandaloneSamSummary(), new List<KerberosAdFinding>(), new List<StandaloneSamFinding>(), new List<StorageAuditItem>());

            Assert.NotEmpty(results);
            Assert.DoesNotContain(results, r => r.Status == ComplianceStatus.Conform);
        }

        [Fact]
        public void Observed_findings_still_set_a_verdict()
        {
            var ad = new AdAuditSummary { PrivilegedGroupChanges = 2, KerberosAttacksDetected = 2 };
            var sam = new StandaloneSamSummary { UsbStorageEventsCount = 1 };

            var results = new ComplianceAuditEngine().Evaluate(new List<ParsedEvent>(), ad, sam, 0, 0);

            Assert.Equal(ComplianceStatus.NonConform, results.Single(r => r.Framework.Contains("585")).Status);
            Assert.Equal(ComplianceStatus.NonConform, results.Single(r => r.Framework.Contains("NIS2")).Status);
            Assert.Equal(ComplianceStatus.Attention, results.Single(r => r.Framework.Contains("GDPR")).Status);
            Assert.DoesNotContain(results, r => r.Status == ComplianceStatus.Conform);
        }

        [Fact]
        public void Absence_of_events_is_said_not_to_prove_conformity()
        {
            var results = new ComplianceAuditEngine().Evaluate(new List<ParsedEvent>(), new AdAuditSummary(), new StandaloneSamSummary(), 0, 0);

            Assert.Contains("nu dovedește conformitatea", results.Single(r => r.Framework.Contains("585")).EvidenceSummary);
        }

        [Fact]
        public void Html_report_shows_not_assessed_with_a_neutral_badge()
        {
            var results = new ComplianceAuditEngine().Evaluate(new List<ParsedEvent>(), new AdAuditSummary(), new StandaloneSamSummary(), 0, 0);

            var html = new AdAuditHtmlReportService().GenerateHtmlReport(
                new AdAuditSummary(), new StandaloneSamSummary(), new List<KerberosAdFinding>(), new List<StandaloneSamFinding>(), new List<UbaAnomalyItem>(), results);

            Assert.Contains("NEEVALUAT", html);
            Assert.Contains("badge-na", html);
            Assert.DoesNotContain(">CONFORM<", html);
        }

        // ---- Dashboard severity counts (replaces fixed 42/28/18/12 sample percentages) ----

        [Fact]
        public void Severity_distribution_counts_real_alerts_and_excludes_test_alerts()
        {
            var issues = new List<DetectedIssue>
            {
                new() { Severity = "Critical" }, new() { Severity = "critical" }, new() { Severity = "High" },
                new() { Severity = "Medium" }, new() { Severity = "Low" }, new() { Severity = "Info" },
                new() { Severity = "Mystery" },
                new() { Severity = "High", IsTestAlert = true },
            };

            var d = SeverityDistribution.Count(issues);

            Assert.Equal(2, d.Critical);
            Assert.Equal(1, d.High);
            Assert.Equal(1, d.Medium);
            Assert.Equal(2, d.LowOrInfo);
            Assert.Equal(1, d.Other);
            Assert.Equal(1, d.TestAlertsExcluded);
            Assert.Equal(7, d.Total);
        }

        [Fact]
        public void Severity_distribution_of_nothing_is_zero_not_a_sample()
        {
            var d = SeverityDistribution.Count(null);

            Assert.Equal(0, d.Total);
            Assert.Equal(0, d.Critical);
        }

        // ---- Test alert is labelled ----

        [Fact]
        public void Test_alert_is_flagged_and_labelled_as_not_real()
        {
            var alert = new LiveSecurityMonitoringEngine().EvaluateLiveEvent(new ParsedEvent
            {
                EventId = 1,
                MachineName = "HOST-1",
                Message = "SIMULARE DFIR test alert",
                TimeCreated = DateTime.UtcNow
            });

            Assert.NotNull(alert);
            Assert.True(alert!.IsTestAlert);
            Assert.StartsWith("[TEST", alert.Title);
            Assert.Contains("NU ESTE O ALERTĂ REALĂ", alert.Title);
            Assert.Contains("NU O DETECȚIE REALĂ", alert.Explanation);
        }

        [Fact]
        public void Real_detection_is_not_flagged_as_test()
        {
            var alert = new LiveSecurityMonitoringEngine().EvaluateLiveEvent(new ParsedEvent
            {
                EventId = 4688,
                MachineName = "SRV-1",
                Message = "Process CommandLine: vssadmin.exe delete shadows /all /quiet",
                TimeCreated = DateTime.UtcNow
            });

            Assert.NotNull(alert);
            Assert.False(alert!.IsTestAlert);
        }

        // ---- Shell sources carry no static safety claims ----

        private static string AppDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml");
                if (File.Exists(candidate)) return Path.Combine(dir.FullName, "LogAnalyzer.App");
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
        }

        public static IEnumerable<object[]> ShellFiles() => new[]
        {
            new object[] { "MainWindow.xaml" },
            new object[] { Path.Combine("Views", "DashboardView.xaml") },
            new object[] { Path.Combine("Views", "AttackStorylineView.xaml") },
        };

        [Theory]
        [MemberData(nameof(ShellFiles))]
        public void Shell_xaml_has_no_static_safety_claims(string relative)
        {
            var text = File.ReadAllText(Path.Combine(AppDir(), relative));
            string[] forbidden =
            {
                "ALL SYSTEMS NORMAL", "SHIELD ARMED", "EVIDENCE VAULT SECURED", "Auto-isolation &amp; kill process armed",
                "Validated\"", "Critical (42%)", "High (28%)", "Medium (18%)", "REAL-TIME SEVERITY DISTRIBUTION",
                "persistent adversary activity progressing", "sensor attached",
            };
            foreach (var phrase in forbidden)
                Assert.DoesNotContain(phrase, text, StringComparison.Ordinal);
        }

        [Fact]
        public void Containment_card_and_status_bar_follow_the_real_auto_containment_switch()
        {
            var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
            var dash = File.ReadAllText(Path.Combine(AppDir(), "Views", "DashboardView.xaml"));

            Assert.Contains("Containment.AutoContainEnabled", main);
            Assert.Contains("Containment.AutoContainEnabled", dash);
            Assert.Contains("oprită (implicit)", main);
            Assert.Contains("Stare: nicio analiză rulată", main);
            Assert.Contains("{Binding ProvenanceStatusMessage}", dash);
        }

        [Fact]
        public void Apt_page_is_titled_technique_overlap_and_says_it_is_not_attribution()
        {
            var text = File.ReadAllText(Path.Combine(AppDir(), "Views", "ThreatIntelView.xaml"));
            var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));

            Assert.DoesNotContain("APT Attribution", text);
            Assert.DoesNotContain("& Attribution", text);
            Assert.Contains("nu este atribuire", text);
            Assert.Contains("NU este atribuire", text);
            Assert.DoesNotContain("Threat Intel &amp; APT\"", main);
        }

        [Fact]
        public void Keyword_only_acoustic_hint_is_not_a_critical_detection()
        {
            var alert = new LiveSecurityMonitoringEngine().EvaluateLiveEvent(new ParsedEvent
            {
                EventId = 1, MachineName = "HOST-2", Message = "fansmitter acoustic test string", TimeCreated = DateTime.UtcNow
            });

            Assert.NotNull(alert);
            Assert.NotEqual("Critical", alert!.Severity);
            Assert.Contains("NEVERIFICAT", alert.Title);
            Assert.Contains("NU este o detecție confirmată", alert.Explanation);
            Assert.DoesNotContain("TEMPEST", alert.Explanation);
        }

        [Fact]
        public void Sanitization_text_certificate_cites_no_unverified_article_or_conformity()
        {
            var text = new SanitizationCertificateGenerator().GenerateTextCertificate(new SanitizationCertificateData());

            Assert.DoesNotContain("ART. 65", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AC/35-D/1022", text);
            Assert.DoesNotContain("Conform NIST", text);
            Assert.Contains("NIST SP 800-88r2", text);
        }

        [Fact]
        public void Compliance_matrix_cites_no_unconfirmed_article_number()
        {
            var results = new ComplianceAuditEngine().Evaluate(new List<ParsedEvent>(), new AdAuditSummary(), new StandaloneSamSummary(), 0, 0);

            Assert.DoesNotContain(results, r => r.ArticleOrControl.StartsWith("Art. 21", StringComparison.Ordinal));
        }

        [Fact]
        public void Data_collection_page_shows_no_static_classification()
        {
            var text = File.ReadAllText(Path.Combine(AppDir(), "Views", "DataCollectionView.xaml"));

            Assert.DoesNotContain("SECRET DE SERVICIU", text);
            Assert.DoesNotContain("NATO AC/35", text);
        }

        [Fact]
        public void Provenance_default_is_not_a_verification_claim()
        {
            var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));

            Assert.DoesNotContain("Lanț Criptografic Verificat (SHA-256)\";", vm);
            Assert.Contains("nu a fost verificat în această sesiune", vm);
        }
    }
}
