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
    /// <summary>
    /// Deepest nesting of subkey index lists accepted (ri → ri → … → lf/lh/li). Windows writes one ri level over leaf lists;
    /// a few more are tolerated. Anything deeper, or a list reached twice, is a corrupt (or hostile) hive: an error, never recursion.
    /// </summary>
    private const int MaxListDepth = 4;
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

    /// <summary>
    /// Runs a read of the hive and turns the exceptions that corrupt structures provoke (offsets, lengths and counts that point
    /// outside their cell, bad FILETIMEs) into one <see cref="InvalidDataException"/>: a damaged hive is a parse error, not a crash.
    /// </summary>
    private static T Guard<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or EndOfStreamException or FormatException)
        {
            throw new InvalidDataException($"Hive corupt: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    /// <summary>A key with its subkey names, values (raw bytes and type, names decoded as stored) and LastWriteTime; null if absent.</summary>
    public RawKey? OpenKey(string keyPath) => Guard(() => Locate(keyPath) is int nk ? ReadKey(nk, keyPath) : null);

    /// <summary>Every direct subkey of <paramref name="keyPath"/>, read by offset (linear, for keys with thousands of children).</summary>
    public IEnumerable<RawKey> SubKeys(string keyPath)
    {
        var offsets = Guard(() =>
        {
            if (Locate(keyPath) is not int nk) return new List<int>();
            var key = Cell(nk);
            Expect(key, "nk");
            if (BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x14)) == 0) return new List<int>();
            return ListOffsets(BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(0x1C))).ToList();
        });
        foreach (var off in offsets)
            yield return Guard(() => ReadKey(off, $@"{keyPath}\{KeyName(off)}"));
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
            if ((long)count * 4 > list.Length) throw new InvalidDataException($"Lista de valori ({count} intrări) depășește celula ({list.Length} octeți).");
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

    private IEnumerable<int> ListOffsets(int listOffset) => WalkList(listOffset, [], 0);

    /// <summary>
    /// Offsets of the key cells under a subkey index list, in file order. An <c>ri</c> list holds further lists; a list reached
    /// twice in one walk (a cycle, including a list that points to itself) or nested deeper than <see cref="MaxListDepth"/> is
    /// <see cref="InvalidDataException"/>, so a crafted hive cannot recurse without end or make the walk loop.
    /// </summary>
    private IEnumerable<int> WalkList(int listOffset, HashSet<int> visited, int depth)
    {
        if (depth > MaxListDepth) throw new InvalidDataException($"Liste de subchei imbricate prea adânc (peste {MaxListDepth} niveluri) la 0x{listOffset:X}.");
        if (!visited.Add(listOffset)) throw new InvalidDataException($"Listă de subchei circulară: celula 0x{listOffset:X} este întâlnită a doua oară.");
        var l = Cell(listOffset);
        if (l.Length < 4) throw new InvalidDataException($"Listă de subchei prea scurtă la 0x{listOffset:X}.");
        var sig = Encoding.ASCII.GetString(l, 0, 2);
        if (sig is not ("lf" or "lh" or "li" or "ri")) throw new InvalidDataException($"Listă de subchei necunoscută: {sig}");
        int n = BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(2));
        int stride = sig is "lf" or "lh" ? 8 : 4;
        if (4 + (long)n * stride > l.Length) throw new InvalidDataException($"Lista „{sig}” declară {n} intrări, dar celula 0x{listOffset:X} are doar {l.Length} octeți.");
        for (int i = 0; i < n; i++)
        {
            int off = BinaryPrimitives.ReadInt32LittleEndian(l.AsSpan(4 + i * stride));
            if (sig == "ri") foreach (var o in WalkList(off, visited, depth + 1)) yield return o;
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
            if ((long)segments * 4 > seglist.Length) throw new InvalidDataException($"Lista de segmente big-data ({segments}) depășește celula ({seglist.Length} octeți).");
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
        foreach (var off in ListOffsets(listOffset))
            if (KeyName(off).Equals(name, StringComparison.OrdinalIgnoreCase)) return off;
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
        long size = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(h)) - 4;
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
