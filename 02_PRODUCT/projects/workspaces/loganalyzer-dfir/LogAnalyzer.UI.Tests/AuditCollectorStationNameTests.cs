using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Response.Collection;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Owner 2026-10-12: the "Colectare date" collector labelled this PC's evidence "PC-AUDIT" (a fixed default), printed "[SUCCESS]" for exit code 2,
    /// mangled "ș" and failed on scheduled tasks without a command line. Every collection must use the name of the station it runs on.
    /// </summary>
    public class AuditCollectorStationNameTests
    {
        private static string Root()
        {
            var d = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(d, "LogAnalyzer.slnx"))) d = Path.GetDirectoryName(d)!;
            return d;
        }

        [Fact]
        public void No_production_source_contains_a_fixed_machine_name()
        {
            var root = Root();
            var offenders = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".ps1"))
                .Where(f => !Regex.IsMatch(f, @"[\\/](obj|bin)[\\/]|Tests[\\/]|\.Tests[\\/]"))
                .Where(f => Regex.IsMatch(File.ReadAllText(f), "\"PC-AUDIT\"|LAB-PC"))
                .Select(f => Path.GetRelativePath(root, f)).ToList();
            Assert.True(offenders.Count == 0, "fixed machine names in: " + string.Join(", ", offenders));
        }

        [Fact]
        public void The_script_labels_PC_and_server_evidence_with_the_real_computer_name()
        {
            var ps1 = File.ReadAllText(Path.Combine(Root(), "LogAnalyzer.Response", "Scripts", "AuditCollector.ps1"));
            Assert.Contains("$Hostname = $env:COMPUTERNAME", ps1);
            Assert.Contains("[Console]::OutputEncoding", ps1);
            Assert.Contains("PSObject.Properties['Execute']", ps1);   // COM-handler task actions have no Execute property
            Assert.DoesNotContain(ps1, c => c > 127);                  // no BOM: Windows PowerShell 5.1 would read non-ASCII text in the ANSI code page
        }

        [Theory]
        [InlineData(0, "[SUCCESS]")]
        [InlineData(2, "[ATENȚIE]")]
        [InlineData(1, "[ERROR]")]
        [InlineData(7, "[ERROR]")]
        public void The_end_line_matches_the_exit_code(int code, string prefix)
        {
            Assert.StartsWith(prefix, PowerShellAuditCollector.CompletionLine(code));
            if (code != 0) Assert.DoesNotContain("SUCCESS", PowerShellAuditCollector.CompletionLine(code));
        }
    }
}
