using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The rules that depend on the station's classification level (owner decision 2026-10-23; matrix rows 8 and 26). The level is profile data: without it
/// these checks are NEDETERMINAT, never CONFORM, and a level is never inferred from the station name or the edition.
/// </summary>
public static partial class ControlEvaluator
{
    public const string LevelUndefinedDetail = "Nivelul de clasificare al stației nu este definit în profilul de proceduri, deci regula care depinde de nivel nu se poate aplica. " +
                                              "Completați „Nivelul stației” în Profilul de proceduri.";

    private static void LevelRules(StationFacts f, ControlReport r)
    {
        var level = StationLevelRules.Resolve(f.StationLevel);
        var rules = level is null ? null : StationLevelRules.For(level);
        AccessLogRetention(f, r, rules);
        InternetByLevel(f, r, rules);
    }

    /// <summary>
    /// A18: the access records must be kept for the period the level asks (HG 585 art. 291). The live Security log rarely holds years, so a log that starts later
    /// is DE VERIFICAT (the older records must exist in the exported archives), not NECONFORM: the application cannot see the archives.
    /// </summary>
    private static void AccessLogRetention(StationFacts f, ControlReport r, StationLevelRules? rules)
    {
        const string id = "A18", area = "Audit", title = "Retenția registrelor de acces cerută de nivelul stației";
        const string rec = "Păstrați jurnalele Security exportate (arhivate) pe toată durata cerută de nivel și consemnați exporturile; la control, prezentați arhivele care acoperă intervalul.";
        if (rules is null) { Add(r, id, area, title, ControlStatus.Nedeterminat, LevelUndefinedDetail, rec: rec); return; }
        var sec = f.Coverage.FirstOrDefault(c => c.Channel == "Security");
        string lvl = $"Nivelul stației: {rules.Level.Name}. Cerința: {rules.RetentionBasis}.";
        if (sec is null || !sec.Readable || sec.OldestUtc is not { } oldest)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, $"{lvl} Jurnalul Security nu a putut fi citit, deci nu se știe de când păstrează stația înregistrările de acces.", rec: rec);
            return;
        }
        if (rules.AccessLogRetentionYears is not { } years)
        {
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"{lvl} Jurnalul Security de pe stație începe la {oldest:yyyy-MM-dd} (UTC). Comparați cu durata fixată în documentația de acreditare și verificați arhivele exportate pentru perioada mai veche.",
                [$"Security, cel mai vechi eveniment {oldest:yyyy-MM-dd HH:mm} UTC"], rec);
            return;
        }
        var required = f.CollectedUtc.AddYears(-years);
        // the rank only compares levels across schemes: for a NATO / EU station the national rule is an equivalence, not its legal basis, so it is never CONFORM
        if (rules.Level.Scheme != LogAnalyzer.Dfir.Model.SecrecyScheme.National)
        {
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"{lvl} Nivelul {rules.Level.Name} nu este un nivel național: s-a aplicat regula nivelului național echivalent (tabelul de echivalență HG 585), care nu este neapărat baza legală a stației. " +
                $"Jurnalul Security de pe stație începe la {oldest:yyyy-MM-dd} (UTC). Confirmați durata cerută din documentația de acreditare.",
                [$"Security, cel mai vechi eveniment {oldest:yyyy-MM-dd HH:mm} UTC"], rec);
            return;
        }
        if (oldest <= required)
            Add(r, id, area, title, ControlStatus.Conform,
                $"{lvl} Jurnalul Security de pe stație începe la {oldest:yyyy-MM-dd} (UTC), deci acoperă singur ultimii {years} ani (de la {required:yyyy-MM-dd}).",
                [$"Security, cel mai vechi eveniment {oldest:yyyy-MM-dd HH:mm} UTC"], rec);
        else
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"{lvl} Jurnalul Security de pe stație începe la {oldest:yyyy-MM-dd} (UTC); înregistrările de acces din {required:yyyy-MM-dd} până la {oldest:yyyy-MM-dd} trebuie să existe în arhivele exportate. " +
                "Aplicația nu vede arhivele: confirmați-le cu registrul exporturilor (întrebarea manuală M06).",
                [$"Security, cel mai vechi eveniment {oldest:yyyy-MM-dd HH:mm} UTC", $"început cerut: {required:yyyy-MM-dd}"], rec);
    }

    /// <summary>N14 (Internet edition only): a station of a level where the internet is forbidden (SSID) must not run the Internet edition.</summary>
    private static void InternetByLevel(StationFacts f, ControlReport r, StationLevelRules? rules)
    {
        const string id = "N14", area = "Rețea", title = "Conectarea la internet este permisă la nivelul stației";
        const string rec = "La strict secret de importanță deosebită stația nu se conectează la internet: folosiți ediția Standalone sau Rețea izolată. La celelalte niveluri, interconectarea se face doar cu aprobarea agenției de acreditare.";
        if (f.Posture != StationPosture.Internet) { Add(r, id, area, title, ControlStatus.Conform, "Ediția stației nu are acces la internet.", rec: rec); return; }
        if (rules is null) { Add(r, id, area, title, ControlStatus.Nedeterminat, LevelUndefinedDetail, rec: rec); return; }
        if (rules.InternetForbidden)
            Add(r, id, area, title, ControlStatus.Neconform,
                $"Stația are nivelul {rules.Level.Name} și rulează ediția cu acces la internet; la acest nivel conectarea la internet este interzisă (matricea HG 585, rândul 26).", rec: rec);
        else
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"Stația are nivelul {rules.Level.Name} și rulează ediția cu acces la internet: conectarea este permisă doar dacă agenția de acreditare a aprobat interconectarea (INFOSEC 2 art. 37–39). Verificați aprobarea.", rec: rec);
    }
}
