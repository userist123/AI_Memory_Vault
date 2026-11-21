using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The raw NTFS volume reader. Oracle: Corpus/Ntfs/volume.raw.gz is the raw image of the test volume; mft.bin and many_i30.bin were extracted from the SAME volume by
/// Windows (fsutil file queryextents), independent of this code. Threat T3 (hostile boot sector / record 0 / data runs / attribute list) and T6 (short read or I/O
/// error = explicit error, never zero-filled bytes) use synthetic volumes and faulty streams.
/// </summary>
public sealed partial class NtfsVolumeReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_ntfsvr_" + Guid.NewGuid().ToString("N"));
    public NtfsVolumeReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Corpus(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);
    private static string BS => ((char)92).ToString();

    private static readonly Lazy<byte[]> Image = new(() =>
    {
        using var gz = new GZipStream(File.OpenRead(Corpus("volume.raw.gz")), CompressionMode.Decompress);
        var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    });
    internal static MemoryStream OpenImage() => new(Image.Value, writable: false);
    internal static byte[] ImageBytes => Image.Value;
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    // ================================================================ oracle

    [Fact]
    public void Oracle_image_is_the_66973696_byte_volume()
        => Assert.Equal(66_973_696, Image.Value.Length);

    [Fact]
    public void Oracle_mft_extracted_from_the_raw_volume_equals_windows_mft_bin_byte_for_byte()
    {
        var expected = File.ReadAllBytes(Corpus("mft.bin"));
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var res = reader.CopyMft(o);
        Assert.Equal(262_144, expected.Length);
        Assert.Equal(expected.Length, res.Size);
        Assert.Equal(Sha(expected), res.Sha256);                  // SHA expected = SHA of Windows' extraction
        Assert.Equal(Sha(o.ToArray()), res.Sha256);               // SHA computed on the bytes written
        Assert.True(expected.AsSpan().SequenceEqual(o.ToArray()));
    }

    [Fact]
    public void Oracle_boot_sector_equals_boot_bin()
    {
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        Assert.True(File.ReadAllBytes(Corpus("boot.bin")).AsSpan().SequenceEqual(reader.BootSectorBytes));
        Assert.Equal(4096, reader.Boot.ClusterSize);
    }

    [Fact]
    public void Oracle_i30_of_many_equals_many_i30_bin_byte_for_byte()
    {
        var expected = File.ReadAllBytes(Corpus("many_i30.bin"));
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var res = reader.CopyDirectoryIndex(BS + "many", o);
        Assert.Equal(NtfsDirectoryStatus.Copied, res.Status);
        Assert.Equal(12_288, res.Copy!.Size);
        Assert.Equal(Sha(expected), res.Copy.Sha256);
        Assert.True(expected.AsSpan().SequenceEqual(o.ToArray()));
        Assert.Equal(Sha(o.ToArray()), res.Copy.Sha256);
    }

    [Fact]
    public void Oracle_paths_are_case_insensitive_and_tolerate_trailing_separator()
    {
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var res = reader.CopyDirectoryIndex(BS + "MANY" + BS, o);
        Assert.Equal(NtfsDirectoryStatus.Copied, res.Status);
        Assert.Equal(12_288, o.Length);
    }

    [Fact]
    public void Oracle_missing_directory_is_reported_and_nothing_is_copied()
    {
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var res = reader.CopyDirectoryIndex(BS + "does" + BS + "not" + BS + "exist", o);
        Assert.Equal(NtfsDirectoryStatus.NotFound, res.Status);
        Assert.Null(res.Copy);
        Assert.Equal(0, o.Length);
    }

    [Fact]
    public void Oracle_directory_without_index_allocation_is_reported_and_nothing_is_copied()
    {
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        var seen = new List<string>();
        foreach (var name in reader.ListChildDirectories(BS).Concat(["$Extend"]))
        {
            using var o = new MemoryStream();
            var res = reader.CopyDirectoryIndex(BS + name, o);
            seen.Add(name + "=" + res.Status);
            if (res.Status != NtfsDirectoryStatus.NoIndexAllocation) continue;
            Assert.Null(res.Copy);
            Assert.Equal(0, o.Length);
            return;
        }
        Assert.Fail("niciun director fără $INDEX_ALLOCATION: " + string.Join(", ", seen));
    }

    [Fact]
    public void Oracle_root_children_include_docs_and_many()
    {
        using var vol = OpenImage();
        var names = NtfsVolumeReader.Open(vol).ListChildDirectories(BS);
        Assert.Contains("docs", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("many", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(names, n => n.Equals("$MFT", StringComparison.OrdinalIgnoreCase));   // files are not directories
    }

    [Fact]
    public void Oracle_parsers_give_the_same_result_on_the_extracted_files_as_on_the_corpus_files()
    {
        using var vol = OpenImage();
        var reader = NtfsVolumeReader.Open(vol);
        var d = Path.Combine(_dir, "extracted"); Directory.CreateDirectory(d);
        using (var f = File.Create(Path.Combine(d, "$MFT"))) reader.CopyMft(f);
        File.WriteAllBytes(Path.Combine(d, "$Boot"), reader.BootSectorBytes);
        using (var f = File.Create(Path.Combine(d, "many.i30"))) Assert.Equal(NtfsDirectoryStatus.Copied, reader.CopyDirectoryIndex(BS + "many", f).Status);

        var c = Path.Combine(_dir, "corpus"); Directory.CreateDirectory(c);
        File.Copy(Corpus("mft.bin"), Path.Combine(c, "$MFT")); File.Copy(Corpus("boot.bin"), Path.Combine(c, "$Boot")); File.Copy(Corpus("many_i30.bin"), Path.Combine(c, "many.i30"));

        var acquired = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var mftA = RunParser(new MftParser(), MftParser.SourceTypeName, Path.Combine(d, "$MFT"), acquired);
        var mftB = RunParser(new MftParser(), MftParser.SourceTypeName, Path.Combine(c, "$MFT"), acquired);
        Assert.NotEmpty(mftB);
        Assert.Equal(mftB, mftA);
        var i30A = RunParser(new I30Parser(), I30Parser.SourceTypeName, Path.Combine(d, "many.i30"), acquired);
        var i30B = RunParser(new I30Parser(), I30Parser.SourceTypeName, Path.Combine(c, "many.i30"), acquired);
        Assert.NotEmpty(i30B);
        Assert.Equal(i30B, i30A);
    }

    private static List<string> RunParser(EvidenceParserBase p, string sourceType, string path, DateTimeOffset acquired)
    {
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "EV-X", CaseId = "C", Source = "test", SourceType = sourceType, StoredPath = path, AcquiredAtUtc = acquired };
        var r = p.Parse(item, path, sink, default);
        var lines = sink.Events.Select(e => string.Join("|", e.Time.ToString(), e.Summary, e.Path, e.Locator, string.Join(";", e.Fields.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value)))).ToList();
        lines.AddRange(r.Gaps.Select(g => "gap:" + g.Reason));
        return lines;
    }

    // ================================================================ T6: short reads and I/O errors

    /// <summary>Returns at most <paramref name="chunk"/> bytes per call, ends at <paramref name="eofAt"/>, throws IOException once past <paramref name="failAt"/>.</summary>
    private sealed class FaultyStream(byte[] data, int chunk = int.MaxValue, long eofAt = long.MaxValue, long failAt = long.MaxValue) : Stream
    {
        private long _pos;
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => Math.Min(data.Length, eofAt);
        public override long Position { get => _pos; set => _pos = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= failAt) throw new IOException("eroare de I/O simulată");
            long avail = Math.Min(Math.Min(data.Length, eofAt), failAt) - _pos;
            if (avail <= 0) return 0;
            int n = (int)Math.Min(Math.Min(count, chunk), avail);
            Array.Copy(data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => _pos = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _pos + offset, _ => Length + offset };
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private const long MftByteOffset = 0x154AL * 4096;

    [Fact]
    public void T6_a_stream_that_returns_only_a_few_bytes_per_call_still_gives_the_exact_bytes()
    {
        using var vol = new FaultyStream(Image.Value, chunk: 997);
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var res = reader.CopyMft(o);
        Assert.Equal(Sha(File.ReadAllBytes(Corpus("mft.bin"))), res.Sha256);
    }

    [Fact]
    public void T6_end_of_stream_in_the_middle_of_the_mft_is_an_error_not_zero_padding()
    {
        using var vol = new FaultyStream(Image.Value, eofAt: MftByteOffset + 100_000);
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var ex = Assert.Throws<NtfsReadException>(() => reader.CopyMft(o));
        Assert.Contains("citire scurtă", ex.Message);
    }

    [Fact]
    public void T6_io_error_in_the_middle_of_the_mft_is_an_explicit_error()
    {
        using var vol = new FaultyStream(Image.Value, failAt: MftByteOffset + 100_000);
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        var ex = Assert.Throws<NtfsReadException>(() => reader.CopyMft(o));
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public void T6_io_error_while_copying_the_i30_blocks_is_an_explicit_error()
    {
        using var vol = new FaultyStream(Image.Value, failAt: 0x5D4L * 4096 + 4096);
        var reader = NtfsVolumeReader.Open(vol);
        using var o = new MemoryStream();
        Assert.Throws<NtfsReadException>(() => reader.CopyDirectoryIndex(BS + "many", o));
    }

    [Fact]
    public void T6_io_error_on_the_boot_sector_is_an_explicit_error()
    {
        using var vol = new FaultyStream(Image.Value, failAt: 0);
        Assert.Throws<NtfsReadException>(() => NtfsVolumeReader.Open(vol));
    }

    // ================================================================ T3: hostile boot sector / record 0 / data runs / attribute list

    private const int Cl = 4096;

    /// <summary>A 64-cluster volume: boot sector, record 0 (and optional records 1..) at cluster 4. Record size 1024.</summary>
    private static byte[] Vol(byte[] record0, params byte[][] more) => VolWith(1024, record0, more);

    private static byte[] VolWith(int recSize, byte[] record0, params byte[][] more)
    {
        var v = new byte[64 * Cl];
        var boot = BootSector(clustersPerRecord: recSize == 1024 ? (byte)0xF6 : (byte)0xF0);
        BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(0x28), 64 * 8); BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(0x30), 4);
        boot.CopyTo(v, 0);
        record0.CopyTo(v, 4 * Cl);
        for (int i = 0; i < more.Length; i++) more[i].CopyTo(v, 4 * Cl + (i + 1) * recSize);
        // recognisable payload in clusters 6..7 so that a wrong copy cannot hash the same
        if (recSize == 1024) for (int i = 6 * Cl; i < 8 * Cl; i++) v[i] = (byte)(i * 31 + 7);
        return v;
    }

    private static byte[] Data(long low, long high, byte[] mapping, long size, long? alloc = null, long? vdl = null, ushort? flags = null)
    {
        var a = NonResident(0x80, low, high, mapping, size);
        if (alloc is long al) BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(40), al);
        if (vdl is long vd) BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(56), vd);
        if (flags is ushort f) BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(12), f);
        return a;
    }

    private static byte[] Rec0(params byte[][] attrs) => Record(1, InUse, 0, attrs);
    private static byte[] WithNumber(byte[] rec, uint n) { BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(44), n); return rec; }
    private static readonly byte[] TwoClustersAt4 = [0x11, 0x02, 0x04, 0x00];

    private static NtfsVolumeReader OpenBytes(byte[] vol) => NtfsVolumeReader.Open(new MemoryStream(vol));
    private static NtfsReadException CopyFails(byte[] vol) { using var o = new MemoryStream(); return Assert.Throws<NtfsReadException>(() => OpenBytes(vol).CopyMft(o)); }

    [Fact]
    public void T3_the_synthetic_volume_with_valid_runs_is_copied_exactly()
    {
        var vol = Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl)));
        using var o = new MemoryStream();
        var res = OpenBytes(vol).CopyMft(o);
        Assert.Equal(2 * Cl, res.Size);
        Assert.True(vol.AsSpan(4 * Cl, 2 * Cl).SequenceEqual(o.ToArray()));
        Assert.Equal(Sha(vol.AsSpan(4 * Cl, 2 * Cl).ToArray()), res.Sha256);
    }

    [Fact]
    public void T3_boot_sector_with_the_mft_lcn_outside_the_volume_is_refused()
    {
        var vol = Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl)));
        BinaryPrimitives.WriteUInt64LittleEndian(vol.AsSpan(0x30), 1UL << 40);
        Assert.Throws<NtfsReadException>(() => OpenBytes(vol));
        BinaryPrimitives.WriteUInt64LittleEndian(vol.AsSpan(0x30), 64);   // first cluster after the end
        Assert.Throws<NtfsReadException>(() => OpenBytes(vol));
    }

    [Fact]
    public void T3_boot_sector_that_is_not_ntfs_is_refused()
    {
        var vol = Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl)));
        vol[3] = (byte)'X';
        Assert.Throws<NtfsReadException>(() => OpenBytes(vol));
    }

    [Fact]
    public void T3_runs_with_a_negative_offset_are_refused()
        => Assert.Contains("negativ", CopyFails(Vol(Rec0(Data(0, 1, [0x11, 0x02, 0xFB, 0x00], 2 * Cl)))).Message);

    [Fact]
    public void T3_a_run_of_enormous_length_is_refused()
        => Assert.Contains("în afara volumului", CopyFails(Vol(Rec0(Data(0, 0xFFFFFFFEL, [0x51, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x04, 0x00], 2 * Cl)))).Message);

    [Fact]
    public void T3_overlapping_runs_are_refused()
        => Assert.Contains("suprap", CopyFails(Vol(Rec0(Data(0, 3, [0x11, 0x02, 0x04, 0x11, 0x02, 0xFF, 0x00], 4 * Cl)))).Message);

    [Fact]
    public void T3_runs_that_sum_to_less_than_the_declared_clusters_are_refused()
        => CopyFails(Vol(Rec0(Data(0, 1, [0x11, 0x01, 0x04, 0x00], 2 * Cl))));

    [Fact]
    public void T3_runs_that_cover_fewer_bytes_than_the_real_size_are_refused()
        => Assert.Contains("acoper", CopyFails(Vol(Rec0(Data(0, 1, TwoClustersAt4, 100_000)))).Message);

    [Fact]
    public void T3_valid_data_length_beyond_real_size_is_refused()
        => CopyFails(Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl, vdl: 3 * Cl))));

    [Fact]
    public void T3_a_sparse_run_in_an_attribute_without_the_sparse_flag_is_refused()
        => Assert.Contains("sparse", CopyFails(Vol(Rec0(Data(0, 2, [0x11, 0x02, 0x04, 0x01, 0x01, 0x00], 3 * Cl)))).Message);

    [Fact]
    public void T3_first_run_that_does_not_start_at_the_boot_mft_lcn_is_refused()
        => Assert.Contains("boot", CopyFails(Vol(Rec0(Data(0, 1, [0x11, 0x02, 0x06, 0x00], 2 * Cl)))).Message);

    [Fact]
    public void T3_an_mft_size_above_the_cap_is_refused()
        => CopyFails(Vol(Rec0(Data(0, (1L << 40) / Cl - 1, [0x11, 0x02, 0x04, 0x00], 1L << 40, flags: 0x8000))));

    [Fact]
    public void T3_a_compressed_data_attribute_is_refused()
        => CopyFails(Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl, flags: 0x0001))));

    [Fact]
    public void T3_record_zero_that_is_not_a_file_record_is_refused()
    {
        var vol = Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl)));
        vol[4 * Cl] = (byte)'X';
        Assert.Throws<NtfsReadException>(() => OpenBytes(vol).CopyMft(new MemoryStream()));
    }

    [Fact]
    public void T3_record_zero_with_a_torn_update_sequence_is_refused()
    {
        var vol = Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl)));
        vol[4 * Cl + 510] ^= 0xFF;
        Assert.Throws<NtfsReadException>(() => OpenBytes(vol).CopyMft(new MemoryStream()));
    }

    [Fact]
    public void T3_record_zero_without_a_data_attribute_is_refused()
        => CopyFails(Vol(Rec0(Resident(0x10, Si(1, 1, 1, 1)))));

    [Fact]
    public void T3_thirty_thousand_runs_are_refused_by_the_run_cap()
    {
        // 64 KiB record: room for ~30,000 two-byte sparse runs (a 1 KiB record cannot hold more than a few hundred)
        const int rs = 65536, n = 30_000;
        var mapping = new List<byte> { 0x11, 0x01, 0x04 };
        for (int i = 1; i < n; i++) { mapping.Add(0x01); mapping.Add(0x01); }
        mapping.Add(0);
        var rec = BigRecord(rs, Data(0, n - 1, [.. mapping], n * (long)Cl, flags: 0x8000));
        var ex = CopyFails(VolWith(rs, rec));
        Assert.Contains("run-uri", ex.Message);
    }

    /// <summary>A FILE record of an arbitrary size (NtfsTestKit.Record is fixed at 1,024).</summary>
    private static byte[] BigRecord(int size, params byte[][] attrs)
    {
        var r = new byte[size];
        "FILE"u8.CopyTo(r);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), 0x30); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6), (ushort)(size / 512 + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(16), 1); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(20), 0x150); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(22), InUse);
        int p = 0x150;
        foreach (var a in attrs) { a.CopyTo(r, p); p += a.Length; }
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(p), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(24), (uint)(p + 8)); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(28), (uint)size);
        return Fixup(r);
    }

    private static byte[] ListEntry(uint type, ulong startVcn, ulong reference, ushort id = 0)
    {
        var e = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0), type); BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4), 32);
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(8), startVcn); BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(16), reference);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(24), id);
        return e;
    }

    private static byte[] ExtRecord(ushort seq, ulong baseRef, params byte[][] attrs) => WithNumber(Record(seq, InUse, baseRef, attrs), 1);

    [Fact]
    public void T3_a_data_segment_in_an_extension_record_is_followed_through_the_attribute_list()
    {
        // segment 0 (VCN 0..0) in record 0 at LCN 4, segment 1 (VCN 1..1) in record 1 at LCN 6; two clusters in all
        var list = Concat(ListEntry(0x80, 0, Ref(0, 1)), ListEntry(0x80, 1, Ref(1, 1)));
        var r0 = Rec0(Data(0, 0, [0x11, 0x01, 0x04, 0x00], 2 * Cl), Resident(0x20, list));
        var r1 = ExtRecord(1, Ref(0, 1), Data(1, 1, [0x11, 0x01, 0x06, 0x00], 2 * Cl));
        var vol = Vol(r0, r1);
        using var o = new MemoryStream();
        var res = OpenBytes(vol).CopyMft(o);
        Assert.Equal(2 * Cl, res.Size);
        var expected = vol.AsSpan(4 * Cl, Cl).ToArray().Concat(vol.AsSpan(6 * Cl, Cl).ToArray()).ToArray();
        Assert.True(expected.AsSpan().SequenceEqual(o.ToArray()));
    }

    [Fact]
    public void T3_a_cyclic_attribute_list_is_refused()
    {
        var list0 = Concat(ListEntry(0x80, 0, Ref(0, 1)), ListEntry(0x80, 1, Ref(1, 1)));
        var list1 = Concat(ListEntry(0x80, 1, Ref(1, 1)), ListEntry(0x20, 0, Ref(0, 1)));   // the extension record lists record 0 again
        var r0 = Rec0(Data(0, 0, [0x11, 0x01, 0x04, 0x00], 2 * Cl), Resident(0x20, list0));
        var r1 = ExtRecord(1, Ref(0, 1), Resident(0x20, list1), Data(1, 1, [0x11, 0x01, 0x06, 0x00], 2 * Cl));
        Assert.Contains("ciclic", CopyFails(Vol(r0, r1)).Message);
    }

    [Fact]
    public void T3_an_attribute_list_entry_with_a_stale_sequence_is_refused()
    {
        var list = Concat(ListEntry(0x80, 0, Ref(0, 1)), ListEntry(0x80, 1, Ref(1, 9)));   // record 1 has sequence 1
        var r0 = Rec0(Data(0, 0, [0x11, 0x01, 0x04, 0x00], 2 * Cl), Resident(0x20, list));
        var r1 = ExtRecord(1, Ref(0, 1), Data(1, 1, [0x11, 0x01, 0x06, 0x00], 2 * Cl));
        Assert.Contains("secven", CopyFails(Vol(r0, r1)).Message);
    }

    [Fact]
    public void T3_an_extension_record_that_belongs_to_another_base_is_refused()
    {
        var list = Concat(ListEntry(0x80, 0, Ref(0, 1)), ListEntry(0x80, 1, Ref(1, 1)));
        var r0 = Rec0(Data(0, 0, [0x11, 0x01, 0x04, 0x00], 2 * Cl), Resident(0x20, list));
        var r1 = ExtRecord(1, Ref(77, 1), Data(1, 1, [0x11, 0x01, 0x06, 0x00], 2 * Cl));
        CopyFails(Vol(r0, r1));
    }

    [Fact]
    public void T3_a_missing_segment_leaves_a_gap_in_the_vcn_chain_and_is_refused()
    {
        var list = Concat(ListEntry(0x80, 0, Ref(0, 1)));   // the second segment is not listed
        var r0 = Rec0(Data(0, 0, [0x11, 0x01, 0x04, 0x00], 2 * Cl), Resident(0x20, list));
        CopyFails(Vol(r0));
    }

    [Fact]
    public void T3_a_malformed_attribute_list_is_refused()
    {
        var list = new byte[32];   // length field 0: cannot advance
        var r0 = Rec0(Data(0, 0, [0x11, 0x01, 0x04, 0x00], 2 * Cl), Resident(0x20, list));
        CopyFails(Vol(r0));
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
