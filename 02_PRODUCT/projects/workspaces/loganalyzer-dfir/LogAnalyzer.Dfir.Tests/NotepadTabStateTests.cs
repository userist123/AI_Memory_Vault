using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Notepad TabState: the parser on two real tab files from the lab (Notepad 11.2312.18.0: a new unsaved tab and the tab of a saved file with unsaved text added),
/// the threat model of an untrusted binary (endless or over-64-bit uLEB128, lengths past the file, size and chunk caps, odd / broken UTF-16, invalid FILETIME,
/// wrong CRC32, hostile text, a path that is never opened) and the file routing.
/// </summary>
public sealed class NotepadTabStateTests : IDisposable
{
    private const string NewId = "0e735e0a-8148-44fb-8cea-7e15f4caa644", SavedId = "13aa65cf-6a0d-48a7-8f5c-e6ac4be9bafc";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_np_" + Guid.NewGuid().ToString("N"));
    public NotepadTabStateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(_dir, true); }

    private static string Fixture(string id) => Path.Combine(AppContext.BaseDirectory, "Corpus", "NotepadTabState", id + ".bin");
    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, data); return p; }
    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-N", CaseId = "C", Source = "test", SourceType = NotepadTabStateParser.SourceTypeName, StoredPath = path };

    private (ParseResult R, ListSink S) Run(string name, byte[] data)
    {
        var p = Write(name, data);
        var sink = new ListSink();
        return (new NotepadTabStateParser().Parse(Item(p), p, sink, default), sink);
    }

    // ---- a small writer for synthetic files ----

    private static byte[] Leb(ulong v)
    {
        var b = new List<byte>();
        do { var x = (byte)(v & 0x7F); v >>= 7; b.Add(v != 0 ? (byte)(x | 0x80) : x); } while (v != 0);
        return [.. b];
    }
    private static byte[] Cat(params byte[][] p) => [.. p.SelectMany(x => x)];
    private static byte[] U16(string s) => Encoding.Unicode.GetBytes(s);
    private static readonly byte[] Np = "NP"u8.ToArray();

    private static byte[] Tab(bool saved, string path, string content, bool unsaved = false, ulong fileTime = 134387299205234055UL, bool badCrc = false, int chunks = 0, ulong seq = 0)
    {
        var body = new List<byte> { (byte)(saved ? 1 : 0) };
        if (saved)
            body.AddRange(Cat(Leb((ulong)path.Length), U16(path), Leb(36), [4, 1], Leb(fileTime), Enumerable.Repeat((byte)0xAB, 32).ToArray(), [0, 1]));
        else body.Add(1);
        body.AddRange(Cat(Leb(0), Leb(0), [1, 0, 0, 0], Leb((ulong)content.Length), U16(content), [(byte)(unsaved ? 1 : 0)]));
        var crc = NotepadTabStateParser.Crc32(body.ToArray());
        if (badCrc) crc ^= 0x1;
        body.AddRange([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
        for (int i = 0; i < chunks; i++) body.AddRange(Cat(Leb(0), Leb(0), Leb(1), U16("x"), [1, 2, 3, 4]));
        return Cat(Np, Leb(seq), [.. body]);
    }

    // ---- the real files ----

    [Fact]
    public void The_real_unsaved_tab_gives_its_text_and_no_time()
    {
        var (r, s) = Run(NewId + ".bin", File.ReadAllBytes(Fixture(NewId)));
        Assert.Equal(1, r.Records);
        Assert.Empty(r.Gaps);
        var e = Assert.Single(s.Events);
        Assert.Equal(NotepadTabStateParser.EventSource, e.Source);
        Assert.Equal("", e.Fields["Saved File Path"]);
        Assert.Equal("", e.Fields["Saved File SHA256"]);
        Assert.Equal("", e.Fields["Saved File Time"]);
        Assert.Equal("da", e.Fields["Unsaved Changes"]);
        Assert.Equal("38", e.Fields["Content Length"]);
        Assert.Equal("TEST-LA fila noua nesalvata 2026-11-09", e.Fields["Content"]);
        Assert.Equal("0", e.Fields["Unsaved Chunks"]);
        Assert.Equal("da", e.Fields["CRC Valid"]);
        Assert.Equal(NewId, e.Fields["Tab ID"]);
        Assert.False(e.Time.IsKnown);                                   // no time is invented
        // the text may hold the analysed user's secrets: it stays in Content; the summary (timeline.csv, reports) gets only its size, not even a hash
        // (an unsalted hash plus the exact length lets a short password be found by a dictionary, review r2)
        Assert.DoesNotContain("TEST-LA fila noua nesalvata", e.Summary);
        Assert.Contains("38 de caractere", e.Summary);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes("TEST-LA fila noua nesalvata 2026-11-09"))), e.Fields["Content SHA256"]);
        Assert.DoesNotContain(e.Fields["Content SHA256"][..8], e.Summary);
        Assert.Contains("sensibil", e.Fields["Content Sensitivity"]);
        Assert.Equal("TabState/" + NewId + ".bin", e.Locator);
    }

    [Fact]
    public void A_secret_in_a_tab_never_reaches_the_summary_and_hidden_characters_are_removed_from_Content()
    {
        var raw = File.ReadAllBytes(Fixture(NewId));
        // same length text: "parola=S3cr3t\u202E" padded to the 38 characters of the fixture (the CRC then no longer matches, which is fine here)
        var secret = ("parola=S3cr3t\u202Ex\u0007" + new string('y', 38)).Substring(0, 38);
        var bytes = System.Text.Encoding.Unicode.GetBytes(secret);
        int at = 12;   // "NP" 00 00 01 26 26 01 00 00 00 26 | text
        Array.Copy(bytes, 0, raw, at, bytes.Length);
        var (_, s) = Run(NewId + ".bin", raw);
        var e = Assert.Single(s.Events);
        Assert.DoesNotContain("S3cr3t", e.Summary);
        Assert.DoesNotContain(e.Fields["Content SHA256"][..8], e.Summary);
        // the Tag block (U+E0041, "ASCII smuggling") and U+2028 are cleaned per code point; the hash is of the cleaned Content
        var tagged = ("ok\U000E0041\u2028z" + new string('y', 38)).Substring(0, 38);
        var raw2 = File.ReadAllBytes(Fixture(NewId));
        Array.Copy(System.Text.Encoding.Unicode.GetBytes(tagged), 0, raw2, 12, 76);
        var (_, s2) = Run(NewId + ".bin", raw2);
        var c2 = Assert.Single(s2.Events).Fields["Content"];
        Assert.False(c2.Contains("\U000E0041", StringComparison.Ordinal));   // ordinal: the culture-aware overload ignores Tag characters
        Assert.DoesNotContain('\u2028', c2);
        Assert.StartsWith("ok\nz", c2);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes(c2))), Assert.Single(s2.Events).Fields["Content SHA256"]);
        Assert.Contains("parola=S3cr3t", e.Fields["Content"]);
        Assert.DoesNotContain('\u202E', e.Fields["Content"]);
        Assert.DoesNotContain('\u0007', e.Fields["Content"]);
    }

    [Fact]
    public void An_oversized_tab_is_still_routed_to_the_parser_which_reports_the_gap()
    {
        var path = Path.Combine(Path.GetTempPath(), "la_np_big_" + Guid.NewGuid().ToString("N"), NewId + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var head = File.ReadAllBytes(Fixture(NewId));
            using (var f = File.Create(path)) { f.Write(head); f.SetLength(NotepadTabStateParser.MaxFileBytes + 10); }
            Assert.Equal(NotepadTabStateParser.SourceTypeName, LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.ImportType(path));
            var sink = new ListSink();
            var r = new NotepadTabStateParser().Parse(new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = NotepadTabStateParser.SourceTypeName, StoredPath = path }, path, sink, default);
            Assert.NotEmpty(r.Gaps);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void The_real_saved_tab_gives_path_hash_time_and_the_unsaved_line()
    {
        var (r, s) = Run(SavedId + ".bin", File.ReadAllBytes(Fixture(SavedId)));
        Assert.Equal(1, r.Records);
        Assert.Empty(r.Gaps);
        var e = Assert.Single(s.Events);
        Assert.Equal(@"C:\LA\np\salvat-test.txt", e.Fields["Saved File Path"]);
        Assert.Equal(@"C:\LA\np\salvat-test.txt", e.Path);
        Assert.Equal("9c0974b40c778ddf6fa02b58e380ec69e4db2b88460a55429d7c4e3eed54ee21", e.Fields["Saved File SHA256"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 32, 0, TimeSpan.Zero).AddTicks(5234055), e.Time.Utc);
        Assert.Equal("da", e.Fields["Unsaved Changes"]);
        Assert.Equal("76", e.Fields["Content Length"]);
        Assert.Contains("TEST-LA continut salvat pe disc", e.Fields["Content"]);
        Assert.Contains("TEST-LA adaugat nesalvat in fisierul salvat", e.Fields["Content"]);
        Assert.Equal("da", e.Fields["CRC Valid"]);
        Assert.Equal("36", e.Fields["Saved File Size"]);                // 33 characters plus the UTF-8 BOM
        Assert.DoesNotContain('\r', e.Summary);
        Assert.DoesNotContain('\n', e.Summary);
    }

    [Theory]
    [InlineData(NewId)]
    [InlineData(SavedId)]
    public void Real_files_truncated_at_every_offset_never_throw_and_keep_a_clean_summary(string id)
    {
        var real = File.ReadAllBytes(Fixture(id));
        for (int n = 0; n < real.Length; n++)
        {
            var cut = real[..n];
            var t = NotepadTabStateParser.Decode(cut);                 // never throws
            Assert.False(t.Complete);
            var (_, s) = Run(id + ".bin", cut);
            foreach (var e in s.Events) { Assert.DoesNotContain('\r', e.Summary); Assert.DoesNotContain('\n', e.Summary); }
        }
        Assert.True(NotepadTabStateParser.Decode(real).Complete);
    }

    [Fact]
    public void Real_files_with_any_single_byte_changed_never_throw()
    {
        foreach (var id in new[] { NewId, SavedId })
        {
            var real = File.ReadAllBytes(Fixture(id));
            for (int i = 0; i < real.Length; i++)
                foreach (var v in new byte[] { 0x00, 0xFF, 0x80 })
                {
                    var b = (byte[])real.Clone(); b[i] = v;
                    NotepadTabStateParser.Decode(b);
                }
        }
    }

    // ---- P1: uLEB128 ----

    [Fact]
    public void Endless_uleb_stops_cleanly()
    {
        var endless = Cat(Np, Enumerable.Repeat((byte)0x80, 5000).ToArray());
        var t = NotepadTabStateParser.Decode(endless);
        Assert.False(t.Complete);
        var (r, s) = Run(NewId + ".bin", endless);
        Assert.Empty(s.Events);
        Assert.Equal(1, r.MalformedRecords);
    }

    [Fact]
    public void Uleb_over_64_bits_stops_cleanly()
    {
        // the tenth byte may carry only bit 63
        var over = Cat(Np, Enumerable.Repeat((byte)0xFF, 9).ToArray(), [0x02], [0, 1, 1, 0, 0]);
        var t = NotepadTabStateParser.Decode(over);
        Assert.False(t.Complete);
        Assert.False(t.IsTab);
        // the largest valid value is accepted as a number and then refused as a length
        var max = Cat(Np, [0], [1], Enumerable.Repeat((byte)0xFF, 9).ToArray(), [0x01]);
        Assert.False(NotepadTabStateParser.Decode(max).Complete);
    }

    // ---- P2: lengths ----

    [Fact]
    public void Lengths_past_the_file_stop_the_read_without_throwing()
    {
        var huge = Leb(ulong.MaxValue);
        // path length
        var a = Cat(Np, [0, 1], huge, U16("C:\\x"));
        Assert.False(NotepadTabStateParser.Decode(a).Complete);
        // content length (unsaved tab: seq, saved=0, unknown, selection 0 0, options, then the length)
        var b = Cat(Np, [0, 0, 1, 0, 0], [1, 0, 0, 0], huge, U16("abc"));
        var t = NotepadTabStateParser.Decode(b);
        Assert.False(t.Complete);
        Assert.Equal("abc", t.Content);                                // what is there is kept
        var (r, s) = Run(NewId + ".bin", b);
        Assert.Single(s.Events);
        Assert.Equal(1, r.MalformedRecords);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("sfârșitului fișierului"));
    }

    // ---- P3: caps ----

    [Fact]
    public void A_file_over_the_size_cap_is_not_read()
    {
        var (r, s) = Run(NewId + ".bin", Cat(Np, new byte[(int)NotepadTabStateParser.MaxFileBytes]));
        Assert.Empty(s.Events);
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Failed);
    }

    [Fact]
    public void More_chunks_than_the_cap_stop_the_count()
    {
        var t = NotepadTabStateParser.Decode(Tab(false, "", "hi", unsaved: true, chunks: NotepadTabStateParser.MaxChunks + 5));
        Assert.Equal(NotepadTabStateParser.MaxChunks, t.Chunks);
        Assert.False(t.Complete);
        var ok = NotepadTabStateParser.Decode(Tab(false, "", "hi", unsaved: true, chunks: 3));
        Assert.Equal(3, ok.Chunks);
        Assert.True(ok.Complete);
    }

    [Fact]
    public void Content_over_64Ki_characters_is_cut_and_marked()
    {
        var text = new string('a', NotepadTabStateParser.MaxContentChars + 100);
        var (_, s) = Run(NewId + ".bin", Tab(false, "", text));
        var e = Assert.Single(s.Events);
        Assert.Equal(text.Length.ToString(), e.Fields["Content Length"]);
        Assert.Contains("trunchiat", e.Fields["Content"]);
        Assert.True(e.Fields["Content"].Length < text.Length);
        Assert.True(e.Summary.Length < 450);
    }

    // ---- P4: UTF-16 ----

    [Fact]
    public void Broken_surrogates_and_an_odd_cut_do_not_throw()
    {
        var bytes = Tab(false, "", "ab\uD800cd");                    // a lone high surrogate
        var (_, s) = Run(NewId + ".bin", bytes);
        Assert.Contains("\uFFFD", Assert.Single(s.Events).Fields["Content"]);
        // cut in the middle of a UTF-16 unit of the content
        var odd = bytes[..(bytes.Length - 6)];
        var t = NotepadTabStateParser.Decode(odd);
        Assert.False(t.Complete);
        Run(NewId + ".bin", odd);
    }

    // ---- P5: FILETIME ----

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x24C85A5ED1C04000UL)]
    public void An_invalid_FILETIME_gives_no_time_and_the_event_stays(ulong ft)
    {
        var (_, s) = Run(SavedId + ".bin", Tab(true, @"C:\LA\a.txt", "x", fileTime: ft));
        var e = Assert.Single(s.Events);
        Assert.False(e.Time.IsKnown);
        Assert.Equal("", e.Fields["Saved File Time"]);
        Assert.Equal(@"C:\LA\a.txt", e.Fields["Saved File Path"]);
    }

    // ---- P6: CRC ----

    [Fact]
    public void A_wrong_CRC_keeps_the_event_and_writes_the_limitation()
    {
        var (r, s) = Run(NewId + ".bin", Tab(false, "", "continut", badCrc: true));
        var e = Assert.Single(s.Events);
        Assert.Equal("nu", e.Fields["CRC Valid"]);
        Assert.Contains("CRC32", e.Fields["Limitation"]);
        Assert.Equal("continut", e.Fields["Content"]);
        Assert.Equal(1, r.Records);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("CRC32"));
        // a real file with one content byte changed
        var real = File.ReadAllBytes(Fixture(NewId)); real[20] ^= 0x01;
        var (_, s2) = Run(NewId + ".bin", real);
        Assert.Equal("nu", Assert.Single(s2.Events).Fields["CRC Valid"]);
    }

    // ---- P7: hostile text ----

    [Fact]
    public void Control_characters_and_RLO_do_not_reach_the_summary()
    {
        const string rlo = "\u202E";
        var content = "linia1\r\nlinia2\t" + rlo + "gpj.exe\u0007\u0000x" + new string('z', 400);
        var (_, s) = Run(NewId + ".bin", Tab(true, "C:\\u" + rlo + "\r\nsers\\a.txt", content, unsaved: true));
        var e = Assert.Single(s.Events);
        foreach (var f in new[] { e.Summary, e.Path })
        {
            Assert.DoesNotContain('\u202E', f);   // char overload: ordinal (a string overload is culture-aware and treats this character as empty)
            Assert.DoesNotContain('\r', f); Assert.DoesNotContain('\n', f); Assert.DoesNotContain('\t', f);
            Assert.DoesNotContain('\u0007', f); Assert.DoesNotContain('\0', f);
        }
        Assert.True(e.Summary.Length < 300 + 250);                     // at most 300 characters of text plus the fixed words and the path
        Assert.EndsWith("…", NotepadTabStateParser.Preview(content));
    }

    // ---- P8: the path is never opened ----

    [Fact]
    public void The_saved_path_is_never_opened()
    {
        var target = Write("target.txt", "secret"u8.ToArray());
        using var hold = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // any open of the path would fail
        var (r, s) = Run(SavedId + ".bin", Tab(true, target, "x"));
        Assert.Equal(1, r.Records);
        Assert.Equal(target, Assert.Single(s.Events).Fields["Saved File Path"]);
    }

    // ---- WindowState, copies, routing ----

    [Fact]
    public void A_WindowState_file_is_noted_and_not_interpreted()
    {
        byte[] window = [0x4E, 0x50, 0x04, 0x5C, 0x00, 0x04, 0x0D, 0x25, 0xA9, 0xCC, 0x40, 0xF6, 0x18, 0x4E, 0x86, 0x56, 0x40, 0x46, 0xDE, 0x99, 0x9D, 0xB9];
        var (r, s) = Run("44adaa60-6ace-4f48-9c04-02b62504631a.0.bin", window);
        Assert.Empty(s.Events);
        Assert.Equal(0, r.MalformedRecords);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("WindowState"));
    }

    [Fact]
    public void Copies_of_a_tab_are_separate_events_with_distinct_locators()
    {
        var data = File.ReadAllBytes(Fixture(SavedId));
        var sink = new ListSink();
        foreach (var name in new[] { SavedId + ".bin", SavedId + ".0.bin", SavedId + ".1.bin" })
        {
            var p = Write(name, data);
            new NotepadTabStateParser().Parse(Item(p), p, sink, default);
        }
        Assert.Equal(3, sink.Events.Count);
        Assert.Equal(3, sink.Events.Select(e => e.Locator).Distinct().Count());
        Assert.Equal(["", "0", "1"], sink.Events.Select(e => e.Fields["Copy Index"]).Order().ToArray());
        Assert.All(sink.Events, e => Assert.Equal(SavedId, e.Fields["Tab ID"]));
    }

    [Fact]
    public void Routing_types_only_a_tab_file_and_leaves_other_bin_files_alone()
    {
        var real = Write(NewId + ".bin", File.ReadAllBytes(Fixture(NewId)));
        Assert.Equal(NotepadTabStateParser.FingerprintName, EvidenceFingerprint.Detect(real));
        Assert.Equal(NotepadTabStateParser.SourceTypeName, InvestigationPipeline.ImportType(real));
        Assert.Equal(NotepadTabStateParser.SourceTypeName, InvestigationPipeline.ImportType(Write(SavedId + ".1.bin", File.ReadAllBytes(Fixture(SavedId)))));

        // WindowState: same names, "NP", but not a tab
        var window = Write("44adaa60-6ace-4f48-9c04-02b62504631a.0.bin", [0x4E, 0x50, 0x04, 0x5C, 0, 4, 0x0D, 0x25, 0xA9, 0xCC, 0x40, 0xF6]);
        Assert.Equal("file", InvestigationPipeline.ImportType(window));
        // tab content under another name, and a GUID .bin that is not "NP"
        Assert.Equal("file", InvestigationPipeline.ImportType(Write("notes.bin", File.ReadAllBytes(Fixture(NewId)))));
        Assert.Equal("file", InvestigationPipeline.ImportType(Write(Guid.NewGuid() + ".bin", [1, 2, 3, 4, 5, 6, 7, 8, 9])));
        Assert.Equal("file", InvestigationPipeline.ImportType(Write(Guid.NewGuid() + ".bin", [])));
    }

    [Fact]
    public void The_parser_is_registered_for_its_source_type()
    {
        var item = new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "s", SourceType = NotepadTabStateParser.SourceTypeName, StoredPath = "x" };
        Assert.Contains(WindowsParsers.Registry.Candidates(item), c => c.Descriptor.ParserId == "NotepadTabStateParser");
    }
}
