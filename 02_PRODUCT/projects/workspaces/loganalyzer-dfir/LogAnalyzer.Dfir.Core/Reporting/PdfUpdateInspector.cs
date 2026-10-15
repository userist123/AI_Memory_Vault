using System.Text;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>Result of inspecting the update after the original: the reason it is refused (null when accepted) and the role of every object in it.</summary>
public sealed record UpdateInspection(string? Problem, IReadOnlyDictionary<int, string> Roles);

/// <summary>
/// What may follow the generated report in a signed file: EXACTLY ONE incremental update, read with a real PDF parser (<see cref="PdfParser"/>), whose
/// cross-reference table is checked entry by entry, and whose objects pass a strict WHITELIST:
/// <list type="bullet">
/// <item>original objects: only the catalog (identical except an added /AcroForm) and the one page that receives the widget (identical except an added /Annots);</item>
/// <item>new objects, each reachable from /AcroForm or /Annots and with an allowed set of keys for its role: the AcroForm, exactly one signature widget
/// (inside the page, at most a quarter of it), exactly one signature dictionary, appearance form XObjects (scale/translate matrix only) and their fonts.</item>
/// </list>
/// Anything else (a free xref entry, an object the xref does not point at, an action, a link, XFA, a second widget, compressed object streams…) is refused:
/// "raportul a fost modificat înainte de semnare".
/// </summary>
public static class PdfUpdateInspector
{
    private static readonly HashSet<string> SigKeys = ["Type", "Filter", "SubFilter", "ByteRange", "Contents", "M", "Name", "Reason", "Location", "ContactInfo", "Prop_Build"];
    private static readonly HashSet<string> WidgetKeys = ["Type", "Subtype", "FT", "Rect", "P", "F", "T", "V", "AP", "DA", "MK", "Ff"];
    private static readonly HashSet<string> AcroFormKeys = ["Fields", "SigFlags", "DR", "DA"];
    private static readonly HashSet<string> XObjectKeys = ["Type", "Subtype", "BBox", "Resources", "Matrix", "Length", "Filter", "FormType"];
    private static readonly HashSet<string> ResourceKeys = ["Font", "ProcSet", "XObject", "ExtGState", "ColorSpace"];
    private static readonly HashSet<string> FontKeys = ["Type", "Subtype", "BaseFont", "Encoding", "FirstChar", "LastChar", "Widths", "FontDescriptor", "ToUnicode", "DescendantFonts", "Name", "CIDSystemInfo", "W", "DW", "CIDToGIDMap"];
    private static readonly HashSet<string> FontDescriptorKeys = ["Type", "FontName", "Flags", "FontBBox", "ItalicAngle", "Ascent", "Descent", "CapHeight", "StemV", "StemH", "XHeight", "Leading", "MaxWidth", "AvgWidth", "MissingWidth", "FontFamily", "FontStretch", "FontWeight", "CharSet", "FontFile", "FontFile2", "FontFile3", "CIDSet"];
    private static readonly HashSet<string> FontFileKeys = ["Length", "Length1", "Length2", "Length3", "Filter", "Subtype", "DecodeParms"];
    private static readonly HashSet<string> EncodingKeys = ["Type", "BaseEncoding", "Differences"];
    private static readonly HashSet<string> GStateKeys = ["Type", "CA", "ca", "LW", "LC", "LJ", "ML", "BM"];
    private static readonly HashSet<string> TrailerKeys = ["Size", "Root", "Info", "Prev", "ID"];

    public static string? Check(byte[] file, long originalLength) => Inspect(file, originalLength).Problem;

    public static UpdateInspection Inspect(byte[] file, long originalLength)
    {
        var roles = new Dictionary<int, string>();
        try { return new(new Run(file, originalLength, roles).Go(), roles); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or OverflowException or IndexOutOfRangeException or InvalidOperationException or KeyNotFoundException)
        { return new("structura actualizării nu poate fi citită (" + ex.Message + ")", roles); }
    }

    private sealed class Run(byte[] file, long originalLength, Dictionary<int, string> roles)
    {
        private PdfStructure _st = null!;
        private readonly Dictionary<int, PdfIndirectObject> _new = [];
        private int _page = -1, _widget = -1;
        private double[] _media = [0, 0, 595, 842];

        public string? Go()
        {
            int L = (int)originalLength;
            var original = file.AsSpan(0, L).ToArray();
            _st = PdfStructure.Parse(original);
            var p = new PdfParser(file, L, file.Length);

            // 1. the objects of the update, in order, then exactly one classic xref table, the trailer, startxref and %%EOF
            var objects = new List<PdfIndirectObject>();
            while (true)
            {
                if (p.PeekKeyword() == "xref") break;
                if (objects.Count > 2000) return "actualizare prea mare";
                objects.Add(p.ReadIndirect());
            }
            p.SkipWhiteAndComments();
            long xrefAt = p.Position;
            p.ExpectKeyword("xref");
            var entries = new List<(int Num, long Offset, int Gen, bool InUse)>();
            while (p.PeekKeyword() != "trailer")
            {
                if (p.ReadValue() is not PdfNumber { Text: var s } || p.ReadValue() is not PdfNumber { Text: var c } || !int.TryParse(s, out var start) || !int.TryParse(c, out var count) || count is < 0 or > 10000)
                    return "tabelul de referințe al actualizării nu poate fi citit";
                for (int i = 0; i < count; i++)
                {
                    if (p.ReadValue() is not PdfNumber off || p.ReadValue() is not PdfNumber gen || p.ReadValue() is not PdfKeyword { Value: "n" or "f" } kind)
                        return "intrare invalidă în tabelul de referințe al actualizării";
                    entries.Add((start + i, (long)off.Value, (int)gen.Value, kind.Value == "n"));
                }
            }
            p.ExpectKeyword("trailer");
            if (p.ReadValue() is not PdfDict trailer) return "trailer invalid";
            p.ExpectKeyword("startxref");
            if (p.ReadValue() is not PdfNumber sx || (long)sx.Value != xrefAt) return "startxref nu indică tabelul de referințe al actualizării";
            var rest = Encoding.Latin1.GetString(file, p.Position, file.Length - p.Position);
            if (rest.Trim('\r', '\n', ' ', '\t', '\0', '\f') != "%%EOF") return "după actualizare urmează altceva decât %%EOF";

            if (trailer.Entries.Keys.Any(k => !TrailerKeys.Contains(k))) return "trailerul actualizării are chei neașteptate (" + string.Join(", ", trailer.Entries.Keys.Except(TrailerKeys)) + ")";
            if (trailer["Prev"] is not PdfNumber prev || (long)prev.Value != _st.StartXref) return "actualizarea nu continuă raportul generat (/Prev)";
            if (trailer["Root"] is not PdfRef root || root.Num != _st.Root) return "actualizarea schimbă rădăcina documentului";
            if (_st.Info > 0 && (trailer["Info"] is not PdfRef info || info.Num != _st.Info)) return "actualizarea schimbă informațiile documentului";

            // 2. xref ⇔ parsed objects: every in-use entry points exactly at a parsed object, no free entry, every object has its entry
            var byNum = new Dictionary<int, PdfIndirectObject>();
            foreach (var o in objects)
            {
                if (o.Gen != 0) return $"obiectul {o.Num} are o generație neașteptată";
                if (!byNum.TryAdd(o.Num, o)) return $"obiectul {o.Num} este definit de două ori în actualizare";
            }
            if (entries.Any(e => !e.InUse)) return "tabelul de referințe al actualizării marchează obiecte ca șterse";
            if (entries.Select(e => e.Num).Distinct().Count() != entries.Count) return "tabelul de referințe are intrări dublate";
            foreach (var e in entries)
                if (!byNum.TryGetValue(e.Num, out var o) || o.Offset != e.Offset || e.Gen != 0) return $"intrarea {e.Num} din tabelul de referințe nu indică obiectul parsat";
            if (byNum.Keys.Any(k => !entries.Any(e => e.Num == k))) return "un obiect al actualizării nu are intrare în tabelul de referințe";
            if (trailer["Size"] is not PdfNumber size || size.Value <= byNum.Keys.Max()) return "/Size al actualizării este prea mic";

            // 3. original objects: only the catalog and the one page that gets the widget
            foreach (var (num, o) in byNum)
            {
                if (num >= _st.Size) { _new[num] = o; continue; }
                if (num == _st.Root) continue;
                if (_st.Pages.Contains(num) && _page < 0) { _page = num; continue; }
                return $"obiectul {num} din raportul generat a fost înlocuit";
            }
            if (!byNum.TryGetValue(_st.Root, out var catNew) || catNew.Value is not PdfDict cat) return "catalogul nu a primit formularul semnăturii";
            if (_page < 0 || byNum[_page].Value is not PdfDict page) return "nicio pagină nu a primit semnătura";
            var catOld = Original(_st.Root); var pageOld = Original(_page);
            if (catOld.Entries.ContainsKey("AcroForm") || !SameExcept(catOld, cat, "AcroForm")) return "catalogul documentului a fost schimbat dincolo de semnătură";
            if (pageOld.Entries.ContainsKey("Annots") || !SameExcept(pageOld, page, "Annots")) return $"pagina (obiectul {_page}) a fost schimbată";
            if (pageOld["MediaBox"] is PdfArray mb && Nums(mb, 4) is { } m) _media = m;
            roles[_st.Root] = "catalog"; roles[_page] = "page";

            // 4. from the catalog's /AcroForm and the page's /Annots: every new object reached exactly once, with its role's key whitelist
            var form = Deref(cat["AcroForm"], "acroform") as PdfDict ?? throw new FormatException("/AcroForm nu este un dicționar");
            if (Keys(form, AcroFormKeys) is { } bad1) return "formularul (AcroForm) are chei nepermise: " + bad1;
            if (form["Fields"] is not PdfArray { Items: [PdfRef fieldRef] }) return "formularul trebuie să aibă exact un câmp, semnătura";
            if (form["SigFlags"] is not (null or PdfNumber)) return "SigFlags invalid";
            if (form["DA"] is not (null or PdfString)) return "DA invalid";
            if (form["DR"] is PdfDict dr) { if (Resources(dr) is { } bad) return bad; } else if (form["DR"] is not null) return "DR invalid";
            if (page["Annots"] is not PdfArray { Items: [PdfRef annotRef] } || !annotRef.Is(fieldRef)) return "pagina trebuie să primească exact o adnotare: widgetul semnăturii";
            if (Widget(fieldRef) is { } bad2) return bad2;
            foreach (var num in _new.Keys) if (!roles.ContainsKey(num)) return $"actualizarea conține un obiect în plus (obiectul {num})";
            return null;
        }

        private PdfDict Original(int num) =>
            PdfParser.ObjectAt(file, _st.Offsets[num], (int)originalLength).Value as PdfDict ?? throw new FormatException($"obiectul original {num} nu este un dicționar");

        private static bool SameExcept(PdfDict before, PdfDict after, string added) =>
            after.Entries.ContainsKey(added) && after.Entries.Count == before.Entries.Count + 1 &&
            before.Entries.All(kv => after.Entries.TryGetValue(kv.Key, out var v) && PdfParser.Same(kv.Value, v));

        private static string? Keys(PdfDict d, HashSet<string> allowed) =>
            d.Entries.Keys.Where(k => !allowed.Contains(k)).ToList() is { Count: > 0 } extra ? string.Join(", ", extra) : null;

        private static double[]? Nums(PdfArray a, int n) =>
            a.Items.Count == n && a.Items.All(i => i is PdfNumber) ? a.Items.Cast<PdfNumber>().Select(x => x.Value).ToArray() : null;

        /// <summary>A value that may be a reference to a NEW object of the given role (taken once); an inline value is returned as is. References to original objects are refused.</summary>
        private PdfValue Deref(PdfValue? v, string role, bool stream = false)
        {
            if (v is not PdfRef r) return v ?? throw new FormatException($"lipsește {role}");
            if (!_new.TryGetValue(r.Num, out var o) || r.Gen != 0) throw new FormatException($"{role} trimite la un obiect care nu este nou în actualizare ({r.Num})");
            if (roles.TryGetValue(r.Num, out var had)) throw new FormatException($"obiectul {r.Num} este folosit de două ori ({had}, {role})");
            if (o.IsStream != stream) throw new FormatException($"obiectul {r.Num} ({role}) are formă neașteptată");
            roles[r.Num] = role;
            return o.Value;
        }

        private string? Widget(PdfRef r)
        {
            var w = Deref(r, "widget") as PdfDict ?? throw new FormatException("widget invalid");
            _widget = r.Num;
            if (Keys(w, WidgetKeys) is { } bad) return "widgetul semnăturii are chei nepermise: " + bad;
            if (w.NameOf("Subtype") != "Widget" || w.NameOf("FT") != "Sig" || w["Type"] is not (null or PdfName { Value: "Annot" })) return "adnotarea nu este widgetul unei semnături";
            if (w["P"] is not (null or PdfRef) || w["P"] is PdfRef pr && pr.Num != _page) return "widgetul indică altă pagină";
            if (w["MK"] is not (null or PdfDict) || w["MK"] is PdfDict mk && HasRef(mk)) return "MK invalid";
            if (w["T"] is not (null or PdfString) || w["DA"] is not (null or PdfString) || w["F"] is not (null or PdfNumber) || w["Ff"] is not (null or PdfNumber)) return "câmpuri invalide în widget";
            if (w["Rect"] is not PdfArray ra || Nums(ra, 4) is not { } rect) return "Rect invalid";
            double x0 = Math.Min(rect[0], rect[2]), x1 = Math.Max(rect[0], rect[2]), y0 = Math.Min(rect[1], rect[3]), y1 = Math.Max(rect[1], rect[3]);
            double mx0 = Math.Min(_media[0], _media[2]), mx1 = Math.Max(_media[0], _media[2]), my0 = Math.Min(_media[1], _media[3]), my1 = Math.Max(_media[1], _media[3]);
            if (x0 < mx0 || y0 < my0 || x1 > mx1 || y1 > my1) return "zona semnăturii iese din pagină";
            if ((x1 - x0) * (y1 - y0) > (mx1 - mx0) * (my1 - my0) / 4) return "zona vizibilă a semnăturii acoperă o parte prea mare din pagină";
            if (w["V"] is not PdfRef sigRef) return "widgetul nu are semnătură";
            if (Sig(sigRef) is { } bad2) return bad2;
            if (w["AP"] is { } apv)
            {
                var ap = Deref(apv, "ap") as PdfDict ?? throw new FormatException("AP invalid");
                if (Keys(ap, ["N"]) is { } bad3) return "aspectul semnăturii are chei nepermise: " + bad3;
                if (ap["N"] is not PdfRef n) return "aspectul semnăturii trebuie să fie un flux";
                if (Appearance(n, 0) is { } bad4) return bad4;
            }
            return null;
        }

        private string? Sig(PdfRef r)
        {
            var s = Deref(r, "sig") as PdfDict ?? throw new FormatException("semnătură invalidă");
            if (Keys(s, SigKeys) is { } bad) return "dicționarul semnăturii are chei nepermise: " + bad;
            if (s.NameOf("Type") is not (null or "Sig") || s["Filter"] is not PdfName || s["SubFilter"] is not PdfName || s["ByteRange"] is not PdfArray || s["Contents"] is not PdfString)
                return "dicționarul semnăturii este incomplet";
            foreach (var k in new[] { "M", "Name", "Reason", "Location", "ContactInfo" }) if (s[k] is not (null or PdfString)) return $"/{k} invalid în semnătură";
            if (s["Prop_Build"] is not (null or PdfDict) || s["Prop_Build"] is PdfDict pb && HasRef(pb)) return "Prop_Build invalid";
            return null;
        }

        private string? Appearance(PdfRef r, int depth)
        {
            if (depth > 4) return "aspect imbricat prea adânc";
            var x = Deref(r, "appearance", stream: true) as PdfDict ?? throw new FormatException("aspect invalid");
            if (Keys(x, XObjectKeys) is { } bad) return "aspectul semnăturii are chei nepermise: " + bad;
            if (x.NameOf("Subtype") != "Form" || x.NameOf("Type") is not (null or "XObject")) return "aspectul semnăturii nu este un formular grafic";
            if (x["BBox"] is not PdfArray bb || Nums(bb, 4) is null) return "BBox invalid";
            if (x["Matrix"] is PdfArray ma)
            {
                if (Nums(ma, 6) is not { } m || m[1] != 0 || m[2] != 0 || m[0] <= 0 || m[3] <= 0) return "matricea aspectului rotește sau deformează semnătura";
            }
            else if (x["Matrix"] is not null) return "Matrix invalid";
            if (x["Filter"] is not (null or PdfName { Value: "FlateDecode" })) return "filtru nepermis în aspect";
            if (x["Resources"] is { } rv)
            {
                var res = Deref(rv, "resources") as PdfDict ?? throw new FormatException("Resources invalid");
                if (Keys(res, ResourceKeys) is { } bad2) return "resursele aspectului au chei nepermise: " + bad2;
                if (Resources(res, depth) is { } bad3) return bad3;
            }
            return null;
        }

        private string? Resources(PdfDict res, int depth = 0)
        {
            if (Keys(res, ResourceKeys) is { } bad0) return "resurse cu chei nepermise: " + bad0;
            if (res["ProcSet"] is not (null or PdfArray) || res["ProcSet"] is PdfArray ps && ps.Items.Any(i => i is not PdfName)) return "ProcSet invalid";
            if (res["ColorSpace"] is not (null or PdfDict) || res["ColorSpace"] is PdfDict cs && HasRef(cs)) return "ColorSpace invalid";
            if (res["Font"] is { } fv)
            {
                var fonts = Deref(fv, "fonts") as PdfDict ?? throw new FormatException("Font invalid");
                foreach (var f in fonts.Entries.Values) { if (f is not PdfRef fr) return "font inline nepermis"; if (Font(fr) is { } bad) return bad; }
            }
            if (res["XObject"] is { } xv)
            {
                var xs = Deref(xv, "xobjects") as PdfDict ?? throw new FormatException("XObject invalid");
                foreach (var v in xs.Entries.Values) { if (v is not PdfRef xr) return "XObject inline nepermis"; if (Appearance(xr, depth + 1) is { } bad) return bad; }
            }
            if (res["ExtGState"] is { } gv)
            {
                var gs = Deref(gv, "extgstates") as PdfDict ?? throw new FormatException("ExtGState invalid");
                foreach (var v in gs.Entries.Values)
                {
                    var g = Deref(v, "extgstate") as PdfDict ?? throw new FormatException("ExtGState invalid");
                    if (Keys(g, GStateKeys) is { } bad) return "starea grafică are chei nepermise: " + bad;
                    if (HasRef(g)) return "starea grafică trimite la alte obiecte";
                }
            }
            return null;
        }

        private string? Font(PdfRef r)
        {
            var f = Deref(r, "font") as PdfDict ?? throw new FormatException("font invalid");
            if (Keys(f, FontKeys) is { } bad) return "fontul are chei nepermise: " + bad;
            if (f.NameOf("Type") is not (null or "Font")) return "font invalid";
            if (f["Widths"] is PdfRef wr) Deref(wr, "widths");
            if (f["W"] is PdfRef w2) Deref(w2, "widths");
            if (f["Encoding"] is PdfRef er || f["Encoding"] is PdfDict)
            {
                var e = Deref(f["Encoding"], "encoding") as PdfDict ?? throw new FormatException("Encoding invalid");
                if (Keys(e, EncodingKeys) is { } be) return "codificarea fontului are chei nepermise: " + be;
                if (HasRef(e)) return "codificarea fontului trimite la alte obiecte";
            }
            if (f["ToUnicode"] is PdfRef tu) { var t = Deref(tu, "tounicode", stream: true) as PdfDict; if (t is null || Keys(t, FontFileKeys) is not null) return "ToUnicode invalid"; }
            if (f["CIDToGIDMap"] is PdfRef cg) { var t = Deref(cg, "cidtogid", stream: true) as PdfDict; if (t is null || Keys(t, FontFileKeys) is not null) return "CIDToGIDMap invalid"; }
            if (f["CIDSystemInfo"] is not (null or PdfDict)) return "CIDSystemInfo invalid";
            if (f["DescendantFonts"] is PdfArray dfs) foreach (var d in dfs.Items) { if (d is not PdfRef dr || Font(dr) is { } bd) return "font descendent invalid"; }
            if (f["FontDescriptor"] is PdfRef fdr)
            {
                var fd = Deref(fdr, "fontdescriptor") as PdfDict ?? throw new FormatException("FontDescriptor invalid");
                if (Keys(fd, FontDescriptorKeys) is { } bf) return "descriptorul fontului are chei nepermise: " + bf;
                foreach (var k in new[] { "FontFile", "FontFile2", "FontFile3", "CIDSet" })
                    if (fd[k] is PdfRef ff) { var t = Deref(ff, "fontfile", stream: true) as PdfDict; if (t is null || Keys(t, FontFileKeys) is not null) return "fișierul fontului are chei nepermise"; }
            }
            return null;
        }

        private static bool HasRef(PdfValue v) => v switch
        {
            PdfRef => true,
            PdfArray a => a.Items.Any(HasRef),
            PdfDict d => d.Entries.Values.Any(HasRef),
            _ => false,
        };
    }
}
