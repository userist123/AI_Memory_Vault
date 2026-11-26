using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private const string SmbServerSecurity = "Microsoft-Windows-SMBServer/Security";
    private const string SmbClientSecurity = "Microsoft-Windows-SmbClient/Security";

    /// <summary>Distinct servers whose administrative shares must be refused to this station inside <see cref="ShareScanWindow"/> before the scan finding is added.</summary>
    internal const int ShareScanServers = 5;

    /// <summary>The window of the multi-server finding: six credential windows (3 hours).</summary>
    internal static readonly TimeSpan ShareScanWindow = TimeSpan.FromTicks(Correlation.CredentialWindow.Ticks * 6);

    /// <summary>NTSTATUS values with a meaning from the public Microsoft NTSTATUS list; any other code is shown raw. Input: "0xc000006d" or decimal.</summary>
    internal static (string Hex, string Meaning) NtStatus(string raw)
    {
        raw = raw.Trim();
        uint v;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? !uint.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                                                                      : !uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out v))
            return (raw.Length == 0 ? "necunoscut" : raw, "");
        var hex = "0x" + v.ToString("X8", CultureInfo.InvariantCulture);
        return (hex, hex switch
        {
            "0xC000006D" => "STATUS_LOGON_FAILURE",
            "0xC0000072" => "STATUS_ACCOUNT_DISABLED",
            "0xC0000022" => "STATUS_ACCESS_DENIED",
            "0xC0000064" => "STATUS_NO_SUCH_USER",
            "0xC000006A" => "STATUS_WRONG_PASSWORD",
            "0xC0000234" => "STATUS_ACCOUNT_LOCKED_OUT",
            _ => "",
        });
    }

    private static string StatusText((string Hex, string Meaning) s) => s.Meaning.Length > 0 ? $"{s.Hex} ({s.Meaning})" : s.Hex;

    /// <summary>
    /// The client of SMBServer/Security 551: a sockaddr_in as hex ("0200" family little-endian, port big-endian, 4 address bytes). Only the IPv4 layout
    /// (family 2) was seen in a real event; anything else is not decoded (family 23 is flagged as an undecoded IPv6 address).
    /// </summary>
    internal static (string? Ip, int Port, bool Ipv6Raw) DecodeClientAddress(string hex)
    {
        hex = hex.Trim();
        if (hex.Length < 16 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit)) return (null, 0, false);
        var b = Convert.FromHexString(hex);
        int family = b[0] | b[1] << 8;
        if (family == 2) return ($"{b[4]}.{b[5]}.{b[6]}.{b[7]}", b[2] << 8 | b[3], false);
        return (null, 0, family == 23);
    }

    private static void SmbAuth(Ctx c, List<Finding> o)
    {
        SmbServerAuthFail(c, o);
        SmbClientShareDenied(c, o);
    }

    private static void SmbServerAuthFail(Ctx c, List<Finding> o)
    {
        var rows = c.Events.Where(e => Ev(e, SmbServerSecurity, 551) && T(e) is not null)
            .Select(e =>
            {
                var raw = F(e, "ClientAddress").Trim();
                var (ip, port, v6) = DecodeClientAddress(raw);
                var name = F(e, "ClientName").Trim().TrimStart('\\');
                // review: undecoded hex still carries the source port (bytes 2-3), new at every connection; without it one series is not split under the threshold
                var rawNoPort = raw.Length >= 8 ? raw[..4] + raw[8..] : raw;
                var label = ip ?? (name.Length > 0 ? $"{name} (nume declarat de client)" : raw.Length == 0 ? "adresă necunoscută"
                                   : v6 ? $"adresă IPv6 nedecodată ({rawNoPort}, fără port)" : $"adresă nedecodată ({rawNoPort}, fără port)");
                return (E: e, Ip: ip, Port: port, Name: name, Label: label, Status: NtStatus(F(e, "Status", "TranslatedStatus")));
            })
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var g in rows)
        {
            var items = g.OrderBy(x => T(x.E)).ToList();
            if (Correlation.FindBurst(items.Select(x => (T(x.E)!.Value, "")).ToList(), Correlation.CredentialThreshold, Correlation.CredentialWindow, distinct: false) is not { } b) continue;
            var w = items.Skip(b.Start).Take(b.End - b.Start + 1).ToList();
            var first = T(w[0].E)!.Value; var last = T(w[^1].E)!.Value;
            var label = w[0].Label;
            var statuses = w.GroupBy(x => x.Status.Hex).OrderByDescending(s => s.Count())
                .Select(s => $"{s.Count()} × {StatusText(s.First().Status)}").ToList();
            var names = w.Select(x => x.Name).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var ports = w.Where(x => x.Port > 0).Select(x => x.Port).Distinct().OrderBy(p => p).ToList();
            var ipKey = w[0].Ip ?? "";
            var corroborating = ipKey.Length == 0 ? [] : c.Existing.Where(x => x.RuleId == "CRED-BRUTEFORCE" && x.Ip.Trim() == ipKey &&
                                                                       x.FirstSeenUtc is { } fs && fs <= last && (x.LastSeenUtc ?? fs) >= first).ToList();
            var desc = $"Serverul SMB al acestei stații a refuzat {w.Count} deschideri de sesiune de la {label}, între {Time(first)} și {Time(last)} " +
                       $"(cel puțin {Correlation.CredentialThreshold} în {Correlation.CredentialWindow.TotalMinutes:0} minute). Stări: {string.Join("; ", statuses)}. " +
                       (names.Count > 0 ? $"Nume declarat de client: {Join(names, 3)}. " : "") +
                       (ports.Count > 0 ? $"Porturi sursă: {Join(ports.Select(p => p.ToString(CultureInfo.InvariantCulture)), 5)}. " : "") +
                       "Evenimentul nu înregistrează numele contului încercat: contul se caută în Security 4625 (tip de autentificare 3) pe aceeași stație sau în 4771/4776 pe controlerul de domeniu." +
                       (corroborating.Count > 0 ? $" Aceeași adresă apare și în {string.Join(", ", corroborating.Select(x => $"{x.RuleId} {x.FindingId}"))}, în aceeași perioadă (coroborare; nu o dovadă pentru cont)." : "");
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "SMB-SERVER-AUTH-FAIL", Title = $"{w.Count} sesiuni SMB refuzate de la {label}",
                Severity = Severity.Medium, Category = "Credential Access", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1110",
                FirstSeenUtc = first, LastSeenUtc = last, Ip = ipKey, Host = w[0].E.Host, Description = desc,
                PlainWhat = $"Acest calculator a refuzat {PlainPhrases.Times(w.Count)} o conectare la partajările lui de rețea venită de la {label}",
                ClassificationReason = "Evenimente SMBServer/Security 551 (deschiderea sesiunii a eșuat) grupate după adresa clientului, cel puțin pragul de autentificări eșuate într-o fereastră scurtă.",
                SemanticType = SemanticType.Observation, RelatedFindingIds = corroborating.Select(x => x.FindingId).ToList(),
                SupportingEvidence = w.Take(20).Select(x => Ref(x.E, $"551 {StatusText(x.Status)}")).ToList(),
                AlternativeExplanations =
                [
                    "O unitate de rețea mapată sau un serviciu cu o parolă veche salvată reîncearcă singur.",
                    "Un cont dezactivat sau de tip invitat folosit ca rezervă (0xC0000072) produce același refuz fără ca cineva să ghicească parole.",
                    .. (ipKey.StartsWith("127.", StringComparison.Ordinal)
                        ? new[] { "Adresa este chiar această stație (127.x): un program sau serviciu local care se conectează la propriile partajări cu o parolă greșită." } : []),
                ],
            });
        }
    }

    private static void SmbClientShareDenied(Ctx c, List<Finding> o)
    {
        var rows = c.Events.Where(e => Ev(e, SmbClientSecurity, 31010) && T(e) is not null)
            .Select(e =>
            {
                var parts = F(e, "ShareName").Trim().TrimStart('\\').Split('\\', 2);
                var share = parts.Length > 1 ? parts[1].Trim('\\') : "";
                var kind = share.Length > 0 ? AdminShareKind(share) : null;
                return (E: e, Server: parts[0].Trim(), Share: share, Kind: kind is "IPC$" ? null : kind, Status: NtStatus(F(e, "Status")));
            })
            .Where(x => x.Server.Length > 0 && x.Kind is not null).ToList();
        foreach (var g in rows.GroupBy(x => x.Server, StringComparer.OrdinalIgnoreCase))
        {
            var items = g.OrderBy(x => T(x.E)).ToList();
            var shares = items.Select(x => x.Kind!).Distinct().ToList();
            var statuses = items.GroupBy(x => x.Status.Hex).Select(s => $"{s.Count()} × {StatusText(s.First().Status)}").ToList();
            var first = T(items[0].E)!.Value; var last = T(items[^1].E)!.Value;
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "SMB-CLIENT-SHARE-DENIED", Title = $"Acces refuzat la partajarea administrativă {string.Join(", ", shares.Select(s => $@"\\{g.Key}\{s}"))}",
                Severity = Severity.Low, Category = "Lateral Movement", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1021.002",
                FirstSeenUtc = first, LastSeenUtc = last, Host = items[0].E.Host, Ip = IpClassifier.Classify(g.Key) is IpScope.Invalid ? "" : g.Key,
                Description = $"Această stație a încercat să deschidă \\\\{g.Key}\\{string.Join(", ", shares)} și a fost refuzată: {string.Join("; ", statuses)}, {Time(first)} – {Time(last)}. " +
                              "Evenimentul nu înregistrează contul folosit și nu arată dacă încercarea a venit de la o persoană sau de la un program.",
                PlainWhat = $"Acest calculator a încercat să deschidă partajarea administrativă {PlainPhrases.Q($@"\\{g.Key}\{shares[0]}")} a altui calculator și accesul i-a fost refuzat",
                ClassificationReason = "Evenimente SmbClient/Security 31010 (accesul la o partajare a fost refuzat clientului) pentru o partajare administrativă (C$, ADMIN$ sau altă literă urmată de $).",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = items.Take(20).Select(x => Ref(x.E, $@"31010 \\{x.Server}\{x.Share} {StatusText(x.Status)}")).ToList(),
                AlternativeExplanations = ["Administrare legitimă făcută cu un cont fără drepturi pe acel server (de exemplu un cont local filtrat de UAC la distanță).", "Un program de inventar sau de backup care verifică partajările administrative."],
            });
        }
        var ordered = rows.OrderBy(x => T(x.E)).ToList();
        if (Correlation.FindBurst(ordered.Select(x => (T(x.E)!.Value, x.Server.ToLowerInvariant())).ToList(), ShareScanServers, ShareScanWindow, distinct: true) is { } b)
        {
            var w = ordered.Skip(b.Start).Take(b.End - b.Start + 1).ToList();
            var servers = w.Select(x => x.Server).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var first = T(w[0].E)!.Value; var last = T(w[^1].E)!.Value;
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "SMB-CLIENT-SHARE-DENIED", Title = $"încercări de acces la partajări administrative pe {servers.Count} servere",
                Severity = Severity.Medium, Category = "Discovery", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1135",
                FirstSeenUtc = first, LastSeenUtc = last, Host = w[0].E.Host,
                Description = $"Această stație a fost refuzată la partajări administrative pe {servers.Count} servere diferite ({Join(servers, 8)}) între {Time(first)} și {Time(last)} " +
                              $"(cel puțin {ShareScanServers} servere în {ShareScanWindow.TotalHours:0} ore). Evenimentul nu înregistrează contul folosit și nu arată intenția.",
                PlainWhat = $"Acest calculator a încercat să deschidă partajările administrative a {servers.Count} alte calculatoare și accesul i-a fost refuzat de fiecare dată",
                ClassificationReason = "Evenimente SmbClient/Security 31010 pentru partajări administrative refuzate pe cel puțin pragul de servere diferite într-o fereastră scurtă.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = w.Take(20).Select(x => Ref(x.E, $@"31010 \\{x.Server}\{x.Share} {StatusText(x.Status)}")).ToList(),
                AlternativeExplanations = ["Un instrument de inventar sau de administrare care parcurge o listă de servere cu un cont fără drepturi.", "Verificarea manuală a mai multor servere de către un administrator."],
            });
        }
    }
}
