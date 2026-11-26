namespace LogAnalyzer.Dfir.Analysis;

/// <summary>Small helpers for <c>Finding.PlainWhat</c> (owner 2026-11-06: findings told for a non-specialist, no event codes, rule ids, SIDs or artifact names).</summary>
internal static class PlainPhrases
{
    /// <summary>„o dată”, „de 5 ori”, „de 20 de ori”.</summary>
    internal static string Times(int n) => n == 1 ? "o dată" : "de " + LogAnalyzer.Dfir.Language.RoText.Count(n, "dată", "ori");

    /// <summary>A span in words: „o oră și 12 minute”, „3 zile”, „40 de secunde”; at most the two largest units.</summary>
    internal static string Duration(TimeSpan d)
    {
        d = d.Duration();
        static string Unit(long n, string one, string singular, string plural) => n == 1 ? one : LogAnalyzer.Dfir.Language.RoText.Count(n, singular, plural);
        var parts = new List<string>();
        if (d.Days > 0) parts.Add(Unit(d.Days, "o zi", "zi", "zile"));
        if (d.Hours > 0) parts.Add(Unit(d.Hours, "o oră", "oră", "ore"));
        if (d.Minutes > 0) parts.Add(Unit(d.Minutes, "un minut", "minut", "minute"));
        if (d.Seconds > 0 || parts.Count == 0) parts.Add(Unit(d.Seconds, "o secundă", "secundă", "secunde"));
        return string.Join(" și ", parts.Take(2));
    }

    /// <summary>„name”, with the quotes the plain sentences use.</summary>
    internal static string Q(string s) => $"„{s}”";

    /// <summary>The account without its domain: "LAB\ana.pop" → "ana.pop".</summary>
    internal static string ShortAccount(string account)
    {
        var a = (account ?? "").Trim();
        return a.Contains('\\') ? a[(a.LastIndexOf('\\') + 1)..] : a;
    }

    /// <summary>
    /// A Windows account is not a person: SYSTEM, DWM-1, NT AUTHORITY\… A name ending in $ is the station only when it is this computer's own account
    /// (<paramref name="host"/> + "$") or the event's subject SID is a machine-account SID (S-1-5-18/19/20); „labbackup$” made by an attacker stays an actor.
    /// </summary>
    internal static bool IsStationAccount(string account, string host = "", string sid = "")
    {
        var a = ShortAccount(account);
        if (a.Length == 0 || a == "-") return true;
        if (LogAnalyzer.Dfir.Language.PlainStory.IsStationAccount(account)) return true;
        if (sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20") return true;
        return LogAnalyzer.Dfir.Language.PlainStory.IsOwnMachineAccount(account, host);
    }

    /// <summary>
    /// This computer's own machine account („HOST$”), which the media and session rules leave out. Any other name ending in $ is kept: an attacker can
    /// name an account so (lab S5: „labbackup$”, round-2 review). Without a host nothing is left out (a real machine account is then named, with its note).
    /// </summary>
    internal static bool IsOwnMachineOrWindows(string account, string host) =>
        ShortAccount(account).EndsWith('$') && LogAnalyzer.Dfir.Language.PlainStory.IsOwnMachineAccount(account, host);

    /// <summary>The computer the events come from (the most frequent Host), "" when none says; used to tell this computer's own „HOST$” account from an account named like it.</summary>
    internal static string HostOf(IEnumerable<LogAnalyzer.Dfir.Model.TimelineEvent> events) =>
        events.Select(e => e.Host ?? "").Where(h => h.Length > 0 && h != "-").GroupBy(h => h, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";

    /// <summary>True when <paramref name="who"/> is ONE real account (not empty, „necunoscut”, a list, or a Windows service): the sentence can then use the account as its subject.</summary>
    internal static bool KnownActor(string who)
    {
        var w = (who ?? "").Trim();
        if (w.Contains("S-1-", StringComparison.Ordinal) && !LogAnalyzer.Dfir.Language.AccountResolver.IsSid(w)) return false;   // a SID inside a label would be printed by Who()
        return w.Length > 0 && w != "-" && !w.Equals("necunoscut", StringComparison.OrdinalIgnoreCase) && !w.Contains(',') && !w.Contains(';') &&
               !LogAnalyzer.Dfir.Language.PlainStory.IsStationAccount(w);
    }

    /// <summary>The last segment of a path ("C:\x\updsvc.exe" → "updsvc.exe").</summary>
    internal static string Leaf(string path)
    {
        var p = (path ?? "").Trim().Trim('"');
        var i = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
        return i >= 0 && i < p.Length - 1 ? p[(i + 1)..] : p;
    }

    /// <summary>One line, at most <paramref name="max"/> characters.</summary>
    internal static string Cut(string s, int max)
    {
        s = (s ?? "").ReplaceLineEndings(" ").Trim();
        return s.Length <= max ? s : s[..max].TrimEnd() + "…";
    }

    /// <summary>"a, b și c", at most <paramref name="max"/> items, then "și alte N".</summary>
    internal static string List(IReadOnlyList<string> items, int max = 5, (string Singular, string Plural)? moreNoun = null)
    {
        var shown = items.Take(max).ToList();
        var more = items.Count - shown.Count;
        var noun = moreNoun ?? ("element", "elemente");
        if (shown.Count == 0) return "";
        var head = shown.Count == 1 ? shown[0] : string.Join(", ", shown.Take(shown.Count - 1)) + " și " + shown[^1];
        return more > 0 ? $"{head} și încă {LogAnalyzer.Dfir.Language.RoText.Count(more, noun.Singular, noun.Plural)}" : head;
    }
}
