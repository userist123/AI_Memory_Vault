using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// The finding told for someone who is not a specialist (owner 2026-11-06: „ca la prosti: Popescu X a băgat un stick și a copiat de acolo acolo
/// documentul cu numele…, fără coduri de eveniment și termeni tehnici”): WHEN, on WHICH computer, WHO (the person from the users register, else the
/// account), WHAT they did and with WHAT. The logs show the account, not the hand on the keyboard: the sentence names the account's person and never
/// says who is guilty; that is the investigator's conclusion. Event codes, rule ids, SIDs and technique ids never appear here: they stay in the
/// technical part of the finding and in the report's annex.
/// </summary>
public static class PlainStory
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Windows' own sessions and services: never "who did it" (lab WS02: DWM-1, UMFD-0 and defaultuser0 were listed as candidates for a USB stick).</summary>
    private static readonly Regex StationAccount = new(@"^(?:DWM-\d+|UMFD-\d+|defaultuser\d*|SYSTEM|LOCAL SERVICE|NETWORK SERVICE|ANONYMOUS LOGON|S-1-5-(?:18|19|20)|-|.*\$)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The sentence: „Pe 6 noiembrie 2026, la ora 15:15, pe calculatorul LAB-WS02, Popescu Ion (contul ion.popescu) a introdus stick-ul «SanDisk Cruzer Blade».”</summary>
    /// <param name="personOf">The person an account belongs to (users register), or null when the register does not know it.</param>
    /// <param name="zone">The time zone the reader lives in (the station's); times are never shown in UTC here.</param>
    public static string Tell(Finding f, Func<string, string?>? personOf, TimeZoneInfo zone)
    {
        var what = f.PlainWhat.Length > 0 ? f.PlainWhat : Fallback(f);
        var when = When(f.FirstSeenUtc, f.LastSeenUtc, zone);
        var where = f.Host.Length > 0 && f.Host != "-" ? $"pe calculatorul {f.Host}" : "";
        var lead = string.Join(", ", new[] { when, where }.Where(s => s.Length > 0));
        // a PlainWhat that starts with a capital letter is a whole clause with its own subject („Windows a oprit…”, „Cineva a încercat…”)
        var clause = what.Length > 0 && char.IsUpper(what[0]) ? what : $"{Who(f.User, personOf)} {what}";
        var sentence = lead.Length > 0 ? $"{lead}, {LowerFirst(clause)}" : UpperFirst(clause);
        return UpperFirst(sentence.TrimEnd('.', ' ')) + ".";
    }

    /// <summary>Who did it, in words: the person and the account, the account alone, or that the logs do not say.</summary>
    public static string Who(string users, Func<string, string?>? personOf)
    {
        var people = users.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => !StationAccount.IsMatch(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (people.Count == 0)
            return users.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(u => u != "-")
                ? "Windows (un serviciu al sistemului, nu o persoană)"
                : "cineva (probele nu arată contul)";
        var named = people.Select(u => Name(u, personOf)).ToList();
        return named.Count == 1 ? named[0] : $"una dintre persoanele conectate atunci ({string.Join(", ", named.Take(4))}{(named.Count > 4 ? " și alții" : "")})";
    }

    private static string Name(string account, Func<string, string?>? personOf)
    {
        var shortName = account.Contains('\\') ? account[(account.LastIndexOf('\\') + 1)..] : account;
        if (shortName.Contains('@')) shortName = shortName[..shortName.IndexOf('@')];
        var person = personOf?.Invoke(account) ?? personOf?.Invoke(shortName);
        return person is { Length: > 0 } p ? $"{p} (contul {shortName})" : $"utilizatorul cu contul „{shortName}”";
    }

    /// <summary>„Pe 6 noiembrie 2026, la ora 15:15”, „între 15:10 și 15:20”, or from one day to another; "" when the time is unknown.</summary>
    public static string When(DateTimeOffset? firstUtc, DateTimeOffset? lastUtc, TimeZoneInfo zone)
    {
        if (firstUtc is not { } a) return "";
        var f = TimeZoneInfo.ConvertTime(a, zone);
        var l = lastUtc is { } b ? TimeZoneInfo.ConvertTime(b, zone) : f;
        string Day(DateTimeOffset d) => d.ToString("d MMMM yyyy", Ro);
        string Hour(DateTimeOffset d) => d.ToString("HH:mm", Ro);
        if (l - f < TimeSpan.FromMinutes(1)) return $"Pe {Day(f)}, la ora {Hour(f)}";
        if (f.Date == l.Date) return $"Pe {Day(f)}, între orele {Hour(f)} și {Hour(l)}";
        return $"Între {Day(f)}, ora {Hour(f)}, și {Day(l)}, ora {Hour(l)}";
    }

    // ---- fallback for a rule that does not tell its own story yet: the title without the technical words ----

    private static readonly (Regex Pattern, string With)[] Technical =
    [
        (new(@"\s*\((?:[^()]*\b(?:Security|System|Application|PowerShell|Sysmon|eveniment(?:ul)?|EventRecordID|evt)\b[^()]*)\)", RegexOptions.IgnoreCase), ""),
        (new(@"\b(?:evenimentul|evenimentele|eveniment|event|ID)\s+\d{2,5}\b", RegexOptions.IgnoreCase), ""),
        (new(@"\b(?:Security|System|Sysmon|PowerShell|Application)\s+\d{2,5}\b"), ""),
        (new(@"\bT\d{4}(?:\.\d{3})?\b"), ""),
        (new(@"\bS-1-\d+(?:-\d+)+\b"), "un cont"),
        (new(@"\b[A-Z]{2,}(?:-[A-Z0-9]{2,})+\b"), ""),
        (new(@"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?"), ""),
        (new(@"\bUTC\b"), ""),
        (new(@"\s{2,}"), " "),
        (new(@"\s+([,.;:])"), "$1"),
        (new(@"[:;,]\s*$"), ""),
    ];

    /// <summary>The title of a finding without event codes, technique ids, SIDs and rule ids, as a sentence about what was seen.</summary>
    public static string Fallback(Finding f)
    {
        var t = WithoutTechnicalTerms(f.Title);
        return t.Length == 0 ? "S-a observat ceva ce trebuie verificat" : "S-a observat: " + LowerFirst(t);
    }

    public static string WithoutTechnicalTerms(string text)
    {
        var t = text ?? "";
        foreach (var (p, w) in Technical) t = p.Replace(t, w);
        return t.Trim().Trim(':', ';', ',', '·', '—', '-').Trim();
    }

    private static string UpperFirst(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Ro) + s[1..];

    // a clause that starts with a proper name or an acronym keeps its capital (only a leading ordinary word is lowered)
    private static string LowerFirst(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) && !s.StartsWith("Windows", StringComparison.Ordinal) && !LooksLikeName(s) ? char.ToLower(s[0], Ro) + s[1..] : s;

    // a person's name from the register („Popescu Ion (contul ion.popescu) a …”): the only subject Who() writes with a capital
    private static bool LooksLikeName(string s) => s.IndexOf(" (contul ", StringComparison.Ordinal) is > 0 and < 80;
}
