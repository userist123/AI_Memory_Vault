using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>A security product (AV / EDR / Sysmon) the SECURITY-AGENT-STOPPED rule knows by name. Data row, see Data/security_agents.json.</summary>
public sealed class SecurityAgentRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<string> ServiceNames { get; set; } = [];
    public List<string> DisplayContains { get; set; } = [];
    public List<string> ProductContains { get; set; } = [];
}

public sealed class SecurityAgentCatalog
{
    public List<string> StoppedWords { get; set; } = [];
    public List<string> RunningWords { get; set; } = [];
    public List<string> DisabledWords { get; set; } = [];
    public List<SecurityAgentRow> Agents { get; set; } = [];

    /// <summary>The agent whose service key name or display name this is; null = not a known agent.</summary>
    public SecurityAgentRow? ByService(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return null;
        return Agents.FirstOrDefault(a => a.ServiceNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                                          a.DisplayContains.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The agent whose product name occurs in <paramref name="text"/> (MsiInstaller message); null = none.</summary>
    public SecurityAgentRow? ByProduct(string text) =>
        text.Length == 0 ? null : Agents.FirstOrDefault(a => a.ProductContains.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)));

    public bool IsStopped(string state) => StoppedWords.Any(w => state.Contains(w, StringComparison.OrdinalIgnoreCase));
    public bool IsRunning(string state) => RunningWords.Any(w => state.Contains(w, StringComparison.OrdinalIgnoreCase));
    public bool IsDisabled(string startType) => DisabledWords.Any(w => startType.Contains(w, StringComparison.OrdinalIgnoreCase));
}

public sealed class RemoteToolRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<string> Executables { get; set; } = [];
    public List<string> ServiceNames { get; set; } = [];
    public List<string> DisplayContains { get; set; } = [];
    public List<string> ProductContains { get; set; } = [];
    public List<string> LogMarkers { get; set; } = [];
}

public sealed class RemoteToolCatalog
{
    public List<RemoteToolRow> Tools { get; set; } = [];
}

public sealed class PrivilegedGroupLists
{
    public List<string> Names { get; set; } = [];
    public List<string> Sids { get; set; } = [];
    public List<string> DomainRids { get; set; } = [];
    public List<string> RemoteAccessGroups { get; set; } = [];

    /// <summary>True when the group (by name or SID) is one that grants administration or remote logon.</summary>
    public bool IsPrivileged(string groupName, string groupSid)
    {
        if (groupName.Length > 0 && Names.Any(n => n.Equals(groupName.Trim(), StringComparison.OrdinalIgnoreCase))) return true;
        if (groupSid.Length == 0) return false;
        if (Sids.Any(s => s.Equals(groupSid, StringComparison.OrdinalIgnoreCase))) return true;
        return groupSid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && DomainRids.Any(r => groupSid.EndsWith("-" + r, StringComparison.Ordinal));
    }
}

public sealed class DnsLists
{
    public List<string> IgnoredSuffixes { get; set; } = [];
    public List<string> BenignSuffixes { get; set; } = [];
    public List<string> HighInterestLolbins { get; set; } = [];
}

public sealed class VssCommandPattern
{
    public string Kind { get; set; } = "delete";
    public string Tool { get; set; } = "";
    public string Regex { get; set; } = "";
}

public sealed class VssLists
{
    public List<VssCommandPattern> CommandPatterns { get; set; } = [];
    public List<string> ImpactMitreIds { get; set; } = [];
    public List<string> ImpactCategories { get; set; } = [];
    public List<string> ImpactDetectionKeywords { get; set; } = [];
}

public sealed class RuleLists
{
    public PrivilegedGroupLists PrivilegedGroups { get; set; } = new();
    public List<string> ExecutableExtensions { get; set; } = [];
    public List<string> SharePipesOfInterest { get; set; } = [];
    public DnsLists Dns { get; set; } = new();
    public VssLists Vss { get; set; } = new();
}

/// <summary>
/// The data the WP11 rules read: known security agents, remote-access tools and shared lists. Loaded from JSON embedded in this assembly
/// (<c>Analysis/Data/*.json</c>); a caller or a test can pass its own to change what the rules know without touching code.
/// </summary>
public sealed class Wp11Data
{
    public required SecurityAgentCatalog Agents { get; init; }
    public required RemoteToolCatalog Tools { get; init; }
    public required RuleLists Lists { get; init; }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static Wp11Data FromJson(string agentsJson, string toolsJson, string listsJson) => new()
    {
        Agents = JsonSerializer.Deserialize<SecurityAgentCatalog>(agentsJson, Json) ?? new(),
        Tools = JsonSerializer.Deserialize<RemoteToolCatalog>(toolsJson, Json) ?? new(),
        Lists = JsonSerializer.Deserialize<RuleLists>(listsJson, Json) ?? new(),
    };

    private static readonly Lazy<Wp11Data> Shipped = new(() => FromJson(Read("security_agents.json"), Read("remote_access_tools.json"), Read("rule_lists.json")));

    /// <summary>The data shipped with the application.</summary>
    public static Wp11Data Default => Shipped.Value;

    private static string Read(string file)
    {
        var asm = typeof(Wp11Data).Assembly;
        using var s = asm.GetManifestResourceStream("LogAnalyzer.Dfir.Analysis.Data." + file)
                      ?? throw new InvalidOperationException($"Resursa încorporată {file} lipsește din {asm.GetName().Name}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

/// <summary>Tunable thresholds of the WP11 rules (defaults are the documented ones).</summary>
public sealed record Wp11Options
{
    /// <summary>DNS-RARE-DOMAIN: a domain seen at most this many times in the whole case counts as rare.</summary>
    public int DnsRareMaxCount { get; init; } = 2;
    /// <summary>Window in which another High finding counts as corroboration of a security-agent stop, and a service restart cancels it.</summary>
    public TimeSpan CorroborationWindow { get; init; } = TimeSpan.FromMinutes(60);
    public TimeSpan RestartWindow { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>Window in which a mass-modification / impact finding already in the case raises VSS-SNAPSHOT-DELETED to High.</summary>
    public TimeSpan ImpactWindow { get; init; } = TimeSpan.FromHours(24);
    /// <summary>Window in which two records (4624 and wsmprovhost start, 5145 and 7045) are taken to belong together.</summary>
    public TimeSpan PairWindow { get; init; } = TimeSpan.FromMinutes(10);
}
