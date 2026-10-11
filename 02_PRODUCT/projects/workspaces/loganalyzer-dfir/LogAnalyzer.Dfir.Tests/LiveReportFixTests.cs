using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regressions from the first live investigation report: own-tool noise, protected Defender folders, argument paths, path prefixes.</summary>
public class LiveReportFixTests
{
    private const string DefenderService = @"""C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26080.3-0\MsMpEng.exe""";
    private const string NvidiaImage = @"""C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe"" -s NvContainerLocalSystem -f ""C:\ProgramData\NVIDIA\NVContainerLocalSystem.log"" -l 3 -d ""C:\Program Files\NVIDIA Corporation\NvContainer\plugins\LocalSystem""";

    // ---- 1. EvtExportLog flags ----

    [Fact]
    public void Export_flags_equal_the_documented_EVT_EXPORTLOG_FLAGS()
    {
        Assert.Equal(0x1, EventLogExport.EvtExportLogChannelPath);
        Assert.Equal(0x2, EventLogExport.EvtExportLogFilePath);
        Assert.Equal(0x1000, EventLogExport.EvtExportLogTolerateQueryErrors);
        Assert.Equal(0x2000, EventLogExport.EvtExportLogOverwrite);
    }

    [SkippableWindowsFact]
    public void A_readable_channel_exports_to_a_real_evtx_and_a_second_export_overwrites_it()
    {
        var path = Path.Combine(Path.GetTempPath(), "ladfir_evtx_" + Guid.NewGuid().ToString("N"), "x.evtx");
        try
        {
            string[] candidates = ["Microsoft-Windows-Kernel-PnP/Configuration", "Windows PowerShell", "Microsoft-Windows-Kernel-PnP/Device Management"];
            string? channel = null;
            foreach (var c in candidates)
            {
                var r = EventLogExport.Export(c, path);
                if (r.Status == EventLogExportStatus.Exported) { channel = c; break; }
            }
            Assert.True(channel is not null, "no readable candidate channel could be exported (error 87 means a wrong flag value)");
            Assert.Equal(EventLogExportStatus.Exported, EventLogExport.Export(channel!, path).Status);   // overwrite of the existing file
            var head = new byte[8];
            using (var fs = File.OpenRead(path)) Assert.Equal(8, fs.Read(head, 0, 8));
            Assert.Equal("ElfFile\0", System.Text.Encoding.ASCII.GetString(head));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch (IOException) { } }
    }

    // ---- 2. LogAnalyzer's own collection script ----

    private static TimelineEvent Prefetch(string process, string refs, double minute = 0) =>
        Row("Prefetch", minute, process, @"C:\Windows\System32\WindowsPowerShell\v1.0\" + process, f: [("ReferencedFiles", refs), ("PrefetchHash", "AAAA" + process)]);

    [Fact]
    public void The_own_remote_collection_script_is_Info_and_anchors_nothing()
    {
        var own = @"\USERS\IONUT\APPDATA\LOCAL\TEMP\LADFIR_REMOTE_0123456789ABCDEF0123456789ABCDEF\TARGET\COLLECT.PS1";
        var found = Correlation.Run([
            Prefetch("POWERSHELL.EXE", @"\WINDOWS\SYSTEM32\NTDLL.DLL|" + own),
            Row("Prefetch", 1, "SETUP.EXE", @"C:\Users\ionut\AppData\Local\Temp\setup.exe", f: [("ReferencedFiles", @"\USERS\IONUT\APPDATA\LOCAL\TEMP\SETUP.EXE"), ("PrefetchHash", "BBBB")]),
        ]);
        Assert.DoesNotContain(found, f => f.Severity >= Severity.Medium && f.RuleId is "EXEC-SCRIPT-VIA-LOLBIN" or "EXEC-USERPATH-CORRELATED");
        Assert.DoesNotContain(found, f => f.RuleId == "EXEC-USERPATH-CORRELATED");
        var info = Assert.Single(found, f => f.RuleId == "EXEC-SCRIPT-VIA-LOLBIN");
        Assert.Equal(Severity.Info, info.Severity);
        Assert.Contains("activitatea uneltei LogAnalyzer", info.Title);
    }

    [Fact]
    public void An_unrelated_temp_script_still_raises_the_High_finding_and_correlates()
    {
        var found = Correlation.Run([
            Prefetch("POWERSHELL.EXE", @"\USERS\IONUT\APPDATA\LOCAL\TEMP\LADFIR_REMOTE_NOTAGUID\COLLECT.PS1|\USERS\IONUT\APPDATA\LOCAL\TEMP\RUN.PS1"),
            Row("Prefetch", 1, "SETUP.EXE", @"C:\Users\ionut\AppData\Local\Temp\setup.exe", f: [("ReferencedFiles", @"\USERS\IONUT\APPDATA\LOCAL\TEMP\SETUP.EXE"), ("PrefetchHash", "BBBB")]),
        ]);
        Assert.Contains(found, f => f.RuleId == "EXEC-SCRIPT-VIA-LOLBIN" && f.Severity == Severity.High);
        Assert.Contains(found, f => f.RuleId == "EXEC-USERPATH-CORRELATED");
    }

    // ---- 3. protected Defender folders ----

    [Theory]
    [InlineData(@"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26080.3-0\MsMpEng.exe")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26080.3-0\MpCmdRun.exe")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows Defender Advanced Threat Protection\Platform\10.8\MsSense.exe")]
    public void Defender_platform_folders_are_not_user_writable_in_every_classifier(string exe)
    {
        Assert.False(Correlation.IsUserWritable(exe));
        Assert.False(SuspiciousProcessDetector.IsUserWritableLocation(exe));
        Assert.True(SuspiciousProcessDetector.IsUserWritableLocation(@"C:\ProgramData\Evil\x.exe"));
    }

    [Fact]
    public void A_defender_service_and_task_command_is_not_writable_but_a_user_task_is()
    {
        Assert.False(SuspiciousProcessDetector.IsUserWritableLocation(LiveStateCollector.ExePath(DefenderService + " -svc")));
        var task = @"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26080.3-0\MpCmdRun.exe -Scan -ScheduleJob";
        Assert.False(SuspiciousProcessDetector.IsUserWritableLocation(LiveStateCollector.ExePath(task)));
        var tasks = Correlation.Run([
            new TimelineEvent { Time = Timestamp.FromUtc(T0, "", "t"), Source = "ScheduledTask", EvidenceId = "EV", Summary = "t", Task = @"\Microsoft\Windows\Windows Defender\Scan", Path = @"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26080.3-0\MpCmdRun.exe" },
            new TimelineEvent { Time = Timestamp.FromUtc(T0, "", "t"), Source = "Service", EvidenceId = "EV", Summary = "t", Service = "WinDefend", Path = @"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.26080.3-0\MsMpEng.exe" },
        ]);
        Assert.DoesNotContain(tasks, f => f.RuleId is "PERSIST-TASK-CONFIG" or "PERSIST-SERVICE-CONFIG");
    }

    // ---- 4. only the executable is classified ----

    [Fact]
    public void Arguments_in_ProgramData_do_not_make_a_Program_Files_service_writable()
    {
        Assert.Equal(@"C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe", Correlation.CommandExecutable(NvidiaImage));
        Assert.Equal(@"C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe", LiveStateCollector.ExePath(NvidiaImage));
        Assert.Equal(@"C:\Program Files\A B\svc.exe", Correlation.CommandExecutable(@"C:\Program Files\A B\svc.exe -f C:\ProgramData\x.log"));
        Assert.Equal(@"C:\Windows\system32\drivers\x.sys", Correlation.CommandExecutable(@"C:\Windows\system32\drivers\x.sys"));
        Assert.False(Correlation.IsUserWritableCommand(NvidiaImage));
        Assert.False(Correlation.Run([Sys(7045, 0, "Service Control Manager", ("ServiceName", "NvContainerLocalSystem"), ("ImagePath", NvidiaImage))]).Any(f => f.RuleId == "PERSIST-SERVICE-USERPATH"));
    }

    [Fact]
    public void A_service_binary_actually_in_appdata_is_still_writable()
    {
        var image = @"""C:\Users\x\AppData\Local\Prog\svc.exe"" -f ""C:\Program Files\log.txt""";
        Assert.True(Correlation.IsUserWritableCommand(image));
        Assert.Contains(Correlation.Run([Sys(7045, 0, "Service Control Manager", ("ServiceName", "Evil"), ("ImagePath", image))]), f => f.RuleId == "PERSIST-SERVICE-USERPATH");
        Assert.True(SuspiciousProcessDetector.IsUserWritableLocation(LiveStateCollector.ExePath(@"C:\Users\x\AppData\Local\Prog\svc.exe -run")) ==
                    SuspiciousProcessDetector.IsUserWritableLocation(@"C:\Users\x\AppData\Local\Prog\svc.exe"));
    }

    // ---- 5. path prefixes ----

    [Theory]
    [InlineData(@"\\?\C:\WINDOWS\system32\conhost.exe", false)]
    [InlineData(@"\??\C:\WINDOWS\system32\conhost.exe", false)]
    [InlineData(@"\\?\C:\Users\x\AppData\Local\Temp\conhost.exe", true)]
    [InlineData(@"\??\C:\Users\x\AppData\Local\Temp\conhost.exe", true)]
    public void Extended_length_and_NT_prefixes_do_not_hide_or_fake_masquerading(string path, bool masquerading)
        => Assert.Equal(masquerading, AntiForensics.IsMasquerading(path));
}

/// <summary>Skips with a clear reason when the test host is not Windows.</summary>
public sealed class SkippableWindowsFactAttribute : FactAttribute
{
    public SkippableWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "EvtExportLog (wevtapi.dll) exists only on Windows.";
    }
}
