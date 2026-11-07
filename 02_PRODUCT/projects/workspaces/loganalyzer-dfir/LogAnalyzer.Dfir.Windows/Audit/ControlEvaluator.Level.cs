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
        // research 2026-11-07 plan P1.3: the archives Windows left on the station (Archive-Security-*.evtx) extend the live log when they join it without a gap
        // review 2026-11-08: an archive written by another computer (its events name a different host) never extends this station's records
        var foreign = f.SecurityArchives.Where(a => a.Computer.Length > 0 && f.Host.Length > 0 && !SameHost(a.Computer, f.Host)).ToList();
        var chain = ContinuousCoverage(oldest, f.SecurityArchives.Except(foreign));
        var archEv = f.SecurityArchives.Select(a => a.Error.Length > 0 ? $"{a.File}: necitit ({a.Error})" : $"{a.File}: {a.OldestUtc:yyyy-MM-dd} – {a.NewestUtc:yyyy-MM-dd} UTC").ToList();
        var unreadable = f.SecurityArchives.Count(a => a.Error.Length > 0 || a.OldestUtc is null || a.NewestUtc is null);
        string archText = f.SecurityArchives.Count == 0 ? "Pe stație nu există arhive Archive-Security-*.evtx."
            : $"Pe stație există {LogAnalyzer.Dfir.Language.RoText.CountFem(f.SecurityArchives.Count, "arhivă", "arhive")} a jurnalului Security" +
              (chain.Used > 0 ? $", dintre care {chain.Used} se leagă fără întrerupere de jurnalul curent, deci înregistrările încep la {chain.Start:yyyy-MM-dd}" : ", dar niciuna nu se leagă fără întrerupere de jurnalul curent") +
              (unreadable > 0 ? $"; {unreadable} nu au putut fi citite" : "") +
              (foreign.Count > 0 ? $"; {foreign.Count} sunt scrise de alt calculator ({string.Join(", ", foreign.Select(a => a.Computer).Distinct(StringComparer.OrdinalIgnoreCase))}) și nu se iau în seamă" : "") +
              (chain.Used > 0 ? "; arhivele de pe stație sunt luate așa cum sunt, integritatea lor nu este verificată aici" : "") +
              (chain.Gap.Length > 0 ? $"; lipsesc înregistrări {chain.Gap} (poate fi și o perioadă în care stația a fost oprită)" : "") + ".";
        oldest = chain.Start;
        // the rank only compares levels across schemes: for a NATO / EU station the national rule is an equivalence, not its legal basis, so it is never CONFORM
        if (rules.Level.Scheme != LogAnalyzer.Dfir.Model.SecrecyScheme.National)
        {
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"{lvl} Nivelul {rules.Level.Name} nu este un nivel național: s-a aplicat regula nivelului național echivalent (tabelul de echivalență HG 585), care nu este neapărat baza legală a stației. " +
                $"Înregistrările de acces de pe stație (jurnalul curent și arhivele legate de el) încep la {oldest:yyyy-MM-dd} (UTC). {archText} Confirmați durata cerută din documentația de acreditare.",
                [$"Security (jurnal curent și arhive legate), primul eveniment {oldest:yyyy-MM-dd HH:mm} UTC", .. archEv], rec);
            return;
        }
        IEnumerable<string> ev = [$"Security (jurnal curent și arhive legate), primul eveniment {oldest:yyyy-MM-dd HH:mm} UTC", .. archEv];
        if (oldest <= required)
            Add(r, id, area, title, ControlStatus.Conform,
                $"{lvl} {(chain.Used == 0 ? "Jurnalul Security de pe stație" : "Jurnalul Security de pe stație, împreună cu arhivele lui,")} începe la {oldest:yyyy-MM-dd} (UTC), deci acoperă ultimii {years} ani (de la {required:yyyy-MM-dd}). {archText}",
                ev, rec);
        else
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"{lvl} Înregistrările de acces de pe stație încep la {oldest:yyyy-MM-dd} (UTC); cele din {required:yyyy-MM-dd} până la {oldest:yyyy-MM-dd} trebuie să existe în arhivele exportate. {archText} " +
                "Arhivele mutate pe alte suporturi nu se văd de aici: confirmați-le cu registrul exporturilor (întrebarea manuală M06).",
                [.. ev, $"început cerut: {required:yyyy-MM-dd}"], rec);
    }

    /// <summary>„LAB-WS02” and „LAB-WS02.lab.local” are the same computer.</summary>
    private static bool SameHost(string a, string b)
    {
        static string Short(string h) => (h.Contains('.') ? h[..h.IndexOf('.')] : h).Trim();
        return Short(a).Equals(Short(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The earliest time from which the access records are continuous up to now: the live log's first event, moved back through every archive that reaches
    /// it within <paramref name="tolerance"/>; the first break is reported as a gap. An archive that is unreadable or empty never extends the coverage. Pure.
    /// </summary>
    public static (DateTimeOffset Start, int Used, string Gap) ContinuousCoverage(DateTimeOffset liveOldest, IEnumerable<SecurityArchiveFact> archives, TimeSpan? tolerance = null)
    {
        var tol = tolerance ?? TimeSpan.FromDays(1);
        var usable = archives.Where(a => a.Error.Length == 0 && a.OldestUtc is not null && a.NewestUtc is not null && a.OldestUtc <= a.NewestUtc).ToList();
        var start = liveOldest; int used = 0;
        while (usable.Where(a => a.NewestUtc >= start - tol && a.OldestUtc < start).MinBy(a => a.OldestUtc) is { } next)
        {
            start = next.OldestUtc!.Value;
            used++;
            usable.Remove(next);
        }
        var before = usable.Where(a => a.NewestUtc < start - tol).MaxBy(a => a.NewestUtc);
        return (start, used, before is null ? "" : $"între {before.NewestUtc:yyyy-MM-dd} și {start:yyyy-MM-dd}");
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
