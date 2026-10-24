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
