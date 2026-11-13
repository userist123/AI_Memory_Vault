using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Review items L1 (record size detection) and L4 (path index budget).</summary>
public sealed class MftParserLimitsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_mftl_" + Guid.NewGuid().ToString("N"));
    public MftParserLimitsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly long Recent = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    private (ParseResult R, List<TimelineEvent> E) Run(byte[] data, MftParser parser)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".mft"); File.WriteAllBytes(p, data);
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = p, AcquiredAtUtc = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero) };
        return (parser.Parse(item, p, sink, default), sink.Events.ToList());
    }

    private static byte[] Header(int size, int usaOffset = 0x30, int count = -1)
    {
        var h = new byte[size];
        "FILE"u8.CopyTo(h);
        h[4] = (byte)usaOffset; h[5] = (byte)(usaOffset >> 8);
        count = count < 0 ? size / 512 + 1 : count;
        h[6] = (byte)count; h[7] = (byte)(count >> 8);
        return h;
    }

    [Fact] // L1
    public void L1_record_size_comes_from_the_first_valid_header_and_checks_alignment_and_sizes()
    {
        Assert.Equal(1024, NtfsFileRecord.DetectRecordSize(Header(1024), out var o1)); Assert.Equal(0, o1);
        Assert.Equal(4096, NtfsFileRecord.DetectRecordSize(Header(4096), out _));
        // record 0 zeroed: the first valid header is at 4096 → size 4096 (offset 1024..3072 are zeros)
        var z4 = new byte[4096].Concat(Header(4096)).ToArray();
        Assert.Equal(4096, NtfsFileRecord.DetectRecordSize(z4, out var o4)); Assert.Equal(4096, o4);
        var z1 = new byte[3 * 1024].Concat(Header(1024)).ToArray();
        Assert.Equal(1024, NtfsFileRecord.DetectRecordSize(z1, out var o2)); Assert.Equal(3072, o2);
        // a 4096-record header sitting at a position that is not a multiple of 4096 is not a record boundary
        Assert.Equal(0, NtfsFileRecord.DetectRecordSize(new byte[1024].Concat(Header(4096)).ToArray(), out _));
        // bad counts: not size/512 + 1 for a power-of-two size, offset outside the first sector
        foreach (int count in new[] { 0, 1, 4, 7, 130, 65535 }) Assert.Equal(0, NtfsFileRecord.DetectRecordSize(Header(2048, count: count), out _));
        Assert.Equal(0, NtfsFileRecord.DetectRecordSize(Header(1024, usaOffset: 600), out _));
        Assert.Equal(0, NtfsFileRecord.DetectRecordSize(Header(1024, usaOffset: 8), out _));
        // a valid header beyond the first 64 positions is not searched for
        Assert.Equal(0, NtfsFileRecord.DetectRecordSize(new byte[64 * 1024].Concat(Header(1024)).ToArray(), out _));
        Assert.Equal(0, NtfsFileRecord.DetectRecordSize([], out _));
        Assert.Equal(0, NtfsFileRecord.DetectRecordSize(new byte[7], out _));
    }

    [Fact] // L4
    public void L4_path_index_budget_in_name_characters_is_a_cap_with_an_explicit_gap()
    {
        var recs = new List<(int, byte[])>();
        for (int i = 0; i < 10; i++) recs.Add((100 + i, DirRecord(1, Ref(5, 5), "d" + i, Recent)));     // 2 characters each
        recs.Add((200, FileRecord(1, Ref(100, 1), "under_first.txt", Recent, Recent)));
        recs.Add((201, FileRecord(1, Ref(109, 1), "under_last.txt", Recent, Recent)));
        var mft = Mft([.. recs]);
        // budget of 8 characters: root "." (1 character) + 3 directories fit, the other 7 do not
        var (r, ev) = Run(mft, new MftParser(maxDirectoryIndex: 1000, maxIndexNameChars: 8));
        Assert.Equal("\\d0\\under_first.txt", Assert.Single(ev, e => F(e, "FileName") == "under_first.txt").Path);
        var last = Assert.Single(ev, e => F(e, "FileName") == "under_last.txt");
        Assert.StartsWith("…\\", last.Path); Assert.Equal("false", F(last, "PathComplete"));
        var gap = Assert.Single(r.Gaps, g => g.Reason.Contains("indexului de căi"));
        Assert.Contains("caractere de nume", gap.Reason);
        // the same file with the default limits: complete paths, no such gap
        var (r2, ev2) = Run(mft, new MftParser());
        Assert.Equal("\\d9\\under_last.txt", Assert.Single(ev2, e => F(e, "FileName") == "under_last.txt").Path);
        Assert.DoesNotContain(r2.Gaps, g => g.Reason.Contains("indexului de căi"));
        // the count cap still works through the same parameter
        var (r3, _) = Run(mft, new MftParser(maxDirectoryIndex: 2, maxIndexNameChars: long.MaxValue));
        Assert.Contains(r3.Gaps, g => g.Reason.Contains("peste 2 directoare"));
    }
}
