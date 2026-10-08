using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LogAnalyzer.Core.Interfaces;

namespace LogAnalyzer.Infrastructure.Services
{
    public sealed record CommandOutcome(int ExitCode, string Output);

    /// <summary>Runs one firewall rule operation (netsh-style description); replaceable so the APPLY/VERIFY logic is tested without touching the firewall.</summary>
    public interface IFirewallCommandRunner
    {
        CommandOutcome Netsh(string arguments);
        /// <summary>Name shown in the execution log.</summary>
        string BackendName => "netsh";
    }

    public sealed class NetshRunner : IFirewallCommandRunner
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public CommandOutcome Netsh(string arguments)
        {
            var psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("netsh.exe nu a putut fi pornit.");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(Timeout))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* exited between the check and the kill */ }
                return new CommandOutcome(-1, $"netsh nu a terminat în {Timeout.TotalSeconds:0} s; operația a fost oprită.");
            }
            return new CommandOutcome(p.ExitCode, (stdout.Result + stderr.Result).Trim());
        }
    }

    /// <summary>
    /// Station-wide firewall actions started by the operator (each one is confirmed in the UI). Every action is APPLY then
    /// VERIFY: success is reported only when the operation succeeded and the rule's presence/absence was checked afterwards.
    /// Per-program containment with evidence lives in Dfir.Windows/Containment.
    /// </summary>
    public static class SystemDefenseExecutionService
    {
        public const string FirewallIsolationRuleName = "DFIR_EMERGENCY_ISOLATION";
        public const string IocRulePrefix = "DFIR_BLOCK_IOC ";

        // Production default: Windows Firewall COM API (no process, no PATH). NetshRunner remains available as an explicit, documented fallback.
        private static readonly IFirewallCommandRunner Default = new ComFirewallRunner();

        /// <summary>Blocks all outbound traffic of the station with one firewall rule.</summary>
        public static DefenseActionResult IsolateHostFromNetwork(IFirewallCommandRunner? runner = null) =>
            AddRule(runner ?? Default, FirewallIsolationRuleName, $"dir=out action=block profile=any",
                "Stația este izolată: regula „{0}” blochează tot traficul de ieșire.", "Izolarea stației");

        /// <summary>Removes the isolation rule and checks that it is gone.</summary>
        public static DefenseActionResult RestoreNetworkAccess(IFirewallCommandRunner? runner = null) =>
            DeleteRule(runner ?? Default, FirewallIsolationRuleName,
                "Izolarea a fost ridicată: regula „{0}” nu mai există.", "Regula de izolare „{0}” nu există; nu era nimic de ridicat.", "Ridicarea izolării");

        /// <summary>Blocks outbound traffic to one IP address or CIDR range. Nothing is run for an empty or invalid target.</summary>
        public static DefenseActionResult BlockMaliciousIoC(string iocTarget, IFirewallCommandRunner? runner = null)
        {
            var target = (iocTarget ?? string.Empty).Trim();
            if (!IsIpOrCidr(target))
                return new DefenseActionResult
                {
                    Status = DefenseActionResult.Rejected,
                    Message = $"Ținta „{target}” nu este o adresă IP sau un interval CIDR valid; nu s-a creat nicio regulă.",
                };
            return AddRule(runner ?? Default, IocRulePrefix + target, $"dir=out action=block remoteip={target}",
                "Traficul de ieșire către {1} este blocat de regula „{0}”. Se șterge din Windows Defender Firewall sau cu: netsh advfirewall firewall delete rule name=\"{0}\".",
                $"Blocarea {target}", target);
        }

        public static bool IsIpOrCidr(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split('/');
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip)) return false;
            // IPAddress.TryParse accepts shorthand such as "1" or "1.2"; require the full dotted form for IPv4.
            if (ip.AddressFamily == AddressFamily.InterNetwork && parts[0].Split('.').Length != 4) return false;
            if (parts.Length == 1) return true;
            int max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            return int.TryParse(parts[1], out var bits) && bits >= 0 && bits <= max && parts[1] == bits.ToString();
        }

        private static DefenseActionResult AddRule(IFirewallCommandRunner fw, string name, string spec, string okMessage, string action, string target = "")
        {
            var log = new System.Text.StringBuilder();
            try
            {
                var add = Run(fw, $"advfirewall firewall add rule name=\"{name}\" {spec}", log);
                if (add.ExitCode != 0)
                    return Result(DefenseActionResult.Failed, $"{action} NU a fost aplicată (operația a întors {add.ExitCode}). Rulați aplicația ca administrator.", log);
                if (!RuleExists(fw, name, log))
                    return Result(DefenseActionResult.NotVerified, $"{action}: operația a raportat succes, dar regula „{name}” nu a fost găsită la verificare.", log);
                return Result(DefenseActionResult.Verified, string.Format(okMessage, name, target), log);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log.AppendLine(ex.ToString());
                return Result(DefenseActionResult.Failed, $"{action} NU a fost aplicată: {ex.Message}", log);
            }
        }

        private static DefenseActionResult DeleteRule(IFirewallCommandRunner fw, string name, string okMessage, string absentMessage, string action)
        {
            var log = new System.Text.StringBuilder();
            try
            {
                if (!RuleExists(fw, name, log))
                    return Result(DefenseActionResult.Verified, string.Format(absentMessage, name), log);
                var del = Run(fw, $"advfirewall firewall delete rule name=\"{name}\"", log);
                if (!RuleExists(fw, name, log))
                    return Result(DefenseActionResult.Verified, string.Format(okMessage, name), log);
                return Result(del.ExitCode != 0 ? DefenseActionResult.Failed : DefenseActionResult.NotVerified,
                    $"{action}: regula „{name}” există în continuare (operația a întors {del.ExitCode}). Rulați aplicația ca administrator.", log);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log.AppendLine(ex.ToString());
                return Result(DefenseActionResult.Failed, $"{action} a eșuat: {ex.Message}", log);
            }
        }

        /// <summary>"show rule" exits 0 when at least one rule has that name, 1 otherwise (independent of the UI language).</summary>
        private static bool RuleExists(IFirewallCommandRunner fw, string name, System.Text.StringBuilder log) =>
            Run(fw, $"advfirewall firewall show rule name=\"{name}\"", log).ExitCode == 0;

        private static CommandOutcome Run(IFirewallCommandRunner fw, string args, System.Text.StringBuilder log)
        {
            var o = fw.Netsh(args);
            log.AppendLine($"{fw.BackendName} {args} → {o.ExitCode}");
            if (o.Output.Length > 0) log.AppendLine(o.Output);
            return o;
        }

        private static DefenseActionResult Result(string status, string message, System.Text.StringBuilder log) => new()
        {
            Success = status == DefenseActionResult.Verified,
            Status = status,
            Message = message,
            ExecutionDetails = log.ToString(),
        };
    }

    /// <summary>The unclassified edition's <see cref="IHostDefense"/>.</summary>
    public sealed class HostDefense : IHostDefense
    {
        public DefenseActionResult IsolateHostFromNetwork() => SystemDefenseExecutionService.IsolateHostFromNetwork();
        public DefenseActionResult RestoreNetworkAccess() => SystemDefenseExecutionService.RestoreNetworkAccess();
        public DefenseActionResult BlockMaliciousIoC(string iocTarget) => SystemDefenseExecutionService.BlockMaliciousIoC(iocTarget);
    }
}
