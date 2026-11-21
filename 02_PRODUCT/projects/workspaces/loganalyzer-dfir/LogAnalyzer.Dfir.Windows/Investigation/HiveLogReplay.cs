using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Planner #13 C1: for every registry hive of the case that has its .LOG1/.LOG2 from the SAME acquisition, replays the transaction logs onto a COPY of the hive
/// (<see cref="RegfLogReplay"/>) and registers the copy as derived evidence (action "derived", parent = the hive's EvidenceId, own SHA-256, notes = logs
/// and entries applied). The original is never touched: it stays in the case unchanged and is reported as superseded by the copy; the parsers read the copy.
/// A hive that is not dirty is left alone. If the replay cannot be done (invalid log, stopped at the first entry, ambiguous pairing, copy not writable or not
/// registrable) the original is parsed as before and the exact reason is recorded as a gap: no silent fallback.
/// Collection: only Amcache.hve is collected together with its logs; the SYSTEM / SOFTWARE hives are saved with reg save (already consistent) and the user hives
/// without logs, so they pass through here untouched unless a case holds their logs (e.g. an imported collection or a remote package).
/// </summary>
public static class HiveLogReplay
{
    public const string Collector = "RegfLogReplay";
    public const string Version = "1.0";

    /// <summary>EvidenceId of an original hive → the derived copy that replaces it for parsing.</summary>
    public static IReadOnlyDictionary<string, EvidenceItem> Apply(CaseWorkspace ws, InvestigationResult r) => Apply(ws, r, out _);

    /// <param name="rejectedCopies">Main-session review 2026-11-20: derived copies that must NOT be parsed (their original or the copy itself changed after
    /// the replay); the pipeline reports them instead of reading them, so no finding cites a copy of an original that is no longer the acquired one.</param>
    public static IReadOnlyDictionary<string, EvidenceItem> Apply(CaseWorkspace ws, InvestigationResult r, out IReadOnlySet<string> rejectedCopies)
    {
        var superseded = new Dictionary<string, EvidenceItem>(StringComparer.Ordinal);
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        rejectedCopies = rejected;
        var all = ws.LoadEvidence();
        foreach (var hive in all)
        {
            // only the copies made here are skipped; a hive imported with a parent (remote package manifest) is a normal original
            if (hive.Collector == Collector || hive.AcquisitionMethod == "derived" || IsLogName(hive.StoredPath)) continue;
            if (!ws.TryFullPath(hive.StoredPath, out var hivePath) || !File.Exists(hivePath)) continue;
            var existing = all.FirstOrDefault(d => d.Collector == Collector && d.ParentEvidenceId == hive.EvidenceId);
            if (existing is not null)
            {
                // an earlier run already derived it: the original and the copy must still match their acquisition hashes before the copy replaces the original
                var preO = EvidencePreflight.Check(hive, hivePath);
                var preD = ws.TryFullPath(existing.StoredPath, out var dp) ? EvidencePreflight.Check(existing, dp) : null;
                if (!preO.CanParse || preD is null || !preD.CanParse)
                {
                    var bad = !preO.CanParse ? $"originalul {hive.EvidenceId}: {preO.Code}: {preO.Detail}"
                        : preD is null ? $"copia {existing.EvidenceId}: {CaseWorkspace.OutsideCaseReason}" : $"copia derivată {existing.EvidenceId}: {preD.Code}: {preD.Detail}";
                    ws.Audit("evidence.mutated", $"{hive.EvidenceId} hive replay rerun {bad}");
                    r.Gaps.Add(new EvidenceGap(Path.GetFileName(hive.StoredPath), EvidenceStatus.Failed, $"EVIDENCE_MUTATED: proba s-a schimbat după reaplicarea jurnalelor ({bad}); copia derivată nu este folosită",
                        "Rezultatele din acest hive nu pot fi atribuite probei achiziționate", "Reachiziție din sursa originală", "Doar prin reachiziție"));
                    rejected.Add(existing.EvidenceId);   // the copy is not read either: it no longer stands for the acquired original
                    continue;   // the original is not marked as superseded: the parse loop reports its own preflight
                }
                superseded[hive.EvidenceId] = existing;
                continue;
            }
            if (!HasRegfSignature(hivePath)) continue;
            var (logItems, ambiguity) = FindLogs(all, hive);
            if (ambiguity is not null) { Gap(r, ws, hive, ambiguity); continue; }
            if (logItems.Count == 0) continue;

            var name = Path.GetFileName(hive.StoredPath);
            string? dest = null;
            bool written = false;
            EvidenceItem? item = null;
            try
            {
                if (!EvidencePreflight.Check(hive, hivePath).CanParse) continue;   // the parse loop reports the hive itself
                var logPaths = new List<string>();
                string? logProblem = null;
                foreach (var l in logItems)
                {
                    if (!ws.TryFullPath(l.StoredPath, out var lp)) { logProblem = $"{l.EvidenceId}: {CaseWorkspace.OutsideCaseReason}"; break; }
                    var pre = EvidencePreflight.Check(l, lp);
                    if (!pre.CanParse) { logProblem = $"{l.EvidenceId} ({Path.GetFileName(l.StoredPath)}): {pre.Code}: {pre.Detail}"; break; }
                    logPaths.Add(lp);
                }
                if (logProblem is not null) { Gap(r, ws, hive, $"jurnalul nu a trecut verificarea de integritate: {logProblem}"); continue; }

                dest = DerivedPath(hivePath);
                var res = RegfLogReplay.Replay(hivePath, logPaths, dest);
                if (res.Outcome == HiveReplayOutcome.NotDirty) continue;
                if (res.Outcome == HiveReplayOutcome.Failed) { Gap(r, ws, hive, res.Reason + (res.Notes.Count > 0 ? " | " + string.Join("; ", res.Notes) : "")); continue; }
                written = true;

                var notes = $"Copie derivată a {hive.EvidenceId} ({name}) cu jurnalele de tranzacții aplicate (RegfLogReplay {Version}, specificația regf M. Suhanov): "
                    + $"{res.Reason}; jurnale folosite: {string.Join(", ", logItems.Select(l => $"{Path.GetFileName(l.StoredPath)} ({l.EvidenceId})"))}"
                    + (res.Notes.Count > 0 ? " | " + string.Join("; ", res.Notes) : "")
                    + $". Originalul {hive.EvidenceId} nu a fost modificat (SHA-256 {hive.Sha256}).";
                item = ws.RegisterStored(dest, hive.StoredPath, hive.Source, hive.SourceType, hive.TemporalType, Collector, Version, "derived",
                    hive.Sensitivity, hive.EvidenceId, notes, timezone: hive.Timezone, acquiredAtUtc: hive.AcquiredAtUtc);
                superseded[hive.EvidenceId] = item;
                ws.Audit("hive.log_replayed", $"{hive.EvidenceId} -> {item.EvidenceId} sha256={item.Sha256} {res.Reason}");
                // a gap only when an invalid entry BROKE the chain (the next expected sequence is the rejected one); stale bytes after the last valid entry are only noted
                if (res.StoppedOnInvalidEntry)
                    r.Gaps.Add(new EvidenceGap(name, EvidenceStatus.Partial, $"reaplicarea jurnalelor s-a oprit la o intrare respinsă: {string.Join("; ", res.Notes.Where(n => n.Contains("respins", StringComparison.Ordinal)))}",
                        "Modificările din jurnal de după intrarea respinsă nu sunt în copia derivată", "Colectarea din nou a hive-ului împreună cu jurnalele", "Posibil"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OutOfMemoryException)
            {
                // a copy that was written but not registered is a derived file nobody can cite: remove it (never the original) so a rerun does not pile up .replayedN files
                if (written && item is null && dest is not null) TryDelete(dest);
                superseded.Remove(hive.EvidenceId);
                Gap(r, ws, hive, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
        return superseded;
    }

    private static void Gap(InvestigationResult r, CaseWorkspace ws, EvidenceItem hive, string reason)
    {
        try { ws.Audit("hive.log_replay_failed", $"{hive.EvidenceId} {reason}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { /* the gap below still tells the operator */ }
        r.Gaps.Add(new EvidenceGap(Path.GetFileName(hive.StoredPath), EvidenceStatus.Partial, $"jurnalele de tranzacții nu au putut fi aplicate: {reason}; hive-ul original este parsat ca atare",
            "Intrările scrise doar în jurnal (cele mai recente) pot lipsi sau pot fi fără valori", "Colectarea din nou a hive-ului împreună cu jurnalele", "Posibil"));
    }

    private static void TryDelete(string path)
    {
        try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the gap recorded by the caller names the failure; a leftover derived file is not evidence */ }
    }

    private static bool IsLogName(string path) =>
        Path.GetFileName(path).EndsWith(".LOG1", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).EndsWith(".LOG2", StringComparison.OrdinalIgnoreCase);

    private static bool Suffix(string a, string b) =>
        string.Equals(a, b + ".LOG1", StringComparison.OrdinalIgnoreCase) || string.Equals(a, b + ".LOG2", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The logs of a hive WITHIN THE SAME ACQUISITION: the same parent manifest (or none) and, first, the same case folder with the name + .LOG1/.LOG2; else the same
    /// original path + .LOG1/.LOG2 (an import puts a typed hive and its log in different folders). When the original path does not name exactly one hive and at most one
    /// log per suffix (e.g. two imports of the same staging folder), the pairing is ambiguous: no replay, the reason is returned.
    /// </summary>
    private static (List<EvidenceItem> Logs, string? Ambiguity) FindLogs(IReadOnlyList<EvidenceItem> all, EvidenceItem hive)
    {
        var sameAcq = all.Where(l => l.EvidenceId != hive.EvidenceId && IsLogName(l.StoredPath) && l.ParentEvidenceId == hive.ParentEvidenceId).ToList();
        var folder = sameAcq.Where(l => string.Equals(Path.GetDirectoryName(l.StoredPath) ?? "", Path.GetDirectoryName(hive.StoredPath) ?? "", StringComparison.OrdinalIgnoreCase)
            && Suffix(Path.GetFileName(l.StoredPath), Path.GetFileName(hive.StoredPath))).ToList();
        if (folder.Count > 0)
            return folder.Select(l => Path.GetExtension(l.StoredPath).ToUpperInvariant()).Distinct().Count() == folder.Count ? (folder, null)
                : ([], $"asociere ambiguă: mai multe jurnale cu același nume în folderul hive-ului ({string.Join(", ", folder.Select(l => l.EvidenceId))})");
        if (hive.OriginalPath.Length == 0) return ([], null);
        var byPath = sameAcq.Where(l => l.OriginalPath.Length > 0 && Suffix(l.OriginalPath, hive.OriginalPath)).ToList();
        if (byPath.Count == 0) return ([], null);
        var sameOriginalHives = all.Where(h => h.EvidenceId != hive.EvidenceId && !IsLogName(h.StoredPath) && h.AcquisitionMethod != "derived" && h.Collector != Collector
            && h.ParentEvidenceId == hive.ParentEvidenceId && string.Equals(h.OriginalPath, hive.OriginalPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sameOriginalHives.Count > 0 || byPath.Select(l => Path.GetExtension(l.OriginalPath).ToUpperInvariant()).Distinct().Count() != byPath.Count)
            return ([], $"asociere ambiguă între jurnale și hive: aceeași cale de origine ({hive.OriginalPath}) pentru mai multe probe ({string.Join(", ", sameOriginalHives.Select(h => h.EvidenceId).Append(hive.EvidenceId).Concat(byPath.Select(l => l.EvidenceId)))}); nu se aplică niciun jurnal");
        return (byPath, null);
    }

    private static bool HasRegfSignature(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var b = new byte[4];
            return fs.Read(b, 0, 4) == 4 && b[0] == 'r' && b[1] == 'e' && b[2] == 'g' && b[3] == 'f';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>"Amcache.hve" → "Amcache.replayed.hve" beside the original (inside the case); a numeric suffix if the name is taken.</summary>
    private static string DerivedPath(string hivePath)
    {
        var dir = Path.GetDirectoryName(hivePath)!;
        var stem = Path.GetFileNameWithoutExtension(hivePath); var ext = Path.GetExtension(hivePath);
        var p = Path.Combine(dir, stem + ".replayed" + ext);
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{stem}.replayed{i}{ext}");
        return p;
    }
}
