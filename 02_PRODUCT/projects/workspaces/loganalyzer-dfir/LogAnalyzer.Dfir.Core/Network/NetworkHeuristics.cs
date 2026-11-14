using System.Globalization;
using System.Net;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Network;

/// <summary>
/// A network connection seen by a sensor (Zeek, PCAP) linked to the process that opened it on the endpoint (Sysmon 3). The link stands on the exact
/// tuple (source IP and port, destination IP and port) and a time difference within the window; without both there is no link, never a guess.
/// </summary>
public sealed record EndpointLink(string NetworkEvidenceId, string NetworkLocator, string NetworkSource, string EndpointEvidenceId, string EndpointLocator,
    string Host, string Image, string ProcessGuid, int? Pid, string SrcIp, string SrcPort, string DstIp, int DstPort, double DeltaSeconds, IReadOnlyList<string> Reasons);

public sealed record NetworkHeuristicsResult(IReadOnlyList<Finding> Findings, IReadOnlyList<EvidenceGap> Gaps, IReadOnlyList<EndpointLink> Links)
{
    public const string LinksRelPath = "Analysis/network_links.json";
}

/// <summary>
/// Master plan 6.5: flow heuristics over the connections in the case timeline: beaconing (regular intervals to one destination), port and host scans,
/// and the endpoint process behind a sensor-observed connection. Each finding is a candidate with its numbers (count, interval, jitter, window) and
/// the rows it stands on. Sources are kept apart (Zeek, PCAP, Sysmon), so one connection seen by two sensors is never counted twice.
/// </summary>
public static class NetworkHeuristics
{
    public const int MinBeaconConnections = 8;
    public const double BeaconTolerance = 0.20;          // an interval within ±20% of the median counts as regular
    public const double BeaconRegularShare = 0.80;       // at least 80% of the intervals must be regular
    public static readonly TimeSpan MinBeaconInterval = TimeSpan.FromSeconds(10), MaxBeaconInterval = TimeSpan.FromHours(6);
    public const int ScanThreshold = 20;
    public static readonly TimeSpan ScanWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan LinkWindow = TimeSpan.FromSeconds(120);
    public const int MaxPerRule = 200, MaxLinks = 50_000, MaxEvidencePerFinding = 25;
    private const string SysmonChannel = "EventLog:Microsoft-Windows-Sysmon/Operational";

    /// <summary>Destination ports of infrastructure services that talk on a schedule by design (DNS, Kerberos, NTP, LDAP, SMB, RPC, DHCP, GC).</summary>
    private static readonly HashSet<int> InfraPorts = [53, 67, 68, 88, 123, 135, 137, 138, 389, 445, 464, 636, 3268, 3269];

    private sealed record Conn(DateTimeOffset T, string Family, string Src, string SrcPort, string Dst, int DstPort, TimelineEvent E);

    private static string F(TimelineEvent e, string k) => e.Fields.GetValueOrDefault(k, "").Trim();
    private static int Port(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 65535 ? p : -1;
    private static EvidenceRef Ref(TimelineEvent e, string why) => new(e.EvidenceId, e.Locator, why, e.SourceSha256);

    /// <summary>One spelling per address (IPv4-mapped IPv6 as IPv4, IPv6 lower-case and compressed), so a sensor and Sysmon writing it differently still match.</summary>
    private static string Norm(string ip)
    {
        if (!IPAddress.TryParse(ip.Trim(), out var a)) return ip.Trim();
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        return a.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// A destination worth counting: not loopback, unspecified, the limited broadcast, multicast or reserved. A subnet broadcast cannot be told from a host
    /// without the netmask, so x.y.z.255 stays (a beacon to it is visible and explained, never hidden).
    /// </summary>
    private static bool Unicastable(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return false;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any) || a.Equals(IPAddress.Broadcast)) return false;
        var b = a.GetAddressBytes();
        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return b[0] is < 224 and not 0;
        return !a.IsIPv6Multicast;
    }

    private static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return false;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        var b = a.GetAddressBytes();
        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254;
        return a.IsIPv6LinkLocal || a.IsIPv6UniqueLocal || a.IsIPv6SiteLocal;
    }

    /// <summary>The connections of the timeline, one per sensor row, with the family that saw them. Rows without a usable time or address are skipped.</summary>
    private static List<Conn> Connections(IReadOnlyList<TimelineEvent> timeline)
    {
        var list = new List<Conn>();
        foreach (var e in timeline)
        {
            if (e.Time.Utc is not { } t) continue;
            Conn? c = e.Source switch
            {
                "Zeek:conn" => new Conn(t, "Zeek", F(e, "OrigH"), F(e, "OrigP"), F(e, "RespH"), Port(F(e, "RespP")), e),
                "PCAP:flow" => new Conn(t, "PCAP", F(e, "ClientIP"), F(e, "ClientPort"), F(e, "ServerIP"), Port(F(e, "ServerPort")), e),
                SysmonChannel when e.EventId == "3" && !F(e, "Initiated").Equals("false", StringComparison.OrdinalIgnoreCase)
                    => new Conn(t, "Sysmon", F(e, "SourceIp"), F(e, "SourcePort"), F(e, "DestinationIp"), Port(F(e, "DestinationPort")), e),
                _ => null,
            };
            if (c is not null && c.Src.Length > 0 && c.DstPort >= 0 && Unicastable(c.Dst)) list.Add(c with { Src = Norm(c.Src), Dst = Norm(c.Dst) });
        }
        return list.OrderBy(c => c.T).ThenBy(c => c.E.EvidenceId, StringComparer.Ordinal).ThenBy(c => c.E.Locator, StringComparer.Ordinal).ToList();
    }

    public static NetworkHeuristicsResult Run(IReadOnlyList<TimelineEvent> timeline, Func<string> nextId)
    {
        var conns = Connections(timeline);
        var findings = new List<Finding>();
        var gaps = new List<EvidenceGap>();
        var links = Links(conns, gaps);
        if (conns.Count == 0) return new(findings, gaps, links);

        var linkByRow = links.GroupBy(l => (l.NetworkEvidenceId, l.NetworkLocator)).ToDictionary(g => g.Key, g => g.First());
        Beacons(conns, linkByRow, findings, gaps, nextId);
        Scans(conns, findings, gaps, nextId);

        bool sensors = conns.Any(c => c.Family is "Zeek" or "PCAP"), sysmon = conns.Any(c => c.Family == "Sysmon");
        if (sensors && !sysmon)
            gaps.Add(new EvidenceGap("Corelarea proces–rețea", EvidenceStatus.Partial,
                "conexiunile vin doar de la senzori de rețea (Zeek sau PCAP), fără evenimente Sysmon 3 de pe stații",
                "Conexiunile nu pot fi atribuite unui proces: senzorul vede adrese și porturi, nu programe", "Importați jurnalul Sysmon al stației sursă", "Posibil"));
        return new(findings, gaps, links);
    }

    // ---- endpoint process ↔ network connection ----

    private static List<EndpointLink> Links(List<Conn> conns, List<EvidenceGap> gaps)
    {
        var sysmon = conns.Where(c => c.Family == "Sysmon").ToLookup(c => (c.Src, c.SrcPort, c.Dst, c.DstPort));
        var links = new List<EndpointLink>();
        foreach (var n in conns.Where(c => c.Family is "Zeek" or "PCAP"))
        {
            if (n.SrcPort.Length == 0) continue;
            var candidates = sysmon[(n.Src, n.SrcPort, n.Dst, n.DstPort)].Where(s => (s.T - n.T).Duration() <= LinkWindow).ToList();
            if (candidates.Count == 0) continue;
            var best = candidates.OrderBy(s => (s.T - n.T).Duration()).First();
            var reasons = new List<string> { $"aceeași pereche sursă–destinație {n.Src}:{n.SrcPort} → {n.Dst}:{n.DstPort}", $"diferență de timp {(best.T - n.T).TotalSeconds:0.###} s (fereastră ±{LinkWindow.TotalSeconds:0} s)" };
            if (F(best.E, "ProcessGuid").Length > 0) reasons.Add("procesul are ProcessGuid (Sysmon), deci este identificat unic pe stație");
            if (candidates.Count > 1) reasons.Add($"{candidates.Count} evenimente Sysmon cu același tuplu în fereastră: s-a ales cel mai apropiat în timp");
            links.Add(new EndpointLink(n.E.EvidenceId, n.E.Locator, n.E.Source, best.E.EvidenceId, best.E.Locator, best.E.Host, F(best.E, "Image"), F(best.E, "ProcessGuid"),
                int.TryParse(F(best.E, "ProcessId"), out var pid) ? pid : best.E.Pid, n.Src, n.SrcPort, n.Dst, n.DstPort, Math.Round((best.T - n.T).TotalSeconds, 3), reasons));
            if (links.Count >= MaxLinks)
            {
                gaps.Add(new EvidenceGap("Corelarea proces–rețea", EvidenceStatus.Partial, $"limita de {MaxLinks:N0} legături atinsă", "Restul conexiunilor nu au fost legate de procese", "Restrângeți intervalul analizat", "Posibil"));
                break;
            }
        }
        return links;
    }

    // ---- beaconing ----

    private static void Beacons(List<Conn> conns, Dictionary<(string, string), EndpointLink> linkByRow, List<Finding> findings, List<EvidenceGap> capGaps, Func<string> nextId)
    {
        var reported = new HashSet<(string, string, int)>();
        int count = 0;
        // one family at a time, the richer sensor first, so the same connections seen by Zeek and PCAP make one finding
        foreach (var family in new[] { "Zeek", "PCAP", "Sysmon" })
            foreach (var g in conns.Where(c => c.Family == family).GroupBy(c => (c.Src, c.Dst, c.DstPort)).OrderBy(g => g.Key.Src, StringComparer.Ordinal).ThenBy(g => g.Key.Dst, StringComparer.Ordinal).ThenBy(g => g.Key.DstPort))
            {
                if (count >= MaxPerRule) { capGaps.Add(Cap("NET-BEACON")); return; }
                var times = g.Select(c => c.T).Distinct().Order().ToList();
                if (times.Count < MinBeaconConnections || reported.Contains(g.Key)) continue;
                var gaps = times.Zip(times.Skip(1), (a, b) => (b - a).TotalSeconds).ToList();
                var sorted = gaps.Order().ToList();
                double median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
                if (median < MinBeaconInterval.TotalSeconds || median > MaxBeaconInterval.TotalSeconds) continue;
                double regular = gaps.Count(x => Math.Abs(x - median) <= BeaconTolerance * median) / (double)gaps.Count;
                if (regular < BeaconRegularShare) continue;
                double mean = gaps.Average(), sd = Math.Sqrt(gaps.Sum(x => (x - mean) * (x - mean)) / gaps.Count);
                reported.Add(g.Key);
                count++;
                var rows = g.ToList();
                // a process is named only when every row that has one names the same; several processes are listed, none is chosen
                var rowLinks = rows.Select(c => linkByRow.GetValueOrDefault((c.E.EvidenceId, c.E.Locator))).Where(l => l is not null).Select(l => l!).ToList();
                var images = (family == "Sysmon" ? rows.Select(c => F(c.E, "Image")) : rowLinks.Select(l => l.Image)).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                // one program can run as several processes: a PID is given only when every link names the same process instance
                var instances = rowLinks.Select(l => l.ProcessGuid.Length > 0 ? l.ProcessGuid : l.Pid?.ToString() ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var link = images.Count == 1 && instances == 1 ? rowLinks.FirstOrDefault() : null;
                string process = images.Count == 1 ? images[0] : "";
                int linkedRows = family == "Sysmon" ? rows.Count : rowLinks.Count;
                bool infra = InfraPorts.Contains(g.Key.DstPort) && IsPrivate(g.Key.Dst);   // DNS to a public address is not the domain controller
                var others = new[] { "Zeek", "PCAP", "Sysmon" }.Where(f => f != family && conns.Any(c => c.Family == f && c.Src == g.Key.Src && c.Dst == g.Key.Dst && c.DstPort == g.Key.DstPort)).ToList();
                var ev = rows.Take(MaxEvidencePerFinding).Select(c => Ref(c.E, $"conexiune {family} la {c.T:yyyy-MM-dd HH:mm:ss.fff} UTC")).ToList();
                foreach (var l in rowLinks.Take(MaxEvidencePerFinding)) ev.Add(new EvidenceRef(l.EndpointEvidenceId, l.EndpointLocator, $"procesul de pe stație (Sysmon 3): {l.Image}", ""));
                string desc = $"Stația {g.Key.Src} a deschis {times.Count} conexiuni către {g.Key.Dst}:{g.Key.DstPort} la intervale aproape egale: mediana {Human(median)}, "
                    + $"{regular:P0} dintre intervale în ±{BeaconTolerance:P0} din mediană, abatere standard {Human(sd)}, între {times[0]:yyyy-MM-dd HH:mm} și {times[^1]:yyyy-MM-dd HH:mm} UTC (văzut de {family}"
                    + (others.Count > 0 ? $"; aceeași destinație apare și în {string.Join(", ", others)}" : "") + "). "
                    + (images.Count > 1 ? $"Conexiunile vin de la mai multe procese ({string.Join(", ", images.Take(5))}); niciunul nu este ales. "
                       : process.Length > 0 ? $"Procesul de pe stație: {process} ({linkedRows} din {rows.Count} conexiuni legate prin tuplu). "
                       : family == "Sysmon" ? "" : "Procesul nu se poate stabili fără Sysmon 3 cu același tuplu. ")
                    + (infra ? "Portul este al unui serviciu de infrastructură care comunică periodic prin construcție (DNS, Kerberos, NTP, LDAP, SMB, RPC); regularitatea singură este de așteptat aici." : "Regularitatea este tipică pentru un program care „sună acasă” (beaconing), dar și pentru actualizări, sincronizări și monitorizare.");
                findings.Add(new Finding
                {
                    FindingId = nextId(), RuleId = "NET-BEACON", Title = $"Conexiuni periodice (posibil beaconing): {g.Key.Src} → {g.Key.Dst}:{g.Key.DstPort}, la ~{Human(median)}",
                    Severity = infra ? Severity.Low : Severity.Medium, Category = "Rețea", Classification = Classification.Candidate, Confidence = infra ? Confidence.Low : Confidence.Medium,
                    Host = rows[0].E.Host, Process = process, Pid = link?.Pid, Ip = g.Key.Dst, Description = desc, PlainWhat = desc,
                    ClassificationReason = "Euristică statistică pe intervalele dintre conexiuni; nu citește conținutul și nu dovedește un program rău-intenționat.",
                    SupportingEvidence = ev, MitreTechniqueId = "T1071",
                    AlternativeExplanations = ["Actualizare sau sincronizare programată", "Agent de monitorizare sau de securitate", "Serviciu de infrastructură (DNS, NTP, controler de domeniu)"],
                    RecommendedNextSteps = ["Identificați procesul (Sysmon 3, netscan) și verificați semnătura lui", "Verificați destinația (DNS, certificat TLS, reputație în pachetul offline de amenințări)", "Comparați cu o stație curată din aceeași rețea"],
                    Limitations = ["Pragurile: cel puțin 8 conexiuni, mediana între 10 s și 6 h, 80% dintre intervale în ±20% din mediană.", "Un beacon cu întârzieri aleatoare mari (jitter) poate să nu fie găsit."],
                });
            }
    }

    private static string Human(double seconds) => seconds switch
    {
        < 120 => $"{seconds:0.#} s", < 7200 => $"{seconds / 60:0.#} min", _ => $"{seconds / 3600:0.#} h",
    };

    // ---- scans ----

    private static EvidenceGap Cap(string rule) => new($"Euristica {rule}", EvidenceStatus.Partial, $"limita de {MaxPerRule} constatări atinsă",
        "Restul candidaților nu au fost raportați; lipsa lor nu înseamnă că nu există", "Restrângeți intervalul sau sursele analizate", "Posibil");

    private static void Scans(List<Conn> conns, List<Finding> findings, List<EvidenceGap> capGaps, Func<string> nextId)
    {
        int count = 0;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in new[] { "Zeek", "PCAP", "Sysmon" })
        {
            var fam = conns.Where(c => c.Family == family).ToList();
            // vertical: many ports of one host; horizontal: one port on many hosts
            foreach (var (kind, groups) in new[]
            {
                ("porturi", fam.GroupBy(c => (c.Src, Key: c.Dst)).Select(g => (g.Key.Src, Target: g.Key.Key, Items: g.ToList(), Distinct: (Func<Conn, string>)(c => c.DstPort.ToString(CultureInfo.InvariantCulture))))),
                ("stații", fam.GroupBy(c => (c.Src, Key: c.DstPort.ToString(CultureInfo.InvariantCulture))).Select(g => (g.Key.Src, Target: "portul " + g.Key.Key, Items: g.ToList(), Distinct: (Func<Conn, string>)(c => c.Dst)))),
            })
                foreach (var g in groups.OrderBy(x => x.Src, StringComparer.Ordinal).ThenBy(x => x.Target, StringComparer.Ordinal))
                {
                    if (count >= MaxPerRule) { capGaps.Add(Cap("NET-SCAN")); return; }
                    var key = $"{kind}|{g.Src}|{g.Target}";
                    if (reported.Contains(key)) continue;
                    var best = Window(g.Items, g.Distinct);
                    if (best.Distinct < ScanThreshold) continue;
                    reported.Add(key);
                    count++;
                    var desc = $"Stația {g.Src} a încercat {best.Distinct} {(kind == "porturi" ? $"porturi diferite pe {g.Target}" : $"stații diferite pe {g.Target}")} în {ScanWindow.TotalSeconds:0} de secunde "
                        + $"({best.Start:yyyy-MM-dd HH:mm:ss} UTC, văzut de {family}). Așa arată o scanare de {(kind == "porturi" ? "porturi" : "rețea")}; o fac și inventarierea, monitorizarea sau un scaner de vulnerabilități autorizat.";
                    findings.Add(new Finding
                    {
                        FindingId = nextId(), RuleId = "NET-SCAN", Title = $"Posibilă scanare de {(kind == "porturi" ? "porturi" : "rețea")} de la {g.Src} ({best.Distinct} {kind} în {ScanWindow.TotalSeconds:0} s)",
                        Severity = Severity.Medium, Category = "Rețea", Classification = Classification.Candidate, Confidence = Confidence.Medium,
                        Host = best.Rows[0].E.Host, Ip = g.Src, Description = desc, PlainWhat = desc,
                        ClassificationReason = "Euristică pe numărul de destinații distincte într-o fereastră de timp; nu știe dacă scanarea era autorizată.",
                        SupportingEvidence = best.Rows.Take(MaxEvidencePerFinding).Select(c => Ref(c.E, $"conexiune către {c.Dst}:{c.DstPort}")).ToList(), MitreTechniqueId = "T1046",
                        AlternativeExplanations = ["Scaner de vulnerabilități sau inventar autorizat", "Monitorizare de rețea", "Aplicație care încearcă mai multe servere la rând"],
                        RecommendedNextSteps = ["Verificați dacă sursa este un scaner autorizat", "Identificați procesul de pe stația sursă (Sysmon 3)", "Căutați conexiuni reușite după scanare către aceleași ținte"],
                        Limitations = [$"Prag: cel puțin {ScanThreshold} destinații distincte în {ScanWindow.TotalSeconds:0} s; o scanare lentă nu se vede."],
                    });
                }
        }
    }

    /// <summary>The window of <see cref="ScanWindow"/> with the most distinct values (sliding, anchored at each connection).</summary>
    private static (int Distinct, DateTimeOffset Start, List<Conn> Rows) Window(List<Conn> items, Func<Conn, string> value)
    {
        var sorted = items.OrderBy(c => c.T).ToList();
        int bestDistinct = 0, bestLo = 0, bestHi = -1;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int lo = 0;
        for (int hi = 0; hi < sorted.Count; hi++)
        {
            var v = value(sorted[hi]);
            counts[v] = counts.GetValueOrDefault(v) + 1;
            while (sorted[hi].T - sorted[lo].T > ScanWindow)
            {
                var o = value(sorted[lo]);
                if (--counts[o] == 0) counts.Remove(o);
                lo++;
            }
            if (counts.Count > bestDistinct) (bestDistinct, bestLo, bestHi) = (counts.Count, lo, hi);
        }
        return bestHi < 0 ? (0, default, []) : (bestDistinct, sorted[bestLo].T, sorted.GetRange(bestLo, Math.Min(bestHi - bestLo + 1, MaxEvidencePerFinding)));
    }
}
