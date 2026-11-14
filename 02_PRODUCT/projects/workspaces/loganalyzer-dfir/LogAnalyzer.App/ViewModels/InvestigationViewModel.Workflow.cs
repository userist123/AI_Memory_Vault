using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One finding that the closure checklist asks a decision for, with the latest decision.</summary>
    public sealed record WorkflowReviewRow(string FindingId, string SeverityText, string Title, string DecisionText, string Reviewer, bool Pending);

    public sealed record WorkflowHistoryRow(string WhenLocal, string Actor, string What);

    /// <summary>
    /// Master plan 6.1, „Flux de lucru” tab: the stage of the open case, the people and their roles, the task queue, the decision on each
    /// important finding and the closure checklist. Every action goes to <see cref="CaseWorkflow"/>, which checks the role and writes the chained log;
    /// the page only shows what it answered.
    /// </summary>
    public partial class InvestigationViewModel
    {
        private static readonly IReadOnlyList<string> RoleNames = Enum.GetValues<CaseRole>().Select(CaseWorkflow.Label).ToList();
        private static readonly IReadOnlyList<string> DecisionNames = Enum.GetValues<FindingDecision>().Select(CaseWorkflow.Label).ToList();
        public IReadOnlyList<string> WorkflowRoleNames => RoleNames;          // instance properties: a WPF binding path does not reach static ones
        public IReadOnlyList<string> WorkflowDecisionNames => DecisionNames;

        public ObservableCollection<string> WorkflowParticipants { get; } = new();
        public ObservableCollection<CaseTaskItem> WorkflowTasks { get; } = new();
        public ObservableCollection<WorkflowReviewRow> WorkflowReviews { get; } = new();
        public ObservableCollection<ChecklistItem> WorkflowChecklist { get; } = new();
        public ObservableCollection<WorkflowHistoryRow> WorkflowHistory { get; } = new();
        public ObservableCollection<string> WorkflowNextStages { get; } = new();

        [ObservableProperty] private string _workflowStageText = "Niciun caz deschis.";
        [ObservableProperty] private string _workflowStatus = "";
        [ObservableProperty] private bool _workflowProblem;
        [ObservableProperty] private string _workflowMe = "";
        [ObservableProperty] private string _workflowChecklistLine = "";

        [ObservableProperty] private string _newParticipantAccount = "";
        [ObservableProperty] private int _newParticipantRole;
        [ObservableProperty] private string _newTaskTitle = "";
        [ObservableProperty] private string _newTaskAssignee = "";
        [ObservableProperty] private string _workflowReason = "";
        [ObservableProperty] private string? _selectedNextStage;
        [ObservableProperty] private CaseTaskItem? _selectedWorkflowTask;
        [ObservableProperty] private WorkflowReviewRow? _selectedWorkflowReview;
        [ObservableProperty] private int _selectedDecision;

        private CaseWorkflow? Workflow => _result?.Case is { } ws ? new CaseWorkflow(ws) : null;
        private static string Me => LogAnalyzer.Dfir.Auth.OperatorIdentity.Who;

        /// <summary>Reads the workflow log of the open case again and fills the tab. A log that cannot be read is shown, never guessed.</summary>
        internal void RefreshWorkflow()
        {
            WorkflowParticipants.Clear(); WorkflowTasks.Clear(); WorkflowReviews.Clear(); WorkflowChecklist.Clear(); WorkflowHistory.Clear(); WorkflowNextStages.Clear();
            WorkflowMe = "Lucrați ca: " + Me;
            if (Workflow is not { } wf || _result is null) { WorkflowStageText = "Niciun caz deschis."; WorkflowChecklistLine = ""; return; }
            try { FillWorkflow(wf); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                WorkflowStageText = "Jurnalul fluxului de lucru nu a putut fi citit: " + ex.Message; WorkflowProblem = true;
            }
        }

        private void FillWorkflow(CaseWorkflow wf)
        {
            var s = wf.Load();
            bool broken = s.Chain is { Status: ChainStatus.Broken };
            WorkflowProblem = broken;
            WorkflowStageText = $"Etapa: {CaseWorkflow.Label(s.Stage)}" + (s.Reopened > 0 ? $" · redeschis de {s.Reopened} ori" : "") +
                                (broken ? " · Jurnalul fluxului este compromis: " + s.Chain!.Reason : s.Chain is null ? " · flux nepornit (adăugați primul participant)" : " · jurnal înlănțuit, intact");
            foreach (var g in s.Participants.GroupBy(p => p.Account, StringComparer.OrdinalIgnoreCase))
                WorkflowParticipants.Add($"{g.Key}: {string.Join(", ", g.Select(p => CaseWorkflow.Label(p.Role)))}");
            foreach (var t in s.Tasks.Values.OrderBy(t => t.Status).ThenBy(t => t.TaskId, StringComparer.Ordinal)) WorkflowTasks.Add(t);
            foreach (var st in CaseWorkflow.NextStages(s.Stage)) WorkflowNextStages.Add(CaseWorkflow.Label(st));
            SelectedNextStage = WorkflowNextStages.FirstOrDefault();

            foreach (var f in _result.Findings.Where(f => f.Severity >= wf.Policy.ReviewFrom || f.Verification?.State is StandardState.Contradicted or StandardState.Rejected)
                                              .OrderByDescending(f => f.Verification?.State is StandardState.Contradicted or StandardState.Rejected).ThenByDescending(f => f.Severity))
            {
                s.Reviews.TryGetValue(f.FindingId, out var r);
                WorkflowReviews.Add(new WorkflowReviewRow(f.FindingId, LogAnalyzer.Dfir.Presentation.SeverityLabels.Romanian(f.Severity),
                    (f.Verification?.State is StandardState.Contradicted ? "[CONTRAZISĂ de verificare] " : f.Verification?.State is StandardState.Rejected ? "[RESPINSĂ de verificare] " : "") + f.Title,
                    r is null ? "fără decizie" : CaseWorkflow.Label(r.Decision) + (r.Reason.Length > 0 ? ": " + r.Reason : ""), r?.Reviewer ?? "",
                    r is null || r.Decision == FindingDecision.NeedsMoreEvidence));
            }
            var list = wf.Checklist(_result.Findings, s, s.Holds(Me, CaseRole.Approver) ? Me : null);
            foreach (var i in list.Items) WorkflowChecklist.Add(i);
            WorkflowChecklistLine = s.Stage == CaseStage.Closed ? "Cazul este închis."
                : list.CanClose ? "Lista de închidere este îndeplinită: aprobatorul poate închide cazul."
                : $"Închiderea este blocată de {LogAnalyzer.Dfir.Language.RoText.Count(list.Blocking.Count(), "condiție", "condiții")}.";
            foreach (var e in s.History.AsEnumerable().Reverse().Take(200))
                WorkflowHistory.Add(new WorkflowHistoryRow(e.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), e.Actor, Describe(e)));
        }

        private static string Describe(WorkflowEvent e) => e.Kind switch
        {
            "participant.add" => $"a adăugat pe {e.Subject} ca {RoleWord(e.Value)}",
            "participant.remove" => $"a retras pe {e.Subject} din rolul de {RoleWord(e.Value)}: {e.Reason}",
            "stage" => $"etapa {e.Subject} → {(Enum.TryParse<CaseStage>(e.Value, out var st) ? CaseWorkflow.Label(st) : e.Value)}" + (e.Reason.Length > 0 ? $" ({e.Reason})" : ""),
            "task.create" => $"sarcina {e.Subject} creată" + (e.Value.Length > 0 ? $", atribuită lui {e.Value}" : ""),
            "task.assign" => $"sarcina {e.Subject} reatribuită lui {e.Value}",
            "task.status" => $"sarcina {e.Subject}: {(e.Value == nameof(CaseTaskStatus.Done) ? "făcută" : "anulată")}" + (e.Reason.Length > 0 ? $" ({e.Reason})" : ""),
            "finding.review" => $"constatarea {e.Subject}: {(Enum.TryParse<FindingDecision>(e.Value, out var d) ? CaseWorkflow.Label(d) : e.Value)} — {e.Reason}",
            "note" => "notă: " + e.Reason,
            _ => e.Kind,
        };

        private static string RoleWord(string v) => Enum.TryParse<CaseRole>(v, out var r) ? CaseWorkflow.Label(r) : v;

        private void Report(Func<StateResult> act, string ok)
        {
            StateResult r;
            try { r = act(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                r = StateResult.Refuse("jurnalul cazului nu a putut fi scris: " + ex.Message);   // e.g. the lock file of another station, a full disk
            }
            WorkflowStatus = r.Ok ? (r.Reason.Length > 0 ? ok + " " + r.Reason : ok) : "Refuzat: " + r.Reason;
            if (r.Ok) WorkflowReason = "";
            RefreshWorkflow();
        }

        private bool NoCase()
        {
            if (Workflow is not null) return false;
            WorkflowStatus = "Deschideți sau creați întâi un caz.";
            return true;
        }

        [RelayCommand]
        private void WorkflowAddParticipant()
        {
            if (NoCase()) return;
            var account = string.IsNullOrWhiteSpace(NewParticipantAccount) ? Me : NewParticipantAccount.Trim();
            var role = Enum.GetValues<CaseRole>()[Math.Clamp(NewParticipantRole, 0, 3)];
            Report(() => Workflow!.AddParticipant(Me, account, role), $"{account} are acum rolul de {CaseWorkflow.Label(role)}.");
            if (WorkflowStatus.StartsWith(account, StringComparison.Ordinal)) NewParticipantAccount = "";
        }

        [RelayCommand]
        private void WorkflowMove()
        {
            if (NoCase()) return;
            var to = Enum.GetValues<CaseStage>().FirstOrDefault(s => CaseWorkflow.Label(s) == SelectedNextStage);
            if (SelectedNextStage is null) { WorkflowStatus = "Nu există o etapă următoare."; return; }
            if (to == CaseStage.Closed) { WorkflowClose(); return; }
            Report(() => Workflow!.Move(Me, to, WorkflowReason), $"Cazul a trecut în etapa {CaseWorkflow.Label(to)}.");
        }

        [RelayCommand]
        private void WorkflowClose()
        {
            if (NoCase()) return;
            CaseClosure? closure = null;
            Report(() => { var c = Workflow!.Close(Me, _result!.Findings); closure = c.Closure; return c.Result; },
                   "Cazul este închis.");
            if (closure is not null) WorkflowStatus = "Cazul este închis. Înscrieți în registrul de custodie: " + closure.RegisterText.ReplaceLineEndings(" · ");
        }

        [RelayCommand]
        private void WorkflowAddTask()
        {
            if (NoCase()) return;
            Report(() => Workflow!.AddTask(Me, NewTaskTitle, NewTaskAssignee, SelectedWorkflowReview?.FindingId), "Sarcina a fost adăugată.");
            if (!WorkflowStatus.StartsWith("Refuzat", StringComparison.Ordinal)) { NewTaskTitle = ""; NewTaskAssignee = ""; }
        }

        [RelayCommand]
        private void WorkflowTaskDone()
        {
            if (NoCase()) return;
            if (SelectedWorkflowTask is not { } t) { WorkflowStatus = "Alegeți o sarcină din listă."; return; }
            Report(() => Workflow!.SetTaskStatus(Me, t.TaskId, CaseTaskStatus.Done, WorkflowReason), $"Sarcina {t.TaskId} este făcută.");
        }

        [RelayCommand]
        private void WorkflowTaskCancel()
        {
            if (NoCase()) return;
            if (SelectedWorkflowTask is not { } t) { WorkflowStatus = "Alegeți o sarcină din listă."; return; }
            Report(() => Workflow!.SetTaskStatus(Me, t.TaskId, CaseTaskStatus.Cancelled, WorkflowReason), $"Sarcina {t.TaskId} este anulată.");
        }

        [RelayCommand]
        private void WorkflowReview()
        {
            if (NoCase()) return;
            if (SelectedWorkflowReview is not { } row) { WorkflowStatus = "Alegeți o constatare din lista de revizuit."; return; }
            var d = Enum.GetValues<FindingDecision>()[Math.Clamp(SelectedDecision, 0, 3)];
            Report(() => Workflow!.ReviewFinding(Me, row.FindingId, d, WorkflowReason), $"Decizia pentru {row.FindingId}: {CaseWorkflow.Label(d)}.");
        }

        [RelayCommand]
        private void WorkflowAddNote()
        {
            if (NoCase()) return;
            Report(() => Workflow!.AddNote(Me, WorkflowReason), "Nota a fost adăugată în jurnalul cazului.");
        }

        [RelayCommand]
        private void WorkflowRefresh() { WorkflowStatus = ""; RefreshWorkflow(); }
    }
}
