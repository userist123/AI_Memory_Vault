using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>SMB-SERVER-AUTH-FAIL (SMBServer/Security 551) and SMB-CLIENT-SHARE-DENIED (SmbClient/Security 31010), from the lab events.</summary>
public class SmbAuthRulesTests
{
    private const string Srv = "Microsoft-Windows-SMBServer/Security";
    private const string Cli = "Microsoft-Windows-SmbClient/Security";

    // real lab event (LAB-WS02), status 0xc0000072, source port 0xD901 = 55553
    private const string Xml551 = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-SMBServer' Guid='{d48ce617-33a2-4bc3-a5c7-11aa4f29619e}'/><EventID>551</EventID><Version>3</Version><Level>2</Level><Task>551</Task><Opcode>0</Opcode><Keywords>0x810000000000008</Keywords><TimeCreated SystemTime='2026-11-18T15:19:08.4282341Z'/><EventRecordID>3</EventRecordID><Correlation/><Execution ProcessID='4' ThreadID='5476'/><Channel>Microsoft-Windows-SMBServer/Security</Channel><Computer>LAB-WS02</Computer><Security UserID='S-1-5-18'/></System><UserData><EventData xmlns='Smb2Namespace'><SessionGUID>{01c146dd-776f-0001-23fc-c1016f77dd01}</SessionGUID><ConnectionGUID>{01c146dd-776f-0002-d7c4-c1016f77dd01}</ConnectionGUID><Status>0xc0000072</Status><TranslatedStatus>0xc0000072</TranslatedStatus><ClientAddressLength>16</ClientAddressLength><ClientAddress>0200D9010A0A0A0A0000000000000000</ClientAddress><SessionId>0x1e80000000009</SessionId><UserNameLength>0</UserNameLength><UserName></UserName><ClientNameLength>13</ClientNameLength><ClientName>\\\\10.10.10.10</ClientName><SPN>session setup failed before the SPN could be queried</SPN><SPNValidationPolicy>0</SPNValidationPolicy><ReasonCode>3</ReasonCode></EventData></UserData></Event>";

    // real lab event (LAB-DC01)
    private const string Xml31010 = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-SMBClient' Guid='{988c59c5-0a1c-45b6-a555-0c62276e327d}'/><EventID>31010</EventID><Version>0</Version><Level>2</Level><Task>136</Task><Opcode>0</Opcode><Keywords>0x200000000000100</Keywords><TimeCreated SystemTime='2026-11-18T15:19:10.5528890Z'/><EventRecordID>1</EventRecordID><Correlation/><Execution ProcessID='0' ThreadID='0'/><Channel>Microsoft-Windows-SmbClient/Security</Channel><Computer>LAB-DC01.lab.local</Computer><Security/></System><EventData><Data Name='Reason'>12</Data><Data Name='Status'>3221225506</Data><Data Name='ShareNameLength'>15</Data><Data Name='ShareName'>\\10.10.10.32\\C$</Data><Data Name='ObjectNameLength'>0</Data><Data Name='ObjectName'></Data></EventData></Event>";

    private const string Addr55553 = "0200D9010A0A0A0A0000000000000000";

    private static TimelineEvent E551(double m, string status = "0xc0000072", string addr = Addr55553, string name = @"\\10.10.10.10", string channel = Srv) =>
        Ev(channel, 551, m, "Microsoft-Windows-SMBServer", ("Status", status), ("TranslatedStatus", status), ("ClientAddress", addr), ("ClientName", name), ("UserName", ""));

    private static TimelineEvent E31010(double m, string share, string status = "3221225506", string channel = Cli) =>
        Ev(channel, 31010, m, "Microsoft-Windows-SMBClient", ("Reason", "12"), ("Status", status), ("ShareName", share));

    private static List<TimelineEvent> Series(int n, string status = "0xc0000072", string addr = Addr55553, string name = @"\\10.10.10.10") =>
        Enumerable.Range(0, n).Select(i => E551(i * 0.5, status, addr, name)).ToList();

    [Fact]
    public void Parser_keeps_the_551_UserData_fields_and_the_31010_EventData_fields()
    {
        var f = new Dictionary<string, string>();
        EvtxParser.ExtractFields(Xml551, f);
        Assert.Equal("0xc0000072", f["Status"]);
        Assert.Equal(Addr55553, f["ClientAddress"]);
        Assert.Equal(@"\\10.10.10.10", f["ClientName"]);
        Assert.Equal("", f["UserName"]);
        Assert.Equal("3", f["ReasonCode"]);
        var g = new Dictionary<string, string>();
        EvtxParser.ExtractFields(Xml31010, g);
        Assert.Equal("3221225506", g["Status"]);
        Assert.Equal(@"\10.10.10.32\C$", g["ShareName"]);
    }

    [Fact]
    public void Real_sockaddr_decodes_to_the_address_and_port()
    {
        var (ip, port, v6) = Wp11Rules.DecodeClientAddress(Addr55553);
        Assert.Equal("10.10.10.10", ip);
        Assert.Equal(55553, port);
        Assert.False(v6);
        Assert.Equal(55552, Wp11Rules.DecodeClientAddress("0200D9000A0A0A0A0000000000000000").Port);
        Assert.Null(Wp11Rules.DecodeClientAddress("1700D9010000000000000000").Ip);
        Assert.True(Wp11Rules.DecodeClientAddress("1700D9010000000000000000").Ipv6Raw);
        Assert.Null(Wp11Rules.DecodeClientAddress("zz").Ip);
    }

    [Fact]
    public void Ten_refused_sessions_from_one_address_make_one_Medium_finding_with_status_counts()
    {
        var evs = Series(7, "0xc0000072"); evs.AddRange(Enumerable.Range(0, 3).Select(i => E551(4 + i * 0.5, "0xc000006d")));
        var f = One(Run(evs), "SMB-SERVER-AUTH-FAIL");
        Assert.Equal("10 sesiuni SMB refuzate de la 10.10.10.10", f.Title);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal("Credential Access", f.Category);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Equal(Confidence.Medium, f.Confidence);
        Assert.Equal("T1110", f.MitreTechniqueId);
        Assert.Contains("7 × 0xC0000072 (STATUS_ACCOUNT_DISABLED)", f.Description);
        Assert.Contains("3 × 0xC000006D (STATUS_LOGON_FAILURE)", f.Description);
        Assert.Contains("10.10.10.10", f.Description);
        Assert.Contains("nu înregistrează numele contului", f.Description);
        Assert.Contains("4625", f.Description);
        Assert.Contains("55553", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("0xC0000072"));
    }

    [Fact]
    public void Threshold_boundary_nine_is_silent_and_ten_fires()
    {
        Assert.DoesNotContain(Run(Series(Correlation.CredentialThreshold - 1)), x => x.RuleId == "SMB-SERVER-AUTH-FAIL");
        Assert.Single(Run(Series(Correlation.CredentialThreshold)), x => x.RuleId == "SMB-SERVER-AUTH-FAIL");
        // ten events spread wider than the window do not count
        var slow = Enumerable.Range(0, 10).Select(i => E551(i * 5)).ToList();
        Assert.DoesNotContain(Run(slow), x => x.RuleId == "SMB-SERVER-AUTH-FAIL");
    }

    [Fact]
    public void Different_addresses_are_separate_series()
    {
        var evs = Series(6); evs.AddRange(Series(6, addr: "0200D9010A0A0A0B0000000000000000", name: @"\\10.10.10.11"));
        Assert.DoesNotContain(Run(evs), x => x.RuleId == "SMB-SERVER-AUTH-FAIL");
    }

    [Fact]
    public void Undecodable_address_falls_back_to_the_client_name_then_to_the_raw_hex()
    {
        var named = One(Run(Series(10, addr: "", name: @"\\LAB-DC01")), "SMB-SERVER-AUTH-FAIL");
        Assert.Equal("10 sesiuni SMB refuzate de la LAB-DC01 (nume declarat de client)", named.Title);
        var v6 = One(Run(Series(10, addr: "1700D9010000000000000000", name: "")), "SMB-SERVER-AUTH-FAIL");
        Assert.Contains("adresă IPv6 nedecodată", v6.Title);
        Assert.Contains("17000000000000000000", v6.Title);   // the hex without the two port bytes
        Assert.Equal("", v6.Ip);
    }

    [Fact]
    public void An_undecoded_series_whose_source_port_changes_at_every_connection_is_still_one_series()
    {
        // review: the port (bytes 2-3) was part of the grouping label, so every connection was its own group and a burst stayed silent
        var evs = Enumerable.Range(0, 10).Select(i => E551(i * 0.5, addr: $"1700{(0xC000 + i):X4}00000000000000000000000000000001", name: "")).ToList();
        var f = One(Run(evs), "SMB-SERVER-AUTH-FAIL");
        Assert.StartsWith("10 sesiuni SMB refuzate", f.Title);
        Assert.Contains("fără port", f.Title);
    }

    [Fact]
    public void A_loopback_series_names_a_local_program_as_an_alternative()
    {
        var f = One(Run(Series(10, addr: "0200D9017F000001", name: "")), "SMB-SERVER-AUTH-FAIL");
        Assert.Equal("127.0.0.1", f.Ip);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("127.x"));
        Assert.DoesNotContain(One(Run(Series(10)), "SMB-SERVER-AUTH-FAIL").AlternativeExplanations, a => a.Contains("127.x"));
    }

    [Fact]
    public void Unknown_status_is_shown_raw_and_the_channel_name_is_case_insensitive()
    {
        var evs = Enumerable.Range(0, 10).Select(i => E551(i * 0.5, "0xc0000193", channel: "microsoft-windows-smbserver/SECURITY")).ToList();
        var f = One(Run(evs), "SMB-SERVER-AUTH-FAIL");
        Assert.Contains("10 × 0xC0000193", f.Description);
        Assert.DoesNotContain("STATUS_", f.Description);
    }

    [Fact]
    public void An_overlapping_CRED_BRUTEFORCE_for_the_same_address_is_named_as_corroboration()
    {
        var other = Other("CRED-BRUTEFORCE", Severity.Medium, 1, "Credential Access", "T1110");
        var withIp = new Finding { FindingId = "F-0900", RuleId = "CRED-BRUTEFORCE", Title = "t", Severity = Severity.Medium, Description = "x", Ip = "10.10.10.10",
            FirstSeenUtc = new DateTimeOffset(T0.AddMinutes(1), TimeSpan.Zero), LastSeenUtc = new DateTimeOffset(T0.AddMinutes(3), TimeSpan.Zero) };
        var f = One(Run(Series(10), existing: [withIp]), "SMB-SERVER-AUTH-FAIL");
        Assert.Contains("CRED-BRUTEFORCE F-0900", f.Description);
        Assert.Contains("F-0900", f.RelatedFindingIds);
        var none = One(Run(Series(10), existing: [other]), "SMB-SERVER-AUTH-FAIL");
        Assert.DoesNotContain("CRED-BRUTEFORCE", none.Description);
    }

    [Fact]
    public void Refused_admin_share_gives_a_Low_finding_for_that_server()
    {
        var f = One(Run([E31010(0, @"\\10.10.10.32\C$")]), "SMB-CLIENT-SHARE-DENIED");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal("T1021.002", f.MitreTechniqueId);
        Assert.Equal("Lateral Movement", f.Category);
        Assert.Equal(Confidence.Medium, f.Confidence);
        Assert.Contains(@"\\10.10.10.32\C$", f.Title + f.Description);
        Assert.Contains("0xC0000022 (STATUS_ACCESS_DENIED)", f.Description);
        Assert.Contains("nu înregistrează contul", f.Description);
        // the real event writes a single leading backslash in the name; both spellings and a mixed-case channel are read
        Assert.Single(Run([E31010(0, @"\10.10.10.32\ADMIN$", channel: "Microsoft-Windows-SMBClient/Security")]), x => x.RuleId == "SMB-CLIENT-SHARE-DENIED");
    }

    [Fact]
    public void Ordinary_shares_and_IPC_refused_make_no_finding()
    {
        Assert.DoesNotContain(Run([E31010(0, @"\\srv\Public"), E31010(1, @"\\srv\IPC$"), E31010(2, @"\\srv\Docs")]), x => x.RuleId == "SMB-CLIENT-SHARE-DENIED");
    }

    [Fact]
    public void Five_servers_inside_the_window_add_one_Medium_scan_finding_and_four_do_not()
    {
        var five = Enumerable.Range(1, 5).Select(i => E31010(i * 10, $@"\\10.10.10.{i}\C$")).ToList();
        var found = Run(five).Where(x => x.RuleId == "SMB-CLIENT-SHARE-DENIED").ToList();
        Assert.Equal(6, found.Count);
        var scan = Assert.Single(found, x => x.Severity == Severity.Medium);
        Assert.Equal("T1135", scan.MitreTechniqueId);
        Assert.Equal("încercări de acces la partajări administrative pe 5 servere", scan.Title);
        Assert.Equal(5, found.Count(x => x.Severity == Severity.Low));
        Assert.DoesNotContain(Run(five.Take(4)), x => x.Severity == Severity.Medium && x.RuleId == "SMB-CLIENT-SHARE-DENIED");
        // five servers spread over more than the window
        var slow = Enumerable.Range(1, 5).Select(i => E31010(i * 60, $@"\\10.10.10.{i}\C$")).ToList();
        Assert.DoesNotContain(Run(slow), x => x.Severity == Severity.Medium && x.RuleId == "SMB-CLIENT-SHARE-DENIED");
    }

    [Fact]
    public void Contracts_and_collection_list_cover_the_new_channels()
    {
        Assert.NotNull(RuleContracts.For("SMB-SERVER-AUTH-FAIL"));
        Assert.NotNull(RuleContracts.For("SMB-CLIENT-SHARE-DENIED"));
        Assert.Contains(Srv, LogAnalyzer.Dfir.Windows.Acquisition.EventLogCollector.Channels);
        Assert.Contains(Cli, LogAnalyzer.Dfir.Windows.Acquisition.EventLogCollector.Channels);
        Assert.Contains("evtx:" + Srv, LogAnalyzer.Dfir.Windows.Acquisition.RemoteCollection.KnownArtifacts);
        Assert.Contains("evtx:" + Cli, LogAnalyzer.Dfir.Windows.Acquisition.RemoteCollection.KnownArtifacts);
    }
}
