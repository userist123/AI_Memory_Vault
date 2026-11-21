using LogAnalyzer.Dfir.Model;
using System.Text.Json;
using LogAnalyzer.Dfir.Network;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Planner #11 A6: findings on the entries of the hosts file (events of <see cref="HostsFileParser"/>). Only what the file itself shows:
/// <list type="bullet">
/// <item>HOSTS-SECURITY-DOMAIN: an entry for a Microsoft / Windows Update / antivirus-vendor domain: High when it maps the name to an address that is neither loopback nor
/// unspecified (a redirect), Medium when it maps it to loopback or 0.0.0.0 (the name is blocked).</item>
/// <item>HOSTS-REDIRECT: any other entry that maps a name to an address that is neither loopback nor unspecified: Low (administrators, VPN and container tools add such lines).</item>
/// </list>
/// Entries to loopback / 0.0.0.0 for other names (including the default "127.0.0.1 localhost" and ad-blocking lists) produce no finding; they stay in the timeline.
/// The wording states the line as a fact (it is in the file); that the name was really redirected is not claimed (DNS cache, proxy, DNS over HTTPS).
/// </summary>
public static class HostsFileRules
{
    public const int MaxFindings = 100;

    /// <summary>Domains of the operating system's updates and security and of the usual antivirus vendors; a name equal to one of them or below it matches.</summary>
    public static readonly string[] SecurityDomains =
    [
        "microsoft.com", "windowsupdate.com", "windows.com", "windows.net", "live.com", "microsoftonline.com", "office.com", "office365.com", "msftncsi.com", "msedge.net",
        "kaspersky.com", "kaspersky-labs.com", "avast.com", "avg.com", "avira.com", "bitdefender.com", "eset.com", "mcafee.com", "norton.com", "symantec.com",
        "sophos.com", "malwarebytes.com", "trendmicro.com", "f-secure.com", "crowdstrike.com", "sentinelone.net", "virustotal.com", "comodo.com", "emsisoft.com", "gdatasoftware.com",
    ];

    /// <summary>The security domain a host name belongs to, or null.</summary>
    public static string? SecurityDomainOf(string host)
    {
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var d in SecurityDomains)
            if (h == d || h.EndsWith("." + d, StringComparison.Ordinal)) return d;
        return null;
    }

    /// <summary>One row of Data/benign_hosts.json: an entry software adds on purpose (Docker Desktop).</summary>
    public sealed class BenignHostsRow
    {
        public string Id { get; set; } = "";
        public string Reason { get; set; } = "";
        public List<string> Hostnames { get; set; } = [];
        public List<string> SectionContains { get; set; } = [];
    }

    private sealed class BenignHostsFile { public List<BenignHostsRow> Entries { get; set; } = []; }

    private static readonly Lazy<List<BenignHostsRow>> Benign = new(() =>
    {
        try { return JsonSerializer.Deserialize<BenignHostsFile>(ShippedData.Read("benign_hosts.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Entries ?? []; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException) { return []; }
    });

    /// <summary>The known-benign row an entry belongs to (all its names listed, or it sits in a section the row names), or null.</summary>
    public static BenignHostsRow? BenignRowOf(string[] names, string section)
    {
        if (names.Length == 0) return null;
        foreach (var r in Benign.Value)
        {
            if (r.Hostnames.Count > 0 && names.All(n => r.Hostnames.Contains(n.TrimEnd('.'), StringComparer.OrdinalIgnoreCase))) return r;
            if (section.Length > 0 && r.SectionContains.Any(x => x.Length > 0 && section.Contains(x, StringComparison.OrdinalIgnoreCase))) return r;
        }
        return null;
    }

    public static List<Finding> Run(IReadOnlyList<TimelineEvent> events, Func<string> nextId)
    {
        var found = new List<(int Rank, Func<Finding> Make)>();
        foreach (var e in events.Where(e => e.Source == HostsFileParser.EventSource && e.EventId == "Entry"))
        {
            if (!e.Fields.TryGetValue("Valid", out var valid) || valid != "da") continue;
            var ipText = e.Fields.GetValueOrDefault("IpAddress", "");
            if (!System.Net.IPAddress.TryParse(ipText, out var ip)) continue;
            var names = e.Fields.GetValueOrDefault("Hostnames", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool loop = HostsFileParser.IsLoopback(ip), unspec = HostsFileParser.IsUnspecified(ip);
            var sec = names.Select(n => (Name: n, Domain: SecurityDomainOf(n))).Where(x => x.Domain is not null).ToList();
            var raw = e.Fields.GetValueOrDefault("RawLine", "");
            var line = e.Fields.GetValueOrDefault("Line", "");
            var rd = $"Fișierul hosts, rândul {line}";
            if (sec.Count > 0)
            {
                bool redirect = !loop && !unspec;
                var shown = string.Join(", ", sec.Select(x => x.Name).Take(5));
                found.Add((redirect ? 0 : 1, () => new Finding
                {
                    FindingId = nextId(), RuleId = "HOSTS-SECURITY-DOMAIN",
                    Title = redirect ? $"Fișierul hosts mapează un nume de actualizare sau securitate la o altă adresă: {shown}" : $"Fișierul hosts blochează un nume de actualizare sau securitate: {shown}",
                    Severity = redirect ? Severity.High : Severity.Medium, Category = "Defense Evasion",
                    Classification = Classification.Direct, Confidence = e.Confidence, MitreTechniqueId = "T1565.001",
                    Host = e.Host,
                    PlainWhat = redirect ? $"fișierul hosts trimite {shown} la adresa {ipText}" : $"fișierul hosts trimite {shown} la {ipText}, adică numele nu se mai rezolvă către serverul real",
                    Description = $"Ce s-a întâmplat: în fișierul hosts, rândul {line}, este scris „{Visible(raw)}”. Numele {Visible(shown)} aparține domeniului {sec[0].Domain}, folosit de actualizările Windows, de Microsoft sau de un producător de antivirus. " +
                                  (redirect ? $"Rândul îl mapează la {ipText}, o adresă care nu este de loopback. " : $"Rândul îl mapează la {ipText}, deci rezolvarea numelui este blocată. ") +
                                  "De ce contează: mutarea sau blocarea acestor nume împiedică actualizările și verificările de securitate; dar unele programe de confidențialitate sau de administrare scriu astfel de rânduri. Ce verificați: cine a adăugat rândul și când (fișierul nu are ore; ora ultimei modificări se vede în proprietățile fișierului).",
                    ClassificationReason = "Rândul este în copia fișierului hosts din caz (Direct). Faptul că numele a fost într-adevăr rezolvat așa pe stație nu este dovedit (cache DNS, proxy, DNS peste HTTPS); intenția nu este dovedită.",
                    SupportingEvidence = [new EvidenceRef(e.EvidenceId, e.Locator, rd, e.SourceSha256)],
                    AlternativeExplanations = ["Program de confidențialitate sau de blocare a telemetriei care scrie în hosts.", "Administrator care a mapat un nume la un server intern sau de test."],
                    RecommendedNextSteps = ["Verificați cine a adăugat rândul și dacă este aprobat, apoi comparați cu fișierul hosts de pe stație."],
                }));
            }
            else if (!loop && !unspec)
            {
                var shown = string.Join(", ", names.Take(5));
                var benign = BenignRowOf(names, e.Fields.GetValueOrDefault("Section", ""));
                found.Add((benign is null ? 2 : 3, () => new Finding
                {
                    FindingId = nextId(), RuleId = "HOSTS-REDIRECT",
                    Title = $"Fișierul hosts mapează un nume la o adresă de rețea: {shown}",
                    Severity = benign is null ? Severity.Low : Severity.Info, Category = "Defense Evasion",
                    Classification = benign is null ? Classification.Direct : Classification.BenignKnown, Confidence = e.Confidence, MitreTechniqueId = "T1565.001",
                    Host = e.Host,
                    PlainWhat = $"fișierul hosts mapează {shown} la adresa {ipText}",
                    Description = $"Ce s-a întâmplat: în fișierul hosts, rândul {line}, este scris „{Visible(raw)}”. Rândul mapează {Visible(shown)} la {ipText}, o adresă care nu este de loopback. " +
                                  "De ce contează: o mapare manuală poate muta un serviciu către alt server; este și o practică obișnuită pentru servere interne, VPN sau unelte pentru containere. Ce verificați: dacă maparea este cunoscută și aprobată (fișierul nu are ore).",
                    ClassificationReason = benign is null ? "Rândul este în copia fișierului hosts din caz (Direct); nu este dovedit că numele a fost rezolvat așa și nici scopul rândului."
                        : $"{char.ToUpperInvariant(benign.Reason[0]) + benign.Reason[1..]} (nume sau bloc din lista de intrări cunoscute); rândul este în copia fișierului hosts din caz.",
                    SupportingEvidence = [new EvidenceRef(e.EvidenceId, e.Locator, rd, e.SourceSha256)],
                    AlternativeExplanations = ["Server intern, VPN sau unealtă pentru containere/virtualizare care scrie în hosts."],
                    RecommendedNextSteps = ["Verificați dacă maparea este cunoscută și aprobată."],
                }));
            }
        }
        var ordered = found.OrderBy(x => x.Rank).Take(MaxFindings).Select(x => x.Make()).ToList();
        if (found.Count > MaxFindings)
            ordered.Add(new Finding
            {
                FindingId = nextId(), RuleId = "HOSTS-REDIRECT", Title = $"Alte {found.Count - MaxFindings} intrări hosts nu au fost listate individual",
                Severity = Severity.Low, Category = "Defense Evasion", Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = "T1565.001",
                PlainWhat = $"Mai multe intrări din fișierul hosts ({found.Count - MaxFindings}, peste cele {MaxFindings} afișate) nu au fost listate individual; ele se găsesc în cronologie",
                Description = $"Raportul listează primele {MaxFindings} de intrări, cele pentru domenii de securitate întâi; restul sunt în cronologie (evenimente HostsFile).",
                ClassificationReason = "Limită de afișare, nu o concluzie.",
            });
        return ordered;
    }

    private static string Visible(string s) => Correlation.Visible(s.ReplaceLineEndings(" ").Length > 300 ? s.ReplaceLineEndings(" ")[..300] + "…" : s.ReplaceLineEndings(" "));
}
