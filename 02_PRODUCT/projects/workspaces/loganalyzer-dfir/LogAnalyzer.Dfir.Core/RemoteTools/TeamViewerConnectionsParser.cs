using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// TeamViewer <c>Connections_incoming.txt</c> (<c>%ProgramFiles%\TeamViewer</c>, sessions accepted on this station) and <c>Connections.txt</c> (<c>%AppData%\TeamViewer</c>,
/// sessions started from the user's account): tab-separated rows with the id of the other end, its display name, the start and the end ("dd-MM-yyyy HH:mm:ss", LOCAL time),
/// the local Windows account, the type (RemoteControl, FileTransfer ...) and a GUID. One event per session with start, end and duration. The file can be edited by anyone
/// with access to the station; no text of it reaches the summary.
/// </summary>
public sealed class TeamViewerConnectionsParser : EvidenceParserBase
{
    public const string SourceTypeName = RemoteToolLogFormats.TeamViewerConnections;
    public const string FingerprintName = RemoteToolLogFormats.TeamViewerConnections;
    public const string EventSource = "Artifact:TeamViewerConnections";
    public const string ToolId = "teamviewer";
    public const long MaxFileBytes = 16L * 1024 * 1024;
    public const int MaxRows = 50_000;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "TeamViewerConnectionsParser", Version = "1.0", Artifact = "Jurnalul de sesiuni TeamViewer (Connections_incoming.txt, Connections.txt)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows cu TeamViewer (text scris de TeamViewer, citit în cod gestionat)",
        FormatVersions = ["rânduri separate prin tab: ID, nume afișat, început „zz-LL-AAAA HH:MM:SS”, sfârșit, cont Windows local, tip, GUID"],
        Limitations =
        [
            "Formatul nu este documentat de TeamViewer; coloanele sunt cele din literatura publică DFIR și din mostre sintetice marcate TEST. Verificarea pe o instalare reală se face în laborator.",
            "Fișierul poate fi modificat, completat sau falsificat de oricine are acces la stație (și de un atacator): evenimentele sunt dovadă Directă, dar cu încredere Medie.",
            "Ora este ora locală a stației, fără fus orar; este convertită în UTC doar când proba poartă fusul stației, altfel rămâne oră locală neconvertită, fără oră pe cronologie.",
            "Connections_incoming.txt = sesiuni primite, Connections.txt = sesiuni inițiate de utilizator; direcția vine din numele fișierului. Jurnalele detaliate (TeamViewer15_Logfile.log) nu sunt citite.",
            "Numele afișat este scris de partener și poate fi orice text; apare curățat doar în câmpuri, nu în rezumat. Jurnalul nu arată ce s-a făcut în sesiune.",
            "Un ID care nu este format din cifre este marcat „format necunoscut”; un interval invers (sfârșit înaintea începutului) este marcat și nu produce durată.",
            $"Cel mult {MaxRows} de rânduri și {MaxFileBytes / (1024 * 1024)} MiB; liniile se citesc până la {RemoteToolLogText.MaxLineChars} de caractere.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "TeamViewerConnectionsTests: mostre sintetice TEST (incoming, outgoing, RemoteControl, FileTransfer), UTF-16, ANSI, linii uriașe, ID-uri și nume ostile, date imposibile și în viitor, interval invers, fus orar",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
        var direction = file.StartsWith("Connections_incoming", StringComparison.OrdinalIgnoreCase) ? "Incoming"
                      : file.StartsWith("Connections", StringComparison.OrdinalIgnoreCase) ? "Outgoing" : "necunoscută";
        var zone = RemoteToolLogText.ZoneOf(item);
        var now = DateTime.UtcNow;
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = RemoteToolLogText.OpenReader(fs);
        var sb = new StringBuilder();
        long line = 0;
        while (RemoteToolLogText.ReadLine(sr, sb, out var truncated) is { } l)
        {
            ct.ThrowIfCancellationRequested();
            line++;
            l = l.TrimStart('\uFEFF');
            if (l.Trim().Length == 0) continue;
            if (truncated) { result.MalformedRecords++; continue; }
            var c = l.Split('\t');
            if (c.Length < 4 || !TryTime(c[2], out var start)) { result.MalformedRecords++; continue; }
            if (result.Records >= MaxRows)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxRows} de rânduri; restul nu au fost citite", "Sesiunile de după rândul " + (line - 1) + " lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            var haveEnd = TryTime(c[3], out var end);
            var remote = RemoteToolLogText.IdField(c[0], out var remoteOk);
            var st = RemoteToolLogText.ToTimestamp(start, c[2].Trim(), zone, now);
            var en = haveEnd ? RemoteToolLogText.ToTimestamp(end, c[3].Trim(), zone, now) : new RemoteToolLogText.LocalTime(Timestamp.Unknown(Clean(c[3], 40)), "", "", "");
            string duration = "", durationProblem = "";
            if (haveEnd)
            {
                var span = end - start;
                if (span < TimeSpan.Zero) durationProblem = "interval invers: sfârșitul este înaintea începutului";
                else duration = ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }
            else if (c[3].Trim().Length > 0) durationProblem = "sfârșit imposibil de interpretat";
            var type = c.Length > 5 ? c[5].Trim() : "";
            var (typeLabel, typeMeaning) = Type(type);
            var guid = c.Length > 6 && Guid.TryParseExact(c[6].Trim().Trim('{', '}'), "D", out var g) ? g.ToString("D") : "";
            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tool Id"] = ToolId, ["Tool"] = "TeamViewer", ["Session Evidence"] = "da",
                ["Direction"] = direction, ["Accepted"] = direction == "Incoming" ? "da" : "necunoscut",
                ["Remote Id"] = remote, ["Remote Id Valid"] = remoteOk ? "da" : "nu",
                ["Remote Name"] = Clean(c[1], 128),
                ["Start Local"] = Show(start), ["End Local"] = haveEnd ? Show(end) : "",
                ["End Time UTC"] = en.Time.Utc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
                ["Duration Seconds"] = duration,
                ["Local Account"] = Clean(c.Length > 4 ? c[4] : "", 128),
                ["Session Type"] = typeLabel, ["Session Type Meaning"] = typeMeaning,
                ["Session GUID"] = guid,
                ["Time Zone Used"] = zone.Info?.Id ?? "necunoscut",
                ["Source File"] = RemoteToolLogText.Sane(file, 128), ["Line"] = line.ToString(CultureInfo.InvariantCulture),
                ["Limitation"] = "Fișierul poate fi falsificat de cine are acces la stația analizată; ora este locală.",
            };
            if (st.Problem.Length > 0) f["Time Problem"] = st.Problem;
            if (durationProblem.Length > 0) f["Duration Problem"] = durationProblem;
            var who = remoteOk ? $"partener {remote}" : remote.Length == 0 ? "partener fără ID" : "partener cu ID de format necunoscut";
            sink.Add(new TimelineEvent
            {
                Time = st.Time,
                TimeSemantics = "Începutul sesiunii TeamViewer din fișierul Connections*.txt: ora locală a stației, fără fus orar în fișier",
                TimeZoneBasis = st.Basis, TimeUncertainty = st.Uncertainty,
                Source = EventSource, EvidenceId = item.EvidenceId, Provider = "TeamViewer " + Clean(file, 64), EventId = "Connections",
                Summary = $"TeamViewer: sesiune {(direction == "Incoming" ? "primită" : direction == "Outgoing" ? "inițiată de utilizator" : "(direcție necunoscută)")}, tip {(typeLabel.Length == 0 ? "lipsă" : typeLabel.StartsWith("necunoscut", StringComparison.Ordinal) ? "necunoscut" : typeLabel)}, {who}, început local {Show(start)}" +
                          (duration.Length > 0 ? $", durată {duration} s" : haveEnd ? "" : ", fără sfârșit"),
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={line}", Fields = f,
            });
            result.Records++;
        }
    }

    private static string Show(DateTime t) => RemoteToolLogText.LocalText(t);
    private static string Clean(string s, int max) => RemoteToolLogText.Sane(s, max);

    private static bool TryTime(string s, out DateTime t) =>
        DateTime.TryParseExact(s.Trim(), "dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

    private static (string Label, string Meaning) Type(string raw) => raw.ToLowerInvariant() switch
    {
        "remotecontrol" => ("RemoteControl", "control de la distanță"),
        "filetransfer" => ("FileTransfer", "transfer de fișiere"),
        "" => ("", "tipul lipsește din rând"),
        _ => ("necunoscut: " + RemoteToolLogText.Sane(raw, 48), "tip care nu este în lista cunoscută"),
    };
}
