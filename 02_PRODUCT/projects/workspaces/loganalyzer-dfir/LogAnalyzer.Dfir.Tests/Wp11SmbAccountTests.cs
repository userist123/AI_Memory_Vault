using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

public class Wp11SmbTests
{
    private static TimelineEvent Share5140(double m, string share, string user = "alice", string ip = "10.0.0.5") =>
        Sec(5140, m, ("ShareName", $@"\\*\{share}"), ("SubjectUserName", user), ("SubjectDomainName", "CORP"), ("IpAddress", ip));

    private static TimelineEvent Obj5145(double m, string share, string target, string user = "alice", string ip = "10.0.0.5") =>
        Sec(5145, m, ("ShareName", $@"\\*\{share}"), ("RelativeTargetName", target), ("SubjectUserName", user), ("SubjectDomainName", "CORP"), ("IpAddress", ip), ("AccessMask", "0x2"));

    [Fact]
    public void Admin_share_from_a_remote_host_is_Low_with_counts_only()
    {
        var f = One(Run([Share5140(0, "C$"), Share5140(1, "C$"), Obj5145(1, "C$", @"Users\bob\notes.txt")]), "SMB-ADMIN-SHARE");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("CORP\\alice", f.Title + f.Description);
        Assert.Contains("10.0.0.5", f.Description);
        Assert.Contains("2 × 5140", f.Description);
        Assert.Contains("1 × 5145", f.Description);
        Assert.Equal(SemanticType.Observation, f.SemanticType);
        Assert.DoesNotContain("volum", f.Description, StringComparison.OrdinalIgnoreCase);   // thresholds belong to WP17
    }

    [Fact]
    public void IPC_only_is_Info_and_named_pipes_of_interest_are_listed()
    {
        var info = One(Run([Share5140(0, "IPC$")]), "SMB-ADMIN-SHARE");
        Assert.Equal(Severity.Info, info.Severity);
        var reg = One(Run([Share5140(0, "IPC$"), Obj5145(0, "IPC$", "winreg")]), "SMB-ADMIN-SHARE");
        Assert.Equal(Severity.Low, reg.Severity);
        Assert.Contains("winreg", reg.Description);
        Assert.Contains("registru", reg.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Executable_written_through_ADMIN_share_is_Medium()
    {
        var f = One(Run([Share5140(0, "ADMIN$"), Obj5145(0, "ADMIN$", "abcd1234.exe")]), "SMB-ADMIN-SHARE");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("abcd1234.exe", f.Description);
    }

    [Fact]
    public void Local_loopback_machine_accounts_and_ordinary_shares_are_not_findings()
    {
        Assert.DoesNotContain(Run([Share5140(0, "C$", ip: "127.0.0.1"), Share5140(0, "C$", ip: "::1"), Share5140(0, "C$", ip: "-")]), x => x.RuleId == "SMB-ADMIN-SHARE");
        Assert.DoesNotContain(Run([Share5140(0, "C$", user: "FILESRV$")]), x => x.RuleId == "SMB-ADMIN-SHARE");
        Assert.DoesNotContain(Run([Share5140(0, "Public")]), x => x.RuleId == "SMB-ADMIN-SHARE");
    }

    [Fact]
    public void Each_account_and_source_host_is_its_own_finding()
    {
        var found = Run([Share5140(0, "C$"), Share5140(0, "C$", user: "bob"), Share5140(0, "C$", ip: "10.0.0.9")]).Where(x => x.RuleId == "SMB-ADMIN-SHARE").ToList();
        Assert.Equal(3, found.Count);
    }

    private static TimelineEvent Perm4670(double m, string type, string name, string oldSd = "D:PAI(A;;FA;;;BA)", string newSd = "D:PAI(A;;FA;;;BA)", string user = "alice", string proc = @"C:\Windows\explorer.exe") =>
        Sec(4670, m, ("ObjectType", type), ("ObjectName", name), ("OldSd", oldSd), ("NewSd", newSd), ("SubjectUserName", user), ("SubjectDomainName", "CORP"), ("ProcessName", proc));

    [Fact]
    public void Permission_change_on_a_file_object_is_Low()
    {
        var f = One(Run([Perm4670(0, "File", @"D:\Shares\Finance\plan.xlsx"), Perm4670(1, "Directory", @"D:\Shares\Finance")]), "SMB-SHARE-PERMS-CHANGED");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("2 obiecte", f.Description);
        Assert.Contains(@"D:\Shares\Finance", f.Description);
        Assert.Equal(2, f.SupportingEvidence.Count);
    }

    [Fact]
    public void Newly_granted_access_to_Everyone_is_Medium()
    {
        var f = One(Run([Perm4670(0, "Directory", @"D:\Shares\Finance", newSd: "D:PAI(A;;FA;;;BA)(A;OICI;FA;;;WD)")]), "SMB-SHARE-PERMS-CHANGED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("Everyone", f.Description);
        // already present in the old descriptor: not a new grant
        var same = One(Run([Perm4670(0, "Directory", @"D:\Shares\Finance", oldSd: "D:PAI(A;OICI;FA;;;WD)", newSd: "D:PAI(A;OICI;FA;;;WD)")]), "SMB-SHARE-PERMS-CHANGED");
        Assert.Equal(Severity.Low, same.Severity);
    }

    [Fact]
    public void Registry_and_service_objects_are_not_share_or_file_permission_changes()
    {
        Assert.DoesNotContain(Run([Perm4670(0, "Key", @"\REGISTRY\MACHINE\SOFTWARE\X"), Perm4670(0, "Service", "Spooler")]), x => x.RuleId == "SMB-SHARE-PERMS-CHANGED");
    }

    [Fact]
    public void Findings_reach_Correlation_and_get_the_contract()
    {
        var found = Correlation.Run([Share5140(0, "ADMIN$"), Obj5145(0, "ADMIN$", "x.exe")]);
        var f = One(found, "SMB-ADMIN-SHARE");
        Assert.NotEmpty(FindingContract.Enrich([f]).Single().Limitations);
        Assert.NotEmpty(f.MissingEvidence);
    }
}

public class Wp11AccountTests
{
    private static TimelineEvent Created(double m, string name = "svc_backup", string sid = "S-1-5-21-1-2-3-1105", string by = "admin1") =>
        Sec(4720, m, ("TargetUserName", name), ("TargetDomainName", "CORP"), ("TargetSid", sid), ("SubjectUserName", by), ("SubjectDomainName", "CORP"));

    private static TimelineEvent AddedToGroup(double m, string group, string groupSid, string member = "svc_backup", string memberSid = "S-1-5-21-1-2-3-1105", int id = 4732) =>
        Sec(id, m, ("MemberName", $"CN={member},CN=Users,DC=corp,DC=local"), ("MemberSid", memberSid), ("TargetUserName", group), ("TargetSid", groupSid), ("SubjectUserName", "admin1"), ("SubjectDomainName", "CORP"));

    private static TimelineEvent Logon(double m, string name = "svc_backup", string type = "2", string ip = "-", string sid = "S-1-5-21-1-2-3-1105") =>
        Sec(4624, m, ("TargetUserName", name), ("TargetDomainName", "CORP"), ("TargetUserSid", sid), ("LogonType", type), ("IpAddress", ip));

    [Fact]
    public void Created_and_never_used_says_so_and_is_Low()
    {
        var found = Run([Created(0), Sec(4722, 1, ("TargetUserName", "svc_backup"), ("TargetSid", "S-1-5-21-1-2-3-1105"))]);
        var f = One(found, "ACCOUNT-CREATED");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("creat, nefolosit în dovezile colectate", f.Description);
        Assert.Contains("4722", f.Description);
        Assert.DoesNotContain(found, x => x.RuleId == "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(SemanticType.Observation, f.SemanticType);
    }

    [Fact]
    public void Account_created_by_SYSTEM_provisioning_is_Info()
    {
        var f = One(Run([Created(0, by: "SYSTEM")]), "ACCOUNT-CREATED");
        Assert.Equal(Severity.Info, f.Severity);
    }

    [Fact]
    public void No_logon_events_at_all_is_not_read_as_unused()
    {
        var f = One(Run([Created(0)]), "ACCOUNT-CREATED");
        Assert.Contains("nu s-a colectat nicio autentificare", f.Description);
    }

    [Fact]
    public void Added_to_Administrators_is_Medium_by_name_or_by_SID()
    {
        var byName = One(Run([AddedToGroup(1, "Administrators", "S-1-5-32-544")]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal(Severity.Medium, byName.Severity);
        Assert.Contains("svc_backup", byName.Description);
        var bySid = One(Run([AddedToGroup(1, "Administratori", "S-1-5-32-544")]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal(Severity.Medium, bySid.Severity);
        var domainAdmins = One(Run([AddedToGroup(1, "Grup necunoscut", "S-1-5-21-1-2-3-512", id: 4728)]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal(Severity.Medium, domainAdmins.Severity);
        // lab SRV01 2026-11-06: the domain join adds Domain Admins (RID 512, member "-") to the local Administrators; the title names it, the
        // alternative explanation says what Windows does at the join
        var join = One(Run([Sec(4732, 1, ("MemberName", "-"), ("MemberSid", "S-1-5-21-1061-450-3444-512"), ("TargetUserName", "Administrators"), ("TargetSid", "S-1-5-32-544"),
            ("SubjectUserName", "Administrator"), ("SubjectDomainName", "LAB-SRV01"))]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal("Cont adăugat într-un grup privilegiat: Domain Admins → Administrators", join.Title);
        Assert.Contains(join.AlternativeExplanations, a => a.Contains("alăturarea stației în domeniu", StringComparison.Ordinal));
        Assert.Equal("", Wp11Rules.DomainGroupOfSid("S-1-5-21-1-2-3-1105"));
        Assert.Equal("", Wp11Rules.DomainGroupOfSid("S-1-5-32-544"));
        Assert.DoesNotContain(Run([AddedToGroup(1, "Users", "S-1-5-32-545")]), x => x.RuleId == "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        One(Run([AddedToGroup(1, "Remote Desktop Users", "S-1-5-32-555", id: 4756)]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
    }

    [Fact]
    public void Used_without_privilege_is_Low_locally_and_Medium_remotely()
    {
        var local = One(Run([Created(0), Logon(10)]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Low, local.Severity);
        var remote = One(Run([Created(0), Logon(10, type: "3", ip: "10.0.0.7")]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Medium, remote.Severity);
        Assert.Contains("10.0.0.7", remote.Description);
    }

    [Fact]
    public void Privileged_and_used_remotely_is_the_only_High()
    {
        var all = Run([Created(0), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10, type: "10", ip: "10.0.0.7")]);
        var f = One(all, "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Equal(SemanticType.Observation, f.SemanticType);
        Assert.NotEmpty(f.AlternativeExplanations);
        var localOnly = One(Run([Created(0), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10)]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Medium, localOnly.Severity);
    }

    private static TimelineEvent At(TimelineEvent e, long record) => new()
    {
        Time = e.Time, Source = e.Source, EventId = e.EventId, Provider = e.Provider, EvidenceId = e.EvidenceId, Summary = e.Summary, Fields = e.Fields, Locator = $"EventRecordID={record}",
    };

    [Fact]
    public void An_administrator_Windows_made_while_it_was_being_installed_is_Low_and_said_so_but_not_after_a_clear_or_later()
    {
        // lab WS02 2026-11-06: labadmin and defaultuser0, made administrators by Windows at setup, were Medium „created in this case and made admin”
        var setup = Run([At(Created(0, by: "SYSTEM"), 40), At(AddedToGroup(1, "Administrators", "S-1-5-32-544") is var g ? new TimelineEvent { Time = g.Time, Source = g.Source, EventId = g.EventId, EvidenceId = "EV", Summary = "x",
            Fields = new Dictionary<string, string>(g.Fields, StringComparer.OrdinalIgnoreCase) { ["SubjectUserName"] = "WS02$" } } : null!, 41), At(Logon(10), 300)]);
        var used = One(setup, "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Low, used.Severity);
        Assert.Contains("creat de Windows la instalare", used.PlainWhat);
        var group = One(setup, "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal(Severity.Low, group.Severity);
        Assert.Contains("Windows la instalare", group.PlainWhat);
        // the same, later in the log (record 9000) or in a log that was cleared: an attacker running as SYSTEM looks like this
        Assert.Equal(Severity.Medium, One(Run([At(Created(0, by: "SYSTEM"), 9000), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10)]), "ACCOUNT-CREATED-THEN-USED").Severity);
        Assert.Equal(Severity.Medium, One(Run([Sec(1102, -1), At(Created(0, by: "SYSTEM"), 40), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10)]), "ACCOUNT-CREATED-THEN-USED").Severity);
        // made by a person, even at setup time: never lowered
        Assert.Equal(Severity.Medium, One(Run([At(Created(0, by: "admin1"), 40), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10)]), "ACCOUNT-CREATED-THEN-USED").Severity);
    }

    [Fact]
    public void What_Windows_does_when_a_server_becomes_a_domain_controller_is_Windows_not_an_attacker()
    {
        // lab DC01 2026-11-07: „krbtgt was created by ANONYMOUS LOGON” and the Administrator's Domain/Enterprise/Schema Admins memberships were
        // reported as account activity (records ~1400, past the setup range)
        TimelineEvent Anon(TimelineEvent e) => new()
        {
            Time = e.Time, Source = e.Source, EventId = e.EventId, EvidenceId = "EV", Summary = "x",
            Fields = new Dictionary<string, string>(e.Fields, StringComparer.OrdinalIgnoreCase) { ["SubjectUserName"] = "ANONYMOUS LOGON", ["SubjectDomainName"] = "NT AUTHORITY", ["SubjectUserSid"] = "S-1-5-7" },
        };
        var krbtgt = At(Anon(Created(0, name: "krbtgt", sid: "S-1-5-21-1-2-3-502")), 1401);
        var addAdmin = AddedToGroup(1, "Domain Admins", "S-1-5-21-1-2-3-512", member: "Administrator", memberSid: "S-1-5-21-1-2-3-500", id: 4728);
        var bySystem = new TimelineEvent { Time = addAdmin.Time, Source = addAdmin.Source, EventId = addAdmin.EventId, EvidenceId = "EV", Summary = "x",
            Fields = new Dictionary<string, string>(addAdmin.Fields, StringComparer.OrdinalIgnoreCase) { ["SubjectUserName"] = "DC01$" } };
        var dc = Run([krbtgt, At(bySystem, 1450)]);
        var created = One(dc, "ACCOUNT-CREATED");
        Assert.Equal(Severity.Info, created.Severity);
        Assert.Contains("de Windows, când serverul a fost transformat în controler de domeniu", created.PlainWhat);
        var group = One(dc, "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal(Severity.Low, group.Severity);
        Assert.Contains("controler de domeniu", group.PlainWhat);

        // a person adding an account to Domain Admins in the same minutes is never lowered; neither is anything after a log clear
        Assert.Equal(Severity.Medium, One(Run([krbtgt, AddedToGroup(2, "Domain Admins", "S-1-5-21-1-2-3-512")]), "ACCOUNT-ADDED-PRIVILEGED-GROUP").Severity);
        Assert.Equal(Severity.Medium, One(Run([Sec(1102, -5), krbtgt, At(bySystem, 1450)]), "ACCOUNT-ADDED-PRIVILEGED-GROUP").Severity);
        // the same SYSTEM action an hour after the promotion is not part of it
        var late = new TimelineEvent { Time = AddedToGroup(60, "x", "y").Time, Source = bySystem.Source, EventId = bySystem.EventId, EvidenceId = "EV", Summary = "x", Fields = bySystem.Fields };
        Assert.Equal(Severity.Medium, One(Run([krbtgt, At(late, 3000)]), "ACCOUNT-ADDED-PRIVILEGED-GROUP").Severity);
    }

    [Fact]
    public void A_setup_account_created_by_SYSTEM_and_used_only_locally_is_Low_unless_privileged()
    {
        // lab VM 2026-11-06: setup accounts of every fresh install were Medium "created then used"
        var local = One(Run([Created(0, by: "SYSTEM"), Logon(10)]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Low, local.Severity);
        Assert.Contains("aprovizionare", local.Description);
        // review round 1: SYSTEM creating a local administrator is also what an attacker running as SYSTEM does
        var admin = One(Run([Created(0, by: "SYSTEM"), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10)]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Medium, admin.Severity);
        // the same account used from another host keeps its severity
        var remote = One(Run([Created(0, by: "SYSTEM"), AddedToGroup(5, "Administrators", "S-1-5-32-544"), Logon(10, type: "10", ip: "10.0.0.7")]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.High, remote.Severity);
    }

    [Fact]
    public void A_local_group_member_logged_as_a_bare_SID_is_named_from_the_accounts_own_events()
    {
        // lab VM 2026-11-06: 4732 logs MemberName "-" for a local account; the title showed "S-1-5-21-…-1002 → Administrators"
        var add = Sec(4732, 5, ("MemberName", "-"), ("MemberSid", "S-1-5-21-1-2-3-1002"), ("TargetUserName", "Administrators"), ("TargetSid", "S-1-5-32-544"), ("SubjectUserName", "it.admin"), ("SubjectDomainName", "LAB"));
        var f = One(Run([Created(0, name: "labbackup$", sid: "S-1-5-21-1-2-3-1002"), add]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.Equal("Cont adăugat într-un grup privilegiat: labbackup$ → Administrators", f.Title);
        Assert.Contains("S-1-5-21-1-2-3-1002", f.Description);
        // no event names the SID: the title keeps the SID rather than inventing a name
        var unknown = Sec(4732, 5, ("MemberName", "-"), ("MemberSid", "S-1-5-21-9-9-9-1003"), ("TargetUserName", "Administrators"), ("TargetSid", "S-1-5-32-544"), ("SubjectUserName", "it.admin"), ("SubjectDomainName", "LAB"));
        Assert.Contains("S-1-5-21-9-9-9-1003", One(Run([unknown]), "ACCOUNT-ADDED-PRIVILEGED-GROUP").Title);
    }

    [Fact]
    public void Logon_before_creation_or_by_another_account_does_not_count()
    {
        Assert.DoesNotContain(Run([Logon(-30), Created(0)]), x => x.RuleId == "ACCOUNT-CREATED-THEN-USED");
        Assert.DoesNotContain(Run([Created(0), Logon(10, name: "someone_else", sid: "S-1-5-21-1-2-3-2000")]), x => x.RuleId == "ACCOUNT-CREATED-THEN-USED");
    }

    [Fact]
    public void Explicit_credentials_4648_count_as_use_and_matching_works_by_name_without_SID()
    {
        var f = One(Run([Created(0, sid: ""), Sec(4648, 10, ("TargetUserName", "SVC_BACKUP"), ("TargetDomainName", "CORP"), ("SubjectUserName", "admin1"))]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("4648", f.Description);
    }
}
