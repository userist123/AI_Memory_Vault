using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Defender DetectionHistory: the parser on a real EICAR record from the lab (compared with its 1116 event), the threat model of an untrusted binary
/// (huge or overrunning lengths, endless chains, strings without NUL, invalid FILETIME, damaged key block, control characters), the file routing,
/// the collector and the DEF-HISTORY-ONLY rule.
/// </summary>
public sealed class DefenderDetectionHistoryTests : IDisposable
{
    private const string RealId = "2169D50A-AB8F-4CC5-992A-DEAFEE4FAE82";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_dh_" + Guid.NewGuid().ToString("N"));
    public DefenderDetectionHistoryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(_dir, true); }

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Corpus", "DefenderDetectionHistory", RealId);
    private static byte[] Real => File.ReadAllBytes(Fixture);
    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, data); return p; }
    private static EvidenceItem Item(string path, string type = DefenderDetectionHistoryParser.SourceTypeName) =>
        new() { EvidenceId = "EV-D", CaseId = "C", Source = "test", SourceType = type, StoredPath = path };

    // ---- a small writer for synthetic files ----

    private static byte[] Rec(uint type, byte[] value, uint? declaredLen = null)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(declaredLen ?? (uint)value.Length)); b.AddRange(BitConverter.GetBytes(type)); b.AddRange(value);
        while (b.Count % 8 != 0) b.Add(0);
        return [.. b];
    }
    private static byte[] Utf16Z(string s) => Encoding.Unicode.GetBytes(s + "\0");
    private static byte[] Str(string s) => Rec(0x15, Utf16Z(s));
    private static byte[] U32(uint v) => Rec(0x06, BitConverter.GetBytes(v));
    private static byte[] Ft(long v) => Rec(0x0A, BitConverter.GetBytes(v));
    private static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
    private static byte[] KeyText(string k, string v) => Cat(BitConverter.GetBytes(Utf16Z(k).Length), Utf16Z(k), BitConverter.GetBytes(6u), BitConverter.GetBytes(Utf16Z(v).Length), Utf16Z(v));
    private static byte[] Block(params byte[][] pairs) => Rec(0x28, Cat(new byte[0x18], Cat(pairs)));
    private static long FileTime(DateTime utc) => utc.ToFileTimeUtc();

    private static byte[] Synthetic(string name, string[] resourcePaths, byte[]? block = null, long? when = null, string user = @"LAB\admin", string process = @"C:\x\p.exe")
    {
        var parts = new List<byte[]>
        {
            Rec(0x08, BitConverter.GetBytes(77UL)), Rec(0x1E, Guid.Parse("11111111-2222-3333-4444-555555555555").ToByteArray()),
            Str("Magic.Version:1.2"), Str(name), U32(0), U32(4), U32(30),
        };
        foreach (var p in resourcePaths) { parts.Add(Str("Magic.Version:1.2")); parts.Add(Str("file")); parts.Add(Str(p)); parts.Add(U32(1)); }
        if (block is not null) parts.Add(block);
        if (when is { } w) parts.Add(Ft(w));
        parts.Add(Rec(0x1E, new byte[16])); parts.Add(Str(user)); parts.Add(U32(3)); parts.Add(Str(process));
        return Cat([.. parts]);
    }

    // ---- the real record ----

    [Fact]
    public void The_real_EICAR_record_gives_every_field_of_its_1116_event()
    {
        var path = Write(RealId, Real);
        var sink = new ListSink();
        var r = new DefenderDetectionHistoryParser().Parse(Item(path), path, sink, default);
        Assert.Equal(1, r.Records);
        Assert.Empty(r.Gaps);
        var e = Assert.Single(sink.Events);
        Assert.Equal(DefenderDetectionHistoryParser.EventSource, e.Source);
        string F(string k) => e.Fields[k];
        Assert.Equal("{" + RealId + "}", F("Detection ID"));
        Assert.Equal("Virus:DOS/EICAR_Test_File", F("Threat Name"));
        Assert.Equal("2147519003", F("Threat ID"));
        Assert.Equal("5", F("Severity ID"));
        Assert.Equal("42", F("Category ID"));
        Assert.Equal("3", F("Source ID"));
        Assert.Equal(@"file:_C:\LA\eicar\eicar-test.com", F("Path"));
        Assert.Equal(@"LAB\Administrator", F("Detection User"));
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", F("Process Name"));
        Assert.Equal("275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f", F("SHA256"));
        Assert.Equal("3395856ce81f2b7382dee72602f798b642f14140", F("SHA1"));
        Assert.Equal("44d88612fea8a8f36de82e1278abb02f", F("MD5"));
        Assert.Equal("68", F("Size"));
        Assert.Equal(new DateTimeOffset(DateTime.FromFileTimeUtc(134387211536495606)), e.Time.Utc);
        Assert.Equal("2026-11-09T18:05:53.6495606Z", e.Time.Utc!.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"));
        Assert.Equal("powershell.exe", e.Process);
        Assert.Equal("275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f", e.Hash);
    }

    [Fact]
    public void The_same_record_rewritten_by_Defender_after_the_log_was_cleared_keeps_the_detection_time_and_every_field()
    {
        // Real lab file: Defender rewrote the record (2360 -> 2400 bytes) after the Operational log was cleared; its FILETIME after the
        // key block moved to 18:06:06.5, while ThreatTrackingStartTime stayed at the detection (18:05:53.6487535, 1116 says 18:05:53.649).
        var bytes = File.ReadAllBytes(Fixture + ".after-log-clear");
        var path = Write(RealId, bytes);
        var sink = new ListSink();
        new DefenderDetectionHistoryParser().Parse(Item(path), path, sink, default);
        var e = Assert.Single(sink.Events);
        Assert.Equal(new DateTimeOffset(DateTime.FromFileTimeUtc(134387211536487535)), e.Time.Utc);
        Assert.Contains("ThreatTrackingStartTime", e.TimeSemantics);
        Assert.Equal("{" + RealId + "}", e.Fields["Detection ID"]);
        Assert.Equal("Virus:DOS/EICAR_Test_File", e.Fields["Threat Name"]);
        Assert.Equal(@"LAB\Administrator", e.Fields["Detection User"]);
        Assert.Equal("275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f", e.Fields["SHA256"]);
        Assert.True(e.Time.Utc!.Value - new DateTimeOffset(2026, 11, 9, 18, 5, 53, 649, TimeSpan.Zero) < TimeSpan.FromMilliseconds(2));
    }

    [Fact]
    public void The_real_record_decodes_completely_with_the_file_ending_on_a_record_boundary()
    {
        var d = DefenderDetectionHistoryParser.Decode(Real);
        Assert.True(d.Complete);
        Assert.True(d.HasKeyBlock);
        Assert.Equal(@"C:\LA\eicar\eicar-test.com", Assert.Single(d.Resources).Path);
        Assert.Equal("file", d.Resources[0].Type);
    }

    [Fact]
    public void The_file_is_routed_by_content_and_declared_type_and_other_extensionless_files_are_not_taken()
    {
        var path = Write(RealId, Real);
        Assert.Equal(DefenderDetectionHistoryParser.FingerprintName, EvidenceFingerprint.Detect(path));
        var sel = WindowsParsers.Registry.Select(Item(path), path);
        Assert.Equal("DefenderDetectionHistoryParser", Assert.Single(sel.Parsers).Descriptor.ParserId);
        Assert.Equal(DefenderDetectionHistoryParser.SourceTypeName, InvestigationPipeline.ImportType(path));

        var other = Write("other\\" + Guid.NewGuid(), Encoding.ASCII.GetBytes(new string('x', 200)));
        Assert.Equal("file", InvestigationPipeline.ImportType(other));
        Assert.DoesNotContain(WindowsParsers.Registry.Candidates(Item(other, "file")), p => p.Descriptor.ParserId == "DefenderDetectionHistoryParser");
        var named = Write("plain.bin", Real);   // right content, but not a GUID name and no declared type: not imported as DetectionHistory
        Assert.Equal("file", InvestigationPipeline.ImportType(named));
        Assert.Empty(WindowsParsers.Registry.Candidates(Item(named, "file")));
    }

    // ---- P1-P7 ----

    [Fact]
    public void P2_P3_the_real_file_cut_at_every_offset_never_throws_and_gives_at_most_one_detection()
    {
        var real = Real;
        for (int n = 0; n <= real.Length; n++)
        {
            var cut = real.AsSpan(0, n).ToArray();
            var d = DefenderDetectionHistoryParser.Decode(cut);   // must not throw
            Assert.True(d.Records <= DefenderDetectionHistoryParser.MaxRecords);
            var p = Write("cut", cut);
            var sink = new ListSink();
            var r = new DefenderDetectionHistoryParser().Parse(Item(p), p, sink, default);
            Assert.True(sink.Events.Count <= 1, $"cut at {n}");
            Assert.True(r.Status != EvidenceStatus.Failed || sink.Events.Count == 0, $"cut at {n}");
        }
    }

    [Fact]
    public void P1_a_length_of_FFFFFFFF_or_one_that_overruns_the_file_stops_cleanly_and_keeps_what_was_read()
    {
        var real = Real;
        var bad = (byte[])real.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(0x58), 0xFFFFFFFF);   // the threat name record
        var d = DefenderDetectionHistoryParser.Decode(bad);
        Assert.False(d.Complete);
        Assert.Equal("{" + RealId + "}", d.DetectionId);
        Assert.Equal("", d.ThreatName);          // not read, not invented
        Assert.Equal(2147519003, d.ThreatId);

        foreach (var len in new uint[] { 0xFFFFFFF8, 0x80000000, 0x7FFFFFFF, 0xFFFFFFFF, 0x100000, (uint)real.Length })
        {
            var b = (byte[])real.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), len);
            Assert.NotNull(DefenderDetectionHistoryParser.Decode(b));
        }
        var huge = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(huge, 0xFFFFFFFF);
        Assert.Equal("", DefenderDetectionHistoryParser.Decode(huge).DetectionId);
    }

    [Fact]
    public void P3_a_chain_of_empty_records_is_capped_and_a_file_over_the_size_cap_is_described_not_read()
    {
        var many = new byte[8 * (DefenderDetectionHistoryParser.MaxRecords + 500)];
        var d = DefenderDetectionHistoryParser.Decode(many);   // len 0, type 0, 8 bytes each: progress is 8
        Assert.Equal(DefenderDetectionHistoryParser.MaxRecords, d.Records);
        Assert.False(d.Complete);

        var big = Real.Concat(new byte[DefenderDetectionHistoryParser.MaxFileBytes]).ToArray();
        var p = Write("big", big);
        var sink = new ListSink();
        var r = new DefenderDetectionHistoryParser().Parse(Item(p), p, sink, default);
        Assert.Empty(sink.Events);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.NotEmpty(r.Gaps);
    }

    [Fact]
    public void P4_strings_without_NUL_with_odd_length_or_broken_surrogates_are_decoded_without_an_exception_and_capped()
    {
        var noNul = Cat(Rec(0x08, BitConverter.GetBytes(1UL)), Rec(0x1E, Guid.NewGuid().ToByteArray()), Str("Magic.Version:1.2"), Rec(0x15, Encoding.Unicode.GetBytes("NoTerminator")));
        Assert.Equal("NoTerminator", DefenderDetectionHistoryParser.Decode(noNul).ThreatName);

        var odd = Cat(Rec(0x08, BitConverter.GetBytes(1UL)), Rec(0x1E, Guid.NewGuid().ToByteArray()), Rec(0x15, [.. Utf16Z("AB"), 0x41, 0x00, 0x42]));   // odd length: the last byte is dropped
        Assert.Equal("AB", DefenderDetectionHistoryParser.Decode(odd).ThreatName);

        var surrogate = Cat(Rec(0x08, BitConverter.GetBytes(1UL)), Rec(0x1E, Guid.NewGuid().ToByteArray()), Rec(0x15, [0x3D, 0xD8, 0x41, 0x00, 0, 0]));   // lone high surrogate
        var s = DefenderDetectionHistoryParser.Decode(surrogate).ThreatName;
        Assert.Equal("\uFFFDA", s);

        var longName = Cat(Rec(0x08, BitConverter.GetBytes(1UL)), Rec(0x1E, Guid.NewGuid().ToByteArray()), Str(new string('Z', 100_000)));
        Assert.Equal(DefenderDetectionHistoryParser.MaxStringChars, DefenderDetectionHistoryParser.Decode(longName).ThreatName.Length);
    }

    [Fact]
    public void P5_a_FILETIME_outside_the_valid_range_is_no_time_and_the_start_time_key_is_the_fallback()
    {
        var real = Real;
        var bad = (byte[])real.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(bad.AsSpan(0x708 + 8), -1);   // the detection time after the block
        var d = DefenderDetectionHistoryParser.Decode(bad);
        Assert.Equal("ThreatTrackingStartTime", d.TimeSource);
        Assert.Equal(DateTime.FromFileTimeUtc(134387211536487535), d.DetectionTimeUtc);

        var zeroed = Synthetic("T", [@"C:\a.exe"], Block(KeyText("ThreatTrackingSha1", "ab")), when: long.MaxValue);
        var z = DefenderDetectionHistoryParser.Decode(zeroed);
        Assert.Null(z.DetectionTimeUtc);

        var p = Write("badtime", Synthetic("T", [@"C:\a.exe"], Block(KeyText("ThreatTrackingSha1", "ab")), when: 0x7FFFFFFFFFFFFFFF));
        var sink = new ListSink();
        new DefenderDetectionHistoryParser().Parse(Item(p), p, sink, default);
        Assert.False(Assert.Single(sink.Events).Time.IsKnown);
    }

    [Fact]
    public void P6_a_damaged_key_block_stops_at_the_damage_and_keeps_the_keys_read_before_it()
    {
        var unknownType = Block(KeyText("ThreatTrackingSha1", "aa11"), Cat(BitConverter.GetBytes(Utf16Z("Weird").Length), Utf16Z("Weird"), BitConverter.GetBytes(99u), new byte[8]), KeyText("ThreatTrackingMD5", "never-read"));
        var d = DefenderDetectionHistoryParser.Decode(Synthetic("T", [@"C:\a.exe"], unknownType, FileTime(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))));
        Assert.Equal("aa11", d.Sha1);
        Assert.Equal("", d.Md5);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), d.DetectionTimeUtc);   // the records after the block are still read

        foreach (var keyLen in new uint[] { 1, 3, 0, 0xFFFFFFFF, 0x7FFFFFFF, 0x10000 })   // odd, zero, negative as int, past the block
        {
            var bad = Block(KeyText("ThreatTrackingSha1", "bb22"), BitConverter.GetBytes(keyLen), new byte[8]);
            var x = DefenderDetectionHistoryParser.Decode(Synthetic("T", [@"C:\a.exe"], bad, FileTime(DateTime.UtcNow)));
            Assert.Equal("bb22", x.Sha1);
        }
        var real = Real;
        for (int i = 0x1f8; i < 0x1f8 + 0x50d; i += 7)   // a corrupted byte anywhere in the block
        {
            var c = (byte[])real.Clone(); c[i] ^= 0xFF;
            Assert.NotNull(DefenderDetectionHistoryParser.Decode(c));
        }
    }

    [Fact]
    public void Without_a_key_block_the_fields_that_depend_on_it_stay_empty()
    {
        var d = DefenderDetectionHistoryParser.Decode(Real.AsSpan(0, 0x1f0));
        Assert.False(d.HasKeyBlock);
        Assert.Equal("Virus:DOS/EICAR_Test_File", d.ThreatName);
        Assert.Null(d.DetectionTimeUtc);
        Assert.Equal("", d.User); Assert.Equal("", d.Process); Assert.Equal("", d.Sha256);
    }

    [Fact]
    public void Several_resources_are_read_and_joined_in_the_1116_format()
    {
        var d = DefenderDetectionHistoryParser.Decode(Synthetic("T", [@"C:\a.zip", @"C:\a.zip->b.exe"]));
        Assert.Equal(@"file:_C:\a.zip; file:_C:\a.zip->b.exe", d.PathText);
    }

    [Fact]
    public void P7_control_characters_and_RLO_in_a_path_or_name_never_reach_a_title_or_summary_unchanged()
    {
        var evil = "C:\\x\\\u202Etxt.exe\r\nFAKE: line\u0001";
        var p = Write("evil", Synthetic("Trojan\r\nX\u202E", [evil]));
        var sink = new ListSink();
        new DefenderDetectionHistoryParser().Parse(Item(p), p, sink, default);
        var e = Assert.Single(sink.Events);
        foreach (var text in new[] { e.Summary, e.Path, e.Process, e.User })
            Assert.DoesNotContain(text, c => c is '\u202E' or '\r' or '\n' or '\u0001');
        Assert.Contains("U+202E", e.Summary);   // the marker says that characters were removed

        var f = Assert.Single(Run([e]));
        foreach (var text in new[] { f.Title, f.PlainWhat, f.Description })
            Assert.DoesNotContain(text, c => c is '\u202E' or '\r' or '\n' or '\u0001');
    }

    [Fact]
    public void P8_the_path_in_the_record_is_only_text_the_file_it_names_is_never_opened()
    {
        var target = Write("canary.txt", [1, 2, 3]);
        using var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);   // an attempt to open it would fail or be noticed
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var named in new[] { target, @"\\nonexistent-host.invalid\share\a.exe", @"\\.\PhysicalDrive0", @"C:\Windows\System32" })
        {
            var p = Write("canary-record", Synthetic("T", [named]));
            var sink = new ListSink();
            var r = new DefenderDetectionHistoryParser().Parse(Item(p), p, sink, default);
            Assert.Equal(1, r.Records);
            Assert.Contains(named, Assert.Single(sink.Events).Fields["Path"]);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "a network path was probably contacted");
    }

    // ---- collector ----

    [Fact]
    public void The_collector_copies_GUID_named_files_skips_the_rest_and_reports_a_missing_folder_without_failing()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "dh", TestScopes.Valid());
        var root = Path.Combine(_dir, "DetectionHistory");
        Write("DetectionHistory\\17\\" + RealId, Real);
        Write("DetectionHistory\\17\\not-a-guid.txt", [1, 2, 3]);
        Write("DetectionHistory\\18\\" + Guid.NewGuid(), new byte[DefenderDetectionHistoryParser.MaxFileBytes + 1]);
        var o = new CollectorOutcome();
        DefenderDetectionHistoryCollector.CollectFrom(ws, root, o, default);
        var item = Assert.Single(o.Evidence);
        Assert.Equal(DefenderDetectionHistoryParser.SourceTypeName, item.SourceType);
        Assert.Contains(o.Errors, e => e.Contains("depășește limita"));

        var c = new DefenderDetectionHistoryCollector();
        Assert.Equal(DefenderDetectionHistoryParser.CollectedBy, c.Name);
        Assert.Contains(InvestigationPipeline.AllCollectors, x => x is DefenderDetectionHistoryCollector);
        var missing = new CollectorOutcome();
        DefenderDetectionHistoryCollector.CollectFrom(ws, Path.Combine(_dir, "nothing-here"), missing, default);
        Assert.Empty(missing.Evidence);
    }

    [Fact]
    public void The_collector_does_not_follow_a_junction_inside_the_folder()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "dhj", TestScopes.Valid());
        var root = Path.Combine(_dir, "DetectionHistory");
        Directory.CreateDirectory(root);
        var other = Path.GetDirectoryName(Write("elsewhere\\" + RealId, Real))!;
        var junction = Path.Combine(root, "17");
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{other}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mk.WaitForExit();
        try
        {
            Assert.True(File.GetAttributes(junction).HasFlag(FileAttributes.ReparsePoint), "the test could not create a junction");
            var o = new CollectorOutcome();
            DefenderDetectionHistoryCollector.CollectFrom(ws, root, o, default);
            Assert.Empty(o.Evidence);
        }
        finally { Directory.Delete(junction); }
    }

    // ---- the rule ----

    private const string Chan = "Microsoft-Windows-Windows Defender/Operational";

    private static TimelineEvent History(string id, double minute = 0, string threat = "Virus:DOS/EICAR_Test_File", bool timed = true) => new()
    {
        Time = timed ? Timestamp.FromUtc(T0.AddMinutes(minute), "", "test") : Timestamp.Unknown(), Source = DefenderDetectionHistoryParser.EventSource, EvidenceId = "EV-H",
        Locator = "DetectionHistory/" + id, Summary = "t",
        Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Detection ID"] = id, ["Threat Name"] = threat, ["Path"] = @"file:_C:\LA\eicar\eicar-test.com", ["Detection User"] = @"LAB\Administrator",
            ["Process Name"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", ["SHA256"] = "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f",
        },
    };

    private static List<Finding> Of(params TimelineEvent[] events) => Run(events).Where(f => f.RuleId == "DEF-HISTORY-ONLY").ToList();

    [Fact]
    public void A_detection_missing_from_a_collected_Defender_log_is_High_with_T1070_001_and_names_what_it_found()
    {
        var f = Assert.Single(Of(History("{" + RealId + "}", 5), Ev(Chan, 1150, 0, "Microsoft-Antimalware-Service"), Ev(Chan, 1150, 10, "Microsoft-Antimalware-Service")));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("Defense Evasion", f.Category);
        Assert.Equal("T1070.001", f.MitreTechniqueId);
        Assert.Contains("EICAR_Test_File", f.Title);
        Assert.Contains(@"C:\LA\eicar\eicar-test.com", f.Description);
        Assert.Contains(@"LAB\Administrator", f.Description);
        Assert.Contains("powershell.exe", f.Description);
        Assert.Contains("275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f", f.Description);
        Assert.Contains("2026-10-01 10:05 UTC", f.Description);
        Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.MissingEvidence); Assert.NotEmpty(f.RecommendedNextSteps); Assert.NotEmpty(f.SupportingEvidence);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("Rotirea"));
    }

    [Fact]
    public void A_detection_that_is_in_the_log_gives_nothing_even_when_the_id_has_other_braces_or_case()
    {
        var fire = Ev(Chan, 1116, 0, "Microsoft-Windows-Windows Defender", ("Detection ID", "{2169d50a-ab8f-4cc5-992a-deafee4fae82}"));
        Assert.Empty(Of(History("{" + RealId + "}", 5), fire));
        Assert.Empty(Of(History(RealId, 5), fire));
        Assert.Empty(Of(History(RealId.ToLowerInvariant(), 5), Ev(Chan, 1117, 1, "x", ("Detection ID", "{" + RealId + "}"))));
        Assert.Single(Of(History(RealId, 5), Ev(Chan, 1116, 0, "x", ("Detection ID", "{00000000-0000-0000-0000-000000000001}"))));   // another detection in the log
    }

    [Fact]
    public void Without_the_Defender_channel_the_finding_is_Medium_without_a_technique_and_says_the_log_is_not_in_the_case()
    {
        var f = Assert.Single(Of(History(RealId, 5), Sec(4624, 0, ("LogonType", "3"))));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal("", f.MitreTechniqueId);
        Assert.Contains("nu apare în caz", f.Description);
        Assert.Contains(f.MissingEvidence, m => m.Contains("nu a fost colectat"));
    }

    [Fact]
    public void The_same_detection_in_two_files_is_one_finding_and_a_flood_is_capped()
    {
        Assert.Single(Of(History(RealId, 5), History("{" + RealId + "}", 6), Ev(Chan, 1150, 0, "x"), Ev(Chan, 1150, 100, "x")));
        var flood = Enumerable.Range(0, 30).Select(i => History(Guid.NewGuid().ToString(), i)).Append(Ev(Chan, 1150, 0, "x")).Append(Ev(Chan, 1150, 100, "x")).ToArray();
        var found = Of(flood);
        Assert.Equal(Wp11Rules.AdMaxFindings + 1, found.Count);
        Assert.Single(found, f => f.Severity == Severity.Low);
    }

    [Fact]
    public void A_parsed_real_record_feeds_the_rule()
    {
        var path = Write(RealId, Real);
        var sink = new ListSink();
        new DefenderDetectionHistoryParser().Parse(Item(path), path, sink, default);
        var f = Assert.Single(Of(sink.Events[0]));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("EICAR_Test_File", f.Title);
        Assert.Contains("2026-11-09 18:05 UTC", f.Description);
    }

    [Fact]
    public void A_detection_older_than_the_collected_log_is_Low_rotation_and_one_after_its_export_is_Low_too()
    {
        var before = Assert.Single(Of(History(RealId, 5), Ev(Chan, 1150, 60, "x"), Ev(Chan, 1150, 90, "x")));
        Assert.Equal(Severity.Low, before.Severity);
        Assert.Equal("", before.MitreTechniqueId);
        Assert.Contains("rotire normală", before.Description);
        var after = Assert.Single(Of(History(RealId, 200), Ev(Chan, 1150, 60, "x"), Ev(Chan, 1150, 90, "x")));
        Assert.Equal(Severity.Low, after.Severity);
        Assert.Contains("exportat înaintea ei", after.Description);
    }

    [Fact]
    public void A_cleared_Defender_log_collected_empty_still_counts_as_in_the_case_and_a_clear_after_the_detection_is_High()
    {
        var clear = Ev("System", 104, 30, "Microsoft-Windows-Eventlog", ("Channel", Chan));
        var f = Assert.Single(Of(History(RealId, 5), clear));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("T1070.001", f.MitreTechniqueId);
        Assert.Contains("golit după ea", f.Description);
        // a clear before the detection does not explain it; another channel's clear does not count at all
        var early = Assert.Single(Of(History(RealId, 5), Ev("System", 104, 1, "Microsoft-Windows-Eventlog", ("Channel", Chan)), Ev(Chan, 1150, 2, "x"), Ev(Chan, 1150, 3, "x")));
        Assert.Equal(Severity.Low, early.Severity);
        Assert.Equal(Severity.Medium, Assert.Single(Of(History(RealId, 5), Ev("System", 104, 30, "Microsoft-Windows-Eventlog", ("Channel", "Application")))).Severity);
    }

    [Fact]
    public void Only_a_clear_before_the_detection_without_channel_events_is_Medium_and_never_claims_an_unknown_time()
    {
        var f = Assert.Single(Of(History(RealId, 5), Ev("System", 104, 1, "Microsoft-Windows-Eventlog", ("Channel", Chan))));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal("", f.MitreTechniqueId);
        Assert.Contains("golit înainte de ea", f.Description);
        Assert.DoesNotContain("ora ei nu se cunoaște", f.Description);
        Assert.DoesNotContain(f.MissingEvidence, m => m.Contains("Ora detecției nu a putut fi citită"));
        Assert.DoesNotContain("jurnalul Defender colectat", f.ClassificationReason);
    }

    [Fact]
    public void Interval_edges_count_as_inside_a_single_event_log_covers_only_its_own_instant_and_an_untimed_clear_is_High()
    {
        Assert.Equal(Severity.High, Assert.Single(Of(History(RealId, 5), Ev(Chan, 1150, 5, "x"), Ev(Chan, 1150, 9, "x"))).Severity);
        Assert.Equal(Severity.High, Assert.Single(Of(History(RealId, 9), Ev(Chan, 1150, 5, "x"), Ev(Chan, 1150, 9, "x"))).Severity);
        Assert.Equal(Severity.High, Assert.Single(Of(History(RealId, 5), Ev(Chan, 1150, 5, "x"))).Severity);
        Assert.Equal(Severity.Low, Assert.Single(Of(History(RealId, 6), Ev(Chan, 1150, 5, "x"))).Severity);
        // a clear before the detection plus a log that covers it: the coverage decides (High)
        Assert.Equal(Severity.High, Assert.Single(Of(History(RealId, 5), Ev("System", 104, 1, "Microsoft-Windows-Eventlog", ("Channel", Chan)), Ev(Chan, 1150, 2, "x"), Ev(Chan, 1150, 9, "x"))).Severity);
        var timedClear = Ev("System", 104, 1, "Microsoft-Windows-Eventlog", ("Channel", Chan));
        var untimedClear = new TimelineEvent
        {
            Time = Timestamp.Unknown(), Source = timedClear.Source, EventId = timedClear.EventId, EvidenceId = timedClear.EvidenceId,
            Locator = timedClear.Locator, Summary = timedClear.Summary, Fields = timedClear.Fields,
        };
        Assert.Equal(Severity.High, Assert.Single(Of(History(RealId, 5), untimedClear)).Severity);
    }

    [Fact]
    public void The_low_summary_of_a_flood_never_carries_T1070_001_even_when_a_listed_detection_is_High()
    {
        var flood = Enumerable.Range(0, 25).Select(i => History(Guid.NewGuid().ToString(), i)).Append(History(RealId, 150)).Append(Ev(Chan, 1150, 100, "x")).Append(Ev(Chan, 1150, 200, "x")).ToArray();
        var found = Of(flood);
        Assert.Equal(Wp11Rules.AdMaxFindings + 1, found.Count);
        Assert.Single(found, f => f.Severity == Severity.High && f.MitreTechniqueId == "T1070.001");
        Assert.All(found.Where(f => f.Severity != Severity.High), f => Assert.Equal("", f.MitreTechniqueId));
    }

    [Fact]
    public void A_detection_without_a_time_is_Medium_and_one_seen_only_in_1118_or_1119_gives_nothing()
    {
        var untimed = History(RealId, 5, timed: false);
        var f = Assert.Single(Of(untimed, Ev(Chan, 1150, 0, "x"), Ev(Chan, 1150, 10, "x")));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal("", f.MitreTechniqueId);
        Assert.Empty(Of(History(RealId, 5), Ev(Chan, 1118, 4, "x", ("Detection ID", "{" + RealId + "}"))));
        Assert.Empty(Of(History(RealId, 5), Ev(Chan, 1119, 4, "x", ("Detection ID", "{" + RealId + "}"))));
    }
}