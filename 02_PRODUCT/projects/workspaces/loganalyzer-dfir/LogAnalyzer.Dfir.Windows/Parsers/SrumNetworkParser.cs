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
            "Doar tabela Network Data Usage; celelalte tabele SRUM nu sunt citite.",
            "Ora unui rând este sfârșitul intervalului de agregare (aproximativ 1 h), nu momentul transferului.",
            "O bază „murdară” fără jurnalele ESE poate pierde ultimele înregistrări.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "CorpusRegressionTests (srum), InvestigationTests pe corpusul NanAgent",
    };

    public sealed record SrumNetRow(DateTime TimestampUtc, int AppId, string App, int UserId, string User, long InterfaceLuid, long BytesSent, long BytesRecvd);

    public static IEnumerable<SrumNetRow> Read(string srudbPath, CancellationToken ct = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "ladfir_srum_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
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
            }
            return rows;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { /* esent may hold the temp files briefly; leftover temp data is not evidence */ }
        }
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        foreach (var row in Read(fullPath, ct))
        {
            var app = string.IsNullOrEmpty(row.App) ? $"(unresolved AppId {row.AppId})" : row.App;
            sink.Add(new TimelineEvent
            {
                Time = Timestamp.FromUtc(row.TimestampUtc, row.TimestampUtc.ToString("o"), "SRUM OLE date (interval end)"),
                Source = "SRUM",
                EvidenceId = item.EvidenceId,
                Provider = "SRUM Network Data Usage",
                User = row.User,
                Process = System.IO.Path.GetFileName(app),
                Path = app,
                Summary = $"SRUM: {System.IO.Path.GetFileName(app)} sent {row.BytesSent:N0} B / received {row.BytesRecvd:N0} B in the interval ending at this time",
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
