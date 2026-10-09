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
            var who = name.Length > 0 ? name : sid;
            var group = F(first, "TargetUserName");
            var by = Account(F(first, "SubjectUserName"), F(first, "SubjectDomainName"));
            var inCase = created.FirstOrDefault(cr => SameAccount(F(cr, "TargetUserName"), F(cr, "TargetSid"), name, sid));
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "ACCOUNT-ADDED-PRIVILEGED-GROUP", Title = $"Cont adăugat într-un grup privilegiat: {who} → {group}",
                Severity = Severity.Medium, Category = "Privilege Escalation", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1098",
                FirstSeenUtc = T(g.First()), LastSeenUtc = T(g.Last()), User = who,
                Description = $"{who} ({sid}) a fost adăugat în grupul {group} ({F(first, "TargetSid")}) de {by}, eveniment {first.EventId}, {Time(T(first))}." +
                              (inCase is not null ? $" Contul a fost creat în același caz ({Time(T(inCase))})." : ""),
                ClassificationReason = "Eveniment Security 4728/4732/4756 pentru un grup din lista de grupuri privilegiate (după nume sau SID). Arată apartenența acordată, nu folosirea ei.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = g.Take(10).Select(e => Ref(e, $"{e.EventId} {group}")).ToList(),
                AlternativeExplanations = ["Administrare planificată (cont de serviciu, administrator nou, grup de acces la distanță).", "Subiectul evenimentului poate fi un administrator autorizat."],
            });
        }

        int allLogonEvents = logons.Count;
        foreach (var cr in created.GroupBy(e => F(e, "TargetSid").Length > 0 ? F(e, "TargetSid").ToUpperInvariant() : F(e, "TargetUserName").ToLowerInvariant()).Select(g => g.First()))
        {
            var name = F(cr, "TargetUserName"); var sid = F(cr, "TargetSid");
            var createdAt = T(cr);
            var by = Account(F(cr, "SubjectUserName"), F(cr, "SubjectDomainName"));
            bool provisioning = IsMachineAccount(F(cr, "SubjectUserName")) || F(cr, "SubjectUserName").Equals("SYSTEM", StringComparison.OrdinalIgnoreCase);

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
            var types = uses.Where(u => u.EventId == "4624").GroupBy(u => F(u, "LogonType")).Select(g => $"tip {g.Key}: {g.Count()}").ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "ACCOUNT-CREATED-THEN-USED", Title = $"Cont creat și folosit ulterior: {name}" + (privileged ? " (și adăugat într-un grup privilegiat)" : ""),
                Severity = sev, Category = "Persistence", Classification = sidMatched ? Classification.Correlated : Classification.Candidate,
                Confidence = sidMatched ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1078",
                FirstSeenUtc = createdAt, LastSeenUtc = T(uses[^1]), User = name, Ip = remoteUses.Count > 0 ? F(remoteUses[0], "IpAddress") : "",
                Description = $"{name} creat {Time(createdAt)}; prima folosire {Time(T(firstUse))} ({firstUse.EventId}); {uses.Count} evenimente 4624/4648 ulterioare" +
                              (types.Count > 0 ? $" ({string.Join(", ", types)})" : "") + "." +
                              (remoteUses.Count > 0 ? $" Folosit de la distanță din {Join(remoteUses.Select(u => F(u, "IpAddress")))}." : " Nicio autentificare de la distanță în dovezile colectate.") +
                              (privileged ? $" Apartenență privilegiată în caz: {Join(privAdds.Where(e => { var m = Member(e); return SameAccount(name, sid, m.Name, m.Sid); }).Select(e => F(e, "TargetUserName")))}." : "") +
                              (sidMatched ? "" : " Legătura s-a făcut doar după nume (SID indisponibil), deci nu exclude refolosirea numelui."),
                ClassificationReason = sidMatched ? "Aceeași SID în 4720 și într-un 4624 ulterior; High doar pentru cont privilegiat folosit de la distanță." : "Același nume în 4720 și într-un 4624/4648 ulterior; SID indisponibil, deci limitat la Medium.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = [Ref(cr, "4720 cont creat"), .. uses.Take(8).Select(u => Ref(u, $"{u.EventId} folosire")), .. privAdds.Where(e => { var m = Member(e); return SameAccount(name, sid, m.Name, m.Sid); }).Take(2).Select(e => Ref(e, "grup privilegiat"))],
                AlternativeExplanations = ["Utilizator nou legitim care s-a autentificat; folosirea unui cont nou este normală.", "Contul poate fi un cont de serviciu creat pentru o aplicație."],
            });
        }
    }
}
