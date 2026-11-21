using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Planner #14 E4: CFG-UPDATES-STALE. From the rows of the System log (channel "System", provider Microsoft-Windows-WindowsUpdateClient, event 19 = "installation successful"):
/// when the last successful QUALITY update (a KB update; Microsoft Defender definition updates and the Malicious Software Removal Tool are not counted) is more than 60 days
/// before the COLLECTION date, or there is none, the finding quotes the latest one (its update title) or says none was found. If the System log does not reach back 60 days
/// (its oldest record is newer than collection - 60 days) and no such event exists, that is a gap, never a finding: the log may simply be too short to show the update.
/// </summary>
public static class WindowsUpdateAudit
{
    public const string Producer = "LogAnalyzer.Dfir.Analysis.WindowsUpdateAudit";
    public const string Rule = "CFG-UPDATES-STALE";
    public const int StaleDays = 60;
    public const string Channel = "System", UpdateProvider = "Microsoft-Windows-WindowsUpdateClient";

    private static readonly Regex Kb = new(@"\bKB\d{6,8}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NotQuality = new(@"Defender|Security Intelligence|Malicious Software Removal|Intelligence Update", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string Title(TimelineEvent e)
    {
        foreach (var k in new[] { "updateTitle", "UpdateTitle" })
            if (e.Fields.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
        return e.Summary;
    }

    /// <summary>True when the row is a successful install (19) of a quality update.</summary>
    public static bool IsQualityInstall(TimelineEvent e) =>
        e.Source.Equals("EventLog:" + Channel, StringComparison.OrdinalIgnoreCase) && e.EventId == "19"
        && e.Provider.Equals(UpdateProvider, StringComparison.OrdinalIgnoreCase) && e.Time.Utc is not null
        && Title(e) is var t && Kb.IsMatch(t) && !NotQuality.IsMatch(t);

    /// <param name="collectedUtc">The collection time; the comparison never uses today's date.</param>
    public static ConfigAuditResult Evaluate(IEnumerable<TimelineEvent> timeline, DateTimeOffset collectedUtc, string host, Func<string> nextId)
    {
        var res = new ConfigAuditResult();
        var system = timeline.Where(e => e.Source.Equals("EventLog:" + Channel, StringComparison.OrdinalIgnoreCase) && e.Time.Utc is not null).ToList();
        if (system.Count == 0) return res;   // the System log was not collected: the coverage matrix and the collector's error already say so
        var oldest = system.MinBy(e => e.Time.Utc!.Value)!;
        var latest = system.Where(IsQualityInstall).MaxBy(e => e.Time.Utc!.Value);
        var limit = collectedUtc.AddDays(-StaleDays);
        string coll = collectedUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), oldestText = oldest.Time.Utc!.Value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (latest is not null && latest.Time.Utc!.Value >= limit) return res;   // fresh

        if (latest is null && oldest.Time.Utc!.Value > limit)
        {
            res.Gaps.Add(new EvidenceGap("Actualizări Windows (CFG-UPDATES-STALE)", EvidenceStatus.Partial,
                $"jurnalul System nu acoperă {StaleDays} de zile: cea mai veche înregistrare este din {oldestText}, iar colectarea din {coll}; nu există niciun eveniment 19 (instalare reușită)",
                "Nu se poate spune din jurnal dacă o actualizare a fost instalată în ultimele 60 de zile; lipsa unei constatări nu înseamnă că stația este la zi",
                "Windows Update → Istoricul actualizărilor, sau Get-HotFix | Sort-Object InstalledOn", "Da, pe stație"));
            return res;
        }

        string days = latest is null ? "" : $"{(int)(collectedUtc - latest.Time.Utc!.Value).TotalDays}";
        string latestText = latest is null ? "" : $"{latest.Time.Utc!.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z";
        string title = latest is null
            ? $"Jurnalul System nu arată nicio actualizare de calitate instalată în cele {StaleDays} de zile dinaintea colectării"
            : $"Ultima actualizare de calitate instalată cu succes este de acum {days} de zile, înainte de colectare";
        string description = latest is null
            ? $"În jurnalul System (înregistrări din {oldestText} până la colectare, {coll}) nu există niciun eveniment Microsoft-Windows-WindowsUpdateClient 19 (instalare reușită) al unei actualizări de calitate (KB). Jurnalul acoperă cele {StaleDays} de zile dinaintea colectării."
            : $"Ultimul eveniment Microsoft-Windows-WindowsUpdateClient 19 (instalare reușită) al unei actualizări de calitate este din {latestText}: „{Title(latest)}”. Colectarea este din {coll}, adică {days} de zile după, mai mult de {StaleDays}.";
        var anchor = latest ?? system.MaxBy(e => e.Time.Utc!.Value)!;
        res.Findings.Add(new Finding
        {
            FindingId = nextId(), RuleId = Rule, Title = title, Description = description, Severity = Severity.Medium, Confidence = Confidence.Medium,
            Classification = Classification.Direct, Category = "Configurație", Host = host, FirstSeenUtc = latest?.Time.Utc, LastSeenUtc = latest?.Time.Utc, SemanticType = SemanticType.Observation,
            PlainWhat = latest is null ? "Windows nu a instalat nicio actualizare de calitate în ultimele 60 de zile înainte de colectare" : $"Windows nu a mai instalat o actualizare de calitate de {days} de zile",
            SupportingEvidence = [Ref(anchor, latest is null ? "ultima înregistrare a jurnalului System (nu există niciun eveniment 19 de actualizare de calitate)" : "ultima instalare reușită a unei actualizări de calitate (WindowsUpdateClient 19)")],
            ClassificationReason = $"Evenimentele din jurnalul System păstrat în caz (SHA-256 în indexul probelor); gravitate medie: cu actualizările lipsă defectele cunoscute rămân deschise. Comparat cu data colectării ({coll}).",
            AlternativeExplanations =
            [
                "Actualizările pot fi instalate de un alt instrument (WSUS, Intune, instalare manuală a pachetului) care nu scrie evenimentul 19 în acest jurnal.",
                "Stația poate fi pusă deliberat pe pauză de actualizări sau administrată de o organizație cu propriul ciclu.",
            ],
            Limitations =
            [
                $"Jurnalul System păstrat în caz începe la {oldestText}; ce s-a întâmplat înaintea acestei date nu se vede.",
                "Se numără ca actualizare de calitate un eveniment 19 al cărui titlu conține un număr KB; actualizările de definiții Microsoft Defender și Instrumentul de eliminare a software-ului rău intenționat nu se numără.",
                $"Compararea se face cu data colectării ({coll}), nu cu data de azi.",
            ],
        });
        return res;
    }

    private static EvidenceRef Ref(TimelineEvent row, string description)
    {
        var (quote, ts, ch) = EvidenceQuote.From(row);
        return new EvidenceRef(row.EvidenceId, row.Locator, description, row.SourceSha256, quote, ts, ch);
    }
}
