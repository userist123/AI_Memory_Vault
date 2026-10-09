using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Containment;

public enum ContainmentState { Contained, Released, Failed }

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

    /// <summary>Every destination the program used or tried, each with the source that shows it.</summary>
    [JsonIgnore]
    public IEnumerable<NetworkIntent> AllNetworkIntents =>
        (Scan?.NetworkIntents ?? []).Concat(BlockedAttempts.Select(BlockedConnectionLog.ToIntent));
}

public sealed record ContainmentOptions(bool SuspendProcess, bool CopySampleIntoCase, bool BlockInbound = true);
