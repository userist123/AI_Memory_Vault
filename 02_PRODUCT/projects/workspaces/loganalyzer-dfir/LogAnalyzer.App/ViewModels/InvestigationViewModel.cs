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
        public ObservableCollection<LogAnalyzer.Dfir.Analysis.AntiForensicCheck> AntiForensics { get; } = new();
        /// <summary>WP15b: "Cronologie politici" (same lines as the investigation PDF).</summary>
        public ObservableCollection<LogAnalyzer.Dfir.Analysis.PolicyReportLine> PolicyTimelineLines { get; } = new();
        [ObservableProperty] private string _policyTimelineSummary = "Cronologie politici: nedefinit (nicio analiză încă).";
        public ObservableCollection<LogAnalyzer.Dfir.Graph.Entity> GraphEntities { get; } = new();
        public ObservableCollection<LogAnalyzer.Dfir.Graph.GraphEdgeRow> GraphEdges { get; } = new();

        // Evidence Graph explorer: find entities, show an entity's edges with their evidence or derivation, the path between two.
        [ObservableProperty] private string _graphSearch = "";
        [ObservableProperty] private LogAnalyzer.Dfir.Graph.Entity? _selectedEntity;
        [ObservableProperty] private string _pathFrom = "";
        [ObservableProperty] private string _pathTo = "";
        [ObservableProperty] private string _graphStatus = "Rulați investigația; apoi căutați o entitate (fișier, domeniu, IP, serviciu, constatare…).";

        partial void OnGraphSearchChanged(string value) => FillGraphEntities();

        partial void OnSelectedEntityChanged(LogAnalyzer.Dfir.Graph.Entity? value)
        {
            GraphEdges.Clear();
            if (_result?.Graph is not { } g || value is null) return;
            foreach (var r in LogAnalyzer.Dfir.Graph.GraphExplorer.EdgesOf(g, value.Id)) GraphEdges.Add(r);
            GraphStatus = $"{value.Type} {value.Label}: {GraphEdges.Count} relații.";
        }

        private void FillGraphEntities()
        {
            GraphEntities.Clear();
            if (_result?.Graph is not { } g) return;
            foreach (var e in LogAnalyzer.Dfir.Graph.GraphExplorer.Search(g, GraphSearch)) GraphEntities.Add(e);
        }

        [RelayCommand]
        private void FindPath()
        {
            GraphEdges.Clear();
            if (_result?.Graph is not { } g) { GraphStatus = "Rulați întâi investigația."; return; }
            var a = LogAnalyzer.Dfir.Graph.GraphExplorer.Search(g, PathFrom, 1).FirstOrDefault();
            var b = LogAnalyzer.Dfir.Graph.GraphExplorer.Search(g, PathTo, 1).FirstOrDefault();
            if (a is null || b is null) { GraphStatus = "Nu găsesc una dintre entități."; return; }
            var path = LogAnalyzer.Dfir.Graph.GraphExplorer.PathBetween(g, a.Id, b.Id);
            foreach (var r in path) GraphEdges.Add(r);
            GraphStatus = path.Count == 0 ? $"{a.Label} și {b.Label} nu sunt legate în graf." : $"Drum {a.Label} → {b.Label}: {path.Count} relații (fiecare cu proba sau derivarea ei).";
        }

        // Remote collection packages. The optional AI layer is a separate screen of the unclassified edition (AiAnalysisViewModel).
        /// <summary>The optional AI screen's view model; null in the classified edition (no AI code in that build).</summary>
        public object? AiAnalysis { get; private set; }

        /// <summary>The finished investigation, for optional layers that read it (AI explanation). Null before a run.</summary>
        public InvestigationResult? CurrentResult => _result;

        /// <summary>Raised when a new investigation result is available.</summary>
        public event EventHandler? ResultChanged;

        public InvestigationViewModel(Func<InvestigationViewModel, object?>? aiFactory = null)
        {
            AiAnalysis = aiFactory?.Invoke(this);
        }

        [ObservableProperty] private string _remoteHost = "";
        [ObservableProperty] private string _remoteJustification = "";
        [ObservableProperty] private string _remoteStatus = "Pachetul rulează pe stația țintă (local, fără rețea), calculează SHA-256 acolo; îl aduceți pe suport extern și îl verificați aici înainte de import.";

        public string[] Profiles { get; } = { "Rapid (jurnale, Prefetch, stare live)", "Standard (+ SRUM)", "Complet" };
        [ObservableProperty] private int _profileIndex = 1;
        [ObservableProperty] private bool _collectFromThisStation = true;
        // Case scope (owner decision 23): mandatory; the case is not created while a field is missing. Nothing is pre-filled on the operator's behalf.
        public string[] LegalBases { get; } = { "Incident", "Audit", "Control" };
        public string[] NetworkCategories { get; } = { "Rețea air-gapped", "PC standalone", "Conectat" };
        public string[] ClassificationLevels { get; } = { "Clasificat", "Neclasificat" };
        [ObservableProperty] private string _scopePurpose = "";
        [ObservableProperty] private string _scopeApprover = "";
        /// <summary>Systems in scope, separated by comma or semicolon.</summary>
        [ObservableProperty] private string _scopeSystems = "";
        [ObservableProperty] private DateTime? _scopeFrom;
        [ObservableProperty] private DateTime? _scopeTo;
        [ObservableProperty] private int _legalBasisIndex = -1;
        [ObservableProperty] private int _networkIndex = -1;
        [ObservableProperty] private int _classificationIndex = -1;

        private CaseScope BuildScope() => new()
        {
            Purpose = ScopePurpose.Trim(),
            Approver = ScopeApprover.Trim(),
            SystemsInScope = ScopeSystems.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PeriodFromUtc = ScopeFrom is { } f ? new DateTimeOffset(DateTime.SpecifyKind(f.Date, DateTimeKind.Utc)) : null,
            PeriodToUtc = ScopeTo is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t.Date, DateTimeKind.Utc)).AddDays(1).AddTicks(-1) : null,
            LegalBasis = LegalBasisIndex switch { 0 => LegalBasis.Incident, 1 => LegalBasis.Audit, 2 => LegalBasis.Control, _ => LegalBasis.Unspecified },
            Network = NetworkIndex switch { 0 => NetworkCategory.AirGappedNetwork, 1 => NetworkCategory.StandalonePc, 2 => NetworkCategory.Connected, _ => NetworkCategory.Unspecified },
            Classification = ClassificationIndex switch { 0 => ClassificationLevel.Classified, 1 => ClassificationLevel.Unclassified, _ => ClassificationLevel.Unspecified },
        };

        [ObservableProperty] private string _caseName = $"Investigație {Environment.MachineName} {DateTime.Now:yyyy-MM-dd}";
        [ObservableProperty] private string _log = "";
        [ObservableProperty] private bool _isBusy;
        /// <summary>Operation state of the last run (UX contract §20), in Romanian. "Finalizat" does not mean any finding is verified.</summary>
        [ObservableProperty] private string _operationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[OperationState.NotStarted];
        [ObservableProperty] private string _summary = "";
        /// <summary>Verdict of the last integrity re-check of the case (WP3b): valid, modified n, missing n, chain broken or legacy. Reports only; nothing is repaired.</summary>
        [ObservableProperty] private string _integrityLine = "";
        /// <summary>WP4: verdict counts from the verification module (Analysis/verification.json) and, when any finding is CONTRADICTED or REJECTED, the warning.</summary>
        [ObservableProperty] private string _verificationLine = "";
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
            var scope = BuildScope();
            var missingScope = scope.MissingFields();
            if (missingScope.Count > 0)
            {
                Log += "Scopul cazului este incomplet; câmpuri lipsă: " + string.Join(", ", missingScope) + Environment.NewLine;
                return;
            }
            LogAnalyzer.UI.Services.LiveCase.Configure(scope);
            IsBusy = true;
            OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[OperationState.Running];
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
                    var ws = InvestigationPipeline.NewCase(casesRoot, name, scope);
                    if (files.Count > 0)
                    {
                        ((IProgress<string>)progress).Report($"Import {files.Count} fișiere (copii; originalele nu se modifică)");
                        InvestigationPipeline.Import(ws, files);
                    }
                    return new InvestigationPipeline().Run(ws, profile, collect, progress, _cts.Token);
                });
                Findings.Clear();
                foreach (var f in _result.Findings) Findings.Add(f);
                ResultChanged?.Invoke(this, EventArgs.Empty);
                FillTimeline();
                var chains = _result.Findings.Where(f => f.RuleId == "INCIDENT-CHAIN").ToList();
                Chains = chains.Count == 0 ? "Niciun lanț de incident (constatări grave grupate în timp)." :
                    string.Join(Environment.NewLine + Environment.NewLine, chains.Select(c => $"{c.Title} [{c.Severity.ToSpec()}]{Environment.NewLine}" +
                        string.Join(Environment.NewLine, c.Description.Split(" → ").Select(s => "  → " + s))));
                GapsText = string.Join(Environment.NewLine, _result.Gaps.Select(g => $"{g.Artifact}: {g.Status.ToSpec()} — {g.Reason}"));
                AntiForensics.Clear();
                foreach (var a in _result.AntiForensics.OrderBy(a => a.Result).ThenBy(a => a.Id)) AntiForensics.Add(a);
                PolicyTimelineLines.Clear();
                if (_result.PolicyTimeline is { } policyTimeline) foreach (var pl in LogAnalyzer.Dfir.Analysis.PolicyTimelineReport.Lines(policyTimeline)) PolicyTimelineLines.Add(pl);
                PolicyTimelineSummary = _result.PolicyTimelineLine;
                FillGraphEntities();
                GraphStatus = _result.Graph is { } gr ? $"Graf: {gr.Entities.Count} entități, {gr.Relationships.Count} relații." : "Graful nu a fost construit.";
                Summary = $"{_result.Timeline.Count:N0} evenimente · {_result.Findings.Count(f => f.Severity == Severity.Critical)} critice · " +
                          $"{_result.Findings.Count(f => f.Severity == Severity.High)} ridicate · {_result.Findings.Count} constatări · {_result.Gaps.Count} goluri · caz {_result.Case.Info.CaseId}";
                var checkedCase = _result.Case;
                var ct = _cts.Token;
                IntegrityLine = (await Task.Run(() => checkedCase.Recheck(ct))).Summary;
                VerificationLine = _result.Verification is { } v
                    ? v.Banner + " (verificare automată, nu externă)" + (v.Warning is { } w ? Environment.NewLine + w : "")
                    : "Verificare: nerulată pentru această analiză.";
                OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[_result.State] + " — " + _result.StateReason;
                Summary += $" · analiză: {LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[_result.State]} (nu verifică constatările)";
                Log += "Gata. Dublu-click pe o constatare sau pe un eveniment pentru detalii." + Environment.NewLine;
            }
            catch (OperationCanceledException) { OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[OperationState.Cancelled]; Log += "Oprit de operator." + Environment.NewLine; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[OperationState.Failed] + " — " + ex.Message;
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
        private void GenerateRemotePackage()
        {
            try
            {
                var ws = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
                if (ws is null) { RemoteStatus = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return; }
            IntegrityLine = LogAnalyzer.UI.Services.LiveCase.IntegrityLine ?? "";
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var (req, sha) = RemoteCollection.Authorize(ws, RemoteHost, id.Name, RemoteJustification, RemoteCollection.KnownArtifacts);
                var dlg = new SaveFileDialog { FileName = $"Colectare_{req.TargetHost}.ps1", Filter = "PowerShell (*.ps1)|*.ps1" };
                if (dlg.ShowDialog() != true) { RemoteStatus = $"Cererea {req.RequestId} a fost autorizată, dar pachetul nu a fost salvat."; return; }
                File.WriteAllText(dlg.FileName, RemoteCollection.PackageScript(req, sha), new System.Text.UTF8Encoding(true));
                RemoteStatus = $"Cererea {req.RequestId} autorizată pentru {req.TargetHost} și înregistrată în caz. Rulați {Path.GetFileName(dlg.FileName)} ca administrator pe {req.TargetHost}; aduceți folderul LA_{req.TargetHost}_… înapoi și apăsați „Verifică și importă”.";
            }
            catch (ArgumentException ex) { RemoteStatus = "Oprit: " + ex.Message; }
        }

        [RelayCommand]
        private void ImportRemotePackage()
        {
            var dlg = new OpenFolderDialog { Title = "Folderul pachetului colectat (conține manifest.json)" };
            if (dlg.ShowDialog() != true) return;
            var ws = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
            if (ws is null) { RemoteStatus = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return; }
            IntegrityLine = LogAnalyzer.UI.Services.LiveCase.IntegrityLine ?? "";
            var check = RemoteCollection.Verify(ws, dlg.FolderName);
            if (!check.Ok) { RemoteStatus = "Pachetul NU a fost importat: " + string.Join("; ", check.Problems); return; }
            var items = RemoteCollection.Import(ws, check);
            foreach (var i in items.Skip(1)) { var f = ws.FullPath(i.StoredPath); if (!ImportFiles.Contains(f)) ImportFiles.Add(f); }
            var failed = check.Manifest!.Steps.Where(s => s.Status != "ok").Select(s => $"{s.Artifact} ({s.Error})").ToList();
            RemoteStatus = $"Importat: {items.Count - 1} fișiere de pe {check.Manifest.Host}, fiecare cu SHA-256 egal cu cel calculat pe țintă; adăugate la lista de probe." +
                           (failed.Count > 0 ? " Pași eșuați pe țintă: " + string.Join("; ", failed) : "");
        }

        /// <summary>Text shown at case close (owner decision 30): the final chain heads to record in the custody register.</summary>
        [ObservableProperty] private string _closureText = "";

        /// <summary>"Închidere caz": audits the closure and shows the head hashes of the custody and audit chains; the operator copies them out of the case.</summary>
        [RelayCommand]
        private void CloseCase()
        {
            if (_result is null) { ClosureText = "Rulați întâi o investigație; închiderea se face pe cazul curent."; return; }
            var closure = LogAnalyzer.Dfir.Case.CaseClosure.Close(_result.Case, $"{Environment.UserDomainName}\\{Environment.UserName}");
            ClosureText = closure.RegisterText;
            try { Clipboard.SetText(closure.RegisterText); ClosureText += Environment.NewLine + "(Copiat în clipboard.)"; }
            catch (System.Runtime.InteropServices.COMException) { /* clipboard busy: the text stays on screen */ }
        }

        [RelayCommand]
        private void OpenCase()
        {
            if (_result is not null) Process.Start(new ProcessStartInfo("explorer.exe", _result.Case.Root) { UseShellExecute = true });
        }
    }
}
