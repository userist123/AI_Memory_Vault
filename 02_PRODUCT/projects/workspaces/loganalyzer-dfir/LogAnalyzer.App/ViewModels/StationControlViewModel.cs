using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Flow;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Audit;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Control stație": collects facts from this station (read-only), evaluates them into control checks, per-user
    /// activity and a timeline of notable actions, and saves the report (JSON + PDF) in the station's case.
    /// </summary>
    public partial class StationControlViewModel : ObservableObject
    {
        private ControlReport? _report;

        public ObservableCollection<ControlCheck> Checks { get; } = new();
        public ObservableCollection<UserActivity> Users { get; } = new();
        public ObservableCollection<ActionEntry> Actions { get; } = new();

        [ObservableProperty] private int _periodDays = 90;
        /// <summary>Fixed at compile time by the edition (never chosen by the operator).</summary>
        public LogAnalyzer.Core.Services.Edition.StationPosture Posture { get; } = LogAnalyzer.Core.Services.Edition.StationPostureContext.Current;
        public string PostureLine => "Ediția: " + LogAnalyzer.Core.Services.Edition.StationPostures.EditionLabel(Posture) + " — regulile de control se aplică după postura ediției.";
        [ObservableProperty] private string _inspector = LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified;
        [ObservableProperty] private string _notes = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _status = "Apăsați „Înainte” ca să alegeți perioada. Controlul doar citește; nu modifică stația.";
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private string _statusFilter = "Toate";
        [ObservableProperty] private string _gapsText = "";
        [ObservableProperty] private string _savedReportInfo = "";

        // Checks only the controller can do (docs/dfir/RESEARCH_2026-10-11_CONTROL_STATII_CLASIFICATE.md §3), answered here and printed in the report.
        public ObservableCollection<ManualCheckItem> ManualItems { get; } = new(LogAnalyzer.Dfir.Flow.ManualChecks.All.Select(c => new ManualCheckItem(c)));
        [ObservableProperty] private string _manualSummary = LogAnalyzer.Dfir.Flow.ManualChecks.Summary([]);
        private void HookManual()
        {
            foreach (var m in ManualItems)
                m.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ManualCheckItem.Answer)) ManualSummary = LogAnalyzer.Dfir.Flow.ManualChecks.Summary(ManualItems.Select(x => x.ToResult()).ToList()); };
        }
        partial void OnSavedReportInfoChanged(string value) => OnPropertyChanged(nameof(HasSavedReport));
        public bool HasSavedReport => SavedReportInfo.Length > 0;

        public string[] StatusFilters { get; } = { "Toate", "NECONFORM", "DE VERIFICAT", "NEDETERMINAT", "CONFORM", "NU SE APLICĂ" };

        partial void OnStatusFilterChanged(string value) => FillChecks();

        // Owner 2026-10-11: every code explained on screen (what, why, how, when) for controllers without IT training: list on the left, explanation on the right.
        [ObservableProperty] private ControlCheck? _selectedCheck;
        [ObservableProperty] private CheckExplanation? _selectedExplanation;
        partial void OnSelectedCheckChanged(ControlCheck? value) => SelectedExplanation = value is null ? null : CheckGuides.Explain(value);
        partial void OnSelectedExplanationChanged(CheckExplanation? value) => OnPropertyChanged(nameof(HasSelectedCheck));
        public bool HasSelectedCheck => SelectedExplanation is not null;

        [ObservableProperty] private ActionEntry? _selectedAction;
        public ObservableCollection<CodeExplanation> SelectedActionCodes { get; } = new();
        partial void OnSelectedActionChanged(ActionEntry? value)
        {
            SelectedActionCodes.Clear();
            if (value is null) return;
            foreach (var c in CodeExplainer.FindIn($"{value.Source} {value.Detail}")) SelectedActionCodes.Add(c);
            if (SelectedActionCodes.Count == 0)
                SelectedActionCodes.Add(new CodeExplanation(value.Source, "sursă", "Această sursă nu are încă o explicație în aplicație.", "Arătați înregistrarea unui specialist IT dacă este nevoie.", ""));
        }

        [RelayCommand]
        private void CopyExplanation()
        {
            if (SelectedExplanation is null) { Status = "Alegeți întâi o verificare din listă."; return; }
            try { Clipboard.SetText(SelectedExplanation.ToPlainText()); Status = "Explicația a fost copiată; o puteți lipi în procesul-verbal."; }
            catch (System.Runtime.InteropServices.COMException) { Status = "Clipboard-ul este ocupat de alt program; încercați din nou."; }
        }

        // ───────── WP18 S4: the guided control, "Verifică această stație", in three steps ─────────

        public sealed record PeriodOption(ControlPeriodChoice Choice, string Label);

        private readonly GuidedFlow _flow;

        public StationControlViewModel()
        {
            HookManual();
            _flow = new GuidedFlow("Verifică această stație",
            [
                new FlowStep("station", "Ce stație?", "Controlul se face pe acest calculator. Aplicația doar citește; nu modifică nimic."),
                new FlowStep("period", "Ce perioadă?", "Alegeți perioada pe care o verificați.", ValidatePeriod),
                new FlowStep("procedures", "Ce proceduri?", "Acestea sunt procedurile după care se judecă stația. O secțiune nedefinită nu poate da CONFORM."),
            ]);
            PeriodOptions = Enum.GetValues<ControlPeriodChoice>().Select(c => new PeriodOption(c, ControlPeriods.Label(c))).ToList();
            StationLine = $"{Environment.MachineName} · {LogAnalyzer.Dfir.Registers.StationName.Parse(Environment.MachineName).Describe()} · " +
                          (AppModeContext.IsAirGapped ? "fără rețea (izolată)" : "conectată");
            LoadPreviousControls();
            InspectorFunction = _headerProfile.InspectorFunction;
            RaiseFlow();
        }

        public IReadOnlyList<PeriodOption> PeriodOptions { get; }
        [ObservableProperty] private ControlPeriodChoice _periodChoice = ControlPeriodChoice.SinceLastControl;
        [ObservableProperty] private int _customDays = 30;
        [ObservableProperty] private string _periodNote = "";
        [ObservableProperty] private string _stationLine = "";
        [ObservableProperty] private string _profileLine = "";
        [ObservableProperty] private string _flowError = "";
        [ObservableProperty] private ControlResultScreen? _result;
        [ObservableProperty] private ControlComparison? _comparison;
        [ObservableProperty] private PreviousControl? _selectedPrevious;
        [ObservableProperty] private string _previousNote = "";
        // WP18 S5: the "proces-verbal" fields (decision D3) and the manual marking (decision D4).
        private readonly LogAnalyzer.Dfir.Reporting.ReportHeaderProfile _headerProfile = LogAnalyzer.Dfir.Reporting.ReportHeaderProfile.Load(null, out _);
        [ObservableProperty] private string _inspectorFunction = "";
        [ObservableProperty] private string _registrationNumber = "";
        [ObservableProperty] private string _markingLevel = "";
        public string MarkingNote => "Marcajul se introduce manual și este tipărit pe fiecare pagină ca „" + ControlReportHeader.MarkingNote + "”.";
        private static string HardwareIdOrUnknown()
        {
            try { return new LogAnalyzer.Core.Services.LicenseService().GetHardwareId(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or System.Management.ManagementException or UnauthorizedAccessException) { return "necunoscut (" + ex.Message + ")"; }
        }
        private ControlReportHeader BuildHeader(ControlReport r) =>
            ControlReportHeader.From(_headerProfile, r.Facts, HardwareIdOrUnknown(), Inspector, InspectorFunction, LogAnalyzer.Core.Services.Edition.StationRoleContext.Current.HumanRole, RegistrationNumber, MarkingLevel);
        public ObservableCollection<SectionStatus> ProfileSections { get; } = new();
        public ObservableCollection<PreviousControl> PreviousControls { get; } = new();
        public ObservableCollection<CheckChange> ComparisonChanges { get; } = new();
        public ObservableCollection<string> ResultNextSteps { get; } = new();

        public string FlowTitle => _flow.Title;
        public string FlowProgress => _flow.ProgressText;
        public int FlowStepIndex => _flow.StepIndex;
        public string FlowStepTitle => _flow.Current.Title;
        public string FlowQuestion => _flow.Current.Question;
        public bool FlowCanGoBack => _flow.CanGoBack;
        public bool FlowIsLastStep => _flow.IsLastStep;
        public bool FlowStopped => _flow.Stopped;
        public bool FlowCompleted => _flow.Completed;
        public bool IsCustomPeriod => PeriodChoice == ControlPeriodChoice.Custom;
        public bool HasResult => Result is not null;
        public string FlowNextText => _flow.IsLastStep ? "Pornește controlul" : "Înainte";

        partial void OnPeriodChoiceChanged(ControlPeriodChoice value) => OnPropertyChanged(nameof(IsCustomPeriod));
        partial void OnResultChanged(ControlResultScreen? value)
        {
            OnPropertyChanged(nameof(HasResult));
            ResultNextSteps.Clear();
            if (value is not null) foreach (var st in value.NextSteps) ResultNextSteps.Add(st);
        }

        private void RaiseFlow()
        {
            foreach (var n in new[] { nameof(FlowProgress), nameof(FlowStepIndex), nameof(FlowStepTitle), nameof(FlowQuestion), nameof(FlowCanGoBack), nameof(FlowIsLastStep), nameof(FlowStopped), nameof(FlowCompleted), nameof(FlowNextText) })
                OnPropertyChanged(n);
            FlowError = _flow.LastError;
        }

        private string? ValidatePeriod()
        {
            var days = ControlPeriods.Days(PeriodChoice, PreviousControls.FirstOrDefault(), CustomDays, DateTimeOffset.UtcNow, out var note);
            if (days <= 0) return note;
            PeriodDays = days; PeriodNote = note;
            return null;
        }

        private void LoadPreviousControls()
        {
            PreviousControls.Clear();
            try
            {
                var list = ControlArchive.List(LogAnalyzer.UI.Services.LiveCase.Root, out var problems);
                foreach (var p in list) PreviousControls.Add(p);
                PreviousNote = list.Count == 0 ? "Niciun control anterior salvat pe această stație."
                    : $"{list.Count} controale anterioare; cel mai recent: {list[0].Label}." + (problems.Count > 0 ? $" Foldere necitite: {problems.Count}." : "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { PreviousNote = "Controalele anterioare nu au putut fi citite: " + ex.Message; }
        }

        /// <summary>
        /// What the control reads from the organisation's own data: the classified folders of the procedure profile (their audit rules are checked)
        /// and the register of offline updates (compared with the installed updates). Missing data gives empty lists, which the checks report as
        /// "not filled in", never as compliant.
        /// </summary>
        internal static (System.Collections.Generic.List<string> Folders, System.Collections.Generic.List<LogAnalyzer.Dfir.Registers.UpdateRow> Updates) ControlInputs()
        {
            ProcedureProfile? p = null;
            try { p = ProfileProvider.Shared.Current; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            var folders = p?.ClassifiedFolders.Select(x => x.Path.Trim()).Where(x => x.Length > 0).ToList() ?? [];
            var updates = new System.Collections.Generic.List<LogAnalyzer.Dfir.Registers.UpdateRow>();
            try { updates = LogAnalyzer.Dfir.Registers.RegisterStore.Load<LogAnalyzer.Dfir.Registers.UpdateRegister>().Register?.Rows ?? updates; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            return (folders, updates);
        }

        private UpdateRegisterViewModel? _updateRegister;
        /// <summary>"Registrul actualizărilor offline" (control research P5), edited on the control page; created when the page first shows it.</summary>
        public UpdateRegisterViewModel UpdateRegister => _updateRegister ??= new UpdateRegisterViewModel();

        private void LoadProfileSections()
        {
            ProfileSections.Clear();
            ProcedureProfile? p = null;
            try { p = ProfileProvider.Shared.Current; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            foreach (var sct in ProfileSummary.Sections(p)) ProfileSections.Add(sct);
            ProfileLine = ProfileSummary.Line(ProfileSections.ToList());
        }

        [RelayCommand]
        private async Task FlowNext()
        {
            if (_flow.Completed) { _flow.Restart(); Result = null; Comparison = null; ComparisonChanges.Clear(); RaiseFlow(); return; }
            bool wasLast = _flow.IsLastStep;
            // The case scope (why, period, systems, approver, act) is asked once, at the start; the report step does not ask again when it is complete.
            if (_flow.StepIndex == 0 && !LogAnalyzer.UI.Services.LiveCase.EnsureScopeAtStart() && LogAnalyzer.UI.Services.LiveCase.ScopePrompt is not null)
                Status = "Scopul cazului nu a fost completat; veți fi întrebat din nou la salvarea raportului.";
            if (!_flow.Next()) { RaiseFlow(); return; }
            if (_flow.StepIndex == 2 && !wasLast) LoadProfileSections();
            RaiseFlow();
            if (wasLast && _flow.Completed)
            {
                await Run();
                BuildResult();
                RaiseFlow();
            }
        }

        [RelayCommand] private void FlowBack() { _flow.Back(); RaiseFlow(); }
        [RelayCommand] private void FlowStop() { _flow.Stop(); RaiseFlow(); Status = "Controlul a fost oprit. Ce ați ales rămâne; apăsați „Înainte” ca să continuați."; }
        [RelayCommand] private void FlowRestart() { _flow.Restart(); Result = null; Comparison = null; ComparisonChanges.Clear(); RaiseFlow(); }

        /// <summary>"Compară cu controlul anterior": a difference against the selected (or the most recent) saved control.</summary>
        [RelayCommand]
        private void CompareWithPrevious()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            var prev = SelectedPrevious ?? PreviousControls.FirstOrDefault();
            if (prev is null) { Status = "Nu există un control anterior cu care să compar."; return; }
            Comparison = ControlComparison.Compare(prev, _report.Checks);
            ComparisonChanges.Clear();
            foreach (var ch in Comparison.Worse.Concat(Comparison.Better).Concat(Comparison.New).Concat(Comparison.Removed).Concat(Comparison.ApplicabilityChanged)) ComparisonChanges.Add(ch);
            BuildResult();
            Status = Comparison.Summary;
        }

        private void BuildResult()
        {
            if (_report is null) return;
            if (ProfileSections.Count == 0) LoadProfileSections();
            if (Comparison is null && PreviousControls.Count > 0 && PeriodChoice == ControlPeriodChoice.SinceLastControl)
            {
                Comparison = ControlComparison.Compare(PreviousControls[0], _report.Checks);
                ComparisonChanges.Clear();
                foreach (var ch in Comparison.Worse.Concat(Comparison.Better).Concat(Comparison.New).Concat(Comparison.Removed).Concat(Comparison.ApplicabilityChanged)) ComparisonChanges.Add(ch);
            }
            Result = ControlResultScreen.Build(_report, ProfileSections.ToList(), Comparison);
        }

        /// <summary>Why the clock is confirmed as corrected (written to the chained audit with the confirmation).</summary>
        [ObservableProperty] private string _clockAckReason = "";

        [RelayCommand]
        private void ConfirmClockCorrected()
        {
            Status = LogAnalyzer.UI.Services.AuthApp.ConfirmClockCorrected(ClockAckReason);
            if (LogAnalyzer.Dfir.Case.ClockMemory.StartupWarning.Length == 0) ClockAckReason = "";
        }

        [RelayCommand]
        private async Task Run()
        {
            if (PeriodDays is < 1 or > 3650) { Status = "Perioada trebuie să fie între 1 și 3650 de zile."; return; }
            IsBusy = true;
            Status = "Se colectează datele stației (conturi, politici, jurnale, USB, rețele)…";
            try
            {
                var start = DateTimeOffset.UtcNow.AddDays(-PeriodDays);
                var posture = Posture;
                SavedReportInfo = "";
                var (folders, updates) = ControlInputs();
                var previousAnchor = PreviousAnchorCode;
                var previousFromStation = PreviousCodeIsStationHint();
                _lastReportPdf = null;
                bool auditedStart = LogAnalyzer.UI.Services.AuthApp.Record(LogAnalyzer.Dfir.Auth.OperationAudit.ControlStarted,
                    LogAnalyzer.Dfir.Auth.OperationAudit.ControlStartedDetail(LogAnalyzer.Core.Services.Edition.StationPostures.EditionLabel(posture), posture.ToString(),
                        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator), PeriodDays, folders.Count));
                _report = await Task.Run(() =>
                {
                    // N13: only the Network edition registers the active probe (LogAnalyzer.Connectors); the other editions have none.
                    var dcProbe = global::LogAnalyzer.UI.AppComposition.ServiceProvider?.GetService(typeof(IDomainControllerProbe)) as IDomainControllerProbe;
                    var facts = StationFactCollector.Collect(start, posture, classifiedFolders: folders, offlineUpdates: updates, dcProbe: dcProbe);
                    // Time memory (S05): the latest time the application recorded, read BEFORE this report adds its own "clock.seen" entry.
                    string? auditPath = null;
                    try { auditPath = LogAnalyzer.UI.Services.AuthApp.Service.AuditPath; } catch (InvalidOperationException) { }
                    if (auditPath is not null)
                    {
                        // A11: the previous anchor code, checked before this control appends anything (an append never changes earlier entries anyway).
                        PrepareAnchorFacts(facts, auditPath, previousAnchor, previousFromStation);
                        try
                        {
                            var cb = LogAnalyzer.Dfir.Case.ClockMemory.ReadBaseline(auditPath);
                            facts.LatestAppRecordUtc = cb.Baseline; facts.ClockSuspectNote = cb.SuspectNote; facts.ClockAckText = cb.AckText;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        try { LogAnalyzer.Dfir.Case.ClockMemory.RecordSeen(auditPath, facts.CollectedUtc, "raport de control stație"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        // The warning (and with it the confidence of timed findings) follows the chain at every report, not only the start.
                        try { LogAnalyzer.Dfir.Case.ClockMemory.Reevaluate(auditPath, facts.CollectedUtc, StationFactCollector.ReadInstallDate()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                    return ControlEvaluator.Evaluate(facts);
                });
                bool auditedFinish = LogAnalyzer.UI.Services.AuthApp.Record(LogAnalyzer.Dfir.Auth.OperationAudit.ControlFinished,
                    LogAnalyzer.Dfir.Auth.OperationAudit.ControlFinishedDetail(_report.Facts.Host, _report.Facts.IsAdministrator, _report.Checks.Count,
                        _report.Count(ControlStatus.Neconform), _report.Count(ControlStatus.DeVerificat), _report.Count(ControlStatus.Nedeterminat)));
                AfterControlAnchor();
                FillChecks();
                Users.Clear();
                foreach (var u in _report.Users) Users.Add(u);
                Actions.Clear();
                foreach (var a in _report.Actions.OrderByDescending(a => a.TimeUtc)) Actions.Add(a);
                Summary = $"NECONFORM {_report.Count(ControlStatus.Neconform)} · DE VERIFICAT {_report.Count(ControlStatus.DeVerificat)} · " +
                          $"NEDETERMINAT {_report.Count(ControlStatus.Nedeterminat)} · CONFORM {_report.Count(ControlStatus.Conform)}" +
                          (_report.Count(ControlStatus.NuSeAplica) > 0 ? $" · NU SE APLICĂ {_report.Count(ControlStatus.NuSeAplica)}" : "");
                GapsText = _report.Facts.Gaps.Count == 0 ? "" :
                    "Surse lipsă sau incomplete: " + string.Join("; ", _report.Facts.Gaps.Select(g => $"{g.Artifact} ({g.Reason})"));
                Status = _report.Facts.IsAdministrator
                    ? $"Control finalizat: {_report.Checks.Count} verificări, {_report.Actions.Count} acțiuni în cronologie. Dublu-click pe orice rând pentru detalii."
                    : "Control finalizat FĂRĂ drepturi de administrator: jurnalul Security și o parte din setări nu au putut fi citite (verificările respective sunt NEDETERMINATE). Reporniți aplicația ca administrator.";
                if (!auditedStart || !auditedFinish) Status += " ATENȚIE: controlul nu a putut fi trecut în jurnalul de audit (vezi avertismentul de sus).";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Status = "Eroare la colectare: " + ex.Message;
            }
            finally { IsBusy = false; }
        }

        private void FillChecks()
        {
            Checks.Clear();
            if (_report is null) return;
            var order = new[] { ControlStatus.Neconform, ControlStatus.DeVerificat, ControlStatus.Nedeterminat, ControlStatus.Conform, ControlStatus.NuSeAplica };
            foreach (var c in _report.Checks.OrderBy(c => Array.IndexOf(order, c.Status)).ThenBy(c => c.Id))
                if (StatusFilter == "Toate" || ControlReportPdf.StatusText(c.Status) == StatusFilter)
                    Checks.Add(c);
            SelectedCheck = Checks.FirstOrDefault();   // the explanation panel is never empty after a control
        }

        [RelayCommand]
        private void SaveReport() => GenerateReport(showCopyDialog: true);

        /// <summary>
        /// Writes the report into the case, records its SHA-256 in the sealed audit ("report.generated": a signed report is accepted only if its original part is
        /// one of these), and remembers it as the report "Semnează ca ofițer de securitate" signs. Returns the PDF path, or null with the reason in Status.
        /// </summary>
        private string? GenerateReport(bool showCopyDialog)
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return null; }
            var ws = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
            if (ws is null) { Status = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return null; }
            if (Result is null) BuildResult();
            var (_, pdf) = ControlReportPdf.SaveToCase(_report, ws, Inspector, Notes, BuildHeader(_report), Result, ManualItems.Select(m => m.ToResult()).ToList());
            string sha;
            using (var fs = File.OpenRead(pdf)) sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
            LogAnalyzer.UI.Services.AuthApp.Record(LogAnalyzer.Dfir.Auth.OperationAudit.ReportGenerated, LogAnalyzer.Dfir.Auth.OperationAudit.ReportGeneratedDetail(Path.GetFileName(pdf), sha));
            _lastReportPdf = pdf;
            var dlg = new SaveFileDialog { FileName = Path.GetFileName(pdf), Filter = "PDF (*.pdf)|*.pdf", Title = "Salvați o copie a raportului (originalul rămâne în caz)" };
            // F3: the report in the case is signed when the operator configured a signing key; a copy carries a copy of the same signature.
            var signLine = LogAnalyzer.Dfir.Reporting.ReportSigningSettings.Load().SignIfConfigured(pdf);
            ws.Audit("report.signature", $"{Path.GetFileName(pdf)}: {signLine}");
            string? copyRefused = null;
            if (showCopyDialog && dlg.ShowDialog() == true && !string.Equals(dlg.FileName, pdf, StringComparison.OrdinalIgnoreCase))
            {
                // the copy leaves the case unmasked: audited first, refused when the audit cannot be written (the original stays in the case)
                copyRefused = LogAnalyzer.UI.Services.ExportAudit.Run(Path.GetFileName(ws.Root.TrimEnd('\\', '/')), "pdf-control", _report.Checks.Count, dlg.FileName, () =>
                {
                    File.Copy(pdf, dlg.FileName, overwrite: true);
                    var sig = LogAnalyzer.Dfir.Reporting.ReportSignature.SigPath(pdf);
                    if (File.Exists(sig)) File.Copy(sig, LogAnalyzer.Dfir.Reporting.ReportSignature.SigPath(dlg.FileName), overwrite: true);
                });
            }
            // confirmation page in words (UX research R19): where it is, its fingerprint as the reference for the official report, what comes next
            SavedReportInfo = $"Raportul a fost salvat.{Environment.NewLine}" +
                              $"Unde: {pdf}{Environment.NewLine}" +
                              $"Amprenta fișierului (SHA-256): {sha}{Environment.NewLine}" +
                              $"{signLine}{Environment.NewLine}" +
                              "Ce urmează: ofițerul de securitate semnează raportul cu certificatul calificat de pe card („Semnează ca ofițer de securitate”); tipăriți raportul, treceți amprenta și codul de ancorare în procesul-verbal. Dacă cineva modifică fișierul, amprenta nu se mai potrivește. " +
                              "Originalul rămâne în cazul stației; orice copie se verifică prin aceeași amprentă.";
            Status = copyRefused is null ? "Raport salvat în caz." : "Raport salvat în caz; copia nu: " + copyRefused;
            LoadPreviousControls();
            if (showCopyDialog) Process.Start(new ProcessStartInfo(copyRefused is null && dlg.FileName is { Length: > 0 } f && File.Exists(f) ? f : pdf) { UseShellExecute = true });
            return pdf;
        }

        [RelayCommand]
        private void OpenCaseFolder()
        {
            var live = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
            if (live is null) { Status = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return; }
            var dir = Path.Combine(live.Root, "Control");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
    }
}
