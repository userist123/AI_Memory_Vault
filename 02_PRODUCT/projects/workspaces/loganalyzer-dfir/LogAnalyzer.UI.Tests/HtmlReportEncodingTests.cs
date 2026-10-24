using System;
using System.Collections.Generic;
using System.IO;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #1: every evidence-derived value in the HTML reports is HTML-encoded and the
    /// report carries a restrictive Content-Security-Policy, so a crafted username or event message
    /// cannot run script when the analyst opens the report in a browser.
    /// </summary>
    public class HtmlReportEncodingTests
    {
        private const string ScriptPayload = "<script>alert(1)</script>";
        private const string AttributePayload = "\"><img src=x onerror=alert(2)>";
        private const string QuotePayload = "' onmouseover='alert(3)";

        private static void AssertNoRawPayload(string html)
        {
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("' onmouseover='", html, StringComparison.Ordinal);
            Assert.DoesNotContain("\"><img", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("&quot;&gt;&lt;img src=x onerror=alert(2)&gt;", html);
            Assert.Contains("Content-Security-Policy", html);
            Assert.Contains("default-src 'none'", html);
        }

        [Fact]
        public void ForensicHtmlReport_EncodesEveryEvidenceField_AndCarriesCsp()
        {
            var issue = new DetectedIssue
            {
                Title = ScriptPayload,
                Severity = AttributePayload,
                Explanation = AttributePayload,
                MitreTechniqueId = QuotePayload,
                ComplianceTag = ScriptPayload,
            };
            var item = new TimelineItem
            {
                Timestamp = DateTime.UtcNow,
                Source = ScriptPayload,
                Category = AttributePayload,
                UserOrHost = ScriptPayload,
                Description = AttributePayload + QuotePayload,
            };
            var path = Path.Combine(Path.GetTempPath(), "la-html-enc-" + Guid.NewGuid().ToString("N") + ".html");
            try
            {
                HtmlReportService.GenerateReport(path, new List<DetectedIssue> { issue }, new List<TimelineItem> { item },
                    ScriptPayload, 1, 1, 1, AttributePayload);
                var html = File.ReadAllText(path);
                AssertNoRawPayload(html);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AdAuditHtmlReport_EncodesEveryEvidenceField_AndCarriesCsp(bool airGapped)
        {
            var html = new AdAuditHtmlReportService().GenerateHtmlReport(
                new AdAuditSummary(),
                new StandaloneSamSummary(),
                new List<KerberosAdFinding> { new() { Category = ScriptPayload, AttackType = AttributePayload, Severity = QuotePayload, TargetAccount = ScriptPayload, MitreTechniqueId = AttributePayload, Description = ScriptPayload } },
                new List<StandaloneSamFinding> { new() { Category = ScriptPayload, FindingType = AttributePayload, Severity = QuotePayload, TargetAccountOrResource = ScriptPayload, MitreTechniqueId = AttributePayload, Description = ScriptPayload, RemediationActionRo = AttributePayload } },
                new List<UbaAnomalyItem> { new() { Username = ScriptPayload, AnomalyType = AttributePayload, Severity = QuotePayload, Description = ScriptPayload } },
                new List<ComplianceCheckResult> { new() { Framework = ScriptPayload, ArticleOrControl = AttributePayload, ControlTitle = QuotePayload, Status = ScriptPayload, EvidenceSummary = AttributePayload, RequiredAction = ScriptPayload } },
                airGapped);
            AssertNoRawPayload(html);
        }

        [Fact]
        public void HtmlText_EncodesQuotesAndAngleBrackets()
        {
            Assert.Equal("&lt;a href=&quot;x&quot; title=&#39;y&#39;&gt;&amp;", HtmlText.Encode("<a href=\"x\" title='y'>&"));
            Assert.Equal(string.Empty, HtmlText.Encode(null));
            Assert.Equal("42", HtmlText.Encode(42));
        }
    }
}
