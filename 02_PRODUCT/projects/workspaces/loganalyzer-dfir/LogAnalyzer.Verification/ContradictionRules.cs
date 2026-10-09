using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>Everything a contradiction rule may look at for one finding. Rules read these facts only; they never call producer code.</summary>
public sealed class RuleContext
{
    public required Finding Finding { get; init; }
    public required IReadOnlyList<ResolvedRef> Refs { get; init; }
    public required CaseFacts Facts { get; init; }
    public required VerificationOptions Options { get; init; }
}

/// <summary>
/// One contradiction test. It returns one Romanian sentence per contradiction it finds (empty = none found). "None found" never means
/// "none exist": a rule only sees the rows that are in the case.
/// </summary>
public interface IContradictionRule
{
    string RuleId { get; }
    string Description { get; }
    IEnumerable<string> Evaluate(RuleContext c);
}

/// <summary>The built-in rule set (v1: three rules, each with its own tests). Add a rule by implementing <see cref="IContradictionRule"/>.</summary>
public static class ContradictionRules
{
    public static IReadOnlyList<IContradictionRule> Default { get; } =
        [new ExecutionBeforeCreationRule(), new ExecutionOfAbsentFileRule(), new LogClearedWithSpanningRecordsRule()];

    internal static string Time(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly Regex Create = new(@"\bfile\s+create\b|\bFILE_CREATE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Delete = new(@"\bfile\s+delete\b|\bFILE_DELETE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool IsCreation(TimelineRow r) => r.Time is not null && (Create.IsMatch(r.Summary) || r.TimeSemantics.Contains("file created", StringComparison.OrdinalIgnoreCase));
    internal static bool IsDeletion(TimelineRow r) => r.Time is not null && Delete.IsMatch(r.Summary);

    /// <summary>The time the finding claims the program ran (last seen, else first seen); null when it claims none.</summary>
    internal static DateTimeOffset? ClaimedExecution(Finding f) => f.LastSeenUtc ?? f.FirstSeenUtc;

    /// <summary>Paths the execution claim is about: the finding's File (several are separated by ';'), else the paths of its execution rows.</summary>
    internal static List<string> ClaimedPaths(RuleContext c)
    {
        var paths = c.Finding.File.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (paths.Count == 0)
            paths.AddRange(c.Refs.Where(ArtifactKinds.IsExecution).SelectMany(r => r.Rows).Select(r => r.Path).Where(p => p.Length > 0));
        return paths.Select(p => p.Trim().Trim('"')).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Upper-case Windows path without drive letter, \VOLUME{..}\ or \device\harddiskvolumeN prefix.</summary>
    internal static string Normalize(string path)
    {
        var p = path.Trim().Trim('"').Replace('/', '\\');
        if (p.StartsWith("\\VOLUME{", StringComparison.OrdinalIgnoreCase)) { var i = p.IndexOf('}'); if (i > 0) p = p[(i + 1)..]; }
        else if (Regex.Match(p, @"^\\device\\harddiskvolume\d+", RegexOptions.IgnoreCase) is { Success: true } m) p = p[m.Length..];
        else if (p.Length > 2 && p[1] == ':') p = p[2..];
        return p.ToUpperInvariant();
    }

    /// <summary>
    /// Same file? Equal normalized paths, or a path the USN parser could only reconstruct from the tail ("…\Users\x\a.exe") that is the tail of
    /// the other (at least two path segments, so a bare common name never matches).
    /// </summary>
    internal static bool SamePath(string a, string b)
    {
        var x = Normalize(a); var y = Normalize(b);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x == y) return true;
        static bool Tail(string partial, string full)
        {
            if (!partial.StartsWith("…\\", StringComparison.Ordinal) && !partial.StartsWith("...\\", StringComparison.Ordinal)) return false;
            var tail = partial[(partial.IndexOf('\\'))..];
            return tail.Count(ch => ch == '\\') >= 2 && full.EndsWith(tail, StringComparison.Ordinal);
        }
        return Tail(x, y) || Tail(y, x);
    }

    internal static IEnumerable<TimelineRow> RowsFor(RuleContext c, string path, Func<TimelineRow, bool> kind) =>
        (c.Facts.Timeline ?? []).Where(r => r.Path.Length > 0 && kind(r) && SamePath(r.Path, path));
}

/// <summary>Execution claimed at T, but every creation record of that file is later than T: the file did not exist yet.</summary>
public sealed class ExecutionBeforeCreationRule : IContradictionRule
{
    public string RuleId => "CONTRA-EXEC-BEFORE-CREATION";
    public string Description => "execuția revendicată precede crearea fișierului (USN File create / oră de creare)";

    public IEnumerable<string> Evaluate(RuleContext c)
    {
        if (c.Finding.SemanticType != SemanticType.Execution || ContradictionRules.ClaimedExecution(c.Finding) is not { } claimed) yield break;
        foreach (var path in ContradictionRules.ClaimedPaths(c))
        {
            var creations = ContradictionRules.RowsFor(c, path, ContradictionRules.IsCreation).OrderBy(r => r.Time).ToList();
            if (creations.Count == 0) continue;
            // A creation at or before the claimed time explains the execution (a later re-creation is another file version).
            if (creations.Any(r => r.Time <= claimed + c.Options.ClockSkewTolerance)) continue;
            var first = creations[0];
            yield return $"{RuleId}: execuția lui {path} revendicată la {ContradictionRules.Time(claimed)} precede prima creare a fișierului, la {ContradictionRules.Time(first.Time!.Value)} " +
                         $"({first.Source}, proba {first.EvidenceId}, {first.Locator}).";
        }
    }
}

/// <summary>Execution claimed at T, but another source shows the file deleted before T and not created again in between: it was absent.</summary>
public sealed class ExecutionOfAbsentFileRule : IContradictionRule
{
    public string RuleId => "CONTRA-EXEC-FILE-ABSENT";
    public string Description => "programul a rulat, dar o altă sursă arată fișierul șters înaintea execuției (USN File delete)";

    public IEnumerable<string> Evaluate(RuleContext c)
    {
        if (c.Finding.SemanticType != SemanticType.Execution || ContradictionRules.ClaimedExecution(c.Finding) is not { } claimed) yield break;
        foreach (var path in ContradictionRules.ClaimedPaths(c))
        {
            var deletions = ContradictionRules.RowsFor(c, path, ContradictionRules.IsDeletion).Where(r => r.Time < claimed - c.Options.ClockSkewTolerance).OrderBy(r => r.Time).ToList();
            if (deletions.Count == 0) continue;
            var last = deletions[^1];
            var recreated = ContradictionRules.RowsFor(c, path, ContradictionRules.IsCreation).Any(r => r.Time > last.Time && r.Time <= claimed + c.Options.ClockSkewTolerance);
            if (recreated) continue;
            yield return $"{RuleId}: {path} apare ca rulat la {ContradictionRules.Time(claimed)}, dar {last.Source} (proba {last.EvidenceId}, {last.Locator}) îl arată șters la " +
                         $"{ContradictionRules.Time(last.Time!.Value)}, fără o nouă creare între timp.";
        }
    }
}

/// <summary>
/// A log-clear finding (Security 1102 / System 104) at time C, but the same log file holds ordinary records both before and after C.
/// A cleared log keeps nothing from before the clear; so either the clear did not happen as claimed or the file is not the cleared log.
/// </summary>
public sealed class LogClearedWithSpanningRecordsRule : IContradictionRule
{
    public string RuleId => "CONTRA-LOG-CLEAR-SPANNING-RECORDS";
    public string Description => "ștergere de jurnal revendicată, dar jurnalul are înregistrări și înainte, și după momentul ștergerii";

    public IEnumerable<string> Evaluate(RuleContext c)
    {
        foreach (var r in c.Refs)
            foreach (var clear in r.Rows.Where(x => x.Time is not null && (x.Source == ArtifactKinds.SecurityLog && x.EventId == "1102" || x.Source == "EventLog:System" && x.EventId == "104")))
            {
                var others = c.Facts.RowsByEvidence[clear.EvidenceId]
                    .Where(x => x.Source == clear.Source && x.Time is not null && x.EventId is not ("1102" or "104" or "1100" or "4719")).ToList();
                var before = others.Where(x => x.Time < clear.Time - c.Options.ClockSkewTolerance).OrderByDescending(x => x.Time).FirstOrDefault();
                var after = others.Where(x => x.Time > clear.Time + c.Options.ClockSkewTolerance).OrderBy(x => x.Time).FirstOrDefault();
                if (before is null || after is null) continue;
                yield return $"{RuleId}: ștergerea {clear.Source[9..]} {clear.EventId} revendicată la {ContradictionRules.Time(clear.Time!.Value)} (proba {clear.EvidenceId}), " +
                             $"dar același jurnal are înregistrări la {ContradictionRules.Time(before.Time!.Value)} ({before.Locator}) și la {ContradictionRules.Time(after.Time!.Value)} ({after.Locator}).";
            }
    }
}
