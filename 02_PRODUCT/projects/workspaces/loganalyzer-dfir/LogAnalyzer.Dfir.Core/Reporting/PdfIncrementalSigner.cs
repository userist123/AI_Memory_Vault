using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>Produces the detached CMS (CAdES) over the bytes covered by /ByteRange. The application implements it with SignedCms + the card key.</summary>
public interface ICmsDetachedSigner
{
    byte[] Sign(byte[] content);
}

/// <summary>What the visible signature box says (ASCII is printed; Romanian diacritics are transliterated for the standard PDF font).</summary>
public sealed record SignatureAppearance(string SignerName, string Issuer, DateTimeOffset SigningTime, string Location, string Reason);

/// <summary>
/// PAdES B-B signature of a PDF that LogAnalyzer generated (QuestPDF: PDF 1.4, classic cross-reference table, uncompressed object dictionaries), as ONE
/// incremental update appended to the unchanged original bytes: signature dictionary (/Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached, /ByteRange,
/// /Contents, /M), a signature field merged with its widget on the last page with a visible appearance, the last page and the catalog rewritten with
/// /Annots and /AcroForm, an xref section for only those objects and a trailer with /Prev. Anything else (cross-reference streams, object streams, an existing
/// form or annotations array by reference) is refused rather than guessed.
/// </summary>
public static class PdfIncrementalSigner
{
    public const int ContentsBytes = 16384;
    private const string ByteRangePlaceholder = "[0 ********** ********** **********]";

    public static byte[] Sign(byte[] original, SignatureAppearance look, ICmsDetachedSigner signer)
    {
        var tail = Tail.Parse(original);
        int n = tail.Size;
        int sigObj = n, fieldObj = n + 1, apObj = n + 2, fontObj = n + 3;
        var objects = new SortedDictionary<int, string>();

        var rect = "[300 55 569 110]";
        var lines = new[]
        {
            $"Semnat de {Ascii(look.SignerName)} - ofiter de securitate",
            $"Data: {look.SigningTime.ToUniversalTime():yyyy-MM-dd HH:mm} UTC",
            $"Emitent certificat: {Ascii(look.Issuer)}",
        };
        var stream = new StringBuilder("q 0.95 0.95 0.95 rg 0 0 269 55 re f 0.2 0.2 0.2 RG 0.8 w 0.4 0.4 268.2 54.2 re S Q\nBT /F1 7.5 Tf 0 0 0 rg\n");
        for (int i = 0; i < lines.Length; i++) stream.Append(CultureInfo.InvariantCulture, $"1 0 0 1 6 {42 - i * 14} Tm ({Esc(Trim(lines[i], 70))}) Tj\n");
        stream.Append("ET\n");
        var streamText = stream.ToString();

        objects[fontObj] = "<</Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding>>";
        objects[apObj] = $"<</Type /XObject /Subtype /Form /BBox [0 0 269 55] /Resources <</Font <</F1 {fontObj} 0 R>>>> /Length {Encoding.ASCII.GetByteCount(streamText)}>>\nstream\n{streamText}endstream";
        objects[fieldObj] = $"<</Type /Annot /Subtype /Widget /FT /Sig /T (Semnatura ofiter de securitate) /V {sigObj} 0 R /Rect {rect} /F 132 /P {tail.LastPage} 0 R /AP <</N {apObj} 0 R>>>>";
        objects[tail.LastPage] = AddEntry(tail.LastPageDict, "/Annots", tail.LastPageDict.Contains("/Annots") ? throw new NotSupportedException("pagina are deja adnotări") : $"[{fieldObj} 0 R]");
        objects[tail.Root] = AddEntry(tail.RootDict, "/AcroForm", tail.RootDict.Contains("/AcroForm") ? throw new NotSupportedException("documentul are deja un formular") : $"<</Fields [{fieldObj} 0 R] /SigFlags 3>>");
        var placeholder = new string('0', ContentsBytes * 2);
        objects[sigObj] = $"<</Type /Sig /Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached /ByteRange {ByteRangePlaceholder} /Contents <{placeholder}> " +
                          $"/M ({PdfDate(look.SigningTime)}) /Name ({Esc(Ascii(look.SignerName))}) /Reason ({Esc(Ascii(look.Reason))}) /Location ({Esc(Ascii(look.Location))})>>";

        // body of the update, offsets measured as written
        using var ms = new MemoryStream();
        ms.Write(original);
        if (original[^1] != (byte)'\n') ms.WriteByte((byte)'\n');
        var offsets = new SortedDictionary<int, long>();
        foreach (var (num, body) in objects)
        {
            offsets[num] = ms.Position;
            ms.Write(Encoding.Latin1.GetBytes($"{num} 0 obj\n{body}\nendobj\n"));
        }
        long xref = ms.Position;
        var x = new StringBuilder("xref\n");
        foreach (var run in Runs(offsets.Keys.ToList()))
        {
            x.Append(CultureInfo.InvariantCulture, $"{run[0]} {run.Count}\n");
            foreach (var num in run) x.Append(CultureInfo.InvariantCulture, $"{offsets[num]:D10} 00000 n \n");
        }
        x.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size {fontObj + 1} /Root {tail.Root} 0 R{(tail.Info > 0 ? $" /Info {tail.Info} 0 R" : "")}{tail.IdEntry} /Prev {tail.StartXref}>>\nstartxref\n{xref}\n%%EOF\n");
        ms.Write(Encoding.ASCII.GetBytes(x.ToString()));
        var bytes = ms.ToArray();

        // /ByteRange: everything except the /Contents hex string (with its < >), then the CMS over exactly those bytes
        int sigStart = (int)offsets[sigObj];
        int lt = IndexOf(bytes, "/Contents <"u8, sigStart) + "/Contents ".Length;
        int gt = lt + 1 + ContentsBytes * 2;
        if (bytes[gt] != (byte)'>') throw new InvalidOperationException("locul semnăturii nu a fost găsit");
        var br = $"[0 {lt,-10} {gt + 1,-10} {bytes.Length - gt - 1,-10}]";
        if (br.Length != ByteRangePlaceholder.Length) throw new InvalidOperationException("intervalul semnat nu încape în rezervare");
        int brAt = IndexOf(bytes, Encoding.ASCII.GetBytes(ByteRangePlaceholder), sigStart);
        Encoding.ASCII.GetBytes(br).CopyTo(bytes, brAt);
        var content = new byte[lt + bytes.Length - gt - 1];
        Buffer.BlockCopy(bytes, 0, content, 0, lt);
        Buffer.BlockCopy(bytes, gt + 1, content, lt, bytes.Length - gt - 1);
        var cms = signer.Sign(content);
        if (cms.Length > ContentsBytes) throw new InvalidOperationException($"semnătura ({cms.Length} octeți) nu încape în spațiul rezervat ({ContentsBytes} octeți)");
        Encoding.ASCII.GetBytes(Convert.ToHexString(cms)).CopyTo(bytes, lt + 1);
        return bytes;
    }

    private static IEnumerable<List<int>> Runs(List<int> nums)
    {
        var run = new List<int>();
        foreach (var n in nums)
        {
            if (run.Count > 0 && n != run[^1] + 1) { yield return run; run = []; }
            run.Add(n);
        }
        if (run.Count > 0) yield return run;
    }

    private static string AddEntry(string dict, string key, string value)
    {
        int end = dict.LastIndexOf(">>", StringComparison.Ordinal);
        return dict[..end] + $"\n{key} {value}" + dict[end..];
    }

    private static string PdfDate(DateTimeOffset t) => "D:" + t.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";

    /// <summary>Romanian letters to ASCII for the standard font, everything else outside printable ASCII to '?'.</summary>
    public static string Ascii(string s)
    {
        var map = new Dictionary<char, char> { ['ă'] = 'a', ['â'] = 'a', ['î'] = 'i', ['ș'] = 's', ['ş'] = 's', ['ț'] = 't', ['ţ'] = 't', ['Ă'] = 'A', ['Â'] = 'A', ['Î'] = 'I', ['Ș'] = 'S', ['Ş'] = 'S', ['Ț'] = 'T', ['Ţ'] = 'T' };
        return new string(s.Select(c => map.TryGetValue(c, out var r) ? r : c is >= ' ' and <= '~' ? c : '?').ToArray());
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

    private static int IndexOf(byte[] b, ReadOnlySpan<byte> pattern, int from)
    {
        int i = b.AsSpan(from).IndexOf(pattern);
        return i < 0 ? throw new InvalidOperationException("structură PDF neașteptată") : from + i;
    }

    /// <summary>What the update needs from the end of the original: the last cross-reference table, trailer, catalog and last page (bounded parsing).</summary>
    private sealed class Tail
    {
        public int Size, Root, Info, LastPage;
        public long StartXref;
        public string RootDict = "", LastPageDict = "", IdEntry = "";
        private readonly Dictionary<int, long> _offsets = [];
        private byte[] _b = [];

        public static Tail Parse(byte[] b)
        {
            var t = new Tail { _b = b };
            if (b.Length < 16 || !b.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new NotSupportedException("fișierul nu este un PDF");
            if (b.AsSpan().IndexOf("/ByteRange"u8) >= 0) throw new NotSupportedException("documentul este deja semnat");
            int sx = b.AsSpan().LastIndexOf("startxref"u8);
            if (sx < 0) throw new NotSupportedException("PDF fără startxref");
            var after = Encoding.ASCII.GetString(b, sx + 9, Math.Min(40, b.Length - sx - 9));
            var m = Regex.Match(after, @"^\s*(\d{1,12})\s*%%EOF");
            if (!m.Success) throw new NotSupportedException("sfârșitul PDF-ului nu poate fi citit");
            t.StartXref = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (t.StartXref <= 0 || t.StartXref >= sx || !b.AsSpan((int)t.StartXref).StartsWith("xref"u8))
                throw new NotSupportedException("PDF-ul nu are un tabel clasic de referințe (xref); doar rapoartele generate de aplicație pot fi semnate");
            int tr = b.AsSpan((int)t.StartXref, sx - (int)t.StartXref).IndexOf("trailer"u8);
            if (tr < 0) throw new NotSupportedException("PDF fără trailer");
            var xrefText = Encoding.ASCII.GetString(b, (int)t.StartXref + 4, tr - 4);
            var lines = xrefText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int cur = -1;
            foreach (var line in lines)
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2) { cur = int.Parse(p[0], CultureInfo.InvariantCulture); continue; }
                if (p.Length == 3 && cur >= 0) { if (p[2] == "n") t._offsets[cur] = long.Parse(p[0], CultureInfo.InvariantCulture); cur++; continue; }
                throw new NotSupportedException("tabelul de referințe nu poate fi citit");
            }
            var trailer = Encoding.ASCII.GetString(b, (int)t.StartXref + tr, sx - (int)t.StartXref - tr);
            t.Size = Int(trailer, @"/Size\s+(\d+)");
            t.Root = Int(trailer, @"/Root\s+(\d+)\s+0\s+R");
            t.Info = Regex.Match(trailer, @"/Info\s+(\d+)\s+0\s+R") is { Success: true } im ? int.Parse(im.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            t.IdEntry = Regex.Match(trailer, @"/ID\s*\[[^\]]{0,200}\]") is { Success: true } idm ? " " + idm.Value : "";
            if (Regex.IsMatch(trailer, @"/(Prev|Encrypt|XRefStm)\b")) throw new NotSupportedException("PDF cu actualizări anterioare sau criptat: nu este un raport generat de aplicație");
            t.RootDict = t.Dict(t.Root);
            var pages = Int(t.RootDict, @"/Pages\s+(\d+)\s+0\s+R");
            var leaves = new List<int>();
            t.Walk(pages, leaves, 0);
            if (leaves.Count == 0) throw new NotSupportedException("documentul nu are pagini");
            t.LastPage = leaves[^1];
            t.LastPageDict = t.Dict(t.LastPage);
            return t;
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

        /// <summary>The dictionary text of object <paramref name="num"/> ("&lt;&lt; … &gt;&gt;"), which must not be a stream.</summary>
        public string Dict(int num)
        {
            if (!_offsets.TryGetValue(num, out var off) || off <= 0 || off >= _b.Length) throw new NotSupportedException($"obiectul {num} lipsește din tabelul de referințe");
            int len = (int)Math.Min(8192, _b.Length - off);
            var text = Encoding.Latin1.GetString(_b, (int)off, len);
            var m = Regex.Match(text, $@"^{num}\s+0\s+obj\s*");
            if (!m.Success) throw new NotSupportedException($"obiectul {num} nu este la poziția din tabelul de referințe");
            int s = m.Length, depth = 0;
            if (!text.AsSpan(s).StartsWith("<<")) throw new NotSupportedException($"obiectul {num} nu este un dicționar");
            for (int i = s; i < text.Length - 1; i++)
            {
                if (text[i] == '<' && text[i + 1] == '<') { depth++; i++; continue; }
                if (text[i] == '>' && text[i + 1] == '>') { depth--; i++; if (depth == 0) return text[s..(i + 1)]; }
            }
            throw new NotSupportedException($"obiectul {num} nu poate fi citit");
        }

        private static int Int(string s, string pattern) =>
            Regex.Match(s, pattern) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : throw new NotSupportedException("PDF incomplet (" + pattern + ")");
    }
}
