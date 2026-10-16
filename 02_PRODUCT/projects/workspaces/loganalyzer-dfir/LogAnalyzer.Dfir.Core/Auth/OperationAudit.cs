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


    /// <summary>Signed media register (owner decision 2026-10-16): a version signed on the transfer station, a version installed or refused on a station.</summary>
    public const string MediaRegisterSigned = "media_register.signed", MediaRegisterImported = "media_register.imported", MediaRegisterRefused = "media_register.refused";

    public static string MediaRegisterDetail(int version, string sha256, string verdict, bool installed, string signerFingerprint, string reason) =>
        $"version={version}; sha256={sha256}; verdict={verdict}; installed={(installed ? "da" : "nu")}; signer={signerFingerprint}; reason={reason.Replace(';', ',').ReplaceLineEndings(" ")}";

    /// <summary>Media history across stations (owner decision 2026-10-16): a station's export signed by the officer at collection, and the officer's reconciliation report.</summary>
    public const string MediaHistoryExported = "media_history.exported", MediaHistoryReconciled = "media_history.reconciled";

    public static string MediaHistoryExportedDetail(string file, string sha256, string signerFingerprint, int media, int transfers, long anchorSeq) =>
        $"file={file.Replace(';', ',')}; sha256={sha256}; signer={signerFingerprint}; media={media}; transfers={transfers}; anchor_seq={anchorSeq}";

    public static string MediaHistoryReconciledDetail(string file, string sha256, int accepted, int rejected, int grave, int high) =>
        $"file={file.Replace(';', ',')}; sha256={sha256}; accepted={accepted}; rejected={rejected}; grav={grave}; ridicat={high}";

    public static string AnchorRecordedDetail(LogAnalyzer.Dfir.Case.AuditAnchorPoint a, string reason) => $"seq={a.Seq}; anchor={a.Code}; chain={a.ChainId}; reason={reason}";

    /// <summary>
    /// "audit.anchor_state": the state that is anchored, written immediately BEFORE the anchor is taken, so it is the anchored entry itself and the paper code
    /// covers it (a rewrite breaks A11): the application install date (UTC) and its basis (check A16), followed by the officer store / trust folder state (A13).
    /// </summary>
    public const string AnchorState = "audit.anchor_state";

    public static string AnchorStateDetail(DateTimeOffset? installUtc, string installBasis, LogAnalyzer.Dfir.Case.ChainIdentity? chain = null) =>
        $"{LogAnalyzer.Dfir.Case.AnchoredInstallDate.UtcKey}={LogAnalyzer.Dfir.Case.AnchoredInstallDate.Format(installUtc)}; {LogAnalyzer.Dfir.Case.AnchoredInstallDate.BasisKey}={installBasis.Replace(';', ',').ReplaceLineEndings(" ")}" +
        (chain is null ? "" : $"; chain_first_hash={chain.FirstHash}; chain_first_utc={chain.FirstWhenUtc.Replace(';', ',')}; chain_first_kind={chain.FirstKind.Replace(';', ',')}");

    public static string OfficerSignedDetail(string reportFile, string originalSha256, string signedSha256, string certFingerprint, string signerCn, string verdict) =>
        $"report={reportFile}; original_sha256={originalSha256}; signed_sha256={signedSha256}; cert_fingerprint={certFingerprint}; signer={signerCn}; verdict={verdict}";

    /// <summary>The SHA-256 of a generated report, kept in the sealed audit: a signed report is accepted only if its original part has one of these hashes.</summary>
    public static string ReportGeneratedDetail(string reportFile, string sha256) => $"report={reportFile}; sha256={sha256}";

    public static string OfficerRegisteredDetail(string fingerprint, string name, string basis, string approvedBy) =>
        $"cert_fingerprint={fingerprint}; name={name}; basis={basis}; approved_by={(approvedBy.Length > 0 ? approvedBy : "(niciunul: primul ofițer, amprenta pe procesul-verbal)")}";

    public static string AnchorExportedDetail(long seq, string code, string logSha256, string folder) =>
        $"seq={seq}; anchor={code}; log_sha256={logSha256}; folder={folder}";

    public const string ControlHashing = "control.hashing", ControlFailed = "control.failed";

    /// <summary>
    /// The window in which the application read the classified files to hash them (start / end UTC, process id, image path, account, file count). The next
    /// controls read it back from the sealed audit and list the application's own access records apart (never as someone's access).
    /// </summary>
    public static string ControlHashingDetail(LogAnalyzer.Dfir.Analysis.SelfActivityWindow w) =>
        $"run={w.RunId}; start={w.StartUtc.UtcDateTime:o}; end={w.EndUtc.UtcDateTime:o}; pid={w.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}; files={w.Files.ToString(System.Globalization.CultureInfo.InvariantCulture)}; account={w.Account}; app_sha256={w.AppSha256}; image={w.ImagePath}";

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
        window = new LogAnalyzer.Dfir.Analysis.SelfActivityWindow(start, end, pid, parts["image"], parts.GetValueOrDefault("account", ""), files, parts.GetValueOrDefault("run", ""), parts.GetValueOrDefault("app_sha256", ""));
        return true;
    }

    /// <summary>The longest hashing window accepted: a longer one would hide too much and is reported instead.</summary>
    public static readonly TimeSpan MaxHashingWindow = TimeSpan.FromHours(2);

    /// <summary>Hashing windows read back from the sealed audit: the ones that pass validation, and why the others were refused.</summary>
    public sealed record HashingWindows(List<LogAnalyzer.Dfir.Analysis.SelfActivityWindow> Valid, List<string> Invalid)
    {
        /// <summary>Records of the chain whose time goes back from the previous record by more than the tolerance (a backdated or forged entry).</summary>
        public List<string> Regressions { get; init; } = [];
    }

    /// <summary>
    /// The hashing windows of the sealed audit. A window is accepted only when the chain verifies, it names its control (run id), it lies between the
    /// "control.started" and "control.finished" records of that same control, it was recorded inside that control, it lasts at most
    /// <see cref="MaxHashingWindow"/> and has a process id and an image path. Every refused window is reported ("fereastră de calcul amprente nevalidă").
    /// </summary>
    /// <summary>How far back a record's time may be from the previous record before it counts as a regression (clock adjustments, rounding).</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(2);

    private const string AnchorEvent = "audit.anchor";

    public static HashingWindows ReadControlHashing(string auditPath, bool chainValid)
    {
        var valid = new List<LogAnalyzer.Dfir.Analysis.SelfActivityWindow>();
        var invalid = new List<string>();
        if (!File.Exists(auditPath)) return new(valid, invalid);
        var scan = Scan(auditPath);
        const string Bad = "fereastră de calcul amprente nevalidă";
        for (var k = 0; k < scan.UnreadableHashing; k++) invalid.Add($"{Bad}: înregistrarea din jurnalul de audit nu poate fi citită.");
        foreach (var (w, when, at) in scan.Hashing)
        {
            var id = w.RunId.Length > 0 ? w.RunId : "fără identificator";
            var s0 = scan.Started.TryGetValue(w.RunId, out var st) ? st : default;
            string? why = RunWhy(scan, chainValid, w.RunId, w.AppSha256, at,
                                 "fereastră scrisă de altă versiune a aplicației (amprenta executabilului din fereastră lipsește sau nu este cea înregistrată la începutul controlului ei)")
                ?? (w.EndUtc < w.StartUtc || w.EndUtc - w.StartUtc > MaxHashingWindow ? $"durează mai mult de {MaxHashingWindow.TotalHours:0} ore sau se termină înainte să înceapă"
                : w.StartUtc < s0.When || w.EndUtc > scan.Finished[w.RunId] || when is not { } rec || rec < s0.When || rec > scan.Finished[w.RunId] ? "nu este în interiorul controlului ei"
                : w.ProcessId <= 0 || w.ImagePath.Trim().Length == 0 ? "nu are procesul sau programul aplicației"
                : null);
            if (why is null) valid.Add(w);
            else invalid.Add($"{Bad}: {why} (control {id}, {w.StartUtc.UtcDateTime:yyyy-MM-dd HH:mm}–{w.EndUtc.UtcDateTime:HH:mm} UTC); nu ascunde nicio înregistrare.");
        }
        return new(valid, invalid) { Regressions = scan.Regressions };
    }

    /// <summary>The longest a program started by the application may run and still be recognised as its own (owner rule: at most 10 minutes).</summary>
    public static readonly TimeSpan MaxExecWindow = TimeSpan.FromMinutes(10);

    /// <summary>Programs the application started, read back from the sealed audit: the ones that pass validation and the refused ones with the reason.</summary>
    public sealed record CollectorExecs(List<CollectorExecRecord> Valid, List<(CollectorExecRecord Record, string Why)> Refused)
    {
        public List<string> Unreadable { get; init; } = [];
        public static CollectorExecs Empty => new([], []);
    }

    /// <summary>
    /// The "collector.exec" records of the sealed audit with their "collector.exec_done". A record is accepted only on the same terms as a hashing window: the
    /// chain verifies, no backdated record before it, it names its control (run id), it lies between that control's start and end, it was written by the
    /// executable recorded at the start of that control, the control starts after the anchor before it; and moreover the record has the application's
    /// process id, the executable's full path and SHA-256 (a program of the fixed inventory whose file has that hash now), its end record (child process id) and the program ran at most <see cref="MaxExecWindow"/>.
    /// </summary>
    /// <param name="inventory">The programs a record may name (default <see cref="CollectorExec.Inventory"/>); a record naming any other program is refused.</param>
    /// <param name="currentSha256">SHA-256 of the file now at a path (default <see cref="CollectorExec.FileSha256"/>): it must equal the recorded hash.</param>
    public static CollectorExecs ReadCollectorExecs(string auditPath, bool chainValid, IReadOnlyCollection<string>? inventory = null, Func<string, string>? currentSha256 = null)
    {
        inventory ??= CollectorExec.Inventory();
        currentSha256 ??= CollectorExec.FileSha256;
        var result = CollectorExecs.Empty;
        if (!File.Exists(auditPath)) return result;
        var scan = Scan(auditPath);
        for (var k = 0; k < scan.UnreadableExec; k++) result.Unreadable.Add("o înregistrare „collector.exec” din jurnalul de audit nu poate fi citită");
        foreach (var (r0, at) in scan.Execs)
        {
            var r = r0 with { Index = at };
            if (scan.Done.TryGetValue(r.ExecId, out var d) && d.RunId == r.RunId) r = r with { ChildPid = d.ChildPid, ExitCode = d.ExitCode, EndUtc = d.EndUtc };
            string? why = RunWhy(scan, chainValid, r.RunId, r.AppSha256, at,
                                 "comandă consemnată de altă versiune a aplicației (amprenta executabilului aplicației lipsește sau nu este cea înregistrată la începutul controlului)")
                ?? (r.StartUtc < scan.Started[r.RunId].When || r.StartUtc > scan.Finished[r.RunId] ? "nu este în interiorul controlului ei"
                : r.AppPid <= 0 ? "nu are procesul aplicației"
                : !Path.IsPathFullyQualified(r.Exe) ? "programul nu este dat prin calea lui completă"
                : !IsSha256(r.ExeSha256) ? "nu are amprenta SHA-256 a programului pornit"
                : !CollectorExec.InInventory(r.Exe, inventory) ? "programul nu face parte din inventarul fix al aplicației (instrumentele Windows pe care le pornește, la locul lor)"
                : !currentSha256(r.Exe).Equals(r.ExeSha256, StringComparison.OrdinalIgnoreCase) ? "fișierul programului nu are acum amprenta SHA-256 consemnată"
                : CollectorExec.ScriptHostRefusal(r) is { } scriptWhy ? scriptWhy
                : r.EndUtc is not { } end || r.ChildPid <= 0 ? "nu are înregistrarea de sfârșit (procesul pornit și codul de ieșire)"
                : end < r.StartUtc || end - r.StartUtc > MaxExecWindow ? $"a rulat mai mult de {MaxExecWindow.TotalMinutes:0} minute sau se termină înainte să înceapă"
                : end > scan.Finished[r.RunId] ? "se termină după sfârșitul controlului ei"
                : null);
            if (why is null) result.Valid.Add(r);
            else result.Refused.Add((r, why));
        }
        return result;
    }

    private static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    /// <summary>What a record must satisfy to belong to a valid control run (the shared part of hashing windows and program records); null = it does.</summary>
    private static string? RunWhy(AuditScan scan, bool chainValid, string runId, string appSha256, int at, string otherVersion)
    {
        var s0 = scan.Started.TryGetValue(runId, out var st) ? st : default;
        var anchorBefore = scan.Anchors.Where(a => a.Index < s0.Index).Select(a => (DateTimeOffset?)a.When).LastOrDefault();
        return !chainValid ? "jurnalul de audit nu se verifică (lanț rupt sau sigiliu nevalid)"
            : at > scan.FirstRegression || s0.Index > scan.FirstRegression ? "este după o înregistrare cu oră în urmă în jurnal"
            : runId.Length == 0 ? "nu numește controlul căruia îi aparține"
            : s0.Index == 0 || !scan.Finished.ContainsKey(runId) ? "controlul ei nu are început și sfârșit înregistrate"
            : appSha256.Length != 64 || !appSha256.Equals(scan.StartedSha.GetValueOrDefault(runId, ""), StringComparison.OrdinalIgnoreCase) ? otherVersion
            : anchorBefore is { } ab && s0.When < ab ? $"controlul ei începe ({s0.When.UtcDateTime:yyyy-MM-dd HH:mm} UTC) înaintea ancorării care îl precede în jurnal ({ab.UtcDateTime:yyyy-MM-dd HH:mm} UTC)"
            : null;
    }

    private sealed class AuditScan
    {
        public Dictionary<string, (DateTimeOffset When, int Index)> Started { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> StartedSha { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DateTimeOffset> Finished { get; } = new(StringComparer.Ordinal);
        public List<(int Index, DateTimeOffset When)> Anchors { get; } = [];
        public List<(LogAnalyzer.Dfir.Analysis.SelfActivityWindow W, DateTimeOffset? When, int Index)> Hashing { get; } = [];
        public List<(CollectorExecRecord R, int Index)> Execs { get; } = [];
        public Dictionary<string, (string ExecId, string RunId, int ChildPid, int ExitCode, DateTimeOffset EndUtc)> Done { get; } = new(StringComparer.Ordinal);
        public List<string> Regressions { get; } = [];
        public int UnreadableHashing { get; set; }
        public int UnreadableExec { get; set; }
        public int FirstRegression { get; set; } = int.MaxValue;
    }

    private static AuditScan Scan(string auditPath)
    {
        var scan = new AuditScan();
        int index = 0;
        DateTimeOffset? previous = null;
        using var fs = new FileStream(auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            index++;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                string ev = "", detail = ""; DateTimeOffset? when = null;
                foreach (var pr in doc.RootElement.EnumerateObject())
                {
                    if (pr.Value.ValueKind != JsonValueKind.String) continue;
                    if (pr.Name.Equals("event", StringComparison.OrdinalIgnoreCase)) ev = pr.Value.GetString() ?? "";
                    else if (pr.Name.Equals("detail", StringComparison.OrdinalIgnoreCase)) detail = pr.Value.GetString() ?? "";
                    else if (pr.Name.Equals("whenUtc", StringComparison.OrdinalIgnoreCase) && pr.Value.TryGetDateTimeOffset(out var t)) when = t;
                }
                // the chain's times never go back: a record earlier than its predecessor (beyond the tolerance) is backdated or forged
                if (when is { } w0)
                {
                    if (previous is { } p0 && w0 < p0 - ClockTolerance)
                    {
                        scan.Regressions.Add($"înregistrări cu oră în urmă în jurnalul aplicației: înregistrarea {index} ({ev}) are ora {w0.UtcDateTime:yyyy-MM-dd HH:mm} UTC, înaintea celei precedente ({p0.UtcDateTime:yyyy-MM-dd HH:mm} UTC); ferestrele de calcul al amprentelor de după ea nu sunt folosite");
                        scan.FirstRegression = Math.Min(scan.FirstRegression, index);
                    }
                    previous = previous is { } pm && pm > w0 ? pm : w0;
                }
                var run = RunOf(detail);
                if (ev == AnchorEvent && when is { } wa) scan.Anchors.Add((index, wa));
                else if (ev == ControlStarted && run.Length > 0 && when is { } ws) { scan.Started[run] = (ws, index); scan.StartedSha[run] = TokenOf(detail, "app_sha256="); }
                else if (ev == ControlFinished && run.Length > 0 && when is { } wf) scan.Finished[run] = wf;
                else if (ev == ControlHashing)
                {
                    if (TryParseControlHashing(detail, out var w)) scan.Hashing.Add((w, when, index));
                    else scan.UnreadableHashing++;
                }
                else if (ev == CollectorExec.Exec)
                {
                    if (when is { } we && CollectorExec.TryParseExec(detail, we, out var r)) scan.Execs.Add((r, index));
                    else scan.UnreadableExec++;
                }
                // the first end record of a program counts; a later one with the same id is ignored
                else if (ev == CollectorExec.ExecDone && CollectorExec.TryParseDone(detail, out var d) && !scan.Done.ContainsKey(d.ExecId)) scan.Done[d.ExecId] = d;
            }
            catch (JsonException) { }
        }
        return scan;
    }

    private static string TokenOf(string detail, string prefix)
    {
        foreach (var kv in detail.Split("; "))
            if (kv.StartsWith(prefix, StringComparison.Ordinal)) return kv[prefix.Length..].Trim();
        return "";
    }

    private static string RunOf(string detail)
    {
        foreach (var kv in detail.Split("; "))
            if (kv.StartsWith("run=", StringComparison.Ordinal)) return kv[4..].Trim();
        return "";
    }

    public static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string ControlStartedDetail(string edition, string posture, bool elevated, int periodDays, int classifiedFolders, string runId = "", string appSha256 = "") =>
        $"edition={edition}; posture={posture}; elevated={(elevated ? "da" : "nu")}; scope=perioada {periodDays} zile, {classifiedFolders} foldere clasificate" + (runId.Length > 0 ? $"; run={runId}" : "") + (appSha256.Length > 0 ? $"; app_sha256={appSha256}" : "");

    public static string ControlFinishedDetail(string host, bool elevated, int checks, int neconform, int deVerificat, int nedeterminat, string runId = "") =>
        $"host={host}; elevated={(elevated ? "da" : "nu")}; checks={checks}; neconform={neconform}; de_verificat={deVerificat}; nedeterminat={nedeterminat}" + (runId.Length > 0 ? $"; run={runId}" : "");

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

    /// <summary>Audit-first export of several files into one folder: the intent lists every file that will be written (review of PR #298).</summary>
    public static string ExportBundleIntentDetail(string caseId, string format, string folder, IReadOnlyList<string> files) =>
        $"case={(caseId.Length > 0 ? caseId : "(fără caz)")}; format={format}; items={files.Count}; folder={folder}; files={string.Join(" | ", files)}; phase=intent";

    /// <summary>Audit-first export of several files: one record per written file, with its SHA-256.</summary>
    public static string ExportBundleFileDetail(string caseId, string format, string folder, string file) =>
        ExportUnmaskedDetail(caseId, format, 1, file) + $"; folder={folder}; file={Path.GetRelativePath(folder, file)}; phase=written";

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
