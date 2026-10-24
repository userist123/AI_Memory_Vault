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

    /// <summary>Both SRUM network tables. <c>ConnectivityError</c> is "" when the Network Connectivity table was read, else why not (the usage rows are still returned).</summary>
    public static (List<SrumNetRow> Network, List<SrumConnRow> Connectivity, string ConnectivityError) ReadAll(string srudbPath, CancellationToken ct = default)
    {
        var conn = new List<SrumConnRow>();
        string connError = "";
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
            using (var db = new EseReadOnlyDatabase(copy))
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

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var (network, connectivity, connError) = ReadAll(fullPath, ct);
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
