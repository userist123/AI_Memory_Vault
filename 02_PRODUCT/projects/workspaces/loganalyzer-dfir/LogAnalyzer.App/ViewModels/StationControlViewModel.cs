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
        [ObservableProperty] private bool _stationShouldBeIsolated = AppModeContext.IsAirGapped;
        [ObservableProperty] private string _inspector = LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified;
        [ObservableProperty] private string _notes = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _status = "Alegeți perioada și apăsați „Rulează controlul”. Colectarea doar citește; nu modifică stația.";
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private string _statusFilter = "Toate";
        [ObservableProperty] private string _gapsText = "";

        public string[] StatusFilters { get; } = { "Toate", "NECONFORM", "DE VERIFICAT", "NEDETERMINAT", "CONFORM" };

        partial void OnStatusFilterChanged(string value) => FillChecks();

        // ───────── WP18 S4: the guided control, "Verifică această stație", in three steps ─────────

        public sealed record PeriodOption(ControlPeriodChoice Choice, string Label);

        private readonly GuidedFlow _flow;

        public StationControlViewModel()
        {
            _flow = new GuidedFlow("Verifică această stație",
            [
                new FlowStep("station", "Ce stație?", "Controlul se face pe acest calculator. Aplicația doar citește; nu modifică nimic."),
                new FlowStep("period", "Ce perioadă?", "Alegeți perioada pe care o verificați.", ValidatePeriod),
                new FlowStep("procedures", "Ce proceduri?", "Acestea sunt procedurile după care se judecă stația. O secțiune nedefinită nu poate da CONFORM."),
            ]);
            PeriodOptions = Enum.GetValues<ControlPeriodChoice>().Select(c => new PeriodOption(c, ControlPeriods.Label(c))).ToList();
            StationLine = $"{Environment.MachineName} · {LogAnalyzer.Core.Services.Edition.StationRoleContext.Current.HumanRole} · " +
                          (AppModeContext.IsAirGapped ? "fără rețea (izolată)" : "conectată");
            LoadPreviousControls();
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
            if (_flow.Completed) { _flow.Restart(); Result = null; RaiseFlow(); return; }
            bool wasLast = _flow.IsLastStep;
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
            foreach (var ch in Comparison.Worse.Concat(Comparison.Better).Concat(Comparison.New).Concat(Comparison.Removed)) ComparisonChanges.Add(ch);
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
                foreach (var ch in Comparison.Worse.Concat(Comparison.Better).Concat(Comparison.New).Concat(Comparison.Removed)) ComparisonChanges.Add(ch);
            }
            Result = ControlResultScreen.Build(_report, ProfileSections.ToList(), Comparison);
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
                var isolated = StationShouldBeIsolated;
                _report = await Task.Run(() => ControlEvaluator.Evaluate(StationFactCollector.Collect(start, isolated)));
                FillChecks();
                Users.Clear();
                foreach (var u in _report.Users) Users.Add(u);
                Actions.Clear();
                foreach (var a in _report.Actions.OrderByDescending(a => a.TimeUtc)) Actions.Add(a);
                Summary = $"NECONFORM {_report.Count(ControlStatus.Neconform)} · DE VERIFICAT {_report.Count(ControlStatus.DeVerificat)} · " +
                          $"NEDETERMINAT {_report.Count(ControlStatus.Nedeterminat)} · CONFORM {_report.Count(ControlStatus.Conform)}";
                GapsText = _report.Facts.Gaps.Count == 0 ? "" :
                    "Goluri de probă: " + string.Join("; ", _report.Facts.Gaps.Select(g => $"{g.Artifact} ({g.Reason})"));
                Status = _report.Facts.IsAdministrator
                    ? $"Control finalizat: {_report.Checks.Count} verificări, {_report.Actions.Count} acțiuni în cronologie. Dublu-click pe orice rând pentru detalii."
                    : "Control finalizat FĂRĂ drepturi de administrator: jurnalul Security și o parte din setări nu au putut fi citite (verificările respective sunt NEDETERMINATE). Reporniți aplicația ca administrator.";
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
            var order = new[] { ControlStatus.Neconform, ControlStatus.DeVerificat, ControlStatus.Nedeterminat, ControlStatus.Conform };
            foreach (var c in _report.Checks.OrderBy(c => Array.IndexOf(order, c.Status)).ThenBy(c => c.Id))
                if (StatusFilter == "Toate" || ControlReportPdf.StatusText(c.Status) == StatusFilter)
                    Checks.Add(c);
        }

        [RelayCommand]
        private void SaveReport()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            var ws = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
            if (ws is null) { Status = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return; }
            var (_, pdf) = ControlReportPdf.SaveToCase(_report, ws, Inspector, Notes);
            var dlg = new SaveFileDialog { FileName = Path.GetFileName(pdf), Filter = "PDF (*.pdf)|*.pdf", Title = "Salvați o copie a raportului (originalul rămâne în caz)" };
            if (dlg.ShowDialog() == true && !string.Equals(dlg.FileName, pdf, StringComparison.OrdinalIgnoreCase))
                File.Copy(pdf, dlg.FileName, overwrite: true);
            Status = $"Raport salvat în caz (cu SHA-256 și custodie): {pdf}";
            LoadPreviousControls();
            Process.Start(new ProcessStartInfo(dlg.FileName is { Length: > 0 } f && File.Exists(f) ? f : pdf) { UseShellExecute = true });
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
