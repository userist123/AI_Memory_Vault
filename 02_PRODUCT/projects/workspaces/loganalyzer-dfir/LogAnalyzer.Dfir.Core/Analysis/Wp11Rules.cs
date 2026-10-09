using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// WP11 Tier 1 rules over the unified timeline: SMB shares, account lifecycle, remote administration, security-agent stops, snapshot
/// deletion, DNS and remote-access tools. Each theme lives in its own partial file; every rule states what the event shows and
/// what it does not ("created is not used", "present is not executed", "one event is not an incident"). Severity follows the spec;
/// software approved in the procedure profile is Info with "aprobat în profil".
/// </summary>
public static partial class Wp11Rules
{
    /// <param name="events">The unified timeline.</param>
    /// <param name="existing">Findings already produced in this run (corroboration only; never modified).</param>
    /// <param name="nextId">Finding id generator of the run.</param>
    /// <param name="profile">The procedure profile, or null when none is defined.</param>
    public static List<Finding> Run(IReadOnlyList<TimelineEvent> events, IReadOnlyList<Finding> existing, Func<string> nextId,
        ProcedureProfile? profile = null, Wp11Data? data = null, Wp11Options? options = null)
    {
        var c = new Ctx(events, existing, nextId, profile, data ?? Wp11Data.Default, options ?? new Wp11Options());
        var found = new List<Finding>();
        Smb(c, found);
        Accounts(c, found);
        Remote(c, found);
        Agents(c, found);
        Vss(c, found);
        Dns(c, found);
        Tools(c, found);
        return found;
    }

    /// <summary>What the themes read: the run's timeline, the findings already produced (corroboration only), the profile, the data lists and the thresholds.</summary>
    internal sealed class Ctx(IReadOnlyList<TimelineEvent> events, IReadOnlyList<Finding> existing, Func<string> nextId, ProcedureProfile? profile, Wp11Data data, Wp11Options options)
    {
        public IReadOnlyList<TimelineEvent> Events { get; } = events;
        public IReadOnlyList<Finding> Existing { get; } = existing;
        public Func<string> NextId { get; } = nextId;
        public ProcedureProfile? Profile { get; } = profile;
        public Wp11Data Data { get; } = data;
        public Wp11Options Options { get; } = options;
    }

    // ---- helpers shared by the themes ----

    internal static string F(TimelineEvent e, string key) => e.Fields.TryGetValue(key, out var v) ? v : "";

    /// <summary>First non-empty value among the given field names.</summary>
    internal static string F(TimelineEvent e, params string[] keys)
    {
        foreach (var k in keys) if (e.Fields.TryGetValue(k, out var v) && v.Length > 0) return v;
        return "";
    }

    internal static bool Ev(TimelineEvent e, string channel, params int[] ids) =>
        e.Source.Equals("EventLog:" + channel, StringComparison.OrdinalIgnoreCase) && int.TryParse(e.EventId, out var id) && ids.Contains(id);

    internal const string Sysmon = "Microsoft-Windows-Sysmon/Operational";

    internal static EvidenceRef Ref(TimelineEvent e, string description) => new(e.EvidenceId, e.Locator, description);

    internal static DateTimeOffset? T(TimelineEvent e) => e.Time.Utc;

    internal static string Time(DateTimeOffset? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm") + " UTC" : "oră necunoscută";

    internal static string Join(IEnumerable<string> items, int max = 10)
    {
        var l = items.Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return string.Join(", ", l.Take(max)) + (l.Count > max ? $" … (+{l.Count - max})" : "");
    }

    /// <summary>"DOMAIN\user", or just the user when there is no domain.</summary>
    internal static string Account(string name, string domain) =>
        domain.Length > 0 && domain != "-" && name.Length > 0 ? domain + "\\" + name : name;

    internal static bool IsMachineAccount(string name) => name.EndsWith('$');

    /// <summary>True when the address names another host: not empty, not "-", not loopback.</summary>
    internal static bool IsRemoteAddress(string ip)
    {
        var s = IpClassifier.Classify(ip.Trim());
        return s is not (IpScope.Invalid or IpScope.Loopback or IpScope.Unspecified);
    }

    /// <summary>A process start: Security 4688 or Sysmon 1. Image path, command line and the (decimal) process id when known.</summary>
    internal readonly record struct ProcStart(TimelineEvent Event, string Image, string CommandLine, int? Pid, string LogonId, string User);

    internal static IEnumerable<ProcStart> ProcessStarts(IEnumerable<TimelineEvent> events)
    {
        foreach (var e in events)
        {
            if (Ev(e, "Security", 4688))
                yield return new(e, F(e, "NewProcessName"), F(e, "CommandLine"), ParsePid(F(e, "NewProcessId")), F(e, "SubjectLogonId"), F(e, "SubjectUserName"));
            else if (Ev(e, Sysmon, 1))
                yield return new(e, F(e, "Image"), F(e, "CommandLine"), ParsePid(F(e, "ProcessId")), F(e, "LogonId"), F(e, "User"));
        }
    }

    private static int? ParsePid(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var h) ? h : null;
        return int.TryParse(s, out var d) ? d : null;
    }

    /// <summary>Everything a row says as one lowercase-insensitive blob (fields, rendered message, summary), for product-name searches.</summary>
    internal static string AllText(TimelineEvent e) =>
        e.Summary + " " + string.Join(" ", e.Fields.Where(kv => !kv.Key.StartsWith("_x", StringComparison.Ordinal)).Select(kv => kv.Value));

    internal static Severity Max(Severity a, Severity b) => a >= b ? a : b;
}
