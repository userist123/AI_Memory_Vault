using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Microsoft.Data.Sqlite;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Chromium "History" database (Chrome, Edge, Brave…): page visits and downloads with their URL chain. Read from a
/// working copy in %TEMP% (an SQLite open can write a journal next to the file); the stored evidence is never opened.
/// </summary>
public sealed class BrowserHistoryParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "BrowserHistoryParser", Version = "1.0", Artifact = "Istoric Chromium (Chrome, Edge) — vizite și descărcări",
        SourceTypes = ["chromium_history"], FileNames = ["History"], Fingerprints = ["sqlite"],
        SupportedOs = "Oricare (SQLite pe o copie de lucru)",
        FormatVersions = ["Schema History Chromium cu tabelele urls, visits, downloads, downloads_url_chains (Chrome / Edge moderne)"],
        Limitations =
        [
            "Fișierul de jurnal (History-journal / -wal) se folosește doar dacă a fost copiat lângă History; altfel ultimele tranzacții pot lipsi.",
            "Înregistrările șterse de utilizator (spațiu liber din baza SQLite) nu sunt recuperate.",
            "Firefox (places.sqlite) nu este citit de acest parser.",
            "O vizită arată că pagina a fost încărcată în browser, nu că utilizatorul a citit-o.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "BrowserHistoryParserTests: History real (profil Default) comparat cu extracția independentă din investigația manuală (5 descărcări, toate vizitele din 18–20.09.2026) și bază sintetică",
    };

    private static readonly string[] States = ["in_progress", "complete", "cancelled", "interrupted", "interrupted"];

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "LogAnalyzer", "sqlite_work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var copy = Path.Combine(work, "History");
        try
        {
            File.Copy(fullPath, copy);
            foreach (var side in new[] { "-journal", "-wal" })
                if (File.Exists(fullPath + side)) File.Copy(fullPath + side, copy + side);
            File.SetAttributes(copy, FileAttributes.Normal);
            SQLitePCL.Batteries_V2.Init();
            using var c = new SqliteConnection($"Data Source={copy};Mode=ReadWrite;Pooling=False");
            c.Open();
            var tables = Strings(c, "SELECT name FROM sqlite_master WHERE type='table'").ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!tables.Contains("visits") || !tables.Contains("urls"))
                throw new InvalidDataException("Baza nu are tabelele visits/urls: nu este un istoric Chromium.");

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT v.id, v.visit_time, u.url, u.title, v.transition, v.visit_duration, v.from_visit FROM visits v LEFT JOIN urls u ON u.id = v.url ORDER BY v.id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var url = r.IsDBNull(2) ? "" : r.GetString(2);
                    long transition = r.IsDBNull(4) ? 0 : r.GetInt64(4);
                    sink.Add(new TimelineEvent
                    {
                        Time = ChromeTime(r.IsDBNull(1) ? 0 : r.GetInt64(1), "visit_time"), TimeSemantics = "page visit (visits.visit_time)",
                        Source = "BrowserVisit", EvidenceId = item.EvidenceId, Dns = Host(url),
                        Summary = $"Vizită: {url}", TemporalType = TemporalType.Historical,
                        Classification = Classification.Direct, Confidence = Confidence.High, Locator = $"visits.id={r.GetInt64(0)}",
                        Fields =
                        {
                            ["Url"] = url, ["Title"] = r.IsDBNull(3) ? "" : r.GetString(3),
                            ["Transition"] = transition.ToString(CultureInfo.InvariantCulture), ["TransitionCore"] = (transition & 0xFF).ToString(CultureInfo.InvariantCulture),
                            ["VisitDurationUs"] = r.IsDBNull(5) ? "" : r.GetInt64(5).ToString(CultureInfo.InvariantCulture),
                            ["FromVisit"] = r.IsDBNull(6) ? "" : r.GetInt64(6).ToString(CultureInfo.InvariantCulture),
                        },
                    });
                    result.Records++;
                }
            }

            if (!tables.Contains("downloads"))
            {
                result.Gaps.Add(new EvidenceGap("downloads", EvidenceStatus.NotAvailable, "tabela downloads lipsește", "Descărcările nu sunt disponibile", "Folderul Downloads, Zone.Identifier", "Nu"));
                return;
            }
            var chains = new Dictionary<long, List<string>>();
            if (tables.Contains("downloads_url_chains"))
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, url FROM downloads_url_chains ORDER BY id, chain_index";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        if (!chains.TryGetValue(r.GetInt64(0), out var l)) chains[r.GetInt64(0)] = l = [];
                        l.Add(r.IsDBNull(1) ? "" : r.GetString(1));
                    }
                }
            var cols = Strings(c, "SELECT name FROM pragma_table_info('downloads')").ToHashSet(StringComparer.OrdinalIgnoreCase);
            string Col(string n) => cols.Contains(n) ? n : "NULL";
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = $"SELECT id, {Col("target_path")}, {Col("start_time")}, {Col("end_time")}, {Col("received_bytes")}, {Col("total_bytes")}, {Col("state")}, " +
                                  $"{Col("danger_type")}, {Col("opened")}, {Col("last_access_time")}, {Col("referrer")}, {Col("tab_url")}, {Col("tab_referrer_url")}, {Col("mime_type")} FROM downloads ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    string S(int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "";
                    long L(int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
                    long id = r.GetInt64(0);
                    var target = S(1);
                    var chain = chains.GetValueOrDefault(id) ?? [];
                    int state = (int)L(6);
                    var end = ChromeTime(L(3), "end_time");
                    var access = ChromeTime(L(9), "last_access_time");
                    sink.Add(new TimelineEvent
                    {
                        Time = ChromeTime(L(2), "start_time"), TimeSemantics = "download started (downloads.start_time)",
                        Source = "BrowserDownload", EvidenceId = item.EvidenceId, Path = target, Process = WinPath.GetFileName(target),
                        Dns = Host(chain.LastOrDefault() ?? S(11)),
                        Summary = $"Descărcare: {target} de pe {S(11)} ({S(4)} octeți, {(state >= 0 && state < States.Length ? States[state] : state.ToString(CultureInfo.InvariantCulture))})",
                        TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
                        Locator = $"downloads.id={id}",
                        Fields =
                        {
                            ["TabUrl"] = S(11), ["TabReferrer"] = S(12), ["Referrer"] = S(10), ["UrlChain"] = string.Join(" -> ", chain),
                            ["ReceivedBytes"] = S(4), ["TotalBytes"] = S(5),
                            ["State"] = state >= 0 && state < States.Length ? States[state] : state.ToString(CultureInfo.InvariantCulture),
                            ["DangerType"] = S(7), ["Opened"] = S(8), ["Mime"] = S(13),
                            ["EndTimeUtc"] = end.Utc?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) ?? "",
                            ["LastAccessUtc"] = access.Utc?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) ?? "",
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

    /// <summary>Chromium time: microseconds since 1601-01-01 UTC; 0 means "not set" (never "now").</summary>
    public static Timestamp ChromeTime(long us, string column) =>
        us <= 0 ? Timestamp.Unknown(us.ToString(CultureInfo.InvariantCulture))
                : Timestamp.FromUtc(DateTime.FromFileTimeUtc(us * 10), us.ToString(CultureInfo.InvariantCulture), $"Chromium {column} (µs since 1601 UTC)");

    private static string Host(string url) => Uri.TryCreate(url.StartsWith("blob:", StringComparison.Ordinal) ? url[5..] : url, UriKind.Absolute, out var u) ? u.Host : "";

    private static IEnumerable<string> Strings(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return r.GetString(0);
    }
}
