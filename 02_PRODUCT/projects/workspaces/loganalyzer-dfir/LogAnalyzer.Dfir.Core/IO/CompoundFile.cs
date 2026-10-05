using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Read-only reader for OLE Compound Files ([MS-CFB] v3/v4): Jump Lists, Office 97–2003 documents. Streams are followed
/// through the FAT, or through the MiniFAT inside the root's mini stream when smaller than the header's cutoff.
/// Chain loops and out-of-range sectors are errors, never silently truncated data.
/// </summary>
public sealed class CompoundFile
{
    private const uint EndOfChain = 0xFFFFFFFE, FreeSect = 0xFFFFFFFF;
    private readonly byte[] _d;
    private readonly int _sectorSize, _miniSectorSize;
    private readonly uint _miniCutoff;
    private readonly List<uint> _fat = [];
    private readonly List<uint> _miniFat = [];
    private readonly byte[] _miniStream;

    public sealed record Entry(string Name, int Type, long Size, uint StartSector, long Created, long Modified);
    public IReadOnlyList<Entry> Entries { get; }

    public CompoundFile(byte[] data)
    {
        _d = data;
        if (data.Length < 512 || BinaryPrimitives.ReadUInt64LittleEndian(data) != 0xE11AB1A1E011CFD0)
            throw new InvalidDataException("Nu este un fișier OLE Compound File (semnătura D0CF11E0 lipsește).");
        _sectorSize = 1 << BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x1E));
        _miniSectorSize = 1 << BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x20));
        if (_sectorSize is not (512 or 4096)) throw new InvalidDataException($"Dimensiune de sector neacceptată: {_sectorSize}");
        uint fatSectors = U(0x2C), firstDir = U(0x30), firstMiniFat = U(0x3C), miniFatSectors = U(0x40), firstDifat = U(0x44), difatSectors = U(0x48);
        _miniCutoff = U(0x38);

        var difat = new List<uint>();
        for (int i = 0; i < 109 && difat.Count < fatSectors; i++) difat.Add(U(0x4C + i * 4));
        uint next = firstDifat;
        for (uint n = 0; n < difatSectors && next is not (EndOfChain or FreeSect); n++)
        {
            var s = Sector(next);
            for (int i = 0; i < _sectorSize / 4 - 1 && difat.Count < fatSectors; i++) difat.Add(BinaryPrimitives.ReadUInt32LittleEndian(s[(i * 4)..]));
            next = BinaryPrimitives.ReadUInt32LittleEndian(s[(_sectorSize - 4)..]);
        }
        foreach (var fs in difat)
        {
            var s = Sector(fs);
            for (int i = 0; i < _sectorSize / 4; i++) _fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(s[(i * 4)..]));
        }

        var dir = ReadChain(firstDir, _fat, _sectorSize, null, long.MaxValue);
        var entries = new List<Entry>();
        for (int off = 0; off + 128 <= dir.Length; off += 128)
        {
            int type = dir[off + 66];
            if (type == 0) continue;
            int nameLen = Math.Clamp((int)BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(off + 64)), 2, 64);
            var name = Encoding.Unicode.GetString(dir, off, nameLen - 2);
            long size = (long)BinaryPrimitives.ReadUInt64LittleEndian(dir.AsSpan(off + 120));
            if (_sectorSize == 512) size &= 0xFFFFFFFF;                     // v3: high dword may be garbage
            entries.Add(new Entry(name, type, size, BinaryPrimitives.ReadUInt32LittleEndian(dir.AsSpan(off + 116)),
                BinaryPrimitives.ReadInt64LittleEndian(dir.AsSpan(off + 100)), BinaryPrimitives.ReadInt64LittleEndian(dir.AsSpan(off + 108))));
        }
        Entries = entries;

        var root = entries.FirstOrDefault(e => e.Type == 5) ?? throw new InvalidDataException("Intrarea Root lipsește.");
        if (miniFatSectors > 0 && firstMiniFat is not (EndOfChain or FreeSect))
        {
            var mf = ReadChain(firstMiniFat, _fat, _sectorSize, null, long.MaxValue);
            for (int i = 0; i + 4 <= mf.Length; i += 4) _miniFat.Add(BinaryPrimitives.ReadUInt32LittleEndian(mf.AsSpan(i)));
        }
        _miniStream = root.Size > 0 ? ReadChain(root.StartSector, _fat, _sectorSize, null, root.Size) : [];
    }

    /// <summary>Content of a stream entry, exactly its declared size.</summary>
    public byte[] Read(Entry e)
    {
        if (e.Type != 2) throw new ArgumentException("Intrarea nu este un stream.", nameof(e));
        if (e.Size == 0) return [];
        return e.Size < _miniCutoff
            ? ReadChain(e.StartSector, _miniFat, _miniSectorSize, _miniStream, e.Size)
            : ReadChain(e.StartSector, _fat, _sectorSize, null, e.Size);
    }

    private byte[] ReadChain(uint start, List<uint> table, int size, byte[]? container, long length)
    {
        var ms = new MemoryStream();
        var seen = new HashSet<uint>();
        for (uint s = start; s is not (EndOfChain or FreeSect) && ms.Length < length; s = table[(int)s])
        {
            if (!seen.Add(s)) throw new InvalidDataException($"Lanț de sectoare circular la {s}.");
            if (s >= table.Count) throw new InvalidDataException($"Sector {s} în afara tabelei ({table.Count}).");
            if (container is null) ms.Write(Sector(s));
            else
            {
                long off = (long)s * size;
                if (off + size > container.Length) throw new InvalidDataException($"Mini-sector {s} în afara mini-streamului.");
                ms.Write(container, (int)off, size);
            }
        }
        if (length != long.MaxValue && ms.Length < length) throw new InvalidDataException($"Stream trunchiat: {ms.Length} din {length} octeți.");
        var all = ms.ToArray();
        return length == long.MaxValue ? all : all[..(int)length];
    }

    /// <summary>
    /// One sector. Windows writes Jump Lists whose last sector is cut at the end of the data (file length not a multiple of
    /// the sector size); that sector is returned zero-padded. Stream lengths are still checked by the caller.
    /// </summary>
    private ReadOnlySpan<byte> Sector(uint n)
    {
        long off = ((long)n + 1) * _sectorSize;
        if (off >= _d.Length) throw new InvalidDataException($"Sector {n} în afara fișierului.");
        if (off + _sectorSize <= _d.Length) return _d.AsSpan((int)off, _sectorSize);
        var padded = new byte[_sectorSize];
        _d.AsSpan((int)off).CopyTo(padded);
        return padded;
    }

    private uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_d.AsSpan(at));
}
