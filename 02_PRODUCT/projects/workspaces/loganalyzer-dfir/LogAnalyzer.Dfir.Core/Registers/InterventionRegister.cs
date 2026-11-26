using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// One intervention on the station's hardware or software (planner #9 item 7; HG 585 art. 277 "orice modificare a SIC de cel puțin 2 persoane", art. 329 alin. 2
/// verificarea dispozitivelor de securitate după reparație; ORNISS model, Anexa 14): who did what, who witnessed it, and whether the security devices were checked
/// afterwards. Cells are text as typed.
/// </summary>
public sealed class InterventionRow
{
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Station { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Components { get; set; } = "";
    public string Description { get; set; } = "";
    public string Performer { get; set; } = "";
    public string Witness { get; set; } = "";
    public string AuthorizationRef { get; set; } = "";
    public string PostCheckDate { get; set; } = "";
    public string PostCheckBy { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>
/// The register of hardware / software interventions with a witness (control check S08). Kept like the other registers (JSON with an audit hash chain, never applied
/// to the system). An empty register is "registru nedefinit", never "conform".
/// </summary>
public sealed partial class InterventionRegister : IRegisterData
{
    public const string CurrentSchemaVersion = "1.0";
    public const string DefaultFileName = "interventions.json";
    public static readonly string[] ColumnNames = ["Date", "Time", "Station", "Kind", "Components", "Description", "Performer", "Witness", "AuthorizationRef", "PostCheckDate", "PostCheckBy", "Notes"];
    /// <summary>A date typed in the register may be at most this many days after today (local date against clock offsets).</summary>
    public const int FutureToleranceDays = 1;
    /// <summary>After this many days a hardware or repair intervention without the post-intervention check is NECONFORM (check S08); inside it, DE VERIFICAT.</summary>
    public const int PostCheckGraceDays = 7;

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public List<InterventionRow> Rows { get; set; } = [];

    [JsonIgnore] public string Kind => "interventions";
    [JsonIgnore] public string Title => "Registrul intervențiilor hardware și software";
    [JsonIgnore] public string FileName => DefaultFileName;
    [JsonIgnore] public IReadOnlyList<string> Columns => ColumnNames;
    [JsonIgnore] public bool IsDefined => Rows.Count > 0;

    public List<string[]> Cells() => Rows.Select(r => new[] { r.Date, r.Time, r.Station, r.Kind, r.Components, r.Description, r.Performer, r.Witness, r.AuthorizationRef, r.PostCheckDate, r.PostCheckBy, r.Notes }).ToList();

    public void SetCells(IEnumerable<IReadOnlyList<string>> rows) =>
        Rows = rows.Select(r => Enumerable.Range(0, ColumnNames.Length).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray())
                   .Select(c => new InterventionRow { Date = c[0], Time = c[1], Station = c[2], Kind = c[3], Components = c[4], Description = c[5], Performer = c[6], Witness = c[7],
                                                      AuthorizationRef = c[8], PostCheckDate = c[9], PostCheckBy = c[10], Notes = c[11] })
                   .ToList();

    /// <summary>hardware | software | configurare | reparatie (Romanian and English words accepted); null when not recognised.</summary>
    public static string? NormalizeKind(string s) => s.Trim().ToLowerInvariant() switch
    {
        "hardware" or "hw" or "echipament" => "hardware",
        "software" or "sw" or "program" => "software",
        "configurare" or "configuration" or "config" => "configurare",
        "reparatie" or "reparație" or "repair" => "reparatie",
        _ => null,
    };

    /// <summary>Hardware and repair interventions must be followed by the check of the security devices (HG 585 art. 329 alin. 2).</summary>
    public static bool NeedsPostCheck(string? normalizedKind) => normalizedKind is "hardware" or "reparatie";

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex Spaces();
    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.CultureInvariant)] private static partial Regex TimeRx();

    /// <summary>True when the witness is the performer: same name ignoring case and spacing ("DOMENIU\user" and "user" count as the same person).</summary>
    public static bool SamePerson(string? performer, string? witness)
    {
        static string N(string? s) => Spaces().Replace((s ?? "").Trim(), " ");
        return RegisterRules.SameAccount(N(performer), N(witness));
    }

    /// <summary>The row has a recorded post-intervention check (both the date and who did it).</summary>
    public static bool HasPostCheck(InterventionRow r) => !string.IsNullOrWhiteSpace(r.PostCheckDate) && !string.IsNullOrWhiteSpace(r.PostCheckBy);

    public List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> c, IReadOnlyList<string[]> previousRows)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Warn(string s) => m.Add((s, false));
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);
        var latest = DateOnly.FromDateTime(DateTime.Now).AddDays(FutureToleranceDays);
        DateOnly date = default; bool dateOk = false;
        if (Empty(c[0])) Err("data intervenției lipsește");
        else if (!RegisterRules.TryDate(c[0], out date)) Err($"data „{c[0]}” nu este în formatul yyyy-MM-dd");
        else
        {
            dateOk = true;
            if (date > latest) Err($"data intervenției „{c[0]}” este în viitor");
        }
        if (!Empty(c[1]) && !TimeRx().IsMatch(c[1].Trim())) Err($"ora „{c[1]}” nu este în formatul HH:mm");
        if (Empty(c[2])) Warn("stația nu este completată");
        if (NormalizeKind(c[3]) is null) Err($"felul „{c[3]}” nu este hardware | software | configurare | reparatie");
        if (Empty(c[4])) Warn("componentele sau programele afectate nu sunt completate");
        if (Empty(c[5])) Warn("descrierea intervenției nu este completată");
        if (Empty(c[6])) Err("executantul lipsește");
        if (Empty(c[7])) Err("martorul lipsește (orice modificare a sistemului se face de cel puțin 2 persoane)");
        else if (!Empty(c[6]) && SamePerson(c[6], c[7])) Err("martorul trebuie să fie altă persoană decât executantul");
        bool hasCheckDate = !Empty(c[9]), hasCheckBy = !Empty(c[10]);
        if (hasCheckDate)
        {
            if (!RegisterRules.TryDate(c[9], out var checkDate)) Err($"data verificării „{c[9]}” nu este în formatul yyyy-MM-dd");
            else
            {
                if (dateOk && checkDate < date) Err("verificarea după intervenție nu poate fi înaintea intervenției");
                if (checkDate > latest) Err($"data verificării „{c[9]}” este în viitor");
            }
        }
        if (hasCheckDate && !hasCheckBy) Err("verificarea după intervenție are dată, dar nu are persoana care a efectuat-o");
        if (hasCheckBy && !hasCheckDate) Err("verificarea după intervenție are persoană, dar nu are data");
        if (NeedsPostCheck(NormalizeKind(c[3])) && !hasCheckDate && !hasCheckBy)
            Warn($"intervenția de tip hardware sau reparație trebuie urmată de verificarea dispozitivelor de securitate; fără ea, după {PostCheckGraceDays} zile controlul o arată neconformă");
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
}
