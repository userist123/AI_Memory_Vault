using System.Text;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// TeamViewer main log (TeamViewer15_Logfile.log): the synthetic TEST sample, consolidation of the markers into one event per incoming session, the UTC offset of the
/// "Start:" header, and the threat model of a text file written on the analysed station (hostile names and addresses, giant lines, caps, encodings, forged headers,
/// overlapping sessions, future dates, names that look like the file but are not).
/// </summary>
public sealed class TeamViewerLogfileTests : IDisposable
{
    private readonly RemoteToolTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static readonly TeamViewerLogfileParser P = new();
    private const string T = TeamViewerLogfileParser.SourceTypeName;
    private static string Sample => RemoteToolTestKit.Corpus(@"teamviewer_TEST\TeamViewer15_Logfile.log");
    private const string Start1 = "Start:              2026/11/09 21:10:02.000 (UTC+1:00)\n";

    private static string R(string time, string msg, string label = "S0") => $"2026/11/09 {time}  4100       1200 {label}   {msg}\n";
    private static string Inc(string time, string sid) => R(time, $"AppSessionManager::HandleSessionIncoming: Connection incoming, sessionID = {sid}");
    private static string Term(string time, string sid) => R(time, $"SessionManagerDesktop::SessionTerminate: removing session with tvsessionprotocol::TVSessionID = {sid}");
    private static string Alloc(string time, string id, int n = 1) => R(time, $"AuthenticationBlocker::Allocate: allocate ok for DyngateID {id}, attempt number {n}", "G3");
    private static string Auth(string time, string text = "authentication using dynamic password was successful") =>
        R(time, "AuthenticationPasswordLogin_Passive::RunAuthenticationMethod: " + text, "G3");
    private static string Part(string time, string id, string name, string type = "6") => R(time, $"CPersistentParticipantManager::AddParticipant: [{id},111222333] type={type} name={name}", "D3");
    private static string Punch(string time, string addr, string v = "UDPv4") => R(time, $"{v}: punch received a={addr}: (*)", "G2");

    private (ParseResult R, List<TimelineEvent> E) RunText(string text, string tz = "", Encoding? enc = null, string name = "TeamViewer15_Logfile.log") =>
        RemoteToolTestKit.Run(P, T, _k.Write(name, text, enc), tz);

    [Fact]
    public void The_TEST_sample_gives_one_event_per_session_with_the_header_offset_and_no_gap()
    {
        // the probe carries Central Europe, the header says UTC+1 then UTC+2: the header wins for the rows after it
        var (r, e) = RemoteToolTestKit.Run(P, T, Sample, RemoteToolTestKit.Zone);
        Assert.Equal(3, r.Records);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Empty(r.Gaps);                                                        // the rows of the real order (Terminate then Statistics, Statistics then "no session") leave nothing orphaned
        Assert.Equal(0, r.MalformedRecords);
        var a = e[0];
        Assert.Equal(TeamViewerLogfileParser.EventSource, a.Source);
        Assert.Equal("teamviewer", a.Fields["Tool Id"]);
        Assert.Equal("da", a.Fields["Session Evidence"]);
        Assert.Equal("Incoming", a.Fields["Direction"]);
        Assert.Equal("da", a.Fields["Accepted"]);
        Assert.Equal("IncomingSession", a.Fields["Kind"]);
        Assert.Equal("999000111", a.Fields["Remote Id"]);
        Assert.Equal("TEST-Partener", a.Fields["Partner Name"]);
        Assert.Equal("dynamic password", a.Fields["Auth Method"]);
        Assert.Equal("1", a.Fields["Auth Attempts"]);
        Assert.Equal("15.55.0", a.Fields["Client Version"]);
        Assert.Equal("19", a.Fields["Client OS Code"]);
        Assert.Equal("-1000000001", a.Fields["Session Id"]);
        Assert.Equal("192.0.2.10", a.Fields["IP"]);                                  // the link-local fe80::1 was seen too, the other address is preferred
        Assert.Contains("fe80::1", a.Fields["IP All"]);
        Assert.StartsWith("da", a.Fields["IP Zone Stripped"]);
        Assert.Equal("2026-11-09 21:21:55", a.Fields["Local Time"]);
        Assert.Equal("2026-11-09 21:47:10", a.Fields["End Local Time"]);
        Assert.Equal("SessionTerminate", a.Fields["End Reason"]);                    // "Logger started." of a new process in the middle did not close it
        Assert.Equal("1514", a.Fields["Duration Seconds"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 21, 55, 493, TimeSpan.Zero), a.Time.Utc);
        Assert.Equal("2026-11-09T20:47:10.2500000+00:00", a.Fields["End Time UTC"]);
        Assert.Contains("UTC+1:00", a.Fields["Time Zone Source"]);
        Assert.DoesNotContain("TEST-Partener", a.Summary);

        var b = e[1];
        Assert.Equal("IncomingSession", b.Fields["Kind"]);
        Assert.Equal("999000222", b.Fields["Remote Id"]);
        Assert.Equal("2001:db8::5", b.Fields["IP"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 6, 0, 1, TimeSpan.Zero), b.Time.Utc);   // 08:00:01 at UTC+2
        Assert.Contains("UTC+2:00", b.Fields["Time Zone Source"]);

        var c = e[2];
        Assert.Equal("FailedAuthentication", c.Fields["Kind"]);
        Assert.Equal("nu", c.Fields["Accepted"]);
        Assert.Equal("Incoming", c.Fields["Direction"]);
        Assert.Equal("999000333", c.Fields["Remote Id"]);
        Assert.Equal("198.51.100.7", c.Fields["IP"]);
        Assert.Equal("3", c.Fields["Auth Attempts"]);                                // the highest "attempt number", not the rows
        Assert.Equal("3", c.Fields["Failed Authentications"]);
        Assert.Equal("da", c.Fields["SRP Secret Mismatch"]);
        Assert.Equal("LoginFailed", c.Fields["End Command Reason"]);
        Assert.Equal("da", c.Fields["Login Aborted"]);
        Assert.Equal("SessionStatistics", c.Fields["End Reason"]);                   // Statistics came first and closed it; "no session ... aborting login" joined the same session
        Assert.Equal("10", c.Fields["Duration Seconds"]);
        Assert.Contains("3 încercări de autentificare", c.Summary);
        Assert.All(e, x => { Assert.Equal(Classification.Direct, x.Classification); Assert.Equal(Confidence.Medium, x.Confidence); RemoteToolTestKit.AssertNothingHidden(x); });
    }

    [Fact]
    public void A_file_without_any_session_gives_zero_events_and_no_error()
    {
        var (r, e) = RunText(Start1 + R("21:10:03.000", "Service started") + R("21:10:04.000", "Something else", "0"));
        Assert.Empty(e);
        Assert.Equal(0, r.Records);
        Assert.Equal(EvidenceStatus.Empty, r.Status);
        Assert.Equal("", r.Error);
    }

    [Fact]
    public void A_session_without_an_end_is_written_with_no_end_time()
    {
        var (r, e) = RunText(Start1 + Inc("21:20:00.000", "-5") + Alloc("21:20:01.000", "999000111") + Auth("21:20:02.000"));
        Assert.Equal(1, r.Records);
        Assert.Equal("", e[0].Fields["End Local Time"]);
        Assert.Equal("", e[0].Fields["Duration Seconds"]);
        Assert.Contains("fără sfârșit", e[0].Summary);
        Assert.Contains("jurnalul se termină", e[0].Fields["End Reason"]);
    }

    [Fact]
    public void Two_overlapping_sessions_are_told_apart_by_session_id_and_the_ambiguity_is_noted()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Inc("21:00:10.000", "-2")
                 + Alloc("21:00:11.000", "999000999")                        // no session id on this marker: two are open
                 + Auth("21:00:12.000") + Term("21:10:00.000", "-1") + Term("21:20:00.000", "-2");
        var (r, e) = RunText(text);
        Assert.Equal(2, r.Records);
        var s1 = e.Single(x => x.Fields["Session Id"] == "-1");
        var s2 = e.Single(x => x.Fields["Session Id"] == "-2");
        Assert.Equal("", s1.Fields["Remote Id"]);
        Assert.Equal("999000999", s2.Fields["Remote Id"]);                   // the most recent open one
        Assert.Contains("2 sesiuni", s2.Fields["Ambiguity"]);
        Assert.Equal("2026-11-09 21:10:00", s1.Fields["End Local Time"]);   // each ended by its own session id
        Assert.Equal("2026-11-09 21:20:00", s2.Fields["End Local Time"]);
    }

    [Fact]
    public void A_partner_that_reconnects_while_the_old_session_is_open_gets_its_markers_in_the_newest_session()
    {
        // the old session (-1) has the partner id and is still open; the new one (-2) has none yet: the markers with the id go to the NEWEST candidate
        var text = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + Inc("21:00:10.000", "-2")
                 + Alloc("21:00:11.000", "999000111") + Part("21:00:12.000", "999000111", "TEST-Partener");
        var (_, e) = RunText(text);
        var s2 = e.Single(x => x.Fields["Session Id"] == "-2");
        Assert.Equal("999000111", s2.Fields["Remote Id"]);
        Assert.Equal("TEST-Partener", s2.Fields["Partner Name"]);
        Assert.Contains("2 sesiuni", s2.Fields["Ambiguity"]);
        Assert.Equal("", e.Single(x => x.Fields["Session Id"] == "-1").Fields["Partner Name"]);
        // two different partners open together are told apart by their ids
        var two = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + Inc("21:00:10.000", "-2") + Alloc("21:00:11.000", "999000222")
                + Part("21:00:12.000", "999000111", "A") + Part("21:00:13.000", "999000222", "B");
        var (_, e2) = RunText(two);
        Assert.Equal("A", e2.Single(x => x.Fields["Session Id"] == "-1").Fields["Partner Name"]);
        Assert.Equal("B", e2.Single(x => x.Fields["Session Id"] == "-2").Fields["Partner Name"]);
    }

    [Fact]
    public void Markers_with_other_partner_ids_are_added_to_the_note_never_overwriting_it()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000333") + Inc("21:00:05.000", "-2") + Alloc("21:00:06.000", "999000111")
                 + R("21:00:07.000", "InfoCommandHandlerDesktop::ReceivedInfo: connected to 999000444, client version is 15.55.0 , OS=19", "H64")
                 + R("21:00:08.000", "InfoCommandHandlerDesktop::ReceivedInfo: connected to 999000555, client version is 15.55.0 , OS=19", "H64");
        var (_, e) = RunText(text);
        var f = e.Single(x => x.Fields["Session Id"] == "-2").Fields;
        Assert.Equal("999000111", f["Remote Id"]);
        Assert.Contains("999000444", f["Other Remote Ids"]);
        Assert.Contains("999000555", f["Other Remote Ids"]);
        Assert.Contains("2 sesiuni", f["Ambiguity"]);                       // the first reason is still there
        Assert.Contains("alt ID de partener", f["Ambiguity"]);
    }

    [Fact]
    public void The_two_components_that_announce_one_session_do_not_make_two_events()
    {
        var text = Start1 + Inc("21:00:00.000", "-1")
                 + R("21:00:00.001", "SessionManagerDesktop::IncomingConnection: Connection incoming, sessionID = -1")
                 + Alloc("21:00:01.000", "999000111") + Auth("21:00:02.000") + Term("21:30:00.000", "-1");
        var (r, e) = RunText(text);
        Assert.Single(e);
        Assert.Equal(1, r.Records);
    }

    [Fact]
    public void Terminate_then_Statistics_in_the_real_order_of_an_accepted_session_leave_no_gap_and_keep_the_Terminate_time()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + Auth("21:00:02.000")
                 + Term("21:30:00.000", "-1") + R("21:30:00.010", "SessionStatistics[3] Session to 999000111 ended. x");
        var (r, e) = RunText(text);
        Assert.Single(e);
        Assert.Equal("SessionTerminate", e[0].Fields["End Reason"]);
        Assert.Equal("2026-11-09 21:30:00", e[0].Fields["End Local Time"]);
        Assert.Empty(r.Gaps);
        // a second Statistics row for the same partner has no session left to join: counted, not attached to anything
        var (r2, _) = RunText(text + R("21:30:00.020", "SessionStatistics[3] Session to 999000111 ended. x"));
        Assert.Contains(r2.Gaps, g => g.Reason.Contains("1 de marcaje fără sesiune"));
    }

    [Fact]
    public void A_failed_login_in_the_real_order_Statistics_first_then_no_session_then_AbortLogin_is_one_session()
    {
        var text = Start1 + Inc("21:00:00.000", "-9") + Alloc("21:00:01.000", "999000111") + Auth("21:00:02.000", "authentication using fixed password was denied")
                 + Alloc("21:00:05.000", "999000111", 2) + Auth("21:00:06.000", "authentication using fixed password was denied")
                 + R("21:00:06.100", "Carrier[4]::CmdEndSession(): Received CMD_ENDSESSION with reason: LoginFailed")
                 + R("21:00:06.200", "SessionStatistics[4] Session to 999000111 ended. x")
                 + R("21:00:06.210", "SessionManagerDesktop::SessionTerminate: no session with ID=-9, aborting login")
                 + R("21:00:06.220", "LoginManager::AbortLogin: aborting login for sessionID -9");
        var (r, e) = RunText(text);
        var f = e.Single().Fields;
        Assert.Empty(r.Gaps);
        Assert.Equal("FailedAuthentication", f["Kind"]);
        Assert.Equal("nu", f["Accepted"]);
        Assert.Equal("2", f["Auth Attempts"]);
        Assert.Equal("2026-11-09 21:00:06", f["End Local Time"]);
        Assert.Contains("2 încercări de autentificare", e[0].Summary);
        // a session that only shows LoginFailed (no authentication row) is a failure too; one that was accepted stays accepted
        Assert.Equal("nu", RunText(Start1 + Inc("21:00:00.000", "-1") + R("21:00:01.000", "Carrier[4]::CmdEndSession(): Received CMD_ENDSESSION with reason: LoginFailed")).E.Single().Fields["Accepted"]);
        Assert.Equal("da", RunText(Start1 + Inc("21:00:00.000", "-1") + Auth("21:00:01.000") + R("21:00:02.000", "Carrier[4]::CmdEndSession(): Received CMD_ENDSESSION with reason: LoginFailed")).E.Single().Fields["Accepted"]);
    }

    [Fact]
    public void A_new_process_Logger_started_row_does_not_close_an_open_session()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + R("21:05:00.000", "Logger started.", "D3") + Term("21:10:00.000", "-1");
        var (_, e) = RunText(text);
        Assert.Equal("SessionTerminate", e.Single().Fields["End Reason"]);
    }

    [Fact]
    public void A_log_that_starts_inside_a_session_is_rebuilt_from_the_markers_and_flagged()
    {
        var (_, e) = RunText(Start1 + Alloc("21:00:01.000", "999000111") + Auth("21:00:02.000") + Term("21:30:00.000", "-7"));
        Assert.Single(e);
        Assert.Equal("da", e[0].Fields["Session Start Missing"]);
        Assert.Equal("-7", e[0].Fields["Session Id"]);
        Assert.Equal("2026-11-09 21:30:00", e[0].Fields["End Local Time"]);
    }

    [Fact]
    public void A_new_Start_header_closes_the_open_session_without_an_end_time()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111")
                 + "Start:              2026/11/09 23:00:00.000 (UTC+1:00)\n" + R("23:01:00.000", "Service started");
        var (_, e) = RunText(text);
        Assert.Single(e);
        Assert.Contains("repornit", e[0].Fields["End Reason"]);
        Assert.Equal("", e[0].Fields["End Local Time"]);
    }

    [Fact]
    public void The_authentication_result_decides_Accepted_from_the_real_wording_only()
    {
        string AcceptedFor(string authText) => RunText(Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + Auth("21:00:02.000", authText)).E.Single().Fields["Accepted"];
        Assert.Equal("da", AcceptedFor("authentication using dynamic password was successful"));
        Assert.Equal("nu", AcceptedFor("authentication using dynamic password was denied"));
        Assert.Equal("nu", AcceptedFor("authentication using fixed password was denied"));
        // nothing else is guessed: any other text claims neither success nor failure
        Assert.Equal("necunoscut", AcceptedFor("authentication using dynamic password was not successful"));
        Assert.Equal("necunoscut", AcceptedFor("authentication using dynamic password was failed"));
        Assert.Equal("necunoscut", AcceptedFor("authentication using dynamic password failed"));
        Assert.Equal("necunoscut", AcceptedFor("authentication using something new"));
        // a retry in the same session: the last known result wins; the denied attempt stays counted
        var (_, e) = RunText(Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + Auth("21:00:02.000", "authentication using dynamic password was denied")
                             + Alloc("21:00:05.000", "999000111", 2) + Auth("21:00:06.000"));
        Assert.Equal("da", e[0].Fields["Accepted"]);
        Assert.Equal("IncomingSession", e[0].Fields["Kind"]);
        Assert.Equal("1", e[0].Fields["Failed Authentications"]);
        Assert.Equal("2", e[0].Fields["Auth Attempts"]);
        // the attempt count is the highest "attempt number" the log shows, not the number of rows
        var rows = RunText(Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111", 4) + Alloc("21:00:02.000", "999000111", 4)).E.Single();
        Assert.Equal("4", rows.Fields["Auth Attempts"]);
    }

    [Fact]
    public void A_hostile_partner_name_is_cleaned_and_capped()
    {
        foreach (var name in new[] { "TEST‮evil", "a\u0001b\u0007c\u001Bd", new string('X', 10_000), "tag\U000E0041\U000E0042x" })
        {
            var (_, e) = RunText(Start1 + Inc("21:00:00.000", "-1") + Part("21:00:01.000", "999000111", name) + Term("21:01:00.000", "-1"));
            var f = e.Single().Fields;
            RemoteToolTestKit.AssertNothingHidden(e[0]);
            Assert.True(f["Partner Name"].Length <= 130, "name not capped: " + f["Partner Name"].Length);
            if (name.Contains('‮')) Assert.Contains("U+202E", f["Partner Name"]);     // the shared sanitiser names the removed character
        }
    }

    [Fact]
    public void Invalid_addresses_are_not_kept_and_are_counted()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Punch("21:00:01.000", "999.1.1.1:5000") + Punch("21:00:02.000", "010.0.0.1:5000")
                 + Punch("21:00:03.000", "not-an-ip:5000") + Punch("21:00:04.000", "1.2:5000");
        var f = RunText(text).E.Single().Fields;
        Assert.False(f.ContainsKey("IP"));
        Assert.Equal("4", f["IP Invalid Count"]);
        // and a valid one after them is kept
        var g = RunText(text + Punch("21:00:05.000", "203.0.113.9:5000")).E.Single().Fields;
        Assert.Equal("203.0.113.9", g["IP"]);
    }

    [Fact]
    public void A_link_local_address_alone_is_kept_with_a_note()
    {
        var f = RunText(Start1 + Inc("21:00:00.000", "-1") + Punch("21:00:01.000", "fe80::a503:bfed:c231:e6f1%5:51640", "UDPv6")).E.Single().Fields;
        Assert.Equal("fe80::a503:bfed:c231:e6f1", f["IP"]);
        Assert.Contains("link-local", f["IP Note"]);
        Assert.StartsWith("da", f["IP Zone Stripped"]);
    }

    [Fact]
    public void A_five_megabyte_line_without_a_newline_is_cut_and_does_not_hang()
    {
        var big = "2026/11/09 21:00:00.000  4100       1200 S0   AppSessionManager::HandleSessionIncoming: Connection incoming, sessionID = -1 " + new string('A', 5 * 1024 * 1024);
        var (r, e) = RunText(Start1 + big);
        Assert.Equal(1, r.Records);
        Assert.StartsWith("da", e[0].Fields["Line Truncated"]);
        var (r2, e2) = RunText(new string('B', 5 * 1024 * 1024));          // no date prefix, no newline: a header-like blob, not an event
        Assert.Empty(e2);
        Assert.NotEqual(EvidenceStatus.Failed, r2.Status);
    }

    [Fact]
    public void A_file_over_the_cap_is_refused_with_a_reason()
    {
        var path = _k.Write("TeamViewer15_Logfile.log", Start1);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write)) fs.SetLength(TeamViewerLogfileParser.MaxFileBytes + 1);
        var (r, e) = RemoteToolTestKit.Run(P, T, path);
        Assert.Empty(e);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains(EvidenceParserBase.TooLargeReason, r.Error);
    }

    [Fact]
    public void Open_sessions_are_capped()
    {
        var sb = new StringBuilder(Start1);
        for (int i = 0; i < TeamViewerLogfileParser.MaxOpenSessions + 10; i++) sb.Append(Inc("21:00:00.000", (-1000 - i).ToString()));
        var (r, e) = RunText(sb.ToString());
        Assert.Equal(TeamViewerLogfileParser.MaxOpenSessions + 10, r.Records);
        Assert.Contains(e, x => x.Fields["End Reason"].Contains("plafonul"));
    }

    [Fact]
    public void UTF16_LE_with_a_BOM_and_ANSI_are_read()
    {
        var text = Start1 + Inc("21:00:00.000", "-1") + Alloc("21:00:01.000", "999000111") + Term("21:10:00.000", "-1");
        var utf16 = new UnicodeEncoding(false, true);
        var data = utf16.GetPreamble().Concat(utf16.GetBytes(text.Replace("\n", "\r\n"))).ToArray();
        var path = _k.Write("TeamViewer15_Logfile.log", data);
        var (r, e) = RemoteToolTestKit.Run(P, T, path);
        Assert.Equal(1, r.Records);
        Assert.Equal("999000111", e[0].Fields["Remote Id"]);
        Assert.Equal(T, InvestigationPipeline.ImportType(path));
        var (r2, _) = RunText(text, enc: Encoding.Latin1);
        Assert.Equal(1, r2.Records);
    }

    [Theory]
    [InlineData("+5:45", 5, 45)]
    [InlineData("-3:30", -3, -30)]
    [InlineData("+3:00", 3, 0)]
    public void The_header_offset_is_applied_including_half_and_quarter_hours(string offset, int h, int m)
    {
        var text = $"Start:              2026/11/09 20:00:00.000 (UTC{offset})\n" + Inc("21:00:00.000", "-1");
        var (_, e) = RunText(text, RemoteToolTestKit.Zone);                    // the probe zone must NOT be used
        var expect = new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero) - new TimeSpan(h, m, 0);
        Assert.Equal(expect, e.Single().Time.Utc);
    }

    [Theory]
    [InlineData("(UTC+25:00)")]
    [InlineData("(UTC+3:75)")]
    [InlineData("(UTC+14:30)")]
    [InlineData("(UTC+x:00)")]
    public void An_impossible_header_offset_is_ignored_and_the_probe_zone_is_used(string offset)
    {
        var (_, e) = RunText($"Start:              2026/11/09 20:00:00.000 {offset}\n" + Inc("21:00:00.000", "-1"), RemoteToolTestKit.Zone);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero), e.Single().Time.Utc);   // 21:00 in Central Europe
        Assert.StartsWith("fusul probei", e.Single().Fields["Time Zone Source"]);
    }

    [Fact]
    public void A_second_Start_with_an_impossible_offset_stops_the_previous_offset_from_applying()
    {
        var text = "Start:              2026/11/09 20:00:00.000 (UTC+5:45)\n" + Inc("21:00:00.000", "-1") + Term("21:01:00.000", "-1")
                 + "Start:              2026/11/09 22:00:00.000 (UTC+25:00)\n" + Inc("23:00:00.000", "-2");
        var (_, e) = RunText(text, RemoteToolTestKit.Zone);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 15, 15, 0, TimeSpan.Zero), e.Single(x => x.Fields["Session Id"] == "-1").Time.Utc);   // UTC+5:45
        var second = e.Single(x => x.Fields["Session Id"] == "-2");
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero), second.Time.Utc);          // 23:00 in Central Europe: the probe zone, not +5:45
        Assert.Contains("nu poate fi citit", second.Fields["Time Zone Source"]);
        // without a probe zone the time is left local and unconverted instead of using the old offset
        var (_, none) = RunText(text);
        Assert.False(none.Single(x => x.Fields["Session Id"] == "-2").Time.IsKnown);
    }

    [Fact]
    public void Without_a_Start_header_the_probe_zone_is_used_and_without_it_the_time_stays_local()
    {
        var text = Inc("21:00:00.000", "-1");
        var (_, withZone) = RunText(text, RemoteToolTestKit.Zone);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero), withZone.Single().Time.Utc);
        var (_, none) = RunText(text);
        Assert.False(none.Single().Time.IsKnown);
        Assert.Contains("neconvertită", none.Single().TimeZoneBasis);
        Assert.Equal("2026-11-09 21:00:00", none.Single().Fields["Local Time"]);
    }

    [Fact]
    public void A_start_row_in_the_middle_of_a_message_is_not_a_header()
    {
        // a dated row is never a header: forged text after the date prefix cannot change the offset
        var (_, e) = RunText(Start1 + R("21:00:00.000", "Start:              2026/11/09 20:00:00.000 (UTC+9:00)") + Inc("21:00:01.000", "-1"));
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 0, 1, TimeSpan.Zero), e.Single().Time.Utc);
    }

    [Fact]
    public void A_time_in_the_future_or_an_impossible_date_is_not_placed_on_the_timeline()
    {
        var (_, future) = RunText(Start1 + "2099/01/01 10:00:00.000  4100       1200 S0   AppSessionManager::HandleSessionIncoming: Connection incoming, sessionID = -1\n");
        Assert.False(future.Single().Time.IsKnown);
        Assert.Equal("ora este în viitor", future.Single().Fields["Time Problem"]);
        var (r, bad) = RunText(Start1 + "2026/13/45 10:00:00.000  4100       1200 S0   AppSessionManager::HandleSessionIncoming: Connection incoming, sessionID = -1\n");
        Assert.Empty(bad);
        Assert.True(r.MalformedRecords > 0);
        var (_, old) = RunText(Start1 + "1999/01/01 10:00:00.000  4100       1200 S0   AppSessionManager::HandleSessionIncoming: Connection incoming, sessionID = -1\n");
        Assert.False(old.Single().Time.IsKnown);
        Assert.Contains("imposibilă", old.Single().Fields["Time Problem"]);
    }

    [Fact]
    public void The_file_is_typed_by_name_and_content_and_a_look_alike_is_not()
    {
        var sample = File.ReadAllBytes(Sample);
        Assert.Equal(T, InvestigationPipeline.ImportType(Sample));
        Assert.Equal(T, EvidenceFingerprint.Detect(Sample));
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("TeamViewer15_Logfile_OLD.log", sample)));
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("TeamViewer15_Logfile_1.log", sample)));       // stored copy of a second file of the same name
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("TeamViewer9_Logfile.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("TeamViewer15_Logfile.txt", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("MyTeamViewer15_Logfile.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("TeamViewer_Logfile.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("TeamViewer15_Logfile_OLD2.log", sample)));
        // the right name with other content stays "file"
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("TeamViewer15_Logfile.log", "just some notes\nno date prefix here\n")));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("TeamViewer15_Logfile.log", "2026-11-09 21:00:00 not the TeamViewer row format\n")));
        Assert.Null(RemoteToolLogFormats.ByName("TeamViewer15_Logfile_OLD.log.bak"));
        Assert.Equal(RemoteToolLogFormats.TeamViewerLogfile, RemoteToolLogFormats.ByName("TEAMVIEWER15_LOGFILE_OLD.LOG"));
        Assert.True(RemoteToolLogFormats.IsKnown(T));
    }

    [Fact]
    public void Descriptor_declares_the_limits_and_the_file_transfer_gap()
    {
        var d = P.Descriptor;
        Assert.Equal(T, d.SourceTypes.Single());
        Assert.Contains(d.Limitations, x => x.Contains("Transferurile de fișiere", StringComparison.Ordinal));
        Assert.Equal(TeamViewerLogfileParser.MaxFileBytes, d.MaxInputBytes);
    }
}
