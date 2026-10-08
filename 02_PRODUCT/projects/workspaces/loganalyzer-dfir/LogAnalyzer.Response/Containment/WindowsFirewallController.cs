using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Containment;

/// <summary>
/// Windows Defender Firewall through its COM API (HNetCfg.FwPolicy2). A program block rule cuts only that executable's
/// traffic, on every profile; the rest of the station keeps its connectivity. Requires administrator rights.
/// </summary>
public sealed class WindowsFirewallController : IFirewallController
{
    public const string RulePrefix = "LogAnalyzer-Containment-";
    private const int NET_FW_ACTION_BLOCK = 0, NET_FW_PROFILE2_ALL = 0x7FFFFFFF, NET_FW_IP_PROTOCOL_ANY = 256;

    public void AddProgramBlockRule(string name, string programPath, FirewallDirection direction, string description)
    {
        if (!name.StartsWith(RulePrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Regula trebuie să înceapă cu {RulePrefix}.", nameof(name));
        if (!Path.IsPathFullyQualified(programPath))
            throw new ArgumentException("Calea programului trebuie să fie absolută.", nameof(programPath));

        dynamic policy = CreatePolicy();
        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
        try
        {
            rule.Name = name;
            rule.Description = description;
            rule.ApplicationName = programPath;
            rule.Protocol = NET_FW_IP_PROTOCOL_ANY;
            rule.Direction = (int)direction;
            rule.Action = NET_FW_ACTION_BLOCK;
            rule.Profiles = NET_FW_PROFILE2_ALL;
            rule.Grouping = "LogAnalyzer Containment";
            rule.Enabled = true;
            RemoveRule(name); // idempotent: re-containing the same program replaces its rule
            policy.Rules.Add(rule);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            throw new UnauthorizedAccessException("Pentru izolarea unui program sunt necesare drepturi de administrator.", ex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(rule);
            Marshal.FinalReleaseComObject(policy);
        }
    }

    public bool RemoveRule(string name)
    {
        if (!name.StartsWith(RulePrefix, StringComparison.Ordinal)) return false;
        dynamic policy = CreatePolicy();
        try
        {
            bool removed = false;
            // A name can match several rules (inbound + outbound share none here, but Remove deletes one per call).
            while (Exists(policy, name))
            {
                policy.Rules.Remove(name);
                removed = true;
            }
            return removed;
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            throw new UnauthorizedAccessException("Pentru ridicarea izolării sunt necesare drepturi de administrator.", ex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(policy);
        }
    }

    public IReadOnlyList<FirewallRuleInfo> ListContainmentRules()
    {
        var list = new List<FirewallRuleInfo>();
        dynamic policy = CreatePolicy();
        try
        {
            foreach (dynamic r in policy.Rules)
            {
                string n = r.Name ?? "";
                if (n.StartsWith(RulePrefix, StringComparison.Ordinal))
                    list.Add(new FirewallRuleInfo(n, (string?)r.ApplicationName ?? "", (FirewallDirection)(int)r.Direction, (bool)r.Enabled, (string?)r.Description ?? ""));
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(policy);
        }
        return list;
    }

    private static bool Exists(dynamic policy, string name)
    {
        try
        {
            dynamic r = policy.Rules.Item(name);
            return r is not null;
        }
        catch (COMException)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static dynamic CreatePolicy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
}
