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
    [JsonIgnore] public string StateText => ContainmentText.State(State);

    /// <summary>Every destination the program used or tried, each with the source that shows it.</summary>
    [JsonIgnore]
    public IEnumerable<NetworkIntent> AllNetworkIntents =>
        (Scan?.NetworkIntents ?? []).Concat(BlockedAttempts.Select(BlockedConnectionLog.ToIntent));
}

public sealed record ContainmentOptions(bool SuspendProcess, bool CopySampleIntoCase, bool BlockInbound = true);

/// <summary>Texts of the containment state for the screen and the PDF: success words only for a re-check that confirmed it (VERIFIED).</summary>
public static class ContainmentText
{
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
