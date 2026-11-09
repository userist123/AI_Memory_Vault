using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>REMOTE-TOOL-PRESENT / REMOTE-TOOL-EXECUTED: DNS evidence, user-writable folders, two tools in 24 h, Quick Assist, the new tools of the catalog.</summary>
public class RemoteToolUpgradeTests
{
    private static List<Finding> Rt(params TimelineEvent[] events) => Rt(null, events);

    private static List<Finding> Rt(ProcedureProfile? profile, params TimelineEvent[] events) =>
        Run(events, profile).Where(f => f.RuleId is "REMOTE-TOOL-PRESENT" or "REMOTE-TOOL-EXECUTED").ToList();

    private static TimelineEvent Start(double minute, string image) =>
        Sec(4688, minute, ("NewProcessName", image), ("CommandLine", image), ("SubjectUserName", "ana"));

    private static TimelineEvent Svc(double minute, string name, string image) =>
        Sys(7045, minute, "Service Control Manager", ("ServiceName", name), ("ImagePath", image), ("AccountName", "LocalSystem"));

    private static Finding Of(List<Finding> f, string title) => Assert.Single(f, x => x.Title.Contains(title));

    [Fact]
    public void Dns_query_is_presence_and_matches_on_label_boundary()
    {
        var yes = Assert.Single(Rt(Sysmon(22, 0, ("QueryName", "relay.screenconnect.com"))));
        Assert.Equal("REMOTE-TOOL-PRESENT", yes.RuleId);
        Assert.Equal(Severity.Low, yes.Severity);
        Assert.Contains("interogare DNS", yes.Description);
        Assert.Single(Rt(Sysmon(22, 0, ("QueryName", "ScreenConnect.com."))));
        Assert.Empty(Rt(Sysmon(22, 0, ("QueryName", "evilscreenconnect.com"))));
        Assert.Empty(Rt(Sysmon(22, 0, ("QueryName", "notanydesk.com"))));
        Assert.Empty(Rt(Sysmon(22, 0, ("QueryName", "google.com"))));
        Assert.Empty(Rt(Sysmon(22, 0, ("QueryName", "remotedesktop.google.com.evil.org"))));
        Assert.Contains("Chrome Remote Desktop", Assert.Single(Rt(Sysmon(22, 0, ("QueryName", "remotedesktop.google.com")))).Title);
    }

    [Fact]
    public void Service_from_Program_Files_stays_Low_present()
    {
        var f = Assert.Single(Rt(Svc(0, "AnyDesk", @"""C:\Program Files (x86)\AnyDesk\AnyDesk.exe"" --service")));
        Assert.Equal("REMOTE-TOOL-PRESENT", f.RuleId);
        Assert.Equal(Severity.Low, f.Severity);
    }

    [Theory]
    [InlineData(@"C:\Users\ana\AppData\Local\Temp\x\AnyDesk.exe", true)]
    [InlineData(@"C:\Users\Public\AnyDesk.exe", true)]
    [InlineData(@"C:\Windows\Temp\AnyDesk.exe", true)]
    [InlineData(@"C:\Users\ana\Desktop\AnyDesk.exe", true)]
    [InlineData(@"C:\Users\ana\Downloads\AnyDesk.exe", true)]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\AnyDesk.exe", true)]
    [InlineData(@"C:\ProgramData\AnyDesk.exe", true)]
    [InlineData(@"C:\ProgramData\AnyDesk\AnyDesk.exe", false)]
    [InlineData(@"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", false)]
    public void Service_from_a_user_writable_folder_raises_present_to_Medium(string image, bool raised)
    {
        var f = Assert.Single(Rt(Svc(0, "AnyDesk", @"""" + image + @""" --service")));
        Assert.Equal("REMOTE-TOOL-PRESENT", f.RuleId);
        Assert.Equal(raised ? Severity.Medium : Severity.Low, f.Severity);
        if (raised) Assert.Contains("poate scrie", f.Description);
    }

    [Fact]
    public void Executed_stays_High_and_names_the_user_folder()
    {
        var f = Assert.Single(Rt(Start(0, @"C:\Users\ana\Downloads\AnyDesk.exe")));
        Assert.Equal("REMOTE-TOOL-EXECUTED", f.RuleId);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("poate scrie", f.Description);
    }

    [Fact]
    public void Amcache_in_a_user_folder_is_presence_only_and_does_not_raise()
    {
        var f = Assert.Single(Rt(Row("Amcache", 0, "anydesk.exe", @"C:\Users\ana\Downloads\AnyDesk.exe", "")));
        Assert.Equal(Severity.Low, f.Severity);
    }

    [Fact]
    public void Service_image_with_NT_prefix_quotes_and_arguments_names_the_executable()
    {
        var f = Assert.Single(Rt(Svc(0, "svc1", @"\??\C:\Users\ana\Downloads\AteraAgent.exe /x")));
        Assert.Contains("Atera", f.Title);
        Assert.Equal(Severity.Medium, f.Severity);
        var sc = Assert.Single(Rt(Svc(0, "ScreenConnect Client (abc123)",
            @"""C:\Program Files (x86)\ScreenConnect Client (abc123)\ScreenConnect.ClientService.exe"" ""?e=Access&y=Guest&h=relay.example.org&p=443"" ")));
        Assert.Contains("ScreenConnect", sc.Title);
        Assert.DoesNotContain("relay.example.org", sc.Description);
    }

    [Fact]
    public void Two_tools_within_24h_raise_present_to_Medium_and_name_each_other()
    {
        var found = Rt(Svc(0, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"), Svc(23 * 60, "TeamViewer", @"C:\Program Files\TeamViewer\TeamViewer_Service.exe"));
        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Equal(Severity.Medium, f.Severity));
        Assert.Contains("TeamViewer", Of(found, "AnyDesk").Description);
        Assert.Contains("AnyDesk", Of(found, "TeamViewer").Description);
    }

    [Fact]
    public void Two_tools_25h_apart_stay_Low()
    {
        var found = Rt(Svc(0, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"), Svc(25 * 60, "TeamViewer", @"C:\Program Files\TeamViewer\TeamViewer_Service.exe"));
        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Equal(Severity.Low, f.Severity));
    }

    [Fact]
    public void Two_tools_on_different_stations_do_not_raise_each_other()
    {
        TimelineEvent OnHost(TimelineEvent e, string host) => new()
        {
            Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, EventId = e.EventId, Provider = e.Provider, Host = host, Locator = e.Locator, Summary = e.Summary, Fields = e.Fields,
        };
        var found = Rt(OnHost(Svc(0, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"), "PC1"), OnHost(Svc(5, "TeamViewer", @"C:\Program Files\TeamViewer\TeamViewer_Service.exe"), "PC2"));
        Assert.All(found, f => Assert.Equal(Severity.Low, f.Severity));
    }

    [Fact]
    public void Quick_Assist_run_from_its_own_place_is_Medium_present_is_Low_and_neither_triggers_the_two_tool_rule()
    {
        // Storm-1811 help-desk scams run exactly the built-in copy (System32 or WindowsApps): executed is never below Medium
        var found = Rt(Start(0, @"C:\Windows\System32\quickassist.exe"), Svc(10, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"));
        var qa = Of(found, "Quick Assist");
        Assert.Equal("REMOTE-TOOL-EXECUTED", qa.RuleId);
        Assert.Equal(Severity.Medium, qa.Severity);
        Assert.Equal(Severity.Low, Of(found, "AnyDesk").Severity);
        Assert.Equal(Severity.Medium, Assert.Single(Rt(Start(0, @"C:\Program Files\WindowsApps\MicrosoftCorporationII.QuickAssist_2.0.0.0_x64__8wekyb3d8bbwe\Microsoft.RemoteAssistance.QuickAssist\QuickAssist.exe"))).Severity);
        Assert.Equal(Severity.Low, Assert.Single(Rt(Row("Amcache", 0, "quickassist.exe", @"C:\Windows\System32\quickassist.exe", ""))).Severity);
    }

    [Fact]
    public void Chrome_Remote_Desktop_executed_stays_High_as_before_and_counts_for_the_two_tool_rule()
    {
        var found = Rt(Start(0, @"C:\Program Files (x86)\Google\Chrome Remote Desktop\130\remoting_host.exe"), Svc(10, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"));
        Assert.Equal(Severity.High, Of(found, "Chrome Remote Desktop").Severity);
        Assert.Equal(Severity.Medium, Of(found, "AnyDesk").Severity);
    }

    [Fact]
    public void Evidence_without_a_station_name_is_not_correlated_across_a_multi_station_case_but_is_on_a_single_station()
    {
        TimelineEvent OnHost(TimelineEvent e, string host) => new()
        {
            Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, EventId = e.EventId, Provider = e.Provider, Host = host, Locator = e.Locator, Summary = e.Summary, Fields = e.Fields,
        };
        var amA = Row("Amcache", 0, "anydesk.exe", @"C:\Program Files\AnyDesk\AnyDesk.exe", "");
        var amT = Row("Amcache", 5, "teamviewer.exe", @"C:\Program Files\TeamViewer\TeamViewer.exe", "");
        // two named stations in the case: unnamed Amcache rows are not tied together
        var multi = Rt(amA, amT, OnHost(Sec(4624, 1, ("LogonType", "3")), "PC1.corp.local"), OnHost(Sec(4624, 2, ("LogonType", "3")), "PC2"));
        Assert.All(multi, f => Assert.Equal(Severity.Low, f.Severity));
        // one station, named once as FQDN and once as NetBIOS: Amcache (no name) and the 7045 (FQDN) are the same station
        var single = Rt(amA, OnHost(Svc(5, "TeamViewer", @"C:\Program Files\TeamViewer\TeamViewer_Service.exe"), "PC1.corp.local"), OnHost(Sec(4624, 1, ("LogonType", "3")), "PC1"));
        Assert.All(single, f => Assert.Equal(Severity.Medium, f.Severity));
    }

    [Fact]
    public void A_DNS_query_alone_or_an_approved_tool_never_raises_another_tool()
    {
        var dns = Rt(Sysmon(22, 0, ("QueryName", "anydesk.com")), Sysmon(22, 1, ("QueryName", "www.teamviewer.com")));
        Assert.All(dns, f => Assert.Equal(Severity.Low, f.Severity));
        var profile = ProfileWithSoftware(("TeamViewer", "TeamViewer Germany GmbH", "", ""));
        var approved = Rt(profile, Start(0, @"C:\Program Files\TeamViewer\TeamViewer.exe"), Svc(10, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"));
        Assert.Equal(Severity.Info, Of(approved, "TeamViewer").Severity);
        Assert.Equal(Severity.Low, Of(approved, "AnyDesk").Severity);
    }

    [Theory]
    [InlineData(@"cmd.exe /c C:\Users\Public\AnyDesk.exe --service")]
    [InlineData(@"C:\Windows\System32\rundll32.exe x.dll,Run C:\Users\Public\AnyDesk.exe")]
    public void A_wrapper_in_front_of_the_tool_in_a_service_image_still_names_the_tool_and_its_folder(string image)
    {
        var f = Assert.Single(Rt(Svc(0, "svc-x", image)));
        Assert.Contains("AnyDesk", f.Title);
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Quick_Assist_from_Downloads_is_High()
    {
        Assert.Equal(Severity.High, Assert.Single(Rt(Start(0, @"C:\Users\ana\Downloads\quickassist.exe"))).Severity);
    }

    [Fact]
    public void New_catalog_tools_are_found_by_whole_file_name()
    {
        (string Exe, string Name)[] tools =
        [
            ("rutserv.exe", "Remote Utilities"), ("rfusclient.exe", "Remote Utilities"), ("AA_v3.exe", "Ammyy"), ("dwagent.exe", "DWService"), ("bomgar-scc.exe", "BeyondTrust"),
            ("Supremo.exe", "Supremo"), ("SupremoService.exe", "Supremo"), ("UltraViewer_Desktop.exe", "UltraViewer"), ("UltraViewer_Service.exe", "UltraViewer"),
            ("ISLLight.exe", "ISL Online"), ("quickassist.exe", "Quick Assist"), ("ZA_Connect.exe", "Zoho"), ("ZAService.exe", "Zoho"),
        ];
        foreach (var (exe, name) in tools)
        {
            var f = Assert.Single(Rt(Start(0, @"C:\Program Files\Vendor\" + exe.ToUpperInvariant())));
            Assert.Contains(name, f.Title);
            Assert.Equal("REMOTE-TOOL-EXECUTED", f.RuleId);
            Assert.Empty(Rt(Start(0, @"C:\Tools\x" + exe)));   // a substring of the file name is not the tool
        }
    }

    [Fact]
    public void New_catalog_tools_have_dns_domains_at_a_label_boundary()
    {
        (string Query, string Name)[] domains =
        [
            ("x.ammyy.com", "Ammyy"), ("dwservice.net", "DWService"), ("a.supremocontrol.com", "Supremo"), ("ultraviewer.net", "UltraViewer"), ("x.islonline.com", "ISL Online"),
            ("remoteassistance.support.services.microsoft.com", "Quick Assist"), ("x.zohoassist.com", "Zoho"), ("x.action1.com", "Action1"), ("a.netsupportsoftware.com", "NetSupport"),
        ];
        foreach (var (q, name) in domains) Assert.Contains(name, Assert.Single(Rt(Sysmon(22, 0, ("QueryName", q)))).Title);
        Assert.Empty(Rt(Sysmon(22, 0, ("QueryName", "microsoft.com")), Sysmon(22, 1, ("QueryName", "notammyy.com"))));
    }

    [Fact]
    public void Hidden_characters_line_breaks_and_quotes_in_the_path_are_sanitized()
    {
        var f = Assert.Single(Rt(Start(0, "C:\\Users\\ana\\Downloads\\a\u202E\r\nb\"\\AnyDesk.exe")));
        foreach (var text in new[] { f.Title, f.Description, f.File })
        {
            Assert.DoesNotContain('\u202E', text);
            Assert.DoesNotContain('\r', text);
            Assert.DoesNotContain('\n', text);
        }
        Assert.DoesNotContain('"', f.Description);
        var svc = Assert.Single(Rt(Svc(0, "AnyDesk", "\"C:\\Users\\ana\\Downloads\\\u202Egpj.\r\nAnyDesk.exe\" --service")));
        Assert.DoesNotContain('\u202E', svc.Description);
        Assert.DoesNotContain('\n', svc.Description);
    }

    [Fact]
    public void Flood_gives_one_finding_per_tool_with_at_most_20_evidence_items()
    {
        var events = Enumerable.Range(0, 5000).Select(i => Start(i * 0.01, @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe")).ToArray();
        var f = Assert.Single(Rt(events));
        Assert.True(f.SupportingEvidence.Count <= 20);
        Assert.Contains("5000", f.Description);
        var dns = Enumerable.Range(0, 5000).Select(i => Sysmon(22, i * 0.01, ("QueryName", "x.teamviewer.com"))).ToArray();
        Assert.True(Assert.Single(Rt(dns)).SupportingEvidence.Count <= 20);
    }

    [Fact]
    public void Approved_by_name_but_run_from_a_user_folder_is_Medium_not_Info()
    {
        var profile = ProfileWithSoftware(("AnyDesk", "AnyDesk Software GmbH", "", ""));
        var normal = Assert.Single(Rt(profile, Start(0, @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe")));
        Assert.Equal(Severity.Info, normal.Severity);
        var f = Assert.Single(Rt(profile, Start(0, @"C:\Users\ana\Downloads\AnyDesk.exe")));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("aprobarea pe nume acoperă numai Program Files și Windows", f.Description);
        Assert.DoesNotContain("aprobat în profil", f.Title);
    }

    [Theory]
    [InlineData(@"C:\Users\ana\AppData\Roaming\AnyDesk.exe")]
    [InlineData(@"C:\ProgramData\Vendorx\AnyDesk.exe")]
    [InlineData(@"C:\Users\ana\Documents\AnyDesk.exe")]
    [InlineData(@"{F38BF404-0000-0000-0000-000000000000}\AnyDesk.exe")]
    [InlineData(@"\\fileserver\share\AnyDesk.exe")]
    [InlineData(@"C:\PROGRA~3\x\AnyDesk.exe")]
    public void Approval_by_name_covers_only_Program_Files_and_Windows(string where)
    {
        var profile = ProfileWithSoftware(("AnyDesk", "AnyDesk Software GmbH", "", ""));
        Assert.Equal(Severity.Medium, Assert.Single(Rt(profile, Start(0, where))).Severity);
        Assert.Equal(Severity.Info, Assert.Single(Rt(profile, Row("UserAssist", 0, "anydesk.exe", @"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\AnyDesk\AnyDesk.exe"))).Severity);
        Assert.Equal(Severity.Info, Assert.Single(Rt(profile, Start(0, @"\Device\HarddiskVolume3\Program Files\AnyDesk\AnyDesk.exe"))).Severity);
        // Prefetch writes \VOLUME{guid}\PROGRAM FILES\…: still Program Files; \VOLUME{guid}\USERS\…\DOWNLOADS is not
        Assert.Equal(Severity.Info, Assert.Single(Rt(profile, Row("Prefetch", 0, "ANYDESK.EXE", @"C:\Program Files\AnyDesk\AnyDesk.exe", f: [("ReferencedFiles", @"\VOLUME{01d}\PROGRAM FILES\ANYDESK\ANYDESK.EXE")]))).Severity);
        Assert.Equal(Severity.Medium, Assert.Single(Rt(profile, Row("Prefetch", 0, "ANYDESK.EXE", @"C:\Users\ana\Downloads\AnyDesk.exe", f: [("ReferencedFiles", @"\VOLUME{01d}\USERS\ANA\DOWNLOADS\ANYDESK.EXE")]))).Severity);
    }

    [Fact]
    public void Approved_by_path_pattern_stays_Info_in_that_path()
    {
        var profile = ProfileWithSoftware(("AnyDesk", "", @"C:\Program Files (x86)\AnyDesk\*", ""));
        Assert.Equal(Severity.Info, Assert.Single(Rt(profile, Start(0, @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe"))).Severity);
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp\AnyDesk.exe")]
    [InlineData(@"C:\Windows\Tasks\AnyDesk.exe")]
    [InlineData(@"C:\Windows\System32\spool\drivers\color\AnyDesk.exe")]
    [InlineData(@"E:\Windows\AnyDesk.exe")]
    [InlineData(@"C:\Windows\..\Users\Public\AnyDesk.exe")]
    [InlineData(@"C:\Program Files\..\Users\ana\Downloads\AnyDesk.exe")]
    [InlineData(@"C:\Program Files\.\..\Users\Public\AnyDesk.exe")]
    public void Approval_by_name_does_not_cover_user_writable_folders_inside_Windows_other_drives_or_dot_dot_paths(string where)
    {
        var profile = ProfileWithSoftware(("AnyDesk", "AnyDesk Software GmbH", "", ""));
        Assert.Equal(Severity.Medium, Assert.Single(Rt(profile, Start(0, where))).Severity);
        Assert.Equal(Severity.Medium, Assert.Single(Rt(profile, Svc(0, "AnyDesk", where))).Severity);
    }

    [Fact]
    public void Environment_variables_in_a_service_image_are_expanded_both_ways()
    {
        Assert.Equal(Severity.Medium, Assert.Single(Rt(Svc(0, "AnyDesk", @"%SystemRoot%\Temp\AnyDesk.exe --service"))).Severity);
        var profile = ProfileWithSoftware(("AnyDesk", "AnyDesk Software GmbH", "", ""));
        Assert.Equal(Severity.Info, Assert.Single(Rt(profile, Svc(0, "AnyDesk", @"""%ProgramFiles%\AnyDesk\AnyDesk.exe"" --service"))).Severity);
        Assert.Equal(Severity.Info, Assert.Single(Rt(profile, Svc(0, "AnyDesk", @"""%ProgramFiles(x86)%\AnyDesk\AnyDesk.exe"" --service"))).Severity);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd /c C:\Users\Public\AnyDesk.exe")]
    [InlineData(@"cmd /c start """" ""C:\Users\Public\My Tools\AnyDesk.exe""")]
    public void A_launcher_without_exe_or_a_quoted_path_with_spaces_still_gives_the_tool_path(string image)
    {
        var profile = ProfileWithSoftware(("AnyDesk", "AnyDesk Software GmbH", "", ""));
        var f = Assert.Single(Rt(profile, Svc(0, "AnyDesk", image)));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains(@"C:\Users\Public\", f.File);
        Assert.DoesNotContain("cmd", f.File, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_32K_hostile_image_path_is_capped_in_the_finding()
    {
        var image = @"C:\Users\Public\AnyDesk.exe " + new string('A', 32_000);
        var f = Assert.Single(Rt(Svc(0, "svc-z", "cmd /c " + image)));
        Assert.True(f.File.Length <= 401, f.File.Length.ToString());
        Assert.True(f.Description.Length < 4000, f.Description.Length.ToString());
    }

    [Fact]
    public void Stations_named_by_IP_address_are_not_merged()
    {
        TimelineEvent OnHost(TimelineEvent e, string host) => new()
        {
            Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, EventId = e.EventId, Provider = e.Provider, Host = host, Locator = e.Locator, Summary = e.Summary, Fields = e.Fields,
        };
        var found = Rt(OnHost(Svc(0, "AnyDesk", @"C:\Program Files\AnyDesk\AnyDesk.exe"), "10.0.0.5"), OnHost(Svc(5, "TeamViewer", @"C:\Program Files\TeamViewer\TeamViewer_Service.exe"), "10.9.9.9"));
        Assert.All(found, f => Assert.Equal(Severity.Low, f.Severity));
    }

    [Fact]
    public void A_case_without_remote_tools_gives_nothing()
    {
        Assert.Empty(Rt(Start(0, @"C:\Windows\System32\cmd.exe"), Svc(1, "Spooler", @"C:\Windows\System32\spoolsv.exe"), Sysmon(22, 2, ("QueryName", "example.org")),
            Row("Prefetch", 3, "NOTEPAD.EXE", @"C:\Windows\notepad.exe")));
    }
}
