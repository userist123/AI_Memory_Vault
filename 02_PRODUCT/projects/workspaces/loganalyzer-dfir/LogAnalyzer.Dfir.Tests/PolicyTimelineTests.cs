using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP15b: policy change events to PolicyChange records, the BEFORE-CHANGE-AFTER-APPLICATION-BEHAVIOR chain, the four levels and the control gap.</summary>
public sealed class PolicyTimelineTests
{
    private static readonly DateTime T0 = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private const string Gpo = "{31B2F340-016D-11D2-945F-00C04FB984F9}";
    private const string Gpo2 = "{6AC1786C-016F-11D2-945F-00C04fB984F9}";
    private const string GpoDn = "CN=" + Gpo + ",CN=Policies,CN=System,DC=corp,DC=local";
    private const string Sec = "EventLog:Security", Gp = "EventLog:Microsoft-Windows-GroupPolicy/Operational", Sys = "EventLog:System";
    private static int _n;

    private static TimelineEvent E(string source, string id, DateTime t, Dictionary<string, string>? f = null, string host = "DC01", string evidence = "EV-1", string provider = "") => new()
    {
        Time = Timestamp.FromUtc(t, "t", "test"), Source = source, EventId = id, Provider = provider, Host = host, EvidenceId = evidence, Summary = "s",
        Locator = $"EventRecordID={Interlocked.Increment(ref _n)}", Fields = new(f ?? [], StringComparer.OrdinalIgnoreCase),
    };

    private static Dictionary<string, string> Ds(string op, string attr, string value, string dn = GpoDn, string cls = "groupPolicyContainer", string corr = "{C1}") => new()
    {
        ["SubjectUserName"] = "admin", ["SubjectDomainName"] = "CORP", ["ObjectDN"] = dn, ["ObjectGUID"] = "{aaaa}", ["ObjectClass"] = cls,
        ["AttributeLDAPDisplayName"] = attr, ["AttributeValue"] = value, ["OperationType"] = op, ["OpCorrelationID"] = corr,
    };

    private static TimelineEvent Applied(DateTime t, string host, params string[] gpos) =>
        E(Gp, "5312", t, new() { ["GPOInfoList"] = string.Join("", gpos.Select(g => $"<GPO ID=\"{g}\"><Name>N{g}</Name></GPO>")) }, host);

    private static PolicyTimelineResult Build(IEnumerable<TimelineEvent> ev, PolicyTimelineOptions? o = null) => PolicyTimeline.Build(ev.ToList(), o);

    // ---- spec 1: events to records ----

    [Fact]
    public void Event_5136_value_deleted_and_added_become_one_change_with_before_and_after()
    {
        var r = Build([E(Sec, "5136", T0, Ds("%%14675", "versionNumber", "5")), E(Sec, "5136", T0.AddMilliseconds(5), Ds("%%14674", "versionNumber", "6"))]);
        var c = Assert.Single(r.Changes);
        Assert.Equal("DS_MODIFIED", c.Kind); Assert.Equal("GPO", c.TargetKind);
        Assert.Equal(Gpo, c.TargetId, ignoreCase: true);
        Assert.Equal("versionNumber", c.Attribute); Assert.Equal("5", c.OldValue); Assert.Equal("6", c.NewValue);
        Assert.Equal("CORP\\admin", c.Who); Assert.Equal("DC01", c.Host);
        Assert.Equal(2, c.Evidence.Count);
    }

    [Fact]
    public void A_5136_with_only_one_side_keeps_the_other_side_empty_not_invented()
    {
        var c = Assert.Single(Build([E(Sec, "5136", T0, Ds("%%14674", "gPCFileSysPath", @"\\corp.local\SysVol\x"))]).Changes);
        Assert.Equal("", c.OldValue); Assert.Contains("SysVol", c.NewValue);
        Assert.Contains("nu poartă valoarea veche", c.Detail);
    }

    [Fact]
    public void Link_changes_on_an_OU_diff_the_gPLink_and_relate_the_changed_GPOs()
    {
        const string ou = "OU=Servers,DC=corp,DC=local";
        string oldLink = $"[LDAP://CN={Gpo},CN=Policies,CN=System,DC=corp,DC=local;0]";
        string newLink = oldLink + $"[LDAP://CN={Gpo2},CN=Policies,CN=System,DC=corp,DC=local;2]";
        var c = Assert.Single(Build([E(Sec, "5136", T0, Ds("%%14675", "gPLink", oldLink, ou, "organizationalUnit", "{C2}")), E(Sec, "5136", T0, Ds("%%14674", "gPLink", newLink, ou, "organizationalUnit", "{C2}"))]).Changes);
        Assert.Equal("OU", c.TargetKind);
        Assert.Contains(Gpo2, c.RelatedGpos, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(Gpo, c.RelatedGpos, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("adăugat", c.Detail); Assert.Contains("impus", c.Detail);
    }

    [Fact]
    public void Events_5137_5141_5139_4739_4907_become_records_of_their_kind()
    {
        var r = Build([
            E(Sec, "5137", T0, new() { ["SubjectUserName"] = "a", ["ObjectDN"] = GpoDn, ["ObjectClass"] = "groupPolicyContainer" }),
            E(Sec, "5141", T0.AddMinutes(1), new() { ["SubjectUserName"] = "a", ["ObjectDN"] = GpoDn, ["ObjectClass"] = "groupPolicyContainer" }),
            E(Sec, "5139", T0.AddMinutes(2), new() { ["SubjectUserName"] = "a", ["OldObjectDN"] = "OU=A,DC=x", ["NewObjectDN"] = "OU=B,DC=x", ["ObjectClass"] = "organizationalUnit" }),
            E(Sec, "4739", T0.AddMinutes(3), new() { ["SubjectUserName"] = "a", ["DomainName"] = "CORP", ["MinPasswordLength"] = "14", ["LockoutThreshold"] = "-" }),
            E(Sec, "4907", T0.AddMinutes(4), new() { ["SubjectUserName"] = "a", ["ObjectName"] = @"C:\Windows\x", ["OldSd"] = "S:old", ["NewSd"] = "S:new", ["ProcessName"] = "p.exe" })]);
        Assert.Equal(["DS_CREATED", "DS_DELETED", "DS_MOVED", "DOMAIN_POLICY", "OBJECT_SACL"], r.Changes.Select(c => c.Kind).ToArray());
        Assert.Equal("OU=A,DC=x", r.Changes[2].OldValue); Assert.Equal("OU=B,DC=x", r.Changes[2].NewValue);
        Assert.Contains("MinPasswordLength=14", r.Changes[3].NewValue); Assert.DoesNotContain("LockoutThreshold", r.Changes[3].NewValue);
        Assert.Equal("S:old", r.Changes[4].OldValue); Assert.Equal("S:new", r.Changes[4].NewValue);
    }

    [Fact]
    public void Event_4719_reuses_the_lifecycle_classifier_and_names_the_subcategory()
    {
        var f = new Dictionary<string, string> { ["SubjectUserName"] = "admin", ["SubjectDomainName"] = "CORP", ["SubjectUserSid"] = "S-1-5-21-1", ["SubcategoryGuid"] = "{0CCE922B-69AE-11D9-BED3-505054503030}", ["AuditPolicyChanges"] = "%%8448" };
        var c = Assert.Single(Build([E(Sec, "4719", T0, f)]).Changes);
        Assert.Equal("AUDIT_POLICY", c.Kind); Assert.Equal("Process Creation", c.Attribute);
        Assert.Contains("necesită verificare", c.Lifecycle);   // no procedure profile: NotAssessed
        Assert.Equal(Severity.High, c.Severity);           // removal of success auditing is High when not routine
        var gpoApplied = new Dictionary<string, string>(f) { ["SubjectUserName"] = "DC01$" };
        Assert.Equal(Severity.Info, Assert.Single(Build([E(Sec, "4719", T0, gpoApplied)]).Changes).Severity);
    }

    [Fact]
    public void Endpoint_application_events_become_application_records_with_phase()
    {
        var r = Build([E(Gp, "4004", T0, host: "PC1"), E(Gp, "5016", T0.AddSeconds(1), host: "PC1"), Applied(T0.AddSeconds(2), "PC1", Gpo), E(Gp, "5313", T0.AddSeconds(3), new() { ["GPOInfoList"] = $"<GPO ID=\"{Gpo2}\"/>" }, "PC1"),
                       E(Gp, "7320", T0.AddSeconds(4), host: "PC1"), E(Gp, "8004", T0.AddSeconds(5), host: "PC1")]);
        Assert.Equal(["start", "extension", "applied_list", "filtered_list", "error", "end"], r.Applications.Select(a => a.Phase).ToArray());
        Assert.Contains(Gpo, r.Applications[2].GpoIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("N" + Gpo, r.Applications[2].GpoNames[0]);
    }

    // ---- spec 2: the chain ----

    private static IEnumerable<TimelineEvent> VersionChange(DateTime t) =>
        [E(Sec, "5136", t, Ds("%%14675", "versionNumber", "5")), E(Sec, "5136", t, Ds("%%14674", "versionNumber", "6"))];

    [Fact]
    public void Application_is_the_first_one_per_host_after_the_change_and_ignores_earlier_ones()
    {
        var r = Build([.. VersionChange(T0), Applied(T0.AddMinutes(-30), "PC1", Gpo), Applied(T0.AddMinutes(20), "PC1", Gpo), Applied(T0.AddMinutes(50), "PC1", Gpo), Applied(T0.AddMinutes(30), "PC2", Gpo)]);
        var chain = Assert.Single(r.Chains);
        Assert.Equal(LevelState.Observed, chain.ApplicationState);
        Assert.Equal(["PC1", "PC2"], chain.FirstApplications.Select(a => a.Host).OrderBy(x => x).ToArray());
        Assert.Equal(T0.AddMinutes(20), chain.FirstApplications.Single(a => a.Host == "PC1").TimeUtc.UtcDateTime);
    }

    [Fact]
    public void Application_is_NOT_OBSERVED_when_the_group_policy_log_exists_but_nothing_applied_after_and_UNKNOWN_without_the_log()
    {
        var withLog = Assert.Single(Build([.. VersionChange(T0), Applied(T0.AddMinutes(-30), "PC1", Gpo)]).Chains);
        Assert.Equal(LevelState.NotObserved, withLog.ApplicationState);
        Assert.Contains("PC1", withLog.ApplicationReason);
        var without = Assert.Single(Build(VersionChange(T0)).Chains);
        Assert.Equal(LevelState.Unknown, without.ApplicationState);
        Assert.Contains("GroupPolicy/Operational", without.ApplicationReason);
    }

    private static Dictionary<string, string> Audit4719(string change, string who = "admin", string guid = "{0CCE922B-69AE-11D9-BED3-505054503030}") =>
        new() { ["SubjectUserName"] = who, ["SubjectDomainName"] = "CORP", ["SubjectUserSid"] = "S-1-5-21-1", ["SubcategoryGuid"] = guid, ["AuditPolicyChanges"] = change };

    private static IEnumerable<TimelineEvent> Proc4688(DateTime from, int count, TimeSpan step, string host = "PC1") =>
        Enumerable.Range(0, count).Select(i => E(Sec, "4688", from + step * i, host: host));

    [Fact]
    public void Behaviour_observed_when_the_audited_event_ids_stop_after_a_removal_and_the_log_goes_on()
    {
        var ev = new List<TimelineEvent>(Proc4688(T0.AddHours(-3), 6, TimeSpan.FromMinutes(20)))
        { E(Sec, "4719", T0, Audit4719("%%8448"), "PC1") };
        ev.AddRange(Enumerable.Range(1, 6).Select(i => E(Sec, "4624", T0.AddMinutes(20 * i), host: "PC1")));
        var chain = Assert.Single(Build(ev).Chains);
        Assert.Equal(BehaviorState.Observed, chain.Behavior);
        Assert.Contains("4688", chain.BehaviorReason);
    }

    [Fact]
    public void Behaviour_not_observed_when_the_events_continue_and_not_evaluable_without_evidence_after()
    {
        var cont = new List<TimelineEvent>(Proc4688(T0.AddHours(-3), 6, TimeSpan.FromMinutes(20))) { E(Sec, "4719", T0, Audit4719("%%8448"), "PC1") };
        cont.AddRange(Proc4688(T0.AddMinutes(10), 6, TimeSpan.FromMinutes(20)));
        Assert.Equal(BehaviorState.NotObserved, Assert.Single(Build(cont).Chains).Behavior);

        var none = new List<TimelineEvent>(Proc4688(T0.AddHours(-3), 6, TimeSpan.FromMinutes(20))) { E(Sec, "4719", T0, Audit4719("%%8448"), "PC1") };
        var c = Assert.Single(Build(none).Chains);
        Assert.Equal(BehaviorState.NotEvaluable, c.Behavior);
        Assert.Contains("după", c.BehaviorReason);

        var unmapped = Assert.Single(Build([E(Sec, "4719", T0, Audit4719("%%8448", guid: "{0CCE9211-69AE-11D9-BED3-505054503030}"), "PC1")]).Chains);
        Assert.Equal(BehaviorState.NotEvaluable, unmapped.Behavior);
        Assert.Contains("nu are evenimente asociate", unmapped.BehaviorReason);
    }

    [Fact]
    public void A_plain_GPO_version_change_has_behaviour_NotEvaluable_with_the_reason_never_inferred()
    {
        var chain = Assert.Single(Build(VersionChange(T0)).Chains);
        Assert.Equal(BehaviorState.NotEvaluable, chain.Behavior);
        Assert.Contains("SYSVOL", chain.BehaviorReason);
    }

    [Fact]
    public void A_GPO_that_touches_the_audit_extension_is_followed_by_the_4719_applied_on_the_hosts()
    {
        const string cse = "[{F3CCC681-B74C-4060-9F26-CD84525DCA2A}{0F3F3735-573D-9804-99E4-AB2A69BA5FD4}]";
        var change = new List<TimelineEvent>
        { E(Sec, "5136", T0, Ds("%%14675", "gPCMachineExtensionNames", "[]")), E(Sec, "5136", T0, Ds("%%14674", "gPCMachineExtensionNames", cse)) };
        var applied = new List<TimelineEvent>(change) { E(Sec, "4719", T0.AddMinutes(40), Audit4719("%%8449", "PC1$"), "PC1") };
        Assert.Equal(BehaviorState.Observed, Build(applied).Chains.Single(c => c.Change.Kind == "DS_MODIFIED").Behavior);
        var notYet = new List<TimelineEvent>(change) { E(Sec, "4624", T0.AddHours(3), host: "PC1") };
        Assert.Equal(BehaviorState.NotObserved, Build(notYet).Chains.Single().Behavior);
    }

    // ---- spec 3 + 4: levels and gap ----

    private static ExpectedSetting AuditProcess(string desired = "Success") => new("C-1", "Auditarea creării proceselor", "audit", "Process Creation", desired, desired, "politica.lapolicy");

    private static PolicyTimelineOptions Opts(Dictionary<string, string>? effective = null, IReadOnlyList<DateTimeOffset>? highs = null) =>
        new() { Expected = [AuditProcess()], Effective = effective, HighFindingTimesUtc = highs ?? [] };

    private static LevelState Level(PolicyTimelineResult r, string level) => Assert.Single(r.Levels).Level(level).State;

    [Fact]
    public void No_expected_policy_means_nedefinit_not_conform()
    {
        var r = Build([E(Sec, "4624", T0)]);
        Assert.Empty(r.Levels); Assert.Empty(r.Gaps);
        Assert.Contains("nedefinit", r.ExpectedNote);
    }

    [Fact]
    public void Levels_with_no_group_policy_log_no_effective_state_and_events_present()
    {
        var r = Build(Proc4688(T0, 4, TimeSpan.FromHours(1)), Opts());
        Assert.Equal(LevelState.Observed, Level(r, "configured"));
        Assert.Equal(LevelState.Unknown, Level(r, "applied"));
        Assert.Equal(LevelState.Unknown, Level(r, "enforced"));
        Assert.Equal(LevelState.Observed, Level(r, "observed"));
        Assert.Empty(r.Gaps);
    }

    [Fact]
    public void Observed_level_is_NOT_OBSERVED_for_a_frequent_activity_when_the_log_has_other_events_and_UNKNOWN_when_there_is_no_log_span()
    {
        var logons = Enumerable.Range(0, 6).Select(i => E(Sec, "4624", T0.AddMinutes(20 * i))).ToList();
        Assert.Equal(LevelState.NotObserved, Level(Build(logons, Opts()), "observed"));
        Assert.Equal(LevelState.Unknown, Level(Build([E(Sec, "4624", T0)], Opts()), "observed"));
        Assert.Equal(LevelState.Unknown, Level(Build([], Opts()), "observed"));
    }

    [Fact]
    public void Enforced_level_comes_from_the_effective_state_where_collected()
    {
        var logons = Enumerable.Range(0, 6).Select(i => E(Sec, "4624", T0.AddMinutes(20 * i))).ToList();
        Assert.Equal(LevelState.NotObserved, Level(Build(logons, Opts(new() { ["audit:Process Creation"] = "No Auditing" })), "enforced"));
        Assert.Equal(LevelState.Observed, Level(Build(logons, Opts(new() { ["audit:Process Creation"] = "Success and Failure" })), "enforced"));
    }

    [Fact]
    public void Applied_level_is_observed_from_the_audit_extension_and_NOT_OBSERVED_from_an_error()
    {
        var ok = E(Gp, "5016", T0, new() { ["CSEExtensionId"] = "{F3CCC681-B74C-4060-9F26-CD84525DCA2A}" }, "PC1");
        Assert.Equal(LevelState.Observed, Level(Build([ok], Opts()), "applied"));
        var err = E(Gp, "7320", T0.AddMinutes(1), new() { ["ErrorCode"] = "5" }, "PC1");
        Assert.Equal(LevelState.NotObserved, Level(Build([ok, err], Opts()), "applied"));
        Assert.Equal(LevelState.Unknown, Level(Build([E(Gp, "4004", T0, host: "PC1")], Opts()), "applied"));
    }

    [Fact]
    public void Gap_finding_is_Medium_by_default_names_the_level_and_lists_alternatives_and_missing_evidence()
    {
        var logons = Enumerable.Range(0, 6).Select(i => E(Sec, "4624", T0.AddMinutes(20 * i))).ToList();
        var r = Build(logons, Opts());
        var gap = Assert.Single(r.Gaps);
        Assert.Equal("observed", gap.BrokenLevel); Assert.Equal(Severity.Medium, gap.Severity);
        var f = Assert.Single(r.ToFindings(() => "F-0100"));
        Assert.Equal("POLICY-CONTROL-GAP", f.RuleId); Assert.Equal(Severity.Medium, f.Severity); Assert.Equal("F-0100", f.FindingId);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("replicare")); Assert.Contains(f.AlternativeExplanations, a => a.Contains("WMI"));
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("loopback")); Assert.Contains(f.AlternativeExplanations, a => a.Contains("oprită") || a.Contains("offline"));
        Assert.NotEmpty(f.MissingEvidence); Assert.Contains("observed", f.Description);
        Assert.NotEmpty(f.SupportingEvidence);
    }

    [Fact]
    public void Gap_is_High_only_when_auditing_is_disabled_and_a_High_finding_falls_in_the_gap_window()
    {
        var logons = Enumerable.Range(0, 6).Select(i => E(Sec, "4624", T0.AddMinutes(20 * i))).ToList();
        var inside = Build(logons, Opts(highs: [new DateTimeOffset(T0.AddMinutes(30), TimeSpan.Zero)]));
        Assert.Equal(Severity.High, Assert.Single(inside.Gaps).Severity);
        var outside = Build(logons, Opts(highs: [new DateTimeOffset(T0.AddDays(5), TimeSpan.Zero)]));
        Assert.Equal(Severity.Medium, Assert.Single(outside.Gaps).Severity);
        // a gap that is not about auditing never becomes High
        var reg = new ExpectedSetting("C-2", "Reg", "registry", "X", "1", "1", "p");
        var other = Build([E(Gp, "7320", T0, new() { ["ErrorCode"] = "5" }, "PC1")], new PolicyTimelineOptions { Expected = [reg], HighFindingTimesUtc = [new DateTimeOffset(T0, TimeSpan.Zero)] });
        Assert.Equal(Severity.Medium, Assert.Single(other.Gaps).Severity);
    }

    [Fact]
    public void Unknown_levels_never_make_a_gap()
    {
        Assert.Empty(Build(Proc4688(T0, 4, TimeSpan.FromHours(1)), Opts()).Gaps);
    }

    [Fact]
    public void The_result_serialises_versioned_with_chains_levels_gaps_and_windows()
    {
        var r = Build([.. VersionChange(T0), E(Sys, "22", T0, provider: "Microsoft-Windows-Kernel-General")], Opts());
        using var doc = JsonDocument.Parse(r.ToJson());
        Assert.Equal(PolicyTimeline.SchemaVersion, doc.RootElement.GetProperty("schema_version").GetString());
        foreach (var k in new[] { "Changes", "Applications", "Chains", "Levels", "Gaps", "TimeWindows" }) Assert.True(doc.RootElement.TryGetProperty(k, out _), k);
    }
}
