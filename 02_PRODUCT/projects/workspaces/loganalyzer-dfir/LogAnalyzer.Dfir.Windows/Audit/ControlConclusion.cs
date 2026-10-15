using Ro = LogAnalyzer.Dfir.Language.RoText;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The "Concluzie pe scurt" box on page 1 of the control report, like the investigation report's (review 2026-10-12): the verdict in plain
/// words, the non-compliant checks grouped by area, what it means for the reader, the next steps taken from the recommendations of the
/// non-compliant checks (media and transfers first, then remote access, encryption, accounts) and what could not be checked.
/// Pure function of the report, tested without producing a PDF.
/// </summary>
public sealed record ControlConclusion(string Verdict, IReadOnlyList<string> Serious, string Meaning, IReadOnlyList<string> NextSteps, IReadOnlyList<string> NotChecked)
{
    public const int MaxSeriousLines = 5;
    public const int MaxNextSteps = 3;
    public const int MaxNotCheckedLines = 8;

    /// <summary>0 media and transfers, 1 remote access, 2 encryption, 3 accounts, 4 the rest: the order of the next steps.</summary>
    public static int Priority(ControlCheck c)
    {
        string t = c.Title;
        bool Has(params string[] words) => words.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (c.Area == "Dispozitive" || c.Id is "P28" or "P29" || Has("suport", "USB", "CD/DVD", "tipări")) return 0;
        if (c.Id is "P06" or "N08" or "N09" or "N10" or "C06" or "U03" || Has("distanță", "Remote Desktop", "WinRM", "portproxy", "Partajarea conexiunii")) return 1;
        if (Has("cripta", "BitLocker", "TPM")) return 2;
        if (c.Area is "Conturi" or "Utilizatori" || c.Id is "P01" or "P02" or "P03" or "P05" || Has("parol", "cont")) return 3;
        return 4;
    }

    public static ControlConclusion Build(ControlReport r)
    {
        var f = r.Facts;
        var bad = r.Checks.Select((c, i) => (c, i)).Where(x => x.c.Status == ControlStatus.Neconform)
                           .OrderBy(x => Priority(x.c)).ThenBy(x => x.i).Select(x => x.c).ToList();
        int nc = bad.Count, dv = r.Count(ControlStatus.DeVerificat), nd = r.Count(ControlStatus.Nedeterminat);

        string others = (dv > 0 ? $" {Ro.Capitalize(Ro.CountFem(dv, "altă verificare cere", "alte verificări cer"))} confirmarea organizației (DE VERIFICAT)." : "")
                      + (nd > 0 ? $" {Ro.Capitalize(Ro.CountFem(nd, "verificare nu a putut fi făcută", "verificări nu au putut fi făcute"))} (NEDETERMINAT)." : "");
        string verdict = nc > 0
            ? $"{Ro.Capitalize(Ro.CountFem(nc, "verificare este NECONFORMĂ", "verificări sunt NECONFORME"))}: stația nu respectă toate regulile verificate." + others
            : r.Applicable == 0 ? "Nu există verificări evaluate."   // NU SE APLICĂ rows are not evaluated checks
            : nd == r.Applicable ? $"{Ro.Capitalize(Ro.CountFem(nd, "verificare aplicabilă nu a putut fi făcută", "verificări aplicabile nu au putut fi făcute"))} (NEDETERMINAT): nu se poate spune nici că stația respectă regulile, nici că nu le respectă."
            : "Nicio verificare neconformă în sursele citite." + others;

        // the non-compliant checks grouped by area, the most important area first, at most five lines
        var groups = bad.GroupBy(c => c.Area).ToList();
        var serious = groups.Take(MaxSeriousLines).Select(g => $"{g.Key}: {string.Join("; ", g.Select(c => c.Title))}").ToList();
        if (groups.Count > MaxSeriousLines)
        {
            int rest = groups.Skip(MaxSeriousLines - 1).Sum(g => g.Count());
            serious[MaxSeriousLines - 1] = $"încă {Ro.CountFem(rest, "verificare neconformă", "verificări neconforme")} în alte domenii (vezi secțiunea 1)";
        }

        string meaning = nc > 0
            ? "Până la remediere, problemele de mai sus lasă deschise căi prin care datele pot ieși de pe stație sau prin care cineva poate ajunge la ea fără aprobare. "
              + "Raportul nu spune că s-a produs un incident; spune că regulile de protecție nu sunt toate aplicate, iar fiecare neconformitate trebuie remediată sau justificată în scris."
            : nd > 0
                ? "Nu s-a găsit nimic neconform, dar o parte din verificări nu au putut fi făcute, deci rezultatul nu dovedește că stația respectă toate regulile."
                : "Nu s-a găsit nimic neconform în sursele citite. Faptele DE VERIFICAT trebuie totuși confirmate de organizație înainte de a încheia controlul.";

        var steps = bad.Select(c => c.Recommendation.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxNextSteps).ToList();
        if (steps.Count < MaxNextSteps && nc > 0) steps.Add("Deschideți fiecare verificare NECONFORMĂ și citiți dovezile ei înainte de a o trece în procesul-verbal.");
        if (steps.Count < MaxNextSteps && !f.IsAdministrator) steps.Add("Reporniți aplicația ca administrator și repetați controlul, ca jurnalul de securitate și setările protejate să poată fi citite.");
        if (steps.Count < MaxNextSteps && dv > 0) steps.Add("Confirmați cu organizația faptele DE VERIFICAT (cine le-a autorizat, pe ce bază).");
        if (steps.Count < MaxNextSteps) steps.Add("Salvați raportul pentru procesul-verbal: PDF și JSON rămân în caz, cu amprentă de integritate.");

        // Elevation is a positive fact (the report header already shows it): it is a limitation only when missing.
        var notChecked = new List<string>();
        if (!f.IsAdministrator)
        {
            // only the checks left undetermined BECAUSE of missing rights (review 2026-10-15), not every undetermined check
            var needAdmin = r.Checks.Where(UndeterminedForLackOfRights).Select(c => c.Id).ToList();
            notChecked.Add("Controlul a rulat FĂRĂ drepturi de administrator: jurnalul de securitate și o parte din setări nu au putut fi citite"
                + (needAdmin.Count > 0 ? $"; verificări rămase nedeterminate: {string.Join(", ", needAdmin)}." : "."));
        }
        var unreadable = f.Coverage.Where(c => !c.Readable).Select(c => c.Channel).ToList();
        if (unreadable.Count > 0) notChecked.Add($"Surse necitite: {string.Join(", ", unreadable)}.");
        var undetermined = r.Checks.Where(c => c.Status == ControlStatus.Nedeterminat).ToList();
        notChecked.AddRange(undetermined.Take(MaxNotCheckedLines).Select(c => $"{c.Id} {c.Title}: {c.Detail}"));
        if (undetermined.Count > MaxNotCheckedLines)
            notChecked.Add($"încă {Ro.CountFem(undetermined.Count - MaxNotCheckedLines, "verificare nedeterminată", "verificări nedeterminate")} (vezi secțiunea 1).");
        if (undetermined.Count == 0 && unreadable.Count == 0) notChecked.Add("Toate verificările automate au putut fi făcute.");

        return new ControlConclusion(verdict, serious, meaning, steps.Take(MaxNextSteps).ToList(), notChecked);
    }

    // What the evaluator and the collector write when a source could not be read for lack of rights: the evaluator's "requires administrator"
    // texts and the access-denied messages of Windows (English and Romanian), which become the detail of the log-coverage checks.
    private static readonly string[] RightsMarkers =
        ["administrator", "acces refuzat", "accesul este interzis", "access is denied", "unauthorized operation", "operațiune neautorizată"];

    /// <summary>A check that is NEDETERMINAT because a source could not be read without administrator rights (not cancelled, absent or unsupported).</summary>
    public static bool UndeterminedForLackOfRights(ControlCheck c) =>
        c.Status == ControlStatus.Nedeterminat && RightsMarkers.Any(m => c.Detail.Contains(m, StringComparison.OrdinalIgnoreCase));
}
