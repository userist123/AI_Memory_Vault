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
                        CaseRemovedFromRecent = "case.removed_from_recent", ProfileEdited = "profile.edited", ClassifiedFoldersChanged = "profile.classified_folders_changed",
                        ReportOfficerSigned = "report.officer_signed", OfficerRegistered = "profile.officer_registered", AnchorExported = "audit.anchor_exported",
                        AnchorRecorded = "audit.anchor", OfficerRemoved = "profile.officer_removed",
                        OfficerImportIgnored = "profile.officer_import_ignored", OfficerTrustImported = "officer_trust.imported",
                        ReportGenerated = "report.generated", OfficerRequested = "profile.officer_requested";


    public static string AnchorRecordedDetail(LogAnalyzer.Dfir.Case.AuditAnchorPoint a, string reason) => $"seq={a.Seq}; anchor={a.Code}; chain={a.ChainId}; reason={reason}";

    /// <summary>
    /// "audit.anchor_state": the state that is anchored, written immediately BEFORE the anchor is taken, so it is the anchored entry itself and the paper code
    /// covers it (a rewrite breaks A11). Today: the application install date (UTC) and its basis (check A14).
    /// </summary>
    public const string AnchorState = "audit.anchor_state";

    public static string AnchorStateDetail(DateTimeOffset? installUtc, string installBasis) =>
        $"{LogAnalyzer.Dfir.Case.AnchoredInstallDate.UtcKey}={LogAnalyzer.Dfir.Case.AnchoredInstallDate.Format(installUtc)}; {LogAnalyzer.Dfir.Case.AnchoredInstallDate.BasisKey}={installBasis.Replace(';', ',').ReplaceLineEndings(" ")}";

    public static string OfficerSignedDetail(string reportFile, string originalSha256, string signedSha256, string certFingerprint, string signerCn, string verdict) =>
        $"report={reportFile}; original_sha256={originalSha256}; signed_sha256={signedSha256}; cert_fingerprint={certFingerprint}; signer={signerCn}; verdict={verdict}";

    /// <summary>The SHA-256 of a generated report, kept in the sealed audit: a signed report is accepted only if its original part has one of these hashes.</summary>
    public static string ReportGeneratedDetail(string reportFile, string sha256) => $"report={reportFile}; sha256={sha256}";

    public static string OfficerRegisteredDetail(string fingerprint, string name, string basis, string approvedBy) =>
        $"cert_fingerprint={fingerprint}; name={name}; basis={basis}; approved_by={(approvedBy.Length > 0 ? approvedBy : "(niciunul: primul ofițer, amprenta pe procesul-verbal)")}";

    public static string AnchorExportedDetail(long seq, string code, string logSha256, string folder) =>
        $"seq={seq}; anchor={code}; log_sha256={logSha256}; folder={folder}";

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
