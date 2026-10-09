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
using LogAnalyzer.Dfir.Language;
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
        [ObservableProperty] private string _airGapSummary = Loc.T("inv.vm.airgap_idle");
        /// <summary>WP14b: the findings of the "Combined sequences" category (control gap + media, SMB + staging + USB, portable software + archive + USB); Finding.Sequence carries the ordered steps.</summary>
        public ObservableCollection<Finding> SequenceFindings { get; } = new();
        [ObservableProperty] private string _sequenceSummary = Loc.T("inv.vm.sequence_idle");
        [ObservableProperty] private string _sequenceSteps = "";
        /// <summary>WP14a: the zone of the analysed system, as the operator declares it (compared with the zone in the media register).</summary>
        [ObservableProperty] private string _systemZone = "";
        [ObservableProperty] private string _registerLine = Loc.T("inv.vm.register_idle");
        public ObservableCollection<TimelineEvent> Timeline { get; } = new();
        public ObservableCollection<LogAnalyzer.Dfir.Analysis.AntiForensicCheck> AntiForensics { get; } = new();
        /// <summary>WP15b: "Cronologie politici" (same lines as the investigation PDF).</summary>
        public ObservableCollection<LogAnalyzer.Dfir.Analysis.PolicyReportLine> PolicyTimelineLines { get; } = new();
        [ObservableProperty] private string _policyTimelineSummary = Loc.T("inv.vm.policy_idle");
        public ObservableCollection<LogAnalyzer.Dfir.Graph.Entity> GraphEntities { get; } = new();
        public ObservableCollection<LogAnalyzer.Dfir.Graph.GraphEdgeRow> GraphEdges { get; } = new();

        // Evidence Graph explorer: find entities, show an entity's edges with their evidence or derivation, the path between two.
        [ObservableProperty] private string _graphSearch = "";
        [ObservableProperty] private LogAnalyzer.Dfir.Graph.Entity? _selectedEntity;
        [ObservableProperty] private string _pathFrom = "";
        [ObservableProperty] private string _pathTo = "";
        [ObservableProperty] private string _graphStatus = Loc.T("inv.vm.graph_idle");

        partial void OnGraphSearchChanged(string value) => FillGraphEntities();

        partial void OnSelectedEntityChanged(LogAnalyzer.Dfir.Graph.Entity? value)
        {
            GraphEdges.Clear();
            if (_result?.Graph is not { } g || value is null) return;
            foreach (var r in LogAnalyzer.Dfir.Graph.GraphExplorer.EdgesOf(g, value.Id)) GraphEdges.Add(r);
            GraphStatus = Loc.Format("inv.vm.graph_entity", value.Type, value.Label, GraphEdges.Count);
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
            if (_result?.Graph is not { } g) { GraphStatus = Loc.T("inv.vm.graph_run_first"); return; }
            var a = LogAnalyzer.Dfir.Graph.GraphExplorer.Search(g, PathFrom, 1).FirstOrDefault();
            var b = LogAnalyzer.Dfir.Graph.GraphExplorer.Search(g, PathTo, 1).FirstOrDefault();
            if (a is null || b is null) { GraphStatus = Loc.T("inv.vm.graph_not_found"); return; }
            var path = LogAnalyzer.Dfir.Graph.GraphExplorer.PathBetween(g, a.Id, b.Id);
            foreach (var r in path) GraphEdges.Add(r);
            GraphStatus = path.Count == 0 ? Loc.Format("inv.vm.graph_not_linked", a.Label, b.Label) : Loc.Format("inv.vm.graph_path", a.Label, b.Label, path.Count);
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
            ? v.BannerRomanian + Loc.T("inv.vm.verification_suffix") + (v.Warning is { } w ? Environment.NewLine + w : "")
            : Loc.T("inv.vm.verification_not_run");

        /// <summary>
        /// WP5 (R9.1): shows an existing case, opened by <see cref="LogAnalyzer.Dfir.Windows.Investigation.CaseLoader"/>, on the same page and with the same
        /// collections as a fresh run. Read-only cases say why; what could not be restored is listed in the log, never hidden.
        /// </summary>
        public void ShowLoaded(LogAnalyzer.Dfir.Windows.Investigation.LoadedCase loaded)
        {
            ShowResult(loaded.Result);
            Loaded = loaded;
            IntegrityLine = loaded.Recheck?.Summary ?? Loc.T("inv.vm.recheck_not_run");
            VerificationLine = VerificationText(loaded.Result);
            OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(loaded.Result.State) + " — " + loaded.Result.StateReason;
            Summary += Loc.Format("inv.vm.case_state", LogAnalyzer.Dfir.Case.CaseLifecycleNames.Label(loaded.Lifecycle));
            IsReadOnlyCase = loaded.ReadOnly;
            ReadOnlyNotice = loaded.ReadOnly ? Loc.Format("inv.vm.case_read_only", string.Join("; ", loaded.ReadOnlyReasons)) : "";
            Log = Loc.Format("inv.vm.case_opened", loaded.Workspace.Info.CaseId, loaded.Workspace.Root) + Environment.NewLine + string.Concat(loaded.Notes.Select(n => Loc.Format("inv.vm.note", n) + Environment.NewLine));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public InvestigationViewModel(Func<InvestigationViewModel, object?>? aiFactory = null)
        {
            AiAnalysis = aiFactory?.Invoke(this);
            Loc.LanguageChanged += (_, _) => OnUi(RefreshLanguage);
        }

        private static void OnUi(Action a)
        {
            if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.Invoke(a); else a();
        }

        /// <summary>
        /// WP6b: the language changed. The texts that are built from the result (summaries, state line, verification line, finding card and grid labels) are built
        /// again in the new language; with no result the idle texts are set. Texts that were written as a one-off message (the log, remote-collection status, closure text)
        /// stay in the language they were written in.
        /// </summary>
        public void RefreshLanguage()
        {
            if (_result is null)
            {
                AirGapSummary = Loc.T("inv.vm.airgap_idle"); SequenceSummary = Loc.T("inv.vm.sequence_idle"); RegisterLine = Loc.T("inv.vm.register_idle");
                PolicyTimelineSummary = Loc.T("inv.vm.policy_idle"); GraphStatus = Loc.T("inv.vm.graph_idle");
                if (!IsBusy) OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(OperationState.NotStarted);
                return;
            }
            var selected = SelectedFinding;
            RenderTexts(_result);
            VerificationLine = VerificationText(_result);
            OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(_result.State) + " — " + _result.StateReason;
            Summary += Loaded is { } l
                ? Loc.Format("inv.vm.case_state", LogAnalyzer.Dfir.Case.CaseLifecycleNames.Label(l.Lifecycle))
                : Loc.Format("inv.vm.analysis_suffix", LogAnalyzer.Dfir.Analysis.StateLabels.Operation(_result.State));
            if (Loaded is { ReadOnly: true } ro) ReadOnlyNotice = Loc.Format("inv.vm.case_read_only", string.Join("; ", ro.ReadOnlyReasons));
            // the grid labels come from converters: put the rows in again so they are read again, and rebuild the card
            Findings.Clear();
            foreach (var f in _result.Findings) Findings.Add(f);
            SelectedFinding = null;
            SelectedFinding = selected;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        [ObservableProperty] private string _remoteHost = "";
        [ObservableProperty] private string _remoteJustification = "";
        [ObservableProperty] private string _remoteStatus = Loc.T("inv.vm.remote_idle");

        public LocChoice[] Profiles { get; } = { new("inv.vm.profile_quick"), new("inv.vm.profile_standard"), new("inv.vm.profile_full") };
        [ObservableProperty] private int _profileIndex = 1;
        [ObservableProperty] private bool _collectFromThisStation = true;
        // Case scope (owner decision 23): mandatory; the case is not created while a field is missing. Nothing is pre-filled on the operator's behalf.
        public LocChoice[] LegalBases { get; } = { new("inv.vm.basis_incident"), new("inv.vm.basis_audit"), new("inv.vm.basis_control") };
        public LocChoice[] NetworkCategories { get; } = { new("inv.vm.net_airgap"), new("inv.vm.net_standalone"), new("inv.vm.net_connected") };
        public LocChoice[] ClassificationLevels { get; } = { new("inv.vm.class_classified"), new("inv.vm.class_unclassified") };
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

        [ObservableProperty] private string _caseName = Loc.Format("inv.vm.default_case_name", Environment.MachineName, DateTime.Now.ToString("yyyy-MM-dd"));
        [ObservableProperty] private string _log = "";
        [ObservableProperty] private bool _isBusy;
        /// <summary>Operation state of the last run (UX contract §20), in Romanian. "Finalizat" does not mean any finding is verified.</summary>
        [ObservableProperty] private string _operationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(OperationState.NotStarted);
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
            var dlg = new OpenFileDialog { Multiselect = true, Filter = Loc.T("inv.vm.dialog_files_filter") };
            if (dlg.ShowDialog() != true) return;
            foreach (var f in dlg.FileNames) if (!ImportFiles.Contains(f)) ImportFiles.Add(f);
        }

        [RelayCommand]
        private void AddFolder()
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("inv.vm.dialog_folder") };
            if (dlg.ShowDialog() != true) return;
            foreach (var f in Directory.EnumerateFiles(dlg.FolderName, "*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pf", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase)))
                if (!ImportFiles.Contains(f)) ImportFiles.Add(f);
            Log += Loc.Format("inv.vm.files_selected", ImportFiles.Count) + Environment.NewLine;
        }

        [RelayCommand]
        private void ClearFiles() => ImportFiles.Clear();

        [RelayCommand]
        private async Task Run()
        {
            if (!CollectFromThisStation && ImportFiles.Count == 0) { Log += Loc.T("inv.vm.choose_source") + Environment.NewLine; return; }
            var scope = BuildScope();
            var missingScope = scope.MissingFields();
            if (missingScope.Count > 0)
            {
                Log += Loc.Format("inv.vm.scope_incomplete", string.Join(", ", missingScope)) + Environment.NewLine;
                return;
            }
            LogAnalyzer.UI.Services.LiveCase.Configure(scope);
            IsBusy = true;
            OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(OperationState.Running);
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
                        ((IProgress<string>)progress).Report(Loc.Format("inv.vm.import_progress", files.Count));
                        InvestigationPipeline.Import(ws, files);
                    }
                    return new InvestigationPipeline().Run(ws, profile, collect, progress, _cts.Token, systemZone: zone);
                });
                ShowResult(_result);
                var checkedCase = _result.Case;
                var ct = _cts.Token;
                IntegrityLine = (await Task.Run(() => checkedCase.Recheck(ct))).Summary;
                VerificationLine = VerificationText(_result);
                OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(_result.State) + " — " + _result.StateReason;
                Summary += Loc.Format("inv.vm.analysis_suffix", LogAnalyzer.Dfir.Analysis.StateLabels.Operation(_result.State));
                Loaded = null; IsReadOnlyCase = false; ReadOnlyNotice = "";
                StateChanged?.Invoke(this, EventArgs.Empty);
                Log += Loc.T("inv.vm.done") + Environment.NewLine;
            }
            catch (OperationCanceledException) { OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(OperationState.Cancelled); Log += Loc.T("inv.vm.stopped") + Environment.NewLine; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                OperationStatus = LogAnalyzer.Dfir.Analysis.StateLabels.Operation(OperationState.Failed) + " — " + ex.Message;
                Log += Loc.Format("inv.vm.error", ex.Message) + Environment.NewLine;
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
            RenderTexts(result);
            RegisterLine = _result.RegisterLine;
            ResultChanged?.Invoke(this, EventArgs.Empty);
            FillTimeline();
            GapsText = string.Join(Environment.NewLine, _result.Gaps.Select(g => $"{g.Artifact}: {g.Status.ToSpec()} — {g.Reason}"));
            AntiForensics.Clear();
            foreach (var a in _result.AntiForensics.OrderBy(a => a.Result).ThenBy(a => a.Id)) AntiForensics.Add(a);
            PolicyTimelineLines.Clear();
            if (_result.PolicyTimeline is { } policyTimeline) foreach (var pl in LogAnalyzer.Dfir.Analysis.PolicyTimelineReport.Lines(policyTimeline)) PolicyTimelineLines.Add(pl);
            PolicyTimelineSummary = _result.PolicyTimelineLine;
            FillGraphEntities();
        }

        /// <summary>The summary texts of the page (WP6b): built from the result with the resource layer, so they can be built again when the language changes.</summary>
        private void RenderTexts(InvestigationResult result)
        {
            _result = result;
            SequenceSummary = SequenceFindings.Count == 0
                ? Loc.T("inv.vm.sequence_none")
                : Loc.Format("inv.vm.sequence_count", SequenceFindings.Count, SequenceFindings.Count(a => a.Severity >= Severity.High));
            SequenceSteps = string.Join(Environment.NewLine + Environment.NewLine, SequenceFindings.Select(f =>
                $"{f.Title} [{f.Severity.ToSpec()}] — {f.FindingId}{Environment.NewLine}" +
                string.Join(Environment.NewLine, (f.Sequence?.Steps ?? []).Select(st => st.Observed
                    ? Loc.Format("inv.vm.sequence_step_observed", st.Order, st.WhenUtc, st.Name, st.Note, st.Account.Length > 0 ? st.Account : Loc.T("inv.vm.sequence_account_unknown"), st.Source)
                    : $"  {st.Order}. {st.Name}: {st.Note}")) +
                Environment.NewLine + "  " + Loc.Format("inv.vm.sequence_link", f.Sequence?.Link) + Environment.NewLine + "  " + Loc.Format("inv.vm.sequence_components", string.Join(", ", f.RelatedFindingIds))));
            AirGapSummary = AirGapFindings.Count == 0
                ? Loc.T("inv.vm.airgap_none")
                : Loc.Format("inv.vm.airgap_count", AirGapFindings.Count, AirGapFindings.Count(a => a.Severity >= Severity.High));
            var chains = _result.Findings.Where(f => f.RuleId == "INCIDENT-CHAIN").ToList();
            Chains = chains.Count == 0 ? Loc.T("inv.vm.no_chain") :
                string.Join(Environment.NewLine + Environment.NewLine, chains.Select(c => $"{c.Title} [{c.Severity.ToSpec()}]{Environment.NewLine}" +
                    string.Join(Environment.NewLine, c.Description.Split(" → ").Select(s => "  → " + s))));
            GraphStatus = _result.Graph is { } gr ? Loc.Format("inv.vm.graph_summary", gr.Entities.Count, gr.Relationships.Count) : Loc.T("inv.vm.graph_not_built");
            Summary = Loc.Format("inv.vm.summary", _result.Timeline.Count, _result.Findings.Count(f => f.Severity == Severity.Critical),
                _result.Findings.Count(f => f.Severity == Severity.High), _result.Findings.Count, _result.Gaps.Count, _result.Case.Info.CaseId);
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
                if (dlg.ShowDialog() != true) { RemoteStatus = Loc.Format("inv.vm.remote_declined", req.RequestId); return; }
                File.WriteAllText(dlg.FileName, RemoteCollection.PackageScript(req, sha), new System.Text.UTF8Encoding(true));
                RemoteStatus = Loc.Format("inv.vm.remote_authorised", req.RequestId, req.TargetHost, Path.GetFileName(dlg.FileName));
            }
            catch (ArgumentException ex) { RemoteStatus = Loc.Format("inv.vm.remote_stopped", ex.Message); }
        }

        [RelayCommand]
        private void ImportRemotePackage()
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("inv.vm.dialog_package") };
            if (dlg.ShowDialog() != true) return;
            var ws = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
            if (ws is null) { RemoteStatus = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return; }
            IntegrityLine = LogAnalyzer.UI.Services.LiveCase.IntegrityLine ?? "";
            var check = RemoteCollection.Verify(ws, dlg.FolderName);
            if (!check.Ok) { RemoteStatus = Loc.Format("inv.vm.remote_not_imported", string.Join("; ", check.Problems)); return; }
            var items = RemoteCollection.Import(ws, check);
            foreach (var i in items.Skip(1)) { var f = ws.FullPath(i.StoredPath); if (!ImportFiles.Contains(f)) ImportFiles.Add(f); }
            var failed = check.Manifest!.Steps.Where(s => s.Status != "ok").Select(s => $"{s.Artifact} ({s.Error})").ToList();
            RemoteStatus = Loc.Format("inv.vm.remote_imported", items.Count - 1, check.Manifest.Host) +
                           (failed.Count > 0 ? Loc.Format("inv.vm.remote_failed_steps", string.Join("; ", failed)) : "");
        }

        /// <summary>Text shown at case close (owner decision 30): the final chain heads to record in the custody register.</summary>
        [ObservableProperty] private string _closureText = "";

        /// <summary>"Închidere caz": audits the closure and shows the head hashes of the custody and audit chains; the operator copies them out of the case.</summary>
        [RelayCommand]
        private void CloseCase()
        {
            if (_result is null) { ClosureText = Loc.T("inv.vm.close_run_first"); return; }
            if (IsReadOnlyCase) { ClosureText = Loc.T("inv.vm.close_read_only"); return; }
            var closure = LogAnalyzer.Dfir.Case.CaseClosure.Close(_result.Case, $"{Environment.UserDomainName}\\{Environment.UserName}");
            ClosureText = closure.RegisterText;
            try { Clipboard.SetText(closure.RegisterText); ClosureText += Environment.NewLine + Loc.T("inv.vm.copied"); }
            catch (System.Runtime.InteropServices.COMException) { /* clipboard busy: the text stays on screen */ }
        }

        [RelayCommand]
        private void OpenCase()
        {
            if (_result is not null) Process.Start(new ProcessStartInfo("explorer.exe", _result.Case.Root) { UseShellExecute = true });
        }
    }
}
