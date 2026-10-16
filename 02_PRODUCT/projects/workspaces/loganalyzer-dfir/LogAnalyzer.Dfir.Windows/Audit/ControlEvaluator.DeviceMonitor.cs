using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Devices;
using Ro = LogAnalyzer.Dfir.Language.RoText;

namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// D05 (owner decision 2026-10-16): the storage media and externally connected devices the real-time monitor saw in the period (<see cref="DeviceScope"/>:
    /// the same scope as the monitor page and report section 3c), with state, user at connection, registration number and alerts.
    /// <list type="bullet">
    /// <item>NECONFORM when a registration number in the journal does not match the LAST "device.registration_number_changed" entry of the sealed audit for that
    /// identity (also an identity the audit has and the journal has lost), when the journal's running windows disagree with the sealed ones, or when the
    /// journal's rows disagree with the devices sealed on first sight (a row missing, re-classed, re-dated or added by hand, or a sealed alert of the period
    /// without an in-scope row of the same class in the period: <see cref="DeviceMonitorAuditCheck.DeviceMismatches"/>).</item>
    /// <item>NECONFORM on an isolated station (DE VERIFICAT on an internet station) when a storage medium has no number, a forbidden class was seen active or a
    /// serial appeared on two devices (possible clone, also a device without a unique serial next to a serialled twin).</item>
    /// <item>NEDETERMINAT when the journal or the sealed audit cannot be read or verified, or when no SEALED running window of the monitor covers any part of the
    /// period (coverage is computed only from sealed records).</item>
    /// <item>DE VERIFICAT, never CONFORM, when the monitor did not run during the whole period or recorded a lost interval however short (each uncovered
    /// interval is named, and the storage devices Windows' own history shows in them are listed), when it observed devices on resume, when the journal is not
    /// trusted or was reset (damaged, sealed), when a critical alert exists only in the journal, when sealed records cannot be read, when a device came back
    /// with another volume, or when it saw no device at all.</item>
    /// </list>
    /// CONFORM only with the whole period covered by sealed running windows, at least one device seen, none breaking the rule, no unsealed critical alert and the
    /// journal agreeing with the sealed audit.
    /// </summary>
    private static void DeviceMonitorCheck(StationFacts f, ControlReport r)
    {
        const string title = "Medii de stocare și dispozitive conectate extern, văzute de monitorul în timp real";
        const string rec = "Pentru fiecare mediu de stocare fără număr de înregistrare: aflați al cui este și treceți numărul în coloana „Nr. de înregistrare” a monitorului; pentru o clasă interzisă: aflați cine a conectat dispozitivul și de ce; pentru un număr care nu corespunde jurnalului sigilat, o serie dublă sau o rulare care nu apare în jurnalul sigilat: tratați-l ca pe o posibilă alterare sau clonare și anunțați responsabilul de securitate; pentru un interval neacoperit: verificați dispozitivele din istoricul Windows listat.";
        const string limit = " Monitorul vede doar cât timp aplicația este pornită; ce s-a conectat cât a fost închisă apare doar în istoricul Windows (D01).";
        if (f.DeviceMonitorProblem.Length > 0)
        {
            Add(r, "D05", "Dispozitive", title, ControlStatus.Nedeterminat,
                "Jurnalul monitorului de dispozitive nu poate fi folosit: " + f.DeviceMonitorProblem + limit, rec: rec);
            return;
        }
        if (f.DeviceMonitor is not { } journal)
        {
            Add(r, "D05", "Dispozitive", title, ControlStatus.Nedeterminat,
                "Jurnalul monitorului de dispozitive lipsește: monitorul nu a rulat pe această stație (pornește automat cu aplicația)." + limit, rec: rec);
            return;
        }
        bool isolated = f.Posture != StationPosture.Internet;
        var from = f.PeriodStartUtc;
        var to = f.PeriodEndUtc != default ? f.PeriodEndUtc : f.CollectedUtc != default ? f.CollectedUtc : DateTimeOffset.UtcNow;
        var s = DeviceMonitorReport.Build(journal, from, to, isolated);
        var audit = f.DeviceAudit;
        bool auditUsable = audit is { ChainValid: true };
        var devices = s.Devices;

        // 1. the journal against the sealed audit
        var mismatches = auditUsable ? DeviceMonitorAuditCheck.Mismatches(journal.Devices, audit!) : [];
        var sealedAlerts = auditUsable ? DeviceMonitorAuditCheck.AlertsInPeriod(audit!, from, to) : [];
        var cloneAlerts = sealedAlerts.Where(a => DeviceMonitorAuditCheck.IsCloneAlert(a.Text)).ToList();
        var journalCloneAlerts = s.Alerts.Where(a => a.Severity == DeviceAlertSeverity.Critical && DeviceMonitorAuditCheck.IsCloneAlert(a.Text)).ToList();
        // every journal row against the device's sealed first-seen record, both ways, and every sealed alert of the period against an in-period row (B1)
        var deviceFindings = auditUsable
            ? DeviceMonitorAuditCheck.DeviceMismatches(journal, audit!, from, to, devices).Concat(DeviceMonitorAuditCheck.PresenceMismatches(journal, audit!, from, to, devices)).ToList() : [];
        // what the sealed audit alone says about a class the policy forbids (the plain journal's problem code and "active" time can be edited)
        var forbiddenSealed = auditUsable ? DeviceMonitorAuditCheck.ForbiddenSealed(audit!, from, to, isolated) : [];
        var pendingSeals = journal.PendingSeals?.Count ?? 0;
        // the critical alerts of the journal that the sealed audit does not have (its write failed when they were raised, or they were added by hand)
        var unsealed = auditUsable ? s.Alerts.Where(a => a.Severity == DeviceAlertSeverity.Critical && !DeviceMonitorAuditCheck.InAudit(a, audit!)).ToList() : [];

        // 2. coverage of the period by the monitor's SEALED running windows (the journal's own copy is only compared with them)
        var coverage = auditUsable ? DeviceMonitorCoverage.Analyze(audit!.Runs, audit.Gaps, from, to) : null;
        var gaps = coverage?.Gaps ?? [];
        var runDisagreements = auditUsable ? DeviceMonitorCoverage.CrossCheck(journal.Runs, audit!.Runs, from, to) : [];
        var history = coverage is { Gaps.Count: > 0 } ? WindowsStorageHistoryInGaps(f, gaps).ToList() : [];
        var resumed = auditUsable ? audit!.ObservedOnResume.Where(o => o.TimeUtc >= from && o.TimeUtc <= to).ToList() : [];
        var lostInPeriod = auditUsable ? audit!.Gaps.Where(g => g.ToUtc >= from && g.FromUtc <= to).ToList() : [];
        var resets = auditUsable ? audit!.Resets.Where(x => x.TimeUtc >= from && x.TimeUtc <= to).ToList() : [];
        var volumeAlerts = (auditUsable ? audit!.InfoAlerts.Concat(audit.CriticalAlerts).Where(a => a.TimeUtc >= from && a.TimeUtc <= to && DeviceMonitorRules.IsVolumeChangedAlert(a.Text))
                               .Select(a => $"VOLUM DIFERIT {DeviceMonitorReport.UtcText(a.TimeUtc)}: {a.Device} — {a.Text} (jurnal sigilat)")
                           : s.Alerts.Where(a => DeviceMonitorRules.IsVolumeChangedAlert(a.Text)).Select(a => "VOLUM DIFERIT " + DeviceMonitorReport.AlertLine(a))).ToList();
        var blind = journal.Radios.Where(x => x.BlindNow || x.LastBlindUtc >= from && x.FirstBlindUtc <= to).ToList();
        int unreadable = auditUsable ? audit!.Unreadable : 0;
        bool untrusted = f.DeviceJournalUntrusted.Length > 0;

        // a journal window the sealed audit does not back is a tampering finding; when the sealed audit has no window over the period at all the status stays NEDETERMINAT
        // (the audit write may simply have failed), but the disagreement is still listed
        bool tamper = mismatches.Count > 0 || deviceFindings.Count > 0 || runDisagreements.Count > 0 && coverage is { AnyCoverage: true };
        bool policy = s.Offending.Count > 0 || cloneAlerts.Count > 0 || journalCloneAlerts.Count > 0 || forbiddenSealed.Count > 0;
        // owner decision 2026-10-16: never CONFORM while anything happened that the controller has to look at
        bool toVerify = gaps.Count > 0 || lostInPeriod.Count > 0 || resumed.Count > 0 || devices.Count == 0 || unsealed.Count > 0 || untrusted || unreadable > 0
                        || resets.Count > 0 || volumeAlerts.Count > 0 || pendingSeals > 0;
        var status = tamper ? ControlStatus.Neconform
            : policy ? (isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat)
            : !auditUsable || coverage is { AnyCoverage: false } ? ControlStatus.Nedeterminat
            : toVerify ? ControlStatus.DeVerificat
            : ControlStatus.Conform;

        var evidence = new List<string>();
        foreach (var m in mismatches)
            evidence.Add("NECONFORM: numărul de înregistrare din jurnal nu corespunde jurnalului sigilat — " + (m.Device is { } md ? DeviceMonitorReport.Line(md) : "dispozitiv " + m.Identity + " (rândul lipsește din jurnalul monitorului)") +
                         $" — în jurnal „{(m.InJournal.Length > 0 ? m.InJournal : "(gol)")}”, în jurnalul sigilat {(m.InAudit is null ? "nu există nicio înregistrare pentru acest dispozitiv" : $"„{(m.InAudit.Length > 0 ? m.InAudit : "(gol)")}”")}");
        foreach (var d in deviceFindings) evidence.Add("NECONFORM: " + d + " — jurnalul monitorului nu corespunde jurnalului sigilat");
        foreach (var d in runDisagreements) evidence.Add("NECONFORM: " + d);
        foreach (var x in resets)
            evidence.Add($"JURNAL RESETAT {DeviceMonitorReport.UtcText(x.TimeUtc)}: jurnalul monitorului era deteriorat și a fost pus deoparte ({x.File}, SHA-256 {x.Sha256}); rândurile de dinainte nu se mai compară");
        if (unreadable > 0) evidence.Add($"ÎNREGISTRĂRI SIGILATE NECITIBILE: {unreadable} înregistrări ale monitorului din jurnalul sigilat nu au putut fi citite");
        evidence.AddRange(volumeAlerts);
        if (untrusted) evidence.Add("JURNALUL MONITORULUI NU ESTE DE ÎNCREDERE: " + f.DeviceJournalUntrusted);
        evidence.AddRange(forbiddenSealed.Select(x => "ÎNCALCĂ REGULA (jurnal sigilat): " + x));
        if (pendingSeals > 0)
            evidence.Add($"ÎNREGISTRĂRI ÎN AȘTEPTARE: {pendingSeals} înregistrări ale monitorului nu au putut fi trecute încă în jurnalul sigilat (se reîncearcă); până atunci jurnalul monitorului nu este dovedit");
        evidence.AddRange(s.Offending.Select(e => "ÎNCALCĂ REGULA: " + DeviceMonitorReport.Line(e)));
        foreach (var a in sealedAlerts)
            evidence.Add($"ALERTĂ CRITICĂ {DeviceMonitorReport.UtcText(a.TimeUtc)}: {a.Device} — {a.Text} (jurnal sigilat)" +
                         (DeviceMonitorAuditCheck.HasDevice(journal, a) ? "" : " — dispozitivul nu are rând în jurnalul monitorului"));
        evidence.AddRange(s.Alerts.Where(a => a.Severity == DeviceAlertSeverity.Critical && (!auditUsable || unsealed.Contains(a))).Select(a =>
            DeviceMonitorReport.AlertLine(a) + (auditUsable ? " (apare doar în jurnalul monitorului, nu și în cel sigilat)" : "")));
        if (coverage is { AnyCoverage: false })
            evidence.Add($"NICIO ACOPERIRE: nicio rulare sigilată a monitorului nu acoperă vreo parte din perioadă ({UtcMinutes(from)} – {UtcMinutes(to)})");
        else
            foreach (var g in gaps) evidence.Add($"INTERVAL NEACOPERIT: monitorul nu a rulat între {UtcMinutes(g.FromUtc)} și {UtcMinutes(g.ToUtc)}");
        if (auditUsable)
            foreach (var g in audit!.Gaps.Where(g => g.ToUtc >= from && g.FromUtc <= to))
                evidence.Add($"INTERVAL PIERDUT (înregistrat de monitor): {UtcMinutes(g.FromUtc)} – {UtcMinutes(g.ToUtc)} — {g.Reason}");
        evidence.AddRange(resumed.Select(o => $"observat la reluare {DeviceMonitorReport.UtcText(o.TimeUtc)}: {o.Device} ({o.Change})"));
        evidence.AddRange(blind.Select(x => x.Text + (x.BlindNow ? "" : $" (văzut așa la {DeviceMonitorReport.UtcText(x.LastBlindUtc ?? to)})")));
        evidence.AddRange(history);
        evidence.AddRange(devices.Except(s.Offending).Select(DeviceMonitorReport.Line));

        int connected = devices.Count(d => d.Present);
        int storage = devices.Count(d => DeviceMonitorRules.IsStorageMedium(d.Class));
        int noNumber = devices.Count(d => DeviceMonitorRules.IsStorageMedium(d.Class) && d.RegistrationNumber.Trim().Length == 0);
        int forbidden = devices.Count(d => DeviceMonitorReport.ForbiddenSeen(d, isolated, from));
        int duplicate = devices.Count(d => d.DuplicateSerial);
        var parts = new List<string>();
        parts.Add(devices.Count == 0
            ? "Monitorul nu a văzut niciun mediu de stocare sau dispozitiv conectat extern în perioada controlată; absența observațiilor nu dovedește că nu s-a conectat nimic"
            : $"{Ro.Capitalize(Ro.CountMasc(devices.Count, "dispozitiv văzut", "dispozitive văzute"))} în perioadă ({connected} conectate acum), dintre care {Ro.CountMasc(storage, "mediu de stocare", "medii de stocare")}" +
              (noNumber > 0 ? $", {noNumber} {DeviceMonitorReport.NoRegistration}" : "") + (forbidden > 0 ? $"; {forbidden} dintr-o clasă interzisă pe o stație izolată" : "") +
              (duplicate > 0 ? $"; {duplicate} cu serie duplicată (posibilă clonă)" : "") +
              $"; {Ro.CountFem(s.Alerts.Count, "alertă", "alerte")} în perioadă ({s.Critical} critice)");
        var detail = parts[0] + ".";
        if (blind.Count > 0) detail += " " + string.Join(". ", blind.Select(x => x.Text)) + " (absența dispozitivelor de acest fel nu înseamnă că nu s-au conectat).";
        if (deviceFindings.Count > 0)
            detail += $" Rândurile jurnalului monitorului nu corespund înregistrărilor sigilate ale dispozitivelor în {Ro.CountMasc(deviceFindings.Count, "punct", "puncte")} (rând șters, adăugat, cu tip sau date schimbate, sau o alertă sigilată fără rând în perioadă): posibilă alterare a fișierului.";
        if (resets.Count > 0) detail += " Jurnalul monitorului a fost resetat în perioadă (fișier deteriorat, păstrat deoparte; resetarea este sigilată).";
        if (lostInPeriod.Count > 0 || resumed.Count > 0) detail += " Monitorul a pierdut evenimente în perioadă (intervale pierdute sau dispozitive observate la reluare, listate în dovezi).";
        if (volumeAlerts.Count > 0) detail += " Un dispozitiv a revenit cu altă serie de volum (reformatat sau clonă).";
        if (unreadable > 0) detail += $" {unreadable} înregistrări sigilate ale monitorului nu au putut fi citite.";
        if (mismatches.Count > 0)
            detail += $" Numărul de înregistrare din jurnal nu corespunde jurnalului sigilat pentru {Ro.CountMasc(mismatches.Count, "dispozitiv", "dispozitive")} (jurnalul monitorului este un fișier simplu; jurnalul sigilat este înregistrarea care contează).";
        if (runDisagreements.Count > 0)
            detail += $" Rulările monitorului din jurnal nu corespund celor din jurnalul sigilat ({runDisagreements.Count}): acoperirea se socotește numai din jurnalul sigilat, iar nepotrivirea este o posibilă alterare.";
        if (forbiddenSealed.Count > 0)
            detail += $" Jurnalul sigilat arată {Ro.CountMasc(forbiddenSealed.Count, "dispozitiv dintr-o clasă interzisă", "dispozitive dintr-o clasă interzisă")} pe o stație izolată, activ în perioadă, oricum arată rândul din jurnalul monitorului.";
        if (pendingSeals > 0)
            detail += $" {pendingSeals} înregistrări ale monitorului așteaptă să fie trecute în jurnalul sigilat (verificarea nu poate fi Conform).";
        if (untrusted) detail += " " + f.DeviceJournalUntrusted;
        if (cloneAlerts.Count + journalCloneAlerts.Count > 0)
            detail += " S-a dat alerta de clonă („" + DeviceMonitorRules.CloneAlertText + "” sau „" + DeviceMonitorRules.DuplicateSerialAlertText + "”): aceeași serie și același VID/PID au apărut pe dispozitive diferite.";
        if (unsealed.Count > 0)
            detail += $" {Ro.Capitalize(Ro.CountFem(unsealed.Count, "alertă critică din jurnalul monitorului nu apare", "alerte critice din jurnalul monitorului nu apar"))} în jurnalul sigilat (verificarea nu poate fi Conform).";
        if (!auditUsable)
            detail += audit is null
                ? " Jurnalul sigilat de audit nu a putut fi citit, deci numerele de înregistrare, alertele și acoperirea nu pot fi verificate."
                : " Jurnalul sigilat de audit nu se verifică (lanț rupt sau sigiliu nevalid), deci numerele de înregistrare, alertele și acoperirea nu pot fi verificate.";
        else if (coverage is { AnyCoverage: false })
            detail += " Nicio rulare a monitorului, înregistrată în jurnalul sigilat, nu acoperă vreo parte din perioadă, deci nu se poate spune nimic despre ea (jurnalul simplu al monitorului nu este luat ca dovadă de acoperire).";
        else if (gaps.Count > 0)
        {
            detail += $" Acoperire incompletă (din rulările sigilate): {string.Join("; ", gaps.Take(5).Select(g => $"monitorul nu a rulat între {UtcMinutes(g.FromUtc)} și {UtcMinutes(g.ToUtc)}"))}" +
                      (gaps.Count > 5 ? $" și încă {gaps.Count - 5}" : "") + ".";
            detail += history.Count > 0
                ? $" În aceste intervale istoricul Windows arată {Ro.CountFem(history.Count, "conectare de dispozitiv de stocare", "conectări de dispozitive de stocare")} (listate în dovezi), pe care monitorul nu le-a văzut."
                : " În aceste intervale istoricul Windows (Partition/Diagnostic și lista dispozitivelor cunoscute) nu arată niciun dispozitiv de stocare; absența observațiilor nu dovedește că nu s-a conectat nimic.";
        }
        Add(r, "D05", "Dispozitive", title, status, detail + limit, evidence, rec);
    }

    private static string UtcMinutes(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";

    /// <summary>
    /// The storage devices Windows' own history shows in the intervals the monitor did not run: the same sources as D01 and D04 (the Partition/Diagnostic log and
    /// the removable media the station knows), not read again.
    /// </summary>
    private static IEnumerable<string> WindowsStorageHistoryInGaps(StationFacts f, IReadOnlyList<CoverageGap> gaps)
    {
        bool In(DateTimeOffset t) => gaps.Any(g => t >= g.FromUtc && t <= g.ToUtc);
        foreach (var l in PartitionConnections(f).Where(l => In(l.TimeUtc)))
            yield return $"Windows (Partition/Diagnostic), cât monitorul nu rula — {DeviceMonitorReport.UtcText(l.TimeUtc)}: {Clean(V(l, "Manufacturer"))} {Clean(V(l, "Model"))} SN {Clean(V(l, "SerialNumber"))} ({FormatBytes(V(l, "Capacity"))}) · {Ev(l)}";
        foreach (var m in f.ObservedMedia ?? [])
            foreach (var t in m.Seen.Where(In).OrderBy(t => t))
                yield return $"Windows (istoricul dispozitivelor), cât monitorul nu rula — {DeviceMonitorReport.UtcText(t)}: {m.Description} SN {(m.Serial.Length > 0 ? m.Serial : "(fără serie)")}";
    }
}
