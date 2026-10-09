using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

public class Wp11DnsTests
{
    private const string DnsClient = "Microsoft-Windows-DNS-Client/Operational";

    private static TimelineEvent Query22(double m, string domain, string image) =>
        Sysmon(22, m, ("QueryName", domain), ("Image", image), ("ProcessId", "4242"), ("QueryResults", "type:  5 x.net;93.184.216.34;"));

    private static TimelineEvent Client(int id, double m, string domain, int pid) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(m), "", "test"), Source = "EventLog:" + DnsClient, EventId = id.ToString(), Provider = "Microsoft-Windows-DNS-Client",
        EvidenceId = "EV-1", Locator = $"EventRecordID={900000 + (int)(m * 10) + id}", Summary = "t", Pid = pid,
        Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["QueryName"] = domain, ["QueryType"] = "1" },
    };

    private const string Rundll = @"C:\Windows\System32\rundll32.exe";
    private const string Powershell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    [Fact]
    public void Rare_domain_resolved_by_a_high_interest_LOLBin_is_Medium_and_other_LOLBins_Low()
    {
        var f = One(Run([Query22(0, "update-check.evil-example.net", Rundll)]), "DNS-RARE-DOMAIN");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Contains("update-check.evil-example.net", f.Description);
        Assert.Contains("nu dovedește", f.Description);   // a lookup is not a connection
        Assert.Equal(Severity.Low, One(Run([Query22(0, "x.rare-example.org", Powershell)]), "DNS-RARE-DOMAIN").Severity);
    }

    [Fact]
    public void User_path_process_is_Low_and_ordinary_processes_are_not_findings()
    {
        Assert.Equal(Severity.Low, One(Run([Query22(0, "cdn.rare-example.org", @"C:\Users\bob\AppData\Local\Temp\x.exe")]), "DNS-RARE-DOMAIN").Severity);
        Assert.DoesNotContain(Run([Query22(0, "cdn.rare-example.org", @"C:\Program Files\Mozilla Firefox\firefox.exe")]), x => x.RuleId == "DNS-RARE-DOMAIN");
    }

    [Fact]
    public void A_domain_seen_more_than_N_times_in_the_case_is_not_rare()
    {
        var many = Enumerable.Range(0, 3).Select(i => Query22(i, "busy.example.org", Rundll)).ToList();
        Assert.DoesNotContain(Run(many), x => x.RuleId == "DNS-RARE-DOMAIN");
        Assert.Single(Run(many, options: new Wp11Options { DnsRareMaxCount = 3 }), x => x.RuleId == "DNS-RARE-DOMAIN");
        // the count is over the whole case, other processes included
        var shared = Run([Query22(0, "shared.example.org", Rundll), Query22(1, "shared.example.org", @"C:\Program Files\App\a.exe"), Query22(2, "shared.example.org", @"C:\Program Files\App\a.exe")]);
        Assert.DoesNotContain(shared, x => x.RuleId == "DNS-RARE-DOMAIN");
    }

    [Fact]
    public void Local_names_benign_infrastructure_IPs_and_profile_destinations_are_skipped()
    {
        var q = new[] { "printer.local", "host", "download.windowsupdate.com", "8.8.8.8", "ntp.corp-approved.example.org" }.Select((d, i) => Query22(i, d, Rundll)).ToList();
        var profile = new ProcedureProfile { NetworkDestinations = [new NetworkDestinationRow { Address = "corp-approved.example.org", Zone = "LAN", Purpose = "NTP" }] };
        Assert.DoesNotContain(Run(q, profile), x => x.RuleId == "DNS-RARE-DOMAIN");
        Assert.Single(Run(q), x => x.RuleId == "DNS-RARE-DOMAIN");   // without the profile only the approved-destination one remains
    }

    [Fact]
    public void One_finding_per_process_lists_its_rare_domains()
    {
        var found = Run([Query22(0, "a.rare-example.org", Rundll), Query22(1, "b.rare-example.org", Rundll), Query22(2, "c.rare-example.org", Powershell)]).Where(x => x.RuleId == "DNS-RARE-DOMAIN").ToList();
        Assert.Equal(2, found.Count);
        Assert.Contains("a.rare-example.org", found.Single(x => x.Severity == Severity.Medium).Description);
        Assert.Contains("b.rare-example.org", found.Single(x => x.Severity == Severity.Medium).Description);
    }

    [Fact]
    public void DNS_Client_events_use_the_process_id_only_as_a_Candidate_attribution()
    {
        var start = Sec(4688, -1, ("NewProcessName", Rundll), ("NewProcessId", "0x1092"), ("CommandLine", "rundll32 x"), ("SubjectUserName", "bob"));
        var f = One(Run([start, Client(3006, 0, "beacon.rare-example.org", 4242), Client(3008, 0, "beacon.rare-example.org", 4242)]), "DNS-RARE-DOMAIN");
        Assert.Equal(Classification.Candidate, f.Classification);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("PID", f.Description);
        // the same query logged by 3006 and 3008 counts once: it is not "seen twice" because of the event pair
        // no process start for that PID: nothing is guessed
        Assert.DoesNotContain(Run([Client(3006, 0, "beacon.rare-example.org", 999)]), x => x.RuleId == "DNS-RARE-DOMAIN");
        // a process that started AFTER the query cannot be its resolver
        Assert.DoesNotContain(Run([Client(3006, 0, "beacon.rare-example.org", 4242), Sec(4688, 5, ("NewProcessName", Rundll), ("NewProcessId", "0x1092"))]), x => x.RuleId == "DNS-RARE-DOMAIN");
    }

    private static TimelineEvent NameServer(double m, string iface, string servers) =>
        Row("SystemConfig", m, f: [("Interface", iface), ("NameServer", servers)]);

    [Fact]
    public void Changed_name_server_is_Low_for_private_and_Medium_for_a_public_server()
    {
        var priv = One(Run([NameServer(0, "{A}", "10.0.0.1"), NameServer(60, "{A}", "10.0.0.2")]), "DNS-SERVER-CHANGED");
        Assert.Equal(Severity.Low, priv.Severity);
        Assert.Equal(SemanticType.Configuration, priv.SemanticType);
        var pub = One(Run([NameServer(0, "{A}", "10.0.0.1"), NameServer(60, "{A}", "8.8.8.8")]), "DNS-SERVER-CHANGED");
        Assert.Equal(Severity.Medium, pub.Severity);
        Assert.Contains("10.0.0.1", pub.Description);
        Assert.Contains("8.8.8.8", pub.Description);
    }

    [Fact]
    public void A_server_approved_in_the_profile_is_Info_and_a_single_value_or_NetworkProfile_alone_is_nothing()
    {
        var profile = new ProcedureProfile { NetworkDestinations = [new NetworkDestinationRow { Address = "8.8.8.8", Zone = "WAN", Purpose = "DNS" }] };
        Assert.Equal(Severity.Info, One(Run([NameServer(0, "{A}", "10.0.0.1"), NameServer(60, "{A}", "8.8.8.8")], profile), "DNS-SERVER-CHANGED").Severity);
        Assert.DoesNotContain(Run([NameServer(0, "{A}", "8.8.8.8"), NameServer(60, "{A}", "8.8.8.8")]), x => x.RuleId == "DNS-SERVER-CHANGED");
        Assert.DoesNotContain(Run([NameServer(0, "{A}", "8.8.8.8")]), x => x.RuleId == "DNS-SERVER-CHANGED");
        Assert.DoesNotContain(Run([Ev("Microsoft-Windows-NetworkProfile/Operational", 10000, 0, "Microsoft-Windows-NetworkProfile", ("Name", "Network 2"))]), x => x.RuleId == "DNS-SERVER-CHANGED");
    }
}

public class Wp11ToolTests
{
    private static TimelineEvent Prefetch(double m, string exe, string path = @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe") =>
        Row("Prefetch", m, exe, path, f: [("RunCount", "4"), ("ReferencedFiles", @"\VOLUME{01d}" + path.Replace("C:", ""))]);

    [Fact]
    public void Execution_artifact_gives_REMOTE_TOOL_EXECUTED_Medium_with_Execution_semantic()
    {
        var f = One(Run([Prefetch(0, "ANYDESK.EXE")]), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(SemanticType.Execution, f.SemanticType);
        Assert.Equal("T1219", f.MitreTechniqueId);
        Assert.Contains("AnyDesk", f.Title);
        Assert.DoesNotContain(Run([Prefetch(0, "ANYDESK.EXE")]), x => x.RuleId == "REMOTE-TOOL-PRESENT");
    }

    [Theory]
    [InlineData("teamviewer.exe", "TeamViewer")]
    [InlineData("tv_x64.exe", "TeamViewer")]
    [InlineData("winvnc.exe", "VNC")]
    [InlineData("tvnserver.exe", "VNC")]
    [InlineData("rustdesk.exe", "RustDesk")]
    [InlineData("screenconnect.clientservice.exe", "ScreenConnect")]
    [InlineData("srservice.exe", "Splashtop")]
    [InlineData("remoting_host.exe", "Chrome Remote Desktop")]
    [InlineData("meshagent.exe", "MeshCentral")]
    public void Every_named_tool_is_recognized_from_a_process_start(string exe, string tool)
    {
        var f = One(Run([Sec(4688, 0, ("NewProcessName", @"C:\Tools\" + exe), ("CommandLine", exe), ("SubjectUserName", "bob"))]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains(tool, f.Title);
        var sys = One(Run([Sysmon(1, 0, ("Image", @"C:\Tools\" + exe), ("CommandLine", exe))]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains(tool, sys.Title);
    }

    [Fact]
    public void Presence_artifacts_alone_are_PRESENT_Low_and_never_executed()
    {
        var amcache = One(Run([Row("Amcache", 0, "anydesk.exe", @"C:\Users\bob\Downloads\AnyDesk.exe", "abc")]), "REMOTE-TOOL-PRESENT");
        Assert.Equal(Severity.Low, amcache.Severity);
        Assert.Equal(SemanticType.Presence, amcache.SemanticType);
        Assert.Contains("nu dovedește execuția", amcache.Description);
        var svc = One(Run([Sys(7045, 0, "Service Control Manager", ("ServiceName", "AnyDesk Service"), ("ImagePath", @"""C:\Program Files (x86)\AnyDesk\AnyDesk.exe"" --service"), ("AccountName", "LocalSystem"))]), "REMOTE-TOOL-PRESENT");
        Assert.Equal(Severity.Low, svc.Severity);
        var msi = One(Run([Ev("Application", 11707, 0, "MsiInstaller", ("Data0", "Product: TeamViewer 15 -- Installation completed successfully."))]), "REMOTE-TOOL-PRESENT");
        Assert.Contains("TeamViewer", msi.Title);
        var log = One(Run([Row("UsnJournal", 0, "ad.trace", @"C:\ProgramData\AnyDesk\ad.trace")]), "REMOTE-TOOL-PRESENT");
        Assert.Contains("AnyDesk", log.Title);
    }

    [Fact]
    public void Execution_wins_over_presence_and_lists_both()
    {
        var found = Run([Row("Amcache", 0, "anydesk.exe", @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", "abc"), Prefetch(5, "ANYDESK.EXE")]);
        var f = One(found, "REMOTE-TOOL-EXECUTED");
        Assert.Equal(2, f.SupportingEvidence.Count);
        Assert.DoesNotContain(found, x => x.RuleId == "REMOTE-TOOL-PRESENT");
    }

    [Fact]
    public void Unrelated_programs_are_not_remote_tools()
    {
        Assert.DoesNotContain(Run([Prefetch(0, "NOTEPAD.EXE", @"C:\Windows\notepad.exe"), Row("Amcache", 0, "7z.exe", @"C:\Program Files\7-Zip\7z.exe")]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
    }

    [Fact]
    public void Approved_in_the_profile_by_path_is_Info_but_a_portable_copy_elsewhere_is_not()
    {
        var profile = ProfileWithSoftware(("AnyDesk", "philandro Software GmbH", @"C:\Program Files (x86)\AnyDesk\*", ""));
        var ok = One(Run([Prefetch(0, "ANYDESK.EXE")], profile), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Info, ok.Severity);
        Assert.Contains("aprobat în profil", ok.Title + ok.Description);
        var portable = One(Run([Prefetch(0, "ANYDESK.EXE", @"C:\Users\bob\Downloads\AnyDesk.exe")], profile), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Medium, portable.Severity);
        Assert.DoesNotContain("aprobat în profil", portable.Title + portable.Description);
    }

    [Fact]
    public void Approved_by_name_when_the_row_has_no_path_and_by_hash_when_it_has_one()
    {
        var byName = ProfileWithSoftware(("TeamViewer", "TeamViewer Germany GmbH", "", ""));
        var tv = Sec(4688, 0, ("NewProcessName", @"C:\Program Files\TeamViewer\TeamViewer.exe"), ("CommandLine", "x"), ("SubjectUserName", "bob"));
        Assert.Equal(Severity.Info, One(Run([tv], byName), "REMOTE-TOOL-EXECUTED").Severity);
        var sha = new string('a', 64);
        var byHash = ProfileWithSoftware(("TeamViewer", "", "", sha));
        Assert.Equal(Severity.Medium, One(Run([tv], byHash), "REMOTE-TOOL-EXECUTED").Severity);   // a hash row needs the hash in the evidence
        var withHash = Sysmon(1, 0, ("Image", @"C:\Program Files\TeamViewer\TeamViewer.exe"), ("CommandLine", "x"), ("Hashes", "MD5=AA,SHA256=" + sha.ToUpperInvariant() + ",IMPHASH=BB"));
        Assert.Equal(Severity.Info, One(Run([withHash], byHash), "REMOTE-TOOL-EXECUTED").Severity);      // Sysmon 1 carries the hash
        Assert.Equal(Severity.Medium, One(Run([tv]), "REMOTE-TOOL-EXECUTED").Severity);                    // no profile: nothing is approved
    }

    [Fact]
    public void The_tool_list_is_data()
    {
        var custom = Wp11Data.FromJson("""{"agents":[]}""",
            """{"tools":[{"id":"acme","name":"AcmeRemote","aliases":["acmeremote"],"executables":["acmerd.exe"],"serviceNames":[],"displayContains":[],"productContains":[],"logMarkers":[]}]}""", "{}");
        Assert.Contains("AcmeRemote", One(Run([Prefetch(0, "ACMERD.EXE", @"C:\x\acmerd.exe")], data: custom), "REMOTE-TOOL-EXECUTED").Title);
        Assert.DoesNotContain(Run([Prefetch(0, "ANYDESK.EXE")], data: custom), x => x.RuleId.StartsWith("REMOTE-TOOL"));
        Assert.DoesNotContain(Run([Prefetch(0, "ACMERD.EXE", @"C:\x\acmerd.exe")]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
        Assert.True(Wp11Data.Default.Tools.Tools.Count >= 8);
    }
}
