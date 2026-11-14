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
