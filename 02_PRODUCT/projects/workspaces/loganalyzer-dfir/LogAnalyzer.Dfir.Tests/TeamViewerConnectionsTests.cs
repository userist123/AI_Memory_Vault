using System.Text;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// TeamViewer Connections_incoming.txt / Connections.txt: the synthetic TEST sample, direction from the file name, start / end / duration, local time and the
/// threat model of a tab-separated file written on the analysed station (encodings, hostile ids and names, inverted and impossible times, future dates,
/// missing separators, giant lines, forged rows, caps, a locked file).
/// </summary>
public sealed class TeamViewerConnectionsTests : IDisposable
{
    private readonly RemoteToolTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static readonly TeamViewerConnectionsParser P = new();
    private const string T = TeamViewerConnectionsParser.SourceTypeName;
    private static string Sample => RemoteToolTestKit.Corpus(@"teamviewer_TEST\Connections_incoming.txt");

    private static string Row(string id = "999000111", string name = "TEST-Partener", string start = "09-11-2026 21:21:55", string end = "09-11-2026 21:47:10",
                              string account = "test-user", string type = "RemoteControl", string guid = "{00000000-0000-4000-8000-0000000000a1}") =>
        string.Join('\t', id, name, start, end, account, type, guid) + "\r\n";

    private (ParseResult R, List<TimelineEvent> E) RunText(string text, string tz = "", Encoding? enc = null, string name = "Connections_incoming.txt") =>
        RemoteToolTestKit.Run(P, T, _k.Write(name, text, enc), tz);

    [Fact]
    public void The_TEST_sample_gives_a_session_event_with_start_end_and_duration()
    {
        var (r, e) = RemoteToolTestKit.Run(P, T, Sample, RemoteToolTestKit.Zone);
        Assert.Equal(2, r.Records);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        var a = e[0];
        Assert.Equal(TeamViewerConnectionsParser.EventSource, a.Source);
        Assert.Equal("teamviewer", a.Fields["Tool Id"]);
        Assert.Equal("Incoming", a.Fields["Direction"]);
        Assert.Equal("da", a.Fields["Accepted"]);
        Assert.Equal("999000111", a.Fields["Remote Id"]);
        Assert.Equal("TEST-Partener", a.Fields["Remote Name"]);
        Assert.Equal("test-user", a.Fields["Local Account"]);
        Assert.Equal("RemoteControl", a.Fields["Session Type"]);
        Assert.Equal("00000000-0000-4000-8000-0000000000a1", a.Fields["Session GUID"]);
        Assert.Equal("2026-11-09 21:21:55", a.Fields["Start Local"]);
        Assert.Equal("2026-11-09 21:47:10", a.Fields["End Local"]);
        Assert.Equal("1515", a.Fields["Duration Seconds"]);                       // 25 min 15 s
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 21, 55, TimeSpan.Zero), a.Time.Utc);
        Assert.Equal("2026-11-09T20:47:10.0000000+00:00", a.Fields["End Time UTC"]);
        Assert.Equal("FileTransfer", e[1].Fields["Session Type"]);
        Assert.Equal("330", e[1].Fields["Duration Seconds"]);
        Assert.All(e, x => { Assert.Equal(Classification.Direct, x.Classification); Assert.Equal(Confidence.Medium, x.Confidence); RemoteToolTestKit.AssertNothingHidden(x); });
        Assert.Contains("1515 s", a.Summary);
        Assert.DoesNotContain("TEST-Partener", a.Summary);                         // a name chosen by the partner is not in the summary
    }

    [Fact]
    public void The_direction_comes_from_the_file_name_and_the_time_stays_local_without_the_zone()
    {
        var (_, e) = RunText(Row(), name: "Connections.txt");
        Assert.Equal("Outgoing", e[0].Fields["Direction"]);
        Assert.Equal("necunoscut", e[0].Fields["Accepted"]);
        Assert.False(e[0].Time.IsKnown);
        Assert.Equal("2026-11-09 21:21:55", e[0].Fields["Start Local"]);
        Assert.Contains("neconvertită", e[0].TimeZoneBasis);
        Assert.Contains("locală", e[0].TimeSemantics);
    }

    [Fact]
    public void Encodings_UTF8_UTF16_and_ANSI()
    {
        foreach (var enc in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(false, false) })
        {
            var data = enc.GetPreamble().Concat(enc.GetBytes(Row() + Row("999000222"))).ToArray();
            var (r, e) = RemoteToolTestKit.Run(P, T, _k.Write("Connections_incoming.txt", data));
            Assert.Equal(2, r.Records);
            Assert.Equal("999000222", e[1].Fields["Remote Id"]);
        }
        var ansi = Encoding.Latin1.GetBytes(Row(name: "André (TEST)"));
        var (ra, ea) = RemoteToolTestKit.Run(P, T, _k.Write("Connections_incoming.txt", ansi));
        Assert.Equal(1, ra.Records);
        Assert.Equal("André (TEST)", ea[0].Fields["Remote Name"]);
    }

    [Fact]
    public void Hostile_ids_names_accounts_and_types_are_cleaned_and_flagged()
    {
        var (r, e) = RunText(Row(id: "9990\u202E0111\U000E0041", name: "Al\u202Eice\u0007\U000E0042 \u2066x", account: "us\u0000er\u202E", type: "Remote\u202EControl") + Row(id: "abc"));
        Assert.Equal(2, r.Records);
        Assert.All(e, x => { RemoteToolTestKit.AssertNothingHidden(x); Assert.Equal("nu", x.Fields["Remote Id Valid"]); Assert.DoesNotContain("Alice", x.Summary); });
        Assert.Contains("U+202E", e[0].Fields["Remote Name"]);
        Assert.StartsWith("format necunoscut", e[0].Fields["Remote Id"]);
        Assert.StartsWith("necunoscut", e[0].Fields["Session Type"]);              // not RemoteControl: the real type name has a hidden character
        Assert.DoesNotContain("9990", e[0].Summary);
    }

    [Fact]
    public void Inverted_impossible_and_future_times_and_a_missing_end()
    {
        var (r, e) = RunText(Row(start: "09-11-2026 21:47:10", end: "09-11-2026 21:21:55") +                // end before start
                             Row(start: "30-02-2026 10:00:00") +                                              // impossible start: not a row
                             Row(start: "01-01-1999 00:00:00") +                                              // before 2010
                             Row(start: "01-01-2999 00:00:00", end: "01-01-2999 01:00:00") +                  // future
                             Row(end: "") +                                                                    // no end
                             Row(end: "99-99-2026 99:99:99"), RemoteToolTestKit.Zone);
        Assert.Equal(5, r.Records);                                                                             // the impossible start is malformed, not an event
        Assert.Equal(1, r.MalformedRecords);
        Assert.Equal("", e[0].Fields["Duration Seconds"]);
        Assert.Contains("invers", e[0].Fields["Duration Problem"]);
        Assert.Contains("imposibil", e[1].Fields["Time Problem"]);
        Assert.False(e[1].Time.IsKnown);
        Assert.Contains("viitor", e[2].Fields["Time Problem"]);
        Assert.False(e[2].Time.IsKnown);
        Assert.Equal("", e[3].Fields["Duration Seconds"]);
        Assert.Contains("fără sfârșit", e[3].Summary);
        Assert.Contains("imposibil de interpretat", e[4].Fields["Duration Problem"]);
    }

    [Fact]
    public void Missing_tabs_truncated_rows_giant_lines_and_garbage_are_counted()
    {
        var giant = Row(name: new string('N', 5000));
        var (r, e) = RunText("999000111 TEST-Partener 09-11-2026 21:21:55 09-11-2026 21:47:10 test-user RemoteControl\r\n" +   // spaces instead of tabs
                             "999000111\tTEST-Partener\t09-11-2026 21:\r\n" +                                                 // cut in the time
                             "999000111\tTEST-Partener\t09-11-2026 21:21:55\r\n" +                                           // cut before the end: 3 columns
                             giant + "\u0000\u0001 garbage\r\n" +
                             "999000111\tTEST-Partener\t09-11-2026 21:21:55\t09-11-2026 21:47:10\r\n");                     // 4 columns: no account, no type
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Equal(1, r.Records);
        Assert.Equal("", e[0].Fields["Session Type"]);
        Assert.Equal("", e[0].Fields["Local Account"]);
        Assert.True(r.MalformedRecords >= 4);
    }

    [Fact]
    public void A_gigantic_line_does_not_blow_up_memory()
    {
        var p = _k.Write("Connections_incoming.txt", Row());
        using (var fs = new FileStream(p, FileMode.Append)) { var chunk = new byte[1024 * 1024]; Array.Fill(chunk, (byte)'A'); for (int i = 0; i < 8; i++) fs.Write(chunk); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var (r, _) = RemoteToolTestKit.Run(P, T, p);
        Assert.Equal(1, r.Records);
        Assert.Equal(1, r.MalformedRecords);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "a giant line must be cut while it is read, not held");
    }

    [Fact]
    public void Forged_rows_are_ordinary_rows_so_every_event_is_Medium()
    {
        var (_, e) = RunText(Row() + Row("999000666", "forged"));
        Assert.All(e, x => { Assert.Equal(Confidence.Medium, x.Confidence); Assert.Contains("falsificat", x.Fields["Limitation"]); });
        Assert.Contains(P.Descriptor.Limitations, l => l.Contains("falsificat"));
    }

    [Fact]
    public void Row_cap_and_size_cap_and_a_locked_file()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < TeamViewerConnectionsParser.MaxRows + 5; i++) sb.Append(Row().TrimEnd('\r', '\n')).Append('\n');
        var (r, e) = RunText(sb.ToString());
        Assert.Equal(TeamViewerConnectionsParser.MaxRows, r.Records);
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Partial && g.Reason.Contains("rânduri"));

        var big = _k.Write("Connections_incoming.txt", Row());
        using (var fs = new FileStream(big, FileMode.Append)) fs.SetLength(TeamViewerConnectionsParser.MaxFileBytes + 1);
        var (rb, eb) = RemoteToolTestKit.Run(P, T, big);
        Assert.Equal(EvidenceStatus.Failed, rb.Status);
        Assert.Empty(eb);

        var p = _k.Write("Connections_incoming.txt", Row());
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
        Assert.Equal(T, InvestigationPipeline.ImportType(_k.Write("Connections.txt", File.ReadAllBytes(Sample))));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("Connections_old.txt", File.ReadAllBytes(Sample))));
        Assert.Equal("file", InvestigationPipeline.ImportType(_k.Write("Connections.txt", "no tabs and no dates here\r\n")));
    }

    [Fact]
    public void The_descriptor_is_complete_and_says_the_detailed_logs_are_not_read()
    {
        var d = P.Descriptor;
        Assert.Equal(ParserMaturity.Tested, d.Status);
        Assert.Contains(d.Limitations, l => l.Contains("TeamViewer15_Logfile.log"));
        Assert.All(new[] { "falsificat", "locală", "UTC" }, w => Assert.Contains(d.Limitations, l => l.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}
