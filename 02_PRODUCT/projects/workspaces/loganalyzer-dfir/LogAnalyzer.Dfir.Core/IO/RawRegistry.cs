using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Minimal read-only reader for one registry value in an offline hive (regf). Exists because DiscUtils.Registry 0.16
/// returns only the 12-byte "db" header for values stored as big data (> 16344 bytes, hive format 1.4+), silently
/// truncating them — AppCompatCache is such a value. Follows nk → subkey lists (lf/lh/li/ri) → vk → data or db segments.
/// Transaction logs (.LOG1/.LOG2) are not applied.
/// </summary>
public sealed class RawRegistry
{
    private const int HbinBase = 0x1000, BigDataSegment = 16344;
    private readonly Stream _s;
    private readonly int _rootCell;

    public RawRegistry(Stream hive)
    {
        _s = hive;
        var b = new byte[0x30];
        _s.Position = 0;
        _s.ReadExactly(b);
        if (Encoding.ASCII.GetString(b, 0, 4) != "regf") throw new InvalidDataException("Nu este un hive de registru (lipsește semnătura regf).");
        _rootCell = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x24));
    }

    /// <summary>Value bytes, or null when the key or value does not exist. <paramref name="keyPath"/> is relative to the hive root.</summary>
    public byte[]? ReadValue(string keyPath, string valueName)
    {
        int nk = _rootCell;
        foreach (var part in keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (FindSubkey(nk, part) is int child) nk = child; else return null;

        var key = Cell(nk);
        Expect(key, "nk");
        int count = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x24));
        if (count <= 0) return null;
        var list = Cell(BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x28)));
        for (int i = 0; i < count; i++)
        {
            var vk = Cell(BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(i * 4)));
            Expect(vk, "vk");
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(vk.AsSpan(2));
            bool ascii = (BinaryPrimitives.ReadUInt16LittleEndian(vk.AsSpan(0x10)) & 1) != 0;
            var name = ascii ? Encoding.Latin1.GetString(vk, 0x14, nameLen) : Encoding.Unicode.GetString(vk, 0x14, nameLen);
            if (!name.Equals(valueName, StringComparison.OrdinalIgnoreCase)) continue;
            return Data(vk);
        }
        return null;
    }

    private byte[] Data(byte[] vk)
    {
        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(vk.AsSpan(4));
        if ((raw & 0x80000000) != 0) return vk.AsSpan(8, (int)(raw & 0x7FFFFFFF)).ToArray();   // stored inline in the offset field
        int size = (int)raw;
        var cell = Cell(BinaryPrimitives.ReadInt32LittleEndian(vk.AsSpan(8)));
        if (size > BigDataSegment && cell.Length >= 8 && cell[0] == (byte)'d' && cell[1] == (byte)'b')
        {
            int segments = BinaryPrimitives.ReadUInt16LittleEndian(cell.AsSpan(2));
            var seglist = Cell(BinaryPrimitives.ReadInt32LittleEndian(cell.AsSpan(4)));
            var result = new byte[size];
            int done = 0;
            for (int i = 0; i < segments && done < size; i++)
            {
                var seg = Cell(BinaryPrimitives.ReadInt32LittleEndian(seglist.AsSpan(i * 4)));
                int n = Math.Min(Math.Min(seg.Length, BigDataSegment), size - done);
                Buffer.BlockCopy(seg, 0, result, done, n);
                done += n;
            }
            if (done != size) throw new InvalidDataException($"Valoare big-data incompletă: {done} din {size} octeți.");
            return result;
        }
        if (size > cell.Length) throw new InvalidDataException($"Datele valorii ({size} octeți) depășesc celula ({cell.Length}).");
        return cell.AsSpan(0, size).ToArray();
    }

    private int? FindSubkey(int nkOffset, string name)
    {
        var nk = Cell(nkOffset);
        Expect(nk, "nk");
        if (BinaryPrimitives.ReadInt32LittleEndian(nk.AsSpan(0x14)) == 0) return null;
        return FindInList(BinaryPrimitives.ReadInt32LittleEndian(nk.AsSpan(0x1C)), name);
    }

    private int? FindInList(int listOffset, string name)
    {
        var l = Cell(listOffset);
        var sig = Encoding.ASCII.GetString(l, 0, 2);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(2));
        int stride = sig is "lf" or "lh" ? 8 : 4;
        for (int i = 0; i < n; i++)
        {
            int off = BinaryPrimitives.ReadInt32LittleEndian(l.AsSpan(4 + i * stride));
            if (sig == "ri")
            {
                if (FindInList(off, name) is int hit) return hit;
                continue;
            }
            if (sig is not ("lf" or "lh" or "li")) throw new InvalidDataException($"Listă de subchei necunoscută: {sig}");
            if (KeyName(off).Equals(name, StringComparison.OrdinalIgnoreCase)) return off;
        }
        return null;
    }

    private string KeyName(int nkOffset)
    {
        var nk = Cell(nkOffset);
        Expect(nk, "nk");
        int len = BinaryPrimitives.ReadUInt16LittleEndian(nk.AsSpan(0x48));
        bool ascii = (BinaryPrimitives.ReadUInt16LittleEndian(nk.AsSpan(2)) & 0x20) != 0;
        return ascii ? Encoding.Latin1.GetString(nk, 0x4C, len) : Encoding.Unicode.GetString(nk, 0x4C, len);
    }

    /// <summary>Cell payload (without the 4-byte size) at a hive-bin-relative offset.</summary>
    private byte[] Cell(int offset)
    {
        if (offset < 0 || HbinBase + (long)offset + 4 > _s.Length) throw new InvalidDataException($"Offset de celulă în afara hive-ului: 0x{offset:X}");
        _s.Position = HbinBase + (long)offset;
        Span<byte> h = stackalloc byte[4];
        _s.ReadExactly(h);
        int size = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h)) - 4;
        if (size < 0 || _s.Position + size > _s.Length) throw new InvalidDataException($"Celulă coruptă la 0x{offset:X}");
        var b = new byte[size];
        _s.ReadExactly(b);
        return b;
    }

    private static void Expect(byte[] cell, string sig)
    {
        if (cell.Length < 2 || cell[0] != (byte)sig[0] || cell[1] != (byte)sig[1])
            throw new InvalidDataException($"Se aștepta o celulă „{sig}”.");
    }
}
