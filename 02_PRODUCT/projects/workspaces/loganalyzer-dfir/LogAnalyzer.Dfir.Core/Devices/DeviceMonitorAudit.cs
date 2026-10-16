using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>A critical alert as the sealed audit holds it ("device.alert_critical"); the time is the time of the audit record.</summary>
public sealed record DeviceAuditAlert(DateTimeOffset TimeUtc, string Identity, string Device, string Text);

/// <summary>A device the full rescan on resume found appeared or gone ("device.observed_on_resume").</summary>
public sealed record DeviceAuditObservation(DateTimeOffset TimeUtc, string Identity, string Device, string Change);

/// <summary>
/// What the sealed audit says about the device monitor (see <see cref="OperationAudit.ReadDeviceMonitor"/>): the last registration number written for each
/// identity (values as the audit holds them, see <see cref="OperationAudit.DeviceField"/>), the critical alerts, the monitor's running windows
/// (<see cref="Runs"/>: start, last heartbeat, clean stop; the ONLY source of the coverage), the intervals it recorded as lost and the devices observed on resume.
/// </summary>
public sealed record DeviceAuditReading(bool ChainValid, IReadOnlyDictionary<string, string> LastRegistration, IReadOnlyList<DeviceAuditAlert> CriticalAlerts, int Unreadable)
{
    public IReadOnlyList<MonitorRun> Runs { get; init; } = [];
    public IReadOnlyList<SealedGap> Gaps { get; init; } = [];
    public IReadOnlyList<DeviceAuditObservation> ObservedOnResume { get; init; } = [];
    public IReadOnlyList<string> Migrations { get; init; } = [];
}

/// <summary>
/// A registration number that does not match the sealed audit. <c>Device</c> is null when the audit knows the identity but the journal has no row for it (a
/// deleted row); <c>InAudit</c> is null when the audit has no entry for the device.
/// </summary>
public sealed record RegistrationMismatch(DeviceJournalEntry? Device, string Identity, string InJournal, string? InAudit);

/// <summary>The comparison of the monitor's journal with the sealed audit (pure): the journal is a plain file, the audit is the record that cannot be edited quietly.</summary>
public static class DeviceMonitorAuditCheck
{
    /// <summary>
    /// Every device whose registration number in the journal differs from the LAST "device.registration_number_changed" entry of the sealed audit for its
    /// identity: a number the audit never recorded, a different value, or a number the audit has but the journal lost. No entry and no number agree. Also every
    /// identity the sealed audit holds a registration for that has NO row in the journal (a deleted row).
    /// </summary>
    public static IReadOnlyList<RegistrationMismatch> Mismatches(IEnumerable<DeviceJournalEntry> devices, DeviceAuditReading audit)
    {
        var bad = new List<RegistrationMismatch>();
        var rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in devices)
        {
            var key = OperationAudit.DeviceField(d.Identity);
            rows.Add(key);
            var inJournal = OperationAudit.DeviceField(d.RegistrationNumber);
            string? inAudit = audit.LastRegistration.TryGetValue(key, out var v) ? v : null;
            if (!string.Equals(inJournal, inAudit ?? "", StringComparison.Ordinal)) bad.Add(new(d, d.Identity, d.RegistrationNumber.Trim(), inAudit));
        }
        foreach (var (identity, number) in audit.LastRegistration)
            if (!rows.Contains(identity)) bad.Add(new(null, identity, "", number));
        return bad;
    }

    /// <summary>The sealed critical alerts of the period.</summary>
    public static IReadOnlyList<DeviceAuditAlert> AlertsInPeriod(DeviceAuditReading audit, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        audit.CriticalAlerts.Where(a => a.TimeUtc >= fromUtc && a.TimeUtc <= toUtc).OrderBy(a => a.TimeUtc).ToList();

    /// <summary>Whether the journal has a row for the device a sealed alert names (a missing row = a row removed from the journal).</summary>
    public static bool HasDevice(DeviceJournal journal, DeviceAuditAlert a) =>
        journal.Devices.Any(d => OperationAudit.DeviceField(d.Identity).Equals(a.Identity, StringComparison.OrdinalIgnoreCase));

    public static bool IsCloneAlert(string text) => DeviceMonitorRules.IsCloneAlert(text);

    /// <summary>
    /// A journal critical alert that the sealed audit does not have (same device identity and text, within two minutes): the audit write failed when it was
    /// raised, or the alert was added to the journal by hand.
    /// </summary>
    public static bool InAudit(DeviceAlertRecord a, DeviceAuditReading audit) =>
        audit.CriticalAlerts.Any(x => x.Identity.Equals(OperationAudit.DeviceField(a.Identity), StringComparison.OrdinalIgnoreCase)
                                      && x.Text.Equals(OperationAudit.DeviceField(a.Text), StringComparison.OrdinalIgnoreCase) && (x.TimeUtc - a.TimeUtc).Duration() <= TimeSpan.FromMinutes(2));
}
