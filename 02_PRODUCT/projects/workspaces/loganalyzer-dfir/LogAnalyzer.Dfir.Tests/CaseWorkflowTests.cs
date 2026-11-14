using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.1: case stages, roles, task queue, finding review and the closure checklist, on a hash-chained append-only log.</summary>
public sealed class CaseWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_wf_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() =>
        CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "CASE-WF", Name = "wf", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "LAB-WS01", Scope = TestScopes.Valid() });

    private static Finding F(string id, Severity sev) => new() { FindingId = id, RuleId = "R", Title = id, Severity = sev, Description = "d" };

    /// <summary>ana investigates, radu reviews, ioana approves.</summary>
    private static CaseWorkflow Team(CaseWorkspace ws, WorkflowPolicy? policy = null)
    {
        var wf = new CaseWorkflow(ws, policy);
        Assert.True(wf.AddParticipant("ana", "ana", CaseRole.Investigator).Ok);
        Assert.True(wf.AddParticipant("ana", "ioana", CaseRole.Approver).Ok);
        Assert.True(wf.AddParticipant("ioana", "radu", CaseRole.Reviewer).Ok);
        return wf;
    }

    private static void ToReview(CaseWorkflow wf)
    {
        Assert.True(wf.Move("ana", CaseStage.Triage).Ok);
        Assert.True(wf.Move("ana", CaseStage.Investigation).Ok);
        Assert.True(wf.Move("ana", CaseStage.Review).Ok);
    }

    private static string AuditText(CaseWorkspace ws) => File.ReadAllText(ws.AppAuditLogPath);

    [Fact]
    public void An_invalid_transition_is_refused_and_the_attempt_is_audited()
    {
        var ws = NewCase();
        var wf = Team(ws);
        var r = wf.Move("ana", CaseStage.Review);
        Assert.False(r.Ok);
        Assert.Contains("tranziție nepermisă Nou -> Revizuire", r.Reason);
        Assert.Equal(CaseStage.New, wf.Load().Stage);
        Assert.Contains("workflow.refused", AuditText(ws));
    }

    [Fact]
    public void A_person_without_the_role_is_refused()
    {
        var ws = NewCase();
        var wf = Team(ws);
        Assert.Contains("nu are rolul de investigator", wf.Move("radu", CaseStage.Triage).Reason);
        Assert.Contains("nu are rolul de investigator", wf.Move("mallory", CaseStage.Triage).Reason);
        Assert.Contains("doar aprobatorul", wf.AddParticipant("radu", "mallory", CaseRole.Approver).Reason);
        ToReview(wf);
        Assert.Contains("nu are rolul de aprobator", wf.Close("radu", []).Result.Reason);
        Assert.Equal(CaseStage.Review, wf.Load().Stage);
    }

    [Fact]
    public void Closing_with_an_unreviewed_critical_finding_is_blocked_until_reviewed_or_waived()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ToReview(wf);
        Finding[] findings = [F("F1", Severity.Critical), F("F2", Severity.High), F("F3", Severity.Low)];

        var (r, list, closure) = wf.Close("ioana", findings);
        Assert.False(r.Ok);
        Assert.Null(closure);
        var item = Assert.Single(list.Blocking);
        Assert.Equal("findings", item.Id);
        Assert.Contains("2 din 2 fără decizie: F1, F2", item.Detail);   // F3 is below the review threshold

        Assert.True(wf.ReviewFinding("radu", "F1", FindingDecision.Confirmed, "evenimentele 4625/4624 din SRV01").Ok);
        Assert.True(wf.ReviewFinding("radu", "F2", FindingDecision.NeedsMoreEvidence, "lipsește jurnalul Sysmon").Ok);
        Assert.False(wf.Close("ioana", findings).Result.Ok);                                  // "needs more evidence" still blocks

        Assert.Contains("nu are rolul de aprobator", wf.ReviewFinding("radu", "F2", FindingDecision.Waived, "x").Reason);
        Assert.True(wf.ReviewFinding("ioana", "F2", FindingDecision.Waived, "sursa nu mai există; acceptat de aprobator").Ok);

        (r, list, closure) = wf.Close("ioana", findings);
        Assert.True(r.Ok, r.Reason);
        Assert.NotNull(closure);
        Assert.Contains(list.Items, i => i.Id == "waivers" && i.Status == ChecklistStatus.Warn);
        var s = wf.Load();
        Assert.Equal(CaseStage.Closed, s.Stage);
        Assert.Equal(FindingDecision.Waived, s.Reviews["F2"].Decision);
        Assert.Equal(2, s.History.Count(e => e.Kind == "finding.review" && e.Subject == "F2"));   // the earlier decision is kept in the log
        Assert.Contains("case.closed", AuditText(ws));
    }

    [Fact]
    public void Four_eyes_the_submitting_investigator_cannot_review_or_close_alone()
    {
        var ws = NewCase();
        var wf = new CaseWorkflow(ws);
        Assert.True(wf.AddParticipant("ana", "ana", CaseRole.Investigator).Ok);
        Assert.True(wf.AddParticipant("ana", "ana", CaseRole.Reviewer).Ok);
        Assert.True(wf.AddParticipant("ana", "ana", CaseRole.Approver).Ok);
        Assert.True(wf.AddParticipant("ana", "radu", CaseRole.Reviewer).Ok);   // a team case: a second person exists, so ana cannot do it all
        ToReview(wf);
        Assert.Contains("nu decide asupra propriilor constatări", wf.ReviewFinding("ana", "F1", FindingDecision.Confirmed, "ok").Reason);
        Assert.Contains("nu decide asupra propriilor constatări", wf.ReviewFinding("ana", "F1", FindingDecision.Waived, "ok").Reason);   // nor waive them
        var (r, list, _) = wf.Close("ana", []);
        Assert.False(r.Ok);
        Assert.Equal(ChecklistStatus.Fail, list.Items.Single(i => i.Id == "four-eyes").Status);
    }

    [Fact]
    public void A_solo_case_is_refused_when_the_policy_forbids_it()
    {
        var ws = NewCase();
        var wf = new CaseWorkflow(ws, new WorkflowPolicy(SoloCaseAllowed: false));
        foreach (var role in Enum.GetValues<CaseRole>()) Assert.True(wf.AddParticipant("ana", "ana", role).Ok);
        ToReview(wf);
        Assert.Equal(ChecklistStatus.Fail, wf.Close("ana", []).Checklist.Items.Single(i => i.Id == "four-eyes").Status);
    }

    [Fact]
    public void A_single_analyst_station_may_close_alone_and_it_is_said()
    {
        var ws = NewCase();
        var wf = new CaseWorkflow(ws);
        foreach (var role in Enum.GetValues<CaseRole>()) Assert.True(wf.AddParticipant("ana", "ana", role).Ok);
        ToReview(wf);
        Assert.True(wf.ReviewFinding("ana", "F1", FindingDecision.Confirmed, "verificat").Ok);
        var (r, list, _) = wf.Close("ana", [F("F1", Severity.High)]);
        Assert.True(r.Ok, r.Reason);
        var four = list.Items.Single(i => i.Id == "four-eyes");
        Assert.Equal(ChecklistStatus.Warn, four.Status);
        Assert.Contains("fără a doua persoană", four.Detail);
    }

    [Fact]
    public void Open_tasks_block_the_submission_and_tasks_follow_their_owner()
    {
        var ws = NewCase();
        var wf = Team(ws);
        Assert.True(wf.Move("ana", CaseStage.Triage).Ok);
        Assert.True(wf.Move("ana", CaseStage.Investigation).Ok);
        Assert.True(wf.AddTask("ana", "Exportă jurnalul Security de pe SRV01", "radu", "F1").Ok);
        Assert.Contains("nu participă la caz", wf.AddTask("ana", "x", "mallory").Reason);

        Assert.Contains("sarcini sunt încă deschise", wf.Move("ana", CaseStage.Review).Reason);
        Assert.Contains("este atribuită lui radu", wf.SetTaskStatus("ana", "T001", CaseTaskStatus.Done).Reason);
        Assert.Contains("cere un motiv", wf.SetTaskStatus("ana", "T001", CaseTaskStatus.Cancelled).Reason);
        Assert.True(wf.SetTaskStatus("radu", "T001", CaseTaskStatus.Done).Ok);
        Assert.Contains("nu mai este deschisă", wf.SetTaskStatus("radu", "T001", CaseTaskStatus.Done).Reason);
        Assert.True(wf.Move("ana", CaseStage.Review).Ok);

        var t = wf.Load().Tasks["T001"];
        Assert.Equal(("Exportă jurnalul Security de pe SRV01", "F1", CaseTaskStatus.Done), (t.Title, t.FindingId, t.Status));
    }

    [Fact]
    public void Returning_and_reopening_need_a_reason_and_the_right_role()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ToReview(wf);
        Assert.Contains("cere un motiv", wf.Move("radu", CaseStage.Investigation).Reason);
        Assert.True(wf.Move("radu", CaseStage.Investigation, "lipsește corelarea cu DC01").Ok);
        Assert.True(wf.Move("ana", CaseStage.Review).Ok);
        Assert.True(wf.Close("ioana", []).Result.Ok);

        Assert.Contains("nu are rolul de aprobator", wf.Move("ana", CaseStage.Investigation, "x").Reason);
        Assert.Contains("redeschiderea cere un motiv", wf.Move("ioana", CaseStage.Investigation).Reason);
        Assert.True(wf.Move("ioana", CaseStage.Investigation, "probă nouă adusă de la SRV01").Ok);
        var s = wf.Load();
        Assert.Equal((CaseStage.Investigation, 1), (s.Stage, s.Reopened));
    }

    [Fact]
    public void The_log_is_hash_chained_and_a_tampered_log_refuses_further_writes_and_blocks_closing()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ToReview(wf);
        Assert.Equal(ChainStatus.Valid, wf.Load().Chain!.Status);

        // someone edits the log to make "mallory" the approver
        var lines = File.ReadAllLines(wf.LogPath);
        lines[1] = lines[1].Replace("\"ioana\"", "\"mallory\"");
        File.WriteAllLines(wf.LogPath, lines);

        var s = wf.Load();
        Assert.Equal(ChainStatus.Broken, s.Chain!.Status);
        Assert.Contains("compromis", wf.AddNote("ana", "notă").Reason);
        Assert.False(wf.Close("mallory", []).Result.Ok);
    }

    [Fact]
    public void A_read_only_case_refuses_every_workflow_write()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ws.MarkReadOnly("sigilat");
        Assert.Contains("doar pentru citire", wf.Move("ana", CaseStage.Triage).Reason);
        Assert.Equal(CaseStage.New, wf.Load().Stage);
    }

    [Fact]
    public void The_state_survives_reopening_the_case_folder()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ToReview(wf);
        Assert.True(wf.AddNote("radu", "Am verificat cronologia pe DC01.").Ok);

        var again = new CaseWorkflow(CaseWorkspace.Open(ws.Root, recheck: false)).Load();
        Assert.Equal(CaseStage.Review, again.Stage);
        Assert.Equal("ana", again.SubmittedBy);
        Assert.Equal(3, again.Participants.Count);
        Assert.Equal("Am verificat cronologia pe DC01.", Assert.Single(again.Notes).Text);
        Assert.All(again.History, e => Assert.True(e.Seq > 0));
    }

    // ---- review 2026-11-07: regressions for the bypasses found by the independent reviewer (probes A-F) ----

    [Fact]
    public void Probe_A_an_investigator_who_did_not_start_the_flow_cannot_name_himself_approver()
    {
        var wf = new CaseWorkflow(NewCase());
        Assert.True(wf.AddParticipant("ana", "ana", CaseRole.Investigator).Ok);
        Assert.True(wf.AddParticipant("ana", "bob", CaseRole.Investigator).Ok);
        var r = wf.AddParticipant("bob", "bob", CaseRole.Approver);
        Assert.False(r.Ok);
        Assert.Contains("doar ana, care a pornit fluxul", r.Reason);
        Assert.Contains("doar ana", wf.RemoveParticipant("bob", "ana", CaseRole.Investigator, "x").Reason);
    }

    [Fact]
    public void Probe_B_removing_the_reviewer_does_not_turn_a_team_case_into_a_solo_one()
    {
        var wf = new CaseWorkflow(NewCase());
        foreach (var role in new[] { CaseRole.Investigator, CaseRole.Reviewer, CaseRole.Approver }) Assert.True(wf.AddParticipant("ana", "ana", role).Ok);
        Assert.True(wf.AddParticipant("ana", "radu", CaseRole.Reviewer).Ok);
        Assert.True(wf.RemoveParticipant("ana", "radu", CaseRole.Reviewer, "plecat").Ok);   // before the review: allowed, but radu still counts
        ToReview(wf);
        Assert.Contains("nu decide asupra propriilor constatări", wf.ReviewFinding("ana", "F1", FindingDecision.Confirmed, "ok").Reason);
        Assert.Equal(ChecklistStatus.Fail, wf.Close("ana", []).Checklist.Items.Single(i => i.Id == "four-eyes").Status);
    }

    [Fact]
    public void Probe_B_participants_are_not_removed_during_review_and_the_last_approver_never()
    {
        var wf = Team(NewCase());
        Assert.Contains("ultimul aprobator", wf.RemoveParticipant("ioana", "ioana", CaseRole.Approver, "x").Reason);
        ToReview(wf);
        Assert.Contains("în revizuire sau închis", wf.RemoveParticipant("ioana", "radu", CaseRole.Reviewer, "x").Reason);
    }

    [Fact]
    public void Probe_C_decisions_are_taken_only_in_review()
    {
        var wf = Team(NewCase());
        Assert.True(wf.Move("ana", CaseStage.Triage).Ok);
        Assert.True(wf.Move("ana", CaseStage.Investigation).Ok);
        Assert.Contains("se iau în etapa Revizuire", wf.ReviewFinding("radu", "F1", FindingDecision.FalsePositive, "x").Reason);
    }

    [Fact]
    public void Probe_D_a_deleted_log_is_broken_not_empty()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ToReview(wf);
        File.Delete(wf.LogPath);
        var s = wf.Load();
        Assert.Equal(ChainStatus.Broken, s.Chain!.Status);
        Assert.Equal("jurnal lipsă", s.Chain.Reason);
        Assert.Contains("compromis", wf.AddParticipant("mallory", "mallory", CaseRole.Approver).Reason);
    }

    [Fact]
    public void Probe_E_a_log_cut_at_the_end_is_broken_and_the_closure_register_carries_its_head()
    {
        var ws = NewCase();
        var wf = Team(ws);
        ToReview(wf);
        var lines = File.ReadAllLines(wf.LogPath);
        File.WriteAllLines(wf.LogPath, lines[..^1]);
        Assert.Equal("jurnal trunchiat", wf.Load().Chain!.Reason);
        File.WriteAllLines(wf.LogPath, lines);

        var (r, _, closure) = wf.Close("ioana", []);
        Assert.True(r.Ok, r.Reason);
        Assert.Contains($"Flux:     Seq {lines.Length + 1}", closure!.RegisterText);
    }

    [Fact]
    public void An_account_with_surrounding_spaces_is_refused_not_silently_trimmed()
    {
        var wf = Team(NewCase());
        Assert.Contains("spații", wf.Move(" ana", CaseStage.Triage).Reason);
    }

    [Fact]
    public void Probe_K_an_investigator_does_not_review_his_own_work_even_when_a_colleague_submitted_it()
    {
        var wf = Team(NewCase());
        Assert.True(wf.AddParticipant("ioana", "ana", CaseRole.Reviewer).Ok);
        Assert.True(wf.AddParticipant("ioana", "bob", CaseRole.Investigator).Ok);
        Assert.True(wf.Move("bob", CaseStage.Triage).Ok);
        Assert.True(wf.Move("bob", CaseStage.Investigation).Ok);
        Assert.True(wf.Move("bob", CaseStage.Review).Ok);                     // bob submits ana's work
        Assert.Contains("nu decide asupra propriilor constatări", wf.ReviewFinding("ana", "F1", FindingDecision.Confirmed, "ok").Reason);
        Assert.True(wf.ReviewFinding("radu", "F1", FindingDecision.Confirmed, "verificat independent").Ok);
    }

    [Fact]
    public void Probe_H_workflow_reads_survive_concurrent_audit_writes()
    {
        var ws = NewCase();
        var wf = Team(ws);
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() => { while (!stop.IsCancellationRequested) ws.Audit("test.noise", "concurrent"); });
        try
        {
            for (int i = 0; i < 60; i++)
            {
                Assert.Equal(ChainStatus.Valid, wf.Load().Chain!.Status);
                Assert.True(wf.AddNote("ana", $"notă {i}").Ok);
            }
        }
        finally { stop.Cancel(); writer.Wait(); }
        Assert.Equal(60, wf.Load().Notes.Count);
    }

    [Fact]
    public void A_low_finding_the_verification_contradicted_must_be_reviewed_before_closing_and_stays_as_it_was()
    {
        var wf = Team(NewCase());
        ToReview(wf);
        var low = F("F9", Severity.Low);
        low.Verification = new FindingVerification(StandardState.Contradicted, "execuția precede crearea fișierului", "CaseVerifier");
        var (r, list, _) = wf.Close("ioana", [low]);
        Assert.False(r.Ok);
        Assert.Contains("F9", Assert.Single(list.Blocking).Detail);
        Assert.True(wf.ReviewFinding("radu", "F9", FindingDecision.FalsePositive, "execuția precede crearea fișierului (CONTRA-EXEC-BEFORE-CREATION)").Ok);
        Assert.True(wf.Close("ioana", [low]).Result.Ok);
        Assert.Equal(StandardState.Contradicted, low.Verification.State);     // the decision is the reviewer's; the verification result is not rewritten
    }
}
