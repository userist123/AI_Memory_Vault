using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Chromium History (Chrome / Edge): synthetic database with exact values, and the real profile against an earlier independent extraction.</summary>
public sealed class BrowserHistoryParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_hist_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Dl = new(2026, 9, 19, 14, 52, 42, DateTimeKind.Utc);

    public BrowserHistoryParserTests() { Directory.CreateDirectory(_dir); SQLitePCL.Batteries_V2.Init(); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-H", CaseId = "C", Source = "chromium_history", SourceType = "chromium_history", StoredPath = path };

    private static long Chrome(DateTime utc) => (utc.ToFileTimeUtc()) / 10;

    private string History()
    {
        var p = Path.Combine(_dir, "History");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE urls(id INTEGER PRIMARY KEY, url TEXT, title TEXT, visit_count INTEGER, typed_count INTEGER, last_visit_time INTEGER, hidden INTEGER);
                CREATE TABLE visits(id INTEGER PRIMARY KEY, url INTEGER, visit_time INTEGER, from_visit INTEGER, transition INTEGER, visit_duration INTEGER);
                CREATE TABLE downloads(id INTEGER PRIMARY KEY, guid TEXT, current_path TEXT, target_path TEXT, start_time INTEGER, received_bytes INTEGER,
                    total_bytes INTEGER, state INTEGER, danger_type INTEGER, interrupt_reason INTEGER, end_time INTEGER, opened INTEGER,
                    last_access_time INTEGER, referrer TEXT, tab_url TEXT, tab_referrer_url TEXT, mime_type TEXT, original_mime_type TEXT);
                CREATE TABLE downloads_url_chains(id INTEGER, chain_index INTEGER, url TEXT);
                INSERT INTO urls VALUES (1, 'https://mailjilq.tzd4is.cyou/', 'Download', 1, 0, {Chrome(Dl)}, 0);
                INSERT INTO urls VALUES (2, 'https://www.google.com/search?q=samfw', 'samfw - Google', 2, 0, {Chrome(Dl.AddMinutes(-3))}, 0);
                INSERT INTO visits VALUES (10, 2, {Chrome(Dl.AddMinutes(-3))}, 0, 805306369, 1500000);
                INSERT INTO visits VALUES (11, 1, {Chrome(Dl.AddSeconds(-20))}, 10, 805306368, 0);
                INSERT INTO visits VALUES (12, 1, 0, 0, 0, 0);
                INSERT INTO downloads VALUES (1, 'g-1', 'C:\Users\u\Downloads\Tool_302044.zip', 'C:\Users\u\Downloads\Tool_302044.zip',
                    {Chrome(Dl)}, 813546747, 813546747, 1, 0, 0, {Chrome(Dl.AddSeconds(65))}, 1, {Chrome(Dl.AddSeconds(66))},
                    '', 'https://mailjilq.tzd4is.cyou/', 'https://confirm-n7o0.tzd4is.cyou/', 'application/x-zip-compressed', 'application/x-zip-compressed');
                INSERT INTO downloads_url_chains VALUES (1, 0, 'https://cdn.example.invalid/a');
                INSERT INTO downloads_url_chains VALUES (1, 1, 'blob:https://130af8a83568f840a6ae55fd.192169503.com/909292a6');
                """;
            cmd.ExecuteNonQuery();
        }
        return p;
    }

    [Fact]
    public void Sqlite_is_recognised_by_content()
    {
        Assert.Equal("sqlite", EvidenceFingerprint.Detect(History()));
    }

    [Fact]
    public void Visits_and_downloads_are_read_with_exact_times_and_the_source_is_not_modified()
    {
        var p = History();
        var before = Hashing.Sha256File(p);
        var sink = new ListSink();
        var r = new BrowserHistoryParser().Parse(Item(p), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(before, Hashing.Sha256File(p));
        Assert.False(File.Exists(p + "-journal"));

        var visits = sink.Events.Where(e => e.Source == "BrowserVisit").ToList();
        Assert.Equal(3, visits.Count);
        var v = Assert.Single(visits, e => e.Locator == "visits.id=11");
        Assert.Equal(new DateTimeOffset(Dl.AddSeconds(-20)), v.Time.Utc);
        Assert.Equal("https://mailjilq.tzd4is.cyou/", v.Fields["Url"]);
        Assert.Equal("mailjilq.tzd4is.cyou", v.Dns);
        Assert.Equal("0", v.Fields["TransitionCore"]);
        Assert.Equal("10", v.Fields["FromVisit"]);
        Assert.Null(Assert.Single(visits, e => e.Locator == "visits.id=12").Time.Utc);   // visit_time 0 = unknown, never "now"
        Assert.Equal("1", Assert.Single(visits, e => e.Locator == "visits.id=10").Fields["TransitionCore"]);

        var d = Assert.Single(sink.Events, e => e.Source == "BrowserDownload");
        Assert.Equal(new DateTimeOffset(Dl), d.Time.Utc);
        Assert.Equal(@"C:\Users\u\Downloads\Tool_302044.zip", d.Path);
        Assert.Equal("813546747", d.Fields["ReceivedBytes"]);
        Assert.Equal("https://mailjilq.tzd4is.cyou/", d.Fields["TabUrl"]);
        Assert.Equal("https://cdn.example.invalid/a -> blob:https://130af8a83568f840a6ae55fd.192169503.com/909292a6", d.Fields["UrlChain"]);
        Assert.Equal(Dl.AddSeconds(65).ToString("o", CultureInfo.InvariantCulture), d.Fields["EndTimeUtc"]);
        Assert.Equal("complete", d.Fields["State"]);
        Assert.Equal("downloads.id=1", d.Locator);
    }

    [Fact]
    public void Not_a_history_database_fails_instead_of_returning_empty()
    {
        var p = Path.Combine(_dir, "History");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE other(x INTEGER);";
            cmd.ExecuteNonQuery();
        }
        var r = new BrowserHistoryParser().Parse(Item(p), p, new ListSink(), default);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains("visits", r.Error);
    }

    [Fact]
    public void Download_followed_by_execution_from_the_same_folder_is_correlated()
    {
        var p = History();
        var sink = new ListSink();
        new BrowserHistoryParser().Parse(Item(p), p, sink, default);
        var exec = new TimelineEvent
        {
            Time = Timestamp.FromUtc(Dl.AddMinutes(4), "", "test"), Source = "Prefetch", EvidenceId = "EV-PF", Locator = "pf", Process = "SETUP.EXE",
            Path = @"\VOLUME{01d}\USERS\U\DOWNLOADS\TOOL_302044\SETUP.EXE", Summary = "run",
            Fields = { ["ReferencedFiles"] = @"\VOLUME{01d}\USERS\U\DOWNLOADS\TOOL_302044\SETUP.EXE", ["RunCount"] = "4" },
        };
        var late = new TimelineEvent
        {
            Time = Timestamp.FromUtc(Dl.AddDays(2), "", "test"), Source = "Prefetch", EvidenceId = "EV-PF2", Locator = "pf2", Process = "OTHER.EXE",
            Path = @"\VOLUME{01d}\USERS\U\DOWNLOADS\OTHER.EXE", Summary = "run",
            Fields = { ["ReferencedFiles"] = @"\VOLUME{01d}\USERS\U\DOWNLOADS\OTHER.EXE", ["RunCount"] = "1" },
        };
        var findings = Correlation.Run(sink.Events.Append(exec).Append(late).ToList());

        var f = Assert.Single(findings, x => x.RuleId == "DOWNLOAD-THEN-EXEC");
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Equal(Confidence.High, f.Confidence);               // the archive's number 302044 is in the program's folder
        Assert.Equal("T1204.002", f.MitreTechniqueId);
        Assert.Contains("tzd4is.cyou", f.Description);
        Assert.Equal(["EV-H", "EV-PF"], f.SupportingEvidence.Select(x => x.EvidenceId).ToArray());
    }

    /// <summary>
    /// The real Default profile History against the timeline extracted during the manual investigation (a separate script,
    /// 18–20.09.2026 UTC): every download and every visit in that window must be identical.
    /// </summary>
    [CorpusFact("chromeHistory")]
    public void Real_history_matches_the_independent_extraction()
    {
        var f = Corpus.File("chromeHistory");
        var sink = new ListSink();
        var r = new BrowserHistoryParser().Parse(Item(f), f, sink, default);
        Assert.Equal(EvidenceStatus.Success, r.Status);

        var lines = File.ReadAllLines(Path.Combine(Corpus.Root, Corpus.S("chromeHistory", "reference")));
        int start = Array.FindIndex(lines, l => l.StartsWith("===== ", StringComparison.Ordinal) && l.Contains(@"Chrome\Default\History"));
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith("===== ", StringComparison.Ordinal));
        var section = lines[(start + 1)..end];
        int visitsAt = Array.IndexOf(section, "-- visits --");

        var refDownloads = section[1..visitsAt].Where(l => l.Contains(" -> ", StringComparison.Ordinal) && !l.StartsWith(' ')).Select(l =>
        {
            var parts = l.Split(" | ");
            var times = parts[0].Split(" -> ");
            return (Start: DateTimeOffset.Parse(times[0], CultureInfo.InvariantCulture), Target: parts[1],
                    Tab: parts[2]["tab= ".Length..], Bytes: parts[5].Split(' ')[1]);
        }).ToList();
        var refVisits = section[(visitsAt + 1)..].Where(l => l.Length > 0).Select(l =>
        {
            int a = l.IndexOf(" | ", StringComparison.Ordinal), b = l.IndexOf(" | ", a + 3, StringComparison.Ordinal);
            return (Time: DateTimeOffset.Parse(l[..a], CultureInfo.InvariantCulture), Url: l[(a + 3)..b]);
        }).ToList();
        Assert.Equal(5, refDownloads.Count);
        Assert.True(refVisits.Count > 2000, $"{refVisits.Count} vizite în referință");

        foreach (var d in refDownloads)
            Assert.Contains(sink.Events, e => e.Source == "BrowserDownload" && e.Time.Utc == d.Start && e.Path == d.Target
                                              && e.Fields["TabUrl"] == d.Tab && e.Fields["ReceivedBytes"] == d.Bytes);

        var from = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
        var mine = sink.Events.Where(e => e.Source == "BrowserVisit" && e.Time.Utc >= from && e.Time.Utc < to).ToList();
        Assert.Equal(refVisits.Count, mine.Count);

        // The manual extraction cut long URLs at a fixed length; the parser keeps them whole. A reference URL of that maximum
        // length must be a prefix of the parser's URL at the same time; any shorter one must be identical.
        int cut = refVisits.Max(v => v.Url.Length);
        var pool = mine.GroupBy(e => e.Time.Utc!.Value).ToDictionary(g => g.Key, g => g.Select(e => e.Fields["Url"]).ToList());
        var unmatched = new List<string>();
        int truncated = 0;
        foreach (var v in refVisits)
        {
            var urls = pool.GetValueOrDefault(v.Time) ?? [];
            int i = urls.FindIndex(u => u == v.Url);
            if (i < 0 && v.Url.Length == cut) { i = urls.FindIndex(u => u.Length > cut && u.StartsWith(v.Url, StringComparison.Ordinal)); if (i >= 0) truncated++; }
            if (i < 0) { unmatched.Add($"{v.Time:O} {v.Url}"); continue; }
            urls.RemoveAt(i);
        }
        Assert.True(unmatched.Count == 0, $"{unmatched.Count} vizite nepotrivite: {string.Join(" || ", unmatched.Take(5))}");
        Assert.True(truncated < refVisits.Count / 2, $"{truncated} URL-uri trunchiate în referință");
    }
}
