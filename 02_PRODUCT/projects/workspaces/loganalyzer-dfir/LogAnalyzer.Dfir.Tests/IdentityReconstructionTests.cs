using LogAnalyzer.Dfir.Identity;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.10: identities keyed by SID, host-scoped logon ids, duplicate DC events, SID history, renames, GPO changes and DC gaps.</summary>
public sealed class IdentityReconstructionTests
{
    private const string Dom = "S-1-5-21-1111111111-2222222222-3333333333";
    private const string Alice = Dom + "-1105", Bob = Dom + "-1106", Admin = Dom + "-500";
    private static int _rec;

    private static TimelineEvent Ev(string host, string eid, int minute, params (string K, string V)[] f) => Ev(host, eid, minute, "ev-" + host, ++_rec, f);
    private static TimelineEvent Ev(string host, string eid, int minute, string evidence, int record, params (string K, string V)[] f)
    {
        var t = new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc).AddMinutes(minute);
        var e = new TimelineEvent
        {
            Time = Timestamp.FromUtc(t, t.ToString("o"), "EVTX SystemTime"), Source = "EventLog:Security", EvidenceId = evidence, EventId = eid,
            Provider = "Microsoft-Windows-Security-Auditing", Host = host, Summary = "Security " + eid, Locator = $"EventRecordID={record}",
        };
        foreach (var (k, v) in f) e.Fields[k] = v;
        return e;
    }

    private static TimelineEvent Logon(string host, int minute, string sid, string name, string logonId, string type = "3", string domain = "LAB") =>
        Ev(host, "4624", minute, ("TargetUserSid", sid), ("TargetUserName", name), ("TargetDomainName", domain), ("TargetLogonId", logonId), ("LogonType", type), ("IpAddress", "10.66.0.21"));
    private static TimelineEvent Logoff(string host, int minute, string sid, string logonId) => Ev(host, "4634", minute, ("TargetUserSid", sid), ("TargetLogonId", logonId));

    [Fact]
    public void A_rename_keeps_one_identity_keyed_by_sid_with_both_names()
    {
        var r = IdentityReconstruction.Build([
            Logon("WS01", 0, Alice, "alice", "0x1"),
            Ev("DC01", "4781", 5, ("TargetSid", Alice), ("OldTargetUserName", "alice"), ("NewTargetUserName", "alice.old"), ("TargetDomainName", "LAB"), ("SubjectUserSid", Admin), ("SubjectUserName", "Administrator")),
            Logon("WS01", 10, Alice, "alice.old", "0x2"),
        ]);
        var id = Assert.Single(r.Identities, i => i.Sid == Alice);
        Assert.Equal(["alice", "alice.old"], id.Names.Select(n => n.Name));
        Assert.Equal(2, id.Logons);
        Assert.Contains(r.Identities, i => i.Sid == Admin && i.Activity.Any(a => a.Text.Contains("redenumit")));   // the actor's side is recorded too
    }

    [Fact]
    public void A_logon_id_is_scoped_to_its_host_and_sessions_pair_logon_and_logoff()
    {
        var r = IdentityReconstruction.Build([
            Logon("WS01", 0, Alice, "alice", "0x3e7a"),
            Logon("SRV01", 1, Bob, "bob", "0x3e7a"),                         // same logon id on another host is another session
            Ev("SRV01", "4672", 1, ("SubjectUserSid", Bob), ("SubjectUserName", "bob"), ("SubjectLogonId", "0x3e7a")),
            Logoff("WS01", 30, Alice, "0x3e7a"),
        ]);
        Assert.Equal(2, r.Sessions.Count);
        var ws = Assert.Single(r.Sessions, s => s.Host == "WS01");
        Assert.Equal(("ridicată", false), (ws.Confidence, ws.Elevated));
        Assert.NotEmpty(ws.EndUtc);
        var srv = Assert.Single(r.Sessions, s => s.Host == "SRV01");
        Assert.True(srv.Elevated);                                          // 4672 on SRV01 marks only the SRV01 session
        Assert.Empty(srv.EndUtc);
        Assert.Equal("medie", srv.Confidence);
        Assert.True(r.Identities.Single(i => i.Sid == Bob).Privileged);
    }

    [Fact]
    public void The_same_dc_record_imported_twice_counts_once()
    {
        var a = Ev("DC01", "4768", 0, "ev-1", 777, ("TargetSid", Alice), ("TargetUserName", "alice"), ("TargetDomainName", "LAB"));
        var b = Ev("dc01.lab.local", "4768", 0, "ev-2", 777, ("TargetSid", Alice), ("TargetUserName", "alice"), ("TargetDomainName", "LAB"));
        var r = IdentityReconstruction.Build([a, b]);
        Assert.Equal(1, r.DuplicatesDropped);
        Assert.Single(r.Identities.Single(i => i.Sid == Alice).Activity);
        Assert.Equal(["DC01"], r.DomainControllers);
    }

    [Fact]
    public void Events_under_an_old_sid_from_sid_history_belong_to_the_current_identity()
    {
        const string old = "S-1-5-21-9999999999-8888888888-7777777777-1300";
        var r = IdentityReconstruction.Build([
            Ev("DC01", "4765", 0, ("TargetSid", Alice), ("SourceSid", old), ("TargetUserName", "alice")),
            Logon("SRV01", 5, old, "alice", "0x9"),
        ]);
        var id = Assert.Single(r.Identities, i => i.Sid == Alice);
        Assert.Contains(old, id.SidHistory);
        Assert.Equal(1, id.Logons);
        Assert.DoesNotContain(r.Identities, i => i.Sid == old);
    }

    [Fact]
    public void A_gpo_change_is_linked_to_the_object_and_the_actor()
    {
        var r = IdentityReconstruction.Build([
            Ev("DC01", "5136", 0, ("ObjectDN", "CN={31B2F340-016D-11D2-945F-00C04FB984F9},CN=Policies,CN=System,DC=lab,DC=local"), ("ObjectClass", "groupPolicyContainer"),
               ("AttributeLDAPDisplayName", "gPCMachineExtensionNames"), ("AttributeValue", "[{x}]"), ("OperationType", "%%14674"), ("SubjectUserSid", Admin), ("SubjectUserName", "Administrator")),
        ]);
        var c = Assert.Single(r.DirectoryChanges);
        Assert.True(c.IsGpo);
        Assert.Equal((Admin, "valoare adăugată"), (c.ActorSid, c.Operation));
        Assert.StartsWith("EventRecordID=", c.Locator);
        Assert.Contains(r.Identities.Single(i => i.Sid == Admin).Activity, a => a.Text.Contains("politică de grup") && a.Locator == c.Locator);
    }

    [Fact]
    public void Domain_logons_without_dc_logs_are_a_gap_not_a_negative()
    {
        var r = IdentityReconstruction.Build([Logon("WS01", 0, Alice, "alice", "0x1"), Logoff("WS01", 5, Alice, "0x1")]);
        Assert.Contains(r.Gaps, g => g.What.Contains("controler de domeniu"));
        Assert.Empty(r.DomainControllers);

        var none = IdentityReconstruction.Build([]);
        Assert.Contains(none.Gaps, g => g.What.Contains("Nu există evenimente"));
    }

    [Fact]
    public void Adding_to_a_privileged_group_marks_the_member_privileged_and_records_the_actor()
    {
        var r = IdentityReconstruction.Build([
            Ev("DC01", "4728", 0, ("MemberSid", Bob), ("TargetSid", Dom + "-512"), ("TargetUserName", "Domain Admins"), ("TargetDomainName", "LAB"),
               ("SubjectUserSid", Alice), ("SubjectUserName", "alice")),
        ]);
        var bob = Assert.Single(r.Identities, i => i.Sid == Bob);
        Assert.True(bob.Privileged);
        Assert.Contains("Domain Admins", bob.PrivilegedReason);
        Assert.Equal(Alice, Assert.Single(bob.Activity).ActorSid);
        Assert.Contains(r.Identities.Single(i => i.Sid == Alice).Activity, a => a.Text.Contains("membrii grupului"));
    }

    [Fact]
    public void A_name_only_event_joins_the_sid_identity_unless_two_sids_carried_the_name()
    {
        var r = IdentityReconstruction.Build([
            Ev("DC01", "4769", 0, ("TargetUserName", "alice@LAB.LOCAL"), ("ServiceName", "SRV01$")),   // before the SID was seen
            Logon("WS01", 1, Alice, "alice", "0x1"),
            Ev("DC01", "4776", 2, ("TargetUserName", "carol"), ("Workstation", "WS02"), ("Status", "0xc000006a")),
        ]);
        Assert.Equal(2, r.Identities.Single(i => i.Sid == Alice).Activity.Count);
        var carol = Assert.Single(r.Identities, i => i.KnownByNameOnly);
        Assert.Equal(1, carol.FailedLogons);

        var amb = IdentityReconstruction.Build([
            Logon("WS01", 0, Alice, "admin", "0x1", domain: "LAB"),
            Logon("WS01", 1, "S-1-5-21-5-5-5-500", "admin", "0x2", domain: "WS01"),
            Ev("DC01", "4776", 2, ("TargetUserName", "admin"), ("Workstation", "WS01"), ("Status", "0x0")),
        ]);
        Assert.Contains(amb.Identities, i => i.KnownByNameOnly);                // never attributed to either SID by name alone
    }

    [Fact]
    public void The_report_written_by_the_pipeline_reads_back_identically_for_a_reopened_case()
    {
        var r = IdentityReconstruction.Build([Logon("WS01", 0, Alice, "alice", "0x1"), Ev("DC01", "4781", 5, ("TargetSid", Alice), ("OldTargetUserName", "alice"), ("NewTargetUserName", "alice2"))]);
        var path = Path.Combine(Path.GetTempPath(), "ladfir_id_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            IdentityReport.Write(path, r);
            var back = IdentityReport.Read(path)!;
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(r), System.Text.Json.JsonSerializer.Serialize(back));
            Assert.Equal("alice → alice2", back.Identities.Single(i => i.Sid == Alice).NameHistory);
        }
        finally { File.Delete(path); }
        Assert.Null(IdentityReport.Read(path));                                     // a case analysed before 6.10 has none
    }

    [Fact]
    public void A_logoff_without_its_logon_is_a_low_confidence_session()
    {
        var r = IdentityReconstruction.Build([Logoff("WS01", 5, Alice, "0x77")]);
        var s = Assert.Single(r.Sessions);
        Assert.Equal("scăzută", s.Confidence);
        Assert.Empty(s.StartUtc);
    }
}
