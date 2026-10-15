using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>Result of checking a detached CMS (PKCS#7 / CAdES) signature over some content.</summary>
public sealed record CmsResult(bool Ok, X509Certificate2? Signer, string Problem);

/// <summary>Detached CMS verification. The application implements it with SignedCms (shared WPF framework); the library stays free of it.</summary>
public interface ICmsDetachedVerifier
{
    CmsResult Verify(byte[] cms, byte[] content);
}

/// <summary>
/// The signature inside a signed PDF (PAdES / Adobe): exactly one signature dictionary, its /ByteRange covering the whole file except the /Contents hex
/// string, nothing appended after it, a CMS that verifies over the covered bytes, and the candidate lengths of the original (unsigned) document: the
/// positions right after each "%%EOF" before the signature's own update. Bounded parsing: only what a signed LogAnalyzer report (our signer or Adobe's
/// incremental update) contains. <see cref="Problem"/> is null when all of it holds.
/// </summary>
public sealed class PdfSignatureCheck
{
    public const long MaxSize = 256L * 1024 * 1024;

    private byte[] _bytes = [];
    public long[] ByteRange { get; private set; } = [];
    public string SubFilter { get; private set; } = "";
    public X509Certificate2? Signer { get; private set; }
    public DateTimeOffset? ClaimedSigningTime { get; private set; }
    public string SignedFileSha256 { get; private set; } = "";
    public IReadOnlyList<long> OriginalLengths { get; private set; } = [];
    /// <summary>Why the signature is not acceptable (in plain Romanian); null when it is.</summary>
    public string? Problem { get; private set; }
    /// <summary>True when bytes were appended after the signed range (a later incremental update): "modificat după semnare".</summary>
    public bool ModifiedAfterSigning { get; private set; }
    public bool NotSigned { get; private set; }

    public static PdfSignatureCheck Check(byte[] pdf, ICmsDetachedVerifier cms)
    {
        var r = new PdfSignatureCheck { _bytes = pdf, SignedFileSha256 = Convert.ToHexString(SHA256.HashData(pdf)) };
        r.Problem = r.Run(cms);
        return r;
    }

    /// <summary>The original length whose prefix has SHA-256 <paramref name="sha256"/>; null when none.</summary>
    public long? OriginalLengthFor(string sha256)
    {
        foreach (var l in OriginalLengths)
            if (string.Equals(Convert.ToHexString(SHA256.HashData(_bytes.AsSpan(0, (int)l))), sha256, StringComparison.OrdinalIgnoreCase)) return l;
        return null;
    }

    /// <summary>The first matching original among <paramref name="sha256s"/>: (length, sha) or null.</summary>
    public (long Length, string Sha256)? MatchOriginal(IEnumerable<string> sha256s)
    {
        var set = sha256s.Select(s => s.ToUpperInvariant()).ToHashSet();
        foreach (var l in OriginalLengths)
        {
            var h = Convert.ToHexString(SHA256.HashData(_bytes.AsSpan(0, (int)l)));
            if (set.Contains(h)) return (l, h);
        }
        return null;
    }

    public bool OriginalContainsMarker(string marker)
    {
        if (OriginalLengths.Count == 0) return false;
        var m = Encoding.ASCII.GetBytes(marker);
        return _bytes.AsSpan(0, (int)OriginalLengths.Max()).IndexOf(m) >= 0;
    }

    private string? Run(ICmsDetachedVerifier cms)
    {
        var b = _bytes;
        if (b.Length > MaxSize) return "fișierul este prea mare pentru un raport";
        if (b.Length < 8 || !b.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return "fișierul nu este un PDF";
        var at = AllIndexes(b, "/ByteRange"u8);
        if (at.Count == 0) { NotSigned = true; return "documentul nu are nicio semnătură electronică"; }
        if (at.Count > 1) return "documentul are mai multe semnături sau a fost modificat după semnare (mai multe /ByteRange)";
        if (!TryReadByteRange(b, at[0] + 10, out var br)) return "intervalul semnat (/ByteRange) nu poate fi citit";
        ByteRange = br;
        long a0 = br[0], l0 = br[1], a1 = br[2], l1 = br[3];
        if (a0 != 0 || l0 <= 0 || a1 <= l0 || l1 < 0 || a1 + l1 > b.Length) return "intervalul semnat (/ByteRange) nu este valid";
        if (a1 + l1 < b.Length) { ModifiedAfterSigning = true; return "documentul a fost modificat după semnare (există o actualizare adăugată după semnătură)"; }
        if (b[l0] != (byte)'<' || b[a1 - 1] != (byte)'>') return "intervalul semnat nu exclude exact semnătura (/Contents)";
        for (long i = l0 + 1; i < a1 - 1; i++) if (!Uri.IsHexDigit((char)b[i])) return "semnătura (/Contents) nu este un șir hexazecimal";
        if (at[0] > l0 && at[0] < a1) return "intervalul semnat nu este valid";

        var sub = AllIndexes(b, "/SubFilter"u8).Select(i => ReadName(b, i + 10)).ToList();
        SubFilter = sub.FirstOrDefault(s => s is "ETSI.CAdES.detached" or "adbe.pkcs7.detached") ?? "";
        if (SubFilter.Length == 0) return "tipul semnăturii nu este acceptat (se acceptă doar ETSI.CAdES.detached și adbe.pkcs7.detached)";
        ClaimedSigningTime = ReadSigningTime(b);

        var der = Convert.FromHexString(Encoding.ASCII.GetString(b, (int)l0 + 1, (int)(a1 - l0 - 2)));
        der = TrimDer(der);
        if (der.Length == 0) return "semnătura (/Contents) este goală";
        var content = new byte[l0 + l1];
        Buffer.BlockCopy(b, 0, content, 0, (int)l0);
        Buffer.BlockCopy(b, (int)a1, content, (int)l0, (int)l1);
        var c = cms.Verify(der, content);
        Signer = c.Signer;
        if (!c.Ok || c.Signer is null) return "semnătura criptografică nu corespunde conținutului (" + c.Problem + ")";

        // originals: every "%%EOF" (with its end of line) inside the first signed segment, i.e. before the signature's own update
        var eofs = new List<long>();
        foreach (var i in AllIndexes(b.AsSpan(0, (int)l0).ToArray(), "%%EOF"u8))
        {
            long end = i + 5;
            eofs.Add(end);
            if (end < l0 && b[end] == '\r') { end++; eofs.Add(end); }
            if (end < l0 && b[end] == '\n') eofs.Add(end + 1);
        }
        OriginalLengths = eofs.Distinct().Where(x => x <= l0).ToList();
        return null;
    }

    private static List<int> AllIndexes(byte[] b, ReadOnlySpan<byte> pattern)
    {
        var list = new List<int>();
        int start = 0;
        while (start < b.Length)
        {
            int i = b.AsSpan(start).IndexOf(pattern);
            if (i < 0) break;
            list.Add(start + i);
            start += i + pattern.Length;
            if (list.Count > 64) break;
        }
        return list;
    }

    private static bool TryReadByteRange(byte[] b, int pos, out long[] values)
    {
        values = new long[4];
        int i = pos;
        while (i < b.Length && i < pos + 16 && b[i] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') i++;
        if (i >= b.Length || b[i] != '[') return false;
        i++;
        for (int k = 0; k < 4; k++)
        {
            while (i < b.Length && i < pos + 200 && b[i] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') i++;
            int s = i;
            while (i < b.Length && i < pos + 200 && b[i] >= '0' && b[i] <= '9') i++;
            if (i == s || i - s > 12 || !long.TryParse(Encoding.ASCII.GetString(b, s, i - s), NumberStyles.None, CultureInfo.InvariantCulture, out values[k])) return false;
        }
        while (i < b.Length && i < pos + 200 && b[i] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') i++;
        return i < b.Length && b[i] == ']';
    }

    private static string ReadName(byte[] b, int pos)
    {
        int i = pos;
        while (i < b.Length && i < pos + 8 && b[i] is (byte)' ' or (byte)'\r' or (byte)'\n') i++;
        if (i >= b.Length || b[i] != '/') return "";
        int s = ++i;
        while (i < b.Length && i < s + 64 && b[i] > ' ' && b[i] is not ((byte)'/' or (byte)'>' or (byte)'[' or (byte)'(' or (byte)'<')) i++;
        return Encoding.ASCII.GetString(b, s, i - s);
    }

    /// <summary>The /M (D:YYYYMMDDHHmmSS...) of the signature dictionary: the time the signer claims (not proven without a timestamp).</summary>
    private static DateTimeOffset? ReadSigningTime(byte[] b)
    {
        foreach (var i in AllIndexes(b, "/M (D:"u8).Concat(AllIndexes(b, "/M(D:"u8)))
        {
            int s = Array.IndexOf(b, (byte)':', i) + 1;
            if (s <= 0 || s + 14 > b.Length) continue;
            var t = Encoding.ASCII.GetString(b, s, 14);
            if (DateTime.TryParseExact(t, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                var off = TimeSpan.Zero;
                int z = s + 14;
                if (z < b.Length && (b[z] == '+' || b[z] == '-') && z + 6 <= b.Length && int.TryParse(Encoding.ASCII.GetString(b, z + 1, 2), out var hh) && int.TryParse(Encoding.ASCII.GetString(b, z + 4, 2), out var mm))
                    off = new TimeSpan(hh, mm, 0) * (b[z] == '-' ? -1 : 1);
                return new DateTimeOffset(dt, off).ToUniversalTime();
            }
        }
        return null;
    }

    /// <summary>The DER SEQUENCE at the start of the zero-padded /Contents (definite length); the whole buffer without trailing zeros otherwise.</summary>
    private static byte[] TrimDer(byte[] d)
    {
        if (d.Length >= 2 && d[0] == 0x30)
        {
            int lenByte = d[1];
            if (lenByte < 0x80) return d.AsSpan(0, Math.Min(d.Length, 2 + lenByte)).ToArray();
            int n = lenByte & 0x7f;
            if (n is > 0 and <= 4 && d.Length >= 2 + n)
            {
                long len = 0;
                for (int k = 0; k < n; k++) len = (len << 8) | d[2 + k];
                long total = 2 + n + len;
                if (total <= d.Length) return d.AsSpan(0, (int)total).ToArray();
            }
        }
        int end = d.Length;
        while (end > 0 && d[end - 1] == 0) end--;
        return d.AsSpan(0, end).ToArray();
    }
}
