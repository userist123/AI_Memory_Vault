using System.Buffers.Binary;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Review of S3a (A1, A2, A3, A6): sparse $MFT, copy sizes bounded by the volume, boot sector totals, valid data length, hostile profile names.</summary>
public sealed partial class NtfsVolumeReaderTests
{
    /// <summary>An output that refuses to take more than <paramref name="cap"/> bytes (the old code wrote 512 MiB of zeros on a 256 KiB volume).</summary>
    internal sealed class CapStream(long cap) : Stream
    {
        private long _len;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _len;
        public override long Position { get => _len; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            _len += count;
            if (_len > cap) throw new InvalidOperationException($"copia a depășit {cap} octeți (volumul are 256 KiB)");
        }
    }

    // sparse run: header 0x03 = 3 length bytes, 0 offset bytes; 00 00 02 = 131,072 clusters = 512 MiB
    private static readonly byte[] HugeSparseTail = [0x03, 0x00, 0x00, 0x02, 0x00];
    private const long HugeClusters = 131_072;

    /// <summary>Volume of 64 clusters whose records 1..7 are the given directories (number, name, parent record, parent sequence, $I30 allocation or null). Root is record 5.</summary>
    internal static byte[] DirVolume(params (int Num, string Name, int Parent, ushort ParentSeq, byte[]? Index)[] dirs)
    {
        long t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var more = new byte[7][];
        for (int i = 0; i < more.Length; i++) more[i] = new byte[1024];
        more[4] = WithNumber(DirRecord(5, Ref(5, 5), ".", t), 5);
        foreach (var d in dirs)
        {
            var attrs = new List<byte[]> { Resident(0x10, Si(t, t, t, t)), Resident(0x30, Fn(Ref((ulong)d.Parent, d.ParentSeq), d.Name, t, t, t, t)) };
            if (d.Index is not null) attrs.Add(d.Index);
            more[d.Num - 1] = WithNumber(Record(1, (ushort)(InUse | Dir), 0, [.. attrs]), (uint)d.Num);
        }
        return Vol(Rec0(Data(0, 1, TwoClustersAt4, 8 * 1024)), more);
    }

    internal static byte[] IndexAllocForTests(long high, byte[] mapping, long size, long? vdl = null, ushort? flags = null) => IndexAlloc(high, mapping, size, vdl, flags);

    private static byte[] IndexAlloc(long high, byte[] mapping, long size, long? vdl = null, ushort? flags = null)
    {
        var a = NonResident(0xA0, 0, high, mapping, size, "$I30");
        if (vdl is long v) BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(56), v);
        if (flags is ushort f) BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(12), f);
        return a;
    }

    private static readonly byte[] OneClusterAt6 = [0x11, 0x01, 0x06, 0x00];

    [Fact]
    public void Synthetic_directory_volume_copies_the_index_allocation_exactly()
    {
        var vol = DirVolume((6, "d", 5, 5, IndexAlloc(0, OneClusterAt6, Cl)));
        using var o = new MemoryStream();
        var r = OpenBytes(vol).CopyDirectoryIndex(BS + "d", o);
        Assert.Equal(NtfsDirectoryStatus.Copied, r.Status);
        Assert.True(vol.AsSpan(6 * Cl, Cl).SequenceEqual(o.ToArray()));
    }

    // ---------------- A1 ----------------

    [Fact]
    public void A1_a_sparse_mft_with_a_huge_sparse_run_is_refused_before_anything_is_written()
    {
        var mapping = new byte[] { 0x11, 0x02, 0x04 }.Concat(HugeSparseTail).ToArray();
        var vol = Vol(Rec0(Data(0, 1 + HugeClusters, mapping, (2 + HugeClusters) * Cl, flags: 0x8000)));
        using var o = new CapStream(1 << 20);
        var ex = Assert.Throws<NtfsReadException>(() => OpenBytes(vol).CopyMft(o));
        Assert.Contains("sparse", ex.Message);
        Assert.Equal(0, o.Length);
    }

    [Fact]
    public void A1_a_sparse_flag_on_the_mft_is_refused_even_without_a_sparse_run()
    {
        var ex = CopyFails(Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl, flags: 0x8000))));
        Assert.Contains("sparse", ex.Message);
    }

    [Fact]
    public void A1_an_index_allocation_larger_than_the_volume_is_refused_before_anything_is_written()
    {
        var mapping = new byte[] { 0x11, 0x01, 0x06 }.Concat(HugeSparseTail).ToArray();
        var vol = DirVolume((6, "d", 5, 5, IndexAlloc(HugeClusters, mapping, (1 + HugeClusters) * Cl, flags: 0x8000)));
        using var o = new CapStream(1 << 20);
        var reader = OpenBytes(vol);
        Assert.Throws<NtfsReadException>(() => reader.CopyDirectoryIndex(BS + "d", o));
        Assert.Equal(0, o.Length);
    }

    // ---------------- A2 ----------------

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(1UL << 63)]
    [InlineData(1UL << 60)]
    [InlineData((1UL << 48) / 512 + 1)]
    public void A2_a_boot_sector_with_an_impossible_total_of_sectors_is_an_NtfsReadException_not_an_overflow(ulong totalSectors)
    {
        var vol = Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl)));
        BinaryPrimitives.WriteUInt64LittleEndian(vol.AsSpan(0x28), totalSectors);
        Assert.Throws<NtfsReadException>(() => OpenBytes(vol));
    }

    // ---------------- A3 ----------------

    [Fact]
    public void A3_valid_data_length_below_the_real_size_of_the_mft_is_an_explicit_error()
    {
        var ex = CopyFails(Vol(Rec0(Data(0, 1, TwoClustersAt4, 2 * Cl, vdl: Cl))));
        Assert.Contains("valid data length", ex.Message);
    }

    [Fact]
    public void A3_zeros_after_the_valid_data_length_of_an_index_are_allowed_and_the_result_carries_the_length()
    {
        var vol = DirVolume((6, "d", 5, 5, IndexAlloc(0, OneClusterAt6, Cl, vdl: 100)));
        using var o = new MemoryStream();
        var r = OpenBytes(vol).CopyDirectoryIndex(BS + "d", o);
        Assert.Equal(NtfsDirectoryStatus.Copied, r.Status);
        Assert.Equal(Cl, r.Copy!.Size);
        Assert.Equal(100, r.Copy.ValidDataLength);
        Assert.True(o.ToArray().AsSpan(0, 100).SequenceEqual(vol.AsSpan(6 * Cl, 100)));
        Assert.All(o.ToArray().Skip(100), b => Assert.Equal(0, b));
    }

    // ---------------- A6 (the reader lists raw names; the collector decides) ----------------

    [Fact]
    public void A6_the_reader_lists_a_raw_name_with_a_separator_verbatim()
    {
        var vol = DirVolume((6, "Users", 5, 5, null), (1, "ok", 6, 1, null), (7, "bad" + BS + "name", 6, 1, null));
        var names = OpenBytes(vol).ListChildDirectories(BS + "Users");
        Assert.Contains("ok", names); Assert.Contains("bad" + BS + "name", names);
    }
}
