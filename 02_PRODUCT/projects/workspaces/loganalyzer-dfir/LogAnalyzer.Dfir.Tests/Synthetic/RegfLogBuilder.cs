using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Tests.Synthetic;

/// <summary>
/// Builds the inputs of <see cref="RegfLogReplay"/> from hives written by <see cref="RegfBuilder"/>: a dirty primary file (sequence numbers patched, checksum
/// recomputed) and new-format ("HvLE") transaction logs, following the public regf specification (M. Suhanov). Hashes use the production Marvin32, which is
/// itself pinned against hashes read from a real Windows log in the tests.
/// </summary>
public static class RegfLogBuilder
{
    /// <summary>Hive with the given primary / secondary sequence numbers (dirty when they differ).</summary>
    public static byte[] WithSequences(byte[] hive, uint primary, uint secondary)
    {
        var h = (byte[])hive.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), primary);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8), secondary);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x1FC), RegfLogReplay.Checksum(h));
        return h;
    }

    /// <summary>One log entry that writes every 4096-byte page of the hive bins data of <paramref name="newHive"/> (so the entry alone brings a hive to that state).</summary>
    public static byte[] Entry(uint sequence, byte[] newHive, uint flags = 0)
    {
        int bins = newHive.Length - 0x1000, pages = bins / 4096;
        int size = (40 + 8 * pages + bins + 511) & ~511;
        var e = new byte[size];
        Encoding.ASCII.GetBytes("HvLE").CopyTo(e, 0);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(4), size);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(12), sequence);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(16), bins);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(20), pages);
        for (int i = 0; i < pages; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(40 + 8 * i), 4096 * i);
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(44 + 8 * i), 4096);
        }
        Buffer.BlockCopy(newHive, 0x1000, e, 40 + 8 * pages, bins);
        Seal(e);
        return e;
    }

    /// <summary>Recomputes Hash-1 (over bytes 40..Size) then Hash-2 (over the first 32 bytes, Hash-1 included).</summary>
    public static void Seal(byte[] entry)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(24), RegfLogReplay.Marvin32(entry.AsSpan(40)));
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(32), RegfLogReplay.Marvin32(entry.AsSpan(0, 32)));
    }

    /// <summary>A new-format log: the 512-byte backup base block (File type 6, both sequence numbers = <paramref name="headerSequence"/>), the entries, a zero tail.</summary>
    public static byte[] Log(byte[] hive, uint headerSequence, params byte[][] entries)
    {
        var total = 512 + entries.Sum(e => e.Length) + 512;
        var l = new byte[total];
        Buffer.BlockCopy(hive, 0, l, 0, 512);
        BinaryPrimitives.WriteUInt32LittleEndian(l.AsSpan(4), headerSequence);
        BinaryPrimitives.WriteUInt32LittleEndian(l.AsSpan(8), headerSequence);
        BinaryPrimitives.WriteUInt32LittleEndian(l.AsSpan(0x1C), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(l.AsSpan(0x1FC), RegfLogReplay.Checksum(l));
        int at = 512;
        foreach (var e in entries) { e.CopyTo(l, at); at += e.Length; }
        return l;
    }
}
