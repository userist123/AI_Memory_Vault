using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>Using the profile in a case: a copy of exactly what was used goes into the case and into its custody, so the result can be explained later.</summary>
public static class ProfileSnapshot
{
    public const string RelPath = "Analysis/procedure_profile.json";

    /// <summary>
    /// Writes <c>Analysis/procedure_profile.json</c> (the profile as used), registers it with <see cref="CaseWorkspace.RecordOutput"/> (SHA-256 in the
    /// custody chain) and audits <c>profile.used</c> with the sections that are defined and those that are "nedefinit". Returns the SHA-256 of the snapshot.
    /// </summary>
    public static string WriteTo(CaseWorkspace ws, ProcedureProfile profile, IReadOnlyList<ProfileIssue>? issues = null)
    {
        var full = ws.FullPath(RelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, ProfileStore.ToJson(profile));
        var rec = ws.RecordOutput(RelPath, "ProcedureProfile", ProcedureProfile.CurrentSchemaVersion);
        int bad = issues?.Count(i => i.IsError) ?? 0;
        ws.Audit("profile.used", $"sha256={rec.Sha256}; {ProfileOps.Describe(profile)}" + (bad > 0 ? $"; {bad} rânduri invalide ignorate" : ""));
        return rec.Sha256;
    }

    /// <summary>
    /// The maintenance policy the analysers use for a case: recurring windows expanded over the time span of the evidence (plus a day on each side)
    /// in the case's own time zone. <c>null</c> when there is no profile or its maintenance section is not defined.
    /// </summary>
    public static LogMaintenancePolicy? MaintenancePolicyFor(ProcedureProfile? profile, IEnumerable<DateTimeOffset> eventTimesUtc, string caseTimezoneId)
    {
        if (profile is null) return null;
        var times = eventTimesUtc.ToList();
        var from = times.Count > 0 ? times.Min() : DateTimeOffset.UtcNow;
        var to = times.Count > 0 ? times.Max() : DateTimeOffset.UtcNow;
        TimeZoneInfo zone;
        try { zone = string.IsNullOrWhiteSpace(caseTimezoneId) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(caseTimezoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.Local; }
        return ProfileOps.ToMaintenancePolicy(profile, from.AddDays(-1), to.AddDays(1), zone);
    }
}
