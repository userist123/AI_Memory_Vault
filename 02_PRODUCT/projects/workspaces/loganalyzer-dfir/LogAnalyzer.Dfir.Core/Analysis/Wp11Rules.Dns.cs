using System.Net;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private const string DnsClientChannel = "Microsoft-Windows-DNS-Client/Operational";

    private sealed record DnsQuery(TimelineEvent Event, string Domain, string Image, bool ByPid, int? Pid);

    private static string NormalizeDomain(string d) => d.Trim().TrimEnd('.').ToLowerInvariant();

    private static bool SuffixMatch(string domain, string suffix)
    {
        suffix = suffix.Trim().TrimStart('.').ToLowerInvariant();
        return suffix.Length > 0 && (domain == suffix || domain.EndsWith("." + suffix, StringComparison.Ordinal));
    }

    /// <summary>
    /// DNS-RARE-DOMAIN: a domain looked up at most N times in the whole case (default 2), by a LOLBin or a process in a user-writable
    /// folder. The process comes from Sysmon 22 (Image) or, for DNS-Client 3006/3008/3020, only from the event's process id matched to the
    /// latest earlier process start (a Candidate: ids are reused). One finding per process. A lookup is not a connection.
    /// DNS-SERVER-CHANGED: a different name-server set for the same interface among registry NameServer rows (Source "SystemConfig", fields
    /// Interface and NameServer, produced by a collector that reads Tcpip\Parameters\Interfaces); the NetworkProfile log carries no DNS
    /// server data and is only attached as context. DnsTunnelingClassifier (LogAnalyzer.Core) is not wired here: it lives in another layer.
    /// </summary>
    private static void Dns(Ctx c, List<Finding> o)
    {
        DnsRare(c, o);
        DnsServerChanged(c, o);
    }

    private static bool ApprovedDestination(Ctx c, string addressOrDomain)
    {
        if (c.Profile is null) return false;
        var d = NormalizeDomain(addressOrDomain);
        return c.Profile.NetworkDestinations.Any(r => r.Address.Trim().Length > 0 && (SuffixMatch(d, r.Address) || r.Address.Trim().Equals(addressOrDomain.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static void DnsRare(Ctx c, List<Finding> o)
    {
        var lists = c.Data.Lists.Dns;
        var starts = ProcessStarts(c.Events).Where(p => p.Pid is not null && T(p.Event) is not null).OrderBy(p => T(p.Event)).ToList();
        var sysmon22 = c.Events.Where(e => Ev(e, Sysmon, 22) && F(e, "QueryName").Length > 0).ToList();
        var client = c.Events.Where(e => Ev(e, DnsClientChannel, 3006) && F(e, "QueryName").Length > 0).ToList();
        if (client.Count == 0) client = c.Events.Where(e => Ev(e, DnsClientChannel, 3008) && F(e, "QueryName").Length > 0).ToList();
        if (sysmon22.Count == 0 && client.Count == 0) return;

        // The same lookup can be logged by Sysmon and by DNS-Client: the case-wide count of a domain is the larger of the two, not the sum.
        var counts = sysmon22.Select(e => NormalizeDomain(F(e, "QueryName"))).Concat(client.Select(e => NormalizeDomain(F(e, "QueryName"))))
            .Distinct().ToDictionary(d => d, d => Math.Max(sysmon22.Count(e => NormalizeDomain(F(e, "QueryName")) == d), client.Count(e => NormalizeDomain(F(e, "QueryName")) == d)));

        var queries = new List<DnsQuery>();
        foreach (var e in sysmon22) queries.Add(new(e, NormalizeDomain(F(e, "QueryName")), F(e, "Image"), false, null));
        foreach (var e in client)
        {
            if (e.Pid is not int pid || T(e) is not { } t) continue;
            var p = starts.LastOrDefault(s => s.Pid == pid && T(s.Event) <= t);
            if (p.Event is not null) queries.Add(new(e, NormalizeDomain(F(e, "QueryName")), p.Image, true, pid));
        }

        bool Skip(string d) => d.Length == 0 || !d.Contains('.') || IPAddress.TryParse(d, out _) ||
                               lists.IgnoredSuffixes.Any(s => SuffixMatch(d, s)) || lists.BenignSuffixes.Any(s => SuffixMatch(d, s)) || ApprovedDestination(c, d);
        foreach (var g in queries.Where(q => q.Image.Length > 0 && !Skip(q.Domain) && counts[q.Domain] <= c.Options.DnsRareMaxCount)
                                 .GroupBy(q => q.Image.ToLowerInvariant()))
        {
            var file = WinPath.GetFileName(g.First().Image);
            bool lolbin = Correlation.Lolbins.Contains(file), userPath = Correlation.IsUserWritable(g.First().Image);
            if (!lolbin && !userPath) continue;
            var items = g.OrderBy(q => T(q.Event)).ToList();
            var domains = items.Select(q => q.Domain).Distinct().ToList();
            bool byPid = items.Any(q => q.ByPid);
            bool highInterest = lolbin && lists.HighInterestLolbins.Contains(file, StringComparer.OrdinalIgnoreCase);
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "DNS-RARE-DOMAIN", Title = $"{(domains.Count == 1 ? "Domeniu rar" : $"{domains.Count} domenii rare")} rezolvate de {file}: {Join(domains, 3)}",
                Severity = highInterest ? Severity.Medium : Severity.Low, Category = "Command and Control", Classification = byPid ? Classification.Candidate : Classification.Direct,
                Confidence = byPid ? Confidence.Low : Confidence.Medium, MitreTechniqueId = "T1071.004",
                FirstSeenUtc = T(items[0].Event), LastSeenUtc = T(items[^1].Event), Process = file, File = g.First().Image, Domain = domains[0],
                Description = $"{file} ({(lolbin ? "unealtă Windows abuzată frecvent" : "locație scriabilă de utilizatori")}) a rezolvat {Join(domains, 8)}; fiecare domeniu apare cel mult de {c.Options.DnsRareMaxCount} ori în tot cazul. " +
                              (byPid ? $"Procesul a fost dedus din PID {items.First(q => q.ByPid).Pid} (cel mai recent proces cu acest PID pornit înainte de interogare; PID-urile se reutilizează). " : "Procesul este cel din Sysmon 22. ") +
                              "Rezolvarea unui nume nu dovedește o conexiune sau un transfer.",
                ClassificationReason = byPid ? "DNS-Client 3006/3008 + proces dedus din PID; atribuire nesigură." : "Sysmon 22: interogare DNS cu imaginea procesului.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = items.Take(20).Select(q => Ref(q.Event, $"DNS {q.Domain}")).ToList(),
                AlternativeExplanations = ["Software legitim care contactează un serviciu rar (actualizări, telemetrie, licențiere).", "Un domeniu rar în acest caz poate fi frecvent în altă parte; colectarea acoperă doar această stație."],
            });
        }
    }

    private static void DnsServerChanged(Ctx c, List<Finding> o)
    {
        foreach (var g in c.Events.Where(e => e.Source == "SystemConfig" && F(e, "NameServer").Length > 0).GroupBy(e => F(e, "Interface").ToLowerInvariant()))
        {
            var items = g.OrderBy(T).ToList();
            static string[] Servers(string s) => s.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var sets = items.Select(e => string.Join(",", Servers(F(e, "NameServer")))).ToList();
            if (sets.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) continue;
            var before = Servers(F(items[0], "NameServer")); var after = Servers(F(items[^1], "NameServer"));
            var added = after.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
            if (added.Count == 0 && after.Length == before.Length) continue;
            bool allApproved = added.Count > 0 && added.All(s => ApprovedDestination(c, s));
            bool external = added.Any(IpClassifier.IsExternal);
            var t0 = T(items[0]); var t1 = T(items[^1]);
            var context = t0 is { } a && t1 is { } b ? c.Events.Where(e => Ev(e, "Microsoft-Windows-NetworkProfile/Operational", 10000, 10001, 4004) && T(e) is { } t && t >= a - TimeSpan.FromHours(1) && t <= b + TimeSpan.FromHours(1)).Take(6).ToList() : [];
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "DNS-SERVER-CHANGED", Title = $"Serverele DNS ale interfeței {F(items[0], "Interface")} s-au schimbat",
                Severity = allApproved ? Severity.Info : external ? Severity.Medium : Severity.Low, Category = "Defense Evasion", Classification = Classification.Direct, Confidence = Confidence.Medium,
                FirstSeenUtc = t0, LastSeenUtc = t1,
                Description = $"Interfața {F(items[0], "Interface")}: {string.Join(", ", before)} ({Time(t0)}) → {string.Join(", ", after)} ({Time(t1)})." +
                              (allApproved ? " Serverele noi sunt destinații aprobate în profil (aprobat în profil)." : external ? " Cel puțin un server nou este o adresă publică care nu apare în profil." : "") +
                              (context.Count > 0 ? $" Evenimente NetworkProfile în apropiere: {context.Count} (contextul rețelei, nu dovada schimbării)." : "") +
                              " Cheia de registru arată valoarea, nu cine a schimbat-o; ora este a colectării/cheii.",
                ClassificationReason = "Valori NameServer diferite pentru aceeași interfață în rândurile de configurație colectate.",
                SemanticType = SemanticType.Configuration,
                SupportingEvidence = items.Take(10).Select(e => Ref(e, $"NameServer {F(e, "NameServer")}")).Concat(context.Select(e => Ref(e, $"NetworkProfile {e.EventId}"))).ToList(),
                AlternativeExplanations = ["Schimbarea rețelei (DHCP diferit, VPN, mutarea între rețele) schimbă serverele DNS.", "Configurare manuală legitimă."],
            });
        }
    }
}
