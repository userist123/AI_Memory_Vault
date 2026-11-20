using System.Globalization;
using System.Net;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;

namespace LogAnalyzer.Dfir.Network;

/// <summary>
/// The hosts file (<c>%SystemRoot%\System32\drivers\etc\hosts</c>): one timeline event per entry (a non-blank line that is not a comment; the part after "#" is a comment),
/// locator <c>line=N</c>, the original line unchanged in <c>Fields["RawLine"]</c>. The file has no times: events have an unknown time. An entry whose first token is not an
/// IP address is kept and marked invalid (Windows ignores it).
/// </summary>
public sealed class HostsFileParser : EvidenceParserBase
{
    public const string SourceTypeName = "hosts_file";
    public const string EventSource = "HostsFile";
    public const long MaxFileBytes = 8L * 1024 * 1024;
    public const int MaxLines = 200_000;
    public const int MaxLineChars = 8_192;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "HostsFileParser", Version = "1.0", Artifact = "Fișierul hosts (System32\\drivers\\etc\\hosts)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = LogAnalyzer.Dfir.Scripting.PsReadLineHistoryParser.AcceptedFormats, MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows (text, citit în cod gestionat)",
        FormatVersions = ["Text: adresă IP, unul sau mai multe nume, comentariu după „#”"],
        Limitations =
        [
            "Fișierul nu are ore: nu se poate spune când a fost adăugată o intrare (ora ultimei modificări a fișierului nu este ora intrării).",
            "Se citește copia fișierului din momentul colectării; o intrare ștearsă înainte nu mai apare. Un program de securitate sau un administrator poate adăuga legitim intrări.",
            "Rezolvarea numelor depinde și de cache-ul DNS, de proxy și de politicile browserului (DNS peste HTTPS ignoră fișierul hosts): o intrare nu dovedește că un nume a fost într-adevăr redirecționat.",
            $"Cel mult {MaxLineChars} de caractere pe rând și {MaxLines} de rânduri; restul este raportat ca lacună.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "HostsFileTests: fișiere hosts sintetice (implicit cu loopback, intrări de redirecționare, comentarii, rând invalid, locator line=N, RawLine neschimbat)",
    };

    /// <summary>True for 127.0.0.0/8 and ::1.</summary>
    public static bool IsLoopback(IPAddress ip) => IPAddress.IsLoopback(ip);
    /// <summary>True for the unspecified addresses 0.0.0.0 and :: (the usual way to block a name).</summary>
    public static bool IsUnspecified(IPAddress ip) => ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any);

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
        int lineNo = 0, longLines = 0;
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = RemoteToolLogText.OpenReader(fs);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            lineNo++;
            if (lineNo > MaxLines)
            {
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxLines} de rânduri; restul nu au fost citite", "Intrările de după limită lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            if ((lineNo & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            if (lineNo == 1) line = line.TrimStart('﻿');
            var raw = line;
            if (raw.Length > MaxLineChars)
            {
                longLines++;
                raw = raw[..(char.IsHighSurrogate(raw[MaxLineChars - 1]) ? MaxLineChars - 1 : MaxLineChars)];
            }
            int hash = raw.IndexOf('#');
            var body = hash >= 0 ? raw[..hash] : raw;
            var tokens = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            var ipText = tokens[0];
            var names = tokens.Skip(1).Select(t => RemoteToolLogText.Sane(t, 255)).ToList();
            var addr = ipText.Length <= 64 && IPAddress.TryParse(ipText.Split('%')[0], out var ip) ? ip : null;
            bool valid = addr is not null && names.Count > 0;
            string kind = addr is null ? "adresă invalidă" : IsLoopback(addr) ? "loopback" : IsUnspecified(addr) ? "adresă nespecificată (blocare)" : "adresă de rețea";
            if (!valid) result.MalformedRecords++;
            var ipShown = addr is null ? RemoteToolLogText.Sane(ipText, 64) : addr.ToString();
            sink.Add(new TimelineEvent
            {
                Time = Timestamp.Unknown(),
                TimeSemantics = "Fișierul hosts nu are ore",
                Source = EventSource, EvidenceId = item.EvidenceId, Provider = "hosts", EventId = "Entry",
                Dns = names.Count > 0 ? names[0] : "",
                Summary = $"Intrare hosts: {ipShown} → {string.Join(" ", names)}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = valid ? Confidence.High : Confidence.Medium,
                Locator = $"line={lineNo}",
                Fields =
                {
                    ["Artifact"] = "hosts file", ["IpAddress"] = ipShown, ["Hostnames"] = string.Join(" ", names), ["AddressKind"] = kind,
                    ["Valid"] = valid ? "da" : "nu (Windows ignoră rândul)", ["RawLine"] = raw,
                    ["Line"] = lineNo.ToString(CultureInfo.InvariantCulture), ["Source File"] = RemoteToolLogText.Sane(file, 128),
                },
            });
            result.Records++;
        }
        if (longLines > 0)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"{longLines} rânduri depășesc {MaxLineChars} de caractere și au fost tăiate", "Sfârșitul rândurilor lungi lipsește din cronologie", "Fișierul brut din caz", "Posibil"));
    }
}
