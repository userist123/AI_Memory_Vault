using System.ComponentModel;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The USN of the system volume next to $MFT / $I30. The journal read is ALWAYS injected: no test reads the journal of the station it runs on.</summary>
public sealed partial class NtfsMetadataCollectorTests
{
    private static readonly DateTimeOffset UsnT = new DateTimeOffset(2026, 11, 14, 9, 59, 30, TimeSpan.Zero).AddTicks(1234567);
    private static UsnRecord Rec(long usn, string name, uint reason, DateTimeOffset t, ulong fileId = 0x0001000000000064) =>
        new(usn, name, reason, t, 0x20, fileId, 0x0005000000000005, 0, 0, 2, 0, 80);

    private static UsnJournalRead Journal(string incomplete = "") =>
        new(new UsnJournalInfo(0x01dd6eca2b00544f, 1000, 5000),
            [Rec(1000, "victim.docx", 0x80000102, UsnT),                 // Data extend | File create | Close
             Rec(1200, "victim.docx", 0x80008000, UsnT.AddSeconds(2)),   // separate "Basic info change" (timestomp type A)
             Rec(1400, "other.txt", 0x80000002, UsnT.AddSeconds(3), 0x0001000000000065)], incomplete);

    private (CollectorOutcome O, CaseWorkspace Ws, List<char> Calls) RunUsn(Func<char, CancellationToken, UsnJournalRead> read, string root = @"C:\")
    {
        var snap = new FakeShadow();
        var calls = new List<char>();
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_" + Guid.NewGuid().ToString("N")), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => NtfsVolumeReaderTests.OpenImage(), root, () => new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
                                          _ => [BS + "many", BS + "docs"], (l, ct) => { calls.Add(l); return read(l, ct); });
        return (c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default), ws, calls);
    }

    [Fact]
    public void Usn_csv_lands_in_the_run_folder_is_parsed_with_the_exact_utc_time_and_the_manifest_has_the_usn_fields()
    {
        var (o, ws, calls) = RunUsn((_, _) => Journal());
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Errors);
        Assert.Equal(['C'], calls);
        var mft = Assert.Single(o.Evidence, e => e.SourceType == MftParser.SourceTypeName);
        var usn = Assert.Single(o.Evidence, e => e.SourceType == "usn_journal");
        var folder = Path.GetDirectoryName(Stored(ws, mft));
        Assert.Equal(folder, Path.GetDirectoryName(Stored(ws, usn)));
        Assert.Equal("usn_C.csv", Path.GetFileName(Stored(ws, usn)));
        Assert.Equal(TimeZoneInfo.Local.Id, usn.Timezone);
        foreach (var n in new[] { "$MFT", "$Boot", "manifest.json" }) Assert.True(File.Exists(Path.Combine(folder!, n)), n);
        Assert.Equal(2, Directory.GetFiles(folder!, "*.i30").Length);

        var sink = new ListSink();
        var r = new UsnJournalParser().Parse(usn, Stored(ws, usn), sink, default);
        Assert.Equal(3, r.Records);
        var create = Assert.Single(sink.Events, e => e.Fields["Usn"] == "1000");
        Assert.Equal(UsnT, create.Time.Utc);                                   // 100 ns exact, not the fsutil local second
        Assert.Contains("citit direct", create.Time.ConversionMethod);
        Assert.Contains(sink.Events, e => e.Fields["Usn"] == "1200" && e.Fields["Reason"].Contains("Basic info change") && !e.Fields["Reason"].Contains("File create"));

        var man = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        using var doc = JsonDocument.Parse(File.ReadAllText(Stored(ws, man)));
        var u = doc.RootElement.GetProperty("usn");
        Assert.Equal("Read", u.GetProperty("status").GetString());
        Assert.Equal("C", u.GetProperty("letter").GetString());
        Assert.Equal("0x01dd6eca2b00544f", u.GetProperty("journalId").GetString());
        Assert.Equal(1000, u.GetProperty("firstUsn").GetInt64());
        Assert.Equal(5000, u.GetProperty("nextUsn").GetInt64());
        Assert.Equal(3, u.GetProperty("records").GetInt32());
        Assert.False(u.GetProperty("incomplete").GetBoolean());
        Assert.Equal("2026-11-14T10:00:00.000Z", u.GetProperty("readUtc").GetString());
        Assert.Equal("usn_C.csv", u.GetProperty("file").GetString());
        Assert.Empty(doc.RootElement.GetProperty("gaps").EnumerateArray());
    }

    [Fact]
    public void A_failed_journal_read_makes_the_run_partial_not_failed_and_keeps_mft_and_i30()
    {
        var (o, ws, _) = RunUsn((_, _) => throw new Win32Exception(1179));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Errors, e => e.Contains("USN") && e.Contains("jurnal USN activ"));
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "usn_journal");
        Assert.Contains(o.Evidence, e => e.SourceType == MftParser.SourceTypeName);
        Assert.Equal(2, o.Evidence.Count(e => e.SourceType == I30Parser.SourceTypeName));
        var man = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        using var doc = JsonDocument.Parse(File.ReadAllText(Stored(ws, man)));
        Assert.Equal("Error", doc.RootElement.GetProperty("usn").GetProperty("status").GetString());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Stored(ws, man))!, "usn_*"));
    }

    [Fact]
    public void Access_denied_is_also_only_a_run_problem()
    {
        var (o, _, _) = RunUsn((_, _) => throw new UnauthorizedAccessException("acces refuzat"));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Errors, e => e.Contains("acces refuzat"));
    }

    [Fact]
    public void A_capped_journal_is_kept_stated_incomplete_in_the_manifest_as_a_gap_and_partial()
    {
        const string why = "citirea s-a oprit la limita de 2000000 înregistrări";
        var (o, ws, _) = RunUsn((_, _) => Journal(why));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains(o.Evidence, e => e.SourceType == "usn_journal");
        var man = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        using var doc = JsonDocument.Parse(File.ReadAllText(Stored(ws, man)));
        var u = doc.RootElement.GetProperty("usn");
        Assert.Equal("Incomplete", u.GetProperty("status").GetString());
        Assert.True(u.GetProperty("incomplete").GetBoolean());
        Assert.Contains(why, u.GetProperty("incompleteReason").GetString());
        Assert.Contains(doc.RootElement.GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains(why));
    }

    [Fact]
    public void The_letter_comes_from_the_volume_root()
    {
        var (_, _, calls) = RunUsn((_, _) => Journal(), @"D:\");
        Assert.Equal(['D'], calls);
    }

    [Fact]
    public void Without_an_injected_reader_the_journal_is_not_read_and_the_manifest_has_no_usn_block()
    {
        var (o, ws, _, _) = Run();
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "usn_journal");
        var man = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        using var doc = JsonDocument.Parse(File.ReadAllText(Stored(ws, man)));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("usn").ValueKind);
    }
}
