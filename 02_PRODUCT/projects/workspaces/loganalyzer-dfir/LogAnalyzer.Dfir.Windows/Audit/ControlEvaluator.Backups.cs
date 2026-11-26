using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// S09 (planner #10 item 3; ORNISS model, Anexa 21; matrix row 30): backup copies are made regularly and a restore from them was tested. Same area as S08 ("Software"), the area
    /// the codebase uses for the registers kept next to the station. No register (or an unreadable or empty one) is NEDETERMINAT; the newest copy older than the profile's
    /// <see cref="ProcedureProfile.BackupMaxAgeDays"/> (default 30) at the control date is NECONFORM (exactly the limit is still inside); no restore test ever, or the newest one older than
    /// <see cref="ProcedureProfile.BackupRestoreTestMaxAgeDays"/> (default 180), is DE VERIFICAT; rows whose date cannot be read or lies in the future are DE VERIFICAT and never counted as the newest copy.
    /// </summary>
    private static void BackupRegisterCheck(StationFacts f, ControlReport r)
    {
        const string id = "S09", area = "Software", title = "Copiile de siguranță sunt făcute la timp și restaurarea a fost testată";
        const string rec = "Treceți fiecare copie de siguranță în Registrul copiilor de siguranță (pagina Control stație): data, ce s-a copiat, suportul, cine a făcut-o și unde se păstrează. Încercați din când în când restaurarea dintr-o copie și treceți data și persoana în coloanele testului de restaurare.";
        const string limit = " Registrul arată ce a fost scris în el; aplicația nu citește copiile în sine.";
        if (f.BackupRegister is not { } rows)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat,
                (f.BackupRegisterProblem.Length > 0 ? $"Registrul copiilor de siguranță nu a putut fi citit: {f.BackupRegisterProblem}" : "Registrul copiilor de siguranță nu este folosit pe această stație (nu există fișierul registrului).") + " Fără registru nu se poate verifica vechimea ultimei copii și testul de restaurare.", rec: rec);
            return;
        }
        if (rows.Count == 0)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Registrul copiilor de siguranță este gol (registru nedefinit): nu se poate verifica vechimea ultimei copii și testul de restaurare." + limit, rec: rec);
            return;
        }
        int maxAge = ProcedureProfile.EffectiveBackupMaxAgeDays(f.BackupMaxAgeDays);
        int maxTestAge = ProcedureProfile.EffectiveBackupRestoreTestMaxAgeDays(f.BackupRestoreTestMaxAgeDays);
        var today = DateOnly.FromDateTime(f.CollectedUtc.UtcDateTime);
        var latestAllowed = today.AddDays(BackupRegister.FutureToleranceDays);
        var check = new List<string>();
        DateOnly? newestCopy = null, newestTest = null;
        BackupRow? newestRow = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var label = $"rândul {i + 1} ({(row.Date.Length > 0 ? row.Date : "fără dată")}, {(row.What.Length > 0 ? row.What : "fără descriere")})";
            if (!RegisterRulesDate(row.Date, out var date)) check.Add($"DE VERIFICAT: {label}: data copiei lipsește sau nu este yyyy-MM-dd; rândul nu se numără");
            else if (date > latestAllowed) check.Add($"DE VERIFICAT: {label}: data copiei este în viitor; rândul nu se numără");
            else if (newestCopy is null || date > newestCopy) { newestCopy = date; newestRow = row; }
            if (row.RestoreTestDate.Length > 0)
            {
                if (!RegisterRulesDate(row.RestoreTestDate, out var test)) check.Add($"DE VERIFICAT: {label}: data testului de restaurare nu este yyyy-MM-dd; testul nu se numără");
                else if (test > latestAllowed) check.Add($"DE VERIFICAT: {label}: data testului de restaurare este în viitor; testul nu se numără");
                else if (newestTest is null || test > newestTest) newestTest = test;
            }
        }
        var evidence = new List<string>();
        if (newestCopy is { } nc)
        {
            int age = Math.Max(0, today.DayNumber - nc.DayNumber);
            var by = newestRow!;
            evidence.Add($"Ultima copie: {nc:yyyy-MM-dd}, {age} zile (termen {maxAge} zile): {(by.What.Length > 0 ? by.What : "fără descriere")}, suport {(by.Medium.Length > 0 ? by.Medium : "necompletat")}{(by.MediumNumber.Length > 0 ? $" ({by.MediumNumber})" : "")}, făcută de {(by.MadeBy.Length > 0 ? by.MadeBy : "necompletat")}, păstrată la {(by.StoredAt.Length > 0 ? by.StoredAt : "necompletat")}");
            if (age > maxAge)
            {
                Add(r, id, area, title, ControlStatus.Neconform,
                    $"Ultima copie de siguranță este din {nc:yyyy-MM-dd}, adică de {age} zile; termenul din profil este {maxAge} zile." + limit, evidence.Concat(check), rec);
                return;
            }
        }
        if (newestTest is { } nt)
        {
            int testAge = Math.Max(0, today.DayNumber - nt.DayNumber);
            evidence.Add($"Ultimul test de restaurare: {nt:yyyy-MM-dd}, {testAge} zile (termen {maxTestAge} zile)");
            if (testAge > maxTestAge) check.Insert(0, $"DE VERIFICAT: ultimul test de restaurare este de {testAge} zile; termenul din profil este {maxTestAge} zile");
        }
        else
        {
            evidence.Add("Ultimul test de restaurare: niciunul înregistrat");
            check.Insert(0, "DE VERIFICAT: nu este înregistrat niciun test de restaurare");
        }
        if (newestCopy is null)
        {
            Add(r, id, area, title, ControlStatus.DeVerificat, "Niciun rând din registru nu are o dată de copie care să poată fi citită; vechimea ultimei copii nu se poate stabili." + limit, evidence.Concat(check), rec);
            return;
        }
        if (check.Count > 0)
            Add(r, id, area, title, ControlStatus.DeVerificat, $"Ultima copie este în termen ({maxAge} zile), dar testul de restaurare lipsește sau este prea vechi, ori rânduri ale registrului nu se pot număra." + limit, evidence.Concat(check), rec);
        else
            Add(r, id, area, title, ControlStatus.Conform, $"Ultima copie este în termen (termen {maxAge} zile) și restaurarea a fost testată în ultimele {maxTestAge} zile." + limit, evidence, rec);
    }
}
