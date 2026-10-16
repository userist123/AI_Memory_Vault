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
        /// <summary>The period the one-click control will check, in words, shown before it runs (review of PR #298).</summary>
        string PlannedPeriod { get; }
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
            // review of PR #298: „de la ultimul control” is taken from the paper anchor code or the sealed audit, never from the case folder
            if (PeriodChoice == ControlPeriodChoice.SinceLastControl)
            {
                var plan = QuickPlan();
                PeriodDays = plan.Days; PeriodNote = plan.Basis;
            }
            await Run();
            BuildResult();
            RaiseFlow();
            OnPropertyChanged(nameof(AnchorLine));
        }

        [RelayCommand] private Task RunQuick() => RunQuickAsync();

        partial void OnCustomDaysChanged(int value) => OnPropertyChanged(nameof(PlannedPeriod));

        private ControlPeriodPlan QuickPlan() => ControlPeriodStart.From(AuditLogPath(), PreviousAnchorCode, DateTimeOffset.UtcNow);

        /// <summary>„Perioada controlului: …” — what „Fă controlul stației” will check, recomputed when the paper code or the chosen period changes.</summary>
        public string PlannedPeriod => PeriodChoice == ControlPeriodChoice.SinceLastControl
            ? "Perioada controlului: " + QuickPlan().Basis + "."
            : $"Perioada controlului: {ControlPeriods.Label(PeriodChoice)}" + (PeriodChoice == ControlPeriodChoice.Custom ? $" ({CustomDays} zile)" : "") + ", aleasă pe pagina „Control stație”.";

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

        /// <summary>The answer to the destination confirmation of „Exportă pentru ofițer”.</summary>
        public enum ExportDestinationAnswer { Export, ChooseAnother, Cancel }

        /// <summary>
        /// Shows the destination folder and the files that will be written and asks once (review of PR #298: Ctrl+E must not write classified-related data
        /// without one look at where it goes). Replaceable by tests.
        /// </summary>
        internal static Func<string, System.Collections.Generic.IReadOnlyList<string>, ExportDestinationAnswer> ConfirmExportDestination { get; set; } = (folder, files) =>
            System.Windows.MessageBox.Show(
                $"Exportul pentru ofițerul de securitate se scrie în:{Environment.NewLine}{folder}{Environment.NewLine}{Environment.NewLine}Fișierele:{Environment.NewLine}" +
                string.Join(Environment.NewLine, files.Select(f => "• " + f)) + Environment.NewLine + Environment.NewLine +
                "Da = Exportă aici · Nu = alege alt folder · Anulare = renunță",
                "Exportă pentru ofițer", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question) switch
            {
                System.Windows.MessageBoxResult.Yes => ExportDestinationAnswer.Export,
                System.Windows.MessageBoxResult.No => ExportDestinationAnswer.ChooseAnother,
                _ => ExportDestinationAnswer.Cancel,
            };

        /// <summary>
        /// „Exportă pentru ofițer”: the report (and the signed report), the copy of the application's log for the CD-R and the station's media history in a new
        /// folder under the folder used last time. Review of PR #298: the destination and the file list are confirmed once every time (also on Ctrl+E), and the
        /// whole bundle goes through <see cref="ExportAudit.RunBundle"/> — the intent lists every file before anything is written (refused as a whole when it
        /// cannot be written) and every written file is recorded with its SHA-256.
        /// </summary>
        [RelayCommand]
        private void ExportForOfficer()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            const string Title = "Alegeți unde se pune exportul pentru ofițerul de securitate (de exemplu stick-ul înrolat sau folderul de ars pe CD-R)";
            var root = FolderMemory.Shared.Get(FolderMemory.OfficerExport) ?? PickFolder(Title, FolderMemory.OfficerExport);
            if (root is null) return;
            var pdf = _lastReportPdf is { } p && File.Exists(p) ? p : GenerateReport();
            if (pdf is null) return;
            var now = DateTimeOffset.UtcNow;
            var log = AuditLogPath();
            var signedReports = Directory.EnumerateFiles(Path.GetDirectoryName(pdf)!, Path.GetFileNameWithoutExtension(pdf) + "*semnat*.pdf").ToList();
            var planned = new System.Collections.Generic.List<string> { Path.GetFileName(pdf) + " — raportul de control" };
            planned.AddRange(signedReports.Select(f => Path.GetFileName(f) + " — raportul semnat de ofițer"));
            planned.Add(OfficerExport.MediaHistoryFile + " — istoricul mediilor (cu seriile lor)");
            if (log is not null) planned.Add(AuditAnchor.ExportFolderName(now) + " — copia jurnalului aplicației, pentru CD-R");
            planned.Add(OfficerExport.ReadmeFile + " — ce conține folderul");
            while (true)
            {
                var answer = ConfirmExportDestination(Path.Combine(root, OfficerExport.FolderName(_report.Facts.Host, now)), planned);
                if (answer == ExportDestinationAnswer.Cancel) { Status = "Exportul pentru ofițer a fost anulat; nu s-a scris nimic."; return; }
                if (answer == ExportDestinationAnswer.Export) break;
                root = PickFolder(Title, FolderMemory.OfficerExport);
                if (root is null) { Status = "Exportul pentru ofițer a fost anulat; nu s-a scris nimic."; return; }
            }
            var dest = Path.Combine(root, OfficerExport.FolderName(_report.Facts.Host, now));
            var facts = _report.Facts;
            string anchorLine = AnchorLine;
            AnchorExport? anchorCopy = null;
            try
            {
                var refused = ExportAudit.RunBundle(ExportAudit.LiveCaseId, "pentru-ofiter", dest, planned, () =>
                {
                    Directory.CreateDirectory(dest);
                    CopyWithSignature(pdf, Path.Combine(dest, Path.GetFileName(pdf)));
                    foreach (var signed in signedReports) File.Copy(signed, Path.Combine(dest, Path.GetFileName(signed)), overwrite: true);
                    OfficerExport.WriteMediaHistory(facts, Path.Combine(dest, OfficerExport.MediaHistoryFile));
                    if (log is not null) { anchorCopy = AuditAnchor.Export(log, dest, Environment.MachineName, now); anchorLine = anchorCopy.Anchor.Line; }
                    File.WriteAllText(Path.Combine(dest, OfficerExport.ReadmeFile), OfficerExport.Readme(facts.Host, now, planned, anchorLine), new System.Text.UTF8Encoding(false));
                });
                if (refused is not null) { Status = "Exportul pentru ofițer nu s-a făcut: " + refused; return; }
                if (anchorCopy is { } x) AuthApp.Record(OperationAudit.AnchorExported, OperationAudit.AnchorExportedDetail(x.Anchor.Seq, x.Anchor.Code, x.LogSha256, x.Folder));
                OfficerInfo = $"Exportul pentru ofițer este în {dest}:{Environment.NewLine}" + string.Join(Environment.NewLine, planned.Select(f => "• " + f));
                Status = $"Export pentru ofițer făcut în {dest} (trecut în jurnalul de audit, cu amprenta fiecărui fișier).";
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
