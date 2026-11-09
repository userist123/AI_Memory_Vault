using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    // ---------------------------------------------------------------- attacks on Active Directory and silencing of the EDR
    // Five rules over Security events a domain controller, a certification authority or a station writes: KERBEROAST (4769), ASREP-ROAST (4768),
    // ADCS-SAN (4886 / 4887), NTLM-RELAY-MACHINE (4624) and EDR-WFP-BLOCK (5441 / 5447 / 5157). Each states what the event shows (a ticket issued, a
    // certificate requested, a computer account that signed in by NTLM from another name, a filter or a blocked connection) and what no log shows
    // (cracked hashes, the template's settings, the tool behind the filter). A field that is missing or empty never produces a finding and never throws;
    // an event without a time is ignored.

    internal const int AdMaxFindings = 20;
    internal const int KerberoastMinServices = 3;
    /// <summary>3-4 distinct services is Medium (RC4 is still the default for user service accounts without msDS-SupportedEncryptionTypes); 5 or more is High.</summary>
    internal const int KerberoastHighServices = 5;
    internal static readonly TimeSpan KerberoastWindow = TimeSpan.FromMinutes(10);
    internal const int EdrMinBlocks = 5;
    internal static readonly TimeSpan EdrBlockWindow = TimeSpan.FromMinutes(10);

    private static readonly string[] EdrProcesses =
    [
        "msmpeng.exe", "mssense.exe", "sensendr.exe", "sensecncproxy.exe", "mpdefendercoreservice.exe", "elastic-agent.exe", "elastic-endpoint.exe",
        "csfalconservice.exe", "sentinelagent.exe", "cb.exe", "xagt.exe",
    ];

    private static void AdAttacks(Ctx c, List<Finding> o)
    {
        Kerberoast(c, o);
        AsRepRoast(c, o);
        AdcsSan(c, o);
        NtlmRelayMachine(c, o);
        EdrWfpBlock(c, o);
    }

    // ---- small readers ----

    /// <summary>A number as Windows writes it: "0x17", "0X17" or "23". Null when empty or not a number.</summary>
    private static long? Num(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : null;
        return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static bool StatusOk(TimelineEvent e) => Num(F(e, "Status")) == 0;
    private static bool IsRc4(TimelineEvent e) => Num(F(e, "TicketEncryptionType")) is 0x17 or 0x18;

    /// <summary>"Ana@CORP.LOCAL" = "ana": the account without its realm, lowercase.</summary>
    private static string AccountKey(string name)
    {
        name = name.Trim();
        var at = name.IndexOf('@');
        if (at >= 0) name = name[..at];
        return name.ToLowerInvariant();
    }

    /// <summary>The short computer name: no leading backslashes, no domain suffix, lowercase.</summary>
    private static string ShortName(string name)
    {
        name = name.Trim().TrimStart('\\');
        var dot = name.IndexOf('.');
        if (dot >= 0) name = name[..dot];
        return name.ToLowerInvariant();
    }

    private static string CleanIp(string ip) => ip.Trim() is { } s && s != "-" ? (s.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) ? s[7..] : s) : "";

    private static string RoT(long n, string sg, string pl) => LogAnalyzer.Dfir.Language.RoText.Count(n, sg, pl);

    private sealed record AdItem(Severity Severity, DateTimeOffset? Start, DateTimeOffset? End, string Who, EvidenceRef? First, Func<Finding> Make);

    /// <summary>The most serious and earliest findings up to the cap, one by one; the rest are one Low summary (a spray over many accounts would give hundreds).</summary>
    private static void EmitCapped(Ctx c, List<Finding> o, List<AdItem> items, string ruleId, string category, string mitre, string nounSg, string nounPl)
    {
        var ordered = items.OrderByDescending(i => i.Severity).ThenBy(i => i.Start).ToList();
        var listed = ordered.Take(AdMaxFindings).OrderBy(i => i.Start).ToList();
        foreach (var i in listed) o.Add(i.Make());
        var rest = ordered.Skip(AdMaxFindings).ToList();
        if (rest.Count == 0) return;
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = ruleId, Title = $"Neafișate individual: {RoT(rest.Count, nounSg, nounPl)}",
            Severity = Severity.Low, Category = category, Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = mitre, SemanticType = SemanticType.Observation,
            FirstSeenUtc = rest.Min(r => r.Start), LastSeenUtc = rest.Max(r => r.End),
            User = Join(rest.Select(r => r.Who), 5),
            PlainWhat = $"mai sunt {RoT(rest.Count, nounSg, nounPl)} de același fel, neafișate una câte una",
            Description = $"Primele {RoT(AdMaxFindings, nounSg, nounPl)} (cele mai grave și cele mai vechi) sunt listate una câte una; restul de {rest.Count}, ale: {Join(rest.Select(r => r.Who), 5)}, sunt în cronologie. " +
                          "Ce s-a întâmplat: tiparul s-a repetat la scară mare. De ce contează: o întindere atât de mare arată adesea o unealtă automată sau un scanner. Ce verificați: sursa comună a evenimentelor.",
            ClassificationReason = "Limită de afișare, nu o concluzie.",
            SupportingEvidence = rest.Where(r => r.First is not null).Take(20).Select(r => r.First!).ToList(),
            AlternativeExplanations = ["Un scanner de vulnerabilități sau o unealtă de inventar rulată pe scară largă."],
            MissingEvidence = ["Identificarea sursei comune a evenimentelor."],
            RecommendedNextSteps = ["Grupați evenimentele după adresa sursă și după oră."],
        });
    }

    // ---------------------------------------------------------------- KERBEROAST
    // One RC4 service ticket is a legacy service; the same account asking for RC4 tickets for several DIFFERENT services in a few minutes is how
    // Kerberoasting (T1558.003) collects hashes to crack offline.

    private static void Kerberoast(Ctx c, List<Finding> o)
    {
        var hits = c.Events
            .Where(e => Ev(e, "Security", 4769) && T(e) is not null && StatusOk(e) && IsRc4(e))
            .Select(e => (E: e, User: AccountKey(F(e, "TargetUserName")), Svc: F(e, "ServiceName").Trim().ToLowerInvariant()))
            .Where(x => x.User.Length > 0 && !x.User.EndsWith('$') && x.Svc.Length > 0 && !x.Svc.EndsWith('$') && x.Svc != "krbtgt" && !x.Svc.StartsWith("krbtgt/", StringComparison.Ordinal))
            .ToList();
        var items = new List<AdItem>();
        foreach (var byUser in hits.GroupBy(x => x.User, StringComparer.Ordinal))
        {
            var seq = byUser.OrderBy(x => T(x.E)).ToList();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int i = 0, j = -1;
            while (i < seq.Count)
            {
                var start = T(seq[i].E)!.Value;
                while (j + 1 < seq.Count && T(seq[j + 1].E)!.Value - start <= KerberoastWindow)
                {
                    j++;
                    counts[seq[j].Svc] = counts.TryGetValue(seq[j].Svc, out var n) ? n + 1 : 1;
                }
                if (counts.Count >= KerberoastMinServices)
                {
                    var win = seq.GetRange(i, j - i + 1);
                    var user = byUser.Key;
                    items.Add(new AdItem(counts.Count >= KerberoastHighServices ? Severity.High : Severity.Medium, T(win[0].E), T(win[^1].E), user, Ref(win[0].E, win[0].Svc), () => MakeKerberoast(c, user, win.Select(w => (w.E, w.Svc)).ToList())));
                    i = j + 1; counts.Clear();
                    continue;
                }
                if (j >= i && --counts[seq[i].Svc] == 0) counts.Remove(seq[i].Svc);
                i++;
            }
        }
        EmitCapped(c, o, items, "KERBEROAST", "Credential Access", "T1558.003", "serie de tichete Kerberos RC4", "serii de tichete Kerberos RC4");
    }

    private static Finding MakeKerberoast(Ctx c, string user, List<(TimelineEvent E, string Svc)> win)
    {
        var services = win.Select(w => w.Svc).Distinct(StringComparer.Ordinal).ToList();
        var ips = Join(win.Select(w => CleanIp(F(w.E, "IpAddress"))), 5);
        var first = win[0].E; var last = win[^1].E;
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "KERBEROAST",
            Title = $"Tichete Kerberos RC4 pentru {RoT(services.Count, "serviciu", "servicii")} diferite cerute de {user}",
            Severity = services.Count >= KerberoastHighServices ? Severity.High : Severity.Medium, Category = "Credential Access", Classification = Classification.Correlated, Confidence = Confidence.Medium,
            MitreTechniqueId = "T1558.003", SemanticType = SemanticType.Observation,
            FirstSeenUtc = T(first), LastSeenUtc = T(last), User = user, Host = first.Host,
            PlainWhat = $"a cerut, în câteva minute, tichete Kerberos slab criptate (RC4) pentru {RoT(services.Count, "serviciu", "servicii")} diferite ({Join(services, 4)}), cum se adună datele care apoi se sparg offline",
            Description = $"Contul {user} a primit tichete de serviciu cu criptare RC4 pentru {RoT(services.Count, "serviciu", "servicii")} diferite ({Join(services, 8)}) între {Time(T(first))} și {Time(T(last))}, de la adresa {(ips.Length > 0 ? ips : "necunoscută")}. " +
                          "Ce s-a întâmplat: un cont a cerut tichete pentru mai multe servicii, toate cu criptarea veche RC4. " +
                          "De ce contează: tichetele RC4 se pot sparge offline pentru a afla parola contului de serviciu (Kerberoasting). " +
                          "Ce verificați: dacă adresa sursă este o stație obișnuită a contului și dacă parolele conturilor de serviciu vizate sunt lungi sau au fost schimbate.",
            ClassificationReason = $"Cel puțin {KerberoastMinServices} servicii distincte (nu conturi de mașină, nu krbtgt) cu tichet RC4 reușit (4769, stare 0x0) pentru același cont în {KerberoastWindow.TotalMinutes:0} minute. 5 sau mai multe servicii: High; 3-4: Medium (RC4 este încă implicit pentru conturile de serviciu de utilizator fără msDS-SupportedEncryptionTypes).",
            SupportingEvidence = win.Take(15).Select(w => Ref(w.E, $"{w.Svc} de la {(CleanIp(F(w.E, "IpAddress")) is { Length: > 0 } ip ? ip : "adresă necunoscută")}")).ToList(),
            AlternativeExplanations = ["Conturi de serviciu vechi, fără chei AES, care cer tichete RC4 în funcționare normală.", "Un scanner de vulnerabilități sau o unealtă de audit Active Directory."],
            MissingEvidence = ["Dacă hash-urile au fost sparte (nu se vede în jurnale).", "Auditul „Kerberos Service Ticket Operations” trebuie activat pe controlerul de domeniu; fără el regula nu vede nimic."],
            RecommendedNextSteps = ["Identificați stația de la adresa sursă și procesele ei.", "Schimbați parolele conturilor de serviciu vizate și treceți-le pe AES sau pe conturi gestionate (gMSA)."],
        };
    }

    // ---------------------------------------------------------------- ASREP-ROAST

    private static void AsRepRoast(Ctx c, List<Finding> o)
    {
        var hits = c.Events
            .Where(e => Ev(e, "Security", 4768) && T(e) is not null && StatusOk(e) && F(e, "PreAuthType").Trim() == "0")
            .Select(e => (E: e, User: AccountKey(F(e, "TargetUserName"))))
            .Where(x => x.User.Length > 0 && !x.User.EndsWith('$'))
            .ToList();
        var items = new List<AdItem>();
        foreach (var g in hits.GroupBy(x => x.User, StringComparer.Ordinal))
        {
            var evs = g.OrderBy(x => T(x.E)).Select(x => x.E).ToList();
            var user = g.Key;
            bool rc4 = evs.Any(IsRc4);
            items.Add(new AdItem(rc4 ? Severity.High : Severity.Medium, T(evs[0]), T(evs[^1]), user, Ref(evs[0], "4768 fără pre-autentificare"), () => MakeAsRep(c, user, evs, rc4)));
        }
        EmitCapped(c, o, items, "ASREP-ROAST", "Credential Access", "T1558.004", "cont fără pre-autentificare Kerberos", "conturi fără pre-autentificare Kerberos");
    }

    private static Finding MakeAsRep(Ctx c, string user, List<TimelineEvent> evs, bool rc4)
    {
        var ips = Join(evs.Select(e => CleanIp(F(e, "IpAddress"))), 5);
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "ASREP-ROAST",
            Title = $"Contul {user} nu cere pre-autentificare Kerberos și i s-a dat un tichet",
            Severity = rc4 ? Severity.High : Severity.Medium, Category = "Credential Access", Classification = Classification.Correlated, Confidence = rc4 ? Confidence.Medium : Confidence.Low,
            MitreTechniqueId = "T1558.004", SemanticType = SemanticType.Observation,
            FirstSeenUtc = T(evs[0]), LastSeenUtc = T(evs[^1]), User = user, Host = evs[0].Host,
            PlainWhat = $"contul nu cere pre-autentificare Kerberos și i s-a dat un tichet ({RoTextCount(evs.Count)}){(rc4 ? ", cu criptare slabă RC4" : "")}",
            Description = $"Contul {user} a primit un tichet Kerberos (TGT) fără pre-autentificare, {RoTextCount(evs.Count)}, între {Time(T(evs[0]))} și {Time(T(evs[^1]))}" +
                          (ips.Length > 0 ? $", de la {ips}" : "") + (rc4 ? "; tichetul a folosit criptarea RC4, cea mai ușor de spart offline" : "") + ". " +
                          "Ce s-a întâmplat: contul are setat „Nu cere pre-autentificare Kerberos” și oricine a putut cere un tichet pentru el fără să-i știe parola. " +
                          "De ce contează: partea criptată a răspunsului se poate sparge offline pentru a afla parola (AS-REP roasting). " +
                          "Ce verificați: dacă setarea este intenționată (aplicații vechi) și de unde au venit cererile.",
            ClassificationReason = "Eveniment 4768 reușit (stare 0x0) cu tipul de pre-autentificare 0." + (rc4 ? " Tichetul are criptare RC4 (0x17 / 0x18): severitate ridicată." : ""),
            SupportingEvidence = evs.Take(15).Select(e => Ref(e, $"4768 de la {(CleanIp(F(e, "IpAddress")) is { Length: > 0 } ip ? ip : "adresă necunoscută")}")).ToList(),
            AlternativeExplanations = ["Setarea „Nu cere pre-autentificare” este uneori păstrată intenționat pentru aplicații vechi.", "O unealtă de audit Active Directory a verificat conturile."],
            MissingEvidence = ["Dacă răspunsul a fost spart (nu se vede în jurnale).", "Auditul „Kerberos Authentication Service” trebuie activat pe controlerul de domeniu."],
            RecommendedNextSteps = ["Dezactivați setarea contului dacă nu este necesară și schimbați parola.", "Identificați stația care a cerut tichetul."],
        };
    }

    private static string RoTextCount(int n) => LogAnalyzer.Dfir.Language.RoText.Count(n, "eveniment", "evenimente");

    // ---------------------------------------------------------------- ADCS-SAN
    // 4886 (request received) / 4887 (certificate issued) on the certification authority whose request attributes carry a subject alternative name chosen by
    // the requester: the EDITF_ATTRIBUTESUBJECTALTNAME2 path (ESC6, T1649). A SAN carried only in the CSR extension (ESC1) is NOT in these attributes. Issued = High; only requested = Medium (nothing here says it was issued).

    private static readonly Regex SanRx = new(@"(?:^|[\r\n])[ \t]*san[ \t]*:[ \t]*([^\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string SanValue(string attributes)
    {
        if (attributes.Length == 0) return "";
        var m = SanRx.Match(attributes);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static void AdcsSan(Ctx c, List<Finding> o)
    {
        var hits = c.Events
            .Where(e => Ev(e, "Security", 4886, 4887) && T(e) is not null)
            .Select(e => (E: e, San: SanValue(F(e, "Attributes"))))
            .Where(x => x.San.Length > 0)
            .ToList();
        // a request and its issuance are one story: same CA host and RequestId
        var items = new List<AdItem>();
        foreach (var g in hits.GroupBy(x => F(x.E, "RequestId").Trim() is { Length: > 0 } id ? x.E.Host.ToLowerInvariant() + "|" + id : "ev|" + x.E.EvidenceId + "|" + x.E.Locator, StringComparer.Ordinal))
        {
            var list = g.OrderBy(x => T(x.E)).ToList();
            bool issued = list.Any(x => x.E.EventId == "4887");
            var main = list.First(x => x.E.EventId == (issued ? "4887" : "4886"));
            var requester = F(main.E, "Requester", "RequesterName").Trim();
            items.Add(new AdItem(issued ? Severity.High : Severity.Medium, T(list[0].E), T(list[^1].E), requester.Length > 0 ? requester : "solicitant necunoscut",
                Ref(main.E, "SAN " + Trunc(main.San, 60)), () => MakeAdcs(c, list.Select(x => (x.E, x.San)).ToList(), issued, main.E, main.San, requester)));
        }
        EmitCapped(c, o, items, "ADCS-SAN", "Credential Access", "T1649", "cerere de certificat cu nume alternativ", "cereri de certificat cu nume alternativ");
    }

    private static Finding MakeAdcs(Ctx c, List<(TimelineEvent E, string San)> list, bool issued, TimelineEvent main, string san, string requester)
    {
        var reqId = F(main, "RequestId").Trim();
        var who = requester.Length > 0 ? requester : "un solicitant necunoscut";
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "ADCS-SAN",
            Title = issued ? $"Certificat emis cu un nume alternativ ales de solicitant ({Trunc(san, 80)})" : $"Cerere de certificat cu un nume alternativ ales de solicitant ({Trunc(san, 80)}), emiterea nu se vede",
            Severity = issued ? Severity.High : Severity.Medium, Category = "Credential Access", Classification = issued ? Classification.Correlated : Classification.Candidate, Confidence = issued ? Confidence.Medium : Confidence.Low,
            MitreTechniqueId = "T1649", SemanticType = SemanticType.Observation,
            FirstSeenUtc = T(list[0].E), LastSeenUtc = T(list[^1].E), User = who, Host = main.Host,
            PlainWhat = issued ? $"a primit un certificat în care el a ales numele pentru care este valabil ({Trunc(san, 60)})" : $"a cerut un certificat în care el alege numele pentru care este valabil ({Trunc(san, 60)})",
            Description = $"Autoritatea de certificare {(main.Host.Length > 0 ? main.Host : "(necunoscută)")}: {who} a {(issued ? "primit" : "cerut")} un certificat (cerere {(reqId.Length > 0 ? reqId : "necunoscută")}) cu numele alternativ „{Trunc(san, 120)}” la {Time(T(main))}. " +
                          "Ce s-a întâmplat: în cerere solicitantul a scris singur numele alternativ (SAN) al certificatului. " +
                          "De ce contează: când autoritatea de certificare acceptă un SAN din atributele cererii (setarea EDITF_ATTRIBUTESUBJECTALTNAME2, ESC6), oricine poate cere un certificat pentru alt cont, de exemplu un administrator, și apoi obține tichete Kerberos în numele lui. " +
                          "Ce verificați: dacă autoritatea de certificare are setarea EDITF_ATTRIBUTESUBJECTALTNAME2 activă și dacă numele din SAN aparține unui cont privilegiat.",
            ClassificationReason = issued ? "Eveniment 4887 (certificat emis) cu un SAN în atributele cererii." : "Eveniment 4886 (cerere primită) cu un SAN în atributele cererii; emiterea nu este dovedită de acest eveniment.",
            SupportingEvidence = list.Take(15).Select(x => Ref(x.E, (x.E.EventId == "4887" ? "emis" : "cerere") + ", SAN " + Trunc(x.San, 60))).ToList(),
            AlternativeExplanations = ["Unele șabloane web sau de înscriere legitime transportă un SAN în cerere.", "Un administrator a emis certificatul pentru un server, cu nume DNS alternative."],
            MissingEvidence = ["Dacă autoritatea de certificare are EDITF_ATTRIBUTESUBJECTALTNAME2 (ESC6): nu se vede în jurnale.", "Un SAN pus doar în extensia cererii (CSR), cum este la șabloanele ESC1, NU apare în 4886 / 4887: absența acestui semnal nu exclude ESC1.", "Auditul autorității de certificare este oprit implicit; fără el aceste evenimente nu există.", "Dacă certificatul a fost apoi folosit la autentificare (4768 cu certificat)."],
            RecommendedNextSteps = ["Verificați setarea EDITF_ATTRIBUTESUBJECTALTNAME2 a autorității de certificare (EditFlags din politica ei) și setările șablonului folosit.", "Căutați un 4768 cu autentificare prin certificat după emitere."],
        };
    }

    // ---------------------------------------------------------------- NTLM-RELAY-MACHINE
    // In a relayed NTLM authentication the AUTHENTICATE message (so WorkstationName) is the victim's; only the source IP is the attacker's. So the rule
    // does not compare names: per computer account it learns the addresses it signed in from by Kerberos (4624 type 3) in the same timeline and flags a
    // type 3 NTLM sign-in of that account from an address outside that set (T1557.001). No Kerberos baseline for the account = no finding.

    /// <summary>The address in one canonical form (IPv6 compressed and lower-case, review round 2), "" for loopback, "-" or unparsable values left as written.</summary>
    private static string NormIp(string ip)
    {
        if (CleanIp(ip) is not { Length: > 0 } s || s is "::1" or "127.0.0.1") return "";
        return System.Net.IPAddress.TryParse(s, out var a) ? (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString().ToLowerInvariant() : s.ToLowerInvariant();
    }

    private static string? MachineAccountKey(TimelineEvent e)
    {
        var t = F(e, "TargetUserName").Trim();
        return t.Length >= 2 && t.EndsWith('$') && !t.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase) ? t.ToLowerInvariant() : null;
    }

    private static void NtlmRelayMachine(Ctx c, List<Finding> o)
    {
        // per account: each address with the time of its FIRST Kerberos sign-in. Only addresses seen before an NTLM sign-in count for it (review round 2:
        // after a relay to the CA web enrolment, a PKINIT ticket gives the attacker's address a later Kerberos sign-in that must not excuse the relay)
        var baseline = new Dictionary<string, Dictionary<string, DateTimeOffset>>(StringComparer.Ordinal);
        var ntlm = new List<(TimelineEvent E, string Account, string Ip)>();
        foreach (var e in c.Events)
        {
            if (!Ev(e, "Security", 4624) || T(e) is not { } when || F(e, "LogonType").Trim() != "3") continue;
            if (MachineAccountKey(e) is not { } acc) continue;
            var ip = NormIp(F(e, "IpAddress"));
            if (ip.Length == 0) continue;
            var pkg = F(e, "AuthenticationPackageName").Trim();
            if (pkg.Equals("Kerberos", StringComparison.OrdinalIgnoreCase))
            {
                if (!baseline.TryGetValue(acc, out var first)) baseline[acc] = first = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                if (!first.TryGetValue(ip, out var t0) || when < t0) first[ip] = when;
            }
            else if (pkg.Equals("NTLM", StringComparison.OrdinalIgnoreCase)) ntlm.Add((e, acc, ip));
        }
        List<string> KnownBefore(string account, DateTimeOffset when) =>
            baseline.TryGetValue(account, out var first) ? [.. first.Where(kv => kv.Value <= when).Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal)] : [];
        var hits = ntlm.Where(x => KnownBefore(x.Account, T(x.E)!.Value) is { Count: > 0 } known && !known.Contains(x.Ip)).ToList();
        var items = new List<AdItem>();
        foreach (var g in hits.GroupBy(x => (x.Account, Host: x.E.Host.ToLowerInvariant(), x.Ip)))
        {
            var list = g.OrderBy(x => T(x.E)).ToList();
            var known = KnownBefore(g.Key.Account, T(list[0].E)!.Value);
            items.Add(new AdItem(Severity.Medium, T(list[0].E), T(list[^1].E), F(list[0].E, "TargetUserName").Trim(),
                Ref(list[0].E, $"{F(list[0].E, "TargetUserName")} prin NTLM de la {list[0].Ip}"), () => MakeRelay(c, list, known)));
        }
        EmitCapped(c, o, items, "NTLM-RELAY-MACHINE", "Lateral Movement", "T1557.001", "autentificare NTLM dintr-o adresă nouă", "autentificări NTLM dintr-o adresă nouă");
    }

    private static Finding MakeRelay(Ctx c, List<(TimelineEvent E, string Account, string Ip)> list, List<string> known)
    {
        var first = list[0]; var name = F(first.E, "TargetUserName").Trim();
        var ws = Join(list.Select(x => F(x.E, "WorkstationName")), 3);
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "NTLM-RELAY-MACHINE",
            Title = $"Contul de calculator {name} s-a autentificat prin NTLM de la o adresă de pe care acest calculator nu s-a mai autentificat (Kerberos): {first.Ip}",
            Severity = Severity.Medium, Category = "Lateral Movement", Classification = Classification.Correlated, Confidence = Confidence.Medium,
            MitreTechniqueId = "T1557.001", SemanticType = SemanticType.Observation,
            FirstSeenUtc = T(first.E), LastSeenUtc = T(list[^1].E), User = name, Host = first.E.Host,
            PlainWhat = $"contul calculatorului {name} s-a autentificat prin NTLM de la o adresă nouă ({first.Ip}), de pe care nu se mai autentificase prin Kerberos ({RoTextCount(list.Count)})",
            Description = $"Pe {(first.E.Host.Length > 0 ? first.E.Host : "stația analizată")}, contul {name} s-a autentificat în rețea (tip 3) prin NTLM de la {first.Ip}, {RoTextCount(list.Count)}, între {Time(T(first.E))} și {Time(T(list[^1].E))}. " +
                          $"Înainte de această autentificare, în același jurnal, contul se autentificase prin Kerberos doar de la {Join(known, 4)}" + (ws.Length > 0 ? $"; numele de stație din eveniment: {ws}" : "") + ". " +
                          "Ce s-a întâmplat: un cont de calculator a folosit NTLM de la o adresă diferită de cele de la care se autentifică prin Kerberos. " +
                          "De ce contează: una dintre explicații este o autentificare forțată (PetitPotam, PrinterBug) retransmisă de un atacator, caz în care numai adresa sursă este a atacatorului, iar numele de stație este al victimei; alte explicații sunt nevinovate (vezi mai jos). " +
                          "Ce verificați: cui aparține adresa sursă și ce a făcut contul imediat după.",
            ClassificationReason = "4624 de tip 3, pachet NTLM, cont care se termină în „$”, de la o adresă absentă din cele de la care același cont s-a autentificat prin Kerberos (cel puțin o adresă cunoscută).",
            SupportingEvidence = list.Take(15).Select(x => Ref(x.E, $"{F(x.E, "TargetUserName")} prin NTLM de la {x.Ip}")).ToList(),
            AlternativeExplanations = ["Schimbarea adresei prin DHCP sau o adresă nouă a aceluiași calculator.", "Calculator cu mai multe plăci de rețea (multihomed).", "NAT sau un server intermediar care schimbă adresa văzută.",
                "Cont de serviciu gestionat de grup (gMSA) folosit de pe o gazdă nouă, sau un nod de cluster.", "NTLM de rezervă în medii mixte.",
                "Rețea cu IPv4 și IPv6 în paralel: același calculator poate folosi Kerberos pe o adresă și NTLM pe cealaltă."],
            MissingEvidence = ["Inventarul adreselor calculatorului (DNS / CMDB).", "Jurnalele DHCP care leagă adresa de un calculator.", "Ce a făcut contul după autentificare (4672, 4662, 4769).", "Auditul „Logon” trebuie activat pe serverul ținta."],
            RecommendedNextSteps = ["Aflați cui aparține adresa sursă (DHCP, DNS, inventar).", "Verificați dacă serverul ținta cere semnare SMB / LDAP și dacă NTLM poate fi restricționat."],
        };
    }

    // ---------------------------------------------------------------- EDR-WFP-BLOCK
    // EDRSilencer-style tools add Windows Filtering Platform filters that block the EDR agent's outbound traffic: the filter itself (5441 / 5447, name
    // "Custom Outbound Filter") and the connections it blocks (5157) for a known security process. T1562.001.

    private static void EdrWfpBlock(Ctx c, List<Finding> o)
    {
        var items = new List<AdItem>();
        bool any5157 = c.Events.Any(e => Ev(e, "Security", 5157));

        var filters = c.Events
            .Where(e => Ev(e, "Security", 5441, 5447) && T(e) is not null && F(e, "FilterName", "Filter Name", "FilterDisplayName").Contains("Custom Outbound Filter", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var g in filters.GroupBy(e => e.Host.ToLowerInvariant()))
        {
            var list = g.OrderBy(e => T(e)).ToList();
            items.Add(new AdItem(Severity.High, T(list[0]), T(list[^1]), list[0].Host.Length > 0 ? list[0].Host : "stația analizată", Ref(list[0], "filtru WFP „Custom Outbound Filter”"), () => MakeEdrFilter(c, list, any5157)));
        }

        var blocks = c.Events
            .Where(e => Ev(e, "Security", 5157) && T(e) is not null)
            .Select(e => (E: e, App: EdrApp(F(e, "Application", "ApplicationName"))))
            .Where(x => x.App.Length > 0)
            .ToList();
        foreach (var g in blocks.GroupBy(x => (Host: x.E.Host.ToLowerInvariant(), x.App)))
        {
            var seq = g.OrderBy(x => T(x.E)).ToList();
            int i = 0;
            while (i < seq.Count)
            {
                int j = i;
                while (j + 1 < seq.Count && T(seq[j + 1].E)!.Value - T(seq[i].E)!.Value <= EdrBlockWindow) j++;
                if (j - i + 1 >= EdrMinBlocks)
                {
                    var win = seq.GetRange(i, j - i + 1).Select(x => x.E).ToList(); var app = g.Key.App;
                    items.Add(new AdItem(Severity.High, T(win[0]), T(win[^1]), app, Ref(win[0], "conexiune blocată pentru " + app), () => MakeEdrBlocks(c, app, win)));
                    i = j + 1;
                }
                else i++;
            }
        }
        EmitCapped(c, o, items, "EDR-WFP-BLOCK", "Defense Evasion", "T1562.001", "semnal de blocare a EDR-ului", "semnale de blocare a EDR-ului");
    }

    /// <summary>The file name of a monitored security process, or "" when the application is none of them.</summary>
    private static string EdrApp(string application)
    {
        var name = WinPath.GetFileName(application.Trim()).ToLowerInvariant();
        return EdrProcesses.Contains(name) ? name : "";
    }

    private static Finding MakeEdrFilter(Ctx c, List<TimelineEvent> list, bool any5157)
    {
        var host = list[0].Host.Length > 0 ? list[0].Host : "stația analizată";
        var missing = new List<string> { "Procesul care a adăugat filtrele (5441 / 5447 nu îl numesc).", "Ce aplicații sunt blocate de filtre (se vede doar prin 5157)." };
        if (!any5157) missing.Add("Auditul „Filtering Platform Connection” (5157) lipsește: nu se vede ce conexiuni au fost blocate efectiv.");
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "EDR-WFP-BLOCK",
            Title = $"Filtre WFP „Custom Outbound Filter” pe {host}",
            Severity = Severity.High, Category = "Defense Evasion", Classification = Classification.Correlated, Confidence = Confidence.Medium,
            MitreTechniqueId = "T1562.001", SemanticType = SemanticType.Observation,
            FirstSeenUtc = T(list[0]), LastSeenUtc = T(list[^1]), Host = list[0].Host,
            PlainWhat = $"au apărut filtre de rețea numite „Custom Outbound Filter”, nume folosit de uneltele care taie legătura programelor de securitate cu serverul lor ({RoTextCount(list.Count)})",
            Description = $"Pe {host} au fost înregistrate {RoTextCount(list.Count)} (5441 / 5447) cu filtre Windows Filtering Platform numite „Custom Outbound Filter”, între {Time(T(list[0]))} și {Time(T(list[^1]))}. " +
                          "Ce s-a întâmplat: au fost adăugate filtre de rețea pentru trafic de ieșire, cu numele implicit al unei unelte de reducere la tăcere a EDR-ului (EDRSilencer). " +
                          "De ce contează: astfel de filtre pot tăia legătura unui agent de securitate cu consola lui, fără să-l oprească; ce program blochează aceste filtre nu se vede din 5441 / 5447. " +
                          "Ce verificați: ce aplicații blochează filtrele (netsh wfp show filters), cine le-a creat și dacă agentul mai raportează.",
            ClassificationReason = "Eveniment 5441 / 5447 cu un nume de filtru „Custom Outbound Filter”.",
            SupportingEvidence = list.Take(15).Select(e => Ref(e, "filtru WFP " + Trunc(F(e, "FilterName", "Filter Name", "FilterDisplayName"), 60))).ToList(),
            AlternativeExplanations = ["Un administrator a adăugat o regulă de firewall cu un nume asemănător.", "Izolarea rețelei prin politică (de exemplu izolarea unei stații suspecte)."],
            MissingEvidence = missing,
            RecommendedNextSteps = ["Listați filtrele WFP active și aplicațiile vizate.", "Verificați dacă agentul EDR a raportat în intervalul respectiv."],
        };
    }

    private static Finding MakeEdrBlocks(Ctx c, string app, List<TimelineEvent> win)
    {
        var host = win[0].Host.Length > 0 ? win[0].Host : "stația analizată";
        var dests = Join(win.Select(e => F(e, "DestAddress", "DestinationAddress")), 4);
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "EDR-WFP-BLOCK",
            Title = $"Conexiunile programului de securitate {app} sunt blocate de Windows Filtering Platform",
            Severity = Severity.High, Category = "Defense Evasion", Classification = Classification.Correlated, Confidence = Confidence.Medium,
            MitreTechniqueId = "T1562.001", SemanticType = SemanticType.Observation,
            FirstSeenUtc = T(win[0]), LastSeenUtc = T(win[^1]), Host = win[0].Host, Process = app,
            PlainWhat = $"conexiunile agentului de securitate {app} au fost blocate (cel puțin {win.Count} în {Math.Max(1, (int)Math.Ceiling((T(win[^1])!.Value - T(win[0])!.Value).TotalMinutes))} min)",
            Description = $"Pe {host}, Windows a blocat {LogAnalyzer.Dfir.Language.RoText.Count(win.Count, "conexiune", "conexiuni")} ale {app} (5157) între {Time(T(win[0]))} și {Time(T(win[^1]))}" + (dests.Length > 0 ? $", spre {dests}" : "") + ". " +
                          "Ce s-a întâmplat: un program de securitate cunoscut a fost oprit repetat din a ieși în rețea. " +
                          "De ce contează: blocările repetate pot însemna că agentul nu mai ajunge la consola lui; dacă alertele au mai ajuns se vede doar în consola EDR. " +
                          "Ce verificați: ce filtru blochează conexiunea (Filter Run-Time ID din 5157) și cine l-a creat.",
            ClassificationReason = $"Cel puțin {EdrMinBlocks} evenimente 5157 pentru același program de securitate în {EdrBlockWindow.TotalMinutes:0} minute.",
            SupportingEvidence = win.Take(15).Select(e => Ref(e, "conexiune blocată pentru " + app)).ToList(),
            AlternativeExplanations = ["O regulă de firewall adăugată de un administrator.", "Izolarea rețelei prin politică."],
            MissingEvidence = ["Procesul care a creat filtrul blocant.", "Dacă agentul a rămas deconectat de consola lui după aceea."],
            RecommendedNextSteps = ["Căutați filtrele 5441 / 5447 din același interval.", "Verificați starea agentului în consola EDR."],
        };
    }
}
