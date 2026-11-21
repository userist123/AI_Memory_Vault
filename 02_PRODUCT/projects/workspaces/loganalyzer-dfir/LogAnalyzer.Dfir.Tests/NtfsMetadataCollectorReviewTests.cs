using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Review of S3a (A2 to A6) at the collector: a hostile boot sector, cancellation, a second collection in the same case, valid data length, absent directories, hostile profile names.</summary>
public sealed partial class NtfsMetadataCollectorTests
{
    private static IEnumerable<string> AllFiles(CaseWorkspace ws) => Directory.EnumerateFiles(ws.RawDir("NtfsMetadata"), "*", SearchOption.AllDirectories);

    private CaseWorkspace NewCase(string name) => CaseWorkspace.Create(Path.Combine(_dir, name), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });

    private static readonly Func<DateTimeOffset> Clock = () => new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    // ---------------- A2 ----------------

    [Theory]
    [InlineData(ulong.MaxValue)]
    [InlineData(1UL << 63)]
    [InlineData(1UL << 60)]
    public void A2_a_boot_sector_with_an_impossible_total_fails_this_collector_only_and_leaves_nothing(ulong totalSectors)
    {
        var img = NtfsVolumeReaderTests.ImageBytes.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(img.AsSpan(0x28), totalSectors);
        var (o, ws, snap, _) = Run(open: _ => new MemoryStream(img));   // must not throw
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.NotEmpty(o.Errors);
        Assert.Empty(o.Evidence);
        Assert.Empty(AllFiles(ws));
        Assert.Equal([FakeShadow.Id], snap.Deleted);
        Assert.Equal(2, snap.Live);
    }

    // ---------------- A4 ----------------

    [Fact]
    public void A4_cancellation_after_the_first_directory_deletes_every_file_this_run_wrote()
    {
        var snap = new FakeShadow();
        var ws = NewCase("case_a4");
        using var cts = new CancellationTokenSource();
        IEnumerable<string> Plan() { yield return BS + "many"; cts.Cancel(); yield return BS + "docs"; }
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => NtfsVolumeReaderTests.OpenImage(), @"C:\", Clock, _ => Plan());
        Assert.ThrowsAny<OperationCanceledException>(() => c.Collect(new CollectorContext { Case = ws, IsElevated = true }, cts.Token));
        Assert.Empty(AllFiles(ws));
        Assert.Equal([FakeShadow.Id], snap.Deleted);
    }

    [Fact]
    public void A4_cancellation_before_the_first_byte_leaves_no_boot_and_no_empty_mft()
    {
        var snap = new FakeShadow();
        var ws = NewCase("case_a4b");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => NtfsVolumeReaderTests.OpenImage(), @"C:\", Clock, _ => [BS + "many"]);
        Assert.ThrowsAny<OperationCanceledException>(() => c.Collect(new CollectorContext { Case = ws, IsElevated = true }, cts.Token));
        Assert.Empty(AllFiles(ws));
    }

    // ---------------- A5 ----------------

    [Fact]
    public void A5_a_second_collection_in_the_same_case_succeeds_in_its_own_folder()
    {
        var ws = NewCase("case_a5");
        CollectorOutcome Once()
        {
            var snap = new FakeShadow();
            var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => NtfsVolumeReaderTests.OpenImage(), @"C:\", Clock, _ => [BS + "many"]);
            return c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        }
        var a = Once(); var b = Once();
        Assert.Equal(EvidenceStatus.Success, a.Status);
        Assert.Equal(EvidenceStatus.Success, b.Status);
        string Folder(CollectorOutcome o) => Path.GetFileName(Path.GetDirectoryName(Path.Combine(ws.Root, o.Evidence.Single(e => e.SourceType == MftParser.SourceTypeName).StoredPath)))!;
        Assert.Matches(new Regex(@"^\d{8}T\d{6}Z_[0-9a-f]{8}$"), Folder(a));
        Assert.StartsWith("20261114T100000Z_", Folder(a));
        Assert.NotEqual(Folder(a), Folder(b));
        // the parsers still find $Boot and $MFT beside the $I30 of each run
        foreach (var o in new[] { a, b })
        {
            var many = o.Evidence.Single(e => e.SourceType == I30Parser.SourceTypeName);
            var sink = new ListSink();
            var r = new I30Parser().Parse(many, Path.Combine(ws.Root, many.StoredPath), sink, default);
            Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("Nu există un $MFT alături"));
            Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("Sectorul de boot"));
        }
    }

    /// <summary>Records how many run folders exist when the shadow copy is created.</summary>
    private sealed class ObservingShadow(string raw) : WindowsSearchIndexCollector.ISnapshot
    {
        public int FoldersAtCreate = -1;
        public (string Id, string DeviceObject) Create(string volumeRoot) { FoldersAtCreate = Directory.GetDirectories(raw).Length; return (FakeShadow.Id, Device); }
        public void Delete(string id) { }
    }

    [Fact]
    public void A5_the_run_folder_is_chosen_before_the_shadow_copy_is_created_and_is_removed_when_nothing_was_written()
    {
        var ws = NewCase("case_a5b");
        var snap = new ObservingShadow(ws.RawDir("NtfsMetadata"));
        var c = new NtfsMetadataCollector(snap, () => 0, _ => throw new IOException("nu se poate deschide"), @"C:\", Clock, _ => []);
        var o = c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        Assert.Equal(1, snap.FoldersAtCreate);
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Empty(Directory.GetDirectories(ws.RawDir("NtfsMetadata")));   // no empty leftover folder
    }

    // ---------------- A3 / absent directories in the manifest ----------------

    private (CollectorOutcome O, CaseWorkspace Ws, JsonElement Manifest) RunSynthetic(byte[] volume, params string[] plan)
    {
        var snap = new FakeShadow();
        var ws = NewCase("case_" + Guid.NewGuid().ToString("N"));
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => new MemoryStream(volume), @"C:\", Clock, _ => plan);
        var o = c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        var man = o.Evidence.Single(e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        return (o, ws, JsonDocument.Parse(File.ReadAllText(Path.Combine(ws.Root, man.StoredPath))).RootElement.Clone());
    }

    [Fact]
    public void A3_zeros_after_the_valid_data_length_of_an_index_are_listed_in_the_manifest_as_a_gap()
    {
        var vol = NtfsVolumeReaderTests.DirVolume((6, "d", 5, 5, NtfsVolumeReaderTests.IndexAllocForTests(0, [0x11, 0x01, 0x06, 0x00], 4096, vdl: 100)));
        var (o, _, m) = RunSynthetic(vol, BS + "d");
        var d = m.GetProperty("directories").EnumerateArray().Single();
        Assert.Equal(100, d.GetProperty("validDataLength").GetInt64());
        Assert.Equal(4096, d.GetProperty("size").GetInt64());
        var gap = Assert.Single(m.GetProperty("gaps").EnumerateArray()).GetString()!;
        Assert.Contains(BS + "d", gap); Assert.Contains("100", gap);
        Assert.Equal(EvidenceStatus.Partial, o.Status);
    }

    [Fact]
    public void A3_directories_that_were_not_found_are_listed_in_the_manifest()
    {
        var (_, _, man) = Run2();
        var nf = man.GetProperty("directoriesNotFound").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal([BS + "absent"], nf);
        Assert.DoesNotContain(man.GetProperty("directories").EnumerateArray(), d => d.GetProperty("path").GetString() == BS + "absent");   // the cap logic is untouched
    }

    private (CollectorOutcome O, CaseWorkspace Ws, JsonElement Manifest) Run2()
    {
        var (o, ws, _, _) = Run();
        var man = o.Evidence.Single(e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        return (o, ws, JsonDocument.Parse(File.ReadAllText(Path.Combine(ws.Root, man.StoredPath))).RootElement.Clone());
    }

    // ---------------- A6 ----------------

    [Fact]
    public void A6_profile_names_with_separators_or_control_characters_are_skipped_and_reported()
    {
        var vol = NtfsVolumeReaderTests.DirVolume((6, "Users", 5, 5, null), (1, "ok", 6, 1, null), (7, "bad" + BS + "name", 6, 1, null), (2, "ctl" + (char)1 + "x", 6, 1, null));
        var reader = NtfsVolumeReader.Open(new MemoryStream(vol));
        var skipped = new List<string>();
        var plan = NtfsMetadataCollector.PlanDirectories(reader, skipped).ToList();
        Assert.Contains(BS + "Users" + BS + "ok" + BS + "Desktop", plan);
        Assert.DoesNotContain(plan, p => p.Contains("bad") || p.Contains("ctl"));
        Assert.Equal(2, skipped.Count);
        Assert.Contains(skipped, s => s.Contains("bad"));
    }

    [Fact]
    public void A6_the_skipped_profiles_are_in_the_manifest_when_the_default_plan_is_used()
    {
        var vol = NtfsVolumeReaderTests.DirVolume((6, "Users", 5, 5, null), (1, "ok", 6, 1, null), (7, "bad" + BS + "name", 6, 1, null));
        var snap = new FakeShadow();
        var ws = NewCase("case_a6");
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => new MemoryStream(vol), @"C:\", Clock);   // default plan
        var o = c.Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        var man = o.Evidence.Single(e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        var r = JsonDocument.Parse(File.ReadAllText(Path.Combine(ws.Root, man.StoredPath))).RootElement;
        var sk = r.GetProperty("profilesSkipped").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Single(sk);
        Assert.Contains("bad", sk[0]);
    }
}
