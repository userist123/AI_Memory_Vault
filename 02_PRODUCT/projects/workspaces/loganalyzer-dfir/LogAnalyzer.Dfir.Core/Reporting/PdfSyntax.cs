using System.Globalization;
using System.Text;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>A parsed PDF value (ISO 32000-1 §7.3). Names are stored DECODED (#xx escapes resolved), so "/Java#53cript" is "JavaScript".</summary>
public abstract class PdfValue;
public sealed class PdfNumber(double value, string text) : PdfValue { public double Value { get; } = value; public string Text { get; } = text; }
public sealed class PdfName(string value) : PdfValue { public string Value { get; } = value; }
public sealed class PdfBool(bool value) : PdfValue { public bool Value { get; } = value; }
public sealed class PdfNull : PdfValue;
public sealed class PdfRef(int num, int gen) : PdfValue { public int Num { get; } = num; public int Gen { get; } = gen; public bool Is(PdfRef o) => o.Num == Num && o.Gen == Gen; }
public sealed class PdfKeyword(string value) : PdfValue { public string Value { get; } = value; }
public sealed class PdfString(byte[] bytes, bool hex) : PdfValue { public byte[] Bytes { get; } = bytes; public bool Hex { get; } = hex; }
public sealed class PdfArray(List<PdfValue> items) : PdfValue { public List<PdfValue> Items { get; } = items; }
public sealed class PdfDict(Dictionary<string, PdfValue> entries) : PdfValue
{
    public Dictionary<string, PdfValue> Entries { get; } = entries;
    public PdfValue? this[string key] => Entries.GetValueOrDefault(key);
    public string? NameOf(string key) => this[key] is PdfName n ? n.Value : null;
}

/// <summary>"N G obj … endobj" with its position (offset of the object number) and, for a stream, where its data is.</summary>
public sealed record PdfIndirectObject(int Num, int Gen, long Offset, PdfValue Value, long StreamStart = -1, long StreamLength = -1)
{
    public bool IsStream => StreamStart >= 0;
}

/// <summary>
/// A strict PDF lexer and parser (ISO 32000-1 §7.2–7.3): white space including NUL and form feed, comments anywhere between tokens, names with #xx escapes,
/// literal strings with nesting and escapes, hex strings, dictionaries, arrays, indirect references, and streams read by their direct /Length.
/// Bounded (depth, sizes); anything malformed throws <see cref="FormatException"/>.
/// </summary>
public sealed class PdfParser(byte[] data, int start, int end)
{
    private int _pos = start;
    public int Position { get => _pos; set => _pos = value; }
    public int End => end;
    private const int MaxDepth = 32;

    public static bool IsWhite(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;
    private static bool IsDelim(byte c) => c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    public void SkipWhiteAndComments()
    {
        while (_pos < end)
        {
            if (IsWhite(data[_pos])) { _pos++; continue; }
            if (data[_pos] == '%') { while (_pos < end && data[_pos] != '\n' && data[_pos] != '\r') _pos++; continue; }
            break;
        }
    }

    /// <summary>The next value; numbers followed by "G R" become a reference. Keywords (obj, stream, xref, trailer …) come back as <see cref="PdfKeyword"/>.</summary>
    public PdfValue ReadValue(int depth = 0)
    {
        if (depth > MaxDepth) throw new FormatException("structură PDF prea adâncă");
        SkipWhiteAndComments();
        if (_pos >= end) throw new FormatException("sfârșit neașteptat");
        byte c = data[_pos];
        if (c == '/') return new PdfName(ReadName());
        if (c == '(') return new PdfString(ReadLiteral(), false);
        if (c == '<' && _pos + 1 < end && data[_pos + 1] == '<')
        {
            _pos += 2;
            var map = new Dictionary<string, PdfValue>(StringComparer.Ordinal);
            while (true)
            {
                SkipWhiteAndComments();
                if (_pos + 1 < end && data[_pos] == '>' && data[_pos + 1] == '>') { _pos += 2; return new PdfDict(map); }
                if (_pos >= end || data[_pos] != '/') throw new FormatException("cheie de dicționar invalidă");
                var key = ReadName();
                var v = ReadValue(depth + 1);
                if (v is PdfKeyword) throw new FormatException("valoare de dicționar invalidă");
                if (!map.TryAdd(key, v)) throw new FormatException($"cheie dublată în dicționar: {key}");
                if (map.Count > 4096) throw new FormatException("dicționar prea mare");
            }
        }
        if (c == '<') return new PdfString(ReadHex(), true);
        if (c == '[')
        {
            _pos++;
            var items = new List<PdfValue>();
            while (true)
            {
                SkipWhiteAndComments();
                if (_pos < end && data[_pos] == ']') { _pos++; return new PdfArray(items); }
                var v = ReadValue(depth + 1);
                if (v is PdfKeyword) throw new FormatException("element de tablou invalid");
                items.Add(v);
                if (items.Count > 1_000_000) throw new FormatException("tablou prea mare");
            }
        }
        if (c is (byte)')' or (byte)'>' or (byte)']' or (byte)'{' or (byte)'}') throw new FormatException("delimitator neașteptat");
        var tok = ReadRegular();
        if (IsNumber(tok))
        {
            // "N G R": a reference (both integers, non-negative)
            int save = _pos;
            if (IsUInt(tok))
            {
                SkipWhiteAndComments();
                if (_pos < end && !IsDelim(data[_pos]))
                {
                    var t2 = ReadRegular();
                    if (IsUInt(t2))
                    {
                        SkipWhiteAndComments();
                        if (_pos < end && !IsDelim(data[_pos]))
                        {
                            int save2 = _pos;
                            var t3 = ReadRegular();
                            if (t3 == "R") return new PdfRef(int.Parse(tok, CultureInfo.InvariantCulture), int.Parse(t2, CultureInfo.InvariantCulture));
                            _pos = save2;
                        }
                    }
                }
            }
            _pos = save;
            return new PdfNumber(double.Parse(tok, NumberStyles.Float, CultureInfo.InvariantCulture), tok);
        }
        return tok switch { "true" => new PdfBool(true), "false" => new PdfBool(false), "null" => new PdfNull(), _ => new PdfKeyword(tok) };
    }

    private static bool IsUInt(string t) => t.Length is > 0 and <= 9 && t.All(char.IsAsciiDigit);
    private static bool IsNumber(string t) => t.Length is > 0 and <= 32 && double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    private string ReadRegular()
    {
        int s = _pos;
        while (_pos < end && !IsWhite(data[_pos]) && !IsDelim(data[_pos])) _pos++;
        if (_pos == s) throw new FormatException("simbol gol");
        if (_pos - s > 256) throw new FormatException("simbol prea lung");
        return Encoding.ASCII.GetString(data, s, _pos - s);
    }

    private string ReadName()
    {
        _pos++; // '/'
        var sb = new StringBuilder();
        while (_pos < end && !IsWhite(data[_pos]) && !IsDelim(data[_pos]))
        {
            if (data[_pos] == '#')
            {
                if (_pos + 2 >= end || !Uri.IsHexDigit((char)data[_pos + 1]) || !Uri.IsHexDigit((char)data[_pos + 2])) throw new FormatException("nume cu #xx invalid");
                sb.Append((char)Convert.ToByte(Encoding.ASCII.GetString(data, _pos + 1, 2), 16));
                _pos += 3;
            }
            else sb.Append((char)data[_pos++]);
            if (sb.Length > 256) throw new FormatException("nume prea lung");
        }
        return sb.ToString();
    }

    private byte[] ReadLiteral()
    {
        _pos++; // '('
        var o = new List<byte>();
        int depth = 1;
        while (_pos < end)
        {
            byte b = data[_pos++];
            if (b == '\\')
            {
                if (_pos >= end) break;
                byte e = data[_pos++];
                switch (e)
                {
                    case (byte)'n': o.Add(10); break;
                    case (byte)'r': o.Add(13); break;
                    case (byte)'t': o.Add(9); break;
                    case (byte)'b': o.Add(8); break;
                    case (byte)'f': o.Add(12); break;
                    case (byte)'\r': if (_pos < end && data[_pos] == '\n') _pos++; break;
                    case (byte)'\n': break;
                    case >= (byte)'0' and <= (byte)'7':
                        int v = e - '0';
                        for (int k = 0; k < 2 && _pos < end && data[_pos] is >= (byte)'0' and <= (byte)'7'; k++) v = v * 8 + (data[_pos++] - '0');
                        o.Add((byte)v); break;
                    default: o.Add(e); break;
                }
                continue;
            }
            if (b == '(') depth++;
            else if (b == ')' && --depth == 0) return [.. o];
            o.Add(b);
            if (o.Count > 1_000_000) throw new FormatException("șir prea lung");
        }
        throw new FormatException("șir neînchis");
    }

    private byte[] ReadHex()
    {
        _pos++; // '<'
        var sb = new StringBuilder();
        while (_pos < end && data[_pos] != '>')
        {
            byte b = data[_pos++];
            if (IsWhite(b)) continue;
            if (!Uri.IsHexDigit((char)b)) throw new FormatException("șir hexazecimal invalid");
            sb.Append((char)b);
            if (sb.Length > 4_000_000) throw new FormatException("șir hexazecimal prea lung");
        }
        if (_pos >= end) throw new FormatException("șir hexazecimal neînchis");
        _pos++;
        if (sb.Length % 2 == 1) sb.Append('0');
        return Convert.FromHexString(sb.ToString());
    }

    public string? PeekKeyword()
    {
        SkipWhiteAndComments();
        if (_pos >= end || IsDelim(data[_pos])) return null;
        int save = _pos;
        var t = ReadRegular();
        _pos = save;
        return t;
    }

    public void ExpectKeyword(string kw)
    {
        SkipWhiteAndComments();
        int s = _pos;
        if (_pos >= end || IsDelim(data[_pos]) || ReadRegular() != kw) { _pos = s; throw new FormatException($"se aștepta „{kw}”"); }
    }

    /// <summary>Reads "N G obj value [stream … endstream] endobj" starting at the current position.</summary>
    public PdfIndirectObject ReadIndirect()
    {
        SkipWhiteAndComments();
        long offset = _pos;
        var n = ReadValue();
        var g = ReadValue();
        if (n is not PdfNumber { Text: var nt } || g is not PdfNumber { Text: var gt } || !IsUInt(nt) || !IsUInt(gt)) throw new FormatException("antet de obiect invalid");
        ExpectKeyword("obj");
        var v = ReadValue();
        if (v is PdfKeyword) throw new FormatException("obiect fără valoare");
        long sStart = -1, sLen = -1;
        SkipWhiteAndComments();
        if (PeekKeyword() == "stream")
        {
            if (v is not PdfDict d || d["Length"] is not PdfNumber { Text: var lt } || !IsUInt(lt)) throw new FormatException("flux fără /Length direct");
            ExpectKeyword("stream");
            if (_pos < end && data[_pos] == '\r') _pos++;
            if (_pos >= end || data[_pos] != '\n') throw new FormatException("după „stream” trebuie un sfârșit de linie");
            _pos++;
            sStart = _pos; sLen = long.Parse(lt, CultureInfo.InvariantCulture);
            if (sStart + sLen > end) throw new FormatException("fluxul depășește fișierul");
            _pos = (int)(sStart + sLen);
            ExpectKeyword("endstream");
        }
        ExpectKeyword("endobj");
        return new PdfIndirectObject(int.Parse(nt, CultureInfo.InvariantCulture), int.Parse(gt, CultureInfo.InvariantCulture), offset, v, sStart, sLen);
    }

    /// <summary>Structural equality of two values (names decoded, numbers by value, strings by bytes).</summary>
    public static bool Same(PdfValue? a, PdfValue? b) => (a, b) switch
    {
        (null, null) => true,
        (PdfNumber x, PdfNumber y) => x.Value == y.Value,
        (PdfName x, PdfName y) => x.Value == y.Value,
        (PdfBool x, PdfBool y) => x.Value == y.Value,
        (PdfNull, PdfNull) => true,
        (PdfRef x, PdfRef y) => x.Is(y),
        (PdfString x, PdfString y) => x.Bytes.AsSpan().SequenceEqual(y.Bytes),
        (PdfArray x, PdfArray y) => x.Items.Count == y.Items.Count && x.Items.Zip(y.Items).All(p => Same(p.First, p.Second)),
        (PdfDict x, PdfDict y) => x.Entries.Count == y.Entries.Count && x.Entries.All(kv => y.Entries.TryGetValue(kv.Key, out var o) && Same(kv.Value, o)),
        _ => false,
    };

    /// <summary>The object at <paramref name="offset"/> of <paramref name="file"/>.</summary>
    public static PdfIndirectObject ObjectAt(byte[] file, long offset, int limit) => new PdfParser(file, (int)offset, limit).ReadIndirect();
}
