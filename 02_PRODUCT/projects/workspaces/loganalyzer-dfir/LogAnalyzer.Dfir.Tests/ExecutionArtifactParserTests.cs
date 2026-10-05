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
