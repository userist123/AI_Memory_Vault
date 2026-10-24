using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>One offline update applied to the station (control research P5): what, when, from where, who brought the medium. Cells are text as typed.</summary>
public sealed class UpdateRow
{
    public string Date { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Package { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Origin { get; set; } = "";
    public string BroughtBy { get; set; } = "";
    public string Medium { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>The Windows update numbers (KB1234567) written in the package cell.</summary>
    [JsonIgnore] public IReadOnlyList<string> KbNumbers => UpdateRegister.KbIn(Package);
}

/// <summary>
/// The register of offline updates (control research P5, manual check M05): every Windows, antivirus or application update brought to the isolated
/// station on a medium. Kept like the other registers (JSON with an audit hash chain, never applied to the system). The station control compares the
/// Windows updates installed in the control period with it.
/// </summary>
public sealed partial class UpdateRegister : IRegisterData
{
    public const string CurrentSchemaVersion = "1.0";
    public const string DefaultFileName = "updates.json";
    public static readonly string[] ColumnNames = ["Date", "Kind", "Package", "Sha256", "Origin", "BroughtBy", "Medium", "Notes"];

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public List<UpdateRow> Rows { get; set; } = [];

    [JsonIgnore] public string Kind => "updates";
    [JsonIgnore] public string Title => "Registrul actualizărilor offline";
    [JsonIgnore] public string FileName => DefaultFileName;
    [JsonIgnore] public IReadOnlyList<string> Columns => ColumnNames;
    [JsonIgnore] public bool IsDefined => Rows.Count > 0;

    public List<string[]> Cells() => Rows.Select(r => new[] { r.Date, r.Kind, r.Package, r.Sha256, r.Origin, r.BroughtBy, r.Medium, r.Notes }).ToList();

    public void SetCells(IEnumerable<IReadOnlyList<string>> rows) =>
        Rows = rows.Select(r => Enumerable.Range(0, ColumnNames.Length).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray())
                   .Select(c => new UpdateRow { Date = c[0], Kind = c[1], Package = c[2], Sha256 = c[3], Origin = c[4], BroughtBy = c[5], Medium = c[6], Notes = c[7] })
                   .ToList();

    /// <summary>windows | antivirus | aplicatie (Romanian and English words accepted); null when not recognised.</summary>
    public static string? NormalizeKind(string s) => s.Trim().ToLowerInvariant() switch
    {
        "windows" or "sistem" or "os" => "windows",
        "antivirus" or "defender" or "semnaturi" or "semnături" => "antivirus",
        "aplicatie" or "aplicație" or "application" or "program" => "aplicatie",
        _ => null,
    };

    [GeneratedRegex(@"\bKB\d{6,8}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex KbRegex();
    [GeneratedRegex("^[0-9A-Fa-f]{64}$")] private static partial Regex Hex64();

    public static IReadOnlyList<string> KbIn(string text) => KbRegex().Matches(text ?? "").Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();

    public List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> c, IReadOnlyList<string[]> previousRows)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Warn(string s) => m.Add((s, false));
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);
        if (Empty(c[0])) Err("data aplicării lipsește");
        else if (!RegisterRules.TryDate(c[0], out _)) Err($"data „{c[0]}” nu este în formatul yyyy-MM-dd");
        if (NormalizeKind(c[1]) is null) Err($"felul „{c[1]}” nu este windows | antivirus | aplicatie");
        if (Empty(c[2])) Err("pachetul (numele sau numărul KB) lipsește");
        else if (NormalizeKind(c[1]) == "windows" && KbIn(c[2]).Count == 0) Warn("pentru o actualizare Windows scrieți numărul KB (de exemplu KB5031234), ca aplicația să o poată regăsi pe stație");
        if (!Empty(c[3]) && !Hex64().IsMatch(c[3].Trim())) Err("amprenta SHA-256 trebuie să aibă 64 de caractere hexazecimale");
        if (Empty(c[3])) Warn("amprenta SHA-256 a pachetului nu este completată");
        if (Empty(c[5])) Err("persoana care a adus suportul lipsește");
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

    /// <summary>
    /// "Preia de pe stație": adds one row for each installed Windows update (KB number) that the register does not have yet, with the date and the
    /// package filled in; the person completes what the station cannot know (origin, who brought the medium, the medium, the hash). Existing rows
    /// are never changed. Returns (added, already present).
    /// </summary>
    public (int Added, int AlreadyPresent) MergeInstalled(IEnumerable<(string HotFixId, DateTime? InstalledOn)> installed)
    {
        var known = Rows.SelectMany(r => r.KbNumbers).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, present = 0;
        foreach (var (id, on) in installed.Where(u => KbIn(u.HotFixId).Count > 0).DistinctBy(u => u.HotFixId.Trim().ToUpperInvariant()).OrderBy(u => u.InstalledOn))
        {
            var kb = KbIn(id)[0];
            if (!known.Add(kb)) { present++; continue; }
            Rows.Add(new UpdateRow { Date = on?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "", Kind = "windows", Package = kb, Notes = "preluat de pe stație; completați proveniența și cine a adus suportul" });
            added++;
        }
        return (added, present);
    }
}
