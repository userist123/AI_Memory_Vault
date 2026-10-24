using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Policy;

/// <summary>A setting as read from the station: its value (null = does not exist) and, where the store has one, its kind.</summary>
public sealed record SettingValue(string? Value, string? Kind)
{
    public static readonly SettingValue Absent = new(null, null);
    [JsonIgnore] public string Display => Value is null ? "(nu există)" : Kind is null ? Value : $"{Value} ({Kind})";
}

/// <summary>Reads and, where it can, writes one family of settings (registry, audit, service, secpol).</summary>
public interface ISettingProvider
{
    bool Handles(SettingRef s);
    /// <summary>Current state; throws when the setting cannot be read (access denied, API error…).</summary>
    SettingValue Read(SettingRef s);
    /// <summary>Null when <see cref="Write"/> is supported for this setting, otherwise the reason it is not.</summary>
    string? CannotWrite(SettingRef s);
    void Write(SettingRef s, string value);
    /// <summary>Removes a value that did not exist before an execution (rollback).</summary>
    void Delete(SettingRef s);
}

public enum ControlState { Compliant, NonCompliant, Unreadable }
public enum PlannedAction { None, Set, Manual }

/// <summary>Outcome of one control in an execution. There is no "success": a change is VERIFIED only when re-reading confirms it.</summary>
public enum ControlOutcome { Compliant, Verified, NotVerified, Failed, Stale, ManualRequired, Unreadable, NotSelected, RolledBack, RollbackFailed, RollbackNotVerified }

public enum ExecutionStatus { Verified, NotVerified, Failed, NothingApplied }

public sealed record ControlPlan(
    string ControlId, string Title, SettingRef Setting, string Desired, string? DesiredValue, SettingValue? Current, string? ReadError,
    ControlState State, PlannedAction Action, string Reason, string Severity, string Impact);

/// <summary>CURRENT + DESIRED + DIFF + IMPACT for one policy on one station. Its SHA-256 is what the operator confirms.</summary>
public sealed record PolicyPlan(
    string PolicyId, string PolicyVersion, string PolicySha256, string Station, DateTimeOffset CreatedUtc, IReadOnlyList<ControlPlan> Controls)
{
    public string Sha256 { get; init; } = "";
    [JsonIgnore] public int ToChange => Controls.Count(c => c.Action == PlannedAction.Set);
    [JsonIgnore] public int Manual => Controls.Count(c => c.Action == PlannedAction.Manual);
    [JsonIgnore] public int Unreadable => Controls.Count(c => c.State == ControlState.Unreadable);
    [JsonIgnore] public int Compliant => Controls.Count(c => c.State == ControlState.Compliant);

    public string ComputeSha256() => PolicyExecutor.Sha(JsonSerializer.Serialize(this with { Sha256 = "" }, PolicyExecutor.Json));
}

public sealed record ControlResult(
    string ControlId, string Setting, ControlOutcome Outcome, SettingValue? Before, string Action, SettingValue? After, string Verification, string Detail);

/// <summary>The evidence of one execution (spec §13): operator, approval, policy id and hash, before, action, after, verification.</summary>
public sealed record ExecutionRecord(
    string ExecutionId, string Kind, string PolicyId, string PolicyVersion, string PolicySha256, string PlanSha256, string Station,
    string Operator, string Approver, DateTimeOffset ApprovedUtc, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc,
    ExecutionStatus Status, bool PolicyCompliantAfter, IReadOnlyList<ControlResult> Results, string? RollbackOf = null)
{
    /// <summary>
    /// The execution in the shared response contract (computed from the re-read results, not serialized): one check per control that
    /// was written; a control that was read again and matched is VERIFIED, one that did not is FAILED, one whose re-read failed is NOT_VERIFIED.
    /// </summary>
    [JsonIgnore]
    public LogAnalyzer.Dfir.Response.ResponseOutcome Verification
    {
        get
        {
            // Only controls something was written to (or attempted on) are checks; untouched, stale and manual controls are not.
            var checks = Results.Where(r => r.Action != "niciuna").Select(r => new LogAnalyzer.Dfir.Response.ResponseCheck(
                r.Setting, r.Action, r.After?.Display ?? r.Detail,
                r.Outcome switch
                {
                    ControlOutcome.Verified or ControlOutcome.RolledBack => LogAnalyzer.Dfir.Response.ResponseStatus.Verified,
                    ControlOutcome.NotVerified or ControlOutcome.RollbackNotVerified => LogAnalyzer.Dfir.Response.ResponseStatus.NotVerified,
                    _ => LogAnalyzer.Dfir.Response.ResponseStatus.Failed,
                })).ToList();
            if (Status == ExecutionStatus.NothingApplied)
                return LogAnalyzer.Dfir.Response.ResponseOutcome.NotAttempted($"policy {PolicyId}", Kind, LogAnalyzer.Dfir.Response.ResponseStatus.Rejected, "nimic aplicat: nicio setare nu a fost scrisă");
            return LogAnalyzer.Dfir.Response.ResponseVerifier.Aggregate($"policy {PolicyId}", Kind, checks);
        }
    }
}

/// <summary>
/// Policy execution (master spec §13): CURRENT → DESIRED → DIFF → IMPACT (<see cref="Plan"/>), APPROVAL (policy APPROVED in
/// the store, same SHA-256, and the operator confirming the exact plan hash), APPLY (only controls with a single desired
/// value and a provider that can write), VERIFY (re-read each change and the whole policy) and EVIDENCE (execution record +
/// hash-chained audit log). A state that changed since the plan is not overwritten; a change that cannot be re-read is
/// NOT_VERIFIED, never success.
/// </summary>
public sealed class PolicyExecutor(IEnumerable<ISettingProvider> providers, string evidenceDir, string? station = null)
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly List<ISettingProvider> _providers = providers.ToList();
    private readonly string _station = station ?? Environment.MachineName;

    public PolicyAuditLog Audit => new(Path.Combine(evidenceDir, "audit.jsonl"));

    internal static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private ISettingProvider? For(SettingRef s) => _providers.FirstOrDefault(p => p.Handles(s));

    private static bool Satisfies(PolicyControl c, SettingValue v) =>
        c.Desired.IsSatisfiedBy(v.Value) && (v.Value is null || v.Kind is null || c.Setting.ValueType.Length == 0 || v.Kind == c.Setting.ValueType);

    public PolicyPlan Plan(PolicyDocument policy)
    {
        var list = new List<ControlPlan>();
        foreach (var c in policy.Controls)
        {
            var provider = For(c.Setting);
            SettingValue? current = null;
            string? error = null;
            if (provider is null) error = $"niciun furnizor pentru tipul „{c.Setting.Type}”";
            else
                try { current = provider.Read(c.Setting); }
                catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }

            ControlState state = current is null ? ControlState.Unreadable : Satisfies(c, current) ? ControlState.Compliant : ControlState.NonCompliant;
            (PlannedAction action, string reason) = state switch
            {
                ControlState.Compliant => (PlannedAction.None, "conform"),
                ControlState.Unreadable => (PlannedAction.None, "starea curentă nu poate fi citită: " + error),
                _ when c.Remediation != "set" => (PlannedAction.Manual, $"remedierea politicii este „{c.Remediation}”"),
                _ when c.Desired.RemediationValue is null => (PlannedAction.Manual, "desired_state nu numește o singură valoare"),
                _ when provider!.CannotWrite(c.Setting) is { } why => (PlannedAction.Manual, why),
                _ => (PlannedAction.Set, $"{current!.Display} → {c.Desired.RemediationValue}"),
            };
            list.Add(new ControlPlan(c.Id, c.Title, c.Setting, c.Desired.Display, c.Desired.RemediationValue, current, error, state, action, reason, c.Severity, c.Impact));
        }
        var plan = new PolicyPlan(policy.Id, policy.Version, policy.Sha256, _station, DateTimeOffset.UtcNow, list);
        return plan with { Sha256 = plan.ComputeSha256() };
    }

    [LogAnalyzer.Dfir.Response.ControlledResponse("each written setting re-read through its provider, then the whole policy re-read")]
    public ExecutionRecord Apply(PolicyDocument policy, PolicyPlan plan, PolicyStore store, string operatorName, string confirmedPlanSha256,
        IReadOnlySet<string>? onlyControls = null)
    {
        LogAnalyzer.Dfir.Auth.OperatorIdentity.RequireHostChange();
        if (string.IsNullOrWhiteSpace(operatorName) && !LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired)
            throw new PolicyLifecycleException("Aplicarea cere numele operatorului.");
        operatorName = PolicyApprovals.ActingAccount(operatorName);   // review #2: the signed-in account, never a typed name
        var ledger = PolicyApprovals.RequiredLedger();
        var record = store.RequireApplicable(policy);
        if (!plan.PolicySha256.Equals(policy.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException($"Planul a fost făcut pentru politica cu SHA-256 {plan.PolicySha256[..12]}…, nu {policy.Sha256[..12]}…: aplicarea este blocată.");
        if (plan.Sha256 != plan.ComputeSha256())
            throw new PolicyLifecycleException("Planul a fost modificat după calcul (SHA-256 nu corespunde): aplicarea este blocată.");
        if (!string.Equals(confirmedPlanSha256, plan.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException("Operatorul nu a confirmat acest plan (SHA-256 diferit): aplicarea este blocată.");
        if (!plan.Station.Equals(_station, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException($"Planul este pentru stația {plan.Station}, nu {_station}.");
        var approval = record.History.LastOrDefault(t => t.To == PolicyStatus.Approved && t.PolicySha256.Equals(policy.Sha256, StringComparison.OrdinalIgnoreCase))
            ?? throw new PolicyLifecycleException("Nu există o aprobare înregistrată pentru acest SHA-256.");
        // The approval in the per-user store is only a cache: it must be confirmed by the sealed authentication audit (review #2).
        if (ledger is not null)
        {
            var sealedApprovals = ledger.Approvals(policy.Id, policy.Version, policy.Sha256);
            if (!sealedApprovals.Any(a => string.Equals(a.Approver.Trim(), approval.Operator.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new PolicyLifecycleException($"Aprobarea din magazinul de politici (de {approval.Operator}) nu apare în jurnalul de autentificare sigilat pentru acest SHA-256: " +
                    "aplicarea este blocată. Nu s-a schimbat nimic. Aprobați din nou politica din aplicație, cu contul aprobatorului.");
            foreach (var a in sealedApprovals)
                if (string.Equals(operatorName.Trim(), a.Approver.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new PolicyLifecycleException($"Contul {operatorName.Trim()} a aprobat această politică, deci nu o poate și aplica. Nu s-a schimbat nimic. Aplicarea o face alt administrator.");
        }
        // Separation of duties: the account that approved this version does not also apply it.
        if (string.Equals(operatorName.Trim(), approval.Operator.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException($"Contul {operatorName.Trim()} a aprobat această politică, deci nu o poate și aplica. Nu s-a schimbat nimic. Aplicarea o face alt administrator.");

        var execId = $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid().ToString("N")[..8]}";
        var started = DateTimeOffset.UtcNow;
        var audit = Audit;
        var results = new List<ControlResult>();
        var byId = policy.Controls.ToDictionary(c => c.Id);
        foreach (var cp in plan.Controls)
        {
            var c = byId[cp.ControlId];
            var setting = c.Setting.Display;
            if (cp.Action != PlannedAction.Set)
            {
                var o = cp.State switch { ControlState.Compliant => ControlOutcome.Compliant, ControlState.Unreadable => ControlOutcome.Unreadable, _ => ControlOutcome.ManualRequired };
                results.Add(new ControlResult(cp.ControlId, setting, o, cp.Current, "niciuna", null, "", cp.Reason));
                continue;
            }
            if (onlyControls is not null && !onlyControls.Contains(cp.ControlId))
            {
                results.Add(new ControlResult(cp.ControlId, setting, ControlOutcome.NotSelected, cp.Current, "niciuna", null, "", "neselectat de operator"));
                continue;
            }
            var provider = For(c.Setting)!;
            var action = $"set {cp.DesiredValue}";
            ControlResult r;
            SettingValue? before = null;
            try { before = provider.Read(c.Setting); }
            catch (Exception ex) { r = new ControlResult(cp.ControlId, setting, ControlOutcome.Unreadable, null, "niciuna", null, "", $"citirea dinainte a eșuat, nu s-a scris nimic: {ex.Message}"); goto done; }
            if (before != cp.Current)
            {
                r = new ControlResult(cp.ControlId, setting, ControlOutcome.Stale, before, "niciuna", null, "",
                    $"starea s-a schimbat după plan ({cp.Current?.Display} → {before.Display}); nu se suprascrie");
                goto done;
            }
            try { provider.Write(c.Setting, cp.DesiredValue!); }
            catch (Exception ex) { r = new ControlResult(cp.ControlId, setting, ControlOutcome.Failed, before, action, null, "", $"scrierea a eșuat: {ex.GetType().Name}: {ex.Message}"); goto done; }
            try
            {
                var after = provider.Read(c.Setting);
                r = Satisfies(c, after)
                    ? new ControlResult(cp.ControlId, setting, ControlOutcome.Verified, before, action, after, "reread", "re-citirea confirmă valoarea dorită")
                    : new ControlResult(cp.ControlId, setting, ControlOutcome.Failed, before, action, after, "reread", $"după scriere valoarea este {after.Display}, nu {c.Desired.Display}");
            }
            catch (Exception ex)
            {
                r = new ControlResult(cp.ControlId, setting, ControlOutcome.NotVerified, before, action, null, "reread", $"scris, dar re-citirea a eșuat: {ex.Message}");
            }
        done:
            results.Add(r);
            audit.Append(new PolicyAuditEntry(DateTimeOffset.UtcNow, execId, "apply", operatorName, approval.Operator, policy.Id, policy.Version, policy.Sha256,
                plan.Sha256, _station, r.ControlId, r.Setting, r.Before?.Display, r.Action, r.After?.Display, r.Outcome.ToString(), r.Detail));
        }

        // VERIFY: the whole policy re-read after the changes.
        bool compliantAfter = policy.Controls.All(c =>
        {
            try { return For(c.Setting) is { } p && Satisfies(c, p.Read(c.Setting)); }
            catch { return false; }
        });
        // A write that threw changed nothing; one that "succeeded" but re-reads wrong did reach the station.
        var applied = results.Count(r => r.Outcome is ControlOutcome.Verified or ControlOutcome.NotVerified || (r.Outcome == ControlOutcome.Failed && r.After is not null));
        var status = results.Any(r => r.Outcome == ControlOutcome.Failed) ? ExecutionStatus.Failed
            : results.Any(r => r.Outcome == ControlOutcome.NotVerified) ? ExecutionStatus.NotVerified
            : applied > 0 ? ExecutionStatus.Verified : ExecutionStatus.NothingApplied;

        var exec = new ExecutionRecord(execId, "apply", policy.Id, policy.Version, policy.Sha256, plan.Sha256, _station, operatorName, approval.Operator,
            approval.AtUtc, started, DateTimeOffset.UtcNow, status, compliantAfter, results);
        var recordSha = Save(exec);
        // Review of the remediations #1: the execution record and its sidecar are per-user files; the sealed audit keeps the hash of the
        // record (and of its before/after values), so a rollback can only restore values this application really wrote.
        if (ledger is not null)
        {
            try { ledger.RecordApplied(policy.Id, policy.Version, policy.Sha256, execId, recordSha, ValuesSha256(exec), operatorName); }
            catch (Exception ex) when (ex is not PolicyLifecycleException)
            {
                throw new PolicyLifecycleException($"Execuția {execId} a schimbat stația, dar nu a putut fi consemnată în jurnalul de autentificare sigilat " +
                    $"({ex.GetType().Name}): rollback-ul ei va fi refuzat. Verificați drepturile folderului de autentificare.");
            }
        }

        if (applied > 0 && record.Status == PolicyStatus.Approved) store.MarkDeployed(policy, operatorName, execId);
        if (compliantAfter && (status is ExecutionStatus.Verified or ExecutionStatus.NothingApplied) && store.Get(policy)!.Status == PolicyStatus.Deployed)
            store.MarkVerified(policy, operatorName, execId);
        return exec;
    }

    /// <summary>
    /// Restores the "before" state of every control an execution wrote, unless it changed again since then. With sign-in required (or a sealed
    /// ledger registered) the execution must be one whose record hash the sealed audit holds as "policy.applied", the policy must still be
    /// registered, unchanged and approved (sealed approval not cancelled by a retirement), and the operator a signed-in administrator; every check
    /// runs before anything is read or written.
    /// </summary>
    [LogAnalyzer.Dfir.Response.ControlledResponse("each restored setting re-read through its provider")]
    public ExecutionRecord Rollback(PolicyDocument policy, string executionId, string operatorName, PolicyStore? store = null)
    {
        LogAnalyzer.Dfir.Auth.OperatorIdentity.RequireHostChange();
        if (string.IsNullOrWhiteSpace(operatorName) && !LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired)
            throw new PolicyLifecycleException("Rollback-ul cere numele operatorului.");
        operatorName = PolicyApprovals.ActingAccount(operatorName);   // the evidence names the signed-in account (review #2)
        var ledger = PolicyApprovals.RequiredLedger();
        var (source, sourceSha) = LoadWithHash(executionId);
        if (!source.PolicySha256.Equals(policy.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException("Execuția aparține altei versiuni a politicii (SHA-256 diferit).");
        if (ledger is not null)
        {
            const string nothing = " Nu s-a schimbat nimic.";
            if (store is null)
                throw new PolicyLifecycleException("Rollback-ul cere magazinul de politici pentru a verifica aprobarea." + nothing);
            store.RequireApplicable(policy);               // still registered, same SHA-256, APPROVED / DEPLOYED / VERIFIED
            if (ledger.Approvals(policy.Id, policy.Version, policy.Sha256).Count == 0)
                throw new PolicyLifecycleException("Politica nu are o aprobare valabilă în jurnalul de autentificare sigilat (lipsește sau a fost anulată la retragere): rollback-ul este blocat." + nothing);
            if (source.Kind != "apply" || source.RollbackOf is not null
                || !ledger.WasApplied(policy.Id, policy.Version, policy.Sha256, source.ExecutionId, sourceSha, ValuesSha256(source), source.Operator)
                || !string.Equals(source.ExecutionId, executionId, StringComparison.Ordinal))
                throw new PolicyLifecycleException($"Execuția {executionId} nu corespunde unei aplicări consemnate în jurnalul de autentificare sigilat " +
                    "(înregistrarea a fost modificată, inventată sau nu provine din această aplicație): rollback-ul este blocat." + nothing);
        }
        var byId = policy.Controls.ToDictionary(c => c.Id);
        var rbId = $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid().ToString("N")[..8]}";
        var started = DateTimeOffset.UtcNow;
        var results = new List<ControlResult>();
        var audit = Audit;
        foreach (var w in source.Results.Where(r => r.Before is not null && (r.Outcome is ControlOutcome.Verified or ControlOutcome.NotVerified || (r.Outcome == ControlOutcome.Failed && r.After is not null))))
        {
            var c = byId[w.ControlId];
            var provider = For(c.Setting)!;
            var before = w.Before!;
            ControlResult r;
            try
            {
                var now = provider.Read(c.Setting);
                if (w.After is not null && now != w.After)
                    r = new ControlResult(w.ControlId, w.Setting, ControlOutcome.Stale, now, "niciuna", null, "", $"valoarea s-a schimbat după execuție ({w.After.Display} → {now.Display}); nu se suprascrie");
                else
                {
                    var target = before.Kind is { } k && k != c.Setting.ValueType ? c.Setting with { ValueType = k } : c.Setting;
                    if (before.Value is null) provider.Delete(c.Setting); else provider.Write(target, before.Value);
                    var rbAction = before.Value is null ? "delete" : $"set {before.Value}";
                    SettingValue? after = null;
                    string? rereadError = null;
                    try { after = provider.Read(c.Setting); }
                    catch (Exception ex) { rereadError = ex.Message; }
                    r = after is null
                        ? new ControlResult(w.ControlId, w.Setting, ControlOutcome.RollbackNotVerified, now, rbAction, null, "reread", $"restaurat, dar re-citirea a eșuat: {rereadError}")
                        : after == before
                        ? new ControlResult(w.ControlId, w.Setting, ControlOutcome.RolledBack, now, rbAction, after, "reread", "starea dinainte a fost restaurată")
                        : new ControlResult(w.ControlId, w.Setting, ControlOutcome.RollbackFailed, now, rbAction, after, "reread", $"după rollback valoarea este {after.Display}, nu {before.Display}");
                }
            }
            catch (Exception ex) { r = new ControlResult(w.ControlId, w.Setting, ControlOutcome.RollbackFailed, null, "rollback", null, "", ex.Message); }
            results.Add(r);
            audit.Append(new PolicyAuditEntry(DateTimeOffset.UtcNow, rbId, "rollback", operatorName, source.Approver, policy.Id, policy.Version, policy.Sha256,
                source.PlanSha256, _station, r.ControlId, r.Setting, r.Before?.Display, r.Action, r.After?.Display, r.Outcome.ToString(), r.Detail));
        }
        var status = results.Any(r => r.Outcome == ControlOutcome.RollbackFailed) ? ExecutionStatus.Failed
            : results.Any(r => r.Outcome == ControlOutcome.RollbackNotVerified) ? ExecutionStatus.NotVerified
            : results.Any(r => r.Outcome == ControlOutcome.RolledBack) ? ExecutionStatus.Verified : ExecutionStatus.NothingApplied;
        var exec = new ExecutionRecord(rbId, "rollback", policy.Id, policy.Version, policy.Sha256, source.PlanSha256, _station, operatorName, source.Approver,
            source.ApprovedUtc, started, DateTimeOffset.UtcNow, status, false, results, executionId);
        var rbSha = Save(exec);
        ledger?.RecordRolledBack(policy.Id, policy.Version, policy.Sha256, rbId, executionId, rbSha, operatorName);
        return exec;
    }

    private string RecordPath(string id) => Path.Combine(evidenceDir, "executions", id + ".json");

    /// <summary>SHA-256 of the before/after values of every control of an execution (what a rollback would restore).</summary>
    internal static string ValuesSha256(ExecutionRecord e) =>
        Sha(string.Join("\n", e.Results.Select(r => $"{r.ControlId}\u001f{r.Setting}\u001f{r.Outcome}\u001f{r.Before?.Value}\u001f{r.Before?.Kind}\u001f{r.Before is null}\u001f{r.After?.Value}\u001f{r.After?.Kind}\u001f{r.After is null}")));

    /// <summary>Writes the record and its sidecar; returns the SHA-256 of the record text.</summary>
    private string Save(ExecutionRecord e)
    {
        var path = RecordPath(e.ExecutionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = JsonSerializer.Serialize(e, Json);
        var sha = Sha(text);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        File.WriteAllText(path + ".sha256", sha + "\n");
        return sha;
    }

    /// <summary>
    /// Loads an execution record; refuses one whose content no longer matches the hash written beside it. The sidecar is a per-user file too:
    /// it detects accidental damage, not a deliberate edit (rollback checks the sealed audit for that).
    /// </summary>
    public ExecutionRecord Load(string executionId) => LoadWithHash(executionId).Record;

    private (ExecutionRecord Record, string Sha256) LoadWithHash(string executionId)
    {
        if (executionId.Length == 0 || executionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || executionId.Contains(".."))
            throw new PolicyLifecycleException($"Identificatorul de execuție „{executionId}” nu este valid.");
        var path = RecordPath(executionId);
        if (!File.Exists(path)) throw new PolicyLifecycleException($"Execuția {executionId} nu există.");
        var text = File.ReadAllText(path);
        var sha = Sha(text);
        var expected = File.Exists(path + ".sha256") ? File.ReadAllText(path + ".sha256").Trim() : "";
        if (!expected.Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException($"Înregistrarea execuției {executionId} a fost modificată (SHA-256 nu corespunde).");
        return (JsonSerializer.Deserialize<ExecutionRecord>(text, Json)!, sha);
    }
}

public sealed record PolicyAuditEntry(
    DateTimeOffset AtUtc, string ExecutionId, string Kind, string Operator, string Approver, string PolicyId, string PolicyVersion, string PolicySha256,
    string PlanSha256, string Station, string ControlId, string Setting, string? Before, string Action, string? After, string Outcome, string Detail)
{
    public string Prev { get; init; } = "";
}

/// <summary>Append-only JSONL audit log; every line carries the SHA-256 of the previous line, so an edited or removed line breaks the chain.</summary>
public sealed class PolicyAuditLog(string path)
{
    private static readonly JsonSerializerOptions Line = new() { WriteIndented = false };

    public string Path => path;

    public void Append(PolicyAuditEntry e)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var last = File.Exists(path) ? File.ReadLines(path).LastOrDefault(l => l.Length > 0) : null;
        var line = JsonSerializer.Serialize(e with { Prev = last is null ? "" : PolicyExecutor.Sha(last) }, Line);
        File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
    }

    public IReadOnlyList<PolicyAuditEntry> Read() =>
        File.Exists(path) ? File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<PolicyAuditEntry>(l, Line)!).ToList() : [];

    /// <summary>Line numbers (1-based) whose "Prev" does not match the previous line.</summary>
    public IReadOnlyList<int> VerifyChain()
    {
        var bad = new List<int>();
        if (!File.Exists(path)) return bad;
        string? prev = null;
        int n = 0;
        foreach (var l in File.ReadLines(path).Where(l => l.Length > 0))
        {
            n++;
            var e = JsonSerializer.Deserialize<PolicyAuditEntry>(l, Line)!;
            if (e.Prev != (prev is null ? "" : PolicyExecutor.Sha(prev))) bad.Add(n);
            prev = l;
        }
        return bad;
    }
}
