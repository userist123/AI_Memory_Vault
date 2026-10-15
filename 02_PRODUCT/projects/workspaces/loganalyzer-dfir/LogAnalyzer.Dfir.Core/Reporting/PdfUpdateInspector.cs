using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// What may follow the generated report in a signed file: EXACTLY ONE incremental update, holding only what a signature needs: the signature dictionary,
/// the signature field / widget (small, on a page), appearance streams and their fonts, the AcroForm, the catalog with /AcroForm added, pages with /Annots
/// added (content, resources and size unchanged) and the document information. Replacing any other original object (a content stream, a font, a page tree
/// node), a second update, compressed object streams, other annotations or actions make the file INVALID: "raportul a fost modificat înainte de semnare".
/// </summary>
public static class PdfUpdateInspector
{
    private static readonly string[] CatalogMayChange = ["AcroForm", "Perms", "DSS", "Metadata", "Extensions"];
    private static readonly string[] Forbidden = ["/JavaScript", "/JS", "/OpenAction", "/AA", "/Launch", "/SubmitForm", "/ImportData", "/RichMedia", "/EmbeddedFile"];

    /// <summary>Null when the part after <paramref name="originalLength"/> is one signature-only update; otherwise the reason (plain Romanian).</summary>
    public static string? Check(byte[] file, long originalLength)
    {
        try { return Run(file, originalLength); }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException or OverflowException or IndexOutOfRangeException or InvalidOperationException)
        { return "structura documentului nu poate fi citită (" + ex.Message + ")"; }
    }

    private static string? Run(byte[] file, long originalLength)
    {
        var original = file.AsSpan(0, (int)originalLength).ToArray();
        var st = PdfStructure.Parse(original);
        var tail = Encoding.Latin1.GetString(file, (int)originalLength, file.Length - (int)originalLength);
        if (Count(tail, "startxref") != 1 || Count(tail, "%%EOF") != 1 || !tail.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal))
            return "după raportul generat există mai mult de o actualizare (sau una incompletă)";
        if (tail.Contains("/ObjStm", StringComparison.Ordinal) || Regex.IsMatch(tail, @"/Type\s*/XRef\b"))
            return "actualizarea conține obiecte comprimate (fluxuri de obiecte) care nu pot fi verificate";
        int tr = tail.LastIndexOf("trailer", StringComparison.Ordinal);
        if (tr < 0) return "actualizarea nu are trailer";
        var trailer = tail[tr..];
        var prev = Regex.Match(trailer, @"/Prev\s+(\d{1,12})");
        if (!prev.Success || long.Parse(prev.Groups[1].Value, CultureInfo.InvariantCulture) != st.StartXref) return "actualizarea nu continuă raportul generat (/Prev)";
        if (!Regex.IsMatch(trailer, $@"/Root\s+{st.Root}\s+0\s+R\b")) return "actualizarea schimbă rădăcina documentului";

        var objects = Objects(tail[..tr]);
        int sigs = 0;
        var widgets = new HashSet<int>();
        var changedPages = new Dictionary<int, string>();
        foreach (var (num, gen, dict) in objects)
        {
            if (gen != 0) return $"obiectul {num} are o generație neașteptată";
            if (dict is not null && Forbidden.Any(f => Regex.IsMatch(dict, Regex.Escape(f) + @"\b"))) return $"obiectul {num} conține acțiuni sau conținut activ";
            if (num < st.Size)
            {
                if (dict is null) return $"obiectul {num} din raportul generat a fost înlocuit";
                if (num == st.Root)
                {
                    if (!SameExcept(PdfStructure.Entries(st.Dict(num)), PdfStructure.Entries(dict), CatalogMayChange)) return "catalogul documentului a fost schimbat dincolo de semnătură";
                }
                else if (st.Pages.Contains(num))
                {
                    if (!SameExcept(PdfStructure.Entries(st.Dict(num)), PdfStructure.Entries(dict), ["Annots"])) return $"pagina (obiectul {num}) a fost schimbată";
                    changedPages[num] = dict;
                }
                else if (num == st.Info) { }
                else return $"obiectul {num} din raportul generat a fost înlocuit";
                continue;
            }
            if (dict is null) continue;
            var e = PdfStructure.Entries(dict);
            if (e.TryGetValue("Type", out var type) && type is "/Page" or "/Pages" or "/Catalog") return $"actualizarea adaugă o pagină sau un catalog nou (obiectul {num})";
            if (e.TryGetValue("Type", out var t2) && t2 == "/Sig") sigs++;
            if (e.ContainsKey("Subtype") && e.ContainsKey("Rect") || e.TryGetValue("Type", out var t3) && t3 == "/Annot")
            {
                if (!(e.TryGetValue("Subtype", out var sub) && sub == "/Widget" && (e.TryGetValue("FT", out var ft) && ft == "/Sig" || e.ContainsKey("Parent"))))
                    return $"actualizarea adaugă o adnotare care nu este semnătura (obiectul {num})";
                if (!SmallRect(e.GetValueOrDefault("Rect", ""))) return "zona vizibilă a semnăturii acoperă o parte prea mare din pagină";
                widgets.Add(num);
            }
        }
        if (sigs != 1) return "actualizarea trebuie să conțină exact o semnătură";
        foreach (var (num, dict) in changedPages)
        {
            var annots = PdfStructure.Entries(dict).GetValueOrDefault("Annots", "");
            var refs = Regex.Matches(annots, @"(\d+)\s*0\s*R").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            var before = PdfStructure.Entries(st.Dict(num)).GetValueOrDefault("Annots", "");
            var old = Regex.Matches(before, @"(\d+)\s*0\s*R").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();
            if (!annots.StartsWith('[') || refs.Any(r => !old.Contains(r) && !widgets.Contains(r))) return $"pagina (obiectul {num}) primește adnotări care nu sunt semnătura";
        }
        return null;
    }

    private static bool SameExcept(Dictionary<string, string> a, Dictionary<string, string> b, string[] except)
    {
        var ka = a.Keys.Except(except).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var kb = b.Keys.Except(except).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return ka.SequenceEqual(kb) && ka.All(k => a[k] == b[k]);
    }

    /// <summary>At most a quarter of an A4 page (an invisible signature has an empty rectangle).</summary>
    private static bool SmallRect(string rect)
    {
        var n = Regex.Matches(rect, @"-?\d+(\.\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        if (n.Count != 4) return false;
        return Math.Abs((n[2] - n[0]) * (n[3] - n[1])) <= 595.0 * 842.0 / 4;
    }

    private static int Count(string s, string what) { int c = 0, i = 0; while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { c++; i += what.Length; } return c; }

    /// <summary>Every "N G obj … endobj" of the update: number, generation and its dictionary (null for a non-dictionary object). Stream data is skipped.</summary>
    private static List<(int Num, int Gen, string? Dict)> Objects(string s)
    {
        var list = new List<(int, int, string?)>();
        var rx = new Regex(@"(?<![0-9])(\d{1,9})\s+(\d{1,5})\s+obj\b");
        int pos = 0;
        while (true)
        {
            var m = rx.Match(s, pos);
            if (!m.Success) break;
            int bodyStart = m.Index + m.Length;
            while (bodyStart < s.Length && char.IsWhiteSpace(s[bodyStart])) bodyStart++;
            var dict = PdfStructure.LeadingDict(s, bodyStart);
            int after = dict is null ? bodyStart : bodyStart + dict.Length;
            int streamAt = s.IndexOf("stream", after, StringComparison.Ordinal);
            int endobj = s.IndexOf("endobj", after, StringComparison.Ordinal);
            if (endobj < 0) throw new NotSupportedException("obiect neterminat în actualizare");
            if (streamAt >= 0 && streamAt < endobj && s.AsSpan(after, streamAt - after).Trim().Length == 0)
            {
                int es = s.IndexOf("endstream", streamAt + 6, StringComparison.Ordinal);
                if (es < 0) throw new NotSupportedException("flux neterminat în actualizare");
                endobj = s.IndexOf("endobj", es, StringComparison.Ordinal);
                if (endobj < 0) throw new NotSupportedException("obiect neterminat în actualizare");
            }
            list.Add((int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), dict));
            if (list.Count > 2000) throw new NotSupportedException("actualizare prea mare");
            pos = endobj + 6;
        }
        return list;
    }
}
