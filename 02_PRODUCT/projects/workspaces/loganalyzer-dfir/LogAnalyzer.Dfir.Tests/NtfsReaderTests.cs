using Xunit;
using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>S1: the NTFS record reader. Synthetic records for exact values and the hostile-input probes P1–P6, P8, P9; real records for truncation at every offset.</summary>
public sealed class NtfsReaderTests
{
    private static string CorpusPath(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);
    private static readonly long T0 = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    private static void WithTimeout(Action a, int seconds = 60)
    {
        var t = Task.Run(a);
        Assert.True(t.Wait(TimeSpan.FromSeconds(seconds)), "timeout: posibilă buclă infinită");
    }

    // ---- boot sector ----
    [Fact]
    public void Boot_sector_of_the_corpus_gives_1024_byte_records()
    {
        var b = NtfsBootSector.TryParse(File.ReadAllBytes(CorpusPath("boot.bin")), out var err);
        Assert.NotNull(b); Assert.Null(err);
        Assert.Equal(1024, b!.MftRecordSize); Assert.Equal(512, b.BytesPerSector); Assert.Equal(8, b.SectorsPerCluster); Assert.Equal(0x154Au, b.MftLcn);
    }

    [Fact] // P8
    public void P8_boot_sector_with_shift_128_and_bad_geometry_is_rejected()
    {
        Assert.NotNull(NtfsBootSector.TryParse(BootSector(), out _));
        Assert.Null(NtfsBootSector.TryParse(BootSector(0x80), out var e1)); Assert.Contains("exponent", e1);   // −128: a shift would wrap
        Assert.Null(NtfsBootSector.TryParse(BootSector(0xE0), out _));   // −32
        Assert.Null(NtfsBootSector.TryParse(BootSector(0x00), out _));
        Assert.Equal(1024, NtfsBootSector.TryParse(BootSector(0xF6), out _)!.MftRecordSize);
        Assert.Equal(4096, NtfsBootSector.TryParse(BootSector(0xF4, 4096, 1), out _)!.MftRecordSize);
        foreach (ushort bps in new ushort[] { 0, 1, 100, 255, 513, 8192, 65535 })
            Assert.Null(NtfsBootSector.TryParse(BootSector(0xF6, bps), out _));
        Assert.Null(NtfsBootSector.TryParse(BootSector(0xF6, 512, 0), out _));
        Assert.Null(NtfsBootSector.TryParse(BootSector(0xF6, 512, 3), out _));
        Assert.Null(NtfsBootSector.TryParse(BootSector(0xF6, 512, 200), out _));
        Assert.Null(NtfsBootSector.TryParse(new byte[100], out _));
        var noSig = BootSector(); noSig[511] = 0; Assert.Null(NtfsBootSector.TryParse(noSig, out _));
        var mftOutside = BootSector(); BinaryPrimitives.WriteUInt64LittleEndian(mftOutside.AsSpan(0x30), ulong.MaxValue); Assert.Null(NtfsBootSector.TryParse(mftOutside, out _));
        for (int v = 0; v < 256; v++) NtfsBootSector.TryParse(BootSector((byte)v), out _);   // no byte value throws
    }

    // ---- file record ----
    [Fact]
    public void Synthetic_record_gives_exact_header_and_attributes()
    {
        var raw = FileRecord(7, Ref(5, 5), "hello.txt", T0, T0 + 10, size: 5);
        var r = NtfsFileRecord.Parse(raw, RecSize);
        Assert.Equal(NtfsRecordState.Ok, r.State); Assert.Equal((ushort)7, r.Sequence); Assert.True(r.InUse); Assert.False(r.IsDirectory); Assert.True(r.IsBaseRecord);
        Assert.Equal(new uint[] { 0x10, 0x30, 0x80 }, r.Attributes.Select(a => a.Type).ToArray());
        var si = StandardInformation.TryParse(r.ValueOf(r.Attributes[0]))!;
        Assert.Equal(T0, si.Created);
        var fn = FileNameAttribute.TryParse(r.ValueOf(r.Attributes[1]))!;
        Assert.Equal("hello.txt", fn.Name); Assert.Equal(T0 + 10, fn.Created); Assert.Equal(5ul, fn.Parent.Number); Assert.Equal((ushort)5, fn.Parent.Sequence); Assert.Equal((byte)1, fn.Namespace);
        Assert.Equal(5, r.Attributes[2].ValueLength);
    }

    [Fact]
    public void Fixup_restores_the_bytes_hidden_under_the_update_sequence()
    {
        var plain = new byte[RecSize];
        "FILE"u8.CopyTo(plain); BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(4), 0x30); BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(6), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(20), 56); BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(24), 64); BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(28), 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(56), 0xFFFFFFFF);
        plain[510] = 0xAB; plain[511] = 0xCD; plain[1022] = 0xEF; plain[1023] = 0x01;
        var r = NtfsFileRecord.Parse(Fixup(plain), RecSize);
        Assert.Equal(NtfsRecordState.Ok, r.State);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, r.Bytes.Slice(510, 2).ToArray()); Assert.Equal(new byte[] { 0xEF, 0x01 }, r.Bytes.Slice(1022, 2).ToArray());
    }

    [Fact] // P1
    public void P1_update_sequence_out_of_bounds_or_wrong_count_is_invalid()
    {
        foreach (var (off, count) in new[] { (0x30, 2), (0x30, 4), (0x30, 0), (0x30, 65535), (600, 3), (0, 3), (8, 3), (41, 3), (508, 3), (506, 3), (65535, 3) })
        {
            var raw = (byte[])FileRecord(1, Ref(5, 5), "a", T0, T0).Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4), (ushort)off); BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(6), (ushort)count);
            var r = NtfsFileRecord.Parse(raw, RecSize);
            Assert.Equal(NtfsRecordState.Invalid, r.State);
            Assert.Empty(r.Attributes);
        }
        Assert.Equal(NtfsRecordState.Invalid, NtfsFileRecord.Parse(FileRecord(1, Ref(5, 5), "a", T0, T0), 1000).State);   // record size not a power of two
        Assert.Equal(NtfsRecordState.Invalid, NtfsFileRecord.Parse(new byte[1 << 20], 1 << 20).State);   // oversized: no allocation of that size
    }

    [Fact] // P2
    public void P2_fixup_mismatch_is_torn_and_every_position_is_checked_before_restoring()
    {
        var good = FileRecord(1, Ref(5, 5), "a", T0, T0);
        foreach (int pos in new[] { 510, 1022 })
        {
            var raw = (byte[])good.Clone(); raw[pos] ^= 0xFF;
            var r = NtfsFileRecord.Parse(raw, RecSize);
            Assert.Equal(NtfsRecordState.Torn, r.State);
            Assert.Empty(r.Attributes);
            Assert.True(r.Bytes.IsEmpty);   // nothing restored from a torn record
        }
        // sector 1 matches, only the last sector is torn: still torn
        var second = (byte[])good.Clone(); second[1023] ^= 0x55;
        Assert.Equal(NtfsRecordState.Torn, NtfsFileRecord.Parse(second, RecSize).State);
        // BAAD is marked, not an exception
        var baad = (byte[])good.Clone(); "BAAD"u8.CopyTo(baad);
        var b = NtfsFileRecord.Parse(baad, RecSize); Assert.Equal(NtfsRecordState.Baad, b.State); Assert.Empty(b.Attributes);
        Assert.Equal(NtfsRecordState.NotRecord, NtfsFileRecord.Parse(new byte[RecSize], RecSize).State);
        // the caller's buffer is untouched
        var snapshot = (byte[])good.Clone(); NtfsFileRecord.Parse(good, RecSize); Assert.Equal(snapshot, good);
    }

    private static byte[] RawWithAttribute(byte[] attr)
    {
        var r = new byte[RecSize];
        "FILE"u8.CopyTo(r); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), 0x30); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(20), 56); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(22), 1);
        attr.CopyTo(r, 56);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(56 + attr.Length), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(24), (uint)(56 + attr.Length + 8)); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(28), 1024);
        return Fixup(r);
    }

    [Fact] // P3
    public void P3_attribute_length_zero_unaligned_or_beyond_the_record_stops_the_walk()
    {
        foreach (uint len in new uint[] { 0, 1, 7, 12, 15, 20, 0x7FFFFFF8, 0xFFFFFFF8, 4096, 1024 })
        {
            var attr = Resident(0x10, Si(T0, T0, T0, T0), lengthOverride: (int)len);
            var raw = RawWithAttribute(attr);
            WithTimeout(() =>
            {
                var r = NtfsFileRecord.Parse(raw, RecSize);
                Assert.Equal(NtfsRecordState.Ok, r.State);
                Assert.Empty(r.Attributes);
                Assert.True(r.AttributeProblems > 0);
            });
        }
        // a good attribute followed by a zero-length one keeps the good one
        var good = Resident(0x10, Si(T0, T0, T0, T0));
        var bad = Resident(0x30, new byte[80], lengthOverride: 0);
        var rec = NtfsFileRecord.Parse(RawWithAttribute([.. good, .. bad]), RecSize);
        Assert.Single(rec.Attributes); Assert.Equal(1, rec.AttributeProblems);
    }

    [Fact] // P3: used / allocated bound the loop
    public void P3_walk_stops_at_used_size_even_without_an_end_marker()
    {
        var a = Resident(0x10, Si(T0, T0, T0, T0));
        var raw = RawWithAttribute(a);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(56 + a.Length), 0);   // no 0xFFFFFFFF
        var un = new byte[RecSize]; Array.Copy(raw, un, RecSize);
        // rebuild fixup after the edit (edit was under sector 0 only, tails untouched)
        var r = NtfsFileRecord.Parse(un, RecSize);
        Assert.Single(r.Attributes);   // second iteration finds type 0 with length 0 → problem, not a loop
    }

    [Fact] // P4
    public void P4_name_length_times_two_beyond_the_buffer_is_rejected_without_exception()
    {
        // attribute name: nameLen 200 in a 24+value attribute
        var attr = Resident(0x80, new byte[8], name: "ab");
        attr[9] = 200;
        var rec = NtfsFileRecord.Parse(RawWithAttribute(attr), RecSize);
        Assert.Empty(rec.Attributes); Assert.Equal(1, rec.AttributeProblems);
        // $FILE_NAME: name length byte says 255 characters, value holds 3
        var fn = Fn(Ref(5, 5), "abc", T0, T0, T0, T0, nameLenField: 255);
        Assert.Null(FileNameAttribute.TryParse(fn));
        Assert.Null(FileNameAttribute.TryParse(fn.AsSpan(0, 65)));
        var badNs = Fn(Ref(5, 5), "abc", T0, T0, T0, T0, ns: 4); Assert.Null(FileNameAttribute.TryParse(badNs));
        // name offset pointing back into the header
        var attr2 = Resident(0x80, new byte[8], name: "ab"); BinaryPrimitives.WriteUInt16LittleEndian(attr2.AsSpan(10), 2);
        Assert.Empty(NtfsFileRecord.Parse(RawWithAttribute(attr2), RecSize).Attributes);
    }

    [Fact]
    public void Names_decode_without_exception_on_unpaired_surrogates()
    {
        var bytes = new byte[] { 0x41, 0, 0x00, 0xD8, 0x42, 0, 0x00, 0xDC, 0x3D, 0xD8, 0x00, 0xDE };   // A, lone high, B, lone low, valid pair
        var fn = FileNameAttribute.TryParse(Fn(Ref(5, 5), "", T0, T0, T0, T0, nameBytes: bytes))!;
        Assert.True(fn.NameHadInvalidUtf16);
        Assert.Equal("A�B�\U0001F600", fn.Name);
        Assert.NotNull(Encoding.UTF8.GetBytes(fn.Name));
    }

    [Fact]
    public void StandardInformation_accepts_48_and_72_and_rejects_shorter()
    {
        Assert.NotNull(StandardInformation.TryParse(Si(1, 2, 3, 4, 48)));
        Assert.Null(StandardInformation.TryParse(Si(1, 2, 3, 4, 48).AsSpan(0, 47)));
        Assert.Null(StandardInformation.TryParse(ReadOnlySpan<byte>.Empty));
        Assert.Null(StandardInformation.TryParse(Si(1, 2, 3, 4, 48))!.Usn);
        Assert.NotNull(StandardInformation.TryParse(Si(1, 2, 3, 4, 72))!.Usn);
    }

    [Fact]
    public void MftReference_splits_48_and_16_bits()
    {
        var r = new MftReference(0x0002_0000_0000_002F);
        Assert.Equal(0x2Ful, r.Number); Assert.Equal((ushort)2, r.Sequence); Assert.Equal("0x0000000000000000000200000000002f", r.FileId);
        Assert.Equal(r, MftReference.From(0x2F, 2));
    }

    // ---- data runs ----
    [Fact]
    public void DataRuns_decode_signed_offsets_and_sparse_runs_without_updating_the_lcn()
    {
        // run1: len 4 @ +100; sparse len 2; run3: len 3 @ +5 → LCN 105 (sparse did not touch it); run4: len 1 @ -50 → 55
        var m = new byte[] { 0x11, 0x04, 0x64, 0x01, 0x02, 0x11, 0x03, 0x05, 0x11, 0x01, 0xCE, 0x00 };
        var res = DataRuns.Decode(m, 0, 9);
        Assert.Null(res.Error);
        Assert.Equal([100L, 105L, 55L], res.Runs.Where(r => !r.IsSparse).Select(r => r.Lcn!.Value).ToArray());
        Assert.Equal(4, res.Runs.Count); Assert.True(res.Runs[1].IsSparse); Assert.Equal(6L, res.Runs[2].Vcn);
    }

    [Fact] // P5
    public void P5_hostile_data_runs_are_rejected_without_exception_or_large_allocation()
    {
        string? Err(byte[] m, long lo = 0, long hi = 0, long total = 0) => DataRuns.Decode(m, lo, hi, total).Error;
        Assert.Null(Err([0x11, 0x01, 0x01, 0x00], 0, 0));                                          // sanity: a valid single run
        Assert.NotNull(Err([0x10, 0x01, 0x00]));                                      // v = 0
        Assert.NotNull(Err([0x19, 1, 2, 3, 4, 5, 6, 7, 8, 9, 1, 0]));                              // v = 9
        Assert.NotNull(Err([0x91, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0]));                              // l = 9
        Assert.NotNull(Err([0x11, 0x00, 0x05, 0x00], 0, -1));                                      // length 0
        Assert.NotNull(Err([0x18, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x05, 0x00], 0, 0)); // length 2^64−1 (> long.MaxValue)
        Assert.NotNull(Err([0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x01, 0x05, 0x00], 0, 0)); // length 2^63
        Assert.Contains("negativ", Err([0x11, 0x02, 0xF0, 0x00], 0, 1) ?? "");                     // LCN −16
        Assert.NotNull(Err([0x11, 0x02, 0x05, 0x00], 0, 4));                                       // sum 2 ≠ 5
        Assert.NotNull(Err([0x11, 0x02, 0x05, 0x00], 0, 0));                                       // sum 2 ≠ 1
        Assert.NotNull(Err([0x11, 0x02], 0, 1));                                                   // truncated
        Assert.NotNull(Err([0x81, 0x02, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0x00], 0, 1, total: 100000)); // LCN huge
        Assert.NotNull(Err([0x11, 0x02, 0x7F, 0x00], 0, 1, total: 100));                            // beyond the volume
        Assert.Null(Err([0x11, 0x02, 0x40, 0x00], 0, 1, total: 100));
        Assert.Null(DataRuns.Decode(ReadOnlySpan<byte>.Empty, 0, -1).Error);                       // empty file: no runs, HighestVCN −1
        // thousands of runs: capped, quick, no huge list
        var many = new List<byte>();
        for (int i = 0; i < 5000; i++) many.AddRange([0x11, 0x01, 0x01]);
        many.Add(0);
        var res = DataRuns.Decode(many.ToArray(), 0, 4999);
        Assert.NotNull(res.Error); Assert.True(res.Runs.Count <= DataRuns.MaxRuns);
        var ok = new List<byte>();
        for (int i = 0; i < 4000; i++) ok.AddRange([0x11, 0x01, 0x01]);
        ok.Add(0);
        Assert.Null(DataRuns.Decode(ok.ToArray(), 0, 3999).Error);
        // arbitrary bytes never throw
        var rnd = new Random(7); var junk = new byte[64];
        for (int i = 0; i < 20000; i++) { rnd.NextBytes(junk); DataRuns.Decode(junk.AsSpan(0, rnd.Next(0, 64)), 0, rnd.Next(-1, 100), rnd.Next(0, 3) * 1000); }
    }

    // ---- attribute list ----
    private static byte[] ListEntry(uint type, ulong record, int lengthOverride = -1)
    {
        var e = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(e, type); BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4), (ushort)(lengthOverride >= 0 ? lengthOverride : 32));
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(16), record);
        return e;
    }

    [Fact] // P6
    public void P6_attribute_list_cycles_self_references_and_zero_lengths_terminate()
    {
        var a = AttributeList.Parse([.. ListEntry(0x30, Ref(11, 1)), .. ListEntry(0x80, Ref(11, 1))], out var mal);
        Assert.False(mal); Assert.Equal(2, a.Count); Assert.Equal(11ul, a[0].Record.Number);
        var lists = new Dictionary<ulong, IReadOnlyList<AttributeListEntry>>
        {
            [10] = AttributeList.Parse(ListEntry(0x30, Ref(11, 1)), out _),
            [11] = AttributeList.Parse(ListEntry(0x80, Ref(10, 1)), out _),   // 10 → 11 → 10
        };
        WithTimeout(() =>
        {
            var found = AttributeList.Walk(10, n => lists.GetValueOrDefault(n), out var cycle, out _);
            Assert.True(cycle); Assert.Equal([11ul], found.ToArray());
        });
        // a list that points only at its own record is just its own attributes
        var self = new Dictionary<ulong, IReadOnlyList<AttributeListEntry>> { [10] = AttributeList.Parse(ListEntry(0x30, Ref(10, 1)), out _) };
        var f2 = AttributeList.Walk(10, n => self.GetValueOrDefault(n), out var c2, out _);
        Assert.False(c2); Assert.Empty(f2);
        // 3-cycle among extension records not including the root
        var l3 = new Dictionary<ulong, IReadOnlyList<AttributeListEntry>>
        {
            [1] = AttributeList.Parse(ListEntry(0x30, Ref(2, 1)), out _), [2] = AttributeList.Parse(ListEntry(0x30, Ref(3, 1)), out _), [3] = AttributeList.Parse(ListEntry(0x30, Ref(2, 1)), out _),
        };
        WithTimeout(() => { AttributeList.Walk(1, n => l3.GetValueOrDefault(n), out var c3, out _); Assert.True(c3); });
        // diamond is not a cycle
        var dia = new Dictionary<ulong, IReadOnlyList<AttributeListEntry>>
        {
            [1] = AttributeList.Parse([.. ListEntry(0x30, Ref(2, 1)), .. ListEntry(0x30, Ref(3, 1))], out _),
            [2] = AttributeList.Parse(ListEntry(0x30, Ref(4, 1)), out _), [3] = AttributeList.Parse(ListEntry(0x30, Ref(4, 1)), out _),
        };
        AttributeList.Walk(1, n => dia.GetValueOrDefault(n), out var c4, out _); Assert.False(c4);
        // entry length 0 / unaligned / too long
        foreach (int len in new[] { 0, 8, 25, 27, 33, 4000 })
        {
            var e = AttributeList.Parse([.. ListEntry(0x30, Ref(11, 1)), .. ListEntry(0x80, Ref(12, 1), len)], out var m);
            Assert.True(m); Assert.Single(e);
        }
        // a huge chain is bounded
        var chain = new Dictionary<ulong, IReadOnlyList<AttributeListEntry>>();
        for (ulong i = 0; i < 1000; i++) chain[i] = AttributeList.Parse(ListEntry(0x30, Ref(i + 1, 1)), out _);
        var walked = AttributeList.Walk(0, n => chain.GetValueOrDefault(n), out _, out var trunc);
        Assert.True(trunc); Assert.True(walked.Count <= AttributeList.MaxRecordsWalked + 1);
        // many entries are capped
        var big = new List<byte>(); for (int i = 0; i < 6000; i++) big.AddRange(ListEntry(0x30, Ref(11, 1)));
        var capped = AttributeList.Parse(big.ToArray(), out var m2); Assert.True(m2); Assert.Equal(AttributeList.MaxEntries, capped.Count);
    }

    // ---- P9 ----
    [Fact]
    public void P9_impossible_filetimes_are_unknown_and_never_throw()
    {
        foreach (long ft in new[] { 0L, -1L, long.MinValue, long.MaxValue, Dfir.Model.Timestamp.MaxFileTime, Dfir.Model.Timestamp.MaxFileTime + 1, 1L, 9L * 10_000_000 * 3600 * 24 * 365 * 1000 })
        {
            Assert.False(NtfsTime.ToTimestamp(ft, "t").IsKnown);
            Assert.StartsWith("raw:", NtfsTime.Text(ft));
        }
        Assert.True(NtfsTime.ToTimestamp(T0, "t").IsKnown);
        Assert.Equal("2026-11-13T10:00:00.0000000Z", NtfsTime.Text(T0));
    }

    // ---- truncation at every offset of a real record ----
    private static void ExerciseRecord(byte[] raw)
    {
        var r = NtfsFileRecord.Parse(raw, RecSize);
        foreach (var a in r.Attributes)
        {
            var v = r.ValueOf(a);
            StandardInformation.TryParse(v); FileNameAttribute.TryParse(v); AttributeList.Parse(v, out _);
            if (a.NonResident) DataRuns.Decode(r.MappingPairsOf(a), a.LowestVcn, a.HighestVcn, 16384);
        }
    }

    [Theory]
    [InlineData(0x29)]   // big.bin: non-resident $DATA with data runs
    [InlineData(0x2B)]   // long name + DOS alias
    [InlineData(5)]      // root directory with $INDEX_ROOT
    public void Truncation_at_every_offset_of_a_real_record_never_throws_or_hangs(int recordNumber)
    {
        var mft = File.ReadAllBytes(CorpusPath("mft.bin"));
        var original = mft.AsSpan(recordNumber * RecSize, RecSize).ToArray();
        var parsed = NtfsFileRecord.Parse(original, RecSize);
        Assert.Equal(NtfsRecordState.Ok, parsed.State);
        var restored = parsed.Bytes.ToArray();
        WithTimeout(() =>
        {
            for (int k = 0; k <= RecSize; k++)
            {
                NtfsFileRecord.Parse(original.AsSpan(0, k), RecSize);                              // file cut at k
                var cut = (byte[])restored.Clone(); Array.Clear(cut, k, RecSize - k);              // record zeroed from k, fixup re-applied
                ExerciseRecord(Fixup(cut, 0x0055));
                var junk = (byte[])restored.Clone(); for (int i = k; i < RecSize; i++) junk[i] = 0xFF;   // tail of 0xFF
                ExerciseRecord(Fixup(junk, 0x0055));
            }
        });
    }

    [Fact]
    public void Every_real_record_survives_a_single_flipped_byte_at_every_position_of_the_header_area()
    {
        var mft = File.ReadAllBytes(CorpusPath("mft.bin"));
        WithTimeout(() =>
        {
            var parsed = NtfsFileRecord.Parse(mft.AsSpan(0x29 * RecSize, RecSize), RecSize);
            var restored = parsed.Bytes.ToArray();
            for (int pos = 0; pos < 400; pos++)
                foreach (byte x in new byte[] { 0x00, 0xFF, 0x80, 0x01 })
                {
                    var c = (byte[])restored.Clone(); c[pos] = x;
                    ExerciseRecord(Fixup(c, 0x0042));
                }
        });
    }
}
