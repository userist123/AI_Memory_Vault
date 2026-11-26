using System.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>„Cum verifici tu”: manual steps per evidence locator, built from the locator formats the parsers write. Synthetic data only.</summary>
public sealed class ManualVerificationTests
{
    private const string Sha = "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12";

    private static EvidenceItem Item(string source, string sourceType, string stored, long size = 2048) => new()
    {
        EvidenceId = "E-0001", CaseId = "C", Source = source, SourceType = sourceType, StoredPath = stored, Size = size, Sha256 = Sha,
    };

    private static string All(IReadOnlyList<VerificationStep> steps) => string.Join("\n", steps.Select(s => s.ToLine()));

    [Fact]
    public void An_evtx_record_gets_the_Get_WinEvent_command_on_the_case_copy_and_the_Event_Viewer_path_on_the_live_log()
    {
        var item = Item("EventLog:Security", "EventLog:Security", @"Raw\Security.evtx");
        var steps = ManualVerification.For(new EvidenceRef("E-0001", "EventRecordID=4711", "x"), item, @"C:\Cazuri\C1");
        Assert.Contains(steps, s => s.IsCommand && s.Action == @"Get-WinEvent -Path 'C:\Cazuri\C1\Raw\Security.evtx' -FilterXPath ""*[System[EventRecordID=4711]]"" | Format-List *");
        Assert.Contains(steps, s => s.Tool == "Event Viewer" && s.Action.Contains("Windows Logs → Security") && s.Action.Contains("4711") && s.Compare.Contains("ciclat"));
        Assert.Contains(steps, s => s.Action.StartsWith("Get-FileHash -Algorithm SHA256", StringComparison.Ordinal) && s.Compare == $"amprenta SHA-256 trebuie să fie {Sha}");
    }

    [Fact]
    public void An_evtx_record_of_an_application_channel_names_that_channel_and_the_event_id_from_the_row()
    {
        var item = Item("EventLog:Microsoft-Windows-PowerShell/Operational", "EventLog:Microsoft-Windows-PowerShell/Operational", @"Raw\ps.evtx");
        var row = new TimelineEvent { Source = "EventLog:x", Summary = "s", EvidenceId = "E-0001", Locator = "EventRecordID=9", EventId = "4104", Time = Timestamp.FromUtc(new DateTime(2026, 11, 6, 13, 0, 0, DateTimeKind.Utc), "x", "y") };
        var steps = ManualVerification.For(new EvidenceRef("E-0001", "EventRecordID=9", "x"), item, null, row);
        var viewer = Assert.Single(steps, s => s.Tool == "Event Viewer");
        Assert.Contains("Applications and Services Logs", viewer.Action);
        Assert.Contains("Microsoft-Windows-PowerShell/Operational", viewer.Action);
        Assert.Contains("Event ID 4104", viewer.Action);
    }

    [Fact]
    public void A_registry_value_gets_reg_query_on_the_live_hive_and_load_query_unload_on_a_working_copy_of_the_saved_hive()
    {
        var item = Item("Registry", "registry", @"Raw\SOFTWARE");
        var steps = ManualVerification.For(new EvidenceRef("E-0001", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit", "x"), item, @"C:\Cazuri\C1");
        var text = All(steps);
        Assert.Contains(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v Userinit", text);
        Assert.Contains(@"Copy-Item -LiteralPath 'C:\Cazuri\C1\Raw\SOFTWARE' -Destination ""$env:TEMP\LA_VERIFY.hive""; reg load HKLM\LA_VERIFY ""$env:TEMP\LA_VERIFY.hive""", text);
        Assert.Contains(@"reg query ""HKLM\LA_VERIFY\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v Userinit", text);
        Assert.Contains(@"reg unload HKLM\LA_VERIFY", text);
        Assert.Contains("regedit", text);
        Assert.Contains("Load Hive", text);
        Assert.Contains("Unload Hive", text);
    }

    [Fact]
    public void A_service_key_in_a_saved_system_hive_is_queried_as_a_key()
    {
        var steps = ManualVerification.For(new EvidenceRef("E-0001", @"SYSTEM\ControlSet001\Services\Spooler", "x"), Item("Registry", "registry", @"Raw\SYSTEM"), @"C:\C");
        Assert.Contains(steps, s => s.Action == @"reg query ""HKLM\SYSTEM\ControlSet001\Services\Spooler""");
        Assert.Contains(steps, s => s.Action == @"reg query ""HKLM\LA_VERIFY\ControlSet001\Services\Spooler""");
    }

    [Fact]
    public void A_text_file_line_locator_gets_a_Get_Content_command_with_the_zero_based_index_on_the_case_copy_and_the_Notepad_path()
    {
        // planner #11 A6: PSReadLine history and hosts file (locator line=N)
        var item = Item("PSReadLine:S-1-5-21-1", "ps_readline_history", @"Raw\UserHistory\PSReadLine\S-1\ConsoleHost_history.txt");
        var reference = new EvidenceRef("E-0001", "line=7", "Istoric PowerShell PSReadLine, rândul 7") { Quote = "whoami /all" };
        var steps = ManualVerification.For(reference, item, @"C:\Cazuri\C1");
        var cmd = Assert.Single(steps, s => s.Action.StartsWith("Get-Content", StringComparison.Ordinal));
        Assert.True(cmd.IsCommand);
        Assert.Equal(@"Get-Content -LiteralPath 'C:\Cazuri\C1\Raw\UserHistory\PSReadLine\S-1\ConsoleHost_history.txt' | Select-Object -Index 6", cmd.Action);
        Assert.Contains("rândul 7", cmd.Compare);
        Assert.Contains("Citat:", cmd.Compare);
        Assert.Contains(steps, s => s.Tool == "Notepad" && s.Action.Contains("linia 7"));
        Assert.Contains(steps, s => s.Action.StartsWith("Get-FileHash -Algorithm SHA256", StringComparison.Ordinal));
        // an apostrophe in the case path is doubled, never interpreted
        var odd = ManualVerification.For(reference, Item("Hosts", "hosts_file", @"Raw\O'Neil\hosts"), @"C:\C");
        Assert.Contains(odd, s => s.Action == @"Get-Content -LiteralPath 'C:\C\Raw\O''Neil\hosts' | Select-Object -Index 6");
        // without a stored copy there is no command, only the specialist step
        Assert.DoesNotContain(ManualVerification.For(reference, null, @"C:\C"), s => s.Action.StartsWith("Get-Content", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stored_artefact_copy_gets_the_hash_command_and_the_size_to_compare()
    {
        var item = Item("Prefetch", "prefetch", @"Raw\CMD.EXE-0BD30981.pf", 3072);
        var steps = ManualVerification.For(new EvidenceRef("E-0001", "CMD.EXE-0BD30981.pf#run2", "x"), item, @"C:\C");
        Assert.Contains(steps, s => s.Action == @"Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\C\Raw\CMD.EXE-0BD30981.pf'" && s.Compare.Contains(Sha));
        Assert.Contains(steps, s => s.Tool == "Proprietăți fișier" && s.Compare.Contains("3072 octeți"));
        Assert.Contains(steps, s => s.Tool == ManualVerification.SpecialistTool);
    }

    [Fact]
    public void An_amcache_entry_is_read_only_from_the_copy_under_Root()
    {
        var steps = ManualVerification.For(new EvidenceRef("E-0001", @"Amcache.hve\Root\InventoryApplicationFile\evil.exe|1234", "x"), Item("Amcache", "amcache", @"Raw\Amcache.hve"), @"C:\C");
        Assert.DoesNotContain(steps, s => s.Action.Contains("HKLM\\SYSTEM") || s.Action.Contains("HKCU"));
        Assert.Contains(steps, s => s.Action.StartsWith(@"reg query ""HKLM\LA_VERIFY\Root\InventoryApplicationFile\evil.exe|1234""", StringComparison.Ordinal));
    }

    [Fact]
    public void Live_snapshot_entries_get_the_service_and_task_commands_and_the_snapshot_note_for_processes()
    {
        var item = Item("Live", "live-snapshot", @"Raw\live.json");
        var svc = All(ManualVerification.For(new EvidenceRef("E-0001", "Services[Spooler]", "x"), item, @"C:\C"));
        Assert.Contains(@"Get-CimInstance Win32_Service -Filter ""Name='Spooler'"" | Format-List *", svc);
        var task = All(ManualVerification.For(new EvidenceRef("E-0001", @"Tasks[\Microsoft\Windows\Defrag\ScheduledDefrag]", "x"), item, @"C:\C"));
        Assert.Contains(@"schtasks /query /tn ""\Microsoft\Windows\Defrag\ScheduledDefrag"" /v /fo LIST", task);
        var proc = ManualVerification.For(new EvidenceRef("E-0001", "Processes[pid=4242]", "x"), item, @"C:\C");
        Assert.DoesNotContain(proc, s => s.Action.StartsWith("Get-Process", StringComparison.Ordinal));
        Assert.Contains(proc, s => s.Action.Contains("instantaneu luat la colectare"));
        Assert.Contains(proc, s => s.Action.StartsWith("Get-FileHash", StringComparison.Ordinal));
    }

    [Fact]
    public void Quotes_and_spaces_are_escaped_for_PowerShell()
    {
        var item = Item("EventLog:Security", "EventLog:Security", @"Raw\O'Brien case\Sec.evtx");
        var evtx = All(ManualVerification.For(new EvidenceRef("E-0001", "EventRecordID=1", "x"), item, @"C:\Cazuri 1"));
        Assert.Contains(@"-Path 'C:\Cazuri 1\Raw\O''Brien case\Sec.evtx'", evtx);
        var svc = All(ManualVerification.For(new EvidenceRef("E-0001", "Services[Bob's $x]", "x"), item, @"C:\C"));
        Assert.Contains(@"-Filter ""Name='Bob\'s `$x'""", svc);
        var reg = All(ManualVerification.For(new EvidenceRef("E-0001", @"NTUSER\Software\My App\Run\Some Value", "x"), Item("Registry", "registry", @"Raw\NTUSER.DAT"), @"C:\C"));
        Assert.Contains(@"reg query ""HKCU\Software\My App\Run"" /v ""Some Value""", reg);
        Assert.DoesNotContain("\n", ManualVerification.Sq("a\nb"));
    }

    [Theory]
    [InlineData("zzz-necunoscut")]
    [InlineData("")]
    public void An_unknown_locator_gets_one_specialist_step_and_no_command(string locator)
    {
        var steps = ManualVerification.For(new EvidenceRef("E-0001", locator, "x"), Item("X", "x", @"Raw\x.bin"), @"C:\C");
        var only = Assert.Single(steps);
        Assert.False(only.IsCommand);
        Assert.Equal($"Verificare de specialist: localizatorul {locator} nu are o procedură automată", only.Action);
    }

    [Fact]
    public void A_missing_evidence_item_never_yields_a_made_up_path()
    {
        var steps = ManualVerification.For(new EvidenceRef("E-0009", "EventRecordID=5", "x"), null, @"C:\C");
        Assert.DoesNotContain(steps, s => s.Action.Contains("Get-WinEvent"));
        Assert.Contains(steps, s => s.Tool == ManualVerification.SpecialistTool);
    }

    [Theory]
    [InlineData("EventRecordID=7")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell")]
    [InlineData("X.pf#run1")]
    [InlineData("Services[a]")]
    [InlineData("Tasks[\\a]")]
    [InlineData(@"Amcache.hve\Root\InventoryApplicationFile\a")]
    [InlineData(@"SYSTEM\ControlSet001\Services\bam\...\S-1\a.exe")]
    [InlineData("line=5")]
    [InlineData("MFT#12")]
    [InlineData("nu-stiu")]
    public void No_step_ever_modifies_the_system(string locator)
    {
        var item = Item("EventLog:Security", "EventLog:Security", @"Raw\a.bin");
        foreach (var s in ManualVerification.For(new EvidenceRef("E-0001", locator, "x"), item, @"C:\C"))
            foreach (var banned in new[] { "reg add", "reg delete", "wevtutil cl", "Remove-", "Clear-", "Set-", "reg import", "del " })
                Assert.DoesNotContain(banned, s.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_investigation_pdf_prints_Cum_verifici_tu_with_the_evtx_command_under_the_evidence_line()
    {
        using var k = new GapsTestKit();
        try
        {
            var ws = k.Case();
            var src = k.File("Security.evtx", "evtx-bytes");
            var item = ws.ImportFile(src, "Import:Security", "EventLog:Security", TemporalType.Historical, "Import", "t");
            var r = new InvestigationResult { Case = ws };
            r.Findings.Add(new Finding
            {
                FindingId = "F-0001", RuleId = "TEST", Title = "t", Description = "x", Severity = Severity.High, Classification = Classification.Candidate,
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, "EventRecordID=4711", "Security 4624")],
            });
            string text = "";
            InvestigationReportPdf.TextObserver.Value = t => text = t;
            InvestigationReportPdf.Write(r, Path.Combine(k.Dir, "verifica.pdf"), "investigator");
            Assert.Contains("Cum verificați", text);
            Assert.Contains("-FilterXPath \"*[System[EventRecordID=4711]]\"", text);
            Assert.Contains("Get-WinEvent -Path '", text);
        }
        finally { InvestigationReportPdf.TextObserver.Value = null; }
    }

    [Fact]
    public void The_finding_card_carries_the_steps_under_the_same_heading()
    {
        var item = Item("EventLog:Security", "EventLog:Security", @"Raw\Security.evtx");
        var f = new Finding { FindingId = "F-1", RuleId = "T", Title = "t", Description = "d", SupportingEvidence = [new EvidenceRef("E-0001", "EventRecordID=3", "x")] };
        var card = FindingCardModel.Build(f, new EvidenceContext { Items = [item], CaseRoot = @"C:\C" });
        Assert.True(card.HasManualSteps);
        Assert.Contains("Get-WinEvent -Path 'C:\\C\\Raw\\Security.evtx'", card.ManualStepsText);
    }
}
