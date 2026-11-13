using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Noise of the $SI/$FN mismatch on a freshly installed Windows (measured: 99,099 mismatches, all in system-managed trees or directories).
/// Mismatches of directories and of files in system trees are counted in the summary, not emitted; the cap applies to the rest.
/// </summary>
public sealed class MftParserNoiseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_noise_" + Guid.NewGuid().ToString("N"));
    public MftParserNoiseTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTimeOffset Acquired = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly long Old = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static readonly long Install = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static readonly long Recent = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    private bool _noDate;
    private List<TimelineEvent> Run(params (int, byte[])[] recs)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".mft");
        File.WriteAllBytes(path, Mft(recs));
        var sink = new ListSink();
        new MftParser().Parse(new EvidenceItem { EvidenceId = "EV-MFT", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = path, AcquiredAtUtc = _noDate ? default : Acquired }, path, sink, default);
        return sink.Events.ToList();
    }

    private static byte[] Mismatch(string name, ulong parent, long si = 0, long fn = 0) => FileRecord(1, parent, name, si == 0 ? Old : si, fn == 0 ? Install : fn);
    private static byte[] DirMismatch(string name, ulong parent, long fn = 0) { fn = fn == 0 ? Install : fn; return Record(1, (ushort)(InUse | Dir), 0, Resident(0x10, Si(Old, Old, Old, Old)), Resident(0x30, Fn(parent, name, fn, fn, fn, fn))); }

    // records: 40 \Windows, 41 \Windows\System32, 42 \Users, 43 \Users\bob, 44 \Program Files, 45 \ProgramData, 46 \ProgramData\Microsoft, 47 \ProgramData\Other, 48 \Windowsx
    private static (int, byte[])[] Tree() =>
    [
        (40, DirRecord(1, Ref(5, 5), "Windows", Recent)), (41, DirRecord(1, Ref(40, 1), "System32", Recent)),
        (42, DirRecord(1, Ref(5, 5), "Users", Recent)), (43, DirRecord(1, Ref(42, 1), "bob", Recent)),
        (44, DirRecord(1, Ref(5, 5), "Program Files", Recent)), (45, DirRecord(1, Ref(5, 5), "ProgramData", Recent)),
        (46, DirRecord(1, Ref(45, 1), "Microsoft", Recent)), (47, DirRecord(1, Ref(45, 1), "Other", Recent)), (48, DirRecord(1, Ref(5, 5), "Windowsx", Recent)),
    ];

    private List<TimelineEvent> RunTree(params (int, byte[])[] more) => Run([.. Tree(), .. more]);

    [Fact]
    public void System_tree_mismatches_are_counted_not_emitted_and_user_mismatches_are_emitted()
    {
        var ev = RunTree((100, Mismatch("a.dll", Ref(41, 1))), (101, Mismatch("b.dll", Ref(40, 1))), (102, Mismatch("c.txt", Ref(46, 1))),
                         (103, Mismatch("stomp.txt", Ref(43, 1))), (104, Mismatch("other.txt", Ref(47, 1))), (105, Mismatch("x.txt", Ref(48, 1))), (106, Mismatch("pf.txt", Ref(44, 1))));
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal("7", F(s, "MismatchTotal"));
        Assert.Equal("4", F(s, "MismatchSystemTrees"));
        Assert.Equal("0", F(s, "MismatchDirectories"));
        Assert.Equal("3", F(s, "MismatchEmitted"));
        Assert.Equal(["\\ProgramData\\Other\\other.txt", "\\Users\\bob\\stomp.txt", "\\Windowsx\\x.txt"], ev.Where(e => F(e, "Category") == "mismatch").Select(e => e.Path).Order().ToArray());
        Assert.Contains("\\Windows\\=2", F(s, "MismatchSystemTreesByRoot"));
        Assert.Contains("\\ProgramData\\Microsoft\\=1", F(s, "MismatchSystemTreesByRoot"));
        Assert.Contains("\\Program Files\\=1", F(s, "MismatchSystemTreesByRoot"));
    }

    [Fact]
    public void Directory_mismatches_are_counted_not_emitted()
    {
        var ev = RunTree((100, DirMismatch("Sub", Ref(43, 1))), (101, DirMismatch("Sub2", Ref(41, 1))));
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal("2", F(s, "MismatchTotal")); Assert.Equal("2", F(s, "MismatchDirectories")); Assert.Equal("0", F(s, "MismatchSystemTrees"));
        Assert.Equal("0", F(s, "MismatchEmitted"));
        Assert.DoesNotContain(ev, e => F(e, "Category") == "mismatch");
    }

    [Fact]
    public void Prefix_match_is_case_insensitive_and_an_incomplete_path_is_not_a_system_tree()
    {
        var ev = RunTree((100, DirRecord(1, Ref(5, 5), "WINDOWS", Recent)), (101, Mismatch("up.dll", Ref(100, 1))),   // an upper-case twin directory
                         (102, Mismatch("orphan.txt", Ref(900, 1))));                                          // parent missing: path "…\"
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal("1", F(s, "MismatchSystemTrees"));
        var orphan = Assert.Single(ev, e => F(e, "FileName") == "orphan.txt");
        Assert.StartsWith("…\\", orphan.Path); Assert.Equal("mismatch", F(orphan, "Category"));
        Assert.True(MftParser.IsSystemTreePath("\\wInDoWs\\x"));
        Assert.False(MftParser.IsSystemTreePath("…\\Windows\\x"));
        Assert.False(MftParser.IsSystemTreePath("\\Windowsx\\x"));
    }

    [Fact]
    public void A_recent_mismatch_in_a_system_tree_stays_in_view_as_a_recent_event_with_both_times()
    {
        // $SI created long ago but modified recently (a creation time set back on a file that was updated): kept as "recent" for the USN path
        var stomped = FileRecord(1, Ref(41, 1), "evil.dll", Old, Install, siValue: Si(Old, Recent, Recent, Recent));
        var ev = RunTree((100, stomped));
        Assert.DoesNotContain(ev, e => F(e, "Category") == "mismatch");
        var r = Assert.Single(ev, e => F(e, "Category") == "recent" && e.Path == "\\Windows\\System32\\evil.dll");
        Assert.NotEqual("", F(r, "SiCreated")); Assert.NotEqual("", F(r, "FnCreated"));
        Assert.NotEqual(F(r, "SiCreated"), F(r, "FnCreated"));
    }

    [Fact]
    public void Install_noise_with_only_a_recent_fn_creation_is_not_flooded_into_the_recent_category()
    {
        var ev = RunTree((100, Mismatch("noise.dll", Ref(41, 1), Old, Recent)), (101, Mismatch("user.txt", Ref(43, 1), Old, Recent)));
        Assert.DoesNotContain(ev, e => e.Path == "\\Windows\\System32\\noise.dll" && F(e, "Category") == "recent");   // it goes to the separate channel instead
        Assert.Contains(ev, e => e.Path == "\\Users\\bob\\user.txt" && F(e, "Category") == "mismatch");   // outside the trees: unchanged
    }

    // ---------------- channel for filtered mismatches created in the window ----------------

    [Fact]
    public void Filtered_mismatches_with_a_recent_fn_creation_go_to_the_system_mismatch_recent_channel_with_the_mismatch_fields()
    {
        var ev = RunTree((100, Mismatch("a.dll", Ref(41, 1), Old, Recent)), (101, DirMismatch("Sub", Ref(41, 1), Recent)),
                         (102, Mismatch("old.dll", Ref(41, 1))));   // $FN created at installation, outside the window: counted only
        var a = Assert.Single(ev, e => F(e, "Category") == "system_mismatch_recent" && e.Path == "\\Windows\\System32\\a.dll");
        Assert.Contains("SI_CREATED_BEFORE_FN_CREATED", F(a, "Signals"));
        Assert.Equal("false", F(a, "HasDosName")); Assert.NotEqual("", F(a, "SiCreated")); Assert.NotEqual("", F(a, "FnCreated")); Assert.NotEqual("", F(a, "SiMftModified"));
        Assert.Equal(Classification.Candidate, a.Classification); Assert.Equal(Confidence.Low, a.Confidence);
        Assert.Contains(ev, e => F(e, "Category") == "system_mismatch_recent" && F(e, "IsDirectory") == "true");
        Assert.DoesNotContain(ev, e => F(e, "FileName") == "old.dll");
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal("2", F(s, "SystemMismatchRecentTotal")); Assert.Equal("2", F(s, "SystemMismatchRecentEmitted")); Assert.Equal("emisă", F(s, "SystemMismatchRecentCategory"));
        Assert.Equal("3", F(s, "MismatchTotal")); Assert.Equal("0", F(s, "MismatchEmitted"));
    }

    [Fact]
    public void The_channel_cap_keeps_the_newest_fn_creation_and_counts_the_rest()
    {
        const int n = MftParser.MaxSystemMismatchRecentEvents + 300;
        var recs = new List<(int, byte[])>();
        for (int i = 0; i < n; i++) recs.Add((100 + i, Mismatch($"s{i}.dll", Ref(41, 1), Old, Recent - i * 10_000_000L)));   // larger i = older
        var ev = RunTree([.. recs]);
        var ch = ev.Where(e => F(e, "Category") == "system_mismatch_recent").ToList();
        Assert.Equal(MftParser.MaxSystemMismatchRecentEvents, ch.Count);
        Assert.Contains(ch, e => F(e, "FileName") == "s0.dll");
        Assert.DoesNotContain(ch, e => F(e, "FileName") == $"s{n - 1}.dll");
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal(n.ToString(CultureInfo.InvariantCulture), F(s, "SystemMismatchRecentTotal"));
        Assert.Equal(MftParser.MaxSystemMismatchRecentEvents.ToString(CultureInfo.InvariantCulture), F(s, "SystemMismatchRecentEmitted"));
    }

    [Fact]
    public void Without_an_acquisition_date_the_channel_is_not_emitted_and_the_summary_says_why()
    {
        _noDate = true;
        var ev = RunTree((100, Mismatch("a.dll", Ref(41, 1), Old, Recent)));
        Assert.DoesNotContain(ev, e => F(e, "Category") == "system_mismatch_recent");
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.StartsWith("omisă", F(s, "SystemMismatchRecentCategory"));
    }

    [Fact]
    public void Packages_under_any_profile_are_a_system_tree_but_public_and_other_appdata_are_not()
    {
        // 200 Users\bob\AppData, 201 Local, 202 Packages, 203 pkg, 204 Other, 205 Users\Public
        var ev = RunTree((200, DirRecord(1, Ref(43, 1), "AppData", Recent)), (201, DirRecord(1, Ref(200, 1), "Local", Recent)), (202, DirRecord(1, Ref(201, 1), "Packages", Recent)),
                         (203, DirRecord(1, Ref(202, 1), "pkg", Recent)), (204, DirRecord(1, Ref(201, 1), "Other", Recent)), (205, DirRecord(1, Ref(42, 1), "Public", Recent)),
                         (300, Mismatch("p.dat", Ref(203, 1))), (301, Mismatch("o.dat", Ref(204, 1))), (302, Mismatch("pub.dat", Ref(205, 1))));
        Assert.DoesNotContain(ev, e => F(e, "FileName") == "p.dat" && F(e, "Category") == "mismatch");
        Assert.Contains(ev, e => F(e, "FileName") == "o.dat" && F(e, "Category") == "mismatch");
        Assert.Contains(ev, e => F(e, "FileName") == "pub.dat" && F(e, "Category") == "mismatch");
        Assert.Contains("\\Users\\*\\AppData\\Local\\Packages\\=1", F(Assert.Single(ev, e => F(e, "Category") == "summary"), "MismatchSystemTreesByRoot"));
        Assert.True(MftParser.IsSystemTreePath("\\users\\X\\appdata\\local\\packages\\a"));
        Assert.False(MftParser.IsSystemTreePath("\\Users\\\\AppData\\Local\\Packages\\a"));
        Assert.False(MftParser.IsSystemTreePath("\\Users\\Public\\a"));
    }

    [Fact]
    public void The_cap_applies_only_to_the_mismatches_that_remain()
    {
        var recs = new List<(int, byte[])>();
        for (int i = 0; i < MftParser.MaxMismatchEvents + 300; i++) recs.Add((100 + i, Mismatch($"s{i}.dll", Ref(41, 1))));
        recs.Add((100 + MftParser.MaxMismatchEvents + 300, Mismatch("stomp.txt", Ref(43, 1))));
        var ev = RunTree([.. recs]);
        Assert.Contains(ev, e => e.Path == "\\Users\\bob\\stomp.txt" && F(e, "Category") == "mismatch");
        var s = Assert.Single(ev, e => F(e, "Category") == "summary");
        Assert.Equal((MftParser.MaxMismatchEvents + 301).ToString(CultureInfo.InvariantCulture), F(s, "MismatchTotal"));
        Assert.Equal((MftParser.MaxMismatchEvents + 300).ToString(CultureInfo.InvariantCulture), F(s, "MismatchSystemTrees"));
        Assert.Equal("1", F(s, "MismatchEmitted"));
    }

    // ---------------- the rule and AF08 ----------------

    private static string T(long ft) => NtfsTime.Text(ft);

    private static TimelineEvent MftEv(string id, string path, bool dir, long si, long fn)
    {
        var e = new TimelineEvent { Time = Timestamp.Unknown(), Source = "MFT", EvidenceId = "EV-MFT", Process = "", Path = path, Summary = "s", TimeSemantics = "t", TemporalType = TemporalType.Historical, Classification = Classification.Candidate, Confidence = Confidence.Low, Locator = "MFT#1" };
        e.Fields["Category"] = "mismatch"; e.Fields["FileId"] = id; e.Fields["IsDirectory"] = dir ? "true" : "false"; e.Fields["HasDosName"] = "false";
        e.Fields["SiCreated"] = T(si); e.Fields["SiModified"] = T(si); e.Fields["SiMftModified"] = T(fn + 10_000_000); e.Fields["FnCreated"] = T(fn);
        return e;
    }

    [Fact]
    public void Medium_is_not_given_to_directories_or_system_trees_but_is_to_a_user_file()
    {
        var hits = NtfsTimestomp.Detect([MftEv("0x100000000000a", "\\Windows\\System32\\a.dll", false, Old, Install), MftEv("0x100000000000b", "\\Users\\bob\\Sub", true, Old, Install),
                                         MftEv("0x100000000000c", "\\Users\\bob\\x.txt", false, Old, Install)]);
        var h = Assert.Single(hits);
        Assert.Equal(NtfsTimestomp.Level.Medium, h.Level); Assert.Equal("\\Users\\bob\\x.txt", h.Path);
    }

    [Fact]
    public void Af08_without_usn_is_undetermined_when_mismatches_were_only_counted_because_only_usn_can_examine_them()
    {
        // Review 2026-11-13 (S3, round 2): system-tree / directory mismatches are counted, not emitted; only the USN path can examine them,
        // so without the journal "nothing found" would claim more than was checked (it was NotDetected before this decision).
        var s = new TimelineEvent { Time = Timestamp.FromUtc(Acquired.UtcDateTime, "a", "t"), Source = "MFT", EvidenceId = "EV-MFT", Process = "", Path = "", Summary = "s", TimeSemantics = "t", TemporalType = TemporalType.Derived, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "summary" };
        foreach (var (k, v) in new Dictionary<string, string> { ["Category"] = "summary", ["TotalRecords"] = "1000", ["RecentCategory"] = "emisă", ["MismatchTotal"] = "900", ["MismatchEmitted"] = "0", ["MismatchSystemTrees"] = "850", ["MismatchDirectories"] = "50", ["RecentTotal"] = "0", ["RecentEmitted"] = "0" }) s.Fields[k] = v;
        var af = AntiForensics.Evaluate([s], []).Single(c => c.Id == "AF08");
        Assert.Equal(AntiForensicResult.Undetermined, af.Result);
        Assert.Contains("fără jurnalul USN", af.Reason);
        Assert.Contains("900", af.Reason);
        // nothing counted (no system-tree or directory mismatch) and nothing found: NotDetected stays possible without USN
        s.Fields["MismatchTotal"] = "0"; s.Fields["MismatchSystemTrees"] = "0"; s.Fields["MismatchDirectories"] = "0";
        Assert.Equal(AntiForensicResult.NotDetected, AntiForensics.Evaluate([s], []).Single(c => c.Id == "AF08").Result);
        s.Fields["MismatchTotal"] = "900"; s.Fields["MismatchSystemTrees"] = "850"; s.Fields["MismatchDirectories"] = "50";
        // the real cap still makes it Undetermined
        s.Fields["MismatchTotal"] = "9000"; s.Fields["MismatchEmitted"] = "5000";
        Assert.Equal(AntiForensicResult.Undetermined, AntiForensics.Evaluate([s], []).Single(c => c.Id == "AF08").Result);
    }

    [Fact]
    public void Af08_is_undetermined_when_the_channel_cap_was_reached_and_says_why()
    {
        var s = new TimelineEvent { Time = Timestamp.FromUtc(Acquired.UtcDateTime, "a", "t"), Source = "MFT", EvidenceId = "EV-MFT", Process = "", Path = "", Summary = "s", TimeSemantics = "t", TemporalType = TemporalType.Derived, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "summary" };
        foreach (var (k, v) in new Dictionary<string, string> { ["Category"] = "summary", ["TotalRecords"] = "1000", ["RecentCategory"] = "emisă", ["MismatchTotal"] = "900", ["MismatchEmitted"] = "0", ["MismatchSystemTrees"] = "900", ["MismatchDirectories"] = "0", ["RecentTotal"] = "0", ["RecentEmitted"] = "0", ["SystemMismatchRecentTotal"] = "20000", ["SystemMismatchRecentEmitted"] = "20000" }) s.Fields[k] = v;
        // channel not capped, no USN, 900 mismatches only counted -> Undetermined for the USN reason (review 2026-11-13 S3 r2), not the channel's
        var baseline = AntiForensics.Evaluate([s], []).Single(c => c.Id == "AF08");
        Assert.Equal(AntiForensicResult.Undetermined, baseline.Result);
        Assert.Contains("fără jurnalul USN", baseline.Reason);
        Assert.DoesNotContain("stație instalată sau actualizată masiv", baseline.Reason);
        s.Fields["SystemMismatchRecentTotal"] = "99000";
        var af = AntiForensics.Evaluate([s], []).Single(c => c.Id == "AF08");
        Assert.Equal(AntiForensicResult.Undetermined, af.Result);
        Assert.Contains("stație instalată sau actualizată masiv", af.Reason);
        Assert.Contains(99000.ToString("N0"), af.Reason); Assert.Contains(20000.ToString("N0"), af.Reason);
    }

    private static TimelineEvent ChannelEv(string id, string path, string si, string fn)
    {
        var e = new TimelineEvent { Time = Timestamp.Unknown(), Source = "MFT", EvidenceId = "EV-MFT", Process = "", Path = path, Summary = "s", TimeSemantics = "t", TemporalType = TemporalType.Historical, Classification = Classification.Candidate, Confidence = Confidence.Low, Locator = "MFT#1" };
        e.Fields["Category"] = "system_mismatch_recent"; e.Fields["FileId"] = id; e.Fields["FileName"] = "x.dll"; e.Fields["IsDirectory"] = "false"; e.Fields["HasDosName"] = "false";
        e.Fields["SiCreated"] = si; e.Fields["SiModified"] = si; e.Fields["SiMftModified"] = "2026-11-13T18:28:53.9523090Z"; e.Fields["FnCreated"] = fn;
        return e;
    }

    private static TimelineEvent UsnOp(string id, string reason, DateTimeOffset at, long usn)
    {
        var e = new TimelineEvent { Time = Timestamp.FromUtc(at.UtcDateTime, "r", "t"), Source = "USN", EvidenceId = "EV-USN", Process = "", Path = "\\Windows\\System32\\x.dll", Summary = "u", TimeSemantics = "t", TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "USN=" + usn };
        e.Fields["Usn"] = usn.ToString(CultureInfo.InvariantCulture); e.Fields["FileName"] = "x.dll"; e.Fields["Reason"] = reason; e.Fields["FileId"] = id;
        return e;
    }

    [Fact]
    public void A_system32_file_from_the_channel_is_high_with_a_separate_usn_basic_info_change_and_never_medium_without_it()
    {
        const string id = "0x0001000000000064", si = "2019-03-14T09:26:53.1234567Z", fn = "2026-11-13T18:28:51.9316850Z";
        var at = new DateTimeOffset(2026, 11, 13, 18, 28, 51, TimeSpan.Zero);
        var mft = ChannelEv(id, "\\Windows\\System32\\x.dll", si, fn);
        var hit = Assert.Single(NtfsTimestomp.Detect([mft, UsnOp(id, "Data extend | File create | Close", at, 100), UsnOp(id, "Basic info change | Close", at.AddSeconds(2), 200)]));
        Assert.Equal(NtfsTimestomp.Level.High, hit.Level);
        Assert.Empty(NtfsTimestomp.Detect([mft]));                                                     // MFT only: not Medium, not Low
        Assert.Empty(NtfsTimestomp.Detect([mft, UsnOp(id, "Data extend | File create | Close", at, 100)]));   // USN without the separate change: nothing
    }
}
