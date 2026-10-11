using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The header of the "Raport pentru proces-verbal" (WP18 S5, decision D3): unit, station (name and Hardware ID), period, who controlled and in what
/// function, the station role, a free registration number, the marking level entered by hand (decision D4) and the signature lines.
/// Nothing here is evidence; it is what the organisation writes on the report.
/// </summary>
public sealed record ControlReportHeader(
    string Unit, string Structure, string Station, string HardwareId, string Period, string Inspector, string InspectorFunction, string StationRole,
    string RegistrationNumber, string Marking, string SignatureLeft, string SignatureRight, IReadOnlyList<ReportHeaderField> ExtraFields)
{
    public const string MarkingNote = "marcaj introdus manual, neverificat de aplicație";
    public const string NoMarking = "fără marcaj introdus";

    /// <summary>The marking line printed on every page.</summary>
    public string MarkingLine => string.IsNullOrWhiteSpace(Marking) ? $"Marcaj: {NoMarking} ({MarkingNote})" : $"Marcaj: {Marking.Trim()} ({MarkingNote})";

    /// <summary>Label: value lines of the header block, in print order; empty values are printed as "—" so the form stays complete.</summary>
    public IReadOnlyList<(string Label, string Value)> Lines()
    {
        string V(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();
        var l = new List<(string, string)>
        {
            ("Unitatea", V(Unit)), ("Structura", V(Structure)), ("Stația", V(Station)), ("Identificatorul stației (Hardware ID)", V(HardwareId)),
            ("Perioada controlată", V(Period)), ("Cine a efectuat controlul", V(Inspector) + (string.IsNullOrWhiteSpace(InspectorFunction) ? "" : $", {InspectorFunction.Trim()}")),
            ("Rolul stației", V(StationRole)), ("Număr de înregistrare", V(RegistrationNumber)),
        };
        foreach (var f in ExtraFields) if (!string.IsNullOrWhiteSpace(f.Label)) l.Add((f.Label.Trim(), V(f.Value)));
        return l;
    }

    public static ControlReportHeader From(ReportHeaderProfile profile, StationFacts facts, string hardwareId, string inspector, string inspectorFunction,
        string stationRole, string registrationNumber, string marking)
    {
        string L(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm");
        return new ControlReportHeader(profile.Unit, profile.Structure, facts.Host, hardwareId, $"{L(facts.PeriodStartUtc)} – {L(facts.PeriodEndUtc)} (ora locală)",
            inspector, string.IsNullOrWhiteSpace(inspectorFunction) ? profile.InspectorFunction : inspectorFunction, stationRole, registrationNumber, marking,
            profile.SignatureLeft, profile.SignatureRight, profile.ExtraFields);
    }
}
