using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Planner #12 B2: the same scheduled task, Run value or Startup entry is seen twice, once in the live snapshot (LIVE-*) and once in a stored copy
/// (task XML / TaskCache, registry hive, collected Startup folder: PERSIST-*). This step runs once, after every finding exists and before the later stages
/// that point at findings by id, and leaves ONE finding per object.
/// <para>
/// Primary rule: the stored copy (PERSIST-*) is kept, because its evidence is a hashed copy held in the case, carries its own timestamps (key LastWrite,
/// task registration) and can be re-opened by the examiner, while the live snapshot is one instantaneous reading. The LIVE-* finding is dropped and its
/// evidence references are appended to the primary one, so no reference is lost; a limitation line on the primary names the dropped rule and finding id.
/// The primary keeps its FindingId, so ids stay stable.
/// </para>
/// <para>
/// Matching is deliberately conservative and deterministic (findings are visited in their current order, candidates compared ordinally after normalisation).
/// When the match is not unique, both findings are kept.
/// </para>
/// </summary>
public static class LiveFindingMerger
{
    private const string RunTitlePrefix = "Pornire automată dintr-o locație în care utilizatorii pot scrie: ";

    public sealed record Result(List<Finding> Findings, int Merged);

    public static Result Merge(IReadOnlyList<Finding> all)
    {
        var drop = new HashSet<Finding>();
        // --- tasks ---
        var liveTasks = all.Where(f => f.RuleId == "LIVE-TASK-USERPATH").ToList();
        var storedTasks = all.Where(f => f.RuleId == "PERSIST-TASK-CONFIG").ToList();
        foreach (var live in liveTasks)
        {
            var liveName = Locator(live, "Tasks[");
            if (liveName is null) continue;
            var full = NormTask(liveName);
            var cands = storedTasks.Where(s => SameHost(live, s) && TaskNameOf(s) is { } n && NormTask(n) == full).ToList();
            if (cands.Count == 0)
            {
                // the two sources may name the task differently ("\Lenovo\Vantage\Schedule\X" vs "X"): fall back to the leaf, only when it is unique on both sides and the program is the same
                var leaf = Leaf(full);
                var sameLeafLive = liveTasks.Count(l => Locator(l, "Tasks[") is { } ln && Leaf(NormTask(ln)) == leaf);
                var leafCands = storedTasks.Where(s => SameHost(live, s) && TaskNameOf(s) is { } n && Leaf(NormTask(n)) == leaf).ToList();
                if (sameLeafLive == 1 && leafCands.Count == 1 && ExeMatches(live.File, leafCands[0].File)) cands = leafCands;
            }
            else if (cands.Count > 1) cands = cands.Where(s => ExeMatches(live.File, s.File)).ToList();
            if (cands.Count == 1) Absorb(cands[0], live, drop);
        }
        // --- Run values ---
        var liveRuns = all.Where(f => f.RuleId == "LIVE-AUTORUN-USERPATH").ToList();
        var storedRuns = all.Where(f => f.RuleId == "PERSIST-RUNKEY-USERPATH").ToList();
        foreach (var live in liveRuns)
        {
            var name = live.Title.StartsWith(RunTitlePrefix, StringComparison.Ordinal) ? live.Title[RunTitlePrefix.Length..] : null;
            if (name is null) continue;
            // the same value name and the same program; several hives (other profiles) with the same pair make it ambiguous: keep both
            var cands = storedRuns.Where(s => SameHost(live, s) && s.Title.StartsWith(RunTitlePrefix, StringComparison.Ordinal)
                                              && string.Equals(s.Title[RunTitlePrefix.Length..], name, StringComparison.OrdinalIgnoreCase) && ExeMatches(live.File, s.File)).ToList();
            if (cands.Count == 1) Absorb(cands[0], live, drop);
        }
        // --- Startup folder ---
        var liveStartup = all.Where(f => f.RuleId is "LIVE-STARTUP-LNK" or "LIVE-STARTUP-FILE").ToList();
        var storedStartup = all.Where(f => f.RuleId == "PERSIST-STARTUP-FOLDER").ToList();
        foreach (var live in liveStartup)
        {
            if (live.File.Length == 0) continue;
            var cands = storedStartup.Where(s => SameHost(live, s) && s.Description.Contains("(" + live.File + ")", StringComparison.OrdinalIgnoreCase)).ToList();
            if (cands.Count == 1) Absorb(cands[0], live, drop);
        }
        return new Result([.. all.Where(f => !drop.Contains(f))], drop.Count);
    }

    private static void Absorb(Finding primary, Finding live, HashSet<Finding> drop)
    {
        if (!drop.Add(live)) return;
        foreach (var r in live.SupportingEvidence)
            if (!primary.SupportingEvidence.Contains(r)) primary.SupportingEvidence.Add(r);
        primary.Limitations = [.. primary.Limitations,
            $"Același obiect a fost observat și în fotografia stării live (regula {live.RuleId}, constatarea {live.FindingId}); referințele acelei probe sunt atașate aici, iar constatarea separată a fost eliminată. Copia stocată din caz este sursa principală."];
        primary.AuditTrail = [.. primary.AuditTrail, new FindingAuditEntry("finding.merged", "LogAnalyzer", $"{live.RuleId} {live.FindingId} -> {primary.RuleId} {primary.FindingId}", DateTimeOffset.UtcNow)];
    }

    private static bool SameHost(Finding a, Finding b) => a.Host.Length == 0 || b.Host.Length == 0 || string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    private static string? Locator(Finding f, string prefix)
    {
        var l = f.SupportingEvidence.Count > 0 ? f.SupportingEvidence[0].Locator : "";
        return l.StartsWith(prefix, StringComparison.Ordinal) && l.EndsWith(']') ? l[prefix.Length..^1] : null;
    }

    /// <summary>PERSIST-TASK-CONFIG descriptions start with the task path, then " rulează ".</summary>
    private static string? TaskNameOf(Finding f)
    {
        var i = f.Description.IndexOf(" rulează ", StringComparison.Ordinal);
        return i > 0 ? f.Description[..i] : null;
    }

    private static string NormTask(string name) =>
        Regex.Replace(name ?? "", @"-?S-1-5-21(?:-\d+){3,4}$", "", RegexOptions.CultureInvariant).Trim().Replace('/', '\\').Trim('\\').ToLowerInvariant();

    private static string Leaf(string normalized) { var i = normalized.LastIndexOf('\\'); return i >= 0 ? normalized[(i + 1)..] : normalized; }

    /// <summary>The live value is a whole command line (maybe quoted, with arguments); the stored one is the program path. Unexpanded variables never match, which keeps both findings.</summary>
    private static bool ExeMatches(string liveCommand, string storedPath)
    {
        var c = (liveCommand ?? "").Trim().Trim('"').ToLowerInvariant();
        var p = (storedPath ?? "").Trim().Trim('"').ToLowerInvariant();
        return c.Length > 0 && p.Length > 0 && c.Contains(p, StringComparison.Ordinal);
    }
}
