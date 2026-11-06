namespace LogAnalyzer.Dfir.Analysis;

/// <summary>Small helpers for <c>Finding.PlainWhat</c> (owner 2026-11-06: findings told for a non-specialist, no event codes, rule ids, SIDs or artifact names).</summary>
internal static class PlainPhrases
{
    /// <summary>„o dată”, „de 5 ori”, „de 20 de ori”.</summary>
    internal static string Times(int n) => n == 1 ? "o dată" : "de " + LogAnalyzer.Dfir.Language.RoText.Count(n, "dată", "ori");

    /// <summary>„name”, with the quotes the plain sentences use.</summary>
    internal static string Q(string s) => $"„{s}”";

    /// <summary>The account without its domain: "LAB\ana.pop" → "ana.pop".</summary>
    internal static string ShortAccount(string account)
    {
        var a = (account ?? "").Trim();
        return a.Contains('\\') ? a[(a.LastIndexOf('\\') + 1)..] : a;
    }

    /// <summary>A Windows or machine account is not a person: "labbackup$", SYSTEM, DWM-1…</summary>
    internal static bool IsStationAccount(string account)
    {
        var a = ShortAccount(account);
        return a.Length == 0 || a == "-" || a.EndsWith('$') || a.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || a.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("UMFD-", StringComparison.OrdinalIgnoreCase) || a.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) || a.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase);
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
    internal static string List(IReadOnlyList<string> items, int max = 5)
    {
        var shown = items.Take(max).ToList();
        var more = items.Count - shown.Count;
        if (shown.Count == 0) return "";
        var head = shown.Count == 1 ? shown[0] : string.Join(", ", shown.Take(shown.Count - 1)) + " și " + shown[^1];
        return more > 0 ? $"{head} și alte {more}" : head;
    }
}
