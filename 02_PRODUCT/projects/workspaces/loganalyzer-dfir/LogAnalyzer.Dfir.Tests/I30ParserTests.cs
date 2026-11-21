using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.I30TestKit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>S5: I30Parser on the real Windows 11 corpus (Corpus/Ntfs: many_i30.bin + mft.bin + boot.bin, truth.json from Windows) and on hostile synthetic blocks (Q1-Q10).</summary>
public sealed class I30ParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_i30_" + Guid.NewGuid().ToString("N"));
    public I30ParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Corpus(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);
    private static long Ft(int y, int mo, int d) => new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static readonly long T = Ft(2026, 11, 10);
    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
    private static string BS => ((char)92).ToString();
    private static ulong Id(ulong number, ushort seq) => number | ((ulong)seq << 48);

    private static (ParseResult R, List<TimelineEvent> E) Run(string path, I30Parser? parser = null)
    {
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "EV-I30", CaseId = "C", Source = "test", SourceType = I30Parser.SourceTypeName, StoredPath = path, AcquiredAtUtc = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero) };
        var r = (parser ?? new I30Parser()).Parse(item, path, sink, default);
        return (r, sink.Events.ToList());
    }

    /// <summary>Writes the data in its own folder (so a sibling $MFT / boot sector is only there when the test puts it there).</summary>
    private string W(string sub, byte[] data, byte[]? mft = null, byte[]? boot = null, string name = "$I30")
    {
        var d = Path.Combine(_dir, sub); Directory.CreateDirectory(d);
        if (mft is not null) File.WriteAllBytes(Path.Combine(d, "mft.bin"), mft);
        if (boot is not null) File.WriteAllBytes(Path.Combine(d, "boot.bin"), boot);
        var p = Path.Combine(d, name); File.WriteAllBytes(p, data); return p;
    }

    private static List<TimelineEvent> Of(List<TimelineEvent> ev, string cat) => ev.Where(e => F(e, "Category") == cat).ToList();
    private static TimelineEvent Summary(List<TimelineEvent> ev) => Assert.Single(ev, e => F(e, "Category") == "summary");

    // ---------------- real corpus ----------------

    private sealed record TruthFile(string Path, string Role, ulong Number, ushort Sequence, long Size)
    {
        public string Name => Path[(Path.LastIndexOf((char)92) + 1)..];
    }

    private static List<TruthFile> TruthFiles()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Corpus("truth.json")));
        var l = new List<TruthFile>();
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
        {
            var id = Convert.ToUInt64(f.GetProperty("fileId").GetString()![2..], 16);
            l.Add(new TruthFile(f.GetProperty("path").GetString()!, f.GetProperty("role").GetString()!, id & 0xFFFF_FFFF_FFFFUL, (ushort)((id >> 48) & 0xFFFF), f.GetProperty("size").GetInt64()));
        }
        foreach (var st in doc.RootElement.GetProperty("stomped").EnumerateArray())
        {
            var a = st.GetProperty("after");
            var id = Convert.ToUInt64(a.GetProperty("fileId").GetString()![2..], 16);
            l.Add(new TruthFile(a.GetProperty("path").GetString()!, "stomp_after", id & 0xFFFF_FFFF_FFFFUL, (ushort)((id >> 48) & 0xFFFF), a.GetProperty("size").GetInt64()));
        }
        return l;
    }

    private static List<TruthFile> LiveInMany() => TruthFiles().Where(f => f.Path.StartsWith(BS + "many" + BS) && f.Role != "to_delete").ToList();

    /// <summary>
    /// With 60 names the directory is a two-level B-tree: the separator entries of the root live in the resident $INDEX_ROOT of the \many record (MFT record 44),
    /// not in the INDX blocks. The bytes of that attribute (mft.bin record 44) hold exactly these two live names, as separators with the child flag.
    /// </summary>
    private static readonly string[] InIndexRoot = ["file_017.txt", "file_035.txt"];

    [Fact]
    public void Corpus_allocated_entries_are_exactly_the_41_live_files_of_many_39_from_the_indx_blocks_and_2_from_the_index_root()
    {
        var (r, ev) = Run(Corpus("many_i30.bin"));
        var live = LiveInMany();
        Assert.Equal(41, live.Count);   // 40 file_xxx + moved.txt
        var expected = live.Select(f => f.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var indx = Of(ev, "allocated");
        var root = Of(ev, "allocated_root");
        Assert.Equal(39, indx.Count);
        Assert.Equal(InIndexRoot, root.Select(e => F(e, "FileName")).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(expected, indx.Concat(root).Select(e => F(e, "FileName")).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.All(root, e => Assert.StartsWith("INDEX_ROOT+0x", e.Locator));
        Assert.All(root, e => Assert.Equal("root", F(e, "Vcn")));
        Assert.Equal(0, r.MalformedRecords);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        var s = Summary(ev);
        Assert.Equal("4096", F(s, "BlockSize"));
        Assert.Equal("3", F(s, "Blocks"));
        Assert.Contains("boot", F(s, "BlockSizeSource"));
        Assert.Equal("39", F(s, "AllocatedEntries"));
        Assert.Equal("2", F(s, "RootEntries"));
        Assert.Equal("passed", F(s, "MftGate"));
        Assert.Equal("true", F(s, "MftUsed"));
    }

    [Fact]
    public void Corpus_allocated_entries_carry_the_reference_size_and_locator_of_the_live_record()
    {
        var (_, ev) = Run(Corpus("many_i30.bin"));
        var truth = LiveInMany().ToDictionary(f => f.Name);
        var entries = Of(ev, "allocated").Concat(Of(ev, "allocated_root")).ToList();
        Assert.Equal(41, entries.Count);
        foreach (var e in entries)
        {
            var t = truth[F(e, "FileName")];
            Assert.Equal(t.Number.ToString(CultureInfo.InvariantCulture), F(e, "RecordNumber"));
            Assert.Equal(t.Sequence.ToString(CultureInfo.InvariantCulture), F(e, "Sequence"));
            Assert.StartsWith("2026-11-13T", F(e, "FnCreated"));
            Assert.Equal(t.Size.ToString(CultureInfo.InvariantCulture), F(e, "Size"));
            Assert.Matches(@"^(VCN \d+|INDEX_ROOT)\+0x[0-9A-F]+$", e.Locator);
            Assert.True(e.Time.IsKnown);
        }
    }

    [Fact]
    public void Corpus_slack_is_34_entries_24_live_copies_and_the_10_deleted_names_with_a_free_record()
    {
        var (_, ev) = Run(Corpus("many_i30.bin"));
        var files = TruthFiles();
        var liveNames = LiveInMany().Select(f => f.Name).ToHashSet();
        var deleted = files.Where(f => f.Role == "to_delete").ToList();
        Assert.Equal(20, deleted.Count);
        var slack = Of(ev, "slack");
        Assert.NotEmpty(slack);
        Assert.Equal(34, slack.Count);
        var copies = slack.Where(e => F(e, "SlackClass") == "live_copy").ToList();
        var free = slack.Where(e => F(e, "SlackClass") == "historical_free").ToList();
        Assert.Equal(24, copies.Count);
        Assert.Equal(10, free.Count);
        Assert.Equal(34, copies.Count + free.Count);   // no other class on this volume
        Assert.NotEmpty(copies);
        Assert.All(copies, e => Assert.Contains(F(e, "FileName"), liveNames));
        Assert.Equal(ExpectedDeletedFoundInSlack, free.Select(e => F(e, "FileName")).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(slack, e =>
        {
            Assert.DoesNotContain("șters", e.Summary);      // nothing from slack is called "deleted"
            Assert.Equal(Classification.Candidate, e.Classification);
            Assert.Equal(Confidence.Medium, e.Confidence);
        });
        foreach (var e in free)
        {
            var d = deleted.Single(d => d.Name == F(e, "FileName"));
            Assert.Equal(d.Number.ToString(CultureInfo.InvariantCulture), F(e, "RecordNumber"));
            Assert.Equal(d.Sequence.ToString(CultureInfo.InvariantCulture), F(e, "Sequence"));
        }
        // the three names whose record the MFT gave to a later file (file_003 -> before_rename, file_006 -> stomped, file_009 -> moved) have no intact slack entry
        var reused = deleted.Where(d => files.Any(f => f.Role != "to_delete" && f.Number == d.Number && f.Sequence != d.Sequence)).Select(d => d.Name).OrderBy(x => x).ToArray();
        Assert.Equal(["file_003.txt", "file_006.txt", "file_009.txt"], reused);
        Assert.Empty(slack.Where(e => reused.Contains(F(e, "FileName"))));
        var s = Summary(ev);
        Assert.Equal("34", F(s, "SlackEntries"));
        Assert.Equal("24", F(s, "SlackLiveCopy"));
        Assert.Equal("10", F(s, "SlackFree"));
        Assert.Equal("0", F(s, "SlackReused"));
        Assert.Equal("0", F(s, "SlackUnclassified"));
    }

    /// <summary>Measured twice: by this parser and by an independent byte scan of many_i30.bin (Python, see the session report).</summary>
    private static readonly string[] ExpectedDeletedFoundInSlack =
        ["file_021.txt", "file_024.txt", "file_027.txt", "file_030.txt", "file_033.txt", "file_039.txt", "file_042.txt", "file_045.txt", "file_048.txt", "file_051.txt"];

    // ---------------- review round: coherence gate (R1), parent-aware classes (R2), $INDEX_ROOT (R3), probes P1, P1b, P2, P3 ----------------

    /// <summary>The corpus $I30 in its own folder with a replaced $MFT (and the corpus boot sector).</summary>
    private string CorpusWith(string sub, byte[]? mft)
        => W(sub, File.ReadAllBytes(Corpus("many_i30.bin")), mft, File.ReadAllBytes(Corpus("boot.bin")));

    private static void AssertGateFailed(ParseResult r, List<TimelineEvent> ev, string reasonPart)
    {
        var slack = Of(ev, "slack");
        Assert.NotEmpty(slack);
        Assert.All(slack, e => { Assert.Equal("unclassified", F(e, "SlackClass")); Assert.Equal(Confidence.Low, e.Confidence); Assert.DoesNotContain("șters", e.Summary); });
        var s = Summary(ev);
        Assert.Equal("failed", F(s, "MftGate"));
        Assert.Equal("false", F(s, "MftUsed"));
        Assert.Contains(reasonPart, F(s, "MftGateReason"));
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Partial && g.Reason.Contains(reasonPart));
        Assert.Empty(Of(ev, "allocated_root"));   // the root comes from the same $MFT: not trusted either
    }

    [Fact] // P1
    public void P1_a_later_mft_where_the_referenced_records_are_free_does_not_classify_anything()
    {
        var mft = File.ReadAllBytes(Corpus("mft.bin"));
        for (int n = 45; n <= 104; n++) mft[n * 1024 + 22] &= 0xFE;   // flags: not in use (outside the fixup positions)
        var (r, ev) = Run(CorpusWith("p1", mft));
        Assert.Equal(34, Of(ev, "slack").Count);
        AssertGateFailed(r, ev, "0 din 39 intrări alocate se regăsesc vii în $MFT-ul alăturat");
        Assert.Contains("nu sunt din același volum sau moment", Summary(ev).Fields["MftGateReason"]);
    }

    [Fact] // P1b
    public void P1b_the_mft_of_an_unrelated_volume_does_not_classify_anything()
    {
        var parts = new List<(int, byte[])> { (0, FileRecord(1, Ref(5, 5), "$MFT", T, T)), (44, DirRecord(1, Ref(5, 5), "other_dir", T)) };
        for (int n = 45; n <= 104; n++) parts.Add((n, FileRecord(2, Ref(5, 5), $"other_{n}.dll", T, T)));
        var (r, ev) = Run(CorpusWith("p1b", Mft([.. parts])));
        AssertGateFailed(r, ev, "intrări alocate se regăsesc vii");
    }

    [Fact]
    public void Gate_fails_when_the_parent_directory_is_not_a_live_directory_with_the_same_sequence()
    {
        var mft = File.ReadAllBytes(Corpus("mft.bin"));
        // record 44 (\many): sequence at +16 is outside the fixup positions; make it another sequence
        mft[44 * 1024 + 16] = 9;
        var (r, ev) = Run(CorpusWith("gate_parent", mft));
        AssertGateFailed(r, ev, "nu este un director viu cu aceeași secvență");
    }

    [Fact]
    public void Gate_fails_when_the_allocated_keys_have_different_parents()
    {
        var blk = Block(0, [Entry(Id(60, 1), "a.txt", T, parent: Id(77, 1)), Entry(Id(61, 1), "b.txt", T, parent: Id(78, 1))], Entry(Id(62, 1), "c.txt", T, parent: Id(77, 1)));
        var mft = Mft((77, DirRecord(1, Ref(5, 5), "d", T)), (78, DirRecord(1, Ref(5, 5), "e", T)), (60, FileRecord(1, Id(77, 1), "a.txt", T, T)), (61, FileRecord(1, Id(78, 1), "b.txt", T, T)));
        var (r, ev) = Run(W("gate_two_parents", blk, mft, BootSector()));
        AssertGateFailed(r, ev, "părinți diferiți");
    }

    /// <summary>60 allocated entries (two blocks) of directory 77 and an $MFT that knows them, except those whose numbers are in <paramref name="missing"/>.</summary>
    private (string Path, ParseResult R, List<TimelineEvent> Ev) Sixty(string sub, int[] missing, params (ulong Number, ushort Seq, string Name, ulong Parent)[] slack)
    {
        var dir = Id(77, 1);
        byte[] Blk(int b) => Block(b, Enumerable.Range(0, 30).Select(i => Entry(Id((ulong)(100 + b * 30 + i), 1), $"f{b * 30 + i:D3}.txt", T, parent: dir)).ToArray(),
                                   b == 1 ? Concat(slack.Select(s => Entry(Id(s.Number, s.Seq), s.Name, T, parent: s.Parent)).ToArray()) : null);
        var recs = new List<(int, byte[])> { (77, DirRecord(1, Ref(5, 5), "Docs", T)) };
        for (int i = 0; i < 60; i++) if (!missing.Contains(100 + i)) recs.Add((100 + i, FileRecord(1, dir, $"f{i:D3}.txt", T, T)));
        recs.Add((300, FileRecord(4, dir, "kept_here.txt", T, T)));                    // live, same parent, same name
        recs.Add((301, FileRecord(6, Ref(5, 5), "moved_away.txt", T, T)));              // live, another parent (the root)
        recs.Add((302, FileRecord(2, dir, "now_called.txt", T, T)));                    // live, same parent, another name
        var p = W(sub, Concat(Blk(0), Blk(1)), Mft([.. recs]), BootSector());
        var (r, ev) = Run(p);
        return (p, r, ev);
    }

    [Fact]
    public void Gate_tolerates_up_to_two_percent_missing_allocated_entries_with_a_gap_and_still_classifies()
    {
        var (_, r, ev) = Sixty("tol1", [105], (300, 4, "kept_here.txt", Id(77, 1)));
        var s = Summary(ev);
        Assert.Equal("passed", F(s, "MftGate"));
        Assert.Equal("live_copy", F(Assert.Single(Of(ev, "slack")), "SlackClass"));
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Partial && g.Reason.Contains("1 din 60"));
        var (_, r2, ev2) = Sixty("tol2", [105, 106], (300, 4, "kept_here.txt", Id(77, 1)));
        Assert.Equal("failed", F(Summary(ev2), "MftGate"));   // 2 of 60 is 3.3 percent
        Assert.Equal("unclassified", F(Assert.Single(Of(ev2, "slack")), "SlackClass"));
        Assert.Contains("58 din 60", F(Summary(ev2), "MftGateReason"));
        _ = r2;
    }

    [Fact] // P2
    public void P2_a_file_moved_out_of_the_directory_is_moved_out_not_a_live_copy()
    {
        var (_, _, ev) = Sixty("p2", [], (301, 6, "moved_away.txt", Id(77, 1)), (300, 4, "kept_here.txt", Id(77, 1)));
        var slack = Of(ev, "slack").ToDictionary(e => F(e, "FileName"));
        Assert.Equal(2, slack.Count);
        Assert.Equal("live_copy", F(slack["kept_here.txt"], "SlackClass"));
        var m = slack["moved_away.txt"];
        Assert.Equal("moved_out", F(m, "SlackClass"));
        Assert.Equal(Confidence.Medium, m.Confidence);
        Assert.Equal(Classification.Candidate, m.Classification);
        Assert.Contains("a fost în acest director", m.Summary);
        Assert.Contains(new MftReference(Ref(5, 5)).FileId, m.Summary);   // the parent the record has now
        Assert.Equal(new MftReference(Ref(5, 5)).FileId, F(m, "MovedToParentFileId"));
        Assert.DoesNotContain("șters", m.Summary);
        Assert.Equal("1", F(Summary(ev), "SlackMovedOut"));
    }

    [Fact]
    public void A_live_record_with_another_name_in_the_same_directory_is_a_possible_earlier_name_with_low_confidence()
    {
        var (_, _, ev) = Sixty("rename", [], (302, 2, "was_called.txt", Id(77, 1)));
        var e = Assert.Single(Of(ev, "slack"));
        Assert.Equal("historical_renamed", F(e, "SlackClass"));
        Assert.Equal(Confidence.Low, e.Confidence);
        Assert.Equal(Classification.Candidate, e.Classification);
        Assert.Contains("nume anterior posibil", e.Summary);
        Assert.DoesNotContain("nume istoric", e.Summary);
    }

    [Fact] // P3
    public void P3_a_slack_entry_whose_parent_is_not_the_directory_of_the_index_is_a_foreign_parent_residue()
    {
        var (_, _, ev) = Sixty("p3", [], (300, 4, "kept_here.txt", Id(999, 9)), (310, 1, "planted.exe", Id(999, 9)));
        var slack = Of(ev, "slack");
        Assert.Equal(2, slack.Count);
        Assert.All(slack, e => { Assert.Equal("foreign_parent", F(e, "SlackClass")); Assert.Equal(Confidence.Low, e.Confidence); Assert.Equal(Classification.Candidate, e.Classification); Assert.DoesNotContain("șters", e.Summary); Assert.Contains("părinte străin", e.Summary); });
        Assert.Equal("2", F(Summary(ev), "SlackForeignParent"));
    }

    // ---- $INDEX_ROOT of the directory record (R3) ----

    private string WithRoot(string sub, byte[] rootValue, bool gateOk = true)
    {
        var dir = Id(77, 1);
        var blk = Block(0, [Entry(Id(60, 1), "from_indx.txt", T, parent: dir)]);
        var dirRec = Record(1, (ushort)(InUse | Dir), 0, Resident(0x10, Si(T, T, T, T)), Resident(0x30, Fn(Ref(5, 5), "Docs", T, T, T, T)), Resident(0x90, rootValue, "$I30"));
        var recs = new List<(int, byte[])> { (77, dirRec), (60, FileRecord(1, dir, "from_indx.txt", T, T)) };
        if (!gateOk) recs.RemoveAt(1);
        return W(sub, blk, Mft([.. recs]), BootSector());
    }

    [Fact]
    public void Index_root_entries_of_the_directory_record_are_emitted_as_allocated_root()
    {
        var dir = Id(77, 1);
        var root = IndexRootValue([Entry(Id(61, 1), "root_a.txt", T, parent: dir, flags: 1), Entry(Id(62, 1), "root_b.txt", T, parent: dir)]);
        var (r, ev) = Run(WithRoot("root_ok", root));
        var re = Of(ev, "allocated_root");
        Assert.Equal(["root_a.txt", "root_b.txt"], re.Select(e => F(e, "FileName")).OrderBy(x => x).ToArray());
        Assert.All(re, e => { Assert.Equal(Classification.Direct, e.Classification); Assert.Matches(@"^INDEX_ROOT\+0x[0-9A-F]+$", e.Locator); });
        Assert.Equal(["from_indx.txt"], Of(ev, "allocated").Select(e => F(e, "FileName")).ToArray());
        Assert.Equal(EvidenceStatus.Success, r.Status);
    }

    [Fact]
    public void Index_root_is_not_carved_for_slack_and_a_hostile_root_is_survived()
    {
        var dir = Id(77, 1);
        // an old entry after the "used" end of the root node must not be carved
        var good = IndexRootValue([Entry(Id(61, 1), "root_a.txt", T, parent: dir)], trailing: Entry(Id(63, 1), "ghost.txt", T, parent: dir));
        var (_, ev) = Run(WithRoot("root_noslack", good));
        Assert.DoesNotContain(ev, e => F(e, "FileName") == "ghost.txt");
        Assert.Equal(["root_a.txt"], Of(ev, "allocated_root").Select(e => F(e, "FileName")).ToArray());
        // an entry with length 0 inside the root ends the walk without looping
        var bad = IndexRootValue([Entry(Id(61, 1), "root_a.txt", T, parent: dir), Entry(Id(62, 1), "root_b.txt", T, parent: dir, entryLen: 0), Entry(Id(64, 1), "root_c.txt", T, parent: dir)]);
        var (r2, ev2) = RunWithTimeout(() => Run(WithRoot("root_bad", bad)));
        Assert.Equal(["root_a.txt"], Of(ev2, "allocated_root").Select(e => F(e, "FileName")).ToArray());
        Assert.Contains(r2.Gaps, g => g.Reason.Contains("$INDEX_ROOT"));
        // a root entry that belongs to another parent is not an entry of this directory
        var foreign = IndexRootValue([Entry(Id(65, 1), "root_f.txt", T, parent: Id(999, 3))]);
        var (_, ev3) = Run(WithRoot("root_foreign", foreign));
        Assert.Empty(Of(ev3, "allocated_root"));
    }

    [Fact]
    public void Without_a_mft_the_gap_says_the_index_root_was_not_read()
    {
        var p = W("noroot", Block(0, [Entry(Id(60, 1), "a.txt", T)]));
        var (r, ev) = Run(p);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("rădăcina indexului ($INDEX_ROOT) nu a fost citită: lista de nume alocate poate fi incompletă"));
        Assert.Equal("not run", F(Summary(ev), "MftGate"));
        Assert.Empty(Of(ev, "allocated_root"));
    }

    [Fact]
    public void A_header_that_claims_a_block_bigger_than_the_file_is_not_believed()
    {
        var b = Block(0, [Entry(Id(60, 1), "a.txt", T)]);
        BitConverter.GetBytes((uint)(65536 - 24)).CopyTo(b, 32);
        var (_, ev) = Run(W("claim64k", b));
        Assert.Equal("4096", F(Summary(ev), "BlockSize"));   // the assumed default, with a gap, not a 64 KiB block cut off
    }

    // ---------------- classification against a synthetic $MFT ----------------

    private (string Path, List<TimelineEvent> Ev, ParseResult R) Classified(string sub, bool withMft = true, byte[]? customMft = null)
    {
        var mft = customMft ?? Mft(
            (40, FileRecord(3, Ref(5, 5), "live.txt", T, T)),
            (41, FileRecord(7, Ref(5, 5), "other.txt", T, T)),
            (42, FileRecord(4, Ref(5, 5), "gone.txt", T, T, flags: 0)),
            (43, FileRecord(2, Ref(5, 5), "renamed_now.txt", T, T)),
            (60, FileRecord(1, Ref(5, 5), "kept.txt", T, T)));   // the allocated entry: the coherence gate needs the $MFT to know it
        var slack = Concat(
            Entry(Id(40, 3), "live.txt", T),          // live, same name
            Entry(Id(41, 2), "old_a.txt", T),         // number exists, sequence differs
            Entry(Id(42, 4), "gone.txt", T),          // record not in use
            Entry(Id(900, 1), "beyond.txt", T),       // number past the end of the $MFT
            Entry(Id(43, 2), "renamed_before.txt", T));   // exact reference is live but with another name
        var p = W(sub, Block(0, [Entry(Id(60, 1), "kept.txt", T)], slack), withMft ? mft : null, BootSector());
        var (r, ev) = Run(p);
        return (p, ev, r);
    }

    [Fact]
    public void Classification_against_mft_covers_live_copy_reused_free_renamed_and_unclassified()
    {
        var (_, ev, _) = Classified("cls");
        var slack = Of(ev, "slack").ToDictionary(e => F(e, "FileName"));
        Assert.Equal(5, slack.Count);
        Assert.Equal("live_copy", F(slack["live.txt"], "SlackClass"));
        Assert.Equal("historical_reused", F(slack["old_a.txt"], "SlackClass"));
        Assert.Equal("historical_free", F(slack["gone.txt"], "SlackClass"));
        Assert.Equal("unclassified", F(slack["beyond.txt"], "SlackClass"));
        Assert.Equal("historical_renamed", F(slack["renamed_before.txt"], "SlackClass"));
        Assert.Contains("copie a unei intrări vii", slack["live.txt"].Summary);
        Assert.Contains("înregistrare reutilizată", slack["old_a.txt"].Summary);
        Assert.Contains("înregistrare liberă", slack["gone.txt"].Summary);
        Assert.All(slack.Values, e => { Assert.Equal(Classification.Candidate, e.Classification); Assert.NotEqual(Confidence.High, e.Confidence); Assert.DoesNotContain("șters", e.Summary); });
        var s = Summary(ev);
        Assert.Equal("1", F(s, "AllocatedEntries"));
        Assert.Equal("5", F(s, "SlackEntries"));
        Assert.Equal("true", F(s, "MftUsed"));
    }

    [Fact]
    public void Without_a_mft_every_slack_entry_is_unclassified_with_a_gap()
    {
        var (_, ev, r) = Classified("nomft", withMft: false);
        var slack = Of(ev, "slack");
        Assert.Equal(5, slack.Count);
        Assert.All(slack, e => { Assert.Equal("unclassified", F(e, "SlackClass")); Assert.Contains("neclasat", e.Summary); Assert.Equal(Confidence.Low, e.Confidence); Assert.DoesNotContain("șters", e.Summary); });
        Assert.Equal("false", F(Summary(ev), "MftUsed"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("$MFT"));
    }

    [Fact] // Q9
    public void Q9_corrupt_neighbouring_mft_gives_unclassified_and_a_gap_not_an_exception()
    {
        var rnd = new Random(9);
        var junk = new byte[6000]; rnd.NextBytes(junk);
        var (_, ev, r) = Classified("q9", customMft: junk);
        Assert.NotEqual(EvidenceStatus.Failed, r.Status);
        var slack = Of(ev, "slack");
        Assert.Equal(5, slack.Count);
        Assert.All(slack, e => Assert.Equal("unclassified", F(e, "SlackClass")));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("$MFT"));
        Assert.Equal("false", F(Summary(ev), "MftUsed"));
    }

    [Fact] // Q9b
    public void Q9b_empty_neighbouring_mft_is_also_unclassified()
    {
        var (_, ev, r) = Classified("q9b", customMft: []);
        Assert.NotEqual(EvidenceStatus.Failed, r.Status);
        Assert.All(Of(ev, "slack"), e => Assert.Equal("unclassified", F(e, "SlackClass")));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("$MFT"));
    }

    [Fact]
    public void A_mft_record_with_a_torn_fixup_leaves_only_that_entry_unclassified()
    {
        var mft = Mft((40, FileRecord(3, Ref(5, 5), "live.txt", T, T)), (41, FileRecord(7, Ref(5, 5), "other.txt", T, T)), (42, FileRecord(4, Ref(5, 5), "gone.txt", T, T, flags: 0)), (60, FileRecord(1, Ref(5, 5), "kept.txt", T, T)));
        mft[41 * RecSize + 510] ^= 0xFF;   // break the fixup of record 41
        var (_, ev, _) = Classified("torn", customMft: mft);
        var slack = Of(ev, "slack").ToDictionary(e => F(e, "FileName"));
        Assert.Equal("live_copy", F(slack["live.txt"], "SlackClass"));
        Assert.Equal("unclassified", F(slack["old_a.txt"], "SlackClass"));
        Assert.Equal("historical_free", F(slack["gone.txt"], "SlackClass"));
    }

    // ---------------- hostile inputs ----------------

    private static byte[] GoodBlock(long vcn, string name = "good.txt", byte[]? slack = null) => Block(vcn, [Entry(Id(50 + (ulong)vcn, 1), name, T)], slack);

    [Theory] // Q1
    [InlineData(3, 40)]     // USA count wrong for 4096 (must be 9)
    [InlineData(9, 600)]    // USA offset beyond the first sector
    [InlineData(9, 20)]     // USA offset inside the header
    [InlineData(0, 40)]
    [InlineData(200, 40)]
    public void Q1_invalid_usa_count_or_offset_is_counted_and_the_other_blocks_are_read(int count, int offset)
    {
        var bad = Block(1, [Entry(Id(70, 1), "bad.txt", T)], usaCount: count, usaOffset: offset);
        var (r, ev) = Run(W("q1", Concat(GoodBlock(0), bad, GoodBlock(2, "good2.txt"))));
        Assert.Equal(["good.txt", "good2.txt"], Of(ev, "allocated").Select(e => F(e, "FileName")).OrderBy(x => x).ToArray());
        Assert.Equal(1, r.MalformedRecords);
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Equal("1", F(Summary(ev), "MalformedBlocks"));
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Partial);
    }

    [Fact] // Q2
    public void Q2_fixup_mismatch_is_counted_and_the_block_is_not_read()
    {
        var bad = Block(1, [Entry(Id(70, 1), "bad.txt", T)]);
        bad[3 * 512 - 2] ^= 0xFF;
        var (r, ev) = Run(W("q2", Concat(GoodBlock(0), bad, GoodBlock(2, "good2.txt"))));
        Assert.DoesNotContain(ev, e => F(e, "FileName") == "bad.txt");
        Assert.Equal(2, Of(ev, "allocated").Count);
        Assert.Equal(1, r.MalformedRecords);
        Assert.Equal("1", F(Summary(ev), "FixupErrors"));
        Assert.Equal(EvidenceStatus.Partial, r.Status);
    }

    [Fact] // Q2b: a fixup mismatch in a sector that holds only slack must not let the slack bytes through
    public void Q2b_fixup_mismatch_in_the_slack_sector_hides_the_slack_entries_too()
    {
        var bad = Block(0, [Entry(Id(70, 1), "bad.txt", T)], Entry(Id(71, 1), "slack_in_bad.txt", T));
        bad[8 * 512 - 2] ^= 0xFF;
        var (_, ev) = Run(W("q2b", bad));
        Assert.Empty(Of(ev, "slack"));
        Assert.Empty(Of(ev, "allocated"));
    }

    [Theory] // Q3
    [InlineData(0u, 0u, 0u)]
    [InlineData(40u, 5000u, 4072u)]      // used beyond the block
    [InlineData(40u, 2000u, 1000u)]      // used > allocated
    [InlineData(40u, 1000u, 4073u)]      // allocated beyond the block
    [InlineData(5000u, 5000u, 5000u)]    // first entry outside
    [InlineData(41u, 1000u, 4072u)]      // first entry unaligned
    [InlineData(8u, 1000u, 4072u)]       // first entry inside the USA
    [InlineData(40u, 0xFFFFFFF0u, 0xFFFFFFFFu)]
    public void Q3_node_header_outside_the_block_is_counted_and_not_read(uint first, uint used, uint alloc)
    {
        var bad = Block(1, [Entry(Id(70, 1), "bad.txt", T)], Entry(Id(71, 1), "slack_bad.txt", T), firstEntry: first, used: used, allocated: alloc);
        var (r, ev) = Run(W("q3", Concat(GoodBlock(0), bad)));
        Assert.Equal(["good.txt"], Of(ev, "allocated").Select(e => F(e, "FileName")).ToArray());
        Assert.DoesNotContain(ev, e => F(e, "FileName") is "bad.txt" or "slack_bad.txt");
        Assert.Equal(1, r.MalformedRecords);
        Assert.Equal("1", F(Summary(ev), "MalformedBlocks"));
    }

    private static TResult RunWithTimeout<TResult>(Func<TResult> f)
    {
        var t = Task.Run(f);
        Assert.True(t.Wait(TimeSpan.FromSeconds(30)), "the parser did not terminate (endless loop)");
        return t.Result;
    }

    [Theory] // Q4
    [InlineData(0)]          // length 0: would loop forever
    [InlineData(5)]          // unaligned
    [InlineData(12)]         // below the 16-byte header
    [InlineData(0xFFF8)]     // past the block
    [InlineData(4000)]       // past "used"
    public void Q4_entry_length_zero_unaligned_or_beyond_the_block_ends_the_block_without_looping(int len)
    {
        var bad = Entry(Id(70, 1), "bad_len.txt", T, entryLen: len);
        var blk = Block(0, [Entry(Id(60, 1), "before.txt", T), bad, Entry(Id(61, 1), "after.txt", T)]);
        var (r, ev) = RunWithTimeout(() => Run(W("q4_" + len, blk)));
        Assert.Contains(ev, e => F(e, "FileName") == "before.txt");
        Assert.DoesNotContain(ev, e => F(e, "FileName") == "after.txt");
        Assert.DoesNotContain(Of(ev, "allocated"), e => F(e, "FileName") == "bad_len.txt");
        Assert.Equal(1, r.MalformedRecords);
    }

    [Theory] // Q5
    [InlineData(40, -1)]     // key shorter than the 66-byte $FILE_NAME header
    [InlineData(0, -1)]      // no key on an entry that is not the last one
    [InlineData(-1, 200)]    // name length (200 chars) over the key
    [InlineData(-1, 255)]
    public void Q5_short_key_or_name_longer_than_the_key_is_not_an_entry(int keyLen, int nameLen)
    {
        var bad = Entry(Id(70, 1), "bad_key.txt", T, keyLen: keyLen < 0 ? null : keyLen, nameLenField: nameLen < 0 ? null : nameLen);
        var blk = Block(0, [Entry(Id(60, 1), "before.txt", T), bad]);
        var (r, ev) = Run(W("q5", blk));
        Assert.Equal(["before.txt"], Of(ev, "allocated").Select(e => F(e, "FileName")).ToArray());
        Assert.Equal(1, r.MalformedRecords);
    }

    [Fact] // Q6
    public void Q6_random_slack_gives_at_most_one_false_entry_in_a_thousand_positions()
    {
        var rnd = new Random(20261113);
        var blocks = new List<byte[]>();
        int positions = 0;
        for (int i = 0; i < 4; i++)
        {
            var probe = Block(i, [Entry(Id(60 + (ulong)i, 1), $"f{i}.txt", T)]);
            int used = (int)(BitConverter.ToUInt32(probe, 28) + 24);
            var junk = new byte[4096 - used]; rnd.NextBytes(junk);   // random garbage over the whole slack, from "used" to the end of the block
            positions += junk.Length / 8;
            blocks.Add(Block(i, [Entry(Id(60 + (ulong)i, 1), $"f{i}.txt", T)], junk));
        }
        Assert.True(positions >= 1000, $"{positions} positions");
        var (_, ev) = Run(W("q6", Concat([.. blocks])));
        Assert.True(Of(ev, "slack").Count <= 1, $"{Of(ev, "slack").Count} false slack entries");
        Assert.Equal(4, Of(ev, "allocated").Count);
    }

    [Fact] // Q6b
    public void Q6b_all_zero_all_ff_and_repeated_patterns_in_slack_are_not_entries()
    {
        var zeros = new byte[1500];
        var ff = Enumerable.Repeat((byte)0xFF, 1500).ToArray();
        var pat = Enumerable.Range(0, 1500).Select(i => (byte)(i % 7 == 0 ? 0x42 : 0x30)).ToArray();
        var (_, ev) = Run(W("q6b", Concat(Block(0, [Entry(Id(60, 1), "a.txt", T)], zeros), Block(1, [Entry(Id(61, 1), "b.txt", T)], ff), Block(2, [Entry(Id(62, 1), "c.txt", T)], pat))));
        Assert.Empty(Of(ev, "slack"));
    }

    [Fact] // Q6c: each single validation rule rejects an otherwise perfect slack entry
    public void Q6c_every_slack_validation_rule_rejects_an_entry_that_otherwise_looks_right()
    {
        long bad = 5;   // an implausible FILETIME (year 1601)
        var candidates = new (string Why, byte[] Entry)[]
        {
            ("sequence 0", Entry(Id(63, 0), "seq0.txt", T)),
            ("namespace 4", Entry(Id(63, 1), "ns4.txt", T, ns: 4)),
            ("one plausible time", Entry(Id(63, 1), "onetime.txt", T, time2: bad)),
            ("key length not matching the name", Entry(Id(63, 1), "keylen.txt", T, keyLen: 66 + 2 * "keylen.txt".Length + 4)),
            ("entry length not matching the key", Entry(Id(63, 1), "entlen.txt", T, entryLen: ((16 + 66 + 2 * "entlen.txt".Length + 7) & ~7) + 8)),
            ("empty name", Entry(Id(63, 1), "", T)),
            ("lone surrogate", Entry(Id(63, 1), "x", T, nameBytes: [0x00, 0xD8, 0x41, 0x00])),
            ("last-entry flag", Entry(Id(63, 1), "lastflag.txt", T, flags: 2)),
        };
        foreach (var (why, entry) in candidates)
        {
            var (_, ev) = Run(W("q6c", Block(0, [Entry(Id(60, 1), "ok.txt", T)], entry)));
            Assert.True(Of(ev, "slack").Count == 0, why);
        }
        // and the same entry with every rule satisfied is found (the harness itself works)
        var (_, ok) = Run(W("q6c_ok", Block(0, [Entry(Id(60, 1), "ok.txt", T)], Entry(Id(63, 1), "found.txt", T))));
        Assert.Equal(["found.txt"], Of(ok, "slack").Select(e => F(e, "FileName")).ToArray());
    }

    [Fact] // Q7
    public void Q7_hostile_names_are_verbatim_in_fields_and_neutralised_in_the_summary()
    {
        string ctl = "a\u0001b\u001b[31mc.txt", rlo = "x\u202Eexe.txt", csv = "=cmd|' /c calc'!A1.txt", nl = "line1\nline2.txt";
        var blk = Block(0, [Entry(Id(60, 1), ctl, T), Entry(Id(61, 1), csv, T), Entry(Id(62, 1), nl, T)],
            Concat(Entry(Id(63, 1), rlo, T), Entry(Id(64, 1), "=2+5.txt", T)));
        var (_, ev) = Run(W("q7", blk));
        var byName = ev.Where(e => F(e, "Category") is "allocated" or "slack").ToDictionary(e => F(e, "FileName"));
        Assert.Contains(ctl, byName.Keys);
        Assert.Contains(rlo, byName.Keys);
        Assert.Contains(csv, byName.Keys);
        Assert.Contains(nl, byName.Keys);
        Assert.Contains("=2+5.txt", byName.Keys);   // a formula-like but control-free slack name is kept verbatim
        Assert.DoesNotContain(byName.Values, e => e.Summary.Any(char.IsControl));
        Assert.False(byName[rlo].Summary.Contains((char)0x202E), "the RLO character must not reach the summary");
        Assert.Contains(MftParser.SafeText(rlo), byName[rlo].Summary);
        Assert.Contains(MftParser.SafeText(ctl), byName[ctl].Summary);
        Assert.DoesNotContain(byName.Values, e => e.Summary.Length > 0 && "=+-@".Contains(e.Summary[0]));
    }

    [Fact] // Q7b: a control character in a SLACK name marks the candidate as garbage, not as a record
    public void Q7b_slack_name_with_a_control_character_is_rejected()
    {
        var blk = Block(0, [Entry(Id(60, 1), "ok.txt", T)], Entry(Id(63, 1), "bad\u0007name.txt", T));
        var (_, ev) = Run(W("q7b", blk));
        Assert.Empty(Of(ev, "slack"));
    }

    [Fact] // Q8
    public void Q8_file_cut_in_the_middle_of_a_block_is_partial_and_keeps_the_complete_blocks()
    {
        var all = Concat(GoodBlock(0), GoodBlock(1, "good1.txt"), GoodBlock(2, "good2.txt"));
        var cut = all[..(4096 + 1500)];
        var (r, ev) = Run(W("q8", cut));
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Equal(["good.txt"], Of(ev, "allocated").Select(e => F(e, "FileName")).ToArray());
        Assert.Equal("true", F(Summary(ev), "TruncatedFile"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("mijlocul"));
        Assert.True(r.MalformedRecords >= 1);
    }

    [Fact] // Q10
    public void Q10_thousands_of_blocks_hit_the_event_cap_with_a_gap_and_exact_counts()
    {
        const int blocks = 1500, perBlock = 35;
        using var ms = new MemoryStream();
        for (int b = 0; b < blocks; b++)
        {
            var entries = Enumerable.Range(0, perBlock).Select(i => Entry(Id((ulong)(100 + b * perBlock + i), 1), $"f{b:D4}_{i:D2}.txt", T)).ToArray();
            ms.Write(Block(b, entries));
        }
        var path = W("q10", ms.ToArray());
        var (r, ev) = Run(path);
        var s = Summary(ev);
        int emitted = ev.Count(e => F(e, "Category") is "allocated" or "slack");
        Assert.Equal(I30Parser.MaxEvents, emitted);
        Assert.Equal((blocks * perBlock).ToString(CultureInfo.InvariantCulture), F(s, "AllocatedEntries"));
        Assert.Equal((blocks * perBlock - I30Parser.MaxEvents).ToString(CultureInfo.InvariantCulture), F(s, "EntriesNotEmitted"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("Plafon") && g.Status == EvidenceStatus.Partial);
    }

    [Fact]
    public void Block_cap_stops_reading_with_a_gap()
    {
        var path = W("blockcap", Concat(GoodBlock(0), GoodBlock(1, "g1.txt"), GoodBlock(2, "g2.txt")));
        var (r, ev) = Run(path, new I30Parser(maxBlocks: 2));
        Assert.Equal(2, Of(ev, "allocated").Count);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("blocuri") && g.Status == EvidenceStatus.Partial);
        Assert.Equal("1", F(Summary(ev), "BlocksNotRead"));
    }

    // ---------------- block size, fingerprint, descriptor ----------------

    [Fact]
    public void Block_size_is_deduced_from_the_blocks_when_there_is_no_boot_sector()
    {
        var p = W("b8192", Concat(Block(0, [Entry(Id(60, 1), "a.txt", T)], blockSize: 8192), Block(1, [Entry(Id(61, 1), "b.txt", T)], blockSize: 8192)));
        var (r, ev) = Run(p);
        Assert.Equal(2, Of(ev, "allocated").Count);
        Assert.Equal("8192", F(Summary(ev), "BlockSize"));
        Assert.Equal(EvidenceStatus.Success, r.Status);
    }

    [Fact]
    public void A_boot_sector_of_another_volume_does_not_override_the_block_headers()
    {
        var boot = BootSector(); boot[0x44] = 2;   // 2 clusters = 8192, but the blocks are 4096
        var p = W("bootmismatch", Concat(GoodBlock(0), GoodBlock(1, "g1.txt")), boot: boot);
        var (r, ev) = Run(p);
        Assert.Equal(2, Of(ev, "allocated").Count);
        Assert.Equal("4096", F(Summary(ev), "BlockSize"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("boot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fingerprint_is_the_indx_signature_and_the_parser_accepts_its_source_type_and_names()
    {
        var p = W("fp", GoodBlock(0));
        Assert.Equal(I30Parser.FingerprintName, EvidenceFingerprint.Detect(p));
        var parser = new I30Parser();
        Assert.Equal("ntfs_i30", I30Parser.SourceTypeName);
        foreach (var n in new[] { "$I30", "dir.i30", "many$I30$INDEX_ALLOCATION.bin", "x$INDEX_ALLOCATION" })
            Assert.True(parser.CanParse(new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = "x", StoredPath = Path.Combine(_dir, n) }), n);
        Assert.False(parser.CanParse(new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = "x", StoredPath = Path.Combine(_dir, "readme.txt") }));
        Assert.Contains(I30Parser.FingerprintName, EvidenceFingerprint.Expected(new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = I30Parser.SourceTypeName, StoredPath = p })!);
    }

    [Fact]
    public void Descriptor_is_validated_and_the_capabilities_entry_exists()
    {
        var d = new I30Parser().Descriptor;
        Assert.Equal("I30Parser", d.ParserId);
        Assert.NotNull(d.Capabilities);
        Assert.Contains(d.Limitations, l => l.Contains("slack"));
    }
}
