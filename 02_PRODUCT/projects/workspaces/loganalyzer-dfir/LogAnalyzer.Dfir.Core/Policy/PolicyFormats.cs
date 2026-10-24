using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Policy;

/// <summary>One Registry.pol / LGPO entry. Action: set, delete (value), deletevalues (all values of the key), other special entries.</summary>
public sealed record PolEntry(string Key, string ValueName, int Type, byte[] Data, string Action)
{
    public const int RegSz = 1, RegExpandSz = 2, RegBinary = 3, RegDword = 4, RegMultiSz = 7, RegQword = 11;

    public string TypeName => Type switch
    {
        RegSz => "string", RegExpandSz => "expand_string", RegBinary => "binary", RegDword => "dword", RegMultiSz => "multi_string", RegQword => "qword",
        _ => $"type{Type}",
    };

    /// <summary>Data as the registry provider and policy compare it: decimal for DWORD/QWORD, text for strings, hex for binary.</summary>
    public string Text => Type switch
    {
        RegDword when Data.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(Data).ToString(CultureInfo.InvariantCulture),
        RegQword when Data.Length >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(Data).ToString(CultureInfo.InvariantCulture),
        RegSz or RegExpandSz => Encoding.Unicode.GetString(Data).TrimEnd('\0'),
        RegMultiSz => string.Join(" ", Encoding.Unicode.GetString(Data).Split('\0', StringSplitOptions.RemoveEmptyEntries)),
        _ => Convert.ToHexString(Data),
    };
}

/// <summary>Registry.pol (PReg v1): "PReg", version 1, then [key;value;type;size;data] with UTF-16 strings and LE integers.</summary>
public static class RegistryPol
{
    public static List<PolEntry> Parse(byte[] d)
    {
        if (d.Length < 8 || Encoding.ASCII.GetString(d, 0, 4) != "PReg" || BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4)) != 1)
            throw new InvalidDataException("Nu este un fișier Registry.pol (antet PReg v1).");
        var list = new List<PolEntry>();
        int p = 8;
        while (p < d.Length)
        {
            Expect(d, ref p, '[');
            var key = Utf16Z(d, ref p);
            Expect(d, ref p, ';');
            var value = Utf16Z(d, ref p);
            Expect(d, ref p, ';');
            int type = Int(d, ref p);
            Expect(d, ref p, ';');
            int size = Int(d, ref p);
            Expect(d, ref p, ';');
            if (size < 0 || p + size > d.Length) throw new InvalidDataException($"Registry.pol: date trunchiate la 0x{p:X} ({key}\\{value}).");
            var data = d.AsSpan(p, size).ToArray();
            p += size;
            Expect(d, ref p, ']');
            list.Add(new PolEntry(key, value, type, data, Action(value)));
        }
        return list;
    }

    private static string Action(string valueName) =>
        valueName.StartsWith("**del.", StringComparison.OrdinalIgnoreCase) ? "delete"
        : valueName.StartsWith("**delvals", StringComparison.OrdinalIgnoreCase) || valueName.Equals("**DeleteValues", StringComparison.OrdinalIgnoreCase) ? "deletevalues"
        : valueName.StartsWith("**", StringComparison.Ordinal) ? "special"
        : "set";

    private static void Expect(byte[] d, ref int p, char c)
    {
        if (p + 2 > d.Length || d[p] != (byte)c || d[p + 1] != 0) throw new InvalidDataException($"Registry.pol: se aștepta „{c}” la 0x{p:X}.");
        p += 2;
    }

    private static string Utf16Z(byte[] d, ref int p)
    {
        int start = p;
        while (p + 1 < d.Length && (d[p] != 0 || d[p + 1] != 0)) p += 2;
        if (p + 1 >= d.Length) throw new InvalidDataException("Registry.pol: șir neterminat.");
        var s = Encoding.Unicode.GetString(d, start, p - start);
        p += 2;
        return s;
    }

    private static int Int(byte[] d, ref int p)
    {
        if (p + 4 > d.Length) throw new InvalidDataException("Registry.pol: întreg trunchiat.");
        int v = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p));
        p += 4;
        return v;
    }
}

/// <summary>Security template (secedit .inf / GptTmpl.inf): sections of "key = value" (UTF-16 or ANSI).</summary>
public static class SecurityTemplate
{
    public static Dictionary<string, List<(string Key, string Value)>> Parse(string text)
    {
        var sections = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        List<(string, string)>? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { cur = sections[line[1..^1]] = []; continue; }
            if (cur is null) continue;
            int eq = line.IndexOf('=');
            // Sections such as [Service General Setting], [Registry Keys] and [File Security] hold CSV lines without "=";
            // they are kept whole so that nothing in the template is dropped.
            cur.Add(eq < 0 ? (line, "") : (line[..eq].Trim(), line[(eq + 1)..].Trim()));
        }
        return sections;
    }

    public static string Decode(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE ? Encoding.Unicode.GetString(data, 2, data.Length - 2) : Encoding.UTF8.GetString(data);
}

/// <summary>Advanced audit policy CSV (audit.csv in a GPO backup / auditpol /backup).</summary>
public static class AuditCsv
{
    public sealed record Row(string Subcategory, Guid Guid, string InclusionSetting, int SettingValue);

    public static List<Row> Parse(string text)
    {
        var rows = CsvReader.ReadRows(new StringReader(text)).ToList();
        if (rows.Count == 0) return [];
        var h = rows[0].Select(x => x.Trim()).ToList();
        int iSub = h.IndexOf("Subcategory"), iGuid = h.IndexOf("Subcategory GUID"), iInc = h.IndexOf("Inclusion Setting"), iVal = h.IndexOf("Setting Value");
        if (iGuid < 0 || iVal < 0) throw new InvalidDataException("audit.csv: lipsesc coloanele Subcategory GUID / Setting Value.");
        return rows.Skip(1).Where(r => r.Length > iVal && Guid.TryParse(r[iGuid].Trim('{', '}'), out _))
            .Select(r => new Row(r[iSub], Guid.Parse(r[iGuid].Trim('{', '}')), iInc >= 0 ? r[iInc] : "", int.Parse(r[iVal], CultureInfo.InvariantCulture))).ToList();
    }
}

/// <summary>LGPO text ("LGPO /parse" format): Computer|User, key, value name, TYPE:data | DELETE | DELETEALLVALUES; ";" comments.</summary>
public static class LgpoText
{
    public static List<(string Scope, PolEntry Entry)> Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith(';')).ToList();
        if (lines.Count % 4 != 0) throw new InvalidDataException($"LGPO text: {lines.Count} linii, se așteaptă grupuri de 4.");
        var list = new List<(string, PolEntry)>();
        for (int i = 0; i < lines.Count; i += 4)
        {
            var scope = lines[i];
            if (scope is not ("Computer" or "User")) throw new InvalidDataException($"LGPO text: „{scope}” trebuie să fie Computer sau User.");
            var (key, name, action) = (lines[i + 1], lines[i + 2], lines[i + 3]);
            if (action.Equals("DELETE", StringComparison.OrdinalIgnoreCase)) { list.Add((scope, new PolEntry(key, name, 0, [], "delete"))); continue; }
            if (action.Equals("DELETEALLVALUES", StringComparison.OrdinalIgnoreCase)) { list.Add((scope, new PolEntry(key, name, 0, [], "deletevalues"))); continue; }
            int colon = action.IndexOf(':');
            if (colon < 0) throw new InvalidDataException($"LGPO text: acțiune necunoscută „{action}”.");
            var (type, value) = (action[..colon].ToUpperInvariant(), action[(colon + 1)..]);
            var entry = type switch
            {
                "DWORD" => new PolEntry(key, name, PolEntry.RegDword, BitConverter.GetBytes(uint.Parse(value, CultureInfo.InvariantCulture)), "set"),
                "QWORD" => new PolEntry(key, name, PolEntry.RegQword, BitConverter.GetBytes(ulong.Parse(value, CultureInfo.InvariantCulture)), "set"),
                "SZ" => new PolEntry(key, name, PolEntry.RegSz, Encoding.Unicode.GetBytes(value + "\0"), "set"),
                "EXSZ" => new PolEntry(key, name, PolEntry.RegExpandSz, Encoding.Unicode.GetBytes(value + "\0"), "set"),
                "MULTISZ" => new PolEntry(key, name, PolEntry.RegMultiSz, Encoding.Unicode.GetBytes(value.Replace("\\0", "\0") + "\0\0"), "set"),
                _ => throw new InvalidDataException($"LGPO text: tipul „{type}” nu este acceptat."),
            };
            list.Add((scope, entry));
        }
        return list;
    }
}

/// <summary>
/// A GPO backup (folder or zip): display name and the Machine/User Registry.pol, GptTmpl.inf and audit.csv it contains, plus the Group Policy
/// Preferences (Registry.xml, Services.xml, ...) and the scripts. <see cref="NotRead"/> lists every other file, so nothing is dropped silently.
/// </summary>
/// <summary>Caps applied when reading a GPO backup (security audit #13). The defaults are far above any real GPO backup.</summary>
public sealed record GpoZipLimits
{
    public static readonly GpoZipLimits Default = new();
    public int MaxEntries { get; init; } = 5_000;
    public long MaxEntryBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 256L * 1024 * 1024;
    public long MaxCompressionRatio { get; init; } = 100;
    /// <summary>Entries smaller than this are not ratio-checked (small text files legitimately compress very well).</summary>
    public long RatioCheckFromBytes { get; init; } = 1024 * 1024;
}

public sealed record GpoBackup(string DisplayName, string Guid, byte[]? MachinePol, byte[]? UserPol, string? SecurityTemplate, string? AuditCsv, string? GpReport,
    IReadOnlyList<(string Path, string Text)>? Preferences = null, IReadOnlyList<(string Path, string Text)>? Scripts = null, IReadOnlyList<string>? NotRead = null)
{
    public static GpoBackup FromZip(Stream zip) => FromZip(zip, GpoZipLimits.Default);

    /// <summary>
    /// Reads a GPO backup zip within <paramref name="limits"/> (security audit #13): entry count, declared and actual uncompressed size per entry
    /// and in total, and compression ratio are checked before and while reading; two entries with the same name (case and slash direction ignored)
    /// are refused. Every refusal is an <see cref="InvalidDataException"/> with a Romanian message naming the entry.
    /// </summary>
    public static GpoBackup FromZip(Stream zip, GpoZipLimits limits)
    {
        using var z = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (z.Entries.Count > limits.MaxEntries)
            throw new InvalidDataException($"Arhiva GPO are prea multe intrări ({z.Entries.Count}; limita este {limits.MaxEntries}). Nu a fost importată.");
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long declared = 0;
        foreach (var e in z.Entries)
        {
            if (e.Length == 0) continue;                                     // folder entries and empty files carry nothing
            var name = e.FullName.Replace('\\', '/');
            if (!files.TryAdd(name, e))
                throw new InvalidDataException($"Arhiva GPO conține de două ori intrarea „{name}” (nume identice ignorând majusculele și sensul barei); nu se poate alege una. Nu a fost importată.");
            if (e.Length > limits.MaxEntryBytes)
                throw new InvalidDataException($"Intrarea „{name}” este prea mare ({e.Length:N0} octeți necomprimat; limita este {limits.MaxEntryBytes:N0}). Arhiva GPO nu a fost importată.");
            if (e.Length > limits.RatioCheckFromBytes && e.CompressedLength > 0 && e.Length / e.CompressedLength > limits.MaxCompressionRatio)
                throw new InvalidDataException($"Intrarea „{name}” are un raport de comprimare suspect ({e.Length / e.CompressedLength}:1; limita este {limits.MaxCompressionRatio}:1). Arhiva GPO nu a fost importată.");
            declared += e.Length;
            if (declared > limits.MaxTotalBytes)
                throw new InvalidDataException($"Arhiva GPO depășește mărimea totală necomprimată permisă ({limits.MaxTotalBytes:N0} octeți). Nu a fost importată.");
        }
        long readTotal = 0;
        byte[] ReadAll(string name, ZipArchiveEntry e)
        {
            var data = ReadBounded(e.Open, name, limits.MaxEntryBytes);
            readTotal += data.Length;
            if (readTotal > limits.MaxTotalBytes)
                throw new InvalidDataException($"Arhiva GPO depășește mărimea totală necomprimată permisă ({limits.MaxTotalBytes:N0} octeți). Nu a fost importată.");
            return data;
        }
        byte[]? Read(Func<string, bool> pick) => files.Keys.FirstOrDefault(pick) is { } k ? ReadAll(k, files[k]) : null;
        return Build(Read, files.Keys, n => files.TryGetValue(n, out var e) ? ReadAll(n, e) : null);
    }

    public static GpoBackup FromFolder(string folder) => FromFolder(folder, GpoZipLimits.Default);

    /// <summary>A GPO backup folder, with the same count and size caps as a zip.</summary>
    public static GpoBackup FromFolder(string folder, GpoZipLimits limits)
    {
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).ToList();
        if (files.Count > limits.MaxEntries)
            throw new InvalidDataException($"Folderul GPO are prea multe fișiere ({files.Count}; limita este {limits.MaxEntries}). Nu a fost importat.");
        long readTotal = 0;
        byte[] ReadFile(string n)
        {
            var data = ReadBounded(() => File.OpenRead(Path.Combine(folder, n)), n, limits.MaxEntryBytes);
            readTotal += data.Length;
            if (readTotal > limits.MaxTotalBytes)
                throw new InvalidDataException($"Folderul GPO depășește mărimea totală permisă ({limits.MaxTotalBytes:N0} octeți). Nu a fost importat.");
            return data;
        }
        byte[]? Read(Func<string, bool> pick) => files.FirstOrDefault(pick) is { } k ? ReadFile(k) : null;
        return Build(Read, files, ReadFile);
    }

    /// <summary>Reads at most <paramref name="max"/> bytes, counting what is actually read (a zip header can lie about the size).</summary>
    private static byte[] ReadBounded(Func<Stream> open, string name, long max)
    {
        using var s = open();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > max)
                throw new InvalidDataException($"Intrarea „{name}” este prea mare (peste {max:N0} octeți necomprimat). Importul GPO a fost oprit.");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    private static GpoBackup Build(Func<Func<string, bool>, byte[]?> read, IEnumerable<string> names, Func<string, byte[]?> readExact)
    {
        bool Ends(string n, string suffix) => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        var backup = read(n => Ends(n, "/Backup.xml") || n.Equals("Backup.xml", StringComparison.OrdinalIgnoreCase));
        string display = "", guid = "";
        if (backup is not null)
        {
            XDocument x;
            try { x = LogAnalyzer.Dfir.IO.SafeXml.Parse(Policy.SecurityTemplate.Decode(backup)); }
            catch (System.Xml.XmlException ex) { throw new InvalidDataException($"Backup.xml nu este un XML acceptat (DTD/entități interzise sau XML invalid): {ex.Message}", ex); }
            display = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value ?? "";
            guid = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "ID")?.Value ?? "";
        }
        if (backup is null && !names.Any(n => Ends(n, "/registry.pol") || Ends(n, "/GptTmpl.inf")))
            throw new InvalidDataException("Nu este un backup GPO (lipsesc Backup.xml, registry.pol și GptTmpl.inf).");
        var inf = read(n => Ends(n, "/SecEdit/GptTmpl.inf"));
        var audit = read(n => Ends(n, "/Audit/audit.csv"));
        var report = read(n => Ends(n, "/gpreport.xml"));
        // Group Policy Preferences and scripts are read as text; they are data and are never executed.
        var prefs = new List<(string, string)>(); var scripts = new List<(string, string)>(); var notRead = new List<string>();
        foreach (var n in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            string Text() => readExact(n) is { } bytes ? Policy.SecurityTemplate.Decode(bytes) : "";
            if (n.Contains("/Preferences/", StringComparison.OrdinalIgnoreCase) && Ends(n, ".xml")) prefs.Add((n, Text()));
            else if (n.Contains("/Scripts/", StringComparison.OrdinalIgnoreCase)) scripts.Add((n, Text()));
            else if (!(Ends(n, "Backup.xml") || Ends(n, "bkupInfo.xml") || Ends(n, "manifest.xml") || Ends(n, "/gpreport.xml") || Ends(n, "/registry.pol")
                       || Ends(n, "/SecEdit/GptTmpl.inf") || Ends(n, "/Audit/audit.csv") || Ends(n, "/comment.cmtx")))
                notRead.Add(n);
        }
        return new GpoBackup(display, guid,
            read(n => Ends(n, "/Machine/registry.pol")), read(n => Ends(n, "/User/registry.pol")),
            inf is null ? null : Policy.SecurityTemplate.Decode(inf), audit is null ? null : Policy.SecurityTemplate.Decode(audit), report is null ? null : Policy.SecurityTemplate.Decode(report),
            prefs, scripts, notRead);
    }
}
