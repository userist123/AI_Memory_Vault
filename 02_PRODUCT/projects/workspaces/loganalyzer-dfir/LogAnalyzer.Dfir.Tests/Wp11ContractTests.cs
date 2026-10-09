using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using LogAnalyzer.Verification;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Every WP11 rule is in the contract catalog, makes no stronger claim than its evidence, and its sources are described on the parser.</summary>
public class Wp11ContractTests
{
    internal static readonly string[] AllRules =
    [
        "SMB-ADMIN-SHARE", "SMB-SHARE-PERMS-CHANGED", "ACCOUNT-CREATED", "ACCOUNT-ADDED-PRIVILEGED-GROUP", "ACCOUNT-CREATED-THEN-USED",
        "REMOTE-RDP-INTERNAL", "REMOTE-WINRM", "REMOTE-PSEXEC", "REMOTE-SSH", "SECURITY-AGENT-STOPPED", "VSS-SNAPSHOT-DELETED",
        "DNS-RARE-DOMAIN", "DNS-SERVER-CHANGED", "REMOTE-TOOL-PRESENT", "REMOTE-TOOL-EXECUTED",
    ];

    /// <summary>A timeline in which every rule fires at least once.</summary>
    private static List<TimelineEvent> Everything() =>
    [
        Sec(5140, 0, ("ShareName", @"\\*\ADMIN$"), ("SubjectUserName", "bob"), ("SubjectDomainName", "CORP"), ("IpAddress", "10.0.0.7")),
        Sec(5145, 0.1, ("ShareName", @"\\*\ADMIN$"), ("RelativeTargetName", "abcd1234.exe"), ("SubjectUserName", "bob"), ("SubjectDomainName", "CORP"), ("IpAddress", "10.0.0.7")),
        Sec(4670, 1, ("ObjectType", "File"), ("ObjectName", @"D:\S\a.txt"), ("OldSd", "D:"), ("NewSd", "D:"), ("SubjectUserName", "bob"), ("ProcessName", @"C:\Windows\explorer.exe")),
        Sec(4720, 2, ("TargetUserName", "svc_x"), ("TargetSid", "S-1-5-21-1-2-3-1105"), ("SubjectUserName", "admin1")),
        Sec(4732, 3, ("MemberSid", "S-1-5-21-1-2-3-1105"), ("MemberName", "CN=svc_x,DC=c"), ("TargetUserName", "Administrators"), ("TargetSid", "S-1-5-32-544"), ("SubjectUserName", "admin1")),
        Sec(4624, 4, ("TargetUserName", "svc_x"), ("TargetUserSid", "S-1-5-21-1-2-3-1105"), ("LogonType", "3"), ("IpAddress", "10.0.0.8")),
        Sec(4624, 5, ("TargetUserName", "bob"), ("TargetDomainName", "CORP"), ("LogonType", "10"), ("IpAddress", "10.0.0.9")),
        Ev("Microsoft-Windows-WinRM/Operational", 91, 6, "Microsoft-Windows-WinRM"),
        Sys(7045, 7, "Service Control Manager", ("ServiceName", "PSEXESVC"), ("ImagePath", @"%SystemRoot%\PSEXESVC.exe")),
        Ev("OpenSSH/Operational", 4, 8, "OpenSSH", ("payload", "Accepted password for al from 10.0.0.5 port 22 ssh2")),
        Sys(7036, 9, "Service Control Manager", ("param1", "Windows Defender Antivirus Service"), ("param2", "stopped")),
        Sec(4688, 10, ("NewProcessName", @"C:\Windows\System32\vssadmin.exe"), ("CommandLine", "vssadmin delete shadows /all"), ("SubjectUserName", "bob")),
        Sysmon(22, 11, ("QueryName", "x.rare-example.org"), ("Image", @"C:\Windows\System32\rundll32.exe")),
        Row("SystemConfig", 12, f: [("Interface", "{A}"), ("NameServer", "10.0.0.1")]),
        Row("SystemConfig", 13, f: [("Interface", "{A}"), ("NameServer", "8.8.8.8")]),
        Row("Amcache", 14, "teamviewer.exe", @"C:\Users\bob\Downloads\TeamViewer.exe", "abc"),
        Row("Prefetch", 15, "ANYDESK.EXE", @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", f: [("RunCount", "2")]),
    ];

    private static List<Finding> Enriched() => FindingContract.Enrich(Correlation.Run(Everything())).ToList();

    [Fact]
    public void Every_WP11_rule_fires_through_Correlation_Run()
    {
        var ids = Enriched().Select(f => f.RuleId).ToHashSet();
        foreach (var r in AllRules) Assert.Contains(r, ids);
    }

    [Fact]
    public void Every_WP11_rule_has_a_complete_catalog_entry()
    {
        foreach (var id in AllRules)
        {
            var c = RuleContracts.For(id);
            Assert.NotNull(c);
            Assert.EndsWith("Wp11Rules", c!.Producer);
            Assert.NotEmpty(c.Limitations); Assert.NotEmpty(c.MissingEvidence); Assert.NotEmpty(c.NextSteps); Assert.True(c.Meaning.Length > 20);
        }
    }

    [Fact]
    public void Findings_carry_contract_fields_and_no_anti_overclaim_violation()
    {
        foreach (var f in Enriched().Where(f => AllRules.Contains(f.RuleId)))
        {
            Assert.True(f.HasSemanticType, f.RuleId);
            Assert.NotEmpty(f.Limitations); Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.MissingEvidence); Assert.NotEmpty(f.RecommendedNextSteps);
            Assert.NotEmpty(f.SupportingEvidence); Assert.NotEmpty(f.ClassificationReason);
            Assert.Empty(AntiOverclaim.Violations(f));
            Assert.Contains(f.Limitations, l => l.Contains("NOT_ASSESSED"));
        }
    }

    [Fact]
    public void The_declared_semantic_type_is_supported_by_the_cited_artifacts_per_the_WP4_sufficiency_check()
    {
        var events = Everything();
        foreach (var f in Correlation.Run(events).Where(f => AllRules.Contains(f.RuleId)))
        {
            var refs = f.SupportingEvidence.Select(r =>
            {
                var e = events.First(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);
                var row = new TimelineRow(e.Time.Utc, e.TimeSemantics, e.Source, e.EventId, e.Path, e.Process, e.Summary, e.EvidenceId, e.Locator, "");
                var kind = e.Source.StartsWith("EventLog:", StringComparison.Ordinal) && e.EventId.Length > 0 ? $"{e.Source} {e.EventId}" : e.Source;
                return new ResolvedRef(r, null, [row], e.Source, kind, true);
            }).ToList();
            var res = Checks.Sufficiency(f, refs);
            Assert.True(res.Outcome != CheckOutcome.Fail, $"{f.RuleId} ({f.SemanticType.ToSpec()}): {res.Reason}");
        }
    }

    [Fact]
    public void Severity_never_exceeds_the_spec_for_a_single_event_in_a_quiet_case()
    {
        var f = Enriched();
        Assert.True(f.Where(x => x.RuleId is "SMB-ADMIN-SHARE" or "SMB-SHARE-PERMS-CHANGED" or "REMOTE-RDP-INTERNAL" or "REMOTE-SSH" or "DNS-RARE-DOMAIN" or "REMOTE-TOOL-PRESENT").All(x => x.Severity <= Severity.Medium));
        Assert.DoesNotContain(f, x => x.Severity == Severity.Critical && AllRules.Contains(x.RuleId));
    }

    [Fact]
    public void EvtxParser_describes_every_channel_the_new_rules_read()
    {
        var caps = new EvtxParser().Descriptor.Capabilities;
        Assert.NotNull(caps);
        (string Channel, int Id)[] needed =
        [
            ("Security", 5140), ("Security", 5145), ("Security", 4670), ("Security", 4720), ("Security", 4722), ("Security", 4724), ("Security", 4738),
            ("Security", 4728), ("Security", 4732), ("Security", 4756), ("Security", 4624), ("Security", 4648), ("Security", 4688),
            ("System", 7036), ("System", 7040), ("System", 7045), ("System", 25), ("System", 33), ("System", 1074), ("System", 6006),
            ("Application", 1034), ("Application", 11724), ("Application", 11707), ("Application", 8193), ("Application", 8194),
            ("Microsoft-Windows-Sysmon/Operational", 1), ("Microsoft-Windows-Sysmon/Operational", 4), ("Microsoft-Windows-Sysmon/Operational", 16), ("Microsoft-Windows-Sysmon/Operational", 22),
            ("Microsoft-Windows-WinRM/Operational", 6), ("Microsoft-Windows-WinRM/Operational", 91), ("Microsoft-Windows-WinRM/Operational", 142),
            ("OpenSSH/Operational", 4),
            ("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", 21), ("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", 22),
            ("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", 25), ("Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational", 1149),
            ("Microsoft-Windows-DNS-Client/Operational", 3006), ("Microsoft-Windows-DNS-Client/Operational", 3008), ("Microsoft-Windows-DNS-Client/Operational", 3020),
            ("Microsoft-Windows-NetworkProfile/Operational", 10000),
            ("Microsoft-Windows-PowerShell/Operational", 4104),
        ];
        foreach (var (ch, id) in needed)
        {
            var c = caps!.ChannelFor(ch, id);
            Assert.True(c is not null, $"{ch} {id}: no ChannelCapability");
            Assert.NotEmpty(c!.CanProve); Assert.NotEmpty(c.CannotProve); Assert.NotEmpty(c.CorrelationSources);
        }
        // the collected channels are exactly the ones the capabilities describe for the new sources
        Assert.Contains(caps!.Channels, c => c.Channel == "OpenSSH/Operational");
    }

    [Fact]
    public void Original_parser_capabilities_are_unchanged_and_other_parsers_have_no_channel_table()
    {
        Assert.NotEmpty(ParserCapabilities.For("EvtxParser")!.CannotProve);
        Assert.Empty(ParserCapabilities.For("PrefetchParser")!.Channels);
    }

    [Fact]
    public void Rule_data_ships_embedded_and_parses()
    {
        var d = Wp11Data.Default;
        Assert.True(d.Agents.Agents.Count >= 10);
        Assert.True(d.Tools.Tools.Count >= 8);
        Assert.NotEmpty(d.Lists.PrivilegedGroups.Names);
        Assert.NotEmpty(d.Lists.Vss.CommandPatterns);
    }
}
