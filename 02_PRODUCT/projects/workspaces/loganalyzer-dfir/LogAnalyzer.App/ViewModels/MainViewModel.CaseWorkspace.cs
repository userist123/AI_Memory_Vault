using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Evaluation 2026-11-05: the advanced pages (Panou general, Firul atacului, Conturi locale și USB, Jurnale, Cronologie, MITRE) read a working
    /// database filled only by „Încarcă jurnale”, so next to a case with 67 123 events and a critical incident chain they showed 16 161 events,
    /// 0 alerts and „niciun atac”. Now the open case is their source: when a run ends or a case is opened, the working database is refilled from
    /// the case's own EVTX copies (read in place, no second custody import) and the alerts are the case's findings, not a second rule engine.
    /// </summary>
    public partial class MainViewModel
    {
        /// <summary>Says which data the advanced pages show; printed on them so the operator never mistakes one data set for another.</summary>
        [ObservableProperty] private string _workspaceSourceLine = NoCaseSourceLine;

        public const string NoCaseSourceLine = "Paginile avansate arată fișierele încărcate cu „Încarcă jurnale” (niciun caz deschis).";

        private CancellationTokenSource? _caseWorkspaceCts;
        private string _workspaceCaseId = "";

        private void OnInvestigationStateChanged(object? sender, EventArgs e)
        {
            if (Investigation.CurrentResult is { Case: not null } r) _ = LoadCaseIntoWorkspaceAsync(r);
        }

        /// <summary>The case EVTX copies the advanced pages read: inside the case folder only, existing, each path once.</summary>
        internal static IReadOnlyList<string> CaseEvtxFiles(LogAnalyzer.Dfir.Case.CaseWorkspace ws)
        {
            var files = new List<string>();
            foreach (var e in ws.LoadEvidence())
            {
                if (!e.StoredPath.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)) continue;
                if (ws.TryFullPath(e.StoredPath, out var full) && File.Exists(full)) files.Add(full);
            }
            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The case's findings as the alerts of the advanced pages (same ids, severity, technique and text as the case report).</summary>
        internal static List<DetectedIssue> IssuesFromFindings(IEnumerable<Finding> findings) => findings.Select(f => new DetectedIssue
        {
            Title = $"{f.FindingId} · {f.Title}",
            Severity = f.Severity switch
            {
                LogAnalyzer.Dfir.Model.Severity.Critical => "Critical",
                LogAnalyzer.Dfir.Model.Severity.High => "High",
                LogAnalyzer.Dfir.Model.Severity.Medium => "Medium",
                LogAnalyzer.Dfir.Model.Severity.Low => "Low",
                _ => "Info",
            },
            Explanation = f.Description,
            MitreTechniqueId = f.MitreTechniqueId ?? "",
            MitreTacticName = LogAnalyzer.Dfir.Language.RoLabels.Category(f.Category ?? ""),
            CreatedAt = (f.FirstSeenUtc ?? f.LastSeenUtc)?.LocalDateTime ?? DateTime.Now,
        }).ToList();

        internal static string CaseSourceLine(InvestigationResult r, int events, int problems) =>
            $"Paginile avansate arată cazul {r.Case!.Info.CaseId} („{r.Case.Info.Name}”): {events:N0} evenimente din copiile EVTX ale cazului și cele {r.Findings.Count:N0} constatări ale lui."
            + (problems > 0 ? $" {problems} fișiere EVTX nu au putut fi citite complet (vezi jurnalul aplicației)." : "");

        /// <summary>
        /// Refills the working database and the alerts from the case. Review 2026-11-05: loads and manual imports run one at a time behind
        /// <see cref="_workspaceGate"/> (a newer one cancels the older and waits for it, so two cases never share the store); the token reaches every
        /// 5000-event batch; any failure is shown in the banner, never left as „Se încarcă…”.
        /// </summary>
        internal async Task LoadCaseIntoWorkspaceAsync(InvestigationResult r)
        {
            _caseWorkspaceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _caseWorkspaceCts = cts;
            var ws = r.Case!;
            await _workspaceGate.WaitAsync();
            try
            {
                if (cts.IsCancellationRequested) return;
                WorkspaceSourceLine = $"Se încarcă în paginile avansate cazul {ws.Info.CaseId}…";
                Events.Clear(); RegistryArtifacts.Clear(); DetectedIssues.Clear(); TimelineItems.Clear();
                var errors = new List<string>();
                int saved = await Task.Run(() =>
                {
                    _databaseService.ClearDatabase();
                    int n = 0;
                    foreach (var f in CaseEvtxFiles(ws))
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        n += IngestEvtxFile(f, errors, cts.Token);
                    }
                    return n;
                }, cts.Token);
                if (cts.IsCancellationRequested) return;
                foreach (var i in IssuesFromFindings(r.Findings)) DetectedIssues.Add(i);
                _workspaceCaseId = ws.Info.CaseId;
                if (errors.Count > 0) _auditService.LogAction("CASE_WORKSPACE_ERRORS", string.Join(" | ", errors));
                EvtxCurrentPage = 1; RegistryCurrentPage = 1; TimelineCurrentPage = 1;
                ReloadEvtxFromDb(); ReloadRegistryFromDb(); ReloadTimelineFromDb(); ReloadDashboardStats(); UpdateDatabaseSize();
                ProvenanceStatusMessage = $"Cazul {ws.Info.CaseId}: {Investigation.IntegrityLine}";
                WorkspaceSourceLine = CaseSourceLine(r, saved, errors.Count);
                await RunAnalysisAsync(announce: false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                WorkspaceSourceLine = $"Cazul {ws.Info.CaseId} nu a putut fi încărcat în paginile avansate ({ex.GetType().Name}): {ex.Message}. Paginile avansate pot fi goale sau incomplete.";
                try { _auditService.LogAction("CASE_WORKSPACE_FAILED", $"{ws.Info.CaseId}: {ex.GetType().Name}: {ex.Message}"); } catch (Exception aex) when (aex is IOException or UnauthorizedAccessException) { }
            }
            finally { _workspaceGate.Release(); }
        }

        private readonly SemaphoreSlim _workspaceGate = new(1, 1);
        private bool _analysisRerun, _analysisRerunAnnounce;
    }
}