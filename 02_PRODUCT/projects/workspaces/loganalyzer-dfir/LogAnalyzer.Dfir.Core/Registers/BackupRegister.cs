using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// One backup copy of the station's data or system (planner #10 item 3; ORNISS model, Anexa 21 "registrul copiilor de siguranță"): when it was made, of what, on which medium,
/// by whom, where the copy is kept, and (optionally) when and by whom a restore from it was tested. Cells are text as typed.
/// </summary>
public sealed class BackupRow
{
    public string Date { get; set; } = "";
    public string Station { get; set; } = "";
    public string What { get; set; } = "";
    public string Medium { get; set; } = "";
    public string MediumNumber { get; set; } = "";
    public string MadeBy { get; set; } = "";
    public string StoredAt { get; set; } = "";
    public string RestoreTestDate { get; set; } = "";
    public string RestoreTestBy { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>
/// The register of backup copies (control check S09). Kept like the other registers (JSON with an audit hash chain, never applied to the system). An empty register is
/// "registru nedefinit", never "conform". The accepted age of the newest copy and of the newest restore test come from the procedure profile.
/// </summary>
public sealed class BackupRegister : IRegisterData
{
    public const string CurrentSchemaVersion = "1.0";
    public const string DefaultFileName = "backups.json";
    public static readonly string[] ColumnNames = ["Date", "Station", "What", "Medium", "MediumNumber", "MadeBy", "StoredAt", "RestoreTestDate", "RestoreTestBy", "Notes"];
    /// <summary>A date typed in the register may be at most this many days after today (local date against clock offsets).</summary>
    public const int FutureToleranceDays = 1;

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public List<BackupRow> Rows { get; set; } = [];

    [JsonIgnore] public string Kind => "backups";
    [JsonIgnore] public string Title => "Registrul copiilor de siguranță";
    [JsonIgnore] public string FileName => DefaultFileName;
    [JsonIgnore] public IReadOnlyList<string> Columns => ColumnNames;
    [JsonIgnore] public bool IsDefined => Rows.Count > 0;

    public List<string[]> Cells() => Rows.Select(r => new[] { r.Date, r.Station, r.What, r.Medium, r.MediumNumber, r.MadeBy, r.StoredAt, r.RestoreTestDate, r.RestoreTestBy, r.Notes }).ToList();

    public void SetCells(IEnumerable<IReadOnlyList<string>> rows) =>
        Rows = rows.Select(r => Enumerable.Range(0, ColumnNames.Length).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray())
                   .Select(c => new BackupRow { Date = c[0], Station = c[1], What = c[2], Medium = c[3], MediumNumber = c[4], MadeBy = c[5], StoredAt = c[6],
                                                RestoreTestDate = c[7], RestoreTestBy = c[8], Notes = c[9] })
                   .ToList();

    public List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> c, IReadOnlyList<string[]> previousRows)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Warn(string s) => m.Add((s, false));
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);
        var latest = DateOnly.FromDateTime(DateTime.Now).AddDays(FutureToleranceDays);
        DateOnly date = default; bool dateOk = false;
        if (Empty(c[0])) Err("data copiei lipsește");
        else if (!RegisterRules.TryDate(c[0], out date)) Err($"data „{c[0]}” nu este în formatul yyyy-MM-dd");
        else
        {
            dateOk = true;
            if (date > latest) Err($"data copiei „{c[0]}” este în viitor");
        }
        if (Empty(c[1])) Warn("stația nu este completată");
        if (Empty(c[2])) Err("ce s-a copiat lipsește (date sau sistem)");
        if (Empty(c[3])) Warn("suportul copiei nu este completat");
        if (Empty(c[5])) Err("persoana care a făcut copia lipsește");
        if (Empty(c[6])) Warn("locul de păstrare a copiei nu este completat");
        bool hasTestDate = !Empty(c[7]), hasTestBy = !Empty(c[8]);
        if (hasTestDate)
        {
            if (!RegisterRules.TryDate(c[7], out var testDate)) Err($"data testului de restaurare „{c[7]}” nu este în formatul yyyy-MM-dd");
            else
            {
                if (dateOk && testDate < date) Err("testul de restaurare nu poate fi înaintea copiei");
                if (testDate > latest) Err($"data testului de restaurare „{c[7]}” este în viitor");
            }
        }
        if (hasTestDate && !hasTestBy) Warn("testul de restaurare are dată, dar nu are persoana care l-a făcut");
        if (hasTestBy && !hasTestDate) Warn("testul de restaurare are persoană, dar nu are data");
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
