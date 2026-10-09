using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Dfir.Windows.Domain;

/// <summary>
/// Everything about one domain user: directory attributes, privileged memberships, and the user's authentication
/// trail read from domain controllers' Security logs (remote, read-only; needs Event Log Readers rights on the DCs).
/// </summary>
public static class UserInvestigation
{
    private static readonly Dictionary<int, string> Meaning = new()
    {
        [4624] = "autentificare reușită", [4625] = "autentificare eșuată", [4740] = "cont blocat", [4767] = "cont deblocat",
        [4768] = "tichet Kerberos TGT", [4769] = "tichet de serviciu Kerberos", [4771] = "pre-autentificare Kerberos eșuată",
        [4776] = "validare NTLM", [4723] = "și-a schimbat parola", [4724] = "parolă resetată de administrator", [4738] = "cont modificat",
        [4725] = "cont dezactivat", [4722] = "cont activat",
    };

    public static UserInvestigationResult Investigate(string sam, DomainSnapshot? snapshot, IEnumerable<string> domainControllers,
                                                      DateTimeOffset sinceUtc, int maxEvents = 5000, CancellationToken ct = default)
    {
        var r = new UserInvestigationResult { User = sam };
        r.Account = snapshot?.Users.FirstOrDefault(u => u.SamAccountName.Equals(sam, StringComparison.OrdinalIgnoreCase));
        if (snapshot is not null)
            foreach (var (g, m) in snapshot.PrivilegedGroups)
                if (m.Contains(sam, StringComparer.OrdinalIgnoreCase)) r.PrivilegedGroups.Add(g);
        if (r.Account is { } a) Observe(a, r);

        var escaped = System.Security.SecurityElement.Escape(sam);
        var ids = string.Join(" or ", Meaning.Keys.Select(i => $"EventID={i}"));
        var xpath = $"*[System[({ids}) and TimeCreated[@SystemTime>='{sinceUtc.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}']]] and " +
                    $"*[EventData[Data[@Name='TargetUserName']='{escaped}']]";
        foreach (var dc in domainControllers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                NetworkPolicy.EnsureAllowed("Citire jurnale de pe controlerele de domeniu");
                using var session = new EventLogSession(dc);
                var q = new EventLogQuery("Security", PathType.LogName, xpath) { Session = session, ReverseDirection = true };
                using var reader = new EventLogReader(q);
                int n = 0;
                for (var rec = reader.ReadEvent(); rec is not null && n < maxEvents; rec = reader.ReadEvent(), n++)
                    using (rec) Add(r, dc, rec);
                if (n >= maxEvents)
                    r.Gaps.Add(new EvidenceGap($"Security pe {dc}", EvidenceStatus.Partial, $"limitat la {maxEvents} evenimente", "Istoric incomplet", "Interval mai scurt", "Da"));
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or NetworkBlockedException)
            {
                r.Gaps.Add(new EvidenceGap($"Security pe {dc}", EvidenceStatus.NotAvailable, ex.Message,
                    "Autentificările înregistrate pe acest controler lipsesc", "Drepturi Event Log Readers pe DC sau export EVTX importat", "Da"));
            }
        }
        r.Timeline.Sort((x, y) => x.TimeUtc.CompareTo(y.TimeUtc));
        Summarize(r);
        return r;
    }

    private static void Add(UserInvestigationResult r, string dc, EventRecord rec)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var e in XDocument.Parse(rec.ToXml()).Descendants().Where(e => e.Name.LocalName == "Data" && e.Attribute("Name") is not null))
                d[e.Attribute("Name")!.Value] = e.Value;
        }
        catch (System.Xml.XmlException ex) { d["_XmlError"] = ex.Message; }
        string V(string k) => d.TryGetValue(k, out var v) ? v : "";
        var source = new[] { V("IpAddress"), V("WorkstationName"), V("Workstation") }.FirstOrDefault(s => s.Length > 0 && s != "-" && s != "::1") ?? "";
        if (source.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase)) source = source[7..];
        if (source.Length > 0) r.LogonSources[source] = r.LogonSources.GetValueOrDefault(source) + 1;
        var detail = rec.Id switch
        {
            4624 => $"tip {V("LogonType")} de la {source}",
            4625 or 4771 => $"de la {source}, status {V("Status")} {V("SubStatus")} {V("FailureCode")}",
            4769 => $"serviciu {V("ServiceName")}, criptare {V("TicketEncryptionType")}, de la {source}",
            4768 => $"de la {source}, criptare {V("TicketEncryptionType")}",
            4740 => $"blocat pe {V("TargetDomainName")} (sursa: {V("SubjectUserName")})",
            4724 or 4738 or 4725 or 4722 => $"de {V("SubjectDomainName")}\\{V("SubjectUserName")}",
            _ => source,
        };
        r.Timeline.Add(new ActionEntry(rec.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue, r.User, Meaning.GetValueOrDefault(rec.Id, $"eveniment {rec.Id}"),
            detail, $"{dc} Security {rec.Id} RecordID {rec.RecordId}"));
    }

    private static void Observe(DirectoryAccount a, UserInvestigationResult r)
    {
        if (!a.Enabled) r.Observations.Add("Contul este DEZACTIVAT.");
        if (a.Flags.HasFlag(Uac.DontRequirePreauth)) r.Observations.Add("Contul nu cere pre-autentificare Kerberos (AS-REP roasting).");
        if (a.Flags.HasFlag(Uac.PasswordNotRequired)) r.Observations.Add("Contul are PASSWD_NOTREQD.");
        if (a.Flags.HasFlag(Uac.DontExpirePassword)) r.Observations.Add("Parola nu expiră.");
        if (a.Spns.Count > 0) r.Observations.Add($"Are SPN ({string.Join(", ", a.Spns.Take(3))}): parola poate fi atacată offline (Kerberoasting).");
        if (r.PrivilegedGroups.Count > 0) r.Observations.Add($"Membru (direct sau indirect) în: {string.Join(", ", r.PrivilegedGroups)}.");
        if (a.AdminCount == 1 && r.PrivilegedGroups.Count == 0) r.Observations.Add("adminCount=1: a fost cândva cont privilegiat.");
    }

    private static void Summarize(UserInvestigationResult r)
    {
        var failed = r.Timeline.Count(t => t.Action is "autentificare eșuată" or "pre-autentificare Kerberos eșuată");
        if (failed > 0) r.Observations.Add($"{failed} autentificări eșuate în perioadă.");
        var lockouts = r.Timeline.Count(t => t.Action == "cont blocat");
        if (lockouts > 0) r.Observations.Add($"Contul a fost blocat de {lockouts} ori.");
        var rc4 = r.Timeline.Count(t => t.Action == "tichet de serviciu Kerberos" && t.Detail.Contains("0x17"));
        if (rc4 > 0) r.Observations.Add($"{rc4} tichete de serviciu cerute cu RC4 (0x17): verificați dacă nu este Kerberoasting.");
        if (r.LogonSources.Count > 0)
            r.Observations.Add("Surse de autentificare: " + string.Join(", ", r.LogonSources.OrderByDescending(s => s.Value).Take(15).Select(s => $"{s.Key} ({s.Value})")));
    }
}
