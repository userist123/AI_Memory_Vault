using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Audit-first for every export that is not masked (review 2026-10-15): the intent (format, item count) is written to the sealed audit BEFORE the
    /// file; when it cannot be written the export is refused and nothing is written. After the file exists its outcome (SHA-256 of the written file)
    /// is appended; a failed write is appended as failed. So a successful export always has an audit entry.
    /// </summary>
    public static class ExportAudit
    {
        public const string RefusedPrefix = "Exportul nu s-a făcut: jurnalul de audit nu poate fi scris — ";

        /// <summary>The message shown when the audit could not be written before an export.</summary>
        public static string Refused(string reason) => RefusedPrefix + reason;

        /// <summary>
        /// Records the intent, runs <paramref name="write"/> and records the outcome. Returns null when the export was done, otherwise the refusal
        /// message (the audit could not be written first; <paramref name="write"/> was not called). An exception of <paramref name="write"/> propagates.
        /// </summary>
        public static string? Run(string caseId, string format, int items, string path, Action write)
        {
            if (AuthApp.TryRecord(OperationAudit.ExportUnmasked, OperationAudit.ExportIntentDetail(caseId, format, items)) is { } error)
                return Refused(error);
            try { write(); }
            catch (Exception ex)
            {
                AuthApp.Record(OperationAudit.ExportUnmasked, OperationAudit.ExportFailedDetail(caseId, format, items, ex.GetType().Name));
                throw;
            }
            AuthApp.Record(OperationAudit.ExportUnmasked, OperationAudit.ExportWrittenDetail(caseId, format, items, path));
            return null;
        }

        /// <summary>
        /// Several files into one folder (review of PR #298: „Exportă pentru ofițer”, the CD-R copy of the log, the signed media history): the intent lists every
        /// planned file and is written BEFORE anything; when it cannot be written the whole export is refused and <paramref name="write"/> is not called.
        /// <paramref name="write"/> returns the files it wrote; each is recorded with its SHA-256 (files already in a chosen folder are neither touched nor listed).
        /// A failed write is recorded as failed and rethrown.
        /// </summary>
        public static string? RunBundle(string caseId, string format, string folder, System.Collections.Generic.IReadOnlyList<string> plannedFiles,
            Func<System.Collections.Generic.IEnumerable<string>> write)
        {
            if (AuthApp.TryRecord(OperationAudit.ExportUnmasked, OperationAudit.ExportBundleIntentDetail(caseId, format, folder, plannedFiles)) is { } error)
                return Refused(error);
            System.Collections.Generic.List<string> written;
            try { written = write().ToList(); }
            catch (Exception ex)
            {
                AuthApp.Record(OperationAudit.ExportUnmasked, OperationAudit.ExportFailedDetail(caseId, format, plannedFiles.Count, ex.GetType().Name));
                throw;
            }
            foreach (var f in written.Where(System.IO.File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
                AuthApp.Record(OperationAudit.ExportUnmasked, OperationAudit.ExportBundleFileDetail(caseId, format, folder, f));
            return null;
        }

        /// <summary>The case id used for the exports of the main window: the folder name of the live case.</summary>
        public static string LiveCaseId => System.IO.Path.GetFileName(LiveCase.Root.TrimEnd('\\', '/'));
    }

    /// <summary>
    /// A failure to write the sealed audit, kept visible until the application is closed: the main window shows it as a banner and in the status line
    /// (review 2026-10-15: the result of the audit write was ignored by every caller).
    /// </summary>
    public sealed partial class AuditHealth : ObservableObject
    {
        public static AuditHealth Instance { get; } = new();

        [ObservableProperty] private string _warning = "";
        [ObservableProperty] private int _failures;

        public bool HasWarning => Failures > 0;

        public void Report(string ev, string error)
        {
            Failures++;
            Warning = $"Jurnalul de audit nu a putut fi scris ({ev}): {error}. Operația s-a făcut, dar nu apare în jurnal ({Failures} " +
                      (Failures == 1 ? "eșec" : "eșecuri") + " de la pornire). Anunțați administratorul; un export nemascat este refuzat cât timp jurnalul nu poate fi scris.";
            OnPropertyChanged(nameof(HasWarning));
        }
    }
}
