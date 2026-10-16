using System;
using System.IO;
using System.Security.Cryptography;
using LogAnalyzer.Response.Collection;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Review of PR #296: the collection script is started only when the file next to the executable is byte for byte the script the assembly was built
    /// with (its SHA-256 is fixed at build time); a changed script is refused before anything starts.
    /// </summary>
    public class AuditCollectorScriptHashTests
    {
        private static string RepoScript()
        {
            var d = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(d, "LogAnalyzer.slnx"))) d = Path.GetDirectoryName(d)!;
            return Path.Combine(d, "LogAnalyzer.Response", "Scripts", "AuditCollector.ps1");
        }

        [Fact]
        public void The_build_time_hash_is_the_hash_of_the_shipped_script()
        {
            var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(RepoScript())));
            Assert.Equal(expected, PowerShellAuditCollector.BuildScriptSha256);
            Assert.Null(PowerShellAuditCollector.ScriptRefusal(RepoScript()));
        }

        [Fact]
        public void A_changed_or_missing_script_is_refused_before_it_starts()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "script-hash-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                var copy = Path.Combine(dir, "AuditCollector.ps1");
                File.Copy(RepoScript(), copy);
                Assert.Null(PowerShellAuditCollector.ScriptRefusal(copy));
                File.AppendAllText(copy, "\nInvoke-Expression $env:X\n");
                var why = PowerShellAuditCollector.ScriptRefusal(copy);
                Assert.NotNull(why);
                Assert.Contains("amprenta", why);
                Assert.Contains(PowerShellAuditCollector.BuildScriptSha256, why);
                Assert.NotNull(PowerShellAuditCollector.ScriptRefusal(Path.Combine(dir, "missing.ps1")));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
