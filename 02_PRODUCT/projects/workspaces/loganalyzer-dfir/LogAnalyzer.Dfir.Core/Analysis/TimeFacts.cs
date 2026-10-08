using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Timezone basis and uncertainty of a timeline row, derived only from what the parser recorded (conversion method and time
/// semantics). Empty means "the source does not say", never a guess. UTC stays the normalized value; the raw source value is
/// exported next to it (program requirements §6).
/// </summary>
public static class TimeFacts
{
    public static (string Zone, string Uncertainty) Describe(Timestamp t, string timeSemantics)
    {
        string zone, unc = "";
        var m = t.ConversionMethod ?? "";
        if (t.Utc is null)
            return (m.StartsWith("no timezone", StringComparison.OrdinalIgnoreCase) ? "unknown (source has no offset; not converted)" : "unknown",
                    "Sursa nu conține o oră utilizabilă; nu s-a folosit ora curentă.");
        if (m.StartsWith("fsutil", StringComparison.OrdinalIgnoreCase)) { zone = "local time of the case timezone, converted to UTC"; unc = m.Contains("ambiguă", StringComparison.OrdinalIgnoreCase) ? "Oră ambiguă la schimbarea orei de vară/iarnă." : "Conversia depinde de fusul orar al cazului."; }
        else if (m.Contains("ISO 8601 with offset", StringComparison.OrdinalIgnoreCase)) zone = "offset stated by the source, converted to UTC";
        else if (m.Contains("observed now", StringComparison.OrdinalIgnoreCase)) { zone = "UTC (collection time)"; unc = "Ora colectării, nu ora unui eveniment din trecut."; }
        else zone = "UTC (native)";
        var sem = timeSemantics ?? "";
        if (sem.Contains("aggregate", StringComparison.OrdinalIgnoreCase)) unc = Join(unc, "Agregat orar: evenimentul s-a petrecut undeva în intervalul de ~1h.");
        if (sem.Contains("last written", StringComparison.OrdinalIgnoreCase)) unc = Join(unc, "Ora cheii de registru, nu a valorii sau a unei acțiuni.");
        if (sem.Contains("author-supplied", StringComparison.OrdinalIgnoreCase)) unc = Join(unc, "Furnizată de autorul înregistrării; poate fi falsificată.");
        if (sem.Contains("not execution", StringComparison.OrdinalIgnoreCase)) unc = Join(unc, "Nu este o oră de execuție.");
        return (zone, unc);
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : a + " " + b;

    /// <summary>What a timeline row is about, from its source. Execution only for artifacts that record a run; "not execution" semantics override.</summary>
    public static SemanticType SemanticOf(string source, string timeSemantics)
    {
        if (timeSemantics.Contains("not execution", StringComparison.OrdinalIgnoreCase)) return SemanticType.Presence;
        return AntiOverclaim.SemanticForArtifact(source);
    }

    public static void Annotate(TimelineEvent e)
    {
        var (zone, unc) = Describe(e.Time, e.TimeSemantics);
        if (e.TimeZoneBasis.Length == 0) e.TimeZoneBasis = zone;
        if (e.TimeUncertainty.Length == 0) e.TimeUncertainty = unc;
        e.SemanticType = SemanticOf(e.Source, e.TimeSemantics);
    }
}
