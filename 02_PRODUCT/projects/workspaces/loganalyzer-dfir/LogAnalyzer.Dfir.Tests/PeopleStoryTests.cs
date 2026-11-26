using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>„Cine ce a făcut” (owner 2026-11-06): the findings regrouped by the person the sentence names as the one who did it, in time order, never a verdict of guilt.</summary>
public sealed class PeopleStoryTests
{
    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);

    private static Finding F(string id, string user, int minutes, Severity sev = Severity.Medium, string rule = "R", Classification cls = Classification.Candidate,
                             string what = "a făcut ceva", string host = "LAB-WS02") => new()
    {
        FindingId = id, RuleId = rule, Title = "t", Description = "x", Severity = sev, Classification = cls, User = user, Host = host, PlainWhat = what,
        FirstSeenUtc = T0.AddMinutes(minutes), LastSeenUtc = T0.AddMinutes(minutes),
    };

    private static UsersRegister Register() => new() { Rows = [new UserRow { Person = "Popescu Ion", Accounts = "LAB\\ion.popescu; ipopescu" }] };

    private static IReadOnlyList<PersonStory> Build(IEnumerable<Finding> fs, Func<string, string?>? personOf = null, int max = PeopleStory.DefaultMaxLines, Func<string, string?>? sids = null) =>
        PeopleStory.Build(fs, personOf, f => PlainStory.Tell(f, personOf, Bucharest, sids), sids, max);

    [Fact]
    public void Each_person_gets_their_own_findings_in_time_order_and_two_accounts_of_one_person_are_one_story()
    {
        var s = Build([
            F("F-3", "ipopescu", 30, what: "a scris pe un suport amovibil un fișier"),
            F("F-1", "LAB\\ion.popescu", 5, Severity.High, what: "a conectat stick-ul „SanDisk”"),
            F("F-2", "ana.pop", 10),
            F("F-4", "LAB\\ion.popescu, ipopescu", 40),   // both accounts are the same person, by the register
        ], Register().PersonOf);

        Assert.Equal(2, s.Count);
        var ion = s[0];   // the person with a serious finding comes first
        Assert.Equal(PersonStoryKind.Person, ion.Kind);
        Assert.Equal("Popescu Ion (conturile ion.popescu, ipopescu): 3 constatări, dintre care una gravă", ion.Title);
        Assert.Equal(["F-1", "F-3", "F-4"], ion.Lines.Select(l => l.FindingId));
        Assert.StartsWith("Pe 6 noiembrie 2026, la ora 15:05", ion.Lines[0].Sentence);
        Assert.Equal("Utilizatorul cu contul „ana.pop”: 1 constatare", s[1].Title);
    }

    [Fact]
    public void An_account_the_sentence_names_only_as_the_object_or_the_victim_is_not_the_author()
    {
        // lab DC01: the account created by Administrator and the account whose password was guessed were listed as if they had done it
        var s = Build([
            F("F-1", "ana.pop", 0, what: "Contul „ana.pop” a fost creat de contul „Administrator”"),
            F("F-2", "ana.pop", 1, Severity.High, what: "Cineva a încercat de 14 ori o parolă greșită pentru contul „ana.pop”"),
            F("F-3", "S-1-5-21-1-2-3-1105", 2, what: "Windows a rulat un modul de administrare obișnuit"),
        ]);
        Assert.DoesNotContain(s, x => x.Kind == PersonStoryKind.Person);
        Assert.Equal(["F-1", "F-2"], s.Single(x => x.Kind == PersonStoryKind.NoAuthor).Lines.Select(l => l.FindingId));
        Assert.Equal(["F-3"], s.Single(x => x.Kind == PersonStoryKind.Windows).Lines.Select(l => l.FindingId));
        Assert.Contains("nu stabilește vinovăția", PeopleStory.Intro);
    }

    [Fact]
    public void A_finding_made_while_several_accounts_were_logged_on_is_put_on_none_of_them_even_when_their_names_read_the_same()
    {
        var s = Build([
            F("F-1", "ion.popescu, ana.pop", 0, Severity.Critical),
            F("F-2", "ana.pop", 1),
            F("F-3", @"WS01\Administrator, CORP\Administrator", 2, Severity.Critical, host: "WS01"),   // review: one heading, two accounts
            F("F-4", "ion@corp.local, CORP\\ion", 3),                                                   // the logs do not say these are one account
        ]);
        var ana = Assert.Single(s, x => x.Kind == PersonStoryKind.Person);
        Assert.Equal(["F-2"], ana.Lines.Select(l => l.FindingId));
        Assert.Equal(0, ana.Serious);
        var shared = Assert.Single(s, x => x.Kind == PersonStoryKind.SeveralPeople);
        Assert.Equal(["F-1", "F-3", "F-4"], shared.Lines.Select(l => l.FindingId));
        Assert.Contains("nu arată care dintre ele", shared.Heading);
    }

    [Fact]
    public void The_same_account_name_on_two_computers_or_in_two_domains_is_two_stories_and_the_heading_says_which()
    {
        var s = Build([
            F("F-1", @"WS01\Administrator", 0, host: "WS01"),
            F("F-2", @"WS02\Administrator", 1, host: "WS02"),
            F("F-3", "Administrator", 2, host: "WS03"),          // the built-in local account: one per computer even without a domain
            F("F-4", @"LAB\ana", 3),
            F("F-5", @"OTHER\ana", 4),
        ]);
        Assert.Equal(5, s.Count);
        Assert.All(s, x => Assert.Single(x.Lines));
        Assert.Contains(s, x => x.Heading == "Utilizatorul cu contul „Administrator” — cont local pe calculatorul WS01");
        Assert.Contains(s, x => x.Heading == "Utilizatorul cu contul „Administrator” — cont local pe calculatorul WS03");
        Assert.Contains(s, x => x.Heading == "Utilizatorul cu contul „ana” — domeniul LAB");
        Assert.Equal(s.Count, s.Select(x => x.Heading).Distinct().Count());
    }

    [Fact]
    public void A_SID_the_case_resolves_joins_its_account_and_Windows_session_SIDs_are_Windows()
    {
        Func<string, string?> sids = sid => sid == "S-1-5-21-1-2-3-1105" ? "ana.pop" : null;
        var s = Build([
            F("F-1", "S-1-5-21-1-2-3-1105", 0),
            F("F-2", "ana.pop", 1),
            F("F-3", "S-1-5-21-1-2-3-1200", 2),
            F("F-4", "S-1-5-21-1-2-3-1300", 3),
            F("F-5", "S-1-5-90-0-1", 4),       // DWM session
            F("F-6", "S-1-5-96-0-1", 5),       // font driver session
        ], sids: sids);
        Assert.Equal(["F-1", "F-2"], s.Single(x => x.Heading == "Utilizatorul cu contul „ana.pop”").Lines.Select(l => l.FindingId));
        Assert.Contains(s, x => x.Heading.EndsWith("identificatorul se termină în 1200") && x.Lines.Single().FindingId == "F-3");
        Assert.Contains(s, x => x.Heading.EndsWith("identificatorul se termină în 1300"));
        Assert.Equal(["F-5", "F-6"], s.Single(x => x.Kind == PersonStoryKind.Windows).Lines.Select(l => l.FindingId));
    }

    [Fact]
    public void Windows_accounts_and_findings_without_an_account_have_their_own_groups_after_the_people()
    {
        var s = Build([
            F("F-1", "NT AUTHORITY\\SYSTEM", 0, Severity.High),
            F("F-2", "", 1, Severity.Critical),
            F("F-3", "LAB-WS02$", 2),           // the station's own computer account
            F("F-4", "labbackup$", 3),          // an attacker's account named like a computer's is a person's, never Windows
            F("F-5", "DWM-1, ana.pop", 4),      // Windows' session beside a real account: the person
        ]);
        Assert.Equal([PersonStoryKind.Person, PersonStoryKind.Person, PersonStoryKind.NoAuthor, PersonStoryKind.Windows], s.Select(x => x.Kind));
        Assert.Contains(s, x => x.Heading.Contains("labbackup$") && x.Lines.Single().FindingId == "F-4");
        Assert.Contains(s, x => x.Heading.Contains("ana.pop") && x.Lines.Single().FindingId == "F-5");
        Assert.Equal(["F-1", "F-3"], s.Single(x => x.Kind == PersonStoryKind.Windows).Lines.Select(l => l.FindingId));
    }

    [Fact]
    public void Chains_benign_and_informative_findings_are_left_out_and_long_stories_count_what_is_not_listed()
    {
        var fs = new List<Finding>
        {
            F("C-1", "ana.pop", 0, Severity.Critical, rule: "INCIDENT-CHAIN"),
            F("B-1", "ana.pop", 0, cls: Classification.BenignKnown),
            F("I-1", "ana.pop", 0, Severity.Info),
        };
        fs.AddRange(Enumerable.Range(1, 15).Select(i => F($"F-{i:00}", "ana.pop", i)));
        var ana = Assert.Single(Build(fs, max: 12));
        Assert.Equal(12, ana.Lines.Count);
        Assert.Equal(3, ana.Omitted);
        Assert.Equal("Utilizatorul cu contul „ana.pop”: 15 constatări", ana.Title);
        Assert.Equal("… și încă 3 constatări, nelistate aici.", ana.OmittedLine);
        Assert.DoesNotContain(ana.Lines, l => l.FindingId is "C-1" or "B-1" or "I-1");
        Assert.Empty(Build([F("I-1", "ana.pop", 0, Severity.Info)]));
        Assert.Contains("informative", PeopleStory.Intro);
    }

    [Fact]
    public void A_register_row_that_only_repeats_the_account_name_is_not_a_person()
    {
        var reg = new UsersRegister { Rows = [new UserRow { Person = "ana.pop", Accounts = "ana.pop" }] };
        var only = Assert.Single(Build([F("F-1", "ana.pop", 0)], reg.PersonOf));
        Assert.Equal("Utilizatorul cu contul „ana.pop”: 1 constatare", only.Title);
    }
}
