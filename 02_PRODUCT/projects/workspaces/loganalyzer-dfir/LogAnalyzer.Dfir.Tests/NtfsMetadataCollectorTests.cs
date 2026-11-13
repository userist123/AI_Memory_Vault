using System.Security.Cryptography;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// NtfsMetadataCollector orchestration with a fake shadow copy provider (a counter of live copies) and the oracle volume (Corpus/Ntfs/volume.raw.gz) as the "device".
/// Nothing here exercises a real shadow copy, WMI or the real device handle: that is verified on a VM by the main session.
/// </summary>
public sealed partial class NtfsMetadataCollectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_ntfsmc_" + Guid.NewGuid().ToString("N"));
    public NtfsMetadataCollectorTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Corpus(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);
    private static string BS => ((char)92).ToString();
    private const string Device = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7";
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    /// <summary>Provider with a live-copy counter shared with the collector's counter function.</summary>
    private sealed class FakeShadow(string device = Device, bool failCreate = false, bool failDelete = false, int extraAfterDelete = 0) : WindowsSearchIndexCollector.ISnapshot
    {
        public int Live { get; private set; } = 2;   // two copies already exist on the "station": they must not be touched
        public int Created { get; private set; }
        public List<string> Deleted { get; } = [];
        public const string Id = "{AAAAAAAA-1111-2222-3333-444444444444}";
        public (string Id, string DeviceObject) Create(string volumeRoot)
        {
            if (failCreate) throw new InvalidOperationException("Win32_ShadowCopy.Create a întors 5");
            Created++; Live++;
            return (Id, device);
        }
        public void Delete(string id)
        {
            if (failDelete) throw new InvalidOperationException("acces refuzat");
            Deleted.Add(id); Live--; Live += extraAfterDelete;
        }
    }

    private (CollectorOutcome O, CaseWorkspace Ws, FakeShadow Snap, List<string> Opened) Run(FakeShadow? snap = null, Func<string, Stream>? open = null, Func<NtfsVolumeReader, IEnumerable<string>>? plan = null, bool elevated = true)
    {
        snap ??= new FakeShadow();
        var opened = new List<string>();
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_" + Guid.NewGuid().ToString("N")), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var c = new NtfsMetadataCollector(snap, () => snap.Live, d => { opened.Add(d); return open is null ? NtfsVolumeReaderTests.OpenImage() : open(d); }, @"C:\",
                                          () => new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero), plan ?? (_ => [BS + "many", BS + "docs", BS + "absent"]));
        var o = c.Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default);
        return (o, ws, snap, opened);
    }

    private static string Stored(CaseWorkspace ws, EvidenceItem e) => Path.Combine(ws.Root, e.StoredPath);

    [Fact]
    public void The_collector_writes_mft_boot_i30_and_manifest_identical_to_the_oracle_and_deletes_the_copy()
    {
        var (o, ws, snap, opened) = Run();
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Errors);
        Assert.Equal(1, snap.Created);
        Assert.Equal([FakeShadow.Id], snap.Deleted);
        Assert.Equal([Device], opened);                                     // only the device of the copy it created
        Assert.Equal(2, snap.Live);                                         // count before = count after

        var mft = Assert.Single(o.Evidence, e => e.SourceType == MftParser.SourceTypeName);
        var expectedMft = File.ReadAllBytes(Corpus("mft.bin"));
        Assert.Equal(Sha(expectedMft), mft.Sha256, ignoreCase: true);
        Assert.True(expectedMft.AsSpan().SequenceEqual(File.ReadAllBytes(Stored(ws, mft))));
        var boot = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.BootSourceType);
        Assert.Equal(512, boot.Size);
        Assert.True(File.ReadAllBytes(Corpus("boot.bin")).AsSpan().SequenceEqual(File.ReadAllBytes(Stored(ws, boot))));
        var i30s = o.Evidence.Where(e => e.SourceType == I30Parser.SourceTypeName).ToList();
        Assert.Equal(2, i30s.Count);                                        // \many and \docs; \absent is not copied
        var many = i30s.Single(e => e.OriginalPath.EndsWith(@"\many", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.ReadAllBytes(Corpus("many_i30.bin")).AsSpan().SequenceEqual(File.ReadAllBytes(Stored(ws, many))));
        Assert.All(o.Evidence, e => Assert.Equal(Path.GetDirectoryName(Stored(ws, mft)), Path.GetDirectoryName(Stored(ws, e))));   // one folder
        Assert.Equal("$MFT", Path.GetFileName(Stored(ws, mft)));
        Assert.Equal("$Boot", Path.GetFileName(Stored(ws, boot)));
    }

    [Fact]
    public void The_manifest_has_the_shas_the_copy_id_utc_time_and_time_zone()
    {
        var (o, ws, _, _) = Run();
        var man = Assert.Single(o.Evidence, e => e.SourceType == NtfsMetadataCollector.ManifestSourceType);
        using var doc = JsonDocument.Parse(File.ReadAllText(Stored(ws, man)));
        var r = doc.RootElement;
        Assert.Equal(FakeShadow.Id, r.GetProperty("shadowId").GetString());
        Assert.Equal("2026-11-14T10:00:00.000Z", r.GetProperty("shadowCreatedUtc").GetString());
        Assert.Equal(TimeZoneInfo.Local.Id, r.GetProperty("stationTimeZone").GetString());
        Assert.True(r.GetProperty("shadowDeleted").GetBoolean());
        Assert.Equal(2, r.GetProperty("shadowCopiesBefore").GetInt32());
        Assert.Equal(2, r.GetProperty("shadowCopiesAfter").GetInt32());
        var mftSha = r.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "$MFT").GetProperty("sha256").GetString();
        Assert.Equal(Sha(File.ReadAllBytes(Corpus("mft.bin"))), mftSha, ignoreCase: true);
        var dirs = r.GetProperty("directories").EnumerateArray().ToList();
        Assert.Contains(dirs, d => d.GetProperty("path").GetString() == BS + "many" && d.GetProperty("sha256").GetString() == Sha(File.ReadAllBytes(Corpus("many_i30.bin"))));
    }

    [Fact]
    public void The_collected_folder_is_read_by_the_parsers_with_mft_and_boot_found_beside_the_i30()
    {
        var (o, ws, _, _) = Run();
        var many = o.Evidence.Single(e => e.SourceType == I30Parser.SourceTypeName && e.OriginalPath.EndsWith(@"\many", StringComparison.OrdinalIgnoreCase));
        var sink = new ListSink();
        var r = new I30Parser().Parse(many, Stored(ws, many), sink, default);
        Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("Nu există un $MFT alături"));
        Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("Sectorul de boot"));
        Assert.Contains(sink.Events, e => e.Fields.TryGetValue("Category", out var c) && c == "summary" && e.Summary.Contains("verificare $MFT: passed", StringComparison.Ordinal));
        var mft = o.Evidence.Single(e => e.SourceType == MftParser.SourceTypeName);
        var sink2 = new ListSink();
        var r2 = new MftParser().Parse(mft, Stored(ws, mft), sink2, default);
        Assert.NotEmpty(sink2.Events);
        Assert.DoesNotContain(r2.Gaps, g => g.Reason.Contains("sectorul de boot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void T4_an_exception_in_the_read_still_deletes_the_copy_and_leaves_no_partial_file()
    {
        var img = NtfsVolumeReaderTests.ImageBytes;
        var (o, ws, snap, _) = Run(open: _ => new FailingStream(img, 0x154AL * 4096 + 100_000));
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Contains(o.Errors, e => e.Contains("I/O") || e.Contains("scurtă"));
        Assert.Equal([FakeShadow.Id], snap.Deleted);
        Assert.Equal(2, snap.Live);
        Assert.Empty(o.Evidence);
        Assert.Empty(Directory.GetFiles(ws.RawDir("NtfsMetadata")));        // no truncated $MFT, no orphan $Boot
    }

    [Fact]
    public void T4_an_unexpected_exception_type_from_the_opener_propagates_but_the_copy_is_still_deleted()
    {
        var snap = new FakeShadow();
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(snap, open: _ => throw new ArgumentOutOfRangeException("x")));
        Assert.Equal([FakeShadow.Id], snap.Deleted);
        Assert.Equal(2, snap.Live);
    }

    [Fact]
    public void T4_cancellation_in_the_middle_still_deletes_the_copy()
    {
        var snap = new FakeShadow();
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_c"), new CaseInfo { CaseId = "C-NTFS", Name = "ntfs", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        using var cts = new CancellationTokenSource();
        var c = new NtfsMetadataCollector(snap, () => snap.Live, _ => NtfsVolumeReaderTests.OpenImage(), @"C:\", null, r => CancelAfterFirst(cts));
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => c.Collect(new CollectorContext { Case = ws, IsElevated = true }, cts.Token));
        Assert.Equal([FakeShadow.Id], snap.Deleted);
        Assert.Equal(2, snap.Live);
    }
    private static IEnumerable<string> CancelAfterFirst(CancellationTokenSource _) { yield return BS + "many"; }

    [Fact]
    public void T2_a_device_path_that_is_not_a_shadow_copy_device_is_never_opened()
    {
        foreach (var bad in new[] { @"C:\Windows", @"\\.\C:", @"\\?\GLOBALROOT\Device\HarddiskVolume3", Device + @"\$MFT", Device + @"\..\x", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7 " })
        {
            var (o, _, snap, opened) = Run(new FakeShadow(bad));
            Assert.Empty(opened);
            Assert.Equal(EvidenceStatus.Failed, o.Status);
            Assert.Equal([FakeShadow.Id], snap.Deleted);                   // still deleted
            Assert.Equal(2, snap.Live);
        }
    }

    [Fact]
    public void T2_only_the_exact_device_form_is_accepted()
    {
        Assert.True(NtfsMetadataCollector.IsShadowDevice(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1"));
        Assert.True(NtfsMetadataCollector.IsShadowDevice(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy123"));
        Assert.False(NtfsMetadataCollector.IsShadowDevice(null));
        Assert.False(NtfsMetadataCollector.IsShadowDevice(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\"));
        Assert.False(NtfsMetadataCollector.IsShadowDevice("\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy1\n"));
    }

    [Fact]
    public void Without_elevation_nothing_is_created_and_the_gap_message_is_the_standard_one()
    {
        var (o, _, snap, opened) = Run(elevated: false);
        Assert.Equal(EvidenceStatus.NotAvailable, o.Status);
        Assert.Contains("necesită drepturi de administrator", Assert.Single(o.Errors));
        Assert.Equal(0, snap.Created);
        Assert.Empty(opened);
    }

    [Fact]
    public void When_the_copy_cannot_be_created_the_outcome_is_failed_and_nothing_is_deleted_or_opened()
    {
        var (o, _, snap, opened) = Run(new FakeShadow(failCreate: true));
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Contains(o.Errors, e => e.Contains("nu a putut fi creată"));
        Assert.Empty(snap.Deleted); Assert.Empty(opened);
        Assert.Equal(2, snap.Live);
    }

    [Fact]
    public void T4_a_failed_delete_is_reported_with_the_manual_command_and_the_count_check_flags_it()
    {
        var (o, _, snap, _) = Run(new FakeShadow(failDelete: true));
        Assert.Contains(o.Errors, e => e.Contains("vssadmin delete shadows"));
        Assert.Contains(o.Errors, e => e.Contains("numărul copiilor de umbră diferă") && e.Contains("2 înainte, 3 după"));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Equal(3, snap.Live);
    }

    [Fact]
    public void T4_a_changed_shadow_copy_count_is_an_error_even_when_the_delete_succeeded()
    {
        var (o, _, _, _) = Run(new FakeShadow(extraAfterDelete: 1));
        Assert.Contains(o.Errors, e => e.Contains("numărul copiilor de umbră diferă"));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
    }

    [Fact]
    public void The_directory_cap_stops_at_64_and_reports_the_rest()
    {
        var (o, _, _, _) = Run(plan: _ => Enumerable.Repeat(BS + "many", 70));
        Assert.Equal(64, o.Evidence.Count(e => e.SourceType == I30Parser.SourceTypeName));
        Assert.Contains(o.Errors, e => e.Contains("plafonul de 64 directoare") && e.Contains("6 directoare"));
        Assert.Equal(EvidenceStatus.Partial, o.Status);
    }

    [Fact]
    public void The_default_plan_lists_the_fixed_directories_then_each_profile_subfolder_without_service_profiles()
    {
        using var vol = NtfsVolumeReaderTests.OpenImage();
        var plan = NtfsMetadataCollector.PlanDirectories(LogAnalyzer.Dfir.FileSystem.Ntfs.NtfsVolumeReader.Open(vol)).ToList();
        Assert.Equal([@"\Windows\Temp", @"\ProgramData", @"\Users\Public"], plan);   // the corpus has no \Users
    }

    [Fact]
    public void The_collector_is_registered_in_collect_everything_with_the_same_elevation_rule_as_the_search_index_collector()
    {
        var c = Assert.Single(InvestigationPipeline.AllCollectors, x => x is NtfsMetadataCollector);
        Assert.True(c.RequiresElevation);
        Assert.Equal(new WindowsSearchIndexCollector().Profiles, c.Profiles);
        Assert.NotNull(InvestigationPipeline.AuxiliaryNote(NtfsMetadataCollector.BootSourceType));
    }

    /// <summary>A volume that throws IOException once reading passes <paramref name="failAt"/>.</summary>
    private sealed class FailingStream(byte[] data, long failAt) : Stream
    {
        private long _pos;
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => _pos = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos + count > failAt && _pos >= failAt - 1) throw new IOException("eroare de I/O simulată");
            int n = (int)Math.Min(Math.Min(count, data.Length - _pos), failAt - _pos);
            if (n <= 0) throw new IOException("eroare de I/O simulată");
            Array.Copy(data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => _pos = origin == SeekOrigin.Begin ? offset : _pos + offset;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
