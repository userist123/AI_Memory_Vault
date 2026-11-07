using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LogAnalyzer.Dfir.Windows.Native;

/// <summary>One change-journal record as the volume gives it (USN_RECORD_V2 or V3).</summary>
public sealed record UsnRecord(long Usn, string FileName, uint Reason, DateTimeOffset TimeUtc, uint Attributes, UInt128 FileId, UInt128 ParentId,
                               uint SourceInfo, uint SecurityId, ushort MajorVersion, ushort MinorVersion, uint RecordLength, ushort FileNameLength = 0);

/// <summary>The journal's identity and range (USN_JOURNAL_DATA).</summary>
public sealed record UsnJournalInfo(ulong JournalId, long FirstUsn, long NextUsn);

/// <summary>What was read: the records, and whether the read stopped early (record limit, or an error after part of the journal was read).</summary>
public sealed record UsnJournalRead(UsnJournalInfo Info, List<UsnRecord> Records, string Incomplete)
{
    public bool Complete => Incomplete.Length == 0;
}

/// <summary>
/// Reads a volume's NTFS change journal directly (FSCTL_QUERY_USN_JOURNAL / FSCTL_READ_USN_JOURNAL), read only. fsutil cannot be used by the application:
/// when its output is redirected it writes the names in the console code page, and „Raport șță” came out „Raport ??a” on the lab VHD (2026-11-07). The
/// records are written in the same CSV layout as „fsutil usn readjournal … csv” (same columns and words, checked against fsutil on that VHD), so one parser
/// reads both, plus a „Time stamp UTC” column with the exact time the volume recorded.
/// </summary>
public static class UsnJournalNative
{
    private const uint FsctlQueryUsnJournal = 0x000900F4, FsctlReadUsnJournal = 0x000900BB;
    private const uint GenericRead = 0x80000000, FileShareReadWrite = 0x3, OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalDataV1
    {
        public long StartUsn; public uint ReasonMask; public uint ReturnOnlyOnClose; public ulong Timeout; public ulong BytesToWaitFor; public ulong UsnJournalId;
        public ushort MinMajorVersion; public ushort MaxMajorVersion;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref ReadUsnJournalDataV1 input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>Every record of the journal of <paramref name="letter"/>, oldest first. Needs administrator rights; changes nothing on the volume.</summary>
    public static UsnJournalRead Read(char letter, CancellationToken ct, int maxRecords = 2_000_000)
    {
        using var h = CreateFileW($@"\\.\{char.ToUpperInvariant(letter)}:", GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var q = new byte[80];
        if (!DeviceIoControl(h, FsctlQueryUsnJournal, IntPtr.Zero, 0, q, q.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new UsnJournalInfo(BitConverter.ToUInt64(q, 0), BitConverter.ToInt64(q, 8), BitConverter.ToInt64(q, 16));

        var records = new List<UsnRecord>();
        var input = new ReadUsnJournalDataV1 { StartUsn = info.FirstUsn, ReasonMask = 0xFFFFFFFF, UsnJournalId = info.JournalId, MinMajorVersion = 2, MaxMajorVersion = 3 };
        var buffer = new byte[1 << 16];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (records.Count >= maxRecords)
                return new UsnJournalRead(info, records, $"citirea s-a oprit la limita de {maxRecords} înregistrări (USN {input.StartUsn} din {info.NextUsn}); restul jurnalului nu este în export");
            if (!DeviceIoControl(h, FsctlReadUsnJournal, ref input, Marshal.SizeOf<ReadUsnJournalDataV1>(), buffer, buffer.Length, out var got, IntPtr.Zero))
            {
                var err = Marshal.GetLastWin32Error();
                // review: an error after part of the journal was read (1181 = entries deleted while reading, 1178 = journal recreated) keeps what was read
                if (records.Count == 0) throw new Win32Exception(err);
                return new UsnJournalRead(info, records, $"citirea s-a întrerupt la USN {input.StartUsn} (cod Windows {err}: {new Win32Exception(err).Message}); se păstrează ce s-a citit");
            }
            if (got <= 8) break;
            records.AddRange(ParseBuffer(buffer.AsSpan(0, got)));
            var next = BitConverter.ToInt64(buffer, 0);
            if (next <= input.StartUsn) break;
            input.StartUsn = next;
        }
        return new UsnJournalRead(info, records, "");
    }
    /// <summary>The records in one FSCTL_READ_USN_JOURNAL output (8-byte next USN, then records). Pure, for the tests; V4 and unknown versions are skipped.</summary>
    public static IEnumerable<UsnRecord> ParseBuffer(ReadOnlySpan<byte> data)
    {
        var list = new List<UsnRecord>();
        for (int off = 8; off + 8 <= data.Length;)
        {
            var len = BitConverter.ToUInt32(data[off..]);
            if (len < 8 || off + len > data.Length) break;
            var r = data.Slice(off, (int)len);
            var major = BitConverter.ToUInt16(r[4..]);
            var minor = BitConverter.ToUInt16(r[6..]);
            if (major == 2 && len >= 60) list.Add(Record(r, major, minor, len, BitConverter.ToUInt64(r[8..]), BitConverter.ToUInt64(r[16..]), 24));
            else if (major == 3 && len >= 76) list.Add(Record(r, major, minor, len, ReadId128(r[8..]), ReadId128(r[24..]), 40));
            off += (int)len;
        }
        return list;
    }

    private static readonly long MaxFileTime = DateTime.MaxValue.ToFileTimeUtc();

    private static UInt128 ReadId128(ReadOnlySpan<byte> b) => new(BitConverter.ToUInt64(b[8..]), BitConverter.ToUInt64(b));

    private static UsnRecord Record(ReadOnlySpan<byte> r, ushort major, ushort minor, uint len, UInt128 id, UInt128 parent, int at)
    {
        var usn = BitConverter.ToInt64(r[at..]);
        var ft = BitConverter.ToInt64(r[(at + 8)..]);
        var reason = BitConverter.ToUInt32(r[(at + 16)..]);
        var source = BitConverter.ToUInt32(r[(at + 20)..]);
        var sec = BitConverter.ToUInt32(r[(at + 24)..]);
        var attr = BitConverter.ToUInt32(r[(at + 28)..]);
        var nameLen = BitConverter.ToUInt16(r[(at + 32)..]);
        var nameOff = BitConverter.ToUInt16(r[(at + 34)..]);
        var name = nameOff + nameLen <= r.Length ? Encoding.Unicode.GetString(r.Slice(nameOff, nameLen)) : "";
        var time = ft > 0 && ft <= MaxFileTime ? new DateTimeOffset(DateTime.FromFileTimeUtc(ft)) : DateTimeOffset.MinValue;   // out of range: unknown time, never an exception
        return new UsnRecord(usn, name, reason, time, attr, id, parent, source, sec, major, minor, len, nameLen);
    }

    // the words fsutil prints; the ones marked * were seen in fsutil's own output on the lab VHD (2026-11-07), the others follow the USN_REASON names
    private static readonly (uint Bit, string Word)[] Reasons =
    [
        (0x00000001, "Data overwrite*"), (0x00000002, "Data extend*"), (0x00000004, "Data truncation*"), (0x00000010, "Named data overwrite"),
        (0x00000020, "Named data extend*"), (0x00000040, "Named data truncation"), (0x00000100, "File create*"), (0x00000200, "File delete*"),
        (0x00000400, "Extended attribute change"), (0x00000800, "Security change*"), (0x00001000, "Rename: old name*"), (0x00002000, "Rename: new name*"),
        (0x00004000, "Indexable change"), (0x00008000, "Basic info change*"), (0x00010000, "Hard link change*"), (0x00020000, "Compression change*"),
        (0x00040000, "Encryption change"), (0x00080000, "Object ID change"), (0x00100000, "Reparse point change"), (0x00200000, "Stream change*"),
        (0x00400000, "Transacted change"), (0x00800000, "Integrity change"), (0x80000000, "Close*"),
    ];

    private static readonly (uint Bit, string Word)[] AttributeWords =
    [
        (0x1, "Read only*"), (0x2, "Hidden*"), (0x4, "System"), (0x10, "Directory*"), (0x20, "Archive*"), (0x40, "Device"), (0x80, "Normal*"),
        (0x100, "Temporary"), (0x200, "Sparse file"), (0x400, "Reparse point"), (0x800, "Compressed*"), (0x1000, "Offline"), (0x2000, "Not content indexed"),
        (0x4000, "Encrypted"), (0x8000, "Integrity stream"),
    ];

    public static string ReasonText(uint reason) => Words(reason, Reasons);
    public static string AttributeText(uint attributes) => Words(attributes, AttributeWords);

    private static string Words(uint value, (uint Bit, string Word)[] table) =>
        string.Join(" | ", table.Where(t => (value & t.Bit) != 0).Select(t => t.Word.TrimEnd('*')));

    private static string Id(UInt128 v) => ((ulong)(v >> 64)).ToString("X16", CultureInfo.InvariantCulture).ToLowerInvariant() + ((ulong)v).ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>The text with every control character (CR, LF, tab ...) replaced by a space, for a header line.</summary>
    public static string OneLine(string s) => new((s ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());

    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// The fsutil CSV layout: the header block (USN Journal ID, First USN, Next USN), the column line, one row per record. „Time stamp” is the local time of
    /// <paramref name="zone"/> in fsutil's form; „Time stamp UTC” (ISO 8601) is the exact time, which the parser prefers. Pure.
    /// </summary>
    public static void WriteCsv(TextWriter w, UsnJournalInfo info, IEnumerable<UsnRecord> records, TimeZoneInfo zone, string volume = "", string incomplete = "")
    {
        // "USN Journal ID" must be the FIRST line: it is how the evidence preflight recognises the format (2026-11-08: with "Exported by" first, the
        // collected journal was refused as a format mismatch and never parsed)
        w.WriteLine($"USN Journal ID    : 0x{info.JournalId:x16}");
        w.WriteLine($"First USN         : {info.FirstUsn.ToString(CultureInfo.InvariantCulture)}");
        w.WriteLine($"Next USN          : {info.NextUsn.ToString(CultureInfo.InvariantCulture)}");
        w.WriteLine("Exported by       : LogAnalyzer (FSCTL_READ_USN_JOURNAL, citire directă; doar înregistrările v2/v3, convertite în formatul fsutil)");
        // review round 2: the volume label comes from the suspect medium; a line break in it would fake the CSV column line and drop the journal
        volume = OneLine(volume); incomplete = OneLine(incomplete);
        if (volume.Length > 0) w.WriteLine($"Volume            : {volume}");
        w.WriteLine($"Read complete     : {(incomplete.Length == 0 ? "da" : "NU - " + incomplete)}");
        w.WriteLine();
        w.WriteLine("Usn,File name,File name length,Reason #,Reason,Time stamp,File attributes #,File attributes,File ID,Parent file ID,Source info #,Source info,Security ID,Major version,Minor version,Record length,Time stamp UTC");
        foreach (var r in records)
        {
            var local = r.TimeUtc == DateTimeOffset.MinValue ? "" : TimeZoneInfo.ConvertTime(r.TimeUtc, zone).ToString("dd-MMM-yy HH:mm:ss", CultureInfo.InvariantCulture);
            var utc = r.TimeUtc == DateTimeOffset.MinValue ? "" : r.TimeUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            w.WriteLine(string.Join(',',
                r.Usn.ToString(CultureInfo.InvariantCulture), Q(r.FileName), (r.FileNameLength > 0 ? r.FileNameLength : r.FileName.Length * 2).ToString(CultureInfo.InvariantCulture),
                $"0x{r.Reason:x8}", Q(ReasonText(r.Reason)), Q(local), $"0x{r.Attributes:x8}", Q(AttributeText(r.Attributes)), Id(r.FileId), Id(r.ParentId),
                $"0x{r.SourceInfo:x8}", Q(r.SourceInfo == 0 ? "*NONE*" : "0x" + r.SourceInfo.ToString("x8", CultureInfo.InvariantCulture)),
                r.SecurityId.ToString(CultureInfo.InvariantCulture), r.MajorVersion.ToString(CultureInfo.InvariantCulture), r.MinorVersion.ToString(CultureInfo.InvariantCulture),
                r.RecordLength.ToString(CultureInfo.InvariantCulture), utc));
        }
    }
}
