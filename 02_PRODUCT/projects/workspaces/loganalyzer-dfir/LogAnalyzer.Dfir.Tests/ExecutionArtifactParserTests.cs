using System.Buffers.Binary;
using System.Text;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>BAM, ShimCache and Amcache on synthetic hives built here (exact expected values) and on the real Amcache.</summary>
public sealed class ExecutionArtifactParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_exec_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Run1 = new(2026, 9, 19, 14, 56, 26, DateTimeKind.Utc);
    private static readonly DateTime Mod1 = new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

    public ExecutionArtifactParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path, string type) =>
        new() { EvidenceId = "EV-X", CaseId = "C", Source = type, SourceType = type, StoredPath = path };

    /// <summary>Windows 10/11 AppCompatCache value: 0x34 header, then "10ts" entries (path, FILETIME, data).</summary>
    public static byte[] ShimCacheBlob(params (string Path, DateTime Modified)[] entries)
    {
        using var ms = new MemoryStream();
        var header = new byte[0x34];
        BinaryPrimitives.WriteInt32LittleEndian(header, 0x34);
        ms.Write(header);
        foreach (var (path, modified) in entries)
        {
            var p = Encoding.Unicode.GetBytes(path);
            var body = new byte[2 + p.Length + 8 + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)p.Length);
            p.CopyTo(body, 2);
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(2 + p.Length), modified.ToFileTimeUtc());
            ms.Write("10ts"u8);
            ms.Write(new byte[4]);
            var size = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, body.Length);
            ms.Write(size);
            ms.Write(body);
        }
        return ms.ToArray();
    }

    private string SystemHive(bool withBam, byte[]? shim)
    {
        var path = Path.Combine(_dir, "SYSTEM");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        hive.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
        if (withBam)
        {
            var sid = hive.Root.CreateSubKey(@"ControlSet001\Services\bam\State\UserSettings\S-1-5-21-1-2-3-1001");
            var data = new byte[24];
            BinaryPrimitives.WriteInt64LittleEndian(data, Run1.ToFileTimeUtc());
            sid.SetValue(@"\Device\HarddiskVolume3\Users\U\Downloads\TOOL_302044\SETUP.EXE", data, RegistryValueType.Binary);
            sid.SetValue("Version", 1, RegistryValueType.Dword);
        }
        if (shim is not null)
            hive.Root.CreateSubKey(@"ControlSet001\Control\Session Manager\AppCompatCache").SetValue("AppCompatCache", shim, RegistryValueType.Binary);
        return path;
    }

    [Fact]
    public void ShimCache_decoder_returns_paths_modified_times_and_order()
    {
        var blob = ShimCacheBlob((@"C:\Users\U\Downloads\TOOL_302044\SETUP.EXE", Mod1), (@"C:\Windows\System32\msiexec.exe", Mod1.AddDays(-1)));
        var e = ShimCache.Parse(blob).ToList();
        Assert.Equal(2, e.Count);
        Assert.Equal(@"C:\Users\U\Downloads\TOOL_302044\SETUP.EXE", e[0].Path);
        Assert.Equal(Mod1.ToFileTimeUtc(), e[0].Modified);
        Assert.Equal(1, e[1].Order);
    }

    [Fact]
    public void ShimCache_decoder_stops_on_unknown_header_or_truncation()
    {
        Assert.Empty(ShimCache.Parse(new byte[0x40]));
        var blob = ShimCacheBlob((@"C:\a.exe", Mod1));
        Assert.Empty(ShimCache.Parse(blob[..(blob.Length - 6)]));
    }

    [Fact]
    public void Bam_is_execution_and_shimcache_is_presence_with_file_time()
    {
        var path = SystemHive(true, ShimCacheBlob((@"C:\Users\U\AppData\Local\Temp\x.exe", Mod1)));
        var sink = new ListSink();
        var r = new SystemHiveExecutionParser().Parse(Item(path, "system_hive"), path, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(2, r.Records);
        var bam = Assert.Single(sink.Events, e => e.Source == "BAM");
        Assert.Equal(new DateTimeOffset(Run1), bam.Time.Utc);
        Assert.Equal(@"\Users\U\Downloads\TOOL_302044\SETUP.EXE", bam.Path);
        Assert.Equal("S-1-5-21-1-2-3-1001", bam.User);
        Assert.Contains("execution", bam.TimeSemantics);
        var shim = Assert.Single(sink.Events, e => e.Source == "ShimCache");
        Assert.Equal(new DateTimeOffset(Mod1), shim.Time.Utc);
        Assert.Contains("not execution", shim.TimeSemantics);
        Assert.Empty(r.Gaps);
    }

    [Fact]
    public void Missing_bam_and_shimcache_are_reported_as_gaps_not_empty_success()
    {
        var path = SystemHive(false, null);
        var r = new SystemHiveExecutionParser().Parse(Item(path, "system_hive"), path, new ListSink(), default);
        Assert.Equal(EvidenceStatus.Empty, r.Status);
        Assert.Contains(r.Gaps, g => g.Artifact == "BAM" && g.Status == EvidenceStatus.NotAvailable);
        Assert.Contains(r.Gaps, g => g.Artifact == "ShimCache" && g.Status == EvidenceStatus.NotAvailable);
    }

    [Fact]
    public void Unknown_shimcache_format_is_partial_gap()
    {
        var path = SystemHive(false, new byte[0x80]);
        var r = new SystemHiveExecutionParser().Parse(Item(path, "system_hive"), path, new ListSink(), default);
        Assert.Contains(r.Gaps, g => g.Artifact == "ShimCache" && g.Status == EvidenceStatus.Partial);
    }

    [Fact]
    public void Amcache_entry_has_sha1_publisher_and_presence_semantics()
    {
        var path = Path.Combine(_dir, "Amcache.hve");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
        using (var hive = RegistryHive.Create(fs))
        {
            var k = hive.Root.CreateSubKey(@"Root\InventoryApplicationFile\setup.exe|abc");
            k.SetValue("LowerCaseLongPath", @"c:\users\u\downloads\tool_302044\setup.exe");
            k.SetValue("Name", "SETUP.EXE");
            k.SetValue("Publisher", "");
            k.SetValue("FileId", "0000" + new string('a', 40));
        }
        var sink = new ListSink();
        var r = new AmcacheParser().Parse(Item(path, "amcache"), path, sink, default);

        var e = Assert.Single(sink.Events);
        Assert.Equal(new string('A', 40), e.Hash);
        Assert.Equal(@"c:\users\u\downloads\tool_302044\setup.exe", e.Path);
        Assert.DoesNotContain("execution", e.TimeSemantics, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(@"Amcache.hve\Root\InventoryApplicationFile\", e.Locator);
        Assert.Contains(r.Gaps, g => g.Artifact == "Amcache transaction logs");
    }

    /// <summary>
    /// Real SYSTEM hive (reg save, 22:15:43) against two outputs taken from the live registry by reg query: BAM at 22:15:36,
    /// AppCompatCache at 22:15:43. Each BAM value and each ShimCache entry must match exactly.
    /// </summary>
    [CorpusFact("systemHive")]
    public void Real_system_hive_matches_reg_query_for_bam_and_shimcache()
    {
        var f = Corpus.File("systemHive");
        var sink = new ListSink();
        var r = new SystemHiveExecutionParser().Parse(Item(f, "system_hive"), f, sink, default);
        Assert.Equal(EvidenceStatus.Success, r.Status);

        // BAM: "    <name>    REG_BINARY    <hex>" under "...\UserSettings\<SID>".
        var expectedBam = new List<(string Sid, string Name, long FileTime)>();
        string sid = "";
        foreach (var line in File.ReadLines(Path.Combine(Corpus.Root, Corpus.S("systemHive", "bamRegQuery"))))
        {
            if (line.StartsWith("HKEY_", StringComparison.Ordinal)) { sid = line[(line.LastIndexOf('\\') + 1)..]; continue; }
            var parts = line.Trim().Split("    REG_BINARY    ");
            if (parts.Length != 2 || parts[1].Length < 16) continue;
            expectedBam.Add((sid, parts[0], BinaryPrimitives.ReadInt64LittleEndian(Convert.FromHexString(parts[1][..16]))));
        }
        Assert.True(expectedBam.Count > 50, $"{expectedBam.Count} valori BAM în reg query");
        var bam = sink.Events.Where(e => e.Source == "BAM").ToList();
        Assert.Equal(expectedBam.Count, bam.Count);
        // reg query ran 7 s before reg save; a program that ran in between legitimately has a newer time in the hive,
        // which must then fall inside that window. Anything else is a parser error.
        var savedUtc = File.GetLastWriteTimeUtc(f).AddSeconds(1);
        int exact = 0;
        var unexplained = new List<string>();
        foreach (var x in expectedBam)
        {
            var hit = bam.SingleOrDefault(e => e.User == x.Sid && e.Fields["DevicePath"] == x.Name);
            var queried = DateTime.FromFileTimeUtc(x.FileTime);
            if (hit?.Time.Raw == x.FileTime.ToString(System.Globalization.CultureInfo.InvariantCulture)) exact++;
            else if (hit?.Time.Utc is not { } t || t.UtcDateTime <= queried || t.UtcDateTime > savedUtc)
                unexplained.Add($"{x.Sid} {x.Name} reg query {queried:o} | hive {hit?.Time.Utc:o}");
        }
        Assert.True(unexplained.Count == 0, string.Join(Environment.NewLine, unexplained));
        Assert.True(exact >= expectedBam.Count - 5, $"{exact}/{expectedBam.Count} identice");
        Assert.Contains(bam, e => !e.Fields["DevicePath"].Contains('\\'));   // packaged (UWP) apps are kept

        // ShimCache: the AppCompatCache value dumped as hex by reg query, decoded the same way, must equal the hive's entries.
        var hex = File.ReadLines(Path.Combine(Corpus.Root, Corpus.S("systemHive", "shimcacheRegQuery")))
            .First(l => l.TrimStart().StartsWith("AppCompatCache ", StringComparison.Ordinal)).Trim().Split("    REG_BINARY    ")[1];
        var expectedShim = ShimCache.Parse(Convert.FromHexString(hex)).ToList();
        var shim = sink.Events.Where(e => e.Source == "ShimCache").ToList();
        Assert.True(expectedShim.Count > 100, $"{expectedShim.Count} intrări ShimCache în reg query");
        Assert.Equal(expectedShim.Select(x => x.Path), shim.Select(e => e.Path));
        Assert.Empty(r.Gaps);
    }

    [Fact]
    public void RawRegistry_reads_small_inline_and_large_values_and_reports_absence()
    {
        var path = Path.Combine(_dir, "raw.hive");
        var big = new byte[40_000];
        new Random(3).NextBytes(big);
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
        using (var hive = RegistryHive.Create(fs))
        {
            var k = hive.Root.CreateSubKey(@"A\B\C");
            k.SetValue("Big", big, RegistryValueType.Binary);
            k.SetValue("Small", new byte[] { 1, 2, 3 }, RegistryValueType.Binary);
            k.SetValue("Dword", 0x11223344, RegistryValueType.Dword);
            hive.Root.CreateSubKey(@"A\Other");
        }
        using var s = File.OpenRead(path);
        var raw = new LogAnalyzer.Dfir.IO.RawRegistry(s);
        Assert.Equal(big, raw.ReadValue(@"A\B\C", "Big"));
        Assert.Equal(new byte[] { 1, 2, 3 }, raw.ReadValue(@"a\b\c", "small"));
        Assert.Equal(new byte[] { 0x44, 0x33, 0x22, 0x11 }, raw.ReadValue(@"A\B\C", "Dword"));
        Assert.Null(raw.ReadValue(@"A\B\C", "Missing"));
        Assert.Null(raw.ReadValue(@"A\Nope", "Big"));
        Assert.Throws<InvalidDataException>(() => new LogAnalyzer.Dfir.IO.RawRegistry(new MemoryStream(new byte[4096])));
    }

    /// <summary>DiscUtils returns 12 bytes for this big-data value; RawRegistry must return exactly what reg query dumped.</summary>
    [CorpusFact("systemHive")]
    public void RawRegistry_big_data_value_is_byte_identical_to_reg_query()
    {
        var hex = File.ReadLines(Path.Combine(Corpus.Root, Corpus.S("systemHive", "shimcacheRegQuery")))
            .First(l => l.TrimStart().StartsWith("AppCompatCache ", StringComparison.Ordinal)).Trim().Split("    REG_BINARY    ")[1];
        using var s = File.OpenRead(Corpus.File("systemHive"));
        var bytes = new LogAnalyzer.Dfir.IO.RawRegistry(s).ReadValue(@"ControlSet001\Control\Session Manager\AppCompatCache", "AppCompatCache");
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 16344);
        Assert.Equal(Convert.FromHexString(hex), bytes);
    }

    [CorpusFact("amcache")]
    public void Real_amcache_is_parsed_completely()
    {
        var f = Corpus.File("amcache");
        var sink = new ListSink();
        var r = new AmcacheParser().Parse(Item(f, "amcache"), f, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.True(r.Records >= Corpus.L("amcache", "minEntries"), $"{r.Records} intrări");
        Assert.Equal(0, r.MalformedRecords);
        Assert.DoesNotContain(r.Gaps, g => g.Artifact == "Amcache transaction logs");
        Assert.True(sink.Events.Count(e => e.Hash.Length == 40) > r.Records * 9 / 10);
        Assert.All(sink.Events, e => Assert.NotNull(e.Time.Utc));
    }
}
