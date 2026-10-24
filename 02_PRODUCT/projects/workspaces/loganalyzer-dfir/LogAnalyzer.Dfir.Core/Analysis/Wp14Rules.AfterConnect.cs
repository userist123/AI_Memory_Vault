using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public enum AfterConnectKind { Execution, FileTouched, Download }

/// <summary>One event in the window around a medium connection, with its score (higher = more likely related to the medium).</summary>
public sealed record AfterConnectItem(DateTimeOffset WhenUtc, AfterConnectKind Kind, string What, string User, int Score, string Source, TimelineEvent Event)
{
    public string KindText => Kind switch { AfterConnectKind.Execution => "execuție", AfterConnectKind.FileTouched => "fișier deschis", _ => "descărcare" };
    public string Text => $"{WhenUtc:yyyy-MM-dd HH:mm} UTC · {KindText} · {What}" + (User.Length > 0 ? $" · {User}" : "") + $" · {Source} (scor {Score})";
}

/// <summary>What followed one connection of a medium: the scored and capped events, how many were excluded as other users' activity, and the session user.</summary>
public sealed record AfterConnectWindow(string Serial, DateTimeOffset ConnectUtc, string SessionUser, IReadOnlyList<AfterConnectItem> Items, int Candidates, int ExcludedOtherUsers)
{
    public bool Capped => Candidates > Items.Count;
    public string Heading =>
        $"Conectare {ConnectUtc:yyyy-MM-dd HH:mm} UTC (sesiune: {(SessionUser.Length > 0 ? SessionUser : "utilizator necunoscut")}): " +
        $"{Candidates} evenimente între −10 min și +2 h" + (Capped ? $", afișate primele {Items.Count} după scor" : "") +
        (ExcludedOtherUsers > 0 ? $"; {ExcludedOtherUsers} excluse (alți utilizatori sau conturi de sistem)" : "") + ".";
}

/// <summary>
/// E5 "Ce a urmat după conectare": for each connection of a medium, the program executions (Prefetch, Security 4688, Sysmon 1), the files touched (LNK,
/// Jump Lists, Sysmon 11) and the downloads in the window −10 min … +2 h, scored (on the medium's drive letter, soon after the connection, same user) and
/// capped. When the user of the interactive session open at the connection is known, other users' and system accounts' activity is excluded. Proximity in
/// time is association, never proof that the medium caused the activity.
/// </summary>
public static class AfterConnect
{
    public static readonly TimeSpan Before = TimeSpan.FromMinutes(10), After = TimeSpan.FromHours(2);
    public const int Cap = 25;

    private static readonly HashSet<string> SystemAccounts = new(StringComparer.OrdinalIgnoreCase) { "SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE", "ANONYMOUS LOGON", "-" };

    public static string Bare(string account)
    {
        var a = account.Trim();
        var i = a.LastIndexOf('\\');
        return i >= 0 ? a[(i + 1)..] : a;
    }

    /// <summary>The single interactive (2, 10, 11) session user open at <paramref name="atUtc"/>; "" when none or more than one.</summary>
    public static string SessionUserAt(IReadOnlyList<TimelineEvent> events, DateTimeOffset atUtc)
    {
        var open = new Dictionary<string, (string User, DateTimeOffset From)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events.Where(e => T(e) is { } t && t <= atUtc && Ev(e, "Security", 4624, 4634, 4647)).OrderBy(e => T(e)))
        {
            var id = F(e, "TargetLogonId", "LogonId");
            if (e.EventId == "4624")
            {
                var user = F(e, "TargetUserName");
                if (user.Length == 0 || IsMachineAccount(user) || SystemAccounts.Contains(user)) continue;
                if (F(e, "LogonType") is not ("2" or "10" or "11")) continue;
                open[id.Length > 0 ? id : Guid.NewGuid().ToString()] = (user, T(e)!.Value);
            }
            else if (id.Length > 0) open.Remove(id);
        }
        var users = open.Values.Select(v => v.User).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return users.Count == 1 ? users[0] : "";
    }

    /// <summary>Scores and caps the events around one connection. <paramref name="driveLetter"/> is "E" or "E:" ("" = unknown).</summary>
    public static AfterConnectWindow Window(IReadOnlyList<TimelineEvent> events, string serial, DateTimeOffset connectUtc, string? sessionUser = null, string driveLetter = "")
    {
        sessionUser ??= SessionUserAt(events, connectUtc);
        var letter = driveLetter.Trim().TrimEnd(':', '\\');
        var prefix = letter.Length == 1 ? letter + @":\" : "";
        var items = new List<AfterConnectItem>();
        int excluded = 0;
        foreach (var e in events)
        {
            if (T(e) is not { } t || t < connectUtc - Before || t > connectUtc + After) continue;
            var (kind, what, source) = Classify(e);
            if (kind is null || what.Length == 0) continue;
            var user = Bare(e.User.Length > 0 ? e.User : F(e, "SubjectUserName", "TargetUserName", "User", "UserName"));
            if (sessionUser.Length > 0 && user.Length > 0)
            {
                bool system = IsMachineAccount(user) || SystemAccounts.Contains(user);
                if (system || !user.Equals(Bare(sessionUser), StringComparison.OrdinalIgnoreCase)) { excluded++; continue; }
            }
            int score = kind == AfterConnectKind.Download ? 2 : 3;
            if (prefix.Length > 0 && what.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) score += 4;
            if (t >= connectUtc && t <= connectUtc.AddMinutes(15)) score += 2;
            if (t < connectUtc) score -= 2;
            if (sessionUser.Length > 0 && user.Length > 0) score += 1;
            items.Add(new AfterConnectItem(t, kind.Value, what, user, score, source, e));
        }
        var shown = items.OrderByDescending(i => i.Score).ThenBy(i => i.WhenUtc).Take(Cap).OrderBy(i => i.WhenUtc).ToList();
        return new AfterConnectWindow(serial, connectUtc, sessionUser, shown, items.Count, excluded);
    }

    private static (AfterConnectKind? Kind, string What, string Source) Classify(TimelineEvent e)
    {
        if (e.Source == "Prefetch") return (AfterConnectKind.Execution, e.Path.Length > 0 ? e.Path : e.Process, "Prefetch");
        if (Ev(e, "Security", 4688)) return (AfterConnectKind.Execution, F(e, "NewProcessName").Length > 0 ? F(e, "NewProcessName") : e.Process, "Security 4688");
        if (Ev(e, Sysmon, 1)) return (AfterConnectKind.Execution, F(e, "Image").Length > 0 ? F(e, "Image") : e.Process, "Sysmon 1");
        if (Ev(e, Sysmon, 11)) return (AfterConnectKind.FileTouched, F(e, "TargetFilename").Length > 0 ? F(e, "TargetFilename") : e.Path, "Sysmon 11");
        if (e.Source is "LNK" or "JumpList") return (AfterConnectKind.FileTouched, e.Path, e.Source == "LNK" ? "scurtătură LNK" : "Jump List");
        if (e.Source == "BrowserDownload") return (AfterConnectKind.Download, e.Path, "istoric browser");
        return (null, "", "");
    }

    /// <summary>Detail lines for the medium page (Centru medii) from an analysed timeline: one heading per connection, then its events.</summary>
    public static IReadOnlyList<string> DetailLines(IReadOnlyList<TimelineEvent> events, string serial, int maxConnections = 5)
    {
        var norm = LogAnalyzer.Dfir.Registers.MediaRegister.NormalizeSerial(serial);
        var m = Wp14Rules.ObservedUsb(events).FirstOrDefault(o => o.Serial.Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (m is null) return ["Ce a urmat după conectare: suportul nu apare în cronologia analizată (rulați „Colectează tot” sau o investigație)."];
        var lines = new List<string>();
        foreach (var w in Wp14Rules.AfterConnectWindows(events, m).Take(maxConnections))
        {
            lines.Add(w.Heading);
            lines.AddRange(w.Items.Select(i => "  • " + i.Text));
        }
        if (lines.Count == 0) lines.Add("Ce a urmat după conectare: nicio oră de conectare citită pentru acest suport.");
        return lines;
    }
}

public static partial class Wp14Rules
{
    internal const string AfterConnectRuleId = "MEDIA-AFTER-CONNECT";

    internal static IEnumerable<AfterConnectWindow> AfterConnectWindows(IReadOnlyList<TimelineEvent> events, Observed m)
    {
        var arrivals = m.Events.Where(e => Ev(e, "Microsoft-Windows-Partition/Diagnostic", 1006) && T(e) is not null).Select(e => T(e)!.Value).Distinct().OrderBy(t => t).ToList();
        if (arrivals.Count == 0) arrivals = m.Times.Distinct().OrderBy(t => t).ToList();
        foreach (var t in arrivals) yield return AfterConnect.Window(events, m.Serial, t, null, m.Letter);
    }

    /// <summary>One MEDIA-AFTER-CONNECT finding per medium whose connections were followed by activity; linked both ways to the medium's own findings.</summary>
    internal static void AfterConnectFindings(Ctx c, List<Finding> o)
    {
        foreach (var m in ObservedUsb(c.Events).Where(m => m.Serial.Length > 0))
        {
            var windows = AfterConnectWindows(c.Events, m).Where(w => w.Items.Count > 0).ToList();
            if (windows.Count == 0) continue;
            var letterPrefix = m.Letter.Length > 0 ? m.Letter.TrimEnd(':') + @":\" : "\0";
            bool execFromMedium = windows.SelectMany(w => w.Items).Any(i => i.Kind == AfterConnectKind.Execution && i.What.StartsWith(letterPrefix, StringComparison.OrdinalIgnoreCase));
            var desc = $"Ce a urmat după conectarea suportului {m.Serial}: " + string.Join(" ", windows.Take(6).Select(w =>
                           w.Heading + " " + string.Join("; ", w.Items.Take(10).Select(i => i.Text)) + (w.Items.Count > 10 ? $"; și încă {w.Items.Count - 10}." : "."))) +
                       (windows.Count > 6 ? $" (și încă {windows.Count - 6} conectări, vezi pagina suportului)." : "") +
                       " Scorul crește pentru căi de pe litera suportului, pentru evenimentele din primele 15 minute și pentru utilizatorul sesiunii; scade pentru cele dinaintea conectării.";
            var id = c.NextId();
            var related = o.Where(f => f.RuleId.StartsWith("MEDIA", StringComparison.Ordinal) && f.RuleId != AfterConnectRuleId &&
                                       (f.Title.Contains(m.Serial, StringComparison.OrdinalIgnoreCase) || f.Description.Contains(m.Serial, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var f in related)
            {
                f.RelatedFindingIds.Add(id);
                f.RecommendedNextSteps.Add($"Vezi „Ce a urmat după conectare” ({id}): {windows.Sum(w => w.Candidates)} evenimente în ferestrele −10 min … +2 h ale conectărilor.");
            }
            o.Add(new Finding
            {
                FindingId = id, RuleId = AfterConnectRuleId,
                Title = $"Ce a urmat după conectarea suportului {m.Serial}: {windows.Sum(w => w.Items.Count)} evenimente asociate în timp",
                Severity = execFromMedium ? Sev(c, Severity.Medium) : Severity.Low,
                Category = c.FindingCategory, Classification = Classification.Correlated, Confidence = Confidence.Low,
                FirstSeenUtc = windows[0].ConnectUtc, LastSeenUtc = windows[^1].Items.Max(i => i.WhenUtc),
                User = string.Join(", ", windows.Select(w => w.SessionUser).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)),
                Description = desc,
                ClassificationReason = "Asociere în timp între ora conectării (Partition/Diagnostic 1006 sau registrul USB) și execuțiile, fișierele deschise și descărcările din fereastră.",
                SemanticType = SemanticType.Observation,
                RelatedFindingIds = related.Select(f => f.FindingId).ToList(),
                SupportingEvidence = windows.SelectMany(w => w.Items).OrderByDescending(i => i.Score).Take(15).Select(i => Ref(i.Event, i.Text)).ToList(),
                AlternativeExplanations =
                [
                    "Apropierea în timp nu dovedește legătura: activitatea putea avea loc și fără suport.",
                    "Când utilizatorul sesiunii nu este cunoscut, evenimentele altor conturi nu pot fi excluse.",
                ],
            });
        }
    }
}
