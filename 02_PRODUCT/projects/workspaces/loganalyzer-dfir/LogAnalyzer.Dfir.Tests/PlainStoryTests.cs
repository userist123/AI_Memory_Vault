using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The plain sentence of a finding (owner 2026-11-06): who (the person from the users register), what, with what, when in Romanian time, on which
/// computer; never an event code, a rule id, a SID or a technique id.
/// </summary>
public sealed class PlainStoryTests
{
    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 13, 15, 0, TimeSpan.Zero);

    private static Finding F(string rule, string title, string user = "", string plainWhat = "", string host = "", DateTimeOffset? last = null) => new()
    {
        FindingId = "F-1", RuleId = rule, Title = title, Description = "x", Severity = Severity.Medium, User = user, Host = host, PlainWhat = plainWhat,
        FirstSeenUtc = T0, LastSeenUtc = last ?? T0,
    };

    private static UsersRegister Register() => new() { Rows = [new UserRow { Person = "Popescu Ion", Accounts = "LAB\\ion.popescu; ipopescu", Sids = "S-1-5-21-1-2-3-1105" }] };

    [Fact]
    public void The_sentence_says_when_in_Romanian_time_where_who_by_name_and_what()
    {
        var f = F("MEDIA-UNREGISTERED", "Suport de stocare amovibil 4C53: NEÎNREGISTRAT", user: "ion.popescu", host: "LAB-WS02",
            plainWhat: "a conectat stick-ul „SanDisk Cruzer Blade”; acest stick NU este trecut în registrul de medii");
        Assert.Equal("Pe 6 noiembrie 2026, la ora 15:15, pe calculatorul LAB-WS02, Popescu Ion (contul ion.popescu) a conectat stick-ul „SanDisk Cruzer Blade”; acest stick NU este trecut în registrul de medii.",
            PlainStory.Tell(f, Register().PersonOf, Bucharest));
    }

    [Fact]
    public void Without_the_register_the_account_is_named_and_Windows_own_sessions_are_never_the_culprit()
    {
        // lab WS02: DWM-1, UMFD-0 and defaultuser0 were listed as who used a USB stick beside the real account
        Assert.Equal("utilizatorul cu contul „labadmin”", PlainStory.Who("defaultuser0, DWM-1, DWM-2, labadmin, UMFD-0", null));
        Assert.Equal("Windows (un serviciu al sistemului, nu o persoană)", PlainStory.Who("S-1-5-18", null));
        Assert.Equal("cineva (probele nu arată contul)", PlainStory.Who("", null));
        // lab S5: the attacker named the new account like a computer's; it is named, never hidden as „Windows”
        Assert.Equal("contul „labbackup$” (nume de cont de calculator: verificați că este un calculator real)", PlainStory.Who("LAB-WS01\\labbackup$", null));
        // Windows' per-service accounts are Windows, not a person (round-2 review residual)
        Assert.Equal("Windows (un serviciu al sistemului, nu o persoană)", PlainStory.Who(@"NT SERVICE\MSSQLSERVER", null));
        Assert.Equal("Windows (un serviciu al sistemului, nu o persoană)", PlainStory.Who("IUSR", null));
        // a register row whose person is still the account name (filled by „Preia de pe stație”) names no person
        var unfilled = new UsersRegister { Rows = [new UserRow { Person = "labadmin", Accounts = "labadmin" }] };
        Assert.Equal("utilizatorul cu contul „labadmin”", PlainStory.Who("labadmin", unfilled.PersonOf));
        Assert.Equal("o dată", PlainPhrases.Times(1));
        Assert.Equal("de 21 de ori", PlainPhrases.Times(21));
        Assert.Equal("Popescu Ion (contul ion.popescu)", PlainStory.Who("LAB\\ion.popescu", Register().PersonOf));
        Assert.Equal("Popescu Ion (contul ion.popescu)", PlainStory.Who("ion.popescu@lab.local", Register().PersonOf));
        Assert.StartsWith("una dintre persoanele conectate atunci (", PlainStory.Who("ion.popescu, ana.pop", Register().PersonOf));
    }

    [Fact]
    public void A_span_of_time_is_told_as_one_day_or_from_one_day_to_another()
    {
        Assert.Equal("Pe 6 noiembrie 2026, între orele 15:15 și 16:45", PlainStory.When(T0, T0.AddMinutes(90), Bucharest));
        Assert.Equal("Între 6 noiembrie 2026, ora 15:15, și 7 noiembrie 2026, ora 15:15", PlainStory.When(T0, T0.AddDays(1), Bucharest));
        Assert.Equal("", PlainStory.When(null, null, Bucharest));
    }

    [Theory]
    [InlineData("Jurnale șterse (Security 1100) de admin, eveniment 4719, T1070.001")]
    [InlineData("Cont adăugat într-un grup privilegiat: S-1-5-21-1061555764-450217201-3444499901-512 → Administrators (vezi MEDIA-FILE-ACTIVITY)")]
    [InlineData("Sesiune {48d281bc-8d6c-4a04-bfe1-ee6b131599b1} la 13:15 UTC")]
    public void A_rule_without_its_own_story_is_told_from_its_title_without_codes(string title)
    {
        var s = PlainStory.Tell(F("ANY-RULE", title, user: "admin"), null, Bucharest);
        Assert.DoesNotMatch(@"\b(?:1100|4719|T1070|S-1-5-21|MEDIA-FILE-ACTIVITY|UTC|48d281bc)\b", s);
        Assert.StartsWith("Pe 6 noiembrie 2026, la ora 15:15, s-a observat:", s);
    }

    [Fact]
    public void A_file_written_on_the_stick_and_one_opened_from_it_say_only_that_a_file_of_the_same_name_is_elsewhere()
    {
        var events = new List<TimelineEvent>
        {
            Ev("LNK", path: @"C:\Users\ion\Documents\Plan.docx"),
            Ev("EventLog:Security", eventId: "4663", fields: new() { ["ObjectName"] = @"E:\Plan.docx", ["AccessMask"] = "0x2", ["SubjectUserName"] = "ion.popescu" }),
            Ev("LNK", path: @"E:\Oferta.pdf", fields: new() { ["DriveType"] = "REMOVABLE" }),
            Ev("USN", path: @"C:\Users\ion\Desktop\Oferta.pdf", fields: new() { ["Reason"] = "FILE_CREATE" }),
        };
        var activity = Wp14Rules.ActivityOn(events, "E");
        Assert.Equal(@"a scris pe stick fișierul „Plan.docx” (un fișier cu același nume se află și în C:\Users\ion\Documents) și a deschis de pe stick fișierul „Oferta.pdf” (un fișier cu același nume se află și în C:\Users\ion\Desktop)",
            Wp14Rules.FilesStory("SanDisk", "E", activity, events));
        Assert.Equal("SanDisk Cruzer Blade", Wp14Rules.PlainName("SanDisk Cruzer Blade USB Device"));
    }

    [Fact]
    public void The_contract_writes_the_sentence_on_every_finding_with_the_register_names()
    {
        var f = F("MEDIA-UNREGISTERED", "Suport de stocare amovibil 4C53: NEÎNREGISTRAT", user: "ipopescu", plainWhat: "a conectat stick-ul „X”");
        var done = Assert.Single(FindingContract.Enrich([f], new FindingContractContext { NowUtc = T0, PersonOf = Register().PersonOf, Zone = Bucharest }));
        Assert.Equal("Pe 6 noiembrie 2026, la ora 15:15, Popescu Ion (contul ipopescu) a conectat stick-ul „X”.", done.Plain);
    }

    [Fact]
    public void A_clause_starting_with_Exista_is_lowered_after_the_time()
    {
        var f = F("MEDIA-OPTICAL-ACTIVITY", "CD/DVD", plainWhat: "Există urme de CD/DVD pe calculator");
        Assert.Equal("Pe 6 noiembrie 2026, la ora 15:15, există urme de CD/DVD pe calculator.", PlainStory.Tell(f, null, Bucharest));
    }

    [Fact]
    public void A_shared_or_well_known_sid_is_never_resolved_to_one_computers_account()
    {
        // round-2 review: S-1-5-18 named after another computer's account; cloned computers sharing a SID named the wrong person
        TimelineEvent Logon(string host, string sid, string name) => new()
        {
            Time = new Timestamp(T0, "", "test"), Source = "EventLog:Security", EvidenceId = "EV", EventId = "4624", Host = host, Summary = "x",
            Fields = new() { ["TargetUserSid"] = sid, ["TargetUserName"] = name },
        };
        var resolve = AccountResolver.FromEvents([Logon("LAB-WS01", "S-1-5-18", "LAB-WS01$"), Logon("LAB-WS01", "S-1-5-21-1-2-3-1001", "ion"),
            Logon("LAB-WS02", "S-1-5-21-1-2-3-1001", "maria"), Logon("LAB-WS02", "S-1-5-21-1-2-3-1002", "ana")]);
        Assert.Null(resolve("S-1-5-18"));
        Assert.Null(resolve("S-1-5-21-1-2-3-1001"));
        Assert.Equal("ana", resolve("S-1-5-21-1-2-3-1002"));
    }

    [Fact]
    public void A_task_definition_is_not_credited_to_Windows_and_its_name_loses_the_users_sid()
    {
        // lab WS02: „Windows are o activitate programată „OneDrive Standalone Update Task-S-1-5-21-…-1000”…”
        var task = new TimelineEvent
        {
            Time = new Timestamp(T0, "", "test"), Source = "ScheduledTask", EvidenceId = "EV", Summary = "x", User = "labadmin",
            Task = @"\OneDrive Standalone Update Task-S-1-5-21-2015769729-1107540261-47266587-1000", Path = @"C:\Users\labadmin\AppData\Local\Microsoft\OneDrive\OneDriveStandaloneUpdater.exe",
        };
        var f = Assert.Single(Correlation.Run([task]), x => x.RuleId == "PERSIST-TASK-CONFIG");
        Assert.StartsWith("Există pe calculator activitatea programată „OneDrive Standalone Update Task”, care rulează sub contul „labadmin”, care pornește singură programul „OneDriveStandaloneUpdater.exe”", f.PlainWhat);
        Assert.DoesNotContain("S-1-5", f.PlainWhat);
        Assert.Contains("există pe calculator", PlainStory.Tell(f, null, Bucharest));
    }

    private static TimelineEvent Ev(string source, string eventId = "", string path = "", Dictionary<string, string>? fields = null) => new()
    {
        Time = new Timestamp(T0, "", "test"), Source = source, EvidenceId = "EV", EventId = eventId, Path = path, Summary = "x",
        Fields = fields ?? [],
    };
}
