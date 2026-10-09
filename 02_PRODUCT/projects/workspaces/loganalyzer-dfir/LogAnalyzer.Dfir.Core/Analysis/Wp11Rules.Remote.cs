using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private const string TsLocalSession = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";
    private const string TsRemoteConnection = "Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational";
    private const string WinRmChannel = "Microsoft-Windows-WinRM/Operational";
    private const string OpenSshChannel = "OpenSSH/Operational";

    private static void Remote(Ctx c, List<Finding> o)
    {
        RdpInternal(c, o);
        WinRm(c, o);
        PsExec(c, o);
        Ssh(c, o);
    }

    // ---------------------------------------------------------------- REMOTE-RDP-INTERNAL

    private sealed record RdpHit(TimelineEvent Event, string Source, string User, string Ip);

    /// <summary>
    /// Internal (non-public) RDP sessions: Security 4624 type 10 and TerminalServices 21/22/25/1149. Public sources belong to
    /// REMOTE-RDP-PUBLIC. A session is routine administration until something else says otherwise, so it is Low.
    /// </summary>
    private static void RdpInternal(Ctx c, List<Finding> o)
    {
        var hits = new List<RdpHit>();
        foreach (var e in c.Events)
        {
            if (Ev(e, "Security", 4624) && F(e, "LogonType") == "10")
            {
                var ip = F(e, "IpAddress").Trim();
                if (!IpClassifier.IsExternal(ip)) hits.Add(new(e, "4624", Account(F(e, "TargetUserName"), F(e, "TargetDomainName")), ip is "" or "-" ? "(adresă necunoscută)" : ip));
            }
            else if (Ev(e, TsLocalSession, 21, 22, 25))
            {
                var ip = F(e, "Address").Trim();
                if (ip.Length > 0 && !ip.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) && !IpClassifier.IsExternal(ip) && IpClassifier.Classify(ip) != IpScope.Invalid)
                    hits.Add(new(e, "TS-" + e.EventId, F(e, "User"), ip));
            }
            else if (Ev(e, TsRemoteConnection, 1149))
            {
                var ip = F(e, "Param3").Trim();
                if (ip.Length > 0 && !IpClassifier.IsExternal(ip) && IpClassifier.Classify(ip) != IpScope.Invalid)
                    hits.Add(new(e, "TS-1149", Account(F(e, "Param1"), F(e, "Param2")), ip));
            }
        }
        if (hits.Count == 0) return;
        var groups = hits.GroupBy(h => (User: h.User.ToLowerInvariant(), h.Ip)).ToList();
        bool correlated = groups.Any(g => g.Any(a => g.Any(b => a.Source != b.Source && T(a.Event) is { } ta && T(b.Event) is { } tb && (ta - tb).Duration() <= c.Options.PairWindow)));
        bool loopback = hits.Any(h => IpClassifier.Classify(h.Ip) == IpScope.Loopback);
        var ordered = hits.Select(h => T(h.Event)).Where(t => t is not null).OrderBy(t => t).ToList();
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = "REMOTE-RDP-INTERNAL", Title = $"Sesiuni RDP din rețeaua internă ({groups.Count} cont/sursă)",
            Severity = Severity.Low, Category = "Lateral Movement", Classification = correlated ? Classification.Correlated : Classification.Direct,
            Confidence = Confidence.High, MitreTechniqueId = "T1021.001", FirstSeenUtc = ordered.FirstOrDefault(), LastSeenUtc = ordered.LastOrDefault(),
            User = groups[0].First().User, Ip = groups[0].Key.Ip,
            Description = string.Join("; ", groups.Take(15).Select(g => $"{g.First().User} de la {g.Key.Ip} ({string.Join(", ", g.GroupBy(x => x.Source).Select(s => $"{s.Count()} × {s.Key}"))})")) +
                          (loopback ? ". O sursă este adresa locală (loopback): RDP prin tunel este o posibilitate, nu o concluzie." : ""),
            ClassificationReason = correlated ? "Aceeași sesiune apare în Security 4624 tip 10 și în TerminalServices, apropiate în timp." : "Eveniment de autentificare RDP (4624 tip 10) sau TerminalServices 21/22/25/1149 dintr-o adresă care nu este publică.",
            SemanticType = SemanticType.Observation,
            SupportingEvidence = hits.Take(20).Select(h => Ref(h.Event, $"{h.Source} {h.User} de la {h.Ip}")).ToList(),
            AlternativeExplanations = ["Administrare de la distanță legitimă dintr-o stație de salt sau din rețeaua internă.", "Adresa sursă poate fi un gateway sau un intermediar."],
        });
    }

    // ---------------------------------------------------------------- REMOTE-WINRM

    /// <summary>
    /// WinRM: WinRM/Operational 6 (client session), 91 (shell created on this host), 142 (failed operation), wsmprovhost.exe starts
    /// (4688 / Sysmon 1 / Prefetch) and the type-3 logon that carries them. Operational events alone are Low; a started session host is
    /// Medium; failures alone are Info.
    /// </summary>
    private static void WinRm(Ctx c, List<Finding> o)
    {
        var ops = c.Events.Where(e => Ev(e, WinRmChannel, 6, 91, 142)).ToList();
        var starts = ProcessStarts(c.Events).Where(p => WinPath.GetFileName(p.Image).Equals("wsmprovhost.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        var prefetch = c.Events.Where(e => e.Source == "Prefetch" && e.Process.Equals("wsmprovhost.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (ops.Count == 0 && starts.Count == 0 && prefetch.Count == 0) return;

        var logons = c.Events.Where(e => Ev(e, "Security", 4624) && F(e, "LogonType") == "3").ToList();
        var pairs = new List<(ProcStart Start, TimelineEvent Logon)>();
        foreach (var s in starts)
        {
            var match = logons.FirstOrDefault(l => s.LogonId.Length > 0 && s.LogonId != "0x0" && F(l, "TargetLogonId").Equals(s.LogonId, StringComparison.OrdinalIgnoreCase)) ??
                        logons.Where(l => T(l) is { } tl && T(s.Event) is { } ts && tl <= ts && ts - tl <= c.Options.PairWindow &&
                                          s.User.Length > 0 && F(l, "TargetUserName").Equals(s.User.Split('\\')[^1], StringComparison.OrdinalIgnoreCase))
                              .OrderByDescending(T).FirstOrDefault();
            if (match is not null) pairs.Add((s, match));
        }
        int n6 = ops.Count(e => e.EventId == "6"), n91 = ops.Count(e => e.EventId == "91"), n142 = ops.Count(e => e.EventId == "142");
        bool sessionHost = starts.Count + prefetch.Count > 0;
        var sev = sessionHost ? Severity.Medium : n6 + n91 > 0 ? Severity.Low : Severity.Info;
        var times = ops.Concat(starts.Select(s => s.Event)).Concat(prefetch).Select(T).Where(t => t is not null).OrderBy(t => t).ToList();
        var pairText = pairs.Count > 0
            ? $" Autentificări tip 3 asociate: {Join(pairs.Select(p => $"{Account(F(p.Logon, "TargetUserName"), F(p.Logon, "TargetDomainName"))} de la {F(p.Logon, "IpAddress")}"))}."
            : starts.Count > 0 ? " Sesiune: nicio autentificare de tip 3 asociată (după LogonId sau utilizator+timp) în dovezile colectate; sursa ei nu este cunoscută." : "";
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = "REMOTE-WINRM", Title = sessionHost ? "Sesiune WinRM: wsmprovhost.exe pornit" : "Activitate WinRM înregistrată",
            Severity = sev, Category = "Lateral Movement", Classification = pairs.Count > 0 ? Classification.Correlated : Classification.Direct,
            Confidence = pairs.Count > 0 ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1021.006",
            FirstSeenUtc = times.FirstOrDefault(), LastSeenUtc = times.LastOrDefault(),
            User = pairs.Count > 0 ? Account(F(pairs[0].Logon, "TargetUserName"), F(pairs[0].Logon, "TargetDomainName")) : "", Ip = pairs.Count > 0 ? F(pairs[0].Logon, "IpAddress") : "",
            Description = $"WinRM/Operational: {n6} × 6 (sesiune inițiată de această stație), {n91} × 91 (shell creat pe această stație), {n142} × 142 (operații eșuate); " +
                          $"wsmprovhost.exe: {starts.Count} porniri 4688/Sysmon 1, {prefetch.Count} intrări Prefetch." + pairText,
            ClassificationReason = pairs.Count > 0 ? "wsmprovhost.exe pornit în sesiunea unei autentificări de tip 3 (LogonId sau utilizator+timp)." : "Evenimente WinRM/Operational și/sau pornirea gazdei de sesiune WinRM.",
            SemanticType = SemanticType.Observation,
            SupportingEvidence = ops.Take(8).Select(e => Ref(e, $"WinRM {e.EventId}")).Concat(starts.Take(6).Select(s => Ref(s.Event, "wsmprovhost.exe"))).Concat(prefetch.Take(2).Select(e => Ref(e, "Prefetch wsmprovhost")))
                                    .Concat(pairs.Take(6).Select(p => Ref(p.Logon, "4624 tip 3"))).ToList(),
            AlternativeExplanations = ["WinRM este folosit legitim de instrumente de administrare și automatizare (inventar, orchestrare, Exchange).", "Evenimentul 6 descrie o sesiune pornită de aceasta către altă stație, nu un acces primit."],
        });
    }

    // ---------------------------------------------------------------- REMOTE-PSEXEC

    private static readonly Regex WindowsDirExe = new(@"^(?:%systemroot%|%windir%|[a-z]:\\windows)\\(?<stem>[a-z0-9]{4,16})\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex FirstExe = new(@"^\s*(?:""(?<q>[^""]+)""|(?<p>.+?\.exe))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static string ServiceExe(string imagePath)
    {
        var m = FirstExe.Match(imagePath);
        return m.Success ? (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["p"].Value).Trim() : imagePath.Trim();
    }

    /// <summary>
    /// PsExec-style remote execution: System 7045 with PSEXESVC, or a service whose image sits directly in the Windows folder (or comes from
    /// ADMIN$), and Security 5145 writes of executables to ADMIN$. PSEXESVC alone is Medium; the 7045 + 5145 pair with the same file name is High;
    /// an executable copied through ADMIN$ with no service is Low. "Service installed" does not prove the tool ran or what it ran.
    /// </summary>
    private static void PsExec(Ctx c, List<Finding> o)
    {
        var adminWrites = c.Events.Where(e => Ev(e, "Security", 5145) && AdminShareKind(F(e, "ShareName")) == "ADMIN$" &&
                                              F(e, "RelativeTargetName").EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        var usedWrites = new HashSet<TimelineEvent>();
        var starts = ProcessStarts(c.Events).Where(p => WinPath.GetFileName(p.Image).Equals("psexesvc.exe", StringComparison.OrdinalIgnoreCase)).Select(p => p.Event)
                       .Concat(c.Events.Where(e => e.Source == "Prefetch" && e.Process.Equals("psexesvc.exe", StringComparison.OrdinalIgnoreCase))).ToList();
        bool anyService = false;

        foreach (var e in c.Events.Where(e => Ev(e, "System", 7045)))
        {
            var name = F(e, "ServiceName"); var image = F(e, "ImagePath"); var exe = ServiceExe(image);
            bool psexesvc = name.Equals("PSEXESVC", StringComparison.OrdinalIgnoreCase) || WinPath.GetFileName(exe).Equals("psexesvc.exe", StringComparison.OrdinalIgnoreCase);
            var rm = WindowsDirExe.Match(exe);
            bool windowsDirRandom = !psexesvc && rm.Success;
            bool fromAdminShare = !psexesvc && image.Contains(@"\ADMIN$\", StringComparison.OrdinalIgnoreCase);
            if (!psexesvc && !windowsDirRandom && !fromAdminShare) continue;
            anyService = true;
            var stem = WinPath.GetFileNameWithoutExtension(exe);
            var pair = adminWrites.Where(w => WinPath.GetFileNameWithoutExtension(F(w, "RelativeTargetName")).Equals(stem, StringComparison.OrdinalIgnoreCase) &&
                                              T(w) is { } tw && T(e) is { } te && (te - tw).Duration() <= c.Options.PairWindow).OrderBy(T).FirstOrDefault();
            if (pair is not null) usedWrites.Add(pair);
            var approved = psexesvc ? ApprovedSoftwareMatcher.Match(c.Profile, ["psexec", "psexesvc"], [exe], []) : null;
            var sev = approved is not null ? Severity.Info : pair is not null ? Severity.High : Severity.Medium;
            var what = psexesvc ? "serviciul PSEXESVC" : fromAdminShare ? "serviciu cu binarul pe ADMIN$" : "serviciu cu nume aleatoriu cu binarul direct în folderul Windows";
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "REMOTE-PSEXEC", Title = $"Serviciu instalat tipic pentru execuție la distanță (stil PsExec): {name}" + (approved is not null ? " (aprobat în profil)" : ""),
                Severity = sev, Category = "Lateral Movement", Classification = pair is not null ? Classification.Correlated : Classification.Direct,
                Confidence = pair is not null ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1569.002",
                FirstSeenUtc = pair is not null ? T(pair) : T(e), LastSeenUtc = T(e), File = exe, User = F(e, "AccountName"),
                Description = $"System 7045: {what} ({name}), ImagePath {image}, cont {F(e, "AccountName")}, {Time(T(e))}. " +
                              (pair is not null ? $"Security 5145: {F(pair, "RelativeTargetName")} accesat pe ADMIN$ de {Account(F(pair, "SubjectUserName"), F(pair, "SubjectDomainName"))} din {F(pair, "IpAddress")}, {Time(T(pair))}. " : "Nu există în caz un 5145 pe ADMIN$ pentru același fișier. ") +
                              (starts.Count > 0 ? $"Proces PSEXESVC.exe pornit: {starts.Count} evenimente. " : "") +
                              (approved is not null ? $"PsExec este software aprobat în profil ({approved.Name}). " : "") +
                              "Instalarea serviciului nu dovedește ce a rulat prin el; indică (nu dovedește) execuție la distanță.",
                ClassificationReason = pair is not null ? "7045 (serviciu) și 5145 (fișier scris pe ADMIN$) cu același nume de fișier, apropiate în timp." : "7045 cu numele PSEXESVC sau cu imaginea în folderul Windows / pe ADMIN$.",
                SemanticType = pair is not null ? SemanticType.Correlation : SemanticType.Configuration,
                SupportingEvidence = [Ref(e, "7045 instalare serviciu"), .. (pair is null ? [] : new[] { Ref(pair, "5145 ADMIN$") }), .. starts.Take(2).Select(s => Ref(s, "PSEXESVC pornit"))],
                AlternativeExplanations = ["Administratorii folosesc PsExec și instrumente similare pentru întreținere.", "Unele produse de gestiune instalează temporar servicii cu nume aleatorii."],
            });
        }

        var rest = adminWrites.Where(w => !usedWrites.Contains(w)).ToList();
        if (rest.Count > 0)
        {
            var files = rest.Select(w => F(w, "RelativeTargetName")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "REMOTE-PSEXEC", Title = $"Executabile accesate prin ADMIN$ fără serviciu asociat în caz ({files.Count})",
                Severity = Severity.Low, Category = "Lateral Movement", Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = "T1021.002",
                FirstSeenUtc = T(rest.OrderBy(T).First()), LastSeenUtc = T(rest.OrderBy(T).Last()),
                Description = $"Security 5145 pe ADMIN$: {Join(files)}; conturi {Join(rest.Select(w => Account(F(w, "SubjectUserName"), F(w, "SubjectDomainName"))))}, surse {Join(rest.Select(w => F(w, "IpAddress")))}. " +
                              "Nu există 7045 corespunzător în dovezile colectate (poate lipsi din jurnalul System colectat); copierea nu dovedește execuția.",
                ClassificationReason = "Evenimente 5145 pe ADMIN$ pentru fișiere .exe; fără serviciu corespunzător în caz.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = rest.Take(10).Select(w => Ref(w, "5145 ADMIN$")).ToList(),
                AlternativeExplanations = ["Distribuție de software prin partajarea administrativă (instrumente de gestiune)."],
            });
        }
        else if (!anyService && starts.Count > 0)
        {
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "REMOTE-PSEXEC", Title = "PSEXESVC.exe pornit fără eveniment de instalare a serviciului în caz",
                Severity = ApprovedSoftwareMatcher.Match(c.Profile, ["psexec", "psexesvc"], [], []) is not null ? Severity.Info : Severity.Medium,
                Category = "Lateral Movement", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1569.002",
                FirstSeenUtc = starts.Select(T).Min(), LastSeenUtc = starts.Select(T).Max(),
                Description = $"{starts.Count} dovezi de execuție pentru PSEXESVC.exe (4688 / Sysmon 1 / Prefetch); nu există 7045 în dovezile colectate. Pornirea nu arată de unde a venit comanda.",
                ClassificationReason = "Proces PSEXESVC.exe înregistrat; instalarea serviciului lipsește din jurnalul colectat.",
                SemanticType = SemanticType.Execution,
                SupportingEvidence = starts.Take(6).Select(s => Ref(s, "PSEXESVC pornit")).ToList(),
                AlternativeExplanations = ["Administrare legitimă cu PsExec."],
            });
        }
    }

    // ---------------------------------------------------------------- REMOTE-SSH

    private static readonly Regex SshAccepted = new(@"Accepted\s+(?<m>\S+)\s+for\s+(?<u>\S+)\s+from\s+(?<ip>\S+)\s+port\s+(?<p>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex SshFailed = new(@"Failed\s+(?<m>\S+)\s+for\s+(?:invalid user\s+)?(?<u>\S+)\s+from\s+(?<ip>\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>OpenSSH/Operational 4 ("Accepted … for user from ip"): an accepted SSH logon. Failures alone are not remote access and are only counted.</summary>
    private static void Ssh(Ctx c, List<Finding> o)
    {
        var accepted = new List<(TimelineEvent E, string User, string Ip, string Method)>();
        int failed = 0;
        foreach (var e in c.Events.Where(e => Ev(e, OpenSshChannel, 4)))
        {
            var text = F(e, "payload", "Payload", "Data0", "Data1");
            if (text.Length == 0) text = AllText(e);
            var a = SshAccepted.Match(text);
            if (a.Success) accepted.Add((e, a.Groups["u"].Value, a.Groups["ip"].Value, a.Groups["m"].Value));
            else if (SshFailed.IsMatch(text)) failed++;
        }
        if (accepted.Count == 0) return;
        bool external = accepted.Any(a => IpClassifier.IsExternal(a.Ip));
        var times = accepted.Select(a => T(a.E)).Where(t => t is not null).OrderBy(t => t).ToList();
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = "REMOTE-SSH", Title = $"Autentificări SSH acceptate ({accepted.Count})" + (external ? " din adrese publice" : ""),
            Severity = external ? Severity.Medium : Severity.Low, Category = "Lateral Movement", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1021.004",
            FirstSeenUtc = times.FirstOrDefault(), LastSeenUtc = times.LastOrDefault(), User = accepted[0].User, Ip = accepted[0].Ip,
            Description = string.Join("; ", accepted.GroupBy(a => (a.User.ToLowerInvariant(), a.Ip)).Take(15).Select(g => $"{g.First().User} de la {g.Key.Item2} ({g.Count()} × {g.First().Method})")) +
                          (failed > 0 ? $". {failed} încercări eșuate în același jurnal." : ""),
            ClassificationReason = "OpenSSH/Operational 4 cu mesaj 'Accepted … for … from …'. Arată o autentificare acceptată, nu activitatea din sesiune.",
            SemanticType = SemanticType.Observation,
            SupportingEvidence = accepted.Take(20).Select(a => Ref(a.E, $"OpenSSH {a.User} de la {a.Ip}")).ToList(),
            AlternativeExplanations = ["Administrare legitimă prin SSH (OpenSSH Server este o funcție Windows).", "Adresa sursă poate fi un intermediar sau un tunel."],
        });
    }
}
