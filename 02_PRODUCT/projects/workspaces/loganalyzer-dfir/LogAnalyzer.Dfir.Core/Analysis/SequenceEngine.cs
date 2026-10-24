using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>One step of a combined sequence: where it comes from, when, which account (as the evidence names it), which object, and the evidence behind it.</summary>
/// <remarks>An unobserved step has <see cref="Observed"/> = false and a <see cref="Note"/> that says why ("pas neobservat (sursa X indisponibilă)"); it is never treated as absent.</remarks>
public sealed record SeqStep(string Name, string Source, DateTimeOffset? TimeUtc, string Account, string ObjectName, string Detail,
    IReadOnlyList<EvidenceRef> Evidence, IReadOnlyList<string> FindingIds, bool Observed = true, string Note = "");

/// <summary>Result of joining two values (accounts, hosts, volumes): equal, different, or not comparable because one side is not known.</summary>
public enum JoinResult { Same, Different, Unknown }

/// <summary>
/// WP14b: the pure part of the sequence rules. It orders steps on the UTC timeline, joins them by host, account (where known), drive letter and time window,
/// and renders them. It reads no log and calls no rule: the rules in <see cref="SequenceRules"/> find the steps, this decides how they fit together. The windows
/// come from <see cref="SequenceData"/>, not from code.
/// </summary>
public static class SequenceEngine
{
    private static readonly Regex SidRx = new(@"^S-1-\d+(-\d+)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>True when <paramref name="later"/> is not before <paramref name="earlier"/> and at most <paramref name="window"/> after it. A missing time never joins.</summary>
    public static bool Follows(DateTimeOffset? earlier, DateTimeOffset? later, TimeSpan window) =>
        earlier is { } a && later is { } b && b >= a && b - a <= window;

    /// <summary>True when <paramref name="t"/> is in [from, to] (both ends inclusive). A missing time is outside.</summary>
    public static bool Within(DateTimeOffset? t, DateTimeOffset from, DateTimeOffset to) => t is { } x && x >= from && x <= to;

    /// <summary>Accounts compare only when both are known and are names (a SID cannot be compared with a name without a lookup); otherwise <see cref="JoinResult.Unknown"/>.</summary>
    public static JoinResult JoinAccount(string a, string b)
    {
        a = a.Trim(); b = b.Trim();
        if (a.Length == 0 || b.Length == 0 || a == "-" || b == "-" || SidRx.IsMatch(a) || SidRx.IsMatch(b)) return JoinResult.Unknown;
        return RegisterRules.SameAccount(a, b) ? JoinResult.Same : JoinResult.Different;
    }

    public static JoinResult JoinHost(string a, string b)
    {
        a = a.Trim(); b = b.Trim();
        if (a.Length == 0 || b.Length == 0) return JoinResult.Unknown;
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ? JoinResult.Same : JoinResult.Different;
    }

    /// <summary>Drive letters ("E" or "E:"); different letters never join, an unknown one cannot be compared.</summary>
    public static JoinResult JoinVolume(string a, string b)
    {
        string L(string s) => s.Trim().TrimEnd(':').ToUpperInvariant();
        a = L(a); b = L(b);
        if (a.Length == 0 || b.Length == 0) return JoinResult.Unknown;
        return a == b ? JoinResult.Same : JoinResult.Different;
    }

    /// <summary>Observed steps by time (ties keep the declared order), then the unobserved ones in the declared order.</summary>
    public static List<SeqStep> Order(IEnumerable<SeqStep> declared)
    {
        var list = declared.Select((s, i) => (S: s, I: i)).ToList();
        var observed = list.Where(x => x.S.Observed).OrderBy(x => x.S.TimeUtc ?? DateTimeOffset.MaxValue).ThenBy(x => x.I).Select(x => x.S);
        var missing = list.Where(x => !x.S.Observed).Select(x => x.S);
        return observed.Concat(missing).ToList();
    }

    /// <summary>First and last time among the given steps and times; null when none is known.</summary>
    public static (DateTimeOffset? First, DateTimeOffset? Last) Span(IEnumerable<SeqStep> steps, IEnumerable<DateTimeOffset?>? extra = null)
    {
        var t = steps.Where(s => s.Observed).Select(s => s.TimeUtc).Concat(extra ?? []).Where(x => x is not null).Select(x => x!.Value).ToList();
        return t.Count == 0 ? (null, null) : (t.Min(), t.Max());
    }

    /// <summary>"pas neobservat (...)": the reason is the source, never the absence of the activity.</summary>
    public static string NotObserved(string sourceLabel, bool sourceCollected) =>
        sourceCollected
            ? $"pas neobservat (sursa {sourceLabel} este colectată, dar nu are înregistrări potrivite; asta nu dovedește că pasul nu a avut loc)"
            : $"pas neobservat (sursa {sourceLabel} indisponibilă)";

    public static SeqStep Missing(string name, string sourceLabel, bool sourceCollected) =>
        new(name, sourceLabel, null, "", "", "", [], [], false, NotObserved(sourceLabel, sourceCollected));

    public static string Stamp(DateTimeOffset? t) => t is { } x ? x.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC" : "oră necunoscută";

    /// <summary>The numbered, ordered steps as text. The account is shown as the evidence gives it, or says that the sources do not give it.</summary>
    public static string Describe(IReadOnlyList<SeqStep> ordered)
    {
        var parts = new List<string>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            if (!s.Observed) { parts.Add($"{i + 1}) {s.Name}: {s.Note}."); continue; }
            var who = s.Account.Length > 0 ? $"cont {s.Account}" : "cont necunoscut în sursa acestui pas";
            parts.Add($"{i + 1}) {Stamp(s.TimeUtc)} — {s.Name}: {s.Detail} ({who}; sursa {s.Source}" + (s.FindingIds.Count > 0 ? $"; constatări {string.Join(", ", s.FindingIds)}" : "") + ").");
        }
        return string.Join(" ", parts);
    }

    public static List<SequenceStepInfo> Infos(IReadOnlyList<SeqStep> ordered) =>
        ordered.Select((s, i) => new SequenceStepInfo(i + 1, s.Name, s.Observed, s.Observed ? s.TimeUtc : null, s.Account, s.ObjectName, s.Source, s.FindingIds, s.Observed ? s.Detail : s.Note)).ToList();
}
