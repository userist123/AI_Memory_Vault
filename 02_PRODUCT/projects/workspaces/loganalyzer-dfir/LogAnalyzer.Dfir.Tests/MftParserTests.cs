using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Comparison row E8: the NTFS master file table exported as a file ($MFT). Records are built byte by byte here from the NTFS layout (FILE header,
/// update sequence array, resident $STANDARD_INFORMATION and $FILE_NAME), so every value the parser reports is known exactly.
/// </summary>
public sealed class MftParserTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ladfir_mft_").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTime T2026 = new(2026, 9, 1, 10, 0, 0, 123, DateTimeKind.Utc);
    private static readonly DateTime T2019 = new(2019, 3, 4, 8, 0, 0, DateTimeKind.Utc);

    internal sealed record Fn(long Parent, ushort ParentSeq, string Name, byte Namespace, DateTime Times);

    /// <summary>One 1024-byte FILE record with the update sequence applied, as it is on disk.</summary>
    internal static byte[] Record(uint number, ushort seq, bool inUse, bool dir, (DateTime C, DateTime M, DateTime R, DateTime A) si, params Fn[] names) =>
        RecordOf(number, seq, inUse, dir, si, names);

    /// <summary>
    /// The same builder with the record size (one update sequence entry per 512-byte sector) and the update sequence offset: 0x30 is the layout with
    /// the record number at 0x2C, 0x2A is the older layout without it (the number is then the position in the file).
    /// </summary>
    internal static byte[] RecordOf(uint number, ushort seq, bool inUse, bool dir, (DateTime C, DateTime M, DateTime R, DateTime A) si, Fn[] names, int size = 1024, int usaOffset = 0x30)
    {
        var b = new byte[size];
        int sectors = size / 512;
        int firstAttr = (usaOffset + 2 * (sectors + 1) + 7) & ~7;
        "FILE"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x04), (ushort)usaOffset);  // update sequence offset
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x06), (ushort)(sectors + 1));     // 1 sequence number + one entry per sector
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x10), seq);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x12), (ushort)names.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x14), (ushort)firstAttr);  // first attribute
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x16), (ushort)((inUse ? 1 : 0) | (dir ? 2 : 0)));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x1C), (uint)size);
        if (usaOffset >= 0x30) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x2C), number);
        int o = firstAttr;
        var siv = new byte[0x48];
        BinaryPrimitives.WriteInt64LittleEndian(siv.AsSpan(0x00), si.C.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(siv.AsSpan(0x08), si.M.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(siv.AsSpan(0x10), si.R.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(siv.AsSpan(0x18), si.A.ToFileTimeUtc());
        o = Attr(b, o, 0x10, siv);
        foreach (var n in names)
        {
            var nm = Encoding.Unicode.GetBytes(n.Name);
            var fv = new byte[0x42 + nm.Length];
            BinaryPrimitives.WriteUInt64LittleEndian(fv.AsSpan(0x00), (ulong)n.Parent | ((ulong)n.ParentSeq << 48));
            for (int i = 0; i < 4; i++) BinaryPrimitives.WriteInt64LittleEndian(fv.AsSpan(0x08 + 8 * i), n.Times.ToFileTimeUtc());
            fv[0x40] = (byte)n.Name.Length;
            fv[0x41] = n.Namespace;
            nm.CopyTo(fv, 0x42);
            o = Attr(b, o, 0x30, fv);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x18), (uint)(o + 8));
        // update sequence: the last two bytes of each sector go to the array, the sector ends get the sequence number
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(usaOffset), 0x0007);
        for (int s = 0; s < sectors; s++)
        {
            b[usaOffset + 2 + 2 * s] = b[510 + 512 * s]; b[usaOffset + 3 + 2 * s] = b[511 + 512 * s];
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(510 + 512 * s), 0x0007);
        }
        return b;
    }

    private static int Attr(byte[] b, int o, uint type, byte[] value)
    {
        int len = (0x18 + value.Length + 7) & ~7;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), type);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o + 4), (uint)len);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o + 0x10), (uint)value.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o + 0x14), 0x18);
        value.CopyTo(b, o + 0x18);
        return o + len;
    }

    private static (DateTime, DateTime, DateTime, DateTime) Same(DateTime t) => (t, t, t, t);

    /// <summary>Root (5), \Docs (40, sequence 2), a deleted \Docs\secret.docx (41), a file whose $SI creation predates its $FILE_NAME one (42).</summary>
    internal static byte[] Sample() =>
    [
        .. Record(5, 5, true, true, Same(T2026), new Fn(5, 5, ".", 3, T2026)),
        .. Record(40, 2, true, true, Same(T2026), new Fn(5, 5, "Docs", 1, T2026)),
        .. Record(41, 3, false, false, Same(T2026.AddDays(3)), new Fn(40, 2, "SECRET~1.DOC", 2, T2026), new Fn(40, 2, "secret.docx", 1, T2026)),
        .. Record(42, 1, true, false, (T2019, T2019, T2026, T2026), new Fn(40, 2, "unealta.exe", 3, T2026)),
    ];

    private string Write(byte[] bytes, string name = "$MFT")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static List<TimelineEvent> Parse(string path, out ParseResult r)
    {
        var sink = new ListSink();
        r = new MftParser().Parse(new EvidenceItem { EvidenceId = "EV-M", CaseId = "C", Source = "mft", SourceType = MftParser.SourceTypeName, StoredPath = path }, path, sink, default);
        return sink.Events;
    }

    [Fact]
    public void The_content_is_recognised_as_an_MFT_and_the_import_type_follows_it()
    {
        var p = Write(Sample());
        Assert.Equal("ntfs_mft", EvidenceFingerprint.Detect(p));
        Assert.Equal(MftParser.SourceTypeName, LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.ImportType(p));
    }

    [Fact]
    public void A_free_record_with_a_name_is_a_deleted_file_with_its_Win32_name_and_the_path_rebuilt_from_its_parents()
    {
        var ev = Parse(Write(Sample()), out var r);
        Assert.Equal(4, r.Records);
        var d = Assert.Single(ev, e => e.Fields["Kind"] == "deleted");
        Assert.Equal(@"\Docs\secret.docx", d.Path);
        Assert.Equal("41", d.Fields["Record"]);
        Assert.Equal("3", d.Fields["Sequence"]);
        Assert.Equal(T2026.AddDays(3), d.Time.Utc!.Value.UtcDateTime);   // last $SI change known before the deletion
        Assert.Contains("ștergerii nu este", d.TimeSemantics, StringComparison.Ordinal);
        Assert.Equal(Classification.Direct, d.Classification);
    }

    [Fact]
    public void A_creation_time_in_STANDARD_INFORMATION_before_the_one_in_FILE_NAME_is_a_timestomp_candidate()
    {
        var ev = Parse(Write(Sample()), out _);
        var t = Assert.Single(ev, e => e.Fields["Kind"] == "timestomp");
        Assert.Equal(@"\Docs\unealta.exe", t.Path);
        Assert.Equal(T2019.ToString("o"), t.Fields["SiCreatedUtc"]);
        Assert.Equal(T2026.ToString("o"), t.Fields["FnCreatedUtc"]);
        Assert.Equal("true", t.Fields["SiWholeSeconds"]);   // the $SI times have no fraction of a second, the $FILE_NAME ones do
        Assert.Equal(Classification.Candidate, t.Classification);
    }

    [Fact]
    public void The_summary_counts_records_free_records_and_candidates()
    {
        var ev = Parse(Write(Sample()), out _);
        var s = Assert.Single(ev, e => e.Fields["Kind"] == "summary");
        Assert.Equal("4", s.Fields["Records"]);
        Assert.Equal("1", s.Fields["Deleted"]);
        Assert.Equal("1", s.Fields["TimestompCandidates"]);
        Assert.Equal("1024", s.Fields["RecordSize"]);
    }

    [Fact]
    public void A_parent_record_reused_since_the_deletion_does_not_lend_its_name_to_the_path()
    {
        // the deleted file points at record 40 with sequence 1, but record 40 is now sequence 2 (another folder)
        byte[] mft = [.. Record(5, 5, true, true, Same(T2026), new Fn(5, 5, ".", 3, T2026)), .. Record(40, 2, true, true, Same(T2026), new Fn(5, 5, "Alt", 1, T2026)),
                      .. Record(41, 3, false, false, Same(T2026), new Fn(40, 1, "vechi.txt", 1, T2026))];
        var d = Assert.Single(Parse(Write(mft), out _), e => e.Fields["Kind"] == "deleted");
        Assert.Equal(@"…\vechi.txt", d.Path);
    }

    [Fact]
    public void A_record_whose_sector_ends_do_not_match_the_update_sequence_is_malformed_and_not_read()
    {
        var bad = Record(41, 3, false, false, Same(T2026), new Fn(5, 5, "x.txt", 1, T2026));
        bad[510] ^= 0xFF;   // torn write: the first sector end no longer carries the sequence number
        byte[] mft = [.. Record(5, 5, true, true, Same(T2026), new Fn(5, 5, ".", 3, T2026)), .. bad];
        var ev = Parse(Write(mft), out var r);
        Assert.Equal(1, r.MalformedRecords);
        Assert.DoesNotContain(ev, e => e.Fields["Kind"] == "deleted");
    }

    [Fact]
    public void An_empty_record_slot_is_skipped_without_counting_it_as_malformed()
    {
        byte[] mft = [.. Record(5, 5, true, true, Same(T2026), new Fn(5, 5, ".", 3, T2026)), .. new byte[1024]];
        Parse(Write(mft), out var r);
        Assert.Equal(1, r.Records);
        Assert.Equal(0, r.MalformedRecords);
    }

    [Fact]
    public void AF08_is_detected_with_a_candidate_not_detected_without_one_and_undetermined_without_an_MFT()
    {
        var with = Parse(Write(Sample()), out _);
        Assert.Equal(AntiForensicResult.Detected, AntiForensics.Evaluate(with, []).Single(c => c.Id == "AF08").Result);
        byte[] clean = [.. Record(5, 5, true, true, Same(T2026), new Fn(5, 5, ".", 3, T2026))];
        Assert.Equal(AntiForensicResult.NotDetected, AntiForensics.Evaluate(Parse(Write(clean, "clean"), out _), []).Single(c => c.Id == "AF08").Result);
        Assert.Equal(AntiForensicResult.Undetermined, AntiForensics.Evaluate([], []).Single(c => c.Id == "AF08").Result);
    }

    private static byte[] Rec(uint number, ushort seq, bool inUse, bool dir, string name, long parent, ushort parentSeq, int size = 1024, int usaOffset = 0x30) =>
        RecordOf(number, seq, inUse, dir, Same(T2026), [new Fn(parent, parentSeq, name, 1, T2026)], size, usaOffset);

    private static byte[] Root(int size = 1024, int usaOffset = 0x30, uint number = 5) =>
        RecordOf(number, 5, true, true, Same(T2026), [new Fn(number, 5, ".", 3, T2026)], size, usaOffset);

    [Theory]
    [InlineData((ushort)3, (ushort)2)]          // a freed folder shows seq + 1
    [InlineData((ushort)0, (ushort)65535)]      // wrap-around of the 16-bit sequence number
    public void A_deleted_child_under_a_deleted_folder_gets_the_full_path_because_freeing_increments_the_folder_sequence(ushort folderNow, ushort childRefersTo)
    {
        byte[] mft = [.. Root(), .. Rec(40, folderNow, false, true, "Proiect", 5, 5), .. Rec(41, 1, false, false, "plan.txt", 40, childRefersTo)];
        var ev = Parse(Write(mft), out _);
        Assert.Equal(@"\Proiect\plan.txt", Assert.Single(ev, e => e.Fields["Kind"] == "deleted" && e.Fields["Record"] == "41").Path);
    }

    [Fact]
    public void A_live_folder_whose_sequence_is_one_higher_is_a_reused_record_not_the_parent()
    {
        byte[] mft = [.. Root(), .. Rec(40, 3, true, true, "Altceva", 5, 5), .. Rec(41, 1, false, false, "plan.txt", 40, 2)];
        Assert.Equal(@"…\plan.txt", Assert.Single(Parse(Write(mft), out _), e => e.Fields["Kind"] == "deleted").Path);
    }

    [Fact]
    public void A_parent_cycle_in_a_damaged_table_yields_a_single_ellipsis_prefix()
    {
        byte[] mft = [.. Root(), .. Rec(40, 1, true, true, "a", 41, 1), .. Rec(41, 1, false, false, "b.txt", 40, 1)];
        var d = Assert.Single(Parse(Write(mft), out _), e => e.Fields["Kind"] == "deleted");
        Assert.Equal(@"…\a\b.txt", d.Path);
        Assert.Equal(1, d.Path.Count(c => c == '…'));
    }

    [Fact]
    public void Records_of_4096_bytes_are_read_with_eight_sectors_in_the_update_sequence()
    {
        byte[] mft = [.. Root(4096), .. Rec(40, 2, true, true, "Docs", 5, 5, 4096), .. Rec(41, 3, false, false, "secret.docx", 40, 2, 4096)];
        var ev = Parse(Write(mft), out var r);
        Assert.Equal(3, r.Records);
        Assert.Equal(0, r.MalformedRecords);
        Assert.Equal("4096", Assert.Single(ev, e => e.Fields["Kind"] == "summary").Fields["RecordSize"]);
        Assert.Equal(@"\Docs\secret.docx", Assert.Single(ev, e => e.Fields["Kind"] == "deleted").Path);
    }

    [Fact]
    public void A_record_with_the_older_layout_has_no_record_number_and_takes_it_from_its_position()
    {
        // update sequence offset 0x2A: the header ends before 0x2C, so the number is the position (0 = root, 1 = Docs, 2 = the deleted file)
        byte[] mft = [.. Root(usaOffset: 0x2A, number: 0), .. Rec(1, 2, true, true, "Docs", 0, 5, usaOffset: 0x2A), .. Rec(2, 3, false, false, "secret.docx", 1, 2, usaOffset: 0x2A)];
        var d = Assert.Single(Parse(Write(mft), out var r), e => e.Fields["Kind"] == "deleted");
        Assert.Equal(3, r.Records);
        Assert.Equal("2", d.Fields["Record"]);
        Assert.Equal(@"\Docs\secret.docx", d.Path);
    }

    private AntiForensicCheck Af08(string name, params byte[][] records) =>
        AntiForensics.Evaluate(Parse(Write([.. records.SelectMany(r => r)], name), out _), []).Single(c => c.Id == "AF08");

    [Fact]
    public void AF08_is_undetermined_when_no_record_could_be_read_or_more_than_a_fifth_are_damaged()
    {
        byte[] Torn() { var t = Rec(41, 1, true, false, "x.txt", 5, 5); t[510] ^= 0xFF; return t; }
        var none = Af08("af08a", Torn());   // 0 readable records, 1 damaged
        Assert.Equal(AntiForensicResult.Undetermined, none.Result);
        Assert.Contains("0 înregistrări citite, 1 deteriorate", none.Reason, StringComparison.Ordinal);
        Assert.Equal(AntiForensicResult.Undetermined, Af08("af08b", Root(), Torn()).Result);   // 1 of 2 damaged
        Assert.Equal(AntiForensicResult.NotDetected, Af08("af08c", Root(), Rec(40, 1, true, false, "a", 5, 5), Rec(42, 1, true, false, "b", 5, 5),
                                                          Rec(43, 1, true, false, "c", 5, 5), Rec(44, 1, true, false, "d", 5, 5), Torn()).Result);   // 1 of 6 is under the limit
    }
}
