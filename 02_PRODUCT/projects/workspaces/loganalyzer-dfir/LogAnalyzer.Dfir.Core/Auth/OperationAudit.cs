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

    public const string ControlHashing = "control.hashing";

    /// <summary>
    /// The window in which the application read the classified files to hash them (start / end UTC, process id, image path, account, file count). The next
    /// controls read it back from the sealed audit and list the application's own access records apart (never as someone's access).
    /// </summary>
    public static string ControlHashingDetail(LogAnalyzer.Dfir.Analysis.SelfActivityWindow w) =>
        $"start={w.StartUtc.UtcDateTime:o}; end={w.EndUtc.UtcDateTime:o}; pid={w.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}; files={w.Files.ToString(System.Globalization.CultureInfo.InvariantCulture)}; account={w.Account}; image={w.ImagePath}";

    public static bool TryParseControlHashing(string detail, out LogAnalyzer.Dfir.Analysis.SelfActivityWindow window)
    {
        window = null!;
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        // image is last: a path may contain "; " but no other key follows it
        var img = detail.IndexOf("; image=", StringComparison.Ordinal);
        if (img < 0) return false;
        parts["image"] = detail[(img + 8)..];
        foreach (var kv in detail[..img].Split("; "))
            if (kv.IndexOf('=') is var eq and > 0) parts[kv[..eq]] = kv[(eq + 1)..];
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var rt = System.Globalization.DateTimeStyles.RoundtripKind;
        if (!parts.TryGetValue("start", out var a) || !DateTimeOffset.TryParse(a, ci, rt, out var start)) return false;
        if (!parts.TryGetValue("end", out var b) || !DateTimeOffset.TryParse(b, ci, rt, out var end)) return false;
        if (!parts.TryGetValue("pid", out var p) || !int.TryParse(p, System.Globalization.NumberStyles.Integer, ci, out var pid)) return false;
        int.TryParse(parts.GetValueOrDefault("files", "0"), System.Globalization.NumberStyles.Integer, ci, out var files);
        window = new LogAnalyzer.Dfir.Analysis.SelfActivityWindow(start, end, pid, parts["image"], parts.GetValueOrDefault("account", ""), files);
        return true;
    }

    /// <summary>Every hashing window recorded in a sealed audit file (lines of JSON with "event" and "detail"); unreadable lines are skipped.</summary>
    public static List<LogAnalyzer.Dfir.Analysis.SelfActivityWindow> ReadControlHashing(string auditPath)
    {
        var list = new List<LogAnalyzer.Dfir.Analysis.SelfActivityWindow>();
        if (!File.Exists(auditPath)) return list;
        using var fs = new FileStream(auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
        {
            if (!line.Contains(ControlHashing, StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                string ev = "", detail = "";
                foreach (var pr in doc.RootElement.EnumerateObject())
                {
                    if (pr.Value.ValueKind != JsonValueKind.String) continue;
                    if (pr.Name.Equals("event", StringComparison.OrdinalIgnoreCase)) ev = pr.Value.GetString() ?? "";
                    else if (pr.Name.Equals("detail", StringComparison.OrdinalIgnoreCase)) detail = pr.Value.GetString() ?? "";
                }
                if (ev == ControlHashing && TryParseControlHashing(detail, out var w)) list.Add(w);
            }
            catch (JsonException) { }
        }
        return list;
    }

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
