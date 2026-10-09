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

        // WP6a (U4): the finding chosen in the grid and its card. The card is built from the finding and the case data by LogAnalyzer.Dfir.Presentation; selecting changes nothing in the case.
        private LogAnalyzer.Dfir.Presentation.EvidenceContext _evidenceContext = LogAnalyzer.Dfir.Presentation.EvidenceContext.Empty;
        [ObservableProperty] private Finding? _selectedFinding;
        [ObservableProperty] private FindingCardViewModel? _selectedCard;

        partial void OnSelectedFindingChanged(Finding? value) => SelectedCard = value is null ? null : new FindingCardViewModel(
            LogAnalyzer.Dfir.Presentation.FindingCardModel.Build(value, _evidenceContext, _result?.Verification?.Of(value.FindingId)?.CheckLines()));

        /// <summary>The case data the card looks evidence details up in. A case file that cannot be read leaves the details "necunoscut în caz" on the card; nothing is guessed.</summary>
        private static LogAnalyzer.Dfir.Presentation.EvidenceContext BuildEvidenceContext(InvestigationResult r)
        {
            IReadOnlyList<EvidenceItem> items;
            try { items = r.Case.LoadEvidence(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { items = []; }
            return new LogAnalyzer.Dfir.Presentation.EvidenceContext { Items = items, Timeline = r.Timeline, Parsing = r.Parsing, Gaps = r.Gaps };
        }
        /// <summary>WP14a: the findings of the "Air-gap integrity" category (Finding.AirGap carries channel, authorised?, who, when, object, classification, direction, destination, evidence).</summary>
        public ObservableCollection<Finding> AirGapFindings { get; } = new();
        [ObservableProperty] private string _airGapSummary = "Integritate air-gap: nedefinit (nicio analiză încă).";
        /// <summary>WP14b: the findings of the "Combined sequences" category (control gap + media, SMB + staging + USB, portable software + archive + USB); Finding.Sequence carries the ordered steps.</summary>
        public ObservableCollection<Finding> SequenceFindings { get; } = new();
        [ObservableProperty] private string _sequenceSummary = "Secvențe combinate: nedefinit (nicio analiză încă).";
        [ObservableProperty] private string _sequenceSteps = "";
        /// <summary>WP14a: the zone of the analysed system, as the operator declares it (compared with the zone in the media register).</summary>
        [ObservableProperty] private string _systemZone = "";
        [ObservableProperty] private string _registerLine = "Registre: nedefinit (nicio analiză încă).";
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

        /// <summary>Raised when the page's case state is complete: after the integrity re-check of a run, or after an existing case was opened (WP5 Home refreshes from it).</summary>
        public event EventHandler? StateChanged;

        /// <summary>WP5: the existing case shown on this page (null for a result of a run made here).</summary>
        public LogAnalyzer.Dfir.Windows.Investigation.LoadedCase? Loaded { get; private set; }

        /// <summary>True while an opened case is sealed, archived, invalidated or failed its integrity re-check: the page shows it for reading only.</summary>
        [ObservableProperty] private bool _isReadOnlyCase;
        [ObservableProperty] private string _readOnlyNotice = "";

        private static string VerificationText(InvestigationResult r) => r.Verification is { } v
            ? v.BannerRomanian + " (verificare automată, nu externă)" + (v.Warning is { } w ? Environment.NewLine + w : "")
            : "Verificare: nerulată pentru această analiză (nedeterminat).";

        /// <summary>
        /// WP5 (R9.1): shows an existing case, opened by <see cref="LogAnalyzer.Dfir.Windows.Investigation.CaseLoader"/>, on the same page and with the same
        /// collections as a fresh run. Read-only cases say why; what could not be restored is listed in the log, never hidden.
        /// </summary>
        public void ShowLoaded(LogAnalyzer.Dfir.Windows.Investigation.LoadedCase loaded)
        {
            ShowResult(loaded.Result);
            Loaded = loaded;
            IntegrityLine = loaded.Recheck?.Summary ?? "Reverificare: nerulată (nedeterminat).";
            VerificationLine = VerificationText(loaded.Result);
            OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[loaded.Result.State] + " — " + loaded.Result.StateReason;
            Summary += $" · caz deschis, stare: {LogAnalyzer.Dfir.Case.CaseLifecycleNames.Label(loaded.Lifecycle)}";
            IsReadOnlyCase = loaded.ReadOnly;
            ReadOnlyNotice = loaded.ReadOnly ? "Caz deschis doar pentru citire: " + string.Join("; ", loaded.ReadOnlyReasons) : "";
            Log = $"Caz deschis: {loaded.Workspace.Info.CaseId} ({loaded.Workspace.Root}).{Environment.NewLine}" + string.Concat(loaded.Notes.Select(n => "Notă: " + n + Environment.NewLine));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

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

        // ───────── WP18 S4: "Primește probe de la o stație", in three steps ─────────

        private readonly LogAnalyzer.Dfir.Flow.GuidedFlow _intake = new("Primește probe de la o stație",
        [
            new LogAnalyzer.Dfir.Flow.FlowStep("folder", "De unde vin probele?", "Alegeți folderul (sau suportul) adus de la stația afectată. Nimic nu se copiază încă."),
            new LogAnalyzer.Dfir.Flow.FlowStep("found", "Ce s-a găsit?", "Ce conține folderul, ce lipsește și ce nu este probă. Integritatea se calculează la import."),
            new LogAnalyzer.Dfir.Flow.FlowStep("scope", "Scopul cazului", "De ce, pentru ce perioadă, pe ce sisteme, cine aprobă. Fără scop nu se creează cazul."),
        ]);
        [ObservableProperty] private string _intakeFolder = "";
        [ObservableProperty] private IncomingEvidenceScan? _intakeScan;
        [ObservableProperty] private string _intakeError = "";
        public ObservableCollection<IncomingFamily> IntakeFamilies { get; } = new();
        public string IntakeTitle => _intake.Title;
        public string IntakeProgress => _intake.ProgressText;
        public int IntakeStepIndex => _intake.StepIndex;
        public string IntakeStepTitle => _intake.Current.Title;
        public string IntakeQuestion => _intake.Current.Question;
        public bool IntakeCanGoBack => _intake.CanGoBack;
        public bool IntakeStopped => _intake.Stopped;
        public string IntakeNextText => _intake.IsLastStep ? "Pornește analiza" : "Înainte";

        private void RaiseIntake()
        {
            foreach (var n in new[] { nameof(IntakeProgress), nameof(IntakeStepIndex), nameof(IntakeStepTitle), nameof(IntakeQuestion), nameof(IntakeCanGoBack), nameof(IntakeStopped), nameof(IntakeNextText) })
                OnPropertyChanged(n);
            IntakeError = _intake.LastError;
        }

        private string? ValidateIntakeStep() => _intake.StepIndex switch
        {
            0 => string.IsNullOrWhiteSpace(IntakeFolder) || !Directory.Exists(IntakeFolder) ? "Alegeți folderul cu probele aduse de la stație." : null,
            1 => IntakeScan is { HasAnything: true } ? null : "Folderul nu conține probe pe care aplicația să le poată importa; alegeți alt folder.",
            _ => BuildScope().MissingFields() is { Count: > 0 } m ? "Scopul cazului este incomplet; câmpuri lipsă: " + string.Join(", ", m) : null,
        };

        [RelayCommand]
        private void IntakePickFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Folderul (sau suportul) cu probele aduse de la stația afectată" };
            if (dlg.ShowDialog() == true) IntakeFolder = dlg.FolderName;
        }

        [RelayCommand]
        private async Task IntakeNext()
        {
            if (_intake.Completed) { _intake.Restart(); RaiseIntake(); return; }
            var error = ValidateIntakeStep();
            if (error is not null) { IntakeError = error; return; }
            bool wasLast = _intake.IsLastStep;
            if (_intake.StepIndex == 0)
            {
                IntakeScan = IncomingEvidence.Scan(IntakeFolder);
                IntakeFamilies.Clear(); foreach (var fam in IntakeScan.Families) IntakeFamilies.Add(fam);
                ImportFiles.Clear(); foreach (var f in IntakeScan.Importable) ImportFiles.Add(f);
                CollectFromThisStation = false;
                Log += $"Folder scanat: {IntakeScan.Summary}{Environment.NewLine}";
            }
            _intake.Next();
            RaiseIntake();
            if (wasLast && _intake.Completed) await Run();
        }

        [RelayCommand] private void IntakeBack() { _intake.Back(); RaiseIntake(); }
        [RelayCommand] private void IntakeStop() { _intake.Stop(); RaiseIntake(); Log += "Primirea probelor a fost oprită; ce ați ales rămâne." + Environment.NewLine; }
        [RelayCommand] private void IntakeRestart() { _intake.Restart(); IntakeScan = null; IntakeFamilies.Clear(); RaiseIntake(); }

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
            var zone = SystemZone.Trim();
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
                    return new InvestigationPipeline().Run(ws, profile, collect, progress, _cts.Token, systemZone: zone);
                });
                ShowResult(_result);
                var checkedCase = _result.Case;
                var ct = _cts.Token;
                IntegrityLine = (await Task.Run(() => checkedCase.Recheck(ct))).Summary;
                VerificationLine = VerificationText(_result);
                OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[_result.State] + " — " + _result.StateReason;
                Summary += $" · analiză: {LogAnalyzer.Dfir.Analysis.StateLabels.RomanianOperation[_result.State]} (nu verifică constatările)";
                Loaded = null; IsReadOnlyCase = false; ReadOnlyNotice = "";
                StateChanged?.Invoke(this, EventArgs.Empty);
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

        /// <summary>Fills every collection and summary line of the page from a result: a run that just finished or an existing case that was opened (WP5).</summary>
        private void ShowResult(InvestigationResult result)
        {
            _result = result;
            SelectedFinding = null;
            _evidenceContext = BuildEvidenceContext(result);
            Findings.Clear();
            foreach (var f in _result.Findings) Findings.Add(f);
            AirGapFindings.Clear();
            foreach (var f in _result.Findings.Where(f => f.Category == LogAnalyzer.Dfir.Analysis.Wp14Rules.Category)) AirGapFindings.Add(f);
            SequenceFindings.Clear();
            foreach (var f in _result.Findings.Where(f => f.Category == LogAnalyzer.Dfir.Analysis.SequenceRules.Category)) SequenceFindings.Add(f);
            SequenceSummary = SequenceFindings.Count == 0
                ? "Secvențe combinate: nicio secvență observată în sursele colectate; asta nu dovedește că nu s-a întâmplat nimic (o sursă necolectată dă „pas neobservat”, nu absență)."
                : $"Secvențe combinate: {SequenceFindings.Count} ({SequenceFindings.Count(a => a.Severity >= Severity.High)} ridicate). Fiecare arată faptele observate, în ordine, și pașii neobservați; nu stabilește scopul.";
            SequenceSteps = string.Join(Environment.NewLine + Environment.NewLine, SequenceFindings.Select(f =>
                $"{f.Title} [{f.Severity.ToSpec()}] — {f.FindingId}{Environment.NewLine}" +
                string.Join(Environment.NewLine, (f.Sequence?.Steps ?? []).Select(st => st.Observed
                    ? $"  {st.Order}. {st.WhenUtc:yyyy-MM-dd HH:mm} UTC — {st.Name}: {st.Note} [cont: {(st.Account.Length > 0 ? st.Account : "necunoscut în sursă")}; sursa: {st.Source}]"
                    : $"  {st.Order}. {st.Name}: {st.Note}")) +
                $"{Environment.NewLine}  Legătură: {f.Sequence?.Link}{Environment.NewLine}  Constatări componente: {string.Join(", ", f.RelatedFindingIds)}"));
            RegisterLine = _result.RegisterLine;
            AirGapSummary = AirGapFindings.Count == 0
                ? "Integritate air-gap: nu s-a observat nimic în sursele colectate; asta nu dovedește că nu s-a întâmplat nimic (jurnalele au o istorie limitată; unele canale pot lipsi, vezi „Goluri de probă”)."
                : $"Integritate air-gap: {AirGapFindings.Count} constatări ({AirGapFindings.Count(a => a.Severity >= Severity.High)} ridicate).";
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
        }

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
            InvestigationReportPdf.Write(_result, dlg.FileName, LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified);
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
            if (IsReadOnlyCase) { ClosureText = "Cazul este deschis doar pentru citire: nu se mai poate închide din nou."; return; }
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
