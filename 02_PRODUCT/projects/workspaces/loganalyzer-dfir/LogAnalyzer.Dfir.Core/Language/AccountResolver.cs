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

    /// <summary>A resolver over the events: SID → account name, or null when no event names it.</summary>
    public static Func<string, string?> FromEvents(IEnumerable<TimelineEvent> events)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events)
        {
            if (!int.TryParse(e.EventId, out var id)) continue;
            foreach (var (channel, ids, sidKey, nameKey) in Sources)
            {
                if (!e.Source.Equals(channel, StringComparison.OrdinalIgnoreCase) || !ids.Contains(id)) continue;
                if (e.Fields.TryGetValue(sidKey, out var sid) && IsSid(sid) && e.Fields.TryGetValue(nameKey, out var name) && name.Length > 0 && name != "-")
                    map.TryAdd(sid.Trim(), name);
            }
        }
        return sid => map.GetValueOrDefault((sid ?? "").Trim());
    }
}
