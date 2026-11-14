using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// SRUM Network Data Usage ({973F5D5C-…}) per application, per hour, with bytes sent/received.
/// Works on a temporary working copy because attaching an ESE database writes beside it.
/// A SRUM row's timestamp is the end of an aggregation interval (≈1 h), not the moment of transfer.
/// </summary>
public sealed class SrumNetworkParser : EvidenceParserBase
{
    public const string NetworkTable = "{973F5D5C-1D90-4944-BE8E-24B94231A174}";
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "SrumNetworkParser", Version = "1.0", Artifact = "SRUM — utilizarea rețelei per aplicație (SRUDB.dat)",
        SourceTypes = ["srum"], FileNames = ["SRUDB.dat"], Fingerprints = ["ese"],
        SupportedOs = "Windows (deschide baza ESE prin esent.dll, pe o copie de lucru)",
        FormatVersions = ["ESE, tabela Network Data Usage {973F5D5C-1D90-4944-BE8E-24B94231A174}"],
        Limitations =
        [
            "Se citesc tabelele Network Data Usage (octeți per aplicație și interfață) și Network Connectivity (durata conectării per interfață); celelalte tabele SRUM nu sunt citite.",
            "Ora unui rând este sfârșitul intervalului de agregare (aproximativ 1 h), nu momentul transferului.",
            "O bază „murdară” fără jurnalele ESE poate pierde ultimele înregistrări.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "CorpusRegressionTests (srum), InvestigationTests pe corpusul VendorAgent",
    };

    public sealed record SrumNetRow(DateTime TimestampUtc, int AppId, string App, int UserId, string User, long InterfaceLuid, long BytesSent, long BytesRecvd);

    /// <summary>One row of the Network Connectivity table: how long (seconds) an interface was connected to a profile in the interval ending at <see cref="TimestampUtc"/>.</summary>
    public sealed record SrumConnRow(DateTime TimestampUtc, long InterfaceLuid, int L2ProfileId, int ConnectedSeconds, DateTime? ConnectStartUtc);

    public const string ConnectivityTable = "{DD6636C4-8929-4683-974E-22C046A43763}";

    public static IEnumerable<SrumNetRow> Read(string srudbPath, CancellationToken ct = default) => ReadAll(srudbPath, ct).Network;

    /// <summary>Set when the working copy was a "dirty shutdown" database recovered with the collected ESE logs; "" when it opened as copied.</summary>
    [ThreadStatic] private static string? _lastRecovery;

    /// <summary>Both SRUM network tables. <c>ConnectivityError</c> is "" when the Network Connectivity table was read, else why not (the usage rows are still returned).</summary>
    public static (List<SrumNetRow> Network, List<SrumConnRow> Connectivity, string ConnectivityError) ReadAll(string srudbPath, CancellationToken ct = default)
    {
        var conn = new List<SrumConnRow>();
        string connError = "";
        _lastRecovery = "";
        var rows = ReadCore(srudbPath, ct, db =>
        {
            try
            {
                using var t = db.OpenTable(ConnectivityTable);
                uint cTs = t.Column("TimeStamp"), cIf = t.Column("InterfaceLuid"), cProf = t.Column("L2ProfileId"), cConn = t.Column("ConnectedTime"), cStart = t.Column("ConnectStartTime");
                foreach (var r in t.Rows())
                {
                    ct.ThrowIfCancellationRequested();
                    if (r.Double(cTs) is not double oa) continue;
                    var start = r.Int64(cStart) is long ft && ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(ft) : (DateTime?)null;
                    conn.Add(new SrumConnRow(DateTime.SpecifyKind(DateTime.FromOADate(oa), DateTimeKind.Utc), r.Int64(cIf) ?? 0, r.Int32(cProf) ?? -1, r.Int32(cConn) ?? 0, start));
                }
            }
            catch (InvalidOperationException ex) { connError = ex.Message; }
        });
        return (rows, conn, connError);
    }

    private static List<SrumNetRow> ReadCore(string srudbPath, CancellationToken ct, Action<EseReadOnlyDatabase> extra)
    {
        var work = LogAnalyzer.Dfir.IO.WorkFolders.NewWorkDirectory(srudbPath, "srum");   // security audit #14: never %TEMP%
        try
        {
            var copy = Path.Combine(work, "SRUDB.dat");
            File.Copy(srudbPath, copy);
            File.SetAttributes(copy, FileAttributes.Normal);
            var rows = new List<SrumNetRow>();
            using (var db = OpenOrRecover(copy, Path.GetDirectoryName(Path.GetFullPath(srudbPath))!, work, ct))
            {
                var idmap = new Dictionary<int, string>();
                using (var t = db.OpenTable("SruDbIdMapTable"))
                {
                    uint cType = t.Column("IdType"), cIdx = t.Column("IdIndex"), cBlob = t.Column("IdBlob");
                    foreach (var r in t.Rows())
                    {
                        if (r.Int32(cIdx) is not int idx) continue;
                        var blob = r.Bytes(cBlob);
                        idmap[idx] = blob is null ? "" : r.Byte(cType) == 3 ? SidToString(blob) : System.Text.Encoding.Unicode.GetString(blob).TrimEnd('\0');
                    }
                }
                using (var t = db.OpenTable(NetworkTable))
                {
                    uint cTs = t.Column("TimeStamp"), cApp = t.Column("AppId"), cUser = t.Column("UserId"), cIf = t.Column("InterfaceLuid"),
                         cSent = t.Column("BytesSent"), cRecv = t.Column("BytesRecvd");
                    foreach (var r in t.Rows())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (r.Double(cTs) is not double oa) continue;
                        int app = r.Int32(cApp) ?? -1, user = r.Int32(cUser) ?? -1;
                        rows.Add(new SrumNetRow(DateTime.SpecifyKind(DateTime.FromOADate(oa), DateTimeKind.Utc), app, idmap.GetValueOrDefault(app, ""),
                                                user, idmap.GetValueOrDefault(user, ""), r.Int64(cIf) ?? 0, r.Int64(cSent) ?? 0, r.Int64(cRecv) ?? 0));
                    }
                }
                extra(db);
            }
            return rows;
        }
        finally
        {
            // esent may hold its files for a moment after detach: one short retry before giving up.
            if (!LogAnalyzer.Dfir.IO.WorkFolders.TryDelete(work)) { Thread.Sleep(200); LogAnalyzer.Dfir.IO.WorkFolders.TryDelete(work); }
        }
    }

    /// <summary>
    /// Opens the working copy; a "dirty shutdown" database (JET -550, the snapshot caught SRUM mid-write) is recovered first with the ESE logs
    /// collected beside it: only the srum_log items of the case evidence index whose SHA-256 still matches (review 2026-11-05: an unchecked
    /// planted .log would have changed the recovered rows), copied into the working folder and replayed there by esentutl /r with every path
    /// (logs, system files, database) pointing to that folder, so nothing outside it is touched. Outside a case there is no index: no recovery.
    /// </summary>
    private static EseReadOnlyDatabase OpenOrRecover(string copy, string evidenceDir, string work, CancellationToken ct)
    {
        try { return new EseReadOnlyDatabase(copy); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("JET error -550", StringComparison.Ordinal))
        {
            var (logs, problem) = VerifiedSrumLogs(evidenceDir, ct);
            if (problem.Length > 0 || logs.Count == 0)
                throw new InvalidOperationException(ex.Message + " Baza SRUM copiată este „murdară” și nu poate fi recuperată: " +
                    (problem.Length > 0 ? problem : "jurnalele ESE nu au fost colectate lângă ea."));
            foreach (var l in logs)
            {
                var d = Path.Combine(work, Path.GetFileName(l.Path));
                File.Copy(l.Path, d, overwrite: true);
                File.SetAttributes(d, FileAttributes.Normal);
                // review round 2 LOW-3: the copy that is replayed is the one checked (no window between the hash and the copy)
                using var fs = new FileStream(d, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).Equals(l.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(ex.Message + $" Baza SRUM copiată este „murdară” și nu poate fi recuperată: jurnalul {l.EvidenceId} s-a schimbat în timpul copierii.");
            }
            var run = LogAnalyzer.Dfir.Windows.Acquisition.ToolRunner.Run(LogAnalyzer.Dfir.Windows.Acquisition.ToolRunner.System32("esentutl.exe"),
                ["/r", "SRU", "/l", work, "/s", work, "/d", work, "/i", "/o"], work, "esentutl_srum_recovery", TimeSpan.FromMinutes(5), ct);
            try
            {
                var db = new EseReadOnlyDatabase(copy);
                _lastRecovery = $"baza SRUM copiată era „murdară” (JET -550); recuperată pe copia de lucru cu jurnalele ESE {string.Join(", ", logs.Select(l => l.EvidenceId))} " +
                                $"(amprente verificate; {run.CommandLine}, cod {run.ExitCode}); originalul din caz nu a fost modificat";
                return db;
            }
            catch (InvalidOperationException again)
            {
                throw new InvalidOperationException($"{again.Message} Recuperarea cu jurnalele ESE colectate (esentutl /r, cod {run.ExitCode}) nu a reușit: probabil lipsesc generațiile de jurnal cerute de bază.");
            }
        }
    }

    /// <summary>The srum_log items of the case that sit beside the database and still have their acquisition hash; a changed one stops the recovery.</summary>
    internal static (List<(string Path, string EvidenceId, string Sha256)> Logs, string Problem) VerifiedSrumLogs(string evidenceDir, CancellationToken ct)
    {
        var logs = new List<(string, string, string)>();
        if (LogAnalyzer.Dfir.IO.WorkFolders.CaseRootOf(Path.Combine(evidenceDir, "SRUDB.dat")) is not { } root) return (logs, "copia nu este într-un caz, deci jurnalele nu pot fi verificate.");
        var index = Path.Combine(root, "Evidence", "evidence_index.jsonl");
        if (!File.Exists(index)) return (logs, "indexul probelor cazului lipsește.");
        var dir = Path.GetFullPath(evidenceDir).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var e in LogAnalyzer.Dfir.IO.Json.ReadLines<EvidenceItem>(index).Where(e => e.SourceType == LogAnalyzer.Dfir.Windows.Acquisition.SrumCollector.SrumLogSourceType))
        {
            ct.ThrowIfCancellationRequested();
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, e.StoredPath)); }
            catch (Exception pex) when (pex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;   // never outside the case
            if (!string.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(full)) return (logs, $"jurnalul {e.EvidenceId} lipsește din caz.");
            string sha;
            using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)) sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
            if (!sha.Equals(e.Sha256, StringComparison.OrdinalIgnoreCase)) return (logs, $"jurnalul {e.EvidenceId} ({Path.GetFileName(full)}) a fost modificat după colectare (SHA-256 diferit).");
            logs.Add((full, e.EvidenceId, sha));
        }
        return (logs, "");
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var (network, connectivity, connError) = ReadAll(fullPath, ct);
        if (_lastRecovery is { Length: > 0 } recovered)
            result.Gaps.Add(new EvidenceGap("SRUM (recuperare)", EvidenceStatus.Partial, recovered,
                "Înregistrările necomise la momentul copierii pot lipsi dacă jurnalele copiate nu le conțin", "o copie SRUDB.dat cu oprire curată", "Posibil"));
        foreach (var c in connectivity)
        {
            sink.Add(new TimelineEvent
            {
                Time = Timestamp.FromUtc(c.TimestampUtc, c.TimestampUtc.ToString("o"), "SRUM OLE date (interval end)"),
                Source = "SRUM", EvidenceId = item.EvidenceId, Provider = "SRUM Network Connectivity",
                Summary = $"SRUM: interfața {c.InterfaceLuid} conectată {c.ConnectedSeconds} s la profilul L2 {c.L2ProfileId} în intervalul care se încheie la această oră",
                TimeSemantics = "SRUM interval end (~1h aggregate)", TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $"{ConnectivityTable}/InterfaceLuid={c.InterfaceLuid}/ts={c.TimestampUtc:o}",
                Fields =
                {
                    ["InterfaceLuid"] = c.InterfaceLuid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["L2ProfileId"] = c.L2ProfileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ConnectedTime"] = c.ConnectedSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ConnectStartTime"] = c.ConnectStartUtc is { } s ? s.ToString("o") : "",
                },
            });
            result.Records++;
        }
        if (connError.Length > 0)
            result.Gaps.Add(new EvidenceGap("SRUM Network Connectivity", EvidenceStatus.Partial,
                $"sursă lipsă: tabela SRUM Network Connectivity {ConnectivityTable}, de ce lipsește: {connError}",
                "Durata conectărilor nu se poate lua din SRUM", "o copie SRUDB.dat completă (cu jurnalele ESE), luată cu esentutl /vss ca administrator", "Posibil"));
        foreach (var row in network)
        {
            var app = string.IsNullOrEmpty(row.App) ? $"(unresolved AppId {row.AppId})" : row.App;
            sink.Add(new TimelineEvent
            {
                Time = Timestamp.FromUtc(row.TimestampUtc, row.TimestampUtc.ToString("o"), "SRUM OLE date (interval end)"),
                Source = "SRUM",
                EvidenceId = item.EvidenceId,
                Provider = "SRUM Network Data Usage",
                User = row.User,
                Process = WinPath.GetFileName(app),
                Path = app,
                Summary = $"SRUM: {WinPath.GetFileName(app)} sent {row.BytesSent:N0} B / received {row.BytesRecvd:N0} B in the interval ending at this time",
                TimeSemantics = "SRUM interval end (~1h aggregate)",
                TemporalType = TemporalType.Historical,
                Classification = Classification.Direct,
                Confidence = Confidence.Medium,
                Locator = $"{NetworkTable}/AppId={row.AppId}/ts={row.TimestampUtc:o}",
                Fields =
                {
                    ["AppId"] = row.AppId.ToString(),
                    ["UserId"] = row.UserId.ToString(),
                    ["InterfaceLuid"] = row.InterfaceLuid.ToString(),
                    ["BytesSent"] = row.BytesSent.ToString(),
                    ["BytesRecvd"] = row.BytesRecvd.ToString(),
                },
            });
            result.Records++;
        }
    }

    private static string SidToString(byte[] b)
    {
        if (b.Length < 8) return Convert.ToHexString(b);
        int count = b[1];
        long authority = 0;
        for (int i = 2; i < 8; i++) authority = (authority << 8) | b[i];
        var sb = new System.Text.StringBuilder($"S-{b[0]}-{authority}");
        for (int i = 0; i < count && 8 + 4 * i + 4 <= b.Length; i++) sb.Append('-').Append(BitConverter.ToUInt32(b, 8 + 4 * i));
        return sb.ToString();
    }
}
