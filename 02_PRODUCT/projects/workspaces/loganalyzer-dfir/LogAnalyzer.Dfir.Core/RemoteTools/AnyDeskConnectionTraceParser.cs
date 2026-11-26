using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// AnyDesk <c>connection_trace.txt</c> (<c>%ProgramData%\AnyDesk</c> for the service, <c>%AppData%\AnyDesk</c> per user): one line per session with the direction
/// (Incoming / Outgoing), the LOCAL date and time ("2026-11-09, 21:21"), the authorisation method (User, Passwd, Token, REJECTED), the id of the other end and the local id.
/// One event per line. The file is written by the analysed station and can be edited by anyone with access to it: the events are Direct but Medium confidence.
/// Read as text only, line by line, bounded in size, line length and number of rows; no text of the file reaches the summary.
/// </summary>
public sealed class AnyDeskConnectionTraceParser : EvidenceParserBase
{
    public const string SourceTypeName = RemoteToolLogFormats.AnyDeskConnectionTrace;
    public const string FingerprintName = RemoteToolLogFormats.AnyDeskConnectionTrace;
    public const string EventSource = "Artifact:AnyDeskConnectionTrace";
    public const string ToolId = "anydesk";
    public const long MaxFileBytes = 16L * 1024 * 1024;
    public const int MaxRows = 50_000;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "AnyDeskConnectionTraceParser", Version = "1.0", Artifact = "Jurnalul de sesiuni AnyDesk (connection_trace.txt)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows cu AnyDesk (text scris de AnyDesk, citit în cod gestionat)",
        FormatVersions = ["un rând pe sesiune: direcție, „AAAA-LL-ZZ, HH:MM”, metodă de autorizare, ID la distanță, ID local (separatori: tab sau spații)"],
        Limitations =
        [
            "Formatul nu este documentat de AnyDesk; câmpurile sunt cele din literatura publică DFIR și din mostre sintetice marcate TEST. Verificarea pe o instalare reală se face în laborator, nu aici.",
            "Fișierul poate fi modificat, completat sau falsificat de oricine are acces la stație (și de un atacator): rândurile sunt dovadă Directă, dar cu încredere Medie; un rând lipsă nu dovedește că sesiunea nu a existat.",
            "Ora este ora locală a stației, fără fus orar; este convertită în UTC doar când proba poartă fusul stației, altfel rămâne oră locală neconvertită, fără oră pe cronologie.",
            "Sensul exact al metodei de autorizare (User / Passwd / Token) este cel din literatura publică; REJECTED = sesiune respinsă. Jurnalul nu arată ce s-a făcut în sesiune.",
            "Nu se deosebește fișierul serviciului (ProgramData) de cel al unui utilizator (AppData) după conținut; locul copiei este în notele probei.",
            "Un ID care nu este format din cifre este marcat „format necunoscut”; un rând fără ID rămâne cu ID gol. Rândurile fără direcție și oră sunt numărate ca neinterpretabile.",
            $"Cel mult {MaxRows} de rânduri și {MaxFileBytes / (1024 * 1024)} MiB; liniile se citesc până la {RemoteToolLogText.MaxLineChars} de caractere. UTF-8, UTF-16 și ANSI (citit ca Latin-1) sunt recunoscute.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "AnyDeskConnectionTraceTests: mostre sintetice TEST (Incoming/Outgoing, User/Passwd/Token/REJECTED), UTF-16, ANSI, linii uriașe, ID-uri ostile, date imposibile și în viitor, injectare de rânduri, fus orar",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
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
            if (l.Trim().Length == 0) continue;
            if (truncated) { result.MalformedRecords++; continue; }
            Match m;
            try { m = RemoteToolLogFormats.ConnectionTraceRow.Match(l.TrimStart('\uFEFF')); }
            catch (RegexMatchTimeoutException) { result.MalformedRecords++; continue; }
            if (!m.Success) { result.MalformedRecords++; continue; }
            if (result.Records >= MaxRows)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxRows} de rânduri; restul nu au fost citite", "Sesiunile de după rândul " + (line - 1) + " lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            var incoming = m.Groups["dir"].Value.Equals("Incoming", StringComparison.OrdinalIgnoreCase);
            var rawTime = Regex.Replace(m.Groups["dt"].Value, @"\s+", " ");
            var timeOk = DateTime.TryParseExact(rawTime, ["yyyy-MM-dd, HH:mm:ss", "yyyy-MM-dd, HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local);
            var tokens = m.Groups["rest"].Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
            string method = "";
            if (tokens.Count > 0 && !tokens[0].All(char.IsAsciiDigit)) { method = tokens[0]; tokens.RemoveAt(0); }
            var remote = RemoteToolLogText.IdField(tokens.Count > 0 ? tokens[0] : "", out var remoteOk);
            var localId = RemoteToolLogText.IdField(tokens.Count > 1 ? tokens[1] : "", out var localOk);
            var (methodLabel, meaning, accepted) = Method(method);
            var lt = timeOk ? RemoteToolLogText.ToTimestamp(local, rawTime, zone, now) : new RemoteToolLogText.LocalTime(Timestamp.Unknown(rawTime), "oră locală a stației", "", "dată sau oră imposibilă (nu există în calendar)");
            if (!timeOk || tokens.Count < 1) result.MalformedRecords++;
            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tool Id"] = ToolId, ["Tool"] = "AnyDesk", ["Session Evidence"] = "da",
                ["Direction"] = incoming ? "Incoming" : "Outgoing",
                ["Accepted"] = accepted, ["Auth Method"] = methodLabel, ["Auth Method Meaning"] = meaning,
                ["Remote Id"] = remote, ["Remote Id Valid"] = remoteOk ? "da" : "nu",
                ["Local Id"] = localId, ["Local Id Valid"] = localOk ? "da" : "nu",
                ["Local Time"] = timeOk ? RemoteToolLogText.LocalText(local) : "",
                ["Time Raw"] = RemoteToolLogText.Sane(rawTime, 40),
                ["Time Zone Used"] = zone.Info?.Id ?? "necunoscut",
                ["Source File"] = RemoteToolLogText.Sane(file, 128), ["Line"] = line.ToString(CultureInfo.InvariantCulture),
                ["Limitation"] = "Fișierul poate fi falsificat de cine are acces la stație; ora este locală.",
            };
            if (lt.Problem.Length > 0) f["Time Problem"] = lt.Problem;
            if (tokens.Count > 2) f["Extra Columns"] = (tokens.Count - 2).ToString(CultureInfo.InvariantCulture);
            if (tokens.Count < 1) f["Line Problem"] = "lipsește ID-ul de la distanță";
            var who = remoteOk ? $"partener {remote}" : remote.Length == 0 ? "partener fără ID" : "partener cu ID de format necunoscut";
            sink.Add(new TimelineEvent
            {
                Time = lt.Time,
                TimeSemantics = "Ora sesiunii AnyDesk din connection_trace.txt: ora locală a stației, fără fus orar în fișier",
                TimeZoneBasis = lt.Basis, TimeUncertainty = lt.Uncertainty,
                Source = EventSource, EvidenceId = item.EvidenceId, Provider = "AnyDesk connection_trace.txt", EventId = "ConnectionTrace",
                Summary = $"AnyDesk: sesiune {(incoming ? "de intrare" : "de ieșire")}, {accepted switch { "da" => "acceptată", "nu" => "respinsă", _ => "rezultat necunoscut" }}, {who}" + (timeOk ? $", ora locală {RemoteToolLogText.LocalText(local)}" : ""),
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={line}", Fields = f,
            });
            result.Records++;
        }
    }

    private static (string Label, string Meaning, string Accepted) Method(string raw)
    {
        if (raw.Length == 0) return ("", "metoda lipsește din rând", "necunoscut");
        if (raw.Equals("User", StringComparison.OrdinalIgnoreCase)) return ("User", "acceptată de utilizatorul stației (literatura publică)", "da");
        if (raw.Equals("Passwd", StringComparison.OrdinalIgnoreCase)) return ("Passwd", "autentificare cu parolă (literatura publică)", "da");
        if (raw.Equals("Token", StringComparison.OrdinalIgnoreCase)) return ("Token", "autentificare cu token (literatura publică)", "da");
        if (raw.Equals("REJECTED", StringComparison.OrdinalIgnoreCase)) return ("REJECTED", "sesiune respinsă", "nu");
        return ("necunoscut: " + RemoteToolLogText.Sane(raw, 32), "metodă care nu este în lista cunoscută", "necunoscut");
    }
}
