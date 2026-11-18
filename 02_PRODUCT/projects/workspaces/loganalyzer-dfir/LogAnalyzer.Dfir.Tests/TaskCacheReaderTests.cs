using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>A [Fact] skipped when a lab file (outside the repository) is not on this machine. Lab files are not part of the corpus, so LADFIR_REQUIRE_CORPUS does not make their absence a failure.</summary>
public sealed class LabFileFactAttribute : FactAttribute
{
    public LabFileFactAttribute(params string[] files)
    {
        var missing = files.FirstOrDefault(f => !File.Exists(f));
        if (missing is not null) Skip = $"Lab file not present: {missing} — REAL-DATA VALIDATION = UNAVAILABLE";
    }
}

/// <summary>
/// Task Scheduler cache (SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache): the bytes of one task created on purpose in a lab VM,
/// hostile blobs and hives (threat-model probes), synthetic hives through the SOFTWARE parser, and the 226-task reference station when present.
/// </summary>
public sealed class TaskCacheReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_tc_" + Guid.NewGuid().ToString("N"));
    public TaskCacheReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private const string Tc = TaskCacheReader.KeyPath;
    private const string LabRef = @"E:\LA-Lab\diag\taskcache\ref\SOFTWARE.hiv", LabRefJson = @"E:\LA-Lab\diag\taskcache\ref\tasks.json", LabEarlier = @"E:\LA-Lab\diag\taskcache\SOFTWARE.hiv";

    // Real DynamicInfo of \LA-TEST\LA-TEST-TaskCache (no SID inside). The real Actions bytes of that task are read from the lab hive at run time
    // (Real_earlier_hive_contains_the_LA_TEST_task_with_its_two_actions): embedded here, their command line (a shell redirection to a file) made Defender flag the test DLL
    // (Wacatac, ML false positive, 2026-11-18), so the layout test below uses the same layout built by the helpers with a benign command.
    private const string RealDynamicHex = "0300000022675F8C5677DD0107666B8C5677DD010000000000000000172A738C5677DD01";

    private static byte[] Hex(string h) => Convert.FromHexString(h);

    // ---------------------------------------------------------------- blob builders

    private static byte[] Str(string s) => [.. BitConverter.GetBytes((uint)(s.Length * 2)), .. Encoding.Unicode.GetBytes(s)];
    private static byte[] Exec(string cmd, string args = "", string wd = "") => [0x66, 0x66, .. Str(""), .. Str(cmd), .. Str(args), .. Str(wd), 0, 0];
    private static byte[] Com(Guid clsid, string data) => [0x77, 0x77, .. Str(""), .. clsid.ToByteArray(), .. Str(data)];
    private static byte[] Actions(string ctx, params byte[][] acts) => [3, 0, .. Str(ctx), .. acts.SelectMany(a => a)];
    private static byte[] Dynamic(DateTime created, DateTime? lastRun, uint result = 0, DateTime? t28 = null)
    {
        var b = new byte[36];
        b[0] = 3;
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(4), created.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(12), lastRun?.ToFileTimeUtc() ?? 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), result);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(28), t28?.ToFileTimeUtc() ?? 0);
        return b;
    }

    // ---------------------------------------------------------------- real LA-TEST bytes

    [Fact]
    public void The_observed_actions_layout_two_exec_actions_after_the_context_is_parsed_completely()
    {
        // same layout as the real LA-TEST blob: version 3, context "Author", Exec with args + working dir, Exec with neither (trailing u16 after each Exec)
        var bytes = Actions("Author", Exec(@"C:\Windows\System32\hostname.exe", "/?", @"C:\LA"), Exec(@"C:\Windows\System32\whoami.exe"));
        Assert.Equal(new byte[] { 0x03, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x41, 0x00 }, bytes[..8]);   // the first bytes of the real blob
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, bytes[^10..]);   // the real blob ends the same way
        var blob = TaskCacheReader.ParseActions(bytes);
        Assert.Null(blob.Gap);
        Assert.Equal(3, blob.Version);
        Assert.Equal("Author", blob.Context);
        Assert.Equal(2, blob.Actions.Count);
        Assert.Equal("Exec", blob.Actions[0].Type);
        Assert.Equal(@"C:\Windows\System32\hostname.exe", blob.Actions[0].Command);
        Assert.Equal("/?", blob.Actions[0].Arguments);
        Assert.Equal(@"C:\LA", blob.Actions[0].WorkingDirectory);
        Assert.Equal(@"C:\Windows\System32\whoami.exe", blob.Actions[1].Command);
        Assert.Equal("", blob.Actions[1].Arguments);
        Assert.Equal("", blob.Actions[1].WorkingDirectory);
    }

    [Fact]
    public void Real_LA_TEST_dynamic_info_has_the_observed_values()
    {
        var (info, gap) = TaskCacheReader.ParseDynamicInfo(Hex(RealDynamicHex));
        Assert.Null(gap);
        Assert.NotNull(info);
        string T(long ft) => DateTime.FromFileTimeUtc(ft).ToString("yyyy-MM-ddTHH:mm:ss.ffff");
        Assert.Equal("2026-11-18T12:15:49.6938", T(info!.CacheRegisteredFileTime));
        Assert.Equal("2026-11-18T12:15:49.7724", T(info.LastRunFileTime));            // 12:15:49.77Z = Get-ScheduledTaskInfo LastRunTime 12:15:49Z
        Assert.Equal(0u, info.LastResultRaw);                                  // = LastTaskResult 0
        Assert.Equal("2026-11-18T12:15:49.8233", T(info.Time28FileTime));
    }

    // ---------------------------------------------------------------- probe 1, 2, 5: hostile blobs

    [Fact]
    public void Probe1_a_length_larger_than_the_blob_stops_that_blob_with_a_gap_and_does_not_throw()
    {
        foreach (var len in new uint[] { 0x7FFFFFF0, 0xFFFFFFFF, 0xFFFFFFFE, 1000 })
        {
            byte[] b = [3, 0, .. BitConverter.GetBytes(len), 1, 2, 3, 4];
            var r = TaskCacheReader.ParseActions(b);
            Assert.NotNull(r.Gap);
            Assert.Empty(r.Actions);
        }
        // a huge length in the middle keeps the actions read so far
        byte[] mid = [.. Actions("ctx", Exec("a.exe", "x")), 0x66, 0x66, .. Str(""), 0xF0, 0xFF, 0xFF, 0x7F];
        var m = TaskCacheReader.ParseActions(mid);
        Assert.NotNull(m.Gap);
        Assert.Equal("a.exe", Assert.Single(m.Actions).Command);
    }

    [Fact]
    public void Probe1_an_odd_utf16_length_and_truncations_are_gaps()
    {
        Assert.NotNull(TaskCacheReader.ParseActions([3, 0, 3, 0, 0, 0, 65, 0, 66]).Gap);
        Assert.NotNull(TaskCacheReader.ParseActions([]).Gap);
        Assert.NotNull(TaskCacheReader.ParseActions([3]).Gap);
        Assert.NotNull(TaskCacheReader.ParseActions([3, 0, 0, 0]).Gap);
        var full = Actions("c", Exec("a.exe", "b", "c:\\"), Com(Guid.NewGuid(), "d"));
        int afterContext = Actions("c").Length, afterExec = Actions("c", Exec("a.exe", "b", "c:\\")).Length;
        for (int cut = 0; cut < full.Length; cut++)
        {
            var r = TaskCacheReader.ParseActions(full.AsSpan(0, cut));   // every prefix: never an exception
            // a prefix that ends exactly between two parts is complete (no gap); any other prefix is a gap, keeping the actions read before the cut
            bool boundary = cut == afterContext || cut == afterExec;
            Assert.Equal(!boundary, r.Gap is not null);
            Assert.Equal(cut >= afterExec ? 1 : 0, r.Actions.Count);
        }
        Assert.Null(TaskCacheReader.ParseActions(full).Gap);
    }

    [Fact]
    public void Probe2_an_unknown_action_type_is_recorded_raw_and_stops_the_blob()
    {
        byte[] b = [.. Actions("ctx", Exec("first.exe")), 0x34, 0x12, .. Exec("never.exe")[2..]];
        var r = TaskCacheReader.ParseActions(b);
        Assert.NotNull(r.Gap);
        Assert.Contains("0x1234", r.Gap);
        Assert.Equal(2, r.Actions.Count);
        Assert.Equal("first.exe", r.Actions[0].Command);
        Assert.Equal("0x1234", r.Actions[1].Type);
        Assert.Equal("", r.Actions[1].Command);
        Assert.DoesNotContain(r.Actions, a => a.Command == "never.exe");
        Assert.NotNull(TaskCacheReader.ParseActions([2, 0, 0, 0, 0, 0]).Gap);   // another version: not guessed
    }

    [Fact]
    public void Probe5_malformed_dynamic_info_gives_no_fields_and_a_gap()
    {
        Assert.NotNull(TaskCacheReader.ParseDynamicInfo(new byte[10]).Gap);
        Assert.Null(TaskCacheReader.ParseDynamicInfo(new byte[10]).Info);
        Assert.NotNull(TaskCacheReader.ParseDynamicInfo([]).Gap);
        var v2 = Hex(RealDynamicHex);
        v2[0] = 2;
        Assert.NotNull(TaskCacheReader.ParseDynamicInfo(v2).Gap);
        Assert.Null(TaskCacheReader.ParseDynamicInfo(v2).Info);
    }

    // ---------------------------------------------------------------- synthetic hives through the SOFTWARE parser

    private static readonly DateTime Created = new(2026, 11, 18, 12, 15, 49, 694, DateTimeKind.Utc);
    private static readonly DateTime Ran = new(2026, 11, 18, 12, 15, 49, 772, DateTimeKind.Utc);

    private sealed class Builder(RegistryHive h)
    {
        public void RunValue(string name, string command) =>
            h.Root.CreateSubKey(@"Microsoft\Windows\CurrentVersion\Run").SetValue(name, command, RegistryValueType.String);

        public void Task(string guid, string name, byte[]? actions, byte[]? dynamic, bool tree = true, bool sd = true, string? category = null,
                         string? uri = null, string? desc = null, bool tasksKey = true)
        {
            if (tree)
            {
                var t = h.Root.CreateSubKey($@"{Tc}\Tree{name}");
                t.SetValue("Id", guid, RegistryValueType.String);
                t.SetValue("Index", 3, RegistryValueType.Dword);
                if (sd) t.SetValue("SD", new byte[] { 1, 0, 4, 0x80 }, RegistryValueType.Binary);
            }
            if (tasksKey)
            {
                var k = h.Root.CreateSubKey($@"{Tc}\Tasks\{guid}");
                k.SetValue("Path", name, RegistryValueType.String);
                k.SetValue("URI", uri ?? name, RegistryValueType.String);
                if (desc is not null) k.SetValue("Description", desc, RegistryValueType.String);
                k.SetValue("Schema", 65538, RegistryValueType.Dword);
                k.SetValue("Triggers", new byte[] { 1, 2, 3 }, RegistryValueType.Binary);
                if (actions is not null) k.SetValue("Actions", actions, RegistryValueType.Binary);
                if (dynamic is not null) k.SetValue("DynamicInfo", dynamic, RegistryValueType.Binary);
            }
            if (category is not null) h.Root.CreateSubKey($@"{Tc}\{category}\{guid}");
        }
    }

    private string NewHive(Action<Builder> build, string name = "SOFTWARE")
    {
        var path = Path.Combine(_dir, name);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var h = RegistryHive.Create(fs);
        h.Root.CreateSubKey(@"Microsoft\Windows\CurrentVersion\Run");
        build(new Builder(h));
        return path;
    }

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-T", CaseId = "C", Source = "software_hive", SourceType = "software_hive", StoredPath = path };

    private (ListSink Sink, ParseResult Result) Run(string hive)
    {
        var sink = new ListSink();
        var result = new SoftwareHiveParser().Parse(Item(hive), hive, sink, default);
        return (sink, result);
    }

    private static List<TimelineEvent> TaskEvents(ListSink s) => s.Events.Where(e => e.Source == "TaskCache").ToList();

    [Fact]
    public void A_synthetic_task_emits_one_event_per_action_with_exact_fields()
    {
        const string g = "{1F66A3F0-954E-4322-ACCA-9AD94C300C4A}";
        var comClass = Guid.Parse("84F0FAE1-C27B-4F6F-807B-28CF6F96287D");
        var path = NewHive(b => b.Task(g, @"\LA-TEST\Sample", Actions("Author", Exec(@"C:\Windows\System32\cmd.exe", "/c dir", @"C:\LA"), Com(comClass, "/RuntimeWide")),
            Dynamic(Created, Ran, 5, Ran.AddMilliseconds(51)), category: "Logon", desc: "sample"));
        var (sink, result) = Run(path);
        Assert.Equal(EvidenceStatus.Success, result.Status);
        var ev = TaskEvents(sink);
        Assert.Equal(2, ev.Count);

        var e = ev[0];
        Assert.Equal(TemporalType.CurrentSnapshot, e.TemporalType);
        Assert.Equal(Classification.Direct, e.Classification);
        Assert.Equal(Confidence.High, e.Confidence);
        Assert.Equal(Created, e.Time.Utc!.Value.UtcDateTime);
        Assert.Contains("offset 4", e.TimeSemantics);
        Assert.Contains("reînregistrare", e.TimeSemantics);
        Assert.Contains("nu crearea activității", e.TimeSemantics);
        Assert.False(e.Fields.ContainsKey("CreatedUtc"));
        Assert.Equal(@"\LA-TEST\Sample", e.Task);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", e.Path);
        Assert.Equal("cmd.exe", e.Process);
        Assert.StartsWith(@"Activitate planificată în cache-ul din registru \LA-TEST\Sample: ", e.Summary);
        Assert.Equal("Exec", e.Fields["ActionType"]);
        Assert.Equal("/c dir", e.Fields["Arguments"]);
        Assert.Equal(@"C:\LA", e.Fields["WorkingDirectory"]);
        Assert.Equal("Author", e.Fields["ActionContext"]);
        Assert.Equal(g, e.Fields["TaskId"]);
        Assert.Equal("Logon", e.Fields["Categories"]);
        Assert.Equal("sample", e.Fields["Description"]);
        Assert.Equal("3", e.Fields["TreeIndex"]);
        Assert.Equal("true", e.Fields["TreeHasSd"]);
        Assert.Equal("false", e.Fields["LastRunNever"]);
        Assert.StartsWith("2026-11-18T12:15:49.772", e.Fields["LastRunUtc"]);
        Assert.StartsWith("2026-11-18T12:15:49.694", e.Fields["CacheRegisteredUtc"]);
        Assert.Equal("", e.Fields["Anomaly"]);
        Assert.True(e.Fields.ContainsKey("TasksKeyLastWriteUtc"));
        Assert.Equal("ComHandler", ev[1].Fields["ActionType"]);
        Assert.Equal("{84F0FAE1-C27B-4F6F-807B-28CF6F96287D}", ev[1].Fields["ComClassId"]);
        Assert.Equal("/RuntimeWide", ev[1].Fields["ComData"]);
        Assert.Equal("", ev[1].Path);
        Assert.Empty(result.Gaps.Where(x => x.Artifact == "TaskCache"));
    }

    [Fact]
    public void A_task_with_no_dynamic_info_falls_back_to_the_tasks_key_time_and_reports_a_gap()
    {
        const string g = "{AAAAAAAA-0000-0000-0000-000000000001}";
        var (sink, result) = Run(NewHive(b => b.Task(g, @"\T\NoDyn", Actions("Author", Exec("a.exe")), new byte[10])));
        var e = Assert.Single(TaskEvents(sink));
        Assert.Contains("Tasks subkey last written", e.TimeSemantics);
        Assert.Equal("", e.Fields["CacheRegisteredUtc"]);
        Assert.Contains("DynamicInfoUnreadable", e.Fields["Anomaly"]);
        Assert.Contains(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.Contains("DynamicInfo"));
    }

    [Fact]
    public void Anomalies_are_fields_and_gaps_and_the_rest_of_the_cache_is_still_read()
    {
        const string noSd = "{AAAAAAAA-0000-0000-0000-000000000001}", orphanTree = "{AAAAAAAA-0000-0000-0000-000000000002}",
                     noTree = "{AAAAAAAA-0000-0000-0000-000000000003}", badActions = "{AAAAAAAA-0000-0000-0000-000000000004}", ok = "{AAAAAAAA-0000-0000-0000-000000000005}";
        var (sink, result) = Run(NewHive(b =>
        {
            b.Task(noSd, @"\T\NoSd", Actions("Author", Exec("nosd.exe")), Dynamic(Created, null), sd: false);
            b.Task(orphanTree, @"\T\Orphan", null, null, tasksKey: false);
            b.Task(noTree, @"\T\NoTree", Actions("Author", Exec("notree.exe")), Dynamic(Created, null), tree: false);
            b.Task(badActions, @"\T\Bad", [3, 0, 0xF0, 0xFF, 0xFF, 0x7F], Dynamic(Created, null));
            b.Task(ok, @"\T\Ok", Actions("Author", Exec("ok.exe")), Dynamic(Created, Ran));
        }));
        var ev = TaskEvents(sink);
        Assert.Equal("TreeEntryWithoutSd", ev.Single(e => e.Fields["TaskId"] == noSd).Fields["Anomaly"]);
        Assert.Equal("false", ev.Single(e => e.Fields["TaskId"] == noSd).Fields["TreeHasSd"]);
        Assert.Equal("TreeIdWithoutTasksKey", ev.Single(e => e.Fields["TaskId"] == orphanTree).Fields["Anomaly"]);
        Assert.Equal("TasksKeyWithoutTreeEntry", ev.Single(e => e.Fields["TaskId"] == noTree).Fields["Anomaly"]);
        Assert.Equal("", ev.Single(e => e.Fields["TaskId"] == noTree).Fields["TreeHasSd"]);
        Assert.Contains("ActionsUnreadable", ev.Single(e => e.Fields["TaskId"] == badActions).Fields["Anomaly"]);
        Assert.Equal("", ev.Single(e => e.Fields["TaskId"] == ok).Fields["Anomaly"]);
        Assert.Equal("ok.exe", ev.Single(e => e.Fields["TaskId"] == ok).Process);

        var gaps = result.Gaps.Where(x => x.Artifact == "TaskCache").Select(x => x.Reason).ToList();
        Assert.Contains(gaps, r => r.StartsWith("intrare Tree fără valoarea SD"));
        Assert.Contains(gaps, r => r.StartsWith("intrare Tree cu Id fără cheie în Tasks"));
        Assert.Contains(gaps, r => r.StartsWith("cheie Tasks fără intrare în Tree"));
        Assert.Contains(gaps, r => r.StartsWith("blobul Actions nu a putut fi interpretat"));
        var sdGap = result.Gaps.First(x => x.Reason.StartsWith("intrare Tree fără valoarea SD"));
        Assert.Contains("lipsește SD; activitatea nu apare în listări", sdGap.Impact);
        Assert.DoesNotContain("atac", sdGap.Impact, StringComparison.OrdinalIgnoreCase);   // no attribution
    }

    [Fact]
    public void Probe6_control_characters_stay_in_fields_but_the_summary_is_one_line()
    {
        const string g = "{AAAAAAAA-0000-0000-0000-000000000009}";
        var (sink, _) = Run(NewHive(b => b.Task(g, "\\T\\Line\r\nBreak", Actions("A", Exec("c:\\x\r\n.exe", "-a\nb\u2028c\u0085d")), Dynamic(Created, null), uri: "\\T\\Line\r\nBreak")));
        var e = Assert.Single(TaskEvents(sink));
        Assert.Contains("\r\n", e.Fields["TaskUri"]);
        Assert.Contains("\n", e.Fields["Arguments"]);
        Assert.DoesNotContain('\n', e.Summary);
        Assert.DoesNotContain('\r', e.Summary);
        Assert.DoesNotContain('\u2028', e.Summary);
        Assert.DoesNotContain('\u0085', e.Summary);
    }

    [Fact]
    public void A_hive_without_task_cache_reports_a_gap_and_no_events()
    {
        var (sink, result) = Run(NewHive(_ => { }));
        Assert.Empty(TaskEvents(sink));
        Assert.Contains(result.Gaps, x => x.Artifact == "TaskCache" && x.Status == EvidenceStatus.NotAvailable);
    }

    // ---------------------------------------------------------------- probe 3: deep Tree, cyclic Tree

    [Fact]
    public void Probe3_a_tree_deeper_than_the_limit_is_cut_with_a_gap_and_shallow_tasks_are_still_read()
    {
        const string deepId = "{AAAAAAAA-0000-0000-0000-0000000000D1}", shallow = "{AAAAAAAA-0000-0000-0000-0000000000D2}";
        var deepName = "\\" + string.Join("\\", Enumerable.Range(0, TaskCacheReader.MaxTreeDepth + 8).Select(i => "d" + i));
        var (sink, result) = Run(NewHive(b =>
        {
            b.Task(deepId, deepName, Actions("A", Exec("deep.exe")), Dynamic(Created, null));
            b.Task(shallow, @"\Shallow", Actions("A", Exec("shallow.exe")), Dynamic(Created, null));
        }));
        Assert.Equal(EvidenceStatus.Success, result.Status);
        Assert.Contains(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.StartsWith("Tree depășește adâncimea maximă"));
        var shallowEv = TaskEvents(sink).Single(e => e.Fields["TaskId"] == shallow);
        Assert.Equal("", shallowEv.Fields["Anomaly"]);
        // the deep Tree entry was not read, so its Tasks key is reported as having no Tree entry
        Assert.Contains("TasksKeyWithoutTreeEntry", TaskEvents(sink).Single(e => e.Fields["TaskId"] == deepId).Fields["Anomaly"]);
    }

    private static byte[] Nk(string name, int subkeyCount, int listOffset)
    {
        var nk = new byte[0x4C + name.Length + 4];
        nk[0] = (byte)'n'; nk[1] = (byte)'k';
        BinaryPrimitives.WriteUInt16LittleEndian(nk.AsSpan(2), 0x20);
        BinaryPrimitives.WriteInt64LittleEndian(nk.AsSpan(4), new DateTime(2026, 9, 19, 14, 55, 1, DateTimeKind.Utc).ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x14), subkeyCount);
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x1C), listOffset);
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x24), 0);
        BinaryPrimitives.WriteInt32LittleEndian(nk.AsSpan(0x28), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(nk.AsSpan(0x48), (ushort)name.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(nk, 0x4C);
        return nk;
    }

    /// <summary>Hand-built regf: a chain of keys down to <c>…\TaskCache\Tree\X</c> whose own subkey list points back at X (a key cycle, not a list cycle).</summary>
    private static byte[] CyclicTreeHive()
    {
        string[] names = ["ROOT", "Microsoft", "Windows NT", "CurrentVersion", "Schedule", "TaskCache", "Tree", "X"];
        var f = new byte[0x1000 + 0x2000];
        Encoding.ASCII.GetBytes("regf").CopyTo(f, 0);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(0x24), 0x20);
        Encoding.ASCII.GetBytes("hbin").CopyTo(f, 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(0x1000 + 8), 0x2000);
        void Put(int off, byte[] payload)
        {
            int size = (payload.Length + 4 + 7) & ~7;
            BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(0x1000 + off), -size);
            payload.CopyTo(f, 0x1000 + off + 4);
        }
        int NkAt(int i) => 0x20 + i * 0x100;
        int ListAt(int i) => NkAt(i) + 0x80;
        for (int i = 0; i < names.Length; i++)
        {
            int child = i + 1 < names.Length ? NkAt(i + 1) : NkAt(i);   // the last key lists itself
            Put(NkAt(i), Nk(names[i], 1, ListAt(i)));
            var li = new byte[8];
            Encoding.ASCII.GetBytes("li").CopyTo(li, 0);
            li[2] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(li.AsSpan(4), child);
            Put(ListAt(i), li);
        }
        return f;
    }

    [Fact]
    public void Probe3_a_cyclic_tree_terminates_with_a_gap()
    {
        var path = Path.Combine(_dir, "cyc.hiv");
        File.WriteAllBytes(path, CyclicTreeHive());
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var reg = new RawRegistry(fs);
        Assert.NotNull(reg.OpenKey(Tc + @"\Tree\X"));   // the hand-built hive really is readable down to X
        var sink = new ListSink();
        var result = new ParseResult { EvidenceId = "E", Parser = "p", ParserVersion = "1" };
        var done = Task.Run(() => TaskCacheReader.Emit(reg, Item(path), sink, result, default));
        Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "the Tree walk did not terminate");
        Assert.Contains(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.StartsWith("Tree conține o celulă vizitată de două ori"));
    }

    // ---------------------------------------------------------------- probe 4: many tasks

    private string ManyTasksHive(int n, string name) => NewHive(b =>
    {
        for (int i = 0; i < n; i++)
            b.Task($"{{AAAAAAAA-0000-0000-0000-{i:D12}}}", $@"\Many\T{i}", Actions("A", Exec($"t{i}.exe")), Dynamic(Created, null), category: i % 2 == 0 ? "Plain" : null);
    }, name);

    private TimeSpan BestOf(string hive, int n, int runs)
    {
        var best = TimeSpan.MaxValue;
        for (int r = 0; r < runs; r++)
        {
            var sw = Stopwatch.StartNew();
            var (sink, result) = Run(hive);
            sw.Stop();
            Assert.Equal(n, TaskEvents(sink).Count);
            Assert.All(TaskEvents(sink), e => Assert.Equal("", e.Fields["Anomaly"]));
            Assert.Empty(result.Gaps.Where(x => x.Artifact == "TaskCache"));
            if (sw.Elapsed < best) best = sw.Elapsed;
        }
        return best;
    }

    [Fact]
    public void Probe4_thousands_of_tasks_are_read_in_linear_time()
    {
        // n and 4n tasks: linear work gives a ratio near 4, quadratic work near 16. The bound 10 sits between with room for timer noise; best of 2 runs each.
        const int n = 600;
        var small = ManyTasksHive(n, "S1");
        var large = ManyTasksHive(4 * n, "S4");
        BestOf(small, n, 1);   // warm-up (JIT)
        var t1 = BestOf(small, n, 2);
        var t4 = BestOf(large, 4 * n, 2);
        Assert.True(t4.TotalMilliseconds < 10 * Math.Max(t1.TotalMilliseconds, 1), $"{n} tasks {t1.TotalMilliseconds:F0} ms, {4 * n} tasks {t4.TotalMilliseconds:F0} ms: not linear");
        Assert.True(t4 < TimeSpan.FromSeconds(60), $"{4 * n} tasks took {t4}");
    }

    // ---------------------------------------------------------------- hostile hives: caps, shared data cells, hostile sizes, duplicates, bidi

    private (ListSink Sink, ParseResult Result) EmitDirect(string hive, int maxEvents = TaskCacheReader.MaxEvents, int maxKeys = TaskCacheReader.MaxKeys)
    {
        var sink = new ListSink();
        var result = new ParseResult { EvidenceId = "E", Parser = "p", ParserVersion = "1" };
        using var fs = new FileStream(hive, FileMode.Open, FileAccess.Read, FileShare.Read);
        TaskCacheReader.Emit(new RawRegistry(fs), Item(hive), sink, result, default, maxEvents, maxKeys);
        return (sink, result);
    }

    private static string G(int i) => $"{{BBBBBBBB-0000-0000-0000-{i:D12}}}";

    /// <summary>Offsets (in the file) of the payload of every vk cell whose value is called <paramref name="valueName"/> (names are stored as Latin-1 or UTF-16).</summary>
    private static List<int> VkCells(byte[] f, string valueName)
    {
        var found = new List<int>();
        foreach (var name in new[] { Encoding.Latin1.GetBytes(valueName), Encoding.Unicode.GetBytes(valueName) })
            for (int i = 0x14; i + name.Length <= f.Length; i++)
                if (f.AsSpan(i, name.Length).SequenceEqual(name) && f[i - 0x14] == (byte)'v' && f[i - 0x13] == (byte)'k'
                    && BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(i - 0x12)) == name.Length)
                    found.Add(i - 0x14);
        found.Sort();
        return found;
    }

    [Fact]
    public void The_per_hive_event_cap_stops_at_exactly_the_cap_with_a_gap_that_states_the_limit()
    {
        var path = NewHive(b => { for (int i = 0; i < 3; i++) b.Task(G(i), $@"\Cap\T{i}", Actions("A", Exec("a.exe"), Exec("b.exe"), Exec("c.exe"), Exec("d.exe"), Exec("e.exe")), Dynamic(Created, null)); });
        var (all, allResult) = EmitDirect(path);
        Assert.Equal(15, all.Events.Count);
        Assert.Empty(allResult.Gaps.Where(x => x.Reason.StartsWith("limită")));

        var (sink, result) = EmitDirect(path, maxEvents: 7);   // stops inside the second task
        Assert.Equal(7, sink.Events.Count);
        var gap = Assert.Single(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.StartsWith("limită de 7 evenimente TaskCache atinsă"));
        Assert.Contains("7 evenimente", gap.Notes);
        Assert.Equal(7, result.Records);
    }

    [Fact]
    public void The_cap_also_covers_tree_entries_without_a_tasks_key()
    {
        var path = NewHive(b => { for (int i = 0; i < 5; i++) b.Task(G(i), $@"\Orphan\T{i}", null, null, tasksKey: false); });
        var (sink, result) = EmitDirect(path, maxEvents: 3);
        Assert.Equal(3, sink.Events.Count);
        Assert.Contains(result.Gaps, x => x.Reason.StartsWith("limită de 3 evenimente"));
    }

    [Fact]
    public void The_key_limit_stops_the_Tasks_walk_with_a_gap()
    {
        var path = NewHive(b => { for (int i = 0; i < 25; i++) b.Task(G(i), $@"\K\T{i}", Actions("A", Exec("a.exe")), Dynamic(Created, null)); });
        var (sink, result) = EmitDirect(path, maxKeys: 10);
        Assert.Equal(10, sink.Events.Count(e => e.Fields["ActionType"] == "Exec"));
        var gap = Assert.Single(result.Gaps, x => x.Reason.StartsWith("Tasks depășește limita de chei"));
        Assert.Contains("10 chei", gap.Notes);
        Assert.Contains(result.Gaps, x => x.Reason.StartsWith("Tree depășește limita de chei"));
    }

    [Fact]
    public void Tasks_whose_Actions_share_one_data_cell_each_yield_their_events_and_the_cap_still_bounds_them()
    {
        var path = NewHive(b => { for (int i = 0; i < 6; i++) b.Task(G(i), $@"\Share\T{i}", Actions("A", Exec("a.exe"), Exec("b.exe"), Exec("c.exe"), Exec("d.exe")), Dynamic(Created, null)); });
        var bytes = File.ReadAllBytes(path);
        var vks = VkCells(bytes, "Actions");
        Assert.Equal(6, vks.Count);
        var firstOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vks[0] + 8));
        foreach (var vk in vks) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(vk + 8), firstOffset);   // every value now points at the first one's data cell
        File.WriteAllBytes(path, bytes);

        var (sink, result) = EmitDirect(path);
        Assert.Equal(24, sink.Events.Count);
        Assert.All(sink.Events.GroupBy(e => e.Fields["TaskId"]), g => Assert.Equal(["a.exe", "b.exe", "c.exe", "d.exe"], g.Select(e => e.Fields["Command"])));
        Assert.Empty(result.Gaps.Where(x => x.Artifact == "TaskCache"));

        var (capped, cappedResult) = EmitDirect(path, maxEvents: 10);
        Assert.Equal(10, capped.Events.Count);
        Assert.Contains(cappedResult.Gaps, x => x.Reason.StartsWith("limită de 10 evenimente"));
    }

    [Fact]
    public void Two_values_on_one_data_cell_with_different_declared_sizes_are_read_as_two_different_blobs()
    {
        // review round 2: the shared-cell cache was keyed on the cell alone; a shorter declared size on the same cell is a different (shorter) blob
        var path = NewHive(b => { for (int i = 0; i < 6; i++) b.Task(G(i), $@"\Size\T{i}", Actions("A", Exec("a.exe"), Exec("b.exe"), Exec("c.exe"), Exec("d.exe")), Dynamic(Created, null)); });
        var bytes = File.ReadAllBytes(path);
        var vks = VkCells(bytes, "Actions");
        Assert.Equal(6, vks.Count);
        var firstOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vks[0] + 8));
        foreach (var vk in vks) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(vk + 8), firstOffset);
        int shortLength = Actions("A", Exec("a.exe"), Exec("b.exe")).Length;   // exactly the first two actions
        foreach (var vk in new[] { vks[0], vks[3] }) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(vk + 4), shortLength);
        File.WriteAllBytes(path, bytes);

        var (sink, result) = EmitDirect(path);
        var perTask = sink.Events.GroupBy(e => e.Fields["TaskId"]).Select(g => g.Select(e => e.Fields["Command"]).ToList()).ToList();
        Assert.Equal(6, perTask.Count);
        Assert.Equal(2, perTask.Count(c => c.SequenceEqual(["a.exe", "b.exe"])));
        Assert.Equal(4, perTask.Count(c => c.SequenceEqual(["a.exe", "b.exe", "c.exe", "d.exe"])));
        Assert.Empty(result.Gaps.Where(x => x.Artifact == "TaskCache"));
    }

    [Fact]
    public void Probe7_a_corrupt_TaskCache_value_gives_a_gap_for_that_task_only_and_the_Run_event_and_other_tasks_survive()
    {
        var path = NewHive(b =>
        {
            b.RunValue("Updater", @"C:\Tools\run.exe");
            b.Task(G(1), @"\P7\Bad", Actions("A", Exec("bad.exe")), Dynamic(Created, null));
            b.Task(G(2), @"\P7\Good", Actions("A", Exec("good.exe")), Dynamic(Created, null));
        });
        var bytes = File.ReadAllBytes(path);
        var vks = VkCells(bytes, "Actions");
        Assert.Equal(2, vks.Count);
        // the declared size of the first Actions value: far larger than the whole hive, without the inline bit (a hostile length, not an OutOfMemory trap)
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(vks[0] + 4), 0x7FFFFFF0);
        File.WriteAllBytes(path, bytes);

        var (sink, result) = Run(path);
        Assert.Contains(sink.Events, e => e.Source == "RunKey" && e.Fields["ValueName"] == "Updater");
        var tc = TaskEvents(sink);
        Assert.Equal("good.exe", Assert.Single(tc).Fields["Command"]);
        var gap = Assert.Single(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.StartsWith("cheie Tasks ilizibilă"));
        Assert.Contains("InvalidDataException", gap.Notes);
        Assert.DoesNotContain(result.Gaps, x => x.Reason.StartsWith("citirea TaskCache s-a oprit"));
    }

    [Fact]
    public void A_task_whose_data_offset_points_outside_the_hive_is_a_gap_and_the_Run_event_is_still_emitted()
    {
        var path = NewHive(b =>
        {
            b.RunValue("Updater", @"C:\Tools\run.exe");
            b.Task(G(1), @"\P7\Only", Actions("A", Exec("only.exe")), Dynamic(Created, null));
        });
        var bytes = File.ReadAllBytes(path);
        var vks = VkCells(bytes, "Actions");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(vks[0] + 8), 0x7FFFFF00);
        File.WriteAllBytes(path, bytes);
        var (sink, result) = Run(path);
        Assert.Contains(sink.Events, e => e.Source == "RunKey");
        Assert.Empty(TaskEvents(sink));   // no false "Tree entry without Tasks key" event: the Tasks key exists, only its data is unreadable
        Assert.Contains(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.StartsWith("cheie Tasks ilizibilă"));
    }

    [Fact]
    public void The_same_Tree_Id_under_two_paths_is_an_anomaly_field_and_a_gap()
    {
        const string g = "{BBBBBBBB-0000-0000-0000-0000000000AA}";
        var path = NewHive(b =>
        {
            b.Task(g, @"\Dup\First", Actions("A", Exec("x.exe")), Dynamic(Created, null));
            b.Task(g, @"\Dup\Second", null, null, tasksKey: false);
        });
        var (sink, result) = Run(path);
        var e = Assert.Single(TaskEvents(sink));
        Assert.Contains("TreeDuplicateId", e.Fields["Anomaly"]);
        Assert.Contains(@"\Dup\First", e.Fields["TreeDuplicateIdPaths"]);
        Assert.Contains(@"\Dup\Second", e.Fields["TreeDuplicateIdPaths"]);
        var gap = Assert.Single(result.Gaps, x => x.Artifact == "TaskCache" && x.Reason.StartsWith("aceeași activitate (Id) apare în mai multe locuri din Tree"));
        Assert.Contains(g, gap.Notes);
    }

    [Fact]
    public void Bidi_controls_are_removed_and_named_in_the_summary_but_kept_in_the_fields()
    {
        var path = NewHive(b => b.Task(G(9), "\\T\\gnp\u202Eexe.txt", Actions("A", Exec("c:\\x\u2066y.exe", "-a\u202Ab")), Dynamic(Created, null), uri: "\\T\\gnp\u202Eexe.txt"));
        var (sink, _) = Run(path);
        var e = Assert.Single(TaskEvents(sink));
        foreach (var ch in "\u202A\u202B\u202C\u202D\u202E\u2066\u2067\u2068\u2069") Assert.DoesNotContain(ch, e.Summary);
        Assert.Contains("U+202E", e.Summary);
        Assert.Contains('\u202E', e.Fields["TaskUri"]);
        Assert.Contains('\u2066', e.Fields["Command"]);
    }

    // ---------------------------------------------------------------- real reference station (skipped when the lab files are absent)

    private static string? Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [LabFileFact(LabRef, LabRefJson)]
    public void Real_reference_station_226_tasks_match_Get_ScheduledTask()
    {
        var sink = new ListSink();
        var result = new ParseResult { EvidenceId = "E", Parser = "p", ParserVersion = "1" };
        using (var fs = new FileStream(LabRef, FileMode.Open, FileAccess.Read, FileShare.Read))
            TaskCacheReader.Emit(new RawRegistry(fs), Item(LabRef), sink, result, default);
        var events = TaskEvents(sink).Where(e => e.Fields.GetValueOrDefault("TaskPath", "").Length > 0).ToLookup(e => e.Fields["TaskPath"], StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllBytes(LabRefJson);
        if (json.Length > 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF) json = json[3..];
        using var doc = JsonDocument.Parse(json);
        int tasks = 0, notFound = 0, actionMismatch = 0, lastRunMismatch = 0, resultMismatch = 0, resultCompared = 0, extraEvents = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            tasks++;
            var uri = Str(t, "Uri")!;
            seen.Add(uri);
            var ev = events[uri].OrderBy(e => int.Parse(e.Fields["ActionIndex"])).ToList();
            if (ev.Count == 0) { notFound++; continue; }
            var refActs = t.GetProperty("Actions").EnumerateArray().ToList();
            bool same = ev.Count == refActs.Count;
            for (int i = 0; same && i < ev.Count; i++)
            {
                var a = refActs[i]; var f = ev[i].Fields;
                bool com = Str(a, "Type") == "MSFT_TaskComHandlerAction";
                same = com
                    ? f["ActionType"] == "ComHandler" && string.Equals(f["ComClassId"], Str(a, "ClassId"), StringComparison.OrdinalIgnoreCase) && f["ComData"] == (Str(a, "Data") ?? "")
                    : f["ActionType"] == "Exec" && f["Command"] == (Str(a, "Execute") ?? "") && f["Arguments"] == (Str(a, "Arguments") ?? "") && f["WorkingDirectory"] == (Str(a, "WorkingDirectory") ?? "");
            }
            if (!same) actionMismatch++;

            var f0 = ev[0].Fields;
            var lastRun = Str(t, "LastRunTime") ?? "";
            bool neverRan = f0["LastRunNever"] == "true";
            if (neverRan ? !(lastRun.StartsWith("1999") || lastRun.Length == 0) : f0["LastRunUtc"][..19] != lastRun[..19]) lastRunMismatch++;
            var lastResult = t.GetProperty("LastTaskResult").GetInt64();
            if (!neverRan && lastResult != 267009)   // 267009 = running at capture time: the service computes it, the cache holds 0
            {
                resultCompared++;
                if (f0["LastResultRaw"] != ((uint)lastResult).ToString()) resultMismatch++;
            }
        }
        extraEvents = events.Select(g => g.Key).Count(k => !seen.Contains(k));
        Assert.Equal(226, tasks);
        Assert.Equal(0, notFound);
        Assert.Equal(0, actionMismatch);
        Assert.Equal(0, lastRunMismatch);
        Assert.Equal(0, resultMismatch);
        Assert.True(resultCompared > 50, $"{resultCompared} results compared");
        Assert.Equal(0, extraEvents);
        // the real station has no gap about unreadable Actions/DynamicInfo
        Assert.DoesNotContain(result.Gaps, x => x.Reason.StartsWith("blobul Actions") || x.Reason.StartsWith("DynamicInfo"));
    }

    [LabFileFact(LabEarlier)]
    public void Real_earlier_hive_contains_the_LA_TEST_task_with_its_two_actions()
    {
        var sink = new ListSink();
        var result = new ParseResult { EvidenceId = "E", Parser = "p", ParserVersion = "1" };
        using (var fs = new FileStream(LabEarlier, FileMode.Open, FileAccess.Read, FileShare.Read))
            TaskCacheReader.Emit(new RawRegistry(fs), Item(LabEarlier), sink, result, default);
        var ev = TaskEvents(sink).Where(e => e.Fields.GetValueOrDefault("TaskPath") == @"\LA-TEST\LA-TEST-TaskCache").OrderBy(e => e.Fields["ActionIndex"]).ToList();
        Assert.Equal(2, ev.Count);
        Assert.Equal("{1F66A3F0-954E-4322-ACCA-9AD94C300C4A}", ev[0].Fields["TaskId"]);
        Assert.Equal("LA TEST TaskCache sample", ev[0].Fields["Description"]);
        Assert.Equal("Logon", ev[0].Fields["Categories"]);
        Assert.Equal("2", ev[0].Fields["TreeIndex"]);
        Assert.Equal("true", ev[0].Fields["TreeHasSd"]);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", ev[0].Fields["Command"]);
        Assert.Equal(@"C:\Windows\System32\whoami.exe", ev[1].Fields["Command"]);
        Assert.StartsWith("2026-11-18T12:15:49.69", ev[0].Fields["CacheRegisteredUtc"]);
        Assert.StartsWith("2026-11-18T12:15:49.77", ev[0].Fields["LastRunUtc"]);
        Assert.Equal("0", ev[0].Fields["LastResultRaw"]);
    }
}
