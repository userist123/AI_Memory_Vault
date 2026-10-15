using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// Event names and detail lines of the operator operations written to the sealed auth audit (<see cref="AuthService.AuditOperation"/>).
/// Details carry facts only: ids, counts and SHA-256 values, never the content of a case, a report or a profile.
/// </summary>
public static class OperationAudit
{
    public const string ControlStarted = "control.started", ControlFinished = "control.finished", ExportUnmasked = "export.unmasked",
                        CaseRemovedFromRecent = "case.removed_from_recent", ProfileEdited = "profile.edited", ClassifiedFoldersChanged = "profile.classified_folders_changed";

    public static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string ControlStartedDetail(string edition, string posture, bool elevated, int periodDays, int classifiedFolders) =>
        $"edition={edition}; posture={posture}; elevated={(elevated ? "da" : "nu")}; scope=perioada {periodDays} zile, {classifiedFolders} foldere clasificate";

    public static string ControlFinishedDetail(string host, bool elevated, int checks, int neconform, int deVerificat, int nedeterminat) =>
        $"host={host}; elevated={(elevated ? "da" : "nu")}; checks={checks}; neconform={neconform}; de_verificat={deVerificat}; nedeterminat={nedeterminat}";

    /// <summary>Detail of an export that is NOT masked; the hash is of the written file (empty when it cannot be read).</summary>
    public static string ExportUnmaskedDetail(string caseId, string format, int items, string outputPath)
    {
        string sha = "";
        try { if (File.Exists(outputPath)) using (var fs = File.OpenRead(outputPath)) sha = Convert.ToHexStringLower(SHA256.HashData(fs)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return $"case={(caseId.Length > 0 ? caseId : "(fără caz)")}; format={format}; items={items}; sha256={sha}";
    }

    /// <summary>Audit-first export: the intent, written before the file (the export is refused when this cannot be written).</summary>
    public static string ExportIntentDetail(string caseId, string format, int items) =>
        $"case={(caseId.Length > 0 ? caseId : "(fără caz)")}; format={format}; items={items}; phase=intent";

    /// <summary>Audit-first export: the outcome after the file was written, with the SHA-256 of the written file.</summary>
    public static string ExportWrittenDetail(string caseId, string format, int items, string outputPath) =>
        ExportUnmaskedDetail(caseId, format, items, outputPath) + "; phase=written";

    /// <summary>Audit-first export: the write failed after the intent was recorded.</summary>
    public static string ExportFailedDetail(string caseId, string format, int items, string error) =>
        $"case={(caseId.Length > 0 ? caseId : "(fără caz)")}; format={format}; items={items}; phase=failed; error={error}";

    /// <summary>A case taken off the recent-cases list; the case folder is not touched (nothing is deleted).</summary>
    public static string CaseRemovedFromRecentDetail(string caseId, string mode) => $"case={caseId}; mode={mode}";

    public static string ProfileHash(ProcedureProfile p)
    {
        var json = ProfileStore.ToJson(Normalized(p));
        return Sha256Hex(json);
    }

    public static string ProfileEditedDetail(string beforeSha, string afterSha, string target) => $"before_sha256={beforeSha}; after_sha256={afterSha}; target={target}";

    public static string FoldersHash(IEnumerable<string> paths) =>
        Sha256Hex(string.Join("\n", paths.Select(x => x.Trim()).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));

    public static string FoldersChangedDetail(IEnumerable<string> before, IEnumerable<string> after) =>
        $"old_sha256={FoldersHash(before)}; new_sha256={FoldersHash(after)}; old_count={before.Count()}; new_count={after.Count()}";

    /// <summary>
    /// After a successful profile save: appends profile.edited (before/after hash) when the content changed, and
    /// profile.classified_folders_changed (old/new list hash) when the classified-folder table changed. Returns the events written.
    /// </summary>
    public static IReadOnlyList<string> AuditProfileSave(AuthService svc, AuthSession? session, ProcedureProfile? before, ProcedureProfile after, string target)
    {
        var written = new List<string>();
        var b = before ?? new ProcedureProfile();
        string bh = before is null ? "(none)" : ProfileHash(b), ah = ProfileHash(after);
        if (bh != ah) { svc.AuditOperation(session, ProfileEdited, ProfileEditedDetail(bh, ah, target)); written.Add(ProfileEdited); }
        var oldF = b.ClassifiedFolders.Select(x => x.Path).ToList();
        var newF = after.ClassifiedFolders.Select(x => x.Path).ToList();
        if (FoldersHash(oldF) != FoldersHash(newF)) { svc.AuditOperation(session, ClassifiedFoldersChanged, FoldersChangedDetail(oldF, newF)); written.Add(ClassifiedFoldersChanged); }
        return written;
    }

    /// <summary>The profile without the volatile timestamp, so "before" and "after" differ only when content differs.</summary>
    private static ProcedureProfile Normalized(ProcedureProfile p)
    {
        var copy = JsonSerializer.Deserialize<ProcedureProfile>(ProfileStore.ToJson(p), LogAnalyzer.Dfir.IO.Json.Options) ?? new ProcedureProfile();
        copy.UpdatedUtc = null;
        return copy;
    }
}
