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

    /// <summary>"Ediție nepotrivită" (see <see cref="EditionFit"/>), or empty. Printed as a header line; it never changes a check result.</summary>
    public string EditionWarning { get; init; } = "";

    /// <summary>
    /// Review of PR #290: the installation date the application computed and the time of the first record of its audit chain, printed on every control
    /// report so the officer can compare them between controls (a later date on a later report means the history was reset). Empty = not printed.
    /// </summary>
    public string AppInstallDate { get; init; } = "";
    public string AppChainFirst { get; init; } = "";

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
            ("Număr de înregistrare", V(RegistrationNumber)),
        };
        var sn = LogAnalyzer.Dfir.Registers.StationName.Parse(Station);
        if (sn.Kind == LogAnalyzer.Dfir.Registers.StationNameKind.Standalone)
        {
            l.Add(("Cod unitate", sn.UnitCode)); l.Add(("Compartiment", sn.Compartment));
            l.Add(("Număr de înregistrare stație", sn.RegistrationNumber)); l.Add(("Clasificare stație", sn.Classification));
        }
        else if (sn.Kind == LogAnalyzer.Dfir.Registers.StationNameKind.Networked)
        {
            l.Add(("Rețea", sn.Network)); l.Add(("Număr stație", sn.Number));
        }
        foreach (var f in ExtraFields) if (!string.IsNullOrWhiteSpace(f.Label)) l.Add((f.Label.Trim(), V(f.Value)));
        if (!string.IsNullOrWhiteSpace(EditionWarning)) l.Add(("Avertisment ediție", EditionWarning));
        if (AppInstallDate.Length > 0) l.Add(("Data instalării aplicației (calculată)", AppInstallDate));
        if (AppChainFirst.Length > 0) l.Add(("Primul rând al jurnalului de audit al aplicației", AppChainFirst));
        return l;
    }

    public static ControlReportHeader From(ReportHeaderProfile profile, StationFacts facts, string hardwareId, string inspector, string inspectorFunction,
        string stationRole, string registrationNumber, string marking)
    {
        string L(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm");
        return new ControlReportHeader(profile.Unit, profile.Structure, facts.Host, hardwareId, $"{L(facts.PeriodStartUtc)} – {L(facts.PeriodEndUtc)} (ora locală)",
            inspector, string.IsNullOrWhiteSpace(inspectorFunction) ? profile.InspectorFunction : inspectorFunction, stationRole, registrationNumber, marking,
            profile.SignatureLeft, profile.SignatureRight, profile.ExtraFields)
        {
            // an unread egress source: no guess, no warning
            EditionWarning = EditionFit.Warning(facts.Posture, facts.EgressUnread.Count > 0 ? null : facts.EgressPaths) ?? "",
            AppInstallDate = facts.AppInstalledUtc is { } ai ? $"{L(ai)} (ora locală); {facts.AppInstallBasis}" : facts.AppInstallBasis.Length > 0 ? "necunoscută: " + facts.AppInstallBasis : "",
            AppChainFirst = facts.AppInstallChainFirstUtc is { } cf ? $"{L(cf)} (ora locală)" : "",
        };
    }
}
