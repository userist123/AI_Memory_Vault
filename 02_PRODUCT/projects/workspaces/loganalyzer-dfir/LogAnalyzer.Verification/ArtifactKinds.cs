using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>A supporting evidence reference resolved against the case: its evidence item, its timeline rows and the artifact kind it represents.</summary>
public sealed record ResolvedRef(EvidenceRef Ref, EvidenceItem? Item, IReadOnlyList<TimelineRow> Rows, string Family, string Kind, bool ResolvedFromRow)
{
    public bool IsResolved => Family != Unresolved;
    public const string Unresolved = "?";
}

/// <summary>
/// What kind of artifact a piece of evidence is, and which kinds a claim needs. Read from the timeline rows and the evidence index only;
/// no parser is called. "Family" is the artifact source (Prefetch, BAM, EventLog:Security…): two records of one family are not independent.
/// </summary>
public static class ArtifactKinds
{
    public const string SecurityLog = "EventLog:Security";

    public static ResolvedRef Resolve(EvidenceRef r, CaseFacts facts)
    {
        facts.Evidence.TryGetValue(r.EvidenceId, out var item);
        var rows = facts.RowsByRef.GetValueOrDefault((r.EvidenceId, r.Locator)) ?? [];
        if (rows.Count > 0)
        {
            var s = rows[0];
            var kind = s.Source.StartsWith("EventLog:", StringComparison.Ordinal) && s.EventId.Length > 0 ? $"{s.Source} {s.EventId}" : s.Source;
            return new ResolvedRef(r, item, rows, s.Source, kind, true);
        }
        if (item is null) return new ResolvedRef(r, null, [], ResolvedRef.Unresolved, ResolvedRef.Unresolved, false);
        var fallback = item.SourceType switch
        {
            "prefetch" => "Prefetch",
            "amcache" => "Amcache",
            "evtx" => "EventLog",
            "live_snapshot" => "Live:" + (r.Locator.IndexOf('[') is var i and > 0 ? r.Locator[..i] : "Snapshot"),
            "" => ResolvedRef.Unresolved,
            var other => other,
        };
        return new ResolvedRef(r, item, [], fallback, fallback, false);
    }

    /// <summary>An artifact that records that something ran. Amcache, ShimCache, LNK and Jump Lists are presence, not execution (AntiOverclaim).</summary>
    public static bool IsExecution(ResolvedRef r)
    {
        if (!r.IsResolved) return false;
        if (r.ResolvedFromRow)
        {
            var row = r.Rows[0];
            if (AntiOverclaim.SemanticForArtifact(row.Source) == SemanticType.Execution) return true;
            if (row.Source == SecurityLog && row.EventId == "4688") return true;
            if (row.Source.Contains("Sysmon", StringComparison.OrdinalIgnoreCase) && row.EventId == "1") return true;
            return false;
        }
        return r.Family is "Prefetch" or "Live:Processes";
    }

    /// <summary>An artifact that records a setting, or a log entry about one. A presence/execution artifact alone does not show configuration.</summary>
    public static bool IsConfiguration(ResolvedRef r)
    {
        if (!r.IsResolved) return false;
        if (AntiOverclaim.SemanticForArtifact(r.Family) == SemanticType.Configuration) return true;
        return r.Family.StartsWith("EventLog", StringComparison.Ordinal) || r.Family is "Live:Services" or "Live:Tasks" or "Live:Autoruns" or "task_xml";
    }

    /// <summary>Kinds an execution claim can rest on, for the "missing" list.</summary>
    public static readonly string[] ExecutionKinds = ["Prefetch", "BAM", "UserAssist", "EventLog:Security 4688", "Sysmon 1 (EventLog:Microsoft-Windows-Sysmon/Operational)", "Live:Processes"];

    public static readonly string ConfigurationKindsText = "ScheduledTask, Service, RunKey, Winlogon, IFEO, SystemConfig, fotografia live (servicii, taskuri, autorun) sau o înregistrare de jurnal despre ele";

    /// <summary>Artifact kinds a claim of this type would expect to find in the case, and whether the case contains them. (label, present)</summary>
    public static IEnumerable<(string Label, bool Present)> Expected(SemanticType t, CaseFacts f)
    {
        bool Src(string source) => f.Timeline?.Any(r => r.Source == source) == true;
        bool Ev(string source, string id) => f.Timeline?.Any(r => r.Source == source && r.EventId == id) == true;
        bool Type(string type) => f.Evidence.Values.Any(e => e.SourceType.Equals(type, StringComparison.OrdinalIgnoreCase));
        bool Sysmon() => f.Timeline?.Any(r => r.Source.Contains("Sysmon", StringComparison.OrdinalIgnoreCase) && r.EventId == "1") == true;
        switch (t)
        {
            case SemanticType.Execution:
            case SemanticType.Presence:
            case SemanticType.Configuration:
                yield return ("Prefetch", Src("Prefetch") || Type("prefetch"));
                yield return ("BAM", Src("BAM"));
                yield return ("EventLog:Security 4688", Ev(SecurityLog, "4688"));
                yield return ("Sysmon 1", Sysmon());
                break;
        }
    }
}
