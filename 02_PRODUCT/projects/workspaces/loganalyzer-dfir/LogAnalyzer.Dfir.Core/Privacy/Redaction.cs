using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Privacy;

/// <summary>
/// Reversible: the pseudonym → original table is kept, encrypted for this Windows account on this station (DPAPI), so an authorised operator can
/// reveal one value later, with a reason, in the audit. Irreversible: the table is never written; the export cannot be mapped back by anyone.
/// </summary>
public enum RedactionMode { Reversible, Irreversible }

/// <summary>Master plan 6.15: which kinds of personal data an export masks, and whether the masking can be reversed.</summary>
public sealed record RedactionPolicy(RedactionMode Mode, IReadOnlyCollection<PiiCategory> Masked)
{
    public static readonly IReadOnlyList<PiiCategory> All = Enum.GetValues<PiiCategory>();
    public static RedactionPolicy Default { get; } = new(RedactionMode.Irreversible, All);

    /// <summary>Sets the masker to leave readable every category this policy does not mask.</summary>
    public PiiMasker Apply(PiiMasker masker)
    {
        masker.Keep.Clear();
        foreach (var c in All.Where(c => !Masked.Contains(c))) masker.Keep.Add(c);
        return masker;
    }

    public static string Name(PiiCategory c) => c switch
    {
        PiiCategory.Users => "utilizatori", PiiCategory.Hosts => "stații și domenii", PiiCategory.Addresses => "adrese IP și MAC",
        PiiCategory.Emails => "e-mailuri", PiiCategory.Sids => "SID-uri", _ => "numere de serie",
    };

    public string Describe() => (Mode == RedactionMode.Reversible ? "pseudonimizare reversibilă" : "mascare ireversibilă") + ": "
        + (Masked.Count == 0 ? "nimic mascat" : string.Join(", ", All.Where(Masked.Contains).Select(Name)));
}

/// <summary>What one redacted export replaced, bound to the case state it was made from. Written once, read-only, and anchored in the case audit.</summary>
public sealed record RedactionRecord(string Id, string Utc, string Operator, RedactionMode Mode, List<PiiCategory> Masked, string Kind, string OutputName,
    string OutputSha256, Dictionary<PiiCategory, int> Counts, string CaseStateSha256, string MappingFile, string MappingSha256, string GeneratorVersion);

/// <summary>One line of the preview: where the text is, as it is in the case, and as the export will show it.</summary>
public sealed record RedactionPreviewRow(string Where, string Before, string After);

public static class Redactions
{
    public const string RelDir = "Reports/Redactions";
    public const string Version = "1.0";
    private static readonly Regex IdRx = new(@"^RED-\d{8}-\d{6}(?:-\d{1,3})?$", RegexOptions.CultureInvariant);
    private static readonly Regex PseudonymRx = new(@"^(?:Utilizator|Statie|IP|SID|email|Serial|Domeniu|MAC)-\d{1,7}(?:@mascat\.invalid)?$", RegexOptions.CultureInvariant);
    /// <summary>Free text for one audit field: no control characters, no quotes, bounded, so it cannot forge another field or line.</summary>
    private static string Clean(string s)
    {
        var x = new string((s ?? "").Trim().Select(c => char.IsControl(c) || c == '"' ? ' ' : c).ToArray());
        return x.Length > 300 ? x[..300] : x;
    }
    private static byte[] Entropy(string id) => Encoding.UTF8.GetBytes("LogAnalyzer.Redaction.v1:" + id);

    /// <summary>The first rows whose text the policy changes, side by side: the operator sees what will leave before anything is written.</summary>
    public static List<RedactionPreviewRow> Preview(IReadOnlyList<TimelineEvent> timeline, IEnumerable<Finding> findings, PiiMasker masker, int max = 40)
    {
        var rows = new List<RedactionPreviewRow>();
        foreach (var f in findings)
        {
            if (rows.Count >= max / 2) break;
            var after = masker.Mask(f.Title + " — " + f.Description);
            if (after != f.Title + " — " + f.Description) rows.Add(new RedactionPreviewRow($"constatarea {f.FindingId}", f.Title + " — " + f.Description, after));
        }
        foreach (var e in timeline)
        {
            if (rows.Count >= max) break;
            var m = masker.Mask(e);
            string before = $"{e.Host} | {e.User} | {e.Summary}", after2 = $"{m.Host} | {m.User} | {m.Summary}";
            if (before != after2) rows.Add(new RedactionPreviewRow($"cronologie {e.EvidenceId} {e.Locator}", before, after2));
        }
        return rows;
    }

    /// <summary>SHA-256 over the hashes of the case files an export is made from (evidence index, findings, timeline), so a record says which case state it shows.</summary>
    public static string CaseState(CaseWorkspace ws)
    {
        var sb = new StringBuilder();
        foreach (var rel in new[] { "Evidence/evidence_index.jsonl", "Analysis/findings.json", "Analysis/timeline.csv" })
        {
            var p = Path.Combine(ws.Root, rel);
            sb.Append(rel).Append('=').Append(File.Exists(p) ? Convert.ToHexString(SHA256.HashData(HashChain.ReadAllShared(p))) : "absent").Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// Records a finished export. A reversible policy stores the table of the masked categories encrypted for the current Windows account (DPAPI, with an
    /// entropy bound to the record id); an irreversible one stores only counts. Nothing in the case evidence or analysis is touched.
    /// </summary>
    public static RedactionRecord Record(CaseWorkspace ws, RedactionPolicy policy, PiiMasker masker, string kind, string outputPath, string operatorName)
    {
        if (ws.IsReadOnly) throw new InvalidOperationException("Cazul este doar pentru citire (sigilat sau închis): înregistrarea redactării nu se scrie în el.");
        if (!File.Exists(outputPath)) throw new InvalidOperationException("Fișierul exportat nu există; nu există ce înregistra.");
        if (policy.Mode == RedactionMode.Reversible && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Pseudonimizarea reversibilă păstrează tabela criptată pentru contul Windows; pe acest sistem alegeți mascarea ireversibilă.");
        var dir = Path.Combine(ws.Root, "Reports", "Redactions");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow;
        string id = "RED-" + stamp.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (int n = 2; File.Exists(Path.Combine(dir, id + ".json")); n++) id = "RED-" + stamp.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + n;

        string mapFile = "", mapSha = "";
        if (policy.Mode == RedactionMode.Reversible && OperatingSystem.IsWindows())
        {
            var entries = masker.Entries.Where(e => policy.Masked.Contains(e.Category)).ToList();
            var plain = JsonSerializer.SerializeToUtf8Bytes(entries, LogAnalyzer.Dfir.IO.Json.Line);
            var blob = ProtectedData.Protect(plain, Entropy(id), DataProtectionScope.CurrentUser);
            CryptographicOperations.ZeroMemory(plain);
            mapFile = id + ".map.dpapi";
            WriteOnce(Path.Combine(dir, mapFile), blob);
            mapSha = Convert.ToHexString(SHA256.HashData(blob));
        }
        var outSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath)));
        var rec = new RedactionRecord(id, stamp.ToString("o"), operatorName, policy.Mode, RedactionPolicy.All.Where(policy.Masked.Contains).ToList(), kind,
            Path.GetFileName(outputPath), outSha, masker.Counts.ToDictionary(kv => kv.Key, kv => kv.Value), CaseState(ws), mapFile, mapSha, Version);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(rec, LogAnalyzer.Dfir.IO.Json.Options);
        WriteOnce(Path.Combine(dir, id + ".json"), bytes);
        ws.Audit("redaction.recorded", $"{id} {kind} {policy.Describe()} iesire={rec.OutputName} sha256={outSha} record_sha256={Convert.ToHexString(SHA256.HashData(bytes))}"
            + (mapSha.Length > 0 ? $" tabela_sha256={mapSha}" : " fara_tabela"));
        return rec;
    }

    private static void WriteOnce(string path, byte[] bytes)
    {
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) fs.Write(bytes);
        File.SetAttributes(path, FileAttributes.ReadOnly);
    }

    /// <summary>The records of a case, newest first. A record whose file does not parse is skipped and named in <paramref name="problems"/>.</summary>
    public static List<RedactionRecord> List(CaseWorkspace ws, List<string>? problems = null)
    {
        var dir = Path.Combine(ws.Root, "Reports", "Redactions");
        var list = new List<RedactionRecord>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "RED-*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<RedactionRecord>(File.ReadAllBytes(f), LogAnalyzer.Dfir.IO.Json.Options);
                if (r is not null && IdRx.IsMatch(r.Id) && Path.GetFileNameWithoutExtension(f) == r.Id) list.Add(r);
                else problems?.Add(Path.GetFileName(f) + ": nu este o înregistrare de redactare validă");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { problems?.Add(Path.GetFileName(f) + ": " + ex.Message); }
        }
        return list.OrderByDescending(r => r.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Reveals the original behind one pseudonym of a reversible export. Needs a reason, works only for the Windows account that made the export
    /// (DPAPI), checks the table against the hash in the record, and writes who asked and why (never the original) into the case audit.
    /// </summary>
    public static (string? Original, string Message) Reveal(CaseWorkspace ws, string id, string pseudonym, string reason, string operatorName)
    {
        if (!IdRx.IsMatch(id ?? "")) return (null, "Identificatorul înregistrării nu este valid.");
        if (string.IsNullOrWhiteSpace(reason)) return (null, "Dezvăluirea cere un motiv; motivul se scrie în auditul cazului.");
        // only a pseudonym is accepted (an original typed here would otherwise land in the audit)
        if (!PseudonymRx.IsMatch(pseudonym.Trim())) return (null, "Scrieți un pseudonim din export (de exemplu Utilizator-3, IP-2, email-1@mascat.invalid).");
        var rec = List(ws).FirstOrDefault(r => r.Id == id);
        if (rec is null) return (null, $"Înregistrarea {id} nu există în caz.");
        if (rec.Mode != RedactionMode.Reversible || rec.MappingFile.Length == 0) return (null, $"Exportul {id} a fost mascat ireversibil: nu există o tabelă din care să se refacă valoarea.");
        if (!OperatingSystem.IsWindows()) return (null, "Tabela se poate deschide doar pe Windows, de contul care a făcut exportul.");
        // the table's name is fixed by the record id: a record edited to point elsewhere is not followed
        if (rec.MappingFile != rec.Id + ".map.dpapi") return (null, "Înregistrarea indică o altă tabelă decât a ei; nu se folosește.");
        var path = Path.Combine(ws.Root, "Reports", "Redactions", rec.MappingFile);
        if (!File.Exists(path)) return (null, "Tabela pseudonimelor lipsește din caz.");
        var blob = File.ReadAllBytes(path);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(blob)), rec.MappingSha256, StringComparison.OrdinalIgnoreCase))
            return (null, "Tabela pseudonimelor nu mai are amprenta din înregistrare: a fost modificată; nu se folosește.");
        byte[] plain;
        try { plain = ProtectedData.Unprotect(blob, Entropy(rec.Id), DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { return (null, "Tabela nu poate fi deschisă de acest cont Windows: doar contul care a făcut exportul, pe această stație, o poate citi."); }
        try
        {
            var entries = JsonSerializer.Deserialize<List<PiiEntry>>(plain, LogAnalyzer.Dfir.IO.Json.Options) ?? [];
            var hit = entries.FirstOrDefault(e => string.Equals(e.Pseudonym, pseudonym.Trim(), StringComparison.OrdinalIgnoreCase));
            ws.Audit("redaction.revealed", $"{rec.Id} pseudonim={pseudonym.Trim()} gasit={(hit is null ? "nu" : "da")} operator=\"{Clean(operatorName)}\" motiv=\"{Clean(reason)}\"");
            return hit is null ? (null, $"Pseudonimul „{pseudonym.Trim()}” nu apare în exportul {rec.Id}.") : (hit.Original, $"{hit.Pseudonym} = {hit.Original} (dezvăluire trecută în audit).");
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
