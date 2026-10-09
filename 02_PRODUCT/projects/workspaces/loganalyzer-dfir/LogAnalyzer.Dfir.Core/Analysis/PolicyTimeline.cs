using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Analysis;

[JsonConverter(typeof(SpecEnumConverter<LevelState>))]
public enum LevelState { Observed, NotObserved, Unknown }

/// <summary>Behaviour = the observable effect of a change in the same evidence. NotEvaluable always carries the reason.</summary>
[JsonConverter(typeof(SpecEnumConverter<BehaviorState>))]
public enum BehaviorState { Observed, NotObserved, NotEvaluable }

/// <summary>One policy change event (or a 5136 before/after pair) reduced to who, when, target, attribute, old and new value.</summary>
public sealed class PolicyChange
{
    public string ChangeId { get; set; } = "";
    public DateTimeOffset TimeUtc { get; init; }
    /// <summary>DS_MODIFIED (5136), DS_CREATED (5137), DS_DELETED (5141), DS_MOVED (5139), DOMAIN_POLICY (4739), AUDIT_POLICY (4719), OBJECT_SACL (4907).</summary>
    public string Kind { get; init; } = "";
    public string EventId { get; init; } = "";
    public string Who { get; init; } = "";
    public string Host { get; init; } = "";
    /// <summary>GPO, OU, Domain, Site, Host or Object.</summary>
    public string TargetKind { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string Attribute { get; init; } = "";
    public string OldValue { get; init; } = "";
    public string NewValue { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>GPO GUIDs this change touches (the GPO itself, or the GPOs added to / removed from / changed in a gPLink).</summary>
    public List<string> RelatedGpos { get; init; } = [];
    /// <summary>4719 only: lifecycle assessment (WP15a classifier).</summary>
    public string Lifecycle { get; init; } = "";
    public Severity Severity { get; init; } = Severity.Info;
    public List<EvidenceRef> Evidence { get; init; } = [];
}

/// <summary>An endpoint record from Microsoft-Windows-GroupPolicy/Operational.</summary>
public sealed class PolicyApplication
{
    public DateTimeOffset TimeUtc { get; init; }
    public string Host { get; init; } = "";
    public string EventId { get; init; } = "";
    /// <summary>start, end, extension, extension_time, applied_list, filtered_list, error.</summary>
    public string Phase { get; init; } = "";
    public List<string> GpoIds { get; init; } = [];
    public List<string> GpoNames { get; init; } = [];
    public bool Error { get; init; }
    public string Detail { get; init; } = "";
    public EvidenceRef? Evidence { get; init; }
}

public sealed class FirstApplication
{
    public string Host { get; init; } = "";
    public DateTimeOffset TimeUtc { get; init; }
    public EvidenceRef? Evidence { get; init; }
}

/// <summary>BEFORE (Change.OldValue) → CHANGE → AFTER (Change.NewValue) → APPLICATION → BEHAVIOR.</summary>
public sealed class PolicyChain
{
    public PolicyChange Change { get; init; } = new();
    [JsonConverter(typeof(SpecEnumConverter<LevelState>))] public LevelState ApplicationState { get; init; } = LevelState.Unknown;
    public string ApplicationReason { get; init; } = "";
    public List<FirstApplication> FirstApplications { get; init; } = [];
    public BehaviorState Behavior { get; init; } = BehaviorState.NotEvaluable;
    public string BehaviorReason { get; init; } = "";
    public List<EvidenceRef> BehaviorEvidence { get; init; } = [];
    public static string BehaviorText(BehaviorState b) => b switch { BehaviorState.Observed => "observat", BehaviorState.NotObserved => "neobservat", _ => "neevaluabil" };
}

/// <summary>A setting of the expected policy (from the existing Policy import; this class does not copy the policy).</summary>
public sealed record ExpectedSetting(string Id, string Title, string Type, string Name, string DesiredDisplay, string DesiredValue, string Source, string Key = "")
{
    /// <summary>Key into <see cref="PolicyTimelineOptions.Effective"/>, e.g. "audit:Process Creation".</summary>
    [JsonIgnore] public string EffectiveKey => Key.Length > 0 ? Key : $"{Type}:{Name}";

    public static List<ExpectedSetting> From(PolicyDocument p) => p.Controls.Select(c => new ExpectedSetting(c.Id, c.Title, c.Setting.Type, c.Setting.Name,
        c.Desired.Display, c.Desired.Operator == "equals" ? c.Desired.Values[0] : "", $"{p.Id} {p.Version}", c.Setting.Display)).ToList();
}

public sealed class LevelEvidence
{
    [JsonConverter(typeof(SpecEnumConverter<LevelState>))] public LevelState State { get; init; } = LevelState.Unknown;
    public string Reason { get; init; } = "";
    public List<EvidenceRef> Evidence { get; init; } = [];
}

/// <summary>Per expected setting: configured → applied → enforced → observed, each OBSERVED / NOT_OBSERVED / UNKNOWN.</summary>
public sealed class SettingLevels
{
    public ExpectedSetting Setting { get; init; } = new("", "", "", "", "", "", "");
    public LevelEvidence Configured { get; init; } = new();
    public LevelEvidence Applied { get; init; } = new();
    public LevelEvidence Enforced { get; init; } = new();
    public LevelEvidence Observed { get; init; } = new();
    public static readonly string[] Names = ["configured", "applied", "enforced", "observed"];
    public LevelEvidence Level(string name) => name switch
    {
        "configured" => Configured, "applied" => Applied, "enforced" => Enforced, "observed" => Observed,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}

public sealed class ControlGap
{
    public string SettingId { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>The first of applied, enforced, observed that is NOT_OBSERVED.</summary>
    public string BrokenLevel { get; init; } = "";
    public string Reason { get; init; } = "";
    public Severity Severity { get; init; } = Severity.Medium;
    public DateTimeOffset? WindowStart { get; init; }
    public DateTimeOffset? WindowEnd { get; init; }
    public int HighFindingsInWindow { get; init; }
    public bool DisablesAuditing { get; init; }
    public string Summary { get; init; } = "";
    public List<EvidenceRef> Evidence { get; init; } = [];
    public List<string> MissingEvidence { get; init; } = [];
}

public sealed class PolicyTimelineOptions
{
    public IReadOnlyList<ExpectedSetting> Expected { get; init; } = [];
    /// <summary>Why the expected policy is not (fully) defined, e.g. a policy file that could not be read; shown with the "nedefinit" note.</summary>
    public IReadOnlyList<string> ExpectedIssues { get; init; } = [];
    public LogMaintenancePolicy? Maintenance { get; init; }
    /// <summary>Effective state where collected (AuditQuerySystemPolicy / StationFacts), by <see cref="ExpectedSetting.EffectiveKey"/>. Null in a case made of EVTX alone.</summary>
    public IReadOnlyDictionary<string, string>? Effective { get; init; }
    public IReadOnlyList<DateTimeOffset> HighFindingTimesUtc { get; init; } = [];
}

public sealed class PolicyTimelineResult
{
    public List<PolicyChange> Changes { get; init; } = [];
    public List<PolicyApplication> Applications { get; init; } = [];
    public List<PolicyChain> Chains { get; init; } = [];
    public List<SettingLevels> Levels { get; init; } = [];
    public List<ControlGap> Gaps { get; init; } = [];
    public string ExpectedNote { get; init; } = "";
    public TimeManipulationResult Time { get; init; } = new();
    public List<ManipulatedWindow> TimeWindows => Time.Windows;
    public List<string> Limitations { get; init; } = [];

    public bool HasContent => Changes.Count > 0 || Applications.Count > 0 || Levels.Count > 0 || Time.Windows.Count > 0 || Time.Jumps.Count > 0 || Time.ZoneChanges.Count > 0;

    /// <summary>One POLICY-CONTROL-GAP finding per gap. Evidence is the proof the level was evaluated on; never a verdict about intent.</summary>
    public List<Finding> ToFindings(Func<string> nextId) => Gaps.Select(g => new Finding
    {
        FindingId = nextId(), RuleId = "POLICY-CONTROL-GAP",
        Title = $"Decalaj de control: „{g.Title}” este configurat, dar nivelul „{g.BrokenLevel}” nu este observat",
        Severity = g.Severity, Category = "Control gap", Classification = Classification.Correlated,
        Confidence = g.Evidence.Count > 0 ? Confidence.Medium : Confidence.Low,
        FirstSeenUtc = g.WindowStart, LastSeenUtc = g.WindowEnd,
        Description = g.Summary,
        ClassificationReason = "Politica așteptată (configured) comparată cu dovezile de aplicare, de stare efectivă și de evenimente; fiecare nivel e OBSERVED / NOT_OBSERVED / UNKNOWN, iar un nivel UNKNOWN nu produce decalaj.",
        SupportingEvidence = g.Evidence.Take(30).ToList(),
        MissingEvidence = g.MissingEvidence,
        AlternativeExplanations =
        [
            "Întârziere de replicare (AD/SYSVOL) sau ciclu de reîmprospătare a politicii încă neterminat.",
            "Filtrare de securitate sau filtru WMI care exclude stația din GPO.",
            "Procesare loopback sau altă GPO cu precedență mai mare care suprascrie setarea.",
            "Stația era oprită sau offline (offline) în fereastra analizată și nu a procesat politica.",
            "Absența evenimentelor poate însemna absența activității auditate, nu absența auditului.",
        ],
        RecommendedNextSteps = ["Verificați rezultatul gpresult / RSoP pe stație și starea efectivă (auditpol /get).", "Verificați replicarea SYSVOL și filtrarea GPO pentru stația respectivă."],
    }).ToList();

    public string ToJson() => SchemaVersions.WithVersion(new
    {
        ExpectedNote,
        Changes,
        Applications = Applications.Take(PolicyTimeline.MaxApplicationsInJson).ToList(),
        ApplicationsTotal = Applications.Count,
        Chains,
        Levels,
        Gaps,
        TimeWindows = Time.Windows,
        ClockJumps = Time.Jumps,
        ZoneChanges = Time.ZoneChanges,
        RecordOrderInversions = Time.Inversions,
        Limitations,
    }, PolicyTimeline.SchemaVersion, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
}

/// <summary>
/// WP15b. Works from EVTX alone (both editions): Security 5136/5137/5139/5141/4739/4719/4907 and Microsoft-Windows-GroupPolicy/Operational. A level
/// that the evidence cannot show is UNKNOWN, never "conform" and never a gap; a behaviour that cannot be seen is NOT_EVALUABLE with the reason.
/// </summary>
public static class PolicyTimeline
{
    public const string SchemaVersion = SchemaVersions.PolicyTimeline;
    public const string GapRuleId = "POLICY-CONTROL-GAP";
    public const int MaxApplicationsInJson = 2000;
    private const string Security = "EventLog:Security";
    private const string GroupPolicy = "EventLog:Microsoft-Windows-GroupPolicy/Operational";
    /// <summary>Audit policy client-side extension (Audit Policy Configuration).</summary>
    public const string AuditCse = "{F3CCC681-B74C-4060-9F26-CD84525DCA2A}";
    /// <summary>Evidence of a stop/start needs the log to go on at least this long after the change.</summary>
    public static readonly TimeSpan MinObservation = TimeSpan.FromHours(1);
    /// <summary>Group Policy refresh: 90 min + up to 30 min offset on member computers; an audit GPO is expected to be applied within this plus margin.</summary>
    public static readonly TimeSpan RefreshObservation = TimeSpan.FromHours(2);
    public static readonly TimeSpan ApplicationWindow = TimeSpan.FromHours(24);

    private static readonly Regex GuidRx = new(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}", RegexOptions.Compiled);
    private static readonly Regex GpoRx = new(@"<GPO\s+ID=""(\{[0-9A-Fa-f\-]{36}\})""[^>]*>(?:\s*<Name>([^<]*)</Name>)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Parses gPLink values ("[LDAP://cn={GUID},cn=policies,...;0]") from event text; never opens a connection. The classified-edition
    // scanner allows exactly this literal in LogAnalyzer.Dfir.Core.dll (ClassifiedEditionBuildTests.ParsingOnlyLiterals).
    private static readonly Regex LinkRx = new(@"\[LDAP://([^;\]]*);(\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
    private static EvidenceRef Ref(TimelineEvent e, string d) => new(e.EvidenceId, e.Locator, d, e.SourceSha256);
    private static string Guid(string s) => GuidRx.Match(s) is { Success: true } m ? m.Value.ToUpperInvariant() : "";
    private static string Who(TimelineEvent e) => F(e, "SubjectUserName") is { Length: > 0 } u ? (F(e, "SubjectDomainName") is { Length: > 0 } d && d != "-" ? d + "\\" : "") + u : "necunoscut";
    private static bool Is(TimelineEvent e, string source, params string[] ids) => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && ids.Contains(e.EventId);
    private static bool SameHost(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- audit subcategory → events that show it works ----
    private sealed record AuditEffect(int[] Success, int[] Failure, bool FrequentSuccess);

    private static readonly Dictionary<string, AuditEffect> Effects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Process Creation"] = new([4688], [], true),
        ["Logon"] = new([4624], [4625], true),
        ["Logoff"] = new([4634, 4647], [], true),
        ["Special Logon"] = new([4672], [], true),
        ["Filtering Platform Connection"] = new([5156], [5157], true),
        ["File System"] = new([4663], [4663], false),
        ["Registry"] = new([4657], [4657], false),
        ["Credential Validation"] = new([4776], [4776], false),
        ["Security Group Management"] = new([4728, 4729, 4732, 4733, 4756, 4757], [], false),
        ["User Account Management"] = new([4720, 4722, 4725, 4726, 4738], [], false),
        ["Other Object Access Events"] = new([4698, 4699, 4702], [], false),
        ["Sensitive Privilege Use"] = new([4673, 4674], [4673], false),
        ["Audit Policy Change"] = new([4719], [], false),
    };

    internal static string SubcategoryName(string guidOrId)
    {
        var g = Guid(guidOrId);
        if (g.Length == 0) return guidOrId;
        foreach (var (name, guid) in AuditSubcategories.ByName)
            if (string.Equals("{" + guid.ToString() + "}", g, StringComparison.OrdinalIgnoreCase)) return name;
        return g;
    }

    /// <summary>
    /// The expected settings the procedure profile links to (the profile stores a path to a policy of the existing Policy import, never a copy). A link that cannot be
    /// read is reported in <paramref name="issues"/>, not dropped.
    /// </summary>
    public static List<ExpectedSetting> ExpectedFromProfile(LogAnalyzer.Dfir.Profile.ProcedureProfile? profile, List<string> issues)
    {
        var all = new List<ExpectedSetting>();
        foreach (var row in profile?.ExpectedPolicies ?? [])
        {
            if (string.IsNullOrWhiteSpace(row.PolicyPath)) continue;
            try
            {
                var doc = PolicyLoader.LoadFile(row.PolicyPath.Trim());
                if (row.Sha256.Length > 0 && !row.Sha256.Equals(doc.Sha256, StringComparison.OrdinalIgnoreCase))
                    issues.Add($"{row.PolicyPath}: SHA-256 diferit de cel din profil (politica s-a schimbat după legare)");
                all.AddRange(ExpectedSetting.From(doc));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { issues.Add($"{row.PolicyPath}: {ex.Message}"); }
        }
        return all;
    }

    public static PolicyTimelineResult Build(IReadOnlyList<TimelineEvent> events, PolicyTimelineOptions? options = null)
    {
        options ??= new();
        var timed = events.Where(e => e.Time.Utc is not null).OrderBy(e => e.Time.Utc).ToList();
        var changes = ExtractChanges(timed, options);
        var apps = ExtractApplications(timed);
        var security = timed.Where(e => e.Source.Equals(Security, StringComparison.OrdinalIgnoreCase)).ToList();
        var chains = changes.Select(c => Chain(c, apps, security)).ToList();
        var levels = options.Expected.Select(s => Levels(s, timed, apps, security, options)).ToList();
        var gaps = levels.Select(l => Gap(l, changes, apps, security, options)).Where(g => g is not null).Select(g => g!).ToList();
        string note = options.Expected.Count > 0 ? "" :
            "Politica așteptată: nedefinit (profilul de proceduri nu leagă nicio politică sau fișierul nu a putut fi citit); nivelurile configured / applied / enforced / observed nu se evaluează, iar asta nu înseamnă conform."
            + (options.ExpectedIssues.Count > 0 ? " Motiv: " + string.Join("; ", options.ExpectedIssues) : "");
        return new PolicyTimelineResult
        {
            Changes = changes, Applications = apps, Chains = chains, Levels = levels, Gaps = gaps, ExpectedNote = note,
            Time = TimeManipulation.Analyze(events),
            Limitations =
            [
                "5136 raportează atributele obiectului GPO din Active Directory (versionNumber, gPLink…), nu setările din SYSVOL: conținutul politicii nu e în EVTX.",
                "Aplicarea pe stații se vede doar în Microsoft-Windows-GroupPolicy/Operational colectat de la acele stații.",
                "Absența unui eveniment nu dovedește absența activității; comportamentul se declară neevaluabil când dovada lipsește.",
            ],
        };
    }

    // ---------------------------------------------------------------- 1. events → records

    private static List<PolicyChange> ExtractChanges(List<TimelineEvent> timed, PolicyTimelineOptions o)
    {
        var list = new List<PolicyChange>();
        var sec = timed.Where(e => e.Source.Equals(Security, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var g in sec.Where(e => e.EventId == "5136").GroupBy(e => (Dn: F(e, "ObjectDN").ToUpperInvariant(), Attr: F(e, "AttributeLDAPDisplayName").ToLowerInvariant(), Corr: F(e, "OpCorrelationID"),
                                                                           Subj: F(e, "SubjectUserName"), Sec: Corr(F(e, "OpCorrelationID")) ? 0 : e.Time.Utc!.Value.ToUnixTimeSeconds())))
        {
            var first = g.First();
            string dn = F(first, "ObjectDN"), attr = F(first, "AttributeLDAPDisplayName"), cls = F(first, "ObjectClass");
            var deleted = g.Where(e => F(e, "OperationType").Contains("14675")).Select(e => F(e, "AttributeValue")).ToList();
            var added = g.Where(e => F(e, "OperationType").Contains("14674")).Select(e => F(e, "AttributeValue")).ToList();
            string oldV = string.Join(" | ", deleted), newV = string.Join(" | ", added);
            var (kind, targetId) = Target(cls, dn, F(first, "ObjectGUID"));
            var related = new List<string>();
            string detail;
            if (attr.Equals("gPLink", StringComparison.OrdinalIgnoreCase)) (detail, related) = LinkDiff(oldV, newV);
            else
            {
                if (kind == "GPO" && targetId.Length > 0) related.Add(targetId);
                detail = $"{attr}: " + (deleted.Count > 0 ? oldV : "(fără valoare veche)") + " → " + (added.Count > 0 ? newV : "(fără valoare nouă)");
            }
            if (deleted.Count == 0) detail += "; evenimentul nu poartă valoarea veche (nu există eveniment %%14675 în pereche)";
            if (added.Count == 0) detail += "; evenimentul nu poartă valoarea nouă (nu există eveniment %%14674 în pereche)";
            list.Add(new PolicyChange
            {
                TimeUtc = g.Max(e => e.Time.Utc!.Value), Kind = "DS_MODIFIED", EventId = "5136", Who = Who(first), Host = first.Host, TargetKind = kind, TargetId = targetId, TargetName = dn,
                Attribute = attr, OldValue = oldV, NewValue = newV, Detail = detail, RelatedGpos = related,
                Evidence = g.Select(e => Ref(e, $"5136 {attr} {(F(e, "OperationType").Contains("14675") ? "valoare ștearsă" : "valoare adăugată")}")).ToList(),
            });
        }
        static bool Corr(string c) => c.Length > 0;

        foreach (var e in sec.Where(e => e.EventId is "5137" or "5141"))
        {
            var (kind, id) = Target(F(e, "ObjectClass"), F(e, "ObjectDN"), F(e, "ObjectGUID"));
            bool created = e.EventId == "5137";
            list.Add(new PolicyChange
            {
                TimeUtc = e.Time.Utc!.Value, Kind = created ? "DS_CREATED" : "DS_DELETED", EventId = e.EventId, Who = Who(e), Host = e.Host, TargetKind = kind, TargetId = id, TargetName = F(e, "ObjectDN"),
                OldValue = created ? "" : F(e, "ObjectDN"), NewValue = created ? F(e, "ObjectDN") : "", RelatedGpos = kind == "GPO" && id.Length > 0 ? [id] : [],
                Detail = $"{(created ? "creat" : "șters")} {F(e, "ObjectClass")} {F(e, "ObjectDN")}", Evidence = [Ref(e, created ? "obiect creat" : "obiect șters")],
            });
        }
        foreach (var e in sec.Where(e => e.EventId == "5139"))
        {
            var (kind, id) = Target(F(e, "ObjectClass"), F(e, "NewObjectDN"), F(e, "ObjectGUID"));
            list.Add(new PolicyChange
            {
                TimeUtc = e.Time.Utc!.Value, Kind = "DS_MOVED", EventId = "5139", Who = Who(e), Host = e.Host, TargetKind = kind, TargetId = id, TargetName = F(e, "NewObjectDN"),
                OldValue = F(e, "OldObjectDN"), NewValue = F(e, "NewObjectDN"), RelatedGpos = kind == "GPO" && id.Length > 0 ? [id] : [],
                Detail = $"mutat {F(e, "OldObjectDN")} → {F(e, "NewObjectDN")}", Evidence = [Ref(e, "obiect mutat")],
            });
        }
        foreach (var e in sec.Where(e => e.EventId == "4739"))
        {
            string[] skip = ["SubjectUserSid", "SubjectUserName", "SubjectDomainName", "SubjectLogonId", "DomainName", "DomainSid", "DomainPolicyChanged"];
            var attrs = e.Fields.Where(kv => !skip.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) && kv.Value.Length > 0 && kv.Value != "-").Select(kv => $"{kv.Key}={kv.Value}").ToList();
            list.Add(new PolicyChange
            {
                TimeUtc = e.Time.Utc!.Value, Kind = "DOMAIN_POLICY", EventId = "4739", Who = Who(e), Host = e.Host, TargetKind = "Domain", TargetId = F(e, "DomainName"), TargetName = F(e, "DomainName"),
                Attribute = "domain policy", NewValue = string.Join("; ", attrs), Detail = "politica de domeniu modificată; evenimentul nu poartă valorile vechi", Evidence = [Ref(e, "politică de domeniu modificată")],
            });
        }
        foreach (var e in sec.Where(e => e.EventId == "4719"))
        {
            string name = SubcategoryName(F(e, "SubcategoryGuid") is { Length: > 0 } sg ? sg : F(e, "SubcategoryId"));
            string codes = F(e, "AuditPolicyChanges");
            var parts = new List<string>();
            foreach (var (code, text) in new[] { ("%%8448", "Success eliminat"), ("%%8449", "Success adăugat"), ("%%8450", "Failure eliminat"), ("%%8451", "Failure adăugat") })
                if (codes.Contains(code, StringComparison.Ordinal)) parts.Add(text);
            var item = LogClearAssessment.AssessAuditPolicyChange(new AuditPolicyChangeEvent(e.Time.Utc!.Value, F(e, "SubjectUserName"), F(e, "SubjectDomainName"), F(e, "SubjectUserSid"), codes), o.Maintenance);
            list.Add(new PolicyChange
            {
                TimeUtc = e.Time.Utc!.Value, Kind = "AUDIT_POLICY", EventId = "4719", Who = Who(e), Host = e.Host, TargetKind = "Host", TargetId = e.Host, TargetName = e.Host,
                Attribute = name, NewValue = parts.Count > 0 ? string.Join(", ", parts) : codes, Detail = $"audit „{name}”: {(parts.Count > 0 ? string.Join(", ", parts) : codes)}; evenimentul nu poartă starea veche absolută",
                Lifecycle = item.Reason, Severity = item.Severity, Evidence = [Ref(e, "politică de audit modificată")],
            });
        }
        foreach (var e in sec.Where(e => e.EventId == "4907"))
            list.Add(new PolicyChange
            {
                TimeUtc = e.Time.Utc!.Value, Kind = "OBJECT_SACL", EventId = "4907", Who = Who(e), Host = e.Host, TargetKind = "Object", TargetId = F(e, "ObjectName"), TargetName = F(e, "ObjectName"),
                Attribute = "SACL", OldValue = F(e, "OldSd"), NewValue = F(e, "NewSd"), Detail = $"SACL schimbată pentru {F(e, "ObjectName")} de procesul {F(e, "ProcessName")}", Evidence = [Ref(e, "SACL modificată")],
            });

        list = list.OrderBy(c => c.TimeUtc).ThenBy(c => c.EventId, StringComparer.Ordinal).ToList();
        for (int i = 0; i < list.Count; i++) list[i].ChangeId = $"PC-{i + 1:D3}";
        return list;
    }

    private static (string Kind, string Id) Target(string objectClass, string dn, string objectGuid)
    {
        string kind = objectClass.ToLowerInvariant() switch
        {
            "grouppolicycontainer" => "GPO", "organizationalunit" => "OU", "domaindns" => "Domain", "site" => "Site", _ => "Object",
        };
        if (kind == "GPO") return (kind, Guid(dn) is { Length: > 0 } g ? g : objectGuid);
        return (kind, dn);
    }

    private static (string Detail, List<string> Related) LinkDiff(string oldV, string newV)
    {
        static Dictionary<string, string> Parse(string s) => LinkRx.Matches(s).Select(m => (Id: Guid(m.Groups[1].Value), Flags: m.Groups[2].Value)).Where(x => x.Id.Length > 0)
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Flags);
        static string FlagText(string f) => int.TryParse(f, out var n) ? string.Join(", ", new[] { (n & 1) != 0 ? "dezactivat" : "", (n & 2) != 0 ? "impus" : "" }.Where(x => x.Length > 0)) is { Length: > 0 } t ? t : "activ" : f;
        var o = Parse(oldV); var n = Parse(newV);
        var rel = new List<string>(); var lines = new List<string>();
        foreach (var (id, f) in n.Where(kv => !o.ContainsKey(kv.Key))) { rel.Add(id); lines.Add($"GPO {id} adăugat în legătură ({FlagText(f)})"); }
        foreach (var (id, f) in o.Where(kv => !n.ContainsKey(kv.Key))) { rel.Add(id); lines.Add($"GPO {id} eliminat din legătură (era {FlagText(f)})"); }
        foreach (var (id, f) in n.Where(kv => o.TryGetValue(kv.Key, out var of) && of != kv.Value)) { rel.Add(id); lines.Add($"GPO {id}: opțiuni schimbate {FlagText(o[id])} → {FlagText(f)}"); }
        return (lines.Count > 0 ? "gPLink: " + string.Join("; ", lines) : "gPLink modificat fără diferență la nivel de GPO (ordine sau text)", rel);
    }

    private static List<PolicyApplication> ExtractApplications(List<TimelineEvent> timed)
    {
        var list = new List<PolicyApplication>();
        foreach (var e in timed.Where(e => e.Source.Equals(GroupPolicy, StringComparison.OrdinalIgnoreCase) && int.TryParse(e.EventId, out _)))
        {
            int id = int.Parse(e.EventId, CultureInfo.InvariantCulture);
            string phase = id switch
            {
                >= 4000 and <= 4007 => "start", >= 8000 and <= 8007 => "end", 5016 or 5017 => "extension", 7016 or 7017 => "extension_time",
                5312 => "applied_list", 5313 => "filtered_list", 7320 => "error", _ => "",
            };
            if (phase.Length == 0) continue;
            var ids = new List<string>(); var names = new List<string>();
            if (phase is "applied_list" or "filtered_list")
            {
                var text = string.Join(" ", e.Fields.Values);
                foreach (Match m in GpoRx.Matches(text)) { ids.Add(m.Groups[1].Value.ToUpperInvariant()); names.Add(m.Groups[2].Success ? m.Groups[2].Value : ""); }
                if (ids.Count == 0) foreach (Match m in GuidRx.Matches(text)) { ids.Add(m.Value.ToUpperInvariant()); names.Add(""); }
            }
            string code = F(e, "ErrorCode");
            bool nonZero = code.Length > 0 && code != "0" && !code.Equals("0x0", StringComparison.OrdinalIgnoreCase) && !code.Equals("0x00000000", StringComparison.OrdinalIgnoreCase);
            list.Add(new PolicyApplication
            {
                TimeUtc = e.Time.Utc!.Value, Host = e.Host, EventId = e.EventId, Phase = phase, GpoIds = ids, GpoNames = names, Error = phase == "error" || nonZero,
                Detail = nonZero ? $"ErrorCode {code}" : "", Evidence = Ref(e, $"GroupPolicy/Operational {e.EventId}"),
            });
        }
        return list;
    }

    // ---------------------------------------------------------------- 2. chain: application and behaviour

    private static PolicyChain Chain(PolicyChange c, List<PolicyApplication> apps, List<TimelineEvent> security)
    {
        var gpos = c.TargetKind == "GPO" ? (c.TargetId.Length > 0 ? new List<string> { c.TargetId } : []) : c.RelatedGpos;
        var first = new List<FirstApplication>();
        LevelState appState; string appReason;
        if (gpos.Count == 0 || c.Kind == "DS_DELETED")
        {
            appState = LevelState.Unknown;
            appReason = c.Kind == "DS_DELETED" ? "GPO șters: aplicarea nu se evaluează." : "Nu este o modificare de GPO sau de legătură GPO: aplicarea pe stații nu se evaluează.";
        }
        else if (apps.Count == 0)
        {
            appState = LevelState.Unknown;
            appReason = "Lipsește Microsoft-Windows-GroupPolicy/Operational din probe: aplicarea pe stații nu poate fi evaluată.";
        }
        else
        {
            foreach (var h in apps.GroupBy(a => a.Host, StringComparer.OrdinalIgnoreCase))
            {
                var hit = h.Where(a => a.Phase == "applied_list" && a.TimeUtc >= c.TimeUtc && a.GpoIds.Any(g => gpos.Contains(g, StringComparer.OrdinalIgnoreCase))).OrderBy(a => a.TimeUtc).FirstOrDefault();
                if (hit is not null) first.Add(new FirstApplication { Host = h.Key, TimeUtc = hit.TimeUtc, Evidence = hit.Evidence });
            }
            var hosts = apps.Select(a => a.Host).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            if (first.Count > 0)
            {
                appState = LevelState.Observed;
                appReason = $"Prima aplicare după modificare: {string.Join("; ", first.OrderBy(f => f.Host).Select(f => $"{f.Host} la {f.TimeUtc:yyyy-MM-dd HH:mm} UTC"))}. Stații fără aplicare în dovezi: " +
                            (hosts.Except(first.Select(f => f.Host), StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } rest ? string.Join(", ", rest) : "niciuna") + ".";
            }
            else
            {
                appState = LevelState.NotObserved;
                appReason = $"Jurnalul GroupPolicy/Operational există pentru {string.Join(", ", hosts)}, dar niciuna nu listează GPO-ul (5312) după modificare. Posibil: replicare întârziată, filtrare, filtru WMI, stație offline.";
            }
        }
        var (beh, why, ev) = Behavior(c, security);
        return new PolicyChain { Change = c, ApplicationState = appState, ApplicationReason = appReason, FirstApplications = first, Behavior = beh, BehaviorReason = why, BehaviorEvidence = ev };
    }

    private static (BehaviorState, string, List<EvidenceRef>) Behavior(PolicyChange c, List<TimelineEvent> security)
    {
        if (c.Kind == "AUDIT_POLICY") return AuditBehavior(c, security);
        if (c.Kind == "DS_MODIFIED" && c.TargetKind == "GPO" && c.Attribute.StartsWith("gPC", StringComparison.OrdinalIgnoreCase) && c.Attribute.EndsWith("ExtensionNames", StringComparison.OrdinalIgnoreCase)
            && c.NewValue.Contains(AuditCse, StringComparison.OrdinalIgnoreCase) && !c.OldValue.Contains(AuditCse, StringComparison.OrdinalIgnoreCase))
        {
            var applied = security.Where(e => e.EventId == "4719" && e.Time.Utc >= c.TimeUtc && e.Time.Utc <= c.TimeUtc + ApplicationWindow
                                              && (F(e, "SubjectUserName").EndsWith('$') || F(e, "SubjectUserName").Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))).ToList();
            if (applied.Count > 0)
                return (BehaviorState.Observed, $"GPO-ul a adăugat extensia de audit și {applied.Count} evenimente 4719 aplicate de sistem/contul calculatorului apar după modificare (stații: {string.Join(", ", applied.Select(e => e.Host).Distinct())}).",
                        applied.Take(5).Select(e => Ref(e, "politică de audit aplicată prin GPO")).ToList());
            var last = security.LastOrDefault(e => e.Time.Utc is not null);
            if (last is not null && last.Time.Utc >= c.TimeUtc + RefreshObservation)
                return (BehaviorState.NotObserved, $"GPO-ul a adăugat extensia de audit, dar niciun 4719 aplicat de sistem nu apare în {ApplicationWindow.TotalHours:0}h după modificare, deși jurnalul Security continuă până la {last.Time.Utc:yyyy-MM-dd HH:mm} UTC (peste intervalul de reîmprospătare ~2h). Un 4719 apare doar dacă setarea de audit se schimbă efectiv.",
                        [Ref(last, "ultimul eveniment Security din jurnal")]);
            return (BehaviorState.NotEvaluable, "GPO-ul adaugă extensia de audit, dar jurnalul Security nu continuă după modificare cel puțin 2h (interval de reîmprospătare): nu se poate vedea dacă s-a aplicat.", []);
        }
        string what = c.Kind switch
        {
            "DS_MODIFIED" when c.TargetKind == "GPO" => $"evenimentul 5136 raportează doar atributul „{c.Attribute}” al obiectului GPO din Active Directory; setările efective sunt în SYSVOL, care nu e în EVTX",
            "DS_MODIFIED" or "DS_CREATED" or "DS_MOVED" or "DS_DELETED" when c.TargetKind is "OU" or "Domain" or "Site" => "efectul unei legături sau mutări depinde de setările GPO-urilor legate (SYSVOL), care nu sunt în EVTX",
            "DOMAIN_POLICY" => "4739 nu are evenimente de efect asociate în catalogul instrumentului",
            "OBJECT_SACL" => "efectul unei SACL se vede doar prin evenimentele de acces la acel obiect, care nu sunt asociate de instrument",
            _ => "tipul modificării nu are un efect observabil asociat în catalogul instrumentului",
        };
        return (BehaviorState.NotEvaluable, "Neevaluabil: " + what + ". Efectul nu se deduce dincolo de dovezi.", []);
    }

    private static (BehaviorState, string, List<EvidenceRef>) AuditBehavior(PolicyChange c, List<TimelineEvent> security)
    {
        if (!Effects.TryGetValue(c.Attribute, out var fx))
            return (BehaviorState.NotEvaluable, $"Subcategoria „{c.Attribute}” nu are evenimente asociate în catalogul instrumentului: efectul nu poate fi evaluat.", []);
        bool removedS = c.NewValue.Contains("Success eliminat"), removedF = c.NewValue.Contains("Failure eliminat");
        bool addedS = c.NewValue.Contains("Success adăugat"), addedF = c.NewValue.Contains("Failure adăugat");
        bool removal = removedS || removedF;
        int[] ids = removal ? (removedS ? fx.Success : []).Concat(removedF ? fx.Failure : []).Distinct().ToArray() : (addedS ? fx.Success : []).Concat(addedF ? fx.Failure : []).Distinct().ToArray();
        if (ids.Length == 0)
            return (BehaviorState.NotEvaluable, $"Pentru „{c.Attribute}” schimbarea ({c.NewValue}) nu are evenimente asociate pe acea latură (Success/Failure): efectul nu poate fi evaluat.", []);
        string idText = string.Join("/", ids);
        var host = security.Where(e => e.EventId != "4719" && SameHost(e.Host, c.Host)).ToList();
        var hits = host.Where(e => ids.Contains(int.TryParse(e.EventId, out var n) ? n : -1)).ToList();
        var before = hits.Where(e => e.Time.Utc < c.TimeUtc).ToList();
        var after = hits.Where(e => e.Time.Utc > c.TimeUtc).ToList();
        var lastAfter = host.LastOrDefault(e => e.Time.Utc > c.TimeUtc);
        if (lastAfter is null || lastAfter.Time.Utc - c.TimeUtc < MinObservation)
            return (BehaviorState.NotEvaluable, $"Nu există suficiente evenimente Security pe {(c.Host.Length > 0 ? c.Host : "stație")} după modificare (cel puțin {MinObservation.TotalHours:0}h de jurnal) ca să se vadă dacă {idText} se oprește sau apare.", []);
        double hours = (lastAfter.Time.Utc!.Value - c.TimeUtc).TotalHours;
        if (removal)
        {
            if (before.Count == 0)
                return (BehaviorState.NotEvaluable, $"Înainte de modificare nu apar evenimente {idText} pe stație: nu se poate vedea o oprire.", []);
            return after.Count == 0
                ? (BehaviorState.Observed, $"Evenimentele {idText} apăreau înainte ({before.Count}) și nu mai apar în {hours:0.#}h după modificare, deși jurnalul Security continuă.", [Ref(before[^1], "ultimul eveniment auditat înainte"), Ref(lastAfter, "jurnalul continuă după modificare")])
                : (BehaviorState.NotObserved, $"{after.Count} evenimente {idText} apar și după modificare: auditarea continuă.", after.Take(3).Select(e => Ref(e, "eveniment auditat după modificare")).ToList());
        }
        if (after.Count > 0 && before.Count == 0)
            return (BehaviorState.Observed, $"Evenimentele {idText} nu apăreau înainte și apar după modificare ({after.Count}).", after.Take(3).Select(e => Ref(e, "eveniment auditat după modificare")).ToList());
        if (after.Count > 0) return (BehaviorState.NotEvaluable, $"Evenimentele {idText} apăreau și înainte de modificare: apariția lor nu poate fi atribuită modificării.", []);
        return fx.FrequentSuccess && addedS
            ? (BehaviorState.NotObserved, $"Nu apare niciun eveniment {idText} în {hours:0.#}h după modificare, deși activitatea auditată este frecventă și jurnalul continuă.", [Ref(lastAfter, "jurnalul continuă după modificare")])
            : (BehaviorState.NotEvaluable, $"Nu apare niciun eveniment {idText} după modificare, dar lipsa lor poate însemna lipsa activității auditate.", []);
    }

    // ---------------------------------------------------------------- 3. levels

    private static SettingLevels Levels(ExpectedSetting s, List<TimelineEvent> timed, List<PolicyApplication> apps, List<TimelineEvent> security, PolicyTimelineOptions o)
    {
        var configured = new LevelEvidence { State = LevelState.Observed, Reason = $"Politica așteptată ({s.Source}) cere „{s.Title}”: {s.DesiredDisplay}." };

        // applied: GroupPolicy/Operational evidence
        LevelEvidence applied;
        var gpEvents = timed.Where(e => e.Source.Equals(GroupPolicy, StringComparison.OrdinalIgnoreCase)).ToList();
        if (gpEvents.Count == 0 && apps.Count == 0)
            applied = new() { Reason = "Lipsește Microsoft-Windows-GroupPolicy/Operational din probe: aplicarea nu poate fi evaluată." };
        else
        {
            bool audit = s.Type == "audit";
            bool Mentions(TimelineEvent e) => e.Fields.Values.Any(v => v.Contains(AuditCse, StringComparison.OrdinalIgnoreCase));
            var okEvents = gpEvents.Where(e => audit ? Mentions(e) && !IsError(e) : e.EventId is "5016" or "5017" or "5312" && !IsError(e)).ToList();
            var errEvents = gpEvents.Where(IsError).ToList();
            var lastOk = okEvents.LastOrDefault(); var lastErr = errEvents.LastOrDefault();
            if (lastErr is not null && (lastOk is null || lastErr.Time.Utc > lastOk.Time.Utc))
                applied = new() { State = LevelState.NotObserved, Reason = $"Ultima procesare a politicii pe {lastErr.Host} (la {lastErr.Time.Utc:yyyy-MM-dd HH:mm} UTC) a raportat eroare (GroupPolicy {lastErr.EventId}{(F(lastErr, "ErrorCode") is { Length: > 0 } c ? ", ErrorCode " + c : "")}), fără o procesare reușită ulterioară.", Evidence = [Ref(lastErr, "eroare de procesare GroupPolicy")] };
            else if (lastOk is not null)
                applied = new() { State = LevelState.Observed, Reason = $"{(audit ? "Extensia de audit" : "Extensia de politică")} a fost procesată fără eroare (ultima la {lastOk.Time.Utc:yyyy-MM-dd HH:mm} UTC pe {lastOk.Host}). Arată procesarea extensiei, nu valoarea setării.", Evidence = [Ref(lastOk, "extensie procesată")] };
            else
                applied = new() { Reason = audit ? "GroupPolicy/Operational nu menționează extensia de audit ({F3CCC681…}) în intervalul analizat: setarea nu poate fi legată de o aplicare." : "GroupPolicy/Operational nu conține evenimente de extensie procesată pentru această setare." };
        }

        // enforced: effective state where collected
        LevelEvidence enforced;
        if (o.Effective is not null && o.Effective.TryGetValue(s.EffectiveKey, out var eff))
        {
            bool ok = s.Type == "audit" ? AuditSatisfied(s.DesiredValue, eff) : string.Equals(eff.Trim(), s.DesiredValue.Trim(), StringComparison.OrdinalIgnoreCase);
            enforced = new() { State = ok ? LevelState.Observed : LevelState.NotObserved, Reason = $"Starea efectivă colectată: „{eff}”; cerut: {s.DesiredDisplay}." };
        }
        else enforced = new() { Reason = "Starea efectivă nu a fost colectată în acest caz (AuditQuerySystemPolicy / StationFacts): nivelul enforced nu poate fi evaluat din EVTX." };

        // observed: events consistent with the setting
        LevelEvidence observed;
        if (s.Type == "audit" && Effects.TryGetValue(s.Name, out var fx))
        {
            var d = s.DesiredValue.Trim();
            bool wantS = d.Contains("Success", StringComparison.OrdinalIgnoreCase), wantF = d.Contains("Failure", StringComparison.OrdinalIgnoreCase);
            int[] ids = (wantS ? fx.Success : []).Concat(wantF ? fx.Failure : []).Distinct().ToArray();
            if (!wantS && !wantF) ids = fx.Success.Concat(fx.Failure).Distinct().ToArray();   // "No Auditing": any of them contradicts
            string idText = string.Join("/", ids);
            var hits = security.Where(e => ids.Contains(int.TryParse(e.EventId, out var n) ? n : -1)).ToList();
            var withTime = security.Where(e => e.Time.Utc is not null).ToList();
            double spanH = withTime.Count > 1 ? (withTime[^1].Time.Utc!.Value - withTime[0].Time.Utc!.Value).TotalHours : 0;
            if (!wantS && !wantF)
                observed = hits.Count > 0 ? new() { State = LevelState.NotObserved, Reason = $"Se cere „No Auditing”, dar apar {hits.Count} evenimente {idText}.", Evidence = hits.Take(3).Select(e => Ref(e, "eveniment auditat deși nu ar trebui")).ToList() }
                                         : new() { Reason = $"Se cere „No Auditing”: lipsa evenimentelor {idText} e compatibilă, dar nu o dovedește (poate lipsi activitatea)." };
            else if (hits.Count > 0)
                observed = new() { State = LevelState.Observed, Reason = $"{hits.Count} evenimente {idText} în jurnalul Security ({hits[0].Time.UtcIso} – {hits[^1].Time.UtcIso}).", Evidence = [Ref(hits[0], "primul eveniment auditat"), Ref(hits[^1], "ultimul eveniment auditat")] };
            else if (withTime.Count > 1 && spanH >= MinObservation.TotalHours && wantS && !wantF && fx.FrequentSuccess || withTime.Count > 1 && spanH >= MinObservation.TotalHours && wantS && wantF && fx.FrequentSuccess)
                observed = new() { State = LevelState.NotObserved, Reason = $"Niciun eveniment {idText} în {spanH:0.#}h de jurnal Security ({withTime.Count} evenimente), deși activitatea auditată e frecventă.", Evidence = [Ref(withTime[0], "începutul jurnalului Security"), Ref(withTime[^1], "sfârșitul jurnalului Security")] };
            else
                observed = new() { Reason = withTime.Count > 1 && spanH >= MinObservation.TotalHours ? $"Niciun eveniment {idText}, dar lipsa lor poate însemna lipsa activității auditate." : $"Jurnalul Security din probe e prea scurt sau lipsește ({withTime.Count} evenimente, {spanH:0.#}h) pentru a judeca lipsa evenimentelor {idText}." };
        }
        else observed = new() { Reason = s.Type == "audit" ? $"Subcategoria „{s.Name}” nu are evenimente asociate în catalogul instrumentului." : $"Setarea de tip „{s.Type}” nu are evenimente asociate în catalogul instrumentului: nivelul observed nu poate fi evaluat." };

        return new SettingLevels { Setting = s, Configured = configured, Applied = applied, Enforced = enforced, Observed = observed };

        static bool IsError(TimelineEvent e)
        {
            var code = F(e, "ErrorCode");
            return e.EventId == "7320" || code.Length > 0 && code != "0" && !code.Equals("0x0", StringComparison.OrdinalIgnoreCase) && !code.Equals("0x00000000", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool AuditSatisfied(string desired, string effective)
    {
        var d = desired.Trim(); var e = effective.Trim();
        if (d.Equals("No Auditing", StringComparison.OrdinalIgnoreCase)) return e.Equals("No Auditing", StringComparison.OrdinalIgnoreCase);
        bool needS = d.Contains("Success", StringComparison.OrdinalIgnoreCase), needF = d.Contains("Failure", StringComparison.OrdinalIgnoreCase);
        return (!needS || e.Contains("Success", StringComparison.OrdinalIgnoreCase)) && (!needF || e.Contains("Failure", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- 4. gap

    private static ControlGap? Gap(SettingLevels l, List<PolicyChange> changes, List<PolicyApplication> apps, List<TimelineEvent> security, PolicyTimelineOptions o)
    {
        string? broken = new[] { "applied", "enforced", "observed" }.FirstOrDefault(n => l.Level(n).State == LevelState.NotObserved);
        if (broken is null) return null;
        var s = l.Setting;
        bool auditing = s.Type == "audit" && !s.DesiredValue.Equals("No Auditing", StringComparison.OrdinalIgnoreCase);
        bool disablesAuditing = auditing && broken is "enforced" or "observed";
        var secTimes = security.Where(e => e.Time.Utc is not null).Select(e => e.Time.Utc!.Value).ToList();
        DateTimeOffset? start = null, end = secTimes.Count > 0 ? secTimes.Max() : apps.Count > 0 ? apps.Max(a => a.TimeUtc) : null;
        if (broken == "applied") start = apps.Where(a => a.Error).Select(a => (DateTimeOffset?)a.TimeUtc).Min();
        else
        {
            start = changes.Where(c => c.Kind == "AUDIT_POLICY" && c.Attribute.Equals(s.Name, StringComparison.OrdinalIgnoreCase) && c.NewValue.Contains("eliminat")).Select(c => (DateTimeOffset?)c.TimeUtc).Max();
            start ??= secTimes.Count > 0 ? secTimes.Min() : null;
        }
        int highs = start is { } a && end is { } b ? o.HighFindingTimesUtc.Count(t => t >= a && t <= b) : 0;
        var sev = disablesAuditing && highs > 0 ? Severity.High : Severity.Medium;
        var levels = string.Join(", ", SettingLevels.Names.Select(n => $"{n}={l.Level(n).State.ToString().Aggregate("", (acc, ch) => acc + (acc.Length > 0 && char.IsUpper(ch) ? "_" : "") + char.ToUpperInvariant(ch))}"));
        string summary = $"„{s.Title}” ({s.Source}) cere {s.DesiredDisplay}. Niveluri: {levels}. Se rupe la nivelul „{broken}”: {l.Level(broken).Reason}" +
                         (sev == Severity.High ? $" Auditarea e dezactivată în fereastra {start:yyyy-MM-dd HH:mm} – {end:yyyy-MM-dd HH:mm} UTC și {highs} constatări High cad în ea." : disablesAuditing ? " Nicio constatare High nu cade în fereastra decalajului." : "");
        var missing = SettingLevels.Names.Where(n => l.Level(n).State == LevelState.Unknown).Select(n => n switch
        {
            "applied" => "Microsoft-Windows-GroupPolicy/Operational de la stație (5016/5312)",
            "enforced" => "starea efectivă a setării (AuditQuerySystemPolicy / auditpol /get, StationFacts)",
            "observed" => "evenimentele asociate setării pentru o perioadă suficient de lungă",
            _ => "politica așteptată",
        }).ToList();
        missing.Add("conținutul GPO din SYSVOL și rezultatul gpresult/RSoP pe stație");
        return new ControlGap
        {
            SettingId = s.Id, Title = s.Title, BrokenLevel = broken, Reason = l.Level(broken).Reason, Severity = sev, WindowStart = start, WindowEnd = end,
            HighFindingsInWindow = highs, DisablesAuditing = disablesAuditing, Summary = summary, Evidence = l.Level(broken).Evidence, MissingEvidence = missing,
        };
    }
}
