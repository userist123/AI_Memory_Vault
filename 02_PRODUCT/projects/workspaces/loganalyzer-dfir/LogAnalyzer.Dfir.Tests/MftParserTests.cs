using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>S2: MftParser on the real Windows 11 corpus (Corpus/Ntfs, truth.json from Windows) and on hostile synthetic files (P7, P9, P10, P11, P1/P2 at file level).</summary>
public sealed class MftParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_mft_" + Guid.NewGuid().ToString("N"));
    public MftParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Corpus(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);
    private static readonly DateTimeOffset Acquired = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static long Ft(int y, int mo, int d) => new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    private static EvidenceItem Item(string path, DateTimeOffset? acquired = null)
        => new() { EvidenceId = "EV-MFT", CaseId = "C", Source = "test", SourceType = MftParser.SourceTypeName, StoredPath = path, AcquiredAtUtc = acquired ?? default };

    private static (ParseResult R, List<TimelineEvent> E) Run(string path, DateTimeOffset? acquired = null, CancellationToken ct = default)
    {
        var sink = new ListSink();
        var r = new MftParser().Parse(Item(path, acquired), path, sink, ct);
        return (r, sink.Events.ToList());
    }

    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, data); return p; }

    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
    private static string Iso(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static JsonElement Truth() => JsonDocument.Parse(File.ReadAllText(Corpus("truth.json"))).RootElement;
    private static ulong NumberOf(string fileId) => Convert.ToUInt64(fileId[2..], 16) & 0xFFFF_FFFF_FFFFUL;

    // ---------------- real corpus ----------------

    [Fact]
    public void Corpus_every_truth_entry_matches_path_size_and_si_times_to_100ns()
    {
        var (r, ev) = Run(Corpus("mft.bin"), Acquired);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(0, r.MalformedRecords);
        var byNumber = ev.Where(e => F(e, "Category") != "summary" && F(e, "InUse") == "true").GroupBy(e => F(e, "RecordNumber")).ToDictionary(g => g.Key, g => g.First());
        int checkedCount = 0;
        foreach (var f in Truth().GetProperty("files").EnumerateArray())
        {
            var role = f.GetProperty("role").GetString()!;
            var path = f.GetProperty("path").GetString()!;
            if (role == "to_delete" || path == "\\docs\\stomped.txt") continue;   // deleted: own test; stomped: own test
            var num = NumberOf(f.GetProperty("fileId").GetString()!).ToString(CultureInfo.InvariantCulture);
            Assert.True(byNumber.TryGetValue(num, out var e), $"nicio înregistrare în uz pentru {path} (#{num})");
            Assert.Equal(path, e!.Path);
            Assert.Equal(f.GetProperty("size").GetInt64().ToString(CultureInfo.InvariantCulture), F(e, "Size"));
            Assert.Equal(Iso(f.GetProperty("siCreated").GetString()!), F(e, "SiCreated"));
            if (F(e, "IsDirectory") == "true")
            {
                // truth.json captured the directories (make_fixture.ps1 lines 35 and 40) BEFORE later steps changed their
                // contents (rename, stomp, move, delete); their modified/accessed times can only have moved forward.
                Assert.True(string.CompareOrdinal(F(e, "SiModified"), Iso(f.GetProperty("siModified").GetString()!)) >= 0, path + " SiModified went backwards");
                Assert.True(string.CompareOrdinal(F(e, "SiAccessed"), Iso(f.GetProperty("siAccessed").GetString()!)) >= 0, path + " SiAccessed went backwards");
            }
            else
            {
                Assert.True(Iso(f.GetProperty("siModified").GetString()!) == F(e, "SiModified"), path + " SiModified: " + F(e, "SiModified"));
                Assert.True(Iso(f.GetProperty("siAccessed").GetString()!) == F(e, "SiAccessed"), path + " SiAccessed: " + F(e, "SiAccessed"));
            }
            Assert.Equal("true", F(e, "PathComplete"));
            if (f.GetProperty("fileId").GetString() is { } id) Assert.Equal(id, F(e, "FileId"));
            checkedCount++;
        }
        Assert.True(checkedCount >= 40, $"prea puține intrări verificate: {checkedCount}");
    }

    [Fact]
    public void Corpus_stomped_file_keeps_the_real_times_in_the_filename_attribute()
    {
        var (_, ev) = Run(Corpus("mft.bin"), Acquired);
        var t = Truth().GetProperty("stomped")[0];
        var e = Assert.Single(ev, x => x.Path == "\\docs\\stomped.txt");
        Assert.Equal("2019-03-14T09:26:53.0000000Z", F(e, "SiCreated"));
        Assert.Equal("2019-03-14T09:26:53.0000000Z", F(e, "SiModified"));
        var before = t.GetProperty("before");
        Assert.Equal(Iso(before.GetProperty("siCreated").GetString()!), F(e, "FnCreated"));
        Assert.Equal(Iso(before.GetProperty("siModified").GetString()!), F(e, "FnModified"));
        Assert.Equal(before.GetProperty("fileId").GetString(), F(e, "FileId"));
        Assert.Equal("mismatch", F(e, "Category"));
        Assert.Contains("SI_CREATED_BEFORE_FN_CREATED", F(e, "Signals"));
        Assert.Equal(Classification.Candidate, e.Classification);   // one signal is never proof
        Assert.Equal(Confidence.Low, e.Confidence);
    }

    [Fact]
    public void Corpus_rename_and_move_keep_the_record_and_update_name_and_parent()
    {
        var (_, ev) = Run(Corpus("mft.bin"), Acquired);
        var ren = Truth().GetProperty("renamed")[0];
        var after = Assert.Single(ev, e => e.Path == "\\docs\\after_rename.txt");
        Assert.Equal(ren.GetProperty("fileId").GetString(), F(after, "FileId"));
        Assert.Equal("after_rename.txt", F(after, "FileName"));
        Assert.DoesNotContain(ev, e => e.Path == "\\docs\\before_rename.txt" && F(e, "InUse") == "true");
        var moved = Assert.Single(ev, e => e.Path == "\\many\\moved.txt");
        var many = Assert.Single(ev, e => e.Path == "\\many" && F(e, "IsDirectory") == "true");
        Assert.Equal(new MftReference(Convert.ToUInt64(F(many, "FileId")[2..], 16)).FileId, F(many, "FileId"));
        Assert.Equal(F(many, "FileId"), F(moved, "ParentFileId"));
        Assert.DoesNotContain(ev, e => e.Path == "\\docs\\moved.txt");
    }

    [Fact]
    public void Corpus_the_17_free_records_appear_as_deleted_and_no_live_record_does()
    {
        var (_, ev) = Run(Corpus("mft.bin"), Acquired);
        var deleted = ev.Where(e => F(e, "Category") == "deleted").ToList();
        var truthDeleted = Truth().GetProperty("deleted").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
        var files = Truth().GetProperty("files").EnumerateArray().ToList();
        var named = deleted.Where(e => System.Text.RegularExpressions.Regex.IsMatch(F(e, "FileName"), @"^file_\d{3}\.txt$")).ToList();
        Assert.Equal(17, named.Count);
        foreach (var e in named)
        {
            Assert.Contains(e.Path, truthDeleted);
            Assert.Equal("false", F(e, "InUse"));
            Assert.Equal("true", F(e, "PathComplete"));
            var f = files.Single(x => x.GetProperty("path").GetString() == e.Path && x.GetProperty("role").GetString() == "to_delete");
            Assert.Equal(NumberOf(f.GetProperty("fileId").GetString()!).ToString(CultureInfo.InvariantCulture), F(e, "RecordNumber"));
        }
        // 3 of the 20 deleted names were reused by later files: their records are live and must not be reported deleted
        var live = ev.Where(e => F(e, "InUse") == "true" && F(e, "Category") != "summary").Select(e => F(e, "RecordNumber")).ToHashSet();
        Assert.DoesNotContain(deleted, e => live.Contains(F(e, "RecordNumber")));
        Assert.All(deleted, e => Assert.Equal("false", F(e, "InUse")));
        foreach (var f in files.Where(x => x.GetProperty("role").GetString() is "live" or "moved" or "renamed"))
            Assert.DoesNotContain(deleted, e => F(e, "RecordNumber") == NumberOf(f.GetProperty("fileId").GetString()!).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Corpus_summary_counts_and_record_size_from_the_boot_sector_or_the_first_header()
    {
        var (r, ev) = Run(Corpus("mft.bin"), Acquired);   // boot.bin sits next to mft.bin
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal("256", F(s, "TotalRecords")); Assert.Equal("1024", F(s, "RecordSize")); Assert.StartsWith("boot sector", F(s, "RecordSizeSource"));
        Assert.Equal("0", F(s, "FixupErrors"));
        Assert.True(int.Parse(F(s, "Deleted")) >= 17);
        // the same bytes without a boot sector next to them
        var copy = Write("$MFT", File.ReadAllBytes(Corpus("mft.bin")));
        var (r2, ev2) = Run(copy, Acquired);
        var s2 = Assert.Single(ev2, e => F(e, "Category") == "summary");
        Assert.Equal("1024", F(s2, "RecordSize")); Assert.StartsWith("primul antet", F(s2, "RecordSizeSource"));
        Assert.Equal(ev.Count, ev2.Count);
        Assert.Equal(r.Records, r2.Records);
    }

    [Fact]
    public void Corpus_without_an_acquisition_date_omits_the_recent_category_with_a_note()
    {
        var (_, ev) = Run(Corpus("mft.bin"), null);
        Assert.DoesNotContain(ev, e => F(e, "Category") == "recent");
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Contains("omisă", F(s, "RecentCategory")); Assert.NotEmpty(s.Notes);
        Assert.DoesNotContain(ev, e => F(e, "Category") == "deleted" && e.Path == "");
    }

    [Fact]
    public void Descriptor_registry_fingerprint_and_capabilities_are_wired()
    {
        var p = WindowsParsers.Registry.Candidates(Item(Corpus("mft.bin"))).OfType<MftParser>().Single();
        Assert.Equal("Validated", ParserMaturityName(p));
        Assert.Contains(WindowsParsers.Registry.Descriptors, d => d.ParserId == "MftParser");
        Assert.True(p.Descriptor.Accepts(Item(Corpus("mft.bin"))));
        Assert.True(p.Descriptor.Accepts(new EvidenceItem { EvidenceId = "e", CaseId = "c", Source = "s", SourceType = "x", StoredPath = "C:\\x\\$MFT" }));
        Assert.True(p.Descriptor.Accepts(new EvidenceItem { EvidenceId = "e", CaseId = "c", Source = "s", SourceType = "x", StoredPath = "C:\\x\\case1.mft" }));
        Assert.Equal("ntfs_mft", EvidenceFingerprint.Detect(Corpus("mft.bin")));
        Assert.Equal("unknown", EvidenceFingerprint.Detect(Write("junk.bin", [.. "FILE"u8, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])));
        Assert.NotNull(p.Descriptor.Capabilities);
        var pre = p.Preflight(Item(Corpus("mft.bin")), Corpus("mft.bin"));
        Assert.NotEqual(PreflightStatus.FormatMismatch, pre.Status);
    }

    private static string ParserMaturityName(MftParser p) => p.Descriptor.Status.ToString();

    // ---------------- hostile files ----------------

    private static byte[] MftOf(params (int, byte[])[] recs) => Mft(recs);
    private static readonly long Recent = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    [Fact] // P7: cyclic parents
    public void P7_cyclic_parent_chain_ends_with_a_marked_incomplete_path()
    {
        var mft = MftOf((40, DirRecord(1, Ref(41, 1), "a", Recent)), (41, DirRecord(1, Ref(40, 1), "b", Recent)),
                        (42, FileRecord(1, Ref(40, 1), "victim.txt", Recent, Recent)));
        var (r, ev) = Run(Write("cyc.mft", mft), Acquired);
        var e = Assert.Single(ev, x => F(x, "FileName") == "victim.txt");
        Assert.StartsWith("…\\", e.Path); Assert.Equal("false", F(e, "PathComplete")); Assert.Contains("ciclu", F(e, "PathProblem"));
        Assert.NotEqual(EvidenceStatus.Failed, r.Status);
        // a directory that is its own parent (not the root)
        var self = MftOf((40, DirRecord(1, Ref(40, 1), "self", Recent)), (42, FileRecord(1, Ref(40, 1), "x.txt", Recent, Recent)));
        var (_, ev2) = Run(Write("self.mft", self), Acquired);
        Assert.Contains("ciclu", F(Assert.Single(ev2, x => F(x, "FileName") == "x.txt"), "PathProblem"));
    }

    [Fact] // P7: depth over 64
    public void P7_depth_over_64_is_cut_and_marked_but_depth_within_64_is_complete()
    {
        List<(int, byte[])> Chain(int depth)
        {
            var l = new List<(int, byte[])>();
            ulong parent = Ref(5, 5);
            for (int i = 0; i < depth; i++) { l.Add((100 + i, DirRecord(1, parent, "d" + i, Recent))); parent = Ref((ulong)(100 + i), 1); }
            l.Add((300, FileRecord(1, parent, "leaf.txt", Recent, Recent)));
            return l;
        }
        var (_, deep) = Run(Write("deep.mft", MftOf([.. Chain(70)])), Acquired);
        var d = Assert.Single(deep, x => F(x, "FileName") == "leaf.txt");
        Assert.Equal("false", F(d, "PathComplete")); Assert.Contains("64", F(d, "PathProblem")); Assert.StartsWith("…\\", d.Path);
        var (_, ok) = Run(Write("ok.mft", MftOf([.. Chain(60)])), Acquired);
        var o = Assert.Single(ok, x => F(x, "FileName") == "leaf.txt");
        Assert.Equal("true", F(o, "PathComplete")); Assert.StartsWith("\\d0\\d1\\", o.Path); Assert.EndsWith("\\d59\\leaf.txt", o.Path);
    }

    [Fact]
    public void Reused_parent_is_marked_not_followed()
    {
        // the file points at (40, seq 1) but record 40 is now a different directory with seq 2
        var mft = MftOf((40, DirRecord(2, Ref(5, 5), "new_owner", Recent)), (42, FileRecord(1, Ref(40, 1), "orphan.txt", Recent, Recent)));
        var (_, ev) = Run(Write("reuse.mft", mft), Acquired);
        var e = Assert.Single(ev, x => F(x, "FileName") == "orphan.txt");
        Assert.Equal("…\\orphan.txt", e.Path); Assert.Equal("false", F(e, "PathComplete")); Assert.Contains("reutilizat", F(e, "PathProblem"));
    }

    [Fact] // P9 at file level
    public void P9_impossible_filetimes_are_kept_raw_and_do_not_stop_the_parse()
    {
        var bad = new[] { 0L, -1L, long.MaxValue, Timestamp.MaxFileTime + 1, 5L };
        var recs = new List<(int, byte[])>();
        for (int i = 0; i < bad.Length; i++)
            recs.Add((40 + i, FileRecord(1, Ref(5, 5), $"t{i}.txt", bad[i], bad[i])));
        recs.Add((60, FileRecord(1, Ref(5, 5), "good.txt", Recent, Recent)));
        var (r, ev) = Run(Write("times.mft", MftOf([.. recs])), Acquired);
        Assert.NotEqual(EvidenceStatus.Failed, r.Status);
        Assert.Contains(ev, e => e.Path == "\\good.txt");
        Assert.Equal(0, ev.Count(e => F(e, "FileName").StartsWith('t') && F(e, "Category") == "recent"));   // never recent: time is not interpreted
        Assert.Single(ev, e => F(e, "Category") == "summary");
    }

    [Fact] // P9: a deleted record with impossible times is still reported, time unknown, raw kept
    public void P9_deleted_record_with_zero_times_is_reported_with_unknown_time()
    {
        var rec = FileRecord(3, Ref(5, 5), "gone.txt", 0, 0, flags: 0);
        var (_, ev) = Run(Write("del0.mft", MftOf((40, rec))), Acquired);
        var e = Assert.Single(ev, x => F(x, "Category") == "deleted");
        Assert.False(e.Time.IsKnown); Assert.Equal("raw:0", F(e, "SiCreated")); Assert.Equal("gone.txt", F(e, "FileName"));
    }

    [Fact] // P10
    public void P10_hostile_names_are_verbatim_in_fields_and_cannot_break_the_summary()
    {
        string[] names = ["a\r\nb.txt", "evil\u202Egnp.exe", "=cmd|' /C calc'!A0", "+SUM(1+1)", "-2+3", "@SUM(A1)", "x\u0000y\u001Bz", "\u200Fhid\u2066den.txt", "tab\tname"];
        var recs = names.Select((n, i) => (40 + i, FileRecord(1, Ref(5, 5), n, Recent, Recent))).ToArray();
        var (r, ev) = Run(Write("hostile.mft", MftOf(recs)), Acquired);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        foreach (var n in names)
        {
            var e = Assert.Single(ev, x => F(x, "FileName") == n);   // verbatim
            Assert.Equal("\\" + n, e.Path);
            Assert.StartsWith("$MFT: ", e.Summary);
            Assert.DoesNotContain(e.Summary, c => char.IsControl(c) || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069') || c == '\u200F' || c == '\u200E');
            Assert.Contains(MftParser.SafeText(n), e.Summary);
        }
        Assert.Contains("evil\\u202Egnp.exe", ev.Single(x => F(x, "FileName") == "evil\u202Egnp.exe").Summary);
        Assert.Contains("a\\u000D\\u000Ab.txt", ev.Single(x => F(x, "FileName") == "a\r\nb.txt").Summary);
        Assert.StartsWith("=cmd", F(ev.Single(x => F(x, "FileName").StartsWith("=cmd")), "FileName"));   // formula start kept verbatim
        Assert.Single(ev, e => F(e, "Category") == "summary");
        // serialises to JSON without error
        foreach (var e in ev) JsonSerializer.Serialize(new { e.Summary, e.Path, e.Fields });
    }


    [Fact] // names with unpaired surrogates
    public void Unpaired_surrogates_in_names_do_not_break_serialisation()
    {
        var fn = Fn(Ref(5, 5), "", Recent, Recent, Recent, Recent, nameBytes: [0x41, 0, 0x00, 0xD8, 0x42, 0]);
        var (_, ev) = Run(Write("sur.mft", MftOf((40, FileRecord(1, Ref(5, 5), "", Recent, Recent, fnValue: fn)))), Acquired);
        var e = Assert.Single(ev, x => F(x, "RecordNumber") == "40");
        Assert.Equal("true", F(e, "NameInvalidUtf16"));
        JsonSerializer.Serialize(new { e.Summary, e.Path, e.Fields });
    }

    [Fact] // P11
    public void P11_file_cut_in_the_middle_of_a_record_is_partial_and_keeps_the_records_before()
    {
        var full = File.ReadAllBytes(Corpus("mft.bin"));
        var (rFull, evFull) = Run(Corpus("mft.bin"), Acquired);
        var cut = Write("cut.mft", full[..(100 * 1024 + 512)]);
        var (r, ev) = Run(cut, Acquired);
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.NotEmpty(r.Gaps);
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal("100", F(s, "TotalRecords")); Assert.Equal("true", F(s, "TruncatedFile"));
        // every record before the cut that was emitted in the full run is emitted identically
        var before = evFull.Where(e => F(e, "Category") is "recent" or "mismatch" && int.Parse(F(e, "RecordNumber")) < 100).Select(e => e.Path + "|" + F(e, "SiCreated")).OrderBy(x => x).ToList();
        var got = ev.Where(e => F(e, "Category") is "recent" or "mismatch").Select(e => e.Path + "|" + F(e, "SiCreated")).OrderBy(x => x).ToList();
        Assert.NotEmpty(before); Assert.Equal(before, got);
        // a file shorter than one record, and cut inside the first record header
        var (r2, _) = Run(Write("tiny.mft", full[..300]), Acquired);
        Assert.NotEqual(EvidenceStatus.Success, r2.Status);
        var (r3, _) = Run(Write("tiny2.mft", full[..6]), Acquired);
        Assert.Equal(EvidenceStatus.Failed, r3.Status);
        // a cut at every 97th offset inside the first 20 records is always Partial (never Success, never an unhandled error)
        for (int k = 8; k < 20 * 1024; k += 97)
        {
            if (k % 1024 == 0) continue;
            var (rk, evk) = Run(Write("k.mft", full[..k]), Acquired);
            Assert.Equal(EvidenceStatus.Partial, rk.Status);
            Assert.Equal((k / 1024).ToString(), F(Assert.Single(evk, e => F(e, "Category") == "summary"), "TotalRecords"));
        }
    }

    [Fact] // P1 / P2 at file level
    public void P1_P2_damaged_records_are_counted_and_do_not_stop_the_parse()
    {
        var full = File.ReadAllBytes(Corpus("mft.bin"));
        var torn = (byte[])full.Clone(); torn[0x29 * 1024 + 510] ^= 0xFF;                       // P2 on big.bin
        var badUsa = (byte[])full.Clone(); badUsa[0x2A * 1024 + 6] = 9;                         // P1: USA count 9
        var both = (byte[])torn.Clone(); badUsa.AsSpan(0x2A * 1024, 1024).CopyTo(both.AsSpan(0x2A * 1024, 1024));
        var (r2, ev2) = Run(Write("both.mft", both), Acquired);
        Assert.Equal(EvidenceStatus.Partial, r2.Status);
        Assert.Equal(2, r2.MalformedRecords);
        var s = Assert.Single(ev2, e => F(e, "Category") == "summary");
        Assert.Equal("1", F(s, "FixupErrors")); Assert.Equal("1", F(s, "InvalidRecords"));
        Assert.DoesNotContain(ev2, e => F(e, "RecordNumber") is "41" or "42");
        Assert.Contains(ev2, e => e.Path == "\\docs\\small.txt");
    }

    [Fact]
    public void A_file_that_is_not_an_mft_fails_and_cancellation_is_partial()
    {
        var (r, _) = Run(Write("notmft.bin", new byte[4096]), Acquired);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var (r2, _) = Run(Corpus("mft.bin"), Acquired, cts.Token);
        Assert.Equal(EvidenceStatus.Partial, r2.Status);
    }

    [Fact] // boot sector next to the file is hostile too (P8 at file level)
    public void P8_a_hostile_boot_sector_beside_the_file_is_reported_and_the_header_size_is_used()
    {
        var dir = Path.Combine(_dir, "withboot"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "$MFT"), File.ReadAllBytes(Corpus("mft.bin")));
        File.WriteAllBytes(Path.Combine(dir, "$Boot"), BootSector(0x80));
        var path = Path.Combine(dir, "$MFT");
        var (r, ev) = Run(path, Acquired);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("boot", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("1024", F(Assert.Single(ev, e => F(e, "Category") == "summary"), "RecordSize"));
    }

    [Fact]
    public void Emission_caps_hold_and_over_cap_records_are_counted_in_the_summary()
    {
        // 5,200 files whose $SI created is before $FN created: only 5,000 mismatch events, the rest counted
        var recs = new List<(int, byte[])>();
        var fnTime = Ft(2026, 11, 10);
        for (int i = 0; i < MftParser.MaxMismatchEvents + 200; i++)
            recs.Add((40 + i, FileRecord(1, Ref(5, 5), $"m{i}.txt", Ft(2020, 1, 1), fnTime)));
        var (r, ev) = Run(Write("caps.mft", MftOf([.. recs])), Acquired);
        Assert.Equal(MftParser.MaxMismatchEvents, ev.Count(e => F(e, "Category") == "mismatch"));
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal((MftParser.MaxMismatchEvents + 200).ToString(), F(s, "MismatchTotal"));
        Assert.NotEmpty(r.Gaps);
    }

    [Fact]
    public void Extension_record_filenames_are_attached_to_their_base_record_by_sequence()
    {
        // base record 40 (seq 3) carries $SI only; its $FILE_NAME lives in extension record 41 (base 40:3)
        var baseRec = Record(3, InUse, 0, Resident(0x10, Si(Recent, Recent, Recent, Recent)), Resident(0x80, new byte[2]));
        var extRec = Record(1, InUse, Ref(40, 3), Resident(0x30, Fn(Ref(5, 5), "split.txt", Recent, Recent, Recent, Recent)));
        var staleExt = Record(1, InUse, Ref(50, 9), Resident(0x30, Fn(Ref(5, 5), "stale.txt", Recent, Recent, Recent, Recent)));
        var base50 = Record(1, InUse, 0, Resident(0x10, Si(Recent, Recent, Recent, Recent)));
        var (_, ev) = Run(Write("ext.mft", MftOf((40, baseRec), (41, extRec), (50, base50), (51, staleExt))), Acquired);
        Assert.Contains(ev, e => e.Path == "\\split.txt" && F(e, "RecordNumber") == "40");
        Assert.DoesNotContain(ev, e => F(e, "FileName") == "stale.txt");   // base sequence differs: not trusted
    }
}
