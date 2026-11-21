using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Corrupt and hostile registry hives. A subkey index list that points to itself used to recurse until the process died with a
/// stack overflow, which no try/catch can stop. These hives are built by hand (about 0x2000 bytes) and must end in an
/// <see cref="InvalidDataException"/> or a FAILED parse result, never in a crash or a hang. Nothing here depends on Windows.
/// </summary>
public sealed class RawRegistryMalformedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_rawreg_" + Guid.NewGuid().ToString("N"));
    public RawRegistryMalformedTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private const int HbinBase = 0x1000, RootCell = 0x20, ListCell = 0x100, ChildCell = 0x400;

    /// <summary>A regf file of 0x2000 bytes: header, one hive bin, a root key with <paramref name="subkeys"/> children listed at <see cref="ListCell"/>.</summary>
    private static byte[] Hive(int subkeys, Action<Func<int, byte[], bool>> cells)
    {
        var f = new byte[0x2000];
        Encoding.ASCII.GetBytes("regf").CopyTo(f, 0);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(0x24), RootCell);
        Encoding.ASCII.GetBytes("hbin").CopyTo(f, HbinBase);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(HbinBase + 8), 0x1000);

        bool Put(int offset, byte[] payload)
        {
            int size = (payload.Length + 4 + 7) & ~7;
            BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(HbinBase + offset), -size);   // negative = allocated
            payload.CopyTo(f, HbinBase + offset + 4);
            return true;
        }
        Put(RootCell, Nk("ROOT", subkeys, ListCell));
        cells(Put);
        return f;
    }

    private static byte[] Nk(string name, int subkeyCount, int listOffset)
    {
        var nk = new byte[0x4C + name.Length + 4];
        nk[0] = (byte)'n'; nk[1] = (byte)'k';
        BinaryPrimitives.WriteUInt16LittleEndian(nk.AsSpan(2), 0x20);                       // ASCII (compressed) key name
        BinaryPrimitives.WriteInt64LittleEndian(nk.AsSpan(4), new DateTime(2026, 9, 19, 14, 55, 1, DateTimeKind.Utc).ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x14), subkeyCount);
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x1C), listOffset);
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x24), 0);                        // no values
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x28), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(nk.AsSpan(0x48), (ushort)name.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(nk, 0x4C);
        return nk;
    }

    /// <summary>A subkey index list: "ri"/"li" hold 4-byte entries, "lf"/"lh" 8-byte entries (offset + hash).</summary>
    private static byte[] List(string sig, params int[] offsets)
    {
        int stride = sig is "lf" or "lh" ? 8 : 4;
        var l = new byte[4 + offsets.Length * stride];
        Encoding.ASCII.GetBytes(sig).CopyTo(l, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(l.AsSpan(2), (ushort)offsets.Length);
        for (int i = 0; i < offsets.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(l.AsSpan(4 + i * stride), offsets[i]);
        return l;
    }

    private static RawRegistry Open(byte[] hive) => new(new MemoryStream(hive));

    /// <summary>Runs a read on a worker and fails the test, instead of hanging the run, if it does not finish.</summary>
    private static T Bounded<T>(Func<T> read)
    {
        // a dedicated thread, not the shared pool: on a busy CI runner a pool item can wait in the queue past the limit and be reported as a HANG
        // although the parser never ran (MalformedBinaryInputTests "truncated to 264 of 268" on the runner, 2026-10-16)
        var task = Task.Factory.StartNew(read, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try { Assert.True(task.Wait(TimeSpan.FromSeconds(20)), "citirea nu s-a terminat în 20 s (buclă infinită)"); }
        catch (AggregateException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); }
        return task.Result;
    }

    private static InvalidDataException Corrupt(Func<object?> read) => Assert.Throws<InvalidDataException>(() => Bounded(read));

    [Fact]
    public void A_well_formed_hive_with_an_ri_list_over_a_leaf_list_is_still_read()
    {
        var hive = Hive(1, put => { put(ListCell, List("ri", 0x140)); put(0x140, List("lf", ChildCell)); put(ChildCell, Nk("x", 0, -1)); });
        var reg = Open(hive);
        Assert.Equal(["x"], reg.OpenKey("")!.SubkeyNames);
        Assert.Equal("x", reg.OpenKey("x")!.Path);
        Assert.Equal(["x"], reg.SubKeys("").Select(k => k.Path.TrimStart('\\')).ToList());
        Assert.Null(reg.OpenKey("missing"));
    }

    [Fact]
    public void A_subkey_list_that_points_to_itself_is_a_parse_error_not_a_stack_overflow()
    {
        var hive = Hive(1, put => put(ListCell, List("ri", ListCell)));
        Assert.Equal(0x2000, hive.Length);
        var reg = Open(hive);

        Assert.Contains("circular", Corrupt(() => reg.OpenKey("x")).Message);        // FindInList
        Assert.Contains("circular", Corrupt(() => reg.OpenKey("")).Message);         // ReadKey, subkey names
        Assert.Contains("circular", Corrupt(() => reg.SubKeys("").ToList()).Message); // SubKeys
        Assert.Contains("circular", Corrupt(() => reg.ReadValue("x", "v")).Message);
    }

    [Fact]
    public void Two_ri_lists_pointing_at_each_other_are_a_parse_error()
    {
        var hive = Hive(1, put => { put(ListCell, List("ri", 0x140)); put(0x140, List("ri", ListCell)); });
        var reg = Open(hive);
        Assert.Contains("circular", Corrupt(() => reg.OpenKey("x")).Message);
        Assert.Contains("circular", Corrupt(() => reg.SubKeys("").ToList()).Message);
    }

    [Fact]
    public void A_list_that_lists_itself_among_valid_entries_is_a_parse_error()
    {
        var hive = Hive(2, put => { put(ListCell, List("ri", 0x140, ListCell)); put(0x140, List("lf", ChildCell)); put(ChildCell, Nk("x", 0, -1)); });
        Assert.Contains("circular", Corrupt(() => Open(hive).OpenKey("")).Message);
    }

    [Fact]
    public void Nesting_deeper_than_the_limit_is_a_parse_error_even_without_a_cycle()
    {
        // ri → ri → ri → ri → ri → ri → ri → lf: seven levels of distinct lists, no repeats.
        var hive = Hive(1, put =>
        {
            int[] at = [ListCell, 0x140, 0x180, 0x1C0, 0x200, 0x240, 0x280, 0x2C0];
            for (int i = 0; i < at.Length - 1; i++) put(at[i], List("ri", at[i + 1]));
            put(at[^1], List("lf", ChildCell));
            put(ChildCell, Nk("x", 0, -1));
        });
        Assert.Contains("prea adânc", Corrupt(() => Open(hive).OpenKey("x")).Message);
    }

    [Fact]
    public void A_list_declaring_more_entries_than_its_cell_holds_is_a_parse_error()
    {
        var hive = Hive(1, put => put(ListCell, List("lf", ChildCell)));
        BinaryPrimitives.WriteUInt16LittleEndian(hive.AsSpan(HbinBase + ListCell + 4 + 2), 60000);    // count = 60000 in a 16-byte cell
        Assert.Contains("declară", Corrupt(() => Open(hive).OpenKey("x")).Message);
    }

    [Fact]
    public void A_list_offset_outside_the_hive_and_an_unknown_list_type_are_parse_errors()
    {
        var outside = Hive(1, put => put(ListCell, List("li", 0x7FFFFF00)));
        Assert.Throws<InvalidDataException>(() => Bounded(() => Open(outside).OpenKey("x")));
        var unknown = Hive(1, put => put(ListCell, List("zz", ChildCell)));
        Assert.Contains("necunoscută", Corrupt(() => Open(unknown).OpenKey("x")).Message);
    }

    [Fact]
    public void A_value_list_longer_than_its_cell_is_a_parse_error()
    {
        var hive = Hive(0, put => { });
        BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(HbinBase + RootCell + 4 + 0x24), 5000);   // 5000 values
        BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(HbinBase + RootCell + 4 + 0x28), ListCell);
        Assert.Throws<InvalidDataException>(() => Bounded(() => Open(hive).OpenKey("")));
    }

    /// <summary>A root key with one value "v" stored as big data: declared size <paramref name="declared"/>, one "db" segment list entry.</summary>
    private static byte[] BigDataValueHive(uint declared, ushort segments)
    {
        const int VkCell = 0x140, DbCell = 0x200, SegList = 0x240, Seg = 0x280;
        var hive = Hive(0, put =>
        {
            put(ListCell, [.. BitConverter.GetBytes(VkCell)]);
            var vk = new byte[0x14 + 1 + 3];
            vk[0] = (byte)'v'; vk[1] = (byte)'k';
            BinaryPrimitives.WriteUInt16LittleEndian(vk.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(vk.AsSpan(4), declared);          // no inline bit
            BinaryPrimitives.WriteInt32LittleEndian(vk.AsSpan(8), DbCell);
            BinaryPrimitives.WriteInt32LittleEndian(vk.AsSpan(0x0C), 3);               // REG_BINARY
            BinaryPrimitives.WriteUInt16LittleEndian(vk.AsSpan(0x10), 1);              // ASCII name
            vk[0x14] = (byte)'v';
            put(VkCell, vk);
            var db = new byte[12];
            db[0] = (byte)'d'; db[1] = (byte)'b';
            BinaryPrimitives.WriteUInt16LittleEndian(db.AsSpan(2), segments);
            BinaryPrimitives.WriteInt32LittleEndian(db.AsSpan(4), SegList);
            put(DbCell, db);
            put(SegList, [.. BitConverter.GetBytes(Seg), 0, 0, 0, 0]);
            put(Seg, new byte[32]);
        });
        BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(HbinBase + RootCell + 4 + 0x24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(hive.AsSpan(HbinBase + RootCell + 4 + 0x28), ListCell);
        return hive;
    }

    [Fact]
    public void A_big_data_value_declaring_more_bytes_than_the_hive_or_its_segments_can_hold_is_a_parse_error_before_any_allocation()
    {
        // 0x7FFFFFF0 bytes (about 2 GB) from a hive of 8 KB: must be rejected, not allocated (OutOfMemoryException would escape every catch for InvalidDataException)
        var huge = Corrupt(() => Open(BigDataValueHive(0x7FFFFFF0, 1)).OpenKey(""));
        Assert.Contains("big-data", huge.Message);
        // smaller than the file but larger than 2 segments can hold (2 x 16344 = 32688 < 40000 > file size 8192 as well; also check a size within the file)
        var tooManyForSegments = Corrupt(() => Open(BigDataValueHive(20000, 1)).OpenKey(""));
        Assert.Contains("big-data", tooManyForSegments.Message);
    }

    [Fact]
    public void Zero_length_truncated_and_random_hives_never_crash()
    {
        var good = Hive(1, put => { put(ListCell, List("ri", 0x140)); put(0x140, List("lf", ChildCell)); put(ChildCell, Nk("x", 0, -1)); });
        var random = new byte[0x2000];
        new Random(212).NextBytes(random);
        Encoding.ASCII.GetBytes("regf").CopyTo(random, 0);
        var cases = new List<byte[]> { Array.Empty<byte>(), new byte[10], new byte[0x30], good[..0x1000], good[..0x1100], good[..0x1030], random };
        foreach (var hive in cases)
        {
            try { Bounded(() => { var reg = Open(hive); reg.OpenKey(""); reg.OpenKey("x"); return reg.SubKeys("").ToList().Count; }); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }   // a controlled error is the expected outcome
        }
    }

    [Fact]
    public void A_parser_over_a_self_referencing_hive_returns_FAILED_with_the_error()
    {
        var path = Path.Combine(_dir, "SYSTEM");
        File.WriteAllBytes(path, Hive(1, put => put(ListCell, List("ri", ListCell))));
        var item = new EvidenceItem { EvidenceId = "EV-R", CaseId = "C", Source = "system", SourceType = "registry_system", StoredPath = path };
        var sink = new ListSink();
        var r = Bounded(() => new ServicesParser().Parse(item, path, sink, default));
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains("InvalidDataException", r.Error);
        Assert.Contains("circular", r.Error);
        Assert.Empty(sink.Events);
    }
}
