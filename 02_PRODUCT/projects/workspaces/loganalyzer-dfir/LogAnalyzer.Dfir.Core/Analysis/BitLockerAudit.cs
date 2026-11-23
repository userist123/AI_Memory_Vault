using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Planner #14 E8: CFG-DISK-UNENCRYPTED from the rows of the stored BitLocker snapshot (source "BitLocker", one row per volume, RawLine = the JSON object as stored).
/// The operating-system volume with protection off is Medium; the fixed data volumes with protection off are one Low finding. Facts only. A removable volume (VolumeType 2),
/// a locked or unknown state (ProtectionStatus 2) and a protected volume give nothing. A snapshot that could not be read gives no rows (the collector reports the reason as a gap).
/// When a case holds several snapshots, only the newest is judged.
/// </summary>
public static class BitLockerAudit
{
    public const string Source = "BitLocker";
    public const string Rule = "CFG-DISK-UNENCRYPTED";
    public const string Category = "Configurație";

    private static string F(TimelineEvent r, string k) => r.Fields.TryGetValue(k, out var v) ? v : "";

    public static ConfigAuditResult Evaluate(IEnumerable<TimelineEvent> timeline, string host, Func<string> nextId)
    {
        var res = new ConfigAuditResult();
        var mine = timeline.Where(r => r.Source == Source).ToList();
        if (mine.Count == 0) return res;
        var id = mine.GroupBy(r => r.EvidenceId).OrderByDescending(g => g.Max(r => r.Time.Utc ?? DateTimeOffset.MinValue)).ThenByDescending(g => g.Key, StringComparer.Ordinal).First().Key;
        var rows = mine.Where(r => r.EvidenceId == id).ToList();
        var off = rows.Where(r => F(r, "ProtectionStatus") == "0" && F(r, "VolumeType") != "2").ToList();

        foreach (var os in off.Where(r => F(r, "IsOsVolume") == "da"))
            res.Findings.Add(Make(os, host, nextId, Severity.Medium, Confidence.High,
                $"Volumul de sistem {F(os, "DriveLetter")} nu are protecția BitLocker pornită: {Status(os)}",
                $"Volumul de sistem {F(os, "DriveLetter")} are ProtectionStatus = 0 (protecția oprită); starea conversiei: {F(os, "ConversionText")}. {Detail(os)}",
                $"Discul de sistem {F(os, "DriveLetter")} nu este protejat prin BitLocker: oricine are discul poate citi fișierele",
                "gravitate medie: dacă stația se pierde sau este furată, conținutul discului poate fi citit fără parola Windows",
                [os],
                ["Stația poate folosi alt produs de criptare a discului sau criptare hardware (unitate cu autocriptare) care nu apare aici.", "Protecția poate fi suspendată temporar pentru o actualizare de firmware sau BIOS."]));

        var data = off.Where(r => F(r, "IsOsVolume") != "da").ToList();
        if (data.Count > 0)
        {
            string list = string.Join(", ", data.Select(r => F(r, "DriveLetter")));
            res.Findings.Add(Make(data[0], host, nextId, Severity.Low, Confidence.High,
                $"{(data.Count == 1 ? "Volumul de date" : "Volumele de date")} {list} nu {(data.Count == 1 ? "are" : "au")} protecția BitLocker pornită",
                $"Volumele de date {list} au ProtectionStatus = 0 (protecția oprită): " + string.Join("; ", data.Select(r => $"{F(r, "DriveLetter")}: {F(r, "ConversionText")}")) + ".",
                $"Discurile de date {list} nu sunt protejate prin criptare",
                "gravitate scăzută: conținutul volumelor de date poate fi citit dacă discul este scos sau stația este pierdută",
                data,
                ["Un volum de date poate fi criptat de alt produs sau poate conține doar date fără valoare.", "Volumele amovibile nu sunt judecate aici."]));
        }
        return res;
    }

    private static string Status(TimelineEvent r) => F(r, "ConversionStatus") == "1" ? "criptat, dar cu protecția suspendată" : "volum necriptat";

    private static string Detail(TimelineEvent r) => F(r, "ConversionStatus") == "1"
        ? "Volumul este criptat, dar protecția este suspendată: cheia de decriptare este păstrată necriptată pe disc până la reluarea protecției."
        : "Volumul nu este criptat sau criptarea nu este activă.";

    private static EvidenceRef Ref(TimelineEvent row, string description)
    {
        var (quote, ts, ch) = EvidenceQuote.From(row);
        return new EvidenceRef(row.EvidenceId, row.Locator, description, row.SourceSha256, quote, ts, ch);
    }

    private static Finding Make(TimelineEvent first, string host, Func<string> nextId, Severity sev, Confidence conf, string title, string description, string plainWhat, string sevReason,
                                IReadOnlyList<TimelineEvent> rows, string[] alternatives) => new()
    {
        FindingId = nextId(), RuleId = Rule, Title = title, Description = description, Severity = sev, Confidence = conf, Classification = Classification.Direct,
        Category = Category, Host = host, FirstSeenUtc = first.Time.Utc, LastSeenUtc = first.Time.Utc, PlainWhat = plainWhat, SemanticType = SemanticType.Configuration,
        SupportingEvidence = [.. rows.Select(r => Ref(r, $"volumul {F(r, "DriveLetter")} din instantaneul BitLocker păstrat în caz"))],
        ClassificationReason = $"Valoare citită prin WMI (Win32_EncryptableVolume) și păstrată în caz (SHA-256 în indexul probelor); {sevReason}.",
        AlternativeExplanations = [.. alternatives],
        Limitations =
        [
            "Ora este momentul colectării (instantaneu), nu momentul în care protecția a fost oprită.",
            "ProtectionStatus este starea raportată de WMI; nu arată dacă există un protector de recuperare sau unde este salvată cheia.",
        ],
    };
}
