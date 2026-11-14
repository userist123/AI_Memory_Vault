using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Builds synthetic $INDEX_ALLOCATION (INDX) blocks for the I30Parser tests. Layout follows docs/dfir/NTFS_FORMAT_CARD.md section 8.</summary>
internal static class I30TestKit
{
    public const int HeaderEntries = 64;   // absolute offset of the first entry: node header at 24, first entry 40 after it

    /// <summary>One $I30 entry: file reference, lengths, flags, $FILE_NAME key. Lengths can be overridden to build hostile entries.</summary>
    public static byte[] Entry(ulong reference, string name, long time, ulong parent = 0x0005_0000_0000_0005UL, ushort flags = 0, byte ns = 1,
                               int? entryLen = null, int? keyLen = null, int? nameLenField = null, byte[]? nameBytes = null, long? time2 = null, long size = 8)
    {
        var key = NtfsTestKit.Fn(parent, name, time, time2 ?? time, time2 ?? time, time2 ?? time, ns, nameBytes, nameLenField);
        BinaryPrimitives.WriteInt64LittleEndian(key.AsSpan(40), size + 4096);
        BinaryPrimitives.WriteInt64LittleEndian(key.AsSpan(48), size);
        int kl = keyLen ?? key.Length;
        int el = entryLen ?? (((16 + key.Length + 7) & ~7) + ((flags & 1) != 0 ? 8 : 0));   // a child entry carries the sub-node VCN after the key
        var e = new byte[((16 + key.Length + 7) & ~7) + ((flags & 1) != 0 ? 8 : 0)];   // the bytes actually present; a hostile length field does not make the entry longer
        BinaryPrimitives.WriteUInt64LittleEndian(e, reference);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(8), (ushort)el);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(10), (ushort)kl);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(12), flags);
        key.CopyTo(e, 16);
        return e;
    }

    public static byte[] EndMarker()
    {
        var e = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(8), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(12), 2);
        return e;
    }

    /// <summary>
    /// An INDX block with the update sequence applied. <paramref name="entries"/> (and the end marker) are the allocated area; <paramref name="slack"/> goes right
    /// after "used" (the old entries a delete leaves behind). Header fields can be overridden to build hostile blocks.
    /// </summary>
    public static byte[] Block(long vcn, IEnumerable<byte[]> entries, byte[]? slack = null, int blockSize = 4096, bool endMarker = true,
                               int? usaOffset = null, int? usaCount = null, uint? firstEntry = null, uint? used = null, uint? allocated = null, ushort usn = 0x1234)
    {
        var b = new byte[blockSize];
        "INDX"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), (ushort)(usaOffset ?? 40));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), (ushort)(usaCount ?? (blockSize / 512 + 1)));
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(16), vcn);
        int start = Math.Max(HeaderEntries, (40 + 2 * (blockSize / 512 + 1) + 7) & ~7);   // entries start after the update sequence array
        int p = start;
        foreach (var e in entries) { e.CopyTo(b, p); p += e.Length; }
        if (endMarker) { EndMarker().CopyTo(b, p); p += 16; }
        int usedAbs = p;
        if (slack is not null) slack.CopyTo(b, p);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), firstEntry ?? (uint)(start - 24));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), used ?? (uint)(usedAbs - 24));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), allocated ?? (uint)(blockSize - 24));
        return NtfsTestKit.Fixup(b, usn);
    }

    /// <summary> value: root header (16 bytes) + node header + entries + end marker. <paramref name="trailing"/> is written after "used" (an old entry in the root).</summary>
    public static byte[] IndexRootValue(IEnumerable<byte[]> entries, byte[]? trailing = null)
    {
        var body = Concat([.. entries, EndMarker()]);
        var v = new byte[16 + 16 + body.Length + (trailing?.Length ?? 0)];
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(0), 0x30); BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(8), 4096); v[12] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(16), 16);                       // first entry, relative to the node header
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(20), (uint)(16 + body.Length));  // used
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(24), (uint)(16 + body.Length + (trailing?.Length ?? 0)));   // allocated
        body.CopyTo(v, 32);
        trailing?.CopyTo(v, 32 + body.Length);
        return v;
    }

    public static byte[] Concat(params byte[][] blocks) => blocks.SelectMany(x => x).ToArray();
}
