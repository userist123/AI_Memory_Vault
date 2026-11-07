using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
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

    /// <summary>Windows' own sessions and services (the account AFTER its domain is stripped): never "who did it" (lab WS02: DWM-1, UMFD-0 and defaultuser0 were listed as candidates for a USB stick).</summary>
    private static readonly Regex StationAccount = new(@"^(?:DWM-\d+|UMFD-\d+|defaultuser\d*|SYSTEM|LOCAL\s*SYSTEM|LOCAL\s*SERVICE|NETWORK\s*SERVICE|ANONYMOUS LOGON|NT AUTHORITY|IUSR|TrustedInstaller|WDAGUtilityAccount|S-1-5-(?:18|19|20)|S-1-5-(?:90|96)-\d+(?:-\d+)*|-)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // NT SERVICE\<service> and IIS APPPOOL\<pool> are per-service virtual accounts Windows makes (round-2 review residual)
    private static readonly string[] StationDomains = ["NT AUTHORITY", "Window Manager", "Font Driver Host", "NT SERVICE", "IIS APPPOOL", "NT VIRTUAL MACHINE"];

    /// <summary>True for Windows' own accounts. The match is made on the name AFTER the domain is removed („NT AUTHORITY\SYSTEM”, „Window Manager\DWM-1”); any „NT AUTHORITY” account counts.</summary>
    public static bool IsStationAccount(string account)
    {
        var a = (account ?? "").Trim();
        var slash = a.LastIndexOf('\\');
        if (slash >= 0 && StationDomains.Contains(a[..slash].Trim(), StringComparer.OrdinalIgnoreCase)) return true;
        var shortName = slash >= 0 ? a[(slash + 1)..].Trim() : a;
        return StationAccount.IsMatch(shortName);
    }

    /// <summary>A name ending in $ is the station only when it is this computer's own account (Host + "$"); an attacker can name an account „labbackup$”.</summary>
    public static bool IsOwnMachineAccount(string account, string host)
    {
        var a = account ?? "";
        var slash = a.LastIndexOf('\\');
        var shortName = (slash >= 0 ? a[(slash + 1)..] : a).Trim();
        if (string.IsNullOrWhiteSpace(host) || host == "-" || !shortName.EndsWith('$')) return false;
        var h = host.Trim();
        var dot = h.IndexOf('.');
        if (dot > 0) h = h[..dot];
        return shortName.Equals(h + "$", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The sentence: „Pe 6 noiembrie 2026, la ora 15:15, pe calculatorul LAB-WS02, Popescu Ion (contul ion.popescu) a introdus stick-ul «SanDisk Cruzer Blade».”</summary>
    /// <param name="personOf">The person an account belongs to (users register), or null when the register does not know it.</param>
    /// <param name="zone">The time zone the reader lives in (the station's); times are never shown in UTC here.</param>
    public static string Tell(Finding f, Func<string, string?>? personOf, TimeZoneInfo zone, Func<string, string?>? accountOfSid = null)
    {
        // a SID in User becomes the account's name when the case's events give it (and the register is then tried with the name first, then with the SID)
        var users = f.User ?? "";
        if (users.Length > 0 && accountOfSid is not null)
        {
            var nameToSid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            users = string.Join(", ", users.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(u =>
            {
                if (!AccountResolver.IsSid(u) || accountOfSid(u) is not { Length: > 0 } n) return u;
                nameToSid[n] = u;
                return n;
            }));
            var basePerson = personOf;
            if (nameToSid.Count > 0) personOf = a => basePerson?.Invoke(a) ?? (nameToSid.TryGetValue(a, out var sid) ? basePerson?.Invoke(sid) : null);
        }
        var what = f.PlainWhat.Length > 0 ? f.PlainWhat : Fallback(f);
        var when = When(f.FirstSeenUtc, f.LastSeenUtc, zone);
        var where = f.Host.Length > 0 && f.Host != "-" ? $"pe calculatorul {f.Host}" : "";
        var lead = string.Join(", ", new[] { when, where }.Where(s => s.Length > 0));
        // a PlainWhat that starts with a capital letter is a whole clause with its own subject („Windows a oprit…”, „Cineva a încercat…”)
        var wholeClause = what.Length > 0 && char.IsUpper(what[0]);
        var clause = wholeClause ? (lead.Length > 0 ? LowerFirst(what) : what) : $"{Who(users, personOf, f.Host)} {what}";
        var sentence = lead.Length > 0 ? $"{lead}, {clause}" : UpperFirst(clause);
        return UpperFirst(sentence.TrimEnd('.', ' ')) + ".";
    }

    /// <summary>The users register the application holds now (account → person); set by the application when the register loads or is saved. Null = none.</summary>
    public static Func<string, string?>? CurrentPersonOf
    {
        get => _currentPersonOf;
        set { _currentPersonOf = value; CurrentPersonOfChanged?.Invoke(); }
    }
    private static Func<string, string?>? _currentPersonOf;

    /// <summary>Raised when the users register changes, so a screen that groups by person („Cine ce a făcut”) tells its sentences again.</summary>
    public static event Action? CurrentPersonOfChanged;

    /// <summary>
    /// The sentence as it should read NOW: told again with the current users register, so a person added to the register after the analysis is named on the
    /// card and in the report. The stored sentence is kept when there is no register, or when the finding's account is a SID (the analysis resolved it from
    /// the case's events, which are not at hand here).
    /// </summary>
    public static string Current(Finding f)
    {
        if (f.Plain.Length > 0 && (CurrentPersonOf is null || AccountResolver.IsSid(f.User))) return f.Plain;
        return Tell(f, CurrentPersonOf, TimeZoneInfo.Local);
    }

    /// <summary>Who did it, in words: the person and the account, the account alone, or that the logs do not say.</summary>
    public static string Who(string users, Func<string, string?>? personOf, string host = "")
    {
        users ??= "";
        var all = users.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var people = all.Where(u => !IsStationAccount(u) && !IsOwnMachineAccount(u, host)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (people.Count == 0)
            return all.Any(u => u != "-")
                ? "Windows (un serviciu al sistemului, nu o persoană)"
                : "cineva (probele nu arată contul)";
        var named = people.Select(u => Name(u, personOf)).ToList();
        return named.Count == 1 ? named[0] : $"una dintre persoanele conectate atunci ({string.Join(", ", named.Take(4))}{(named.Count > 4 ? " și alții" : "")})";
    }

    private static string Name(string account, Func<string, string?>? personOf)
    {
        // only the SID is known: never printed; the register may still know whose it is
        if (AccountResolver.IsSid(account))
            return personOf?.Invoke(account) is { Length: > 0 } sidPerson ? sidPerson : "un cont pe care probele îl arată doar prin identificatorul lui";
        var shortName = account.Contains('\\') ? account[(account.LastIndexOf('\\') + 1)..] : account;
        if (shortName.Contains('@')) shortName = shortName[..shortName.IndexOf('@')];
        var person = personOf?.Invoke(account) ?? personOf?.Invoke(shortName);
        // „Preia de pe stație” fills the person column with the account name until someone writes the person: that is no person yet (lab VM 2026-11-07)
        if (person is { Length: > 0 } p && !p.Trim().Equals(shortName.Trim(), StringComparison.OrdinalIgnoreCase) && !p.Trim().Equals(account.Trim(), StringComparison.OrdinalIgnoreCase))
            return $"{p} (contul {shortName})";
        // a name ending in $ is usually a computer's own account, but an attacker can name an account so (lab S5: „labbackup$”): never hidden as „Windows”
        return shortName.EndsWith('$') ? $"contul „{shortName}” (nume de cont de calculator: verificați că este un calculator real)" : $"utilizatorul cu contul „{shortName}”";
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

    private const string LogNames = "Security|System|Sysmon|PowerShell|Application";
    private static readonly Regex EventWord = new(@"\b(?:evenimentul|evenimentele|eveniment|event|ID)\s+(\d{3,5})\b", RegexOptions.IgnoreCase);
    private static readonly Regex LogAndNumber = new(@"\b(?:" + LogNames + @")\s+(\d{3,5})\b");
    private static readonly Regex ParenGroup = new(@"\s*\(([^()]*)\)");
    private static readonly Regex TechnicalId = new(@"^T\d{4}(?:\.\d{3})?$");
    private static readonly Regex AnyRuleShape = new(@"\b[A-Z]{2,}(?:-[A-Z0-9]{2,})+\b");
    private static readonly Regex TechnicalWord = new(@"^(?:" + LogNames + @"|eveniment(?:ul|ele)?|event|EventRecordID|evt|EID|ID|vezi|și|sau|cu|UTC)$", RegexOptions.IgnoreCase);
    private static readonly Regex Sid = new(@"^S-1-\d+(?:-\d+)+$");
    private static readonly Regex GuidTok = new(@"^\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?$");

    private static bool IsYear(string digits) => int.TryParse(digits, out var n) && n is >= 1900 and <= 2100;

    private static bool IsRuleId(string token) => RuleContracts.For(token) is not null;

    // a parenthesis goes only when every word in it is technical: „(Security 1100)”, „(eveniment 4688)”, „(vezi MEDIA-FILE-ACTIVITY)”; never „(fișier Application.docx)”, „(copie eveniment 2026)”
    private static bool WholeParenthesisIsTechnical(string content)
    {
        var words = content.Split([' ', ',', ';', '/', '+', '·'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return false;
        return words.All(w => TechnicalWord.IsMatch(w) || TechnicalId.IsMatch(w) || Sid.IsMatch(w) || GuidTok.IsMatch(w) || IsRuleId(w)
            || (w.All(char.IsDigit) && w.Length is >= 3 and <= 5 && !IsYear(w)));
    }

    /// <summary>The title of a finding without event codes, technique ids, SIDs and rule ids, as a sentence about what was seen.</summary>
    public static string Fallback(Finding f)
    {
        var t = WithoutTechnicalTerms(f.Title);
        return t.Length == 0 ? "S-a observat ceva ce trebuie verificat" : "S-a observat: " + t;
    }

    public static string WithoutTechnicalTerms(string text)
    {
        var t = text ?? "";
        t = ParenGroup.Replace(t, m => WholeParenthesisIsTechnical(m.Groups[1].Value) ? "" : m.Value);
        t = EventWord.Replace(t, m => IsYear(m.Groups[1].Value) ? m.Value : "");
        t = LogAndNumber.Replace(t, m => IsYear(m.Groups[1].Value) ? m.Value : "");
        t = Regex.Replace(t, @"\bT\d{4}(?:\.\d{3})?\b", "");
        t = Regex.Replace(t, @"\bS-1-\d+(?:-\d+)+\b", "un cont");
        // only real rule ids go; computer names (LAB-WS05) and document names (RAPORT-2026, NATO-SECRET) are content
        t = AnyRuleShape.Replace(t, m => IsRuleId(m.Value) ? "" : m.Value);
        t = Regex.Replace(t, @"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?", "");
        t = Regex.Replace(t, @"\bUTC\b", "");
        t = Regex.Replace(t, @"\s{2,}", " ");
        t = Regex.Replace(t, @"\s+([,.;:])", "$1");
        t = Regex.Replace(t, @",(?:\s*,)+", ",");
        t = Regex.Replace(t, @"[:;,]\s*$", "");
        return t.Trim().Trim(':', ';', ',', '·', '—', '-').Trim();
    }

    private static string UpperFirst(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Ro) + s[1..];

    // the fixed openings of the clauses this product writes; only these lose their capital inside a sentence (never „Microsoft”, „PowerShell”, „Windows”, a computer or a person)
    private static readonly string[] LowerablePrefixes =
    [
        "S-a ", "Cineva", "Contul", "Conturile", "Calculatorul", "Programul", "Programele", "Stick-ul", "Serviciul", "A fost", "A rulat", "A sosit", "După ce", "Mai multe", "Au fost", "Există ", "Nu ",
    ];

    /// <summary>A whole clause put after „Pe 6 noiembrie, la ora 15:15,”: lowers the capital of the fixed openings only.</summary>
    public static string LowerFirst(string s)
    {
        foreach (var p in LowerablePrefixes)
            if (s.StartsWith(p, StringComparison.Ordinal) && (p.EndsWith(' ') || s.Length == p.Length || !char.IsLetterOrDigit(s[p.Length])))
                return char.ToLower(s[0], Ro) + s[1..];
        return s;
    }
}
