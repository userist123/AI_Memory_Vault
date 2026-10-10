using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>The period choices of the guided control (WP18 S4), in the user's words.</summary>
public enum ControlPeriodChoice { SinceLastControl, Days30, Days90, Custom }

public static class ControlPeriods
{
    public const int DefaultDays = 90, MaxDays = 3650;

    public static string Label(ControlPeriodChoice c) => c switch
    {
        ControlPeriodChoice.SinceLastControl => "De la ultimul control",
        ControlPeriodChoice.Days30 => "Ultimele 30 de zile",
        ControlPeriodChoice.Days90 => "Ultimele 90 de zile",
        _ => "Interval ales de mine",
    };

    /// <summary>
    /// Days back for a choice. "Since the last control" uses the previous control's collection time (plus one day of overlap so nothing at the
    /// boundary is missed); without a previous control it falls back to 90 days and says so in <paramref name="note"/>.
    /// </summary>
    public static int Days(ControlPeriodChoice choice, PreviousControl? previous, int customDays, DateTimeOffset nowUtc, out string note)
    {
        note = "";
        switch (choice)
        {
            case ControlPeriodChoice.Days30: return 30;
            case ControlPeriodChoice.Days90: return 90;
            case ControlPeriodChoice.Custom:
                if (customDays is < 1 or > MaxDays) { note = $"Intervalul trebuie să fie între 1 și {MaxDays} zile."; return 0; }
                return customDays;
            default:
                if (previous is null) { note = "Nu există un control anterior pe această stație; se verifică ultimele 90 de zile."; return DefaultDays; }
                var days = (int)Math.Ceiling((nowUtc - previous.CollectedUtc).TotalDays) + 1;
                if (days > MaxDays) { note = $"Ultimul control este mai vechi de {MaxDays} zile; se verifică {MaxDays} zile."; return MaxDays; }
                note = $"De la controlul din {previous.CollectedUtc.ToLocalTime():yyyy-MM-dd HH:mm} (cu o zi de suprapunere).";
                return Math.Max(1, days);
        }
    }
}

/// <summary>A control saved earlier in the station's case (Control/CONTROL_&lt;date&gt;/control_report.json), as far as the checks are concerned.</summary>
public sealed record PreviousControl(string Folder, DateTimeOffset CollectedUtc, string Host, IReadOnlyList<ControlCheck> Checks)
{
    public string Label => $"{CollectedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {Checks.Count} verificări";
}

public static class ControlArchive
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    private sealed class ReportFile { public FactsPart? Facts { get; set; } public List<ControlCheck>? Checks { get; set; } }
    private sealed class FactsPart { public string Host { get; set; } = ""; public DateTimeOffset CollectedUtc { get; set; } }

    /// <summary>Previous controls of a case, newest first. Unreadable folders are skipped (and listed in <paramref name="problems"/>), never guessed.</summary>
    public static IReadOnlyList<PreviousControl> List(string caseRoot, out IReadOnlyList<string> problems)
    {
        var list = new List<PreviousControl>(); var bad = new List<string>();
        var dir = Path.Combine(caseRoot, "Control");
        if (Directory.Exists(dir))
            foreach (var folder in Directory.EnumerateDirectories(dir, "CONTROL_*"))
            {
                var json = Path.Combine(folder, "control_report.json");
                if (!File.Exists(json)) { bad.Add($"{Path.GetFileName(folder)}: fără control_report.json"); continue; }
                try
                {
                    var r = JsonSerializer.Deserialize<ReportFile>(File.ReadAllText(json), Json);
                    if (r?.Facts is null || r.Checks is null) { bad.Add($"{Path.GetFileName(folder)}: conținut incomplet"); continue; }
                    list.Add(new PreviousControl(folder, r.Facts.CollectedUtc, r.Facts.Host, r.Checks));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { bad.Add($"{Path.GetFileName(folder)}: {ex.Message}"); }
            }
        problems = bad;
        return list.OrderByDescending(p => p.CollectedUtc).ToList();
    }
}

/// <summary>One check compared between two controls.</summary>
public sealed record CheckChange(string Id, string Title, ControlStatus? Before, ControlStatus After, string Kind)
{
    public string BeforeText => Before is { } b ? ControlReportPdf.StatusText(b) : "—";
    public string AfterText => ControlReportPdf.StatusText(After);
}

/// <summary>"Compară cu controlul anterior": what got worse, what got better, what is new, what is unchanged. A difference, not two reports.</summary>
public sealed record ControlComparison(DateTimeOffset PreviousUtc, IReadOnlyList<CheckChange> Worse, IReadOnlyList<CheckChange> Better, IReadOnlyList<CheckChange> New,
    IReadOnlyList<CheckChange> Removed, int Unchanged)
{
    public string Summary =>
        $"Față de controlul din {PreviousUtc.ToLocalTime():yyyy-MM-dd}: {Worse.Count} înrăutățite, {Better.Count} îmbunătățite, {New.Count} noi, {Removed.Count} dispărute, {Unchanged} neschimbate.";

    private static int Rank(ControlStatus s) => s switch { ControlStatus.Neconform => 3, ControlStatus.DeVerificat => 2, ControlStatus.Nedeterminat => 1, _ => 0 };

    public static ControlComparison Compare(PreviousControl previous, IReadOnlyList<ControlCheck> current)
    {
        var before = previous.Checks.ToDictionary(c => c.Id, c => c);
        var worse = new List<CheckChange>(); var better = new List<CheckChange>(); var @new = new List<CheckChange>(); int same = 0;
        foreach (var c in current)
        {
            if (!before.TryGetValue(c.Id, out var b)) { @new.Add(new(c.Id, c.Title, null, c.Status, "nouă")); continue; }
            if (Rank(c.Status) > Rank(b.Status)) worse.Add(new(c.Id, c.Title, b.Status, c.Status, "înrăutățită"));
            else if (Rank(c.Status) < Rank(b.Status)) better.Add(new(c.Id, c.Title, b.Status, c.Status, "îmbunătățită"));
            else same++;
        }
        var ids = current.Select(c => c.Id).ToHashSet();
        var removed = previous.Checks.Where(c => !ids.Contains(c.Id)).Select(c => new CheckChange(c.Id, c.Title, c.Status, c.Status, "dispărută")).ToList();
        return new ControlComparison(previous.CollectedUtc, worse, better, @new, removed, same);
    }
}

/// <summary>The sections of the procedure profile, defined or "nedefinit": shown before the control, because an undefined section is never "conform".</summary>
public static class ProfileSummary
{
    public static IReadOnlyList<SectionStatus> Sections(ProcedureProfile? p) =>
        ProfileTables.All.Select(t => new SectionStatus(ProfileTables.Title(t), p is not null && ProfileTables.Cells(p, t).Count > 0, p is null ? 0 : ProfileTables.Cells(p, t).Count)).ToList();

    public static string Line(IReadOnlyList<SectionStatus> sections)
    {
        var undefined = sections.Where(s => !s.Defined).Select(s => s.Section).ToList();
        return undefined.Count == 0 ? "Profilul de proceduri este complet definit."
             : $"Secțiuni nedefinite în profilul de proceduri ({undefined.Count} din {sections.Count}): {string.Join(", ", undefined)}. Verificările care depind de ele nu pot da CONFORM.";
    }
}

/// <summary>
/// The single result screen of the guided control (WP18 S4): the five answers, computed from the report. There is no SAFE: with nothing
/// non-compliant it says "nimic neconform găsit în sursele citite" together with what was read and what was not.
/// </summary>
public sealed record ControlResultScreen(string Headline, string Problem, string Seriousness, string Trust, string Found, IReadOnlyList<string> NextSteps, string CoverageLine, bool NothingFound)
{
    public const int MaxNextSteps = 5;

    public static ControlResultScreen Build(ControlReport r, IReadOnlyList<SectionStatus> profile, ControlComparison? comparison = null)
    {
        var f = r.Facts;
        int nc = r.Count(ControlStatus.Neconform), dv = r.Count(ControlStatus.DeVerificat), nd = r.Count(ControlStatus.Nedeterminat), ok = r.Count(ControlStatus.Conform);
        var unreadable = f.Coverage.Where(c => !c.Readable).Select(c => c.Channel).ToList();
        var readable = f.Coverage.Count(c => c.Readable);
        var coverage = f.Coverage.Count == 0 ? "Acoperire: nicio sursă citită."
            : $"Surse citite: {readable} din {f.Coverage.Count}" + (unreadable.Count == 0 ? "." : $"; necitite: {string.Join(", ", unreadable)}.")
              + (f.Gaps.Count == 0 ? "" : $" Goluri de probă: {f.Gaps.Count}.");
        bool nothing = nc == 0 && dv == 0;

        string headline = nc > 0 ? $"ATENȚIE — {nc} verificări NECONFORME"
            : dv > 0 ? $"DE LĂMURIT — {dv} verificări cer confirmarea organizației"
            : nd == r.Checks.Count && r.Checks.Count > 0 ? "NEDETERMINAT — nicio verificare nu a putut fi făcută"
            : "NIMIC NECONFORM GĂSIT în sursele citite";
        string problem = nc > 0 ? $"Da. {nc} verificări sunt NECONFORME" + (dv > 0 ? $" și {dv} sunt DE VERIFICAT." : ".")
            : dv > 0 ? $"De lămurit. {dv} verificări arată fapte pe care numai organizația le poate confirma ca autorizate (DE VERIFICAT)."
            : $"Nimic neconform găsit în sursele citite. {coverage}";
        var areas = r.Checks.Where(c => c.Status == ControlStatus.Neconform).Select(c => c.Area).Distinct().ToList();
        string seriousness = nc > 0 ? $"Neconformitățile privesc: {string.Join(", ", areas)}." + (nd > 0 ? $" {nd} verificări sunt NEDETERMINATE (sursa nu a putut fi citită)." : "")
            : nd > 0 ? $"{nd} verificări NEDETERMINATE: sursa nu a putut fi citită, deci nu se poate spune nici că totul este în ordine."
            : ok > 0 ? $"{ok} verificări CONFORME, {dv} DE VERIFICAT." : "Nu există verificări evaluate.";
        string trust = !f.IsAdministrator
            ? "Probe parțiale: controlul a rulat FĂRĂ drepturi de administrator, deci jurnalul de securitate și o parte din setări nu au putut fi citite."
            : unreadable.Count > 0 || f.Gaps.Count > 0 ? $"Probe parțiale. {coverage}"
            : $"Probele sunt complete pentru acest control. {coverage}";
        var top = r.Checks.Where(c => c.Status == ControlStatus.Neconform).Take(3).Select(c => c.Title).ToList();
        string found = $"NECONFORM {nc} · DE VERIFICAT {dv} · NEDETERMINAT {nd} · CONFORM {ok}" + (top.Count > 0 ? $". De exemplu: {string.Join("; ", top)}." : ".")
            + (comparison is null ? "" : " " + comparison.Summary);

        var steps = new List<string>();
        if (!f.IsAdministrator) steps.Add("Reporniți aplicația ca administrator și rulați controlul din nou, ca jurnalul de securitate să poată fi citit.");
        if (nc > 0) steps.Add("Deschideți fiecare verificare NECONFORMĂ și citiți dovezile ei înainte de a o trece în procesul-verbal.");
        if (dv > 0) steps.Add("Confirmați cu organizația faptele DE VERIFICAT (cine a autorizat, pe ce bază).");
        var undefined = profile.Where(s => !s.Defined).Select(s => s.Section).ToList();
        if (undefined.Count > 0) steps.Add($"Completați profilul de proceduri ({string.Join(", ", undefined.Take(3))}{(undefined.Count > 3 ? "…" : "")}): fără el, verificările respective nu pot da CONFORM.");
        if (comparison is { Worse.Count: > 0 }) steps.Add($"Priviți întâi cele {comparison.Worse.Count} verificări înrăutățite față de controlul anterior.");
        steps.Add("Salvați raportul pentru procesul-verbal: PDF și JSON rămân în caz, cu amprentă de integritate.");
        return new ControlResultScreen(headline, problem, seriousness, trust, found, steps.Take(MaxNextSteps).ToList(), coverage, nothing);
    }
}
