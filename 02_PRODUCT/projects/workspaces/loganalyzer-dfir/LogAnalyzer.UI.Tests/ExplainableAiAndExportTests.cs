using System;
using System.Collections.Generic;
using System.IO;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    public class ExplainableAiAndExportTests
    {
        [Fact]
        public void ExplainableAiRiskEngine_ComputesWeightedScoreAndFactors()
        {
            var engine = new ExplainableAiRiskEngine();
            var issues = new List<DetectedIssue>
            {
                new DetectedIssue
                {
                    Title = "Test Kerberoasting",
                    Severity = "High",
                    MitreTechniqueId = "T1558.003",
                    Explanation = "RC4 encryption requested in TGS ticket."
                }
            };

            var assessment = engine.Evaluate(issues, highEntropyCount: 2, masqueradingCount: 1, offHoursCount: 1, yaraMatchesCount: 1);

            Assert.NotNull(assessment);
            Assert.True(assessment.TotalScore > 0 && assessment.TotalScore <= 100);
            Assert.NotEmpty(assessment.Factors);
            Assert.Contains(assessment.Factors, f => f.MitreTechniqueId == "T1027");
            Assert.Contains(assessment.Factors, f => f.MitreTechniqueId == "T1036.003");
            Assert.False(string.IsNullOrWhiteSpace(assessment.ExecutiveSummaryRo));
        }

        [Fact]
        public void CaseUcoExportService_GeneratesValidJsonLdOntology()
        {
            var exportService = new CaseUcoExportService();
            var artifacts = new List<ForensicArtifact>
            {
                new ForensicArtifact
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "PassTheHash.exe",
                    ArtifactType = "Prefetch Execution",
                    SourceFilePath = "C:\\Windows\\Prefetch\\PASSTHEHASH.EXE-12345.pf",
                    SourceSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    Timestamp = DateTime.UtcNow,
                    Strength = EvidenceStrength.ExecutionProven
                }
            };

            string jsonLd = exportService.ExportCaseJsonLd("CASE-2026-001", "Operator SOC", "CERT-RO", artifacts, new List<ProvenanceLedgerEntry>());

            Assert.NotNull(jsonLd);
            Assert.Contains("@context", jsonLd);
            Assert.Contains("uco-core", jsonLd);
            Assert.Contains("CASE-2026-001", jsonLd);
        }

        [Fact]
        public void SuperTimelineExportService_GeneratesValidPlasoCsv()
        {
            var exportService = new SuperTimelineExportService();
            var events = new List<ParsedEvent>
            {
                new ParsedEvent
                {
                    TimeCreated = DateTime.UtcNow,
                    ProviderName = "Microsoft-Windows-Security-Auditing",
                    EventId = 4624,
                    Level = "Information",
                    MachineName = "ADMIN-PC",
                    Message = "Successful Logon Event ID 4624"
                }
            };

            string tempCsv = Path.Combine(Path.GetTempPath(), $"timeline_test_{Guid.NewGuid():N}.csv");
            try
            {
                exportService.ExportPlasoCsv(tempCsv, events, new List<ForensicArtifact>(), new List<RegistryArtifact>());
                Assert.True(File.Exists(tempCsv));
                string content = File.ReadAllText(tempCsv);
                Assert.StartsWith("message,datetime,timestamp_desc,", content);
                Assert.Contains("Successful Logon Event ID 4624", content);
            }
            finally
            {
                if (File.Exists(tempCsv)) File.Delete(tempCsv);
            }
        }

        [Fact]
        public void SuperTimelineExport_TimesketchRow_HasIsoUtcAndEscapesCommasQuotesNewlines()
        {
            var events = new List<ParsedEvent>
            {
                new ParsedEvent
                {
                    TimeCreated = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                    ProviderName = "P", EventId = 1, MachineName = "H",
                    Message = "a, \"b\"\nline2"
                }
            };
            string tempCsv = Path.Combine(Path.GetTempPath(), $"ts_{Guid.NewGuid():N}.csv");
            try
            {
                new SuperTimelineExportService().ExportPlasoCsv(tempCsv, events, new List<ForensicArtifact>(), new List<RegistryArtifact>());
                string content = File.ReadAllText(tempCsv);
                Assert.StartsWith(SuperTimelineExportService.Header + "\r\n", content);
                Assert.Contains("\"a, \"\"b\"\"\nline2\",2026-03-04T05:06:07.000000+00:00,Event Recorded,", content);
            }
            finally { if (File.Exists(tempCsv)) File.Delete(tempCsv); }
        }

        [Fact]
        public void SuperTimelineExport_LocalTimeIsConvertedToUtc()
        {
            var local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
            Assert.Equal(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'+00:00'"),
                SuperTimelineExportService.ToIsoUtc(local));
        }
    }
}
