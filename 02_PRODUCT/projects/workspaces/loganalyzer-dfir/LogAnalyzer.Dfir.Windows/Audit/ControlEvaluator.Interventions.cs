using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// S08 (planner #9 item 7; HG 585 art. 277 and 329 alin. 2; ORNISS model Anexa 14; matrix rows 20, 23, 29): the interventions on the station's hardware or software
    /// of the controlled period are in the register, each with a witness other than the performer, and hardware / repair interventions were followed by the check of the
    /// security devices. No register (or an unreadable or empty one) is NEDETERMINAT; a missing or identical witness is NECONFORM; a missing post-intervention check is
    /// NECONFORM after <see cref="InterventionRegister.PostCheckGraceDays"/> days and DE VERIFICAT inside them. A period with no recorded intervention is CONFORM (the register is in
    /// use, the sentence says that absence of rows does not prove absence of interventions), except that Windows Installer operations seen in the logs without a row of the same
    /// day (+/- 1) make it DE VERIFICAT.
    /// </summary>
    private static void InterventionRegisterCheck(StationFacts f, ControlReport r)
    {
        const string id = "S08", area = "Software", title = "Intervențiile hardware și software sunt în registru, cu martor";
        const string rec = "Treceți fiecare intervenție în Registrul intervențiilor (pagina Control stație), cu executant și martor diferiți; după o reparație sau o schimbare de hardware verificați dispozitivele de securitate și treceți data și persoana.";
        const string limit = " Absența înregistrărilor nu dovedește absența intervențiilor.";
        if (f.InterventionRegister is not { } rows)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat,
                (f.InterventionRegisterProblem.Length > 0 ? $"Registrul intervențiilor nu a putut fi citit: {f.InterventionRegisterProblem}" : "Registrul intervențiilor nu este folosit pe această stație (nu există fișierul registrului).") + " Fără registru nu se poate verifica martorul și verificarea după intervenție.", rec: rec);
            return;
        }
        if (rows.Count == 0)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Registrul intervențiilor este gol (registru nedefinit): nu se poate verifica martorul și verificarea după intervenție." + limit, rec: rec);
            return;
        }
        var start = DateOnly.FromDateTime(f.PeriodStartUtc.UtcDateTime);
        var end = DateOnly.FromDateTime(f.PeriodEndUtc.UtcDateTime);
        var today = DateOnly.FromDateTime(f.CollectedUtc.UtcDateTime);
        var bad = new List<string>(); var check = new List<string>(); var ok = new List<string>();
        var inPeriodDates = new List<DateOnly>();
        int inPeriod = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var label = $"rândul {i + 1} ({(row.Date.Length > 0 ? row.Date : "fără dată")}, {(row.Components.Length > 0 ? row.Components : row.Description.Length > 0 ? row.Description : "fără descriere")})";
            if (!RegisterRulesDate(row.Date, out var date)) { check.Add($"DE VERIFICAT: {label}: data lipsește sau nu este yyyy-MM-dd; intervenția nu poate fi încadrată în perioadă"); continue; }
            if (date < start || date > end) continue;
            inPeriod++; inPeriodDates.Add(date);
            bool rowBad = false;
            if (string.IsNullOrWhiteSpace(row.Performer)) { bad.Add($"NECONFORM: {label}: executantul lipsește"); rowBad = true; }
            if (string.IsNullOrWhiteSpace(row.Witness)) { bad.Add($"NECONFORM: {label}: fără martor (orice modificare se face de cel puțin 2 persoane)"); rowBad = true; }
            else if (InterventionRegister.SamePerson(row.Performer, row.Witness)) { bad.Add($"NECONFORM: {label}: martorul este aceeași persoană cu executantul ({row.Performer})"); rowBad = true; }
            var kind = InterventionRegister.NormalizeKind(row.Kind);
            if (kind is null) { check.Add($"DE VERIFICAT: {label}: felul „{row.Kind}” nu este recunoscut (hardware | software | configurare | reparatie)"); rowBad = true; }
            else if (InterventionRegister.NeedsPostCheck(kind) && !InterventionRegister.HasPostCheck(row))
            {
                int age = today.DayNumber - date.DayNumber;
                if (age > InterventionRegister.PostCheckGraceDays) { bad.Add($"NECONFORM: {label}: fără verificare după intervenție, la {age} zile după intervenție (termen {InterventionRegister.PostCheckGraceDays} zile)"); rowBad = true; }
                else { check.Add($"DE VERIFICAT: {label}: verificarea după intervenție nu este încă înregistrată ({age} zile din {InterventionRegister.PostCheckGraceDays})"); rowBad = true; }
            }
            if (!rowBad) ok.Add($"{label}: martor {row.Witness}" + (InterventionRegister.HasPostCheck(row) ? $", verificare după intervenție {row.PostCheckDate} ({row.PostCheckBy})" : ""));
        }
        // Windows Installer operations (the S01 events) with no intervention row on the same day (+/- 1: the logs are UTC, the register is local).
        // Main-session review 2026-11-19: a day covered by the offline updates register is already documented there (no second register row demanded).
        var updateDates = (f.OfflineUpdateRegister ?? [])
            .Select(u => DateOnly.TryParse(u.Date.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (DateOnly?)null)
            .OfType<DateOnly>().ToList();
        bool Near(IEnumerable<DateOnly> days, DateOnly day) => days.Any(d => Math.Abs(day.DayNumber - d.DayNumber) <= 1);
        var uncovered = Logs(f, "Application", 11707, 11724, 1033, 1034)
            .Where(l => { var day = DateOnly.FromDateTime(l.TimeUtc.UtcDateTime); return !Near(inPeriodDates, day) && !Near(updateDates, day); })
            .Select(l => $"DE VERIFICAT: operație Windows Installer fără intervenție în registru: {Ev(l)}: {string.Join(" ", l.Data.Values.Take(2))}").ToList();
        check.AddRange(uncovered);
        var counts = $"{inPeriod} {(inPeriod == 1 ? "intervenție în perioadă" : "intervenții în perioadă")}, {bad.Count} neconforme, {check.Count} de verificat";
        var evidence = bad.Concat(check).Concat(ok);
        if (bad.Count > 0)
            Add(r, id, area, title, ControlStatus.Neconform, $"{counts}. O intervenție nu are martor diferit de executant sau nu are verificarea după intervenție în termen." + limit, evidence, rec);
        else if (check.Count > 0)
            Add(r, id, area, title, ControlStatus.DeVerificat, $"{counts}." + (uncovered.Count > 0 ? " Aplicația a văzut în jurnale operații de instalare sau dezinstalare fără o intervenție în registru în aceeași zi." : "") + limit, evidence, rec);
        else if (inPeriod == 0)
            Add(r, id, area, title, ControlStatus.Conform, "Nicio intervenție înregistrată în perioadă; absența înregistrărilor nu dovedește absența intervențiilor.", evidence, rec);
        else
            Add(r, id, area, title, ControlStatus.Conform, $"{counts}: toate au martor diferit de executant și, unde se cere, verificarea după intervenție." + limit, evidence, rec);
    }

    private static bool RegisterRulesDate(string s, out DateOnly d) =>
        DateOnly.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d);
}
