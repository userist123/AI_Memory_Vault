using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// Bounded reader of the structure of a PDF that LogAnalyzer generated (classic cross-reference tables, uncompressed object dictionaries): the last
/// startxref, the cross-reference sections followed through /Prev (newest wins), the trailer, object dictionaries and the page list. Anything else throws
/// <see cref="NotSupportedException"/> with a readable reason; callers that face untrusted input turn that into a verdict, never into a crash.
/// </summary>
public sealed class PdfStructure
{
    public int Size { get; private set; }
    public int Root { get; private set; }
    public int Info { get; private set; }
    public long StartXref { get; private set; }
    public string IdEntry { get; private set; } = "";
    /// <summary>Number of cross-reference sections (1 = no incremental update).</summary>
    public int Sections { get; private set; }
    public IReadOnlyList<int> Pages { get; private set; } = [];
    public IReadOnlyDictionary<int, long> Offsets => _offsets;

    private readonly Dictionary<int, long> _offsets = [];
    private byte[] _b = [];

    public static PdfStructure Parse(byte[] b, int maxSections = 1)
    {
        var t = new PdfStructure { _b = b };
        if (b.Length < 16 || !b.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new NotSupportedException("fișierul nu este un PDF");
        int sx = b.AsSpan().LastIndexOf("startxref"u8);
        if (sx < 0) throw new NotSupportedException("PDF fără startxref");
        var after = Encoding.ASCII.GetString(b, sx + 9, Math.Min(40, b.Length - sx - 9));
        var m = Regex.Match(after, @"^\s*(\d{1,12})\s*%%EOF");
        if (!m.Success) throw new NotSupportedException("sfârșitul PDF-ului nu poate fi citit");
        t.StartXref = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        long xref = t.StartXref;
        string? firstTrailer = null;
        var seen = new HashSet<long>();
        while (true)
        {
            if (!seen.Add(xref) || seen.Count > maxSections) throw new NotSupportedException(maxSections == 1 ? "PDF cu actualizări anterioare: nu este un raport generat de aplicație" : "prea multe secțiuni de referințe");
            var trailer = t.ReadSection(xref);
            firstTrailer ??= trailer;
            if (Regex.IsMatch(trailer, @"/(Encrypt|XRefStm)\b")) throw new NotSupportedException("PDF criptat sau cu fluxuri de referințe: nu este un raport generat de aplicație");
            var prev = Regex.Match(trailer, @"/Prev\s+(\d{1,12})");
            if (!prev.Success) break;
            xref = long.Parse(prev.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        t.Sections = seen.Count;
        t.Size = Int(firstTrailer, @"/Size\s+(\d+)");
        t.Root = Int(firstTrailer, @"/Root\s+(\d+)\s+0\s+R");
        t.Info = Regex.Match(firstTrailer, @"/Info\s+(\d+)\s+0\s+R") is { Success: true } im ? int.Parse(im.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        t.IdEntry = Regex.Match(firstTrailer, @"/ID\s*\[[^\]]{0,200}\]") is { Success: true } idm ? " " + idm.Value : "";
        var pages = new List<int>();
        t.Walk(Int(t.Dict(t.Root), @"/Pages\s+(\d+)\s+0\s+R"), pages, 0);
        if (pages.Count == 0) throw new NotSupportedException("documentul nu are pagini");
        t.Pages = pages;
        return t;
    }

    /// <summary>Reads the xref table at <paramref name="xref"/> into the offsets (entries already known from a newer section are kept) and returns its trailer text.</summary>
    private string ReadSection(long xref)
    {
        if (xref <= 0 || xref >= _b.Length - 4 || !_b.AsSpan((int)xref).StartsWith("xref"u8))
            throw new NotSupportedException("PDF-ul nu are un tabel clasic de referințe (xref) la poziția indicată");
        int tr = _b.AsSpan((int)xref).IndexOf("trailer"u8);
        if (tr < 0) throw new NotSupportedException("PDF fără trailer");
        var lines = Encoding.ASCII.GetString(_b, (int)xref + 4, tr - 4).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int cur = -1;
        foreach (var line in lines)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 2 && int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)) { cur = start; continue; }
            if (p.Length == 3 && cur >= 0 && long.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var off))
            {
                if (p[2] == "n" && !_offsets.ContainsKey(cur)) _offsets[cur] = off;
                cur++; continue;
            }
            throw new NotSupportedException("tabelul de referințe nu poate fi citit");
        }
        int end = _b.AsSpan((int)xref + tr).IndexOf("startxref"u8);
        int len = end < 0 ? Math.Min(4096, _b.Length - (int)xref - tr) : Math.Min(end, 4096);
        return Encoding.Latin1.GetString(_b, (int)xref + tr, len);
    }

    private void Walk(int obj, List<int> leaves, int depth)
    {
        if (depth > 16 || leaves.Count > 10000) throw new NotSupportedException("arborele de pagini este prea adânc");
        var d = Dict(obj);
        if (Regex.IsMatch(d, @"/Type\s*/Page\b")) { leaves.Add(obj); return; }
        var kids = Regex.Match(d, @"/Kids\s*\[([^\]]*)\]");
        if (!kids.Success) throw new NotSupportedException("nodul de pagini nu are /Kids");
        foreach (Match k in Regex.Matches(kids.Groups[1].Value, @"(\d+)\s+0\s+R")) Walk(int.Parse(k.Groups[1].Value, CultureInfo.InvariantCulture), leaves, depth + 1);
    }

    /// <summary>The dictionary text of object <paramref name="num"/> ("&lt;&lt; … &gt;&gt;").</summary>
    public string Dict(int num)
    {
        if (!_offsets.TryGetValue(num, out var off) || off <= 0 || off >= _b.Length) throw new NotSupportedException($"obiectul {num} lipsește din tabelul de referințe");
        var text = Encoding.Latin1.GetString(_b, (int)off, (int)Math.Min(8192, _b.Length - off));
        var m = Regex.Match(text, $@"^{num}\s+0\s+obj\s*");
        if (!m.Success) throw new NotSupportedException($"obiectul {num} nu este la poziția din tabelul de referințe");
        return LeadingDict(text, m.Length) ?? throw new NotSupportedException($"obiectul {num} nu este un dicționar");
    }

    /// <summary>The balanced "&lt;&lt; … &gt;&gt;" starting at <paramref name="start"/>; null when there is none.</summary>
    public static string? LeadingDict(string text, int start)
    {
        if (start + 1 >= text.Length || text[start] != '<' || text[start + 1] != '<') return null;
        int depth = 0;
        for (int i = start; i < text.Length - 1; i++)
        {
            if (text[i] == '(') { i = SkipString(text, i); continue; }
            if (text[i] == '<' && text[i + 1] == '<') { depth++; i++; continue; }
            if (text[i] == '<') { int e = text.IndexOf('>', i + 1); if (e < 0) return null; i = e; continue; }   // a hex string: its '>' does not close a dictionary
            if (text[i] == '>' && text[i + 1] == '>') { depth--; i++; if (depth == 0) return text[start..(i + 1)]; }
        }
        return null;
    }

    private static int SkipString(string s, int i)
    {
        int depth = 0;
        for (; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return s.Length - 1;
    }

    /// <summary>
    /// The top-level entries of a dictionary, key → value with whitespace normalised (so the same dictionary written by another tool compares equal).
    /// </summary>
    public static Dictionary<string, string> Entries(string dict)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!dict.StartsWith("<<", StringComparison.Ordinal) || !dict.EndsWith(">>", StringComparison.Ordinal)) throw new NotSupportedException("dicționar invalid");
        var s = dict[2..^2];
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            if (s[i] != '/') throw new NotSupportedException("dicționar invalid");
            int k = i + 1;
            while (k < s.Length && !IsDelimiter(s[k])) k++;
            var key = s[(i + 1)..k];
            i = k;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int v = i;
            i = SkipValue(s, i);
            map[key] = Normalize(s[v..i]);
            if (map.Count > 512) throw new NotSupportedException("dicționar prea mare");
        }
        return map;
    }

    private static bool IsDelimiter(char c) => char.IsWhiteSpace(c) || c is '/' or '<' or '>' or '[' or ']' or '(' or ')';

    private static int SkipValue(string s, int i)
    {
        if (i >= s.Length) return i;
        if (s[i] == '/') { i++; while (i < s.Length && !IsDelimiter(s[i])) i++; return i; }
        if (s[i] == '(') return SkipString(s, i) + 1;
        if (s.AsSpan(i).StartsWith("<<")) { var d = LeadingDict(s, i) ?? throw new NotSupportedException("dicționar neînchis"); return i + d.Length; }
        if (s[i] == '<') { int e = s.IndexOf('>', i); return e < 0 ? throw new NotSupportedException("șir hex neînchis") : e + 1; }
        if (s[i] == '[')
        {
            int depth = 0;
            for (; i < s.Length; i++)
            {
                if (s[i] == '(') { i = SkipString(s, i); continue; }
                if (s[i] == '[') depth++;
                else if (s[i] == ']' && --depth == 0) return i + 1;
            }
            throw new NotSupportedException("tablou neînchis");
        }
        // numbers, booleans, null, "n g R" references: up to the next key
        while (i < s.Length && s[i] != '/') i++;
        return i;
    }

    private static string Normalize(string v)
    {
        var x = Regex.Replace(v.Trim(), @"\s+", " ");
        return Regex.Replace(x, @"\s*(<<|>>|\[|\]|/)\s*", "$1").Trim();
    }

    private static int Int(string s, string pattern) =>
        Regex.Match(s, pattern) is { Success: true } m && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v
            : throw new NotSupportedException("PDF incomplet (" + pattern + ")");
}
