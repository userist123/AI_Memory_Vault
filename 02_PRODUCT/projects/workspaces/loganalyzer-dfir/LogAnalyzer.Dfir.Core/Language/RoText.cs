using System.Globalization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// Romanian text helpers used wherever a number, a time, a path or an operating-system error is printed for a reader: the Romanian
/// "de" after numbers of 20 or more, the ro-RO digit grouping, sentence capitals, readable paths and the time convention
/// (review 2026-10-12, sections B and D). Pure functions, no I/O.
/// </summary>
public static class RoText
{
    private static readonly NumberFormatInfo RoNumbers = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };

    /// <summary>ro-RO grouping with a dot: 19988 → "19.988" (the number alone, no noun).</summary>
    public static string Number(long n) => n.ToString("N0", RoNumbers);

    /// <summary>
    /// The number with its noun in the right form: "1 eveniment", "5 evenimente", "20 de evenimente", "3.078 de intrări".
    /// The "de" is needed when the last two digits are 20-99 or 00 (and the number is not zero): 100, 120, 1.000 but not 101 or 119.
    /// </summary>
    public static string Count(long n, string singular, string plural)
    {
        if (n == 1) return "1 " + singular;
        long mod = Math.Abs(n) % 100;
        bool de = n != 0 && (mod == 0 || mod >= 20);
        return Number(n) + (de ? " de " : " ") + plural;
    }

    /// <summary>"3 ori" / "70 de ori" / "o dată".</summary>
    public static string Times(long n) => n == 1 ? "o dată" : Count(n, "dată", "ori");

    /// <summary>Capital after a full stop, no doubled full stops: "a. b.." → "a. B.".</summary>
    public static string FixSentences(string s)
    {
        if (s.Length == 0) return s;
        s = Regex.Replace(s, @"(?<!\.)\.\.(?!\.)", ".");
        return Regex.Replace(s, @"(?<=[\p{Ll}\)\d]\.) +(\p{Ll})", m => " " + m.Groups[1].Value.ToUpper(CultureInfo.GetCultureInfo("ro-RO")));
    }

    /// <summary>The common Windows error codes in Romanian; any other code is shown as "cod N" with the system's own message in brackets.</summary>
    public static string OsError(int code, string systemMessage = "") => code switch
    {
        5 => "cod 5 (acces refuzat)",
        87 => "cod 87 (parametru incorect)",
        2 => "cod 2 (fișierul nu a fost găsit)",
        32 => "cod 32 (fișierul este folosit de alt proces)",
        _ => systemMessage.Length > 0 ? $"cod {code} (mesaj Windows: {systemMessage.TrimEnd('.', ' ')})" : $"cod {code}",
    };


    /// <summary>
    /// Readable path: "\VOLUME{guid}\Users\x" → ("\Users\x", "volumul guid"); a "\\?\" or "\??\" prefix is dropped. The volume is returned
    /// separately so a report can say it once. A path without a volume prefix is returned unchanged with an empty note.
    /// </summary>
    public static (string Path, string VolumeNote) CleanPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return (path ?? "", "");
        var m = Regex.Match(path, @"^\\VOLUME\{(?<g>[0-9a-fA-F\-]+)\}(?<rest>\\.*)?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success) return (m.Groups["rest"].Success ? m.Groups["rest"].Value.TrimStart('\\') : "", "volumul " + m.Groups["g"].Value.ToLowerInvariant());
        m = Regex.Match(path, @"^(?:\\\\\?\\|\\\?\?\\)(?<rest>.*)$", RegexOptions.Singleline);
        return m.Success ? (m.Groups["rest"].Value, "") : (path, "");
    }

    private static readonly Regex VolumeInText = new(@"\\VOLUME\{(?<g>[0-9a-fA-F\-]{8,})\}\\?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NtPrefixInText = new(@"\\\\\?\\|\\\?\?\\", RegexOptions.Compiled);

    /// <summary>
    /// Readable paths inside free text: a volume prefix such as VOLUME{guid} is removed and the volume is named once at the end
    /// ("(volumul guid)"); the NT prefixes (backslash-backslash-question-backslash and backslash-question-question-backslash) are dropped.
    /// Text without such paths is returned unchanged.
    /// </summary>
    public static string CleanPathsInText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var vols = new List<string>();
        var t = VolumeInText.Replace(text, m => { var g = m.Groups["g"].Value.ToLowerInvariant(); if (!vols.Contains(g)) vols.Add(g); return ""; });
        t = NtPrefixInText.Replace(t, "");
        if (vols.Count == 0) return t;
        return t.TrimEnd() + (vols.Count == 1 ? $" (volumul {vols[0]})" : $" (volume: {string.Join(", ", vols)})");
    }

    /// <summary>"X (volumul …)" for text; the path alone when there is no volume.</summary>
    public static string ReadablePath(string path)
    {
        var (p, v) = CleanPath(path);
        return v.Length == 0 ? p : $"{p} ({v})";
    }

    /// <summary>"ora României (UTC+03:00)" for the Romanian zone (GTB / E. Europe), otherwise the Windows zone id with its offset.</summary>
    public static string ZoneName(TimeZoneInfo zone, DateTimeOffset at)
    {
        var off = zone.GetUtcOffset(at);
        string o = "UTC" + (off < TimeSpan.Zero ? "-" : "+") + off.Duration().ToString(@"hh\:mm");
        bool ro = zone.Id is "GTB Standard Time" or "E. Europe Standard Time" or "Europe/Bucharest";
        return ro ? $"ora României ({o})" : $"{zone.Id} ({o})";
    }

    /// <summary>One time convention: local station time with UTC in brackets, once per event ("2026-10-11 14:05 (UTC 11:05)").</summary>
    public static string LocalWithUtc(DateTimeOffset t, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(t, zone);
        var utc = t.ToUniversalTime();
        string day = local.Date == utc.Date ? "" : utc.ToString("yyyy-MM-dd ");
        return $"{local:yyyy-MM-dd HH:mm} (UTC {day}{utc:HH:mm})";
    }
}
