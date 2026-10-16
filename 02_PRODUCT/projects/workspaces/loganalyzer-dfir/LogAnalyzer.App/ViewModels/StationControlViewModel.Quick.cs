using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Flow;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// What the Home page needs to run a station control in one click and to offer the three follow-ups in the same panel (usability 2026-10-16).
    /// Implemented by <see cref="StationControlViewModel"/>; tests give a fake.
    /// </summary>
    public interface IStationQuickControl : INotifyPropertyChanged
    {
        bool IsBusy { get; }
        string Status { get; }
        string Summary { get; }
        bool HasResult { get; }
        string AnchorLine { get; }
        /// <summary>The results of the last control as chips (CONFORM 12, NECONFORM 2…): each word has its explanation as tooltip.</summary>
        System.Collections.Generic.IReadOnlyList<StatusCount> ResultCounts { get; }
        /// <summary>The anchor code typed from the previous proces-verbal (empty at the first control).</summary>
        string PreviousAnchorCode { get; set; }
        /// <summary>Runs the control with the defaults: since the last anchored control, or the last 90 days when there is none.</summary>
        Task RunQuickAsync();
        /// <summary>Saves the report in the case (once) and opens it.</summary>
        void OpenReport();
        /// <summary>The officer signs the report with the card (the PIN is asked by Windows).</summary>
        void SignAsOfficer();
        /// <summary>Report, log copy for the CD-R and media history in one folder (the folder used last time, asked only the first time).</summary>
        void ExportForOfficer();
    }

    /// <summary>One result chip: the result word as printed (CONFORM, DE VERIFICAT…) and how many checks have it.</summary>
    public sealed record StatusCount(string Label, int Count);

    public partial class StationControlViewModel : IStationQuickControl
    {
        /// <summary>The result chips of the last control, worst first; empty before the first control.</summary>
        public System.Collections.Generic.IReadOnlyList<StatusCount> ResultCounts =>
            _report is null ? [] : new[] { ControlStatus.Neconform, ControlStatus.DeVerificat, ControlStatus.Nedeterminat, ControlStatus.Conform, ControlStatus.NuSeAplica }
                .Select(st => new StatusCount(ControlReportPdf.StatusText(st), _report.Count(st))).Where(c => c.Count > 0 || c.Label != ControlReportPdf.StatusText(ControlStatus.NuSeAplica)).ToList();

        partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(ResultCounts));

        /// <summary>
        /// „Fă controlul stației” (Home): the guided flow with its defaults, without the three „Înainte” clicks. The case scope is still asked when it is missing
        /// (it is the authorisation of the control), the period is „de la ultimul control” (90 days when there is none) and the procedures are the profile's.
        /// </summary>
        public async Task RunQuickAsync()
        {
            if (IsBusy) return;
            _flow.Restart(); Result = null; Comparison = null; ComparisonChanges.Clear();
            if (!LiveCase.EnsureScopeAtStart() && LiveCase.ScopePrompt is not null)
                Status = "Scopul cazului nu a fost completat; veți fi întrebat din nou la salvarea raportului.";
            if (PeriodChoice == ControlPeriodChoice.Custom && CustomDays <= 0) PeriodChoice = ControlPeriodChoice.SinceLastControl;
            LoadProfileSections();
            // step through the flow with its validations (the period is computed by ValidatePeriod), stopping at the first refusal
            while (!_flow.Completed)
                if (!_flow.Next()) { RaiseFlow(); Status = "Controlul nu a pornit: " + _flow.LastError; return; }
            RaiseFlow();
            await Run();
            BuildResult();
            RaiseFlow();
            OnPropertyChanged(nameof(AnchorLine));
        }

        [RelayCommand] private Task RunQuick() => RunQuickAsync();

        void IStationQuickControl.OpenReport() => OpenReport();
        void IStationQuickControl.SignAsOfficer() => SignAsOfficer();
        void IStationQuickControl.ExportForOfficer() => ExportForOfficer();

        /// <summary>„Deschide raportul”: the report of this control, saved in the case the first time, then opened (no dialog).</summary>
        [RelayCommand]
        private void OpenReport()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            var pdf = _lastReportPdf is { } p && File.Exists(p) ? p : GenerateReport();
            if (pdf is not null) OpenFile(pdf);
        }

        /// <summary>
        /// „Exportă pentru ofițer”: the report (and the signed report), the copy of the application's log for the CD-R and the station's media history in a new
        /// folder under the folder used last time; the folder is asked only the first time (or after „Alege alt folder”). The export is audited before any file is written.
        /// </summary>
        [RelayCommand]
        private void ExportForOfficer()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            var root = FolderMemory.Shared.Get(FolderMemory.OfficerExport) ?? PickFolder("Alegeți unde se pune exportul pentru ofițerul de securitate (de exemplu stick-ul înrolat sau folderul de ars pe CD-R)", FolderMemory.OfficerExport);
            if (root is null) return;
            var pdf = _lastReportPdf is { } p && File.Exists(p) ? p : GenerateReport();
            if (pdf is null) return;
            var now = DateTimeOffset.UtcNow;
            var dest = Path.Combine(root, OfficerExport.FolderName(_report.Facts.Host, now));
            var files = new System.Collections.Generic.List<string>();
            string anchorLine = AnchorLine;
            try
            {
                // audited before anything leaves the case; refused when the audit cannot be written
                string? refused = CopyOutOfCase(pdf, Path.Combine(EnsureDir(dest), Path.GetFileName(pdf)), "pentru-ofiter");
                if (refused is not null) { Status = "Exportul pentru ofițer nu s-a făcut: " + refused; return; }
                files.Add(Path.GetFileName(pdf) + " — raportul de control");
                foreach (var signed in Directory.EnumerateFiles(Path.GetDirectoryName(pdf)!, Path.GetFileNameWithoutExtension(pdf) + "*semnat*.pdf"))
                { File.Copy(signed, Path.Combine(dest, Path.GetFileName(signed)), overwrite: true); files.Add(Path.GetFileName(signed) + " — raportul semnat de ofițer"); }
                int media = OfficerExport.WriteMediaHistory(_report.Facts, Path.Combine(dest, OfficerExport.MediaHistoryFile));
                files.Add($"{OfficerExport.MediaHistoryFile} — istoricul mediilor ({media} rânduri)");
                if (AuditLogPath() is { } log)
                {
                    var x = AuditAnchor.Export(log, dest, Environment.MachineName, now);
                    AuthApp.Record(OperationAudit.AnchorExported, OperationAudit.AnchorExportedDetail(x.Anchor.Seq, x.Anchor.Code, x.LogSha256, x.Folder));
                    files.Add(Path.GetFileName(x.Folder) + " — copia jurnalului aplicației, pentru CD-R");
                    anchorLine = x.Anchor.Line;
                }
                File.WriteAllText(Path.Combine(dest, OfficerExport.ReadmeFile), OfficerExport.Readme(_report.Facts.Host, now, files, anchorLine), new System.Text.UTF8Encoding(false));
                OfficerInfo = $"Exportul pentru ofițer este în {dest}:{Environment.NewLine}" + string.Join(Environment.NewLine, files.Select(f => "• " + f));
                Status = $"Export pentru ofițer făcut în {dest}. Folderul se schimbă din „Alege alt folder”.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Status = "Exportul pentru ofițer s-a oprit: " + ex.Message;
            }
        }

        /// <summary>„Alege alt folder”: forgets the folder of the officer export and asks for a new one now.</summary>
        [RelayCommand]
        private void ChooseOfficerExportFolder()
        {
            var picked = PickFolder("Alegeți unde se pune exportul pentru ofițerul de securitate", FolderMemory.OfficerExport);
            Status = picked is null ? "Folderul exportului nu s-a schimbat." : $"Exporturile pentru ofițer se vor face în {picked}.";
        }

        private static string EnsureDir(string dir) { Directory.CreateDirectory(dir); return dir; }

        /// <summary>A folder dialog that starts in the folder used last time for <paramref name="kind"/> and remembers the new choice.</summary>
        internal static string? PickFolder(string title, string kind)
        {
            var dlg = new OpenFolderDialog { Title = title };
            if (FolderMemory.Shared.Get(kind) is { } last) dlg.InitialDirectory = last;
            if (dlg.ShowDialog() != true) return null;
            FolderMemory.Shared.Remember(kind, dlg.FolderName);
            return dlg.FolderName;
        }
    }
}
