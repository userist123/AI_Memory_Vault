using System.Text.Json;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// The unit's report header (WP18 S5, owner decision D3): the fields the organisation puts on a control report ("proces-verbal"), configured by the
/// administrator and kept next to the procedure profile. Data only; the application never fills them in on the organisation's behalf.
/// </summary>
public sealed class ReportHeaderProfile
{
    public const string FileName = "report_header.json";

    /// <summary>Unit / institution.</summary>
    public string Unit { get; set; } = "";
    /// <summary>Structure / department.</summary>
    public string Structure { get; set; } = "";
    /// <summary>Default function of the person who controls (e.g. "ofițer de securitate").</summary>
    public string InspectorFunction { get; set; } = "";
    /// <summary>Label of the left signature line (the one who controls).</summary>
    public string SignatureLeft { get; set; } = "Cel care a efectuat controlul";
    /// <summary>Label of the right signature line (the one controlled).</summary>
    public string SignatureRight { get; set; } = "Responsabilul stației controlate";
    /// <summary>Extra lines the organisation wants on every report (label: value).</summary>
    public List<ReportHeaderField> ExtraFields { get; set; } = [];

    /// <summary>%PROGRAMDATA%\LogAnalyzer\profile\report_header.json (same folder as the procedure profile, administrators write).</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "profile", FileName);

    /// <summary>A missing file is an empty header (nothing invented); an unreadable one is reported through <paramref name="problem"/> and also gives an empty header.</summary>
    public static ReportHeaderProfile Load(string? path, out string? problem)
    {
        problem = null;
        var p = path ?? DefaultPath;
        try
        {
            if (!File.Exists(p)) return new ReportHeaderProfile();
            return JsonSerializer.Deserialize<ReportHeaderProfile>(File.ReadAllText(p)) ?? new ReportHeaderProfile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { problem = ex.Message; return new ReportHeaderProfile(); }
    }

    public static void Save(ReportHeaderProfile h, string? path)
    {
        var p = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, JsonSerializer.Serialize(h, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class ReportHeaderField { public string Label { get; set; } = ""; public string Value { get; set; } = ""; }

/// <summary>"Pentru cine este raportul?" (UX contract §15): the audience chooses the sections, never the facts.</summary>
public enum ReportAudience { Management, ItSecurity, Forensic, IncidentResponse, Audit, Everything }

/// <summary>Sections of the investigation report, by number as the PDF prints them.</summary>
public enum ReportSection { IncidentChains = 1, Findings = 2, EvidenceCollected = 3, Gaps = 4, Detections = 5, AntiForensics = 6, PolicyTimeline = 7, Integrity = 8 }

public static class ReportAudiences
{
    public static IReadOnlyList<ReportAudience> All { get; } = Enum.GetValues<ReportAudience>();

    public static string Label(ReportAudience a) => a switch
    {
        ReportAudience.Management => "Pentru conducere",
        ReportAudience.ItSecurity => "Pentru IT / securitate",
        ReportAudience.Forensic => "Pentru investigație criminalistică",
        ReportAudience.IncidentResponse => "Pentru răspuns la incident",
        ReportAudience.Audit => "Pentru audit / conformitate",
        _ => "Tot (raport complet)",
    };

    public static string Description(ReportAudience a) => a switch
    {
        ReportAudience.Management => "Ce s-a întâmplat, cât de grav, ce lipsește și ce urmează; fără detalii tehnice.",
        ReportAudience.ItSecurity => "Lanțul incidentului, constatările cu probe, detecțiile și golurile; fără integritatea probă cu probă.",
        ReportAudience.Forensic => "Totul, cu accent pe probe: colectare, parsare, integritate, goluri, anti-forensics.",
        ReportAudience.IncidentResponse => "Lanțul incidentului, constatările, detecțiile și golurile, ca să se decidă următorii pași.",
        ReportAudience.Audit => "Probele colectate, golurile, cronologia politicilor și integritatea; constatările pe scurt.",
        _ => "Toate secțiunile raportului.",
    };

    /// <summary>Which sections the audience gets. The title, the integrity banner, the verification line and the summary boxes are always printed.</summary>
    public static IReadOnlySet<ReportSection> Sections(ReportAudience a) => a switch
    {
        ReportAudience.Management => new HashSet<ReportSection> { ReportSection.IncidentChains, ReportSection.Gaps },
        ReportAudience.ItSecurity => new HashSet<ReportSection> { ReportSection.IncidentChains, ReportSection.Findings, ReportSection.Detections, ReportSection.Gaps, ReportSection.AntiForensics },
        ReportAudience.IncidentResponse => new HashSet<ReportSection> { ReportSection.IncidentChains, ReportSection.Findings, ReportSection.Detections, ReportSection.Gaps },
        ReportAudience.Audit => new HashSet<ReportSection> { ReportSection.IncidentChains, ReportSection.EvidenceCollected, ReportSection.Gaps, ReportSection.PolicyTimeline, ReportSection.Integrity },
        _ => new HashSet<ReportSection>(Enum.GetValues<ReportSection>()),
    };

    /// <summary>Full (Forensic / Everything) reports print every finding; the others keep the findings short.</summary>
    public static bool FindingsInFull(ReportAudience a) => a is ReportAudience.Forensic or ReportAudience.Everything or ReportAudience.ItSecurity or ReportAudience.IncidentResponse;
}
