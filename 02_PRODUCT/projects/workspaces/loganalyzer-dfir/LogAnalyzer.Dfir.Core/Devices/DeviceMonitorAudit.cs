using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>An alert as the sealed audit holds it ("device.alert_critical" or "device.alert_info"); the time is the time of the audit record; <c>Class</c> "" = not recorded.</summary>
public sealed record DeviceAuditAlert(DateTimeOffset TimeUtc, string Identity, string Device, string Text, string Class = "");

/// <summary>A device as the sealed audit recorded it the first time the monitor saw it ("device.first_seen"); <c>RecordUtc</c> is the time of the audit record.</summary>
public sealed record SealedDevice(string Identity, string Class, string Serial, DateTimeOffset FirstSeenUtc, DateTimeOffset RecordUtc, string Device = "", int ClassifierVersion = 1);

/// <summary>A kind change the application sealed because its classifier changed ("device.reclassified"): old and new kind, the classifier versions, the operator ("application").</summary>
public sealed record SealedReclassification(DateTimeOffset TimeUtc, string Identity, string OldClass, string NewClass, int OldVersion, int NewVersion, string Device = "", string Operator = "");

/// <summary>A radio state change sealed by the monitor ("device.radio_state"): <c>Blind</c> = from this time the radio is off, absent or disabled.</summary>
public sealed record SealedRadioState(DateTimeOffset TimeUtc, string Kind, bool Blind, string Text, string User);

/// <summary>A device seen present at the start of a monitor run ("device.present", <c>Present</c> true) or disconnected ("device.disconnected", false), with the console user.</summary>
public sealed record SealedPresence(DateTimeOffset TimeUtc, string Identity, string Class, bool Present, string User);

/// <summary>The problem code and the active state of a device as the monitor sealed them ("device.state_changed"): <c>Active</c> = not a disabled radio (code 22).</summary>
public sealed record SealedDeviceState(DateTimeOffset TimeUtc, string Identity, string Class, int Code, bool Active);

/// <summary>One sealed record of a device as the history shows it: the record's time, event name, the device identity, the account that wrote it and its fields.</summary>
public sealed record SealedDeviceEvent(DateTimeOffset TimeUtc, string Event, string Identity, string Operator, IReadOnlyDictionary<string, string> Fields, int Index = 0);

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
    /// <summary>The devices first seen (whole history, also before a journal reset: the monitor rebuilds the rows from them): every journal row must have one, and every one must have its journal row.</summary>
    public IReadOnlyList<SealedDevice> Devices { get; init; } = [];
    /// <summary>The informational alerts (a connection): each in the period must map to a journal row in the period.</summary>
    public IReadOnlyList<DeviceAuditAlert> InfoAlerts { get; init; } = [];
    /// <summary>The journal resets (a damaged journal set aside).</summary>
    public IReadOnlyList<SealedJournalReset> Resets { get; init; } = [];
    /// <summary>The radio state changes (blind / observable again), in order.</summary>
    public IReadOnlyList<SealedRadioState> RadioStates { get; init; } = [];
    /// <summary>Every sealed disconnection and the set of devices present at each run start (whole history).</summary>
    public IReadOnlyList<SealedPresence> Presence { get; init; } = [];
    /// <summary>The kind changes the application sealed after a classifier change ("device.reclassified"), in the order of the audit.</summary>
    public IReadOnlyList<SealedReclassification> Reclassifications { get; init; } = [];
    /// <summary>The sealed problem code and active state transitions of the devices (whole history).</summary>
    public IReadOnlyList<SealedDeviceState> States { get; init; } = [];
    /// <summary>Every sealed record that concerns one device (first seen, connection and disconnection, registration change, alert, state change), in the order of the audit, with the account that wrote it.</summary>
    public IReadOnlyList<SealedDeviceEvent> Events { get; init; } = [];
    /// <summary>Index (1-based line number) of the first record whose time goes back in the audit, or <see cref="int.MaxValue"/> when none does: times of records from there on cannot be relied on.</summary>
    public int FirstRegression { get; init; } = int.MaxValue;
    /// <summary>Re-keyed media, old identity → new identity (as sealed; followed through several re-keys by <see cref="Follow"/>).</summary>
    public IReadOnlyDictionary<string, string> Renames { get; init; } = new Dictionary<string, string>();
    /// <summary>The identity a (possibly old) identity has now, following the sealed re-keys.</summary>
    public string Follow(string id)
    {
        for (int i = 0; i < 16 && Renames.TryGetValue(id, out var next); i++) id = next;
        return id;
    }
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
            if (sd.Class.Length > 0 && !ClassAgrees(audit, id, sd, row.Class))
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
            else if (a.Class.Length > 0 && !a.Class.Equals(row.Class.ToString(), StringComparison.OrdinalIgnoreCase) && !AlertClassAgrees(audit, a, row.Class, sealedById))
                findings.Add($"{kind} sigilată din {T(a.TimeUtc)} pentru {a.Device} ({ClassWord(a.Class)}) are în jurnalul monitorului alt tip: „{DeviceMonitorRules.ClassText(row.Class)}”");
        }
        return findings;
    }

    /// <summary>
    /// The chain of kinds a device legitimately had: the kind of its first-seen record, then every sealed "device.reclassified" note that continues it (the note's
    /// old kind is the current one and its new classifier version is HIGHER than the classifier version the device was last sealed with). A note that does not
    /// continue the chain, or lowers the version, is ignored: it proves nothing about the row.
    /// </summary>
    public static IReadOnlyList<(string Class, SealedReclassification? Via)> KindChain(DeviceAuditReading audit, string identity, SealedDevice first)
    {
        var chain = new List<(string, SealedReclassification?)> { (first.Class, null) };
        var current = first.Class;
        int version = first.ClassifierVersion;
        foreach (var r in audit.Reclassifications.Where(r => r.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.TimeUtc))
        {
            if (!r.OldClass.Equals(current, StringComparison.OrdinalIgnoreCase) || r.NewClass.Equals(r.OldClass, StringComparison.OrdinalIgnoreCase)
                || r.NewVersion <= r.OldVersion || r.NewVersion <= version || r.OldVersion < version
                || !DeviceScope.IsKnownReclassification(r.OldVersion, r.NewVersion, r.OldClass, r.NewClass)) continue;   // only what that version bump is known to change
            current = r.NewClass; version = r.NewVersion;
            chain.Add((current, r));
        }
        return chain;
    }

    /// <summary>
    /// The last kind of every device and the classifier version it was sealed with, by sealed identity (the monitor's source for a reclassification; never the
    /// journal): the first-seen record's kind and version, moved on by every valid sealed note (<see cref="KindChain"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, SealedKind> SealedKinds(DeviceAuditReading audit)
    {
        var map = new Dictionary<string, SealedKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in audit.Devices.GroupBy(d => d.Identity, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.OrderBy(d => d.FirstSeenUtc).First();
            var chain = KindChain(audit, g.Key, first);
            map[g.Key] = new SealedKind(chain[^1].Class, chain[^1].Via?.NewVersion ?? first.ClassifierVersion);
        }
        return map;
    }

    private static bool ClassAgrees(DeviceAuditReading audit, string identity, SealedDevice first, DeviceClass rowClass) =>
        KindChain(audit, identity, first)[^1].Class.Equals(rowClass.ToString(), StringComparison.OrdinalIgnoreCase);

    // a sealed alert written under an earlier kind of the device (before its reclassification) still belongs to the row, once the row is the last kind of the chain
    private static bool AlertClassAgrees(DeviceAuditReading audit, DeviceAuditAlert a, DeviceClass rowClass, IReadOnlyDictionary<string, SealedDevice> sealedById) =>
        sealedById.TryGetValue(a.Identity, out var first) && KindChain(audit, a.Identity, first) is { Count: > 1 } chain
        && chain[^1].Class.Equals(rowClass.ToString(), StringComparison.OrdinalIgnoreCase) && chain.Any(c => c.Class.Equals(a.Class, StringComparison.OrdinalIgnoreCase));

    /// <summary>The text of one "device.reclassified" record (history and report).</summary>
    public static string ReclassifiedText(IReadOnlyDictionary<string, string> f)
    {
        string V(string k) => f.TryGetValue(k, out var v) ? v : "";
        return $"{ClassWord(V("class_old"))} → {ClassWord(V("class_new"))} (clasificator v{V("classifier_old")} → v{V("classifier_new")}: s-a schimbat regula de clasificare a aplicației, nu dispozitivul)";
    }

    /// <summary>
    /// Informational lines for the rows whose kind differs from the sealed first-seen kind only through a valid chain of sealed reclassification notes: the
    /// reason is the classifier, so D05 does not report them as a kind changed in the file.
    /// </summary>
    public static IReadOnlyList<string> Reclassifications(DeviceJournal journal, DeviceAuditReading audit)
    {
        var lines = new List<string>();
        var sealedById = audit.Devices.GroupBy(d => d.Identity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.FirstSeenUtc).First(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in journal.Devices)
        {
            if (!sealedById.TryGetValue(OperationAudit.DeviceField(row.Identity), out var first)) continue;
            var chain = KindChain(audit, OperationAudit.DeviceField(row.Identity), first);
            if (chain.Count < 2 || !chain[^1].Class.Equals(row.Class.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (cls, via) in chain.Skip(1))
                lines.Add($"RECLASIFICAT (informativ): {(row.Name.Length > 0 ? row.Name : row.Identity)} — {ClassWord(via!.OldClass)} → {ClassWord(cls)}, {DeviceMonitorReport.UtcText(via.TimeUtc)}; " +
                          $"motiv: clasificatorul aplicației a trecut de la versiunea {via.OldVersion} la {via.NewVersion} (dispozitivul este același, nu a fost schimbat); operator: {(via.Operator.Length > 0 ? via.Operator : "aplicație")} (jurnal sigilat)");
        }
        return lines;
    }

    /// <summary>
    /// What the SEALED audit says breaks the policy on an isolated station, whatever the journal row says (the plain journal's problem code and "active seen" time
    /// can be edited): a sealed critical alert of the period for a class the policy forbids, and a sealed state transition showing such a device ACTIVE in the
    /// period or still active from before it (<paramref name="isolated"/> only for the states; the alert exists only where the monitor raised it).
    /// </summary>
    public static IReadOnlyList<string> ForbiddenSealed(DeviceAuditReading audit, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool isolated)
    {
        static string T(DateTimeOffset t) => DeviceMonitorReport.UtcText(t);
        var found = new List<string>();
        foreach (var a in AlertsInPeriod(audit, fromUtc, toUtc).Where(a => DeviceMonitorRules.IsForbiddenClassAlert(a.Text)))
            found.Add($"{(a.Device.Length > 0 ? a.Device : a.Identity)} ({ClassWord(a.Class)}) — {a.Text} (alertă critică sigilată {T(a.TimeUtc)})");
        if (isolated)
            foreach (var g in audit.States.Where(x => x.TimeUtc <= toUtc).GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<DeviceClass>(g.First().Class, ignoreCase: true, out var cls) || !DeviceMonitorRules.ForbiddenOnIsolated(cls, "")) continue;
                var ordered = g.OrderBy(x => x.TimeUtc).ToList();
                var activeInPeriod = ordered.FirstOrDefault(x => x.Active && x.TimeUtc >= fromUtc);
                var carried = ordered.LastOrDefault(x => x.TimeUtc < fromUtc);
                var hit = activeInPeriod ?? (carried is { Active: true } ? carried : null);
                if (hit is not null)
                    found.Add($"{audit.Devices.FirstOrDefault(d => d.Identity.Equals(g.Key, StringComparison.OrdinalIgnoreCase))?.Device ?? g.Key} ({ClassWord(hit.Class)}) — activ (cod {hit.Code}) în perioadă, stare sigilată {T(hit.TimeUtc)}");
            }
        return found;
    }

    /// <summary>
    /// The sealed connections (the alerts of a connection, "device.present" at each run start) and disconnections ("device.disconnected") of a device against its
    /// journal row: a sealed disconnection or presence in the period whose row is not among the rows of the period (<paramref name="inPeriod"/>), a row that claims
    /// absence when the last sealed event is a presence (or presence when it is a disconnection), and a row whose last-disconnected time is not the sealed one.
    /// Only events from the first sealed presence record on are compared (an audit written before the monitor sealed disconnections cannot be held against the row).
    /// </summary>
    public static IReadOnlyList<string> PresenceMismatches(DeviceJournal journal, DeviceAuditReading audit, DateTimeOffset fromUtc, DateTimeOffset toUtc, IEnumerable<DeviceJournalEntry> inPeriod)
    {
        static string T(DateTimeOffset t) => DeviceMonitorReport.UtcText(t);
        static string K(string identity) => OperationAudit.DeviceField(identity);
        var findings = new List<string>();
        if (audit.Presence.Count == 0) return findings;
        var since = audit.Presence.Min(p => p.TimeUtc);
        var rows = new Dictionary<string, DeviceJournalEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in journal.Devices) rows.TryAdd(K(d.Identity), d);
        var listed = new HashSet<string>(inPeriod.Select(d => K(d.Identity)), StringComparer.OrdinalIgnoreCase);
        // a record whose write was refused is pending (visible, never sealed): the device's comparison waits for it
        var pending = new HashSet<string>(journal.PendingSeals.Where(p => p.Event is OperationAudit.DeviceDisconnected or OperationAudit.DevicePresent)
            .Select(p => Field(p.Detail, "identity")).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        const string Connect = "conectare sigilată";
        var events = audit.Presence.Select(p => new PresenceEvent(p.TimeUtc, p.Identity, p.Present, p.Class, p.Present ? "prezență sigilată la pornirea monitorului" : "deconectare sigilată"))
            .Concat(audit.InfoAlerts.Concat(audit.CriticalAlerts).Where(a => !DeviceMonitorRules.IsVolumeChangedAlert(a.Text)).Select(a => new PresenceEvent(a.TimeUtc, a.Identity, true, a.Class, Connect)))
            .Where(e => e.TimeUtc >= since && e.TimeUtc <= toUtc).ToList();
        foreach (var g in events.GroupBy(e => e.Identity, StringComparer.OrdinalIgnoreCase))
        {
            if (!rows.TryGetValue(g.Key, out var row) || pending.Contains(g.Key)) continue;
            var ordered = g.OrderBy(e => e.TimeUtc).ToList();
            var what = $"{(row.Name.Length > 0 ? row.Name : g.Key)} ({ClassWord(ordered[0].Class)})";
            if (ordered.FirstOrDefault(e => e.TimeUtc >= fromUtc && e.Kind != Connect) is { } inP && !listed.Contains(g.Key))
                findings.Add($"{what}: {inP.Kind} din {T(inP.TimeUtc)}, dar rândul lui nu apare în perioadă în jurnalul monitorului (dată sau stare schimbată în fișier?)");
            var last = ordered[^1];
            if (last.Present && !row.Present)
                findings.Add($"{what}: jurnalul sigilat îl arată prezent ({last.Kind} {T(last.TimeUtc)}), dar rândul lui din jurnalul monitorului spune deconectat");
            else if (!last.Present && row.Present)
                findings.Add($"{what}: jurnalul sigilat îl arată deconectat la {T(last.TimeUtc)}, dar rândul lui din jurnalul monitorului spune conectat");
            else if (!last.Present && (row.LastDisconnectedUtc is null || (row.LastDisconnectedUtc.Value - last.TimeUtc).Duration() > FirstSeenTolerance))
                findings.Add($"{what}: deconectarea sigilată este la {T(last.TimeUtc)}, dar rândul lui din jurnalul monitorului are altă oră a deconectării ({(row.LastDisconnectedUtc is { } d ? T(d) : "nicio oră")})");
        }
        return findings;
    }

    private sealed record PresenceEvent(DateTimeOffset TimeUtc, string Identity, bool Present, string Class, string Kind);

    private static string Field(string detail, string key)
    {
        foreach (var kv in detail.Split("; "))
            if (kv.StartsWith(key + "=", StringComparison.Ordinal)) return kv[(key.Length + 1)..];
        return "";
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
