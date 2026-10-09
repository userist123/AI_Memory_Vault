using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>
/// The organisation's procedure profile (owner decisions 18 and 19): working hours, log maintenance, approved software, expected GPO
/// policy, zones and approved transfers. It is DATA the operator enters (or pastes / imports); the application never applies any of it
/// to Windows (decision 13). Every section may be empty: an empty section is "nedefinit" (not defined) and is never reported as "conform".
/// </summary>
public sealed class ProcedureProfile
{
    public const string CurrentSchemaVersion = "1.0";

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "";
    public DateTimeOffset? UpdatedUtc { get; set; }

    // Working hours
    public List<WorkingHoursRow> WorkingHours { get; set; } = [];
    public List<HolidayRow> Holidays { get; set; } = [];
    public List<ShiftRow> Shifts { get; set; } = [];

    // Log maintenance
    public List<ApprovedAccountRow> ApprovedAccounts { get; set; } = [];
    public List<MaintenanceWindowRow> MaintenanceWindows { get; set; } = [];
    public List<RotationStepRow> RotationProcedure { get; set; } = [];

    // Approved software
    public List<ApprovedSoftwareRow> ApprovedSoftware { get; set; } = [];

    // Expected GPO / policy: a link to a policy in the existing Policy store / import (Core/Policy), never a copy of it.
    public List<ExpectedPolicyRow> ExpectedPolicies { get; set; } = [];

    // Zones and transfers (decision 19)
    public List<ZoneRow> Zones { get; set; } = [];
    public List<TransferChannelRow> TransferChannels { get; set; } = [];
    public List<NetworkDestinationRow> NetworkDestinations { get; set; } = [];
}

public sealed class WorkingHoursRow { public string Day { get; set; } = ""; public string Start { get; set; } = ""; public string End { get; set; } = ""; }
public sealed class HolidayRow { public string Date { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class ShiftRow { public string Name { get; set; } = ""; public string Start { get; set; } = ""; public string End { get; set; } = ""; }
public sealed class ApprovedAccountRow { public string Account { get; set; } = ""; public string Note { get; set; } = ""; }

/// <summary>Kind is once | weekly | monthly. Once: Date. Weekly: Day. Monthly: Ordinal (1-4 or last) + Day, e.g. the first Monday of the month.</summary>
public sealed class MaintenanceWindowRow
{
    public string Kind { get; set; } = "";
    public string Date { get; set; } = "";
    public string Day { get; set; } = "";
    public string Ordinal { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class RotationStepRow { public string Step { get; set; } = ""; public string Note { get; set; } = ""; }
public sealed class ApprovedSoftwareRow { public string Name { get; set; } = ""; public string Publisher { get; set; } = ""; public string PathPattern { get; set; } = ""; public string Sha256 { get; set; } = ""; }
public sealed class ExpectedPolicyRow { public string PolicyPath { get; set; } = ""; public string Sha256 { get; set; } = ""; public string Note { get; set; } = ""; }
public sealed class ZoneRow { public string Name { get; set; } = ""; public string Description { get; set; } = ""; }
public sealed class TransferChannelRow { public string Name { get; set; } = ""; public string FromZone { get; set; } = ""; public string ToZone { get; set; } = ""; public string Medium { get; set; } = ""; public string Note { get; set; } = ""; }
public sealed class NetworkDestinationRow { public string Address { get; set; } = ""; public string Zone { get; set; } = ""; public string Purpose { get; set; } = ""; }

/// <summary>The tables of the profile, in the order the view shows them.</summary>
public enum ProfileTable
{
    WorkingHours, Holidays, Shifts,
    ApprovedAccounts, MaintenanceWindows, RotationProcedure,
    ApprovedSoftware, ExpectedPolicies,
    Zones, TransferChannels, NetworkDestinations,
}

/// <summary>A validation problem on one line (1-based data row; for imports, the line of the text). Never dropped silently.</summary>
public sealed record ProfileIssue(ProfileTable? Table, int Line, string Message, bool IsError = true)
{
    public override string ToString() => $"{(Table is null ? "profil" : ProfileTables.Title(Table.Value))}" + (Line > 0 ? $", linia {Line}" : "") + $": {Message}" + (IsError ? "" : " (avertisment)");
}
