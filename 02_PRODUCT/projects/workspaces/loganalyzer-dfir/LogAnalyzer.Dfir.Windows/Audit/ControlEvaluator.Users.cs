using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using Ro = LogAnalyzer.Dfir.Language.RoText;

namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// U07 (planner #10 item 4; HG 585 art. 35, 274(2), 278; matrix rows 2 and 31). Compared per ENABLED local person account (disabled accounts, RIDs 501-504, system names and machine accounts are
    /// left out; an enabled built-in Administrator, RID 500, is compared): the rows that name the account (matched as Wp14 matches accounts) must hold a clearance valid at the control date.
    /// No row = NECONFORM; rows but no clearance valid at the date (all expired or not yet valid) = NECONFORM; rows with no clearance at all (for example rows taken over from the station) = DE VERIFICAT.
    /// A renewal is a new row: expired history rows stay (Wp14 needs them for past use) and do not matter while another row is valid; expired rows of disabled or missing accounts are only listed.
    /// Training and pledge are checked for each such person and for every person of the register whose clearance is valid now (a domain station has no local person accounts): none or older than
    /// <see cref="ProcedureProfile.TrainingMaxAgeDays"/> (default 365; exactly the limit is inside), or no pledge, or a date that cannot be read or lies in the future = DE VERIFICAT.
    /// No register (or an unreadable or empty one) is NEDETERMINAT. When the local accounts could not be read the account part is NEDETERMINAT with the reason of the gap (no NECONFORM from clearances alone).
    /// Without any local person account to compare the result is at best DE VERIFICAT, never CONFORM.
    /// </summary>
    private static void UsersRegisterCheck(StationFacts f, ControlReport r)
    {
        const string id = "U07", area = "Utilizatori", title = "Conturile locale active sunt în Registrul utilizatori, cu abilitare în termen, instruire și angajament";
        const string rec = "Treceți în Registrul utilizatori (pagina Control stație) fiecare persoană care are un cont activ pe stație, cu abilitarea ei, data ultimei instruiri și data angajamentului de confidențialitate semnat. Rândurile vechi rămân în registru (sunt necesare pentru folosirile din trecut); la reînnoirea unei abilitări se adaugă un rând nou. Dezactivați conturile persoanelor care nu mai lucrează pe stație.";
        const string limit = " Registrul arată ce a fost scris în el; aplicația nu poate verifica dacă instruirea sau angajamentul au avut loc.";
        if (f.UsersRegister is not { } reg)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat,
                (f.UsersRegisterProblem.Length > 0 ? $"Registrul utilizatori nu a putut fi citit: {f.UsersRegisterProblem}" : "Registrul utilizatori nu este folosit pe această stație (nu există fișierul registrului).") + " Fără registru nu se pot compara conturile, abilitările, instruirea și angajamentul.", rec: rec);
            return;
        }
        if (reg.Rows.Count == 0)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Registrul utilizatori este gol (registru nedefinit): nu se pot compara conturile, abilitările, instruirea și angajamentul." + limit, rec: rec);
            return;
        }

        int maxTraining = ProcedureProfile.EffectiveTrainingMaxAgeDays(f.TrainingMaxAgeDays);
        var today = DateOnly.FromDateTime(f.CollectedUtc.UtcDateTime);
        var latestAllowed = today.AddDays(UsersRegister.FutureToleranceDays);
        var evidence = new List<string>();
        var nonConform = new List<string>();
        var toVerify = new List<string>();

        // clearance state of one row at the control date: valid / expired / future (not yet valid) / unreadable date / none
        (string State, string Why) Clearance(UserRow row)
        {
            if (row.Clearance.Length == 0) return ("none", "");
            DateOnly from = default, to = default;
            bool okFrom = row.ClearanceValidFrom.Length == 0 || RegisterRulesDate(row.ClearanceValidFrom, out from);
            bool okTo = row.ClearanceValidTo.Length == 0 || RegisterRulesDate(row.ClearanceValidTo, out to);
            if (!okFrom || !okTo) return ("unreadable", $"{row.Clearance}: data de început sau de sfârșit a abilitării nu este yyyy-MM-dd");
            if (row.ClearanceValidTo.Length > 0 && to < today) return ("expired", $"{row.Clearance} expirată la {to:yyyy-MM-dd}");
            if (row.ClearanceValidFrom.Length > 0 && from > today) return ("future", $"{row.Clearance} valabilă abia de la {from:yyyy-MM-dd}");
            return ("valid", "");
        }

        // newest readable date of a cell over rows; unreadable and future dates are reported and never count
        DateOnly? Newest(IEnumerable<UserRow> rows, Func<UserRow, string> cell, string what, List<string> problems)
        {
            DateOnly? newest = null;
            foreach (var v in rows.Select(cell).Where(v => v.Length > 0))
            {
                if (!RegisterRulesDate(v, out var d)) problems.Add($"data {what} „{v}” nu este yyyy-MM-dd");
                else if (d > latestAllowed) problems.Add($"data {what} „{v}” este în viitor");
                else if (newest is null || d > newest) newest = d;
            }
            return newest;
        }

        // training and pledge of the rows of one person: the line of evidence and the problems
        void TrainingAndPledge(string who, string person, List<UserRow> rows)
        {
            var problems = new List<string>();
            var training = Newest(rows, x => x.LastTraining, "instruirii", problems);
            var pledge = Newest(rows, x => x.PledgeSigned, "angajamentului", problems);
            string trainingText = "instruire necompletată", pledgeText = "angajament necompletat";
            if (training is { } t)
            {
                int age = Math.Max(0, today.DayNumber - t.DayNumber);
                trainingText = $"instruire {t:yyyy-MM-dd} ({age} zile, termen {maxTraining})";
                if (age > maxTraining) problems.Add($"ultima instruire este de {age} zile; termenul din profil este {maxTraining} zile");
            }
            else problems.Add("nu este trecută nicio dată de instruire care să poată fi numărată");
            if (pledge is { } p) pledgeText = $"angajament {p:yyyy-MM-dd}";
            else problems.Add("nu este trecută nicio dată a angajamentului de confidențialitate care să poată fi numărată");
            evidence.Add($"{(problems.Count > 0 ? "DE VERIFICAT" : "în regulă")}: {who}, persoana {(person.Length > 0 ? person : "necompletată")}: {trainingText}, {pledgeText}");
            foreach (var pr in problems) toVerify.Add($"DE VERIFICAT: {who}: {pr}");
        }

        var gap = f.Gaps.FirstOrDefault(g => g.Artifact == "Conturi locale");
        bool accountsKnown = f.Accounts.Count > 0 && gap is null;
        var matched = new HashSet<UserRow>();
        int people = 0;
        if (accountsKnown)
        {
            var list = f.Accounts.Where(a => !a.Disabled && !IsBuiltInOrSystemAccount(a)).ToList();
            people = list.Count;
            foreach (var a in list)
            {
                var rows = reg.ForAccount(a.Name, a.Sid).ToList();
                var who = $"cont {a.Name}{(a.Sid.Length > 0 ? $" ({a.Sid})" : "")}";
                if (rows.Count == 0) { nonConform.Add($"NECONFORM: {who}: activ pe stație, fără niciun rând în Registrul utilizatori"); continue; }
                foreach (var x in rows) matched.Add(x);
                var states = rows.Select(Clearance).ToList();
                var person = string.Join(", ", rows.Select(x => x.Person).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
                if (states.Any(s => s.State == "valid")) { /* at least one valid clearance: in order; expired rows are history */ }
                else if (states.All(s => s.State == "none")) toVerify.Add($"DE VERIFICAT: {who}: fără abilitare înregistrată în registru (rândurile nu au nivel de abilitare)");
                else if (states.Any(s => s.State == "unreadable")) toVerify.Add($"DE VERIFICAT: {who}: {string.Join("; ", states.Where(s => s.State == "unreadable").Select(s => s.Why))}");
                else nonConform.Add($"NECONFORM: {who}: nicio abilitare valabilă la {today:yyyy-MM-dd} ({string.Join("; ", states.Where(s => s.Why.Length > 0).Select(s => s.Why))})");
                TrainingAndPledge(who, person, rows);
            }
            if (people == 0) evidence.Add("Niciun cont local activ de persoană de comparat cu registrul (conturile dezactivate, cele încorporate Guest și ale sistemului și conturile de mașină nu se numără).");
        }

        // the people of the register whose clearance is valid now and who are not covered above (domain accounts, accounts not read): training and pledge
        string Key(UserRow x) => x.Person.Length > 0 ? x.Person.ToLowerInvariant() : "\0" + reg.Rows.IndexOf(x);
        var others = reg.Rows.Where(x => !matched.Contains(x) && Clearance(x).State == "valid").Select(Key).Distinct().ToList();
        foreach (var key in others)
        {
            var rows = reg.Rows.Where(x => Key(x) == key).ToList();
            foreach (var x in rows) matched.Add(x);
            TrainingAndPledge($"persoana {rows[0].Person}", rows[0].Person, rows);
        }
        // expired clearances of rows that no checked person holds: history, listed only
        for (int i = 0; i < reg.Rows.Count; i++)
        {
            var row = reg.Rows[i];
            if (matched.Contains(row) || Clearance(row).State != "expired") continue;
            evidence.Add($"istoric (fără efect asupra rezultatului): rândul {i + 1} ({row.Person}), abilitarea {Clearance(row).Why}");
        }

        var unread = gap is not null
            ? $"Lista conturilor locale este incompletă ({gap.Reason}), deci conturile active nu se pot compara cu registrul."
            : "Lista conturilor locale nu a putut fi citită, deci conturile active nu se pot compara cu registrul.";
        var shown = evidence.Concat(nonConform).Concat(toVerify).ToList();
        if (!accountsKnown) shown.Insert(0, unread);
        const string noLocal = "Stația nu are niciun cont local de persoană de comparat cu registrul (stație de domeniu sau doar conturi încorporate); s-au verificat instruirea și angajamentul persoanelor din registru cu abilitare valabilă.";
        if (nonConform.Count > 0)
            Add(r, id, area, title, ControlStatus.Neconform,
                $"{Ro.Capitalize(Ro.Count(nonConform.Count, "neconformitate", "neconformități"))} între conturile active și Registrul utilizatori: cont activ fără rând sau fără abilitare valabilă." + limit, shown, rec);
        else if (!accountsKnown)
            Add(r, id, area, title, ControlStatus.Nedeterminat, unread + limit, shown, rec);
        else if (people == 0)
            Add(r, id, area, title, ControlStatus.DeVerificat, noLocal + (toVerify.Count > 0 ? " Unele date lipsesc, sunt prea vechi sau nu se pot citi." : "") + limit, shown, rec);
        else if (toVerify.Count > 0)
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"Conturile active sunt în registru, dar abilitarea, instruirea (termen {maxTraining} zile) sau angajamentul lipsesc ori sunt prea vechi, sau o dată nu se poate citi." + limit, shown, rec);
        else
            Add(r, id, area, title, ControlStatus.Conform,
                $"Fiecare cont local activ este în registru cu o abilitare valabilă, instruirea este în ultimele {maxTraining} de zile și angajamentul este semnat." + limit, shown, rec);
    }

    /// <summary>Accounts that are not a person's: the built-in Guest, krbtgt, DefaultAccount and WDAGUtilityAccount (RIDs 501-504), the system names the logon checks skip and machine accounts. An enabled built-in Administrator (RID 500) is compared.</summary>
    private static bool IsBuiltInOrSystemAccount(LocalAccount a)
    {
        var rid = a.Sid.LastIndexOf('-') is var i and >= 0 && int.TryParse(a.Sid[(i + 1)..], out var n) ? n : -1;
        return rid is >= 501 and <= 504 || SystemAccounts.Contains(a.Name) || a.Name.EndsWith('$');
    }
}
