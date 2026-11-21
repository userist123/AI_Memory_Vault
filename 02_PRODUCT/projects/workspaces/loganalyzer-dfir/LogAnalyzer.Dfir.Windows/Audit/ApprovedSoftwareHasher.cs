using System.Security.Cryptography;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>One file read for an approved-software row: the path, its observed SHA-256 (empty when it could not be read) and why it could not be read (empty when it was).</summary>
public sealed record ApprovedSoftwareFile(string Path, string Sha256, string Problem);

/// <summary>
/// What the station shows for one profile row that has a path and a SHA-256. <see cref="Problem"/> is a problem of the row as a whole (no file at the path,
/// directory missing, too many matches, wildcard in the directory, unresolved variable); the per-file problems are in <see cref="Files"/>.
/// </summary>
public sealed record ApprovedSoftwareObservation(string Name, string ExpectedSha256, string PathPattern, IReadOnlyList<ApprovedSoftwareFile> Files, string Problem);

/// <summary>The hashes of the approved software of the procedure profile (check S07). <see cref="NoPath"/>: valid rows with a SHA-256 but no path (cannot be checked); <see cref="InvalidRows"/>: rows ignored because they fail the profile validation.</summary>
public sealed record ApprovedSoftwareIntegrity(IReadOnlyList<ApprovedSoftwareObservation> Rows, int NoPath, int InvalidRows);

/// <summary>
/// Reads, from the station, the files the procedure profile approves by path and SHA-256 and hashes them (the I/O of check S07; the evaluator only compares).
/// A literal path is hashed as it is; a pattern with wildcards is expanded only inside its own directory, without recursion, to at most
/// <see cref="MaxMatches"/> files. A file over <see cref="MaxFileBytes"/> is not hashed. Nothing is read when the profile has no such row.
/// </summary>
public static class ApprovedSoftwareHasher
{
    public const int MaxMatches = 50;
    public const long MaxFileBytes = 1L << 30;
    public const string TooBig = "prea mare, nehash-uit";
    public const string Missing = "lipsește";
    public const string AccessDenied = "acces refuzat";
    public const string TooManyMatches = "prea multe potriviri (peste 50), s-au citit primele 50";

    public static ApprovedSoftwareIntegrity Collect(IEnumerable<ApprovedSoftwareRow>? rows, CancellationToken ct = default)
    {
        var result = new List<ApprovedSoftwareObservation>();
        int noPath = 0, invalid = 0;
        foreach (var row in rows ?? [])
        {
            ct.ThrowIfCancellationRequested();
            if (ProfileTables.ValidateRow(ProfileTable.ApprovedSoftware, [row.Name, row.Publisher, row.PathPattern, row.Sha256]).Count > 0) { invalid++; continue; }
            var sha = row.Sha256.Trim();
            if (!IsHex64(sha)) continue;                         // no hash: nothing to compare against
            var pattern = row.PathPattern.Trim();
            if (pattern.Length == 0) { noPath++; continue; }
            result.Add(Observe(row.Name.Trim(), sha.ToLowerInvariant(), pattern, ct));
        }
        return new ApprovedSoftwareIntegrity(result, noPath, invalid);
    }

    private static bool IsHex64(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    private static ApprovedSoftwareObservation Observe(string name, string expected, string pattern, CancellationToken ct)
    {
        ApprovedSoftwareObservation Row(IReadOnlyList<ApprovedSoftwareFile> files, string problem) => new(name, expected, pattern, files, problem);
        var expanded = Environment.ExpandEnvironmentVariables(pattern).Replace('/', '\\');
        if (expanded.Contains('%')) return Row([], "variabilă de mediu nedefinită în cale");
        if (!Path.IsPathFullyQualified(expanded)) return Row([], "calea nu este completă (fără unitate de disc)");
        bool wild = expanded.IndexOfAny(['*', '?']) >= 0;
        if (!wild) return Row([HashFile(expanded, ct)], "");
        var dir = Path.GetDirectoryName(expanded);
        var filePattern = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(dir) || dir.IndexOfAny(['*', '?']) >= 0) return Row([], "metacaractere în directorul căii: se expandează doar numele fișierului, în directorul lui");
        List<string> found;
        try
        {
            found = Directory.EnumerateFiles(dir, filePattern, new EnumerationOptions { RecurseSubdirectories = false, MatchType = MatchType.Simple, IgnoreInaccessible = false })
                .Take(MaxMatches + 1).ToList();
        }
        catch (DirectoryNotFoundException) { return Row([], "directorul " + Missing); }
        catch (UnauthorizedAccessException) { return Row([], "directorul: " + AccessDenied); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return Row([], "directorul nu poate fi citit: " + ex.Message); }
        if (found.Count == 0) return Row([], "niciun fișier nu corespunde modelului");
        bool tooMany = found.Count > MaxMatches;
        found.Sort(StringComparer.OrdinalIgnoreCase);
        var files = found.Take(MaxMatches).Select(p => HashFile(p, ct)).ToList();
        return Row(files, tooMany ? TooManyMatches : "");
    }

    private static ApprovedSoftwareFile HashFile(string path, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists) return new(path, "", Missing);
            if (info.Length > MaxFileBytes) return new(path, "", TooBig);
            // shared read: an executable that is running is open for the loader; the file is only read, never changed
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            return new(path, Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(), "");
        }
        catch (UnauthorizedAccessException) { return new(path, "", AccessDenied); }
        catch (FileNotFoundException) { return new(path, "", Missing); }
        catch (DirectoryNotFoundException) { return new(path, "", Missing); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return new(path, "", "nu poate fi citit: " + ex.Message); }
    }
}
