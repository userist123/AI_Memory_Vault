using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    // ---------------------------------------------------------------- DISCOVERY-BURST
    // Lab VM 2026-11-06: whoami /all, net group "Domain Admins" /domain, nltest /dclist and net view ran within a second (4688 with command line)
    // and no rule said so. One command is administration; several different discovery commands by one account in a short window is the
    // post-compromise reconnaissance pattern (T1087 / T1082). What the finding claims is observed (the processes started, 4688 /
    // Sysmon 1); "reconnaissance" is the reading of it, and an administrator troubleshooting does the same (alternative explanations).

    internal const int DiscoveryMinDistinct = 3;
    internal static readonly TimeSpan DiscoveryWindow = TimeSpan.FromMinutes(10);

    private static readonly string[] NetDiscoverySub = ["group", "user", "localgroup", "view", "accounts", "share", "session", "config"];
    private static readonly string[] DomainDiscovery = ["net group", "net user /domain", "net accounts /domain", "nltest", "dsquery", "adfind", "setspn", "net view"];

    /// <summary>The discovery command a process start is ("whoami", "net group", "nltest"...), or "" when it is none. net.exe and the net1.exe it spawns give the same key.</summary>
    internal static string DiscoveryKey(string image, string commandLine)
    {
        var exe = WinPath.GetFileName(image).ToLowerInvariant();
        var cl = commandLine.ToLowerInvariant();
        switch (exe)
        {
            case "whoami.exe": return "whoami";
            case "nltest.exe": return "nltest";
            case "systeminfo.exe": return "systeminfo";
            case "quser.exe": case "qwinsta.exe": case "query.exe": return "query user";
            case "dsquery.exe": return "dsquery";
            case "adfind.exe": return "adfind";
            case "setspn.exe": return "setspn";
            case "klist.exe": return "klist";
            case "gpresult.exe": return "gpresult";
            case "arp.exe": return cl.Contains(" -a") ? "arp -a" : "";
            case "route.exe": return cl.Contains("print") ? "route print" : "";
            case "netstat.exe": return "netstat";
            case "tasklist.exe": return "tasklist";
            case "ipconfig.exe": return cl.Contains("/all") ? "ipconfig /all" : "";
            case "nslookup.exe": return "nslookup";
            case "net.exe": case "net1.exe":
            {
                if (cl.Contains("/add") || cl.Contains("/del")) return "";   // a change (account, group), not a look (review round 1)
                var sub = NetDiscoverySub.FirstOrDefault(s => System.Text.RegularExpressions.Regex.IsMatch(cl, $@"\bnet1?(?:\.exe)?""?\s+{s}\b"));
                if (sub is null) return "";
                return sub is "user" or "accounts" && cl.Contains("/domain") ? $"net {sub} /domain" : "net " + sub;
            }
            case "wmic.exe":
                return new[] { "useraccount", "group", "computersystem", "ntdomain", "qfe", "startup" }.FirstOrDefault(cl.Contains) is { } w ? "wmic " + w : "";
            default: return "";
        }
    }

    /// <summary>At most this many series are listed one by one; the rest are one Low summary (an hourly inventory would give hundreds).</summary>
    internal const int DiscoveryMaxFindings = 20;

    /// <summary>The actor name of the station's own accounts (SYSTEM, LOCAL / NETWORK SERVICE, machine account): their series are Low.</summary>
    internal const string StationActorPrefix = "contul stației: ";

    private static void Discovery(Ctx c, List<Finding> o)
    {
        var hits = ProcessStarts(c.Events)
            .Select(p => (Start: p, Key: DiscoveryKey(p.Image, p.CommandLine), Who: DiscoveryActor(p)))
            .Where(x => x.Key.Length > 0 && T(x.Start.Event) is not null && x.Who.Length > 0)
            .ToList();
        var bursts = new List<(string Who, List<(ProcStart Start, string Key, string Who)> Items, List<string> Keys)>();
        foreach (var byActor in hits.GroupBy(x => x.Who, StringComparer.OrdinalIgnoreCase))
        {
            var seq = byActor.OrderBy(x => T(x.Start.Event)).ToList();
            // two pointers over the sorted series, counts of each command in the window: linear in the number of commands (review round 1)
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int i = 0, j = -1;
            while (i < seq.Count)
            {
                var start = T(seq[i].Start.Event)!.Value;
                while (j + 1 < seq.Count && T(seq[j + 1].Start.Event)!.Value - start <= DiscoveryWindow)
                {
                    j++;
                    counts[seq[j].Key] = counts.TryGetValue(seq[j].Key, out var n) ? n + 1 : 1;
                }
                if (counts.Count >= DiscoveryMinDistinct)
                {
                    bursts.Add((byActor.Key, seq.GetRange(i, j - i + 1), counts.Keys.ToList()));
                    i = j + 1; counts.Clear();
                    continue;
                }
                if (j >= i && --counts[seq[i].Key] == 0) counts.Remove(seq[i].Key);
                i++;
            }
        }
        // listed: the first series of EVERY actor (a newcomer is never buried under an hourly inventory, review round 2), then the earliest others up to the cap
        var ordered = bursts.OrderBy(b => T(b.Items[0].Start.Event)).ToList();
        var firstPerActor = ordered.GroupBy(b => b.Who, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var listed = firstPerActor.Concat(ordered.Except(firstPerActor).Take(Math.Max(0, DiscoveryMaxFindings - firstPerActor.Count)))
            .OrderBy(b => T(b.Items[0].Start.Event)).ToList();
        foreach (var (who, burst, _) in listed)
        {
                bool station = who.StartsWith(StationActorPrefix, StringComparison.Ordinal);
                var keys = burst.Select(x => x.Key).Distinct(StringComparer.Ordinal).ToList();
                bool domain = keys.Any(k => DomainDiscovery.Any(d => k.StartsWith(d, StringComparison.Ordinal)));
                var first = burst[0].Start; var last = burst[^1].Start;
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = "DISCOVERY-BURST",
                    Title = $"Serie de comenzi de recunoaștere: {Join(keys, 6)}",
                    // the station's own accounts (SYSTEM, services, machine account) run these all day: Low, still listed (psexec -s / potato reconnaissance)
                    Severity = station ? Severity.Low : Severity.Medium, Category = "Discovery", Classification = Classification.Correlated, Confidence = Confidence.Medium,
                    MitreTechniqueId = domain ? "T1087.002" : "T1082", SemanticType = SemanticType.Observation,
                    FirstSeenUtc = T(first.Event), LastSeenUtc = T(last.Event), User = who,
                    PlainWhat = $"a rulat rapid {LogAnalyzer.Dfir.Language.RoText.Count(burst.Count, "comandă", "comenzi")} prin care a aflat cine este, ce drepturi are și ce calculatoare sunt în rețea ({Join(burst.Select(x => PlainPhrases.Cut(x.Start.CommandLine.Length > 0 ? x.Start.CommandLine : WinPath.GetFileName(x.Start.Image), 40)), 4)})",
                    Description = $"{keys.Count} comenzi de recunoaștere diferite rulate de {who} între {Time(T(first.Event))} și {Time(T(last.Event))}" +
                                  (domain ? ", inclusiv enumerarea domeniului (grupuri, controlere, stații)" : "") +
                                  $": {Join(burst.Select(x => Trunc(x.Start.CommandLine.Length > 0 ? x.Start.CommandLine : WinPath.GetFileName(x.Start.Image), 120)), 8)}. " +
                                  "Ce s-a întâmplat: cineva a aflat rapid cine este, ce drepturi are și ce mai este în rețea. De ce contează: este pasul obișnuit de după o compromitere, înainte de mișcarea laterală. " +
                                  "Ce verificați: dacă seria vine dintr-o sesiune de administrare cunoscută și ce a urmat (autentificări spre alte stații, unelte noi).",
                    ClassificationReason = $"Cel puțin {DiscoveryMinDistinct} comenzi de recunoaștere distincte (4688 / Sysmon 1) ale aceluiași cont în {DiscoveryWindow.TotalMinutes:0} minute.",
                    SupportingEvidence = burst.Take(15).Select(x => Ref(x.Start.Event, x.Key)).ToList(),
                    AlternativeExplanations = ["Un administrator care depanează sau un script de inventar rulează aceleași comenzi.", "Instrumentele de suport tehnic pornesc whoami / ipconfig la diagnostic."],
                    MissingEvidence = ["Ce s-a afișat (rezultatul comenzilor nu este înregistrat).", "Procesul părinte, când auditul nu îl înregistrează."],
                    RecommendedNextSteps = ["Identificați sesiunea (LogonId) și sursa ei (4624).", "Căutați în fereastra următoare autentificări spre alte stații și programe noi."],
                });
        }
        if (ordered.Count > listed.Count)
        {
            var rest = ordered.Except(listed).ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "DISCOVERY-BURST", Title = $"Alte {rest.Count} serii de comenzi de recunoaștere nu au fost listate individual",
                Severity = Severity.Low, Category = "Discovery", Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = "T1082", SemanticType = SemanticType.Observation,
                FirstSeenUtc = T(rest[0].Items[0].Start.Event), LastSeenUtc = T(rest[^1].Items[^1].Start.Event), User = Join(rest.Select(b => b.Who), 5),
                Description = $"Primele {DiscoveryMaxFindings} serii sunt listate una câte una; alte {rest.Count} serii ale conturilor {Join(rest.Select(b => b.Who), 5)} sunt în cronologie. " +
                              "Multe serii repetate la intervale regulate arată de obicei un script de inventar programat.",
                ClassificationReason = "Limită de afișare, nu o concluzie.",
                SupportingEvidence = rest.Take(20).Select(b => Ref(b.Items[0].Start.Event, b.Items[0].Key)).ToList(),
                AlternativeExplanations = ["Un script de inventar sau de monitorizare rulat periodic."],
                MissingEvidence = ["Ce s-a afișat (rezultatul comenzilor nu este înregistrat)."],
                RecommendedNextSteps = ["Grupați seriile după cont și oră: un tipar regulat este o sarcină programată."],
            });
        }
    }

    /// <summary>
    /// Who ran it, comparable across 4688 and Sysmon 1: the account name without its domain („LAB\it.admin” = „it.admin”), else the SID (4688 in a
    /// remote session may log the name as "-"), else the logon session. The station's own accounts (SYSTEM, LOCAL / NETWORK SERVICE, machine
    /// accounts) give "": Windows and its agents run these commands all day (review round 1: an hourly inventory gave 720 findings).
    /// </summary>
    private static string DiscoveryActor(ProcStart p)
    {
        var sid = F(p.Event, "SubjectUserSid") is { Length: > 0 } s && s != "-" ? s : "";
        var name = p.User.Length > 0 && p.User != "-" ? p.User.Split('\\')[^1] : "";
        // the station's own accounts are one actor, kept (Low): reconnaissance run as SYSTEM (psexec -s, a potato) is not a blind spot (review round 2)
        if (sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20" || IsMachineAccount(name) || name.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) || name.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase))
            return StationActorPrefix + "SYSTEM / servicii / contul mașinii";
        if (name.Length > 0) return name.ToLowerInvariant();
        if (sid.Length > 0) return sid;
        return p.LogonId is { Length: > 0 } l && l != "0x0" ? "sesiunea " + l : "";
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
