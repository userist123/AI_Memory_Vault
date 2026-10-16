using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// One medium of the signed media register (owner decision 2026-10-16): every medium is enrolled on the transfer station with what identifies it (USB
/// serial, VID/PID, vendor / model, capacity, volume serial, BitLocker To Go state and volume id) and what the operator fills in (inventory number,
/// classification level, holder, as HG 585/2002 art. 294 asks). <see cref="Status"/> is "activ", "retras" or "distrus"; a retired or destroyed medium
/// carries the date of that change in <see cref="StatusChangedUtc"/>.
/// </summary>
public sealed class SignedMediaEntry
{
    public string InventoryNumber { get; set; } = "";
    public string UsbSerial { get; set; } = "";
    public string VidPid { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Capacity in bytes; 0 = unknown.</summary>
    public long CapacityBytes { get; set; }
    public string VolumeSerial { get; set; } = "";
    /// <summary>"criptat", "necriptat" or "necunoscut" (BitLocker To Go at enrolment).</summary>
    public string BitLockerStatus { get; set; } = "necunoscut";
    /// <summary>The BitLocker persistent volume id when the medium is encrypted (Win32_EncryptableVolume.PersistentVolumeID); "" = none.</summary>
    public string BitLockerVolumeId { get; set; } = "";
    public string Classification { get; set; } = "";
    public string Holder { get; set; } = "";
    public string Status { get; set; } = SignedMediaRegister.StatusActive;
    /// <summary>ISO-8601 UTC time of the retirement / destruction; "" for an active medium.</summary>
    public string StatusChangedUtc { get; set; } = "";
    public string EnrolledUtc { get; set; } = "";
    public string Notes { get; set; } = "";

    public SignedMediaEntry Copy() => (SignedMediaEntry)MemberwiseClone();

    public bool IsActive => Status == SignedMediaRegister.StatusActive;
    public DateTimeOffset? StatusChanged => SignedMediaRegister.TryUtc(StatusChangedUtc);
}

/// <summary>
/// The signed media register document. Its canonical form (<see cref="SignedMediaRegister.Canonical"/>: keys sorted by ordinal, no whitespace, UTF-8
/// without BOM) is the exact byte string the security officer signs with the qualified certificate on the card (detached CMS, file ".p7s"). Each change
/// (add, retire, destroy) is a new <see cref="Version"/> that names the SHA-256 of the previous one, so a station refuses older versions and versions that
/// do not continue the one it has.
/// </summary>
public sealed class SignedMediaRegisterDoc
{
    public string Format { get; set; } = SignedMediaRegister.FormatId;
    public int Version { get; set; } = 1;
    /// <summary>Lower-case hex SHA-256 of the canonical bytes of the previous version; "" for version 1.</summary>
    public string PreviousVersionSha256 { get; set; } = "";
    /// <summary>The transfer station that issued this version.</summary>
    public string StationId { get; set; } = "";
    public string IssuedUtc { get; set; } = "";
    public List<SignedMediaEntry> Entries { get; set; } = [];

    public DateTimeOffset? Issued => SignedMediaRegister.TryUtc(IssuedUtc);
    public IEnumerable<SignedMediaEntry> Active => Entries.Where(e => e.IsActive);
}

public static class SignedMediaRegister
{
    public const string FormatId = "loganalyzer.media_register.v1";
    public const string StatusActive = "activ", StatusRetired = "retras", StatusDestroyed = "distrus";
    public static readonly string[] Statuses = [StatusActive, StatusRetired, StatusDestroyed];
    public const string SignatureExtension = ".p7s";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = false };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string FileName(int version) => $"registru_medii_v{version.ToString("D4", CultureInfo.InvariantCulture)}.json";

    public static string FormatUtc(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static DateTimeOffset? TryUtc(string? s) =>
        DateTimeOffset.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // ---- canonical form -----------------------------------------------------------------------------------------------------------------

    private static SortedDictionary<string, object> EntryMap(SignedMediaEntry e) => new(StringComparer.Ordinal)
    {
        ["bitlocker_status"] = e.BitLockerStatus ?? "", ["bitlocker_volume_id"] = e.BitLockerVolumeId ?? "", ["capacity_bytes"] = e.CapacityBytes,
        ["classification"] = e.Classification ?? "", ["enrolled_utc"] = e.EnrolledUtc ?? "", ["holder"] = e.Holder ?? "", ["inventory_number"] = e.InventoryNumber ?? "",
        ["model"] = e.Model ?? "", ["notes"] = e.Notes ?? "", ["status"] = e.Status ?? "", ["status_changed_utc"] = e.StatusChangedUtc ?? "",
        ["usb_serial"] = e.UsbSerial ?? "", ["vendor"] = e.Vendor ?? "", ["vid_pid"] = e.VidPid ?? "", ["volume_serial"] = e.VolumeSerial ?? "",
    };

    private static readonly string[] EntryKeys = [.. EntryMap(new SignedMediaEntry()).Keys];
    private static readonly string[] DocKeys = ["entries", "format", "issued_utc", "previous_version_sha256", "station_id", "version"];

    /// <summary>The canonical bytes: keys sorted (ordinal), no insignificant whitespace, UTF-8 without BOM; entries keep their order.</summary>
    public static byte[] Canonical(SignedMediaRegisterDoc doc)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, WriterOptions))
        {
            w.WriteStartObject();
            w.WritePropertyName("entries");
            w.WriteStartArray();
            foreach (var e in doc.Entries)
            {
                w.WriteStartObject();
                foreach (var (k, v) in EntryMap(e))
                    if (v is long n) w.WriteNumber(k, n); else w.WriteString(k, (string)v);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("format", doc.Format ?? "");
            w.WriteString("issued_utc", doc.IssuedUtc ?? "");
            w.WriteString("previous_version_sha256", doc.PreviousVersionSha256 ?? "");
            w.WriteString("station_id", doc.StationId ?? "");
            w.WriteNumber("version", doc.Version);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Reads a register file. Only the canonical form is accepted: the bytes must be exactly what <see cref="Canonical"/> writes for the content (no BOM,
    /// no extra key, no reordering, no whitespace), so what was signed is what is read. Throws <see cref="FormatException"/> with the reason otherwise.
    /// </summary>
    public static SignedMediaRegisterDoc Parse(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) throw new FormatException("fișierul începe cu BOM; forma canonică este UTF-8 fără BOM");
        if (bytes.Length > 16 * 1024 * 1024) throw new FormatException("fișierul este prea mare pentru un registru de medii");
        SignedMediaRegisterDoc doc;
        try
        {
            using var j = JsonDocument.Parse(bytes);
            var root = j.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("conținutul nu este un obiect JSON");
            Keys(root, DocKeys, "registru");
            doc = new SignedMediaRegisterDoc
            {
                Format = Str(root, "format"), Version = root.GetProperty("version").GetInt32(), PreviousVersionSha256 = Str(root, "previous_version_sha256"),
                StationId = Str(root, "station_id"), IssuedUtc = Str(root, "issued_utc"),
            };
            if (root.GetProperty("entries").ValueKind != JsonValueKind.Array) throw new FormatException("„entries” nu este o listă");
            foreach (var e in root.GetProperty("entries").EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) throw new FormatException("o intrare nu este un obiect JSON");
                Keys(e, EntryKeys, "intrare");
                doc.Entries.Add(new SignedMediaEntry
                {
                    BitLockerStatus = Str(e, "bitlocker_status"), BitLockerVolumeId = Str(e, "bitlocker_volume_id"), CapacityBytes = e.GetProperty("capacity_bytes").GetInt64(),
                    Classification = Str(e, "classification"), EnrolledUtc = Str(e, "enrolled_utc"), Holder = Str(e, "holder"), InventoryNumber = Str(e, "inventory_number"),
                    Model = Str(e, "model"), Notes = Str(e, "notes"), Status = Str(e, "status"), StatusChangedUtc = Str(e, "status_changed_utc"),
                    UsbSerial = Str(e, "usb_serial"), Vendor = Str(e, "vendor"), VidPid = Str(e, "vid_pid"), VolumeSerial = Str(e, "volume_serial"),
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new FormatException("registrul nu poate fi citit: " + ex.Message, ex);
        }
        if (!Canonical(doc).AsSpan().SequenceEqual(bytes)) throw new FormatException("fișierul nu este în forma canonică (chei sortate, fără spații, UTF-8 fără BOM): a fost modificat după emitere");
        return doc;
    }

    private static void Keys(JsonElement o, string[] expected, string what)
    {
        var names = o.EnumerateObject().Select(p => p.Name).ToList();
        var extra = names.Except(expected, StringComparer.Ordinal).ToList();
        var missing = expected.Except(names, StringComparer.Ordinal).ToList();
        if (extra.Count > 0 || missing.Count > 0)
            throw new FormatException($"{what}: " + (extra.Count > 0 ? "chei necunoscute " + string.Join(", ", extra) : "") + (extra.Count > 0 && missing.Count > 0 ? "; " : "") + (missing.Count > 0 ? "chei lipsă " + string.Join(", ", missing) : ""));
    }

    private static string Str(JsonElement o, string name) =>
        o.GetProperty(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : throw new FormatException($"„{name}” nu este text");

    // ---- validation ---------------------------------------------------------------------------------------------------------------------

    /// <summary>What makes the document unusable as a register (empty = valid). Warnings (unencrypted media) are in <see cref="Warnings"/>.</summary>
    public static List<string> Problems(SignedMediaRegisterDoc doc)
    {
        var p = new List<string>();
        if (doc.Format != FormatId) p.Add($"formatul „{doc.Format}” nu este {FormatId}");
        if (doc.Version < 1) p.Add("versiunea trebuie să fie cel puțin 1");
        if (doc.Version == 1 && doc.PreviousVersionSha256.Length > 0) p.Add("versiunea 1 nu are versiune anterioară");
        if (doc.Version > 1 && !(doc.PreviousVersionSha256.Length == 64 && doc.PreviousVersionSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            p.Add("amprenta versiunii anterioare lipsește sau nu are forma SHA-256 (64 de caractere hexazecimale mici)");
        if (doc.StationId.Trim().Length == 0) p.Add("stația care a emis registrul nu este numită");
        if (doc.Issued is null) p.Add($"data emiterii „{doc.IssuedUtc}” nu are forma yyyy-MM-ddTHH:mm:ssZ");
        var inv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < doc.Entries.Count; i++)
        {
            var e = doc.Entries[i]; var at = $"intrarea {i + 1}";
            if (e.InventoryNumber.Trim().Length == 0) p.Add($"{at}: numărul de inventar lipsește");
            else if (!inv.Add(e.InventoryNumber.Trim())) p.Add($"{at}: numărul de inventar „{e.InventoryNumber}” apare de mai multe ori");
            if (MediaRegister.NormalizeSerial(e.UsbSerial).Length == 0) p.Add($"{at}: seria USB lipsește (mediul nu poate fi recunoscut)");
            if (e.VidPid.Length > 0 && RegisterRules.NormalizeVidPid(e.VidPid) is null) p.Add($"{at}: VID/PID „{e.VidPid}” nu are forma 0951:1666");
            if (e.CapacityBytes < 0) p.Add($"{at}: capacitatea nu poate fi negativă");
            if (!Statuses.Contains(e.Status)) p.Add($"{at}: starea „{e.Status}” nu este {string.Join(" | ", Statuses)}");
            else if (e.Status != StatusActive && e.StatusChanged is null) p.Add($"{at}: mediul {e.Status} nu are data schimbării stării");
            if (e.Classification.Trim().Length > 0 && !SecrecyLevels.TryParse(e.Classification, out _)) p.Add($"{at}: nivelul de clasificare „{e.Classification}” nu este cunoscut");
            if (e.Holder.Trim().Length == 0) p.Add($"{at}: deținătorul lipsește (HG 585/2002 art. 294)");
        }
        // two entries with the same serial are allowed only when at most one of them is active (the same medium re-enrolled after retirement)
        foreach (var g in doc.Entries.Where(e => e.IsActive).GroupBy(e => MediaRegister.NormalizeSerial(e.UsbSerial)).Where(g => g.Key.Length > 0 && g.Count() > 1))
            p.Add($"seria „{g.Key}” apare la mai multe medii active ({string.Join(", ", g.Select(e => e.InventoryNumber))})");
        return p;
    }

    public static List<string> Warnings(SignedMediaRegisterDoc doc) =>
        doc.Entries.Where(e => e.IsActive && e.BitLockerStatus != "criptat")
           .Select(e => $"{e.InventoryNumber} ({e.UsbSerial}): {(e.BitLockerStatus == "necriptat" ? "NU este criptat" : "starea criptării nu este cunoscută")}; se recomandă BitLocker To Go").ToList();

    /// <summary>
    /// The next version on the transfer station: the given entries, version = previous + 1 and the previous version's hash (version 1 when there is none).
    /// </summary>
    public static SignedMediaRegisterDoc NextVersion(SignedMediaRegisterDoc? previous, string? previousSha256, IEnumerable<SignedMediaEntry> entries, string stationId, DateTimeOffset nowUtc) => new()
    {
        Version = previous is null ? 1 : previous.Version + 1,
        PreviousVersionSha256 = previous is null ? "" : (previousSha256 ?? "").ToLowerInvariant(),
        StationId = stationId, IssuedUtc = FormatUtc(nowUtc), Entries = entries.Select(e => e.Copy()).ToList(),
    };
}
