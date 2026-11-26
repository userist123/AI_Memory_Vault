using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// The account name the case's own events give a SID (some blocks, e.g. PowerShell 4104, carry only the SID): 4720/4722/4738 TargetSid, 4624 TargetUserSid,
/// 4688/4672 SubjectUserSid. Used before a plain sentence is told, so a raw SID never reaches the reader.
/// </summary>
public static class AccountResolver
{
    private static readonly Regex SidShape = new(@"^S-1-\d+(?:-\d+)+$", RegexOptions.CultureInvariant);

    public static bool IsSid(string s) => SidShape.IsMatch((s ?? "").Trim());

    private static readonly (string Channel, int[] Ids, string SidKey, string NameKey)[] Sources =
    [
        ("EventLog:Security", [4720, 4722, 4738], "TargetSid", "TargetUserName"),
        ("EventLog:Security", [4624], "TargetUserSid", "TargetUserName"),
        ("EventLog:Security", [4688, 4672], "SubjectUserSid", "SubjectUserName"),
    ];

    /// <summary>
    /// A resolver over the events: SID → account name, or null when no event names it. Only account SIDs (S-1-5-21-…) are resolved: SYSTEM, LOCAL / NETWORK
    /// SERVICE and the built-in groups are the same on every computer, so another computer's event could name them after its own account (round-2 review:
    /// S-1-5-18 became „LAB-WS01$” on a WS02 finding). A SID that the events give two different names (cloned computers sharing a machine SID) is not
    /// resolved either: the sentence then says the account is known only by its identifier, never the wrong person.
    /// </summary>
    public static Func<string, string?> FromEvents(IEnumerable<TimelineEvent> events)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events)
        {
            if (!int.TryParse(e.EventId, out var id)) continue;
            foreach (var (channel, ids, sidKey, nameKey) in Sources)
            {
                if (!e.Source.Equals(channel, StringComparison.OrdinalIgnoreCase) || !ids.Contains(id)) continue;
                if (e.Fields.TryGetValue(sidKey, out var sid) && IsAccountSid(sid) && e.Fields.TryGetValue(nameKey, out var name) && name.Length > 0 && name != "-")
                {
                    if (!map.TryGetValue(sid.Trim(), out var names)) map[sid.Trim()] = names = new(StringComparer.OrdinalIgnoreCase);
                    names.Add(name.Trim());
                }
            }
        }
        return sid => IsAccountSid(sid) && map.TryGetValue(sid.Trim(), out var names) && names.Count == 1 ? names.First() : null;
    }

    /// <summary>An account's own SID (domain or local machine: S-1-5-21-…), not a well-known one shared by every computer.</summary>
    public static bool IsAccountSid(string? s) => s is not null && IsSid(s) && s.Trim().StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
}