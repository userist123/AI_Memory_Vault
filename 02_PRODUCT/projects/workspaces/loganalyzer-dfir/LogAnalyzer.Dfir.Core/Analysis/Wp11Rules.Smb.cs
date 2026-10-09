using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    /// <summary>"ADMIN$", "IPC$", "C$"…: what kind of administrative share this is; null = an ordinary share.</summary>
    internal static string? AdminShareKind(string shareName)
    {
        var tail = shareName[(shareName.LastIndexOf('\\') + 1)..].Trim().ToUpperInvariant();
        if (tail == "IPC$") return "IPC$";
        if (tail == "ADMIN$") return "ADMIN$";
        return tail.Length == 2 && char.IsAsciiLetter(tail[0]) && tail[1] == '$' ? tail : null;
    }

    // Allow ACE (A;flags;rights;;;SID) for Everyone (WD) or Authenticated Users (AU) with a write/full right.
    private static readonly Regex BroadAllow = new(@"\(A;[^;]*;(?<rights>[^;]*);[^;]*;[^;]*;(?:WD|AU)\)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static int BroadGrants(string sddl) =>
        BroadAllow.Matches(sddl).Count(m => m.Groups["rights"].Value is var r && (r.Contains("FA") || r.Contains("GA") || r.Contains("FW") || r.Contains("GW") ||
                                                                               r.StartsWith("0x", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// SMB-ADMIN-SHARE (5140 / 5145 on C$, ADMIN$, IPC$…) and SMB-SHARE-PERMS-CHANGED (4670 on a file or directory object).
    /// Counts only: volume thresholds are WP17. A single access to an administrative share is routine for administrators and backup
    /// software, so it is Low (Info for IPC$ alone) and says so.
    /// </summary>
    private static void Smb(Ctx c, List<Finding> o)
    {
        var pipes = c.Data.Lists.SharePipesOfInterest;
        var exts = c.Data.Lists.ExecutableExtensions;
        var groups = c.Events.Where(e => Ev(e, "Security", 5140, 5145))
            .Select(e => (E: e, Kind: AdminShareKind(F(e, "ShareName")), User: F(e, "SubjectUserName"), Domain: F(e, "SubjectDomainName"), Ip: F(e, "IpAddress")))
            .Where(x => x.Kind is not null && x.User.Length > 0 && !IsMachineAccount(x.User) && IsRemoteAddress(x.Ip))
            .GroupBy(x => (Acc: Account(x.User, x.Domain).ToLowerInvariant(), Ip: x.Ip.Trim()));
        foreach (var g in groups)
        {
            var items = g.OrderBy(x => T(x.E)).ToList();
            var shares = items.Select(x => x.Kind!).Distinct().ToList();
            int n5140 = items.Count(x => x.E.EventId == "5140"), n5145 = items.Count(x => x.E.EventId == "5145");
            var targets = items.Where(x => x.E.EventId == "5145").Select(x => F(x.E, "RelativeTargetName")).Where(t => t.Length > 0).ToList();
            var exeTargets = items.Where(x => x.E.EventId == "5145" && x.Kind != "IPC$" &&
                                              exts.Any(ext => F(x.E, "RelativeTargetName").EndsWith(ext, StringComparison.OrdinalIgnoreCase))).Select(x => F(x.E, "RelativeTargetName")).Distinct().ToList();
            var pipeHits = items.Where(x => x.Kind == "IPC$").Select(x => F(x.E, "RelativeTargetName")).Where(t => pipes.Contains(t, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool onlyIpc = shares.All(s => s == "IPC$");
            var sev = exeTargets.Count > 0 ? Severity.Medium : onlyIpc && pipeHits.Count == 0 ? Severity.Info : Severity.Low;
            var acc = Account(items[0].User, items[0].Domain);
            var parts = new List<string> { $"{n5140} × 5140 (acces la partajare)", $"{n5145} × 5145 (acces la obiect)" };
            var desc = $"{acc} de la {g.Key.Ip}: partajări {string.Join(", ", shares)}; {string.Join(", ", parts)}; {Time(T(items[0].E))} – {Time(T(items[^1].E))}." +
                       (exeTargets.Count > 0 ? $" Fișiere executabile/script atinse prin partajare: {Join(exeTargets)}." : "") +
                       (pipeHits.Count > 0 ? $" Pipe-uri IPC$ de interes: {Join(pipeHits)}" + (pipeHits.Contains("winreg", StringComparer.OrdinalIgnoreCase) ? " (winreg = acces la registru de la distanță)" : "") + "." : "") +
                       (targets.Count > 0 && exeTargets.Count == 0 ? $" Obiecte accesate: {Join(targets, 5)}." : "");
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "SMB-ADMIN-SHARE", Title = $"Acces la partajare administrativă ({string.Join(", ", shares)}) de la {g.Key.Ip}: {acc}",
                Severity = sev, Category = "Lateral Movement", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1021.002",
                FirstSeenUtc = T(items[0].E), LastSeenUtc = T(items[^1].E), User = acc, Ip = g.Key.Ip, Description = desc,
                ClassificationReason = "Evenimente Security 5140/5145 care arată accesul la o partajare administrativă dintr-o adresă care nu este locală. Numărătoare, nu prag de volum.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = items.Take(20).Select(x => Ref(x.E, $"{x.E.EventId} {F(x.E, "ShareName")} {F(x.E, "RelativeTargetName")}".Trim())).ToList(),
                AlternativeExplanations = ["Administrare legitimă, software de backup sau de inventar care folosește partajările administrative.", "Contul poate fi administrator autorizat; evenimentul nu spune dacă este."],
            });
        }

        var perms = c.Events.Where(e => Ev(e, "Security", 4670) &&
                                        (F(e, "ObjectType").Equals("File", StringComparison.OrdinalIgnoreCase) || F(e, "ObjectType").Equals("Directory", StringComparison.OrdinalIgnoreCase) ||
                                         F(e, "ObjectType").Contains("share", StringComparison.OrdinalIgnoreCase) || F(e, "ObjectName").StartsWith(@"\\", StringComparison.Ordinal)))
            .GroupBy(e => (Acc: Account(F(e, "SubjectUserName"), F(e, "SubjectDomainName")).ToLowerInvariant(), Proc: F(e, "ProcessName").ToLowerInvariant()));
        foreach (var g in perms)
        {
            var items = g.OrderBy(T).ToList();
            var names = items.Select(e => F(e, "ObjectName")).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool broad = items.Any(e => BroadGrants(F(e, "NewSd")) > BroadGrants(F(e, "OldSd")));
            var acc = Account(F(items[0], "SubjectUserName"), F(items[0], "SubjectDomainName"));
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "SMB-SHARE-PERMS-CHANGED",
                Title = $"Permisiuni modificate pe {names.Count} obiecte de fișier/director de {acc}" + (broad ? " (acces nou pentru Everyone/Authenticated Users)" : ""),
                Severity = broad ? Severity.Medium : Severity.Low, Category = "Defense Evasion", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1222.001",
                FirstSeenUtc = T(items[0]), LastSeenUtc = T(items[^1]), User = acc, Process = WinPath.GetFileName(F(items[0], "ProcessName")),
                Description = $"{names.Count} obiecte, {items.Count} evenimente 4670 de {acc} prin {F(items[0], "ProcessName")}: {Join(names, 8)}." +
                              (broad ? " Descriptorul nou acordă drepturi de scriere/control total pentru Everyone sau Authenticated Users, absente din cel vechi." : ""),
                ClassificationReason = "Evenimente Security 4670 (permisiuni schimbate) pe obiecte de tip fișier/director/partajare; descriptorii vechi și noi sunt comparați doar pentru acordări largi noi.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = items.Take(20).Select(e => Ref(e, $"4670 {F(e, "ObjectName")}")).ToList(),
                AlternativeExplanations = ["Instalatoare, migrări de date și administrarea normală a permisiunilor produc același eveniment.", "Moștenirea permisiunilor poate declanșa 4670 pe multe obiecte dintr-o singură acțiune."],
            });
        }
    }
}
