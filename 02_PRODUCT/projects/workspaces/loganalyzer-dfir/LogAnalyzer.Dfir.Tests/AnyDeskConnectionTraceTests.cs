using System.Text;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.RemoteTools;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// AnyDesk connection_trace.txt: the synthetic TEST sample (Corpus\RemoteToolLogs\anydesk_TEST), the time rules (local time, converted only with the station's zone),
/// the threat model of a file written by the analysed station (encodings, giant lines, hidden characters, impossible and future dates, truncated lines, missing
/// separators, forged rows, size caps, a locked file) and the file routing.
/// </summary>
public sealed class AnyDeskConnectionTraceTests : IDisposable
{
    private readonly RemoteToolTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static readonly AnyDeskConnectionTraceParser P = new();
    private const string T = AnyDeskConnectionTraceParser.SourceTypeName;
    private static string Sample => RemoteToolTestKit.Corpus(@"anydesk_TEST\connection_trace.txt");

    private (LogAnalyzer.Dfir.Model.ParseResult R, List<TimelineEvent> E) RunText(string text, string tz = "", Encoding? enc = null)
    {
        var p = _k.Write("connection_trace.txt", text, enc);
        return RemoteToolTestKit.Run(P, T, p, tz);
    }

    [Fact]
    public void The_TEST_sample_gives_one_event_per_session_with_direction_method_and_ids()
    {
        var (r, e) = RemoteToolTestKit.Run(P, T, Sample, RemoteToolTestKit.Zone);
        Assert.Equal(4, r.Records);
        Assert.Equal(0, r.MalformedRecords);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        var first = e[0];
        Assert.Equal(AnyDeskConnectionTraceParser.EventSource, first.Source);
        Assert.Equal("Incoming", first.Fields["Direction"]);
        Assert.Equal("User", first.Fields["Auth Method"]);
        Assert.Equal("999000111", first.Fields["Remote Id"]);
        Assert.Equal("999000222", first.Fields["Local Id"]);
        Assert.Equal("da", first.Fields["Accepted"]);
        Assert.Equal("anydesk", first.Fields["Tool Id"]);
        Assert.Equal("Passwd", e[1].Fields["Auth Method"]);
        Assert.Equal("Outgoing", e[2].Fields["Direction"]);
        Assert.Equal("Token", e[2].Fields["Auth Method"]);
        Assert.Equal("REJECTED", e[3].Fields["Auth Method"]);
        Assert.Equal("nu", e[3].Fields["Accepted"]);
        Assert.Contains("respinsă", e[3].Summary);
        // a file anyone with access to the station can write: Direct, never High
        Assert.All(e, x => { Assert.Equal(Classification.Direct, x.Classification); Assert.Equal(Confidence.Medium, x.Confidence); RemoteToolTestKit.AssertNothingHidden(x); });
        Assert.Contains("connection_trace.txt:line=1", first.Locator);
        Assert.Contains("falsificat", P.Descriptor.Limitations.First(l => l.Contains("modificat")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_local_time_is_converted_only_with_the_stations_zone()
    {
        var (_, withZone) = RemoteToolTestKit.Run(P, T, Sample, RemoteToolTestKit.Zone);
        // 2026-11-09 21:21 in Central Europe (UTC+1 in November) is 20:21 UTC
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 21, 0, TimeSpan.Zero), withZone[0].Time.Utc);
        Assert.Contains("→ UTC", withZone[0].TimeZoneBasis);
        Assert.Contains("locală", withZone[0].TimeSemantics);
        Assert.Equal(RemoteToolTestKit.Zone, withZone[0].Fields["Time Zone Used"]);

        var (_, none) = RemoteToolTestKit.Run(P, T, Sample);
        Assert.All(none, x => Assert.False(x.Time.IsKnown));                     // no UTC is invented
        Assert.Equal("2026-11-09 21:21:00", none[0].Fields["Local Time"]);
        Assert.Contains("neconvertită", none[0].TimeZoneBasis);
        Assert.NotEmpty(none[0].TimeUncertainty);

        var (_, unknownZone) = RemoteToolTestKit.Run(P, T, Sample, "Nu Exista Acest Fus");
        Assert.All(unknownZone, x => Assert.False(x.Time.IsKnown));
        Assert.Contains("nu este cunoscut", unknownZone[0].TimeZoneBasis);
    }

    [Fact]
    public void UTF8_with_BOM_UTF16_with_BOM_UTF16_without_BOM_and_ANSI_all_read()
    {
        const string row = "Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\r\n";
        foreach (var enc in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
        {
            var data = enc.GetPreamble().Concat(enc.GetBytes(row)).ToArray();
            var (r, e) = RemoteToolTestKit.Run(P, T, _k.Write("connection_trace.txt", data));
            Assert.Equal(1, r.Records);
            Assert.Equal("999000111", e[0].Fields["Remote Id"]);
        }
        var noBom = _k.Write("connection_trace.txt", new UnicodeEncoding(false, false).GetBytes(row + row));
        Assert.Equal(2, RemoteToolTestKit.Run(P, T, noBom).R.Records);
        // ANSI (Windows-1252): "Pâsswd" has a lone 0xE2 byte, which is not valid UTF-8; the method is unknown but the row is kept
        var ansi = Encoding.Latin1.GetBytes("Incoming\t2026-11-09, 21:21\tPâsswd\t999000111\t999000222\r\n");
        var (ra, ea) = RemoteToolTestKit.Run(P, T, _k.Write("connection_trace.txt", ansi));
        Assert.Equal(1, ra.Records);
        Assert.Equal("necunoscut: Pâsswd", ea[0].Fields["Auth Method"]);
        Assert.Equal("necunoscut", ea[0].Fields["Accepted"]);
    }

    [Fact]
    public void Hostile_ids_and_methods_are_cleaned_and_flagged_not_trusted()
    {
        // RLO, a Tag-block character (U+E0041), BEL and NUL inside the id and the method; a letter id; an id that is too long
        var (r, e) = RunText("Incoming\t2026-11-09, 21:21\tUs\u202Eer\u0007\t9990\u202E01\U000E0041\t999000222\r\n" +
                             "Incoming\t2026-11-09, 21:22\tUser\tabc123\t999000222\r\n" +
                             "Incoming\t2026-11-09, 21:23\tUser\t99900011122233344455\t999000222\r\n");
        Assert.Equal(3, r.Records);
        Assert.All(e, x => { Assert.Equal("nu", x.Fields["Remote Id Valid"]); Assert.StartsWith("format necunoscut", x.Fields["Remote Id"]); RemoteToolTestKit.AssertNothingHidden(x); Assert.DoesNotContain("format necunoscut", x.Summary.Replace("de format necunoscut", "")); });
        Assert.Contains("U+202E", e[0].Fields["Auth Method"] + e[0].Fields["Remote Id"]);   // named, not silently dropped
        // the text of the file never reaches the summary
        Assert.DoesNotContain("abc123", e[1].Summary);
        Assert.DoesNotContain("9990", e[0].Summary);
    }

    [Fact]
    public void Impossible_dates_and_times_in_the_future_never_reach_the_timeline()
    {
        var (r, e) = RunText("Incoming\t2026-02-30, 21:21\tUser\t999000111\t999000222\r\n" +
                             "Incoming\t1999-01-01, 10:00\tUser\t999000111\t999000222\r\n" +
                             "Incoming\t2999-01-01, 10:00\tUser\t999000111\t999000222\r\n" +
                             "Incoming\t2026-11-09, 25:61\tUser\t999000111\t999000222\r\n" +
                             "Incoming\t2026-03-29, 02:30\tUser\t999000111\t999000222\r\n", RemoteToolTestKit.Zone);
        Assert.Equal(5, r.Records);
        Assert.All(e, x => Assert.False(x.Time.IsKnown));
        Assert.Contains("imposibil", e[0].Fields["Time Problem"]);
        Assert.Contains("imposibil", e[1].Fields["Time Problem"]);
        Assert.Contains("viitor", e[2].Fields["Time Problem"]);
        Assert.Equal("2999-01-01, 10:00", e[2].Time.Raw);
        Assert.Contains("nu există", e[4].Fields["Time Problem"]);        // the hour skipped by the change to summer time
        Assert.True(r.MalformedRecords >= 2);
    }

    [Fact]
    public void Truncated_lines_missing_separators_giant_lines_and_garbage_are_counted_not_fatal()
    {
        var giant = "Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222" + new string('x', 5000);
        var (r, e) = RunText("Incoming 2026-11-09, 21:21 Token 999000111 999000222\r\n" +   // single spaces only
                             "Incoming\t2026-11-09, 21:\r\n" +                                  // cut in the middle of the time
                             "Incoming\t2026-11-09, 21:21\tUser\r\n" +                          // cut after the method
                             giant + "\r\n" +                                                   // over the line cap
                             "\u0000\u0001garbage that is not a row\r\n" +
                             "Outgoing\t2026-11-09, 21:30\tUser\t999000111\t999000222\r\n");
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Equal(3, r.Records);                                // single-space row, the one cut after the method, the last row
        Assert.Equal("999000111", e[0].Fields["Remote Id"]);
        Assert.Equal("Token", e[0].Fields["Auth Method"]);
        Assert.Equal("", e[1].Fields["Remote Id"]);
        Assert.Equal("lipsește ID-ul de la distanță", e[1].Fields["Line Problem"]);
        Assert.Equal("Outgoing", e[2].Fields["Direction"]);
        Assert.True(r.MalformedRecords >= 4);
    }

    [Fact]
    public void A_gigantic_line_without_a_line_break_does_not_blow_up_memory_or_time()
    {
        var p = _k.Write("connection_trace.txt", "Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\r\n");
        using (var fs = new FileStream(p, FileMode.Append)) { var chunk = new byte[1024 * 1024]; Array.Fill(chunk, (byte)'A'); for (int i = 0; i < 8; i++) fs.Write(chunk); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var (r, e) = RemoteToolTestKit.Run(P, T, p);
        Assert.Equal(1, r.Records);
        Assert.Equal(1, r.MalformedRecords);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "a giant line must be cut while it is read, not held");
    }

    [Fact]
    public void Forged_rows_are_ordinary_rows_with_Medium_confidence_and_the_limitation_says_so()
    {
        // an attacker appends a fake session: it cannot be told from a real one, so no row is ever High
        var (r, e) = RunText("Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\r\nIncoming\t2026-11-09, 21:22\tUser\t999000666\t999000222\r\n");
        Assert.Equal(2, r.Records);
        Assert.All(e, x => Assert.Equal(Confidence.Medium, x.Confidence));
        Assert.All(e, x => Assert.Contains("falsificat", x.Fields["Limitation"]));
    }

    [Fact]
    public void Row_cap_writes_a_gap_and_the_size_cap_refuses_the_file()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < AnyDeskConnectionTraceParser.MaxRows + 5; i++) sb.Append("Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\n");
        var (r, e) = RunText(sb.ToString());
        Assert.Equal(AnyDeskConnectionTraceParser.MaxRows, r.Records);
        Assert.Equal(AnyDeskConnectionTraceParser.MaxRows, e.Count);
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Partial && g.Reason.Contains("rânduri"));
        Assert.Equal(EvidenceStatus.Partial, r.Status);

        var big = _k.Write("connection_trace.txt", "Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\r\n");
        using (var fs = new FileStream(big, FileMode.Append)) fs.SetLength(AnyDeskConnectionTraceParser.MaxFileBytes + 1);
        var (rb, eb) = RemoteToolTestKit.Run(P, T, big);
        Assert.Equal(EvidenceStatus.Failed, rb.Status);
        Assert.Empty(eb);
        Assert.NotEmpty(rb.Gaps);
    }

    [Fact]
    public void A_locked_file_is_a_failed_parse_not_an_unhandled_exception()
    {
        var p = _k.Write("connection_trace.txt", "Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\r\n");
        using var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (r, e) = RemoteToolTestKit.Run(P, T, p);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.NotEmpty(r.Error);
        Assert.Empty(e);
    }

    [Fact]
    public void Only_the_exact_name_with_matching_content_is_typed()
    {
        Assert.Equal(T, InvestigationPipeline.ImportType(Sample));
        Assert.Equal(T, EvidenceFingerprint.Detect(Sample));
        // another name, or the same name with other content, stays a plain file
        var other = _k.Write("connection_trace_copy.txt", File.ReadAllBytes(Sample));
        Assert.Equal("file", InvestigationPipeline.ImportType(other));
        var junk = _k.Write("connection_trace.txt", "this is not a session log at all\r\nnothing here\r\n");
        Assert.Equal("file", InvestigationPipeline.ImportType(junk));
        var empty = _k.Write("connection_trace.txt", "");
        Assert.Equal("file", InvestigationPipeline.ImportType(empty));
        // UTF-16 LE with a BOM is recognised by content too
        var u16 = _k.Write("connection_trace.txt", [.. new UnicodeEncoding(false, true).GetPreamble(), .. new UnicodeEncoding(false, false).GetBytes("Incoming\t2026-11-09, 21:21\tUser\t999000111\t999000222\r\n")]);
        Assert.Equal(T, InvestigationPipeline.ImportType(u16));
    }

    [Fact]
    public void The_descriptor_is_complete_and_names_the_forgery_and_time_limits()
    {
        var d = P.Descriptor;
        Assert.Equal(ParserMaturityTested(), d.Status);
        Assert.All(new[] { "falsificat", "locală", "UTC" }, w => Assert.Contains(d.Limitations, l => l.Contains(w, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(T, d.SourceTypes);
        Assert.Contains(T, d.Fingerprints);
    }

    private static LogAnalyzer.Dfir.Parsing.ParserMaturity ParserMaturityTested() => LogAnalyzer.Dfir.Parsing.ParserMaturity.Tested;
}
