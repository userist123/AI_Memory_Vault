using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Research backlog #4 (H3): parsers that read a whole evidence file into memory declare <see cref="ParserDescriptor.MaxInputBytes"/>; a larger file is
/// refused before it is read, with a visible gap („fișier prea mare pentru acest parser”), never an out-of-memory crash or a silent skip.
/// </summary>
public sealed class ParserInputCapsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_caps_" + Guid.NewGuid().ToString("N"));
    public ParserInputCapsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    public static IEnumerable<object[]> WholeFileParsers() =>
    [
        [new LnkParser(), "x.lnk", "lnk"],
        [new JumpListParser(), "x.automaticDestinations-ms", "jumplist_auto"],
        [new PrefetchParser(), "X.EXE-12345678.pf", "prefetch"],
        [new UsnJournalParser(), "usn.csv", "usn_journal"],
    ];

    /// <summary>A file of the given length without writing its content (NTFS extends it lazily).</summary>
    private string Sized(string name, long length)
    {
        var p = Path.Combine(_dir, name);
        using var fs = new FileStream(p, FileMode.Create, FileAccess.Write);
        fs.SetLength(length);
        return p;
    }

    [Theory]
    [MemberData(nameof(WholeFileParsers))]
    public void Every_whole_file_parser_declares_a_cap(EvidenceParserBase parser, string _, string __) =>
        Assert.True(parser.Descriptor.MaxInputBytes > 0, parser.Descriptor.ParserId);

    [Theory]
    [MemberData(nameof(WholeFileParsers))]
    public void An_oversized_input_is_refused_with_a_gap_before_it_is_read(EvidenceParserBase parser, string name, string sourceType)
    {
        var path = Sized(name, parser.Descriptor.MaxInputBytes + 1);
        var item = new EvidenceItem { EvidenceId = "EV-BIG", CaseId = "C", Source = "s", SourceType = sourceType, StoredPath = path };

        var r = parser.Parse(item, path, new ListSink(), default);

        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains("fișier prea mare pentru acest parser", r.Error);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("fișier prea mare pentru acest parser"));
        Assert.Equal(0, r.Records);
    }

    [Fact]
    public void A_file_at_the_cap_is_still_given_to_the_parser()
    {
        var parser = new LnkParser();
        var path = Sized("small.lnk", 100);
        var item = new EvidenceItem { EvidenceId = "EV-S", CaseId = "C", Source = "s", SourceType = "lnk", StoredPath = path };
        var r = parser.Parse(item, path, new ListSink(), default);
        Assert.DoesNotContain("prea mare", r.Error);
    }

    [Fact]
    public void A_shortcut_on_a_medium_larger_than_the_cap_is_not_read()
    {
        var path = Sized("huge.lnk", LnkParser.MaxLinkFileBytes + 1);
        Assert.Null(RemovableMediaInspector.ReadLink(path));
    }

    [Fact]
    public void Evtx_chunk_inspection_streams_instead_of_loading_the_whole_log()
    {
        var path = Sized("big.evtx", 256L * 1024 * 1024);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var chunks = EvtxRepair.Inspect(path);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(chunks.Count > 4000);
        Assert.True(allocated < 32L * 1024 * 1024, $"{allocated:N0} bytes allocated for a 256 MiB log");
    }
}
