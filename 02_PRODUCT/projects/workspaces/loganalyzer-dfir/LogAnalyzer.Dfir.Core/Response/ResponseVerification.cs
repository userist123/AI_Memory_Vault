using System.Text;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Response;

/// <summary>
/// State of a controlled response (R13.4-R13.6, R22.6). Only <see cref="Verified"/> means success: the change was applied AND an
/// independent re-read of the system showed the expected state. Nothing else may be shown or reported as success.
/// </summary>
public enum ResponseStatus { Verified, NotVerified, Failed, Rejected, NotAuthorized, Unavailable }

/// <summary>What one re-read showed for one target.</summary>
public sealed record ResponseObservation(ResponseStatus Status, string Observed)
{
    /// <summary>The re-read shows the expected state.</summary>
    public static ResponseObservation Holds(string observed) => new(ResponseStatus.Verified, observed);
    /// <summary>The re-read shows a state other than the expected one (for example the old one).</summary>
    public static ResponseObservation Differs(string observed) => new(ResponseStatus.Failed, observed);
    /// <summary>The re-read could not decide (target vanished, field unreadable, unsupported).</summary>
    public static ResponseObservation Unknown(string observed) => new(ResponseStatus.NotVerified, observed);
}

/// <summary>One target of a response and the re-read that checks it. <see cref="Observe"/> must read the system again; it must not reuse the action's return value.</summary>
public sealed record ResponseProbe(string Target, string Expected, Func<ResponseObservation> Observe);

/// <summary>The re-check evidence for one target.</summary>
public sealed record ResponseCheck(string Target, string Expected, string Observed, ResponseStatus Status);

/// <summary>Outcome of one controlled response (apply or rollback) after its independent re-check.</summary>
public sealed class ResponseOutcome
{
    public string Action { get; init; } = "";
    /// <summary>"apply" or "rollback".</summary>
    public string Phase { get; init; } = "apply";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResponseStatus Status { get; init; } = ResponseStatus.NotVerified;
    /// <summary>Why the status is what it is (for NOT_VERIFIED: the reason the re-check was impossible).</summary>
    public string Cause { get; init; } = "";
    public List<ResponseCheck> Checks { get; init; } = [];
    public DateTimeOffset CheckedUtc { get; init; }
    /// <summary>Set when the outcome could not be written to the case audit; such an outcome is never a success.</summary>
    public string? AuditError { get; set; }

    /// <summary>True only for VERIFIED with its audit entry written.</summary>
    [JsonIgnore] public bool IsSuccess => Status == ResponseStatus.Verified && AuditError is null;

    [JsonIgnore] public string StatusText => Text(Status) + (AuditError is null ? "" : " (AUDIT NESCRIS)");

    public static string Text(ResponseStatus s) => s switch
    {
        ResponseStatus.Verified => "VERIFIED",
        ResponseStatus.NotVerified => "NOT_VERIFIED",
        ResponseStatus.Failed => "FAILED",
        ResponseStatus.Rejected => "REJECTED",
        ResponseStatus.NotAuthorized => "NOT_AUTHORIZED",
        _ => "UNAVAILABLE",
    };

    /// <summary>A response that was not attempted (refused, not authorized, unavailable): nothing to re-check.</summary>
    public static ResponseOutcome NotAttempted(string action, string phase, ResponseStatus status, string cause) => new()
    {
        Action = action, Phase = phase, Status = status, Cause = cause, CheckedUtc = DateTimeOffset.UtcNow,
    };

    /// <summary>One line for the audit log and reports: status, cause, and every target with what the re-read showed.</summary>
    public string Evidence()
    {
        var sb = new StringBuilder($"{Action} {Phase} {Text(Status)}; cauza: {Cause}");
        foreach (var c in Checks) sb.Append($" | {c.Target}: {Text(c.Status)}, așteptat {c.Expected}, observat {c.Observed}");
        return sb.ToString();
    }
}

/// <summary>
/// The single re-check of every controlled response: after an action is applied, each target is re-read from the system and compared
/// with the expected state. Aggregation: any target FAILED -> FAILED (with the list); otherwise any target that could not be re-read ->
/// NOT_VERIFIED (with the cause); VERIFIED only when every target was confirmed. A re-read that throws, is denied, is unsupported or
/// times out is NOT_VERIFIED, never VERIFIED. A response with no target to check is NOT_VERIFIED.
/// </summary>
public static class ResponseVerifier
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public const string AuditAction = "response.verify";

    public static ResponseOutcome Verify(string action, string phase, IEnumerable<ResponseProbe> probes, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var checks = new List<ResponseCheck>();
        foreach (var p in probes)
        {
            var o = Observe(p.Observe, limit);
            checks.Add(new ResponseCheck(p.Target, p.Expected, o.Observed, o.Status));
        }
        return Aggregate(action, phase, checks);
    }

    /// <summary>Aggregates already-made checks with the same rules as <see cref="Verify"/>.</summary>
    public static ResponseOutcome Aggregate(string action, string phase, List<ResponseCheck> checks)
    {
        ResponseStatus status;
        string cause;
        if (checks.Count == 0) (status, cause) = (ResponseStatus.NotVerified, "nicio țintă de verificat: efectul nu poate fi confirmat");
        else if (checks.Any(c => c.Status == ResponseStatus.Failed))
        {
            var bad = checks.Where(c => c.Status != ResponseStatus.Verified).ToList();
            (status, cause) = (ResponseStatus.Failed,
                $"{checks.Count - bad.Count} din {checks.Count} ținte confirmate; neconfirmate: " + string.Join("; ", bad.Select(c => $"{c.Target} ({c.Observed})")));
        }
        else if (checks.Any(c => c.Status != ResponseStatus.Verified))
            (status, cause) = (ResponseStatus.NotVerified,
                "re-verificarea nu a putut fi făcută: " + string.Join("; ", checks.Where(c => c.Status != ResponseStatus.Verified).Select(c => $"{c.Target} ({c.Observed})")));
        else (status, cause) = (ResponseStatus.Verified, $"re-citirea confirmă toate cele {checks.Count} ținte");
        return new ResponseOutcome { Action = action, Phase = phase, Status = status, Cause = cause, Checks = checks, CheckedUtc = DateTimeOffset.UtcNow };
    }

    /// <summary>Runs one re-read with a time limit; every failure of the reader becomes NOT_VERIFIED with its cause.</summary>
    public static ResponseObservation Observe(Func<ResponseObservation> read, TimeSpan timeout)
    {
        Task<ResponseObservation> task;
        try { task = Task.Run(read); }
        catch (Exception ex) { return ResponseObservation.Unknown("re-citirea nu a pornit: " + Describe(ex)); }
        try
        {
            if (!task.Wait(timeout))
                return ResponseObservation.Unknown($"re-citirea nu a răspuns în {timeout.TotalSeconds:0.#} s (timeout)");
            var r = task.Result;
            return r ?? ResponseObservation.Unknown("re-citirea nu a întors nicio stare");
        }
        catch (AggregateException ex) { return ResponseObservation.Unknown("re-citirea a eșuat: " + Describe(ex.GetBaseException())); }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => $"acces refuzat ({ex.Message})",
        NotSupportedException or PlatformNotSupportedException => $"nesuportat ({ex.Message})",
        TimeoutException => $"timeout ({ex.Message})",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    /// <summary>
    /// Writes the outcome and its re-check evidence to the case audit (hash-chained). A missing sink or a failed write is recorded on the
    /// outcome (<see cref="ResponseOutcome.AuditError"/>), which then is never a success; the failure is surfaced, not swallowed.
    /// </summary>
    public static ResponseOutcome Record(ResponseOutcome outcome, Action<string, string>? caseAudit)
    {
        if (caseAudit is null) { outcome.AuditError = "nu există un jurnal de caz în care să se scrie rezultatul"; return outcome; }
        try { caseAudit(AuditAction, outcome.Evidence()); }
        catch (Exception ex) { outcome.AuditError = $"rezultatul nu a putut fi scris în jurnalul cazului: {ex.GetType().Name}: {ex.Message}"; }
        return outcome;
    }
}

/// <summary>
/// Marks a method that changes the station as a controlled response. Its result carries a <see cref="ResponseOutcome"/> produced by
/// <see cref="ResponseVerifier"/> (directly, or as a public property of the returned type). The response-verification tests find every
/// method that reaches the host-change gate or a host-changing primitive and require it to carry this mark.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ControlledResponseAttribute(string reRead) : Attribute
{
    /// <summary>What is re-read after the action (for the inventory).</summary>
    public string ReRead { get; } = reRead;
}

/// <summary>Identity of a process at the moment a response targeted it: a reused PID is a different process.</summary>
public sealed record ResponseProcessIdentity(int Pid, DateTimeOffset StartUtc, string ImagePath, string ImageSha256);

/// <summary>A process found by re-enumerating the station's processes. Null fields could not be read.</summary>
public sealed record ProcessObservation(int Pid, DateTimeOffset? StartUtc, string? ImagePath, bool? AllThreadsSuspended);

/// <summary>Re-enumerates the station's processes (seam: the tests use a fake, production reads the live process list).</summary>
public interface IProcessTable
{
    /// <summary>The process that holds <paramref name="pid"/> now, or null when no process has it. Throws when the list cannot be read.</summary>
    ProcessObservation? Observe(int pid);
    /// <summary>SHA-256 (hex) of an image file, or null when it cannot be read.</summary>
    string? HashImage(string path);
}

/// <summary>Process checks for containment (R13.4): the same process is the same PID AND start time AND image SHA-256.</summary>
public static class ProcessVerification
{
    /// <summary>The identity of the process holding <paramref name="pid"/> now; null when it cannot be established.</summary>
    public static ResponseProcessIdentity? Capture(IProcessTable table, int pid)
    {
        var o = table.Observe(pid);
        if (o is not { StartUtc: { } start, ImagePath: { Length: > 0 } path }) return null;
        var sha = table.HashImage(path);
        return sha is null ? null : new ResponseProcessIdentity(pid, start, path, sha);
    }

    public enum Match { Same, Gone, Reused, Unknown }

    /// <summary>Compares what holds the PID now with the recorded identity.</summary>
    public static (Match Match, string Observed) Compare(ResponseProcessIdentity id, ProcessObservation? now, IProcessTable table)
    {
        if (now is null) return (Match.Gone, $"niciun proces cu PID {id.Pid}: procesul s-a terminat");
        if (now.StartUtc is null || string.IsNullOrEmpty(now.ImagePath))
            return (Match.Unknown, $"PID {id.Pid} există, dar ora de start sau imaginea nu pot fi citite");
        if (now.StartUtc.Value != id.StartUtc)
            return (Match.Reused, $"PID {id.Pid} aparține altui proces (start {now.StartUtc:O}, nu {id.StartUtc:O}): procesul original s-a terminat");
        var sha = table.HashImage(now.ImagePath);
        if (sha is null) return (Match.Unknown, $"imaginea {now.ImagePath} nu poate fi citită pentru SHA-256");
        if (!sha.Equals(id.ImageSha256, StringComparison.OrdinalIgnoreCase))
            return (Match.Reused, $"PID {id.Pid} are altă imagine (SHA-256 {sha[..Math.Min(12, sha.Length)]}…): procesul original s-a terminat");
        return (Match.Same, $"același proces (PID {id.Pid}, start {id.StartUtc:O})");
    }

    /// <summary>Re-check after a suspension: the same process with every thread suspended.</summary>
    public static ResponseProbe Suspended(ResponseProcessIdentity? id, int pid, IProcessTable? table) =>
        new($"proces PID {pid}", "suspendat (toate firele)", () =>
        {
            if (table is null) return ResponseObservation.Unknown("nu există un cititor al listei de procese");
            if (id is null) return ResponseObservation.Unknown("identitatea procesului (start, SHA-256) nu a putut fi stabilită înainte de acțiune");
            var now = table.Observe(pid);
            var (m, observed) = Compare(id, now, table);
            if (m != Match.Same) return ResponseObservation.Unknown(observed);
            return now!.AllThreadsSuspended switch
            {
                true => ResponseObservation.Holds("toate firele sunt suspendate"),
                false => ResponseObservation.Differs("procesul rulează: nu toate firele sunt suspendate"),
                _ => ResponseObservation.Unknown("starea firelor nu poate fi citită"),
            };
        });

    /// <summary>Re-check after a resume (rollback): the original process runs again, or it has ended. A reused PID is counted as ended.</summary>
    public static ResponseProbe NotSuspended(ResponseProcessIdentity? id, int pid, IProcessTable? table) =>
        new($"proces PID {pid}", "nu mai este suspendat (reluat sau terminat)", () =>
        {
            if (table is null) return ResponseObservation.Unknown("nu există un cititor al listei de procese");
            if (id is null) return ResponseObservation.Unknown("identitatea procesului nu a fost stabilită la izolare");
            var now = table.Observe(pid);
            var (m, observed) = Compare(id, now, table);
            if (m is Match.Gone or Match.Reused) return ResponseObservation.Holds(observed);
            if (m == Match.Unknown) return ResponseObservation.Unknown(observed);
            return now!.AllThreadsSuspended switch
            {
                false => ResponseObservation.Holds("procesul rulează din nou"),
                true => ResponseObservation.Differs("procesul este în continuare suspendat"),
                _ => ResponseObservation.Unknown("starea firelor nu poate fi citită"),
            };
        });

    /// <summary>Re-check after a termination: no process with this identity remains. A reused PID (other start time or image) is a terminated original.</summary>
    public static ResponseProbe Terminated(ResponseProcessIdentity? id, int pid, IProcessTable? table) =>
        new($"proces PID {pid}", "terminat", () =>
        {
            if (table is null) return ResponseObservation.Unknown("nu există un cititor al listei de procese");
            if (id is null) return ResponseObservation.Unknown("identitatea procesului nu a fost stabilită înainte de acțiune");
            var (m, observed) = Compare(id, table.Observe(pid), table);
            return m switch
            {
                Match.Gone or Match.Reused => ResponseObservation.Holds(observed),
                Match.Same => ResponseObservation.Differs("procesul rulează în continuare"),
                _ => ResponseObservation.Unknown(observed),
            };
        });
}
