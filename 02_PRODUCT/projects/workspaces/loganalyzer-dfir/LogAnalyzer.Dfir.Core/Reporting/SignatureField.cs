using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// The place of the security officer's signature, reserved when the report is GENERATED (owner decision, review round 3): the last page carries only an
/// empty framed box, and the generated file already holds an empty signature field whose widget covers exactly that box. The recorded hash of the report
/// covers both, so signing (in the application, or in Adobe in the existing field) can only fill the field; it cannot place a signature anywhere else.
/// </summary>
public static class SignatureField
{
    /// <summary>The box, in points from the top-left corner of the last page (A4).</summary>
    public const double BoxLeft = 162, BoxTop = 240, BoxWidth = 271, BoxHeight = 90;
    public const string FieldName = "Semnatura ofiterului de securitate";

    /// <summary>The box as a PDF rectangle on a page of height <paramref name="pageHeight"/> (origin bottom-left).</summary>
    public static double[] Rect(double pageHeight) => [BoxLeft, pageHeight - BoxTop - BoxHeight, BoxLeft + BoxWidth, pageHeight - BoxTop];

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Appends to a freshly generated PDF (one cross-reference section) one incremental update: an AcroForm with one signature field, unsigned, whose widget
    /// is exactly the reserved box on the last page. Returns the new bytes; the original bytes stay as their prefix.
    /// </summary>
    public static byte[] Reserve(byte[] pdf)
    {
        var st = PdfStructure.Parse(pdf);
        int page = st.Pages[^1];
        var pageDict = st.Dict(page);
        var root = st.Dict(st.Root);
        if (pageDict.Contains("/Annots", StringComparison.Ordinal) || root.Contains("/AcroForm", StringComparison.Ordinal))
            throw new NotSupportedException("documentul are deja adnotări sau un formular");
        var mb = Regex.Match(pageDict, @"/MediaBox\s*\[\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*\]");
        if (!mb.Success) throw new NotSupportedException("ultima pagină nu are /MediaBox");
        double height = double.Parse(mb.Groups[4].Value, CultureInfo.InvariantCulture) - double.Parse(mb.Groups[2].Value, CultureInfo.InvariantCulture);
        var r = Rect(height);
        int widget = st.Size, form = st.Size + 1;
        var objects = new SortedDictionary<int, string>
        {
            [widget] = $"<</Type /Annot /Subtype /Widget /FT /Sig /T ({FieldName}) /Rect [{F(r[0])} {F(r[1])} {F(r[2])} {F(r[3])}] /F 132 /P {page} 0 R>>",
            [form] = $"<</Fields [{widget} 0 R]>>",
            [page] = AddEntry(pageDict, "/Annots", $"[{widget} 0 R]"),
            [st.Root] = AddEntry(root, "/AcroForm", $"{form} 0 R"),
        };
        return Append(pdf, st, objects, form + 1);
    }

    /// <summary>One incremental update holding <paramref name="objects"/>, with an exact classic xref table and a trailer chained by /Prev.</summary>
    internal static byte[] Append(byte[] pdf, PdfStructure st, IReadOnlyDictionary<int, string> objects, int size)
    {
        using var ms = new MemoryStream();
        ms.Write(pdf);
        if (pdf[^1] != (byte)'\n') ms.WriteByte((byte)'\n');
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
            x.Append(run[0].ToString(CultureInfo.InvariantCulture)).Append(' ').Append(run.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var num in run) x.Append(offsets[num].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        x.Append("trailer\n<</Size ").Append(size.ToString(CultureInfo.InvariantCulture)).Append(" /Root ").Append(st.Root.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        if (st.Info > 0) x.Append(" /Info ").Append(st.Info.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        x.Append(st.IdEntry).Append(" /Prev ").Append(st.StartXref.ToString(CultureInfo.InvariantCulture)).Append(">>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        ms.Write(Encoding.ASCII.GetBytes(x.ToString()));
        return ms.ToArray();
    }

    internal static IEnumerable<List<int>> Runs(List<int> nums)
    {
        var run = new List<int>();
        foreach (var n in nums)
        {
            if (run.Count > 0 && n != run[^1] + 1) { yield return run; run = []; }
            run.Add(n);
        }
        if (run.Count > 0) yield return run;
    }

    internal static string AddEntry(string dict, string key, string value)
    {
        int end = dict.LastIndexOf(">>", StringComparison.Ordinal);
        return dict[..end] + $"\n{key} {value}" + dict[end..];
    }

    /// <summary>The reserved field of a generated report: AcroForm object, widget object and its rectangle; null when the document has none.</summary>
    public static (int Form, int Widget, double[] Rect)? Find(PdfStructure st)
    {
        var form = Regex.Match(st.Dict(st.Root), @"/AcroForm\s+(\d+)\s+0\s+R");
        if (!form.Success) return null;
        int f = int.Parse(form.Groups[1].Value, CultureInfo.InvariantCulture);
        var fields = Regex.Match(st.Dict(f), @"/Fields\s*\[\s*(\d+)\s+0\s+R\s*\]");
        if (!fields.Success) return null;
        int w = int.Parse(fields.Groups[1].Value, CultureInfo.InvariantCulture);
        var wd = st.Dict(w);
        var rect = Regex.Match(wd, @"/Rect\s*\[\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*\]");
        if (!rect.Success || !Regex.IsMatch(wd, @"/FT\s*/Sig\b")) return null;
        return (f, w, Enumerable.Range(1, 4).Select(i => double.Parse(rect.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray());
    }
}
