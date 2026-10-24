using Xunit;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Synthetic timeline rows for the WP11 rule tests.</summary>
internal static class W11
{
    public static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private static int _rec;

    public static TimelineEvent Ev(string channel, int id, double minute, string provider = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = "EventLog:" + channel, EventId = id.ToString(), Provider = provider,
        EvidenceId = "EV-1", Locator = $"EventRecordID={Interlocked.Increment(ref _rec)}", Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    public static TimelineEvent Sec(int id, double minute, params (string K, string V)[] f) => Ev("Security", id, minute, "Microsoft-Windows-Security-Auditing", f);
    public static TimelineEvent Sys(int id, double minute, string provider, params (string K, string V)[] f) => Ev("System", id, minute, provider, f);
    public static TimelineEvent Sysmon(int id, double minute, params (string K, string V)[] f) => Ev("Microsoft-Windows-Sysmon/Operational", id, minute, "Microsoft-Windows-Sysmon", f);

    /// <summary>A non-EVTX row (Prefetch, BAM, Amcache, ...).</summary>
    public static TimelineEvent Row(string source, double minute, string process = "", string path = "", string hash = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = source, EvidenceId = "EV-2",
        Locator = $"row={Interlocked.Increment(ref _rec)}", Summary = "t", Process = process, Path = path, Hash = hash,
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    public static List<Finding> Run(IEnumerable<TimelineEvent> events, ProcedureProfile? profile = null, IEnumerable<Finding>? existing = null, Wp11Data? data = null, Wp11Options? options = null)
    {
        int n = 0;
        return Wp11Rules.Run(events.ToList(), (existing ?? []).ToList(), () => $"F-{++n:D4}", profile, data, options);
    }

    public static Finding One(List<Finding> found, string ruleId) => Assert.Single(found, f => f.RuleId == ruleId);

    public static Finding Other(string ruleId, Severity s, double minute, string category = "", string mitre = "", string description = "x") => new()
    {
        FindingId = "F-9" + Math.Abs(ruleId.GetHashCode() % 100), RuleId = ruleId, Title = ruleId, Severity = s, Category = category, MitreTechniqueId = mitre,
        Classification = Classification.Direct, FirstSeenUtc = new DateTimeOffset(T0.AddMinutes(minute), TimeSpan.Zero), Description = description,
    };

    public static ProcedureProfile ProfileWithSoftware(params (string Name, string Publisher, string Path, string Sha)[] rows) => new()
    {
        ApprovedSoftware = rows.Select(r => new ApprovedSoftwareRow { Name = r.Name, Publisher = r.Publisher, PathPattern = r.Path, Sha256 = r.Sha }).ToList(),
    };
}
