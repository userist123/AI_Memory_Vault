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
    public byte[]? ReadValue(string keyPath, string valueName) =>
        OpenKey(keyPath)?.Values.FirstOrDefault(v => v.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase))?.Data;

    /// <summary>A key with its subkey names, values (raw bytes and type, names decoded as stored) and LastWriteTime; null if absent.</summary>
    public RawKey? OpenKey(string keyPath) => Locate(keyPath) is int nk ? ReadKey(nk, keyPath) : null;

    /// <summary>Every direct subkey of <paramref name="keyPath"/>, read by offset (linear, for keys with thousands of children).</summary>
    public IEnumerable<RawKey> SubKeys(string keyPath)
    {
        if (Locate(keyPath) is not int nk) yield break;
        var key = Cell(nk);
        Expect(key, "nk");
        if (BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x14)) == 0) yield break;
        foreach (var off in ListOffsets(BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x1C))).ToList())
            yield return ReadKey(off, $@"{keyPath}\{KeyName(off)}");
    }

    private int? Locate(string keyPath)
    {
        int nk = _rootCell;
        foreach (var part in keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (FindSubkey(nk, part) is int child) nk = child; else return null;
        return nk;
    }

    private RawKey ReadKey(int nk, string keyPath)
    {
        var key = Cell(nk);
        Expect(key, "nk");
        var lastWrite = DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(key.AsSpan(4)));
        var subkeys = new List<string>();
        if (BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x14)) > 0)
            foreach (var off in ListOffsets(BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x1C))))
                subkeys.Add(KeyName(off));
        var values = new List<RawValue>();
        int count = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x24));
        if (count > 0)
        {
            var list = Cell(BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x28)));
            for (int i = 0; i < count; i++)
            {
                var vk = Cell(BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(i * 4)));
                Expect(vk, "vk");
                int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(vk.AsSpan(2));
                bool ascii = (BinaryPrimitives.ReadUInt16LittleEndian(vk.AsSpan(0x10)) & 1) != 0;
                // A non-compressed name is UTF-16 code units, whatever text they form (some programs store ANSI bytes there).
                var name = ascii ? Encoding.Latin1.GetString(vk, 0x14, nameLen) : Utf16Units(vk.AsSpan(0x14, nameLen));
                values.Add(new RawValue(name, BinaryPrimitives.ReadInt32LittleEndian(vk.AsSpan(0x0C)), Data(vk)));
            }
        }
        return new RawKey(keyPath, lastWrite, subkeys, values);
    }

    /// <summary>UTF-16 code units taken as-is (no replacement of unpaired surrogates), as regedit shows them.</summary>
    private static string Utf16Units(ReadOnlySpan<byte> b)
    {
        var chars = new char[b.Length / 2];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(b[(i * 2)..]);
        return new string(chars);
    }

    private IEnumerable<int> ListOffsets(int listOffset)
    {
        var l = Cell(listOffset);
        var sig = Encoding.ASCII.GetString(l, 0, 2);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(2));
        int stride = sig is "lf" or "lh" ? 8 : 4;
        if (sig is not ("lf" or "lh" or "li" or "ri")) throw new InvalidDataException($"Listă de subchei necunoscută: {sig}");
        for (int i = 0; i < n; i++)
        {
            int off = BinaryPrimitives.ReadInt32LittleEndian(l.AsSpan(4 + i * stride));
            if (sig == "ri") foreach (var o in ListOffsets(off)) yield return o;
            else yield return off;
        }
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

public sealed record RawValue(string Name, int Type, byte[] Data)
{
    public const int RegSz = 1, RegExpandSz = 2, RegBinary = 3, RegDword = 4, RegMultiSz = 7, RegQword = 11;

    /// <summary>Text of REG_SZ / REG_EXPAND_SZ (not expanded) / REG_MULTI_SZ (joined by spaces); DWORD as decimal; otherwise "".</summary>
    public string AsText => Type switch
    {
        RegSz or RegExpandSz => System.Text.Encoding.Unicode.GetString(Data).TrimEnd('\0'),
        RegMultiSz => string.Join(" ", System.Text.Encoding.Unicode.GetString(Data).Split('\0', StringSplitOptions.RemoveEmptyEntries)),
        RegDword when Data.Length >= 4 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(Data).ToString(System.Globalization.CultureInfo.InvariantCulture),
        RegQword when Data.Length >= 8 => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(Data).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    };
}

public sealed record RawKey(string Path, DateTime LastWriteUtc, IReadOnlyList<string> SubkeyNames, IReadOnlyList<RawValue> Values)
{
    public RawValue? Value(string name) => Values.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
