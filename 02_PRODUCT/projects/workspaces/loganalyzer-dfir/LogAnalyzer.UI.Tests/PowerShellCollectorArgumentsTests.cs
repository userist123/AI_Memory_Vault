using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LogAnalyzer.Response.Collection;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #6: the collector's command line is built with ProcessStartInfo.ArgumentList (no string interpolation), so a USB root
    /// "E:\", a path with spaces or a quote in the host name reach the script verbatim as one argument each; the host name is also validated in C#
    /// with the script's own pattern before anything is started.
    /// </summary>
    public sealed class PowerShellCollectorArgumentsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-psargs-" + Guid.NewGuid().ToString("N"));

        public PowerShellCollectorArgumentsTests() => Directory.CreateDirectory(_dir);

        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        private static string PowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

        /// <summary>A stand-in for AuditCollector.ps1 that only prints what it received, one parameter per line.</summary>
        private string EchoScript()
        {
            var p = Path.Combine(_dir, "echo args.ps1");
            File.WriteAllText(p, "param([string]$TargetType, [string]$OutputDirectory, [string]$Hostname)\r\n" +
                                 "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)\r\n" +
                                 "Write-Output ('T=[' + $TargetType + ']')\r\nWrite-Output ('O=[' + $OutputDirectory + ']')\r\nWrite-Output ('H=[' + $Hostname + ']')\r\n" +
                                 "Write-Output ('N=' + $args.Count)\r\n", new UTF8Encoding(true));
            return p;
        }

        private static List<string> Run(ProcessStartInfo psi)
        {
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            using var p = Process.Start(psi)!;
            var lines = p.StandardOutput.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            p.WaitForExit(60_000);
            return lines;
        }

        [Theory]
        [InlineData(@"E:\", "PC01")]
        [InlineData(@"C:\Probe colectate\caz 7\", "PC01")]
        [InlineData(@"D:\out", "PC01\" -TargetType \"NAS")]
        public void Arguments_reach_the_script_verbatim(string outputDir, string hostname)
        {
            if (!File.Exists(PowerShell)) return;
            var psi = PowerShellAuditCollector.BuildStartInfo(PowerShell, EchoScript(), "PC", outputDir, hostname);
            Assert.True(string.IsNullOrEmpty(psi.Arguments));      // ArgumentList only, no hand-built command line
            Assert.Contains("-File", psi.ArgumentList);
            var lines = Run(psi);
            Assert.Contains("T=[PC]", lines);
            Assert.Contains($"O=[{outputDir}]", lines);
            Assert.Contains($"H=[{hostname}]", lines);
            Assert.Contains("N=0", lines);
        }

        [Theory]
        [InlineData("PC01", true)]
        [InlineData("srv-02.corp_x", true)]
        [InlineData("PC01\" -TargetType \"NAS", false)]
        [InlineData("..\\..\\x", false)]
        [InlineData("-PC", false)]
        [InlineData("", false)]
        [InlineData("a b", false)]
        public void Hostname_is_validated_with_the_scripts_pattern(string hostname, bool ok)
        {
            Assert.Equal(ok, PowerShellAuditCollector.IsValidHostname(hostname));
        }

        [Fact]
        public void The_C_sharp_pattern_is_the_one_in_the_script()
        {
            var d = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(d, "LogAnalyzer.slnx"))) d = Path.GetDirectoryName(d)!;
            var ps1 = File.ReadAllText(Path.Combine(d, "LogAnalyzer.Response", "Scripts", "AuditCollector.ps1"));
            Assert.Contains($"$Hostname -notmatch '{PowerShellAuditCollector.HostnamePattern}'", ps1);
        }

        [Fact]
        public async Task An_invalid_hostname_is_refused_before_anything_is_started()
        {
            var log = new List<string>();
            await new PowerShellAuditCollector().RunCollectionAsync("PC", _dir, "PC01\" -AllowNetwork \"x", log.Add);
            Assert.Contains(log, l => l.StartsWith("[ERROR]") && l.Contains("Hostname"));
            Assert.DoesNotContain(log, l => l.StartsWith("[INIT] Pornire"));
        }
    }
}
