using System.Globalization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>
/// Table view of the profile: titles, columns and cells per table. The same adaptor serves the editing grids, CSV import and clipboard
/// paste, and the validation, so a row is checked the same way whichever way it entered.
/// </summary>
public static partial class ProfileTables
{
    public static IReadOnlyList<ProfileTable> All { get; } = Enum.GetValues<ProfileTable>();

    public static string Title(ProfileTable t) => t switch
    {
        ProfileTable.WorkingHours => "Program de lucru",
        ProfileTable.Holidays => "Sărbători legale",
        ProfileTable.Shifts => "Schimburi",
        ProfileTable.ApprovedAccounts => "Conturi aprobate pentru golirea jurnalelor",
        ProfileTable.MaintenanceWindows => "Ferestre de mentenanță",
        ProfileTable.RotationProcedure => "Procedura de rotire a jurnalelor",
        ProfileTable.ApprovedSoftware => "Software aprobat",
        ProfileTable.ExpectedPolicies => "Politică GPO așteptată",
        ProfileTable.Zones => "Zone",
        ProfileTable.TransferChannels => "Canale de transfer aprobate",
        ProfileTable.NetworkDestinations => "Destinații de rețea autorizate",
        _ => t.ToString(),
    };

    public static string Section(ProfileTable t) => t switch
    {
        ProfileTable.WorkingHours or ProfileTable.Holidays or ProfileTable.Shifts => "Program de lucru",
        ProfileTable.ApprovedAccounts or ProfileTable.MaintenanceWindows or ProfileTable.RotationProcedure => "Mentenanța jurnalelor",
        ProfileTable.ApprovedSoftware => "Software aprobat",
        ProfileTable.ExpectedPolicies => "Politică GPO așteptată",
        _ => "Zone și transferuri",
    };

    public static IReadOnlyList<string> Columns(ProfileTable t) => t switch
    {
        ProfileTable.WorkingHours => ["Day", "Start", "End"],
        ProfileTable.Holidays => ["Date", "Name"],
        ProfileTable.Shifts => ["Name", "Start", "End"],
        ProfileTable.ApprovedAccounts => ["Account", "Note"],
        ProfileTable.MaintenanceWindows => ["Kind", "Date", "Day", "Ordinal", "Start", "End", "Note"],
        ProfileTable.RotationProcedure => ["Step", "Note"],
        ProfileTable.ApprovedSoftware => ["Name", "Publisher", "PathPattern", "Sha256"],
        ProfileTable.ExpectedPolicies => ["PolicyPath", "Sha256", "Note"],
        ProfileTable.Zones => ["Name", "Description"],
        ProfileTable.TransferChannels => ["Name", "FromZone", "ToZone", "Medium", "Note"],
        ProfileTable.NetworkDestinations => ["Address", "Zone", "Purpose"],
        _ => [],
    };

    public static int Count(ProcedureProfile p, ProfileTable t) => t switch
    {
        ProfileTable.WorkingHours => p.WorkingHours.Count,
        ProfileTable.Holidays => p.Holidays.Count,
        ProfileTable.Shifts => p.Shifts.Count,
        ProfileTable.ApprovedAccounts => p.ApprovedAccounts.Count,
        ProfileTable.MaintenanceWindows => p.MaintenanceWindows.Count,
        ProfileTable.RotationProcedure => p.RotationProcedure.Count,
        ProfileTable.ApprovedSoftware => p.ApprovedSoftware.Count,
        ProfileTable.ExpectedPolicies => p.ExpectedPolicies.Count,
        ProfileTable.Zones => p.Zones.Count,
        ProfileTable.TransferChannels => p.TransferChannels.Count,
        ProfileTable.NetworkDestinations => p.NetworkDestinations.Count,
        _ => 0,
    };

    public static List<string[]> Cells(ProcedureProfile p, ProfileTable t) => t switch
    {
        ProfileTable.WorkingHours => p.WorkingHours.Select(r => new[] { r.Day, r.Start, r.End }).ToList(),
        ProfileTable.Holidays => p.Holidays.Select(r => new[] { r.Date, r.Name }).ToList(),
        ProfileTable.Shifts => p.Shifts.Select(r => new[] { r.Name, r.Start, r.End }).ToList(),
        ProfileTable.ApprovedAccounts => p.ApprovedAccounts.Select(r => new[] { r.Account, r.Note }).ToList(),
        ProfileTable.MaintenanceWindows => p.MaintenanceWindows.Select(r => new[] { r.Kind, r.Date, r.Day, r.Ordinal, r.Start, r.End, r.Note }).ToList(),
        ProfileTable.RotationProcedure => p.RotationProcedure.Select(r => new[] { r.Step, r.Note }).ToList(),
        ProfileTable.ApprovedSoftware => p.ApprovedSoftware.Select(r => new[] { r.Name, r.Publisher, r.PathPattern, r.Sha256 }).ToList(),
        ProfileTable.ExpectedPolicies => p.ExpectedPolicies.Select(r => new[] { r.PolicyPath, r.Sha256, r.Note }).ToList(),
        ProfileTable.Zones => p.Zones.Select(r => new[] { r.Name, r.Description }).ToList(),
        ProfileTable.TransferChannels => p.TransferChannels.Select(r => new[] { r.Name, r.FromZone, r.ToZone, r.Medium, r.Note }).ToList(),
        ProfileTable.NetworkDestinations => p.NetworkDestinations.Select(r => new[] { r.Address, r.Zone, r.Purpose }).ToList(),
        _ => [],
    };

    /// <summary>Replaces the rows of one table with <paramref name="rows"/> (each row has exactly <see cref="Columns"/> cells; missing cells are empty). Cells are trimmed.</summary>
    public static void SetCells(ProcedureProfile p, ProfileTable t, IEnumerable<IReadOnlyList<string>> rows)
    {
        var data = rows.Select(r => Enumerable.Range(0, Columns(t).Count).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray()).ToList();
        switch (t)
        {
            case ProfileTable.WorkingHours: p.WorkingHours = data.Select(c => new WorkingHoursRow { Day = c[0], Start = c[1], End = c[2] }).ToList(); break;
            case ProfileTable.Holidays: p.Holidays = data.Select(c => new HolidayRow { Date = c[0], Name = c[1] }).ToList(); break;
            case ProfileTable.Shifts: p.Shifts = data.Select(c => new ShiftRow { Name = c[0], Start = c[1], End = c[2] }).ToList(); break;
            case ProfileTable.ApprovedAccounts: p.ApprovedAccounts = data.Select(c => new ApprovedAccountRow { Account = c[0], Note = c[1] }).ToList(); break;
            case ProfileTable.MaintenanceWindows:
                p.MaintenanceWindows = data.Select(c => new MaintenanceWindowRow { Kind = c[0], Date = c[1], Day = c[2], Ordinal = c[3], Start = c[4], End = c[5], Note = c[6] }).ToList(); break;
            case ProfileTable.RotationProcedure: p.RotationProcedure = data.Select(c => new RotationStepRow { Step = c[0], Note = c[1] }).ToList(); break;
            case ProfileTable.ApprovedSoftware: p.ApprovedSoftware = data.Select(c => new ApprovedSoftwareRow { Name = c[0], Publisher = c[1], PathPattern = c[2], Sha256 = c[3] }).ToList(); break;
            case ProfileTable.ExpectedPolicies: p.ExpectedPolicies = data.Select(c => new ExpectedPolicyRow { PolicyPath = c[0], Sha256 = c[1], Note = c[2] }).ToList(); break;
            case ProfileTable.Zones: p.Zones = data.Select(c => new ZoneRow { Name = c[0], Description = c[1] }).ToList(); break;
            case ProfileTable.TransferChannels:
                p.TransferChannels = data.Select(c => new TransferChannelRow { Name = c[0], FromZone = c[1], ToZone = c[2], Medium = c[3], Note = c[4] }).ToList(); break;
            case ProfileTable.NetworkDestinations: p.NetworkDestinations = data.Select(c => new NetworkDestinationRow { Address = c[0], Zone = c[1], Purpose = c[2] }).ToList(); break;
        }
    }

    // ---- parsing helpers shared by validation and by the conversions ----

    private static readonly (string[] Names, DayOfWeek Day)[] DayNames =
    [
        (["mon", "monday", "lu", "luni"], DayOfWeek.Monday),
        (["tue", "tuesday", "ma", "marti", "marți"], DayOfWeek.Tuesday),
        (["wed", "wednesday", "mi", "miercuri"], DayOfWeek.Wednesday),
        (["thu", "thursday", "jo", "joi"], DayOfWeek.Thursday),
        (["fri", "friday", "vi", "vineri"], DayOfWeek.Friday),
        (["sat", "saturday", "sa", "sambata", "sâmbătă"], DayOfWeek.Saturday),
        (["sun", "sunday", "du", "duminica", "duminică"], DayOfWeek.Sunday),
    ];

    public static bool TryParseDay(string s, out DayOfWeek day)
    {
        var k = s.Trim().ToLowerInvariant();
        foreach (var (names, d) in DayNames)
            if (names.Contains(k)) { day = d; return true; }
        day = default; return false;
    }

    public static bool TryParseTime(string s, out TimeOnly t) =>
        TimeOnly.TryParseExact(s.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

    public static bool TryParseDate(string s, out DateOnly d) =>
        DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    /// <summary>Kind of a maintenance window row: once | weekly | monthly (Romanian words accepted).</summary>
    public static string? NormalizeKind(string s) => s.Trim().ToLowerInvariant() switch
    {
        "once" or "unic" or "o dată" or "o data" => "once",
        "weekly" or "saptamanal" or "săptămânal" => "weekly",
        "monthly" or "lunar" => "monthly",
        _ => null,
    };

    /// <summary>1-4 = first..fourth, 0 = last.</summary>
    public static bool TryParseOrdinal(string s, out int ordinal)
    {
        var k = s.Trim().ToLowerInvariant();
        if (k is "last" or "ultima" or "ultimul" or "ultima zi") { ordinal = 0; return true; }
        if (int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) && ordinal is >= 1 and <= 4) return true;
        ordinal = -1; return false;
    }

    public static readonly string[] RotationOrder = ["EXPORT", "HASH", "ARCHIVE", "VERIFY", "CLEAR"];

    [GeneratedRegex("^[0-9a-fA-F]{64}$")] private static partial Regex Sha256Regex();
    public static bool IsSha256(string s) => Sha256Regex().IsMatch(s.Trim());

    // ---- validation ----

    /// <summary>Checks one row (cells in <see cref="Columns"/> order). Returns the messages; empty = valid. <paramref name="knownZones"/> is the zone names defined in the same profile.</summary>
    public static List<(string Message, bool IsError)> ValidateRow(ProfileTable t, IReadOnlyList<string> c, IReadOnlyCollection<string>? knownZones = null)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Time(string name, string v) { if (!TryParseTime(v, out _)) Err($"{name} „{v}” nu este o oră validă (HH:mm)"); }
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);
        switch (t)
        {
            case ProfileTable.WorkingHours:
                if (!TryParseDay(c[0], out _)) Err($"ziua „{c[0]}” nu este recunoscută (Mon..Sun sau Luni..Duminică)");
                Time("începutul", c[1]); Time("sfârșitul", c[2]);
                if (TryParseTime(c[1], out var ws) && TryParseTime(c[2], out var we) && ws == we) Err("începutul și sfârșitul sunt identice");
                break;
            case ProfileTable.Holidays:
                if (!TryParseDate(c[0], out _)) Err($"data „{c[0]}” nu este în formatul yyyy-MM-dd");
                break;
            case ProfileTable.Shifts:
                if (Empty(c[0])) Err("numele schimbului lipsește");
                Time("începutul", c[1]); Time("sfârșitul", c[2]);
                break;
            case ProfileTable.ApprovedAccounts:
                if (Empty(c[0])) Err("contul lipsește");
                else if (c[0].Count(ch => ch == '\\') > 1 || c[0].StartsWith('\\') || c[0].EndsWith('\\')) Err($"contul „{c[0]}” trebuie scris DOMENIU\\utilizator sau utilizator");
                break;
            case ProfileTable.MaintenanceWindows:
                var kind = NormalizeKind(c[0]);
                if (kind is null) { Err($"tipul „{c[0]}” nu este once | weekly | monthly"); }
                Time("începutul", c[4]); Time("sfârșitul", c[5]);
                if (TryParseTime(c[4], out var ms) && TryParseTime(c[5], out var me) && ms == me) Err("începutul și sfârșitul sunt identice");
                if (kind == "once" && !TryParseDate(c[1], out _)) Err($"data „{c[1]}” lipsește sau nu este yyyy-MM-dd (fereastră unică)");
                if (kind is "weekly" or "monthly" && !TryParseDay(c[2], out _)) Err($"ziua săptămânii „{c[2]}” lipsește sau nu este recunoscută");
                if (kind == "monthly" && !TryParseOrdinal(c[3], out _)) Err($"ordinalul „{c[3]}” trebuie să fie 1-4 sau last (ex. prima zi de luni din lună = 1 + Mon)");
                break;
            case ProfileTable.RotationProcedure:
                if (!RotationOrder.Contains(c[0].Trim().ToUpperInvariant())) Err($"pasul „{c[0]}” nu este unul din {string.Join(" → ", RotationOrder)}");
                break;
            case ProfileTable.ApprovedSoftware:
                if (Empty(c[0])) Err("numele software-ului lipsește");
                if (Empty(c[1]) && Empty(c[2]) && Empty(c[3])) Err("trebuie indicat cel puțin editorul, tiparul de cale sau hash-ul");
                if (!Empty(c[3]) && !IsSha256(c[3])) Err("SHA-256 trebuie să aibă 64 de caractere hexazecimale");
                break;
            case ProfileTable.ExpectedPolicies:
                if (Empty(c[0])) Err("calea sau identificatorul politicii lipsește");
                if (!Empty(c[1]) && !IsSha256(c[1])) Err("SHA-256 trebuie să aibă 64 de caractere hexazecimale");
                break;
            case ProfileTable.Zones:
                if (Empty(c[0])) Err("numele zonei lipsește");
                break;
            case ProfileTable.TransferChannels:
                if (Empty(c[0])) Err("numele canalului lipsește");
                if (Empty(c[1]) || Empty(c[2])) Err("zona sursă și zona destinație sunt obligatorii");
                if (Empty(c[3])) Err("mediul de transfer lipsește");
                if (knownZones is { Count: > 0 })
                    foreach (var z in new[] { c[1], c[2] }.Where(z => !Empty(z) && !knownZones.Contains(z.Trim(), StringComparer.OrdinalIgnoreCase)))
                        Err($"zona „{z}” nu este definită în tabelul de zone");
                break;
            case ProfileTable.NetworkDestinations:
                if (Empty(c[0])) Err("adresa lipsește");
                if (knownZones is { Count: > 0 } && !Empty(c[1]) && !knownZones.Contains(c[1].Trim(), StringComparer.OrdinalIgnoreCase)) Err($"zona „{c[1]}” nu este definită în tabelul de zone");
                break;
        }
        return m;
    }
}
