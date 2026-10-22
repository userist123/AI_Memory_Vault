using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Containment;

/// <summary>Contained = applied AND re-checked (VERIFIED); NotVerified = applied, the re-check could not confirm it; Failed = not applied or the re-check shows otherwise.</summary>
public enum ContainmentState { Contained, Released, Failed, NotVerified }

public sealed record ContainmentAction(DateTimeOffset TimeUtc, string Action, string Target, bool Success, string Detail, string Operator);

/// <summary>One contained program: what was done to it, what the scan found, and where the evidence is.</summary>
public sealed class ProcessIncident
{
    public required string IncidentId { get; init; }
    public required string CaseId { get; init; }
    public required string ProgramPath { get; init; }
    public int? Pid { get; init; }
    public required string Trigger { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public ContainmentState State { get; set; }
    public bool ProcessSuspended { get; set; }
    public List<string> FirewallRules { get; init; } = [];
    public List<ContainmentAction> Actions { get; init; } = [];
    public ProcessScanReport? Scan { get; set; }
    public List<BlockedConnection> BlockedAttempts { get; init; } = [];
    public List<Finding> Findings { get; init; } = [];
    public List<string> EvidenceIds { get; init; } = [];
    public string Notes { get; set; } = "";
    /// <summary>Identity of the targeted process when containment started (PID + start time + image SHA-256); null without a PID or when it could not be read.</summary>
    public LogAnalyzer.Dfir.Response.ResponseProcessIdentity? ProcessIdentity { get; set; }
    /// <summary>Independent re-check of the containment (firewall rules re-read, process re-enumerated).</summary>
    public LogAnalyzer.Dfir.Response.ResponseOutcome? Verification { get; set; }
    /// <summary>Independent re-check of the last release (rollback).</summary>
    public LogAnalyzer.Dfir.Response.ResponseOutcome? ReleaseVerification { get; set; }
    /// <summary>The state as shown on screen (success words only when re-checked).</summary>
    [JsonIgnore] public string StateText => ContainmentText.State(this);

    /// <summary>Every destination the program used or tried, each with the source that shows it.</summary>
    [JsonIgnore]
    public IEnumerable<NetworkIntent> AllNetworkIntents =>
        (Scan?.NetworkIntents ?? []).Concat(BlockedAttempts.Select(BlockedConnectionLog.ToIntent));
}

/// <param name="ExpectedSha256">SHA-256 of the suspect's image when known: the process holding the PID is suspended only if its image matches.</param>
public sealed record ContainmentOptions(bool SuspendProcess, bool CopySampleIntoCase, bool BlockInbound = true, string? ExpectedSha256 = null);

/// <summary>Which programs automatic containment must leave alone.</summary>
public static class ContainmentAutoPolicy
{
    /// <summary>
    /// Programs already handled: contained, not verified, or any not-released incident that still holds firewall rules (a Failed partial
    /// containment included). Automatic containment skips them, so it never stacks a new rule, incident and sample copy every tick.
    /// </summary>
    public static HashSet<string> AlreadyHandled(IEnumerable<ProcessIncident> incidents) =>
        incidents.Where(i => i.State is ContainmentState.Contained or ContainmentState.NotVerified
                             || (i.State != ContainmentState.Released && i.FirewallRules.Count > 0))
                 .Select(i => i.ProgramPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Texts of the containment state for the screen and the PDF: success words only for a re-check that confirmed it (VERIFIED).</summary>
public static class ContainmentText
{
    /// <summary>Shown for an incident saved before verification existed: "contained" without a recorded re-check is not VERIFIED.</summary>
    public const string ContainedBeforeVerification = "izolat (neverificat: incident înregistrat înainte de verificare)";
    public const string ReleasedBeforeVerification = "izolare ridicată (neverificat: incident înregistrat înainte de verificare)";

    /// <summary>The state of one incident: VERIFIED words only when the incident carries the re-check that confirmed it.</summary>
    public static string State(ProcessIncident i) => i.State switch
    {
        ContainmentState.Contained when i.Verification is not { Status: LogAnalyzer.Dfir.Response.ResponseStatus.Verified } => ContainedBeforeVerification,
        ContainmentState.Released when i.ReleaseVerification is not { Status: LogAnalyzer.Dfir.Response.ResponseStatus.Verified } => ReleasedBeforeVerification,
        _ => State(i.State),
    };

    /// <summary>
    /// One-line outcome for the operator: the firewall part and the suspension part separately, and for a failed containment the
    /// firewall rules that remain active (they are not removed automatically; "Ridicare izolare" removes them).
    /// </summary>
    public static string Summary(ProcessIncident i)
    {
        var v = i.Verification;
        var parts = new List<string>();
        if (v is not null)
        {
            var ruleChecks = v.Checks.Where(c => !c.Target.StartsWith("proces PID", StringComparison.Ordinal)).ToList();
            if (ruleChecks.Count > 0)
                parts.Add($"reguli firewall: {ruleChecks.Count(c => c.Status == LogAnalyzer.Dfir.Response.ResponseStatus.Verified)} din {ruleChecks.Count} confirmate la re-citire");
            foreach (var c in v.Checks.Where(c => c.Target.StartsWith("proces PID", StringComparison.Ordinal)))
                parts.Add($"{c.Target}: {LogAnalyzer.Dfir.Response.ResponseOutcome.Text(c.Status)} ({c.Observed})");
        }
        if (i.State == ContainmentState.Failed)
            parts.Add(i.FirewallRules.Count > 0
                ? $"reguli rămase ACTIVE: {string.Join(", ", i.FirewallRules)} (nu se șterg automat; folosiți „Ridicare izolare”)"
                : "nicio regulă activă");
        return string.Join("; ", parts);
    }

    public static string State(ContainmentState s) => s switch
    {
        ContainmentState.Contained => "IZOLAT (VERIFICAT)",
        ContainmentState.Released => "IZOLARE RIDICATĂ (VERIFICAT)",
        ContainmentState.NotVerified => "IZOLARE NEVERIFICATĂ",
        _ => "IZOLARE EȘUATĂ",
    };

    /// <summary>A logged action: the command returned, which is not a verification.</summary>
    public static string Action(bool success) => success ? "aplicat (vezi verificarea)" : "EȘUAT";

    /// <summary>Verification lines: status, cause, audit problem, and every re-checked target.</summary>
    public static IEnumerable<string> Verification(string title, LogAnalyzer.Dfir.Response.ResponseOutcome? v)
    {
        if (v is null) { yield return $"{title}: NOT_VERIFIED — nu există o re-verificare înregistrată."; yield break; }
        yield return $"{title}: {v.StatusText} — {v.Cause}";
        if (v.AuditError is not null) yield return "  ATENȚIE: " + v.AuditError;
        foreach (var c in v.Checks)
            yield return $"  {c.Target}: {LogAnalyzer.Dfir.Response.ResponseOutcome.Text(c.Status)} (așteptat: {c.Expected}; observat: {c.Observed})";
    }
}
