namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

/// <summary>
/// Update sequence array fixup shared by FILE records and INDX blocks (format card section 3). Every sector tail is verified against USA[0]
/// before ANY of them is restored, so a torn block never yields a half-restored copy.
/// </summary>
internal static class NtfsFixup
{
    /// <summary>Returns the fixed-up copy of <paramref name="raw"/>, or null with <paramref name="badSector"/> set to the first sector whose tail differs from the USN.</summary>
    public static byte[]? Apply(ReadOnlySpan<byte> raw, int usaOffset, int usaCount, out int badSector)
    {
        badSector = -1;
        var copy = raw.ToArray();
        ushort usn = Le.U16(copy, usaOffset);
        for (int i = 1; i < usaCount; i++)
            if (Le.U16(copy, i * 512 - 2) != usn) { badSector = i; return null; }
        for (int i = 1; i < usaCount; i++)
        {
            copy[i * 512 - 2] = copy[usaOffset + 2 * i];
            copy[i * 512 - 1] = copy[usaOffset + 2 * i + 1];
        }
        return copy;
    }
}
