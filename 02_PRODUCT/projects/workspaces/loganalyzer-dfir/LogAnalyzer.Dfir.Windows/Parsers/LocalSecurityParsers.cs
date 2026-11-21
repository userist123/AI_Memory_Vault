using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// <c>auditpol /get /category:* /r</c> saved by LocalSecurityStateCollector: one row per subcategory, found by its GUID (the name and the setting text are localized, the GUID is not).
/// Locator "auditpol:&lt;GUID&gt;", RawLine = the CSV line as stored. The setting is read as Success / Failure flags from the English and the Romanian wording; any other
/// wording gives "necunoscut" (never a guess), so a rule cannot fire on a setting that was not understood.
/// </summary>
public sealed class AuditPolicyCsvParser : EvidenceParserBase
{
    public const string Source = "auditpol";
    private static readonly Regex Guid = new(@"^\{?([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?$", RegexOptions.Compiled);

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "AuditPolicyCsvParser", Version = "1.0", Artifact = "Politica de audit (auditpol /get /category:* /r) — o linie pe subcategorie",
        SourceTypes = ["auditpol_csv"], FileNames = [], Fingerprints = ["auditpol_csv"],
        SupportedOs = "Windows (auditpol.exe); antetul și textul setărilor sunt localizate, subcategoria se recunoaște după GUID",
        FormatVersions = ["CSV: Machine Name, Policy Target, Subcategory, Subcategory GUID, Inclusion Setting, Exclusion Setting"],
        Limitations =
        [
            "Setările se citesc din coloana numerică „Setting Value” (0-3) când fișierul o are, altfel din textul în limba engleză sau română („Success”/„Succes”, „Failure”/„Eșec”, „No Auditing”/„Fără auditare”); pe o stație în altă limbă, fără coloana numerică, textul dă „necunoscut” (lacună păstrată: auditpol /get /r scrie doar coloanele text).",
            "Este politica efectivă la colectare, nu istoricul ei (schimbările se văd în Security 4719, dacă a fost auditat).",
            "Fără oră proprie: rândul poartă ora colectării.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "LocalSecurityStateTests: CSV sintetic în engleză și română, potrivire după GUID",
        MaxInputBytes = 4_000_000,
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var time = Timestamp.FromUtc(item.AcquiredAtUtc.UtcDateTime, item.AcquiredAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture), "observed now (collection time)");
        foreach (var raw in File.ReadLines(fullPath, new UTF8Encoding(false)))
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = SplitCsv(line);
            int g = f.FindIndex(x => Guid.IsMatch(x.Trim()));
            if (g < 1 || g + 1 >= f.Count) continue;   // the header line (localized, no GUID) and anything that is not a subcategory row
            string guid = Guid.Match(f[g].Trim()).Groups[1].Value.ToUpperInvariant(), name = f[g - 1].Trim(), setting = f[g + 1].Trim();
            var (ok, s, fl) = f.Count > g + 3 ? InterpretValue(f[g + 3], setting) : Interpret(setting);   // the numeric "Setting Value" column (when the file has one) is language independent and wins over the text
            sink.Add(new TimelineEvent
            {
                Time = time, TimeSemantics = "collection time (snapshot)", Source = Source, EvidenceId = item.EvidenceId, Host = f.Count > 0 ? f[0].Trim() : "",
                Summary = $"Politica de audit: {name} = {setting}", TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $"auditpol:{guid}", SemanticType = SemanticType.Configuration,
                Fields =
                {
                    ["Subcategory"] = name, ["SubcategoryGuid"] = guid, ["Setting"] = setting, ["AuditSuccess"] = ok ? (s ? "da" : "nu") : "necunoscut",
                    ["AuditFailure"] = ok ? (fl ? "da" : "nu") : "necunoscut", ["RawLine"] = line,
                },
            });
            result.Records++;
        }
        if (result.Records == 0)
            result.Gaps.Add(new EvidenceGap("Politica de audit (auditpol)", EvidenceStatus.Failed, "fișierul nu conține nicio linie cu GUID de subcategorie",
                "CFG-AUDIT-* nu pot fi evaluate", @"auditpol /get /category:*", "Da, pe stație, cu drepturi de administrator"));
    }

    /// <summary>
    /// planner #13 D2: the numeric "Setting Value" column of the audit CSV (0 = no auditing, 1 = Success, 2 = Failure, 3 = both; the same column the audit.csv of <c>auditpol /backup</c> has),
    /// which does not depend on the language of the station. A value that is not 0..3 (or an empty column) falls back to the text of the Inclusion Setting (<see cref="Interpret"/>).
    /// <c>auditpol /get /r</c> on the stations seen so far writes only the six text columns, so a station in another language gives "necunoscut" until the file carries the numeric column.
    /// </summary>
    public static (bool Understood, bool Success, bool Failure) InterpretValue(string numeric, string inclusionText)
    {
        var v = numeric.Trim();
        if (v.Length == 1 && v[0] is >= '0' and <= '3') { int n = v[0] - '0'; return (true, (n & 1) != 0, (n & 2) != 0); }
        return Interpret(inclusionText);
    }

    /// <summary>(understood, success, failure). "No Auditing" first: it contains neither word, but must not be read as unknown.</summary>
    public static (bool Understood, bool Success, bool Failure) Interpret(string setting)
    {
        var t = setting.Trim().ToLowerInvariant();
        if (t.Contains("no auditing") || t.Contains("fără auditare") || t.Contains("fara auditare") || t.Contains("fãrã auditare")) return (true, false, false);
        bool s = t.Contains("success") || t.Contains("succes"), fl = t.Contains("failure") || t.Contains("eșec") || t.Contains("eşec") || t.Contains("esec");
        return s || fl ? (true, s, fl) : (false, false, false);
    }

    internal static List<string> SplitCsv(string line)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = false; } else sb.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }
}

/// <summary>
/// The local accounts snapshot (JSON of LocalSecurityStateCollector): one row per account (locator "LocalAccounts[name]") and per member of the local Administrators group
/// (locator "LocalAdministrators[member]"), RawLine = the text of the JSON object as stored. A part that was not read gives a gap, never an empty list.
/// </summary>
public sealed class LocalAccountsParser : EvidenceParserBase
{
    public const string Source = "Conturi locale";

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "LocalAccountsParser", Version = "1.0", Artifact = "Conturi locale și membrii grupului Administrators (instantaneu JSON la colectare)",
        SourceTypes = ["local_accounts_json"], FileNames = [], Fingerprints = ["json"],
        SupportedOs = "Windows (WMI Win32_UserAccount, Win32_GroupUser; grupul se găsește după SID-ul S-1-5-32-544, deci numele localizat nu contează)",
        FormatVersions = ["la.local_accounts/1"],
        Limitations =
        [
            "Este starea de la colectare, nu istoricul conturilor sau al apartenenței.",
            "Ultima autentificare vine din profilul de rețea WMI și poate lipsi.",
            "SID-ul unui cont de domeniu nerezolvabil rămâne gol.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "LocalSecurityStateTests: JSON sintetic (conturi, membri, parte necitită)",
        MaxInputBytes = 8_000_000,
    };

    /// <summary>True when the file is the local accounts snapshot: a JSON object (within the parser's size cap) whose keys include "schema" = "la.local_accounts/..." or the pair "collectedUtc" + ("accounts" or "administrators") as arrays. Never throws.</summary>
    public static bool LooksLikeAccountsJson(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0 || fi.Length > 8_000_000) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return false;
            if (r.TryGetProperty("schema", out var sc) && sc.ValueKind == JsonValueKind.String && (sc.GetString() ?? "").StartsWith("la.local_accounts", StringComparison.Ordinal)) return true;
            bool Arr(string n) => r.TryGetProperty(n, out var a) && a.ValueKind == JsonValueKind.Array;
            return r.TryGetProperty("collectedUtc", out _) && (Arr("accounts") || Arr("administrators"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(fullPath, new UTF8Encoding(false)));
        var root = doc.RootElement;
        string Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string Bool(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? (v.GetBoolean() ? "da" : "nu") : "necunoscut";
        var when = DateTimeOffset.TryParse(Str(root, "collectedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var c) ? c : item.AcquiredAtUtc;
        var time = Timestamp.FromUtc(when.UtcDateTime, when.UtcDateTime.ToString("o", CultureInfo.InvariantCulture), "observed now (collection time)");
        string machine = Str(root, "machine"), interactive = Str(root, "interactiveUser"), domain = Bool(root, "partOfDomain");

        void Row(string locator, string summary, JsonElement obj, Dictionary<string, string> fields)
        {
            fields["RawLine"] = obj.GetRawText(); fields["Machine"] = machine; fields["InteractiveUser"] = interactive; fields["PartOfDomain"] = domain;
            sink.Add(new TimelineEvent
            {
                Time = time, TimeSemantics = "collection time (snapshot)", Source = Source, EvidenceId = item.EvidenceId, Host = machine, Summary = summary,
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High, Locator = locator,
                SemanticType = SemanticType.Configuration, Fields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase),
            });
            result.Records++;
        }

        if (root.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
            foreach (var a in accounts.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                string name = Str(a, "name");
                if (name.Length == 0) { result.MalformedRecords++; continue; }
                string enabled = Bool(a, "enabled"), rid = a.TryGetProperty("rid", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32().ToString(CultureInfo.InvariantCulture) : "";
                Row($"LocalAccounts[{name}]", $"Cont local {name} (RID {rid}): activat = {enabled}", a,
                    new() { ["Name"] = name, ["Sid"] = Str(a, "sid"), ["Rid"] = rid, ["Enabled"] = enabled, ["LastLogonUtc"] = Str(a, "lastLogonUtc"),
                      ["PasswordRequired"] = Bool(a, "passwordRequired"), ["PasswordExpires"] = Bool(a, "passwordExpires"), ["PasswordChangeable"] = Bool(a, "passwordChangeable"), ["Lockout"] = Bool(a, "lockout") });
            }
        else result.Gaps.Add(new EvidenceGap("Conturi locale", EvidenceStatus.Failed, NotRead(Str(root, "accountsError")),
            "CFG-GUEST-ENABLED nu poate fi evaluat", "net user (sau Get-LocalUser)", "Da, pe stație"));

        if (root.TryGetProperty("administrators", out var admins) && admins.ValueKind == JsonValueKind.Array)
            foreach (var m in admins.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                string name = Str(m, "name"), dom = Str(m, "domain");
                if (name.Length == 0) { result.MalformedRecords++; continue; }
                string member = dom.Length > 0 ? $@"{dom}\{name}" : name;
                Row($"LocalAdministrators[{member}]", $"Membru al grupului Administrators: {member} ({Str(m, "kind")}, {Str(m, "scope")})", m,
                    new() { ["Name"] = name, ["Domain"] = dom, ["Member"] = member, ["Sid"] = Str(m, "sid"), ["Kind"] = Str(m, "kind"), ["Scope"] = Str(m, "scope") });
            }
        else result.Gaps.Add(new EvidenceGap("Membrii grupului Administrators", EvidenceStatus.Failed, NotRead(Str(root, "administratorsError")),
            "CFG-EXTRA-ADMINS nu poate fi evaluat", "Get-LocalGroupMember -SID S-1-5-32-544", "Da, pe stație"));
    }

    private static string NotRead(string reason) => reason.Length > 0 ? reason : "partea nu a fost citită la colectare";
}
