using System.Diagnostics;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public sealed class AntiForensicsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_af_" + Guid.NewGuid().ToString("N"));
    public AntiForensicsTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static int _n;
    private static TimelineEvent E(string source, string id = "", string provider = "", string path = "", Dictionary<string, string>? f = null, long? record = null) => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc), "t", "test"), Source = source, EventId = id, Provider = provider,
        EvidenceId = "EV-1", Path = path, Summary = "s", Locator = record is { } r ? $"EventRecordID={r}" : $"L{Interlocked.Increment(ref _n)}",
        Fields = f is null ? new(StringComparer.OrdinalIgnoreCase) : new(f, StringComparer.OrdinalIgnoreCase),
    };

    private static AntiForensicCheck C(IEnumerable<AntiForensicCheck> cs, string id) => Assert.Single(cs, c => c.Id == id);
    private static List<AntiForensicCheck> Run(params TimelineEvent[] e) => AntiForensics.Evaluate(e, []);

    [Fact]
    public void Every_technique_of_the_spec_has_a_check_and_none_says_clean()
    {
        var empty = Run();
        Assert.Equal(16, empty.Count);
        Assert.All(empty, c => Assert.Equal(AntiForensicResult.Undetermined, c.Result));   // nothing analysed: nothing can be said
        Assert.All(empty, c => Assert.DoesNotContain("curat", c.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Log_clearing_needs_both_logs_for_a_negative()
    {
        var cleared = Run(E("EventLog:System", "104", "Microsoft-Windows-Eventlog", f: new() { ["Channel"] = "Application", ["SubjectUserName"] = "x" }));
        Assert.Equal(AntiForensicResult.Detected, C(cleared, "AF01").Result);
        Assert.Contains("Application", C(cleared, "AF01").Reason);
        Assert.Equal(AntiForensicResult.Undetermined, C(Run(E("EventLog:System", "7036")), "AF01").Result);
        Assert.Equal(AntiForensicResult.NotDetected, C(Run(E("EventLog:System", "7036"), E("EventLog:Security", "4624")), "AF01").Result);
        // A 104 from another provider is not a log clear.
        Assert.Equal(AntiForensicResult.Undetermined, C(Run(E("EventLog:System", "104", "Some-Driver")), "AF01").Result);
    }

    [Fact]
    public void Record_id_holes_are_reported_with_their_range()
    {
        var holes = Run(E("EventLog:System", "1", record: 10), E("EventLog:System", "1", record: 11), E("EventLog:System", "1", record: 15));
        var af = C(holes, "AF04");
        Assert.Equal(AntiForensicResult.Detected, af.Result);
        Assert.Contains("RecordID 12–14", af.Reason);
        Assert.Equal(AntiForensicResult.NotDetected, C(Run(E("EventLog:System", "1", record: 1), E("EventLog:System", "1", record: 2)), "AF04").Result);
    }

    [Fact]
    public void Clock_changes_by_time_sync_hardware_clock_or_time_zone_are_not_manipulation()
    {
        var sync = E("EventLog:Security", "4616", f: new() { ["ProcessName"] = @"C:\Windows\System32\svchost.exe" });
        var hw = E("EventLog:System", "1", "Microsoft-Windows-Kernel-General", f: new() { ["Reason"] = "2", ["ProcessName"] = "" });
        var tz = E("EventLog:System", "1", "Microsoft-Windows-Kernel-General", f: new() { ["Reason"] = "3", ["ProcessName"] = @"\Device\HarddiskVolume3\Windows\ImmersiveControlPanel\SystemSettings.exe" });
        Assert.Equal(AntiForensicResult.NotDetected, C(Run(sync, hw, tz), "AF05").Result);
        var manual = E("EventLog:Security", "4616", f: new() { ["ProcessName"] = @"C:\Windows\System32\SystemSettingsAdminFlows.exe", ["SubjectUserName"] = "Owner" });
        Assert.Equal(AntiForensicResult.Detected, C(Run(sync, hw, manual), "AF05").Result);
    }

    [Fact]
    public void Audit_removal_is_detected_and_its_absence_proves_nothing()
    {
        var removed = E("EventLog:Security", "4719", f: new() { ["AuditPolicyChanges"] = "%%8448, %%8450", ["SubcategoryGuid"] = "{0cce922b-69ae-11d9-bed3-505054503030}", ["SubjectUserName"] = "u" });
        var af = C(Run(removed), "AF10");
        Assert.Equal(AntiForensicResult.Detected, af.Result);
        Assert.Contains("Process Creation", af.Reason);
        Assert.Contains("Success removed, Failure removed", af.Reason);
        var added = E("EventLog:Security", "4719", f: new() { ["AuditPolicyChanges"] = "%%8449" });
        Assert.Equal(AntiForensicResult.Undetermined, C(Run(added), "AF10").Result);
        var disabled = E("Service", f: new() { ["StartValue"] = "4" }) is var s ? new TimelineEvent { Time = s.Time, Source = "Service", EvidenceId = "EV", Summary = "x", Service = "EventLog", Fields = s.Fields } : null;
        Assert.Equal(AntiForensicResult.Detected, C(Run(disabled!), "AF10").Result);
    }

    [Fact]
    public void Deleted_service_task_and_firewall_traces()
    {
        var install = E("EventLog:System", "7045", f: new() { ["ServiceName"] = "Evil Svc", ["ImagePath"] = @"C:\Users\Public\e.exe" });
        var kept = E("EventLog:System", "7045", f: new() { ["ServiceName"] = "Good", ["ImagePath"] = @"C:\x\good.exe" });
        var configured = new TimelineEvent { Time = install.Time, Source = "Service", EvidenceId = "EV", Summary = "s", Service = "GoodSvc", Fields = { ["DisplayName"] = "Good", ["ImagePath"] = @"C:\x\good.exe" } };
        var af11 = C(Run(install, kept, configured), "AF11");
        Assert.Equal(AntiForensicResult.Detected, af11.Result);
        Assert.Single(af11.Evidence);
        Assert.Equal(AntiForensicResult.Undetermined, C(Run(install), "AF11").Result);

        Assert.Equal(AntiForensicResult.Detected, C(Run(E("EventLog:Microsoft-Windows-TaskScheduler/Operational", "141", f: new() { ["TaskName"] = @"\T" })), "AF12").Result);
        Assert.Equal(AntiForensicResult.NotDetected, C(Run(E("EventLog:Microsoft-Windows-TaskScheduler/Operational", "200")), "AF12").Result);

        const string fw = "EventLog:Microsoft-Windows-Windows Firewall With Advanced Security/Firewall";
        Assert.Equal(AntiForensicResult.Detected, C(Run(E(fw, "2059", f: new() { ["Store Type"] = "12" })), "AF16").Result);
        Assert.Equal(AntiForensicResult.Detected, C(Run(E(fw, "2097", f: new() { ["Action"] = "3", ["ApplicationPath"] = @"C:\Users\a\AppData\Local\Temp\x.exe" })), "AF16").Result);
        Assert.Equal(AntiForensicResult.NotDetected, C(Run(E(fw, "2097", f: new() { ["Action"] = "3", ["ApplicationPath"] = @"C:\Program Files\App\app.exe" })), "AF16").Result);
    }

    [Theory]
    [InlineData(@"\VOLUME{01d0}\WINDOWS\TEMP\SVCHOST.EXE", true)]
    [InlineData(@"C:\Users\Public\lsass.exe", true)]
    [InlineData(@"\??\C:\ProgramData\csrss.exe", true)]
    [InlineData(@"C:\Windows\System32\svchost.exe", false)]
    [InlineData(@"\Device\HarddiskVolume3\Windows\System32\lsass.exe", false)]
    [InlineData(@"c:\windows\syswow64\windowspowershell\v1.0\powershell.exe", false)]
    [InlineData(@"C:\Windows\WinSxS\amd64_x\svchost.exe", false)]
    [InlineData(@"C:\Windows\explorer.exe", false)]
    [InlineData(@"C:\Tools\notepad.exe", false)]
    public void Masquerading_compares_core_process_names_with_their_folders(string path, bool expected) =>
        Assert.Equal(expected, AntiForensics.IsMasquerading(path));

    [Fact]
    public void Renamed_system_utility_comes_from_amcache_original_file_name()
    {
        var renamed = E("Amcache", path: @"c:\users\a\downloads\update.exe", f: new() { ["OriginalFileName"] = "certutil.exe" });
        var benign = E("Amcache", path: @"c:\program files\x\3.6.0_47178.exe", f: new() { ["OriginalFileName"] = "utorrent.exe" });
        Assert.Equal(AntiForensicResult.Detected, C(Run(renamed, benign), "AF15").Result);
        Assert.Equal(AntiForensicResult.NotDetected, C(Run(benign), "AF15").Result);   // an ordinary renamed program is not this technique
    }

    /// <summary>Exports this station's System log (readable without elevation), then cuts it: the parser must say it is truncated.</summary>
    [Fact]
    public void Truncated_evtx_is_reported_by_the_parser_and_detected()
    {
        var clean = Path.Combine(_dir, "system.evtx");
        using (var p = Process.Start(new ProcessStartInfo("wevtutil.exe", $"epl System \"{clean}\" /ow:true") { UseShellExecute = false, CreateNoWindow = true })!)
            p.WaitForExit(60_000);
        Assert.True(new FileInfo(clean).Length > 4096 + 2 * 65536, "System log too small for the test");
        Assert.Null(EvtxParser.Truncation(clean));

        var bytes = File.ReadAllBytes(clean);
        var cutTail = Path.Combine(_dir, "cut_tail.evtx");
        File.WriteAllBytes(cutTail, bytes[..^1000]);
        Assert.Contains("după ultimul chunk complet", EvtxParser.Truncation(cutTail));
        var cutChunk = Path.Combine(_dir, "cut_chunk.evtx");
        File.WriteAllBytes(cutChunk, bytes[..^65536]);
        // A whole missing chunk is visible only if the header counted it (a live log's header can lag behind its chunks).
        int declared = BitConverter.ToUInt16(bytes, 42);
        long left = (bytes.Length - 65536 - 4096) / 65536;
        if (declared > left) Assert.Equal($"antetul declară {declared} chunk-uri, fișierul conține {left}", EvtxParser.Truncation(cutChunk));
        else Assert.Null(EvtxParser.Truncation(cutChunk));
        var headerCut = Path.Combine(_dir, "header_cut.evtx");
        var hb = bytes[..(4096 + 65536)];
        BitConverter.GetBytes((ushort)5).CopyTo(hb, 42);
        File.WriteAllBytes(headerCut, hb);
        Assert.Equal("antetul declară 5 chunk-uri, fișierul conține 1", EvtxParser.Truncation(headerCut));

        var item = new EvidenceItem { EvidenceId = "EV-T", CaseId = "C", Source = "System", SourceType = "evtx", StoredPath = cutTail };
        var sink = new ListSink();
        var r = new EvtxParser().Parse(item, cutTail, sink, default);
        var gap = Assert.Single(r.Gaps, g => g.Reason.StartsWith(AntiForensics.TruncatedMarker, StringComparison.Ordinal));
        Assert.Equal(AntiForensicResult.Detected, AntiForensics.Evaluate(sink.Events, r.Gaps).Single(c => c.Id == "AF03").Result);
        Assert.Equal(AntiForensicResult.NotDetected, AntiForensics.Evaluate(sink.Events, []).Single(c => c.Id == "AF03").Result);
        Assert.NotNull(gap);
    }

    // ---- real corpus, with each expected value recomputed by wevtutil (an independent reader) ----

    private static string[] Wevtutil(string file, string xpath)
    {
        var psi = new ProcessStartInfo("wevtutil.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in new[] { "qe", file, "/lf:true", "/q:" + xpath, "/f:xml" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var xml = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return Regex.Split(xml, "(?=<Event )").Where(x => x.StartsWith("<Event ", StringComparison.Ordinal)).ToArray();
    }

    private static string Data(string ev, string name) => Regex.Match(ev, $"<Data Name='{Regex.Escape(name)}'>([^<]*)</Data>").Groups[1].Value;

    [CorpusFact("antiForensics")]
    public void Real_corpus_traces_match_wevtutil_and_the_wmi_service_list()
    {
        string P(string k) => Path.Combine(Corpus.Root, Corpus.S("antiForensics", k));
        var events = new List<TimelineEvent>();
        var gaps = new List<EvidenceGap>();
        void Parse(LogAnalyzer.Dfir.Parsing.IEvidenceParser parser, string path, string id)
        {
            var sink = new ListSink();
            var r = parser.Parse(new EvidenceItem { EvidenceId = id, CaseId = "C", Source = id, SourceType = "x", StoredPath = path }, path, sink, default);
            events.AddRange(sink.Events);
            gaps.AddRange(r.Gaps);
        }
        Parse(new EvtxParser(), P("security"), "EV-SEC");
        Parse(new EvtxParser(), Corpus.File("antiForensics"), "EV-SYS");
        Parse(new EvtxParser(), P("firewall"), "EV-FW");
        Parse(new ServicesParser(), P("systemHive"), "EV-HIVE");
        Parse(new AmcacheParser(), P("amcache"), "EV-AMC");
        var af = AntiForensics.Evaluate(events, gaps).ToDictionary(c => c.Id);

        // AF01: the clears wevtutil sees (System 104 + Security 1102 from the Eventlog provider) are the ones reported.
        var clears = Wevtutil(Corpus.File("antiForensics"), "*[System[Provider[@Name='Microsoft-Windows-Eventlog'] and EventID=104]]");
        Assert.Empty(Wevtutil(P("security"), "*[System[Provider[@Name='Microsoft-Windows-Eventlog'] and EventID=1102]]"));
        Assert.Equal(AntiForensicResult.Detected, af["AF01"].Result);
        Assert.Equal(clears.Length, af["AF01"].Evidence.Count);
        foreach (var ch in Corpus.S("antiForensics", "clearedChannels").Split('|')) Assert.Contains($" {ch} de ", af["AF01"].Reason);
        Assert.Contains(Corpus.S("antiForensics", "clearedAtUtc"), af["AF01"].Reason);

        // AF10: audit removals (%%8448 Success removed / %%8450 Failure removed).
        var removals = Wevtutil(P("security"), "*[System[EventID=4719]]").Count(e => Data(e, "AuditPolicyChanges") is var c && (c.Contains("%%8448") || c.Contains("%%8450")));
        Assert.Equal(Corpus.L("antiForensics", "auditRemovals"), removals);
        Assert.Equal(removals, af["AF10"].Evidence.Count);

        // AF05: clock set by something other than svchost (W32Time), in both logs that record it.
        var t4616 = Wevtutil(P("security"), "*[System[EventID=4616]]").Count(e => !Data(e, "ProcessName").EndsWith(@"\svchost.exe", StringComparison.OrdinalIgnoreCase));
        var kg = Wevtutil(Corpus.File("antiForensics"), "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=1]]")
            .Count(e => Data(e, "Reason") == "1" && !Data(e, "ProcessName").EndsWith(@"\svchost.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Corpus.L("antiForensics", "manualTimeChanges4616"), t4616);
        Assert.Equal(t4616 + kg, af["AF05"].Evidence.Count);

        // AF16: the one "all rules deleted" event, during the incident window.
        var all = Wevtutil(P("firewall"), "*[System[EventID=2059]]");
        Assert.Equal(all.Length, af["AF16"].Evidence.Count);
        Assert.Contains(Corpus.S("antiForensics", "firewallAllRulesDeletedUtc"), af["AF16"].Reason);

        // AF11: every service reported as gone from the SYSTEM hive is also absent from WMI Win32_Service on the station.
        var rows = CsvReader.ReadRows(new StreamReader(P("servicesCsv"))).ToList();
        int iDisp = Array.IndexOf(rows[0], "DisplayName"), iPath = Array.IndexOf(rows[0], "PathName");
        var wmi = rows.Skip(1).Where(r => r.Length > Math.Max(iDisp, iPath)).ToList();
        Assert.Equal(AntiForensicResult.Detected, af["AF11"].Result);
        foreach (var r in af["AF11"].Evidence)
        {
            var e = events.Single(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);
            Assert.DoesNotContain(wmi, w => w[iDisp].Equals(e.Fields["ServiceName"], StringComparison.OrdinalIgnoreCase)
                                            || w[iPath].Trim().Equals(e.Fields["ImagePath"].Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Negatives, each with the source analysed: structure intact, no record holes, no masquerading or renamed utility.
        foreach (var id in new[] { "AF02", "AF03", "AF04", "AF13", "AF15" }) Assert.Equal(AntiForensicResult.NotDetected, af[id].Result);
        Assert.Equal(AntiForensicResult.Undetermined, af["AF06"].Result);   // Prefetch on (EnablePrefetcher = 3); .pf deletion is not visible
        Assert.Contains("EnablePrefetcher = 3", af["AF06"].Reason);
        Assert.Equal(AntiForensicResult.Undetermined, af["AF12"].Result);   // no TaskScheduler/Operational in the corpus
    }
}
