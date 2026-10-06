using System.Globalization;
using System.IO;
using System.Text;

namespace LogAnalyzer.LicenseManager;

public sealed record IssuedLicense(DateTime IssuedUtc, string Client, string HardwareId, DateTime ExpiryDate, string License, string Notes);

/// <summary>Append-only CSV register of issued licenses, kept outside the application folder (%APPDATA%).</summary>
public sealed class LicenseLedger
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogAnalyzer", "LicenseManager");
    private readonly string _path;

    public LicenseLedger(string? path = null) => _path = path ?? Path.Combine(Folder, "issued_licenses.csv");

    private const string Header = "IssuedUtc,Client,HardwareId,ExpiryDate,License,Notes";

    public void Append(IssuedLicense l)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        bool newFile = !File.Exists(_path);
        using var w = new StreamWriter(_path, append: true, new UTF8Encoding(true));
        if (newFile) w.WriteLine(Header);
        w.WriteLine(string.Join(',', new[]
        {
            l.IssuedUtc.ToString("o", CultureInfo.InvariantCulture), Esc(l.Client), l.HardwareId,
            l.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.License, Esc(l.Notes),
        }));
    }

    public IEnumerable<IssuedLicense> ReadAll()
    {
        if (!File.Exists(_path)) yield break;
        foreach (var line in File.ReadLines(_path).Skip(1))
        {
            var f = Split(line);
            if (f.Count < 6) continue;
            if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var issued)) continue;
            if (!DateTime.TryParseExact(f[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp)) continue;
            yield return new IssuedLicense(issued, f[1], f[2], exp, f[4], f[5]);
        }
    }

    private static string Esc(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static List<string> Split(string line)
    {
        var res = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }
}
