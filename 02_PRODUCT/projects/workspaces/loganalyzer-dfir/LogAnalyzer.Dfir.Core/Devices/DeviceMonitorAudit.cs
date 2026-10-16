using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>An alert as the sealed audit holds it ("device.alert_critical" or "device.alert_info"); the time is the time of the audit record; <c>Class</c> "" = not recorded.</summary>
public sealed record DeviceAuditAlert(DateTimeOffset TimeUtc, string Identity, string Device, string Text, string Class = "");

/// <summary>A device as the sealed audit recorded it the first time the monitor saw it ("device.first_seen"); <c>RecordUtc</c> is the time of the audit record.</summary>
public sealed record SealedDevice(string Identity, string Class, string Serial, DateTimeOffset FirstSeenUtc, DateTimeOffset RecordUtc, string Device = "");

/// <summary>A radio state change sealed by the monitor ("device.radio_state"): <c>Blind</c> = from this time the radio is off, absent or disabled.</summary>
public sealed record SealedRadioState(DateTimeOffset TimeUtc, string Kind, bool Blind, string Text, string User);

/// <summary>A damaged journal set aside and replaced by an empty one ("device.journal_reset"), with the SHA-256 of the damaged file.</summary>
public sealed record SealedJournalReset(DateTimeOffset TimeUtc, string Sha256, string Reason, string File = "");

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
    /// <summary>The devices first seen (after the last journal reset): every journal row must have one, and every one must have its journal row.</summary>
    public IReadOnlyList<SealedDevice> Devices { get; init; } = [];
    /// <summary>The informational alerts (a connection): each in the period must map to a journal row in the period.</summary>
    public IReadOnlyList<DeviceAuditAlert> InfoAlerts { get; init; } = [];
    /// <summary>The journal resets (a damaged journal set aside).</summary>
    public IReadOnlyList<SealedJournalReset> Resets { get; init; } = [];
    /// <summary>The radio state changes (blind / observable again), in order.</summary>
    public IReadOnlyList<SealedRadioState> RadioStates { get; init; } = [];
    /// <summary>The time of the last journal reset, or null.</summary>
    public DateTimeOffset? LastResetUtc => Resets.Count > 0 ? Resets.Max(r => r.TimeUtc) : null;
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

    /// <summary>How far a first-seen time may differ between the journal and the sealed record.</summary>
    public static readonly TimeSpan FirstSeenTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The journal's device rows compared with the sealed audit both ways (finding B1 of the review: the journal is a plain file anyone may edit). Each finding is a
    /// sentence for the evidence list; any finding is a possible tampering:
    /// <list type="bullet">
    /// <item>a device sealed as first seen that has no journal row, or whose row has another class or another first-seen time;</item>
    /// <item>an in-scope journal row that has no sealed first-seen record (a row added by hand);</item>
    /// <item>a device sealed as first seen IN the period whose row is not among the rows of the period (its dates or state moved out of it);</item>
    /// <item>a sealed alert (critical or connection) of the period, written after the last journal reset, whose device has no in-scope journal row of the same
    /// class in the period (<paramref name="inPeriod"/>: the rows the report lists), e.g. a row whose dates were moved out of the period.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> DeviceMismatches(DeviceJournal journal, DeviceAuditReading audit, DateTimeOffset fromUtc, DateTimeOffset toUtc, IEnumerable<DeviceJournalEntry> inPeriod)
    {
        static string T(DateTimeOffset t) => DeviceMonitorReport.UtcText(t);
        static string K(string identity) => OperationAudit.DeviceField(identity);
        var findings = new List<string>();
        var rows = new Dictionary<string, DeviceJournalEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in journal.Devices) rows.TryAdd(K(d.Identity), d);
        var sealedById = audit.Devices.GroupBy(d => d.Identity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.FirstSeenUtc).First(), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, sd) in sealedById)
        {
            if (sd.FirstSeenUtc > toUtc) continue;
            var what = $"{(sd.Device.Length > 0 ? sd.Device : id)} ({ClassWord(sd.Class)}, văzut prima dată {T(sd.FirstSeenUtc)})";
            if (!rows.TryGetValue(id, out var row))
            {
                findings.Add($"dispozitivul {what} este în jurnalul sigilat, dar rândul lui lipsește din jurnalul monitorului (rând șters?)");
                continue;
            }
            if (sd.Class.Length > 0 && !sd.Class.Equals(row.Class.ToString(), StringComparison.OrdinalIgnoreCase))
                findings.Add($"dispozitivul {what} apare în jurnalul monitorului cu alt tip: „{DeviceMonitorRules.ClassText(row.Class)}” (tip schimbat în fișier?)");
            if ((row.FirstSeenUtc - sd.FirstSeenUtc).Duration() > FirstSeenTolerance)
                findings.Add($"dispozitivul {what} apare în jurnalul monitorului cu altă dată a primei observări: {T(row.FirstSeenUtc)} (dată mutată în fișier?)");
        }
        foreach (var row in journal.Devices.Where(DeviceScope.IsInScope))
            if (row.FirstSeenUtc <= toUtc && !sealedById.ContainsKey(K(row.Identity)))
                findings.Add($"rândul {DeviceMonitorRules.ClassText(row.Class)} „{(row.Name.Length > 0 ? row.Name : row.Identity)}” (văzut prima dată {T(row.FirstSeenUtc)}) nu are înregistrarea primei observări în jurnalul sigilat (rând adăugat în fișier?)");
        var listed = new Dictionary<string, DeviceJournalEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in inPeriod) listed.TryAdd(K(d.Identity), d);
        foreach (var (id, sd) in sealedById)
            if (sd.FirstSeenUtc >= fromUtc && sd.FirstSeenUtc <= toUtc && rows.ContainsKey(id) && !listed.ContainsKey(id))
                findings.Add($"dispozitivul {(sd.Device.Length > 0 ? sd.Device : id)} ({ClassWord(sd.Class)}) a fost văzut prima dată în perioadă ({T(sd.FirstSeenUtc)}), dar rândul lui nu apare în perioadă în jurnalul monitorului (tip, stare sau date schimbate în fișier?)");
        var reset = audit.LastResetUtc;
        foreach (var (a, kind) in audit.CriticalAlerts.Select(a => (a, "alerta critică")).Concat(audit.InfoAlerts.Select(a => (a, "alerta de conectare"))))
        {
            if (a.TimeUtc < fromUtc || a.TimeUtc > toUtc || reset is { } r && a.TimeUtc < r) continue;
            if (!listed.TryGetValue(a.Identity, out var row))
                findings.Add($"{kind} sigilată din {T(a.TimeUtc)} pentru {a.Device} ({ClassWord(a.Class)}) — {a.Text} — nu are rând în perioadă în jurnalul monitorului (rând șters sau cu date mutate?)");
            else if (a.Class.Length > 0 && !a.Class.Equals(row.Class.ToString(), StringComparison.OrdinalIgnoreCase))
                findings.Add($"{kind} sigilată din {T(a.TimeUtc)} pentru {a.Device} ({ClassWord(a.Class)}) are în jurnalul monitorului alt tip: „{DeviceMonitorRules.ClassText(row.Class)}”");
        }
        return findings;
    }

    private static string ClassWord(string sealedClass) =>
        Enum.TryParse<DeviceClass>(sealedClass, ignoreCase: true, out var c) ? DeviceMonitorRules.ClassText(c) : sealedClass.Length > 0 ? sealedClass : "tip necunoscut";

    /// <summary>
    /// A journal critical alert that the sealed audit does not have (same device identity and text, within two minutes): the audit write failed when it was
    /// raised, or the alert was added to the journal by hand.
    /// </summary>
    public static bool InAudit(DeviceAlertRecord a, DeviceAuditReading audit) =>
        audit.CriticalAlerts.Any(x => x.Identity.Equals(OperationAudit.DeviceField(a.Identity), StringComparison.OrdinalIgnoreCase)
                                      && x.Text.Equals(OperationAudit.DeviceField(a.Text), StringComparison.OrdinalIgnoreCase) && (x.TimeUtc - a.TimeUtc).Duration() <= TimeSpan.FromMinutes(2));
}
