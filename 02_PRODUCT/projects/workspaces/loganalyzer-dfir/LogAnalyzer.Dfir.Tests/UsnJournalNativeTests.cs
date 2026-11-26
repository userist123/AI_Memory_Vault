using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Research 2026-11-07, plan P1.4: the change journal of external NTFS media is read directly at collection. fsutil was not used because, redirected, it lost
/// Romanian letters („Raport șță” → „Raport ??a”); on the lab VHD the direct read matched fsutil on all 87 records, field by field, and kept the names.
/// </summary>
public sealed class UsnJournalNativeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_usnn_" + Guid.NewGuid().ToString("N"));
    public UsnJournalNativeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly DateTimeOffset T = new(2026, 11, 7, 15, 10, 47, TimeSpan.Zero);

    // one FSCTL_READ_USN_JOURNAL output: 8-byte next USN, then records
    private static byte[] Buffer(params byte[][] records)
    {
        var b = new List<byte>(BitConverter.GetBytes(999L));
        foreach (var r in records) b.AddRange(r);
        return [.. b];
    }

    private static byte[] V2(long usn, string name, uint reason, ulong id, ulong parent, uint attr = 0x20)
    {
        var n = Encoding.Unicode.GetBytes(name);
        var len = (60 + n.Length + 7) & ~7;
        var r = new byte[len];
        BitConverter.GetBytes((uint)len).CopyTo(r, 0); BitConverter.GetBytes((ushort)2).CopyTo(r, 4);
        BitConverter.GetBytes(id).CopyTo(r, 8); BitConverter.GetBytes(parent).CopyTo(r, 16); BitConverter.GetBytes(usn).CopyTo(r, 24);
        BitConverter.GetBytes(T.UtcDateTime.ToFileTimeUtc()).CopyTo(r, 32); BitConverter.GetBytes(reason).CopyTo(r, 40); BitConverter.GetBytes(attr).CopyTo(r, 52);
        BitConverter.GetBytes((ushort)n.Length).CopyTo(r, 56); BitConverter.GetBytes((ushort)60).CopyTo(r, 58); n.CopyTo(r, 60);
        return r;
    }

    private static byte[] V3(long usn, string name, uint reason, ulong idLow, ulong parentLow)
    {
        var n = Encoding.Unicode.GetBytes(name);
        var len = (76 + n.Length + 7) & ~7;
        var r = new byte[len];
        BitConverter.GetBytes((uint)len).CopyTo(r, 0); BitConverter.GetBytes((ushort)3).CopyTo(r, 4);
        BitConverter.GetBytes(idLow).CopyTo(r, 8); BitConverter.GetBytes(parentLow).CopyTo(r, 24); BitConverter.GetBytes(usn).CopyTo(r, 40);
        BitConverter.GetBytes(T.UtcDateTime.ToFileTimeUtc()).CopyTo(r, 48); BitConverter.GetBytes(reason).CopyTo(r, 56); BitConverter.GetBytes(0x20u).CopyTo(r, 68);
        BitConverter.GetBytes((ushort)n.Length).CopyTo(r, 72); BitConverter.GetBytes((ushort)76).CopyTo(r, 74); n.CopyTo(r, 76);
        return r;
    }

    [Fact]
    public void Version_2_and_3_records_are_read_with_their_Unicode_names_and_exact_time()
    {
        var recs = UsnJournalNative.ParseBuffer(Buffer(V2(0, "Raport șțăîâ.docx", 0x80000102, 0x0001000000000027, 0x0005000000000005),
                                                        V3(104, "plan secret.xlsx", 0x80000200, 0x0002000000000030, 0x0005000000000005))).ToList();
        Assert.Equal(2, recs.Count);
        Assert.Equal("Raport șțăîâ.docx", recs[0].FileName);
        Assert.Equal(T, recs[0].TimeUtc);
        Assert.Equal((UInt128)0x0001000000000027, recs[0].FileId);
        Assert.Equal(3, recs[1].MajorVersion);
        Assert.Equal((UInt128)0x0002000000000030, recs[1].FileId);
        Assert.Equal("File delete | Close", UsnJournalNative.ReasonText(recs[1].Reason));
    }

    [Fact]
    public void The_words_are_the_ones_fsutil_prints()
    {
        // seen in fsutil's own output on the lab VHD
        Assert.Equal("Data extend | File create | Close", UsnJournalNative.ReasonText(0x80000102));
        Assert.Equal("Rename: old name", UsnJournalNative.ReasonText(0x00001000));
        Assert.Equal("Named data extend | Stream change | Close", UsnJournalNative.ReasonText(0x80200020));
        Assert.Equal("Archive | Compressed", UsnJournalNative.AttributeText(0x820));
        Assert.Equal("Read only | Hidden", UsnJournalNative.AttributeText(0x3));
    }

    [Fact]
    public void The_written_journal_is_read_by_the_USN_parser_with_the_exact_UTC_time_and_the_full_path()
    {
        var recs = UsnJournalNative.ParseBuffer(Buffer(
            V2(0, "Documente", 0x80000100, 0x0001000000000030, 0x0005000000000005, 0x10),
            V2(96, "Raport șțăîâ.docx", 0x80000102, 0x0001000000000027, 0x0001000000000030))).ToList();
        var path = Path.Combine(_dir, "usn_E.csv");
        using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            UsnJournalNative.WriteCsv(w, new UsnJournalInfo(0x01dd6eca2b00544f, 0, 200), recs, TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time"));

        var sink = new ListSink();
        var result = new UsnJournalParser().Parse(new EvidenceItem { EvidenceId = "EV-U", CaseId = "C", Source = "USN:E", SourceType = "usn_journal", StoredPath = path }, path, sink, default);
        Assert.Equal(2, result.Records);
        var file = Assert.Single(sink.Events, e => e.Fields["FileName"] == "Raport șțăîâ.docx");
        Assert.Equal(T, file.Time.Utc);
        Assert.Contains("citit direct", file.Time.ConversionMethod);
        Assert.Equal(@"…\Documente\Raport șțăîâ.docx", file.Path);
        Assert.Equal("0x01dd6eca2b00544f", file.Fields["JournalId"]);
        // the fsutil local-time column is still written, in fsutil's own form, for an examiner who compares by eye
        Assert.Contains("\"07-Nov-26 17:10:47\"", File.ReadAllText(path));
    }

    [Fact]
    public void Only_external_NTFS_volumes_are_read_never_the_system_case_or_application_volume_or_FAT_media()
    {
        var targets = UsnJournalCollector.Targets(
        [
            new("C", "NTFS", false, false), new("D", "NTFS", false, false),      // system and internal data disk
            new("E", "NTFS", true, false), new("F", "FAT32", true, false),       // NTFS stick, FAT stick
            new("G", "NTFS", false, true), new("H", "exFAT", false, true),       // USB hard disk (reports as fixed), exFAT one
            new("C", "NTFS", true, true),                                        // the system letter is never taken, whatever it reports
            new("K", "NTFS", false, true), new("L", "NTFS", true, false),        // review: the examiner's case disk and the app's stick
        ], @"C:\Windows\System32", @"K:\Cazuri\CAZ-1", @"L:\LogAnalyzer\");
        Assert.Equal(["E", "G"], targets);
        Assert.Equal("", UsnJournalCollector.Letter(@"\\server\share\caz"));
    }

    [Fact]
    public void A_crafted_or_damaged_buffer_never_throws_and_a_time_past_year_9999_is_unknown()
    {
        // review round 1: a record with FILETIME = long.MaxValue threw and failed the whole collection
        var bad = V2(0, "x.txt", 0x80000100, 1, 5);
        BitConverter.GetBytes(long.MaxValue).CopyTo(bad, 32);
        var r = Assert.Single(UsnJournalNative.ParseBuffer(Buffer(bad)));
        Assert.Equal(DateTimeOffset.MinValue, r.TimeUtc);

        var zeroLen = V2(0, "a", 0x100, 1, 5); BitConverter.GetBytes(0u).CopyTo(zeroLen, 0);
        var hugeLen = V2(0, "a", 0x100, 1, 5); BitConverter.GetBytes(0xFFFFFFF0u).CopyTo(hugeLen, 0);
        var badName = V2(0, "abc", 0x100, 1, 5); BitConverter.GetBytes((ushort)0x7FF0).CopyTo(badName, 58);
        var v3Short = V3(0, "a", 0x100, 1, 5)[..70]; BitConverter.GetBytes(70u).CopyTo(v3Short, 0);
        Assert.Empty(UsnJournalNative.ParseBuffer(Buffer(zeroLen)));
        Assert.Empty(UsnJournalNative.ParseBuffer(Buffer(hugeLen)));
        Assert.Equal("", Assert.Single(UsnJournalNative.ParseBuffer(Buffer(badName))).FileName);
        Assert.Empty(UsnJournalNative.ParseBuffer(Buffer(v3Short)));
        Assert.Empty(UsnJournalNative.ParseBuffer(Buffer(V2(0, "abc", 0x100, 1, 5)[..30])));   // cut off
    }

    [Fact]
    public void The_high_half_of_a_128_bit_file_id_is_kept_and_names_with_quotes_and_commas_survive_the_CSV()
    {
        var v3 = V3(0, "a\"b,c.txt", 0x80000100, 0x27, 5);
        BitConverter.GetBytes(0x00000000000000ABUL).CopyTo(v3, 16);   // high 8 bytes of the file id
        var r = Assert.Single(UsnJournalNative.ParseBuffer(Buffer(v3)));
        Assert.Equal(((UInt128)0xAB << 64) | 0x27, r.FileId);

        var path = Path.Combine(_dir, "q.csv");
        using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            UsnJournalNative.WriteCsv(w, new UsnJournalInfo(1, 0, 100), [r], TimeZoneInfo.Utc, "volumul NTFS, seria 1A2B-3C4D", "citirea s-a întrerupt la USN 50");
        var text = File.ReadAllText(path);
        Assert.Contains("00000000000000ab0000000000000027", text);
        Assert.Contains("Read complete     : NU - citirea s-a întrerupt", text);
        Assert.Contains("seria 1A2B-3C4D", text);
        var sink = new ListSink();
        new UsnJournalParser().Parse(new EvidenceItem { EvidenceId = "EV-U", CaseId = "C", Source = "USN:E", SourceType = "usn_journal", StoredPath = path }, path, sink, default);
        Assert.Equal("a\"b,c.txt", Assert.Single(sink.Events).Fields["FileName"]);
    }

    [Fact]
    public void A_volume_label_with_a_line_break_cannot_fake_the_column_line_and_drop_the_journal()
    {
        // review round 2: the label comes from the suspect medium and is written into the header
        var recs = UsnJournalNative.ParseBuffer(Buffer(V2(0, "plan.docx", 0x80000100, 0x27, 5))).ToList();
        var path = Path.Combine(_dir, "label.csv");
        using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            UsnJournalNative.WriteCsv(w, new UsnJournalInfo(1, 0, 100), recs, TimeZoneInfo.Utc, "volumul NTFS „x\r\nUsn,File name\nfals”", "a\nb");
        var sink = new ListSink();
        var result = new UsnJournalParser().Parse(new EvidenceItem { EvidenceId = "EV-U", CaseId = "C", Source = "USN:E", SourceType = "usn_journal", StoredPath = path }, path, sink, default);
        Assert.Equal(1, result.Records);
        Assert.Equal(0, result.MalformedRecords);
        Assert.Equal("plan.docx", Assert.Single(sink.Events).Fields["FileName"]);
    }
}