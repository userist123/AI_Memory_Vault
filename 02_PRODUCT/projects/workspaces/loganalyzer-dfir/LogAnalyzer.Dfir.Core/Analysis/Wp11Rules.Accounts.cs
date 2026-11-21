using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private static readonly Regex DnCommonName = new(@"CN=(?<cn>[^,]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>The account named by a group-membership event: SID when present, name from the distinguished name otherwise.</summary>
    private static (string Name, string Sid) Member(TimelineEvent e)
    {
        var dn = F(e, "MemberName");
        var m = DnCommonName.Match(dn);
        var name = m.Success ? m.Groups["cn"].Value : dn is "-" ? "" : dn;
        var sid = F(e, "MemberSid");
        return (name, sid is "-" ? "" : sid);
    }

    /// <summary>The account name the evidence gives a SID (4720 TargetSid, 4624 TargetUserSid, 4688 SubjectUserSid), or "" when none does.</summary>
    private static string NameOfSid(Ctx c, string sid)
    {
        foreach (var (idKey, nameKey, ids) in new[] { ("TargetSid", "TargetUserName", new[] { 4720, 4722, 4738 }), ("TargetUserSid", "TargetUserName", new[] { 4624 }), ("SubjectUserSid", "SubjectUserName", new[] { 4688, 4672 }) })
        {
            var hit = c.Events.FirstOrDefault(e => Ev(e, "Security", ids) && F(e, idKey).Equals(sid, StringComparison.OrdinalIgnoreCase) && F(e, nameKey) is { Length: > 0 } n && n != "-");
            if (hit is not null) return F(hit, nameKey);
        }
        return DomainGroupOfSid(sid);
    }

    /// <summary>
    /// The well-known groups of a domain by their relative id (S-1-5-21-&lt;domain&gt;-512 is Domain Admins in every domain), or "". No event names them:
    /// lab SRV01 2026-11-06 showed "S-1-5-21-…-512 → Administrators", which is Windows adding Domain Admins at the domain join.
    /// </summary>
    public static string DomainGroupOfSid(string sid)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sid, @"^S-1-5-21-\d+-\d+-\d+-(?<rid>\d+)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!m.Success) return "";
        return m.Groups["rid"].Value switch
        {
            "512" => "Domain Admins",
            "513" => "Domain Users",
            "514" => "Domain Guests",
            "515" => "Domain Computers",
            "516" => "Domain Controllers",
            "518" => "Schema Admins",
            "519" => "Enterprise Admins",
            "520" => "Group Policy Creator Owners",
            _ => "",
        };
    }

    /// <summary>The first records of a Security log that was never cleared: what Windows wrote while it was being installed.</summary>
    internal const long SetupRecordLimit = 500;

    /// <summary>
    /// An account Windows created while it was being installed (defaultuser0, the setup administrator): created by SYSTEM / the machine account AND among
    /// the first <see cref="SetupRecordLimit"/> records of a Security log with no clear (1102). The record number, not the clock: lab VMs started with the
    /// clock hours ahead. An attacker running as SYSTEM later cannot be in that range unless the log was cleared, and then the rule does not apply
    /// (review round 1: never lower a privileged account on the creator alone).
    /// </summary>
    private static bool CreatedAtSetup(Ctx c, TimelineEvent e, string creator) =>
        (IsMachineAccount(creator) || creator.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)) &&
        EvtxCompleteness.RecordId(e) is > 0 and <= SetupRecordLimit && !c.Events.Any(x => Ev(x, "Security", 1102))
        || AtDomainControllerPromotion(c, e, creator);

    /// <summary>Margin around the creation of krbtgt in which the promotion of the server to domain controller writes its own accounts and groups.</summary>
    internal static readonly TimeSpan PromotionWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The promotion of a server to domain controller (lab DC01 2026-11-07): Windows creates krbtgt as ANONYMOUS LOGON (S-1-5-7), then its built-in
    /// accounts and the Administrator's memberships in Domain Admins / Enterprise Admins / Schema Admins, all within minutes. Only an action of
    /// ANONYMOUS LOGON, SYSTEM or the machine account in that window, on the host where krbtgt was created, in a Security log with no clear (1102).
    /// An account created by a person is never lowered here.
    /// </summary>
    private static bool AtDomainControllerPromotion(Ctx c, TimelineEvent e, string creator)
    {
        bool windows = IsMachineAccount(creator) || creator.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || creator.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase)
                       || creator.Length == 0 || creator == "-";
        if (!windows || e.Time.Utc is not { } at || c.Events.Any(x => Ev(x, "Security", 1102))) return false;
        return c.Events.Any(k => Ev(k, "Security", 4720) && k.Time.Utc is { } kt && (kt - at).Duration() <= PromotionWindow
                                 && F(k, "TargetUserName").Equals("krbtgt", StringComparison.OrdinalIgnoreCase)
                                 && F(k, "SubjectUserSid").Equals("S-1-5-7", StringComparison.OrdinalIgnoreCase)
                                 && k.Host.Equals(e.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameAccount(string name, string sid, string otherName, string otherSid)
    {
        if (sid.Length > 0 && otherSid.Length > 0 && sid != "-" && otherSid != "-") return sid.Equals(otherSid, StringComparison.OrdinalIgnoreCase);
        return name.Length > 0 && name.Equals(otherName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Account lifecycle. ACCOUNT-CREATED (4720) says whether the account was used in the collected evidence; ACCOUNT-ADDED-PRIVILEGED-GROUP
    /// (4728/4732/4756) is Medium; ACCOUNT-CREATED-THEN-USED needs a later 4624/4648 for the same SID (or name) and is High only when
    /// the account is also privileged AND was used from another host. "Created" never means "used".
    /// </summary>
    private static void Accounts(Ctx c, List<Finding> o)
    {
        var lists = c.Data.Lists.PrivilegedGroups;
        var created = c.Events.Where(e => Ev(e, "Security", 4720) && F(e, "TargetUserName").Length > 0).OrderBy(T).ToList();
        var groupAdds = c.Events.Where(e => Ev(e, "Security", 4728, 4732, 4756)).OrderBy(T).ToList();
        var logons = c.Events.Where(e => Ev(e, "Security", 4624, 4648) && F(e, "TargetUserName").Length > 0).OrderBy(T).ToList();
        var privAdds = groupAdds.Where(e => lists.IsPrivileged(F(e, "TargetUserName"), F(e, "TargetSid"))).ToList();

        foreach (var g in privAdds.GroupBy(e => (Member: Member(e) is var m && m.Sid.Length > 0 ? m.Sid.ToUpperInvariant() : m.Name.ToLowerInvariant(), Group: F(e, "TargetUserName").ToLowerInvariant() + "|" + F(e, "TargetSid"))))
        {
            var first = g.First();
            var (name, sid) = Member(first);
            // a local group (4732) logs the member as "-" with its SID: take the name from the account's own 4720 or logons (lab VM 2026-11-06 titles showed bare SIDs)
            if (name.Length == 0 && sid.Length > 0) name = NameOfSid(c, sid);
            var who = name.Length > 0 ? name : sid;
            var group = F(first, "TargetUserName");
            var by = Account(F(first, "SubjectUserName"), F(first, "SubjectDomainName"));
            var inCase = created.FirstOrDefault(cr => SameAccount(F(cr, "TargetUserName"), F(cr, "TargetSid"), name, sid));
            // put into the group by Windows itself while it was being installed (lab WS02: labadmin, defaultuser0 → Administrators, Medium)
            bool atSetup = CreatedAtSetup(c, first, F(first, "SubjectUserName")) && (inCase is null || CreatedAtSetup(c, inCase, F(inCase, "SubjectUserName")));
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "ACCOUNT-ADDED-PRIVILEGED-GROUP", Title = $"Cont adăugat într-un grup privilegiat: {who} → {group}",
                Severity = atSetup ? Severity.Low : Severity.Medium, Category = "Privilege Escalation", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1098",
                FirstSeenUtc = T(g.First()), LastSeenUtc = T(g.Last()), User = who,
                PlainWhat = $"Contul {(name.Length > 0 ? PlainPhrases.Q(name) : "(necunoscut după nume)")} a fost pus în grupul {PlainPhrases.Q(group)} " +
                            (group.Equals("Administrators", StringComparison.OrdinalIgnoreCase) || group.Equals("Administratori", StringComparison.OrdinalIgnoreCase)
                                ? "(administratori: control total asupra calculatorului)" : "(un grup cu drepturi mari)") +
                            (PlainPhrases.IsStationAccount(F(first, "SubjectUserName"), first.Host, F(first, "SubjectUserSid")) ? "" : $", de către contul {PlainPhrases.Q(F(first, "SubjectUserName"))}") +
                            (atSetup ? (AtDomainControllerPromotion(c, first, F(first, "SubjectUserName"))
                                    ? "; asta a făcut Windows când serverul a fost transformat în controler de domeniu"
                                    : "; asta a făcut Windows la instalare (printre primele înregistrări ale jurnalului de securitate)")
                             : inCase is not null ? "; contul fusese creat chiar în acest caz" : ""),
                Description = $"{who} ({sid}) a fost adăugat în grupul {group} ({F(first, "TargetSid")}) de {by}, eveniment {first.EventId}, {Time(T(first))}." +
                              (inCase is not null ? $" Contul a fost creat în același caz ({Time(T(inCase))})." : "") +
                              (atSetup ? AtDomainControllerPromotion(c, first, F(first, "SubjectUserName"))
                                  ? $" Adăugat de Windows în cel mult {PromotionWindow.TotalMinutes:0} minute de la crearea krbtgt de către ANONYMOUS LOGON (promovarea în controler de domeniu), jurnal Security negolit: de aceea Low."
                                  : $" Adăugat de SYSTEM / contul mașinii printre primele {SetupRecordLimit} înregistrări ale jurnalului Security, care nu a fost golit: instalarea Windows, de aceea Low." : ""),
                ClassificationReason = "Eveniment Security 4728/4732/4756 pentru un grup din lista de grupuri privilegiate (după nume sau SID). Arată apartenența acordată, nu folosirea ei.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = g.Take(10).Select(e => Ref(e, $"{e.EventId} {group}")).ToList(),
                AlternativeExplanations = DomainGroupOfSid(sid) == "Domain Admins" && group.Equals("Administrators", StringComparison.OrdinalIgnoreCase)
                    ? ["La alăturarea stației în domeniu, Windows adaugă singur Domain Admins în grupul local Administrators; verificați dacă momentul coincide cu alăturarea.", "Administrare planificată."]
                    : ["Administrare planificată (cont de serviciu, administrator nou, grup de acces la distanță).", "Subiectul evenimentului poate fi un administrator autorizat."],
            });
        }

        int allLogonEvents = logons.Count;
        foreach (var cr in created.GroupBy(e => F(e, "TargetSid").Length > 0 ? F(e, "TargetSid").ToUpperInvariant() : F(e, "TargetUserName").ToLowerInvariant()).Select(g => g.First()))
        {
            var name = F(cr, "TargetUserName"); var sid = F(cr, "TargetSid");
            var createdAt = T(cr);
            var by = Account(F(cr, "SubjectUserName"), F(cr, "SubjectDomainName"));
            bool promotion = AtDomainControllerPromotion(c, cr, F(cr, "SubjectUserName"));
            bool provisioning = IsMachineAccount(F(cr, "SubjectUserName")) || F(cr, "SubjectUserName").Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || promotion;

            var lifecycle = c.Events.Where(e => Ev(e, "Security", 4722, 4724, 4738) && SameAccount(name, sid, F(e, "TargetUserName"), F(e, "TargetSid"))).ToList();
            var lifeText = string.Join(", ", lifecycle.GroupBy(e => e.EventId).OrderBy(g => g.Key).Select(g => $"{g.Count()} × {g.Key}"));
            var myGroups = groupAdds.Where(e => { var m = Member(e); return SameAccount(name, sid, m.Name, m.Sid); }).Select(e => F(e, "TargetUserName")).Distinct().ToList();
            var uses = logons.Where(e => (createdAt is null || T(e) is null || T(e) >= createdAt) &&
                                         SameAccount(name, sid, F(e, "TargetUserName"), e.EventId == "4624" ? F(e, "TargetUserSid") : "")).ToList();
            bool sidMatched = sid.Length > 0 && uses.Any(u => u.EventId == "4624" && F(u, "TargetUserSid").Equals(sid, StringComparison.OrdinalIgnoreCase));
            bool privileged = privAdds.Any(e => { var m = Member(e); return SameAccount(name, sid, m.Name, m.Sid); });
            var remoteUses = uses.Where(u => u.EventId == "4624" && F(u, "LogonType") is "3" or "8" or "10" && IsRemoteAddress(F(u, "IpAddress"))).ToList();

            string usage = uses.Count > 0
                ? $"Folosit după creare în dovezile colectate: {uses.Count} autentificări (vezi ACCOUNT-CREATED-THEN-USED)."
                : "creat, nefolosit în dovezile colectate" + (allLogonEvents == 0
                    ? "; nu s-a colectat nicio autentificare (4624/4648), deci lipsa nu arată că nu a fost folosit."
                    : $"; în dovezi există {allLogonEvents} autentificări ale altor conturi, niciuna a acestui cont după creare.");
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "ACCOUNT-CREATED", Title = $"Cont creat: {name}",
                Severity = provisioning ? Severity.Info : Severity.Low, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1136",
                FirstSeenUtc = createdAt, User = name,
                PlainWhat = $"Contul {PlainPhrases.Q(name)} a fost creat " +
                            (promotion ? "de Windows, când serverul a fost transformat în controler de domeniu"
                                : provisioning ? "de Windows (la instalare sau printr-un serviciu)" : $"de contul {PlainPhrases.Q(F(cr, "SubjectUserName"))}") +
                            (uses.Count > 0 ? ", apoi a fost folosit pentru a intra pe calculator"
                                : allLogonEvents == 0 ? "; nu se știe dacă a fost folosit, pentru că nu s-a colectat lista cu cine a intrat pe calculator"
                                : "; după creare nu apare nicio intrare cu acest cont în probele colectate"),
                Description = $"{Account(name, F(cr, "TargetDomainName"))} ({sid}) creat de {by}, {Time(createdAt)}." +
                              (lifeText.Length > 0 ? $" Alte evenimente de ciclu de viață: {lifeText}." : "") +
                              (myGroups.Count > 0 ? $" Grupuri în care a fost adăugat: {Join(myGroups)}." : "") + " " + usage,
                ClassificationReason = "Eveniment Security 4720. Arată crearea contului, nu folosirea lui; severitatea e Info când creatorul este SYSTEM/cont de mașină (aprovizionare), altfel Low.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = [Ref(cr, "4720 cont creat"), .. lifecycle.Take(5).Select(e => Ref(e, e.EventId))],
                AlternativeExplanations = ["Angajat nou, cont de serviciu sau cont creat de un instrument de administrare."],
            });

            if (uses.Count == 0) continue;
            var firstUse = uses[0];
            var sev = privileged && remoteUses.Count > 0 ? Severity.High : privileged || remoteUses.Count > 0 ? Severity.Medium : Severity.Low;
            if (!sidMatched && sev == Severity.High) sev = Severity.Medium;   // a name-only match cannot exclude a reused name
            // created by SYSTEM / the machine (Windows setup: defaultuser0, the unattended administrator) and used only locally: provisioning, Low
            // (lab VM 2026-11-06: every freshly installed station showed Medium "created then used" for its setup accounts)
            // never for a privileged account: an attacker running as SYSTEM creating a local administrator looks the same (review round 1)
            if (provisioning && !privileged && remoteUses.Count == 0) sev = Severity.Low;
            // created and made administrator by Windows while it was being installed, used only locally: Low (lab WS02: labadmin Medium)
            bool atSetup = CreatedAtSetup(c, cr, F(cr, "SubjectUserName")) && remoteUses.Count == 0;
            if (atSetup) sev = Severity.Low;
            var types = uses.Where(u => u.EventId == "4624").GroupBy(u => F(u, "LogonType")).Select(g => $"tip {g.Key}: {g.Count()}").ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "ACCOUNT-CREATED-THEN-USED", Title = $"Cont creat și folosit ulterior: {name}" + (privileged ? " (și adăugat într-un grup privilegiat)" : ""),
                Severity = sev, Category = "Persistence", Classification = sidMatched ? Classification.Correlated : Classification.Candidate,
                Confidence = sidMatched ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1078",
                FirstSeenUtc = createdAt, LastSeenUtc = T(uses[^1]), User = name, Ip = remoteUses.Count > 0 ? F(remoteUses[0], "IpAddress") : "",
                PlainWhat = (atSetup ? $"Contul {PlainPhrases.Q(name)}, creat de Windows la instalare, a fost folosit apoi pentru a intra pe calculator ({PlainPhrases.Times(uses.Count)}), doar de pe calculator"
                    : $"Contul {PlainPhrases.Q(name)}, creat în acest caz, a fost folosit după aceea pentru a intra pe calculator ({PlainPhrases.Times(uses.Count)})") +
                            (remoteUses.Count > 0 ? $", inclusiv de la distanță, de la adresa {F(remoteUses[0], "IpAddress")}" : "") +
                            (privileged ? (atSetup ? "; Windows l-a pus și în grupul administratorilor" : "; contul a fost pus și într-un grup cu drepturi mari") : ""),
                Description = $"{name} creat {Time(createdAt)}; prima folosire {Time(T(firstUse))} ({firstUse.EventId}); {LogAnalyzer.Dfir.Language.RoText.Count(uses.Count, "eveniment", "evenimente")} 4624/4648 ulterioare" +
                              (types.Count > 0 ? $" ({string.Join(", ", types)})" : "") + "." +
                              (remoteUses.Count > 0 ? $" Folosit de la distanță din {Join(remoteUses.Select(u => F(u, "IpAddress")))}." : " Nicio autentificare de la distanță în dovezile colectate.") +
                              (privileged ? $" Apartenență privilegiată în caz: {Join(privAdds.Where(e => { var m = Member(e); return SameAccount(name, sid, m.Name, m.Sid); }).Select(e => F(e, "TargetUserName")))}." : "") +
                              (sidMatched ? "" : " Legătura s-a făcut doar după nume (SID indisponibil), deci nu exclude refolosirea numelui.") +
                              (provisioning && !privileged && remoteUses.Count == 0 ? $" Creat de {by} (aprovizionare: instalarea Windows sau un serviciu), neprivilegiat și folosit doar local, de aceea Low." : "") +
                              (atSetup && privileged ? $" Creat de {by} printre primele {SetupRecordLimit} înregistrări ale jurnalului Security, care nu a fost golit (instalarea Windows), folosit doar local, de aceea Low." : ""),
                ClassificationReason = sidMatched ? "Aceeași SID în 4720 și într-un 4624 ulterior; High doar pentru cont privilegiat folosit de la distanță." : "Același nume în 4720 și într-un 4624/4648 ulterior; SID indisponibil, deci limitat la Medium.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = [Ref(cr, "4720 cont creat"), .. uses.Take(8).Select(u => Ref(u, $"{u.EventId} folosire")), .. privAdds.Where(e => { var m = Member(e); return SameAccount(name, sid, m.Name, m.Sid); }).Take(2).Select(e => Ref(e, "grup privilegiat"))],
                AlternativeExplanations = ["Utilizator nou legitim care s-a autentificat; folosirea unui cont nou este normală.", "Contul poate fi un cont de serviciu creat pentru o aplicație."],
            });
        }
    }
}
