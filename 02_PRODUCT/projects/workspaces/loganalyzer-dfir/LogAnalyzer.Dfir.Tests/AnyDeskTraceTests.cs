using System.Text;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// AnyDesk ad.trace / ad_svc.trace: only the session lines become events (the TEST sample has irrelevant lines that must be ignored), the time is the station's
/// local time, and the threat model of a large text log written on the analysed station (encodings, giant lines, invalid IPs and ids, hidden characters, dates,
/// forged lines, the 50 000-line and 64 MiB caps, a locked file).
/// </summary>
public sealed class AnyDeskTraceTests : IDisposable
{
    private readonly RemoteToolTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static readonly AnyDeskTraceParser P = new();
    private const string T = AnyDeskTraceParser.SourceTypeName;
    private static string Sample => RemoteToolTestKit.Corpus(@"anydesk_TEST\ad_svc.trace");

    private static string Line(string msg, string level = "   info", string time = "2026-11-09 21:21:55.123", string proc = "gsvc") =>
        $"{level} {time}       {proc}   4120   6000   21  app.service - {msg}\r\n";

    private (ParseResult R, List<TimelineEvent> E) RunText(string text, string tz = "", Encoding? enc = null, string name = "ad.trace") =>
        RemoteToolTestKit.Run(P, T, _k.Write(name, text, enc), tz);

    [Fact]
    public void The_TEST_sample_keeps_only_the_session_lines()
    {
        var (r, e) = RemoteToolTestKit.Run(P, T, Sample, RemoteToolTestKit.Zone);
        // relay login, session request, accepting, remote OS, permanent token, file transfer, closed, stopped = 8; startup, the unrelated error and the TEST header are ignored
        Assert.Equal(8, r.Records);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(["LoggedInFromRelay", "IncomingSessionRequest", "AcceptedFrom", "RemoteOs", "AuthenticatedPermanentToken", "FileTransfer", "SessionEnded", "SessionEnded"], e.Select(x => x.EventId));
        var relay = e[0];
        Assert.Equal("203.0.113.7", relay.Fields["IP"]);
        Assert.Equal("50123", relay.Fields["IP Port"]);
        Assert.Equal("valid", relay.Fields["IP Status"]);
        Assert.Equal("info", relay.Fields["Level"]);
        Assert.Equal("gsvc", relay.Fields["Process"]);
        var accepted = e[2];
        Assert.Equal("999000111", accepted.Fields["Remote Id"]);
        Assert.Equal("Incoming", accepted.Fields["Direction"]);
        Assert.Equal("da", accepted.Fields["Accepted"]);
        Assert.Equal("necunoscut", e[1].Fields["Accepted"]);                 // a request is not an acceptance
        Assert.Equal("Windows 10 (TEST)", e[3].Fields["Remote OS"]);
        Assert.Equal("warning", e[5].Fields["Level"]);
        Assert.Equal("back", e[5].Fields["Process"]);
        // 21:21:55.123 local = 20:21:55.123 UTC in November
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 21, 55, 123, TimeSpan.Zero), accepted.Time.Utc);
        Assert.All(e, x => { Assert.Equal(Classification.Direct, x.Classification); Assert.Equal(Confidence.Medium, x.Confidence); RemoteToolTestKit.AssertNothingHidden(x); });
        Assert.Equal("anydesk", accepted.Fields["Tool Id"]);
        Assert.Contains("ad_svc.trace:line=5", accepted.Locator);
    }

    [Fact]
    public void Without_the_zone_the_local_time_stays_local_and_no_UTC_is_invented()
    {
        var (_, e) = RemoteToolTestKit.Run(P, T, Sample);
        Assert.All(e, x => Assert.False(x.Time.IsKnown));
        Assert.Equal("2026-11-09 21:21:55", e[2].Fields["Local Time"]);
        Assert.Contains("neconvertită", e[2].TimeZoneBasis);
        Assert.Contains("locală", e[2].TimeSemantics);
    }

    [Fact]
    public void Encodings_UTF8_UTF16_and_ANSI()
    {
        var text = Line("Accepting from 999000111 (x)") + Line("Session closed.");
        foreach (var enc in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(false, false) })
        {
            var data = enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray();
            var (r, e) = RemoteToolTestKit.Run(P, T, _k.Write("ad.trace", data));
            Assert.Equal(2, r.Records);
            Assert.Equal("AcceptedFrom", e[0].EventId);
        }
        // ANSI with an accented letter in the alias (a lone 0xE9 is not UTF-8)
        var ansi = Encoding.Latin1.GetBytes(Line("Accepting from 999000111 (André)"));
        var (ra, ea) = RemoteToolTestKit.Run(P, T, _k.Write("ad.trace", ansi));
        Assert.Equal(1, ra.Records);
        Assert.Equal("999000111", ea[0].Fields["Remote Id"]);
    }

    [Fact]
    public void Invalid_IPs_and_ids_are_marked_and_never_copied_into_the_summary()
    {
        var bad = new[] { "999.1.1.1", "1.2", "fe80::1%12", "<script>alert(1)</script>", "1.2.3.4.5", "0x7f.1", "::g", "300.300.300.300", "010.0.0.1" };   // a leading zero would be read as octal (8.0.0.1)
        var text = string.Concat(bad.Select(b => Line($"Logged in from {b}:80 on relay abc")))
                 + Line("Logged in from 2001:db8::7 on relay abc") + Line("Logged in from [2001:db8::8]:443 on relay abc")
                 + Line("Accepting from <b>evil</b> (x)") + Line("Accepting from 12ab (x)");
        var (r, e) = RunText(text);
        Assert.Equal(bad.Length + 4, r.Records);
        foreach (var x in e.Take(bad.Length)) { Assert.Equal("invalid", x.Fields["IP Status"]); Assert.False(x.Fields.ContainsKey("IP")); Assert.Contains("invalidă", x.Summary); }
        Assert.Equal("2001:db8::7", e[bad.Length].Fields["IP"]);
        Assert.Equal("2001:db8::8", e[bad.Length + 1].Fields["IP"]);
        Assert.Equal("443", e[bad.Length + 1].Fields["IP Port"]);
        Assert.Equal("nu", e[bad.Length + 2].Fields["Remote Id Valid"]);
        Assert.StartsWith("format necunoscut", e[bad.Length + 3].Fields["Remote Id"]);
        Assert.All(e, x => { Assert.DoesNotContain("script", x.Summary); Assert.DoesNotContain("<b>", x.Summary); RemoteToolTestKit.AssertNothingHidden(x); });
    }

    [Fact]
    public void Hidden_bidi_and_Tag_characters_in_the_message_are_removed_and_named()
    {
        var (r, e) = RunText(Line("Accepting from 999000111 (Al\u202Ei\u0007ce\U000E0041\U000E0042 \u2066x)") + Line("Remote OS: Win\u202Edows\u0000 \U000E0041"));
        Assert.Equal(2, r.Records);
        Assert.All(e, x => { RemoteToolTestKit.AssertNothingHidden(x); Assert.Contains("U+202E", x.Fields["Message"]); });
        Assert.Contains("bloc Tag", e[0].Fields["Message"]);
        Assert.Contains("U+202E", e[1].Fields["Remote OS"]);
    }

    [Fact]
    public void A_long_line_is_cut_at_2048_characters_keeping_the_session_text_at_its_start()
    {
        var (r, e) = RunText(Line("Accepting from 999000111 " + new string('x', 6000)) + Line("Session closed."));
        Assert.Equal(2, r.Records);
        Assert.Equal("999000111", e[0].Fields["Remote Id"]);
        Assert.StartsWith("da", e[0].Fields["Line Truncated"]);
        Assert.True(e[0].Fields["Message"].Length <= 310);
        Assert.False(e[1].Fields.ContainsKey("Line Truncated"));
    }

    [Fact]
    public void A_gigantic_line_does_not_blow_up_memory()
    {
        var p = _k.Write("ad.trace", Line("Session closed."));
        using (var fs = new FileStream(p, FileMode.Append)) { var chunk = new byte[1024 * 1024]; Array.Fill(chunk, (byte)'A'); for (int i = 0; i < 12; i++) fs.Write(chunk); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var (r, _) = RemoteToolTestKit.Run(P, T, p);
        Assert.Equal(1, r.Records);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "a giant line must be cut while it is read, not held");
    }

    [Fact]
    public void Impossible_and_future_dates_and_truncated_lines_are_handled()
    {
        var (r, e) = RunText(Line("Session closed.", time: "2026-02-30 21:21:55.123") + Line("Session closed.", time: "1999-01-01 00:00:00.000") +
                             Line("Session closed.", time: "2999-01-01 10:00:00.000") + Line("Session closed.", time: "2026-11-09 25:61:00.000") +
                             "   info 2026-11-09 21:21:55.123       gsvc   4120   60\r\n" +          // truncated header: not a log line
                             "   info 2026-11-09 21:2", RemoteToolTestKit.Zone);
        Assert.Equal(4, r.Records);
        Assert.All(e, x => Assert.False(x.Time.IsKnown));
        Assert.Contains("imposibil", e[0].Fields["Time Problem"]);
        Assert.Contains("viitor", e[2].Fields["Time Problem"]);
        Assert.True(r.MalformedRecords >= 2);
    }

    [Fact]
    public void Forged_lines_look_like_real_ones_so_every_event_is_Medium()
    {
        var (_, e) = RunText(Line("Accepting from 999000111 (real)") + Line("Accepting from 999000666 (forged by an attacker)"));
        Assert.All(e, x => { Assert.Equal(Confidence.Medium, x.Confidence); Assert.Contains("falsificat", x.Fields["Limitation"]); });
        Assert.Contains(P.Descriptor.Limitations, l => l.Contains("falsificat"));
    }

    [Fact]
    public void More_than_50000_relevant_lines_give_a_gap_and_stop()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < AnyDeskTraceParser.MaxRelevantLines + 3; i++) sb.Append("   info 2026-11-09 21:21:55.123       gsvc   4120   6000   21  app.service - Session closed.\n");
        var (r, e) = RunText(sb.ToString());
        Assert.Equal(AnyDeskTraceParser.MaxRelevantLines, r.Records);
        Assert.Equal(AnyDeskTraceParser.MaxRelevantLines, e.Count);
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Partial && g.Reason.Contains("rânduri relevante"));
        Assert.Equal(EvidenceStatus.Partial, r.Status);
    }

    [Fact]
    public void A_file_over_64_MiB_is_refused_with_a_gap_and_a_locked_file_fails_cleanly()
    {
        var big = _k.Write("ad.trace", Line("Session closed."));
        using (var fs = new FileStream(big, FileMode.Append)) fs.SetLength(AnyDeskTraceParser.MaxFileBytes + 1);
        var (rb, eb) = RemoteToolTestKit.Run(P, T, big);
        Assert.Equal(EvidenceStatus.Failed, rb.Status);
        Assert.Empty(eb);
        Assert.NotEmpty(rb.Gaps);

        var p = _k.Write("ad.trace", Line("Session closed."));
        using var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (rl, el) = RemoteToolTestKit.Run(P, T, p);
        Assert.Equal(EvidenceStatus.Failed, rl.Status);
        Assert.Empty(el);
    }

    [Fact]
    public void Only_the_exact_names_with_matching_content_are_typed()
    {
        Assert.Equal(T, InvestigationPipeline.ImportType(Sample));
        Assert.Equal(T, EvidenceFingerprint.Detect(Sample));
        var user = _k.Write("ad.trace", File.ReadAllBytes(Sample));
        Assert.Equal(T, InvestigationPipeline.ImportType(user));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("ad_other.trace", File.ReadAllBytes(Sample))));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("ad.trace", "just some text\r\nwithout the log format\r\n")));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("ad.trace", new byte[200])));
    }

    [Fact]
    public void The_descriptor_is_complete()
    {
        var d = P.Descriptor;
        Assert.Equal(ParserMaturity.Tested, d.Status);
        Assert.Equal(64L * 1024 * 1024, d.MaxInputBytes);
        Assert.All(new[] { "falsificat", "locală", "50000", "64 MiB" }, w => Assert.Contains(d.Limitations, l => l.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}
