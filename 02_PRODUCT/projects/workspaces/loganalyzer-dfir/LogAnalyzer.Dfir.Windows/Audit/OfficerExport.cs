using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// Usability 2026-10-16: „Exportă pentru ofițer” puts in one folder what the security officer takes away after a control: the report (and the signed report,
/// when there is one), the copy of the application's log for the CD-R and the media history of the station. This class holds the parts that do not need
/// the user interface: the folder name, the media history file and the short „citește-mă” text.
/// </summary>
public static class OfficerExport
{
    public const string MediaHistoryFile = "istoric_medii.csv";
    public const string ReadmeFile = "citeste_ma.txt";

    /// <summary>„Pentru_ofiter_STATIE_20261016_143000”: one folder per export, never overwritten.</summary>
    public static string FolderName(string host, DateTimeOffset nowUtc)
    {
        var safe = string.Concat((host ?? "").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"Pentru_ofiter_{(safe.Length == 0 ? "statie" : safe)}_{nowUtc:yyyyMMdd_HHmmss}";
    }

    /// <summary>
    /// Every removable medium the control saw: from the Windows device records (USBSTOR and the other sources of <see cref="StationFacts.UsbDevices"/>) and,
    /// when the media register is in force, the media observed with their connection times. Returns the number of rows written (0 = header only).
    /// </summary>
    public static int WriteMediaHistory(StationFacts f, string path)
    {
        int rows = 0;
        using var w = new CsvWriter(path, ["Serie", "Dispozitiv", "Denumire", "VID/PID", "Capacitate (octeți)", "Conectat acum", "Conectări văzute (UTC)", "Sursa"]);
        foreach (var d in f.UsbDevices.OrderBy(d => d.Serial, StringComparer.OrdinalIgnoreCase))
        {
            w.WriteRow(new[] { d.Serial, d.Device, d.FriendlyName, "", "", "", "", d.Source });
            rows++;
        }
        foreach (var m in (f.ObservedMedia ?? []).OrderBy(m => m.Serial, StringComparer.OrdinalIgnoreCase))
        {
            w.WriteRow(new[] { m.Serial, m.Description, "", m.VidPid, m.CapacityBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                        m.ConnectedNow ? "da" : "nu", string.Join("; ", m.Seen.OrderBy(s => s).Select(s => s.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"))), "inventarul mediilor" });
            rows++;
        }
        return rows;
    }

    /// <summary>The „citește-mă” of the folder: what each file is and what the officer does with it.</summary>
    public static string Readme(string host, DateTimeOffset nowUtc, IEnumerable<string> files, string anchorLine) => string.Join(Environment.NewLine,
        new[]
        {
            $"LogAnalyzer — export pentru ofițerul de securitate · stația {host} · {nowUtc:yyyy-MM-dd HH:mm} UTC",
            "",
            "Ce conține acest folder:",
        }
        .Concat(files.Select(f => "• " + f))
        .Concat(
        [
            "",
            "Raportul de control (PDF) devine oficial doar cu semnătura electronică calificată a ofițerului de securitate.",
            $"{MediaHistoryFile}: toate memoriile USB și discurile externe văzute de stație, cu seria și momentele conectării; se compară cu registrul de medii.",
            "Folderul LogAnalyzer_ancorare_…: copia jurnalului aplicației; se arde pe un CD-R (se scrie o singură dată) și se păstrează la ofițer.",
            anchorLine.Length > 0 ? anchorLine + " — se trece pe procesul-verbal." : "",
            "",
        ]));
}
