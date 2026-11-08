using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Investigation;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Master plan 6.16, „Rulări și diferențe” tab: the immutable runs of the case with their integrity verdict, and the difference between two
    /// runs chosen by the operator (by default the last two), with the likely source of each change. Reading only: nothing in the case changes here.
    /// </summary>
    public partial class InvestigationViewModel
    {
        public ObservableCollection<RunListItem> AnalysisRunRows { get; } = new();
        public ObservableCollection<string> RunDiffLines { get; } = new();
        [ObservableProperty] private string _runsLine = "Nicio rulare înregistrată în acest caz.";
        [ObservableProperty] private string _runDiffSummary = "";
        [ObservableProperty] private bool _runDiffNeedsReview;
        [ObservableProperty] private RunListItem? _runBefore;
        [ObservableProperty] private RunListItem? _runAfter;

        internal void RefreshRuns()
        {
            AnalysisRunRows.Clear(); RunDiffLines.Clear(); RunDiffSummary = ""; RunDiffNeedsReview = false;
            if (_result?.Case is not { } ws) { RunsLine = "Niciun caz deschis."; return; }
            try
            {
                foreach (var r in AnalysisRuns.List(ws)) AnalysisRunRows.Add(r);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                RunsLine = "Rulările nu au putut fi citite: " + ex.Message; return;
            }
            int bad = AnalysisRunRows.Count(r => !r.IntegrityOk);
            RunsLine = AnalysisRunRows.Count == 0 ? "Nicio rulare înregistrată în acest caz (cazurile analizate înainte de această versiune nu au manifest de rulare)."
                : $"{LogAnalyzer.Dfir.Language.RoText.Count(AnalysisRunRows.Count, "rulare", "rulări")}, " + (bad == 0 ? "toate intacte" : $"{bad} cu probleme de integritate");
            var ok = AnalysisRunRows.Where(r => r.IntegrityOk).ToList();
            RunAfter = ok.LastOrDefault();
            RunBefore = ok.Count >= 2 ? ok[^2] : null;
            CompareRuns();
        }

        /// <summary>
        /// Master plan 6.16 replay: the pipeline again on the evidence already in the open case, with no new collection. The latest outputs are
        /// rewritten (custody keeps every version); the earlier run stays recorded and the tab compares the two. Refused on a read-only case and
        /// on a case the workflow has closed (reopen it first: a closed case does not change silently).
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task Reanalyze()
        {
            if (_result?.Case is not { } ws) { RunDiffSummary = "Deschideți întâi un caz."; return; }
            if (IsBusy) return;
            if (ws.IsReadOnly) { RunDiffSummary = "Cazul este doar pentru citire (" + ws.ReadOnlyReason + "): nu se reanalizează."; return; }
            try
            {
                if (new CaseWorkflow(ws).Load().Stage == CaseStage.Closed) { RunDiffSummary = "Cazul este închis în fluxul de lucru: redeschideți-l întâi (aprobatorul, cu motiv)."; return; }
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { RunDiffSummary = "Fluxul de lucru nu a putut fi citit: " + ex.Message; return; }
            IsBusy = true;
            _cts = new System.Threading.CancellationTokenSource();
            var progress = new Progress<string>(m => Log += $"{DateTime.Now:HH:mm:ss}  {m}{Environment.NewLine}");
            var zone = SystemZone.Trim();
            var ct = _cts.Token;
            try
            {
                Log += "Reanaliză pe probele din caz (fără colectare nouă)." + Environment.NewLine;
                var r = await System.Threading.Tasks.Task.Run(() =>
                {
                    // the same inputs as the original run (review 2026-11-08, P6): the signed media register kept in the case is verified and used again
                    LogAnalyzer.Dfir.Registers.MediaRegister? signedMedia = null;
                    if (LogAnalyzer.UI.Services.MediaRegisterFlows.FromCase(ws.Root) is { } signedInCase)
                    {
                        ((IProgress<string>)progress).Report(signedInCase.Note);
                        if (signedInCase.Used) signedMedia = LogAnalyzer.Dfir.Registers.SignedMediaRegisterCase.ToMediaRegister(signedInCase.Active.Doc!);
                    }
                    return new InvestigationPipeline { ThreatIntel = LogAnalyzer.UI.Services.ThreatIntelFlows.Active() }.Run(ws, LogAnalyzer.Dfir.Windows.Acquisition.CollectionProfile.Standard, false, progress, ct, mediaRegister: signedMedia, systemZone: zone);
                });
                ShowResult(r);
                IntegrityLine = (await System.Threading.Tasks.Task.Run(() => ws.Recheck(ct))).Summary;   // as after a first run: the rewritten outputs are re-checked
                Log += "Reanaliza s-a încheiat: " + (r.RunDiff?.Summary ?? "prima rulare înregistrată a cazului") + Environment.NewLine;
            }
            catch (OperationCanceledException) { RunDiffSummary = "Reanaliza a fost oprită."; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // like Run(): an internal error is shown, never let through to the dispatcher (evaluation 2026-11-05)
                RunDiffSummary = "Reanaliza nu s-a încheiat: " + ex.Message;
                Log += $"Eroare internă la reanaliză ({ex.GetType().Name}): {ex.Message}{Environment.NewLine}";
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void CompareRuns()
        {
            RunDiffLines.Clear(); RunDiffNeedsReview = false;
            if (_result?.Case is not { } ws) return;
            if (RunBefore is null || RunAfter is null) { RunDiffSummary = AnalysisRunRows.Count < 2 ? "Comparația cere cel puțin două rulări." : "Alegeți două rulări."; return; }
            if (RunBefore.RunId == RunAfter.RunId) { RunDiffSummary = "Ați ales aceeași rulare de două ori."; return; }
            if (!RunBefore.IntegrityOk || !RunAfter.IntegrityOk) { RunDiffSummary = "O rulare aleasă nu este intactă: comparația nu se face pe date neverificate."; return; }
            if (AnalysisRuns.Load(ws, RunBefore.RunId) is not { } a || AnalysisRuns.Load(ws, RunAfter.RunId) is not { } b) { RunDiffSummary = "Manifestul unei rulări nu a putut fi citit."; return; }
            var d = AnalysisRuns.Diff(a, b);
            RunDiffSummary = $"{a.RunId} → {b.RunId}: {d.Summary}";
            RunDiffNeedsReview = d.NeedsReview;
            foreach (var c in d.InputChanges) RunDiffLines.Add("Probe: " + c);
            foreach (var c in d.ToolchainChanges) RunDiffLines.Add("Unelte: " + c);
            foreach (var f in d.Added) RunDiffLines.Add($"+ nouă: {f.FindingId} {f.Title} [{f.Severity}]");
            foreach (var f in d.Removed) RunDiffLines.Add($"− dispărută: {f.FindingId} {f.Title} [{f.Severity}]");
            foreach (var c in d.Changed)
                RunDiffLines.Add($"~ modificată: {c.BeforeId}→{c.AfterId} {c.Title}: " + string.Join("; ", c.Fields.Select(x => $"{x.Field} {x.Before} → {x.After}")));
        }
    }
}
