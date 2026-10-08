using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Microsoft.Data.Sqlite;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Firefox places.sqlite: page visits (moz_historyvisits + moz_places) and downloads (moz_annos
/// downloads/destinationFileURI and downloads/metaData). Read from a working copy in %TEMP%, like the Chromium parser.
/// </summary>
public sealed class FirefoxHistoryParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "FirefoxHistoryParser", Version = "1.0", Artifact = "Istoric Firefox (places.sqlite) — vizite și descărcări",
        SourceTypes = ["firefox_places"], FileNames = ["places.sqlite"], Fingerprints = ["sqlite"],
        SupportedOs = "Oricare (SQLite pe o copie de lucru)",
        FormatVersions = ["places.sqlite cu moz_places, moz_historyvisits, moz_annos (Firefox 57 și ulterior)"],
        Limitations =
        [
            "Jurnalul WAL (places.sqlite-wal) se folosește doar dacă a fost copiat lângă bază.",
            "Descărcările se iau din moz_annos; cele șterse din bibliotecă nu mai apar.",
            "Înregistrările șterse (spațiu liber SQLite) nu sunt recuperate.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "FirefoxHistoryParserTests: places.sqlite real comparat cu o citire independentă prin modulul sqlite3 din Python (toate vizitele și descărcările) și bază sintetică",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "LogAnalyzer", "sqlite_work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var copy = Path.Combine(work, "places.sqlite");
        try
        {
            File.Copy(fullPath, copy);
            foreach (var side in new[] { "-wal", "-journal" })
                if (File.Exists(fullPath + side)) File.Copy(fullPath + side, copy + side);
            File.SetAttributes(copy, FileAttributes.Normal);
            SQLitePCL.Batteries_V2.Init();
            using var c = new SqliteConnection($"Data Source={copy};Mode=ReadWrite;Pooling=False");
            c.Open();
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) tables.Add(r.GetString(0));
            }
            if (!tables.Contains("moz_places") || !tables.Contains("moz_historyvisits"))
                throw new InvalidDataException("Baza nu are moz_places/moz_historyvisits: nu este un places.sqlite Firefox.");

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT v.id, v.visit_date, p.url, p.title, v.visit_type, v.from_visit FROM moz_historyvisits v LEFT JOIN moz_places p ON p.id = v.place_id ORDER BY v.id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var url = r.IsDBNull(2) ? "" : r.GetString(2);
                    sink.Add(new TimelineEvent
                    {
                        Time = UnixMicros(r.IsDBNull(1) ? 0 : r.GetInt64(1), "visit_date"), TimeSemantics = "page visit (moz_historyvisits.visit_date)",
                        Source = "BrowserVisit", EvidenceId = item.EvidenceId, Dns = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "",
                        Summary = $"Vizită Firefox: {url}", TemporalType = TemporalType.Historical,
                        Classification = Classification.Direct, Confidence = Confidence.High, Locator = $"moz_historyvisits.id={r.GetInt64(0)}",
                        Fields =
                        {
                            ["Browser"] = "Firefox", ["Url"] = url, ["Title"] = r.IsDBNull(3) ? "" : r.GetString(3),
                            ["VisitType"] = r.IsDBNull(4) ? "" : r.GetInt64(4).ToString(CultureInfo.InvariantCulture),
                            ["FromVisit"] = r.IsDBNull(5) ? "" : r.GetInt64(5).ToString(CultureInfo.InvariantCulture),
                        },
                    });
                    result.Records++;
                }
            }

            if (!tables.Contains("moz_annos") || !tables.Contains("moz_anno_attributes")) return;
            var meta = new Dictionary<long, string>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT n.place_id, n.content FROM moz_annos n JOIN moz_anno_attributes a ON a.id = n.anno_attribute_id WHERE a.name = 'downloads/metaData'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) meta[r.GetInt64(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
            }
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT n.id, n.place_id, n.content, n.dateAdded, p.url FROM moz_annos n JOIN moz_anno_attributes a ON a.id = n.anno_attribute_id " +
                                  "LEFT JOIN moz_places p ON p.id = n.place_id WHERE a.name = 'downloads/destinationFileURI' ORDER BY n.id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var dest = r.IsDBNull(2) ? "" : r.GetString(2);
                    var path = Uri.TryCreate(dest, UriKind.Absolute, out var fu) && fu.IsFile ? fu.LocalPath : dest;
                    var source = r.IsDBNull(4) ? "" : r.GetString(4);
                    string state = "", size = "", end = "";
                    if (meta.TryGetValue(r.GetInt64(1), out var json) && json.Length > 0)
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("state", out var s)) state = s.ToString();
                        if (root.TryGetProperty("fileSize", out var fsz)) size = fsz.ToString();
                        if (root.TryGetProperty("endTime", out var et) && et.TryGetInt64(out var ms) && ms > 0)
                            end = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
                    }
                    sink.Add(new TimelineEvent
                    {
                        Time = UnixMicros(r.IsDBNull(3) ? 0 : r.GetInt64(3), "dateAdded"), TimeSemantics = "download recorded (moz_annos.dateAdded)",
                        Source = "BrowserDownload", EvidenceId = item.EvidenceId, Path = path, Process = Path.GetFileName(path),
                        Dns = Uri.TryCreate(source, UriKind.Absolute, out var su) ? su.Host : "",
                        Summary = $"Descărcare Firefox: {path} de pe {source}", TemporalType = TemporalType.Historical,
                        Classification = Classification.Direct, Confidence = Confidence.High, Locator = $"moz_annos.id={r.GetInt64(0)}",
                        Fields =
                        {
                            ["Browser"] = "Firefox", ["TabUrl"] = source, ["UrlChain"] = source, ["ReceivedBytes"] = size, ["State"] = state,
                            ["EndTimeUtc"] = end, ["DestinationUri"] = dest,
                        },
                    });
                    result.Records++;
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(work, true); }
            catch (IOException) { /* the working copy is not evidence; a leftover in %TEMP% is harmless */ }
        }
    }

    /// <summary>Firefox time: microseconds since 1970-01-01 UTC; 0 = not set.</summary>
    public static Timestamp UnixMicros(long us, string column) =>
        us <= 0 ? Timestamp.Unknown(us.ToString(CultureInfo.InvariantCulture))
                : Timestamp.FromUtc(DateTime.UnixEpoch.AddTicks(us * 10), us.ToString(CultureInfo.InvariantCulture), $"Firefox {column} (µs since 1970 UTC)");
}
