using System.Text.Json;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>Reads and writes the profile file. The file is data the operator entered; nothing in it is ever applied to the system.</summary>
public static class ProfileStore
{
    public const string FileName = "procedure_profile.json";

    /// <summary>%PROGRAMDATA%\LogAnalyzer\profile\procedure_profile.json</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "profile", FileName);

    /// <summary>A missing file is "no profile" (null, no issues); an unreadable or invalid one is reported, never treated as an empty profile.</summary>
    public static ProfileImportResult Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new ProfileImportResult();
        try { return ProfileImport.FromJson(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new ProfileImportResult { Issues = [new ProfileIssue(null, 0, $"profilul nu poate fi citit: {ex.Message}")] }; }
    }

    /// <summary>
    /// Validates and writes. If any row is invalid nothing is written and every error is returned (the operator fixes or removes the rows);
    /// warnings do not block. Returns the issues found; the SHA-256 of the written file is in <paramref name="sha256"/>.
    /// </summary>
    public static List<ProfileIssue> Save(ProcedureProfile p, string? path, out string sha256)
    {
        sha256 = "";
        var issues = ProfileOps.Validate(p);
        if (issues.Any(i => i.IsError)) return issues;
        path ??= DefaultPath;
        p.SchemaVersion = ProcedureProfile.CurrentSchemaVersion;
        p.UpdatedUtc = DateTimeOffset.UtcNow;
        Json.Write(path, p);
        sha256 = Hashing.Sha256File(path);
        return issues;
    }

    public static string ToJson(ProcedureProfile p) => JsonSerializer.Serialize(p, Json.Options);
}
