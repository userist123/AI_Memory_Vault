using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// The fields of the case-scope dialog (owner decisions 23 and 28) and their conversion to a <see cref="CaseScope"/>. UI-free so it is tested
/// without WPF. Nothing is pre-filled on the operator's behalf except what the case already holds; indexes are -1 until the operator picks.
/// </summary>
public sealed class ScopeForm
{
    public static readonly string[] LegalBases = ["Incident", "Audit", "Control"];
    public static readonly string[] NetworkCategories = ["Rețea air-gapped", "PC standalone", "Conectat"];
    public static readonly string[] ClassificationLevels = ["Clasificat", "Neclasificat"];

    /// <summary>
    /// The scope as printed at the top of a report (evaluation 2026-11-05: the investigation PDF had no purpose, approver, legal basis,
    /// period, category or authorising act). Every mandatory field is printed, an empty one as "nespecificat"; the first line says whether
    /// the scope is confirmed. Text the operator typed is printed as typed (the PDF writer escapes it).
    /// </summary>
    /// <summary>
    /// Warning, never a block, when the scope period ends before <paramref name="todayUtc"/> (lab WS02 2026-11-17: a reused scope 01–09.11 for a
    /// collection on 17.11; the period-aware rules then skip everything after the 9th without a word). Null when the period is open or covers today.
    /// The period is the approver's decision, so it is never extended on the operator's behalf.
    /// </summary>
    public static string? PeriodEndsBeforeWarning(DateTime? periodTo, DateTime todayUtc)
    {
        if (periodTo is not { } to || to.Date >= todayUtc.Date) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"Perioada scopului se încheie la {to.ToString("dd.MM.yyyy", inv)}, înainte de ziua colectării ({todayUtc.ToString("dd.MM.yyyy", inv)}): " +
               "evenimentele de după această dată nu sunt luate în calcul de regulile care țin cont de perioadă. Extindeți perioada dacă ziua colectării trebuie inclusă.";
    }

    /// <summary><see cref="PeriodEndsBeforeWarning(DateTime?, DateTime)"/> for a saved scope (its end is the last tick of the day).</summary>
    public static string? PeriodEndsBeforeWarning(CaseScope? s, DateTimeOffset nowUtc) =>
        s?.PeriodToUtc is { } to ? PeriodEndsBeforeWarning(to.UtcDateTime.Date, nowUtc.UtcDateTime) : null;

    public static IReadOnlyList<string> ReportLines(CaseScope? s)
    {
        if (s is null) return ["Scopul cazului: lipsește (caz creat înaintea scopului obligatoriu); rezultatele nu au un scop confirmat."];
        static string Or(string v) => string.IsNullOrWhiteSpace(v) ? "nespecificat" : v.Trim();
        static string Day(DateTimeOffset? d) => d is { } x ? x.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "nespecificat";
        string Pick(string[] labels, int i) => i >= 0 && i < labels.Length ? labels[i] : "nespecificat";
        var f = FromScope(s);
        return
        [
            s.IsConfirmed ? "Scopul cazului: confirmat de operator." : "Scopul cazului: PROVIZORIU, NECONFIRMAT" + (s.MissingFields().Count > 0 ? " (lipsesc: " + string.Join(", ", s.MissingFields()) + ")" : "") + " — concluziile nu pot fi folosite ca bază a unei decizii.",
            "Scop: " + Or(s.Purpose),
            $"Perioada: {Day(s.PeriodFromUtc)} – {Day(s.PeriodToUtc)}",
            "Sisteme în scop: " + Or(string.Join(", ", s.SystemsInScope.Where(x => !string.IsNullOrWhiteSpace(x)))),
            "Aprobator: " + Or(s.Approver),
            "Temei: " + Pick(LegalBases, f.LegalBasisIndex) + " · Categoria sistemului: " + Pick(NetworkCategories, f.NetworkIndex) + " · " + Pick(ClassificationLevels, f.ClassificationIndex),
            "Actul care autorizează: " + Or(s.AuthorizingAct),
        ];
    }

    public string Purpose { get; set; } = "";
    public string Approver { get; set; } = "";
    /// <summary>Systems in scope separated by comma or semicolon.</summary>
    public string Systems { get; set; } = "";
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public int LegalBasisIndex { get; set; } = -1;
    public int NetworkIndex { get; set; } = -1;
    public int ClassificationIndex { get; set; } = -1;
    public string Notes { get; set; } = "";
    public string AuthorizingAct { get; set; } = "";
    /// <summary>Classification marking typed by the operator (empty = unmarked, decision 21). Not verified; printed on the reports.</summary>
    public string Marking { get; set; } = "";

    /// <summary>Why the typed marking is refused, or null when it is empty or acceptable. "Unclassified" words are not a marking (decision 21).</summary>
    public string? MarkingError() =>
        SecrecyLevels.IsUnclassifiedMarking(Marking) ? $"„{Marking.Trim()}” nu este un nivel de clasificare; lăsați câmpul gol = nemarcat" : null;

    /// <summary>
    /// The form for an existing scope. A provisional scope's placeholder values (approver "necunoscut (de confirmat)", the one-year period,
    /// the most restrictive categories) are NOT carried over as if they were answers: the operator has to enter them.
    /// </summary>
    public static ScopeForm FromScope(CaseScope? s)
    {
        if (s is null) return new ScopeForm();
        var f = new ScopeForm { Purpose = s.Purpose, Systems = string.Join("; ", s.SystemsInScope) };
        if (s.Provisional) return f;
        f.Approver = s.Approver; f.Notes = s.Notes; f.AuthorizingAct = s.AuthorizingAct; f.Marking = s.Marking;
        f.PeriodFrom = s.PeriodFromUtc?.UtcDateTime.Date; f.PeriodTo = s.PeriodToUtc?.UtcDateTime.Date;
        f.LegalBasisIndex = s.LegalBasis switch { LegalBasis.Incident => 0, LegalBasis.Audit => 1, LegalBasis.Control => 2, _ => -1 };
        f.NetworkIndex = s.Network switch { NetworkCategory.AirGappedNetwork => 0, NetworkCategory.StandalonePc => 1, NetworkCategory.Connected => 2, _ => -1 };
        f.ClassificationIndex = s.Classification switch { ClassificationLevel.Classified => 0, ClassificationLevel.Unclassified => 1, _ => -1 };
        return f;
    }

    /// <summary>The scope, never provisional. False with the names of the missing fields (the dialog stays open and lists them).</summary>
    public bool TryBuild(out CaseScope scope, out IReadOnlyList<string> missing)
    {
        scope = new CaseScope
        {
            Purpose = Purpose.Trim(),
            Approver = Approver.Trim(),
            SystemsInScope = Systems.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PeriodFromUtc = PeriodFrom is { } f ? new DateTimeOffset(DateTime.SpecifyKind(f.Date, DateTimeKind.Utc)) : null,
            PeriodToUtc = PeriodTo is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t.Date, DateTimeKind.Utc)).AddDays(1).AddTicks(-1) : null,
            LegalBasis = LegalBasisIndex switch { 0 => LegalBasis.Incident, 1 => LegalBasis.Audit, 2 => LegalBasis.Control, _ => LegalBasis.Unspecified },
            Network = NetworkIndex switch { 0 => NetworkCategory.AirGappedNetwork, 1 => NetworkCategory.StandalonePc, 2 => NetworkCategory.Connected, _ => NetworkCategory.Unspecified },
            Classification = ClassificationIndex switch { 0 => ClassificationLevel.Classified, 1 => ClassificationLevel.Unclassified, _ => ClassificationLevel.Unspecified },
            Notes = Notes.Trim(),
            AuthorizingAct = AuthorizingAct.Trim(),
            Marking = Marking.Trim(),
            Provisional = false,
        };
        var m = scope.MissingFields().ToList();
        if (MarkingError() is { } bad) m.Add(bad);
        missing = m;
        return m.Count == 0;
    }
}
