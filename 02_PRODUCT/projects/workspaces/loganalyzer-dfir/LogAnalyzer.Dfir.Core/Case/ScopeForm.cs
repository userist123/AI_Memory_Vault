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

    /// <summary>
    /// The form for an existing scope. A provisional scope's placeholder values (approver "necunoscut (de confirmat)", the one-year period,
    /// the most restrictive categories) are NOT carried over as if they were answers: the operator has to enter them.
    /// </summary>
    public static ScopeForm FromScope(CaseScope? s)
    {
        if (s is null) return new ScopeForm();
        var f = new ScopeForm { Purpose = s.Purpose, Systems = string.Join("; ", s.SystemsInScope) };
        if (s.Provisional) return f;
        f.Approver = s.Approver; f.Notes = s.Notes;
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
            Provisional = false,
        };
        missing = scope.MissingFields();
        return missing.Count == 0;
    }
}
