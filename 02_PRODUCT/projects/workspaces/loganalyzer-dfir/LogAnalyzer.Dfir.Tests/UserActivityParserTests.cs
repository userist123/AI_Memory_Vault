using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.Synthetic.ShellItemBuilder;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// ShellBags (UsrClass.dat and NTUSER), RecentDocs and Office MRU (E7 slice 1) on synthetic hives written with <see cref="RegfBuilder"/>,
/// plus the hostile-hive probes of the threat model (depth, cycles, node cap, bad MRUListEx, damaged keys, bad Office values).
/// </summary>
public sealed class UserActivityParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_uact_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 9, 2, 9, 30, 15, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 9, 3, 14, 45, 30, DateTimeKind.Utc);
    private static readonly DateTime T3 = new(2026, 9, 4, 16, 5, 45, DateTimeKind.Utc);
    private static readonly DateTime Shell = new(2026, 8, 20, 10, 0, 2);   // a shell-item DOS time, deliberately unlike every key time
    private const string BagRoot = @"Local Settings\Software\Microsoft\Windows\Shell\BagMRU";
    private const uint End = 0xFFFFFFFF;

    public UserActivityParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path, string type) =>
        new() { EvidenceId = "EV-U", CaseId = "C", Source = type, SourceType = type, StoredPath = path, OriginalPath = $@"C:\Users\test\{Path.GetFileName(path)}" };

    private static RegfBuilder.Key Child(RegfBuilder.Key parent, string name, DateTime t)
    {
        var k = new RegfBuilder.Key(name, t);
        parent.Children.Add(k);
        return k;
    }

    private static RegfBuilder.Key Chain(RegfBuilder.Key root, string path, DateTime t)
    {
        var k = root;
        foreach (var part in path.Split('\\')) k = k.Children.Find(c => c.Name == part) ?? Child(k, part, t);
        return k;
    }

    private string Save(RegfBuilder.Key root, string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, RegfBuilder.Build(root, name));
        return p;
    }

    private static (ParseResult R, ListSink S) RunUsrClass(string path)
    {
        var sink = new ListSink();
        return (new ShellBagsParser().Parse(Item(path, "usrclass_hive"), path, sink, default), sink);
    }

    private static (ParseResult R, ListSink S) RunNtUser(string path)
    {
        var sink = new ListSink();
        return (new UserHiveParser().Parse(Item(path, "ntuser_hive"), path, sink, default), sink);
    }

    /// <summary>Start of the nk cell payload (the "nk" signature) of the nth key with this name, in file order; -1 if none.</summary>
    private static int FindNk(byte[] hive, string name, int nth = 0)
    {
        for (int i = 0x1000; i + 0x4C + name.Length < hive.Length; i += 8)
        {
            if (hive[i + 4] != (byte)'n' || hive[i + 5] != (byte)'k') continue;
            int len = BinaryPrimitives.ReadUInt16LittleEndian(hive.AsSpan(i + 4 + 0x48));
            if (len != name.Length || Encoding.ASCII.GetString(hive, i + 4 + 0x4C, len) != name) continue;
            if (nth-- == 0) return i + 4;
        }
        return -1;
    }

    // ---- ShellBags: paths and time semantics ----

    private RegfBuilder.Key SampleBags()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        bag.Value("0", RegfBuilder.Binary, List(Root(ThisPc)));
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var pc = Child(bag, "0", T1);
        pc.Value("0", RegfBuilder.Binary, List(Volume(@"C:\")));
        pc.Value("1", RegfBuilder.Binary, List(Volume(@"E:\")));
        pc.Value("MRUListEx", RegfBuilder.Binary, Mru(1, 0, End));           // E: is the newest entry
        var c = Child(pc, "0", T2);
        c.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "Users", 0, Shell)));
        c.Value("1", RegfBuilder.Binary, List(FileEntry(0x31, "Temp", 0, Shell)));
        c.Value("MRUListEx", RegfBuilder.Binary, Mru(0, 1, End));
        var users = Child(c, "0", T3);
        users.Value("0", RegfBuilder.Binary, List(FileEntry(0x35, "TEST~1", 0, Shell, new Ext(7, "test", Cre, Acc, 4242, 3))));
        users.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        return root;
    }

    private static readonly DateTime Cre = new(2025, 1, 2, 3, 4, 6);
    private static readonly DateTime Acc = new(2025, 6, 7, 8, 9, 10);

    [Fact]
    public void ShellBags_walks_the_tree_and_builds_full_paths()
    {
        var p = Save(SampleBags(), "UsrClass.dat");
        var (r, s) = RunUsrClass(p);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        var paths = s.Events.Select(e => e.Fields["BagPath"]).ToHashSet();
        Assert.Contains("Acest PC", paths);
        Assert.Contains(@"Acest PC\C:\", paths);
        Assert.Contains(@"Acest PC\E:\", paths);
        Assert.Contains(@"Acest PC\C:\Users", paths);
        Assert.Contains(@"Acest PC\C:\Users\test", paths);
        Assert.Contains(@"Acest PC\C:\Temp", paths);
        Assert.Equal(6, s.Events.Count);
        Assert.Equal(6, r.Records);

        var leaf = s.Events.Single(e => e.Fields["BagPath"] == @"Acest PC\C:\Users\test");
        Assert.Equal(@"C:\Users\test", leaf.Path);                            // the root name is not part of the path field
        Assert.Equal("4242", leaf.Fields["MftEntry"]);
        Assert.Equal("3", leaf.Fields["MftSequence"]);
        Assert.Equal("UsrClass", leaf.Fields["Hive"]);
        Assert.Contains("test", leaf.Summary);                                // profile name from the original path
    }

    [Fact]
    public void ShellBags_event_time_is_the_bagmru_key_time_and_the_shell_item_times_are_details_only()
    {
        var p = Save(SampleBags(), "UsrClass.dat");
        var (_, s) = RunUsrClass(p);

        var pc = s.Events.Single(e => e.Fields["BagPath"] == "Acest PC");
        Assert.Equal(new DateTimeOffset(T1), pc.Time.Utc);                    // key "0" below BagMRU
        Assert.Contains("last time Explorer updated this folder's entry", pc.TimeSemantics);
        Assert.Contains("not the first time it was opened", pc.TimeSemantics);
        Assert.Contains("not proof that a file was read", pc.TimeSemantics);

        var users = s.Events.Single(e => e.Fields["BagPath"] == @"Acest PC\C:\Users");
        Assert.Equal(new DateTimeOffset(T3), users.Time.Utc);
        Assert.Equal("2026-08-20T10:00:02", users.Fields["ShellModifiedLocal"]);
        Assert.NotEqual(Shell, users.Time.Utc!.Value.DateTime);
        Assert.Contains("fus orar", users.Fields["ShellTimesNote"]);

        var leaf = s.Events.Single(e => e.Fields["BagPath"] == @"Acest PC\C:\Users\test");
        Assert.Equal("2025-01-02T03:04:06", leaf.Fields["ShellCreatedLocal"]);
        Assert.Equal("2025-06-07T08:09:10", leaf.Fields["ShellAccessedLocal"]);
    }

    [Fact]
    public void ShellBags_only_the_newest_leaf_gets_the_parent_key_time_the_others_are_undated()
    {
        var p = Save(SampleBags(), "UsrClass.dat");
        var (_, s) = RunUsrClass(p);

        var e = s.Events.Single(x => x.Fields["BagPath"] == @"Acest PC\E:\");   // first in MRUListEx [1,0], no subkey
        Assert.Equal(new DateTimeOffset(T1), e.Time.Utc);
        Assert.Contains("parent BagMRU key", e.TimeSemantics);

        var temp = s.Events.Single(x => x.Fields["BagPath"] == @"Acest PC\C:\Temp");  // second in MRUListEx [0,1], no subkey
        Assert.False(temp.Time.IsKnown);
        Assert.Contains("no time of its own", temp.TimeSemantics);
    }

    // ---- ShellBags: hostile hives ----

    [Fact]
    public void ShellBags_depth_limit_stops_a_very_deep_chain_and_reports_it()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var node = Chain(root, BagRoot, T0);
        for (int i = 0; i < 80; i++)
        {
            node.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, $"d{i}", 0, Shell)));
            node = Child(node, "0", T1);
        }
        var (r, s) = RunUsrClass(Save(root, "UsrClass.dat"));

        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.InRange(s.Events.Count, 40, 80);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("adâncimea"));
    }

    [Fact]
    public void ShellBags_key_whose_subkey_list_loops_back_is_visited_once_and_reported()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        bag.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "a", 0, Shell)));
        var b = Child(bag, "0", T1);
        b.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "b", 0, Shell)));
        var c = Child(b, "0", T2);
        c.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "c", 0, Shell)));
        Child(c, "0", T3);                                                   // gives C a subkey list; patched below to point back
        var hive = RegfBuilder.Build(root, "UsrClass.dat");

        // The deepest key named "0" gets the subkey list of BagMRU, whose only subkey is the first "0": a cycle.
        int bagNk = FindNk(hive, "BagMRU");
        int listOfBag = BinaryPrimitives.ReadInt32LittleEndian(hive.AsSpan(bagNk + 0x1C));
        int deepest = FindNk(hive, "0", 2);                                   // keys named "0": b, c, d (file order); d is the leaf
        BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(deepest + 0x14), 1);
        BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(deepest + 0x1C), listOfBag);
        var p = Path.Combine(_dir, "UsrClass.dat");
        File.WriteAllBytes(p, hive);

        var (r, s) = RunUsrClass(p);

        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("circulară"));
        Assert.InRange(s.Events.Count, 1, 6);                                 // finite, not a loop to the depth limit
    }

    [Fact]
    public void ShellBags_node_cap_bounds_a_hive_with_far_too_many_keys()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        int n = 20_000 + 50   /* UserActivity.MaxBagNodes + 50 */;
        for (int i = 0; i < n; i++)
        {
            bag.Value(i.ToString(), RegfBuilder.Binary, List(FileEntry(0x31, "d", 0, Shell)));
            Child(bag, i.ToString(), T1);
        }
        var (r, s) = RunUsrClass(Save(root, "UsrClass.dat"));

        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("limita parserului"));
        Assert.Equal(n, s.Events.Count);                                      // every entry of the first node is read; the extra nodes are not walked
    }

    [Fact]
    public void ShellBags_name_budget_bounds_work_when_few_keys_list_many_subkeys()
    {
        // Few BagMRU keys, each listing many subkeys: the read count stays small but each read lists all the names, so the work is bounded by names.
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        for (int i = 0; i < 6; i++)
        {
            bag.Value(i.ToString(), RegfBuilder.Binary, List(FileEntry(0x31, "d" + i, 0, Shell)));
            var k = Child(bag, i.ToString(), T1);
            for (int j = 0; j < 400; j++) Child(k, "s" + j, T1);
        }
        var p = Save(root, "UsrClass.dat");
        const long budget = 1000;                                              // far below 6 x 400 = 2 400 names; the production value is UserActivity.MaxBagNameReads
        var sink = new ListSink();
        var result = new ParseResult { EvidenceId = "EV-U", Parser = "t", ParserVersion = "1" };
        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            UserActivity.ShellBags(new RawRegistry(fs), "UsrClass", BagRoot, Item(p, "usrclass_hive"), sink, result, default, budget);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        }
        Assert.Contains(result.Gaps, g => g.Reason.Contains("nume de subchei") && g.Reason.Contains("limita"));
        Assert.Equal(EvidenceStatus.Partial, result.Gaps.First(g => g.Reason.Contains("nume de subchei")).Status);

        // The same hive under the production budget is read fully and reports no cap gap.
        var (r2, _) = RunUsrClass(p);
        Assert.DoesNotContain(r2.Gaps, g => g.Reason.Contains("limita"));
    }

    [Fact]
    public void ShellBags_bad_shell_items_and_bad_mrulistex_do_not_hide_the_good_entries()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        bag.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "bun", 0, Shell)));
        bag.Value("1", RegfBuilder.Binary, new byte[] { 0, 0 });                                      // size 0: no item at all
        var cut = Wrap(0x31, new byte[6]); BinaryPrimitives.WriteUInt16LittleEndian(cut, 0x7000);    // larger than the value
        bag.Value("2", RegfBuilder.Binary, cut);
        bag.Value("3", RegfBuilder.Binary, List(Wrap(0x99, [9, 9])));                                 // unknown type
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(0, 0, 7, 1, 2, 3));                            // duplicate, missing index, no terminator
        var (r, s) = RunUsrClass(Save(root, "UsrClass.dat"));

        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Single(s.Events, e => e.Fields["BagPath"] == "bun");
        var unknown = Assert.Single(s.Events, e => e.Fields["BagPath"] == "element necunoscut (tip 0x99)");
        Assert.Equal("0x99", unknown.Fields["ShellItemType"]);
        Assert.True(unknown.Fields["ItemHex"].Length > 0);
        Assert.Single(s.Events, e => e.Fields.TryGetValue("ItemTruncated", out var t) && t == "true");
        Assert.Contains(r.Gaps, g => g.Reason.Contains("MRUListEx"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("niciun element shell"));
    }

    [Fact]
    public void ShellBags_damaged_subkey_is_reported_and_its_siblings_are_still_read()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        bag.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "stricat", 0, Shell)));
        bag.Value("1", RegfBuilder.Binary, List(FileEntry(0x31, "intact", 0, Shell)));
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(0, 1, End));
        var branch = Child(bag, "0", T1);
        Child(branch, "0", T1);                                              // its parent can no longer be read once this key is damaged
        Child(bag, "1", T2);
        var hive = RegfBuilder.Build(root, "UsrClass.dat");
        int damaged = FindNk(hive, "0", 1);                                   // the grandchild
        hive[damaged] = (byte)'x';                                            // "nk" signature destroyed
        var p = Path.Combine(_dir, "UsrClass.dat");
        File.WriteAllBytes(p, hive);

        var (r, s) = RunUsrClass(p);

        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("nu au putut fi citite"));
        Assert.Contains(s.Events, e => e.Fields["BagPath"] == "intact" && e.Time.Utc == new DateTimeOffset(T2));
    }

    [Fact]
    public void ShellBags_shared_subkey_lists_finish_quickly_and_are_reported()
    {
        const int n = 200;
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T0);
        for (int i = 0; i < n; i++)
        {
            bag.Value(i.ToString(), RegfBuilder.Binary, List(FileEntry(0x31, "d", 0, Shell)));
            Child(bag, i.ToString(), T1);
        }
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(Enumerable.Range(0, n).Select(i => (uint)i).Append(End).ToArray()));
        var hive = RegfBuilder.Build(root, "UsrClass.dat");
        int listOfBag = BinaryPrimitives.ReadInt32LittleEndian(hive.AsSpan(FindNk(hive, "BagMRU") + 0x1C));
        for (int i = 0; i < n; i++)                                           // every child shares BagMRU's list of n children
        {
            int nk = FindNk(hive, i.ToString());
            BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(nk + 0x14), n);
            BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(nk + 0x1C), listOfBag);
        }
        var p = Path.Combine(_dir, "UsrClass.dat");
        File.WriteAllBytes(p, hive);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (r, s) = RunUsrClass(p);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("circulară"));
        Assert.Equal(n, s.Events.Count);
    }

    [Fact]
    public void Missing_mrulistex_is_reported_and_no_entry_is_taken_as_the_newest()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, BagRoot, T1);
        bag.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "unu", 0, Shell)));
        bag.Value("1", RegfBuilder.Binary, List(FileEntry(0x31, "doi", 0, Shell)));
        var (r, s) = RunUsrClass(Save(root, "UsrClass.dat"));
        Assert.Equal(2, s.Events.Count);
        Assert.All(s.Events, e => Assert.False(e.Time.IsKnown));
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("MRUListEx"));

        var root2 = new RegfBuilder.Key("ROOT", T0);
        var rd = Chain(root2, @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", T1);
        rd.Value("0", RegfBuilder.Binary, RecentValue("unu.docx"));
        rd.Value("1", RegfBuilder.Binary, RecentValue("doi.docx"));
        var (r2, s2) = RunNtUser(Save(root2, "NTUSER.DAT"));
        var docs = s2.Events.Where(e => e.Source == "RecentDocs").ToList();
        Assert.Equal(2, docs.Count);
        Assert.All(docs, e => Assert.False(e.Time.IsKnown));
        Assert.Contains(r2.Gaps, g => g.Reason.Contains("MRUListEx"));
    }

    [Fact]
    public void ShellBags_hive_without_bagmru_reports_the_gap_not_an_empty_success()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        Chain(root, @"Local Settings\Software", T0);
        var (r, s) = RunUsrClass(Save(root, "UsrClass.dat"));
        Assert.Empty(s.Events);
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.NotAvailable);
    }

    [Fact]
    public void ShellBags_in_ntuser_are_read_through_the_user_hive_parser()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = Chain(root, @"Software\Microsoft\Windows\Shell\BagMRU", T0);
        bag.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "Proiecte", 0, Shell)));
        var (r, s) = RunNtUser(Save(root, "NTUSER.DAT"));
        var e = Assert.Single(s.Events, x => x.Source == "ShellBags");
        Assert.Equal("NTUSER", e.Fields["Hive"]);
        Assert.Equal("Proiecte", e.Fields["BagPath"]);
    }

    // ---- RecentDocs ----

    private static byte[] RecentValue(string name, byte[]? items = null) =>
        [.. Encoding.Unicode.GetBytes(name), 0, 0, .. items ?? List(FileEntry(0x32, "x", 100, Shell, new Ext(7, name, Cre, Acc, 77, 1)))];

    [Fact]
    public void RecentDocs_follow_mrulistex_and_only_the_first_entry_has_the_key_time()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var rd = Chain(root, @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", T1);
        rd.Value("0", RegfBuilder.Binary, RecentValue("raport.docx"));
        rd.Value("1", RegfBuilder.Binary, RecentValue("buget.xlsx"));
        rd.Value("2", RegfBuilder.Binary, RecentValue("Proiect ăâîșț.txt"));
        rd.Value("MRUListEx", RegfBuilder.Binary, Mru(2, 0, 1, End));
        var docx = Child(rd, ".docx", T2);
        docx.Value("0", RegfBuilder.Binary, RecentValue("raport.docx"));
        docx.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var folder = Child(rd, "Folder", T3);
        folder.Value("0", RegfBuilder.Binary, RecentValue("Documente", List(FileEntry(0x31, "Documente", 0, Shell))));
        folder.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));

        var (r, s) = RunNtUser(Save(root, "NTUSER.DAT"));
        var ev = s.Events.Where(e => e.Source == "RecentDocs").ToList();

        Assert.Equal(5, ev.Count);
        var main = ev.Where(e => e.Fields["Key"].EndsWith("RecentDocs")).OrderBy(e => int.Parse(e.Fields["MruPosition"])).ToList();
        Assert.Equal(["Proiect ăâîșț.txt", "raport.docx", "buget.xlsx"], main.Select(e => e.Fields["Name"]).ToArray());
        Assert.Equal(new DateTimeOffset(T1), main[0].Time.Utc);
        Assert.False(main[1].Time.IsKnown);
        Assert.False(main[2].Time.IsKnown);
        Assert.Equal("77", main[0].Fields["MftEntry"]);
        var d = ev.Single(e => e.Fields["Extension"] == ".docx");
        Assert.Equal(new DateTimeOffset(T2), d.Time.Utc);
        var f = ev.Single(e => e.Fields["Extension"] == "Folder");
        Assert.Equal(new DateTimeOffset(T3), f.Time.Utc);
        Assert.Contains("foldere", f.Summary);
        Assert.Equal(EvidenceStatus.Success, r.Status);
    }

    [Fact]
    public void RecentDocs_bad_mrulistex_and_strange_values_do_not_throw()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var rd = Chain(root, @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", T1);
        rd.Value("0", RegfBuilder.Binary, RecentValue("unu.docx"));
        rd.Value("1", RegfBuilder.Binary, Encoding.Unicode.GetBytes("fara-terminator"));       // no NUL, no shell item
        rd.Value("2", RegfBuilder.Binary, new byte[] { 0x41 });                                  // one byte, odd length
        rd.Value("3", RegfBuilder.Binary, Array.Empty<byte>());                                  // empty value
        rd.Value("MRUListEx", RegfBuilder.Binary, Mru(9, 0, 0, 1, 2, 3));                        // missing index, duplicate, no terminator
        var (r, s) = RunNtUser(Save(root, "NTUSER.DAT"));

        var ev = s.Events.Where(e => e.Source == "RecentDocs").ToList();
        Assert.Single(ev, e => e.Fields["Name"] == "unu.docx");
        Assert.Single(ev, e => e.Fields["Name"] == "fara-terminator");
        Assert.Equal(4, ev.Count);                                                                // each existing value exactly once
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("MRUListEx"));
    }

    [Fact]
    public void A_corrupt_recentdocs_subkey_is_a_gap_and_the_rest_of_the_hive_is_still_parsed()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var rd = Chain(root, @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", T1);
        rd.Value("0", RegfBuilder.Binary, RecentValue("pastrat.docx"));
        var zzz = Child(rd, ".zzz", T2);
        zzz.Value("0", RegfBuilder.Binary, RecentValue("pierdut.zzz"));
        Child(zzz, "x", T2);                                                                     // damaged below: the ".zzz" key cannot be read
        Child(rd, ".txt", T2).Value("0", RegfBuilder.Binary, RecentValue("alaturat.txt"));       // sibling that must survive
        Chain(root, @"Software\Microsoft\Windows\CurrentVersion\Run", T1).Value("Updater", RegfBuilder.String, @"C:\Users\test\upd.exe");
        Chain(root, @"Software\Microsoft\Office\16.0\Word\File MRU", T1).Value("Item 1", RegfBuilder.String, OfficeItem(@"C:\Users\test\dupa-coruptie.docx"));
        Chain(root, @"Software\Microsoft\Windows\Shell\BagMRU", T1).Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "dupa", 0, Shell)));
        var hive = RegfBuilder.Build(root, "NTUSER.DAT");
        hive[FindNk(hive, "x")] = (byte)'x';
        var p = Path.Combine(_dir, "NTUSER.DAT");
        File.WriteAllBytes(p, hive);

        var (r, s) = RunNtUser(p);

        Assert.Contains(s.Events, e => e.Source == "RunKey");
        Assert.Contains(s.Events, e => e.Source == "OfficeMRU" && e.Path.EndsWith("dupa-coruptie.docx"));   // read after the corrupt part
        Assert.Contains(s.Events, e => e.Source == "ShellBags" && e.Fields["BagPath"] == "dupa");
        Assert.Contains(s.Events, e => e.Source == "RecentDocs" && e.Fields["Name"] == "pastrat.docx");
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(s.Events, e => e.Source == "RecentDocs" && e.Fields["Name"] == "alaturat.txt");   // the sibling extension key
        Assert.DoesNotContain(s.Events, e => e.Source == "RecentDocs" && e.Fields["Name"] == "pierdut.zzz");
        Assert.Contains(r.Gaps, g => g.Artifact == "RecentDocs" && g.Reason.Contains("nu au putut fi citite"));
    }

    // ---- Office MRU ----

    private static string OfficeItem(string path, DateTime? t = null, string flags = "00000000") =>
        $"[F{flags}][T{(t ?? T1).ToFileTimeUtc():X16}][O00000000]*{path}";

    [Fact]
    public void OfficeMru_reads_file_place_and_user_mru_with_the_filetime_as_utc()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var word = Chain(root, @"Software\Microsoft\Office\16.0\Word", T0);
        var fm = Child(word, "File MRU", T0);
        fm.Value("Item 1", RegfBuilder.String, OfficeItem(@"C:\Users\test\Documents\plan.docx", T2));
        fm.Value("Item 2", RegfBuilder.String, OfficeItem(@"E:\stick\secret.docx", T1));
        fm.Value("Max Display", RegfBuilder.Dword, 25);
        Child(word, "Place MRU", T0).Value("Item 1", RegfBuilder.String, OfficeItem(@"C:\Users\test\Documents", T3));
        var excel = Chain(root, @"Software\Microsoft\Office\16.0\Excel\User MRU\ADAL_ABC123", T0);
        Child(excel, "File MRU", T0).Value("Item 1", RegfBuilder.String, OfficeItem(@"C:\Users\test\buget.xlsx", T3));
        Chain(root, @"Software\Microsoft\Office\16.0\Common", T0);                  // not an application: no MRU keys

        var (r, s) = RunNtUser(Save(root, "NTUSER.DAT"));
        var ev = s.Events.Where(e => e.Source == "OfficeMRU").ToList();

        Assert.Equal(4, ev.Count);
        var plan = ev.Single(e => e.Path == @"C:\Users\test\Documents\plan.docx");
        Assert.Equal(new DateTimeOffset(T2), plan.Time.Utc);
        Assert.Contains("last open by that Office application", plan.TimeSemantics);
        Assert.Equal("Word", plan.Fields["App"]);
        Assert.Equal("16.0", plan.Fields["OfficeVersion"]);
        Assert.Equal("File MRU", plan.Fields["MruKind"]);
        Assert.Equal(new DateTimeOffset(T1), ev.Single(e => e.Path == @"E:\stick\secret.docx").Time.Utc);
        var place = ev.Single(e => e.Fields["MruKind"] == "Place MRU");
        Assert.Contains("folder recent", place.Summary);
        var user = ev.Single(e => e.Path == @"C:\Users\test\buget.xlsx");
        Assert.Equal("ADAL_ABC123", user.Fields["UserMruId"]);
        Assert.Equal(EvidenceStatus.Success, r.Status);
    }

    [Fact]
    public void OfficeMru_bad_values_are_handled_one_by_one()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var fm = Chain(root, @"Software\Microsoft\Office\15.0\PowerPoint\File MRU", T0);
        fm.Value("Item 1", RegfBuilder.String, "[F00000000][T01D9A1B2C3D4E5F6][O00000000]C:\\fara-stea.pptx");   // no '*'
        fm.Value("Item 2", RegfBuilder.String, @"[F00000000][TZZZZ][O00000000]*C:\Users\test\hex-prost.pptx");
        fm.Value("Item 3", RegfBuilder.String, @"[F00000000][TFFFFFFFFFFFFFFFF][O00000000]*C:\Users\test\dincolo-de-calendar.pptx");
        fm.Value("Item 4", RegfBuilder.String, @"[F00000000][T7FFFFFFFFFFFFFFF][O00000000]*C:\Users\test\peste-max.pptx");
        fm.Value("Item 5", RegfBuilder.String, @"[F00000000][O00000000]*C:\Users\test\fara-timp.pptx");
        fm.Value("Item 6", RegfBuilder.String, @"nu are paranteze*C:\x.pptx");
        fm.Value("Item 7", RegfBuilder.String, "[F0][T1]*");                                                    // empty path
        fm.Value("Item 8", RegfBuilder.String, OfficeItem(@"C:\Users\test\bun.pptx", T2));
        fm.Value("Item X", RegfBuilder.String, OfficeItem(@"C:\Users\test\nu-numar.pptx", T2));                       // not "Item N": ignored
        var (r, s) = RunNtUser(Save(root, "NTUSER.DAT"));
        var ev = s.Events.Where(e => e.Source == "OfficeMRU").ToList();

        Assert.Equal(["bun.pptx", "dincolo-de-calendar.pptx", "fara-timp.pptx", "hex-prost.pptx", "peste-max.pptx"],
            ev.Select(e => Path.GetFileName(e.Path)).Order().ToArray());
        Assert.True(ev.Single(e => e.Path.EndsWith("bun.pptx")).Time.IsKnown);
        foreach (var name in new[] { "hex-prost", "dincolo-de-calendar", "fara-timp", "peste-max" })
            Assert.False(ev.Single(e => e.Path.Contains(name)).Time.IsKnown);
        Assert.Equal("ZZZZ", ev.Single(e => e.Path.Contains("hex-prost")).Fields["RawTime"]);               // raw kept
        Assert.Equal("FFFFFFFFFFFFFFFF", ev.Single(e => e.Path.Contains("dincolo")).Fields["RawTime"]);
        Assert.Equal(EvidenceStatus.Partial, r.Status);                                                      // items without a path
        Assert.Equal(3, r.Gaps.Count(g => g.Artifact == "Office MRU"));
    }

    // ---- selection by content ----

    [Fact]
    public void Registry_selects_the_shellbags_parser_by_declared_type_and_by_file_name_and_confirms_the_content()
    {
        var p = Save(SampleBags(), "UsrClass.dat");
        var byType = WindowsParsers.Registry.Select(Item(p, "usrclass_hive"), p);
        Assert.Equal("regf", byType.Preflight!.Fingerprint);
        Assert.Equal(["ShellBagsParser"], byType.Parsers.Select(x => x.Descriptor.ParserId).ToArray());

        var byName = WindowsParsers.Registry.Select(Item(p, "file"), p);
        Assert.Contains(byName.Parsers, x => x.Descriptor.ParserId == "ShellBagsParser");

        Assert.Equal("usrclass_hive", InvestigationPipeline.ImportType(p));

        var notHive = Path.Combine(_dir, "UsrClass2.dat");
        File.WriteAllText(notHive, "nu este un hive de registru");
        var wrong = WindowsParsers.Registry.Select(Item(notHive, "usrclass_hive"), notHive);
        Assert.Empty(wrong.Parsers);
        Assert.NotNull(wrong.Preflight);
        Assert.False(wrong.Preflight!.CanParse);
    }

    [Fact]
    public void Descriptors_state_the_evidence_semantics_and_do_not_overclaim()
    {
        var sb = new ShellBagsParser().Descriptor;
        Assert.Equal(ParserMaturity.Tested, sb.Status);
        Assert.Contains(sb.Limitations, l => l.Contains("nu este prima deschidere"));
        Assert.Contains(sb.Limitations, l => l.Contains("fus orar") && l.Contains("2 secunde"));
        var user = new UserHiveParser().Descriptor;
        Assert.Contains(user.Limitations, l => l.Contains("RecentDocs") && l.Contains("[T]"));
        Assert.Contains("doar sintetic", user.Validation);
    }
}
