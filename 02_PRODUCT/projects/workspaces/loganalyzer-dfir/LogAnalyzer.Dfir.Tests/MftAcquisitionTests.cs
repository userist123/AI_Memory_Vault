using System.Buffers.Binary;
using System.Security.Cryptography;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Comparison row E8, live acquisition of the $MFT: the boot sector, the run list and record 0 are decoded here from bytes built by hand; the read of a real
/// volume runs only in an elevated process. Nothing from a real volume (names, paths) is written to the test output.
/// </summary>
public sealed class MftAcquisitionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ladfir_mftacq_").FullName;
    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);   // registered evidence is read-only
            Directory.Delete(_dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static byte[] Boot(ushort bps = 512, byte spc = 8, ulong lcn = 786432, sbyte cpr = -10)
    {
        var b = new byte[512];
        "NTFS    "u8.CopyTo(b.AsSpan(3));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0B), bps);
        b[0x0D] = spc;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x30), lcn);
        b[0x40] = (byte)cpr;
        return b;
    }

    [Fact]
    public void The_boot_sector_gives_the_cluster_size_the_MFT_position_and_the_record_size()
    {
        var g = NtfsBootSector.Parse(Boot());
        Assert.Equal(512, g.BytesPerSector);
        Assert.Equal(8, g.SectorsPerCluster);
        Assert.Equal(4096, g.ClusterSize);
        Assert.Equal(786432, g.MftLcn);
        Assert.Equal(1024, g.RecordSize);   // -10 means 2^10 bytes
    }

    [Fact]
    public void Sectors_per_cluster_above_0x80_are_a_power_of_two_and_a_positive_record_size_counts_clusters()
    {
        Assert.Equal(64 * 512, NtfsBootSector.Parse(Boot(spc: 0xFA)).ClusterSize);   // 256 - 250 = 6 -> 2^6 = 64 sectors
        Assert.Equal(128 * 512, NtfsBootSector.Parse(Boot(spc: 0x80)).ClusterSize);   // 0x80 itself is a plain 128
        var g = NtfsBootSector.Parse(Boot(bps: 4096, spc: 1, cpr: 1));
        Assert.Equal(4096, g.RecordSize);   // one 4096-byte cluster per record
        Assert.Equal(4096, NtfsBootSector.Parse(Boot(cpr: -12)).RecordSize);
    }

    [Theory]
    [InlineData("exfat")]
    [InlineData("short")]
    [InlineData("bps")]
    [InlineData("spc")]
    [InlineData("lcn")]
    [InlineData("record")]
    public void A_boot_sector_that_is_not_NTFS_or_has_impossible_values_is_refused(string what)
    {
        var b = Boot();
        switch (what)
        {
            case "exfat": "EXFAT   "u8.CopyTo(b.AsSpan(3)); break;
            case "short": b = b[..0x20]; break;
            case "bps": BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0B), 300); break;
            case "spc": b[0x0D] = 0; break;
            case "lcn": BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x30), ulong.MaxValue); break;
            case "record": b[0x40] = 0; break;
        }
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(b));
    }

    [Fact]
    public void A_run_list_is_decoded_with_offsets_relative_to_the_previous_run()
    {
        // 0x21: 1 length byte, 2 offset bytes. run 1: 0x30 clusters at 0x0100; run 2: 0x10 clusters at 0x0100 + 0x0200; run 3: 8 clusters at 0x0300 - 0x0080 (negative)
        byte[] list = [0x21, 0x30, 0x00, 0x01, 0x21, 0x10, 0x00, 0x02, 0x21, 0x08, 0x80, 0xFF, 0x00];
        var runs = DataRuns.Decode(list);
        Assert.Equal([new DataRuns.Run(0x100, 0x30), new DataRuns.Run(0x300, 0x10), new DataRuns.Run(0x280, 8)], runs);
    }

    [Fact]
    public void A_sparse_run_has_no_cluster_and_does_not_move_the_position_and_the_terminator_ends_the_list()
    {
        // 0x31: 1 length byte, 3 offset bytes; 0x01: sparse, 1 length byte; then a run relative to the run before the hole; then the end and garbage after it
        byte[] list = [0x31, 0x04, 0x00, 0x10, 0x00, 0x01, 0x10, 0x11, 0x02, 0x05, 0x00, 0x21, 0x09, 0x09, 0x09];
        var runs = DataRuns.Decode(list);
        Assert.Equal([new DataRuns.Run(0x1000, 4), new DataRuns.Run(null, 0x10), new DataRuns.Run(0x1005, 2)], runs);
    }

    [Fact]
    public void An_eight_byte_length_and_offset_are_read_and_a_list_without_terminator_ends_with_the_data()
    {
        byte[] list = [0x88, 1, 0, 0, 0, 0, 0, 0, 0, 0x10, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal([new DataRuns.Run(0x10, 1)], DataRuns.Decode(list));
        Assert.Empty(DataRuns.Decode([]));
        Assert.Empty(DataRuns.Decode([0x00]));
    }

    [Theory]
    [InlineData(new byte[] { 0x21, 0x30, 0x00 })]                                            // offset field cut off
    [InlineData(new byte[] { 0x01 })]                                                         // length field cut off
    [InlineData(new byte[] { 0x20, 0x00, 0x00 })]                                             // length size 0
    [InlineData(new byte[] { 0x91, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]                        // offset size 9
    [InlineData(new byte[] { 0x11, 0x00, 0x10 })]                                             // zero-length run
    [InlineData(new byte[] { 0x11, 0x01, 0x10, 0x11, 0x01, 0xE0 })]                           // goes before the start of the volume
    [InlineData(new byte[] { 0x18, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x10 })]   // length above long.MaxValue
    public void A_malformed_run_list_is_refused(byte[] list) => Assert.Throws<InvalidDataException>(() => DataRuns.Decode(list));

    [Fact]
    public void A_position_that_overflows_is_refused_not_wrapped()
    {
        byte[] list = [0x88, 1, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F,
                       0x88, 1, 0, 0, 0, 0, 0, 0, 0, 0x01, 0, 0, 0, 0, 0, 0, 0, 0x00];
        Assert.Throws<InvalidDataException>(() => DataRuns.Decode(list));
    }

    /// <summary>Record 0 of a volume as on disk (update sequence applied): one non-resident unnamed $DATA, optionally preceded by a named one.</summary>
    private static byte[] Record0(ReadOnlySpan<byte> runs, ulong realSize, bool namedFirst = false, bool resident = false, bool badFixup = false)
    {
        var b = new byte[1024];
        "FILE"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x04), 0x30);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x06), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x14), 0x38);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x1C), 1024);
        int o = 0x38;
        if (namedFirst) o = Data(b, o, [0x11, 0x01, 0x77, 0x00], 4096, named: true);
        o = resident ? Resident(b, o) : Data(b, o, runs, realSize, named: false);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x18), (uint)(o + 8));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x30), 0x0007);
        for (int s = 0; s < 2; s++)
        {
            b[0x32 + 2 * s] = b[510 + 512 * s]; b[0x33 + 2 * s] = b[511 + 512 * s];
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(510 + 512 * s), badFixup && s == 1 ? (ushort)0x0009 : (ushort)0x0007);
        }
        return b;
    }

    private static int Data(byte[] b, int o, ReadOnlySpan<byte> runs, ulong real, bool named)
    {
        int len = (0x40 + runs.Length + 7) & ~7;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o + 4), (uint)len);
        b[o + 8] = 1;                           // non-resident
        b[o + 9] = (byte)(named ? 4 : 0);        // name length
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o + 0x20), 0x40);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o + 0x30), real);
        runs.CopyTo(b.AsSpan(o + 0x40));
        return o + len;
    }

    private static int Resident(byte[] b, int o)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o + 4), 0x28);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o + 0x10), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o + 0x14), 0x18);
        return o + 0x28;
    }

    [Fact]
    public void The_unnamed_non_resident_DATA_of_record_0_gives_the_run_list_and_the_real_size()
    {
        byte[] runs = [0x31, 0x40, 0x00, 0x00, 0x03, 0x11, 0x20, 0xF0, 0x00];   // 0x40 clusters at 0x030000, then 0x20 clusters at 0x030000 - 0x10
        var (list, real) = MftAcquisition.ReadDataAttribute(Record0(runs, 90_000_000UL, namedFirst: true));
        Assert.Equal(90_000_000L, real);
        Assert.Equal([new DataRuns.Run(0x030000, 0x40), new DataRuns.Run(0x02FFF0, 0x20)], list);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("fixup")]
    [InlineData("resident")]
    [InlineData("zero")]
    public void A_record_0_that_is_not_a_usable_MFT_record_is_refused(string what)
    {
        byte[] runs = [0x11, 0x08, 0x10, 0x00];
        var rec = what switch
        {
            "fixup" => Record0(runs, 4096, badFixup: true),
            "resident" => Record0(runs, 4096, resident: true),
            "zero" => Record0(runs, 0),
            _ => Record0(runs, 4096),
        };
        if (what == "signature") rec[0] = (byte)'B';
        Assert.Throws<InvalidDataException>(() => MftAcquisition.ReadDataAttribute(rec));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("CC:")]
    [InlineData(@"\\server\share")]
    [InlineData("1:")]
    public void A_volume_that_is_not_a_drive_letter_is_refused_without_an_exception(string? volume)
    {
        var r = MftAcquisition.Acquire(volume, Path.Combine(_dir, "x"), elevated: false, default);
        Assert.False(r.Ok);
        Assert.Contains("Volum nevalid", r.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir, "x")));
    }

    [Fact]
    public void A_drive_that_does_not_exist_or_cannot_be_opened_is_a_reason_not_an_exception()
    {
        // Z: is rarely mounted; if it is, the read is refused without administrator rights or ends in a refusal for a non-NTFS volume. Either way: no exception.
        var dest = Path.Combine(_dir, "z");
        var r = MftAcquisition.Acquire("Z:", dest, elevated: false, default);
        if (!r.Ok) { Assert.NotEmpty(r.Problem); Assert.False(File.Exists(dest)); }
    }

    private sealed class CountingSink : IEventSink { public long Count; public void Add(TimelineEvent e) => Count++; }

    [AdminFact]
    public void The_MFT_of_the_system_volume_is_copied_read_only_and_parsed()
    {
        var volume = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        var dest = Path.Combine(_dir, "$MFT");
        var r = MftAcquisition.Acquire(volume, dest, elevated: true, default);
        Assert.True(r.Ok, r.Problem);
        try
        {
            Assert.True(r.Bytes > 0);
            Assert.Equal(r.Bytes, new FileInfo(dest).Length);
            Assert.Equal(0, r.Bytes % r.RecordSize);
            using (var fs = File.OpenRead(dest))
            {
                var head = new byte[4];
                fs.ReadExactly(head);
                Assert.Equal("FILE"u8.ToArray(), head);
                fs.Position = 0;
                Assert.Equal(r.Sha256, Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant());
            }
            var sink = new CountingSink();
            var res = new MftParser().Parse(new EvidenceItem { EvidenceId = "EV-LIVE", CaseId = "C", Source = "mft", SourceType = MftParser.SourceTypeName, StoredPath = dest }, dest, sink, default);
            Assert.True(res.Records > 1000, $"Records={res.Records}");
            Assert.True(res.MalformedRecords * 100 < res.Records, $"Malformed={res.MalformedRecords} of {res.Records}");   // counts only: no name or path from the volume is shown
        }
        finally { File.Delete(dest); }
    }

    // ---- what a refused open or a damaged boot sector means (no real volume) ----

    [Fact]
    public void A_refused_volume_open_is_an_absent_source_without_administrator_rights_and_a_failure_as_administrator()
    {
        var notAdmin = MftAcquisition.AccessDenied(elevated: false);
        Assert.True(notAdmin.SourceAbsent);
        Assert.Contains("administrator", notAdmin.Problem, StringComparison.Ordinal);
        var admin = MftAcquisition.AccessDenied(elevated: true);
        Assert.False(admin.Ok);
        Assert.False(admin.SourceAbsent);
        Assert.Equal("Accesul la volum a fost refuzat deși aplicația rulează ca administrator (posibil blocat de software-ul de securitate).", admin.Problem);
    }

    [Fact]
    public void A_volume_without_the_NTFS_identifier_is_absent_but_an_NTFS_boot_sector_with_invalid_geometry_is_a_failure()
    {
        var fat = Boot();
        "MSDOS5.0"u8.CopyTo(fat.AsSpan(3));
        Assert.True(MftAcquisition.ReadBootSector(fat, out _)!.SourceAbsent);

        var r = MftAcquisition.ReadBootSector(Boot(bps: 100), out _)!;   // labelled NTFS, impossible sector size
        Assert.False(r.Ok);
        Assert.False(r.SourceAbsent);
        Assert.Contains("date nevalide", r.Problem, StringComparison.Ordinal);

        Assert.Null(MftAcquisition.ReadBootSector(Boot(), out var geo));
        Assert.Equal(1024, geo.RecordSize);
    }

    // ---- the collector outcome on a temporary case (no real volume) ----

    private CaseWorkspace NewCase() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "e8", TestScopes.Valid());

    private static string VolumeOf(CaseWorkspace ws) => Path.GetPathRoot(ws.Root)!.TrimEnd('\\');
    private static string OtherVolume(CaseWorkspace ws) => VolumeOf(ws).Equals("Q:", StringComparison.OrdinalIgnoreCase) ? "R:" : "Q:";

    private static (string Dest, MftAcquisitionResult Ok) CopiedTable(CaseWorkspace ws)
    {
        var dest = Path.Combine(ws.RawDir("MFT"), "$MFT");
        var bytes = new byte[2048];
        "FILE"u8.CopyTo(bytes);
        File.WriteAllBytes(dest, bytes);
        return (dest, new MftAcquisitionResult(true, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), 1024, ""));
    }

    private static string AuditText(CaseWorkspace ws) => File.ReadAllText(ws.AuditChainPath);

    [Fact]
    public void A_case_on_the_same_volume_gets_a_warning_in_the_warnings_the_evidence_notes_and_the_audit_but_no_error()
    {
        var ws = NewCase();
        var volume = VolumeOf(ws);
        var (dest, ok) = CopiedTable(ws);
        var o = MftCollector.Finish(ws, volume, volume + @"\$MFT", dest, ok, "MftCollector", "1.0");
        var warning = MftAcquisition.SameVolumeWarning(volume);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Errors);                                   // an error would become a Partial gap ("run as administrator / import")
        Assert.Equal(warning, Assert.Single(o.Warnings));
        var item = Assert.Single(o.Evidence);
        Assert.Equal(EvidenceStatus.Success, item.Status);
        Assert.Equal(warning, item.Notes);
        Assert.Contains("acquisition.warning", AuditText(ws), StringComparison.Ordinal);
        Assert.Contains("avertisment: " + warning, InvestigationPipeline.CollectionNotes(o), StringComparison.Ordinal);   // what the report lists for the collector
    }

    [Fact]
    public void A_case_on_another_volume_has_no_warning()
    {
        var ws = NewCase();
        var (dest, ok) = CopiedTable(ws);
        var o = MftCollector.Finish(ws, OtherVolume(ws), @"Q:\$MFT", dest, ok, "MftCollector", "1.0");
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Warnings);
        Assert.Empty(o.Errors);
        Assert.Equal("", Assert.Single(o.Evidence).Notes);
        Assert.DoesNotContain("acquisition.warning", AuditText(ws), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, EvidenceStatus.NotAvailable)]
    [InlineData(false, EvidenceStatus.Failed)]
    public void A_read_that_produced_no_copy_maps_absent_to_not_available_and_anything_else_to_failed(bool absent, EvidenceStatus expected)
    {
        var ws = NewCase();
        var r = MftAcquisition.AccessDenied(elevated: !absent);
        var o = MftCollector.Finish(ws, OtherVolume(ws), @"Q:\$MFT", Path.Combine(ws.RawDir("MFT"), "$MFT"), r, "MftCollector", "1.0");
        Assert.Equal(expected, o.Status);
        Assert.Equal(r.Problem, Assert.Single(o.Errors));
        Assert.Empty(o.Evidence);
    }

    [Fact]
    public void A_copy_whose_hash_differs_is_registered_as_failed_counted_audited_and_not_parsed()
    {
        var ws = NewCase();
        var (dest, ok) = CopiedTable(ws);
        var o = MftCollector.Finish(ws, OtherVolume(ws), @"Q:\$MFT", dest, ok with { Sha256 = new string('0', 64) }, "MftCollector", "1.0");
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Contains("nu este de încredere", Assert.Single(o.Errors), StringComparison.Ordinal);
        var item = Assert.Single(o.Evidence);                    // counted, so the collection row matches the evidence index
        Assert.Equal(EvidenceStatus.Failed, item.Status);
        Assert.Contains("nu este de încredere", item.Notes, StringComparison.Ordinal);
        Assert.Equal(EvidenceStatus.Failed, Assert.Single(ws.LoadEvidence()).Status);
        Assert.Contains("acquisition.hash_mismatch", AuditText(ws), StringComparison.Ordinal);

        var run = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        var pr = Assert.Single(run.Parsing, p => p.EvidenceId == item.EvidenceId);
        Assert.Equal(EvidenceStatus.Failed, pr.Status);
        Assert.Equal("-", pr.Parser);
        Assert.StartsWith("Proba nu a fost analizată", pr.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Timeline, e => e.EvidenceId == item.EvidenceId);
        Assert.Equal(AntiForensicResult.Undetermined, run.AntiForensics.Single(c => c.Id == "AF08").Result);
    }

    [Fact]
    public void A_trusted_copy_is_handed_to_the_MFT_parser()
    {
        var ws = NewCase();
        var (dest, ok) = CopiedTable(ws);
        var item = Assert.Single(MftCollector.Finish(ws, OtherVolume(ws), @"Q:\$MFT", dest, ok, "MftCollector", "1.0").Evidence);
        var run = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.Contains(run.Parsing, p => p.EvidenceId == item.EvidenceId && p.Parser != "-");
    }

    // ---- same-volume decision, plausibility bounds and the copy loop (no real volume) ----

    [Theory]
    [InlineData(@"C:\Cazuri\caz-1", "C:", true)]
    [InlineData(@"c:\Cazuri\caz-1", "C:", true)]
    [InlineData(@"C:\Cazuri\caz-1", @"C:\", true)]
    [InlineData(@"D:\Cazuri\caz-1", "C:", false)]
    [InlineData(@"\\server\share\caz", "C:", false)]
    [InlineData("", "C:", false)]
    [InlineData(null, "C:", false)]
    [InlineData(@"C:\Cazuri", null, false)]
    public void The_case_folder_is_on_the_same_volume_when_the_normalised_path_roots_match(string? caseRoot, string? volume, bool expected) =>
        Assert.Equal(expected, MftAcquisition.IsSameVolume(caseRoot, volume));

    [Fact]
    public void The_same_volume_warning_names_the_volume_and_the_remedy()
    {
        var w = MftAcquisition.SameVolumeWarning("C:");
        Assert.Contains("(C:)", w, StringComparison.Ordinal);
        Assert.Contains("alt volum", w, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_sector_total_sectors_are_read_from_offset_0x28()
    {
        var b = Boot();
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x28), 8000);
        Assert.Equal(8000, NtfsBootSector.Parse(b).TotalSectors);
    }

    private static readonly NtfsBootSector SmallVolume = new(512, 8, 100, 1024, TotalSectors: 8000);   // 4 096 000 bytes, 1000 clusters of 4096

    [Fact]
    public void A_plausible_claim_passes_the_bounds_check_and_sparse_runs_are_not_placed_on_the_volume()
    {
        Assert.Null(MftAcquisition.CheckBounds(SmallVolume, 3_000_000, [new DataRuns.Run(100, 400), new DataRuns.Run(null, 5000), new DataRuns.Run(990, 10)]));
    }

    [Fact]
    public void A_real_size_above_the_volume_or_above_the_hard_cap_is_refused()
    {
        var tooBig = MftAcquisition.CheckBounds(SmallVolume, 5_000_000, [new DataRuns.Run(100, 100)]);
        Assert.NotNull(tooBig);
        Assert.Contains("depășește dimensiunea volumului", tooBig, StringComparison.Ordinal);

        var huge = new NtfsBootSector(512, 8, 100, 1024, TotalSectors: 1L << 40);   // a 512 TiB volume: the volume check passes, the cap does not
        var capped = MftAcquisition.CheckBounds(huge, MftAcquisition.MaxMftBytes + 1, [new DataRuns.Run(100, 100)]);
        Assert.NotNull(capped);
        Assert.Contains("GiB", capped, StringComparison.Ordinal);
        Assert.Null(MftAcquisition.CheckBounds(huge, MftAcquisition.MaxMftBytes, [new DataRuns.Run(100, 100)]));
    }

    [Theory]
    [InlineData(990L, 11L)]            // ends one cluster after the last (1000)
    [InlineData(1000L, 1L)]            // starts after the last cluster
    [InlineData(0L, 1001L)]            // longer than the volume
    [InlineData(long.MaxValue - 5, 10L)]   // overflow-prone
    public void A_run_that_leaves_the_volume_is_refused(long lcn, long length)
    {
        var r = MftAcquisition.CheckBounds(SmallVolume, 4096, [new DataRuns.Run(lcn, length)]);
        Assert.NotNull(r);
        Assert.Contains("iese din volum", r, StringComparison.Ordinal);
    }

    [Fact]
    public void A_boot_sector_without_total_sectors_is_refused()
    {
        Assert.NotNull(MftAcquisition.CheckBounds(new NtfsBootSector(512, 8, 100, 1024), 4096, [new DataRuns.Run(1, 1)]));
    }

    // A fake volume of 16 clusters of 1024 bytes; byte i holds (i % 251) + 1, so no data byte is zero.
    private const int FakeCluster = 1024;
    private static readonly byte[] FakeVolume = Enumerable.Range(0, 16 * FakeCluster).Select(i => (byte)(i % 251 + 1)).ToArray();

    private static int FakeRead(long offset, Span<byte> buffer)
    {
        int n = (int)Math.Min(buffer.Length, FakeVolume.Length - offset);
        if (n <= 0) return 0;
        FakeVolume.AsSpan((int)offset, n).CopyTo(buffer);
        return n;
    }

    [Fact]
    public void The_copy_is_truncated_to_the_real_size_and_hashed_over_exactly_those_bytes()
    {
        var dest = Path.Combine(_dir, "trunc");
        var r = MftAcquisition.CopyRuns([new DataRuns.Run(2, 3)], FakeCluster, 2500, 1024, FakeRead, dest, default);
        Assert.True(r.Ok, r.Problem);
        var expected = FakeVolume.AsSpan(2 * FakeCluster, 2500).ToArray();
        Assert.Equal(2500, r.Bytes);
        Assert.Equal(expected, File.ReadAllBytes(dest));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), r.Sha256);
        Assert.Equal(1024, r.RecordSize);
    }

    [Fact]
    public void A_sparse_run_is_written_as_zeroes_without_reading_the_volume()
    {
        var dest = Path.Combine(_dir, "sparse");
        var offsets = new List<long>();
        var r = MftAcquisition.CopyRuns([new DataRuns.Run(1, 1), new DataRuns.Run(null, 2), new DataRuns.Run(4, 1)], FakeCluster, 4 * FakeCluster, 1024,
            (offset, buffer) => { offsets.Add(offset); return FakeRead(offset, buffer); }, dest, default);
        Assert.True(r.Ok, r.Problem);
        var bytes = File.ReadAllBytes(dest);
        Assert.Equal(4 * FakeCluster, bytes.Length);
        Assert.Equal(FakeVolume.AsSpan(FakeCluster, FakeCluster).ToArray(), bytes[..FakeCluster]);
        Assert.All(bytes[FakeCluster..(3 * FakeCluster)], x => Assert.Equal(0, x));
        Assert.Equal(FakeVolume.AsSpan(4 * FakeCluster, FakeCluster).ToArray(), bytes[(3 * FakeCluster)..]);
        Assert.Equal([1L * FakeCluster, 4L * FakeCluster], offsets);   // the sparse clusters never reached the reader
    }

    [Fact]
    public void A_read_failure_deletes_the_partial_file_and_rethrows()
    {
        var dest = Path.Combine(_dir, "ioerr");
        int calls = 0;
        Assert.Throws<IOException>(() => MftAcquisition.CopyRuns([new DataRuns.Run(0, 1), new DataRuns.Run(1, 1)], FakeCluster, 2 * FakeCluster, 1024,
            (offset, buffer) => ++calls == 1 ? FakeRead(offset, buffer) : throw new IOException("simulat"), dest, default));
        Assert.Equal(2, calls);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void A_source_that_ends_early_deletes_the_partial_file()
    {
        var dest = Path.Combine(_dir, "eof");
        Assert.Throws<IOException>(() => MftAcquisition.CopyRuns([new DataRuns.Run(0, 1)], FakeCluster, FakeCluster, 1024, (_, _) => 0, dest, default));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Cancellation_deletes_the_partial_file_and_rethrows()
    {
        var dest = Path.Combine(_dir, "cancel");
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => MftAcquisition.CopyRuns([new DataRuns.Run(0, 1), new DataRuns.Run(1, 1)], FakeCluster, 2 * FakeCluster, 1024,
            (offset, buffer) => { cts.Cancel(); return FakeRead(offset, buffer); }, dest, cts.Token));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Runs_that_cover_less_than_the_real_size_give_a_refusal_and_no_file()
    {
        var dest = Path.Combine(_dir, "short");
        var r = MftAcquisition.CopyRuns([new DataRuns.Run(0, 1)], FakeCluster, 2 * FakeCluster, 1024, FakeRead, dest, default);
        Assert.False(r.Ok);
        Assert.False(File.Exists(dest));
    }
}
