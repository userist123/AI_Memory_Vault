using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Identity;

/// <summary>One node of an account's path: a host, a logon session on it, a process started in that session, or a resource the process or session touched.</summary>
public sealed class IdentityPathNode
{
    /// <summary>host, session, process, resource.</summary>
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string Utc { get; set; } = "";
    public string EvidenceId { get; set; } = "";
    public string Locator { get; set; } = "";
    /// <summary>Why this node hangs under its parent (the exact keys that matched). Empty for a host (the sessions say where they were).</summary>
    public string Reason { get; set; } = "";
    /// <summary>How many rows were folded into this node (the same resource touched again, for instance).</summary>
    public int Count { get; set; } = 1;
    public List<IdentityPathNode> Children { get; set; } = [];
    public bool ChildrenTruncated { get; set; }
    /// <summary>For display: how many times, the event row, and a note when children were left out.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Suffix => (Count > 1 ? $" · de {Count} ori" : "") + (Locator.Length > 0 ? $" · {EvidenceId} {Locator}" : "") + (ChildrenTruncated ? " · lista este limitată" : "");
}

public sealed class IdentityGraphReport
{
    public const string RelPath = "Analysis/identity_graph.json";
    public string Version { get; set; } = IdentityGraph.Version;
    /// <summary>Identity key (as in identity.json) → its hosts, each with sessions, processes and resources.</summary>
    public Dictionary<string, List<IdentityPathNode>> Paths { get; set; } = [];
    public int ProcessesLinked { get; set; }
    public int ResourcesLinked { get; set; }
    public List<IdentityGap> Gaps { get; set; } = [];

    public static void Write(string path, IdentityGraphReport r) => LogAnalyzer.Dfir.IO.Json.Write(path, r);
    public static IdentityGraphReport? Read(string path) => File.Exists(path) ? LogAnalyzer.Dfir.IO.Json.Read<IdentityGraphReport>(path) : null;
}

/// <summary>
/// Master plan 6.10, the account's path: account → authentication (session) → host → process → resource. Every link is made by exact keys only:
/// a process or a resource joins a session by the host that issued the logon id plus the logon id itself, inside the session's time; a resource joins
/// a process by Sysmon's ProcessGuid, or by host + session + process id for a Security 4663. Nothing is linked by a similar name or a nearby time.
/// What cannot be linked is counted as a gap, not attached to a guess.
/// </summary>
public static class IdentityGraph
{
    public const string Version = "1.0";
    public const int MaxProcessesPerSession = 300;
    public const int MaxResourcesPerParent = 100;

    /// <summary>Logon ids of the machine's own accounts (SYSTEM, LOCAL SERVICE, NETWORK SERVICE): no 4624 opens them, so they never join a person's session.</summary>
    private static readonly HashSet<string> MachineLogons = ["0x3e7", "0x3e4", "0x3e5"];

    private static string F(TimelineEvent e, string k) => e.Fields.GetValueOrDefault(k, "").Trim();
    private static string HostKey(string h) => h.Trim().ToUpperInvariant();
    private static bool IsSecurity(TimelineEvent e) => e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase)
        && (e.Source.EndsWith("Security", StringComparison.OrdinalIgnoreCase) || e.Provider.Contains("Security-Auditing", StringComparison.OrdinalIgnoreCase));
    private static bool IsSysmon(TimelineEvent e) => e.Source.Contains("Sysmon", StringComparison.OrdinalIgnoreCase);

    /// <summary>A logon id in one spelling: "0x0003E7", "0x3e7" and "999" are the same id. Empty when it is not a number.</summary>
    public static string NormLogon(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return "";
        ulong v;
        bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
            : ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        return ok && v != 0 ? "0x" + v.ToString("x", CultureInfo.InvariantCulture) : "";
    }

    private static string NormPid(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h.ToString(CultureInfo.InvariantCulture) : "";
        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d.ToString(CultureInfo.InvariantCulture) : "";
    }

    private static DateTimeOffset? ParseUtc(string s) =>
        s.Length > 0 && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    private sealed class SessionSlot
    {
        public required HostSession S;
        public DateTimeOffset? Start;
        /// <summary>The start used to bound the window when the log has no logon: the station's latest boot before the session ended, if the case has one.</summary>
        public DateTimeOffset? BoundStart;
        public DateTimeOffset? End;
        public required IdentityPathNode Node;
        public int Processes;
        public readonly Dictionary<string, IdentityPathNode> Resources = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProcessSlot
    {
        public required IdentityPathNode Node;
        public required SessionSlot Session;
        public DateTimeOffset Created;
        public DateTimeOffset? Exited;
        public readonly Dictionary<string, IdentityPathNode> Resources = new(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsProcessEvent(TimelineEvent e) => (e.EventId == "4688" && IsSecurity(e)) || (e.EventId == "1" && IsSysmon(e));
    private static bool IsResourceEvent(TimelineEvent e) =>
        (IsSysmon(e) && e.EventId is "3" or "11" or "22") || (IsSecurity(e) && e.EventId is "5140" or "5145" or "4663");
    /// <summary>A station start: Security 4608 or Kernel-General 12 in the System log.</summary>
    private static bool IsBoot(TimelineEvent e) =>
        (e.EventId == "4608" && IsSecurity(e)) || (e.EventId == "12" && e.Source.EndsWith(":System", StringComparison.OrdinalIgnoreCase) && e.Provider.Contains("Kernel-General", StringComparison.OrdinalIgnoreCase));
    private static string FirstLabel(string host) => HostKey(host).Split('.')[0];

    public static IdentityGraphReport Build(IdentityReport identity, IReadOnlyList<TimelineEvent> timeline)
    {
        var report = new IdentityGraphReport();
        var seen = new HashSet<string>(StringComparer.Ordinal);   // a log imported twice gives each process and access once (same key as identity.json)
        var unique = timeline.Where(e => seen.Add(IdentityReconstruction.DedupKey(e))).ToList();
        var ordered = unique.Where(e => e.Time.Utc is not null).OrderBy(e => e.Time.Utc).ToList();
        int noTime = unique.Count(e => e.Time.Utc is null && (IsProcessEvent(e) || IsResourceEvent(e)));
        var boots = ordered.Where(IsBoot).GroupBy(e => HostKey(e.Host)).ToDictionary(g => g.Key, g => g.Select(e => e.Time.Utc!.Value).ToList(), StringComparer.Ordinal);

        var hostsOf = new Dictionary<string, Dictionary<string, IdentityPathNode>>(StringComparer.Ordinal);   // identity → host → node
        var byKey = new Dictionary<string, List<SessionSlot>>(StringComparer.Ordinal);                         // HOST|0xid → sessions (a reused id is several)
        var hostsOfLogon = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);                    // 0xid → first labels of the hosts that issued it
        foreach (var s in identity.Sessions)
        {
            if (s.Sid.Length == 0) continue;
            var lid = NormLogon(s.LogonId);
            if (lid.Length == 0) continue;
            var hosts = hostsOf.TryGetValue(s.Sid, out var h) ? h : hostsOf[s.Sid] = new(StringComparer.Ordinal);
            if (!hosts.TryGetValue(s.Host, out var hostNode))
                hosts[s.Host] = hostNode = new IdentityPathNode { Kind = "host", Text = $"Stația {s.Host}" };
            var node = new IdentityPathNode
            {
                Kind = "session", Utc = s.StartUtc, EvidenceId = s.EvidenceId, Locator = s.Locator,
                Text = $"Sesiune {lid}, tip {(s.LogonType.Length > 0 ? s.LogonType : "necunoscut")}" + (s.SourceIp.Length > 0 ? $", din {s.SourceIp}" : "")
                       + $": {(s.StartUtc.Length > 0 ? s.StartUtc : "început necunoscut")} → {(s.EndUtc.Length > 0 ? s.EndUtc : "sfârșit necunoscut")}" + (s.Elevated ? " · cu privilegii speciale" : ""),
                Reason = $"autentificarea {s.Locator} pe {s.Host}",
            };
            hostNode.Children.Add(node);
            var key = s.Host + "|" + lid;
            (byKey.TryGetValue(key, out var list) ? list : byKey[key] = []).Add(new SessionSlot { S = s, Start = ParseUtc(s.StartUtc), End = ParseUtc(s.EndUtc), Node = node });
            (hostsOfLogon.TryGetValue(lid, out var hl) ? hl : hostsOfLogon[lid] = new(StringComparer.Ordinal)).Add(FirstLabel(s.Host));
        }
        foreach (var (key, l) in byKey)
        {
            l.Sort((a, b) => Nullable.Compare(a.Start ?? a.End, b.Start ?? b.End));
            // a session whose logon is not in the log reaches back only to the station's latest boot before it ended (a logon id does not survive a restart)
            var host = key[..key.IndexOf('|')];
            foreach (var sl in l.Where(x => x.Start is null))
                if (boots.TryGetValue(host, out var bs) && bs.Where(b => sl.End is not { } en || b <= en).Select(b => (DateTimeOffset?)b).Max() is { } boot)
                    sl.BoundStart = boot;
        }

        int noSession = 0, hostSpelling = 0, outsideWindow = 0, noProcess = 0, machine = 0, capped = 0, noLogon = 0, noGuid = 0;

        SessionSlot? Session(TimelineEvent e, string rawLogon, out string why)
        {
            why = "";
            var lid = NormLogon(rawLogon);
            if (lid.Length == 0) { noLogon++; return null; }
            if (MachineLogons.Contains(lid)) { machine++; return null; }
            var key = HostKey(e.Host) + "|" + lid;
            if (!byKey.TryGetValue(key, out var slots))
            {
                // the same id issued by a station spelt otherwise (short name against full name): said as such, never joined
                if (hostsOfLogon.TryGetValue(lid, out var hl) && hl.Contains(FirstLabel(e.Host))) hostSpelling++; else noSession++;
                return null;
            }
            var t = e.Time.Utc!.Value;
            // the session whose window holds the event; a session with no end lasts until the next one with the same id begins
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                var sl = slots[i];
                var start = sl.Start ?? sl.BoundStart;
                if (start is { } st && t < st) continue;
                var end = sl.End ?? (i + 1 < slots.Count ? slots[i + 1].Start ?? slots[i + 1].BoundStart : null);
                if (end is { } en && t > en) break;
                why = $"aceeași stație {HostKey(e.Host)} și același LogonId {lid}" + (sl.Start is not null ? ", în intervalul sesiunii"
                    : sl.BoundStart is { } b ? $" (începutul sesiunii nu este în jurnal; după pornirea stației din {b:yyyy-MM-ddTHH:mm:ssZ})"
                    : " (începutul sesiunii și pornirea stației nu sunt în jurnal: legătura nu are limită în trecut)");
                return sl;
            }
            outsideWindow++;
            return null;
        }

        static IdentityPathNode Resource(Dictionary<string, IdentityPathNode> into, IdentityPathNode parent, string key, Func<IdentityPathNode> make, ref int capped)
        {
            if (into.TryGetValue(key, out var n)) { n.Count++; return n; }
            if (into.Count >= MaxResourcesPerParent) { parent.ChildrenTruncated = true; capped++; return parent; }
            n = make();
            into[key] = n;
            parent.Children.Add(n);
            return n;
        }

        var byGuid = new Dictionary<string, ProcessSlot>(StringComparer.OrdinalIgnoreCase);
        var cappedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byPid = new Dictionary<string, List<ProcessSlot>>(StringComparer.Ordinal);   // HOST|0xlogon|pid → every process that had it, in creation order (PIDs are reused)
        static string PidKey(TimelineEvent e, string logon, string pid) => HostKey(e.Host) + "|" + NormLogon(logon) + "|" + pid;

        // processes first, so that a resource seen in the same second still finds its process
        foreach (var e in ordered)
        {
            if (e.EventId == "4689" && IsSecurity(e))
            {
                // the end of a process closes its PID: a later access with the same PID belongs to another process
                if (byPid.TryGetValue(PidKey(e, F(e, "SubjectLogonId"), NormPid(F(e, "ProcessId"))), out var ended)
                    && ended.LastOrDefault(x => x.Created <= e.Time.Utc && x.Exited is null) is { } done)
                    done.Exited = e.Time.Utc;
                continue;
            }
            if (!IsProcessEvent(e)) continue;
            bool sys1 = IsSysmon(e);
            // 4688: TargetLogonId (Windows 10+) is the new process's own logon; older systems give only the creator's SubjectLogonId
            var rawLogon = sys1 ? F(e, "LogonId") : NormLogon(F(e, "TargetLogonId")).Length > 0 ? F(e, "TargetLogonId") : F(e, "SubjectLogonId");
            var sl = Session(e, rawLogon, out var why);
            if (sl is null) continue;
            if (sl.Processes >= MaxProcessesPerSession)
            {
                sl.Node.ChildrenTruncated = true; capped++;
                if (sys1 && F(e, "ProcessGuid") is { Length: > 0 } cg) cappedGuids.Add(cg);
                continue;
            }
            var image = sys1 ? F(e, "Image") : F(e, "NewProcessName");
            var pid = NormPid(sys1 ? F(e, "ProcessId") : F(e, "NewProcessId"));
            var cmd = F(e, "CommandLine");
            var node = new IdentityPathNode
            {
                Kind = "process", Utc = e.Time.UtcIso, EvidenceId = e.EvidenceId, Locator = e.Locator,
                Text = $"Proces {(image.Length > 0 ? image : "fără nume")}" + (pid.Length > 0 ? $" (PID {pid})" : "") + (cmd.Length > 0 ? $": {(cmd.Length > 200 ? cmd[..200] + "…" : cmd)}" : ""),
                Reason = why + (sys1 ? " (Sysmon 1)" : " (4688)"),
            };
            sl.Node.Children.Add(node);
            sl.Processes++;
            report.ProcessesLinked++;
            var slot = new ProcessSlot { Node = node, Session = sl, Created = e.Time.Utc!.Value };
            if (sys1 && F(e, "ProcessGuid") is { Length: > 0 } g) byGuid[g] = slot;
            if (pid.Length > 0) (byPid.TryGetValue(PidKey(e, rawLogon, pid), out var pl) ? pl : byPid[PidKey(e, rawLogon, pid)] = []).Add(slot);
        }

        foreach (var e in ordered)
        {
            if (!IsResourceEvent(e)) continue;
            if (IsSysmon(e))
            {
                var g = F(e, "ProcessGuid");
                if (g.Length == 0) { noGuid++; continue; }
                if (!byGuid.TryGetValue(g, out var p)) { if (cappedGuids.Contains(g)) capped++; else noProcess++; continue; }
                var (kind, value) = e.EventId switch
                {
                    "3" => ("conexiune", $"{F(e, "DestinationIp")}:{F(e, "DestinationPort")}" + (F(e, "DestinationHostname") is { Length: > 0 } dh ? $" ({dh})" : "")),
                    "11" => ("fișier creat", F(e, "TargetFilename")),
                    _ => ("interogare DNS", F(e, "QueryName")),
                };
                if (value.Trim(':').Length == 0) continue;
                var r = Resource(p.Resources, p.Node, kind + "|" + value, () => new IdentityPathNode
                {
                    Kind = "resource", Text = $"{char.ToUpperInvariant(kind[0])}{kind[1..]}: {value}", Utc = e.Time.UtcIso, EvidenceId = e.EvidenceId, Locator = e.Locator,
                    Reason = $"același ProcessGuid {g} (Sysmon {e.EventId})",
                }, ref capped);
                if (r.Kind == "resource" && r.Count == 1) report.ResourcesLinked++;
                continue;
            }
            var sl = Session(e, F(e, "SubjectLogonId"), out var why);
            if (sl is null) continue;
            if (e.EventId == "4663")
            {
                var obj = F(e, "ObjectName");
                if (obj.Length == 0) continue;
                var pid = NormPid(F(e, "ProcessId"));
                var t = e.Time.Utc!.Value;
                // the process that held this PID at the time of the access, in the same session; none → the access stays under the session
                var parent = pid.Length > 0 && byPid.TryGetValue(PidKey(e, F(e, "SubjectLogonId"), pid), out var candidates)
                    ? candidates.LastOrDefault(x => x.Session == sl && x.Created <= t && (x.Exited is not { } ex || t <= ex)) : null;
                var resources = parent?.Resources ?? sl.Resources;
                var r = Resource(resources, parent?.Node ?? sl.Node, "obiect|" + obj, () => new IdentityPathNode
                {
                    Kind = "resource", Text = $"Obiect accesat: {obj}" + (parent is null && F(e, "ProcessName") is { Length: > 0 } pn ? $" (de {pn})" : ""), Utc = e.Time.UtcIso, EvidenceId = e.EvidenceId, Locator = e.Locator,
                    Reason = parent is not null ? $"{why}; același PID {pid}, al procesului pornit în sesiune înainte de acces și neîncheiat (4663)" : $"{why} (4663)",
                }, ref capped);
                if (r.Kind == "resource" && r.Count == 1) report.ResourcesLinked++;
            }
            else
            {
                var share = F(e, "ShareName");
                if (share.Length == 0) continue;
                var target = F(e, "RelativeTargetName");
                var value = share + (target.Length > 0 && target != "\\" ? "\\" + target.TrimStart('\\') : "");
                var r = Resource(sl.Resources, sl.Node, "share|" + value, () => new IdentityPathNode
                {
                    Kind = "resource", Text = $"Partajare accesată: {value}" + (F(e, "IpAddress") is { Length: > 0 } ip ? $" (din {ip})" : ""), Utc = e.Time.UtcIso, EvidenceId = e.EvidenceId, Locator = e.Locator,
                    Reason = $"{why} ({e.EventId})",
                }, ref capped);
                if (r.Kind == "resource" && r.Count == 1) report.ResourcesLinked++;
            }
        }

        foreach (var (key, hosts) in hostsOf.OrderBy(k => k.Key, StringComparer.Ordinal))
            report.Paths[key] = hosts.Values.OrderBy(h => h.Text, StringComparer.Ordinal).ToList();

        void Gap(int n, string what, string why) { if (n > 0) report.Gaps.Add(new IdentityGap(what.Replace("{n}", n.ToString(CultureInfo.InvariantCulture)), why)); }
        Gap(noSession, "{n} evenimente de proces sau de acces au un LogonId fără autentificare corespunzătoare pe aceeași stație.",
            "Jurnalul de autentificare al stației lipsește sau a fost suprascris; aceste evenimente nu sunt legate de niciun cont prin presupunere.");
        Gap(hostSpelling, "{n} evenimente au un LogonId emis de o stație scrisă altfel (nume scurt față de nume complet).",
            "Numele stației diferă între jurnale (de exemplu Sysmon și Security); evenimentele nu sunt legate doar pentru că numele seamănă.");
        Gap(outsideWindow, "{n} evenimente au un LogonId cunoscut, dar ora lor este în afara oricărei sesiuni cu acel LogonId.",
            "Un LogonId se poate refolosi după repornire; fără sesiunea care îl conține, evenimentul nu este legat.");
        Gap(noTime, "{n} evenimente de proces sau de acces fără oră nu au putut fi puse într-o sesiune.", "Fără oră nu se poate alege sesiunea.");
        Gap(noLogon, "{n} evenimente de proces sau de acces nu au un LogonId utilizabil.", "Fără LogonId evenimentul nu poate fi legat de o sesiune.");
        Gap(noGuid, "{n} evenimente Sysmon (rețea, fișier, DNS) nu au ProcessGuid.", "Fără ProcessGuid nu se știe ce proces le-a produs.");
        Gap(noProcess, "{n} evenimente Sysmon (rețea, fișier, DNS) au un ProcessGuid al cărui proces nu este legat de nicio sesiune.",
            "Pornirea procesului (Sysmon 1) lipsește, sau procesul aparține unui cont de sistem.");
        Gap(capped, $"{{n}} legături nu au fost afișate (limita de {MaxProcessesPerSession} procese pe sesiune și {MaxResourcesPerParent} resurse pe nod).",
            "Toate evenimentele rămân în cronologie.");
        Gap(machine, "{n} evenimente aparțin conturilor de sistem ale stației (SYSTEM, LOCAL SERVICE, NETWORK SERVICE).", "Nu sunt puse sub contul unei persoane.");
        return report;
    }
}