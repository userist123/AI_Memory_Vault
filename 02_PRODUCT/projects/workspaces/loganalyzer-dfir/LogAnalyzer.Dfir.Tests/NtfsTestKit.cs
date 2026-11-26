using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Builds synthetic NTFS structures for the NTFS reader / MftParser tests. Output follows docs/dfir/NTFS_FORMAT_CARD.md.</summary>
internal static class NtfsTestKit
{
    public const int RecSize = 1024;
    private static byte[] B(int n) => new byte[n];
    private static void W16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)v);
    private static void W32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    private static void W64(byte[] b, int o, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o), v);

    public static byte[] Ft(DateTime utc) => BitConverter.GetBytes(utc.ToFileTimeUtc());

    /// <summary>$STANDARD_INFORMATION value (48 or 72 bytes) from four raw FILETIMEs.</summary>
    public static byte[] Si(long c, long m, long mm, long a, int size = 72)
    {
        var v = B(size);
        BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(0), c); BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(8), m);
        BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(16), mm); BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(24), a);
        return v;
    }

    /// <summary>$FILE_NAME value. <paramref name="nameBytes"/> overrides the encoded name; <paramref name="nameLenField"/> overrides the length byte.</summary>
    public static byte[] Fn(ulong parentRef, string name, long c, long m, long mm, long a, byte ns = 1, byte[]? nameBytes = null, int? nameLenField = null)
    {
        nameBytes ??= Encoding.Unicode.GetBytes(name);
        var v = B(66 + nameBytes.Length);
        W64(v, 0, parentRef);
        BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(8), c); BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(16), m);
        BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(24), mm); BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(32), a);
        v[64] = (byte)(nameLenField ?? nameBytes.Length / 2); v[65] = ns;
        nameBytes.CopyTo(v, 66);
        return v;
    }

    public static byte[] Resident(uint type, byte[] value, string name = "", int? lengthOverride = null)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        int nameOff = 24, valOff = (24 + nameBytes.Length + 7) & ~7;
        int len = (valOff + value.Length + 7) & ~7;
        var a = B(len);
        W32(a, 0, type); W32(a, 4, (uint)(lengthOverride ?? len)); a[8] = 0; a[9] = (byte)name.Length; W16(a, 10, nameOff);
        W32(a, 16, (uint)value.Length); W16(a, 20, valOff);
        nameBytes.CopyTo(a, nameOff); value.CopyTo(a, valOff);
        return a;
    }

    public static byte[] NonResident(uint type, long low, long high, byte[] mapping, long size, string name = "")
    {
        int mp = 64 + ((Encoding.Unicode.GetBytes(name).Length + 7) & ~7);
        int len = (mp + mapping.Length + 8) & ~7;
        var a = B(len);
        W32(a, 0, type); W32(a, 4, (uint)len); a[8] = 1; a[9] = (byte)name.Length; W16(a, 10, 64);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(16), low); BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(24), high);
        W16(a, 32, mp);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(40), size); BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(48), size); BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(56), size);
        Encoding.Unicode.GetBytes(name).CopyTo(a, 64);
        mapping.CopyTo(a, mp);
        return a;
    }

    public const ushort InUse = 1, Dir = 2;

    /// <summary>A FILE record with update sequence applied (sector tails replaced by the USN).</summary>
    public static byte[] Record(ushort seq, ushort flags, ulong baseRef, params byte[][] attrs)
    {
        var r = B(RecSize);
        "FILE"u8.CopyTo(r);
        W16(r, 4, 0x30); W16(r, 6, RecSize / 512 + 1); W16(r, 16, seq); W16(r, 18, 1); W16(r, 20, 56); W16(r, 22, flags);
        W64(r, 32, baseRef);
        int p = 56;
        foreach (var a in attrs) { a.CopyTo(r, p); p += a.Length; }
        W32(r, p, 0xFFFFFFFF);
        W32(r, 24, (uint)(p + 8)); W32(r, 28, RecSize);
        return Fixup(r);
    }

    /// <summary>Applies the update sequence to a record whose sector tails hold data (USN 0x1234).</summary>
    public static byte[] Fixup(byte[] r, ushort usn = 0x1234)
    {
        r = (byte[])r.Clone();
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(4));
        if (usa < 8 || usa + 2 * (r.Length / 512 + 1) > r.Length) return r;   // a header that cannot carry an update sequence is left as it is
        W16(r, usa, usn);
        for (int i = 1; i < r.Length / 512 + 1; i++)
        {
            r[usa + 2 * i] = r[i * 512 - 2]; r[usa + 2 * i + 1] = r[i * 512 - 1];
            W16(r, i * 512 - 2, usn);
        }
        return r;
    }

    public static ulong Ref(ulong number, ushort seq) => number | ((ulong)seq << 48);

    /// <summary>A live file record with one $SI and one $FN. Size via resident $DATA of <paramref name="size"/> bytes.</summary>
    public static byte[] FileRecord(ushort seq, ulong parent, string name, long si, long fn, int size = 3, ushort flags = InUse, byte[]? fnValue = null, byte[]? siValue = null)
        => Record(seq, flags, 0,
            Resident(0x10, siValue ?? Si(si, si, si, si)),
            Resident(0x30, fnValue ?? Fn(parent, name, fn, fn, fn, fn)),
            Resident(0x80, new byte[size]));

    public static byte[] DirRecord(ushort seq, ulong parent, string name, long t)
        => Record(seq, (ushort)(InUse | Dir), 0, Resident(0x10, Si(t, t, t, t)), Resident(0x30, Fn(parent, name, t, t, t, t)));

    /// <summary>Records 0..4 blank, 5 = root, then the given records from number 6 upward (gaps filled with zeroed records).</summary>
    public static byte[] Mft(params (int Number, byte[] Record)[] records)
    {
        int max = Math.Max(5, records.Length == 0 ? 5 : records.Max(x => x.Number));
        var file = new byte[(max + 1) * RecSize];
        long t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var root = DirRecord(5, Ref(5, 5), ".", t);
        root.CopyTo(file, 5 * RecSize);
        foreach (var (n, rec) in records) rec.CopyTo(file, n * RecSize);
        // record 0 must start the file with a FILE header, as in a real $MFT
        if (!records.Any(x => x.Number == 0))
            Record(1, InUse, 0, Resident(0x10, Si(t, t, t, t)), Resident(0x30, Fn(Ref(5, 5), "$MFT", t, t, t, t))).CopyTo(file, 0);
        return file;
    }

    public static byte[] BootSector(byte clustersPerRecord = 0xF6, ushort bps = 512, byte spc = 8)
    {
        var b = B(512);
        "NTFS    "u8.CopyTo(b.AsSpan(3));
        W16(b, 0x0B, bps); b[0x0D] = spc; W64(b, 0x28, 131072); W64(b, 0x30, 0x154A); W64(b, 0x38, 2); b[0x40] = clustersPerRecord; b[0x44] = 1; W64(b, 0x48, 0x1122334455667788);
        b[510] = 0x55; b[511] = 0xAA;
        return b;
    }
}
