using System.Text;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.HashSets;

/// <summary>Safety limits for an import. Over a limit the import is refused whole, with a message; nothing is truncated silently.</summary>
public sealed record HashSetImportOptions(long MaxFileBytes = 2L * 1024 * 1024 * 1024, int MaxEntries = 5_000_000);

public sealed record HashSetLineIssue(int Line, string Reason)
{
    public override string ToString() => $"linia {Line}: {Reason}";
}

public sealed class HashSetImportResult
{
    public KnownHashSet? Set { get; init; }
    /// <summary>Set when the import was refused as a whole (limit, unreadable file, unknown category).</summary>
    public string Error { get; init; } = "";
    public int InvalidLineCount { get; init; }
    /// <summary>The first 20 invalid lines with their numbers; <see cref="InvalidLineCount"/> is the full count.</summary>
    public List<HashSetLineIssue> InvalidLines { get; init; } = [];
    public int DuplicateCount { get; init; }
    public int LinesRead { get; init; }
    public bool Ok => Set is not null && Error.Length == 0;
}

/// <summary>
/// Offline import of fingerprint lists: plain text (one hash per line, optionally followed by a space / tab and the file name) or CSV with a header that
/// has a SHA-256 / SHA256 column (and optionally SHA-1, MD5, FileName: the NSRL RDS CSV layout). Invalid lines are counted and listed, never skipped silently.
/// </summary>
public static class HashSetImporter
{
    public const int MaxListedIssues = 20;

    public static HashSetImportResult ImportFile(string path, string name, string category, HashSetImportOptions? options = null, string importedBy = "")
    {
        options ??= new();
        if (HashSetCategory.Normalize(category) is not { Length: > 0 } cat)
            return new HashSetImportResult { Error = "Categoria trebuie să fie „cunoscut-bun” sau „cunoscut-rău”." };
        try
        {
            var len = new FileInfo(path).Length;
            if (len > options.MaxFileBytes)
                return new HashSetImportResult { Error = $"Fișierul are {len:N0} octeți, peste limita de {options.MaxFileBytes:N0}. Împărțiți lista în fișiere mai mici sau măriți limita." };
            var sourceSha = Hashing.Sha256File(path);
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(reader, name, cat, Path.GetFileName(path), sourceSha, options, importedBy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HashSetImportResult { Error = $"Fișierul nu poate fi citit: {ex.Message}" };
        }
    }

    public static HashSetImportResult Parse(TextReader reader, string name, string category, string sourceFileName, string sourceSha256,
        HashSetImportOptions? options = null, string importedBy = "")
    {
        options ??= new();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<HashSetLineIssue>();
        int invalid = 0, duplicates = 0, lineNo = 0;
        int iSha256 = -1, iSha1 = -1, iMd5 = -1;
        bool csv = false, first = true;
        string? line;

        void Bad(string reason) { invalid++; if (issues.Count < MaxListedIssues) issues.Add(new HashSetLineIssue(lineNo, reason)); }
        bool Add(string hex)
        {
            var h = hex.ToUpperInvariant();
            if (hashes.Contains(h)) { duplicates++; return true; }
            if (hashes.Count >= options.MaxEntries) return false;
            hashes.Add(h);
            return true;
        }
        HashSetImportResult Over() => new() { Error = $"Lista depășește limita de {options.MaxEntries:N0} intrări (oprit la linia {lineNo}). Nu s-a importat nimic; împărțiți lista sau măriți limita.", LinesRead = lineNo };

        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            var t = line.Trim().TrimStart('﻿');
            if (t.Length == 0 || t[0] == '#') continue;
            if (first)
            {
                first = false;
                var cols = SplitCsv(t);
                if (cols.Count > 1 || IsHashHeader(cols[0]))
                {
                    iSha256 = cols.FindIndex(c => Norm(c) == "sha256");
                    iSha1 = cols.FindIndex(c => Norm(c) == "sha1");
                    iMd5 = cols.FindIndex(c => Norm(c) == "md5");
                    if (iSha256 >= 0 || iSha1 >= 0 || iMd5 >= 0) { csv = true; continue; }
                }
            }
            if (csv)
            {
                var cols = SplitCsv(t);
                int added = 0, bad = 0;
                foreach (var i in new[] { iSha256, iSha1, iMd5 })
                {
                    if (i < 0) continue;
                    var v = i < cols.Count ? cols[i].Trim() : "";
                    if (v.Length == 0) continue;
                    if (!IsHex(v) || KnownHashSet.AlgorithmOf(v) is null) { bad++; continue; }
                    if (!Add(v)) return Over();
                    added++;
                }
                if (added == 0) Bad(bad > 0 ? "amprentă invalidă (nu este hexazecimală de 32, 40 sau 64 de caractere)" : "nicio amprentă în coloanele SHA-256 / SHA-1 / MD5");
                else if (bad > 0) Bad("o amprentă din rând este invalidă (celelalte au fost importate)");
            }
            else
            {
                var token = t.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim('"');
                if (!IsHex(token) || KnownHashSet.AlgorithmOf(token) is null) { Bad($"amprentă invalidă „{Shorten(token)}” (se așteaptă 32, 40 sau 64 de caractere hexazecimale)"); continue; }
                if (!Add(token)) return Over();
            }
        }
        if (first) return new HashSetImportResult { Error = "Fișierul este gol: nu conține nicio linie.", LinesRead = lineNo };
        if (hashes.Count == 0)
            return new HashSetImportResult { Error = "Fișierul nu conține nicio amprentă validă.", InvalidLineCount = invalid, InvalidLines = issues, LinesRead = lineNo };

        var info = new KnownHashSetInfo
        {
            Id = Guid.NewGuid().ToString("N")[..12], Name = name.Trim(), Category = HashSetCategory.Normalize(category), EntryCount = hashes.Count,
            Algorithms = new[] { (64, "SHA-256"), (40, "SHA-1"), (32, "MD5") }.Where(a => hashes.Any(h => h.Length == a.Item1)).Select(a => a.Item2).ToList(),
            SourceFileName = sourceFileName, SourceSha256 = sourceSha256, ImportedUtc = DateTimeOffset.UtcNow, ImportedBy = importedBy,
        };
        return new HashSetImportResult { Set = new KnownHashSet(info, hashes), InvalidLineCount = invalid, InvalidLines = issues, DuplicateCount = duplicates, LinesRead = lineNo };
    }

    private static string Norm(string c) => c.Trim().Trim('"').Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
    private static bool IsHashHeader(string c) => Norm(c) is "sha256" or "sha1" or "md5";
    private static string Shorten(string s) => s.Length <= 40 ? s : s[..40] + "…";
    private static bool IsHex(string s) { foreach (var ch in s) if (!Uri.IsHexDigit(ch)) return false; return s.Length > 0; }

    /// <summary>Comma-separated, with double-quoted fields ("" = quote).</summary>
    internal static List<string> SplitCsv(string line)
    {
        var res = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); }
            else if (ch == '"') q = true;
            else if (ch == ',') { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res;
    }
}
