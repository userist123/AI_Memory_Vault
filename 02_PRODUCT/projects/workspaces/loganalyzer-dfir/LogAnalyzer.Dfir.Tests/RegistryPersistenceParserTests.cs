using System.Buffers.Binary;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>NTUSER (UserAssist, Run/RunOnce) and SOFTWARE (Run/RunOnce, Winlogon, IFEO) hives: synthetic exact values and real hives vs reg export.</summary>
public sealed class RegistryPersistenceParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_regp_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Last = new(2026, 9, 19, 14, 55, 1, DateTimeKind.Utc);

    public RegistryPersistenceParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path, string type) => new() { EvidenceId = "EV-R", CaseId = "C", Source = type, SourceType = type, StoredPath = path };

    public static string Rot13(string s) => new(s.Select(c => c switch
    {
        >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
        >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
        _ => c,
    }).ToArray());

    private static byte[] UserAssistData(int runs, int focus, int focusMs, DateTime last)
    {
        var d = new byte[72];
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), runs);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), focus);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(12), focusMs);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(60), last.ToFileTimeUtc());
        return d;
    }

    private string NtUser()
    {
        var path = Path.Combine(_dir, "NTUSER.DAT");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        var ua = hive.Root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count");
        ua.SetValue(Rot13(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE"), UserAssistData(4, 3, 61000, Last), RegistryValueType.Binary);
        ua.SetValue(Rot13(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe"), UserAssistData(2, 0, 0, Last.AddDays(-1)), RegistryValueType.Binary);
        ua.SetValue(Rot13("UEME_CTLSESSION"), new byte[1612], RegistryValueType.Binary);
        var run = hive.Root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        run.SetValue("Updater", "\"C:\\Users\\u\\AppData\\Roaming\\upd\\upd.exe\" /quiet", RegistryValueType.String);
        hive.Root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce").SetValue("Once", @"C:\Windows\system32\cmd.exe /c echo", RegistryValueType.String);
        return path;
    }

    private string Software()
    {
        var path = Path.Combine(_dir, "SOFTWARE");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        hive.Root.CreateSubKey(@"Microsoft\Windows\CurrentVersion\Run").SetValue("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", RegistryValueType.ExpandString);
        hive.Root.CreateSubKey(@"WOW6432Node\Microsoft\Windows\CurrentVersion\Run").SetValue("Agent", @"C:\ProgramData\agent\a.exe", RegistryValueType.String);
        var wl = hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\Winlogon");
        wl.SetValue("Shell", "explorer.exe", RegistryValueType.String);
        wl.SetValue("Userinit", @"C:\Windows\system32\userinit.exe,C:\Users\Public\x.exe", RegistryValueType.String);
        hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sethc.exe").SetValue("Debugger", @"C:\Windows\System32\cmd.exe", RegistryValueType.String);
        hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\Image File Execution Options\notepad.exe").SetValue("UseFilter", 1, RegistryValueType.Dword);
        return path;
    }

    [Fact]
    public void UserAssist_entries_are_decoded_with_count_focus_and_last_run()
    {
        var p = NtUser();
        var sink = new ListSink();
        var r = new UserHiveParser().Parse(Item(p, "ntuser_hive"), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        var ua = sink.Events.Where(e => e.Source == "UserAssist").ToList();
        Assert.Equal(2, ua.Count);                                     // the UEME_CTL control value is not a program
        var setup = Assert.Single(ua, e => e.Process == "SETUP.EXE");
        Assert.Equal(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", setup.Path);
        Assert.Equal(new DateTimeOffset(Last), setup.Time.Utc);
        Assert.Equal("4", setup.Fields["RunCount"]);
        Assert.Equal("3", setup.Fields["FocusCount"]);
        Assert.Equal("61000", setup.Fields["FocusTimeMs"]);
        Assert.Contains("GUI", setup.TimeSemantics);
        var cmd = Assert.Single(ua, e => e.Process == "cmd.exe");
        Assert.Equal(@"%SystemRoot%\System32\cmd.exe", cmd.Path);       // known-folder GUID resolved
        Assert.Equal(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe", cmd.Fields["DecodedName"]);
    }

    [Fact]
    public void User_run_keys_are_persistence_events_and_user_writable_ones_are_findings()
    {
        var p = NtUser();
        var sink = new ListSink();
        new UserHiveParser().Parse(Item(p, "ntuser_hive"), p, sink, default);

        var run = Assert.Single(sink.Events, e => e.Source == "RunKey" && e.Fields["ValueName"] == "Updater");
        Assert.Equal(@"C:\Users\u\AppData\Roaming\upd\upd.exe", run.Path);
        Assert.Equal("upd.exe", run.Process);
        Assert.Equal("\"C:\\Users\\u\\AppData\\Roaming\\upd\\upd.exe\" /quiet", run.Fields["Command"]);
        Assert.Equal(@"NTUSER\Software\Microsoft\Windows\CurrentVersion\Run\Updater", run.Locator);
        Assert.Contains("key last written", run.TimeSemantics);
        Assert.Single(sink.Events, e => e.Source == "RunKey" && e.Fields["Key"].EndsWith("RunOnce"));

        var f = Assert.Single(Correlation.Run(sink.Events), x => x.RuleId == "PERSIST-RUNKEY-USERPATH");
        Assert.Equal(@"C:\Users\u\AppData\Roaming\upd\upd.exe", f.File);
        Assert.Equal("T1547.001", f.MitreTechniqueId);
    }

    [Fact]
    public void Software_hive_run_keys_winlogon_and_ifeo_debugger()
    {
        var p = Software();
        var sink = new ListSink();
        var r = new SoftwareHiveParser().Parse(Item(p, "software_hive"), p, sink, default);
        Assert.Equal(EvidenceStatus.Success, r.Status);

        Assert.Contains(sink.Events, e => e.Source == "RunKey" && e.Fields["ValueName"] == "SecurityHealth" && e.Path == @"%windir%\system32\SecurityHealthSystray.exe");
        Assert.Contains(sink.Events, e => e.Source == "RunKey" && e.Fields["Key"].StartsWith("WOW6432Node") && e.Path == @"C:\ProgramData\agent\a.exe");
        var userinit = Assert.Single(sink.Events, e => e.Source == "Winlogon" && e.Fields["ValueName"] == "Userinit");
        Assert.Equal("true", userinit.Fields["NonDefault"]);
        Assert.Equal("false", Assert.Single(sink.Events, e => e.Source == "Winlogon" && e.Fields["ValueName"] == "Shell").Fields["NonDefault"]);
        var ifeo = Assert.Single(sink.Events, e => e.Source == "IFEO");
        Assert.Equal("sethc.exe", ifeo.Fields["Target"]);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", ifeo.Path);

        var findings = Correlation.Run(sink.Events);
        Assert.Contains(findings, x => x.RuleId == "PERSIST-IFEO-DEBUGGER" && x.Severity == Severity.High && x.MitreTechniqueId == "T1546.012");
        Assert.Contains(findings, x => x.RuleId == "PERSIST-WINLOGON" && x.Description.Contains(@"C:\Users\Public\x.exe"));
        Assert.Contains(findings, x => x.RuleId == "PERSIST-RUNKEY-USERPATH" && x.File == @"C:\ProgramData\agent\a.exe");
        Assert.DoesNotContain(findings, x => x.File.Contains("SecurityHealth"));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\A B\\x.exe\" -arg", @"C:\Program Files\A B\x.exe")]
    [InlineData(@"C:\Program Files\A B\x.exe -arg", @"C:\Program Files\A B\x.exe")]
    [InlineData(@"%windir%\system32\rundll32.exe a.dll,Entry", @"%windir%\system32\rundll32.exe")]
    [InlineData("explorer.exe", "explorer.exe")]
    [InlineData(@"C:\tools\run.cmd /x", @"C:\tools\run.cmd")]
    [InlineData("", "")]
    public void Executable_is_extracted_from_a_command_line(string command, string expected)
        => Assert.Equal(expected, CommandLine.Executable(command));

    /// <summary>NTUSER and SOFTWARE saved at 23:50:59; reg export of the same keys at 23:51:00. Every exported value must match.</summary>
    [CorpusFact("userHive")]
    public void Real_user_hive_matches_reg_export_for_userassist_and_run()
    {
        var hive = Corpus.File("userHive");
        var sink = new ListSink();
        Assert.Equal(EvidenceStatus.Success, new UserHiveParser().Parse(Item(hive, "ntuser_hive"), hive, sink, default).Status);

        var ua = RegExport.Read(Path.Combine(Corpus.Root, Corpus.S("userHive", "userAssistExport")));
        int compared = 0;
        foreach (var (key, values) in ua.Where(k => k.Key.EndsWith(@"\Count", StringComparison.OrdinalIgnoreCase)))
            foreach (var v in values.Where(v => v.Data.Length >= 68))
            {
                var name = Rot13(v.Name);
                if (name.StartsWith("UEME_CTL", StringComparison.Ordinal)) continue;
                compared++;
                var e = sink.Events.SingleOrDefault(x => x.Source == "UserAssist" && x.Fields["DecodedName"] == name && key.Contains(x.Fields["Guid"], StringComparison.OrdinalIgnoreCase));
                Assert.True(e is not null, $"lipsește {name} [{string.Join(" ", name.Select(c => ((int)c).ToString("X4")))}]; nepotrivite din hive: " +
                    string.Join(" || ", sink.Events.Where(x => x.Source == "UserAssist" && !x.Fields["DecodedName"].All(c => c < 128))
                        .Take(2).Select(x => string.Join(" ", x.Fields["DecodedName"].Select(c => ((int)c).ToString("X4"))))));
                Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(v.Data.AsSpan(4)).ToString(), e!.Fields["RunCount"]);
                Assert.Equal(BinaryPrimitives.ReadInt64LittleEndian(v.Data.AsSpan(60)), long.Parse(e.Fields["LastRunFileTime"]));
            }
        Assert.True(compared > 50, $"{compared} intrări UserAssist comparate");
        Assert.Equal(compared, sink.Events.Count(x => x.Source == "UserAssist"));

        var runValues = Corpus.S("userHive", "runExports").Split('|')
            .SelectMany(f => RegExport.Read(Path.Combine(Corpus.Root, f)).SelectMany(k => k.Value)).ToList();
        Assert.NotEmpty(runValues);
        foreach (var v in runValues)
            Assert.Contains(sink.Events, x => x.Source == "RunKey" && x.Fields["ValueName"] == v.Name && x.Fields["Command"] == v.AsString);
    }

    [CorpusFact("softwareHive")]
    public void Real_software_hive_matches_reg_export_for_run_and_winlogon()
    {
        var hive = Corpus.File("softwareHive");
        var sink = new ListSink();
        Assert.Equal(EvidenceStatus.Success, new SoftwareHiveParser().Parse(Item(hive, "software_hive"), hive, sink, default).Status);

        var values = Corpus.S("softwareHive", "runExports").Split('|')
            .SelectMany(f => RegExport.Read(Path.Combine(Corpus.Root, f)).SelectMany(k => k.Value)).ToList();
        Assert.NotEmpty(values);
        foreach (var v in values)
            Assert.Contains(sink.Events, x => x.Source == "RunKey" && x.Fields["ValueName"] == v.Name && x.Fields["Command"] == v.AsString);

        var wl = RegExport.Read(Path.Combine(Corpus.Root, Corpus.S("softwareHive", "winlogonExport")));
        foreach (var name in new[] { "Shell", "Userinit" })
        {
            var v = wl[@"Microsoft\Windows NT\CurrentVersion\Winlogon"].First(x => x.Name == name);
            Assert.Equal(v.AsString, Assert.Single(sink.Events, x => x.Source == "Winlogon" && x.Fields["ValueName"] == name).Fields["Command"]);
        }
    }
}
