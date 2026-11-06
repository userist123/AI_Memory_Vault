using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review findings on the plain sentences (they go into official reports): a sentence never claims more than the evidence shows and never hides or
/// misattributes the account that acted. One regression test per finding.
/// </summary>
public sealed class PlainStoryReviewTests
{
    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 13, 15, 0, TimeSpan.Zero);

    private static Finding F(string plainWhat = "", string user = "", string host = "", string title = "t", string rule = "ANY-RULE") => new()
    {
        FindingId = "F-1", RuleId = rule, Title = title, Description = "x", Severity = Severity.Medium, User = user, Host = host, PlainWhat = plainWhat, FirstSeenUtc = T0, LastSeenUtc = T0,
    };

    private static TimelineEvent Trace(string source, double minute, string path = "", string eventId = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.UtcDateTime.AddMinutes(minute), "", "test"), Source = source, EvidenceId = "EV", EventId = eventId, Path = path, Summary = "x",
        Fields = f.ToDictionary(x => x.K, x => x.V),
    };

    // ---- 5: a matching name is not a copy ----

    [Fact]
    public void Finding5_a_same_name_is_not_a_copy_unless_both_are_writes_in_the_right_order()
    {
        // same name on the computer (a shortcut), no write there: only „same name”, never „copiat”
        var events = new List<TimelineEvent>
        {
            Trace("LNK", 0, @"C:\Users\ion\Documents\Plan.docx"),
            Trace("EventLog:Security", 5, "", "4663", ("ObjectName", @"E:\Plan.docx"), ("AccessMask", "0x2"), ("SubjectUserName", "ion.popescu")),
        };
        var s = Wp14Rules.FilesStory("SanDisk", "E", Wp14Rules.ActivityOn(events, "E"), events);
        Assert.Equal(@"a scris pe stick fișierul „Plan.docx” (un fișier cu același nume se află și în C:\Users\ion\Documents)", s);
        Assert.DoesNotContain("copiat", s);

        // copy onto the stick: a write on the computer, then the write on the stick
        events =
        [
            Trace("USN", 0, @"C:\Users\ion\Desktop\Plan.docx", "", ("Reason", "FILE_CREATE")),
            Trace("EventLog:Security", 5, "", "4663", ("ObjectName", @"E:\Plan.docx"), ("AccessMask", "0x2"), ("SubjectUserName", "ion.popescu")),
        ];
        Assert.Equal(@"a scris pe stick fișierul „Plan.docx”, probabil copiat din C:\Users\ion\Desktop (același nume, scris după)",
            Wp14Rules.FilesStory("SanDisk", "E", Wp14Rules.ActivityOn(events, "E"), events));

        // the same two writes two days apart are not a copy (round-2 review: no time limit)
        events =
        [
            Trace("USN", 0, @"C:\Users\ion\Desktop\Plan.docx", "", ("Reason", "FILE_CREATE")),
            Trace("EventLog:Security", 2 * 24 * 60, "", "4663", ("ObjectName", @"E:\Plan.docx"), ("AccessMask", "0x2"), ("SubjectUserName", "ion.popescu")),
        ];
        Assert.DoesNotContain("copiat", Wp14Rules.FilesStory("SanDisk", "E", Wp14Rules.ActivityOn(events, "E"), events));

        // copy from the stick: opened on the stick, then written on the computer
        events =
        [
            Trace("LNK", 0, @"E:\Oferta.pdf", "", ("DriveType", "REMOVABLE")),
            Trace("USN", 5, @"C:\Users\ion\Desktop\Oferta.pdf", "", ("Reason", "FILE_CREATE")),
        ];
        Assert.Equal(@"a deschis de pe stick fișierul „Oferta.pdf”, probabil copiat în C:\Users\ion\Desktop (același nume, scris după)",
            Wp14Rules.FilesStory("SanDisk", "E", Wp14Rules.ActivityOn(events, "E"), events));

        // the computer's write came BEFORE the open on the stick: not a copy from the stick
        events =
        [
            Trace("USN", 0, @"C:\Users\ion\Desktop\Oferta.pdf", "", ("Reason", "FILE_CREATE")),
            Trace("LNK", 5, @"E:\Oferta.pdf", "", ("DriveType", "REMOVABLE")),
        ];
        var before = Wp14Rules.FilesStory("SanDisk", "E", Wp14Rules.ActivityOn(events, "E"), events);
        Assert.DoesNotContain("copiat", before);
        Assert.Contains(@"un fișier cu același nume se află și în C:\Users\ion\Desktop", before);
    }

    [Fact]
    public void Finding5_folder_shell_files_are_not_content()
    {
        var events = new List<TimelineEvent>
        {
            Trace("EventLog:Security", 0, "", "4663", ("ObjectName", @"E:\desktop.ini"), ("AccessMask", "0x2"), ("SubjectUserName", "ion.popescu")),
            Trace("LNK", 1, @"E:\Thumbs.db", "", ("DriveType", "REMOVABLE")),
        };
        Assert.Equal("", Wp14Rules.FilesStory("SanDisk", "E", Wp14Rules.ActivityOn(events, "E"), events));
    }

    // ---- 6: accounts ending in $ ----

    [Fact]
    public void Finding6_a_dollar_account_is_the_station_only_when_it_is_this_computers_own()
    {
        Assert.False(PlainPhrases.IsStationAccount("labbackup$"));
        Assert.False(PlainPhrases.IsStationAccount("LAB\\labbackup$", "LAB-WS01"));
        Assert.True(PlainPhrases.IsStationAccount("LAB-WS01$", "LAB-WS01"));
        Assert.True(PlainPhrases.IsStationAccount("LAB\\lab-ws01$", "LAB-WS01.lab.local"));
        Assert.False(PlainPhrases.IsStationAccount("LAB-WS01$", "LAB-WS02"));
        Assert.True(PlainPhrases.IsStationAccount("anything$", "LAB-WS01", "S-1-5-18"));
        Assert.Contains("labbackup", PlainStory.Who("labbackup$", null, "LAB-WS01"));
        Assert.StartsWith("Windows", PlainStory.Who("LAB-WS01$", null, "LAB-WS01"));
    }

    [Fact]
    public void Finding6_two_real_accounts_that_cleared_logs_are_named_not_somebody()
    {
        var f = One(Correlation.Run([Sec(1102, 0, ("SubjectUserName", "ana"), ("SubjectDomainName", "LAB")), Sec(1102, 5, ("SubjectUserName", "labbackup$"), ("SubjectDomainName", "LAB"))]), "LOG-TAMPER");
        Assert.StartsWith("Conturile „ana” și „labbackup$” au șters jurnalul „Security”", f.PlainWhat);
        Assert.DoesNotContain("cineva", PlainStory.Tell(f, null, Bucharest), StringComparison.OrdinalIgnoreCase);
    }

    // ---- 7: each part of the log-tamper story to its own account ----

    [Fact]
    public void Finding7_the_clearer_is_not_credited_with_other_accounts_parts()
    {
        var f = One(Correlation.Run([Sec(1102, 0, ("SubjectUserName", "admin1"), ("SubjectDomainName", "LAB")),
            Sec(4719, 1, ("SubjectUserName", "other.admin"), ("SubjectDomainName", "LAB"), ("AuditPolicyChanges", "%%8448"))]), "LOG-TAMPER");
        Assert.StartsWith("Contul „admin1” a șters jurnalul „Security”", f.PlainWhat);
        Assert.EndsWith("contul „other.admin” a schimbat regulile după care Windows notează ce se întâmplă pe calculator", f.PlainWhat);
        // a part with no account of its own is a subject-less clause
        var g = One(Correlation.Run([Sec(1102, 0, ("SubjectUserName", "admin1"), ("SubjectDomainName", "LAB")), Sec(4719, 1, ("SubjectUserName", "-"), ("AuditPolicyChanges", "%%8448"))]), "LOG-TAMPER");
        Assert.Contains("; a fost schimbat", g.PlainWhat.Replace("au fost schimbate", "a fost schimbat"));
        Assert.DoesNotContain("admin1” a schimbat", g.PlainWhat);
    }

    // ---- 8: station accounts are matched after the domain is stripped ----

    [Theory]
    [InlineData(@"NT AUTHORITY\SYSTEM")]
    [InlineData(@"Window Manager\DWM-1")]
    [InlineData(@"Font Driver Host\UMFD-0")]
    [InlineData("LocalSystem")]
    [InlineData("LocalService")]
    [InlineData("NetworkService")]
    [InlineData(@"NT AUTHORITY\IUSR")]
    [InlineData(@"NT AUTHORITY\LOCAL SERVICE")]
    [InlineData("defaultuser0")]
    public void Finding8_windows_own_accounts_are_never_a_person(string account)
    {
        Assert.StartsWith("Windows (un serviciu al sistemului", PlainStory.Who(account, null));
        Assert.True(PlainPhrases.IsStationAccount(account));
    }

    // ---- 10: capital-first vs subject-needing ----

    [Fact]
    public void Finding10_the_nothing_but_windows_clause_has_its_own_capital_and_no_person_in_front()
    {
        var what = Wp14Rules.AfterConnectStory("SanDisk", []);
        Assert.StartsWith("După ce a fost conectat stick-ul „SanDisk”, pe calculator au mai fost doar activități ale Windows", what);
        var register = new UsersRegister { Rows = [new UserRow { Person = "Popescu Ion", Accounts = "ion.popescu" }] };
        var told = PlainStory.Tell(F(what, "ion.popescu"), register.PersonOf, Bucharest);
        Assert.Equal("Pe 6 noiembrie 2026, la ora 15:15, după ce a fost conectat stick-ul „SanDisk”, pe calculator au mai fost doar activități ale Windows; nimic al utilizatorului nu a putut fi legat de stick.", told);
        Assert.DoesNotContain("Popescu", told);
        // a clause that needs the subject keeps it in front
        Assert.StartsWith("Pe 6 noiembrie 2026, la ora 15:15, Popescu Ion (contul ion.popescu) a pornit", PlainStory.Tell(F("a pornit programul „x.exe” după ce a fost conectat stick-ul „S”", "ion.popescu"), register.PersonOf, Bucharest));
    }

    // ---- 11: the fallback scrubber keeps real content ----

    [Theory]
    [InlineData("Acces de pe LAB-WS05 la RAPORT-2026 și NATO-SECRET", "Acces de pe LAB-WS05 la RAPORT-2026 și NATO-SECRET")]
    [InlineData("Fișier deschis (fișier Application.docx)", "Fișier deschis (fișier Application.docx)")]
    [InlineData("Document copiat (copie eveniment 2026)", "Document copiat (copie eveniment 2026)")]
    [InlineData("Jurnal șters (Security 1100)", "Jurnal șters")]
    [InlineData("Proces pornit (eveniment 4688)", "Proces pornit")]
    [InlineData("Proces pornit, eveniment 4688, de admin", "Proces pornit, de admin")]
    [InlineData("Conferința din eveniment 2026 a avut loc", "Conferința din eveniment 2026 a avut loc")]
    [InlineData("Cont adăugat (vezi MEDIA-FILE-ACTIVITY)", "Cont adăugat")]
    [InlineData("Activitate MEDIA-FILE-ACTIVITY pe unitatea E", "Activitate pe unitatea E")]
    public void Finding11_only_technical_things_are_removed(string title, string expected) => Assert.Equal(expected, PlainStory.WithoutTechnicalTerms(title));

    [Theory]
    [InlineData("Microsoft Defender a oprit protecția", "Microsoft Defender a oprit protecția")]
    [InlineData("PowerShell a rulat un script", "PowerShell a rulat un script")]
    [InlineData("Windows a pornit un serviciu", "Windows a pornit un serviciu")]
    [InlineData("LAB-WS05 a răspuns", "LAB-WS05 a răspuns")]
    [InlineData("Cineva a încercat o parolă", "cineva a încercat o parolă")]
    [InlineData("Contul „x” a fost creat", "contul „x” a fost creat")]
    [InlineData("A fost instalat un serviciu", "a fost instalat un serviciu")]
    [InlineData("După ce a fost conectat stick-ul „X”, ceva", "după ce a fost conectat stick-ul „X”, ceva")]
    [InlineData("Mai multe lucruri grave", "mai multe lucruri grave")]
    public void Finding11_only_the_fixed_openings_lose_their_capital(string clause, string expected) => Assert.Equal(expected, PlainStory.LowerFirst(clause));

    [Fact]
    public void Finding11_the_fallback_does_not_lowercase_a_proper_noun()
    {
        var s = PlainStory.Tell(F("", title: "Microsoft Defender: protecția a fost oprită"), null, Bucharest);
        Assert.StartsWith("Pe 6 noiembrie 2026, la ora 15:15, s-a observat: Microsoft Defender", s);
    }

    // ---- 13: grammar ----

    [Fact]
    public void Finding13_counts_agree_with_their_nouns()
    {
        Assert.Contains("s-a deschis 1 fișier", Wp14Rules.UsageStory("X", ["ion"], false, true, 1));
        Assert.Contains("s-au deschis 3 fișiere", Wp14Rules.UsageStory("X", ["ion"], false, true, 3));
        Assert.Contains("s-au deschis 25 de fișiere", Wp14Rules.UsageStory("X", ["ion"], false, true, 25));
        var items = Enumerable.Range(1, 8).Select(i => "a" + i).ToList();
        Assert.Equal("a1, a2, a3 și a4 și încă 4 elemente", PlainPhrases.List(items, 4));
        Assert.Equal("a1, a2, a3 și a4 și încă 1 element", PlainPhrases.List(items.Take(5).ToList(), 4));
        Assert.EndsWith("și încă 21 de elemente", PlainPhrases.List(Enumerable.Range(1, 25).Select(i => "a" + i).ToList(), 4));
        Assert.Equal("a1 și încă 3 fișiere", PlainPhrases.List(items.Take(4).ToList(), 1, ("fișier", "fișiere")));
    }

    [Fact]
    public void Finding13_discovery_commands_use_the_right_plural()
    {
        var found = Wp11Rules.Run([
            Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\whoami.exe"), ("CommandLine", "whoami /all"), ("SubjectUserName", "it.admin"), ("SubjectLogonId", "0x1")),
            Sec(4688, 0.1, ("NewProcessName", @"C:\Windows\System32\net.exe"), ("CommandLine", "net group \"Domain Admins\" /domain"), ("SubjectUserName", "it.admin"), ("SubjectLogonId", "0x1")),
            Sec(4688, 0.2, ("NewProcessName", @"C:\Windows\System32\nltest.exe"), ("CommandLine", "nltest /dclist:lab.local"), ("SubjectUserName", "it.admin"), ("SubjectLogonId", "0x1"))], [], () => "F-1");
        var f = Assert.Single(found, x => x.RuleId == "DISCOVERY-BURST");
        Assert.StartsWith("a rulat rapid 3 comenzi prin care", f.PlainWhat);
    }

    // ---- A: a SID is never printed ----

    [Fact]
    public void FindingA_a_sid_in_User_is_resolved_from_the_case_events_else_never_printed()
    {
        const string sid = "S-1-5-21-2015769729-1107540261-47266587-1000";
        var who = PlainStory.Who(sid, null);
        Assert.Equal("un cont pe care probele îl arată doar prin identificatorul lui", who);
        Assert.DoesNotContain("S-1-5", who);

        var events = new List<TimelineEvent> { Sec(4688, 0, ("SubjectUserSid", sid), ("SubjectUserName", "labadmin"), ("NewProcessName", "x.exe")) };
        var resolver = AccountResolver.FromEvents(events);
        Assert.Equal("labadmin", resolver(sid));
        Assert.Null(resolver("S-1-5-21-9-9-9-9"));
        var told = PlainStory.Tell(F("a rulat un script", sid), null, Bucharest, resolver);
        Assert.Equal("Pe 6 noiembrie 2026, la ora 15:15, utilizatorul cu contul „labadmin” a rulat un script.", told);
        Assert.DoesNotContain("S-1-5", PlainStory.Tell(F("a rulat un script", sid), null, Bucharest));

        // the register knows the SID (SidList): the person is named
        var register = new UsersRegister { Rows = [new UserRow { Person = "Popescu Ion", Accounts = "labadmin", Sids = sid }] };
        Assert.Contains("Popescu Ion (contul labadmin)", PlainStory.Tell(F("a rulat un script", sid), register.PersonOf, Bucharest, resolver));
        Assert.Contains("Popescu Ion", PlainStory.Tell(F("a rulat un script", sid), register.PersonOf, Bucharest));

        // the contract wires the resolver in
        var done = Assert.Single(FindingContract.Enrich([F("a rulat un script", sid)], new FindingContractContext { NowUtc = T0, Zone = Bucharest, AccountOfSid = resolver }));
        Assert.Contains("„labadmin”", done.Plain);
    }

    // ---- B: the presence sentence of a stick names the accounts MEDIA-USAGE lists ----

    private static List<TimelineEvent> StickWithSessions(params string[] accounts)
    {
        var l = new List<TimelineEvent> { Usb("STICK0001", 10, label: "SanDisk Cruzer Blade USB Device", letter: "E") };
        int i = 0;
        foreach (var a in accounts) l.Add(Sec(4624, 1 + i, ("TargetUserName", a), ("TargetLogonId", "0x" + (100 + i++)), ("LogonType", "2"), ("TargetUserSid", "S-1-5-21-1-2-3-" + (1000 + i))));
        return l;
    }

    [Fact]
    public void FindingB_windows_setup_account_plus_one_real_account_is_named_not_somebody()
    {
        var found = Go(StickWithSessions("defaultuser0", "labadmin"), Unclassified());
        var usage = One(found, "MEDIA-USAGE");
        var presence = Assert.Single(found, f => f.RuleId == "MEDIA-UNKNOWN");
        Assert.DoesNotContain("cineva", PlainStory.Tell(presence, null, Bucharest), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("labadmin", PlainStory.Tell(presence, null, Bucharest));
        Assert.DoesNotContain("defaultuser0", PlainStory.Tell(presence, null, Bucharest));
        Assert.Contains("labadmin", usage.PlainWhat + usage.User);
    }

    [Fact]
    public void FindingB_several_real_accounts_are_listed_as_open_when_the_stick_was_connected()
    {
        var found = Go(StickWithSessions("defaultuser0", "labadmin", "ana.pop"), Unclassified());
        var presence = Assert.Single(found, f => f.RuleId == "MEDIA-UNKNOWN");
        var told = PlainStory.Tell(presence, null, Bucharest);
        // round-2 review: the accounts link the stick to people, they do not prove who connected it
        Assert.Contains("stick-ul „SanDisk Cruzer Blade” a fost conectat; probele îl leagă de conturile „ana.pop” și „labadmin”, fără să arate sigur cine l-a conectat", told);
        Assert.DoesNotContain("cineva", told, StringComparison.OrdinalIgnoreCase);
    }

    // ---- 9: MEDIA-FILE-ACTIVITY names the account of the file traces ----

    [Fact]
    public void Finding9_file_activity_prefers_the_accounts_of_the_4663_traces()
    {
        var events = StickWithSessions("labadmin", "ana.pop");
        events.Add(Sec(4663, 11, ("ObjectName", @"E:\Plan.docx"), ("AccessMask", "0x2"), ("SubjectUserName", "ana.pop")));
        var act = One(Go(events, Unclassified()), "MEDIA-FILE-ACTIVITY");
        Assert.Equal("ana.pop", act.User);
    }
}
