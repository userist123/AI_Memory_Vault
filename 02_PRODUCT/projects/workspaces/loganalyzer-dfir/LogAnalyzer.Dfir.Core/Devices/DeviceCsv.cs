using System.Text;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// The CSV of the monitor's device list (pure): ';' separator (the Romanian Excel's list separator), values quoted when they hold ';', '"' or a line break
/// (quotes doubled), CRLF line ends, UTF-8 with a byte-order mark so Excel reads the diacritics. A value that starts like a formula ('=', '+', '-', '@', tab, CR) is
/// prefixed with an apostrophe so a spreadsheet never runs it (the device names and labels come from the device).
/// </summary>
public static class DeviceCsv
{
    public const char Separator = ';';

    /// <summary>The columns of the monitor page, in order; the first row of the file.</summary>
    public static readonly IReadOnlyList<string> Headers =
    [
        "Stare", "Nume", "Tip", "Producător", "Model", "VID", "PID", "Serie", "Capacitate", "Literă", "Etichetă", "Utilizator",
        "Prima apariție", "Ultima conectare", "Ultima deconectare", "Nr. de înregistrare", "Registru", "Alertă",
    ];

    public static string Quote(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([Separator, '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    public static string Line(IEnumerable<string?> cells) => string.Join(Separator, cells.Select(Quote));

    /// <summary>The whole file as text: the header row, then one row per device.</summary>
    public static string Build(IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(Line(Headers)).Append("\r\n");
        foreach (var r in rows) sb.Append(Line(r)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>The bytes of the file: the UTF-8 byte-order mark and the text.</summary>
    public static byte[] ToBytes(IEnumerable<IReadOnlyList<string>> rows)
    {
        var body = new UTF8Encoding(false).GetBytes(Build(rows));
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }
}
