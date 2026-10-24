using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Registers;

public enum MediaStatus { Active, Withdrawn, Destroyed }

/// <summary>One registered medium (owner decision 16). All cells are text, exactly as the operator typed them; <see cref="MediaRegister.ValidateRow"/> checks them.</summary>
public sealed class MediaRow
{
    public string RegistrationNumber { get; set; } = "";
    public string Serial { get; set; } = "";
    public string VidPid { get; set; } = "";
    public string Type { get; set; } = "";
    public string Classification { get; set; } = "";
    public string AssignedUser { get; set; } = "";
    public string Zone { get; set; } = "";
    public string ValidFrom { get; set; } = "";
    public string ValidTo { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";

    [JsonIgnore] public MediaStatus? StatusValue => MediaRegister.ParseStatus(Status);
    [JsonIgnore] public SecrecyLevel? Level => SecrecyLevels.TryParse(Classification, out var l) ? l : null;
    [JsonIgnore] public DateOnly? From => RegisterRules.TryDate(ValidFrom, out var d) ? d : null;
    [JsonIgnore] public DateOnly? To => RegisterRules.TryDate(ValidTo, out var d) ? d : null;
    [JsonIgnore] public bool IsOptical => MediaRegister.NormalizeType(Type) is "CD" or "DVD";
}

/// <summary>
/// The authorised media register (owner decision 16): the application keeps its own media database. The administrator enters the registration number
/// plus serial, VID/PID, type (USB/HDD/SSD/CD/DVD/other), classification, assigned user, zone, validity and status. Observed media are compared with it;
/// nothing here is ever written to the system.
/// </summary>
public sealed class MediaRegister : IRegisterData
{
    public const string CurrentSchemaVersion = "1.0";
    public const string DefaultFileName = "media.json";
    public static readonly string[] ColumnNames = ["RegistrationNumber", "Serial", "VidPid", "Type", "Classification", "AssignedUser", "Zone", "ValidFrom", "ValidTo", "Status", "Notes"];
    public static readonly string[] Types = ["USB", "HDD", "SSD", "CD", "DVD", "other"];

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public List<MediaRow> Rows { get; set; } = [];

    [JsonIgnore] public string Kind => "media";
    [JsonIgnore] public string Title => "Registru medii";
    [JsonIgnore] public string FileName => DefaultFileName;
    [JsonIgnore] public IReadOnlyList<string> Columns => ColumnNames;
    [JsonIgnore] public bool IsDefined => Rows.Count > 0;

    public List<string[]> Cells() => Rows.Select(r => new[] { r.RegistrationNumber, r.Serial, r.VidPid, r.Type, r.Classification, r.AssignedUser, r.Zone, r.ValidFrom, r.ValidTo, r.Status, r.Notes }).ToList();

    public void SetCells(IEnumerable<IReadOnlyList<string>> rows) =>
        Rows = rows.Select(r => Enumerable.Range(0, ColumnNames.Length).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray())
                   .Select(c => new MediaRow { RegistrationNumber = c[0], Serial = c[1], VidPid = c[2], Type = c[3], Classification = c[4], AssignedUser = c[5], Zone = c[6], ValidFrom = c[7], ValidTo = c[8], Status = c[9], Notes = c[10] })
                   .ToList();

    /// <summary>"usb" -> "USB"; "other"/"altul" -> "other"; null = not a type of the register.</summary>
    public static string? NormalizeType(string s) => s.Trim().ToUpperInvariant() switch
    {
        "USB" => "USB", "HDD" => "HDD", "SSD" => "SSD", "CD" => "CD", "DVD" => "DVD", "OTHER" or "ALTUL" or "ALTELE" => "other", _ => null,
    };

    public static MediaStatus? ParseStatus(string s) => s.Trim().ToLowerInvariant() switch
    {
        "active" or "activ" => MediaStatus.Active,
        "withdrawn" or "retras" => MediaStatus.Withdrawn,
        "destroyed" or "distrus" => MediaStatus.Destroyed,
        _ => null,
    };

    public List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> c, IReadOnlyList<string[]> previousRows)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Warn(string s) => m.Add((s, false));
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);
        if (Empty(c[0])) Err("câmpul „număr de înregistrare” lipsește");
        else if (previousRows.Any(p => p[0].Trim().Equals(c[0].Trim(), StringComparison.OrdinalIgnoreCase))) Err($"numărul de înregistrare „{c[0]}” apare de mai multe ori");
        var type = NormalizeType(c[3]);
        if (type is null) Err($"tipul „{c[3]}” nu este {string.Join(" | ", Types)}");
        if (Empty(c[1])) { if (type is "CD" or "DVD") Warn("seria lipsește: mediul optic nu poate fi regăsit după serie în observații (rămâne „necunoscut”)"); else Err("seria lipsește"); }
        else if (previousRows.Any(p => p[1].Trim().Equals(c[1].Trim(), StringComparison.OrdinalIgnoreCase))) Warn($"seria „{c[1]}” apare la mai multe medii");
        if (!Empty(c[2]) && RegisterRules.NormalizeVidPid(c[2]) is null) Err($"VID/PID „{c[2]}” nu este de forma 0951:1666 sau VID_0951&PID_1666");
        if (!Empty(c[4]) && !SecrecyLevels.IsUnmarked(c[4]))
        {
            if (SecrecyLevels.IsUnclassifiedMarking(c[4])) Err($"„{c[4]}” nu este un nivel de clasificare: nu există nivel neclasificat (decizia 21); lăsați câmpul gol = nemarcat");
            else if (!SecrecyLevels.TryParse(c[4], out _)) Err($"nivelul de clasificare „{c[4]}” nu este cunoscut (NATO, UE sau nivel național; decizia 21)");
        }
        DateOnly from = default, to = default;
        bool hasFrom = !Empty(c[7]), hasTo = !Empty(c[8]);
        bool okFrom = !hasFrom || RegisterRules.TryDate(c[7], out from), okTo = !hasTo || RegisterRules.TryDate(c[8], out to);
        if (!okFrom) Err($"data „{c[7]}” nu este în formatul yyyy-MM-dd (valabil de la)");
        if (!okTo) Err($"data „{c[8]}” nu este în formatul yyyy-MM-dd (valabil până la)");
        if (hasFrom && hasTo && okFrom && okTo && to < from) Err("sfârșitul valabilității precedă începutul");
        if (ParseStatus(c[9]) is null) Err($"starea „{c[9]}” nu este active | withdrawn | destroyed");
        return m;
    }

    public List<RegisterIssue> Validate()
    {
        var issues = new List<RegisterIssue>();
        var cells = Cells();
        for (int i = 0; i < cells.Count; i++)
            foreach (var (msg, err) in ValidateRow(cells[i], cells.Take(i).ToList())) issues.Add(new RegisterIssue(Title, i + 1, msg, err));
        return issues;
    }

    /// <summary>Normalised serial for matching: trimmed, upper case, no spaces, without the "&amp;0"/"&amp;1" instance suffix USBSTOR appends.</summary>
    public static string NormalizeSerial(string s)
    {
        s = (s ?? "").Trim().Replace(" ", "").ToUpperInvariant();
        return s.EndsWith("&0", StringComparison.Ordinal) || s.EndsWith("&1", StringComparison.Ordinal) ? s[..^2] : s;
    }
}
