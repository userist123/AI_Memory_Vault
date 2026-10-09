using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>Result of an import / paste / load: what was accepted and every problem found, per line. Nothing is dropped silently.</summary>
public sealed class RegisterImportResult<T> where T : class, IRegisterData
{
    public T? Register { get; init; }
    public int RowsAccepted { get; init; }
    public int RowsRejected { get; init; }
    public List<RegisterIssue> Issues { get; init; } = [];
    public bool HasErrors => Issues.Any(i => i.IsError);
}

public static class RegisterImport
{
    /// <summary>The register's own JSON format. A newer major version is refused; unparseable JSON is one error, not an empty register.</summary>
    public static RegisterImportResult<T> FromJson<T>(string json) where T : class, IRegisterData, new()
    {
        var title = new T().Title;
        T? r;
        try { r = JsonSerializer.Deserialize<T>(json, Json.Options); }
        catch (JsonException ex) { return Failed($"JSON invalid: {ex.Message}"); }
        if (r is null) return Failed("JSON gol");
        if (SchemaVersions.Major(r.SchemaVersion) != 1) return Failed($"schema_version „{r.SchemaVersion}” nu este suportată (registru 1.x)");
        var issues = r.Validate();
        int rows = r.Cells().Count;
        int bad = issues.Where(i => i.IsError && i.Line > 0).Select(i => i.Line).Distinct().Count();
        return new RegisterImportResult<T> { Register = r, RowsAccepted = rows - bad, RowsRejected = bad, Issues = issues };

        RegisterImportResult<T> Failed(string msg) => new() { Issues = [new RegisterIssue(title, 0, msg)] };
    }

    /// <summary>
    /// CSV or tab-separated text (clipboard paste from Excel / a table), added to <paramref name="target"/>. A header row with the column names is optional
    /// (columns are then matched by name); without one the columns are positional. Valid rows are appended; every invalid row is listed with its line number
    /// and is NOT added.
    /// </summary>
    public static RegisterImportResult<T> Text<T>(T target, string text, bool replace = false) where T : class, IRegisterData
    {
        var issues = new List<RegisterIssue>();
        var lines = ProfileImport.SplitRecords(text, out _);
        var cols = target.Columns;
        var map = Enumerable.Range(0, cols.Count).ToArray();
        int first = 0;
        if (lines.Count > 0)
        {
            var header = lines[0].Fields;
            var byName = cols.Select(c => header.FindIndex(h => h.Trim().Equals(c, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (byName.Count(i => i >= 0) >= Math.Max(1, cols.Count / 2) && byName[0] >= 0 || byName.Count(i => i >= 0) == cols.Count) { map = byName; first = 1; }
        }
        var existing = replace ? [] : target.Cells();
        var accepted = new List<string[]>();
        int rejected = 0;
        for (int i = first; i < lines.Count; i++)
        {
            var (lineNo, f) = lines[i];
            if (f.All(string.IsNullOrWhiteSpace)) continue;
            var cells = map.Select(ix => ix >= 0 && ix < f.Count ? f[ix].Trim() : "").ToArray();
            if (f.Count > cols.Count && map.SequenceEqual(Enumerable.Range(0, cols.Count)))
                issues.Add(new RegisterIssue(target.Title, lineNo, $"{f.Count} câmpuri în loc de {cols.Count}; câmpurile în plus ({string.Join(" | ", f.Skip(cols.Count))}) nu sunt folosite", false));
            var problems = target.ValidateRow(cells, existing.Concat(accepted).ToList());
            foreach (var (msg, err) in problems) issues.Add(new RegisterIssue(target.Title, lineNo, msg, err));
            if (problems.Any(x => x.IsError)) { rejected++; continue; }
            accepted.Add(cells);
        }
        target.SetCells(existing.Concat(accepted));
        return new RegisterImportResult<T> { Register = target, RowsAccepted = accepted.Count, RowsRejected = rejected, Issues = issues };
    }
}

/// <summary>One line of the register audit: who changed which register, when, from what to what.</summary>
public sealed record RegisterAuditEntry(string Register, string Action, string Who, string WhoSource, DateTimeOffset WhenUtc,
    string BeforeSha256, string AfterSha256, List<string[]> Before, List<string[]> After);

/// <summary>
/// Reads and writes a register file (data the operator entered; never applied to the system) and keeps an append-only, hash-chained register audit
/// (<c>register_audit.jsonl</c>, the same <see cref="HashChain"/> as the case custody) next to it.
/// </summary>
public static class RegisterStore
{
    public const string AuditFileName = "register_audit.jsonl";
    public const string WhoSourceNote = "cont de sistem de operare; aplicația nu are autentificare, deci identitatea este neautentificată";

    /// <summary>%PROGRAMDATA%\LogAnalyzer\registers</summary>
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "registers");
    public static string DefaultPath<T>() where T : IRegisterData, new() => Path.Combine(DefaultDir, new T().FileName);
    public static string AuditPathFor(string registerPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(registerPath))!, AuditFileName);
    public static ChainVerification VerifyAudit(string registerPath) => new HashChain(AuditPathFor(registerPath)).Verify();

    /// <summary>A missing file is "no register" (null, no issues); an unreadable or invalid one is reported, never treated as an empty register.</summary>
    public static RegisterImportResult<T> Load<T>(string? path = null) where T : class, IRegisterData, new()
    {
        path ??= DefaultPath<T>();
        if (!File.Exists(path)) return new RegisterImportResult<T>();
        try { return RegisterImport.FromJson<T>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new RegisterImportResult<T> { Issues = [new RegisterIssue(new T().Title, 0, $"registrul nu poate fi citit: {ex.Message}")] }; }
    }

    /// <summary>
    /// Validates and writes. If any row is invalid nothing is written (and nothing is audited) and every error is returned. A change goes to the audit chain
    /// with who/when/before/after; saving an unchanged register adds no audit line. <paramref name="sha256"/> is the SHA-256 of the file.
    /// </summary>
    public static List<RegisterIssue> Save<T>(T register, string? path, out string sha256, string? who = null, string action = "save") where T : class, IRegisterData, new()
    {
        sha256 = "";
        var issues = register.Validate();
        if (issues.Any(i => i.IsError)) return issues;
        path ??= DefaultPath<T>();
        var before = File.Exists(path) ? Load<T>(path).Register?.Cells() ?? [] : [];
        var beforeSha = File.Exists(path) ? Hashing.Sha256File(path) : "";
        var after = register.Cells();
        if (beforeSha.Length > 0 && SameCells(before, after)) { sha256 = beforeSha; return issues; }
        register.SchemaVersion = "1.0";
        register.UpdatedUtc = DateTimeOffset.UtcNow;
        Json.Write(path, register);
        sha256 = Hashing.Sha256File(path);
        new HashChain(AuditPathFor(path)).Append(new RegisterAuditEntry(register.Kind, action, who ?? $"{Environment.UserDomainName}\\{Environment.UserName}", WhoSourceNote,
            DateTimeOffset.UtcNow, beforeSha, sha256, before, after));
        return issues;
    }

    private static bool SameCells(List<string[]> a, List<string[]> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second, StringComparer.Ordinal));
}
