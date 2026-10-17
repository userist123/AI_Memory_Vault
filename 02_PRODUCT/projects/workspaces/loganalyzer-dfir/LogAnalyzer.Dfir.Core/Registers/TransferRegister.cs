using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Registers;

public enum TransferDirection { In, Out }

/// <summary>
/// One data transfer on a storage medium (owner request 2026-10-11, model: the organisation's "registrul de evidență a transferurilor de date pe medii
/// de stocare"): what left or entered the station, on which registered medium, with the files' SHA-256, who carried it and who witnessed it
/// (four-eyes). All cells are text as typed; <see cref="TransferRegister.ValidateRow"/> checks them.
/// </summary>
public sealed class TransferRow
{
    public string RegistrationNumber { get; set; } = "";
    public string DateTime { get; set; } = "";
    public string Classification { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceStation { get; set; } = "";
    public string Destination { get; set; } = "";
    public string DestinationStation { get; set; } = "";
    public string Person { get; set; } = "";
    public string PersonFunction { get; set; } = "";
    public string MediumType { get; set; } = "";
    public string MediumSerial { get; set; } = "";
    public string MediumRegistration { get; set; } = "";
    public string Content { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Witness { get; set; } = "";
    public string Approver { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>How many copies / exits this entry covers ("" = 1).</summary>
    public string Copies { get; set; } = "";

    [JsonIgnore] public TransferDirection? DirectionValue => TransferRegister.ParseDirection(Direction);
    [JsonIgnore] public SecrecyLevel? Level => SecrecyLevels.TryParse(Classification, out var l) ? l : null;
}

/// <summary>
/// The data transfer register: the application's own record of every transfer on a medium, kept like the other registers (JSON with an audit hash
/// chain, never applied to the system). Numbering is automatic and gap-free per year (<see cref="NextNumber"/>).
/// </summary>
public sealed class TransferRegister : IRegisterData
{
    public const string CurrentSchemaVersion = "1.0";
    public const string DefaultFileName = "transfers.json";
    public static readonly string[] ColumnNames =
        ["RegistrationNumber", "DateTime", "Classification", "Direction", "Source", "SourceStation", "Destination", "DestinationStation", "Person", "PersonFunction",
         "MediumType", "MediumSerial", "MediumRegistration", "Content", "Sha256", "Witness", "Approver", "Notes", "Copies"];

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public List<TransferRow> Rows { get; set; } = [];

    [JsonIgnore] public string Kind => "transfers";
    [JsonIgnore] public string Title => "Registru transferuri";
    [JsonIgnore] public string FileName => DefaultFileName;
    [JsonIgnore] public IReadOnlyList<string> Columns => ColumnNames;
    [JsonIgnore] public bool IsDefined => Rows.Count > 0;

    public List<string[]> Cells() => Rows.Select(r => new[]
    {
        r.RegistrationNumber, r.DateTime, r.Classification, r.Direction, r.Source, r.SourceStation, r.Destination, r.DestinationStation, r.Person, r.PersonFunction,
        r.MediumType, r.MediumSerial, r.MediumRegistration, r.Content, r.Sha256, r.Witness, r.Approver, r.Notes, r.Copies,
    }).ToList();

    public void SetCells(IEnumerable<IReadOnlyList<string>> rows) =>
        Rows = rows.Select(r => Enumerable.Range(0, ColumnNames.Length).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray())
                   .Select(c => new TransferRow
                   {
                       RegistrationNumber = c[0], DateTime = c[1], Classification = c[2], Direction = c[3], Source = c[4], SourceStation = c[5], Destination = c[6],
                       DestinationStation = c[7], Person = c[8], PersonFunction = c[9], MediumType = c[10], MediumSerial = c[11], MediumRegistration = c[12],
                       Content = c[13], Sha256 = c[14], Witness = c[15], Approver = c[16], Notes = c[17], Copies = c[18],
                   }).ToList();

    public static TransferDirection? ParseDirection(string s) => s.Trim().ToLowerInvariant() switch
    {
        "intrare" or "in" or "intrat" => TransferDirection.In,
        "iesire" or "ieșire" or "out" or "iesit" or "ieșit" => TransferDirection.Out,
        _ => null,
    };

    public static string DirectionText(TransferDirection d) => d == TransferDirection.In ? "intrare" : "ieșire";

    public static bool TryDateTime(string s, out DateTime value) =>
        System.DateTime.TryParseExact(s.Trim(), ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static readonly Regex Hex64 = new("^[0-9A-Fa-f]{64}$", RegexOptions.Compiled);

    /// <summary>"<paramref name="prefix"/>-2026-0007": the next number of the year, one more than the highest already used (gaps are not refilled).</summary>
    public string NextNumber(string prefix, int year)
    {
        prefix = string.IsNullOrWhiteSpace(prefix) ? "TR" : prefix.Trim();
        var rx = new Regex($"^{Regex.Escape(prefix)}-{year}-(\\d+)$", RegexOptions.IgnoreCase);
        var max = Rows.Select(r => rx.Match(r.RegistrationNumber.Trim())).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        return $"{prefix}-{year}-{max + 1:0000}";
    }

    public List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> c, IReadOnlyList<string[]> previousRows)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Warn(string s) => m.Add((s, false));
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);

        if (Empty(c[0])) Err("numărul de înregistrare lipsește");
        else if (previousRows.Any(p => p[0].Trim().Equals(c[0].Trim(), StringComparison.OrdinalIgnoreCase))) Err($"numărul de înregistrare „{c[0]}” apare de mai multe ori");
        if (Empty(c[1])) Err("data și ora lipsesc");
        else if (!TryDateTime(c[1], out _)) Err($"data „{c[1]}” nu este în formatul yyyy-MM-dd HH:mm");
        if (!Empty(c[2]) && !SecrecyLevels.IsUnmarked(c[2]))
        {
            if (SecrecyLevels.IsUnclassifiedMarking(c[2])) Err($"„{c[2]}” nu este un nivel de clasificare; lăsați câmpul gol = nemarcat");
            else if (!SecrecyLevels.TryParse(c[2], out _)) Err($"nivelul de clasificare „{c[2]}” nu este cunoscut (NATO, UE sau nivel național)");
        }
        if (ParseDirection(c[3]) is null) Err($"direcția „{c[3]}” nu este intrare | ieșire");
        if (Empty(c[4])) Err("sursa lipsește");
        if (Empty(c[6])) Err("destinația lipsește");
        if (Empty(c[8])) Err("persoana care face transferul lipsește");
        if (Empty(c[11]) && Empty(c[12])) Err("mediul de stocare nu este identificat (serie sau număr din registrul de medii)");
        if (Empty(c[13])) Err("conținutul transferat lipsește");
        var hashes = c[14].Split([';', ',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hashes.Length == 0) Err("amprenta SHA-256 a conținutului lipsește");
        else if (hashes.Any(h => !Hex64.IsMatch(h))) Err("o amprentă SHA-256 nu are 64 de caractere hexazecimale");
        if (Empty(c[15])) Err("martorul (regula celor patru ochi) lipsește");
        else if (!Empty(c[8]) && c[15].Trim().Equals(c[8].Trim(), StringComparison.OrdinalIgnoreCase)) Err("martorul nu poate fi aceeași persoană care face transferul (regula celor patru ochi)");
        if (Empty(c[16])) Warn("aprobarea șefului structurii nu este completată; fără ea transferul nu justifică la control ieșirea fișierelor clasificate");
        if (c.Count > 18 && !Empty(c[18]) && !(int.TryParse(c[18].Trim(), System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies) && copies is > 0 and <= 1000))
            Err($"numărul de copii „{c[18]}” nu este un număr între 1 și 1000");
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

/// <summary>A transfer checked against the media register and the station: what a controller must be told about it.</summary>
public sealed record TransferFinding(string RegistrationNumber, string Message, bool IsError);

public static class TransferChecks
{
    /// <summary>
    /// Cross-checks: the medium must be in the media register, active and valid on the transfer date; the content's level may not exceed the medium's;
    /// a medium seen on the station is matched by serial. Pure; nothing is written.
    /// </summary>
    public static List<TransferFinding> Check(TransferRegister transfers, MediaRegister? media)
    {
        var r = new List<TransferFinding>();
        foreach (var t in transfers.Rows)
        {
            var serial = MediaRegister.NormalizeSerial(t.MediumSerial);
            var medium = media?.Rows.FirstOrDefault(m => (serial.Length > 0 && MediaRegister.NormalizeSerial(m.Serial) == serial) ||
                                                         (t.MediumRegistration.Length > 0 && m.RegistrationNumber.Trim().Equals(t.MediumRegistration.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (medium is null) { r.Add(new(t.RegistrationNumber, "mediul folosit NU este în registrul de medii", true)); continue; }
            if (medium.StatusValue is MediaStatus.Withdrawn or MediaStatus.Destroyed) r.Add(new(t.RegistrationNumber, $"mediul {medium.RegistrationNumber} este marcat {medium.Status} în registrul de medii", true));
            if (TransferRegister.TryDateTime(t.DateTime, out var when))
            {
                var day = DateOnly.FromDateTime(when);
                if (medium.From is { } f && day < f) r.Add(new(t.RegistrationNumber, $"mediul {medium.RegistrationNumber} nu era încă valabil la data transferului", true));
                if (medium.To is { } to && day > to) r.Add(new(t.RegistrationNumber, $"valabilitatea mediului {medium.RegistrationNumber} expirase la data transferului", true));
            }
            if (t.Level is { } content && medium.Level is { } ml && content.Rank > ml.Rank)
                r.Add(new(t.RegistrationNumber, $"conținutul ({content.Name}) are nivel mai mare decât mediul ({ml.Name})", true));
            if (t.Level is not null && medium.Level is null) r.Add(new(t.RegistrationNumber, $"conținut clasificat pe un mediu nemarcat ({medium.RegistrationNumber})", true));
        }
        return r;
    }
}

/// <summary>
/// Append-only store for transfers: any signed-in operator may ADD a transfer (unlike the other registers, which only the administrator edits), but
/// existing rows are never changed through this path. Every addition goes to the register audit chain with who/when and the before/after hashes.
/// </summary>
public static class TransferStore
{
    public static string DefaultPath => RegisterStore.DefaultPath<TransferRegister>();

    public static TransferRegister Load(string? path, out List<RegisterIssue> issues)
    {
        var r = RegisterStore.Load<TransferRegister>(path ?? DefaultPath);
        issues = r.Issues;
        return r.Register ?? new TransferRegister();
    }

    /// <summary>Validates the whole register with the new row; if anything is invalid nothing is written and the errors are returned.</summary>
    public static List<RegisterIssue> Append(TransferRow row, string? path, out string sha256)
    {
        sha256 = "";
        path ??= DefaultPath;
        if (LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired && LogAnalyzer.Dfir.Auth.OperatorIdentity.Current is null)
            return [new RegisterIssue("Registru transferuri", 0, "autentificați-vă în aplicație ca să înregistrați un transfer")];
        var reg = Load(path, out var loadIssues);
        if (loadIssues.Any(i => i.IsError)) return loadIssues;
        var beforeSha = File.Exists(path) ? LogAnalyzer.Dfir.IO.Hashing.Sha256File(path) : "";
        var before = reg.Cells();
        reg.Rows.Add(row);
        var issues = reg.Validate();
        if (issues.Any(i => i.IsError)) return issues;
        reg.SchemaVersion = TransferRegister.CurrentSchemaVersion;
        reg.UpdatedUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        LogAnalyzer.Dfir.IO.Json.Write(path, reg);
        sha256 = LogAnalyzer.Dfir.IO.Hashing.Sha256File(path);
        new LogAnalyzer.Dfir.Case.HashChain(RegisterStore.AuditPathFor(path)).Append(new RegisterAuditEntry(reg.Kind, "transfer.add",
            LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified, LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoSource, DateTimeOffset.UtcNow, beforeSha, sha256, before, reg.Cells()));
        return issues;
    }
}
