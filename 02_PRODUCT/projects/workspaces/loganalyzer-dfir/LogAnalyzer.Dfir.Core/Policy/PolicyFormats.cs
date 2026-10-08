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

/// <summary>A GPO backup (folder or zip): display name and the Machine/User Registry.pol, GptTmpl.inf and audit.csv it contains.</summary>
public sealed record GpoBackup(string DisplayName, string Guid, byte[]? MachinePol, byte[]? UserPol, string? SecurityTemplate, string? AuditCsv, string? GpReport)
{
    public static GpoBackup FromZip(Stream zip)
    {
        using var z = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var files = z.Entries.Where(e => e.Length > 0).ToDictionary(e => e.FullName.Replace('\\', '/'), e => e, StringComparer.OrdinalIgnoreCase);
        byte[]? Read(Func<string, bool> pick) => files.Keys.FirstOrDefault(pick) is { } k ? ReadAll(files[k]) : null;
        return Build(Read, files.Keys);
    }

    public static GpoBackup FromFolder(string folder)
    {
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).ToList();
        byte[]? Read(Func<string, bool> pick) => files.FirstOrDefault(pick) is { } k ? File.ReadAllBytes(Path.Combine(folder, k)) : null;
        return Build(Read, files);
    }

    private static byte[] ReadAll(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static GpoBackup Build(Func<Func<string, bool>, byte[]?> read, IEnumerable<string> names)
    {
        bool Ends(string n, string suffix) => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        var backup = read(n => Ends(n, "/Backup.xml") || n.Equals("Backup.xml", StringComparison.OrdinalIgnoreCase));
        string display = "", guid = "";
        if (backup is not null)
        {
            var x = XDocument.Parse(Policy.SecurityTemplate.Decode(backup));
            display = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value ?? "";
            guid = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "ID")?.Value ?? "";
        }
        if (backup is null && !names.Any(n => Ends(n, "/registry.pol") || Ends(n, "/GptTmpl.inf")))
            throw new InvalidDataException("Nu este un backup GPO (lipsesc Backup.xml, registry.pol și GptTmpl.inf).");
        var inf = read(n => Ends(n, "/SecEdit/GptTmpl.inf"));
        var audit = read(n => Ends(n, "/Audit/audit.csv"));
        var report = read(n => Ends(n, "/gpreport.xml"));
        return new GpoBackup(display, guid,
            read(n => Ends(n, "/Machine/registry.pol")), read(n => Ends(n, "/User/registry.pol")),
            inf is null ? null : Policy.SecurityTemplate.Decode(inf), audit is null ? null : Policy.SecurityTemplate.Decode(audit), report is null ? null : Policy.SecurityTemplate.Decode(report));
    }
}
