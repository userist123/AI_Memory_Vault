using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review 2026-11-13, round 2: one damaged or planted FILE header in record 0 decided the record size for the whole $MFT (every real record lost,
/// and the gap blamed a correct boot sector). The size is now voted by every valid header in the first 64 KiB; the boot sector breaks ties only.
/// </summary>
public sealed class MftParserRecordSizeVoteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mftvote_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Acq = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    private static long Ft(int y, int m, int d) => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    private static byte[] Base() => Mft(
        (40, FileRecord(1, Ref(5, 5), "x.txt", Ft(2026, 11, 10), Ft(2026, 11, 10))),
        (41, FileRecord(1, Ref(5, 5), "gone.txt", Ft(2026, 11, 10), Ft(2026, 11, 10), flags: 0)),
        (42, FileRecord(1, Ref(5, 5), "y.txt", Ft(2026, 11, 10), Ft(2026, 11, 10))));

    private string Write(string sub, byte[] mft, byte[]? boot = null)
    {
        var d = Path.Combine(_dir, sub);
        Directory.CreateDirectory(d);
        var p = Path.Combine(d, "$MFT");
        File.WriteAllBytes(p, mft);
        if (boot is not null) File.WriteAllBytes(Path.Combine(d, "$Boot"), boot);
        return p;
    }

    private static (ParseResult R, List<TimelineEvent> E) Run(string p)
    {
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = p, AcquiredAtUtc = Acq };
        var r = new MftParser().Parse(item, p, sink, default);
        return (r, sink.Events.ToList());
    }

    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    [Theory]
    [InlineData((byte)2, false)]
    [InlineData((byte)2, true)]
    [InlineData((byte)9, false)]
    [InlineData((byte)9, true)]
    public void One_altered_header_in_record_0_does_not_change_the_size_of_every_record(byte usaCount, bool correctBootBeside)
    {
        var m = Base();
        m[6] = usaCount;   // record 0 claims 512 or 4,096 bytes; every other record is 1,024
        var (r, ev) = Run(Write($"c{usaCount}{correctBootBeside}", m, correctBootBeside ? BootSector() : null));

        var summary = ev.Single(e => F(e, "Category") == "summary");
        Assert.Equal("1024", F(summary, "RecordSize"));
        Assert.Contains(ev, e => F(e, "FileName") == "x.txt");
        Assert.Contains(ev, e => F(e, "FileName") == "y.txt");
        Assert.Contains(ev, e => F(e, "FileName") == "gone.txt");
        // The gap names the real cause (headers in conflict), never a correct boot sector as "another volume".
        Assert.Contains(r.Gaps, g => g.Reason.Contains("Antete FILE în conflict", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("nu corespunde acestui $MFT", StringComparison.Ordinal));
    }

    [Fact]
    public void A_planted_boot_sector_plus_one_planted_header_cannot_outvote_the_majority()
    {
        var m = Base();
        m[6] = 9;   // one header says 4,096
        var (r, ev) = Run(Write("planted", m, BootSector(clustersPerRecord: 0xF4)));   // and the boot sector beside says 4,096 too

        Assert.Equal("1024", F(ev.Single(e => F(e, "Category") == "summary"), "RecordSize"));
        Assert.Contains(ev, e => F(e, "FileName") == "x.txt");
        Assert.Contains(r.Gaps, g => g.Reason.Contains("Antete FILE în conflict", StringComparison.Ordinal)
                                     && g.Reason.Contains("majoritatea antetelor nu o confirmă", StringComparison.Ordinal));
    }

    [Fact]
    public void Real_corpus_has_no_conflict_and_uses_1024()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", "mft.bin");
        Assert.True(File.Exists(p), p);
        var (r, ev) = Run(p);
        Assert.Equal("1024", F(ev.Single(e => F(e, "Category") == "summary"), "RecordSize"));
        Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("conflict", StringComparison.Ordinal));
    }
}
