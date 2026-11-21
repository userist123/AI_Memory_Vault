using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using Xunit.Abstractions;
using static LogAnalyzer.Dfir.Tests.Synthetic.ShellItemBuilder;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Lab-file fact that is also skipped (reported as skipped, never a silent pass) off Windows, where SHGetPathFromIDListW does not exist.</summary>
public sealed class WindowsLabFileFactAttribute : FactAttribute
{
    public WindowsLabFileFactAttribute(string file)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Needs Windows (SHGetPathFromIDListW) - REAL-DATA VALIDATION = UNAVAILABLE";
        else if (!File.Exists(file)) Skip = $"Lab file not present: {file} - REAL-DATA VALIDATION = UNAVAILABLE";
    }
}

/// <summary>
/// ComDlg32 lists (OpenSavePidlMRU, LastVisitedPidlMRU, CIDSizeMRU): the real lab hive compared with Windows' own decoding of the same bytes
/// (SHGetPathFromIDListW), and synthetic hostile hives.
/// </summary>
public sealed class ComDlg32MruTests : IDisposable
{
    private const string LabHive = @"E:\LA-Lab\diag\mru\NTUSER-copy.dat";
    private const string Base = UserActivity.ComDlgKey;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_cd_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 9, 2, 9, 30, 15, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 9, 3, 14, 45, 30, DateTimeKind.Utc);
    private static readonly DateTime Shell = new(2026, 8, 20, 10, 0, 2);
    private const uint End = 0xFFFFFFFF;

    public ComDlg32MruTests(ITestOutputHelper output) { _out = output; Directory.CreateDirectory(_dir); }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);

    private static EvidenceItem Item(string path) =>
        new() { EvidenceId = "EV-CD", CaseId = "C", Source = "ntuser_hive", SourceType = "ntuser_hive", StoredPath = path, OriginalPath = @"C:\Users\labadmin\NTUSER.DAT" };

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

    private (ParseResult R, ListSink S) Run(RegfBuilder.Key root)
    {
        var p = Path.Combine(_dir, "NTUSER.DAT");
        File.WriteAllBytes(p, RegfBuilder.Build(root, "NTUSER.DAT"));
        var sink = new ListSink();
        return (new UserHiveParser().Parse(Item(p), p, sink, default), sink);
    }

    private static byte[] Pidl(string folder) =>
        List(Root(new Guid("59031A47-3F72-44A7-89C5-5595FE6B30EE")), FileEntry(0x31, folder, 0, Shell));

    private static byte[] Named(string exe, byte[] tail) => [.. Encoding.Unicode.GetBytes(exe), 0, 0, .. tail];

    // ---- real lab hive vs Windows ----

    [WindowsLabFileFact(LabHive)]
    public void Lab_hive_paths_match_windows_own_decoding()
    {
        using var fs = new FileStream(LabHive, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reg = new RegistryReaderHelper(fs).Reg;
        int match = 0, mismatch = 0, windowsFalse = 0, undecodable = 0, total = 0;
        var keys = new List<(RawKey Key, bool Named)>();
        foreach (var name in new[] { "LastVisitedPidlMRU", "OpenSavePidlMRU" })
            if (reg.OpenKey(Base + @"\" + name) is { } k)
            {
                keys.Add((k, name == "LastVisitedPidlMRU"));
                foreach (var off in reg.ChildOffsets(k)) keys.Add((reg.ReadChild(k, off), false));
            }
        Assert.NotEmpty(keys);
        foreach (var (key, named) in keys)
            foreach (var v in key.Values.Where(v => v.Name.Length > 0 && v.Name.All(char.IsAsciiDigit)))
            {
                var d = v.Data;
                int start = 0;
                if (named) { while (start + 2 <= d.Length && (d[start] != 0 || d[start + 1] != 0)) start += 2; start += 2; }
                var pidl = d.AsSpan(start).ToArray();
                total++;
                var notes = new List<string>();
                var (ours, _, items, _, _) = UserActivity.DecodePidl(pidl, @"C:\Users\labadmin", notes);
                var sb = new StringBuilder(1024);
                bool ok;
                var h = GCHandle.Alloc(pidl, GCHandleType.Pinned);
                try { ok = SHGetPathFromIDListW(h.AddrOfPinnedObject(), sb); } finally { h.Free(); }
                if (items == 0 || ours.Length == 0) { undecodable++; _out.WriteLine($"{key.Path}\\{v.Name}: UNDECODABLE ({string.Join("; ", notes)}) windows={(ok ? sb.ToString() : "false")}"); continue; }
                if (!ok) { windowsFalse++; _out.WriteLine($"{key.Path}\\{v.Name}: windows could not resolve; ours={ours}"); continue; }
                var win = sb.ToString();
                // ours starts with the user-folder root item ("Dosarul utilizatorului"); Windows has the absolute profile path there.
                // With no separator the whole leaf name is compared; the tail is never empty (EndsWith("") is always true).
                var tail = ours.Contains('\\') ? ours[ours.IndexOf('\\')..] : @"\" + ours;
                Assert.True(tail.Length > 1, $"empty comparison tail for {key.Path} {v.Name}");
                bool same = win.EndsWith(tail, StringComparison.OrdinalIgnoreCase) && win.StartsWith(@"C:\Users\", StringComparison.OrdinalIgnoreCase);
                if (same) match++; else mismatch++;
                _out.WriteLine($"{key.Path}\\{v.Name}: {(same ? "MATCH" : "MISMATCH")} windows={win} ours={ours} notes={string.Join("; ", notes)}");
            }
        _out.WriteLine($"values={total} match={match} mismatch={mismatch} windowsFalse={windowsFalse} undecodable={undecodable}");
        Assert.True(total > 0);
        Assert.Equal(0, mismatch);
        Assert.Equal(0, undecodable);
        Assert.Equal(0, windowsFalse);
        Assert.Equal(total, match);   // every value was compared with Windows and agreed
    }

    [LabFileFact(LabHive)]
    public void Lab_hive_events_follow_mrulistex_and_name_the_program()
    {
        var sink = new ListSink();
        var r = new UserHiveParser().Parse(Item(LabHive), LabHive, sink, default);
        var os = sink.Events.Where(e => e.Source == "OpenSaveMRU").ToList();
        var lv = sink.Events.Where(e => e.Source == "LastVisitedMRU").ToList();
        var cid = sink.Events.Where(e => e.Source == "CIDSizeMRU").ToList();
        _out.WriteLine($"OpenSave={os.Count} LastVisited={lv.Count} CIDSize={cid.Count} gaps={r.Gaps.Count}");
        foreach (var e in os.Concat(lv).Concat(cid)) _out.WriteLine($"{e.Source} {e.Fields["Key"]} #{e.Fields["ValueName"]} pos={e.Fields["MruPosition"]} listed={e.Fields["MruListed"]} time={e.Time.IsKnown} exe={e.Fields["ExeName"]} path={e.Fields["Path"]} note={e.Fields["DecodeNote"]}");
        Assert.Equal(4, os.Count);   // two keys ("*" and pdf), two values each
        Assert.Single(lv);
        Assert.Single(cid);
        Assert.Equal("LogAnalyzer.Standalone.exe", lv[0].Fields["ExeName"]);
        Assert.Equal("LogAnalyzer.Standalone.exe", cid[0].Fields["ExeName"]);
        foreach (var g in os.GroupBy(e => e.Fields["Key"]))
        {
            Assert.Equal("1", g.Single(e => e.Fields["MruPosition"] == "0").Fields["ValueName"]);   // MRUListEx 1,0: value "1" is newest
            Assert.Single(g, e => e.Time.IsKnown);
        }
    }

    // ---- synthetic ----

    [Fact]
    public void Order_follows_mrulistex_only_the_newest_has_the_key_time_and_unlisted_entries_are_marked()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\pdf", T1);
        k.Value("0", RegfBuilder.Binary, Pidl("Unu"));
        k.Value("1", RegfBuilder.Binary, Pidl("Doi"));
        k.Value("2", RegfBuilder.Binary, Pidl("Trei"));
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(1, 0, End));
        Chain(root, Base + @"\OpenSavePidlMRU\*", T2).Value("0", RegfBuilder.Binary, Pidl("Stea"));
        Chain(root, Base + @"\OpenSavePidlMRU\*", T2).Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var (r, s) = Run(root);
        var ev = s.Events.Where(e => e.Source == "OpenSaveMRU" && e.Fields["Extension"] == "pdf").ToList();
        Assert.Equal(["1", "0", "2"], ev.Select(e => e.Fields["ValueName"]).ToArray());
        Assert.Equal(["0", "1", "2"], ev.Select(e => e.Fields["MruPosition"]).ToArray());
        Assert.Equal(["true", "true", "false"], ev.Select(e => e.Fields["MruListed"]).ToArray());
        Assert.Equal(new DateTimeOffset(T1), ev[0].Time.Utc);
        Assert.False(ev[1].Time.IsKnown);
        Assert.False(ev[2].Time.IsKnown);
        Assert.Contains("Doi", ev[0].Fields["Path"]);
        var star = s.Events.Single(e => e.Fields["Extension"] == "*");
        Assert.Equal(new DateTimeOffset(T2), star.Time.Utc);
        Assert.Equal(EvidenceStatus.Success, r.Status);
    }

    [Fact]
    public void LastVisited_has_exe_and_folder_and_CIDSize_only_the_exe()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var lv = Chain(root, Base + @"\LastVisitedPidlMRU", T1);
        lv.Value("0", RegfBuilder.Binary, Named("app.exe", Pidl("Raport")));
        lv.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var cid = Chain(root, Base + @"\CIDSizeMRU", T2);
        cid.Value("0", RegfBuilder.Binary, Named("app.exe", new byte[40]));
        cid.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var (_, s) = Run(root);
        var a = s.Events.Single(e => e.Source == "LastVisitedMRU");
        Assert.Equal("app.exe", a.Fields["ExeName"]);
        Assert.EndsWith("Raport", a.Fields["Path"]);
        Assert.Equal(new DateTimeOffset(T1), a.Time.Utc);
        var b = s.Events.Single(e => e.Source == "CIDSizeMRU");
        Assert.Equal("app.exe", b.Fields["ExeName"]);
        Assert.Equal("", b.Fields["Path"]);
        Assert.Equal("", b.Fields["PidlHex"]);
    }

    [Fact]
    public void Malformed_mrulistex_and_hostile_values_give_gaps_not_exceptions()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\txt", T1);
        k.Value("0", RegfBuilder.Binary, Pidl("Bun"));
        k.Value("1", RegfBuilder.Binary, new byte[] { 0x05, 0x00 });                         // item size 5 > data
        k.Value("2", RegfBuilder.Binary, new byte[] { 0x41 });                               // one byte
        k.Value("3", RegfBuilder.Binary, Array.Empty<byte>());                               // empty
        k.Value("4", RegfBuilder.Binary, new byte[] { 0x01, 0x00, 0x00, 0x00 });             // invalid size
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(9, 0, 0, 1).Concat(new byte[] { 0x07, 0x00 }).ToArray());          // missing index, duplicate, odd length, no terminator
        var lv = Chain(root, Base + @"\LastVisitedPidlMRU", T1);
        lv.Value("0", RegfBuilder.Binary, Encoding.Unicode.GetBytes("fara-terminator"));     // no NUL
        lv.Value("MRUListEx", RegfBuilder.Binary, new byte[] { 1, 2, 3 });                   // shorter than one index
        var (r, s) = Run(root);
        Assert.Equal(5, s.Events.Count(e => e.Source == "OpenSaveMRU"));
        Assert.Single(s.Events, e => e.Source == "LastVisitedMRU");
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("MRUListEx"));
    }

    [Fact]
    public void A_huge_value_is_cut_and_a_long_list_is_capped()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\big", T1);
        var items = new byte[UserActivity.MaxComDlgPidlBytes * 2];
        for (int i = 0; i + 4 <= items.Length; i += 4) { items[i] = 4; items[i + 2] = 0x31; }   // thousands of tiny items
        k.Value("0", RegfBuilder.Binary, items);
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var many = Chain(root, Base + @"\OpenSavePidlMRU\many", T1);
        var idx = new List<uint>();
        for (int i = 0; i < UserActivity.MaxComDlgValues + 50; i++) { many.Value(i.ToString(), RegfBuilder.Binary, Pidl("x")); idx.Add((uint)i); }
        idx.Add(End);
        many.Value("MRUListEx", RegfBuilder.Binary, Mru(idx.ToArray()));
        var (r, s) = Run(root);
        var big = s.Events.Single(e => e.Fields["Extension"] == "big");
        Assert.Contains("primii", big.Fields["DecodeNote"]);
        Assert.True(big.Fields["PidlHex"].Length <= UserActivity.PidlHexBytes * 2 + 1);
        Assert.Equal(UserActivity.MaxComDlgValues, s.Events.Count(e => e.Fields["Extension"] == "many"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("valori"));
    }

    [Fact]
    public void No_comdlg_keys_means_no_events()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        Chain(root, @"Software\Microsoft\Windows\CurrentVersion\Run", T1).Value("A", RegfBuilder.String, @"C:\a.exe");
        var (_, s) = Run(root);
        Assert.DoesNotContain(s.Events, e => e.Source is "OpenSaveMRU" or "LastVisitedMRU" or "CIDSizeMRU");
    }

    // ---- review fixes ----

    private static void Damage(byte[] hive, string keyName)
    {
        for (int i = 0x1000; i + 0x4C + keyName.Length < hive.Length; i += 8)
        {
            if (hive[i + 4] != (byte)'n' || hive[i + 5] != (byte)'k') continue;
            int len = BinaryPrimitives.ReadUInt16LittleEndian(hive.AsSpan(i + 4 + 0x48));
            if (len != keyName.Length || Encoding.ASCII.GetString(hive, i + 4 + 0x4C, len) != keyName) continue;
            hive[i + 4] = (byte)'x';
            return;
        }
        throw new InvalidOperationException("key not found: " + keyName);
    }

    [Fact]
    public void A_missing_newest_value_gives_the_key_time_to_nobody_and_says_so()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\pdf", T1);
        k.Value("0", RegfBuilder.Binary, Pidl("Unu"));
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(9, 0, End));   // "9" was deleted: the key time is the deletion time, not the choice of "0"
        var (r, s) = Run(root);
        var e = s.Events.Single(x => x.Source == "OpenSaveMRU");
        Assert.Equal("0", e.Fields["ValueName"]);
        Assert.False(e.Time.IsKnown);
        Assert.Contains(r.Gaps, g => g.Artifact == "OpenSaveMRU" && g.Reason.Contains("nu există") && g.Reason.Contains("9"));
        Assert.Equal(EvidenceStatus.Partial, r.Status);
    }

    [Fact]
    public void An_existing_newest_value_keeps_the_key_time_even_with_a_missing_older_one()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\pdf", T1);
        k.Value("0", RegfBuilder.Binary, Pidl("Unu"));
        k.Value("1", RegfBuilder.Binary, Pidl("Doi"));
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(1, 9, 0, End));
        var (_, s) = Run(root);
        Assert.Equal(new DateTimeOffset(T1), s.Events.Single(x => x.Fields["ValueName"] == "1").Time.Utc);
        Assert.False(s.Events.Single(x => x.Fields["ValueName"] == "0").Time.IsKnown);
    }

    [Fact]
    public void A_program_name_over_the_limit_is_not_decoded_as_a_pidl_and_one_of_exactly_the_limit_with_NUL_is_normal()
    {
        // 260+ characters, no NUL; the bytes after the 260th character form a valid item list that must NOT be decoded.
        var over = new RegfBuilder.Key("ROOT", T0);
        var lv = Chain(over, Base + @"\LastVisitedPidlMRU", T1);
        lv.Value("0", RegfBuilder.Binary, Encoding.Unicode.GetBytes(new string('a', 270)).Concat(Pidl("Ascuns")).ToArray());
        lv.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var (r, s) = Run(over);
        var e = s.Events.Single(x => x.Source == "LastVisitedMRU");
        Assert.Contains("depășește limita", e.Fields["DecodeNote"]);
        Assert.DoesNotContain("terminator", e.Fields["DecodeNote"]);
        Assert.Equal("", e.Fields["Path"]);
        Assert.Equal(260, e.Fields["ExeName"].Length);
        Assert.Contains(r.Gaps, g => g.Artifact == "LastVisitedMRU" && g.Reason.Contains("260"));

        // exactly 260 characters followed by NUL: a normal entry
        var exact = new RegfBuilder.Key("ROOT", T0);
        var lv2 = Chain(exact, Base + @"\LastVisitedPidlMRU", T1);
        lv2.Value("0", RegfBuilder.Binary, Named(new string('a', 260), Pidl("Vizibil")));
        lv2.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var (_, s2) = Run(exact);
        var e2 = s2.Events.Single(x => x.Source == "LastVisitedMRU");
        Assert.EndsWith("Vizibil", e2.Fields["Path"]);
        Assert.Equal("", e2.Fields["DecodeNote"]);
    }

    [Fact]
    public void Undecodable_lists_are_counted_and_reported_once_per_key_with_bounded_examples()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\bad", T1);
        var order = new List<uint>();
        for (int i = 0; i < 8; i++) { k.Value(i.ToString(), RegfBuilder.Binary, new byte[] { 0x01, 0x00, 0x00, 0x00 }); order.Add((uint)i); }
        order.Add(End);
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(order.ToArray()));   // clean MRUListEx, every list garbage
        var (r, s) = Run(root);
        Assert.Equal(8, s.Events.Count(e => e.Source == "OpenSaveMRU"));
        Assert.NotEqual(EvidenceStatus.Success, r.Status);
        Assert.True(r.MalformedRecords >= 1);
        var gap = Assert.Single(r.Gaps, g => g.Artifact == "OpenSaveMRU" && g.Reason.Contains("niciun element shell citibil"));
        Assert.Contains("8", gap.Reason);
        Assert.DoesNotContain("7", gap.Reason.Split("(ex.:")[1]);   // only the first three names are listed
    }

    [Fact]
    public void A_cut_value_and_a_list_over_the_item_cap_are_gaps()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\big", T1);
        var items = new byte[UserActivity.MaxComDlgPidlBytes * 2];
        for (int i = 0; i + 4 <= items.Length; i += 4) { items[i] = 4; items[i + 2] = 0x31; }
        k.Value("0", RegfBuilder.Binary, items);
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var (r, _) = Run(root);
        Assert.Contains(r.Gaps, g => g.Artifact == "OpenSaveMRU" && g.Reason.Contains("depășesc"));
        Assert.Contains(r.Gaps, g => g.Artifact == "OpenSaveMRU" && g.Reason.Contains("mai mult de"));
        Assert.NotEqual(EvidenceStatus.Success, r.Status);
    }

    private (ParseResult R, ListSink S) RunDamaged(string damagedKey)
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var lv = Chain(root, Base + @"\LastVisitedPidlMRU", T1);
        lv.Value("0", RegfBuilder.Binary, Named("visited.exe", Pidl("X")));
        lv.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var cid = Chain(root, Base + @"\CIDSizeMRU", T1);
        cid.Value("0", RegfBuilder.Binary, Named("cid.exe", new byte[8]));
        cid.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var os = Chain(root, Base + @"\OpenSavePidlMRU\pdf", T2);
        os.Value("0", RegfBuilder.Binary, Pidl("Pastrat"));
        os.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var hive = RegfBuilder.Build(root, "NTUSER.DAT");
        Damage(hive, damagedKey);
        var p = Path.Combine(_dir, "NTUSER.DAT");
        File.WriteAllBytes(p, hive);
        var s = new ListSink();
        return (new UserHiveParser().Parse(Item(p), p, s, default), s);
    }

    [Fact]
    public void A_corrupt_OpenSave_key_is_its_own_gap_and_the_other_two_keys_are_still_read()
    {
        var (r, s) = RunDamaged("OpenSavePidlMRU");
        Assert.Contains(s.Events, e => e.Source == "LastVisitedMRU" && e.Fields["ExeName"] == "visited.exe");
        Assert.Contains(s.Events, e => e.Source == "CIDSizeMRU" && e.Fields["ExeName"] == "cid.exe");
        Assert.DoesNotContain(s.Events, e => e.Source == "OpenSaveMRU");
        Assert.Contains(r.Gaps, g => g.Artifact == "OpenSaveMRU");
        Assert.Equal(EvidenceStatus.Partial, r.Status);
    }

    [Fact]
    public void A_corrupt_LastVisited_key_is_labelled_with_its_own_name_and_CIDSize_is_still_read()
    {
        var (r, s) = RunDamaged("LastVisitedPidlMRU");
        Assert.DoesNotContain(s.Events, e => e.Source == "LastVisitedMRU");
        Assert.Contains(s.Events, e => e.Source == "CIDSizeMRU" && e.Fields["ExeName"] == "cid.exe");
        Assert.Contains(r.Gaps, g => g.Artifact == "LastVisitedMRU");
        Assert.Equal(EvidenceStatus.Partial, r.Status);
    }

    [Fact]
    public void An_unknown_profile_folder_is_explained_in_ResolutionNote_and_time_semantics_are_english()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var k = Chain(root, Base + @"\OpenSavePidlMRU\pdf", T1);
        k.Value("0", RegfBuilder.Binary, Pidl("Unu"));
        k.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var (_, s) = Run(root);
        var e = s.Events.Single(x => x.Source == "OpenSaveMRU");
        // a hive outside any Users\<name> folder: the profile folder is unknown, so the path is not placed on disk and the note says why (review round 2)
        Assert.False(e.Fields.ContainsKey("ResolvedPath"));
        Assert.False(string.IsNullOrWhiteSpace(e.Fields.GetValueOrDefault("ResolutionNote")));
        Assert.StartsWith("ComDlg32 key last written", e.TimeSemantics);
        Assert.Equal("0", e.Fields["MruPosition"]);
    }

    private sealed class RegistryReaderHelper(Stream s) { public RawRegistry Reg { get; } = new(s); }
}
