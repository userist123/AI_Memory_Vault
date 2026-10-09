using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Answers "is this software listed as approved in the procedure profile?" (WP15a, owner decision 19). The profile is data the operator
/// enters; a row that fails validation is not used (same rule as the maintenance policy). A match makes a finding Info with
/// "aprobat în profil" - it never deletes the finding, and no profile means nothing is approved.
/// </summary>
public static class ApprovedSoftwareMatcher
{
    /// <summary>The row that approves it, or null. The row's most specific constraint decides: SHA-256, else path pattern (<c>*</c> and <c>?</c> wildcards), else the name (an alias occurs in the row's Name).</summary>
    public static ApprovedSoftwareRow? Match(ProcedureProfile? profile, IEnumerable<string> aliases, IEnumerable<string> paths, IEnumerable<string> sha256s)
    {
        if (profile is null || profile.ApprovedSoftware.Count == 0) return null;
        var al = aliases.Select(a => a.Trim()).Where(a => a.Length >= 3).ToList();
        var ps = paths.Where(p => p.Length > 0).ToList();
        var hs = sha256s.Where(h => h.Length == 64).ToList();
        foreach (var row in profile.ApprovedSoftware)
        {
            if (ProfileTables.ValidateRow(ProfileTable.ApprovedSoftware, [row.Name, row.Publisher, row.PathPattern, row.Sha256]).Count > 0) continue;
            // The most specific constraint of the row decides: a hash, else a path pattern (a portable copy somewhere else is NOT the approved
            // installation), else the name. Evidence that cannot be checked against the constraint is not approved.
            if (row.Sha256.Trim().Length == 64) { if (hs.Any(h => h.Equals(row.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))) return row; continue; }
            if (row.PathPattern.Trim().Length > 0) { if (ps.Any(p => PathMatches(row.PathPattern.Trim(), p))) return row; continue; }
            if (row.Name.Trim().Length > 0 && al.Any(a => row.Name.Contains(a, StringComparison.OrdinalIgnoreCase))) return row;
        }
        return null;
    }

    /// <summary>Path without its volume: "C:\x" and "\VOLUME{guid}\x" (Prefetch) both become "\x", so one pattern fits every source.</summary>
    private static string WithoutVolume(string path)
    {
        var p = path.Replace('/', '\\').Trim();
        if (p.StartsWith(@"\VOLUME{", StringComparison.OrdinalIgnoreCase)) { var i = p.IndexOf('}'); if (i > 0) return p[(i + 1)..]; }
        return p.Length > 1 && p[1] == ':' ? p[2..] : p;
    }

    public static bool PathMatches(string pattern, string path)
    {
        var rx = "^" + Regex.Escape(WithoutVolume(pattern)).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(WithoutVolume(path), rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    }
}
