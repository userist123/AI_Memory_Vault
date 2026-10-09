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
