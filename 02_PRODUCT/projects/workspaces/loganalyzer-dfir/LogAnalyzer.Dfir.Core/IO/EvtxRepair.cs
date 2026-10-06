using System.Buffers.Binary;
using System.IO.Hashing;

namespace LogAnalyzer.Dfir.IO;

public sealed record EvtxChunkInfo(int Index, bool Valid, string Problem, long FirstRecordId, long LastRecordId);

public sealed record EvtxRepairResult(string OutputPath, int TotalChunks, int KeptChunks, IReadOnlyList<EvtxChunkInfo> Chunks)
{
    public IEnumerable<EvtxChunkInfo> Dropped => Chunks.Where(c => !c.Valid);
}

/// <summary>
/// Recovers a damaged .evtx: the Windows event log API refuses the whole file when one chunk is corrupt.
/// The file is a 4 KiB header followed by independent 64 KiB chunks, each with its own CRC32 checksums and its own
/// string/template tables, so valid chunks can be copied into a new file with a rebuilt header. Original
/// EventRecordIDs are kept, so the hole left by dropped chunks remains visible. The source file is never modified.
/// </summary>
public static class EvtxRepair
{
    private const int FileHeaderSize = 4096, ChunkSize = 65536;
    private static readonly byte[] FileMagic = "ElfFile\0"u8.ToArray();
    private static readonly byte[] ChunkMagic = "ElfChnk\0"u8.ToArray();

    public static IReadOnlyList<EvtxChunkInfo> Inspect(string path)
    {
        var data = File.ReadAllBytes(path);
        var list = new List<EvtxChunkInfo>();
        for (int i = 0, off = FileHeaderSize; off + ChunkSize <= data.Length; i++, off += ChunkSize)
            list.Add(CheckChunk(data.AsSpan(off, ChunkSize), i));
        return list;
    }

    public static EvtxRepairResult Repair(string sourcePath, string outputPath)
    {
        var data = File.ReadAllBytes(sourcePath);
        if (data.Length < FileHeaderSize || !data.AsSpan(0, 8).SequenceEqual(FileMagic))
            throw new InvalidDataException("Nu este un fișier EVTX (antet ElfFile absent).");

        var chunks = new List<EvtxChunkInfo>();
        var kept = new List<int>();
        for (int i = 0, off = FileHeaderSize; off + ChunkSize <= data.Length; i++, off += ChunkSize)
        {
            var info = CheckChunk(data.AsSpan(off, ChunkSize), i);
            chunks.Add(info);
            if (info.Valid) kept.Add(off);
        }
        if (kept.Count == 0) throw new InvalidDataException("Niciun chunk EVTX valid nu poate fi recuperat.");
        // The header stores the chunk count in 16 bits.
        if (kept.Count > ushort.MaxValue) kept = kept.Take(ushort.MaxValue).ToList();

        var header = data.AsSpan(0, FileHeaderSize).ToArray();
        long lastKeptChunk = kept.Count - 1;
        long nextRecordId = chunks.Where(c => c.Valid).Max(c => c.LastRecordId) + 1;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), 0);                       // first chunk number
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), lastKeptChunk);          // last chunk number
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24), nextRecordId);           // next record identifier
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(42), (ushort)kept.Count);    // number of chunks
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(120), 0);                    // flags: not dirty, not full
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(124), Crc32.HashToUInt32(header.AsSpan(0, 120)));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            fs.Write(header);
            foreach (var off in kept) fs.Write(data, off, ChunkSize);
        }
        return new EvtxRepairResult(outputPath, chunks.Count, kept.Count, chunks);
    }

    private static EvtxChunkInfo CheckChunk(ReadOnlySpan<byte> c, int index)
    {
        if (!c[..8].SequenceEqual(ChunkMagic))
            return new EvtxChunkInfo(index, false, c.IndexOfAnyExcept((byte)0) < 0 ? "chunk gol (nealocat)" : "semnătură ElfChnk absentă", 0, 0);
        long firstId = BinaryPrimitives.ReadInt64LittleEndian(c[24..]);
        long lastId = BinaryPrimitives.ReadInt64LittleEndian(c[32..]);
        uint freeOffset = BinaryPrimitives.ReadUInt32LittleEndian(c[48..]);
        uint recordsCrc = BinaryPrimitives.ReadUInt32LittleEndian(c[52..]);
        uint headerCrc = BinaryPrimitives.ReadUInt32LittleEndian(c[124..]);

        var crc = new Crc32();
        crc.Append(c[..120]);
        crc.Append(c[128..512]);
        if (crc.GetCurrentHashAsUInt32() != headerCrc) return new EvtxChunkInfo(index, false, "CRC antet chunk invalid", firstId, lastId);
        if (freeOffset < 512 || freeOffset > ChunkSize) return new EvtxChunkInfo(index, false, "offset spațiu liber invalid", firstId, lastId);
        if (Crc32.HashToUInt32(c[512..(int)freeOffset]) != recordsCrc) return new EvtxChunkInfo(index, false, "CRC înregistrări invalid (date modificate sau corupte)", firstId, lastId);
        return new EvtxChunkInfo(index, true, "", firstId, lastId);
    }
}
