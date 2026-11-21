using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// AnyDesk <c>ad.trace</c> (<c>%AppData%\AnyDesk</c>) and <c>ad_svc.trace</c> (<c>%ProgramData%\AnyDesk</c>): a large text log with lines like
/// <c>   info 2026-11-09 21:21:55.123       gsvc   4120   6000   21  anynet.relay_conn - Logged in from 203.0.113.7:50123 on relay ...</c>.
/// Only the lines that describe sessions are kept ("Incoming session request", "Remote OS", "Logged in from", "Accepting from", "Session closed" / "Session stopped",
/// "Authenticated with permanent token", "File transfer"), one event each, with the level, the LOCAL time, the process and the cleaned text. The file is read as a stream,
/// line by line (a line is cut at 2 048 characters), at most 64 MiB and 50 000 relevant lines; beyond that a gap is written. Everything in the file can be forged by
/// anyone with access to the station.
/// </summary>
public sealed class AnyDeskTraceParser : EvidenceParserBase
{
    public const string SourceTypeName = RemoteToolLogFormats.AnyDeskTrace;
    public const string FingerprintName = RemoteToolLogFormats.AnyDeskTrace;
    public const string EventSource = "Artifact:AnyDeskTrace";
    public const string ToolId = "anydesk";
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const int MaxRelevantLines = 50_000;
    /// <summary>Lines looked at in total (relevant or not): a file of 64 MiB of line breaks must not keep the parser busy for long.</summary>
    public const long MaxLinesScanned = 5_000_000;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "AnyDeskTraceParser", Version = "1.0", Artifact = "Jurnalul AnyDesk (ad.trace, ad_svc.trace): rândurile despre sesiuni",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows cu AnyDesk (text scris de AnyDesk, citit în flux în cod gestionat)",
        FormatVersions = ["„nivel AAAA-LL-ZZ HH:MM:SS.mmm proces pid tid nr modul - mesaj”"],
        Limitations =
        [
            "Formatul și mesajele nu sunt documentate de AnyDesk și diferă între versiuni; rândurile recunoscute sunt cele din literatura publică DFIR și din mostre sintetice marcate TEST. Verificarea pe o instalare reală se face în laborator.",
            "Se extrag doar rândurile despre sesiuni; restul jurnalului (zeci de mii de rânduri tehnice) nu este citit ca eveniment. Un mesaj schimbat de o versiune nouă nu este recunoscut.",
            "„Logged in from <IP>” arată o adresă văzută de AnyDesk pe releu; literatura publică nu este unanimă dacă este adresa partenerului sau a stației, așa că nu este tratată ca adresa partenerului dovedită.",
            "Ora este ora locală a stației, fără fus orar; este convertită în UTC doar când proba poartă fusul stației, altfel rămâne oră locală neconvertită, fără oră pe cronologie.",
            "Jurnalul poate fi modificat, trunchiat sau rotit; fișierul poate fi falsificat de cine are acces la stație (evenimentele sunt Directe, cu încredere Medie). Jurnalul nu arată ce s-a făcut în sesiune.",
            "O adresă IP care nu este un literal IPv4 / IPv6 valid este ignorată și marcată; un ID care nu este format din cifre este marcat „format necunoscut”.",
            $"Fișier de cel mult {MaxFileBytes / (1024 * 1024)} MiB, cel mult {MaxRelevantLines} de rânduri relevante; liniile se citesc până la {RemoteToolLogText.MaxLineChars} de caractere (restul se aruncă și se marchează). Peste plafon se scrie o lacună.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "AnyDeskTraceTests: mostre sintetice TEST în formatul documentat, rânduri irelevante ignorate, UTF-16, ANSI, linii uriașe, IP-uri și ID-uri ostile, caractere ascunse, date imposibile și în viitor, plafonul de rânduri",
    };

    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex IpPort = new(@"Logged in from\s+(?<tok>\S{1,80})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RxTimeout);
    private static readonly Regex AcceptId = new(@"Accepting from\s+(?<id>[^\s(,;]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RxTimeout);
    private static readonly Regex RequestId = new(@"Incoming session request:?\s*(?<id>[^\s(,;]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RxTimeout);

    private sealed record Kind(string Name, string Phrase, string Direction, string Accepted);

    /// <summary>The kind of a relevant message, or null when the line is not about a session. The order matters: the most specific marker first.</summary>
    private static Kind? Classify(string msg)
    {
        bool Has(string s) => msg.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("Authenticated with permanent token")) return new("AuthenticatedPermanentToken", "autentificare cu token permanent", "necunoscută", "da");
        if (Has("Accepting from")) return new("AcceptedFrom", "sesiune de intrare acceptată", "Incoming", "da");
        if (Has("Incoming session request")) return new("IncomingSessionRequest", "cerere de sesiune de intrare (acceptarea nu reiese din rând)", "Incoming", "necunoscut");
        if (Has("Logged in from")) return new("LoggedInFromRelay", "adresă văzută pe releu („Logged in from”)", "necunoscută", "necunoscut");
        if (Has("Remote OS")) return new("RemoteOs", "sistemul de operare al celuilalt capăt", "necunoscută", "necunoscut");
        if (Has("Session closed") || Has("Session stopped")) return new("SessionEnded", "sesiune încheiată", "necunoscută", "necunoscut");
        if (Has("File transfer")) return new("FileTransfer", "transfer de fișiere", "necunoscută", "necunoscut");
        return null;
    }

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
            if (line > MaxLinesScanned)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxLinesScanned} de rânduri citite; restul nu au fost cercetate", "Sesiunile din restul jurnalului lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            // cheap pre-filter before any regex: a line must name one of the markers
            if (l.Length < 40) continue;
            Match m;
            try { m = RemoteToolLogFormats.TraceRow.Match(l); }
            catch (RegexMatchTimeoutException) { result.MalformedRecords++; continue; }
            if (!m.Success) continue;
            var msg = m.Groups["msg"].Value;
            var kind = Classify(msg);
            if (kind is null) continue;
            if (result.Records >= MaxRelevantLines)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxRelevantLines} de rânduri relevante; restul nu au fost citite", "Sesiunile de după rândul " + (line - 1) + " lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            var level = m.Groups["lvl"].Value.ToLowerInvariant() switch { "warn" or "warning" => "warning", "error" or "fatal" => "error", "debug" => "debug", _ => "info" };
            var rawTime = m.Groups["d"].Value + " " + m.Groups["t"].Value;
            var timeOk = DateTime.TryParseExact(rawTime, ["yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local);
            var lt = timeOk ? RemoteToolLogText.ToTimestamp(local, rawTime, zone, now) : new RemoteToolLogText.LocalTime(Timestamp.Unknown(rawTime), "oră locală a stației", "", "dată sau oră imposibilă (nu există în calendar)");
            if (!timeOk) result.MalformedRecords++;
            var proc = RemoteToolLogText.Sane(m.Groups["proc"].Value, 32);

            string remoteId = "", ip = "", ipStatus = "", port = "", remoteOs = "";
            bool idOk = false;
            try
            {
                if (kind.Name == "AcceptedFrom" && AcceptId.Match(msg) is { Success: true } a) remoteId = RemoteToolLogText.IdField(a.Groups["id"].Value, out idOk);
                else if (kind.Name == "IncomingSessionRequest" && RequestId.Match(msg) is { Success: true } q) remoteId = RemoteToolLogText.IdField(q.Groups["id"].Value, out idOk);
                else if (kind.Name == "LoggedInFromRelay")
                {
                    if (IpPort.Match(msg) is { Success: true } p)
                    {
                        // "[v6]:port", "v4:port" (exactly one colon) or a bare address (an IPv6 literal has several colons)
                        var tok = p.Groups["tok"].Value;
                        string host = tok, portText = "";
                        if (tok.StartsWith('[') && tok.IndexOf(']') is var close and > 0) { host = tok[1..close]; portText = tok[(close + 1)..].TrimStart(':'); }
                        else if (tok.Count(ch => ch == ':') == 1) { var colon = tok.IndexOf(':'); host = tok[..colon]; portText = tok[(colon + 1)..]; }
                        ip = RemoteToolLogText.ValidIp(host);
                        ipStatus = ip.Length > 0 ? "valid" : "invalid";
                        if (ip.Length > 0 && int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var pn) && pn is > 0 and <= 65535) port = pn.ToString(CultureInfo.InvariantCulture);
                    }
                    else ipStatus = "invalid";
                }
                else if (kind.Name == "RemoteOs")
                {
                    int at = msg.IndexOf("Remote OS", StringComparison.OrdinalIgnoreCase);
                    remoteOs = RemoteToolLogText.Sane(msg[(at + 9)..].TrimStart(':', ' '), 96);
                }
            }
            catch (RegexMatchTimeoutException) { ipStatus = kind.Name == "LoggedInFromRelay" ? "invalid" : ""; }

            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tool Id"] = ToolId, ["Tool"] = "AnyDesk", ["Session Evidence"] = "da",
                ["Kind"] = kind.Name, ["Direction"] = kind.Direction, ["Accepted"] = kind.Accepted,
                ["Level"] = level, ["Process"] = proc,
                ["Message"] = RemoteToolLogText.Sane(msg, 300),
                ["Local Time"] = timeOk ? RemoteToolLogText.LocalText(local) : "",
                ["Time Raw"] = RemoteToolLogText.Sane(rawTime, 40),
                ["Time Zone Used"] = zone.Info?.Id ?? "necunoscut",
                ["Source File"] = RemoteToolLogText.Sane(file, 128), ["Line"] = line.ToString(CultureInfo.InvariantCulture),
                ["Limitation"] = "Jurnalul poate fi falsificat de cine are acces la stație; ora este locală.",
            };
            if (remoteId.Length > 0) { f["Remote Id"] = remoteId; f["Remote Id Valid"] = idOk ? "da" : "nu"; }
            if (ipStatus.Length > 0) { f["IP Status"] = ipStatus; if (ip.Length > 0) { f["IP"] = ip; f["IP Port"] = port; f["IP Meaning"] = "adresă văzută pe releu; nu este dovedită ca adresa partenerului"; } }
            if (remoteOs.Length > 0) f["Remote OS"] = remoteOs;
            if (truncated) f["Line Truncated"] = $"da (peste {RemoteToolLogText.MaxLineChars} de caractere)";
            if (lt.Problem.Length > 0) f["Time Problem"] = lt.Problem;
            var detail = kind.Name switch
            {
                "AcceptedFrom" or "IncomingSessionRequest" => idOk ? $", ID {remoteId}" : remoteId.Length > 0 ? ", ID de format necunoscut" : "",
                "LoggedInFromRelay" => ip.Length > 0 ? $", adresa {ip}" : ", adresă invalidă ignorată",
                _ => "",
            };
            sink.Add(new TimelineEvent
            {
                Time = lt.Time,
                TimeSemantics = "Ora rândului din jurnalul AnyDesk: ora locală a stației, fără fus orar în fișier",
                TimeZoneBasis = lt.Basis, TimeUncertainty = lt.Uncertainty,
                Source = EventSource, EvidenceId = item.EvidenceId, Provider = "AnyDesk " + RemoteToolLogText.Sane(file, 64), EventId = kind.Name,
                Summary = $"AnyDesk ({level}, proces {proc}): {kind.Phrase}{detail}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={line}", Fields = f,
            });
            result.Records++;
        }
    }
}
