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
/// PAdES B-B signature of a report that LogAnalyzer generated, into the signature field RESERVED at generation (<see cref="SignatureField"/>), as ONE
/// incremental update appended to the unchanged original bytes: the signature dictionary (/Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached,
/// /ByteRange, /Contents, /M), the existing widget with /V and /AP added (nothing else of it changes), an appearance form XObject the size of the box, its
/// font, an xref section for only those objects and a trailer with /Prev. The page and the catalog are not touched. A document without the reserved,
/// unsigned field is refused.
/// </summary>
public static class PdfIncrementalSigner
{
    public const int ContentsBytes = 16384;
    private const string ByteRangePlaceholder = "[0 ********** ********** **********]";

    public static byte[] Sign(byte[] original, SignatureAppearance look, ICmsDetachedSigner signer) => Sign(original, look, signer, allowPriorUpdates: false, extraObjects: null);

    /// <summary>
    /// Tests only, to reproduce attacks: <paramref name="allowPriorUpdates"/> signs a file with more updates (what Adobe does with any file),
    /// <paramref name="extraObjects"/> are written into the same update (a number below /Size replaces an original object), <paramref name="rewrite"/> changes
    /// the body of any object before it is written, <paramref name="raw"/> adds raw text with its own header (null text = a FREE xref entry), and
    /// <paramref name="rewriteXrefAndTrailer"/> rewrites the xref table and trailer text.
    /// </summary>
    internal static byte[] Sign(byte[] original, SignatureAppearance look, ICmsDetachedSigner signer, bool allowPriorUpdates, IReadOnlyDictionary<int, string>? extraObjects,
                                Func<int, string, string>? rewrite = null, IReadOnlyList<(int Num, string? Text)>? raw = null, Func<string, string>? rewriteXrefAndTrailer = null)
    {
        if (original.AsSpan().IndexOf("/ByteRange"u8) >= 0) throw new NotSupportedException("documentul este deja semnat");
        var st = PdfStructure.Parse(original, allowPriorUpdates ? 16 : 2);
        var field = SignatureField.Find(st) ?? throw new NotSupportedException("raportul nu are câmpul de semnătură rezervat (a fost generat cu o versiune mai veche): generați raportul din nou");
        var widgetDict = st.Dict(field.Widget);
        if (Regex.IsMatch(widgetDict, @"/(V|AP)\b")) throw new NotSupportedException("câmpul de semnătură este deja completat");
        double w = Math.Abs(field.Rect[2] - field.Rect[0]), h = Math.Abs(field.Rect[3] - field.Rect[1]);
        int n = st.Size;
        int sigObj = n, apObj = n + 1, fontObj = n + 2;
        var objects = new SortedDictionary<int, string>();

        var lines = new[]
        {
            $"Semnat de {Ascii(look.SignerName)}",
            "ofiter de securitate",
            $"Data: {look.SigningTime.ToUniversalTime():yyyy-MM-dd HH:mm} UTC",
            $"Emitent certificat: {Ascii(look.Issuer)}",
        };
        var stream = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"q 0.95 0.95 0.95 rg 0 0 {w:0.##} {h:0.##} re f 0.2 0.2 0.2 RG 0.8 w 0.4 0.4 {w - 0.8:0.##} {h - 0.8:0.##} re S Q\nBT /F1 8 Tf 0 0 0 rg\n"));
        for (int i = 0; i < lines.Length; i++) stream.Append(CultureInfo.InvariantCulture, $"1 0 0 1 8 {h - 18 - i * 16:0.##} Tm ({Esc(Trim(lines[i], (int)(w / 4.6)))}) Tj\n");
        stream.Append("ET\n");
        var streamText = stream.ToString();

        objects[fontObj] = "<</Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding>>";
        objects[apObj] = string.Create(CultureInfo.InvariantCulture, $"<</Type /XObject /Subtype /Form /BBox [0 0 {w:0.###} {h:0.###}] /Resources <</Font <</F1 {fontObj} 0 R>>>> /Length {Encoding.ASCII.GetByteCount(streamText)}>>\nstream\n{streamText}endstream");
        objects[field.Widget] = SignatureField.AddEntry(SignatureField.AddEntry(widgetDict, "/V", $"{sigObj} 0 R"), "/AP", $"<</N {apObj} 0 R>>");
        foreach (var (num, body) in extraObjects ?? new Dictionary<int, string>()) objects[num] = body;
        var placeholder = new string('0', ContentsBytes * 2);
        objects[sigObj] = $"<</Type /Sig /Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached /ByteRange {ByteRangePlaceholder} /Contents <{placeholder}> " +
                          $"/M ({PdfDate(look.SigningTime)}) /Name ({Esc(Ascii(look.SignerName))}) /Reason ({Esc(Ascii(look.Reason))}) /Location ({Esc(Ascii(look.Location))})>>";

        using var ms = new MemoryStream();
        ms.Write(original);
        if (original[^1] != (byte)'\n') ms.WriteByte((byte)'\n');
        var offsets = new SortedDictionary<int, long>();
        var free = new HashSet<int>();
        foreach (var (num, body) in objects)
        {
            offsets[num] = ms.Position;
            ms.Write(Encoding.Latin1.GetBytes($"{num} 0 obj\n{(rewrite is null ? body : rewrite(num, body))}\nendobj\n"));
        }
        foreach (var (num, text) in raw ?? [])
        {
            if (text is null) { free.Add(num); offsets[num] = 0; continue; }
            offsets[num] = ms.Position;
            ms.Write(Encoding.Latin1.GetBytes(text));
        }
        long xref = ms.Position;
        var x = new StringBuilder("xref\n");
        foreach (var run in SignatureField.Runs(offsets.Keys.ToList()))
        {
            x.Append(run[0].ToString(CultureInfo.InvariantCulture)).Append(' ').Append(run.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var num in run) x.Append(free.Contains(num) ? "0000000000 00001 f \n" : offsets[num].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }
        x.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size {Math.Max(fontObj, offsets.Keys.Max()) + 1} /Root {st.Root} 0 R{(st.Info > 0 ? $" /Info {st.Info} 0 R" : "")}{st.IdEntry} /Prev {st.StartXref}>>\nstartxref\n{xref}\n%%EOF\n");
        var tailText = rewriteXrefAndTrailer is null ? x.ToString() : rewriteXrefAndTrailer(x.ToString());
        ms.Write(Encoding.ASCII.GetBytes(tailText));
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
}
