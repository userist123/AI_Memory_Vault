using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>Result of inspecting the update after the original: the reason it is refused (null when accepted) and the role of every object in it.</summary>
public sealed record UpdateInspection(string? Problem, IReadOnlyDictionary<int, string> Roles);

/// <summary>
/// What may follow the generated report in a signed file: EXACTLY ONE incremental update, read with a real PDF parser (<see cref="PdfParser"/>) and an
/// exact classic cross-reference grammar, which only FILLS the signature field reserved at generation (<see cref="SignatureField"/>):
/// <list type="bullet">
/// <item>original objects: only the reserved widget (identical, plus /V and /AP) and the AcroForm (identical, plus /SigFlags, /DR, /DA); never the page or the catalog;</item>
/// <item>new objects, each reachable from the widget's /V or /AP (or the AcroForm's /DR), with an allowed set of keys for its role: exactly one signature
/// dictionary, appearance form XObjects the size of the reserved box (no matrix other than identity, no images, no XObject calls, only text and simple
/// path operators) and their fonts.</item>
/// </list>
/// Anything else is refused: "raportul a fost modificat înainte de semnare".
/// </summary>
public static class PdfUpdateInspector
{
    private static readonly HashSet<string> SigKeys = ["Type", "Filter", "SubFilter", "ByteRange", "Contents", "M", "Name", "Reason", "Location", "ContactInfo", "Prop_Build"];
    private static readonly HashSet<string> AcroFormMayAdd = ["SigFlags", "DR", "DA"];
    private static readonly HashSet<string> XObjectKeys = ["Type", "Subtype", "BBox", "Resources", "Matrix", "Length", "Filter", "FormType"];
    private static readonly HashSet<string> ResourceKeys = ["Font", "ProcSet"];
    private static readonly HashSet<string> FontKeys = ["Type", "Subtype", "BaseFont", "Encoding", "FirstChar", "LastChar", "Widths", "FontDescriptor", "ToUnicode", "DescendantFonts", "Name", "CIDSystemInfo", "W", "DW", "CIDToGIDMap"];
    private static readonly HashSet<string> FontDescriptorKeys = ["Type", "FontName", "Flags", "FontBBox", "ItalicAngle", "Ascent", "Descent", "CapHeight", "StemV", "StemH", "XHeight", "Leading", "MaxWidth", "AvgWidth", "MissingWidth", "FontFamily", "FontStretch", "FontWeight", "CharSet", "FontFile", "FontFile2", "FontFile3", "CIDSet"];
    private static readonly HashSet<string> FontFileKeys = ["Length", "Length1", "Length2", "Length3", "Filter", "Subtype"];
    private static readonly HashSet<string> EncodingKeys = ["Type", "BaseEncoding", "Differences"];
    private static readonly HashSet<string> TrailerKeys = ["Size", "Root", "Info", "Prev", "ID"];
    /// <summary>Content operators allowed in the appearance: text and simple paths only (no images, no XObjects, no graphics-state dictionaries).</summary>
    private static readonly HashSet<string> Operators = ["BT", "ET", "Tf", "Td", "TD", "Tm", "T*", "Tj", "TJ", "'", "\"", "Tc", "Tw", "Tz", "TL", "Ts",
                                                         "g", "G", "rg", "RG", "k", "K", "re", "m", "l", "c", "h", "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n", "q", "Q", "cm", "w", "W", "W*"];

    public static string? Check(byte[] file, long originalLength) => Inspect(file, originalLength).Problem;

    public static UpdateInspection Inspect(byte[] file, long originalLength)
    {
        var roles = new Dictionary<int, string>();
        try { return new(new Run(file, originalLength, roles).Go(), roles); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or OverflowException or IndexOutOfRangeException
                                       or InvalidOperationException or KeyNotFoundException or InvalidDataException)
        { return new("structura actualizării nu poate fi citită (" + ex.Message + ")", roles); }
    }

    private sealed class Run(byte[] file, long originalLength, Dictionary<int, string> roles)
    {
        private PdfStructure _st = null!;
        private readonly Dictionary<int, PdfIndirectObject> _new = [];
        private double _boxW, _boxH;

        public string? Go()
        {
            int L = (int)originalLength;
            _st = PdfStructure.Parse(file.AsSpan(0, L).ToArray(), 2);
            var field = SignatureField.Find(_st) ?? throw new FormatException("raportul generat nu are câmpul de semnătură rezervat");
            _boxW = Math.Abs(field.Rect[2] - field.Rect[0]); _boxH = Math.Abs(field.Rect[3] - field.Rect[1]);
            var p = new PdfParser(file, L, file.Length);

            // 1. the objects of the update, then exactly one classic xref table, the trailer, startxref and %%EOF
            var objects = new List<PdfIndirectObject>();
            while (true)
            {
                if (p.PeekKeyword() == "xref") break;
                if (objects.Count > 2000) return "actualizare prea mare";
                objects.Add(p.ReadIndirect());
            }
            p.SkipWhiteAndComments();
            int xrefAt = p.Position;
            var entries = ReadXref(xrefAt, out int afterXref);
            p.Position = afterXref;
            p.ExpectKeyword("trailer");
            if (p.ReadValue() is not PdfDict trailer) return "trailer invalid";
            p.ExpectKeyword("startxref");
            if (p.ReadValue() is not PdfNumber sx || !IsInt(sx) || (long)sx.Value != xrefAt) return "startxref nu indică tabelul de referințe al actualizării";
            var rest = Encoding.Latin1.GetString(file, p.Position, file.Length - p.Position);
            if (rest.Trim('\r', '\n', ' ', '\t', '\0', '\f') != "%%EOF") return "după actualizare urmează altceva decât %%EOF";

            if (trailer.Entries.Keys.Any(k => !TrailerKeys.Contains(k))) return "trailerul actualizării are chei neașteptate (" + string.Join(", ", trailer.Entries.Keys.Except(TrailerKeys)) + ")";
            if (trailer["Prev"] is not PdfNumber prev || !IsInt(prev) || (long)prev.Value != _st.StartXref) return "actualizarea nu continuă raportul generat (/Prev)";
            if (trailer["Root"] is not PdfRef root || root.Num != _st.Root || root.Gen != 0) return "actualizarea schimbă rădăcina documentului";
            if (_st.Info > 0 && (trailer["Info"] is not PdfRef info || info.Num != _st.Info || info.Gen != 0)) return "actualizarea schimbă informațiile documentului";
            if (_st.Info == 0 && trailer["Info"] is not null) return "actualizarea adaugă informații documentului";

            // 2. xref ⇔ parsed objects
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
            if (trailer["Size"] is not PdfNumber size || !IsInt(size) || size.Value <= byNum.Keys.Max()) return "/Size al actualizării este prea mic";

            // 3. original objects: only the reserved widget and the AcroForm
            foreach (var (num, o) in byNum)
            {
                if (num >= _st.Size) { _new[num] = o; continue; }
                if (num == field.Widget || num == field.Form) continue;
                return $"obiectul {num} din raportul generat a fost înlocuit (doar câmpul de semnătură rezervat se completează)";
            }
            if (!byNum.TryGetValue(field.Widget, out var wNew) || wNew.Value is not PdfDict widget) return "câmpul de semnătură rezervat nu a fost completat";
            var wOld = Original(field.Widget);
            if (wOld.Entries.ContainsKey("V") || wOld.Entries.ContainsKey("AP")) return "câmpul rezervat al raportului generat nu era gol";
            if (!SameExcept(wOld, widget, ["V", "AP"], required: true)) return "widgetul semnăturii a fost schimbat (doar /V și /AP se pot adăuga câmpului rezervat)";
            roles[field.Widget] = "widget";
            if (byNum.TryGetValue(field.Form, out var fNew))
            {
                if (fNew.Value is not PdfDict form || !SameExcept(Original(field.Form), form, AcroFormMayAdd, required: false)) return "formularul (AcroForm) a fost schimbat dincolo de semnătură";
                if (form["SigFlags"] is not (null or PdfNumber) || form["DA"] is not (null or PdfString)) return "AcroForm invalid";
                if (form["DR"] is { } drv)
                {
                    var dr = Deref(drv, "dr") as PdfDict ?? throw new FormatException("DR invalid");
                    if (Resources(dr) is { } bad) return bad;
                }
                roles[field.Form] = "acroform";
            }

            // 4. the signature and its appearance, reached from the widget
            if (widget["V"] is not PdfRef sigRef) return "widgetul nu are semnătură";
            if (Sig(sigRef) is { } badSig) return badSig;
            var ap = Deref(widget["AP"], "ap") as PdfDict ?? throw new FormatException("AP invalid");
            if (ap.Entries.Keys.Any(k => k != "N") || ap["N"] is not PdfRef n) return "aspectul semnăturii trebuie să fie un singur formular grafic (/N)";
            if (Appearance(n) is { } badAp) return badAp;
            if (_new.Values.Count(o => o.Value is PdfDict d && d.NameOf("Type") == "Sig") != 1) return "actualizarea trebuie să conțină exact o semnătură";
            foreach (var num in _new.Keys) if (!roles.ContainsKey(num)) return $"actualizarea conține un obiect în plus (obiectul {num})";
            return null;
        }

        private static bool IsInt(PdfNumber n) => n.Text.Length is > 0 and <= 12 && n.Text.All(char.IsAsciiDigit);

        /// <summary>The exact classic xref grammar: "xref" EOL; "N M" EOL; entries of exactly 20 bytes "nnnnnnnnnn ggggg n|f" + (SP CR | SP LF | CR LF); no comments.</summary>
        private List<(int Num, long Offset, int Gen, bool InUse)> ReadXref(int at, out int end)
        {
            var list = new List<(int, long, int, bool)>();
            int pos = at;
            if (!Match(pos, "xref")) throw new FormatException("lipsește tabelul de referințe");
            pos += 4;
            pos = Eol(pos) ?? throw new FormatException("după „xref” trebuie un sfârșit de linie");
            while (!Match(pos, "trailer"))
            {
                int lineEnd = pos;
                while (lineEnd < file.Length && file[lineEnd] is not ((byte)'\r' or (byte)'\n') && lineEnd - pos < 32) lineEnd++;
                var header = Encoding.ASCII.GetString(file, pos, lineEnd - pos);
                var m = Regex.Match(header, @"^(\d{1,9}) (\d{1,5})$");
                if (!m.Success) throw new FormatException("antet de subsecțiune invalid în tabelul de referințe");
                pos = Eol(lineEnd) ?? throw new FormatException("antet de subsecțiune fără sfârșit de linie");
                int start = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), count = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (count > 10000) throw new FormatException("subsecțiune prea mare");
                for (int i = 0; i < count; i++)
                {
                    if (pos + 20 > file.Length) throw new FormatException("tabel de referințe trunchiat");
                    var e = Encoding.ASCII.GetString(file, pos, 20);
                    var em = Regex.Match(e, @"^(\d{10}) (\d{5}) ([nf])( \r| \n|\r\n)$");
                    if (!em.Success) throw new FormatException("intrare care nu are exact 20 de octeți în tabelul de referințe");
                    list.Add((start + i, long.Parse(em.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(em.Groups[2].Value, CultureInfo.InvariantCulture), em.Groups[3].Value == "n"));
                    pos += 20;
                }
                if (list.Count > 20000) throw new FormatException("tabel de referințe prea mare");
            }
            end = pos;
            return list;
        }

        private bool Match(int pos, string s) => pos + s.Length <= file.Length && Encoding.ASCII.GetString(file, pos, s.Length) == s;

        private int? Eol(int pos)
        {
            if (pos < file.Length && file[pos] == '\r') return pos + 1 < file.Length && file[pos + 1] == '\n' ? pos + 2 : pos + 1;
            if (pos < file.Length && file[pos] == '\n') return pos + 1;
            return null;
        }

        private PdfDict Original(int num) =>
            PdfParser.ObjectAt(file, _st.Offsets[num], (int)originalLength).Value as PdfDict ?? throw new FormatException($"obiectul original {num} nu este un dicționar");

        /// <summary>Same entries, except <paramref name="added"/> keys that may be added (all of them when <paramref name="required"/>).</summary>
        private static bool SameExcept(PdfDict before, PdfDict after, IReadOnlyCollection<string> added, bool required)
        {
            if (required && added.Any(k => !after.Entries.ContainsKey(k))) return false;
            if (before.Entries.Keys.Any(added.Contains)) return false;
            return after.Entries.Keys.All(k => before.Entries.ContainsKey(k) || added.Contains(k)) &&
                   before.Entries.All(kv => after.Entries.TryGetValue(kv.Key, out var v) && PdfParser.Same(kv.Value, v));
        }

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

        /// <summary>The appearance: a form XObject exactly the size of the reserved box, identity matrix, fonts only, text and simple path operators only.</summary>
        private string? Appearance(PdfRef r)
        {
            var x = Deref(r, "appearance", stream: true) as PdfDict ?? throw new FormatException("aspect invalid");
            if (Keys(x, XObjectKeys) is { } bad) return "aspectul semnăturii are chei nepermise: " + bad;
            if (x.NameOf("Subtype") != "Form" || x.NameOf("Type") is not (null or "XObject")) return "aspectul semnăturii nu este un formular grafic";
            if (x["BBox"] is not PdfArray bb || Nums(bb, 4) is not { } box) return "BBox invalid";
            if (Math.Abs(Math.Abs(box[2] - box[0]) - _boxW) > 0.5 || Math.Abs(Math.Abs(box[3] - box[1]) - _boxH) > 0.5) return "aspectul semnăturii nu are dimensiunea câmpului rezervat";
            if (x["Matrix"] is PdfArray ma) { if (Nums(ma, 6) is not { } m || !m.SequenceEqual([1d, 0, 0, 1, 0, 0])) return "aspectul semnăturii are o matrice de transformare"; }
            else if (x["Matrix"] is not null) return "Matrix invalid";
            if (x["Resources"] is { } rv)
            {
                var res = Deref(rv, "resources") as PdfDict ?? throw new FormatException("Resources invalid");
                if (Resources(res) is { } bad2) return bad2;
            }
            var o = _new[r.Num];
            return Content(Decode(o, x, AppearanceLimit), box);
        }

        private const int AppearanceLimit = 1_000_000, FontProgramLimit = 4 * 1024 * 1024;

        /// <summary>The stream data, decoded (none or FlateDecode), refused beyond <paramref name="limit"/> bytes (a small stream can inflate enormously).</summary>
        private byte[] Decode(PdfIndirectObject o, PdfDict d, int limit)
        {
            if (o.StreamLength > limit) throw new FormatException("flux prea mare");
            var raw = file.AsSpan((int)o.StreamStart, (int)o.StreamLength).ToArray();
            var filter = d["Filter"];
            if (filter is null) return raw;
            if (filter is not PdfName { Value: "FlateDecode" }) throw new FormatException("filtru nepermis în flux");
            using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var outp = new MemoryStream();
            var buf = new byte[8192];
            for (int k; (k = z.Read(buf)) > 0;)
            {
                outp.Write(buf, 0, k);
                if (outp.Length > limit) throw new FormatException("flux prea mare după decomprimare");
            }
            return outp.ToArray();
        }

        /// <summary>
        /// The appearance content: it must START with the clip to its own box ("[q] x0 y0 w h re W n") and keep that clip in force to the end: no Q may go below
        /// the graphics-state level of the clip, except the single Q that closes its leading q, which must be the last operator. It may use only text and simple
        /// path operators. (Viewers also clip a form XObject to its BBox, which must equal the reserved box.)
        /// </summary>
        private static string? Content(byte[] data, double[] box)
        {
            var p = new PdfParser(data, 0, data.Length);
            var first = new List<PdfValue>();
            for (int i = 0; i < 8; i++)
            {
                p.SkipWhiteAndComments();
                if (p.Position >= data.Length) break;
                first.Add(p.ReadValue());
            }
            int k0 = first.Count > 0 && first[0] is PdfKeyword { Value: "q" } ? 1 : 0;
            double[] want = [Math.Min(box[0], box[2]), Math.Min(box[1], box[3]), Math.Abs(box[2] - box[0]), Math.Abs(box[3] - box[1])];
            bool clip = first.Count >= k0 + 7
                && Enumerable.Range(0, 4).All(i => first[k0 + i] is PdfNumber nmb && Math.Abs(nmb.Value - want[i]) < 0.01)
                && first[k0 + 4] is PdfKeyword { Value: "re" } && first[k0 + 5] is PdfKeyword { Value: "W" } && first[k0 + 6] is PdfKeyword { Value: "n" };
            if (!clip) return "aspectul semnăturii nu începe cu decuparea la chenarul lui (0 0 lățime înălțime re W n)";
            // the clip stays in force to the end: the graphics-state depth right after "W n" is the clip's level; a Q may never go below it, except one Q that
            // closes the leading q of the clip and is then the very last operator
            int clipLevel = k0, depth = 0, opIndex = 0;
            bool closed = false;
            p = new PdfParser(data, 0, data.Length);
            int ops = 0;
            while (true)
            {
                p.SkipWhiteAndComments();
                if (p.Position >= data.Length) return null;
                var v = p.ReadValue();
                if (v is PdfKeyword k)
                {
                    if (!Operators.Contains(k.Value)) return $"aspectul semnăturii folosește operatorul nepermis „{k.Value}” (se permit doar text și linii simple)";
                    if (++ops > 20000) return "aspect prea complex";
                    if (closed) return "aspectul semnăturii desenează după ce a închis decuparea la chenar";
                    opIndex++;
                    if (k.Value == "q") { if (++depth > 64) return "aspect prea complex"; }
                    else if (k.Value == "Q")
                    {
                        if (depth == 0) return "aspectul semnăturii are un Q fără q (ar anula decuparea la chenar)";
                        depth--;
                        if (opIndex > 4 && depth < clipLevel) closed = true;     // the Q of the leading q: allowed only as the last operator
                    }
                }
                else if (v is PdfDict or PdfRef) return "aspectul semnăturii conține dicționare sau trimiteri nepermise";
            }
        }

        private string? Resources(PdfDict res)
        {
            if (Keys(res, ResourceKeys) is { } bad0) return "resursele aspectului au chei nepermise: " + bad0;
            if (res["ProcSet"] is not (null or PdfArray) || res["ProcSet"] is PdfArray ps && ps.Items.Any(i => i is not PdfName)) return "ProcSet invalid";
            if (res["Font"] is { } fv)
            {
                var fonts = Deref(fv, "fonts") as PdfDict ?? throw new FormatException("Font invalid");
                foreach (var f in fonts.Entries.Values) { if (f is not PdfRef fr) return "font inline nepermis"; if (Font(fr) is { } bad) return bad; }
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
            if (f["Encoding"] is PdfRef || f["Encoding"] is PdfDict)
            {
                var e = Deref(f["Encoding"], "encoding") as PdfDict ?? throw new FormatException("Encoding invalid");
                if (Keys(e, EncodingKeys) is { } be) return "codificarea fontului are chei nepermise: " + be;
                if (HasRef(e)) return "codificarea fontului trimite la alte obiecte";
            }
            if (f["ToUnicode"] is PdfRef tu && FontFile(tu, "tounicode") is { } b1) return b1;
            if (f["CIDToGIDMap"] is PdfRef cg && FontFile(cg, "cidtogid") is { } b2) return b2;
            if (f["CIDSystemInfo"] is not (null or PdfDict)) return "CIDSystemInfo invalid";
            if (f["DescendantFonts"] is PdfArray dfs) foreach (var d in dfs.Items) { if (d is not PdfRef dr || Font(dr) is not null) return "font descendent invalid"; }
            if (f["FontDescriptor"] is PdfRef fdr)
            {
                var fd = Deref(fdr, "fontdescriptor") as PdfDict ?? throw new FormatException("FontDescriptor invalid");
                if (Keys(fd, FontDescriptorKeys) is { } bf) return "descriptorul fontului are chei nepermise: " + bf;
                foreach (var k in new[] { "FontFile", "FontFile2", "FontFile3", "CIDSet" })
                    if (fd[k] is PdfRef ff && FontFile(ff, "fontfile") is { } b3) return b3;
            }
            return null;
        }

        /// <summary>Font program, ToUnicode or CIDToGIDMap stream: allowed keys only, /Filter only FlateDecode or none, no /DecodeParms.</summary>
        private string? FontFile(PdfRef r, string role)
        {
            var t = Deref(r, role, stream: true) as PdfDict;
            if (t is null || Keys(t, FontFileKeys) is not null) return $"fluxul fontului ({role}) are chei nepermise";
            if (t["Filter"] is not (null or PdfName { Value: "FlateDecode" })) return $"fluxul fontului ({role}) folosește un filtru nepermis";
            try { Decode(_new[r.Num], t, FontProgramLimit); }
            catch (Exception ex) when (ex is FormatException or InvalidDataException) { return $"fluxul fontului ({role}) este prea mare sau nu poate fi decomprimat ({ex.Message})"; }
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
