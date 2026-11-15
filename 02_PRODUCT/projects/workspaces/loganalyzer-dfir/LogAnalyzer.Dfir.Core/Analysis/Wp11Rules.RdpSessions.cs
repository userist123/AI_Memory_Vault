using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    // ---------------------------------------------------------------- RDP-SESSIONS

    private const int RdpSessionCap = 2000;
    private const int RdpSessionsListed = 15;
    private static readonly TimeSpan RdpCorroborationWindow = TimeSpan.FromMinutes(2);

    private enum RdpEnd { None, Disconnected, LoggedOff, Replaced }

    private sealed class RdpSession
    {
        public string Host = "", User = "", SessionId = "", UserKey = "";
        public readonly List<string> Addresses = [];
        public DateTimeOffset? Start, End, LastTime, ConnectedSince, ReplacedAt;
        public bool BeforeLog;
        public RdpEnd EndKind = RdpEnd.None;
        public TimeSpan Connected;
        public int Reconnects, Intervals, UnrecordedEnds, Net1149, Logon4624, Sec4778, Sec4779;
        public readonly List<TimelineEvent> Refs = [];
        public readonly List<(DateTimeOffset Time, string Addr, bool Reconnect)> StartPoints = [];   // 21/25 events, used for corroboration
        public bool Corroborated => Net1149 + Logon4624 + Sec4778 + Sec4779 > 0;
    }

    private sealed record RdpAux(TimelineEvent Event, DateTimeOffset Time, string User, string Ip, string Host, string Type);

    private readonly record struct RdpPoint(RdpSession S, DateTimeOffset P, bool Reconnect);

    /// <summary>User name without domain or UPN suffix, lower case: the form used to compare accounts across channels.</summary>
    private static string RdpUserPart(string u)
    {
        var s = u.Trim();
        int i = s.LastIndexOf('\\');
        if (i >= 0) s = s[(i + 1)..];
        int at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        return s.ToLowerInvariant();
    }

    private static bool RdpNetworkAddress(string ip) =>
        ip.Length > 0 && !ip.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) && IpClassifier.Classify(ip) != IpScope.Invalid;

    private static string RdpWhen(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

    /// <summary>The EventRecordID from a locator such as "EventRecordID=123", or 0 when there is none (tie-break for equal timestamps).</summary>
    private static long RdpRecordId(TimelineEvent e)
    {
        const string key = "EventRecordID=";
        int i = e.Locator.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        int j = i + key.Length, k = j;
        while (k < e.Locator.Length && char.IsAsciiDigit(e.Locator[k])) k++;
        return k > j && long.TryParse(e.Locator.AsSpan(j, k - j), out var v) ? v : 0;
    }

    /// <summary>
    /// Reconstructs RDP sessions from LocalSessionManager 21/22/23/24/25 linked by SessionID per station. The events are sorted by time
    /// (stable, then EventRecordID, then input order): the pipeline hands rules a time-ordered timeline, so "log order" does not exist and a reused
    /// ID is recognised by a new 21 on an ID that already has a session. Corroborated by RemoteConnectionManager 1149 and Security 4624 (type 10,
    /// type 7 for reconnects) within two minutes with the same address and user, and by Security 4778/4779; each corroborating event is assigned to
    /// exactly one session (the nearest in time). One informational finding; the severity of other rules is untouched.
    /// </summary>
    private static void RdpSessions(Ctx c, List<Finding> o)
    {
        var sessions = new List<RdpSession>();
        var active = new Dictionary<(string Host, string Id), RdpSession>();
        var seen = new HashSet<(string, int, string, long, string, string)>();
        var aux1149 = new List<RdpAux>();
        var aux4624 = new List<RdpAux>();
        var aux477x = new List<RdpAux>();
        var lsm = new List<(TimelineEvent E, DateTimeOffset T, long Rec, int Idx)>();
        int noSessionId = 0, noTime = 0, duplicates = 0, excluded = 0, orphans = 0, n1149 = 0, n4778 = 0, n4779 = 0, idx = 0;

        foreach (var e in c.Events)
        {
            idx++;
            if (Ev(e, TsRemoteConnection, 1149))
            {
                n1149++;
                var ip = F(e, "Param3").Trim();
                if (T(e) is { } t && RdpNetworkAddress(ip)) aux1149.Add(new(e, t, RdpUserPart(F(e, "Param1")), ip, e.Host.Trim(), ""));
            }
            else if (Ev(e, "Security", 4624))
            {
                var type = F(e, "LogonType");
                var ip = F(e, "IpAddress").Trim();
                if ((type is "10" or "7") && T(e) is { } t && RdpNetworkAddress(ip)) aux4624.Add(new(e, t, RdpUserPart(F(e, "TargetUserName")), ip, e.Host.Trim(), type));
            }
            else if (Ev(e, "Security", 4778, 4779))
            {
                bool reconnect = e.EventId == "4778";
                if (reconnect) n4778++; else n4779++;
                var ip = F(e, "ClientAddress").Trim();
                if (T(e) is { } t && RdpNetworkAddress(ip)) aux477x.Add(new(e, t, RdpUserPart(F(e, "AccountName")), ip, e.Host.Trim(), reconnect ? "4778" : "4779"));
            }
            else if (Ev(e, TsLocalSession, 21, 22, 23, 24, 25))
            {
                if (F(e, "SessionID").Trim().Length == 0) { noSessionId++; continue; }
                if (T(e) is not { } time) { noTime++; continue; }
                lsm.Add((e, time, RdpRecordId(e), idx));
            }
        }
        if (lsm.Count == 0 && noSessionId == 0 && noTime == 0) return;
        lsm.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Rec != b.Rec ? a.Rec.CompareTo(b.Rec) : a.Idx.CompareTo(b.Idx));

        RdpSession Begin(string host, string id, bool beforeLog)
        {
            var s = new RdpSession { Host = host, SessionId = id, BeforeLog = beforeLog };
            sessions.Add(s);
            active[(host.ToLowerInvariant(), id)] = s;
            return s;
        }
        void Touch(RdpSession s, TimelineEvent e, DateTimeOffset t, string user, string ip)
        {
            if (s.User.Length == 0 && user.Length > 0) { s.User = user; s.UserKey = RdpUserPart(user); }
            if (ip.Length > 0 && !s.Addresses.Contains(ip, StringComparer.OrdinalIgnoreCase)) s.Addresses.Add(ip);
            s.LastTime = t;
            if (s.Refs.Count < 3) s.Refs.Add(e);
        }
        void Disconnect(RdpSession s, DateTimeOffset t)
        {
            if (s.ConnectedSince is { } since) { s.Connected += t - since; s.Intervals++; s.ConnectedSince = null; }
        }

        foreach (var (e, time, _, _) in lsm)
        {
            int id = int.Parse(e.EventId);
            var sid = F(e, "SessionID").Trim();
            var host = e.Host.Trim();
            var key = (host.ToLowerInvariant(), sid);
            var user = F(e, "User").Trim();
            var addr = F(e, "Address").Trim();
            if (!seen.Add((key.Item1, id, sid, time.UtcTicks, user.ToLowerInvariant(), addr.ToLowerInvariant()))) { duplicates++; continue; }
            bool net = RdpNetworkAddress(addr);
            active.TryGetValue(key, out var cur);

            switch (id)
            {
                case 21:
                    // a logon happens once per session: an existing session with this ID belongs to an earlier life of the ID
                    if (cur is not null) { cur.EndKind = RdpEnd.Replaced; cur.ReplacedAt = time; active.Remove(key); cur = null; }
                    if (!net) { excluded++; break; }
                    cur = Begin(host, sid, false);
                    cur.Start = time; cur.ConnectedSince = time; cur.StartPoints.Add((time, addr, false));
                    Touch(cur, e, time, user, addr);
                    break;
                case 25:
                    if (!net) { excluded++; break; }
                    cur ??= Begin(host, sid, true);
                    if (cur.ConnectedSince is not null) cur.UnrecordedEnds++;   // reconnect with no disconnect before it: that interval has no recorded end, so it is not counted
                    cur.ConnectedSince = time;
                    cur.EndKind = RdpEnd.None; cur.End = null;
                    cur.Reconnects++; cur.StartPoints.Add((time, addr, true));
                    Touch(cur, e, time, user, addr);
                    break;
                case 22:
                    if (cur is null) { if (net) orphans++; else excluded++; break; }
                    Touch(cur, e, time, user, net ? addr : "");
                    break;
                default:   // 23, 24
                    if (cur is null && net) cur = Begin(host, sid, true);
                    if (cur is null) { if (net || id == 23 || addr.Length == 0) orphans++; else excluded++; break; }
                    Touch(cur, e, time, user, net ? addr : "");
                    Disconnect(cur, time);
                    cur.End = time;
                    if (id == 24) cur.EndKind = RdpEnd.Disconnected;
                    else { cur.EndKind = RdpEnd.LoggedOff; active.Remove(key); }
                    break;
            }
        }

        if (sessions.Count == 0) return;   // only console / invalid / unlinkable events: nothing to reconstruct

        // ---- keep the most recent sessions (the cap is on sessions, not on events)
        int total = sessions.Count;
        var ordered = sessions.Select((s, i) => (s, i)).OrderByDescending(x => x.s.LastTime ?? DateTimeOffset.MinValue).ThenByDescending(x => x.i).Select(x => x.s).ToList();
        var kept = ordered.Take(RdpSessionCap).ToList();
        bool capped = total > kept.Count;

        // ---- corroboration: same address, same user (case-insensitive, no domain), same station, at most two minutes apart; one event, one session
        var points = new Dictionary<string, List<RdpPoint>>(StringComparer.OrdinalIgnoreCase);
        var spans = new Dictionary<string, List<RdpSession>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in kept)
        {
            foreach (var p in s.StartPoints)
            {
                if (!points.TryGetValue(p.Addr, out var l)) points[p.Addr] = l = [];
                l.Add(new(s, p.Time, p.Reconnect));
            }
            foreach (var a in s.Addresses)
            {
                if (!spans.TryGetValue(a, out var l)) spans[a] = l = [];
                l.Add(s);
            }
        }
        foreach (var l in points.Values) l.Sort((a, b) => a.P.CompareTo(b.P));

        IEnumerable<RdpPoint> Near(RdpAux a)
        {
            if (!points.TryGetValue(a.Ip, out var l)) yield break;
            int lo = 0, hi = l.Count, from = 0;
            var min = a.Time - RdpCorroborationWindow;
            while (lo < hi) { int mid = (lo + hi) / 2; if (l[mid].P < min) lo = mid + 1; else hi = mid; }
            from = lo;
            for (int i = from; i < l.Count && l[i].P <= a.Time + RdpCorroborationWindow; i++)
                if (RdpMatch(l[i].S, a)) yield return l[i];
        }

        int assigned1149 = 0;
        foreach (var a in aux1149)
        {
            RdpPoint? best = null;
            foreach (var p in Near(a))
                if (p.P >= a.Time && (best is null || p.P - a.Time < best.Value.P - a.Time)) best = p;
            if (best is { } b) { b.S.Net1149++; assigned1149++; }
        }
        foreach (var a in aux4624)
        {
            RdpPoint? best = null;
            foreach (var p in Near(a))
                if ((a.Type == "10" || p.Reconnect) && (best is null || (p.P - a.Time).Duration() < (best.Value.P - a.Time).Duration())) best = p;
            if (best is { } b) b.S.Logon4624++;
        }
        foreach (var a in aux477x)
        {
            if (!spans.TryGetValue(a.Ip, out var cands)) continue;
            RdpSession? best = null;
            var bestGap = TimeSpan.MaxValue;
            foreach (var s in cands)
            {
                if (!RdpMatch(s, a)) continue;
                var lo = s.Start ?? DateTimeOffset.MinValue;
                // review r2: a session whose ID was reused ends, at the latest, when the new 21 reused it; it never takes Security events after that
                var hi = s.EndKind is RdpEnd.Disconnected or RdpEnd.LoggedOff && s.End is { } en ? en
                       : s.EndKind == RdpEnd.Replaced && s.ReplacedAt is { } ra ? ra : DateTimeOffset.MaxValue;
                var gap = a.Time < lo ? lo - a.Time : a.Time > hi ? a.Time - hi : TimeSpan.Zero;
                if (gap <= RdpCorroborationWindow && gap < bestGap) { best = s; bestGap = gap; }
            }
            if (best is null) continue;
            if (a.Type == "4778") best.Sec4778++; else best.Sec4779++;
        }
        int unmatched1149 = n1149 - assigned1149;

        var listed = kept.Take(RdpSessionsListed).ToList();
        var correlated = kept.Any(s => s.Corroborated);
        // review r2: the last time of a session is its last event, not the last of the few evidence references kept for it
        var times = kept.SelectMany(s => s.Refs.Select(T).Append(s.LastTime)).Where(t => t is not null).OrderBy(t => t).ToList();
        bool namedHosts = sessions.Any(s => s.Host.Length > 0);
        var tail = new List<string>();
        if (capped) tail.Add($"S-au păstrat cele mai recente {RdpSessionCap} din {total} sesiuni (plafon intern), restul au fost numărate, nu reconstituite");
        if (total > listed.Count) tail.Add($"Au fost listate cele mai recente {listed.Count} din {kept.Count} sesiuni păstrate");
        if (noSessionId > 0) tail.Add($"{RoText.Count(noSessionId, "eveniment", "evenimente")} fără număr de sesiune (neluate în calcul)");
        if (noTime > 0) tail.Add($"{RoText.Count(noTime, "eveniment", "evenimente")} fără oră (neluate în calcul)");
        if (duplicates > 0) tail.Add($"{RoText.Count(duplicates, "duplicat exact", "duplicate exacte")} ignorate");
        if (excluded > 0) tail.Add($"{RoText.Count(excluded, "eveniment", "evenimente")} din consolă sau cu adresă nevalidă excluse");
        if (orphans > 0) tail.Add($"{RoText.Count(orphans, "eveniment", "evenimente")} fără sesiune cunoscută (nu s-a creat nicio sesiune)");
        if (unmatched1149 > 0) tail.Add($"{RoText.Count(unmatched1149, "autentificare de rețea (1149)", "autentificări de rețea (1149)")} fără sesiune corespunzătoare (nu creează sesiune)");
        int c4778 = kept.Sum(s => s.Sec4778), c4779 = kept.Sum(s => s.Sec4779);
        if (c4778 + c4779 > 0) tail.Add($"Security a confirmat {RoText.Count(c4778, "reconectare", "reconectări")} (4778) și {RoText.Count(c4779, "deconectare", "deconectări")} (4779)");
        if (n4778 + n4779 > c4778 + c4779) tail.Add($"{RoText.Count(n4778 + n4779 - c4778 - c4779, "eveniment Security 4778/4779", "evenimente Security 4778/4779")} fără sesiune corespunzătoare");

        string DescribeSession(RdpSession s)
        {
            var user = s.User.Length > 0 ? ToolSafe(s.User) : "(utilizator necunoscut)";
            var addrs = s.Addresses.Select(ToolSafe).ToList();
            var addrText = addrs.Count == 0 ? "adresă necunoscută" : addrs.Count == 1 ? "de la " + addrs[0] : "de la adresele " + string.Join(", ", addrs) + " (reconectare din altă adresă)";
            // review r2: a session whose 21 is missing may have started before the log or its 21 may be lost (a 24/25 after a 23 on a reused ID): both are said
            var start = s.BeforeLog ? "fără început înregistrat (începută înainte de jurnal sau evenimentul 21 lipsește)" : "început " + RdpWhen(s.Start!.Value);
            string end = s.EndKind switch
            {
                RdpEnd.Disconnected => "ultima deconectare " + RdpWhen(s.End!.Value) + " (sesiune deconectată, fără închidere în jurnal)",
                RdpEnd.LoggedOff => "încheiată " + RdpWhen(s.End!.Value),
                RdpEnd.Replaced => "sfârșit neînregistrat (ID-ul a fost refolosit de o sesiune nouă" + (s.End is { } le ? "; ultima deconectare văzută " + RdpWhen(le) : "") + ")",
                _ => "fără sfârșit în jurnal",
            };
            string dur = s.Intervals > 0
                ? "durată totală din intervalele cu început și sfârșit înregistrate: " + PlainPhrases.Duration(s.Connected)
                : "durată necunoscută (niciun interval cu început și sfârșit înregistrate)";
            var co = new List<string>();
            if (s.Net1149 > 0) co.Add("autentificare de rețea reușită confirmată");
            if (s.Logon4624 > 0) co.Add($"{s.Logon4624} × autentificare Security 4624");
            if (s.Sec4778 + s.Sec4779 > 0) co.Add($"Security: {RoText.Count(s.Sec4778, "reconectare", "reconectări")} și {RoText.Count(s.Sec4779, "deconectare", "deconectări")} confirmate");
            if (s.UnrecordedEnds > 0) co.Add($"{RoText.Count(s.UnrecordedEnds, "reconectare", "reconectări")} fără deconectare înregistrată înainte (interval fără sfârșit, neluat în durată)");
            return $"{user} {addrText}: {start}, {end}, {dur}, {(s.Reconnects == 0 ? "fără reconectări" : "reconectări: " + s.Reconnects)}" +
                   (s.Host.Length > 0 ? $", stația {ToolSafe(s.Host)}" : namedHosts ? ", stație necunoscută" : "") + (co.Count > 0 ? "; " + string.Join(", ", co) : "");
        }

        var latest = listed[0];
        var distinctUsers = sessions.Select(s => s.UserKey).Where(u => u.Length > 0).Distinct().Count();
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = "RDP-SESSIONS", Title = $"Reconstituirea sesiunilor RDP ({RoText.Count(total, "sesiune", "sesiuni")})",
            Severity = Severity.Info, Category = "Lateral Movement", Classification = correlated ? Classification.Correlated : Classification.Direct,
            Confidence = correlated ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1021.001", SemanticType = SemanticType.Observation,
            FirstSeenUtc = times.FirstOrDefault(), LastSeenUtc = times.LastOrDefault(),
            User = ToolSafe(latest.User), Ip = latest.Addresses.FirstOrDefault() is { } ip0 ? ToolSafe(ip0) : "",
            PlainWhat = $"s-au reconstituit sesiunile de lucru la distanță pe acest calculator: {RoText.Count(total, "sesiune", "sesiuni")}" +
                        (distinctUsers > 0 ? $", pentru {RoText.Count(distinctUsers, "utilizator", "utilizatori")}" : "") + (capped ? " (au fost păstrate cele mai recente, lista este incompletă)" : ""),
            Description = string.Join("; ", listed.Select(DescribeSession)) + (tail.Count > 0 ? ". " + string.Join(". ", tail) : "") + ".",
            ClassificationReason = correlated
                ? "Sesiunile din LocalSessionManager sunt confirmate de al doilea canal (RemoteConnectionManager 1149, Security 4624 sau 4778/4779) cu aceeași adresă și același utilizator, la cel mult două minute."
                : "Sesiunile se bazează pe un singur canal (TerminalServices LocalSessionManager 21/22/23/24/25), legat prin numărul sesiunii.",
            SupportingEvidence = listed.SelectMany(s => s.Refs.Take(2).Select(e => Ref(e, $"{e.EventId} sesiunea {ToolSafe(s.SessionId)}"))).Take(30).ToList(),
            AlternativeExplanations = ["Adresa poate fi a unui gateway sau a unui tunel, nu a persoanei.", "Același număr de sesiune poate fi refolosit după repornire; legătura se face după ordinea în timp, nu după un identificator unic."],
        });
    }

    /// <summary>Same account and same station; an event with no station never matches a session on a named station.</summary>
    private static bool RdpMatch(RdpSession s, RdpAux a)
    {
        if (s.UserKey.Length == 0 || a.User.Length == 0 || s.UserKey != a.User) return false;
        return s.Host.Equals(a.Host, StringComparison.OrdinalIgnoreCase);
    }
}
