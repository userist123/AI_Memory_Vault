using System.Text.Json;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>Where the investigation is (master plan 6.1). Distinct from <see cref="CaseLifecycle"/>, which says whether the case folder may still change.</summary>
public enum CaseStage { New, Triage, Investigation, Review, Closed }

/// <summary>Case roles (master plan 6.1). One person may hold several; the four-eyes rule is checked on people, not on roles.</summary>
public enum CaseRole { Investigator, Reviewer, Approver, Auditor }

public enum CaseTaskStatus { Open, Done, Cancelled }

/// <summary>A reviewer's decision on one finding. <see cref="NeedsMoreEvidence"/> keeps the finding open; <see cref="Waived"/> is recorded only by an approver.</summary>
public enum FindingDecision { Confirmed, FalsePositive, NeedsMoreEvidence, Waived }

public enum ChecklistStatus { Pass, Warn, Fail }

/// <summary>
/// One line of <c>Logs/workflow.jsonl</c>. The log is hash-chained and append-only: nothing is edited in place, a correction is a new event
/// (a task re-assigned, a decision replaced by a later one). <see cref="Kind"/> values are listed on <see cref="CaseWorkflow"/>.
/// </summary>
public sealed record WorkflowEvent(DateTimeOffset TimestampUtc, string Actor, string Kind, string Subject, string Value, string Reason)
{
    public long Seq { get; init; }
}

public sealed record CaseParticipant(string Account, CaseRole Role, string AddedBy, DateTimeOffset AddedUtc);

public sealed record CaseTaskItem(string TaskId, string Title, string Assignee, CaseTaskStatus Status, string CreatedBy, DateTimeOffset CreatedUtc, string? FindingId);

public sealed record FindingReview(string FindingId, FindingDecision Decision, string Reviewer, string Reason, DateTimeOffset AtUtc);

public sealed record CaseNote(string Author, string Text, DateTimeOffset AtUtc);

public sealed record ChecklistItem(string Id, string Title, ChecklistStatus Status, string Detail);

public sealed record ClosureChecklist(IReadOnlyList<ChecklistItem> Items)
{
    public bool CanClose => Items.All(i => i.Status != ChecklistStatus.Fail);
    public IEnumerable<ChecklistItem> Blocking => Items.Where(i => i.Status == ChecklistStatus.Fail);
}

/// <summary>
/// Administrator setting. With <see cref="FourEyesClosure"/> off, or with <see cref="SoloCaseAllowed"/> on a case that has a single person in it
/// (one analyst on an isolated station), a self-reviewed and self-approved close is allowed and the checklist says so; a team case always needs a
/// second person.
/// </summary>
public sealed record WorkflowPolicy(bool FourEyesClosure = true, Severity ReviewFrom = Severity.High, bool SoloCaseAllowed = true);

/// <summary>The state folded from the workflow log. Never stored: always recomputed from the chain.</summary>
public sealed class CaseWorkflowState
{
    public CaseStage Stage { get; internal set; } = CaseStage.New;
    public List<CaseParticipant> Participants { get; } = [];
    public Dictionary<string, CaseTaskItem> Tasks { get; } = new(StringComparer.Ordinal);
    /// <summary>The latest decision per finding; earlier ones stay in the log.</summary>
    public Dictionary<string, FindingReview> Reviews { get; } = new(StringComparer.Ordinal);
    public List<CaseNote> Notes { get; } = [];
    public List<WorkflowEvent> History { get; } = [];
    /// <summary>Who moved the case into Review the last time (the four-eyes rule compares the closer with this person).</summary>
    public string? SubmittedBy { get; internal set; }
    public int Reopened { get; internal set; }
    public ChainVerification? Chain { get; internal set; }

    public bool Holds(string account, CaseRole role) =>
        Participants.Any(p => p.Role == role && string.Equals(p.Account, account, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> Holders(CaseRole role) => Participants.Where(p => p.Role == role).Select(p => p.Account);
    public IEnumerable<CaseTaskItem> OpenTasks => Tasks.Values.Where(t => t.Status == CaseTaskStatus.Open);
    /// <summary>
    /// Every person who ever took part, removed ones included (review 2026-11-07: counting only current participants let an investigator remove the
    /// reviewer and then close "alone").
    /// </summary>
    public int People => History.Where(e => e.Kind == "participant.add").Select(e => e.Subject).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    /// <summary>The first person added to the case; until an approver exists only this person manages participants.</summary>
    public string? Founder => History.FirstOrDefault(e => e.Kind == "participant.add")?.Subject;
}

/// <summary>
/// Case workflow (master plan 6.1): stage machine, roles, task queue, finding review and the closure checklist, in a hash-chained append-only log
/// next to the custody and audit chains. Every refused action is written to the case audit as <c>workflow.refused</c>, so an attempt leaves a trace.
/// Event kinds: <c>participant.add</c>, <c>participant.remove</c>, <c>stage</c>, <c>task.create</c>, <c>task.assign</c>, <c>task.status</c>,
/// <c>finding.review</c>, <c>note</c>.
/// Permissions are checked here, never in the UI: the UI only disables what this class would refuse.
/// </summary>
public sealed class CaseWorkflow
{
    public const string FileName = "workflow.jsonl";

    private readonly CaseWorkspace _ws;
    private readonly HashChain _chain;
    private readonly object _gate = new();

    public WorkflowPolicy Policy { get; }
    public string LogPath { get; }

    public CaseWorkflow(CaseWorkspace ws, WorkflowPolicy? policy = null)
    {
        _ws = ws;
        Policy = policy ?? new WorkflowPolicy();
        LogPath = Path.Combine(ws.Root, "Logs", FileName);
        _chain = new HashChain(LogPath);
    }

    /// <summary>Allowed moves, and the role the actor must hold for each. Anything else is refused.</summary>
    private static readonly Dictionary<(CaseStage From, CaseStage To), CaseRole> Moves = new()
    {
        [(CaseStage.New, CaseStage.Triage)] = CaseRole.Investigator,
        [(CaseStage.Triage, CaseStage.Investigation)] = CaseRole.Investigator,
        [(CaseStage.Investigation, CaseStage.Review)] = CaseRole.Investigator,
        [(CaseStage.Review, CaseStage.Investigation)] = CaseRole.Reviewer,     // returned for more work, reason required
        [(CaseStage.Review, CaseStage.Closed)] = CaseRole.Approver,            // through Close(), checklist required
        [(CaseStage.Closed, CaseStage.Investigation)] = CaseRole.Approver,     // reopen, reason required
    };

    public static IReadOnlyList<CaseStage> NextStages(CaseStage from) => Moves.Keys.Where(k => k.From == from).Select(k => k.To).ToList();

    public static string Label(CaseStage s) => s switch
    {
        CaseStage.New => "Nou", CaseStage.Triage => "Triere", CaseStage.Investigation => "Investigație", CaseStage.Review => "Revizuire", _ => "Închis",
    };

    public static string Label(CaseRole r) => r switch
    {
        CaseRole.Investigator => "investigator", CaseRole.Reviewer => "revizor", CaseRole.Approver => "aprobator", _ => "auditor",
    };

    public static string Label(FindingDecision d) => d switch
    {
        FindingDecision.Confirmed => "confirmată", FindingDecision.FalsePositive => "fals pozitiv", FindingDecision.NeedsMoreEvidence => "necesită probe suplimentare",
        _ => "exceptată cu aprobare",
    };

    // ---- reading ----

    /// <summary>
    /// Folds the log into the state. The chain verdict is <see cref="ChainStatus.Broken"/> also when the log is missing or shorter than the
    /// workflow events the (separately chained) case audit recorded: removing lines from the end, or the whole file, leaves a valid chain on its own
    /// (review 2026-11-07, probes D and E), so the audit copy is the cross-check.
    /// </summary>
    public CaseWorkflowState Load()
    {
        var s = new CaseWorkflowState();
        int audited = AuditedEvents();
        if (!File.Exists(LogPath))
        {
            if (audited > 0) s.Chain = new ChainVerification(FileName, ChainStatus.Broken, 0, 0, 1, "jurnal lipsă", $"jurnalul fluxului lipsește, dar auditul cazului are {audited} evenimente de flux");
            return s;
        }
        var bytes = ReadShared(LogPath);   // one read: the verdict and the state come from the same bytes
        s.Chain = _chain.Verify(bytes);
        foreach (var line in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            WorkflowEvent? e;
            try { e = JsonSerializer.Deserialize<WorkflowEvent>(line, Json.Options); }
            catch (JsonException) { continue; }   // the verdict already reports the broken line
            if (e is not null) Apply(s, e);
        }
        if (s.Chain.Status != ChainStatus.Broken && s.History.Count < audited)
            s.Chain = s.Chain with { Status = ChainStatus.Broken, Reason = "jurnal trunchiat", Message = $"jurnalul fluxului are {s.History.Count} evenimente, auditul cazului {audited}" };
        return s;
    }

    /// <summary>Reads a chained log that a writer may hold exclusively for a moment (HashChain.Append opens with FileShare.None): retried like <see cref="HashChain.Head"/>.</summary>
    private static byte[] ReadShared(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 100 && File.Exists(path)) { Thread.Sleep(20); }
        }
    }

    /// <summary>How many workflow events the case audit chain recorded (refusals excluded).</summary>
    private int AuditedEvents()
    {
        if (!File.Exists(_ws.AuditChainPath)) return 0;
        int n = 0;
        foreach (var line in System.Text.Encoding.UTF8.GetString(ReadShared(_ws.AuditChainPath)).Split('\n'))
            if (line.Contains("\"action\":\"workflow.", StringComparison.Ordinal) && !line.Contains("\"action\":\"workflow.refused\"", StringComparison.Ordinal)) n++;
        return n;
    }

    private static void Apply(CaseWorkflowState s, WorkflowEvent e)
    {
        s.History.Add(e);
        switch (e.Kind)
        {
            case "participant.add" when Enum.TryParse<CaseRole>(e.Value, out var r):
                if (!s.Holds(e.Subject, r)) s.Participants.Add(new CaseParticipant(e.Subject, r, e.Actor, e.TimestampUtc));
                break;
            case "participant.remove" when Enum.TryParse<CaseRole>(e.Value, out var r):
                s.Participants.RemoveAll(p => p.Role == r && string.Equals(p.Account, e.Subject, StringComparison.OrdinalIgnoreCase));
                break;
            case "stage" when Enum.TryParse<CaseStage>(e.Value, out var st):
                if (st == CaseStage.Review) s.SubmittedBy = e.Actor;
                if (s.Stage == CaseStage.Closed && st == CaseStage.Investigation) s.Reopened++;
                s.Stage = st;
                break;
            case "task.create":
                var (title, finding) = SplitTask(e.Reason);
                s.Tasks[e.Subject] = new CaseTaskItem(e.Subject, title, e.Value, CaseTaskStatus.Open, e.Actor, e.TimestampUtc, finding);
                break;
            case "task.assign" when s.Tasks.TryGetValue(e.Subject, out var t):
                s.Tasks[e.Subject] = t with { Assignee = e.Value };
                break;
            case "task.status" when s.Tasks.TryGetValue(e.Subject, out var t) && Enum.TryParse<CaseTaskStatus>(e.Value, out var ts):
                s.Tasks[e.Subject] = t with { Status = ts };
                break;
            case "finding.review" when Enum.TryParse<FindingDecision>(e.Value, out var d):
                s.Reviews[e.Subject] = new FindingReview(e.Subject, d, e.Actor, e.Reason, e.TimestampUtc);
                break;
            case "note":
                s.Notes.Add(new CaseNote(e.Actor, e.Reason, e.TimestampUtc));
                break;
        }
    }

    // The task title and the optional finding it serves travel in one field: "<finding id>\u001f<title>".
    private static string JoinTask(string title, string? findingId) => string.IsNullOrEmpty(findingId) ? title : findingId + '\u001f' + title;
    private static (string Title, string? FindingId) SplitTask(string reason) =>
        reason.IndexOf('\u001f') is var i and >= 0 ? (reason[(i + 1)..], reason[..i]) : (reason, null);

    // ---- writing: every method validates against the current state, under the log's own lock file ----

    private StateResult Write(string actor, string action, Func<CaseWorkflowState, StateResult> check, Func<CaseWorkflowState, WorkflowEvent> make)
    {
        actor ??= "";
        StateResult result;
        if (_ws.IsReadOnly) result = StateResult.Refuse("caz doar pentru citire: " + _ws.ReadOnlyReason);
        else if (actor.Trim().Length == 0) result = StateResult.Refuse("acțiune fără cont de operator");
        else if (actor != actor.Trim()) result = StateResult.Refuse("contul operatorului are spații la început sau la sfârșit");   // the checks and the log must see one spelling
        else
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                using var lockFile = OpenLock();
                var s = Load();
                result = s.Chain is { Status: ChainStatus.Broken } c
                    ? StateResult.Refuse("jurnalul fluxului de lucru este compromis (" + c.Reason + "); nu se mai scrie nimic în el")
                    : check(s);
                if (result.Ok)
                {
                    var e = make(s) with { Actor = actor };
                    _chain.Append(e);
                    _ws.Audit("workflow." + e.Kind, $"{e.Subject} {e.Value} {e.Reason}".Trim());
                    return result;
                }
            }
        }
        _ws.Audit("workflow.refused", $"{action} by={actor}: {result.Reason}");
        return result;
    }

    private FileStream OpenLock()
    {
        var p = LogPath + ".lock";
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(p, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            // another station writes the same case on a share; a lock file still pending delete (antivirus, SMB) answers access denied
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 150) { Thread.Sleep(20); }
        }
    }

    private static WorkflowEvent Ev(string kind, string subject, string value, string reason = "") =>
        new(DateTimeOffset.UtcNow, "", kind, subject, value, reason.ReplaceLineEndings(" ").Trim());

    /// <summary>
    /// Adds a person to a role. The first participant of a case may be added by anyone (the operator who opened it, the founder); after that only an
    /// approver manages participants, or the founder while the case has no approver. An investigator who is not the founder cannot name himself
    /// approver (review 2026-11-07, probe A).
    /// </summary>
    public StateResult AddParticipant(string actor, string account, CaseRole role) => Write(actor, $"participant.add {account} {role}", s =>
    {
        if (string.IsNullOrWhiteSpace(account)) return StateResult.Refuse("contul participantului lipsește");
        if (s.Holds(account, role)) return StateResult.Refuse($"{account} are deja rolul de {Label(role)}");
        return MayManage(s, actor);
    }, _ => Ev("participant.add", account.Trim(), role.ToString()));

    public StateResult RemoveParticipant(string actor, string account, CaseRole role, string reason) => Write(actor, $"participant.remove {account} {role}", s =>
    {
        if (!s.Holds(account, role)) return StateResult.Refuse($"{account} nu are rolul de {Label(role)}");
        if (string.IsNullOrWhiteSpace(reason)) return StateResult.Refuse("motivul retragerii lipsește");
        if (s.Stage is CaseStage.Review or CaseStage.Closed)
            return StateResult.Refuse("participanții nu se retrag cât cazul este în revizuire sau închis");   // probe B: removing the reviewer to close alone
        if (role == CaseRole.Approver && s.Holders(CaseRole.Approver).Count() == 1)
            return StateResult.Refuse("ultimul aprobator nu poate fi retras; adăugați întâi alt aprobator");   // probe F: it would reopen the bootstrap
        return MayManage(s, actor);
    }, _ => Ev("participant.remove", account.Trim(), role.ToString(), reason));

    private static StateResult MayManage(CaseWorkflowState s, string actor)
    {
        if (s.Founder is null) return StateResult.Success;
        if (s.Holds(actor, CaseRole.Approver)) return StateResult.Success;
        if (!s.Holders(CaseRole.Approver).Any() && string.Equals(s.Founder, actor, StringComparison.OrdinalIgnoreCase)) return StateResult.Success;
        return StateResult.Refuse(s.Holders(CaseRole.Approver).Any() ? "doar aprobatorul cazului gestionează participanții"
                                                                       : $"până la numirea unui aprobator, participanții îi gestionează doar {s.Founder}, care a pornit fluxul");
    }

    /// <summary>Whether this case needs a second person: always with four-eyes on, unless solo cases are allowed and only one person takes part.</summary>
    private bool FourEyes(CaseWorkflowState s) => Policy.FourEyesClosure && !(Policy.SoloCaseAllowed && s.People <= 1);

    private static StateResult Needs(CaseWorkflowState s, string actor, CaseRole role) =>
        s.Holds(actor, role) ? StateResult.Success : StateResult.Refuse($"{actor} nu are rolul de {Label(role)} în acest caz");

    /// <summary>Moves the case one step. Closing goes through <see cref="Close"/>, which runs the checklist.</summary>
    public StateResult Move(string actor, CaseStage to, string reason = "") => Write(actor, $"stage -> {to}", s =>
    {
        if (to == CaseStage.Closed) return StateResult.Refuse("închiderea se face doar prin lista de închidere (Close)");
        if (!Moves.TryGetValue((s.Stage, to), out var role))
            return StateResult.Refuse($"tranziție nepermisă {Label(s.Stage)} -> {Label(to)}" +
                                      (NextStages(s.Stage) is { Count: > 0 } n ? $" (permis: {string.Join(", ", n.Select(Label))})" : ""));
        var who = Needs(s, actor, role);
        if (!who.Ok) return who;
        bool backwards = s.Stage is CaseStage.Review or CaseStage.Closed;
        if (backwards && string.IsNullOrWhiteSpace(reason))
            return StateResult.Refuse(s.Stage == CaseStage.Closed ? "redeschiderea cere un motiv" : "returnarea la investigație cere un motiv");
        if (to == CaseStage.Review && s.OpenTasks.Any())
            return StateResult.Refuse($"{s.OpenTasks.Count()} sarcini sunt încă deschise; finalizați-le sau anulați-le cu motiv");
        return StateResult.Success;
    }, s => Ev("stage", Label(s.Stage), to.ToString(), reason));

    public StateResult AddTask(string actor, string title, string assignee, string? findingId = null) => Write(actor, "task.create", s =>
    {
        if (string.IsNullOrWhiteSpace(title)) return StateResult.Refuse("titlul sarcinii lipsește");
        if (s.Stage == CaseStage.Closed) return StateResult.Refuse("cazul este închis; redeschideți-l pentru sarcini noi");
        if (!s.Holds(actor, CaseRole.Investigator) && !s.Holds(actor, CaseRole.Reviewer) && !s.Holds(actor, CaseRole.Approver))
            return StateResult.Refuse($"{actor} nu participă la caz ca investigator, revizor sau aprobator");
        if (!string.IsNullOrWhiteSpace(assignee) && !s.Participants.Any(p => string.Equals(p.Account, assignee, StringComparison.OrdinalIgnoreCase)))
            return StateResult.Refuse($"{assignee} nu participă la caz");
        return StateResult.Success;
    }, s => Ev("task.create", $"T{s.Tasks.Count + 1:D3}", (assignee ?? "").Trim(), JoinTask(title.Trim(), findingId)));

    public StateResult AssignTask(string actor, string taskId, string assignee) => Write(actor, $"task.assign {taskId}", s =>
    {
        if (!s.Tasks.TryGetValue(taskId, out var t)) return StateResult.Refuse($"sarcină necunoscută: {taskId}");
        if (t.Status != CaseTaskStatus.Open) return StateResult.Refuse($"sarcina {taskId} nu mai este deschisă");
        if (!s.Participants.Any(p => string.Equals(p.Account, assignee, StringComparison.OrdinalIgnoreCase))) return StateResult.Refuse($"{assignee} nu participă la caz");
        return s.Holds(actor, CaseRole.Approver) || string.Equals(t.CreatedBy, actor, StringComparison.OrdinalIgnoreCase)
            ? StateResult.Success : StateResult.Refuse("doar autorul sarcinii sau aprobatorul o reatribuie");
    }, _ => Ev("task.assign", taskId, assignee.Trim()));

    /// <summary>Done by the assignee (or the creator when unassigned); Cancelled by its creator or an approver, with a reason.</summary>
    public StateResult SetTaskStatus(string actor, string taskId, CaseTaskStatus status, string reason = "") => Write(actor, $"task.status {taskId} {status}", s =>
    {
        if (!s.Tasks.TryGetValue(taskId, out var t)) return StateResult.Refuse($"sarcină necunoscută: {taskId}");
        if (t.Status != CaseTaskStatus.Open) return StateResult.Refuse($"sarcina {taskId} nu mai este deschisă");
        if (status == CaseTaskStatus.Open) return StateResult.Refuse("sarcina este deja deschisă");
        bool owner = string.Equals(string.IsNullOrEmpty(t.Assignee) ? t.CreatedBy : t.Assignee, actor, StringComparison.OrdinalIgnoreCase);
        if (status == CaseTaskStatus.Done)
            return owner || s.Holds(actor, CaseRole.Approver) ? StateResult.Success : StateResult.Refuse($"sarcina {taskId} este atribuită lui {t.Assignee}");
        if (string.IsNullOrWhiteSpace(reason)) return StateResult.Refuse("anularea unei sarcini cere un motiv");
        return string.Equals(t.CreatedBy, actor, StringComparison.OrdinalIgnoreCase) || s.Holds(actor, CaseRole.Approver)
            ? StateResult.Success : StateResult.Refuse("doar autorul sarcinii sau aprobatorul o anulează");
    }, _ => Ev("task.status", taskId, status.ToString(), reason));

    /// <summary>
    /// A decision on one finding. A reviewer decides Confirmed / FalsePositive / NeedsMoreEvidence; only an approver records a waiver, and the
    /// investigator who submitted the case can neither review nor waive its findings under the four-eyes policy. Decisions are taken only in Review,
    /// once the submitter is known (review 2026-11-07, probe C: a decision taken before submitting escaped the check). A later decision replaces the
    /// earlier one in the state; both stay in the log.
    /// </summary>
    public StateResult ReviewFinding(string actor, string findingId, FindingDecision decision, string reason) => Write(actor, $"finding.review {findingId} {decision}", s =>
    {
        if (string.IsNullOrWhiteSpace(findingId)) return StateResult.Refuse("constatarea lipsește");
        if (s.Stage != CaseStage.Review) return StateResult.Refuse($"deciziile asupra constatărilor se iau în etapa Revizuire (acum {Label(s.Stage)})");
        if (string.IsNullOrWhiteSpace(reason)) return StateResult.Refuse("decizia asupra unei constatări cere o justificare");
        var role = decision == FindingDecision.Waived ? CaseRole.Approver : CaseRole.Reviewer;
        var who = Needs(s, actor, role);
        if (!who.Ok) return who;
        if (FourEyes(s) && IsSubmitter(s, actor))
            return StateResult.Refuse("investigatorul care a trimis cazul la revizuire nu decide asupra propriilor constatări");
        return StateResult.Success;
    }, _ => Ev("finding.review", findingId.Trim(), decision.ToString(), reason));

    /// <summary>
    /// The investigation side of the case: whoever submitted it and whoever ever held the investigator role (review round 2, probe K: comparing only
    /// with the person who clicked "submit" let an investigator review his own work once a colleague submitted it).
    /// </summary>
    private static bool IsSubmitter(CaseWorkflowState s, string account) =>
        (s.SubmittedBy is { } sub && string.Equals(sub, account, StringComparison.OrdinalIgnoreCase)) ||
        s.History.Any(e => e.Kind == "participant.add" && e.Value == nameof(CaseRole.Investigator) && string.Equals(e.Subject, account, StringComparison.OrdinalIgnoreCase));

    /// <summary>A decision that counts for the checklist: not "needs more evidence", and under four-eyes not taken by the submitter.</summary>
    private bool Counts(CaseWorkflowState s, FindingReview r) =>
        r.Decision != FindingDecision.NeedsMoreEvidence && !(FourEyes(s) && IsSubmitter(s, r.Reviewer));

    public StateResult AddNote(string actor, string text) => Write(actor, "note", s =>
    {
        if (string.IsNullOrWhiteSpace(text)) return StateResult.Refuse("nota este goală");
        return s.Participants.Any(p => string.Equals(p.Account, actor, StringComparison.OrdinalIgnoreCase))
            ? StateResult.Success : StateResult.Refuse($"{actor} nu participă la caz");
    }, _ => Ev("note", "", "", text));

    // ---- closure ----

    /// <summary>
    /// The closure checklist. A finding at or above <see cref="WorkflowPolicy.ReviewFrom"/> blocks the close until it carries a decision other than
    /// "needs more evidence" (a waiver counts). Legacy chains warn, broken chains block.
    /// </summary>
    public ClosureChecklist Checklist(IReadOnlyList<Finding> findings, CaseWorkflowState? state = null, string? closer = null)
    {
        var s = state ?? Load();
        var items = new List<ChecklistItem>();
        void Add(string id, string title, ChecklistStatus st, string detail) => items.Add(new ChecklistItem(id, title, st, detail));

        Add("stage", "Cazul a trecut prin revizuire", s.Stage == CaseStage.Review ? ChecklistStatus.Pass : ChecklistStatus.Fail,
            s.Stage == CaseStage.Review ? "în revizuire" : $"etapa curentă: {Label(s.Stage)}");

        Add("scope", "Scopul cazului este confirmat", _ws.ScopeNote is null ? ChecklistStatus.Pass : ChecklistStatus.Fail, _ws.ScopeNote ?? "confirmat");

        var chains = _ws.VerifyChains();
        var all = new[] { chains.Custody, chains.Audit }.Concat(s.Chain is { } w ? [w] : Array.Empty<ChainVerification>()).ToList();
        var broken = all.Where(c => c.Status == ChainStatus.Broken).ToList();
        var legacy = all.Where(c => c.Status == ChainStatus.Legacy).ToList();
        Add("chains", "Jurnalele de custodie, audit și flux sunt intacte",
            broken.Count > 0 ? ChecklistStatus.Fail : legacy.Count > 0 ? ChecklistStatus.Warn : ChecklistStatus.Pass,
            broken.Count > 0 ? string.Join("; ", broken.Select(c => $"{c.Log}: {c.Reason}")) : legacy.Count > 0 ? HashChain.LegacyMessage : "lanțuri valide");

        var rc = _ws.LastRecheck;
        Add("integrity", "Reverificarea probelor la deschidere",
            rc is null ? ChecklistStatus.Warn : rc.Verdict is RecheckVerdict.Valid ? ChecklistStatus.Pass : rc.Verdict is RecheckVerdict.Legacy ? ChecklistStatus.Warn : ChecklistStatus.Fail,
            rc is null ? "nereverificat în această sesiune" : rc.Summary);

        var open = s.OpenTasks.ToList();
        Add("tasks", "Nu există sarcini deschise", open.Count == 0 ? ChecklistStatus.Pass : ChecklistStatus.Fail,
            open.Count == 0 ? $"{s.Tasks.Count} sarcini, toate încheiate" : string.Join("; ", open.Select(t => $"{t.TaskId} {t.Title} ({(t.Assignee == "" ? "neatribuită" : t.Assignee)})")));

        var mustReview = findings.Where(f => f.Severity >= Policy.ReviewFrom).ToList();
        var pending = mustReview.Where(f => !s.Reviews.TryGetValue(f.FindingId, out var r) || !Counts(s, r)).ToList();
        Add("findings", $"Constatările de la {Policy.ReviewFrom} în sus au o decizie", pending.Count == 0 ? ChecklistStatus.Pass : ChecklistStatus.Fail,
            pending.Count == 0 ? $"{mustReview.Count} constatări revizuite" : $"{pending.Count} din {mustReview.Count} fără decizie: " + string.Join(", ", pending.Take(8).Select(f => f.FindingId)) + (pending.Count > 8 ? " …" : ""));

        var waived = s.Reviews.Values.Count(r => r.Decision == FindingDecision.Waived);
        if (waived > 0) Add("waivers", "Excepții aprobate", ChecklistStatus.Warn, $"{waived} constatări închise prin excepție aprobată; apar ca atare în raport");

        if (!s.Holders(CaseRole.Approver).Any()) Add("approver", "Cazul are un aprobator", ChecklistStatus.Fail, "niciun aprobator desemnat");
        if (closer is not null)
        {
            bool self = IsSubmitter(s, closer);
            Add("four-eyes", "Închiderea o face altă persoană decât investigatorul",
                !self ? ChecklistStatus.Pass : FourEyes(s) ? ChecklistStatus.Fail : ChecklistStatus.Warn,
                !self ? $"trimis de {s.SubmittedBy ?? "?"}, închis de {closer}" : FourEyes(s) ? "aceeași persoană a trimis și ar închide cazul" : "închis fără a doua persoană (caz cu un singur analist); raportul o spune");
        }
        return new ClosureChecklist(items);
    }

    /// <summary>
    /// Closes the case: the approver's checklist must pass, then the stage moves to Closed and <see cref="CaseClosure.Close"/> records the chain heads.
    /// The case folder itself is not sealed here.
    /// </summary>
    public (StateResult Result, ClosureChecklist Checklist, CaseClosure? Closure) Close(string actor, IReadOnlyList<Finding> findings)
    {
        ClosureChecklist list = new([]);
        var r = Write(actor, "stage -> Closed", s =>
        {
            var who = Needs(s, actor, CaseRole.Approver);
            if (!who.Ok) return who;
            list = Checklist(findings, s, actor.Trim());
            return list.CanClose ? StateResult.Success
                : StateResult.Refuse("lista de închidere nu este îndeplinită: " + string.Join("; ", list.Blocking.Select(i => $"{i.Title} ({i.Detail})")));
        }, s => Ev("stage", Label(s.Stage), nameof(CaseStage.Closed), string.Join("; ", list.Items.Where(i => i.Status != ChecklistStatus.Pass).Select(i => $"{i.Id}={i.Status}"))));
        if (!r.Ok) return (r, list, null);
        // the Closed event is already in both chains; a failure to read the heads afterwards is reported, the close itself stands
        try
        {
            var closure = CaseClosure.Close(_ws, actor);
            var (seq, head) = _chain.Head();   // the workflow head goes to the register too, so a later truncation of this log is detectable on paper
            return (r, list, closure with { RegisterText = closure.RegisterText.Replace("Se înscriu", $"Flux:     Seq {seq}  {head}{Environment.NewLine}Se înscriu", StringComparison.Ordinal) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ok: the case IS closed; only the register text is missing, and the reason says so
            return (new StateResult(true, "cazul este închis, dar capetele lanțurilor nu au putut fi citite pentru registru: " + ex.Message), list, null);
        }
    }
}
