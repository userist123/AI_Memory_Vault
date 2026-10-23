using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;
using static LogAnalyzer.Dfir.Tests.Synthetic.ShellItemBuilder;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// E7 slice 2: live collection of NTUSER.DAT / UsrClass.dat (VSS copy through a fake esentutl) and $Recycle.Bin\&lt;SID&gt;\$I* on a synthetic volume
/// in a temp folder, then the production pipeline parsing what was collected. Every probe of the slice 2 probe list has a test here; real VSS is not
/// used (the copy delegate and the shadow-copy list are the seams).
/// </summary>
public sealed class UserArtifactsCollectorTests : IDisposable
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string Sid2 = "S-1-5-21-1111111111-2222222222-3333333333-1002";
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 9, 2, 9, 30, 15, DateTimeKind.Utc);
    private static readonly DateTime Deleted = new(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);
    private const uint End = 0xFFFFFFFF;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_e7c_" + Guid.NewGuid().ToString("N"));
    private readonly string _vol, _profile;
    private readonly CaseWorkspace _ws;
    private readonly List<(string Src, string Dest)> _copies = [];

    public UserArtifactsCollectorTests()
    {
        Directory.CreateDirectory(_dir);
        _vol = Path.Combine(_dir, "vol");
        _profile = Path.Combine(_vol, "Users", "test");
        _ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "e7-colectare", TestScopes.Valid());
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    // ---- synthetic station ----

    private static byte[] UsrClass()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = root.Sub(@"Local Settings\Software\Microsoft\Windows\Shell\BagMRU");
        bag.Value("0", RegfBuilder.Binary, List(Root(ThisPc)));
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        return RegfBuilder.Build(root, "UsrClass.dat");
    }

    private static byte[] NtUser()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var rd = root.Sub(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs");
        byte[] recent = [.. Encoding.Unicode.GetBytes("raport.docx"), 0, 0, .. List(FileEntry(0x32, "raport.docx", 100, new DateTime(2026, 8, 20, 10, 0, 2)))];
        rd.Value("0", RegfBuilder.Binary, recent);
        rd.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        return RegfBuilder.Build(root, "NTUSER.DAT");
    }

    private static byte[] InfoV2(string path, long size = 7654321)
    {
        var text = Encoding.Unicode.GetBytes(path + "\0");
        var d = new byte[28 + text.Length];
        BinaryPrimitives.WriteInt64LittleEndian(d, 2);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16), Deleted.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), path.Length + 1);
        text.CopyTo(d, 28);
        return d;
    }

    private void Profile(string profileDir, bool usrClass = true)
    {
        Directory.CreateDirectory(profileDir);
        File.WriteAllBytes(Path.Combine(profileDir, "NTUSER.DAT"), NtUser());
        if (!usrClass) return;
        var w = Path.Combine(profileDir, "AppData", "Local", "Microsoft", "Windows");
        Directory.CreateDirectory(w);
        File.WriteAllBytes(Path.Combine(w, "UsrClass.dat"), UsrClass());
    }

    private string RecycleDir(string sid)
    {
        var d = Path.Combine(_vol, "$Recycle.Bin", sid);
        Directory.CreateDirectory(d);
        return d;
    }

    private string Info(string sid, string id, string original, int rLength = -1)
    {
        var d = RecycleDir(sid);
        var p = Path.Combine(d, "$I" + id);
        File.WriteAllBytes(p, InfoV2(original));
        if (rLength >= 0) File.WriteAllBytes(Path.Combine(d, "$R" + id), new byte[rLength]);
        return p;
    }

    private VssCopyResult FakeVss(string src, string dest, string logDir, string label, CancellationToken ct)
    {
        _copies.Add((src, dest));
        File.Copy(src, dest);
        return new VssCopyResult(0, $"esentutl.exe /y {src} /vss /d {dest}");
    }

    private UserArtifactsCollector Collector(IReadOnlyList<UserProfileEntry>? profiles = null, Func<string, string, string, string, CancellationToken, VssCopyResult>? vss = null,
        Func<IReadOnlySet<string>?>? shadows = null, Func<string, byte[]>? readInfo = null, int maxProfiles = UserArtifactsCollector.DefaultMaxProfiles,
        int maxPerSid = UserArtifactsCollector.DefaultMaxInfoPerSid, int maxTotal = UserArtifactsCollector.DefaultMaxInfoTotal) =>
        new(() => profiles ?? [new UserProfileEntry(Sid, _profile)], () => [_vol + Path.DirectorySeparatorChar], vss ?? FakeVss,
            shadows ?? (() => new HashSet<string>()), readInfo, maxProfiles, maxPerSid, maxTotal);

    private CollectorOutcome Collect(UserArtifactsCollector c, bool elevated = true, CancellationToken ct = default) =>
        c.Collect(new CollectorContext { Case = _ws, IsElevated = elevated }, ct);

    private static bool MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(10000);
        return p.ExitCode == 0 && Directory.Exists(link);
    }

    // ---- placement and end to end ----

    [Fact]
    public void Collector_is_in_the_full_profile_only_and_in_the_production_list()
    {
        var c = new UserArtifactsCollector();
        Assert.Equal(CollectionProfile.FullForensic, c.Profiles);
        Assert.True(c.RequiresElevation);
        Assert.Contains(InvestigationPipeline.AllCollectors, x => x is UserArtifactsCollector);
        Assert.DoesNotContain(InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & CollectionProfile.Standard) != 0), x => x is UserArtifactsCollector);
        Assert.Equal(RecycleBinInfoParser.CollectedBy, c.Name);
    }

    [Fact]
    public void Collected_hives_and_info_files_are_parsed_by_the_production_pipeline()
    {
        Profile(_profile);
        Info(Sid, "AB12CD.docx", @"C:\Users\test\Documents\plan.docx", rLength: 5);
        Info(Sid, "EF34GH.txt", @"C:\Users\test\Documents\note.txt");

        var o = Collect(Collector());

        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Errors);
        Assert.Equal(["ntuser_hive", "recycle_info", "recycle_info", "usrclass_hive"], o.Evidence.Select(e => e.SourceType).Order().ToArray());
        Assert.Equal(2, _copies.Count);                                               // the two hives went through the VSS copy, the $I files did not
        foreach (var e in o.Evidence)
        {
            Assert.Contains(Sid, e.Notes);
            Assert.Equal(Hashing.Sha256File(_ws.FullPath(e.StoredPath)), e.Sha256);  // SHA-256 at collection, of the copy
            Assert.Equal(RecycleBinInfoParser.CollectedBy, e.Collector);
        }
        Assert.Contains(o.Commands, c => c.Contains("/vss") && c.Contains("NTUSER.DAT"));
        // $R content is never copied into the case
        Assert.Empty(Directory.EnumerateFiles(_ws.Root, "$R*", SearchOption.AllDirectories));

        var r = new InvestigationPipeline().Run(_ws, CollectionProfile.FullForensic, collect: false);
        string ParserOf(string type) => r.Parsing.Single(p => p.EvidenceId == o.Evidence.Single(e => e.SourceType == type).EvidenceId).Parser;
        Assert.Equal("UserHiveParser", ParserOf("ntuser_hive"));
        Assert.Equal("ShellBagsParser", ParserOf("usrclass_hive"));
        var bins = r.Parsing.Where(p => o.Evidence.Any(e => e.SourceType == "recycle_info" && e.EvidenceId == p.EvidenceId)).ToList();
        Assert.Equal(2, bins.Count);
        Assert.All(bins, p => { Assert.Equal("RecycleBinInfoParser", p.Parser); Assert.Equal(EvidenceStatus.Success, p.Status); });

        var plan = r.Timeline.Single(e => e.Source == "RecycleBin" && e.Path.EndsWith("plan.docx"));
        Assert.Equal("present", plan.Fields["RContent"]);                            // recorded at collection, though $R is not in the case
        Assert.Equal("5", plan.Fields["RSize"]);
        Assert.Equal(Sid, plan.Fields["OwnerSid"]);
        Assert.Contains("la colectare", plan.Summary);
        var note = r.Timeline.Single(e => e.Source == "RecycleBin" && e.Path.EndsWith("note.txt"));
        Assert.Equal("absent", note.Fields["RContent"]);
        Assert.Contains(r.Timeline, e => e.Source == "RecentDocs");
        Assert.Contains(r.Timeline, e => e.EvidenceId == o.Evidence.Single(x => x.SourceType == "usrclass_hive").EvidenceId);
    }

    [Fact]
    public void An_imported_item_does_not_trust_a_collection_note()
    {
        var item = new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "s", SourceType = "recycle_info", Collector = "Import", AcquisitionMethod = "imported", Notes = RecycleBinInfoParser.RNote("present", 9) };
        Assert.Null(RecycleBinInfoParser.CollectedRState(item));
        var collected = new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "s", SourceType = "recycle_info", Collector = RecycleBinInfoParser.CollectedBy, AcquisitionMethod = "acquired", Notes = "x. " + RecycleBinInfoParser.RNote("present", 9) };
        Assert.Equal(("present", "9"), RecycleBinInfoParser.CollectedRState(collected));
    }

    // ---- gaps ----

    [Fact]
    public void Not_elevated_collects_nothing_and_says_why()
    {
        Profile(_profile);
        var o = Collect(Collector(), elevated: false);
        Assert.Equal(EvidenceStatus.NotAvailable, o.Status);
        Assert.Contains("necesită drepturi de administrator", Assert.Single(o.Errors));
        Assert.Empty(_copies);
    }

    [Fact]
    public void A_failed_vss_copy_is_a_reported_gap_and_leaves_no_partial_file()
    {
        Profile(_profile);
        var o = Collect(Collector(vss: (s, d, l, n, ct) => { File.WriteAllText(d, "partial"); return new VssCopyResult(5, "esentutl.exe /y " + s + " /vss /d " + d); }));
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Equal(2, o.Errors.Count(e => e.Contains("copierea din copia de umbră a eșuat") && e.Contains("cod 5")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_ws.Root, "Raw"), "*.dat", SearchOption.AllDirectories));
    }

    [Fact]
    public void Missing_profile_folder_and_missing_hive_are_reported()
    {
        Profile(_profile, usrClass: false);
        var o = Collect(Collector(profiles: [new UserProfileEntry(Sid, _profile), new UserProfileEntry(Sid2, Path.Combine(_vol, "Users", "gone"))]));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Errors, e => e.Contains(Sid2) && e.Contains("folderul profilului lipsește"));
        Assert.Contains(o.Errors, e => e.StartsWith("UsrClass.dat (" + Sid + ")") && e.Contains("hive-ul lipsește"));
        Assert.Single(o.Evidence, e => e.SourceType == "ntuser_hive");
    }

    [Fact]
    public void Service_sids_are_skipped_unless_a_hive_exists()
    {
        var sys = Path.Combine(_vol, "Windows", "system32", "config", "systemprofile");
        Profile(sys, usrClass: false);
        var local = Path.Combine(_vol, "Windows", "ServiceProfiles", "LocalService");
        Directory.CreateDirectory(local);
        var o = Collect(Collector(profiles: [new("S-1-5-18", sys), new("S-1-5-19", local), new("S-1-5-20", Path.Combine(_vol, "nowhere"))]));
        Assert.Single(o.Evidence, e => e.SourceType == "ntuser_hive" && e.Notes.Contains("S-1-5-18"));
        Assert.DoesNotContain(o.Errors, e => e.Contains("S-1-5-19") || e.Contains("S-1-5-20"));
        Assert.Contains(o.Commands, c => c.StartsWith("S-1-5-19") && c.Contains("omis"));
        Assert.Contains(o.Commands, c => c.StartsWith("S-1-5-20") && c.Contains("omis"));
    }

    [Fact]
    public void An_unreadable_profile_list_is_a_gap_and_the_recycle_bin_is_still_collected()
    {
        Info(Sid, "AB12CD.docx", @"C:\Users\test\a.docx");
        var c = new UserArtifactsCollector(() => throw new UnauthorizedAccessException("acces refuzat"), () => [_vol], FakeVss, () => new HashSet<string>());
        var o = Collect(c);
        Assert.Contains(o.Errors, e => e.StartsWith("Lista profilurilor (ProfileList) nu a putut fi citită"));
        Assert.Single(o.Evidence, e => e.SourceType == "recycle_info");
    }

    // ---- probe: names that are not SIDs ----

    [Fact]
    public void Profile_keys_and_recycle_folders_that_are_not_sids_are_skipped_with_a_note()
    {
        Profile(_profile);
        Info(Sid, "AB12CD.docx", @"C:\Users\test\a.docx");
        var odd = Path.Combine(_vol, "$Recycle.Bin", "NotASid");
        Directory.CreateDirectory(odd);
        File.WriteAllBytes(Path.Combine(odd, "$IZZZZZZ.txt"), InfoV2(@"C:\x.txt"));
        var o = Collect(Collector(profiles: [new(Sid, _profile), new(Sid + ".bak", _profile)]));
        Assert.Contains(o.Errors, e => e.Contains(Sid + ".bak") && e.Contains("nu este un SID"));
        Assert.Contains(o.Errors, e => e.Contains("NotASid") && e.Contains("nu este un SID"));
        Assert.Single(o.Evidence, e => e.SourceType == "recycle_info");
        Assert.Equal(2, _copies.Count);
    }

    [Theory]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1001", true)]
    [InlineData("S-1-5-18", true)]
    [InlineData("S-1-5-21-1-2-3-1001.bak", false)]
    [InlineData("..", false)]
    [InlineData("S-1-5-21-1\\..\\x", false)]
    [InlineData("desktop.ini", false)]
    [InlineData("", false)]
    public void Sid_check(string name, bool ok) => Assert.Equal(ok, UserArtifactsCollector.IsSid(name));

    // ---- probe: path traversal in names ----

    [Theory]
    [InlineData("$IAB12CD.docx", true)]
    [InlineData("$IAB12CD", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("$I..\\..\\x", false)]
    [InlineData("$I../x", false)]
    [InlineData("$IAB:ads", false)]
    [InlineData("$IAB12CD.", false)]
    [InlineData("", false)]
    public void Child_name_check(string name, bool ok) => Assert.Equal(ok, UserArtifactsCollector.IsSafeChildName(name));

    [Theory]
    [InlineData(@"C:\Users\test", "")]
    [InlineData(@"C:\Users\..\Windows", "„..”")]
    [InlineData(@"\\server\share\test", "nu este locală")]
    [InlineData(@"Users\test", "nu este o cale completă")]
    [InlineData(@"%SystemDrive%\Users\test", "nu este o cale completă")]
    [InlineData(@"C:\Users\%USERNAME%", "neexpandată")]
    [InlineData("", "lipsește")]
    public void Profile_path_check(string path, string expected)
    {
        var why = UserArtifactsCollector.UnsafeProfilePath(path);
        if (expected.Length == 0) Assert.Equal("", why);
        else Assert.Contains(expected, why);
    }

    [Fact]
    public void A_profile_path_with_dot_dot_is_refused_before_any_copy()
    {
        Profile(_profile);
        var o = Collect(Collector(profiles: [new(Sid, Path.Combine(_vol, "Users", "x", "..", "test"))]));
        Assert.Contains(o.Errors, e => e.Contains(Sid) && e.Contains("„..”"));
        Assert.Empty(_copies);
    }

    // ---- probe: reparse points are never followed ----

    [Fact]
    public void A_junction_in_the_profile_path_is_not_followed()
    {
        var outside = Path.Combine(_dir, "outside", "victim");
        Profile(outside);
        Directory.CreateDirectory(Path.Combine(_vol, "Users"));
        Assert.True(MakeJunction(_profile, outside), "the junction could not be created");
        var o = Collect(Collector());
        Assert.Empty(_copies);
        Assert.Equal(2, o.Errors.Count(e => e.Contains("reparse point") && e.Contains("nu este urmată")));
        Assert.Empty(o.Evidence);
        Directory.Delete(_profile);
    }

    [Fact]
    public void A_junction_below_the_profile_on_the_way_to_usrclass_is_not_followed()
    {
        Profile(_profile, usrClass: false);
        var outside = Path.Combine(_dir, "outside", "Local");
        Directory.CreateDirectory(Path.Combine(outside, "Microsoft", "Windows"));
        File.WriteAllBytes(Path.Combine(outside, "Microsoft", "Windows", "UsrClass.dat"), UsrClass());
        Directory.CreateDirectory(Path.Combine(_profile, "AppData"));
        var link = Path.Combine(_profile, "AppData", "Local");
        Assert.True(MakeJunction(link, outside), "the junction could not be created");
        var o = Collect(Collector());
        Assert.Single(_copies);                                                    // NTUSER.DAT only
        Assert.Contains(o.Errors, e => e.StartsWith("UsrClass.dat") && e.Contains("reparse point"));
        Directory.Delete(link);
    }

    [Fact]
    public void Junctions_under_the_recycle_bin_are_not_followed()
    {
        var outside = Path.Combine(_dir, "outside", "bin");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "$IAAAAAA.txt"), InfoV2(@"C:\secret.txt"));
        RecycleDir(Sid);
        var link = Path.Combine(_vol, "$Recycle.Bin", Sid2);
        Assert.True(MakeJunction(link, outside), "the junction could not be created");
        var inner = Path.Combine(RecycleDir(Sid), "$IBBBBBB");
        Assert.True(MakeJunction(inner, outside), "the junction could not be created");
        var o = Collect(Collector(profiles: []));
        Assert.Empty(o.Evidence);
        Assert.Contains(o.Errors, e => e.Contains(Sid2) && e.Contains("reparse point"));
        Assert.Contains(o.Errors, e => e.Contains("$IBBBBBB") && e.Contains("reparse point"));
        Directory.Delete(link);
        Directory.Delete(inner);
    }

    [Fact]
    public void A_recycle_bin_that_is_a_junction_is_not_followed()
    {
        var outside = Path.Combine(_dir, "outside", "rb");
        Directory.CreateDirectory(Path.Combine(outside, Sid));
        File.WriteAllBytes(Path.Combine(outside, Sid, "$IAAAAAA.txt"), InfoV2(@"C:\secret.txt"));
        Directory.CreateDirectory(_vol);
        var link = Path.Combine(_vol, "$Recycle.Bin");
        Assert.True(MakeJunction(link, outside), "the junction could not be created");
        var o = Collect(Collector(profiles: []));
        Assert.Empty(o.Evidence);
        Assert.Contains(o.Errors, e => e.Contains("$Recycle.Bin este o legătură"));
        Directory.Delete(link);
    }

    // ---- probe: caps ----

    [Fact]
    public void Profile_cap_is_reported()
    {
        Profile(_profile);
        var p2 = Path.Combine(_vol, "Users", "test2");
        Profile(p2);
        var o = Collect(Collector(profiles: [new(Sid, _profile), new(Sid2, p2)], maxProfiles: 1));
        Assert.Equal(2, _copies.Count);
        Assert.Contains(o.Errors, e => e.Contains("Limita profilurilor colectate (1)") && e.Contains("necolectate: 1"));
    }

    [Fact]
    public void Per_sid_and_total_info_caps_are_reported()
    {
        foreach (var id in new[] { "AAAAAA", "BBBBBB", "CCCCCC" }) Info(Sid, id + ".txt", @"C:\Users\test\" + id + ".txt");
        foreach (var id in new[] { "DDDDDD", "EEEEEE" }) Info(Sid2, id + ".txt", @"C:\Users\test2\" + id + ".txt");
        var o = Collect(Collector(profiles: [], maxPerSid: 2, maxTotal: 3));
        Assert.Equal(3, o.Evidence.Count);
        Assert.Equal(2, o.Evidence.Count(e => e.Notes.Contains(Sid)));
        Assert.Contains(o.Errors, e => e.Contains("limita de fișiere $I pentru un SID (2)"));
        Assert.Contains(o.Errors, e => e.Contains("Limita totală de fișiere $I (3)"));
    }

    [Fact]
    public void An_info_file_over_the_parser_input_limit_is_not_copied()
    {
        var d = RecycleDir(Sid);
        File.WriteAllBytes(Path.Combine(d, "$IHUGE00.bin"), new byte[RecycleBinInfoParser.MaxInfoFileBytes + 1]);
        var o = Collect(Collector(profiles: []));
        Assert.Empty(o.Evidence);
        Assert.Contains(o.Errors, e => e.Contains("$IHUGE00.bin") && e.Contains($"peste limita unui $I ({RecycleBinInfoParser.MaxInfoFileBytes} octeți)"));
    }

    [Fact]
    public void Production_caps_are_the_documented_values()
    {
        Assert.Equal(200, UserArtifactsCollector.DefaultMaxProfiles);
        Assert.Equal(10_000, UserArtifactsCollector.DefaultMaxInfoPerSid);
        Assert.Equal(20_000, UserArtifactsCollector.DefaultMaxInfoTotal);
        Assert.Equal(128 * 1024, RecycleBinInfoParser.MaxInfoFileBytes);
    }

    // ---- probe: the snapshot is checked after the collector, also on failure and cancellation ----

    [Fact]
    public void A_shadow_copy_left_after_the_collector_is_reported_and_audited()
    {
        Profile(_profile);
        int calls = 0;
        var o = Collect(Collector(shadows: () => ++calls == 1 ? new HashSet<string> { "{OLD}" } : new HashSet<string> { "{OLD}", "{NEW-LEFT}" }));
        Assert.Equal(2, calls);
        Assert.Contains(o.Errors, e => e.Contains("{NEW-LEFT}") && e.Contains("a rămas o copie de umbră"));
        Assert.DoesNotContain(o.Errors, e => e.Contains("{OLD}"));
        Assert.Contains("vss.leftover", File.ReadAllText(_ws.AppAuditLogPath));
    }

    [Fact]
    public void Cancellation_during_a_vss_copy_still_checks_for_a_leftover_snapshot()
    {
        Profile(_profile);
        int calls = 0;
        using var cts = new CancellationTokenSource();
        var c = Collector(shadows: () => ++calls == 1 ? new HashSet<string>() : new HashSet<string> { "{STUCK}" },
                          vss: (s, d, l, n, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return new VssCopyResult(0, ""); });
        Assert.Throws<OperationCanceledException>(() => Collect(c, ct: cts.Token));
        Assert.Equal(2, calls);
        Assert.Contains("vss.leftover", File.ReadAllText(_ws.AppAuditLogPath));
    }

    [Fact]
    public void An_unlistable_shadow_list_is_reported_only_when_vss_was_used()
    {
        Info(Sid, "AB12CD.docx", @"C:\Users\test\a.docx");
        var noVss = Collect(Collector(profiles: [], shadows: () => null));
        Assert.DoesNotContain(noVss.Errors, e => e.Contains("copie de umbră"));
        Profile(_profile);
        var withVss = Collect(Collector(shadows: () => null));
        Assert.Contains(withVss.Errors, e => e.Contains("Nu s-a putut verifica dacă după copiere a rămas o copie de umbră"));
    }

    // ---- probe: a file changing during the copy ----

    [Fact]
    public void An_info_file_that_changes_during_the_copy_keeps_the_first_read_and_is_reported()
    {
        var src = Info(Sid, "AB12CD.docx", @"C:\Users\test\a.docx");
        var first = InfoV2(@"C:\Users\test\a.docx");
        int reads = 0;
        var o = Collect(Collector(profiles: [], readInfo: p => ++reads == 1 ? first : InfoV2(@"C:\Users\test\b.docx")));
        var e = Assert.Single(o.Evidence);
        Assert.Equal(Hashing.Sha256(first), e.Sha256, ignoreCase: true);
        Assert.Contains("s-a schimbat în timpul copierii", e.Notes);
        Assert.Contains(o.Errors, x => x.Contains("s-a schimbat în timpul copierii"));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Equal(src, e.OriginalPath);
    }

    [Fact]
    public void An_unchanged_info_file_says_so()
    {
        Info(Sid, "AB12CD.docx", @"C:\Users\test\a.docx");
        var e = Assert.Single(Collect(Collector(profiles: [])).Evidence);
        Assert.Contains("A doua citire a sursei este identică", e.Notes);
    }

    // ---- $R metadata ----

    [Fact]
    public void R_metadata_is_existence_and_size_only()
    {
        var d = RecycleDir(Sid);
        Info(Sid, "AAAAAA.txt", @"C:\a.txt", rLength: 3);
        Info(Sid, "BBBBBB", @"C:\folder");
        Directory.CreateDirectory(Path.Combine(d, "$RBBBBBB"));
        Info(Sid, "CCCCCC.txt", @"C:\c.txt");
        Assert.Equal(RecycleBinInfoParser.RNote("present", 3), UserArtifactsCollector.RNoteFor(new FileInfo(Path.Combine(d, "$IAAAAAA.txt"))));
        Assert.Equal(RecycleBinInfoParser.RNote("present_folder"), UserArtifactsCollector.RNoteFor(new FileInfo(Path.Combine(d, "$IBBBBBB"))));
        Assert.Equal(RecycleBinInfoParser.RNote("absent"), UserArtifactsCollector.RNoteFor(new FileInfo(Path.Combine(d, "$ICCCCCC.txt"))));
        var o = Collect(Collector(profiles: []));
        Assert.Equal(3, o.Evidence.Count);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_ws.Root, "$R*", SearchOption.AllDirectories));
    }
}
