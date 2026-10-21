using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.FileSystem;

public enum ShellItemKind { RootFolder, Volume, Folder, File, Network, ControlPanel, Uri, UsersFilesFolder, Variable, Unknown }

/// <summary>
/// A FAT/DOS date and time as stored in a shell item: local time of the machine that wrote it, no time zone, 2-second resolution.
/// It is kept as the naive local value plus the raw words; it is never presented as UTC.
/// </summary>
public sealed record DosDateTime(DateTime Local, ushort RawDate, ushort RawTime)
{
    public const string Method = "DOS date/time: local time without time zone, 2-second resolution (not converted)";

    /// <summary>Naive local rendering, e.g. 2026-09-01T10:00:02 (no zone suffix).</summary>
    public string LocalIso => Local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public string Raw => $"0x{RawDate:X4}:0x{RawTime:X4}";

    /// <summary>
    /// As a <see cref="Timestamp"/> for callers that need one: the local reading is carried in <see cref="Timestamp.Utc"/> only as a naive value,
    /// and <see cref="Timestamp.ConversionMethod"/> says it has no zone. Event times must not be taken from this.
    /// </summary>
    public Timestamp ToTimestamp() => Timestamp.FromUtc(DateTime.SpecifyKind(Local, DateTimeKind.Utc), Raw, Method);
}

/// <summary>One decoded shell item. Nothing here is trusted: every field is read with bounds checks against the item.</summary>
public sealed record ShellItem(
    ShellItemKind Kind, byte TypeByte, string DisplayName, string PathSegment, string ShortName, string Guid,
    DosDateTime? Modified, DosDateTime? Created, DosDateTime? Accessed, long? MftEntry, int? MftSequence,
    uint FileSize, int Size, string Hex, bool Truncated, int ExtensionVersion);

/// <summary>Result of reading an ItemID list: the items, whether the 0x0000 terminator was seen, and what was malformed.</summary>
public sealed record ShellItemList(IReadOnlyList<ShellItem> Items, bool Terminated, int BytesConsumed, IReadOnlyList<string> Issues);

/// <summary>
/// Pure, bounded decoder of Windows shell items (ItemID lists as stored in ShellBags BagMRU, RecentDocs, jump lists). Reads only from the
/// buffer given; no item can make it read past its own size, allocate by a declared length, or recurse. An item it does not understand
/// is returned as <see cref="ShellItemKind.Unknown"/> with its hex, never dropped.
/// The names read from the long-name field of the 0xBEEF0004 extension block are validated against the layouts published for versions
/// 3, 7, 8 and 9; where the fixed position does not hold a plausible string a bounded search inside the block is used.
/// </summary>
public static class ShellItems
{
    public const int MaxItems = 512;
    private const int HexBytes = 64;
    private const uint ExtensionSignature = 0xBEEF0004;

    private static readonly Dictionary<Guid, string> KnownGuids = new()
    {
        [new("20D04FE0-3AEA-1069-A2D8-08002B30309D")] = "Acest PC",
        [new("59031A47-3F72-44A7-89C5-5595FE6B30EE")] = "Dosarul utilizatorului",
        [new("450D8FBA-AD25-11D0-98A8-0800361B1103")] = "Documente",
        [new("FDD39AD0-238F-46AF-ADB4-6C85480369C7")] = "Documente",
        [new("D3162B92-9365-467A-956B-92703ACA08AF")] = "Documente",
        [new("374DE290-123F-4565-9164-39C4925E467B")] = "Descărcări",
        [new("088E3905-0323-4B02-9826-5D99428E115F")] = "Descărcări",
        [new("24AD3AD4-A569-4530-98E1-AB02F9417AA8")] = "Imagini",
        [new("4BD8D571-6D19-48D3-BE97-422220080E43")] = "Muzică",
        [new("A0953C92-50DC-43BF-BE83-3742FED03C9C")] = "Videoclipuri",
        [new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")] = "Desktop",
        [new("F02C1A0D-BE21-4350-88B0-7367FC96EF3C")] = "Rețea",
        [new("208D2C60-3AEA-1069-A2D7-08002B30309D")] = "Locuri în rețea",
        [new("645FF040-5081-101B-9F08-00AA002F954E")] = "Coș de reciclare",
        [new("031E4825-7B94-4DC3-B131-E946B44C8DD5")] = "Biblioteci",
        [new("26EE0668-A00A-44D7-9371-BEB064C98683")] = "Panou de control",
        [new("21EC2020-3AEA-1069-A2DD-08002B30309D")] = "Toate elementele Panoului de control",
        [new("5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0")] = "Toate elementele Panoului de control",
        [new("679F85CB-0220-4080-B29B-5540CC05AAB6")] = "Acces rapid",
        [new("018D5C66-4533-4307-9B53-224DE2ED1FE6")] = "OneDrive",
    };

    /// <summary>Reads an ItemID list: items of [uint16 size][data] until a size of 0 (terminator) or the end of the buffer.</summary>
    public static ShellItemList ParseList(ReadOnlySpan<byte> data)
    {
        var items = new List<ShellItem>();
        var issues = new List<string>();
        int pos = 0;
        bool terminated = false;
        while (pos < data.Length)
        {
            if (items.Count >= MaxItems)
            {
                issues.Add($"lista are mai mult de {MaxItems} elemente; restul nu a fost citit");
                break;
            }
            if (pos + 2 > data.Length)
            {
                issues.Add("lista se termină în mijlocul antetului unui element");
                break;
            }
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
            if (size == 0) { terminated = true; pos += 2; break; }
            if (size < 3)
            {
                // No type byte: the list cannot be resynchronised after this point.
                issues.Add($"element cu dimensiune invalidă ({size}) la poziția {pos}; restul listei nu poate fi citit");
                break;
            }
            int available = data.Length - pos;
            if (size > available)
            {
                issues.Add($"elementul de la poziția {pos} declară {size} octeți, dar mai sunt doar {available}");
                items.Add(Decode(data.Slice(pos, available), truncated: true));
                pos = data.Length;
                break;
            }
            items.Add(Decode(data.Slice(pos, size), truncated: false));
            pos += size;
        }
        if (!terminated && issues.Count == 0 && items.Count > 0) issues.Add("lista nu are terminatorul 0x0000");
        return new(items, terminated, pos, issues);
    }

    /// <summary>Decodes one shell item; <paramref name="item"/> starts with its own 2-byte size. Never throws.</summary>
    public static ShellItem Decode(ReadOnlySpan<byte> item, bool truncated = false)
    {
        try { return DecodeCore(item, truncated); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return Unknown(item, truncated);
        }
    }

    private static ShellItem DecodeCore(ReadOnlySpan<byte> item, bool truncated)
    {
        if (item.Length < 3) return Unknown(item, truncated);
        byte type = item[2];
        int cls = type & 0xF0, length = item.Length;
        string hex = Hex(item);
        ShellItem Make(ShellItemKind kind, string name, string segment = "", string shortName = "", string guid = "",
                       DosDateTime? m = null, DosDateTime? c = null, DosDateTime? a = null, long? mft = null, int? seq = null, uint size = 0, int ver = 0)
            => new(kind, type, name, segment.Length > 0 ? segment : name, shortName, guid, m, c, a, mft, seq, size, length, hex, truncated, ver);

        // Root folders and "My Computer" style items hold a GUID at offset 4.
        if (type == 0x1F || type == 0x1E || type == 0x1D || type == 0x2E || (cls == 0x10 && item.Length >= 20))
        {
            if (item.Length >= 20)
            {
                var g = new Guid(item.Slice(4, 16));
                var text = g.ToString("B").ToUpperInvariant();
                return Make(ShellItemKind.RootFolder, KnownGuids.TryGetValue(g, out var n) ? n : text, guid: text);
            }
            return Unknown(item, truncated);
        }
        if (cls == 0x20)
        {
            var v = AsciiZ(item, 3);
            return v.Length == 0 ? Unknown(item, truncated) : Make(ShellItemKind.Volume, v);
        }
        if (cls == 0x30) return DecodeFileEntry(item, type, truncated);
        if (cls == 0x40 && (type & 0x0F) is 0x01 or 0x02 or 0x03 or 0x05 or 0x06 or 0x0D or 0x0E or 0x0F)
        {
            var loc = AsciiZ(item, 5);
            return loc.Length == 0 ? Unknown(item, truncated) : Make(ShellItemKind.Network, loc);
        }
        if (type == 0x71)
        {
            if (item.Length >= 30)
            {
                var g = new Guid(item.Slice(14, 16));
                var text = g.ToString("B").ToUpperInvariant();
                return Make(ShellItemKind.ControlPanel, KnownGuids.TryGetValue(g, out var n) ? n : "Element Panou de control " + text, guid: text);
            }
            return Unknown(item, truncated);
        }
        if (type == 0x61)
        {
            var url = PrintableRun(item, 5) ?? "";
            return url.Length == 0 ? Unknown(item, truncated) : Make(ShellItemKind.Uri, url);
        }
        if (type == 0x74)
        {
            var (ext, extVer) = FindExtension(item, 4);
            string name = ext is { } e ? LongName(item, e.Start, e.Size, e.Version) ?? "" : "";
            if (name.Length == 0) name = PrintableRun(item, 10) ?? "Folderul fișierelor utilizatorului";
            DosDateTime? cr = null, ac = null;
            long? mft = null; int? sq = null;
            if (ext is { } e2) { cr = ReadDos(item, e2.Start + 8); ac = ReadDos(item, e2.Start + 12); (mft, sq) = FileRef(item, e2); }
            return Make(ShellItemKind.UsersFilesFolder, name, c: cr, a: ac, mft: mft, seq: sq, ver: extVer);
        }
        if (type == 0x00)
        {
            // Variable-length items (ZIP members, MTP devices, ...): best effort, the first plausible UTF-16 or ASCII text.
            var text = PrintableRun(item, 3);
            return text is null ? Unknown(item, truncated) : Make(ShellItemKind.Variable, text);
        }
        return Unknown(item, truncated);
    }

    private static ShellItem DecodeFileEntry(ReadOnlySpan<byte> item, byte type, bool truncated)
    {
        bool dir = (type & 0x01) != 0, file = (type & 0x02) != 0;
        if (item.Length < 14 || (!dir && !file)) return Unknown(item, truncated);
        bool unicode = (type & 0x04) != 0;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(item[4..]);
        var modified = ReadDos(item, 8);
        string shortName = unicode ? Utf16Z(item, 14, item.Length, out _) : AsciiZ(item, 14);
        var (ext, extVer) = FindExtension(item, 14);
        DosDateTime? created = null, accessed = null;
        long? mft = null; int? seq = null;
        string longName = "";
        if (ext is { } e)
        {
            created = ReadDos(item, e.Start + 8);
            accessed = ReadDos(item, e.Start + 12);
            (mft, seq) = FileRef(item, e);
            longName = LongName(item, e.Start, e.Size, e.Version) ?? "";
        }
        string name = longName.Length > 0 ? longName : shortName;
        if (name.Length == 0) name = $"(fără nume, tip 0x{type:X2})";
        return new(dir ? ShellItemKind.Folder : ShellItemKind.File, type, name, name, shortName, "", modified, created, accessed, mft, seq, size, item.Length, Hex(item), truncated, extVer);
    }

    private readonly record struct Extension(int Start, int Size, int Version);

    /// <summary>
    /// The 0xBEEF0004 extension block: found by the offset stored in the item's last two bytes, else by scanning for the signature.
    /// A block with a size that leaves the item, or a version below 3, is ignored.
    /// </summary>
    private static (Extension? Block, int Version) FindExtension(ReadOnlySpan<byte> item, int from)
    {
        if (item.Length >= 2 + 10)
        {
            int off = BinaryPrimitives.ReadUInt16LittleEndian(item[^2..]);
            if (off >= from - 4 && Valid(item, off) is { } ok) return (ok, ok.Version);
        }
        for (int p = Math.Max(from, 4) - 4; p + 8 <= item.Length; p++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(item[(p + 4)..]) == ExtensionSignature && Valid(item, p) is { } found) return (found, found.Version);
        return (null, 0);
    }

    private static Extension? Valid(ReadOnlySpan<byte> item, int start)
    {
        if (start < 0 || start + 10 > item.Length) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(item[(start + 4)..]) != ExtensionSignature) return null;
        int size = BinaryPrimitives.ReadUInt16LittleEndian(item[start..]);
        int ver = BinaryPrimitives.ReadUInt16LittleEndian(item[(start + 2)..]);
        if (size < 12 || start + size > item.Length || ver < 3 || ver > 20) return null;
        return new Extension(start, size, ver);
    }

    /// <summary>MFT entry (48 bits) and sequence (16 bits) from the file reference of extension version 7 and later; null when absent or zero.</summary>
    private static (long?, int?) FileRef(ReadOnlySpan<byte> item, Extension e)
    {
        if (e.Version < 7 || e.Size < 0x1C) return (null, null);
        long entry = 0;
        for (int i = 5; i >= 0; i--) entry = (entry << 8) | item[e.Start + 0x14 + i];
        int seq = BinaryPrimitives.ReadUInt16LittleEndian(item[(e.Start + 0x1A)..]);
        return entry == 0 && seq == 0 ? (null, null) : (entry, seq);
    }

    /// <summary>Long Unicode name of the extension block: the published position for the version, else a bounded search; null when none is plausible.</summary>
    private static string? LongName(ReadOnlySpan<byte> item, int start, int size, int version)
    {
        int end = Math.Min(start + size, item.Length) - 2;   // the last two bytes of the block are not text
        int fixedOff = version switch { 3 => 0x14, 7 => 0x26, 8 => 0x2A, _ when version >= 9 => 0x2E, _ => 0x26 };
        for (int p = start + fixedOff; p <= start + fixedOff + 16 && p + 2 <= end; p += 2)
        {
            if (p != start + fixedOff && version == 3) break;   // version 3 has one fixed position
            var s = Utf16Z(item, p, end, out bool terminated);
            if (terminated && s.Length > 0 && IsPlausibleName(s)) return s;
        }
        return null;
    }

    private static bool IsPlausibleName(string s)
    {
        foreach (var ch in s) if (ch < 0x20 || char.IsSurrogate(ch) && s.Length == 1) return false;
        return true;
    }

    private static DosDateTime? ReadDos(ReadOnlySpan<byte> item, int at)
    {
        if (at < 0 || at + 4 > item.Length) return null;
        ushort d = BinaryPrimitives.ReadUInt16LittleEndian(item[at..]), t = BinaryPrimitives.ReadUInt16LittleEndian(item[(at + 2)..]);
        if (d == 0) return null;
        int day = d & 0x1F, month = (d >> 5) & 0x0F, year = 1980 + (d >> 9);
        int sec = (t & 0x1F) * 2, min = (t >> 5) & 0x3F, hour = t >> 11;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || sec > 59 || min > 59 || hour > 23) return null;
        return new DosDateTime(new DateTime(year, month, day, hour, min, sec, DateTimeKind.Unspecified), d, t);
    }

    /// <summary>NUL-terminated single-byte text starting at <paramref name="at"/>, within the item; the bytes are Latin-1 (the ANSI code page is unknown).</summary>
    private static string AsciiZ(ReadOnlySpan<byte> item, int at)
    {
        if (at < 0 || at >= item.Length) return "";
        var s = item[at..];
        int n = s.IndexOf((byte)0);
        if (n < 0) n = s.Length;
        return Encoding.Latin1.GetString(s[..n]);
    }

    /// <summary>NUL-terminated UTF-16 text in item[at..end); an odd trailing byte is ignored; <paramref name="terminated"/> says whether a NUL was found.</summary>
    private static string Utf16Z(ReadOnlySpan<byte> item, int at, int end, out bool terminated)
    {
        terminated = false;
        end = Math.Min(end, item.Length);
        if (at < 0 || at + 2 > end) return "";
        var sb = new StringBuilder();
        for (int p = at; p + 2 <= end; p += 2)
        {
            char ch = (char)BinaryPrimitives.ReadUInt16LittleEndian(item[p..]);
            if (ch == 0) { terminated = true; break; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>First run of at least 3 printable characters, as UTF-16 (preferred) or single-byte text, searching from <paramref name="from"/>.</summary>
    private static string? PrintableRun(ReadOnlySpan<byte> item, int from)
    {
        for (int p = Math.Max(from, 0); p + 6 <= item.Length; p++)
        {
            int n = 0;
            while (p + 2 * n + 2 <= item.Length && IsText(item[p + 2 * n], item[p + 2 * n + 1])) n++;
            if (n >= 3) return Encoding.Unicode.GetString(item.Slice(p, 2 * n));
        }
        for (int p = Math.Max(from, 0); p + 3 <= item.Length; p++)
        {
            int n = 0;
            while (p + n < item.Length && item[p + n] >= 0x20 && item[p + n] < 0x7F) n++;
            if (n >= 3) return Encoding.Latin1.GetString(item.Slice(p, n));
        }
        return null;
    }

    private static bool IsText(byte lo, byte hi) => hi == 0 && lo >= 0x20 && lo < 0x7F;

    private static ShellItem Unknown(ReadOnlySpan<byte> item, bool truncated)
    {
        byte type = item.Length >= 3 ? item[2] : (byte)0;
        return new(ShellItemKind.Unknown, type, $"element necunoscut (tip 0x{type:X2})", $"element necunoscut (tip 0x{type:X2})", "", "", null, null, null, null, null, 0, item.Length, Hex(item), truncated, 0);
    }

    private static string Hex(ReadOnlySpan<byte> item) =>
        Convert.ToHexString(item[..Math.Min(item.Length, HexBytes)]) + (item.Length > HexBytes ? "…" : "");

    /// <summary>Full path of a chain of items ("Acest PC\C:\Users\test"): segments joined by a backslash, a volume's own trailing backslash not doubled.</summary>
    public static string JoinPath(IEnumerable<string> segments)
    {
        var list = segments.Where(s => s.Length > 0).ToList();
        var joined = string.Join('\\', list.Select(s => s.TrimEnd('\\')));
        return list.Count > 0 && list[^1].EndsWith('\\') ? joined + "\\" : joined;   // a volume root keeps its backslash ("C:\")
    }
}
