using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// Whose story a group is: one person (or one account the register does not know), several accounts at once, nobody the sentence names as the one who did
/// it, or Windows itself.
/// </summary>
public enum PersonStoryKind { Person, SeveralPeople, NoAuthor, Windows }

/// <summary>One finding in a person's story, in plain words.</summary>
public sealed record PersonStoryLine(DateTimeOffset? WhenUtc, string Sentence, Severity Severity, string FindingId);

/// <summary>Everything the logs show one person did (or one of the shared groups), in the order it happened.</summary>
public sealed record PersonStory(string Heading, PersonStoryKind Kind, IReadOnlyList<PersonStoryLine> Lines, int Serious, int Omitted)
{
    /// <summary>„Popescu Ion (contul ion.popescu): 5 constatări, dintre care 2 grave”.</summary>
    public string Title
    {
        get
        {
            var n = Lines.Count + Omitted;
            var serious = Serious == 0 ? "" : Serious == 1 ? ", dintre care una gravă" : $", dintre care {RoText.Count(Serious, "gravă", "grave")}";
            return $"{Heading}: {RoText.Count(n, "constatare", "constatări")}{serious}";
        }
    }

    /// <summary>„… și încă 3 constatări, nelistate aici.”, or "" when every one is listed.</summary>
    public string OmittedLine => Omitted == 0 ? "" : $"… și încă {RoText.CountFem(Omitted, "constatare", "constatări")}, nelistate aici.";
}

/// <summary>
/// „Cine ce a făcut” (owner 2026-11-06: „cine se face vinovat”, written for someone who is not a specialist): the findings regrouped by the person the
/// sentence names as the one who did it, each person's in the order it happened, the persons with serious findings first.
/// <list type="bullet">
/// <item>A finding goes to a person only when its sentence makes that account the subject. A whole clause with its own subject („Contul „ana” a fost creat de
/// …”, „Cineva a încercat parole pentru contul „ana””) names the account as the object, often the victim: it goes to the group without an author.</item>
/// <item>A finding made while several accounts were logged on is put on none of them.</item>
/// <item>Accounts are told apart as the logs write them: LAB\ana and WS01\ana are two accounts, and a local Administrator is one per computer. Only the users
/// register joins two accounts into one person (it is the register's statement). A SID is first resolved to its name from the case's own events.</item>
/// <item>The logs show the account, not the hand on the keyboard: the section never says who is guilty.</item>
/// </list>
/// Pure; the report and the screen print the same list.
/// </summary>
public static class PeopleStory
{
    /// <summary>The sentences printed above the list.</summary>
    public const string Intro =
        "Pentru fiecare persoană, ce arată jurnalele că a făcut contul ei, în ordinea în care s-a întâmplat. O constatare este trecută la o persoană doar " +
        "când propoziția o numește pe ea ca autor; când contul doar apare în faptă (contul a fost creat de altcineva, cineva a încercat parole pentru el), " +
        "constatarea stă la „Fără un autor anume”. Ce s-a întâmplat cât erau conectate mai multe conturi nu este pus pe seama niciunuia. Jurnalele arată " +
        "contul folosit, nu omul de la tastatură: lista nu stabilește vinovăția nimănui, aceasta rămâne concluzia investigatorului. Constatările doar " +
        "informative și cele explicate ca activitate obișnuită nu sunt numărate aici.";

    /// <summary>Lines kept per group; the rest is counted.</summary>
    public const int DefaultMaxLines = 12;

    // the built-in local accounts every computer has: the same name on two computers is two accounts
    private static readonly HashSet<string> LocalBuiltIn = new(StringComparer.OrdinalIgnoreCase) { "Administrator", "Administrateur", "Guest", "Invitat", "DefaultAccount" };

    private sealed class Group(PersonStoryKind kind, string? person)
    {
        public PersonStoryKind Kind { get; } = kind;
        public string? Person { get; } = person;
        public List<string> Accounts { get; } = [];
        public string Account = "", Host = "", Qualifier = "";
        public List<Finding> Items { get; } = [];
    }

    /// <param name="tell">The plain sentence of a finding (the report passes its own, so the two parts never disagree).</param>
    /// <param name="accountOfSid">The account name the case's events give a SID (<see cref="AccountResolver.FromEvents"/>), or null.</param>
    public static IReadOnlyList<PersonStory> Build(IEnumerable<Finding> findings, Func<string, string?>? personOf, Func<Finding, string> tell,
                                                   Func<string, string?>? accountOfSid = null, int maxLines = DefaultMaxLines)
    {
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        Group G(string key, PersonStoryKind kind, string? person = null) => groups.TryGetValue(key, out var g) ? g : groups[key] = new Group(kind, person);

        // an incident chain repeats findings already told; an informative or known-benign finding is not something a person „did”
        foreach (var f in findings.Where(f => f.RuleId != "INCIDENT-CHAIN" && f.Classification != Classification.BenignKnown && f.Severity > Severity.Info))
        {
            // the same test PlainStory.Tell makes: a capital first letter is a whole clause with its own subject, so the account is not the author
            var what = f.PlainWhat.Length > 0 ? f.PlainWhat : PlainStory.Fallback(f);
            if (what.Length > 0 && char.IsUpper(what[0]))
            {
                G(what.StartsWith("Windows ", StringComparison.Ordinal) ? "~windows" : "~none",
                  what.StartsWith("Windows ", StringComparison.Ordinal) ? PersonStoryKind.Windows : PersonStoryKind.NoAuthor).Items.Add(f);
                continue;
            }

            var all = (f.User ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(u => u != "-")
                                     .Select(u => (Raw: u, Name: AccountResolver.IsSid(u) && accountOfSid?.Invoke(u) is { Length: > 0 } n ? n : u)).ToList();
            var people = all.Where(a => !PlainStory.IsStationAccount(a.Name) && !PlainStory.IsOwnMachineAccount(a.Name, f.Host))
                            .Select(a => Identify(a.Raw, a.Name, f.Host, personOf)).ToList();
            var keys = people.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (keys.Count == 1)
            {
                var p = people[0];
                var g = G(p.Key, PersonStoryKind.Person, p.Person);
                foreach (var a in people.Select(x => x.Account)) if (!g.Accounts.Contains(a, StringComparer.OrdinalIgnoreCase)) g.Accounts.Add(a);
                if (g.Account.Length == 0) { g.Account = p.Name; g.Host = f.Host; g.Qualifier = p.Qualifier; }
                g.Items.Add(f);
            }
            else if (keys.Count > 1) G("~several", PersonStoryKind.SeveralPeople).Items.Add(f);
            else if (all.Count > 0) G("~windows", PersonStoryKind.Windows).Items.Add(f);
            else G("~none", PersonStoryKind.NoAuthor).Items.Add(f);
        }

        var headed = groups.Values.Select(g => (g, Heading: Heading(g))).ToList();
        // two accounts the logs write differently but that read the same („Administrator” of WS01 and of WS02): the heading says which is which
        var repeated = headed.GroupBy(h => h.Heading, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        return headed.Select(h =>
            {
                var heading = repeated.Contains(h.Heading) && h.g.Qualifier.Length > 0 ? $"{h.Heading} — {h.g.Qualifier}" : h.Heading;
                var ordered = h.g.Items.OrderBy(f => f.FirstSeenUtc ?? DateTimeOffset.MaxValue).ThenByDescending(f => f.Severity).ThenBy(f => f.FindingId, StringComparer.Ordinal).ToList();
                var lines = ordered.Take(Math.Max(1, maxLines)).Select(f => new PersonStoryLine(f.FirstSeenUtc, tell(f), f.Severity, f.FindingId)).ToList();
                return new PersonStory(heading, h.g.Kind, lines, ordered.Count(f => f.Severity >= Severity.High), ordered.Count - lines.Count);
            })
            .OrderBy(s => s.Kind)
            .ThenByDescending(s => s.Serious).ThenByDescending(s => s.Lines.Count + s.Omitted).ThenBy(s => s.Heading, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>The account as the logs write it, and the person the users register gives it (the register alone joins two accounts into one person).</summary>
    private static (string Key, string? Person, string Account, string Name, string Qualifier) Identify(string raw, string name, string host, Func<string, string?>? personOf)
    {
        var n = name.Trim();
        string domain = "", shortName = n;
        if (AccountResolver.IsSid(n)) shortName = n;
        else if (n.Contains('\\')) { domain = n[..n.LastIndexOf('\\')].Trim(); shortName = n[(n.LastIndexOf('\\') + 1)..].Trim(); }
        var person = PersonOf(n, shortName, raw, personOf);
        var hostShort = (host ?? "").Trim() is var h && h.Contains('.') ? h[..h.IndexOf('.')] : (host ?? "").Trim();
        var display = shortName.Contains('@') ? shortName[..shortName.IndexOf('@')] : shortName;
        if (person is not null) return ("p:" + person, person, display, n, "");
        if (AccountResolver.IsSid(n)) return ("sid:" + n, null, display, n, $"identificatorul se termină în {n[(n.LastIndexOf('-') + 1)..]}");
        if (shortName.Contains('@')) return ("upn:" + shortName, null, display, n, $"contul de domeniu {shortName}");
        bool local = domain.Length > 0
            ? domain == "." || domain.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase) || domain.Equals(hostShort, StringComparison.OrdinalIgnoreCase)
            : LocalBuiltIn.Contains(shortName);
        if (local) return ($"local:{hostShort}\\{shortName}", null, display, n, hostShort.Length > 0 ? $"cont local pe calculatorul {hostShort}" : "cont local");
        if (domain.Length > 0) return ($"dom:{domain}\\{shortName}", null, display, n, $"domeniul {domain}");
        return ("name:" + shortName, null, display, n, "jurnalele nu arată domeniul");
    }

    /// <summary>The person the users register gives for the account; null when it gives none, or only repeats the account name („Preia de pe stație”).</summary>
    private static string? PersonOf(string account, string shortName, string raw, Func<string, string?>? personOf)
    {
        if (personOf is null) return null;
        var p = (personOf(account) ?? personOf(shortName) ?? (raw != account ? personOf(raw) : null))?.Trim();
        return p is { Length: > 0 } && !p.Equals(shortName, StringComparison.OrdinalIgnoreCase) && !p.Equals(account.Trim(), StringComparison.OrdinalIgnoreCase) ? p : null;
    }

    private static string Heading(Group g) => g.Kind switch
    {
        PersonStoryKind.SeveralPeople => "Cât erau conectate mai multe conturi (jurnalele nu arată care dintre ele)",
        PersonStoryKind.NoAuthor => "Fără un autor anume (cine este implicat scrie în fiecare propoziție)",
        PersonStoryKind.Windows => "Windows și serviciile lui (nu o persoană)",
        _ when g.Person is not null => $"{g.Person} ({(g.Accounts.Count == 1 ? "contul " + g.Accounts[0] : "conturile " + string.Join(", ", g.Accounts.Order(StringComparer.OrdinalIgnoreCase)))})",
        _ => UpperFirst(PlainStory.Who(g.Account, null, g.Host)),
    };

    private static string UpperFirst(string s) => s.Length == 0 ? s : char.ToUpper(s[0], System.Globalization.CultureInfo.GetCultureInfo("ro-RO")) + s[1..];
}
