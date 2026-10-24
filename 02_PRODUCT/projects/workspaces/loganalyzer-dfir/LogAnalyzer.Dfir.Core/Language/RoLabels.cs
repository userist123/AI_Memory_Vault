using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// Romanian reader-facing labels of the enumerations that reports print (review 2026-10-12, D4). The English spec codes stay in the data
/// and in a legend at the end of a report; the body of a report uses these. Feminine agreement with "constatare" for severity and state.
/// </summary>
public static class RoLabels
{
    public static string Severity(Severity s) => s switch
    {
        Model.Severity.Critical => "CRITICĂ", Model.Severity.High => "RIDICATĂ", Model.Severity.Medium => "MEDIE",
        Model.Severity.Low => "SCĂZUTĂ", _ => "INFORMATIVĂ",
    };

    /// <summary>"grad de încredere: mediu".</summary>
    public static string Confidence(Confidence c) => "grad de încredere: " + c switch { Model.Confidence.High => "ridicat", Model.Confidence.Medium => "mediu", _ => "scăzut" };

    public static string Source(EvidenceStatus s) => s switch
    {
        EvidenceStatus.Success => "reușit", EvidenceStatus.Empty => "gol", EvidenceStatus.Failed => "eșuat",
        EvidenceStatus.NotAvailable => "indisponibil", EvidenceStatus.Partial => "parțial", _ => "omis intenționat",
    };

    public static string AntiForensic(AntiForensicResult r) => r switch { AntiForensicResult.Detected => "DETECTAT", AntiForensicResult.NotDetected => "NEDETECTAT", _ => "NEDETERMINAT" };

    public static string Classification(Classification c) => StateLabels.ForClassification(c);

    public static string Semantic(SemanticType t) => t switch
    {
        SemanticType.Observation => "observație", SemanticType.Presence => "prezență", SemanticType.Execution => "execuție",
        SemanticType.Configuration => "configurație", SemanticType.Correlation => "asociere", SemanticType.Inference => "deducție", _ => "atribuire",
    };

    /// <summary>MITRE-style category of a finding in Romanian; an unknown category is returned unchanged.</summary>
    public static string Category(string category) => category switch
    {
        "Execution" => "Execuție", "Persistence" => "Persistență", "Defense Evasion" => "Evitarea apărării", "Credential Access" => "Accesarea credențialelor",
        "Discovery" => "Descoperire", "Lateral Movement" => "Mișcare laterală", "Collection" => "Colectare", "Exfiltration" => "Exfiltrare",
        "Command and Control" => "Comandă și control", "Initial Access" => "Acces inițial", "Privilege Escalation" => "Escaladarea privilegiilor",
        "Impact" => "Impact", "InitialAccess" => "Acces inițial", "DefenseEvasion" => "Evitarea apărării", "Removable media" => "Suporturi de stocare amovibile",
        "Control gap" => "Decalaj de control", "Command and Control / Exfiltration" => "Comandă și control / Exfiltrare", "Combined sequences" => "Secvențe combinate",
        "Air-gap integrity" => "Integritatea izolării fizice (air-gap)", "Incident" => "Incident","Reconnaissance" => "Recunoaștere", "Anti-Forensics" or "Anti-forensics" => "Ascunderea sau ștergerea urmelor",
        _ => category,
    };

    public static string Temporal(TemporalType t) => t switch
    {
        TemporalType.Historical => "istoric", TemporalType.Live => "stare live", TemporalType.CurrentSnapshot => "instantaneu curent",
        TemporalType.Derived => "derivat", _ => "necunoscut",
    };
}
