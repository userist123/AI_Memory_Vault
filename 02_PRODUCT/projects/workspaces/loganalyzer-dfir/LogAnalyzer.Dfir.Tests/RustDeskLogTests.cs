using System.Diagnostics;
using System.Text;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// RustDesk log (rustdesk_rCURRENT.log): the synthetic TEST sample, one event per "#id" session, the exact UTC time from the offset written on every row, and the threat
/// model of a text file written on the analysed station (hostile reasons and addresses, impossible offsets, giant lines, caps, encodings, interleaved sessions and an
/// ambiguous wakelock, reused ids, future dates, names that look like the file but are not, thousands of sessions).
/// </summary>
public sealed class RustDeskLogTests : IDisposable
{
    private readonly RemoteToolTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static readonly RustDeskLogParser P = new();
    private const string T = RustDeskLogParser.SourceTypeName;
    private static string Sample => RemoteToolTestKit.Corpus(@"rustdesk_TEST\rustdesk_rCURRENT.log");

    private static string R(string time, string msg, string level = "DEBUG", string offset = "+01:00", string date = "2026-11-09", string src = @"src\server\connection.rs:3001") =>
        $"[{date} {time} {offset}] {level} [{src}] {msg}\n";
    private static string Open(string time, string id, string addr, string offset = "+01:00", string date = "2026-11-09") => R(time, (id.Length > 0 ? $"#{id} " : "") + $"Connection opened from {addr}.", offset: offset, date: date);
    private static string Close(string time, string id, string reason = "Peer close") => R(time, (id.Length > 0 ? $"#{id} " : "") + "Connection closed: " + reason, "INFO");
    private static string Loop(string time, string id) => R(time, (id.Length > 0 ? $"#{id} " : "") + "connection loop exited");
    private static string Wake(string time, string display) => R(time, $"new wakelock, require display on: {display}", "INFO", src: @"src\platform\mod.rs:210");
    private static string Drop(string time) => R(time, "drop wakelock", "INFO", src: @"src\platform\mod.rs:215");

    private (ParseResult R, List<TimelineEvent> E) RunText(string text, string tz = "", Encoding? enc = null, string name = "rustdesk_rCURRENT.log") =>
        RemoteToolTestKit.Run(P, T, _k.Write(name, text, enc), tz);

    [Fact]
    public void The_TEST_sample_gives_one_event_per_session_with_the_exact_UTC_time_and_no_gap()
    {
        // the probe zone is irrelevant: the offset is written on every row
        foreach (var zone in new[] { "", RemoteToolTestKit.Zone, "Tokyo Standard Time" })
        {
            var (r, e) = RemoteToolTestKit.Run(P, T, Sample, zone);
            Assert.Equal(4, r.Records);
            Assert.Equal(EvidenceStatus.Success, r.Status);
            Assert.Empty(r.Gaps);
            Assert.Equal(0, r.MalformedRecords);
            Assert.Equal(["903", "901", "902", "904"], e.Select(x => x.Fields["Session Id"]).ToArray());

            var local = e[0];
            Assert.Equal("LocalConnection", local.Fields["Kind"]);
            Assert.Equal("nu", local.Fields["Session Evidence"]);                    // loopback: a local process or a relay, not a partner
            Assert.Equal("127.0.0.1", local.Fields["IP"]);
            Assert.Equal("57510", local.Fields["Remote Port"]);
            Assert.Equal("connection manager", local.Fields["Close Reason"]);

            var a = e[1];
            Assert.Equal(RustDeskLogParser.EventSource, a.Source);
            Assert.Equal("rustdesk", a.Fields["Tool Id"]);
            Assert.Equal("da", a.Fields["Session Evidence"]);
            Assert.Equal("Incoming", a.Fields["Direction"]);
            Assert.Equal("necunoscut", a.Fields["Accepted"]);                        // the log does not show acceptance
            Assert.Equal("IncomingConnection", a.Fields["Kind"]);
            Assert.Equal("192.0.2.10", a.Fields["IP"]);
            Assert.Equal("51234", a.Fields["Remote Port"]);
            Assert.Equal("Peer close", a.Fields["Close Reason"]);
            Assert.Equal("da", a.Fields["Display"]);                                 // the only partner session open at the wakelock row
            Assert.Equal("da", a.Fields["Wakelock Dropped"]);
            Assert.Equal("Connection closed", a.Fields["End Reason"]);
            Assert.Equal("da", a.Fields["Connection Loop Exited"]);                  // the loop-exited row came after the closed row
            Assert.Equal("600", a.Fields["Duration Seconds"]);
            Assert.Equal("2026-11-09 08:31:00", a.Fields["Local Time"]);
            Assert.Equal("+01:00", a.Fields["Time Offset"]);
            Assert.Equal(new DateTimeOffset(2026, 11, 9, 7, 31, 0, 250, TimeSpan.Zero), a.Time.Utc);
            Assert.Equal(new DateTimeOffset(2026, 11, 9, 7, 41, 0, 500, TimeSpan.Zero), DateTimeOffset.Parse(a.Fields["End Time UTC"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.DoesNotContain("Peer close", a.Summary);                          // log text never reaches the summary

            var b = e[2];
            Assert.Equal("198.51.100.7", b.Fields["IP"]);
            Assert.Equal("Broken pipe (os error 32)", b.Fields["Close Reason"]);
            Assert.Equal("connection loop exited", b.Fields["End Reason"]);           // the loop-exited row came first and closed it, the reason joined afterwards
            Assert.Equal("300", b.Fields["Duration Seconds"]);
            Assert.Equal("", b.Fields["Display"]);                                   // no wakelock row

            var c = e[3];
            Assert.Equal("2001:db8::5", c.Fields["IP"]);
            Assert.Equal("443", c.Fields["Remote Port"]);
            Assert.Equal(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero), c.Time.Utc);   // 12:00 at +02:00
            Assert.Equal("+02:00", c.Fields["Time Offset"]);
            Assert.All(e, x => { Assert.Equal(Classification.Direct, x.Classification); Assert.Equal(Confidence.Medium, x.Confidence); RemoteToolTestKit.AssertNothingHidden(x); });
        }
    }

    [Fact]
    public void A_file_without_any_session_gives_zero_events_and_no_error()
    {
        var (r, e) = RunText(R("08:00:00.000000", "TEST: server started", "INFO") + R("08:00:01.000000", "something else", "WARN"));
        Assert.Empty(e);
        Assert.Equal(0, r.Records);
        Assert.Equal(EvidenceStatus.Empty, r.Status);
        Assert.Equal("", r.Error);
    }

    [Fact]
    public void A_connection_without_a_close_is_written_without_an_end_time()
    {
        var (r, e) = RunText(Open("08:00:00.000000", "901", "192.0.2.1:1000"));
        Assert.Equal(1, r.Records);
        Assert.Equal("", e[0].Fields["End Local Time"]);
        Assert.Equal("", e[0].Fields["Duration Seconds"]);
        Assert.Equal("", e[0].Fields["Close Reason"]);
        Assert.Contains("fără sfârșit", e[0].Summary);
        Assert.Contains("jurnalul se termină", e[0].Fields["End Reason"]);
    }

    [Fact]
    public void A_close_without_an_open_is_counted_and_never_becomes_an_event()
    {
        var (r, e) = RunText(Close("08:00:00.000000", "950") + Loop("08:00:00.100000", "950") + Open("08:01:00.000000", "951", "192.0.2.1:1000") + Close("08:02:00.000000", "951"));
        Assert.Single(e);
        Assert.Equal("951", e[0].Fields["Session Id"]);
        var gap = Assert.Single(r.Gaps);
        Assert.Contains("2 de închideri fără conexiune deschisă", gap.Reason);
    }

    [Fact]
    public void Two_interleaved_sessions_are_told_apart_by_id_and_an_ambiguous_wakelock_is_not_guessed()
    {
        var text = Open("08:00:00.000000", "901", "192.0.2.1:1001") + Open("08:00:01.000000", "902", "192.0.2.2:1002") + Wake("08:00:02.000000", "true")
                 + Close("08:01:00.000000", "902", "Peer close") + Wake("08:01:30.000000", "false") + Close("08:02:00.000000", "901", "Reset by the peer");
        var (r, e) = RunText(text);
        Assert.Equal(2, r.Records);
        var s901 = e.Single(x => x.Fields["Session Id"] == "901");
        var s902 = e.Single(x => x.Fields["Session Id"] == "902");
        Assert.Equal("Reset by the peer", s901.Fields["Close Reason"]);
        Assert.Equal("Peer close", s902.Fields["Close Reason"]);
        Assert.Equal("59", s902.Fields["Duration Seconds"]);
        Assert.Equal("120", s901.Fields["Duration Seconds"]);
        // with two partner sessions open the first wakelock row belongs to neither (the ambiguity is noted on both); once only 901 is open the second one is its
        Assert.Contains("2 sesiuni", s901.Fields["Ambiguity"]);
        Assert.Contains("2 sesiuni", s902.Fields["Ambiguity"]);
        Assert.Equal("nu", s901.Fields["Display"]);
        Assert.Equal("", s902.Fields["Display"]);
        Assert.Equal("1", s901.Fields["Wakelock Rows"]);
        Assert.False(s902.Fields.ContainsKey("Wakelock Rows"));
    }

    [Fact]
    public void A_local_connection_open_beside_one_partner_session_does_not_make_the_wakelock_ambiguous()
    {
        var (_, e) = RunText(Open("08:00:00.000000", "1", "127.0.0.1:5000") + Open("08:00:01.000000", "2", "192.0.2.1:1002") + Wake("08:00:02.000000", "true") + Close("08:01:00.000000", "2") + Close("08:01:01.000000", "1"));
        Assert.Equal("da", e.Single(x => x.Fields["Session Id"] == "2").Fields["Display"]);
        Assert.Equal("", e.Single(x => x.Fields["Session Id"] == "1").Fields["Display"]);
    }

    [Fact]
    public void A_reused_id_after_a_rotation_starts_a_new_session()
    {
        var text = Open("08:00:00.000000", "7", "192.0.2.1:1000") + Close("08:01:00.000000", "7") + Open("09:00:00.000000", "7", "192.0.2.9:2000") + Close("09:02:00.000000", "7", "Reset by the peer")
                 + Open("10:00:00.000000", "7", "192.0.2.5:3000") + Open("11:00:00.000000", "7", "192.0.2.6:4000") + Close("11:01:00.000000", "7");
        var (r, e) = RunText(text);
        Assert.Equal(4, r.Records);
        Assert.Equal(["192.0.2.1", "192.0.2.9", "192.0.2.5", "192.0.2.6"], e.Select(x => x.Fields["IP"]).ToArray());
        Assert.Equal("60", e[0].Fields["Duration Seconds"]);
        Assert.Equal("120", e[1].Fields["Duration Seconds"]);
        Assert.Equal("Reset by the peer", e[1].Fields["Close Reason"]);
        // the third session never closed before the id was reused: no end, and it says why; the close joined the fourth
        Assert.Equal("", e[2].Fields["End Local Time"]);
        Assert.Contains("id reutilizat", e[2].Fields["End Reason"]);
        Assert.Equal("60", e[3].Fields["Duration Seconds"]);
        Assert.Empty(r.Gaps);
    }

    [Fact]
    public void Old_versions_without_an_id_are_closed_only_when_it_is_unambiguous()
    {
        var (_, one) = RunText(Open("08:00:00.000000", "", "192.0.2.1:1000") + Close("08:01:00.000000", "") + Loop("08:01:00.100000", ""));
        Assert.Equal("60", one.Single().Fields["Duration Seconds"]);
        Assert.Equal("", one.Single().Fields["Session Id"]);
        Assert.Equal("da", one.Single().Fields["Connection Loop Exited"]);
        // two open sessions without id: the close is not guessed, it is counted
        var (r, two) = RunText(Open("08:00:00.000000", "", "192.0.2.1:1000") + Open("08:00:01.000000", "", "192.0.2.2:1001") + Close("08:01:00.000000", ""));
        Assert.Equal(2, two.Count);
        Assert.All(two, x => Assert.Equal("", x.Fields["End Local Time"]));
        Assert.Single(r.Gaps);
    }

    [Fact]
    public void A_close_without_an_id_is_attached_only_when_exactly_one_session_is_a_candidate()
    {
        // A is closed by its "connection loop exited" row; B and C are open; a "Connection closed" without id must NOT reach A (three candidates: not guessed, counted)
        var text = Open("08:00:00.000000", "", "192.0.2.1:1001") + Loop("08:00:10.000000", "")
                 + Open("08:01:00.000000", "", "192.0.2.2:1002") + Open("08:01:01.000000", "", "192.0.2.3:1003") + Close("08:02:00.000000", "", "Peer close");
        var (r, e) = RunText(text);
        Assert.Equal(3, r.Records);
        var a = e.Single(x => x.Fields["IP"] == "192.0.2.1");
        Assert.Equal("", a.Fields["Close Reason"]);
        Assert.Equal("connection loop exited", a.Fields["End Reason"]);
        Assert.All(e.Where(x => x != a), x => { Assert.Equal("", x.Fields["Close Reason"]); Assert.Equal("", x.Fields["End Local Time"]); });
        Assert.Contains("1 de închideri fără conexiune deschisă", Assert.Single(r.Gaps).Reason);
        // with B and C gone, A (closed without a reason) is the only candidate: the reason joins it
        var (_, one) = RunText(Open("08:00:00.000000", "", "192.0.2.1:1001") + Loop("08:00:10.000000", "") + Close("08:00:11.000000", "", "Peer close"));
        Assert.Equal("Peer close", one.Single().Fields["Close Reason"]);
        // an open session without id plus an earlier one closed without a reason are two candidates: the close is counted, not guessed
        var (r2, two) = RunText(Open("08:00:00.000000", "", "192.0.2.1:1001") + Loop("08:00:10.000000", "") + Open("08:01:00.000000", "", "192.0.2.2:1002") + Close("08:02:00.000000", "", "Peer close"));
        Assert.Equal(2, two.Count);
        Assert.All(two, x => Assert.Equal("", x.Fields["Close Reason"]));
        Assert.Single(r2.Gaps);
    }

    [Fact]
    public void A_hostile_close_reason_is_cleaned_and_capped()
    {
        foreach (var reason in new[] { "TEST‮evil", "a\u0001b\u0007c\u001Bd", new string('X', 10_000), "tag\U000E0041\U000E0042x", "line\tbreak" })
        {
            var (_, e) = RunText(Open("08:00:00.000000", "901", "192.0.2.1:1000") + Close("08:01:00.000000", "901", reason));
            var f = e.Single().Fields;
            RemoteToolTestKit.AssertNothingHidden(e[0]);
            Assert.True(f["Close Reason"].Length <= 140, "reason not capped: " + f["Close Reason"].Length);
            Assert.True(e[0].Summary.Length < 400, "summary carries log text");
            if (reason.Contains('‮')) Assert.Contains("U+202E", f["Close Reason"]);   // the shared sanitiser names the removed character
        }
    }

    [Theory]
    [InlineData("192.0.2.5:5000", "192.0.2.5", "5000", "IncomingConnection", "valid")]
    [InlineData("[2001:db8::1]:443", "2001:db8::1", "443", "IncomingConnection", "valid")]
    [InlineData("192.0.2.5", "192.0.2.5", "", "IncomingConnection", "valid")]
    [InlineData("127.0.0.1:57510", "127.0.0.1", "57510", "LocalConnection", "valid")]
    [InlineData("127.9.9.9:1", "127.9.9.9", "1", "LocalConnection", "valid")]
    [InlineData("[::1]:7", "::1", "7", "LocalConnection", "valid")]
    [InlineData("[::ffff:127.0.0.1]:7", "::ffff:127.0.0.1", "7", "LocalConnection", "valid")]
    [InlineData("999.1.1.1:5", "", "", "IncomingConnection", "invalid")]
    [InlineData("010.0.0.1:5", "", "", "IncomingConnection", "invalid")]       // a leading zero would be read as octal: 8.0.0.1
    [InlineData("not-an-ip:5", "", "", "IncomingConnection", "invalid")]
    [InlineData("1.2:5", "", "", "IncomingConnection", "invalid")]
    [InlineData("2001:db8::1:443", "", "", "IncomingConnection", "invalid")]    // an IPv6 without brackets is not what RustDesk writes
    [InlineData("192.0.2.5:99999", "", "", "IncomingConnection", "invalid")]
    [InlineData("192.0.2.5:abc", "", "", "IncomingConnection", "invalid")]
    [InlineData("[2001:db8::1", "", "", "IncomingConnection", "invalid")]
    [InlineData("[2001:db8::1]x443", "", "", "IncomingConnection", "invalid")]
    [InlineData("[fe80::1%5]:443", "", "", "IncomingConnection", "invalid")]
    public void Addresses_are_validated_and_loopback_is_not_a_partner(string addr, string ip, string port, string kind, string status)
    {
        var (_, e) = RunText(Open("08:00:00.000000", "901", addr));
        var f = e.Single().Fields;
        Assert.Equal(kind, f["Kind"]);
        Assert.Equal(status, f["IP Status"]);
        Assert.Equal(ip, f.TryGetValue("IP", out var got) ? got : "");
        Assert.Equal(port, f.TryGetValue("Remote Port", out var p) ? p : "");
        Assert.Equal(kind == "LocalConnection" ? "nu" : "da", f["Session Evidence"]);
        if (status == "invalid") Assert.True(f.ContainsKey("IP Raw"));
        RemoteToolTestKit.AssertNothingHidden(e[0]);
    }

    [Theory]
    [InlineData("+05:45", 6, 15)]
    [InlineData("-03:30", 15, 30)]
    [InlineData("+00:00", 12, 0)]
    [InlineData("+14:00", 22, 0)]
    public void The_row_offset_is_applied_including_half_and_quarter_hours(string offset, int utcHour, int utcMinute)
    {
        var (_, e) = RunText(Open("12:00:00.000000", "901", "192.0.2.1:1000", offset), RemoteToolTestKit.Zone);     // the probe zone must NOT be used
        var expect = new DateTimeOffset(2026, 11, 9, utcHour, utcMinute, 0, TimeSpan.Zero);
        if (offset == "+14:00") expect = new DateTimeOffset(2026, 11, 8, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(expect, e.Single().Time.Utc);
        Assert.Equal(offset, e.Single().Fields["Time Offset"]);
    }

    [Theory]
    [InlineData("+25:00")]
    [InlineData("+03:75")]
    [InlineData("+14:30")]
    [InlineData("-14:01")]
    [InlineData("+99:99")]
    public void An_impossible_offset_rejects_the_row_and_it_is_counted(string offset)
    {
        var (r, e) = RunText(Open("12:00:00.000000", "901", "192.0.2.1:1000", offset) + Open("12:00:01.000000", "902", "192.0.2.2:1000"));
        Assert.Equal("902", e.Single().Fields["Session Id"]);                    // only the valid row
        Assert.True(r.MalformedRecords >= 1);
        Assert.Contains("decalaj imposibil", Assert.Single(r.Gaps).Reason);
    }

    [Fact]
    public void An_offset_in_the_middle_of_a_message_or_a_forged_prefix_inside_it_changes_nothing()
    {
        // a continuation line that looks like a marker, but has no row prefix, is not an event
        var text = Open("08:00:00.000000", "901", "192.0.2.1:1000") + "#555 Connection opened from 203.0.113.9:1.\n" + "  [2026-11-09 08:00:01.000000 +09:00] still a continuation\n" + Close("08:01:00.000000", "901");
        var (r, e) = RunText(text);
        Assert.Single(e);
        Assert.Equal(0, r.MalformedRecords);
        Assert.Equal("901", e[0].Fields["Session Id"]);
    }

    [Fact]
    public void A_message_on_several_lines_keeps_its_row_and_the_continuation_lines_are_ignored()
    {
        var text = Open("08:00:00.000000", "901", "192.0.2.1:1000") + "continuation one\n\tcontinuation two\n\n" + Close("08:01:00.000000", "901", "Peer close\nsecond line of the reason") + "#901 Connection closed: forged\n";
        var (r, e) = RunText(text);
        Assert.Single(e);
        Assert.Equal("Peer close", e[0].Fields["Close Reason"]);                // the reason is the first line only
        Assert.Equal("60", e[0].Fields["Duration Seconds"]);
        Assert.Empty(r.Gaps);                                                   // the forged continuation is not a row, so it is not an orphan close either
    }

    [Fact]
    public void A_five_megabyte_row_is_cut_and_does_not_hang()
    {
        var (r, e) = RunText(Open("08:00:00.000000", "901", "192.0.2.1:1000") + R("08:01:00.000000", "#901 Connection closed: " + new string('A', 5 * 1024 * 1024), "INFO"));
        Assert.Equal(1, r.Records);
        Assert.StartsWith("da", e[0].Fields["Line Truncated"]);
        Assert.True(e[0].Fields["Close Reason"].Length <= 140);
        Assert.Equal("60", e[0].Fields["Duration Seconds"]);
        var (r2, e2) = RunText(new string('B', 5 * 1024 * 1024));              // no prefix, no newline: a blob, not an event
        Assert.Empty(e2);
        Assert.NotEqual(EvidenceStatus.Failed, r2.Status);
    }

    [Fact]
    public void A_file_over_the_cap_is_refused_with_a_reason()
    {
        var path = _k.Write("rustdesk_rCURRENT.log", Open("08:00:00.000000", "901", "192.0.2.1:1000"));
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write)) fs.SetLength(RustDeskLogParser.MaxFileBytes + 1);
        var (r, e) = RemoteToolTestKit.Run(P, T, path);
        Assert.Empty(e);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains(EvidenceParserBase.TooLargeReason, r.Error);
    }

    [Fact]
    public void UTF16_LE_with_a_BOM_and_ANSI_are_read()
    {
        var text = Open("08:00:00.000000", "901", "192.0.2.1:1000") + Close("08:10:00.000000", "901");
        var utf16 = new UnicodeEncoding(false, true);
        var data = utf16.GetPreamble().Concat(utf16.GetBytes(text.Replace("\n", "\r\n"))).ToArray();
        var path = _k.Write("rustdesk_rCURRENT.log", data);
        var (r, e) = RemoteToolTestKit.Run(P, T, path);
        Assert.Equal(1, r.Records);
        Assert.Equal("192.0.2.1", e[0].Fields["IP"]);
        Assert.Equal("600", e[0].Fields["Duration Seconds"]);
        Assert.Equal(T, InvestigationPipeline.ImportType(path));
        var (r2, _) = RunText(text, enc: Encoding.Latin1);
        Assert.Equal(1, r2.Records);
    }

    [Fact]
    public void A_time_in_the_future_or_an_impossible_date_is_not_placed_on_the_timeline()
    {
        var (_, future) = RunText(Open("10:00:00.000000", "901", "192.0.2.1:1000", date: "2099-01-01"));
        Assert.False(future.Single().Time.IsKnown);
        Assert.Equal("ora este în viitor", future.Single().Fields["Time Problem"]);
        Assert.Equal("2099-01-01 10:00:00", future.Single().Fields["Local Time"]);
        var (r, bad) = RunText(Open("10:00:00.000000", "901", "192.0.2.1:1000", date: "2026-13-45"));
        Assert.Empty(bad);
        Assert.True(r.MalformedRecords > 0);
        var (_, old) = RunText(Open("10:00:00.000000", "901", "192.0.2.1:1000", date: "1999-01-01"));
        Assert.False(old.Single().Time.IsKnown);
        Assert.Contains("imposibilă", old.Single().Fields["Time Problem"]);
    }

    [Fact]
    public void Two_thousand_sessions_are_read_in_reasonable_time()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 2000; i++)
        {
            int s = i % 60, m = (i / 60) % 60, h = 1 + i / 3600;
            sb.Append(Open($"{h:00}:{m:00}:{s:00}.000000", (1000 + i).ToString(), $"192.0.2.{1 + i % 250}:{2000 + i}"));
            sb.Append(Wake($"{h:00}:{m:00}:{s:00}.100000", "true"));
            sb.Append(Close($"{h:00}:{m:00}:{s:00}.500000", (1000 + i).ToString()));
            sb.Append(Loop($"{h:00}:{m:00}:{s:00}.600000", (1000 + i).ToString()));
        }
        var sw = Stopwatch.StartNew();
        var (r, e) = RunText(sb.ToString());
        sw.Stop();
        Assert.Equal(2000, r.Records);
        Assert.All(e, x => Assert.Equal("da", x.Fields["Display"]));
        Assert.Empty(r.Gaps);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"2,000 sessions took {sw.Elapsed}");
    }

    [Fact]
    public void Two_thousand_sessions_that_never_close_are_capped_at_the_open_limit()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 2000; i++) sb.Append(Open($"08:{(i / 60) % 60:00}:{i % 60:00}.000000", (5000 + i).ToString(), "192.0.2.1:1000"));
        sb.Append(Wake("09:59:00.000000", "true"));                              // 64 partner sessions open: ambiguous, never attached
        var sw = Stopwatch.StartNew();
        var (r, e) = RunText(sb.ToString());
        sw.Stop();
        Assert.Equal(2000, r.Records);
        Assert.Equal(2000 - RustDeskLogParser.MaxOpenSessions, e.Count(x => x.Fields["End Reason"].Contains("plafonul")));
        Assert.All(e, x => Assert.Equal("", x.Fields["Display"]));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
    }

    [Fact]
    public void The_file_is_typed_by_name_and_content_and_a_look_alike_is_not()
    {
        var sample = File.ReadAllBytes(Sample);
        Assert.Equal(T, InvestigationPipeline.ImportType(Sample));
        Assert.Equal(T, EvidenceFingerprint.Detect(Sample));
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("RustDesk_rCurrent.log", sample)));                              // another capitalisation
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("rustdesk_r2024-12-10_08-32-15.log", sample)));
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("rustdesk_rCURRENT_1.log", sample)));                            // stored copy of a second file of the same name
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("rustdesk_r2024-12-10_08-32-15_2.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_rCURRENT.txt", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("myrustdesk_rCURRENT.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_r2024-12-10.log", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_r2024-12-10_08-32-15.log.bak", sample)));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_rCURRENT_OLD.log", sample)));
        // the right name with other content stays "file"
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_rCURRENT.log", "just some notes\nno row prefix here\n")));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_rCURRENT.log", "2026/11/09 21:00:00.000  4100       1200 S0   TeamViewer-like row\n")));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("rustdesk_rCURRENT.log", "[2026-11-09 08:00:00 +01:00] INFO [a.rs:1] not the microsecond format\n")));
        Assert.Null(RemoteToolLogFormats.ByName("rustdesk_rCURRENT.log.bak"));
        Assert.Equal(RemoteToolLogFormats.RustDeskLog, RemoteToolLogFormats.ByName("RUSTDESK_RCURRENT.LOG"));
        Assert.True(RemoteToolLogFormats.IsKnown(T));
    }

    [Fact]
    public void Descriptor_declares_the_limits_and_the_transfer_and_authentication_gap()
    {
        var d = P.Descriptor;
        Assert.Equal(T, d.SourceTypes.Single());
        Assert.Contains(d.Limitations, x => x.Contains("Transferurile de fișiere", StringComparison.Ordinal) && x.Contains("autentificările", StringComparison.Ordinal));
        Assert.Equal(RustDeskLogParser.MaxFileBytes, d.MaxInputBytes);
        Assert.Equal("rustdesk", RustDeskLogParser.ToolId);
    }
}
