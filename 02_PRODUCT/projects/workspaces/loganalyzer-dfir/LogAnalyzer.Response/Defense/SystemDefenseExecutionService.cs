using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Dfir.Response;

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

        /// <summary>The firewall tool this runner starts (System32); the editions that ship this runner add it to the application's program inventory.</summary>
        public static string NetshPath => System.IO.Path.Combine(Environment.SystemDirectory, "netsh.exe");

        public CommandOutcome Netsh(string arguments)
        {
            var psi = new ProcessStartInfo(NetshPath, arguments)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = LogAnalyzer.Dfir.Auth.CollectorExec.Start(psi);
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(Timeout))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* exited between the check and the kill */ }
                LogAnalyzer.Dfir.Auth.CollectorExec.Done(p);
                return new CommandOutcome(-1, $"netsh nu a terminat în {Timeout.TotalSeconds:0} s; operația a fost oprită.");
            }
            LogAnalyzer.Dfir.Auth.CollectorExec.Done(p);
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

        /// <summary>Time limit of each re-read.</summary>
        public static TimeSpan VerifyTimeout { get; set; } = ResponseVerifier.DefaultTimeout;

        /// <summary>Blocks all outbound traffic of the station with one firewall rule.</summary>
        [ControlledResponse("firewall rule re-read by name (show rule: 0 present, 1 absent, other = cannot tell)")]
        public static DefenseActionResult IsolateHostFromNetwork(IFirewallCommandRunner? runner = null) =>
            NotAuthorized() ?? AddRule(runner ?? Default, FirewallIsolationRuleName, $"dir=out action=block profile=any",
                "Stația este izolată: regula „{0}” blochează tot traficul de ieșire.", "Izolarea stației");

        /// <summary>Removes the isolation rule and checks that it is gone.</summary>
        [ControlledResponse("firewall rule re-read by name (absent)")]
        public static DefenseActionResult RestoreNetworkAccess(IFirewallCommandRunner? runner = null) =>
            NotAuthorized() ?? DeleteRule(runner ?? Default, FirewallIsolationRuleName,
                "Izolarea a fost ridicată: regula „{0}” nu mai există.", "Regula de izolare „{0}” nu există; nu era nimic de ridicat.", "Ridicarea izolării");

        /// <summary>Blocks outbound traffic to one IP address or CIDR range. Nothing is run for an empty or invalid target.</summary>
        [ControlledResponse("firewall rule re-read by name (present)")]
        public static DefenseActionResult BlockMaliciousIoC(string iocTarget, IFirewallCommandRunner? runner = null)
        {
            if (NotAuthorized() is { } refused) return refused;
            var target = (iocTarget ?? string.Empty).Trim();
            if (!IsIpOrCidr(target))
            {
                var why = $"Ținta „{target}” nu este o adresă IP sau un interval CIDR valid; nu s-a creat nicio regulă.";
                return new DefenseActionResult
                {
                    Status = DefenseActionResult.Rejected,
                    Message = why,
                    Verification = ResponseOutcome.NotAttempted("Blocare IoC", "apply", ResponseStatus.Rejected, why),
                };
            }
            return AddRule(runner ?? Default, IocRulePrefix + target, $"dir=out action=block remoteip={target}",
                "Traficul de ieșire către {1} este blocat de regula „{0}”. Se șterge din Windows Defender Firewall sau cu: netsh advfirewall firewall delete rule name=\"{0}\".",
                $"Blocarea {target}", target);
        }

        /// <summary>Role gate (security audit #2): a signed-in operator never changes the station's firewall; nothing is run.</summary>
        private static DefenseActionResult? NotAuthorized() =>
            LogAnalyzer.Dfir.Auth.OperatorIdentity.MayChangeHost ? null : new DefenseActionResult
            {
                Status = DefenseActionResult.NotAuthorized,
                Message = LogAnalyzer.Dfir.Auth.OperatorIdentity.HostChangeRefusedMessage,
                Verification = ResponseOutcome.NotAttempted("defense", "apply", ResponseStatus.NotAuthorized, LogAnalyzer.Dfir.Auth.OperatorIdentity.HostChangeRefusedMessage),
            };

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
            CommandOutcome add;
            try { add = Run(fw, $"advfirewall firewall add rule name=\"{name}\" {spec}", log); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Append(log, ex.ToString());
                var why = $"{action} NU a fost aplicată: {ex.Message}";
                return Result(DefenseActionResult.Failed, why, log, ResponseOutcome.NotAttempted(action, "apply", ResponseStatus.Failed, why));
            }
            // VERIFY: re-read the rule from the firewall; the exit code of the add is not evidence that it exists.
            var v = ResponseVerifier.Verify(action, "apply", [Probe(fw, name, mustExist: true, log)], VerifyTimeout);
            if (add.ExitCode != 0)
            {
                var why = $"{action} NU a fost aplicată (operația a întors {add.ExitCode}). Rulați aplicația ca administrator.";
                return Result(DefenseActionResult.Failed, why, log, new ResponseOutcome { Action = action, Phase = "apply", Status = ResponseStatus.Failed, Cause = why, Checks = v.Checks, CheckedUtc = v.CheckedUtc });
            }
            return v.Status switch
            {
                ResponseStatus.Verified => Result(DefenseActionResult.Verified, string.Format(okMessage, name, target), log, v),
                ResponseStatus.Failed => Result(DefenseActionResult.Failed, $"{action}: operația a raportat succes, dar regula „{name}” nu a fost găsită la verificare.", log, v),
                _ => Result(DefenseActionResult.NotVerified, $"{action}: operația a raportat succes, dar regula „{name}” nu a putut fi re-verificată ({v.Cause}).", log, v),
            };
        }

        private static DefenseActionResult DeleteRule(IFirewallCommandRunner fw, string name, string okMessage, string absentMessage, string action)
        {
            var log = new System.Text.StringBuilder();
            var before = ResponseVerifier.Verify(action, "rollback", [Probe(fw, name, mustExist: false, log)], VerifyTimeout);
            if (before.Status == ResponseStatus.Verified)
                return Result(DefenseActionResult.Verified, string.Format(absentMessage, name), log, before);
            CommandOutcome del;
            try { del = Run(fw, $"advfirewall firewall delete rule name=\"{name}\"", log); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Append(log, ex.ToString());
                var why = $"{action} a eșuat: {ex.Message}";
                return Result(DefenseActionResult.Failed, why, log, ResponseOutcome.NotAttempted(action, "rollback", ResponseStatus.Failed, why));
            }
            // VERIFY the rollback: the rule must be re-read as absent.
            var v = ResponseVerifier.Verify(action, "rollback", [Probe(fw, name, mustExist: false, log)], VerifyTimeout);
            return v.Status switch
            {
                ResponseStatus.Verified => Result(DefenseActionResult.Verified, string.Format(okMessage, name), log, v),
                ResponseStatus.Failed => Result(DefenseActionResult.Failed,
                    $"{action}: regula „{name}” există în continuare (operația a întors {del.ExitCode}). Rulați aplicația ca administrator.", log, v),
                _ => Result(DefenseActionResult.NotVerified, $"{action}: operația a întors {del.ExitCode}, dar absența regulii „{name}” nu a putut fi re-verificată ({v.Cause}).", log, v),
            };
        }

        /// <summary>
        /// Re-read of one rule. "show rule" exits 0 when a rule has that name and 1 when none has (independent of the UI language); any other
        /// code (access denied, timeout, backend unavailable) means the state cannot be told and is NOT_VERIFIED, never "absent".
        /// </summary>
        private static ResponseProbe Probe(IFirewallCommandRunner fw, string name, bool mustExist, System.Text.StringBuilder log) =>
            new(name, mustExist ? "regula există" : "regula nu există", () =>
            {
                var o = Run(fw, $"advfirewall firewall show rule name=\"{name}\"", log);
                return o.ExitCode switch
                {
                    0 => mustExist ? ResponseObservation.Holds("regula există") : ResponseObservation.Differs("regula există în continuare"),
                    1 => mustExist ? ResponseObservation.Differs("regula nu există") : ResponseObservation.Holds("regula nu există"),
                    _ => ResponseObservation.Unknown($"re-citirea a întors {o.ExitCode}: {o.Output}"),
                };
            });

        private static void Append(System.Text.StringBuilder log, string line) { lock (log) log.AppendLine(line); }

        private static CommandOutcome Run(IFirewallCommandRunner fw, string args, System.Text.StringBuilder log)
        {
            var o = fw.Netsh(args);
            lock (log)
            {
                log.AppendLine($"{fw.BackendName} {args} → {o.ExitCode}");
                if (o.Output.Length > 0) log.AppendLine(o.Output);
            }
            return o;
        }

        private static DefenseActionResult Result(string status, string message, System.Text.StringBuilder log, ResponseOutcome verification)
        {
            string details;
            lock (log) details = log.ToString() + verification.Evidence();
            return new()
            {
                Success = status == DefenseActionResult.Verified && verification.Status == ResponseStatus.Verified,
                Status = status,
                Message = message,
                ExecutionDetails = details,
                Verification = verification,
            };
        }
    }

    /// <summary>The unclassified edition's <see cref="IHostDefense"/>.</summary>
    public sealed class HostDefense : IHostDefense
    {
        [ControlledResponse("delegates to SystemDefenseExecutionService.IsolateHostFromNetwork")]
        public DefenseActionResult IsolateHostFromNetwork() => SystemDefenseExecutionService.IsolateHostFromNetwork();
        [ControlledResponse("delegates to SystemDefenseExecutionService.RestoreNetworkAccess")]
        public DefenseActionResult RestoreNetworkAccess() => SystemDefenseExecutionService.RestoreNetworkAccess();
        [ControlledResponse("delegates to SystemDefenseExecutionService.BlockMaliciousIoC")]
        public DefenseActionResult BlockMaliciousIoC(string iocTarget) => SystemDefenseExecutionService.BlockMaliciousIoC(iocTarget);
    }
}
