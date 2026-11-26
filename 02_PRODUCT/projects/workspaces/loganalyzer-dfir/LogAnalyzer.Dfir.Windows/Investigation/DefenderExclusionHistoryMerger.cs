using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Planner #14 E3: a PS-HISTORY-SUSPICIOUS finding whose command is <c>Add-MpPreference -ExclusionPath &lt;p&gt;</c> is, when a Defender exclusion path of a
/// CFG-DEFENDER-EXCL finding equals the literal path, the probable source of that exclusion. The history line is added as supporting evidence of the CFG finding
/// (with a note that PSReadLine has no times) and the separate PS-HISTORY finding for that line is removed. Runs once, after every finding exists, in the same
/// post-processing stage as <see cref="LiveFindingMerger"/>. Deterministic: findings are visited in their current order; the first matching CFG finding on the same
/// host takes the line. Only literal paths match (a <c>$variable</c> cannot be resolved here); non-matching lines stay PS-HISTORY findings.
/// The CFG evidence quote lists at most <c>ConfigurationAudit.ListCap</c> exclusions, so a path beyond that cap is not matched.
/// </summary>
public static class DefenderExclusionHistoryMerger
{
    public const string Note = "comandă din istoricul PowerShell care poate fi sursa excluderii; PSReadLine nu are ore";

    public sealed record Result(List<Finding> Findings, int Merged);

    private const string Item = @"(?:""[^""]*""|'[^']*'|[^\s,;)""']+)";
    private static readonly Regex Cmd = new(@"Add-MpPreference\b.*?-ExclusionPath(?:\s+|:)(?:@\()?\s*(?<v>" + Item + @"(?:\s*,\s*" + Item + @")*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    private static readonly Regex Part = new(Item, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static Result Merge(IReadOnlyList<Finding> all)
    {
        var cfg = all.Where(f => f.RuleId == "CFG-DEFENDER-EXCL").ToList();
        if (cfg.Count == 0) return new Result([.. all], 0);
        var drop = new HashSet<Finding>();
        foreach (var ps in all.Where(f => f.RuleId == "PS-HISTORY-SUSPICIOUS" && f.SupportingEvidence.Count > 0))
        {
            var literal = LiteralPaths(ps.Description);
            if (literal.Count == 0) continue;
            foreach (var c in cfg)
            {
                if (!SameHost(ps, c)) continue;
                var listed = ListedPaths(c);
                if (!literal.Any(listed.Contains)) continue;
                var line = ps.SupportingEvidence[0];
                var shown = line with { Description = line.Description + ": " + Note };
                if (!c.SupportingEvidence.Contains(shown)) c.SupportingEvidence.Add(shown);
                c.AuditTrail = [.. c.AuditTrail, new FindingAuditEntry("finding.merged", "LogAnalyzer", $"{ps.RuleId} {ps.FindingId} -> {c.RuleId} {c.FindingId}", DateTimeOffset.UtcNow)];
                drop.Add(ps);
                break;
            }
        }
        return new Result([.. all.Where(f => !drop.Contains(f))], drop.Count);
    }

    /// <summary>Literal exclusion paths in the command text (normalised); an item with a $variable or a wildcard-free expression it cannot resolve is skipped.</summary>
    internal static List<string> LiteralPaths(string text)
    {
        var res = new List<string>();
        try
        {
            var m = Cmd.Match(text ?? "");
            if (!m.Success) return res;
            foreach (Match p in Part.Matches(m.Groups["v"].Value))
            {
                var v = p.Value.Trim().Trim('"', '\'');
                if (v.Length == 0 || v.Contains('$') || v.Contains('`') || v.Contains('(')) continue;
                res.Add(Norm(v));
            }
        }
        catch (RegexMatchTimeoutException) { }
        return res;
    }

    private static HashSet<string> ListedPaths(Finding cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in cfg.SupportingEvidence)
        {
            if (!e.Locator.EndsWith("Paths", StringComparison.OrdinalIgnoreCase)) continue;
            var q = e.Quote ?? "";
            var i = q.IndexOf(" excluderi: ", StringComparison.Ordinal);
            if (i < 0) continue;
            foreach (var item in q[(i + " excluderi: ".Length)..].Split("; ", StringSplitOptions.RemoveEmptyEntries))
                if (!item.StartsWith("… ", StringComparison.Ordinal)) set.Add(Norm(item));
        }
        return set;
    }

    private static string Norm(string p) => p.Trim().Trim('"', '\'').Replace('/', '\\').TrimEnd('\\');

    private static bool SameHost(Finding a, Finding b) => a.Host.Length == 0 || b.Host.Length == 0 || string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
}
