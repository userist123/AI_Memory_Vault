using System.Buffers.Binary;
using System.Globalization;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>The file system a volume boot record belongs to.</summary>
public enum VbrFileSystem { Ntfs, ExFat, Fat32, Fat16 }

/// <summary>
/// Volume serial number read from the first sector of a volume (the volume boot record, VBR). Windows logs the first bytes of each volume of a disk
/// in Partition/Diagnostic 1006 (<c>Vbr0</c> ... <c>Vbr3</c>); LNK files and Jump Lists carry the same serial for the volume a file was opened from
/// (<c>DriveSerialNumber</c>). Own code, no library: the layouts are fixed and short.
/// The serial is returned the way the LNK parser prints it: the 32-bit value as 8 upper-case hex digits (for NTFS the low 32 bits of the 64-bit
/// serial, which is what <c>GetVolumeInformation</c> reports and what Windows stores in a shortcut).
/// </summary>
public static class VolumeBootRecord
{
    public sealed record Volume(VbrFileSystem FileSystem, string Serial)
    {
        public string FileSystemName => FileSystem switch { VbrFileSystem.Ntfs => "NTFS", VbrFileSystem.ExFat => "exFAT", VbrFileSystem.Fat32 => "FAT32", _ => "FAT16/FAT12" };
    }

    /// <summary>The serial and file system of a boot sector, or null when the bytes are not a recognised VBR (no signature, unknown layout, too short).</summary>
    public static Volume? TryParse(ReadOnlySpan<byte> s)
    {
        if (s.Length < 512 || s[510] != 0x55 || s[511] != 0xAA) return null;
        if (Oem(s, 3, "NTFS    ")) return new(VbrFileSystem.Ntfs, Hex(s, 0x48));
        if (Oem(s, 3, "EXFAT   ")) return new(VbrFileSystem.ExFat, Hex(s, 0x64));
        // FAT: the extended boot signature 0x29 precedes the volume id; FAT32 keeps it at 0x42, FAT12/16 at 0x26. The type label confirms which.
        if (Oem(s, 0x52, "FAT32   ") && s[0x42] == 0x29) return new(VbrFileSystem.Fat32, Hex(s, 0x43));
        if ((Oem(s, 0x36, "FAT16   ") || Oem(s, 0x36, "FAT12   ") || Oem(s, 0x36, "FAT     ")) && s[0x26] == 0x29) return new(VbrFileSystem.Fat16, Hex(s, 0x27));
        return null;
    }

    /// <summary>Decodes the hex text of a logged VBR (digits only matter; spaces, "0x" and separators are ignored); null when it is not valid hex.</summary>
    public static byte[]? FromHex(string text)
    {
        var digits = new List<char>(text.Length);
        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        foreach (var c in t)
        {
            if (Uri.IsHexDigit(c)) digits.Add(c);
            else if (!(char.IsWhiteSpace(c) || c is '-' or ':' or ',')) return null;
        }
        if (digits.Count == 0 || digits.Count % 2 != 0) return null;
        var bytes = new byte[digits.Count / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(new string([digits[2 * i], digits[2 * i + 1]]), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    /// <summary>
    /// The volume serials in one Partition/Diagnostic 1006 event: <c>Vbr0</c> to <c>Vbr3</c> (up to four volumes on the disk), each parsed on its own.
    /// A field that is empty or not a recognised VBR is skipped, never guessed.
    /// </summary>
    public static IReadOnlyList<Volume> FromEvent(Func<string, string> field)
    {
        var list = new List<Volume>();
        for (int i = 0; i < 4; i++)
            if (field("Vbr" + i.ToString(CultureInfo.InvariantCulture)) is { Length: > 0 } hex && FromHex(hex) is { } bytes && TryParse(bytes) is { } v && !list.Contains(v))
                list.Add(v);
        return list;
    }

    private static bool Oem(ReadOnlySpan<byte> s, int at, string name)
    {
        for (int i = 0; i < name.Length; i++) if (s[at + i] != (byte)name[i]) return false;
        return true;
    }

    private static string Hex(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt32LittleEndian(s[at..]).ToString("X8", CultureInfo.InvariantCulture);
}
