using System.Globalization;
using System.Text;

namespace LogAnalyzer.Dfir.IO;

/// <summary>RFC 4180 CSV writer, UTF-8 with BOM (opens cleanly in Excel with diacritics).</summary>
public sealed class CsvWriter : IDisposable
{
    private readonly StreamWriter _w;
    private readonly int _columns;

    public CsvWriter(string path, IReadOnlyList<string> header, bool append = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bool writeHeader = !append || !File.Exists(path) || new FileInfo(path).Length == 0;
        _w = new StreamWriter(path, append, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _columns = header.Count;
        if (writeHeader) WriteRow(header);
    }

    public void WriteRow(IReadOnlyList<object?> values)
    {
        if (values.Count != _columns) throw new ArgumentException($"Expected {_columns} columns, got {values.Count}.");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) _w.Write(',');
            _w.Write(Escape(Format(values[i])));
        }
        _w.Write("\r\n");
    }

    public void WriteRow(IReadOnlyList<string> values) => WriteRow(values.Cast<object?>().ToList());

    public static string Format(object? v) => v switch
    {
        null => "",
        DateTimeOffset d => d.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    public static string Escape(string s)
    {
        // Neutralize spreadsheet formula injection from evidence-derived text (spec §116/§118).
        if (s.Length > 0 && "=+-@".Contains(s[0]) && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) s = "'" + s;
        return s.IndexOfAny(['"', ',', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public void Dispose() => _w.Dispose();
}

/// <summary>Minimal RFC 4180 reader used to import CSV evidence produced by collectors.</summary>
public static class CsvReader
{
    public static IEnumerable<Dictionary<string, string>> ReadDicts(string path)
    {
        using var r = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string[]? header = null;
        foreach (var row in ReadRows(r))
        {
            if (header is null) { header = row; continue; }
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++) d[header[i]] = i < row.Length ? row[i] : "";
            yield return d;
        }
    }

    public static IEnumerable<string[]> ReadRows(TextReader r)
    {
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false, any = false;
        int c;
        while ((c = r.Read()) != -1)
        {
            any = true;
            char ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (r.Peek() == '"') { field.Append('"'); r.Read(); }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { }
            else if (ch == '\n') { row.Add(field.ToString()); field.Clear(); yield return row.ToArray(); row.Clear(); any = false; }
            else field.Append(ch);
        }
        if (any || field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return row.ToArray(); }
    }
}
