using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Dfir.IO;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #8: the Plaso / Timesketch super-timeline CSV goes through the Dfir.Core CSV escaping, which neutralises spreadsheet formulas
    /// (a leading = + - @ TAB or CR gets a ' prefix), so an event message such as =HYPERLINK(...) is shown as text when the CSV is opened in Excel.
    /// </summary>
    public class CsvFormulaNeutralisationTests
    {
        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
        [InlineData("@SUM(A1)", "'@SUM(A1)")]
        [InlineData("-2+3", "'-2+3")]
        [InlineData("\t=1", "'\t=1")]
        [InlineData("\r=1", "\"'\r=1\"")]
        [InlineData("-5", "-5")]
        [InlineData("-", "-")]
        [InlineData("normal", "normal")]
        public void Csv_escape_neutralises_formulas(string input, string expected)
        {
            Assert.Equal(expected, CsvWriter.Escape(input));
        }

        /// <summary>Independent review, finding 4: the AD audit CSV cells are evidence-derived and go through the same neutralisation and hidden-character removal.</summary>
        [Fact]
        public void Ad_audit_csv_neutralises_formulas_and_removes_hidden_characters()
        {
            var f = new KerberosAdFinding { Category = "c", AttackType = "=1+1", TargetAccount = "@acct", ClientIp = "+10.0.0.1", Description = "a‮b", ContainmentActionRo = "-x" };
            var u = new UbaAnomalyItem { Username = "=2+2", AnomalyType = "t", Workstation = "w", Description = "d" };
            var csv = new AdAuditReportService().GenerateCsvReport(new AdAuditSummary(), new[] { f }, new[] { u });
            Assert.Contains("\"'=1+1\"", csv);
            Assert.Contains("\"'@acct\"", csv);
            Assert.Contains("\"'+10.0.0.1\"", csv);
            Assert.Contains("\"'-x\"", csv);
            Assert.Contains("\"'=2+2\"", csv);
            Assert.DoesNotContain("‮", csv, StringComparison.Ordinal);   // ordinal: culture comparison ignores the bidi control
            Assert.Contains("[caractere ascunse eliminate: U+202E]", csv);
        }

        [Fact]
        public void Super_timeline_cells_starting_with_a_formula_are_prefixed()
        {
            var events = new List<ParsedEvent>
            {
                new() { TimeCreated = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), ProviderName = "@prov", EventId = 1, MachineName = "+host", Level = "-x",
                        Message = "=HYPERLINK(\"http://example.invalid\",\"x\")" },
            };
            var reg = new List<RegistryArtifact> { new() { KeyPath = "=1+1", ValueData = "v", LastWriteTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ValueName = "=2+2", HiveType = "h", Category = "c" } };
            var path = Path.Combine(Path.GetTempPath(), $"ts_formula_{Guid.NewGuid():N}.csv");
            try
            {
                new SuperTimelineExportService().ExportPlasoCsv(path, events, new List<ForensicArtifact>(), reg);
                var rows = File.ReadAllText(path).Split("\r\n").Skip(1).Where(r => r.Length > 0).ToList();
                Assert.StartsWith("\"'=HYPERLINK(", rows[0]);
                Assert.Contains(",'@prov,", rows[0]);
                Assert.Contains(",'+host,", rows[0]);
                Assert.Contains(",'-x,", rows[0]);
                Assert.StartsWith("'=1+1 -> v,", rows[1]);
                Assert.Contains(",'=2+2,", rows[1]);
                Assert.DoesNotContain(rows, r => r.StartsWith("=") || r.Contains(",=") || r.Contains(",+") || r.Contains(",@"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
