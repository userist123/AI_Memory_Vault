using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Containment;

public enum FirewallDirection { Inbound = 1, Outbound = 2 }

public sealed record FirewallRuleInfo(string Name, string ProgramPath, FirewallDirection Direction, bool Enabled, string Description);

/// <summary>Per-program firewall rules. Only rules whose name starts with <see cref="RulePrefix"/> are ever removed.</summary>
public interface IFirewallController
{
    void AddProgramBlockRule(string name, string programPath, FirewallDirection direction, string description);
    bool RemoveRule(string name);
    IReadOnlyList<FirewallRuleInfo> ListContainmentRules();
}
