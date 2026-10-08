using System.Globalization;
using System.Text;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Reader for regedit/reg export files (.reg, "Windows Registry Editor Version 5.00"), used as an independent reference:
/// reg export reads the live registry, the parsers read the saved hive. Values come back as the raw bytes stored.
/// </summary>
public static class RegExport
{
    public sealed record Value(string Name, int Type, byte[] Data)
    {
        public string AsString => Encoding.Unicode.GetString(Data).TrimEnd('\0');
    }

    /// <summary>Key path (as written in the file, without the hive prefix) → values.</summary>
    public static Dictionary<string, List<Value>> Read(string path)
    {
        var keys = new Dictionary<string, List<Value>>(StringComparer.OrdinalIgnoreCase);
        List<Value>? cur = null;
        var logical = new StringBuilder();
        foreach (var raw in File.ReadLines(path, Encoding.Unicode))
        {
            var line = raw.TrimEnd();
            if (logical.Length > 0) { logical.Append(line.TrimStart()); }
            else logical.Append(line);
            if (line.EndsWith('\\') && !line.EndsWith("\\\\\"", StringComparison.Ordinal) && IsHexContinuation(logical)) { logical.Length--; continue; }
            var l = logical.ToString();
            logical.Clear();
            if (l.StartsWith('[') && l.EndsWith(']'))
            {
                var k = l[1..^1];
                k = k[(k.IndexOf('\\') + 1)..];            // drop HKEY_xxx
                if (k.StartsWith("S-1-", StringComparison.Ordinal)) k = k[(k.IndexOf('\\') + 1)..];   // HKEY_USERS\<SID>\...
                if (k.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase) && raw.Contains("HKEY_LOCAL_MACHINE")) k = k[9..];
                cur = keys[k] = [];
                continue;
            }
            if (cur is null || l.Length == 0) continue;
            int eq = FindEquals(l);
            if (eq < 0) continue;
            var name = l[..eq] == "@" ? "" : Unescape(l[1..(eq - 1)]);
            cur.Add(Parse(name, l[(eq + 1)..]));
        }
        return keys;
    }

    private static bool IsHexContinuation(StringBuilder s) => s.ToString().Contains("=hex", StringComparison.Ordinal);

    private static int FindEquals(string l)
    {
        if (l.StartsWith('@')) return 1;
        if (!l.StartsWith('"')) return -1;
        for (int i = 1; i < l.Length; i++)
        {
            if (l[i] == '\\') { i++; continue; }
            if (l[i] == '"') return i + 1 < l.Length && l[i + 1] == '=' ? i + 1 : -1;
        }
        return -1;
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static Value Parse(string name, string v)
    {
        if (v.StartsWith('"')) return new(name, 1, Encoding.Unicode.GetBytes(Unescape(v[1..^1]) + "\0"));
        if (v.StartsWith("dword:", StringComparison.Ordinal))
            return new(name, 4, BitConverter.GetBytes(uint.Parse(v[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        int type = 3, colon = v.IndexOf(':');
        if (v.StartsWith("hex(", StringComparison.Ordinal)) type = int.Parse(v[4..v.IndexOf(')')], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var hex = v[(colon + 1)..].Replace(",", "").Replace(" ", "");
        return new(name, type, Convert.FromHexString(hex));
    }
}
