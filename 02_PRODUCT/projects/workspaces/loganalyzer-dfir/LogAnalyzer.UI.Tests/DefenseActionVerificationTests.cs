using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Infrastructure.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Station-wide firewall actions: APPLY then VERIFY. Success only when the command succeeded AND the rule state was
    /// checked afterwards; otherwise FAILED or NOT_VERIFIED. No fixed fallback target, no argument injection.
    /// </summary>
    public class DefenseActionVerificationTests
    {
        private sealed class FakeNetsh : IFirewallCommandRunner
        {
            private readonly HashSet<string> _rules = new();
            public List<string> Calls { get; } = new();
            public int AddExit { get; set; }
            public bool AddCreatesRule { get; set; } = true;
            public int DeleteExit { get; set; }
            public bool DeleteRemovesRule { get; set; } = true;

            public CommandOutcome Netsh(string arguments)
            {
                Calls.Add(arguments);
                var name = arguments.Split("name=\"")[1].Split('"')[0];
                if (arguments.Contains(" add rule "))
                {
                    if (AddExit == 0 && AddCreatesRule) _rules.Add(name);
                    return new CommandOutcome(AddExit, AddExit == 0 ? "Ok." : "The requested operation requires elevation.");
                }
                if (arguments.Contains(" delete rule "))
                {
                    if (DeleteExit == 0 && DeleteRemovesRule) _rules.Remove(name);
                    return new CommandOutcome(DeleteExit, DeleteExit == 0 ? "Deleted 1 rule(s)." : "No rules match the specified criteria.");
                }
                return _rules.Contains(name) ? new CommandOutcome(0, "Rule Name: " + name) : new CommandOutcome(1, "No rules match the specified criteria.");
            }
        }

        [Fact]
        public void Isolation_is_success_only_after_the_rule_is_verified()
        {
            var fw = new FakeNetsh();
            var r = SystemDefenseExecutionService.IsolateHostFromNetwork(fw);
            Assert.True(r.Success);
            Assert.Equal(DefenseActionResult.Verified, r.Status);
            Assert.Contains(fw.Calls, c => c.Contains("show rule"));
        }

        [Fact]
        public void Isolation_failure_is_not_reported_as_success()
        {
            var r = SystemDefenseExecutionService.IsolateHostFromNetwork(new FakeNetsh { AddExit = 1 });
            Assert.False(r.Success);
            Assert.Equal(DefenseActionResult.Failed, r.Status);
            Assert.DoesNotContain("succes", r.Message);
            Assert.Contains("elevation", r.ExecutionDetails);
        }

        [Fact]
        public void Command_ok_but_rule_absent_is_not_verified()
        {
            var r = SystemDefenseExecutionService.IsolateHostFromNetwork(new FakeNetsh { AddCreatesRule = false });
            Assert.False(r.Success);
            Assert.Equal(DefenseActionResult.NotVerified, r.Status);
        }

        [Fact]
        public void Restore_checks_exit_code_and_that_the_rule_is_gone()
        {
            var fw = new FakeNetsh();
            SystemDefenseExecutionService.IsolateHostFromNetwork(fw);
            Assert.True(SystemDefenseExecutionService.RestoreNetworkAccess(fw).Success);

            var stuck = new FakeNetsh { DeleteRemovesRule = false };
            SystemDefenseExecutionService.IsolateHostFromNetwork(stuck);
            var r = SystemDefenseExecutionService.RestoreNetworkAccess(stuck);
            Assert.False(r.Success);
            Assert.Equal(DefenseActionResult.NotVerified, r.Status);
        }

        [Fact]
        public void Restore_without_isolation_rule_reports_nothing_to_remove()
        {
            var r = SystemDefenseExecutionService.RestoreNetworkAccess(new FakeNetsh { DeleteExit = 1 });
            Assert.True(r.Success);
            Assert.Contains("nu există", r.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-an-ip")]
        [InlineData("1.2.3.4\" dir=in action=allow name=\"x")]
        [InlineData("999.1.1.1")]
        public void BlockIoC_rejects_empty_or_invalid_targets_without_running_anything(string target)
        {
            var fw = new FakeNetsh();
            var r = SystemDefenseExecutionService.BlockMaliciousIoC(target, fw);
            Assert.False(r.Success);
            Assert.Empty(fw.Calls);
            Assert.DoesNotContain("185.220.101.5", r.Message);
        }

        [Fact]
        public void BlockIoC_uses_a_deterministic_rule_name_and_verifies_it()
        {
            var fw = new FakeNetsh();
            var r = SystemDefenseExecutionService.BlockMaliciousIoC(" 203.0.113.7 ", fw);
            Assert.True(r.Success);
            Assert.Equal(DefenseActionResult.Verified, r.Status);
            Assert.Contains("name=\"DFIR_BLOCK_IOC 203.0.113.7\"", fw.Calls.First());
            Assert.Contains("remoteip=203.0.113.7", fw.Calls.First());
            Assert.Contains("DFIR_BLOCK_IOC 203.0.113.7", r.Message);
        }

        [Fact]
        public void BlockIoC_failure_is_reported()
        {
            var r = SystemDefenseExecutionService.BlockMaliciousIoC("203.0.113.7", new FakeNetsh { AddExit = 1 });
            Assert.False(r.Success);
            Assert.Equal(DefenseActionResult.Failed, r.Status);
        }
    }
}
