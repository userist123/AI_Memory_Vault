using System;
using System.IO;
using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Response;
using LogAnalyzer.Response.Collection;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Review of PR #296: a "collector.exec" record for PowerShell is the application's own only when its command line runs exactly the application's
    /// collection script, in the argument form the application emits, and the recorded script hash is the hash of the script embedded at build time.
    /// </summary>
    public class AuditCollectorScriptRecordTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        private static readonly string AppSha = new('a', 64);

        private static string Line(string ev, DateTimeOffset when, string detail) => JsonSerializer.Serialize(new { Event = ev, WhenUtc = when, Detail = detail });

        private static OperationAudit.CollectorExecs Read(string cmd, string scriptSha)
        {
            ResponseProgramInventory.Register();
            var ps = PowerShellAuditCollector.PowerShellPath;
            var dir = Path.Combine(AppContext.BaseDirectory, "ps-record-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "audit.jsonl");
            File.WriteAllLines(path,
            [
                Line(OperationAudit.ControlStarted, T0, OperationAudit.ControlStartedDetail("Unclassified", "Internet", true, 30, 0, "run-A", AppSha)),
                Line(CollectorExec.Exec, T0.AddMinutes(1), CollectorExec.ExecDetail("e1", "run-A", 4100, AppSha, CollectorExec.FileSha256(ps), scriptSha, ps, cmd)),
                Line(CollectorExec.ExecDone, T0.AddMinutes(2), CollectorExec.DoneDetail("e1", "run-A", 5200, 0, T0.AddMinutes(2))),
                Line(OperationAudit.ControlFinished, T0.AddMinutes(20), OperationAudit.ControlFinishedDetail("WS", true, 1, 0, 0, 0, "run-A")),
            ]);
            try { return OperationAudit.ReadCollectorExecs(path, chainValid: true); }
            finally { Directory.Delete(dir, true); }
        }

        private static string AppCommand(string outputDir = @"E:\Colectare 1\", string host = "ST-01") =>
            CollectorExec.CommandLineOf(PowerShellAuditCollector.BuildStartInfo(PowerShellAuditCollector.PowerShellPath, PowerShellAuditCollector.ScriptPath, "PC", outputDir, host));

        [Fact]
        public void The_applications_own_collection_run_is_accepted()
        {
            Assert.Equal(64, PowerShellAuditCollector.BuildScriptBlockSha256.Length);
            Assert.Single(Read(AppCommand(), PowerShellAuditCollector.BuildScriptBlockSha256).Valid);
            Assert.Single(Read(AppCommand(@"D:\out", "pc.local"), PowerShellAuditCollector.BuildScriptBlockSha256).Valid);
        }

        [Fact]
        public void Any_other_powershell_command_line_or_script_hash_is_refused()
        {
            var sha = PowerShellAuditCollector.BuildScriptBlockSha256;
            var ps = "\"" + PowerShellAuditCollector.PowerShellPath + "\"";
            foreach (var cmd in new[]
            {
                ps + " -NoProfile -Command Get-Process",
                AppCommand().Replace(PowerShellAuditCollector.ScriptPath, @"C:\Temp\AuditCollector.ps1"),       // another script file
                AppCommand() + " -Extra 1",                                                                     // an argument the application never adds
                AppCommand().Replace(" -NonInteractive", ""),                                                  // not the exact form
                AppCommand(host: "x;Invoke-Expression"),                                                       // a host name the application refuses
            })
            {
                var r = Read(cmd, sha);
                Assert.Empty(r.Valid);
                Assert.Contains("scriptul de colectare", Assert.Single(r.Refused).Why);
            }
            var other = Read(AppCommand(), new string('d', 64));
            Assert.Empty(other.Valid);
            Assert.Contains("amprenta scriptului", Assert.Single(other.Refused).Why);
        }
    }
}
