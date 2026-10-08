using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Infrastructure.Services
{
    /// <summary>
    /// Production backend of <see cref="SystemDefenseExecutionService"/>: the same three rule operations through Windows Defender
    /// Firewall's COM API (HNetCfg.FwPolicy2), with no netsh.exe process, no PATH lookup and no parsing of localized output.
    /// It understands exactly the netsh-style rule descriptions the service builds (add / delete / show rule, dir=out,
    /// action=block, profile=any, optional remoteip=) and nothing else; anything different is refused, not guessed.
    /// Exit codes keep the netsh convention the service already relies on: 0 = done / rule exists, 1 = rule absent, 5 = access denied.
    /// </summary>
    public sealed class ComFirewallRunner : IFirewallCommandRunner
    {
        public string BackendName => "INetFwPolicy2";

        private static readonly Regex Cmd = new(
            "^advfirewall firewall (?<verb>add|delete|show) rule name=\"(?<name>[^\"]+)\"(?<rest>.*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex RemoteIp = new(@"\bremoteip=(?<ip>\S+)", RegexOptions.CultureInvariant);

        private const int NET_FW_ACTION_BLOCK = 0, NET_FW_PROFILE2_ALL = 0x7FFFFFFF, NET_FW_IP_PROTOCOL_ANY = 256, NET_FW_RULE_DIR_OUT = 2;

        public CommandOutcome Netsh(string arguments)
        {
            var m = Cmd.Match(arguments);
            if (!m.Success) return new CommandOutcome(-1, $"Comandă de firewall nerecunoscută; nu s-a executat nimic: {arguments}");
            string name = m.Groups["name"].Value, rest = m.Groups["rest"].Value;
            dynamic? policy = null;
            try
            {
                policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
                switch (m.Groups["verb"].Value)
                {
                    case "show":
                        return Exists(policy, name) ? new CommandOutcome(0, name) : new CommandOutcome(1, "Nicio regulă nu corespunde.");
                    case "delete":
                        if (!Exists(policy, name)) return new CommandOutcome(1, "Nicio regulă nu corespunde.");
                        while (Exists(policy, name)) policy.Rules.Remove(name);
                        return new CommandOutcome(0, "Șters.");
                    default:
                        if (!rest.Contains("dir=out") || !rest.Contains("action=block"))
                            return new CommandOutcome(-1, "Doar regulile de ieșire cu acțiunea block sunt acceptate.");
                        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
                        try
                        {
                            rule.Name = name;
                            rule.Description = "LogAnalyzer: regulă creată la cererea operatorului";
                            rule.Protocol = NET_FW_IP_PROTOCOL_ANY;
                            rule.Direction = NET_FW_RULE_DIR_OUT;
                            rule.Action = NET_FW_ACTION_BLOCK;
                            rule.Profiles = NET_FW_PROFILE2_ALL;
                            rule.Grouping = "LogAnalyzer Defense";
                            var ip = RemoteIp.Match(rest);
                            if (ip.Success) rule.RemoteAddresses = ip.Groups["ip"].Value;
                            rule.Enabled = true;
                            policy.Rules.Add(rule);
                        }
                        finally { Marshal.FinalReleaseComObject(rule); }
                        return new CommandOutcome(0, "Ok.");
                }
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
            {
                return new CommandOutcome(5, "Acces refuzat: sunt necesare drepturi de administrator.");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                return new CommandOutcome(-1, "Firewall COM indisponibil pe acest sistem: " + ex.Message);
            }
            finally
            {
                if (policy is not null) Marshal.FinalReleaseComObject(policy);
            }
        }

        private static bool Exists(dynamic policy, string name)
        {
            try { return policy.Rules.Item(name) is not null; }
            catch (COMException) { return false; }
            catch (System.IO.FileNotFoundException) { return false; }
        }
    }
}
