using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>A validation problem on one line (1-based data row; for imports, the line of the text). Never dropped silently.</summary>
public sealed record RegisterIssue(string Register, int Line, string Message, bool IsError = true)
{
    public override string ToString() => $"{Register}" + (Line > 0 ? $", linia {Line}" : "") + $": {Message}" + (IsError ? "" : " (avertisment)");
}

/// <summary>
/// A register the operator maintains (media register, users register): DATA only. The application never applies it to the system (decision 13);
/// an empty register is "registru nedefinit" and is never read as "conform".
/// </summary>
public interface IRegisterData
{
    /// <summary>"media" or "users".</summary>
    string Kind { get; }
    string Title { get; }
    string FileName { get; }
    string SchemaVersion { get; set; }
    DateTimeOffset? UpdatedUtc { get; set; }
    IReadOnlyList<string> Columns { get; }
    /// <summary>At least one row. An empty register is not defined.</summary>
    bool IsDefined { get; }
    List<string[]> Cells();
    /// <summary>Replaces the rows (each row has exactly <see cref="Columns"/> cells; missing cells are empty; cells are trimmed).</summary>
    void SetCells(IEnumerable<IReadOnlyList<string>> rows);
    /// <summary>Checks one row against the rows before it (uniqueness). Empty = valid.</summary>
    List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> cells, IReadOnlyList<string[]> previousRows);
    List<RegisterIssue> Validate();
}

internal static partial class RegisterRules
{
    [GeneratedRegex(@"^(?:VID_)?(?<v>[0-9A-Fa-f]{4})\s*[:&_/ ]\s*(?:PID_)?(?<p>[0-9A-Fa-f]{4})$", RegexOptions.CultureInvariant)] private static partial Regex VidPidRegex();
    [GeneratedRegex(@"^S-1-\d+(?:-\d+)+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)] private static partial Regex SidRegex();

    /// <summary>"0951:1666" / "VID_0951&amp;PID_1666" -> "0951:1666" (lower case); null when it is not a VID/PID.</summary>
    public static string? NormalizeVidPid(string s)
    {
        var m = VidPidRegex().Match(s.Trim());
        return m.Success ? (m.Groups["v"].Value + ":" + m.Groups["p"].Value).ToLowerInvariant() : null;
    }

    public static bool IsSid(string s) => SidRegex().IsMatch(s.Trim());

    public static List<string> SplitList(string s) =>
        s.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static bool BadAccount(string a) => a.Count(ch => ch == '\\') > 1 || a.StartsWith('\\') || a.EndsWith('\\');

    public static bool TryDate(string s, out DateOnly d) => ProfileTables.TryParseDate(s, out d);

    /// <summary>"DOMAIN\user" and "user" compare equal on the user part only when the other side has no domain; otherwise the whole string.</summary>
    public static bool SameAccount(string a, string b)
    {
        a = a.Trim(); b = b.Trim();
        if (a.Length == 0 || b.Length == 0) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        bool aHas = a.Contains('\\'), bHas = b.Contains('\\');
        if (aHas && bHas) return false;
        return UserPart(a).Equals(UserPart(b), StringComparison.OrdinalIgnoreCase);
    }

    public static string UserPart(string a) => a[(a.LastIndexOf('\\') + 1)..];
}
