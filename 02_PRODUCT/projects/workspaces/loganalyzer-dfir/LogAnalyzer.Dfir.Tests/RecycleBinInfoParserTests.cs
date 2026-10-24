using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Recycle Bin $I (E7): v1 and v2 exact values, the $R presence rule, hostile files, and selection by content.</summary>
public sealed class RecycleBinInfoParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_rbin_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Deleted = new(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);
    private const string Original = @"C:\Users\test\Documents\plan.docx";

    public RecycleBinInfoParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path, string type = "recycle_info", string original = "") =>
        new() { EvidenceId = "EV-I", CaseId = "C", Source = type, SourceType = type, StoredPath = path, OriginalPath = original };

    private static byte[] V1(string path, long size = 1234567, long? fileTime = null)
    {
        var d = new byte[RecycleBinInfoParser.V1Length];
        BinaryPrimitives.WriteInt64LittleEndian(d, 1);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16), fileTime ?? Deleted.ToFileTimeUtc());
        Encoding.Unicode.GetBytes(path).CopyTo(d, 24);
        return d;
    }

    private static byte[] V2(string path, long size = 7654321, long? fileTime = null, int? declared = null)
    {
        var text = Encoding.Unicode.GetBytes(path + "\0");
        var d = new byte[28 + text.Length];
        BinaryPrimitives.WriteInt64LittleEndian(d, 2);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16), fileTime ?? Deleted.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), declared ?? path.Length + 1);
        text.CopyTo(d, 28);
        return d;
    }

    private string Write(string name, byte[] data)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, data);
        return p;
    }

    private static (ParseResult R, ListSink S) Run(string path, string original = "")
    {
        var sink = new ListSink();
        var r = new RecycleBinInfoParser().Parse(Item(path, original: original), path, sink, default);
        return (r, sink);
    }

    [Fact]
    public void Version_1_gives_deletion_time_original_path_and_size()
    {
        var p = Write("$IAB12CD.docx", V1(Original));
        var (r, s) = Run(p);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        var e = Assert.Single(s.Events);
        Assert.Equal(new DateTimeOffset(Deleted), e.Time.Utc);
        Assert.Contains("deletion time", e.TimeSemantics);
        Assert.Equal(Original, e.Path);
        Assert.Equal("1234567", e.Fields["OriginalSize"]);
        Assert.Equal("1", e.Fields["FormatVersion"]);
        Assert.Contains(Original, e.Summary);
    }

    [Fact]
    public void Version_2_gives_the_same_fields_with_the_stored_length()
    {
        var p = Write("$IXY98ZW.xlsx", V2(@"D:\Arhiva\buget ăâîșț.xlsx"));
        var (r, s) = Run(p);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        var e = Assert.Single(s.Events);
        Assert.Equal(@"D:\Arhiva\buget ăâîșț.xlsx", e.Path);
        Assert.Equal("7654321", e.Fields["OriginalSize"]);
        Assert.Equal("2", e.Fields["FormatVersion"]);
        Assert.Equal(new DateTimeOffset(Deleted), e.Time.Utc);
    }

    [Fact]
    public void Missing_R_file_is_reported_as_content_emptied_or_removed_and_a_present_one_as_present()
    {
        var p = Write("$IAAA111.docx", V2(Original));
        var (_, missing) = Run(p);
        var e = Assert.Single(missing.Events);
        Assert.Equal("absent", e.Fields["RContent"]);
        Assert.Contains("conținut golit sau eliminat", e.Summary);
        Assert.DoesNotContain("nu a existat", e.Summary);

        File.WriteAllText(Path.Combine(_dir, "$RAAA111.docx"), "x");
        var (_, present) = Run(p);
        var f = Assert.Single(present.Events);
        Assert.Equal("present", f.Fields["RContent"]);
        Assert.DoesNotContain("golit", f.Summary);
    }

    [Fact]
    public void A_stored_copy_that_lost_its_I_name_does_not_claim_the_content_is_gone()
    {
        var p = Write("EV-0007.bin", V2(Original));
        var (_, s) = Run(p);
        Assert.Equal("not_checked", Assert.Single(s.Events).Fields["RContent"]);
    }

    [Fact]
    public void Owner_sid_comes_from_the_recycle_bin_folder_of_the_original_path()
    {
        var p = Write("$IOWN001.txt", V2(Original));
        var (_, s) = Run(p, @"C:\$Recycle.Bin\S-1-5-21-1111-2222-3333-1001\$IOWN001.txt");
        Assert.Equal("S-1-5-21-1111-2222-3333-1001", Assert.Single(s.Events).Fields["OwnerSid"]);
    }

    [Fact]
    public void Unusable_filetime_is_an_unknown_time_with_the_raw_value_kept()
    {
        foreach (var ft in new long[] { 0, -5, long.MaxValue })
        {
            var p = Write($"$IFT{(ft < 0 ? "N" : "P")}{ft.ToString().Length}.txt", V2(Original, fileTime: ft));
            var (r, s) = Run(p);
            var e = Assert.Single(s.Events);
            Assert.False(e.Time.IsKnown);
            Assert.Equal(ft.ToString(), e.Time.Raw);
            Assert.Equal(EvidenceStatus.Success, r.Status);
        }
    }

    [Fact]
    public void Negative_size_is_reported_as_unknown()
    {
        var p = Write("$INEG001.txt", V2(Original, size: -1));
        var (_, s) = Run(p);
        Assert.Contains("dimensiune necunoscută", Assert.Single(s.Events).Summary);
    }

    [Theory]
    [InlineData("v2_negative_count", -5)]
    [InlineData("v2_zero_count", 0)]
    [InlineData("v2_beyond_file", 4000)]
    [InlineData("v2_int_max", int.MaxValue)]
    public void Hostile_v2_name_length_is_a_reported_gap_never_an_exception(string name, int declared)
    {
        var p = Write("$I" + name + ".txt", V2(Original, declared: declared));
        var (r, s) = Run(p);
        Assert.Empty(s.Events);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.NotEmpty(r.Gaps);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public void Truncated_unknown_version_and_tiny_files_are_reported_gaps()
    {
        var cases = new Dictionary<string, byte[]>
        {
            ["$Ishort1.txt"] = V1(Original).Take(100).ToArray(),
            ["$Ishort2.txt"] = V2(Original).Take(27).ToArray(),
            ["$Itiny.txt"] = [1, 0, 0, 0],
            ["$Iempty.txt"] = [],
        };
        var v3 = V2(Original); BinaryPrimitives.WriteInt64LittleEndian(v3, 3); cases["$Iv3.txt"] = v3;
        var v0 = V2(Original); BinaryPrimitives.WriteInt64LittleEndian(v0, 0); cases["$Iv0.txt"] = v0;
        foreach (var (name, data) in cases)
        {
            var (r, s) = Run(Write(name, data));
            Assert.Empty(s.Events);
            Assert.Equal(EvidenceStatus.Failed, r.Status);
            Assert.NotEmpty(r.Gaps);
        }
    }

    [Fact]
    public void File_over_the_input_limit_is_refused_before_reading()
    {
        var big = V2(Original); Array.Resize(ref big, (int)RecycleBinInfoParser.MaxInfoFileBytes + 1);
        var (r, s) = Run(Write("$Ibig.txt", big));
        Assert.Empty(s.Events);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains(EvidenceParserBase.TooLargeReason, r.Error);
    }

    // ---- selection by content ----

    [Fact]
    public void Registry_selects_the_parser_by_content_for_declared_type_and_for_the_I_name()
    {
        var p = Write("$ISEL001.docx", V2(Original));
        var byType = WindowsParsers.Registry.Select(Item(p), p);
        Assert.Equal("recycle_info", byType.Preflight!.Fingerprint);
        Assert.Contains(byType.Parsers, x => x.Descriptor.ParserId == "RecycleBinInfoParser");

        // A "$I" name alone is not a selector: an item typed "file" gets no candidate (skipped by design), not a failed preflight.
        Assert.Empty(WindowsParsers.Registry.Candidates(Item(p, "file")));
        var notInfo = Write("$Ijust.txt", Encoding.UTF8.GetBytes("text oarecare"));
        Assert.Empty(WindowsParsers.Registry.Select(Item(notInfo, "file"), notInfo).Parsers);
        Assert.Null(WindowsParsers.Registry.Select(Item(notInfo, "file"), notInfo).Preflight);
    }

    [Fact]
    public void Detection_does_not_take_unrelated_files_for_recycle_info()
    {
        var text = Write("$Itext.txt", Encoding.UTF8.GetBytes("nu este un fișier Coș, doar text de aproximativ aceeași lungime"));
        Assert.NotEqual("recycle_info", EvidenceFingerprint.Detect(text));
        var wrongType = WindowsParsers.Registry.Select(Item(text), text);
        Assert.Empty(wrongType.Parsers);
        Assert.Equal(PreflightStatus.FormatMismatch, wrongType.Preflight!.Status);

        var zeros = Write("$Izeros.bin", new byte[544]);
        Assert.NotEqual("recycle_info", EvidenceFingerprint.Detect(zeros));
        var renamed = Write("copie.bin", V1(Original));      // no $I name: the path-like text field decides
        Assert.Equal("recycle_info", EvidenceFingerprint.Detect(renamed));
        var noPath = Write("copie2.bin", V1("fara cale"));
        Assert.NotEqual("recycle_info", EvidenceFingerprint.Detect(noPath));
    }

    [Fact]
    public void Import_type_recognises_I_files_by_name_and_content_only()
    {
        var good = Write("$IIMP001.docx", V2(Original));
        Assert.Equal("recycle_info", InvestigationPipeline.ImportType(good));
        var notInfo = Write("$Iwhatever.txt", Encoding.UTF8.GetBytes("just text"));
        Assert.Equal("file", InvestigationPipeline.ImportType(notInfo));
    }

    [Theory]
    [InlineData("$IAB12CD.lnk")]
    [InlineData("$IAB12CD.evtx")]
    [InlineData("$IAB12CD.pf")]
    [InlineData("$IAB12CD.automaticDestinations-ms")]
    public void Import_type_keeps_the_deletion_record_whatever_the_original_extension(string name)
    {
        var p = Write(name, V2(Original));
        Assert.Equal("recycle_info", InvestigationPipeline.ImportType(p));
        var sel = WindowsParsers.Registry.Select(Item(p, "recycle_info"), p);
        Assert.Contains(sel.Parsers, x => x.Descriptor.ParserId == "RecycleBinInfoParser");
    }

    [Fact]
    public void Import_type_keeps_the_extension_type_for_files_not_named_I()
    {
        // A real shortcut and a real event log that are not Recycle Bin records keep their type (no regression).
        var lnk = Write("document.lnk", new byte[] { 0x4C, 0, 0, 0, 0x01, 0x14, 0x02, 0, 0, 0, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46 });
        Assert.Equal("lnk", InvestigationPipeline.ImportType(lnk));
        var evtx = Write("Security.evtx", Encoding.ASCII.GetBytes("ElfFile\0").Concat(new byte[4096]).ToArray());
        Assert.Equal("evtx", InvestigationPipeline.ImportType(evtx));
        // Even a $I-named file that is not a deletion record is typed by its extension.
        var fake = Write("$Inotrecord.evtx", Encoding.ASCII.GetBytes("ElfFile\0").Concat(new byte[4096]).ToArray());
        Assert.Equal("evtx", InvestigationPipeline.ImportType(fake));
    }

    [Fact]
    public void Descriptor_is_honest_about_its_limits()
    {
        var d = new RecycleBinInfoParser().Descriptor;
        Assert.Equal(ParserMaturity.Tested, d.Status);
        Assert.Contains(d.Limitations, l => l.Contains("golit sau eliminat"));
        Assert.True(d.MaxInputBytes > 0);
    }
}
