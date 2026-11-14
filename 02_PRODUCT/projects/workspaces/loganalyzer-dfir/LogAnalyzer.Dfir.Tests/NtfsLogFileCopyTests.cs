using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>L2: $LogFile (MFT record 2) kept raw from the same shadow copy. Reader tests on the oracle image and on hostile variants built in memory; collector tests with the fake shadow provider.</summary>
public sealed class NtfsLogFileCopyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_ntfslog_" + Guid.NewGuid().ToString("N"));
    public NtfsLogFileCopyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private const string Device = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7";
    private const string SnapId = "{AAAAAAAA-1111-2222-3333-444444444444}";
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static readonly Func<DateTimeOffset> Clock = () => new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeShadow : WindowsSearchIndexCollector.ISnapshot
    {
        public int Live { get; private set; } = 2;
        public List<string> Deleted { get; } = [];
        public (string Id, string DeviceObject) Create(string volumeRoot) { Live++; return (SnapId, Device); }
        public void Delete(string id) { Deleted.Add(id); Live--; }
    }

    // ---- locating record 2 and its attributes in the image bytes (independent of the reader's copy path)

    private static (int RecOff, NtfsFileRecord Rec, NtfsAttribute Data) Record2(byte[] img)
    {
        using var vol = new MemoryStream(img, writable: false);
        var boot = NtfsVolumeReader.Open(vol).Boot;
        int recOff = checked((int)((long)boot.MftLcn * boot.ClusterSize) + 2 * boot.MftRecordSize);
        var rec = NtfsFileRecord.Parse(img.AsSpan(recOff, boot.MftRecordSize).ToArray(), boot.MftRecordSize);
        var data = rec.Attributes.Single(a => a.Type == NtfsAttributeType.Data && a.Name.Length == 0);
        return (recOff, rec, data);
    }

    private static byte[] Mutated(Action<byte[], int, NtfsFileRecord, NtfsAttribute> change)
    {
        var img = NtfsVolumeReaderTests.ImageBytes.ToArray();
        var (off, rec, data) = Record2(img);
        change(img, off, rec, data);
        return img;
    }

    private static long LogFileImageOffset(byte[] img)
    {
        using var vol = new MemoryStream(img, writable: false);
        var boot = NtfsVolumeReader.Open(vol).Boot;
        var (_, rec, data) = Record2(img);
        var runs = DataRuns.Decode(rec.MappingPairsOf(data), 0, data.HighestVcn, (long)boot.TotalClusters).Runs;
        return runs[0].Lcn!.Value * boot.ClusterSize;
    }

    private static NtfsLogFileResult Copy(byte[] img, out byte[] bytes)
    {
        using var vol = new MemoryStream(img, writable: false);
        using var ms = new MemoryStream();
        var r = NtfsVolumeReader.Open(vol).CopyLogFile(ms);
        bytes = ms.ToArray();
        return r;
    }

    // ---------------- reader on the oracle image ----------------

    [Fact]
    public void CopyLogFile_copies_the_real_data_size_with_a_correct_sha_and_reports_the_restart_pages_from_the_copied_bytes()
    {
        var img = NtfsVolumeReaderTests.ImageBytes;
        var (_, _, data) = Record2(img);
        var r = Copy(img, out var bytes);
        Assert.Equal(data.RealSize, r.Copy.Size);
        Assert.Equal(data.RealSize, bytes.Length);
        Assert.Equal(Sha(bytes), r.Copy.Sha256, ignoreCase: true);
        Assert.Equal(data.ValidDataLength, r.Copy.ValidDataLength);
        Assert.Equal(0, r.Copy.SparseRuns);
        // header values re-read here from the copied bytes
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x10)), r.SystemPageSize);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x14)), r.LogPageSize);
        Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x1C)), r.VersionMajor);
        Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x1A)), r.VersionMinor);
        Assert.Equal(4096u, r.SystemPageSize);   // the oracle image uses 4096-byte pages
        Assert.True(r.SecondPageLocated);
        Assert.Equal([(0L, "RSTR"), (4096L, "RSTR")], r.RestartPages.Select(p => (p.Offset, p.Magic)).ToArray());
        Assert.All(r.RestartPages, p => Assert.True(p.IsValid));
        Assert.Equal("RSTR", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("RSTR", Encoding.ASCII.GetString(bytes, 4096, 4));
    }

    [Fact]
    public void The_plafon_is_one_gibibyte() => Assert.Equal(1L << 30, NtfsVolumeReader.MaxLogFileBytes);

    // ---------------- hostile volumes ----------------

    private static void AssertRefused(byte[] img, string contains)
    {
        using var vol = new MemoryStream(img, writable: false);
        using var ms = new MemoryStream();
        var ex = Assert.Throws<NtfsReadException>(() => NtfsVolumeReader.Open(vol).CopyLogFile(ms));
        Assert.Contains(contains, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, ms.Length);   // nothing written
    }

    private static int NameOffset(int recOff, NtfsFileRecord rec)
    {
        var a = rec.Attributes.First(x => x.Type == NtfsAttributeType.FileName && !x.NonResident && FileNameAttribute.TryParse(rec.ValueOf(x))!.Namespace != 2);
        return recOff + a.Offset + a.ValueOffset;   // start of the $FILE_NAME value
    }

    [Fact]
    public void A_record_2_with_another_name_is_refused() =>
        AssertRefused(Mutated((img, off, rec, _) => img[NameOffset(off, rec) + 66 + 2] = (byte)'X'), "nu are numele");   // "$LogFile" -> "$XogFile"

    [Fact]
    public void A_record_2_whose_parent_is_not_the_root_is_refused() =>
        AssertRefused(Mutated((img, off, rec, _) => BinaryPrimitives.WriteUInt64LittleEndian(img.AsSpan(NameOffset(off, rec)), 6)), "părintele");

    [Fact]
    public void A_record_2_whose_parent_sequence_differs_from_the_real_one_of_record_5_is_refused() =>
        AssertRefused(Mutated((img, off, rec, _) => img[NameOffset(off, rec) + 6] ^= 0x01), "părintele");   // sequence = high 16 bits of the reference

    [Theory]
    [InlineData(0xFC)]   // neither in use nor a directory
    [InlineData(0xFD)]   // in use, not a directory
    [InlineData(0xFE)]   // a directory, not in use
    public void A_root_record_that_is_not_an_in_use_directory_is_refused(int mask) =>
        // review round 2: with only the sequence compared, a root with its flags cleared confirmed the parent of $LogFile
        AssertRefused(Mutated((img, off, _, _) =>
        {
            using var vol = new MemoryStream(img, writable: false);
            int recSize = NtfsVolumeReader.Open(vol).Boot.MftRecordSize;
            img[off + 3 * recSize + 22] &= (byte)mask;   // record 5 = record 2 + 3 records; flags at 0x16
        }), "rădăcina");

    [Fact]
    public void A_resident_data_attribute_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) =>
        {
            int a = off + data.Offset;
            img[a + 8] = 0;                                                      // resident
            BinaryPrimitives.WriteUInt32LittleEndian(img.AsSpan(a + 16), 0x10);   // value length
            BinaryPrimitives.WriteUInt16LittleEndian(img.AsSpan(a + 20), 0x18);   // value offset (inside the attribute)
        }), "rezident");

    [Fact]
    public void A_record_2_flagged_as_a_directory_is_refused() =>
        AssertRefused(Mutated((img, off, _, _) => img[off + 22] |= 2), "nu este o înregistrare de bază de fișier în uz");

    [Fact]
    public void A_record_2_not_in_use_is_refused() =>
        AssertRefused(Mutated((img, off, _, _) => img[off + 22] &= 0xFE), "în uz");

    [Fact]
    public void A_sparse_data_attribute_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) => img[off + data.Offset + 13] |= 0x80), "sparse");   // attribute flags 0x8000

    [Fact]
    public void A_compressed_data_attribute_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) => img[off + data.Offset + 12] |= 0x01), "comprimat");

    [Fact]
    public void An_encrypted_data_attribute_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) => img[off + data.Offset + 13] |= 0x40), "criptat");

    [Fact]
    public void A_real_size_over_the_plafon_is_refused_and_nothing_is_written() =>
        AssertRefused(Mutated((img, off, _, data) => BinaryPrimitives.WriteInt64LittleEndian(img.AsSpan(off + data.Offset + 0x30), NtfsVolumeReader.MaxLogFileBytes + 1)), "plafon");

    [Fact]
    public void A_negative_real_size_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) => BinaryPrimitives.WriteInt64LittleEndian(img.AsSpan(off + data.Offset + 0x30), -1)), "mărime");

    [Fact]
    public void A_real_size_under_two_pages_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) =>
        {
            BinaryPrimitives.WriteInt64LittleEndian(img.AsSpan(off + data.Offset + 0x30), 4096);
            BinaryPrimitives.WriteInt64LittleEndian(img.AsSpan(off + data.Offset + 0x38), 4096);
        }), "sub două pagini");

    [Fact]
    public void A_valid_data_length_above_the_size_is_refused() =>
        AssertRefused(Mutated((img, off, _, data) => BinaryPrimitives.WriteInt64LittleEndian(img.AsSpan(off + data.Offset + 0x38), data.RealSize + 1)), "valid data length");

    [Fact]
    public void A_zero_magic_in_page_0_is_copied_and_reported_not_thrown()
    {
        var img = NtfsVolumeReaderTests.ImageBytes.ToArray();
        long at = LogFileImageOffset(img);
        img.AsSpan((int)at, 4).Clear();
        var r = Copy(img, out var bytes);
        Assert.Equal(2097152, bytes.Length);
        Assert.Equal("0x00000000", r.RestartPages[0].Magic);
        Assert.False(r.RestartPages[0].IsValid);
        Assert.Equal("RSTR", r.RestartPages[1].Magic);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3000u)]            // not a power of two
    [InlineData(256u)]             // under 512
    [InlineData(131072u)]          // over 65536
    [InlineData(0xFFFFFFFFu)]
    public void An_unusable_system_page_size_means_the_second_page_is_not_located(uint sys)
    {
        var img = NtfsVolumeReaderTests.ImageBytes.ToArray();
        long at = LogFileImageOffset(img);
        BinaryPrimitives.WriteUInt32LittleEndian(img.AsSpan((int)at + 0x10), sys);
        var r = Copy(img, out _);
        Assert.False(r.SecondPageLocated);
        Assert.Single(r.RestartPages);
        Assert.Equal(sys, r.SystemPageSize);
    }

    // ---------------- collector ----------------

    private (CollectorOutcome O, CaseWorkspace Ws, FakeShadow Snap) Run(byte[]? img = null)
    {
        var snap = new FakeShadow();
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_" + Guid.NewGuid().ToString("N")), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        Func<string, Stream> open = _ => img is null ? NtfsVolumeReaderTests.OpenImage() : new MemoryStream(img, writable: false);
        var c = new NtfsMetadataCollector(snap, () => snap.Live, open, @"C:\", Clock, _ => []);
        var o = c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        return (o, ws, snap);
    }

    private static IEnumerable<string> AllFiles(CaseWorkspace ws) => Directory.Exists(ws.RawDir("NtfsMetadata")) ? Directory.EnumerateFiles(ws.RawDir("NtfsMetadata"), "*", SearchOption.AllDirectories) : [];

    private static JsonElement Manifest(CaseWorkspace ws, CollectorOutcome o)
    {
        var man = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(ws.Root, man.StoredPath))).RootElement.Clone();
    }

    [Fact]
    public void A_normal_run_registers_the_logfile_and_the_manifest_matches_the_case_hash()
    {
        var (o, ws, snap) = Run();
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Errors);
        var lf = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.LogFileSourceType);
        Assert.Equal("$LogFile", Path.GetFileName(lf.StoredPath));
        Assert.Equal(@"C:\$LogFile", lf.OriginalPath);
        var stored = File.ReadAllBytes(Path.Combine(ws.Root, lf.StoredPath));
        Assert.Equal(Sha(stored), lf.Sha256, ignoreCase: true);
        var b = Manifest(ws, o).GetProperty("logFile");
        Assert.Equal("Copied", b.GetProperty("status").GetString());
        Assert.Equal(lf.Sha256, b.GetProperty("sha256").GetString(), ignoreCase: true);
        Assert.Equal(stored.Length, b.GetProperty("size").GetInt64());
        Assert.Equal(4096, b.GetProperty("systemPageSize").GetInt64());
        Assert.Equal("RSTR", b.GetProperty("restartPages")[0].GetProperty("magic").GetString());
        Assert.Equal(4096, b.GetProperty("restartPages")[1].GetProperty("offset").GetInt64());
        Assert.Equal([SnapId], snap.Deleted);
        Assert.Equal(2, snap.Live);   // shadow copies 0 -> 0 (before = after)
    }

    private static UsnJournalRead FakeJournal() =>
        new(new UsnJournalInfo(0x01dd6eca2b00544f, 1000, 5000),
            [new UsnRecord(1000, "victim.docx", 0x80000102, new DateTimeOffset(2026, 11, 14, 9, 59, 30, TimeSpan.Zero), 0x20, 0x0001000000000064, 0x0005000000000005, 0, 0, 2, 0, 80)], "");

    private (CollectorOutcome O, CaseWorkspace Ws) RunFull(byte[] img)
    {
        var snap = new FakeShadow();
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_" + Guid.NewGuid().ToString("N")), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => new MemoryStream(img, writable: false), @"C:\", Clock, _ => [@"\many", @"\docs"], (_, _) => FakeJournal());
        return (c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default), ws);
    }

    [Fact]
    public void A_logfile_error_keeps_mft_i30_and_usn_registers_partial_and_leaves_no_logfile()
    {
        var img = Mutated((i, off, rec, _) => i[NameOffset(off, rec) + 66 + 2] = (byte)'X');
        var (o, ws) = RunFull(img);
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Errors, e => e.Contains("$LogFile necopiat") && e.Contains("nu are numele"));   // the reader's reason, not any error
        Assert.Contains(o.Evidence, e => e.SourceType == LogAnalyzer.Dfir.FileSystem.MftParser.SourceTypeName);
        Assert.Equal(2, o.Evidence.Count(e => e.SourceType == LogAnalyzer.Dfir.FileSystem.I30Parser.SourceTypeName));
        var usn = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.UsnSourceType);
        Assert.Equal("usn_C.csv", Path.GetFileName(usn.StoredPath));
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == NtfsMetadataCollector.LogFileSourceType);
        Assert.DoesNotContain(AllFiles(ws), f => Path.GetFileName(f) == "$LogFile");
        Assert.Contains(AllFiles(ws), f => Path.GetFileName(f) == "usn_C.csv");
        Assert.Equal("Error", Manifest(ws, o).GetProperty("logFile").GetProperty("status").GetString());
    }

    [Fact]
    public void A_valid_data_length_under_the_size_keeps_the_logfile_with_a_gap_and_partial()
    {
        var img = Mutated((i, off, _, data) => BinaryPrimitives.WriteInt64LittleEndian(i.AsSpan(off + data.Offset + 0x38), data.RealSize - 4096));
        var (o, ws, _) = Run(img);
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Evidence, e => e.SourceType == NtfsMetadataCollector.LogFileSourceType);
        var man = Manifest(ws, o);
        Assert.Contains(man.GetProperty("gaps").EnumerateArray().Select(g => g.GetString()), g => g!.Contains("valid data length"));
        var b = man.GetProperty("logFile");
        Assert.Equal("Copied", b.GetProperty("status").GetString());
        Assert.True(b.GetProperty("validDataLength").GetInt64() < b.GetProperty("size").GetInt64());
    }

    [Fact]
    public void A_bad_restart_magic_keeps_the_file_and_makes_the_run_partial_with_a_gap()
    {
        var img = NtfsVolumeReaderTests.ImageBytes.ToArray();
        img.AsSpan((int)LogFileImageOffset(img), 4).Clear();
        var (o, ws, _) = Run(img);
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Evidence, e => e.SourceType == NtfsMetadataCollector.LogFileSourceType);
        Assert.Contains(o.Errors, e => e.Contains("$LogFile nu are pagina de restart validă la offset 0 (magie 0x00000000)"));
        var man = Manifest(ws, o);
        Assert.Contains(man.GetProperty("gaps").EnumerateArray().Select(g => g.GetString()), g => g!.Contains("offset 0 (magie 0x00000000)"));
        Assert.Equal("Copied", man.GetProperty("logFile").GetProperty("status").GetString());
    }

    /// <summary>Cancels on the first read inside the $LogFile data area (only CopyLogFile reads there) and notes whether $MFT was already written at that moment.</summary>
    private sealed class CancelInLogFileStream(byte[] data, long from, long to, CancellationTokenSource cts, Func<bool> mftWritten) : MemoryStream(data, writable: false)
    {
        public int ReadsInLogFile { get; private set; }
        public bool MftWrittenAtTrigger { get; private set; }
        public long FurthestInLogFile { get; private set; }   // highest image offset read inside the $LogFile area
        public override int Read(byte[] buffer, int offset, int count) { Check(count); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Check(buffer.Length); return base.Read(buffer); }
        private void Check(int count)
        {
            if (Position < from || Position >= to) return;
            FurthestInLogFile = Math.Max(FurthestInLogFile, Math.Min(to, Position + count));
            if (++ReadsInLogFile == 1) { MftWrittenAtTrigger = mftWritten(); cts.Cancel(); }
        }
    }

    [Fact]
    public void Cancellation_while_the_logfile_is_copied_leaves_no_file_and_deletes_the_shadow_copy()
    {
        var img = NtfsVolumeReaderTests.ImageBytes.ToArray();
        using var cts = new CancellationTokenSource();
        var snap = new FakeShadow();
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_cancel"), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        long from = LogFileImageOffset(img);
        var stream = new CancelInLogFileStream(img, from, from + Record2(img).Data.RealSize, cts, () => AllFiles(ws).Any(f => Path.GetFileName(f) == "$MFT"));
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => stream, @"C:\", Clock, _ => []);
        Assert.ThrowsAny<OperationCanceledException>(() => c.Collect(new CollectorContext { Case = ws, IsElevated = true }, cts.Token));
        Assert.True(stream.ReadsInLogFile >= 1, "CopyLogFile never started reading");
        Assert.True(stream.FurthestInLogFile <= from + (1 << 20), $"the copy went on past its first 1 MiB chunk after the cancellation ({stream.FurthestInLogFile - from:N0} bytes read)");
        Assert.True(stream.MftWrittenAtTrigger, "$MFT must already be on disk when $LogFile starts");
        Assert.Empty(AllFiles(ws));   // neither $LogFile nor $MFT nor $Boot remain
        Assert.Equal([SnapId], snap.Deleted);
        Assert.Equal(2, snap.Live);
    }

    [Fact]
    public void NtfsMetadata_is_second_in_collect_everything_right_after_live_state()
    {
        Assert.IsType<LiveStateCollector>(InvestigationPipeline.AllCollectors[0]);
        Assert.IsType<NtfsMetadataCollector>(InvestigationPipeline.AllCollectors[1]);
        Assert.Single(InvestigationPipeline.AllCollectors, x => x is NtfsMetadataCollector);
    }

    [Fact]
    public void A_logfile_without_a_parser_is_listed_as_by_design_not_as_a_failure()
    {
        var note = InvestigationPipeline.AuxiliaryNote(NtfsMetadataCollector.LogFileSourceType);
        Assert.NotNull(note);
        Assert.Contains("nu are încă parser", note);
    }
}
