using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

/// <summary>NTFS file reference: 48-bit record number + 16-bit sequence (docs/dfir/NTFS_FORMAT_CARD.md §2). Valid only when the sequence equals the target record's.</summary>
public readonly record struct MftReference(ulong Raw)
{
    public const ulong NumberMask = 0x0000_FFFF_FFFF_FFFF;
    public ulong Number => Raw & NumberMask;
    public ushort Sequence => (ushort)(Raw >> 48);
    public static MftReference From(ulong number, ushort sequence) => new((number & NumberMask) | ((ulong)sequence << 48));
    /// <summary>The 128-bit file ID form used by <c>fsutil file queryfileid</c>: 0x + 16 zero digits + sequence (4) + record number (12).</summary>
    public string FileId => string.Create(CultureInfo.InvariantCulture, $"0x{0UL:x16}{Sequence:x4}{Number:x12}");
    public override string ToString() => $"{Number}:{Sequence}";
}

/// <summary>Bounded little-endian reads. Callers check <see cref="Has"/> first; a bad offset is a failed check, never a thrown surprise.</summary>
internal static class Le
{
    public static bool Has(ReadOnlySpan<byte> s, long offset, long length) => offset >= 0 && length >= 0 && offset + length <= s.Length;
    public static ushort U16(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadUInt16LittleEndian(s[o..]);
    public static uint U32(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadUInt32LittleEndian(s[o..]);
    public static ulong U64(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadUInt64LittleEndian(s[o..]);
    public static long I64(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadInt64LittleEndian(s[o..]);
}

/// <summary>UTF-16LE names as NTFS stores them: any 16-bit sequence is legal, so unpaired surrogates occur. They become U+FFFD (the string stays encodable) and are flagged.</summary>
public static class NtfsText
{
    public static string DecodeUtf16(ReadOnlySpan<byte> bytes, out bool hadInvalid)
    {
        hadInvalid = false;
        int n = bytes.Length / 2;
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < n && char.IsLowSurrogate((char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[((i + 1) * 2)..])))
                { sb.Append(c); sb.Append((char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[((i + 1) * 2)..])); i++; }
                else { sb.Append('�'); hadInvalid = true; }
            }
            else if (char.IsLowSurrogate(c)) { sb.Append('�'); hadInvalid = true; }
            else sb.Append(c);
        }
        if ((bytes.Length & 1) != 0) { sb.Append('�'); hadInvalid = true; }
        return sb.ToString();
    }
}

/// <summary>FILETIME handling: zero, negative, beyond year 9999 or outside 1980–2100 is kept raw and not interpreted (design: emission policy 4).</summary>
public static class NtfsTime
{
    private static readonly long Min = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static readonly long Max = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    public static bool IsPlausible(long fileTime) => fileTime >= Min && fileTime < Max;

    public static Timestamp ToTimestamp(long fileTime, string method)
        => IsPlausible(fileTime) ? Timestamp.FromFileTime(fileTime, method) : Timestamp.Unknown(fileTime.ToString(CultureInfo.InvariantCulture));

    /// <summary>ISO text with 100 ns precision, or "raw:n" when the value is not a plausible time.</summary>
    public static string Text(long fileTime)
        => IsPlausible(fileTime) ? DateTime.FromFileTimeUtc(fileTime).ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)
                                 : "raw:" + fileTime.ToString(CultureInfo.InvariantCulture);
}
