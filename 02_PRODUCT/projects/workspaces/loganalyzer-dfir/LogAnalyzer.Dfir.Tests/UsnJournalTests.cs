using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public sealed class UsnJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_usn_" + Guid.NewGuid().ToString("N"));
    public UsnJournalTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");

    private const string Header = """
        USN Journal ID    : 0x01dc84b0d77fa447
        First USN         : 100
        Next USN          : 900
        Start USN         : 0
        Min major version : Supported=2, requested=2
        Max major version : Supported=4, requested=4

        Usn,File name,File name length,Reason #,Reason,Time stamp,File attributes #,File attributes,File ID,Parent file ID,Source info #,Source info,Security ID,Major version,Minor version,Record length
        """;

    private static string Row(long usn, string name, string reason, string time, string id, string parent, string attr = "Archive") =>
        $"{usn},\"{name}\",{name.Length * 2},0x00000000,\"{reason}\",\"{time}\",0x00000020,\"{attr}\",{id},{parent},0x00000000,\"*NONE*\",0,3,0,96";

    private string Write(string name, params string[] rows)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, Header + "\n" + string.Join("\n", rows) + "\n", new UTF8Encoding(false));
        return p;
    }

    private static List<TimelineEvent> Parse(string path, TimeZoneInfo zone, out ParseResult r)
    {
        var sink = new ListSink();
        r = new UsnJournalParser(zone).Parse(new EvidenceItem { EvidenceId = "EV-U", CaseId = "C", Source = "usn", SourceType = "usn_journal", StoredPath = path }, path, sink, default);
        return sink.Events;
    }

    [Fact]
    public void Closed_operations_become_events_with_local_time_converted_and_the_raw_text_kept()
    {
        var p = Write("usn.csv",
            Row(100, "Prefetch", "Close", "03-Oct-26 16:00:00", "D1", "ROOT", "Directory"),
            Row(200, "A.EXE-1.pf", "File create", "03-Oct-26 16:54:27", "F1", "D1"),
            Row(300, "A.EXE-1.pf", "File create | Close", "03-Oct-26 16:54:27", "F1", "D1"),
            Row(400, "old.log", "File delete | Close", "13-Jan-26 19:00:00", "F2", "D1"));
        var ev = Parse(p, Bucharest, out var r);
        Assert.Equal((EvidenceStatus.Success, 3), (r.Status, r.Records));          // the record without Close is folded into its Close
        var pf = ev.Single(e => e.Fields["FileName"] == "A.EXE-1.pf");
        Assert.Equal("2026-10-03T13:54:27.000Z", pf.Time.UtcIso);                 // October: EEST, UTC+3
        Assert.Equal("03-Oct-26 16:54:27", pf.Time.Raw);
        Assert.Contains("GTB Standard Time", pf.Time.ConversionMethod);
        Assert.Equal("2026-01-13T17:00:00.000Z", ev.Single(e => e.Fields["FileName"] == "old.log").Time.UtcIso);   // January: EET, UTC+2
        Assert.Equal(@"…\Prefetch\A.EXE-1.pf", pf.Path);
        Assert.Equal(("USN=300", "0x01dc84b0d77fa447"), (pf.Locator, pf.Fields["JournalId"]));
    }

    [Fact]
    public void Journal_id_is_read_as_its_creation_time()
    {
        Assert.Equal("2026-01-13T17:19:57", UsnJournalParser.JournalCreatedUtc("0x01dc84b0d77fa447")!.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"));
        Assert.Null(UsnJournalParser.JournalCreatedUtc("0x0000000000000005"));
        Assert.Throws<InvalidDataException>(() => UsnJournalParser.ReadHeader(["USN Journal ID : 0x1", "no csv here"]));
    }

    [Fact]
    public void Exports_are_recognised_by_content_in_utf8_and_utf16()
    {
        var utf8 = Write("a.txt", Row(100, "x", "Close", "03-Oct-26 16:00:00", "F", "D"));
        Assert.Equal("usn_fsutil", EvidenceFingerprint.Detect(utf8));
        var utf16 = Path.Combine(_dir, "b.txt");
        File.WriteAllText(utf16, File.ReadAllText(utf8), Encoding.Unicode);
        Assert.Equal("usn_fsutil", EvidenceFingerprint.Detect(utf16));
        Assert.Single(Parse(utf16, Bucharest, out _));
    }

    private static TimelineEvent U(string name, string reason, int second, string journal = "0x01dc84b0d77fa447") => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 10, 3, 13, 54, second, DateTimeKind.Utc), "t", "test"), Source = "USN", EvidenceId = "EV-U",
        Locator = $"USN={name}{second}", Summary = "s", Fields = { ["FileName"] = name, ["Reason"] = reason, ["JournalId"] = journal },
    };

    [Fact]
    public void Prefetch_deletion_and_journal_recreation_are_read_from_the_journal()
    {
        var af = AntiForensics.Evaluate([U("A.pf", "File delete | Close", 27), U("B.pf", "File delete | Close", 27), U("A.pf", "File create | Close", 40)], []).ToDictionary(c => c.Id);
        Assert.Equal(AntiForensicResult.Detected, af["AF06"].Result);
        Assert.Contains("2 fișiere .pf șterse", af["AF06"].Reason);
        Assert.Contains("1 recreate ulterior", af["AF06"].Reason);
        Assert.Contains("2 în aceeași secundă, 2026-10-03T13:54:27Z", af["AF06"].Reason);
        Assert.Equal(AntiForensicResult.NotDetected, AntiForensics.Evaluate([U("x.txt", "Close", 1)], []).Single(c => c.Id == "AF06").Result);

        Assert.Equal(AntiForensicResult.Detected,
            AntiForensics.Evaluate([U("x", "Close", 1), U("y", "Close", 2, "0x01dc84b0d77fa448")], []).Single(c => c.Id == "AF07").Result);
        // A journal created after the oldest event log record was recreated (or the volume is new).
        var oldLog = new TimelineEvent { Time = Timestamp.FromUtc(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "t", "x"), Source = "EventLog:System", EvidenceId = "E", Summary = "s", Locator = "EventRecordID=1" };
        Assert.Equal(AntiForensicResult.Detected, AntiForensics.Evaluate([oldLog, U("x", "Close", 1)], []).Single(c => c.Id == "AF07").Result);
        Assert.Equal(AntiForensicResult.Undetermined, AntiForensics.Evaluate([], []).Single(c => c.Id == "AF07").Result);
    }

    // ---- real corpus ----

    [CorpusFact("usn")]
    public void Real_journal_exports_agree_and_their_times_match_prefetch_in_utc()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Corpus.S("usn", "zone"));
        var a = Parse(Corpus.File("usn"), zone, out var ra);
        var b = Parse(Path.Combine(Corpus.Root, Corpus.S("usn", "second")), zone, out var rb);
        Assert.Equal((EvidenceStatus.Success, EvidenceStatus.Success, 0, 0), (ra.Status, rb.Status, ra.MalformedRecords, rb.MalformedRecords));

        // Two exports made at different times: every USN they share describes the same operation.
        var bByUsn = b.ToDictionary(e => e.Fields["Usn"]);
        var common = a.Where(e => bByUsn.ContainsKey(e.Fields["Usn"])).ToList();
        Assert.True(common.Count > 50_000, $"{common.Count} USN comune");
        Assert.All(common, e =>
        {
            var o = bByUsn[e.Fields["Usn"]];
            Assert.Equal((e.Fields["FileName"], e.Fields["Reason"], e.Time.UtcIso, e.Fields["FileId"]), (o.Fields["FileName"], o.Fields["Reason"], o.Time.UtcIso, o.Fields["FileId"]));
        });

        // Converted times against an independent UTC clock: Prefetch run times (FILETIME) of the same .pf files.
        var writes = a.Where(e => e.Fields["FileName"].EndsWith(".pf", StringComparison.OrdinalIgnoreCase) && e.Fields["Reason"].Contains("Data"))
                      .GroupBy(e => e.Fields["FileName"], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(e => e.Time.Utc!.Value).ToList(), StringComparer.OrdinalIgnoreCase);
        int matched = 0;
        foreach (var f in Directory.GetFiles(Path.Combine(Corpus.Root, Corpus.S("usn", "prefetch")), "*.pf"))
        {
            if (!writes.TryGetValue(Path.GetFileName(f), out var times)) continue;
            var run = PrefetchParser.Read(f).RunTimesUtc[0];
            if (times.Any(t => (t - run).TotalSeconds is >= 0 and <= 60)) matched++;   // the .pf is written seconds after the run
        }
        Assert.True(matched >= 50, $"{matched} fișiere .pf scrise în USN în primul minut după rularea înregistrată (UTC)");

        // Journal ID = creation FILETIME, on the day Windows was installed (SOFTWARE\...\CurrentVersion\InstallTime).
        Assert.Equal(Corpus.S("usn", "journalId"), a[0].Fields["JournalId"]);
        var created = UsnJournalParser.JournalCreatedUtc(a[0].Fields["JournalId"])!.Value;
        Assert.Equal(Corpus.S("usn", "journalCreatedUtc"), created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"));
        using (var fs = File.OpenRead(Path.Combine(Corpus.Root, Corpus.S("usn", "softwareHive"))))
        {
            var install = DateTime.FromFileTimeUtc(BitConverter.ToInt64(new RawRegistry(fs).OpenKey(@"Microsoft\Windows NT\CurrentVersion")!.Value("InstallTime")!.Data));
            Assert.Equal(Corpus.S("usn", "installUtc"), install.ToString("yyyy-MM-ddTHH:mm:ss"));
            Assert.True(created.UtcDateTime < install && (install - created.UtcDateTime).TotalHours < 3);
        }

        // The bulk deletion of Prefetch files, the same in both exports.
        foreach (var ev in new[] { a, b })
        {
            var af = AntiForensics.Evaluate(ev, []).ToDictionary(c => c.Id);
            Assert.Equal(AntiForensicResult.Detected, af["AF06"].Result);
            Assert.Contains($"{Corpus.L("usn", "prefetchBulkDeleteCount")} în aceeași secundă, {Corpus.S("usn", "prefetchBulkDeleteUtc")}Z", af["AF06"].Reason);
            Assert.Equal(AntiForensicResult.NotDetected, af["AF07"].Result);
        }
    }
}
