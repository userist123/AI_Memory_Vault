using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    // ---------------------------------------------------------------- DISCOVERY-BURST
    // Lab VM 2026-11-06: whoami /all, net group "Domain Admins" /domain, nltest /dclist and net view ran within a second (4688 with command line)
    // and no rule said so. One command is administration; several different discovery commands by one account in a short window is the
    // post-compromise reconnaissance pattern (T1087 / T1018 / T1082 / T1016). What the finding claims is observed (the processes started, 4688 /
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
                var sub = NetDiscoverySub.FirstOrDefault(s => System.Text.RegularExpressions.Regex.IsMatch(cl, $@"\bnet1?(?:\.exe)?""?\s+{s}\b"));
                if (sub is null) return "";
                return sub is "user" or "accounts" && cl.Contains("/domain") ? $"net {sub} /domain" : "net " + sub;
            }
            case "wmic.exe":
                return new[] { "useraccount", "group", "computersystem", "ntdomain", "qfe", "startup" }.FirstOrDefault(cl.Contains) is { } w ? "wmic " + w : "";
            default: return "";
        }
    }

    private static void Discovery(Ctx c, List<Finding> o)
    {
        var hits = ProcessStarts(c.Events)
            .Select(p => (Start: p, Key: DiscoveryKey(p.Image, p.CommandLine), Who: DiscoveryActor(p)))
            .Where(x => x.Key.Length > 0 && T(x.Start.Event) is not null && x.Who.Length > 0)
            .ToList();
        foreach (var byActor in hits.GroupBy(x => x.Who, StringComparer.OrdinalIgnoreCase))
        {
            var seq = byActor.OrderBy(x => T(x.Start.Event)).ToList();
            int i = 0;
            while (i < seq.Count)
            {
                var start = T(seq[i].Start.Event)!.Value;
                int j = i;
                while (j + 1 < seq.Count && T(seq[j + 1].Start.Event)!.Value - start <= DiscoveryWindow) j++;
                var burst = seq.GetRange(i, j - i + 1);
                var keys = burst.Select(x => x.Key).Distinct(StringComparer.Ordinal).ToList();
                if (keys.Count < DiscoveryMinDistinct) { i++; continue; }
                bool domain = keys.Any(k => DomainDiscovery.Any(d => k.StartsWith(d, StringComparison.Ordinal)));
                var first = burst[0].Start; var last = burst[^1].Start;
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = "DISCOVERY-BURST",
                    Title = $"Serie de comenzi de recunoaștere: {Join(keys, 6)}",
                    Severity = Severity.Medium, Category = "Discovery", Classification = Classification.Correlated, Confidence = Confidence.Medium,
                    MitreTechniqueId = domain ? "T1087.002" : "T1082", SemanticType = SemanticType.Observation,
                    FirstSeenUtc = T(first.Event), LastSeenUtc = T(last.Event), User = byActor.Key,
                    Description = $"{keys.Count} comenzi de recunoaștere diferite rulate de {byActor.Key} între {Time(T(first.Event))} și {Time(T(last.Event))}" +
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
                i = j + 1;
            }
        }
    }

    /// <summary>Who ran it: the account name, else the SID (4688 run in a remote session may log the name as "-"), else the logon session.</summary>
    private static string DiscoveryActor(ProcStart p)
    {
        if (p.User.Length > 0 && p.User != "-") return p.User;
        var sid = F(p.Event, "SubjectUserSid");
        if (sid.Length > 0 && sid != "-") return sid;
        return p.LogonId is { Length: > 0 } l && l != "0x0" ? "sesiunea " + l : "";
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
