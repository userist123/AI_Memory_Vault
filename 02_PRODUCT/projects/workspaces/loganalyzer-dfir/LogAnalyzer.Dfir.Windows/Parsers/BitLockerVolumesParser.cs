using System.Globalization;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Planner #14 E8: the BitLocker snapshot (JSON of BitLockerCollector): one row per volume, locator "BitLockerVolume[C:]", RawLine = the text of the JSON object as stored.
/// The numbers are WMI codes (ProtectionStatus 0 off, 1 on, 2 unknown; ConversionStatus 0 fully decrypted ... 5 decryption paused); the readable names are added as fields.
/// A file without a volume list gives a gap, never an empty list.
/// </summary>
public sealed class BitLockerVolumesParser : EvidenceParserBase
{
    public const string Source = "BitLocker";
    public const string SourceTypeName = "bitlocker_volumes_json";

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "BitLockerVolumesParser", Version = "1.0", Artifact = "Starea BitLocker / criptării dispozitivului pe volume (instantaneu JSON la colectare)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = ["json"],
        SupportedOs = "Windows (WMI root\\CIMV2\\Security\\MicrosoftVolumeEncryption, Win32_EncryptableVolume); lipsește pe ediția Home fără criptarea dispozitivului",
        FormatVersions = ["la.bitlocker/1"],
        Limitations =
        [
            "Este starea de la colectare, nu istoricul ei.",
            "ProtectionStatus 0 înseamnă că protecția este oprită: volumul poate fi decriptat sau criptat cu protecția suspendată; ConversionStatus spune care.",
            "Un volum blocat sau o stare necunoscută (ProtectionStatus 2) nu este tratat ca necriptat.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "BitLockerTests: JSON sintetic (criptat, oprit, fără listă de volume)",
        MaxInputBytes = 1_000_000,
    };

    /// <summary>True when the file is the BitLocker snapshot: a JSON object (within the parser's size cap) whose "schema" starts with "la.bitlocker".</summary>
    public static bool LooksLikeBitLockerJson(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0 || fi.Length > 1_000_000) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var r = doc.RootElement;
            return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("schema", out var sc) && sc.ValueKind == JsonValueKind.String && (sc.GetString() ?? "").StartsWith("la.bitlocker", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
    }

    public static string ProtectionText(int? v) => v switch { 0 => "oprită", 1 => "pornită", 2 => "necunoscută", _ => "necunoscută" };
    public static string ConversionText(int? v) => v switch
    {
        0 => "complet decriptat", 1 => "complet criptat", 2 => "criptare în curs", 3 => "decriptare în curs", 4 => "criptare oprită temporar", 5 => "decriptare oprită temporar", _ => "necunoscută",
    };
    public static string MethodText(int? v) => v switch
    {
        0 => "niciuna", 1 => "AES 128 cu difuzor", 2 => "AES 256 cu difuzor", 3 => "AES 128", 4 => "AES 256", 5 => "hardware", 6 => "XTS-AES 128", 7 => "XTS-AES 256", _ => "necunoscută",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(fullPath, new UTF8Encoding(false)));
        var root = doc.RootElement;
        string Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int? Num(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        string N(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
        var when = DateTimeOffset.TryParse(Str(root, "collectedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var c) ? c : item.AcquiredAtUtc;
        var time = Timestamp.FromUtc(when.UtcDateTime, when.UtcDateTime.ToString("o", CultureInfo.InvariantCulture), "observed now (collection time)");
        string machine = Str(root, "machine"), systemDrive = Str(root, "systemDrive");

        if (!root.TryGetProperty("volumes", out var volumes) || volumes.ValueKind != JsonValueKind.Array)
        {
            result.Gaps.Add(new EvidenceGap("Starea BitLocker", EvidenceStatus.Failed, "fișierul nu conține lista de volume", "CFG-DISK-UNENCRYPTED nu poate fi evaluat", "manage-bde -status", "Da, pe stație"));
            return;
        }
        foreach (var v in volumes.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            string letter = Str(v, "driveLetter");
            if (letter.Length == 0) { result.MalformedRecords++; continue; }
            int? prot = Num(v, "protectionStatus"), conv = Num(v, "conversionStatus"), meth = Num(v, "encryptionMethod"), type = Num(v, "volumeType");
            bool os = v.TryGetProperty("isOsVolume", out var o) && o.ValueKind == JsonValueKind.True;
            sink.Add(new TimelineEvent
            {
                Time = time, TimeSemantics = "collection time (snapshot)", Source = Source, EvidenceId = item.EvidenceId, Host = machine,
                Summary = $"Volum {letter}: protecția BitLocker {ProtectionText(prot)}, conversie: {ConversionText(conv)}",
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High, Locator = $"BitLockerVolume[{letter}]",
                SemanticType = SemanticType.Configuration,
                Fields =
                {
                    ["DriveLetter"] = letter, ["ProtectionStatus"] = N(prot), ["ProtectionText"] = ProtectionText(prot), ["ConversionStatus"] = N(conv), ["ConversionText"] = ConversionText(conv),
                    ["EncryptionMethod"] = N(meth), ["EncryptionMethodText"] = MethodText(meth), ["VolumeType"] = N(type), ["IsOsVolume"] = os ? "da" : "nu",
                    ["SystemDrive"] = systemDrive, ["Machine"] = machine, ["RawLine"] = v.GetRawText(),
                },
            });
            result.Records++;
        }
    }
}
