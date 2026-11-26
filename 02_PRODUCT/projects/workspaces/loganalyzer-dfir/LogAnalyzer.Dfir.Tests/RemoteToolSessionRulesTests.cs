using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.RemoteTools;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The link between the session logs of remote-access tools (AnyDesk, TeamViewer parsers; ScreenConnect events in the Application log) and REMOTE-TOOL-EXECUTED /
/// REMOTE-TOOL-PRESENT: a session is evidence of use as strong as an execution artifact (but Medium confidence: the logs can be forged), the finding lists the sessions,
/// and an accepted incoming session from an id / IP that the profile does not name raises an approved tool from Info to High.
/// </summary>
[CollectionDefinition("ToolSessionsTiming", DisableParallelization = true)]
public class ToolSessionsTimingCollection;

[Collection("ToolSessionsTiming")]
public sealed class RemoteToolSessionRulesTests : IDisposable
{
    private readonly RemoteToolTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static TimelineEvent AnyDeskSession(double m, string direction = "Incoming", string accepted = "da", string remote = "999000111", string method = "User", string kind = "") =>
        Row(AnyDeskConnectionTraceParser.EventSource, m, f:
        [
            ("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Direction", direction), ("Accepted", accepted), ("Remote Id", remote), ("Auth Method", method), ("Kind", kind),
        ]);

    private static TimelineEvent TvSession(double m, string account = "test-user", string remote = "999000222") =>
        Row(TeamViewerConnectionsParser.EventSource, m, f:
        [
            ("Tool Id", "teamviewer"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "da"), ("Remote Id", remote), ("Local Account", account), ("Session Type", "RemoteControl"),
        ]);

    private static TimelineEvent ScreenConnect(double m, string message, string provider = "ScreenConnect Client (0123456789abcdef0123456789abcdef01234567)") =>
        Ev("Application", 0, m, provider, ("Data0", message));

    [Fact]
    public void An_AnyDesk_session_alone_is_a_High_REMOTE_TOOL_EXECUTED_with_Medium_confidence_and_lists_the_session()
    {
        var f = One(Run([AnyDeskSession(0)]), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Confidence.Medium, f.Confidence);                       // the log is a file anyone with access to the station can edit
        Assert.Equal(SemanticType.Execution, f.SemanticType);
        Assert.Contains("AnyDesk", f.Title);
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 1", f.Description);
        Assert.Contains("intrare (", f.Description);                          // the direction in Romanian, not the log's "Incoming"
        Assert.DoesNotContain("Incoming", f.Description);
        Assert.Contains("999000111", f.Description);
        Assert.Contains("metodă User", f.Description);
        Assert.Contains("falsificat", f.Description + f.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nu arată o sesiune", f.Description);          // the generic sentence is replaced when sessions exist
        Assert.Single(f.SupportingEvidence);
    }

    [Fact]
    public void One_connection_written_twice_is_estimated_as_one_distinct_connection()
    {
        // ad.trace writes "Incoming session request" and then "Accepting from" for the same connection; connection_trace.txt can repeat it too
        var request = Row(AnyDeskTraceParser.EventSource, 0, f: [("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Kind", "IncomingSessionRequest"), ("Direction", "Incoming"), ("Accepted", "necunoscut"), ("Remote Id", "999000111")]);
        var f = One(Run([AnyDeskSession(0), request]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 2", f.Description);
        Assert.Contains("aproximativ 1 conexiuni distincte", f.Description);
        // two partners: no estimate added
        var g = One(Run([AnyDeskSession(0), AnyDeskSession(5, remote: "999000222")]), "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain("conexiuni distincte", g.Description);
    }

    [Fact]
    public void Rows_that_only_describe_a_session_are_counted_apart_and_not_listed_as_sessions()
    {
        // lab 2026-11-11: ad.trace relay / remote-OS rows (no direction) made "12 sesiuni" out of 3 connections
        var relay = Row(AnyDeskTraceParser.EventSource, 1, f: [("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Kind", "LoggedInFromRelay"), ("Direction", "necunoscută"), ("Accepted", "necunoscut"), ("IP", "203.0.113.7")]);
        var f = One(Run([AnyDeskSession(0), relay]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 1", f.Description);
        Assert.Contains("alte rânduri despre sesiuni, fără direcție", f.Description);
        Assert.Contains("încheiere): 1", f.Description);
        Assert.DoesNotContain("203.0.113.7", f.Description);
        Assert.DoesNotContain("necunoscută", f.Description[f.Description.IndexOf("Primele", StringComparison.Ordinal)..]);
        Assert.Equal(2, f.SupportingEvidence.Count);                          // both rows stay evidence
    }

    [Fact]
    public void A_TeamViewer_session_names_the_account_and_the_remote_id()
    {
        var f = One(Run([TvSession(0)]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("TeamViewer", f.Title);
        Assert.Contains("cont local test-user", f.Description);
        Assert.Contains("la distanță 999000222", f.Description);
        Assert.Contains("RemoteControl", f.Description);
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void ScreenConnect_Application_events_are_session_evidence_for_ScreenConnect()
    {
        var found = Run([ScreenConnect(0, "Cloud Account Administrator Connected"), ScreenConnect(2, "Transferred files with action Upload"), ScreenConnect(3, "Executed command whoami")]);
        var f = One(found, "REMOTE-TOOL-EXECUTED");
        Assert.Contains("ScreenConnect", f.Title);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Confidence.Medium, f.Confidence);
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 3", f.Description);
        Assert.Contains("conectare", f.Description);
        Assert.Contains("transfer de fișiere", f.Description);
        Assert.Contains("comandă executată", f.Description);
        Assert.NotNull(f.FirstSeenUtc);
    }

    [Fact]
    public void An_approved_ScreenConnect_with_a_connection_is_Medium_to_check_because_its_events_name_no_partner()
    {
        // review finding 2026-11-11: ScreenConnect events carry no remote id, so no profile entry can name the partner; Medium "de verificat", never High
        var f = One(Run([ScreenConnect(0, "Cloud Account Administrator Connected")], ProfileWithSoftware(("ScreenConnect", "ConnectWise", "", ""))), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("sau pe care jurnalul nu o arată", f.Description);
    }

    [Fact]
    public void Other_Application_events_with_ID_0_or_another_provider_are_not_ScreenConnect_sessions()
    {
        Assert.DoesNotContain(Run([ScreenConnect(0, "Cloud Account Administrator Connected", provider: "SomeOtherApp")]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
        Assert.DoesNotContain(Run([ScreenConnect(0, "Service started normally")]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
        // ID 0 on another channel does not count either
        var wrongChannel = Ev("System", 0, 0, "ScreenConnect Client (0123456789abcdef0123456789abcdef01234567)", ("Data0", "Cloud Account Administrator Connected"));
        Assert.DoesNotContain(Run([wrongChannel]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
    }

    [Fact]
    public void A_forged_field_on_a_row_of_another_source_is_not_a_session()
    {
        // an EVTX row whose data names the same fields as the parsers must not count: only the three session-log sources do
        var fake = Ev("Application", 1000, 0, "SomeApp", ("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "da"));
        var fake2 = Row("Artifact:NotATool", 0, f: [("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "da")]);
        Assert.DoesNotContain(Run([fake, fake2]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
        // and a session event of one tool does not make a finding for another tool
        Assert.DoesNotContain(Run([AnyDeskSession(0)]), x => x.Title.Contains("TeamViewer"));
    }

    [Fact]
    public void A_session_plus_an_execution_artifact_is_High_confidence_and_lists_both()
    {
        var found = Run([AnyDeskSession(0), Row("Prefetch", 1, "ANYDESK.EXE", @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", f: [("RunCount", "3")])]);
        var f = One(found, "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Confidence.High, f.Confidence);
        Assert.Equal(2, f.SupportingEvidence.Count);
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 1", f.Description);
        Assert.Contains("Prefetch", f.Description);
    }

    [Fact]
    public void A_session_does_not_make_the_executable_path_rules_fire()
    {
        // a session has no path: it must not be treated as running from a user-writable folder or count as "present only"
        var f = One(Run([AnyDeskSession(0)]), "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain("folder în care utilizatorul poate scrie", f.Description);
        Assert.Equal("", f.File);
        Assert.DoesNotContain(Run([AnyDeskSession(0)]), x => x.RuleId == "REMOTE-TOOL-PRESENT");
    }

    [Fact]
    public void An_approved_tool_stays_Info_for_outgoing_rejected_or_profile_listed_sessions_and_becomes_Medium_to_check_for_a_stranger()
    {
        var profile = ProfileWithSoftware(("AnyDesk", "philandro Software GmbH", "", ""));
        // outgoing: the user connected out; incoming REJECTED: nothing was accepted
        Assert.Equal(Severity.Info, One(Run([AnyDeskSession(0, direction: "Outgoing")], profile), "REMOTE-TOOL-EXECUTED").Severity);
        Assert.Equal(Severity.Info, One(Run([AnyDeskSession(0, accepted: "nu", method: "REJECTED")], profile), "REMOTE-TOOL-EXECUTED").Severity);
        // an accepted incoming session from an id that the profile does not name: the approval covers the program, not who connects
        var stranger = One(Run([AnyDeskSession(0)], profile), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Medium, stranger.Severity);
        Assert.Contains("De verificat", stranger.Description);
        Assert.Contains("Aprobarea acoperă programul", stranger.Description);
        Assert.DoesNotContain("(aprobat în profil)", stranger.Title);
        Assert.Contains("din care 1 de intrare acceptate", stranger.Description);
        // the same id listed as a network destination in the profile is approved: Info again
        var listed = ProfileWithSoftware(("AnyDesk", "philandro Software GmbH", "", ""));
        listed.NetworkDestinations = [new NetworkDestinationRow { Address = "999000111", Zone = "LAN", Purpose = "Suport IT" }];
        var ok = One(Run([AnyDeskSession(0)], listed), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Info, ok.Severity);
        Assert.Contains("(aprobat în profil)", ok.Title);
        // an IP from ad.trace is matched the same way
        var ip = Row(AnyDeskTraceParser.EventSource, 0, f: [("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Kind", "AcceptedFrom"), ("Direction", "Incoming"), ("Accepted", "da"), ("IP", "203.0.113.7")]);
        var listedIp = ProfileWithSoftware(("AnyDesk", "philandro Software GmbH", "", ""));
        listedIp.NetworkDestinations = [new NetworkDestinationRow { Address = "203.0.113.7", Zone = "LAN", Purpose = "Suport IT" }];
        Assert.Equal(Severity.Info, One(Run([ip], listedIp), "REMOTE-TOOL-EXECUTED").Severity);
        Assert.Equal(Severity.Medium, One(Run([ip], profile), "REMOTE-TOOL-EXECUTED").Severity);
    }

    [Fact]
    public void An_unapproved_tool_with_an_accepted_incoming_session_stays_High()
    {
        Assert.Equal(Severity.High, One(Run([AnyDeskSession(0)]), "REMOTE-TOOL-EXECUTED").Severity);
        // a profile that approves another program does not lower it
        Assert.Equal(Severity.High, One(Run([AnyDeskSession(0)], ProfileWithSoftware(("7-Zip", "Igor Pavlov", "", ""))), "REMOTE-TOOL-EXECUTED").Severity);
    }

    [Fact]
    public void A_tool_approved_only_at_a_path_with_only_a_session_log_is_Medium_to_check_not_High()
    {
        // review finding 2026-11-11: a session has no path, so a path-pattern approval could never match and an officially installed AnyDesk became High
        var profile = ProfileWithSoftware(("AnyDesk", "philandro Software GmbH", @"C:\Program Files (x86)\AnyDesk\*", ""));
        var f = One(Run([AnyDeskSession(0)], profile), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("nici confirmată, nici exclusă", f.Description);
        // with an execution artifact in the approved path it is approved; in another path it is not
        Assert.Equal(Severity.Info, One(Run([AnyDeskSession(0, direction: "Outgoing"), Row("Prefetch", 1, "ANYDESK.EXE", @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe")], profile), "REMOTE-TOOL-EXECUTED").Severity);
        Assert.Equal(Severity.High, One(Run([AnyDeskSession(0), Row("Prefetch", 1, "ANYDESK.EXE", @"C:\Users\bob\Downloads\AnyDesk.exe")], profile), "REMOTE-TOOL-EXECUTED").Severity);
    }

    [Fact]
    public void Existing_behaviour_without_sessions_is_unchanged()
    {
        var f = One(Run([Row("Prefetch", 0, "ANYDESK.EXE", @"C:\Users\bob\Downloads\AnyDesk.exe")]), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Confidence.High, f.Confidence);
        Assert.Contains("nu arată o sesiune", f.Description);
        Assert.DoesNotContain("Sesiuni în jurnalul", f.Description);
    }

    [Fact]
    public void A_flood_of_sessions_lists_only_the_first_lines_and_the_evidence_stays_capped()
    {
        var sessions = Enumerable.Range(0, 2000).Select(i => AnyDeskSession(i % 500, remote: "999" + (100000 + i))).ToList();
        var f = One(Run(sessions), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 2000", f.Description);
        Assert.Contains("… (+1994)", f.Description);
        Assert.True(f.SupportingEvidence.Count <= Wp11Rules.ToolMaxEvidence);
        Assert.True(f.Description.Length < 4000);
    }

    [Fact]
    public void Hostile_text_in_a_session_row_is_cleaned_in_the_finding()
    {
        var f = One(Run([AnyDeskSession(0, remote: "format necunoscut: 12\u202E34\"quote")]), "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain('\u202E', f.Description);
        Assert.DoesNotContain('"', f.Description);
    }

    [Fact]
    public void The_TeamViewer_main_log_sessions_flow_into_the_rule_and_the_same_connection_in_Connections_incoming_counts_once()
    {
        var (_, log) = RemoteToolTestKit.Run(new TeamViewerLogfileParser(), TeamViewerLogfileParser.SourceTypeName, RemoteToolTestKit.Corpus(@"teamviewer_TEST\TeamViewer15_Logfile.log"), RemoteToolTestKit.Zone);
        var t = One(Run(log), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.High, t.Severity);
        Assert.Equal(Confidence.Medium, t.Confidence);
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 3", t.Description);
        Assert.Contains("999000111", t.Description);
        Assert.Contains("neacceptată", t.Description);                       // the failed authentication is listed as not accepted

        // both files of the same station: 3 + 2 rows. The two Connections_incoming rows start within seconds of two sessions of the main log.
        // Without a zone on the items both files keep local times: compared in local time, 3 connections. With the probe zone (W. Europe, UTC+1 in
        // November) the corpus header says UTC+2 from 10 November on purpose (header precedence), so the 08:00 session gets two UTC times an hour
        // apart: rows whose UTC times disagree are NOT paired (decision 2026-11-16, review r2: the estimate may stay higher, never lower) -> 4.
        foreach (var (zone, expected) in new[] { ("", 3), (RemoteToolTestKit.Zone, 4) })
        {
            var (_, log2) = RemoteToolTestKit.Run(new TeamViewerLogfileParser(), TeamViewerLogfileParser.SourceTypeName, RemoteToolTestKit.Corpus(@"teamviewer_TEST\TeamViewer15_Logfile.log"), zone);
            var (_, cin) = RemoteToolTestKit.Run(new TeamViewerConnectionsParser(), TeamViewerConnectionsParser.SourceTypeName, RemoteToolTestKit.Corpus(@"teamviewer_TEST\Connections_incoming.txt"), zone);
            var both = One(Run([.. log2, .. cin]), "REMOTE-TOOL-EXECUTED");
            Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 5", both.Description);
            Assert.Contains($"aproximativ {expected} conexiuni distincte", both.Description);
        }
    }

    [Fact]
    public void The_RustDesk_log_sessions_flow_into_the_rule_and_a_local_connection_is_not_a_session()
    {
        var (_, log) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, RemoteToolTestKit.Corpus(@"rustdesk_TEST\rustdesk_rCURRENT.log"), RemoteToolTestKit.Zone);
        Assert.Equal(6, log.Count);                                           // 3 partner sessions, 1 loopback connection and 2 FileRead transfers
        var t = One(Run(log), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("RustDesk", t.Title);
        Assert.Equal(Severity.High, t.Severity);
        Assert.Equal(Confidence.Medium, t.Confidence);
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 3", t.Description);      // the loopback connection (Session Evidence = nu) is not counted
        Assert.Contains("192.0.2.10", t.Description);
        Assert.Contains("2001:db8::5", t.Description);
        // the log never shows acceptance: it is claimed neither way (lab 2026-11-16: "neacceptată" contradicted "acceptarea nu reiese din jurnal")
        Assert.Contains("acceptarea nu reiese din jurnalul RustDesk", t.Description);
        Assert.DoesNotContain("neacceptată", t.Description);
        Assert.DoesNotContain("acceptată de la", t.Description);
        Assert.DoesNotContain("pentru TeamViewer", t.Description);                     // the TeamViewer pairing note only where it applies
        Assert.DoesNotContain("127.0.0.1", t.Description);
        Assert.DoesNotContain("Peer close", t.Description);                               // no text of the log reaches the finding

        // only a local connection: no session, no finding
        var (_, local) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, _k.Write("rustdesk_rCURRENT.log",
            "[2026-11-09 08:30:00.000102 +01:00] DEBUG [src/server/connection.rs:3001] #903 Connection opened from 127.0.0.1:57510.\n"), RemoteToolTestKit.Zone);
        Assert.Single(local);
        Assert.DoesNotContain(Run(local), f => f.RuleId == "REMOTE-TOOL-EXECUTED");
    }

    [Fact]
    public void RustDesk_transfers_are_one_sentence_of_their_own_not_sessions_and_do_not_change_the_severity()
    {
        var (_, main) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, RemoteToolTestKit.Corpus(@"rustdesk_TEST\rustdesk_rCURRENT.log"), RemoteToolTestKit.Zone);
        var (_, cm) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, RemoteToolTestKit.Corpus(@"rustdesk_TEST\cm\rustdesk_rCURRENT.log"), RemoteToolTestKit.Zone);
        var sessionsOnly = One(Run(main.Where(e => !e.Fields["Kind"].StartsWith("File", StringComparison.Ordinal)).ToList()), "REMOTE-TOOL-EXECUTED");
        var f = One(Run([.. main, .. cm]), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(sessionsOnly.Severity, f.Severity);                                       // informative: text only
        Assert.Equal(sessionsOnly.Confidence, f.Confidence);
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 3", f.Description);           // the transfers are not sessions ...
        Assert.DoesNotContain("alte rânduri despre sesiuni", f.Description);                   // ... and not "other rows about sessions"
        Assert.Contains("Transferuri de fișiere în jurnalul RustDesk: 3 scrise pe stație (primite de la distanță), 2 citite de pe stație (trimise la distanță). Primele:", f.Description);
        Assert.Contains(@"C:\Users\TEST-user\Desktop\TEST-raport.docx", f.Description);
        Assert.Contains("(date din memorie, fără cale)", f.Description);
        Assert.Contains("pot fi falsificate", f.Description);
        Assert.Equal(1, Count(f.Description, "Căile vin din jurnalul stației și pot fi falsificate"));
    }

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    [Fact]
    public void A_RustDesk_log_with_only_transfers_is_evidence_of_use_and_lists_at_most_five_paths()
    {
        var (_, cm) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, RemoteToolTestKit.Corpus(@"rustdesk_TEST\cm\rustdesk_rCURRENT.log"), RemoteToolTestKit.Zone);
        var f = One(Run(cm), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.High, f.Severity);
        Assert.DoesNotContain("Sesiuni în jurnalul propriu al uneltei", f.Description);
        Assert.DoesNotContain("alte rânduri despre sesiuni", f.Description);
        Assert.Contains("Transferuri de fișiere în jurnalul RustDesk: 3 scrise pe stație (primite de la distanță), 0 citite de pe stație (trimise la distanță).", f.Description);
        Assert.Equal(1, Count(f.Description, "Căile vin din jurnalul stației și pot fi falsificate"));   // said once

        TimelineEvent Tr(int i) => Row(RustDeskLogParser.EventSource, i, f: [("Tool Id", "rustdesk"), ("Session Evidence", "da"), ("Kind", "FileRead"), ("File Path", @"C:\TEST\f" + i + ".txt")]);
        var many = One(Run(Enumerable.Range(0, 8).Select(Tr).ToList()), "REMOTE-TOOL-EXECUTED");
        Assert.Contains(@"f4.txt", many.Description);
        Assert.DoesNotContain(@"f5.txt", many.Description);                                      // the first five only
        Assert.Contains("… (+3)", many.Description);
        Assert.Contains("0 scrise pe stație (primite de la distanță), 8 citite de pe stație (trimise la distanță)", many.Description);
    }

    [Fact]
    public void An_IPv4_mapped_IPv6_address_matches_the_IPv4_named_in_the_profile()
    {
        // review r2 2026-11-16: a tool listening on both protocols writes "::ffff:192.0.2.10"; compared as text it never matched "192.0.2.10" -> a false Medium
        TimelineEvent Rd(string ip) => Row(RustDeskLogParser.EventSource, 0, f: [("Tool Id", "rustdesk"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "necunoscut"), ("IP", ip), ("Kind", "IncomingConnection")]);
        var profile = ProfileWithSoftware(("RustDesk", "Purslane Ltd", "", ""));
        profile.NetworkDestinations = [new NetworkDestinationRow { Address = "192.0.2.10", Zone = "LAN", Purpose = "Suport IT" }];
        Assert.Equal(Severity.Info, One(Run([Rd("::ffff:192.0.2.10")], profile), "REMOTE-TOOL-EXECUTED").Severity);
        Assert.Equal(Severity.Medium, One(Run([Rd("::ffff:192.0.2.11")], profile), "REMOTE-TOOL-EXECUTED").Severity);
    }

    [Fact]
    public void An_approved_RustDesk_with_an_incoming_connection_from_an_unlisted_address_is_Medium_to_check_and_never_claims_acceptance()
    {
        var (_, log) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, RemoteToolTestKit.Corpus(@"rustdesk_TEST\rustdesk_rCURRENT.log"), RemoteToolTestKit.Zone);
        var profile = ProfileWithSoftware(("RustDesk", "Purslane Ltd", "", ""));
        // owner decision 2026-11-16: acceptance does not show in the RustDesk log ("Connection opened" is written before authentication), yet the connection is to be checked
        var f = One(Run(log, profile), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("De verificat", f.Description);
        Assert.Contains("Acceptarea nu reiese din jurnalul RustDesk", f.Description);
        Assert.Contains("din care 3 de intrare cu acceptarea necunoscută", f.Description);
        Assert.DoesNotContain("acceptate de la un ID/IP", f.Description);
        Assert.DoesNotContain("(aprobat în profil)", f.Title);
        // some addresses listed, one not: still to check
        var some = ProfileWithSoftware(("RustDesk", "Purslane Ltd", "", ""));
        some.NetworkDestinations = [new NetworkDestinationRow { Address = "192.0.2.10", Zone = "LAN", Purpose = "Suport IT" }, new NetworkDestinationRow { Address = "198.51.100.7", Zone = "LAN", Purpose = "Suport IT" }];
        Assert.Equal(Severity.Medium, One(Run(log, some), "REMOTE-TOOL-EXECUTED").Severity);
        // every partner address listed in the profile: informative again
        var all = ProfileWithSoftware(("RustDesk", "Purslane Ltd", "", ""));
        all.NetworkDestinations = new[] { "192.0.2.10", "198.51.100.7", "2001:db8::5" }.Select(a => new NetworkDestinationRow { Address = a, Zone = "LAN", Purpose = "Suport IT" }).ToList();
        var ok = One(Run(log, all), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.Info, ok.Severity);
        Assert.Contains("(aprobat în profil)", ok.Title);
        // not approved: stays High
        Assert.Equal(Severity.High, One(Run(log), "REMOTE-TOOL-EXECUTED").Severity);
        // the other tools are unchanged: an incoming AnyDesk row with "necunoscut" acceptance is still not a stranger's accepted session
        var request = Row(AnyDeskTraceParser.EventSource, 0, f: [("Tool Id", "anydesk"), ("Session Evidence", "da"), ("Kind", "IncomingSessionRequest"), ("Direction", "Incoming"), ("Accepted", "necunoscut"), ("Remote Id", "999000111")]);
        Assert.Equal(Severity.Info, One(Run([request], ProfileWithSoftware(("AnyDesk", "philandro Software GmbH", "", ""))), "REMOTE-TOOL-EXECUTED").Severity);
    }

    [Fact]
    public void A_RustDesk_log_with_only_loopback_connections_is_presence_not_execution_and_with_a_partner_session_it_is_not_listed_apart()
    {
        var (_, local) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, _k.Write("rustdesk_rCURRENT.log",
            "[2026-11-09 08:30:00.000102 +01:00] DEBUG [src/server/connection.rs:3001] #903 Connection opened from 127.0.0.1:57510.\n[2026-11-09 08:31:00.000000 +01:00] INFO [src/server/connection.rs:3120] #903 Connection closed: connection manager\n"), RemoteToolTestKit.Zone);
        var found = Run(local);
        Assert.DoesNotContain(found, x => x.RuleId == "REMOTE-TOOL-EXECUTED");
        var p = One(found, "REMOTE-TOOL-PRESENT");
        Assert.Contains("RustDesk", p.Title);
        Assert.Equal(Severity.Low, p.Severity);
        Assert.Contains("jurnal RustDesk (doar conexiuni locale)", p.Description);
        Assert.DoesNotContain("Sesiuni în jurnalul propriu al uneltei", p.Description);
        // the sample has partner sessions: execution, and no "only local" evidence kind
        var (_, log) = RemoteToolTestKit.Run(new RustDeskLogParser(), RustDeskLogParser.SourceTypeName, RemoteToolTestKit.Corpus(@"rustdesk_TEST\rustdesk_rCURRENT.log"), RemoteToolTestKit.Zone);
        var all = Run(log);
        Assert.DoesNotContain(all, x => x.RuleId == "REMOTE-TOOL-PRESENT");
        Assert.DoesNotContain("doar conexiuni locale", One(all, "REMOTE-TOOL-EXECUTED").Description);
    }

    [Fact]
    public void Two_TeamViewer_rows_of_one_partner_are_one_connection_only_within_two_minutes_and_across_files()
    {
        TimelineEvent Log(double m) => Row(TeamViewerLogfileParser.EventSource, m, f: [("Tool Id", "teamviewer"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "da"), ("Remote Id", "999000111"), ("Kind", "IncomingSession")]);
        var near = One(Run([Log(0), TvSession(1.5, remote: "999000111")]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("aproximativ 1 conexiuni distincte", near.Description);
        var far = One(Run([Log(0), TvSession(10, remote: "999000111")]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 2", far.Description);
        Assert.DoesNotContain("conexiuni distincte", far.Description);                  // 10 minutes apart: two connections
        var other = One(Run([Log(0), TvSession(1, remote: "999000999")]), "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain("conexiuni distincte", other.Description);                // another partner
        var sameFile = One(Run([Log(0), Log(1.5)]), "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain("conexiuni distincte", sameFile.Description);             // two rows of the same file within 2 minutes are two reconnects, not one
    }

    [Fact]
    public void Pairing_across_the_two_TeamViewer_files_never_chains_does_not_depend_on_order_and_stays_per_station()
    {
        // review r2 2026-11-16: a Connections row between two reconnects of the main log merged them all into one; the result depended on the row order
        static TimelineEvent On(TimelineEvent e, string host) => host.Length == 0 ? e : new TimelineEvent
        {
            Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, Locator = e.Locator, Summary = e.Summary, Host = host,
            Fields = new Dictionary<string, string>(e.Fields, StringComparer.OrdinalIgnoreCase),
        };
        TimelineEvent Log(double m, string host = "") => On(Row(TeamViewerLogfileParser.EventSource, m, f: [("Tool Id", "teamviewer"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "da"), ("Remote Id", "999000111"), ("Kind", "IncomingSession")]), host);
        TimelineEvent Cin(double m, string host = "") => On(TvSession(m, remote: "999000111"), host);
        // two reconnects 1.5 minutes apart, each written in both files: 4 rows, 2 connections, in every order
        foreach (var order in new[] { new[] { Log(0), Log(1.5), Cin(0.1), Cin(1.6) }, [Log(0), Cin(0.1), Log(1.5), Cin(1.6)], [Cin(1.6), Log(1.5), Cin(0.1), Log(0)] })
            Assert.Contains("aproximativ 2 conexiuni distincte", One(Run(order), "REMOTE-TOOL-EXECUTED").Description);
        // one Connections row between two reconnects of the main log pairs with ONE of them: 3 rows, 2 connections, in both orders
        Assert.Contains("aproximativ 2 conexiuni distincte", One(Run([Log(0), Log(1.5), Cin(0.75)]), "REMOTE-TOOL-EXECUTED").Description);
        Assert.Contains("aproximativ 2 conexiuni distincte", One(Run([Log(0), Cin(0.75), Log(1.5)]), "REMOTE-TOOL-EXECUTED").Description);
        // rows of two stations are never one connection
        Assert.DoesNotContain("conexiuni distincte", One(Run([Log(0, "LAB-WS01"), Cin(0.5, "LAB-WS02")]), "REMOTE-TOOL-EXECUTED").Description);
    }

    [Fact]
    public void Ten_thousand_session_rows_of_one_partner_are_counted_quickly()
    {
        // review r2 2026-11-16: the first pairing was quadratic (10,000 rows of one partner: 16.9 s)
        TimelineEvent Log(double m) => Row(TeamViewerLogfileParser.EventSource, m, f: [("Tool Id", "teamviewer"), ("Session Evidence", "da"), ("Direction", "Incoming"), ("Accepted", "da"), ("Remote Id", "999000111"), ("Kind", "IncomingSession")]);
        var rows = Enumerable.Range(0, 5000).SelectMany(i => new[] { Log(i * 3), TvSession(i * 3 + 0.1, remote: "999000111") }).ToList();
        // best of 3, in a collection that does not run in parallel with the others (the same practice as the RDP timing test): the machine load of a
        // full parallel run is not the rule's cost. (Review 2026-11-16: an earlier edit left this test with no body at all; it is restored here.)
        long best = long.MaxValue;
        Finding? f = null;
        for (int k = 0; k < 3; k++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            f = One(Run(rows), "REMOTE-TOOL-EXECUTED");
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 10000", f!.Description);
        Assert.Contains("aproximativ 5000 conexiuni distincte", f.Description);
        Assert.True(best < 5000, $"best of 3 took {best} ms");
    }

    [Fact]
    public void RustDesk_file_transfers_are_not_counted_as_sessions_and_forty_thousand_of_them_are_handled_quickly()
    {
        // review 2026-11-16: the header said "N × sesiune din jurnalul uneltei" for transfer rows, and List.Contains per hit made the rule quadratic (80,000 rows: 9.6 s)
        TimelineEvent Tr(double m, string kind) => Row(RustDeskLogParser.EventSource, m, f: [("Tool Id", "rustdesk"), ("Session Evidence", "da"), ("Kind", kind), ("File Path", $@"C:\Users\TEST-user\Desktop\TEST-{m}.txt")]);
        var few = One(Run([Tr(0, "FileWrite"), Tr(1, "FileRead")]), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("2 × transfer de fișiere din jurnalul uneltei", few.Description);
        Assert.DoesNotContain("× sesiune din jurnalul uneltei", few.Description);
        Assert.Equal(Confidence.Medium, few.Confidence);                                 // a transfer row is not an execution artifact
        var rows = Enumerable.Range(0, 40_000).Select(i => Tr(i * 0.01, i % 2 == 0 ? "FileWrite" : "FileRead")).ToList();
        long best = long.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var f = One(Run(rows), "REMOTE-TOOL-EXECUTED");
            best = Math.Min(best, sw.ElapsedMilliseconds);
            Assert.Contains("20000 scrise pe stație", f.Description);
        }
        Assert.True(best < 5000, $"best of 3 took {best} ms");
    }

    [Fact]
    public void Parsed_TEST_samples_flow_into_the_rule()
    {
        var (_, anydesk) = RemoteToolTestKit.Run(new AnyDeskConnectionTraceParser(), AnyDeskConnectionTraceParser.SourceTypeName, RemoteToolTestKit.Corpus(@"anydesk_TEST\connection_trace.txt"), RemoteToolTestKit.Zone);
        var (_, tv) = RemoteToolTestKit.Run(new TeamViewerConnectionsParser(), TeamViewerConnectionsParser.SourceTypeName, RemoteToolTestKit.Corpus(@"teamviewer_TEST\Connections_incoming.txt"), RemoteToolTestKit.Zone);
        var found = Run([.. anydesk, .. tv]);
        var a = found.Single(x => x.RuleId == "REMOTE-TOOL-EXECUTED" && x.Title.Contains("AnyDesk"));
        var t = found.Single(x => x.RuleId == "REMOTE-TOOL-EXECUTED" && x.Title.Contains("TeamViewer"));
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 4", a.Description);
        Assert.Contains("999000111", a.Description);
        Assert.Contains("2026-11-09 20:21 UTC", a.Description);               // the local 21:21 converted with the zone of the item
        Assert.Contains("Sesiuni în jurnalul propriu al uneltei: 2", t.Description);
        Assert.Contains("cont local test-user", t.Description);
        // without a zone the finding says the time is local and unconverted
        var (_, noZone) = RemoteToolTestKit.Run(new AnyDeskConnectionTraceParser(), AnyDeskConnectionTraceParser.SourceTypeName, RemoteToolTestKit.Corpus(@"anydesk_TEST\connection_trace.txt"));
        var nz = One(Run(noZone), "REMOTE-TOOL-EXECUTED");
        Assert.Contains("ora locală 2026-11-09 21:21:00 (fusul stației nu este cunoscut)", nz.Description);
        Assert.Null(nz.FirstSeenUtc);
    }
}
