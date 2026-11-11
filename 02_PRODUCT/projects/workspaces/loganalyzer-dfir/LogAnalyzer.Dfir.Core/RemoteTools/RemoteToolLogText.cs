using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// Shared reading and sanitising rules for the session logs of remote-access tools (AnyDesk connection_trace.txt / ad.trace, TeamViewer Connections*.txt).
/// These files come from the analysed station and may be written by an attacker: the encoding is sniffed (UTF-8, UTF-16 with or without a BOM, else ANSI read
/// as Latin-1), a line is cut at <see cref="MaxLineChars"/> characters while it is read (never held whole), hidden / bidi / Tag-block characters are removed
/// and named, remote ids must be digits, IP addresses must parse as a literal IPv4 / IPv6 address, and a time is converted to UTC only when the evidence
/// item carries the station's time zone (otherwise it stays "local time, zone unknown", never an invented UTC).
/// </summary>
internal static class RemoteToolLogText
{
    public const int MaxLineChars = 2048;
    public const int MaxFieldChars = 256;
    public const int MaxIdChars = 64;
    /// <summary>Earliest year accepted for a session time (AnyDesk and TeamViewer logs do not predate it); an earlier date is "impossible".</summary>
    public const int MinYear = 2010;
    /// <summary>A time more than this far after "now" is in the future: kept as raw text, never placed on the timeline.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(1);

    private static readonly Regex DigitsId = new(@"^\d{6,12}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Ipv4Literal = new(@"^\d{1,3}(?:\.\d{1,3}){3}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // ---- reading ----

    /// <summary>A reader over the file with the encoding sniffed from its first 64 KiB. The stream is left at the start.</summary>
    public static StreamReader OpenReader(FileStream fs)
    {
        var head = new byte[64 * 1024];
        int n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        fs.Seek(0, SeekOrigin.Begin);
        return new StreamReader(fs, SniffFallback(head.AsSpan(0, n)), detectEncodingFromByteOrderMarks: true);
    }

    /// <summary>The encoding to use when there is no BOM: UTF-16 LE when the zero bytes say so, UTF-8 when the bytes are valid UTF-8, else ANSI (Latin-1).</summary>
    public static Encoding SniffFallback(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF) || (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)))
            return new UTF8Encoding(false);   // the BOM wins in StreamReader
        int probe = Math.Min(head.Length, 512), odd = 0, oddZero = 0, even = 0, evenZero = 0;
        for (int i = 0; i < probe; i++) { if ((i & 1) == 1) { odd++; if (head[i] == 0) oddZero++; } else { even++; if (head[i] == 0) evenZero++; } }
        if (probe >= 8 && odd > 0 && oddZero * 10 >= odd * 7 && evenZero * 10 <= even) return new UnicodeEncoding(false, false);
        // a multi-byte character cut by the end of the window is not an error
        for (int cut = 0; cut <= 3 && cut <= head.Length; cut++)
            if (System.Text.Unicode.Utf8.IsValid(head[..(head.Length - cut)])) return new UTF8Encoding(false);
        return Encoding.Latin1;
    }

    /// <summary>
    /// The next line, at most <see cref="MaxLineChars"/> characters of it (<paramref name="truncated"/> says the rest was dropped); null at the end of the file.
    /// A surrogate pair is never cut. The caller's <paramref name="sb"/> is reused, so a gigantic line never allocates more than the cap.
    /// </summary>
    public static string? ReadLine(TextReader r, StringBuilder sb, out bool truncated)
    {
        sb.Clear();
        truncated = false;
        bool any = false;
        int c;
        while ((c = r.Read()) >= 0)
        {
            any = true;
            if (c == '\n') break;
            if (c == '\r') continue;
            if (sb.Length < MaxLineChars) sb.Append((char)c); else truncated = true;
        }
        if (!any) return null;
        if (sb.Length > 0 && char.IsHighSurrogate(sb[^1])) sb.Length--;
        return sb.ToString();
    }

    // ---- sanitising ----

    /// <summary>
    /// Text for display: clipped to <paramref name="max"/> characters (surrogate pairs kept whole), the Tag block (U+E0000-E007F) removed and named, line breaks and tabs
    /// turned into spaces, bidi overrides and control characters removed and named (<see cref="DefenderDetectionHistoryParser.Safe"/>).
    /// </summary>
    public static string Sane(string s, int max = MaxFieldChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool clipped = false;
        if (s.Length > max)
        {
            int cut = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
            s = s[..cut];
            clipped = true;
        }
        if (s.Contains('\uDB40'))
        {
            var sb = new StringBuilder(s.Length);
            bool tag = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\uDB40' && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) && s[i + 1] <= '\uDC7F') { tag = true; i++; continue; }
                sb.Append(s[i]);
            }
            s = sb.Append(tag ? " [caractere Unicode ascunse eliminate: bloc Tag]" : "").ToString();
        }
        var safe = DefenderDetectionHistoryParser.Safe(s).Trim();
        return clipped ? safe + "…" : safe;
    }

    /// <summary>A remote id (AnyDesk / TeamViewer): 6-12 digits. Returns the digits, or "format necunoscut: &lt;text&gt;" with the cleaned text; <paramref name="valid"/> tells which.</summary>
    public static string IdField(string raw, out bool valid)
    {
        var t = raw.Trim();
        valid = t.Length > 0 && IsDigits(t);
        if (valid) return t;
        return t.Length == 0 ? "" : "format necunoscut: " + Sane(t, MaxIdChars);
    }

    private static bool IsDigits(string t)
    {
        try { return DigitsId.IsMatch(t); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>An IPv4 (four dotted decimal numbers) or IPv6 literal; no scope id, no port, no shorthand like "1.2". The normalised text, or "" when it is not an address.</summary>
    public static string ValidIp(string raw)
    {
        var t = raw.Trim().Trim('[', ']');
        if (t.Length is 0 or > 45 || t.Contains('%')) return "";
        try
        {
            // an octet with a leading zero ("010.0.0.1") is rejected: IPAddress reads it as octal (8.0.0.1), which would put a wrong address in the IOC list
            if (Ipv4Literal.IsMatch(t)) return t.Split('.').All(o => o.Length == 1 || o[0] != '0') && IPAddress.TryParse(t, out var v4) && v4.AddressFamily == AddressFamily.InterNetwork ? v4.ToString() : "";
        }
        catch (RegexMatchTimeoutException) { return ""; }
        return t.Contains(':') && IPAddress.TryParse(t, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6 ? v6.ToString() : "";
    }

    // ---- time ----

    /// <summary>The station's time zone from the evidence item; Zone is null (with a note) when the item carries none or an unknown one.</summary>
    public readonly record struct Zone(TimeZoneInfo? Info, string Note);

    public static Zone ZoneOf(EvidenceItem item)
    {
        if (item.Timezone.Length == 0) return new(null, "fusul orar al stației nu este pe probă");
        // the id comes from the target (a remote package manifest): a Windows zone id is short, anything longer is not one
        if (item.Timezone.Length > 64) return new(null, "fusul orar de pe probă nu este valid");
        try { return new(TimeZoneInfo.FindSystemTimeZoneById(item.Timezone), item.Timezone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return new(null, $"fusul „{Sane(item.Timezone, 64)}” nu este cunoscut"); }
    }

    /// <summary>What a local time from a log became. <see cref="Problem"/> is "" or why the time is not usable (future, impossible, nonexistent local time).</summary>
    public readonly record struct LocalTime(Timestamp Time, string Basis, string Uncertainty, string Problem);

    /// <summary>
    /// A local date and time written by the tool, as a timeline time. With the station's zone: converted to UTC (an ambiguous hour at the end of daylight saving
    /// time is noted). Without it: unknown time with the raw text kept (the local time is also written in the event's fields), never UTC by assumption.
    /// A year before <see cref="MinYear"/>, a local time that does not exist (daylight saving gap) and a time after now + 1 day are not placed on the timeline.
    /// </summary>
    public static LocalTime ToTimestamp(DateTime local, string raw, Zone zone, DateTime nowUtc)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (local.Year < MinYear) return new(Timestamp.Unknown(raw), "oră locală a stației", "", $"dată imposibilă (înainte de {MinYear})");
        if (zone.Info is null)
            return new(Timestamp.Unknown(raw), $"oră locală a stației; neconvertită ({zone.Note})", "Ora este locală, fusul stației nu se cunoaște: nu a fost convertită în UTC.", "");
        if (zone.Info.IsInvalidTime(local)) return new(Timestamp.Unknown(raw), $"oră locală a stației ({zone.Note})", "", "ora locală nu există (salt de oră la trecerea la ora de vară)");
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone.Info);
        if (utc > nowUtc + FutureTolerance) return new(Timestamp.Unknown(raw), $"oră locală a stației ({zone.Note})", "", "ora este în viitor");
        var uncertainty = zone.Info.IsAmbiguousTime(local) ? "Ora locală este ambiguă (se repetă la trecerea la ora de iarnă); s-a folosit ora standard." : "";
        return new(Timestamp.FromUtc(utc, raw, $"ora locală a stației ({zone.Note}) → UTC"), $"ora locală a stației ({zone.Note}) → UTC", uncertainty, "");
    }

    public static string LocalText(DateTime local) => local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
