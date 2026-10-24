using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

public class Wp11RemoteTests
{
    private const string TsLsm = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";
    private const string TsRcm = "Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational";
    private const string WinRm = "Microsoft-Windows-WinRM/Operational";

    private static TimelineEvent Logon(double m, string type, string ip, string user = "bob", string logonId = "0x1111") =>
        Sec(4624, m, ("TargetUserName", user), ("TargetDomainName", "CORP"), ("LogonType", type), ("IpAddress", ip), ("TargetLogonId", logonId));

    [Fact]
    public void Collector_now_exports_the_WinRM_and_OpenSSH_channels()
    {
        Assert.Contains("Microsoft-Windows-WinRM/Operational", EventLogCollector.Channels);
        Assert.Contains("OpenSSH/Operational", EventLogCollector.Channels);
        Assert.Equal(EventLogCollector.Channels.Length, EventLogCollector.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- REMOTE-RDP-INTERNAL ----
    [Fact]
    public void Internal_RDP_logon_is_Low_and_public_RDP_is_left_to_the_existing_rule()
    {
        var f = One(Run([Logon(0, "10", "10.0.0.9")]), "REMOTE-RDP-INTERNAL");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("CORP\\bob", f.Description);
        Assert.Contains("10.0.0.9", f.Description);
        Assert.DoesNotContain(Run([Logon(0, "10", "93.184.216.34"), Logon(0, "10", "8.8.8.8")]), x => x.RuleId == "REMOTE-RDP-INTERNAL");
        Assert.DoesNotContain(Run([Logon(0, "3", "10.0.0.9")]), x => x.RuleId == "REMOTE-RDP-INTERNAL");   // network logon, not RDP
    }

    [Fact]
    public void TerminalServices_events_count_and_agreeing_sources_are_Correlated()
    {
        var lsm = Ev(TsLsm, 21, 1, "Microsoft-Windows-TerminalServices-LocalSessionManager", ("User", @"CORP\bob"), ("SessionID", "3"), ("Address", "10.0.0.9"));
        var rcm = Ev(TsRcm, 1149, 0.5, "Microsoft-Windows-TerminalServices-RemoteConnectionManager", ("Param1", "bob"), ("Param2", "CORP"), ("Param3", "10.0.0.9"));
        var only = One(Run([lsm]), "REMOTE-RDP-INTERNAL");
        Assert.Equal(Classification.Direct, only.Classification);
        var both = One(Run([lsm, rcm, Logon(0, "10", "10.0.0.9")]), "REMOTE-RDP-INTERNAL");
        Assert.Equal(Classification.Correlated, both.Classification);
        Assert.Contains("21", both.Description);
        Assert.Contains("1149", both.Description);
    }

    [Fact]
    public void Local_and_public_TerminalServices_addresses_are_ignored()
    {
        var local = Ev(TsLsm, 21, 1, "x", ("User", @"CORP\bob"), ("Address", "LOCAL"));
        var pub = Ev(TsLsm, 25, 1, "x", ("User", @"CORP\bob"), ("Address", "93.184.216.34"));
        var pub2 = Ev(TsRcm, 1149, 1, "x", ("Param1", "bob"), ("Param2", "CORP"), ("Param3", "8.8.4.4"));
        Assert.DoesNotContain(Run([local, pub, pub2]), x => x.RuleId == "REMOTE-RDP-INTERNAL");
    }

    // ---- REMOTE-WINRM ----
    private static TimelineEvent Wsmprov(double m, string logonId = "0x1111", bool sysmon = false) => sysmon
        ? Sysmon(1, m, ("Image", @"C:\Windows\System32\wsmprovhost.exe"), ("CommandLine", "C:\\Windows\\system32\\wsmprovhost.exe -Embedding"), ("ProcessId", "4242"), ("LogonId", logonId), ("User", @"CORP\bob"))
        : Sec(4688, m, ("NewProcessName", @"C:\Windows\System32\wsmprovhost.exe"), ("CommandLine", "x"), ("NewProcessId", "0x1092"), ("SubjectLogonId", logonId), ("SubjectUserName", "bob"));

    [Fact]
    public void WinRM_server_shell_events_alone_are_Low_and_failures_alone_Info()
    {
        var f = One(Run([Ev(WinRm, 91, 0, "Microsoft-Windows-WinRM", ("resourceUri", "http://schemas.microsoft.com/powershell/Microsoft.PowerShell"))]), "REMOTE-WINRM");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("91", f.Description);
        var fail = One(Run([Ev(WinRm, 142, 0, "Microsoft-Windows-WinRM", ("errorCode", "5"))]), "REMOTE-WINRM");
        Assert.Equal(Severity.Info, fail.Severity);
        Assert.DoesNotContain(Run([Ev(WinRm, 81, 0, "x")]), x => x.RuleId == "REMOTE-WINRM");
    }

    [Fact]
    public void Wsmprovhost_with_a_matching_network_logon_is_Medium_and_Correlated()
    {
        var f = One(Run([Logon(0, "3", "10.0.0.7"), Wsmprov(1)]), "REMOTE-WINRM");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Contains("10.0.0.7", f.Description);
        Assert.Contains("wsmprovhost", f.Description);
        var sys = One(Run([Logon(0, "3", "10.0.0.7"), Wsmprov(1, sysmon: true)]), "REMOTE-WINRM");
        Assert.Equal(Classification.Correlated, sys.Classification);
    }

    [Fact]
    public void Wsmprovhost_without_a_logon_stays_Direct_and_does_not_invent_the_source()
    {
        var f = One(Run([Wsmprov(1)]), "REMOTE-WINRM");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Contains("nicio autentificare de tip 3", f.Description);
        // a logon of another session 3 hours earlier is not a match
        var far = One(Run([Logon(-180, "3", "10.0.0.7", logonId: "0x9999"), Wsmprov(1)]), "REMOTE-WINRM");
        Assert.Equal(Classification.Direct, far.Classification);
    }

    // ---- REMOTE-PSEXEC ----
    private static TimelineEvent Svc7045(double m, string name, string image) =>
        Sys(7045, m, "Service Control Manager", ("ServiceName", name), ("ImagePath", image), ("ServiceType", "user mode service"), ("StartType", "demand start"), ("AccountName", "LocalSystem"));

    private static TimelineEvent AdminExe(double m, string file, string share = "ADMIN$") =>
        Sec(5145, m, ("ShareName", $@"\\*\{share}"), ("RelativeTargetName", file), ("SubjectUserName", "bob"), ("SubjectDomainName", "CORP"), ("IpAddress", "10.0.0.7"), ("AccessMask", "0x2"));

    [Fact]
    public void PSEXESVC_service_is_Medium_and_with_the_ADMIN_share_copy_it_is_High()
    {
        var alone = One(Run([Svc7045(1, "PSEXESVC", @"%SystemRoot%\PSEXESVC.exe")]), "REMOTE-PSEXEC");
        Assert.Equal(Severity.Medium, alone.Severity);
        Assert.Equal(SemanticType.Configuration, alone.SemanticType);
        Assert.Contains("nu dovedește", alone.Description);
        var paired = One(Run([AdminExe(0, "PSEXESVC.exe"), Svc7045(1, "PSEXESVC", @"%SystemRoot%\PSEXESVC.exe")]), "REMOTE-PSEXEC");
        Assert.Equal(Severity.High, paired.Severity);
        Assert.Equal(Classification.Correlated, paired.Classification);
        Assert.Equal(2, paired.SupportingEvidence.Count);
    }

    [Fact]
    public void Random_name_service_from_the_Windows_folder_needs_the_ADMIN_share_copy_to_be_High()
    {
        var alone = One(Run([Svc7045(1, "xKq3Zt9a", @"%SystemRoot%\xKq3Zt9a.exe")]), "REMOTE-PSEXEC");
        Assert.Equal(Severity.Medium, alone.Severity);
        var paired = One(Run([AdminExe(0, "xKq3Zt9a.exe"), Svc7045(1, "xKq3Zt9a", @"%SystemRoot%\xKq3Zt9a.exe")]), "REMOTE-PSEXEC");
        Assert.Equal(Severity.High, paired.Severity);
        var unrelated = Run([AdminExe(0, "other.exe"), Svc7045(1, "xKq3Zt9a", @"%SystemRoot%\xKq3Zt9a.exe")]).Where(x => x.RuleId == "REMOTE-PSEXEC").ToList();
        Assert.Equal(Severity.Medium, Assert.Single(unrelated, x => x.Severity == Severity.Medium).Severity);   // the service
        Assert.Single(unrelated, x => x.Severity == Severity.Low);                                               // the unrelated copy stays its own Low candidate
    }

    [Fact]
    public void Ordinary_services_and_ordinary_shares_do_not_fire()
    {
        Assert.DoesNotContain(Run([Svc7045(1, "Spooler", @"C:\Windows\System32\spoolsv.exe"), Svc7045(2, "Vendor", @"""C:\Program Files\Vendor\svc.exe"" -run")]), x => x.RuleId == "REMOTE-PSEXEC");
        Assert.DoesNotContain(Run([AdminExe(0, "report.docx"), AdminExe(0, "tool.exe", share: "Public")]), x => x.RuleId == "REMOTE-PSEXEC");
    }

    [Fact]
    public void Executable_copied_to_ADMIN_share_without_a_service_is_Low()
    {
        var f = One(Run([AdminExe(0, "payload.exe")]), "REMOTE-PSEXEC");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(Classification.Candidate, f.Classification);
    }

    [Fact]
    public void PsExec_approved_in_the_profile_is_Info()
    {
        var profile = ProfileWithSoftware(("PsExec (Sysinternals)", "Microsoft Sysinternals", "", ""));
        var f = One(Run([Svc7045(1, "PSEXESVC", @"%SystemRoot%\PSEXESVC.exe")], profile), "REMOTE-PSEXEC");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("aprobat în profil", f.Description);
        Assert.Equal(Severity.Medium, One(Run([Svc7045(1, "PSEXESVC", @"%SystemRoot%\PSEXESVC.exe")]), "REMOTE-PSEXEC").Severity);
    }

    // ---- REMOTE-SSH ----
    private static TimelineEvent Ssh(double m, string payload) => Ev("OpenSSH/Operational", 4, m, "OpenSSH", ("process", "sshd"), ("payload", payload));

    [Fact]
    public void Accepted_SSH_logon_is_Low_and_from_a_public_address_Medium()
    {
        var f = One(Run([Ssh(0, "Accepted publickey for alice from 10.0.0.5 port 50000 ssh2: RSA SHA256:abc")]), "REMOTE-SSH");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("alice", f.Description);
        Assert.Contains("10.0.0.5", f.Description);
        var pub = One(Run([Ssh(0, "Accepted password for root from 203.0.113.50 port 4000 ssh2")]), "REMOTE-SSH");
        Assert.Equal(Severity.Low, pub.Severity);   // 203.0.113.0/24 is documentation space, not routable
        var ext = One(Run([Ssh(0, "Accepted password for root from 8.8.8.8 port 4000 ssh2")]), "REMOTE-SSH");
        Assert.Equal(Severity.Medium, ext.Severity);
    }

    [Fact]
    public void Failed_SSH_attempts_alone_are_not_a_remote_access_finding_but_are_counted_when_there_is_one()
    {
        Assert.DoesNotContain(Run([Ssh(0, "Failed password for invalid user admin from 10.0.0.5 port 1 ssh2")]), x => x.RuleId == "REMOTE-SSH");
        var f = One(Run([Ssh(0, "Failed password for alice from 10.0.0.5 port 1 ssh2"), Ssh(1, "Failed password for alice from 10.0.0.5 port 2 ssh2"), Ssh(2, "Accepted password for alice from 10.0.0.5 port 3 ssh2")]), "REMOTE-SSH");
        Assert.Contains("2 încercări eșuate", f.Description);
    }
}
