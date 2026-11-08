using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Identity;

/// <summary>One name an identity was seen with; a rename (4781) adds a name, it never replaces the identity.</summary>
public sealed record IdentityName(string Name, string Domain, string FirstSeenUtc, string HowKnown);

/// <summary>One thing that happened to or was done by an identity, pointing back to the event row.</summary>
public sealed record IdentityActivity(string Utc, string Host, string EventId, string Kind, string Text, string EvidenceId, string Locator, string ActorSid = "");

/// <summary>An identity keyed by its SID. An account seen only by name (4769, 4776 carry no SID) is kept apart, keyed by name, and marked so.</summary>
public sealed class IdentityRecord
{
    public string Key { get; set; } = "";
    public string Sid { get; set; } = "";
    public bool KnownByNameOnly { get; set; }
    public List<IdentityName> Names { get; set; } = [];
    /// <summary>Earlier SIDs folded into this identity through SID history (4765/4738); events under them count here.</summary>
    public List<string> SidHistory { get; set; } = [];
    public bool Privileged { get; set; }
    public string PrivilegedReason { get; set; } = "";
    public int Logons { get; set; }
    public int FailedLogons { get; set; }
    public List<string> Hosts { get; set; } = [];
    public string FirstSeenUtc { get; set; } = "";
    public string LastSeenUtc { get; set; } = "";
    public List<IdentityActivity> Activity { get; set; } = [];
    public bool ActivityTruncated { get; set; }
    public string DisplayName => Names.Count == 0 ? Key : (Names[^1].Domain.Length > 0 ? $"{Names[^1].Domain}\\{Names[^1].Name}" : Names[^1].Name);
    public string NameHistory => string.Join(" → ", Names.Select(n => n.Name).Distinct(StringComparer.OrdinalIgnoreCase));
}

/// <summary>A logon session on one host. A logon id means something only on the host that issued it, so the key is host + logon id.</summary>
public sealed record HostSession(string Host, string LogonId, string Sid, string Account, string LogonType, string SourceIp, string Workstation,
    string AuthPackage, string StartUtc, string EndUtc, bool Elevated, string Confidence, string Note, string EvidenceId, string Locator);

/// <summary>A directory object change seen on a domain controller (5136/5137/5141). A change to a groupPolicyContainer is a GPO change.</summary>
public sealed record DirectoryChange(string Utc, string Dc, string Operation, string ObjectDn, string ObjectClass, string Attribute, string Value,
    bool IsGpo, string ActorSid, string ActorName, string EvidenceId, string Locator);

/// <summary>What the logs cannot show. A gap is not a negative: missing DC logs mean the domain side is unknown, not that nothing happened.</summary>
public sealed record IdentityGap(string What, string Why);

public sealed class IdentityReport
{
    public const string RelPath = "Analysis/identity.json";
    public string Version { get; set; } = IdentityReconstruction.Version;
    public List<IdentityRecord> Identities { get; set; } = [];
    public List<HostSession> Sessions { get; set; } = [];
    public List<DirectoryChange> DirectoryChanges { get; set; } = [];
    public List<IdentityGap> Gaps { get; set; } = [];
    public List<string> DomainControllers { get; set; } = [];
    public int DuplicatesDropped { get; set; }
    public int EventsUsed { get; set; }

    public static void Write(string path, IdentityReport r) => LogAnalyzer.Dfir.IO.Json.Write(path, r);
    public static IdentityReport? Read(string path) => File.Exists(path) ? LogAnalyzer.Dfir.IO.Json.Read<IdentityReport>(path) : null;
}

/// <summary>
/// Master plan 6.10: identities rebuilt from Windows Security events across domain controllers, servers and workstations. The SID is the identity
/// key, so a rename keeps one identity with two names; a logon id is scoped to the host that issued it; a copy of the same DC log imported twice
/// counts once; and the absence of domain controller logs is reported as a gap. No single event proves an account's full path: every row here
/// points to the event it came from.
/// </summary>
public static class IdentityReconstruction
{
    public const string Version = "1.0";
    public const int MaxActivityPerIdentity = 2000;

    /// <summary>Well-known privileged groups by SID (builtin) or RID (domain): Administrators, Account/Server/Backup Operators; Domain, Schema, Enterprise and Key Admins.</summary>
    private static readonly HashSet<string> PrivilegedBuiltin = ["S-1-5-32-544", "S-1-5-32-548", "S-1-5-32-549", "S-1-5-32-551"];
    private static readonly HashSet<string> PrivilegedRids = ["512", "518", "519", "526", "527"];
    private static readonly HashSet<string> DcSideEvents = ["4768", "4769", "4771", "5136", "5137", "5141"];
    private static readonly HashSet<string> NotAccounts = ["", "-", "S-1-0-0"];

    private static readonly Dictionary<string, (string Kind, string Text)> Events = new()
    {
        ["4624"] = ("logon", "autentificare reușită"), ["4625"] = ("logon_failed", "autentificare eșuată"),
        ["4634"] = ("logoff", "delogare"), ["4647"] = ("logoff", "delogare inițiată de utilizator"),
        ["4648"] = ("explicit_credentials", "autentificare cu date de acces explicite"), ["4672"] = ("privileges", "privilegii speciale la autentificare"),
        ["4720"] = ("account", "cont creat"), ["4722"] = ("account", "cont activat"), ["4723"] = ("account", "schimbare de parolă"),
        ["4724"] = ("account", "resetare de parolă"), ["4725"] = ("account", "cont dezactivat"), ["4726"] = ("account", "cont șters"),
        ["4738"] = ("account", "cont modificat"), ["4740"] = ("account", "cont blocat"), ["4767"] = ("account", "cont deblocat"),
        ["4781"] = ("account", "cont redenumit"), ["4765"] = ("account", "istoric SID adăugat"), ["4766"] = ("account", "adăugarea istoricului SID a eșuat"),
        ["4728"] = ("group", "adăugat într-un grup global"), ["4732"] = ("group", "adăugat într-un grup local"), ["4756"] = ("group", "adăugat într-un grup universal"),
        ["4729"] = ("group", "scos dintr-un grup global"), ["4733"] = ("group", "scos dintr-un grup local"), ["4757"] = ("group", "scos dintr-un grup universal"),
        ["4768"] = ("kerberos", "tichet Kerberos TGT cerut"), ["4769"] = ("kerberos", "tichet Kerberos de serviciu cerut"),
        ["4771"] = ("kerberos_failed", "preautentificare Kerberos eșuată"), ["4776"] = ("ntlm", "validare NTLM a datelor de acces"),
        ["5136"] = ("directory", "obiect de director modificat"), ["5137"] = ("directory", "obiect de director creat"), ["5141"] = ("directory", "obiect de director șters"),
    };

    private static string F(TimelineEvent e, string k) => e.Fields.GetValueOrDefault(k, "").Trim();
    private static string NormSid(string s) => s.Trim().ToUpperInvariant();
    private static bool IsSecurity(TimelineEvent e) =>
        e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && Events.ContainsKey(e.EventId)
        && (e.Source.EndsWith("Security", StringComparison.OrdinalIgnoreCase) || e.Provider.Contains("Security-Auditing", StringComparison.OrdinalIgnoreCase));
    private static string HostKey(string h) => h.Split('.')[0].ToUpperInvariant();

    /// <summary>The same record of the same host imported twice (two exports of one DC log) is one event.</summary>
    private static string DedupKey(TimelineEvent e)
    {
        if (e.Locator.StartsWith("EventRecordID=", StringComparison.Ordinal)) return $"{HostKey(e.Host)}|{e.EventId}|{e.Locator}";
        var fields = string.Join("\u001f", e.Fields.Where(kv => !kv.Key.StartsWith('_')).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));
        return $"{HostKey(e.Host)}|{e.EventId}|{e.Time.UtcIso}|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fields)))[..24];
    }

    private static (string Name, string Domain) SplitName(string name, string domain)
    {
        name = name.Trim();
        int at = name.IndexOf('@');
        if (at > 0) return (name[..at], domain.Length > 0 ? domain : name[(at + 1)..]);
        int bs = name.IndexOf('\\');
        if (bs > 0) return (name[(bs + 1)..], name[..bs]);
        return (name, domain.Trim());
    }

    public static IdentityReport Build(IReadOnlyList<TimelineEvent> timeline)
    {
        var report = new IdentityReport();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var events = new List<TimelineEvent>();
        foreach (var e in timeline.Where(IsSecurity))
        {
            if (!seen.Add(DedupKey(e))) { report.DuplicatesDropped++; continue; }
            events.Add(e);
        }
        events = events.OrderBy(e => e.Time.Utc ?? DateTimeOffset.MaxValue).ThenBy(e => e.Host, StringComparer.OrdinalIgnoreCase).ToList();
        report.EventsUsed = events.Count;

        // SID history first: an event under an old SID belongs to the identity that now carries it.
        var alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events.Where(e => e.EventId == "4765"))
            if (F(e, "SourceSid") is { Length: > 0 } old && F(e, "TargetSid") is { Length: > 0 } now) alias[NormSid(old)] = NormSid(now);
        foreach (var e in events.Where(e => e.EventId == "4738"))
            foreach (var old in F(e, "SidHistory").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Where(s => s.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)))
                if (F(e, "TargetSid") is { Length: > 0 } now) alias[NormSid(old)] = NormSid(now);

        var ids = new Dictionary<string, IdentityRecord>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // account name (no domain) -> SID key
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);              // a name two SIDs carried: never resolved by name

        IdentityRecord Id(string sid)
        {
            var key = NormSid(sid);
            if (alias.TryGetValue(key, out var primary)) key = primary;
            if (!ids.TryGetValue(key, out var r))
            {
                r = new IdentityRecord { Key = key, Sid = key };
                ids[key] = r;
                foreach (var a in alias.Where(a => a.Value == key)) r.SidHistory.Add(a.Key);
            }
            return r;
        }
        IdentityRecord? ByNameOnly(string rawName, string domain)
        {
            var (n, d) = SplitName(rawName, domain);
            if (n.Length == 0 || n == "-") return null;
            if (!ambiguous.Contains(n) && byName.TryGetValue(n, out var sidKey)) return ids[sidKey];
            var key = "name:" + (d.Length > 0 ? d.ToUpperInvariant() + "\\" : "") + n.ToUpperInvariant();
            if (!ids.TryGetValue(key, out var r)) { r = new IdentityRecord { Key = key, KnownByNameOnly = true }; ids[key] = r; AddName(r, n, d, "", "numele din eveniment (fără SID)"); }
            return r;
        }
        void AddName(IdentityRecord r, string name, string domain, string utc, string how)
        {
            if (name.Length == 0 || name == "-") return;
            if (r.Names.Count > 0 && string.Equals(r.Names[^1].Name, name, StringComparison.OrdinalIgnoreCase)) return;
            if (r.Names.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) && how.StartsWith("observat", StringComparison.Ordinal)) return;
            r.Names.Add(new IdentityName(name, domain, utc, how));
            if (r.KnownByNameOnly) return;
            if (byName.TryGetValue(name, out var other) && !string.Equals(other, r.Key, StringComparison.OrdinalIgnoreCase)) ambiguous.Add(name);
            else byName[name] = r.Key;
        }
        void Touch(IdentityRecord r, TimelineEvent e, string text, string actor = "")
        {
            var utc = e.Time.UtcIso;
            if (utc.Length > 0) { if (r.FirstSeenUtc.Length == 0) r.FirstSeenUtc = utc; r.LastSeenUtc = utc; }
            var host = HostKey(e.Host);
            if (host.Length > 0 && !r.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase)) r.Hosts.Add(host);
            if (r.Activity.Count >= MaxActivityPerIdentity) { r.ActivityTruncated = true; return; }
            r.Activity.Add(new IdentityActivity(utc, host, e.EventId, Events[e.EventId].Kind, text, e.EvidenceId, e.Locator, actor));
        }
        void Privilege(IdentityRecord r, string why) { if (!r.Privileged) { r.Privileged = true; r.PrivilegedReason = why; } }

        var open = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // host|logonId -> index into sessions
        var sessions = new List<HostSession>();

        foreach (var e in events)
        {
            var (kind, text) = Events[e.EventId];
            var host = HostKey(e.Host);
            switch (e.EventId)
            {
                case "4624" or "4625" or "4648":
                {
                    var sid = F(e, "TargetUserSid");
                    var r = NotAccounts.Contains(sid) ? ByNameOnly(F(e, "TargetUserName"), F(e, "TargetDomainName")) : Id(sid);
                    if (r is null) break;
                    if (!r.KnownByNameOnly) AddName(r, F(e, "TargetUserName"), F(e, "TargetDomainName"), e.Time.UtcIso, "observat în autentificare");
                    var ip = F(e, "IpAddress") is var a && a is not ("-" or "::1" or "127.0.0.1") ? a : "";
                    Touch(r, e, $"{text} pe {host}" + (F(e, "LogonType") is { Length: > 0 } lt ? $", tip {lt}" : "") + (ip.Length > 0 ? $", din {ip}" : "")
                        + (e.EventId == "4648" && F(e, "TargetServerName") is { Length: > 0 } ts ? $", către {ts}" : ""), NormSid(F(e, "SubjectUserSid")));
                    if (e.EventId == "4625") { r.FailedLogons++; break; }
                    if (e.EventId == "4648") break;
                    r.Logons++;
                    var lid = F(e, "TargetLogonId");
                    if (lid.Length == 0) break;
                    var key = host + "|" + lid.ToLowerInvariant();
                    if (open.TryGetValue(key, out var prev) && sessions[prev].EndUtc.Length == 0)
                        sessions[prev] = sessions[prev] with { Confidence = "scăzută", Note = "același LogonId a apărut din nou pe stație înainte de delogare (repornire sau jurnal incomplet)" };
                    sessions.Add(new HostSession(host, lid, r.Key, r.DisplayName, F(e, "LogonType"), ip, F(e, "WorkstationName"), F(e, "AuthenticationPackageName"),
                        e.Time.UtcIso, "", false, "medie", "fără delogare în jurnal", e.EvidenceId, e.Locator));
                    open[key] = sessions.Count - 1;
                    break;
                }
                case "4634" or "4647":
                {
                    var sid = F(e, "TargetUserSid");
                    if (!NotAccounts.Contains(sid)) Touch(Id(sid), e, $"{text} pe {host}");
                    var key = host + "|" + F(e, "TargetLogonId").ToLowerInvariant();
                    if (open.TryGetValue(key, out var i) && sessions[i].EndUtc.Length == 0)
                        sessions[i] = sessions[i] with { EndUtc = e.Time.UtcIso, Confidence = sessions[i].Note.StartsWith("același", StringComparison.Ordinal) ? "scăzută" : "ridicată", Note = sessions[i].Note.StartsWith("același", StringComparison.Ordinal) ? sessions[i].Note : "" };
                    else if (F(e, "TargetLogonId").Length > 0 && !open.ContainsKey(key))
                    {
                        sessions.Add(new HostSession(host, F(e, "TargetLogonId"), NotAccounts.Contains(sid) ? "" : Id(sid).Key, NotAccounts.Contains(sid) ? F(e, "TargetUserName") : Id(sid).DisplayName,
                            F(e, "LogonType"), "", "", "", "", e.Time.UtcIso, false, "scăzută", "începutul sesiunii nu este în jurnal", e.EvidenceId, e.Locator));
                        open[key] = sessions.Count - 1;
                    }
                    break;
                }
                case "4672":
                {
                    var sid = F(e, "SubjectUserSid");
                    if (NotAccounts.Contains(sid)) break;
                    var r = Id(sid);
                    AddName(r, F(e, "SubjectUserName"), F(e, "SubjectDomainName"), e.Time.UtcIso, "observat la privilegii");
                    Touch(r, e, $"{text} pe {host}");
                    if (!sid.Equals("S-1-5-18", StringComparison.OrdinalIgnoreCase)) Privilege(r, $"privilegii speciale la autentificare (4672) pe {host}");
                    var key = host + "|" + F(e, "SubjectLogonId").ToLowerInvariant();
                    if (open.TryGetValue(key, out var i)) sessions[i] = sessions[i] with { Elevated = true };
                    break;
                }
                case "4728" or "4732" or "4756" or "4729" or "4733" or "4757":
                {
                    var member = F(e, "MemberSid");
                    if (NotAccounts.Contains(member)) break;
                    var r = Id(member);
                    var group = F(e, "TargetUserName") is { Length: > 0 } g ? (F(e, "TargetDomainName") is { Length: > 0 } gd ? $"{gd}\\{g}" : g) : F(e, "TargetSid");
                    Touch(r, e, $"{text}: {group} (SID grup {F(e, "TargetSid")}), făcut de {F(e, "SubjectUserName")}", NormSid(F(e, "SubjectUserSid")));
                    bool added = e.EventId is "4728" or "4732" or "4756";
                    var gsid = NormSid(F(e, "TargetSid"));
                    bool privGroup = PrivilegedBuiltin.Contains(gsid) || PrivilegedRids.Contains(gsid.Split('-')[^1]) && gsid.StartsWith("S-1-5-21-", StringComparison.Ordinal);
                    if (added && privGroup) Privilege(r, $"adăugat în grupul privilegiat {group}");
                    ActorTouch(e, $"a modificat membrii grupului {group} ({(added ? "adăugare" : "scoatere")} {r.DisplayName})");
                    break;
                }
                case "4720" or "4722" or "4723" or "4724" or "4725" or "4726" or "4738" or "4740" or "4767" or "4781" or "4765" or "4766":
                {
                    var target = F(e, "TargetSid");
                    if (NotAccounts.Contains(target)) break;
                    var r = Id(target);
                    if (e.EventId == "4781")
                    {
                        if (r.Names.Count == 0) AddName(r, F(e, "OldTargetUserName"), F(e, "TargetDomainName"), "", "numele dinainte de redenumire");
                        AddName(r, F(e, "NewTargetUserName"), F(e, "TargetDomainName"), e.Time.UtcIso, "redenumire (4781)");
                        Touch(r, e, $"{text}: {F(e, "OldTargetUserName")} → {F(e, "NewTargetUserName")}", NormSid(F(e, "SubjectUserSid")));
                    }
                    else
                    {
                        AddName(r, F(e, "TargetUserName"), F(e, "TargetDomainName"), e.Time.UtcIso, "observat la gestionarea contului");
                        Touch(r, e, e.EventId == "4765" ? $"{text}: {F(e, "SourceSid")}" : text, NormSid(F(e, "SubjectUserSid")));
                    }
                    ActorTouch(e, $"{text} pentru {r.DisplayName}");
                    break;
                }
                case "4768" or "4771":
                {
                    var sid = F(e, "TargetSid");
                    var r = NotAccounts.Contains(sid) ? ByNameOnly(F(e, "TargetUserName"), F(e, "TargetDomainName")) : Id(sid);
                    if (r is null) break;
                    if (!r.KnownByNameOnly) AddName(r, F(e, "TargetUserName"), F(e, "TargetDomainName"), e.Time.UtcIso, "observat în Kerberos");
                    Touch(r, e, $"{text} pe controlerul {host}" + (F(e, "IpAddress") is { Length: > 0 } ip ? $", client {ip}" : ""));
                    if (e.EventId == "4771") r.FailedLogons++;
                    break;
                }
                case "4769" or "4776":
                {
                    var r = ByNameOnly(e.EventId == "4769" ? F(e, "TargetUserName") : F(e, "TargetUserName"), F(e, "TargetDomainName"));
                    if (r is null) break;
                    var status = F(e, e.EventId == "4776" ? "Status" : "Status");
                    Touch(r, e, e.EventId == "4769" ? $"{text}: {F(e, "ServiceName")} pe {host}" : $"{text} pe {host}, stația {F(e, "Workstation")}" + (status is { Length: > 0 } and not "0x0" ? $", stare {status}" : ""));
                    if (e.EventId == "4776" && status is { Length: > 0 } and not "0x0") r.FailedLogons++;
                    break;
                }
                case "5136" or "5137" or "5141":
                {
                    var cls = F(e, "ObjectClass");
                    bool gpo = cls.Equals("groupPolicyContainer", StringComparison.OrdinalIgnoreCase);
                    var op = e.EventId switch { "5137" => "creare", "5141" => "ștergere", _ => F(e, "OperationType") switch { "%%14674" => "valoare adăugată", "%%14675" => "valoare ștearsă", var o => o.Length > 0 ? o : "modificare" } };
                    var actorSid = NormSid(F(e, "SubjectUserSid"));
                    report.DirectoryChanges.Add(new DirectoryChange(e.Time.UtcIso, host, op, F(e, "ObjectDN"), cls, F(e, "AttributeLDAPDisplayName"), F(e, "AttributeValue"), gpo,
                        actorSid, F(e, "SubjectUserName"), e.EvidenceId, e.Locator));
                    ActorTouch(e, (gpo ? "a modificat o politică de grup (GPO): " : "a modificat un obiect de director: ") + F(e, "ObjectDN")
                        + (F(e, "AttributeLDAPDisplayName") is { Length: > 0 } at ? $", atributul {at}" : ""));
                    break;
                }
            }

            void ActorTouch(TimelineEvent ev, string what)
            {
                var actor = F(ev, "SubjectUserSid");
                if (NotAccounts.Contains(actor) || actor.Equals("S-1-5-18", StringComparison.OrdinalIgnoreCase)) return;
                var a = Id(actor);
                AddName(a, F(ev, "SubjectUserName"), F(ev, "SubjectDomainName"), ev.Time.UtcIso, "observat ca autor al unei modificări");
                Touch(a, ev, what);
            }
        }

        // A name seen without a SID before the same name was seen with one: fold it in now. A name never seen with a SID stays apart.
        foreach (var nameOnly in ids.Values.Where(r => r.KnownByNameOnly).ToList())
        {
            if (ambiguous.Contains(nameOnly.Names[0].Name) || !byName.TryGetValue(nameOnly.Names[0].Name, out var sidKey)) continue;
            var target = ids[sidKey];
            target.Activity = target.Activity.Concat(nameOnly.Activity).OrderBy(a => a.Utc, StringComparer.Ordinal).Take(MaxActivityPerIdentity).ToList();
            target.ActivityTruncated |= nameOnly.ActivityTruncated || target.Activity.Count == MaxActivityPerIdentity;
            target.FailedLogons += nameOnly.FailedLogons;
            foreach (var h in nameOnly.Hosts.Where(h => !target.Hosts.Contains(h, StringComparer.OrdinalIgnoreCase))) target.Hosts.Add(h);
            if (string.CompareOrdinal(nameOnly.FirstSeenUtc, target.FirstSeenUtc) < 0 && nameOnly.FirstSeenUtc.Length > 0) target.FirstSeenUtc = nameOnly.FirstSeenUtc;
            if (string.CompareOrdinal(nameOnly.LastSeenUtc, target.LastSeenUtc) > 0) target.LastSeenUtc = nameOnly.LastSeenUtc;
            ids.Remove(nameOnly.Key);
        }

        report.Identities = ids.Values.OrderByDescending(r => r.Privileged).ThenBy(r => r.KnownByNameOnly).ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        report.Sessions = sessions;
        report.DomainControllers = events.Where(e => DcSideEvents.Contains(e.EventId)).Select(e => HostKey(e.Host)).Where(h => h.Length > 0).Distinct().Order().ToList();
        report.Gaps = Gaps(events, report);
        return report;
    }

    private static readonly HashSet<string> LocalDomains = new(StringComparer.OrdinalIgnoreCase) { "", "-", "NT AUTHORITY", "Window Manager", "Font Driver Host", "NT SERVICE", "BUILTIN" };

    private static List<IdentityGap> Gaps(List<TimelineEvent> events, IdentityReport report)
    {
        var gaps = new List<IdentityGap>();
        if (events.Count == 0)
        {
            gaps.Add(new IdentityGap("Nu există evenimente de securitate Windows în caz.", "Fără jurnalul Security (stații, servere, controlere de domeniu) identitățile nu se pot reconstitui; absența lor nu înseamnă că nu a existat activitate."));
            return gaps;
        }
        bool domainLogons = events.Any(e => e.EventId == "4624" && !LocalDomains.Contains(F(e, "TargetDomainName"))
            && !string.Equals(F(e, "TargetDomainName"), HostKey(e.Host), StringComparison.OrdinalIgnoreCase));
        if (domainLogons && report.DomainControllers.Count == 0)
            gaps.Add(new IdentityGap("Apar conturi de domeniu, dar lipsesc jurnalele unui controler de domeniu.",
                "Tichetele Kerberos, validările NTLM ale domeniului, modificările de grupuri și de obiecte de director se văd doar pe controler. Absența lor aici este o lipsă de date, nu o dovadă că nu au avut loc."));
        if (report.DomainControllers.Count > 0 && report.DirectoryChanges.Count == 0)
            gaps.Add(new IdentityGap("Pe controlerele de domeniu nu apare nicio modificare de obiect de director (5136/5137/5141).",
                "Auditul „Directory Service Changes” poate fi neactivat sau SACL-urile lipsesc; modificările de GPO și de obiecte nu se pot reconstitui."));
        foreach (var h in events.Where(e => e.EventId == "4624").Select(e => HostKey(e.Host)).Distinct())
            if (!events.Any(e => e.EventId is "4634" or "4647" && HostKey(e.Host) == h))
                gaps.Add(new IdentityGap($"Pe {h} apar autentificări, dar nicio delogare.", "Durata sesiunilor de pe această stație nu se poate stabili; auditul „Logoff” poate fi neactivat sau jurnalul a fost suprascris."));
        if (report.Identities.Any(i => i.KnownByNameOnly))
            gaps.Add(new IdentityGap("Unele conturi apar doar după nume (4769, 4776 nu au SID).",
                "Ele au fost legate de un SID numai când același nume a apărut cu SID în alt eveniment; altfel rămân separate, pentru că un nume se poate schimba sau repeta între domenii."));
        return gaps;
    }
}
