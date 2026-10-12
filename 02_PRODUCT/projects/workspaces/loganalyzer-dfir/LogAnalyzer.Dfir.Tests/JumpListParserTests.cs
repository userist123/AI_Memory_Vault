using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Compound file reader and automatic Jump Lists: synthetic file with exact values, real files for internal consistency.</summary>
public sealed class JumpListParserTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_jl_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Used = new(2026, 9, 19, 15, 1, 2, DateTimeKind.Utc);

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-J", CaseId = "C", Source = "jl", SourceType = "jumplist_auto", StoredPath = path };

    /// <summary>Minimal [MS-CFB] v3 writer: 512-byte sectors, mini-stream cutoff 0 so every stream uses regular sectors.</summary>
    public static byte[] BuildCfb(params (string Name, byte[] Data)[] streams)
    {
        const uint End = 0xFFFFFFFE, Free = 0xFFFFFFFF, FatSect = 0xFFFFFFFD;
        int dirEntries = streams.Length + 1, dirSectors = (dirEntries * 128 + 511) / 512;
        var fat = new List<uint> { FatSect };
        for (int i = 0; i < dirSectors; i++) fat.Add(i == dirSectors - 1 ? End : (uint)(fat.Count + 1));
        var starts = new List<uint>();
        foreach (var (_, data) in streams)
        {
            int n = Math.Max(1, (data.Length + 511) / 512);
            starts.Add((uint)fat.Count);
            for (int i = 0; i < n; i++) fat.Add(i == n - 1 ? End : (uint)(fat.Count + 1));
        }
        var file = new byte[512 * (1 + fat.Count)];
        BinaryPrimitives.WriteUInt64LittleEndian(file, 0xE11AB1A1E011CFD0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x18), 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1A), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1C), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1E), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x20), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x2C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x30), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x38), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x3C), End);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x44), End);
        for (int i = 0; i < 109; i++) BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x4C + i * 4), i == 0 ? 0 : Free);
        for (int i = 0; i < 128; i++) BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(512 + i * 4), i < fat.Count ? fat[i] : Free);

        void Dir(int index, string name, int type, uint start, long size)
        {
            int off = 1024 + index * 128;
            var n = Encoding.Unicode.GetBytes(name + "\0");
            n.CopyTo(file, off);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(off + 64), (ushort)n.Length);
            file[off + 66] = (byte)type;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(off + 68), Free);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(off + 72), Free);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(off + 76), Free);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(off + 116), start);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(off + 120), (ulong)size);
        }
        Dir(0, "Root Entry", 5, End, 0);
        for (int i = 0; i < streams.Length; i++)
        {
            Dir(i + 1, streams[i].Name, 2, starts[i], streams[i].Data.Length);
            streams[i].Data.CopyTo(file, 512 * (1 + (int)starts[i]));
        }
        return file;
    }

    internal static byte[] DestList(params (int EntryNo, string Path, DateTime Used, int Count, bool Pinned)[] entries)
    {
        var ms = new MemoryStream();
        var header = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(header, 6);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), entries.Length);
        ms.Write(header);
        foreach (var (no, path, used, count, pinned) in entries)
        {
            var e = new byte[130];
            Encoding.ASCII.GetBytes("marius-pc").CopyTo(e, 72);
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(88), no);
            BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(100), used.ToFileTimeUtc());
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(108), pinned ? 0 : -1);
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(116), count);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(128), (ushort)path.Length);
            ms.Write(e);
            ms.Write(Encoding.Unicode.GetBytes(path));
            ms.Write(new byte[4]);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Compound_file_streams_are_read_exactly_and_damage_is_an_error()
    {
        var big = new byte[1500];
        new Random(5).NextBytes(big);
        var data = BuildCfb(("a", [1, 2, 3]), ("b", big));
        var cf = new CompoundFile(data);
        Assert.Equal(new byte[] { 1, 2, 3 }, cf.Read(cf.Entries.Single(e => e.Name == "a")));
        Assert.Equal(big, cf.Read(cf.Entries.Single(e => e.Name == "b")));

        var cut = data[..(data.Length - 700)];                          // last stream loses sectors
        var cfCut = new CompoundFile(cut);
        Assert.Throws<InvalidDataException>(() => cfCut.Read(cfCut.Entries.Single(e => e.Name == "b")));
        Assert.Throws<InvalidDataException>(() => new CompoundFile(new byte[600]));
    }

    [Fact]
    public void DestList_entries_are_joined_to_their_links()
    {
        var lnk = LnkParserTests.BuildLnk(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", "/x", @"C:\Users\u\Downloads", "marius-pc");
        var data = BuildCfb(("1", lnk), ("DestList", DestList((1, @"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", Used, 3, false))));
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "f01b4d95cf55d32a.automaticDestinations-ms");
        File.WriteAllBytes(p, data);
        Assert.Equal("cfb", EvidenceFingerprint.Detect(p));

        var sink = new ListSink();
        var r = new JumpListParser().Parse(Item(p), p, sink, default);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        var e = Assert.Single(sink.Events);
        Assert.Equal(new DateTimeOffset(Used), e.Time.Utc);
        Assert.Equal(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", e.Path);
        Assert.Equal("3", e.Fields["AccessCount"]);
        Assert.Equal("false", e.Fields["Pinned"]);
        Assert.Equal("marius-pc", e.Host);
        Assert.Equal("f01b4d95cf55d32a", e.Fields["AppId"]);
        Assert.Equal("/x", e.Fields["Arguments"]);
    }

    [Fact]
    public void Empty_jump_list_is_empty_not_failed()
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "e.automaticDestinations-ms");
        File.WriteAllBytes(p, BuildCfb(("DestList", DestList())));
        Assert.Equal(EvidenceStatus.Empty, new JumpListParser().Parse(Item(p), p, new ListSink(), default).Status);
    }

    /// <summary>
    /// All real jump lists: each opens; DestList entry count equals the link streams; every entry has its stream; where the
    /// DestList path is a file path and the link has a LinkInfo target, both name the same file.
    /// </summary>
    [CorpusFact("jumpLists")]
    public void Real_jump_lists_are_internally_consistent()
    {
        var folder = Path.Combine(Corpus.Root, Corpus.S("jumpLists", "folder"));
        int files = 0, entries = 0, compared = 0;
        var problems = new List<string>();
        foreach (var f in Directory.EnumerateFiles(folder, "*.automaticDestinations-ms", SearchOption.AllDirectories))
        {
            files++;
            var sink = new ListSink();
            var r = new JumpListParser().Parse(Item(f), f, sink, default);
            if (r.Status is not (EvidenceStatus.Success or EvidenceStatus.Empty) || r.MalformedRecords > 0) { problems.Add($"{Path.GetFileName(f)}: {r.Status} {r.MalformedRecords} {r.Error}"); continue; }
            var cf = new CompoundFile(File.ReadAllBytes(f));
            int linkStreams = cf.Entries.Count(e => e.Type == 2 && JumpListParser.IsEntryStream(e.Name));
            if (sink.Events.Count != linkStreams) problems.Add($"{Path.GetFileName(f)}: {sink.Events.Count} intrări DestList vs {linkStreams} streamuri");
            entries += sink.Events.Count;
            foreach (var e in sink.Events.Where(e => e.Fields["DestListPath"].Length > 2 && e.Fields["DestListPath"][1] == ':' && e.Fields["LnkTarget"].Length > 0))
            {
                compared++;
                if (!e.Fields["DestListPath"].Equals(e.Fields["LnkTarget"], StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{Path.GetFileName(f)} #{e.Fields["EntryNumber"]}: DestList „{e.Fields["DestListPath"]}” vs link „{e.Fields["LnkTarget"]}”");
            }
        }
        output.WriteLine($"{files} fișiere, {entries} intrări, {compared} căi comparate");
        Assert.True(files > 40 && entries > 300 && compared > 100, $"{files} fișiere, {entries} intrări, {compared} comparate");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
    }
}
