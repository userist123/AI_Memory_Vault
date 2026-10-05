using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>"Investigație completă": collect from this station and/or import evidence, run the pipeline, review findings, export the report.</summary>
    public partial class InvestigationViewModel : ObservableObject
    {
        private InvestigationResult? _result;
        private CancellationTokenSource? _cts;

        public ObservableCollection<string> ImportFiles { get; } = new();
        public ObservableCollection<Finding> Findings { get; } = new();
        public ObservableCollection<TimelineEvent> Timeline { get; } = new();

        public string[] Profiles { get; } = { "Rapid (jurnale, Prefetch, stare live)", "Standard (+ SRUM)", "Complet" };
        [ObservableProperty] private int _profileIndex = 1;
        [ObservableProperty] private bool _collectFromThisStation = true;
        [ObservableProperty] private string _caseName = $"Investigație {Environment.MachineName} {DateTime.Now:yyyy-MM-dd}";
        [ObservableProperty] private string _log = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private string _chains = "";
        [ObservableProperty] private string _gapsText = "";
        [ObservableProperty] private string _timelineFilter = "";

        partial void OnTimelineFilterChanged(string value) => FillTimeline();

        [RelayCommand]
        private void AddFiles()
        {
            var dlg = new OpenFileDialog { Multiselect = true, Filter = "Probe (*.evtx;*.pf;SRUDB.dat;*.pcapng;hive-uri;Amcache.hve;History)|*.evtx;*.pf;SRUDB.dat;*.pcapng;SYSTEM;SOFTWARE;NTUSER.DAT;*.hiv;Amcache.hve;History|Toate fișierele|*.*" };
            if (dlg.ShowDialog() != true) return;
            foreach (var f in dlg.FileNames) if (!ImportFiles.Contains(f)) ImportFiles.Add(f);
        }

        [RelayCommand]
        private void AddFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Folder cu probe (EVTX, Prefetch, SRUDB.dat, PCAPNG), căutat recursiv" };
            if (dlg.ShowDialog() != true) return;
            foreach (var f in Directory.EnumerateFiles(dlg.FolderName, "*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pf", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase)))
                if (!ImportFiles.Contains(f)) ImportFiles.Add(f);
            Log += $"{ImportFiles.Count} fișiere de probă selectate.{Environment.NewLine}";
        }

        [RelayCommand]
        private void ClearFiles() => ImportFiles.Clear();

        [RelayCommand]
        private async Task Run()
        {
            if (!CollectFromThisStation && ImportFiles.Count == 0) { Log += "Alegeți colectarea de pe această stație sau adăugați probe de importat." + Environment.NewLine; return; }
            IsBusy = true;
            _cts = new CancellationTokenSource();
            Log = "";
            var progress = new Progress<string>(m => Log += $"{DateTime.Now:HH:mm:ss}  {m}{Environment.NewLine}");
            var profile = ProfileIndex switch { 0 => CollectionProfile.Quick, 2 => CollectionProfile.FullForensic, _ => CollectionProfile.Standard };
            var files = ImportFiles.ToList();
            bool collect = CollectFromThisStation;
            var name = CaseName;
            try
            {
                _result = await Task.Run(() =>
                {
                    var casesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "Cases");
                    var ws = InvestigationPipeline.NewCase(casesRoot, name);
                    if (files.Count > 0)
                    {
                        ((IProgress<string>)progress).Report($"Import {files.Count} fișiere (copii; originalele nu se modifică)");
                        InvestigationPipeline.Import(ws, files);
                    }
                    return new InvestigationPipeline().Run(ws, profile, collect, progress, _cts.Token);
                });
                Findings.Clear();
                foreach (var f in _result.Findings) Findings.Add(f);
                FillTimeline();
                var chains = _result.Findings.Where(f => f.RuleId == "INCIDENT-CHAIN").ToList();
                Chains = chains.Count == 0 ? "Niciun lanț de incident (constatări grave grupate în timp)." :
                    string.Join(Environment.NewLine + Environment.NewLine, chains.Select(c => $"{c.Title} [{c.Severity.ToSpec()}]{Environment.NewLine}" +
                        string.Join(Environment.NewLine, c.Description.Split(" → ").Select(s => "  → " + s))));
                GapsText = string.Join(Environment.NewLine, _result.Gaps.Select(g => $"{g.Artifact}: {g.Status.ToSpec()} — {g.Reason}"));
                Summary = $"{_result.Timeline.Count:N0} evenimente · {_result.Findings.Count(f => f.Severity == Severity.Critical)} critice · " +
                          $"{_result.Findings.Count(f => f.Severity == Severity.High)} ridicate · {_result.Findings.Count} constatări · {_result.Gaps.Count} goluri · caz {_result.Case.Info.CaseId}";
                Log += "Gata. Dublu-click pe o constatare sau pe un eveniment pentru detalii." + Environment.NewLine;
            }
            catch (OperationCanceledException) { Log += "Oprit de operator." + Environment.NewLine; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log += "Eroare: " + ex.Message + Environment.NewLine;
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void Cancel() => _cts?.Cancel();

        private void FillTimeline()
        {
            Timeline.Clear();
            if (_result is null) return;
            IEnumerable<TimelineEvent> q = _result.Timeline;
            var flt = TimelineFilter.Trim();
            if (flt.Length > 0)
                q = q.Where(e => e.Summary.Contains(flt, StringComparison.OrdinalIgnoreCase) || e.Path.Contains(flt, StringComparison.OrdinalIgnoreCase) ||
                                 e.Process.Contains(flt, StringComparison.OrdinalIgnoreCase) || e.User.Contains(flt, StringComparison.OrdinalIgnoreCase) ||
                                 e.EventId == flt || e.RemoteIp.Contains(flt, StringComparison.OrdinalIgnoreCase) || e.Dns.Contains(flt, StringComparison.OrdinalIgnoreCase));
            foreach (var e in q.Take(20000)) Timeline.Add(e);
        }

        [RelayCommand]
        private void ExportPdf()
        {
            if (_result is null) return;
            var dlg = new SaveFileDialog { FileName = $"Raport_{_result.Case.Info.CaseId}.pdf", Filter = "PDF (*.pdf)|*.pdf", InitialDirectory = _result.Case.Root };
            if (dlg.ShowDialog() != true) return;
            InvestigationReportPdf.Write(_result, dlg.FileName, $"{Environment.UserDomainName}\\{Environment.UserName}");
            _result.Case.Audit("report.pdf", dlg.FileName);
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenCase()
        {
            if (_result is not null) Process.Start(new ProcessStartInfo("explorer.exe", _result.Case.Root) { UseShellExecute = true });
        }
    }
}
