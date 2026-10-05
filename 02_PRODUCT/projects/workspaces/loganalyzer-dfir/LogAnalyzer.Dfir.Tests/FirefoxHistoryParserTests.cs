using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Firefox places.sqlite: synthetic database with exact values, and the real profile against Python's sqlite3.</summary>
public sealed class FirefoxHistoryParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_ff_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Visit = new(2026, 9, 19, 14, 50, 0, DateTimeKind.Utc);

    public FirefoxHistoryParserTests() { Directory.CreateDirectory(_dir); SQLitePCL.Batteries_V2.Init(); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-FF", CaseId = "C", Source = "ff", SourceType = "firefox_places", StoredPath = path };
    private static long Micros(DateTime t) => (t - DateTime.UnixEpoch).Ticks / 10;

    [Fact]
    public void Visits_and_downloads_are_read_with_exact_values()
    {
        var p = Path.Combine(_dir, "places.sqlite");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $$"""
                CREATE TABLE moz_places(id INTEGER PRIMARY KEY, url TEXT, title TEXT);
                CREATE TABLE moz_historyvisits(id INTEGER PRIMARY KEY, from_visit INTEGER, place_id INTEGER, visit_date INTEGER, visit_type INTEGER);
                CREATE TABLE moz_anno_attributes(id INTEGER PRIMARY KEY, name TEXT);
                CREATE TABLE moz_annos(id INTEGER PRIMARY KEY, place_id INTEGER, anno_attribute_id INTEGER, content TEXT, dateAdded INTEGER);
                INSERT INTO moz_places VALUES (1, 'https://mailjilq.tzd4is.cyou/', 'x');
                INSERT INTO moz_historyvisits VALUES (5, 0, 1, {{Micros(Visit)}}, 1);
                INSERT INTO moz_anno_attributes VALUES (1, 'downloads/destinationFileURI'), (2, 'downloads/metaData');
                INSERT INTO moz_annos VALUES (1, 1, 1, 'file:///C:/Users/u/Downloads/Tool_302044.zip', {{Micros(Visit.AddSeconds(30))}});
                INSERT INTO moz_annos VALUES (2, 1, 2, '{"state":1,"deleted":false,"endTime":{{(long)(Visit.AddSeconds(90) - DateTime.UnixEpoch).TotalMilliseconds}},"fileSize":813546747}', {{Micros(Visit.AddSeconds(90))}});
                """;
            cmd.ExecuteNonQuery();
        }
        var sink = new ListSink();
        var r = new FirefoxHistoryParser().Parse(Item(p), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        var v = Assert.Single(sink.Events, e => e.Source == "BrowserVisit");
        Assert.Equal(new DateTimeOffset(Visit), v.Time.Utc);
        Assert.Equal("mailjilq.tzd4is.cyou", v.Dns);
        var d = Assert.Single(sink.Events, e => e.Source == "BrowserDownload");
        Assert.Equal(@"C:\Users\u\Downloads\Tool_302044.zip", d.Path);
        Assert.Equal(new DateTimeOffset(Visit.AddSeconds(30)), d.Time.Utc);
        Assert.Equal("813546747", d.Fields["ReceivedBytes"]);
        Assert.Equal("1", d.Fields["State"]);
        Assert.Equal(Visit.AddSeconds(90).ToString("o"), d.Fields["EndTimeUtc"]);
    }

    private const string Reference = """
        import sqlite3, sys, shutil, tempfile, os
        d = tempfile.mkdtemp(); dst = os.path.join(d, "places.sqlite"); shutil.copy(sys.argv[1], dst)
        c = sqlite3.connect("file:" + dst + "?mode=ro", uri=True)
        for vid, vd, url in c.execute("select v.id, v.visit_date, p.url from moz_historyvisits v left join moz_places p on p.id = v.place_id"):
            print("V\t%d\t%d\t%s" % (vid, vd or 0, url or ""))
        for aid, content, added in c.execute("select n.id, n.content, n.dateAdded from moz_annos n join moz_anno_attributes a on a.id = n.anno_attribute_id where a.name = 'downloads/destinationFileURI'"):
            print("D\t%d\t%d\t%s" % (aid, added or 0, content))
        c.close(); shutil.rmtree(d)
        """;

    [DifferentialFact("firefoxPlaces")]
    public void Real_places_match_python_sqlite3()
    {
        var f = Corpus.File("firefoxPlaces");
        var sink = new ListSink();
        Assert.Equal(EvidenceStatus.Success, new FirefoxHistoryParser().Parse(Item(f), f, sink, default).Status);
        var reference = Differential.RunPython(Reference, f);

        var refVisits = reference.Where(l => l.StartsWith("V\t")).Select(l => l.Split('\t')).ToList();
        var refDownloads = reference.Where(l => l.StartsWith("D\t")).Select(l => l.Split('\t')).ToList();
        Assert.NotEmpty(refVisits);
        Assert.Equal(refVisits.Count, sink.Events.Count(e => e.Source == "BrowserVisit"));
        foreach (var v in refVisits)
        {
            var e = Assert.Single(sink.Events, x => x.Locator == $"moz_historyvisits.id={v[1]}");
            Assert.Equal(v[2], e.Time.Raw);
            Assert.Equal(v[3], e.Fields["Url"]);
        }
        Assert.Equal(refDownloads.Count, sink.Events.Count(e => e.Source == "BrowserDownload"));
        foreach (var d in refDownloads)
        {
            var e = Assert.Single(sink.Events, x => x.Locator == $"moz_annos.id={d[1]}");
            Assert.Equal(d[2], e.Time.Raw);
            Assert.Equal(d[3], e.Fields["DestinationUri"]);
        }
    }
}
