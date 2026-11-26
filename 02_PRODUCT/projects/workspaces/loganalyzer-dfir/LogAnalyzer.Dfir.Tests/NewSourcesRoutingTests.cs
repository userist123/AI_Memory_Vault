using System.Text;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The sources added on 2026-11-07/08 reach their parser through the pipeline's own selection (content fingerprint + declared type), not only when a test
/// calls the parser directly. The native USN export once began with "Exported by" and was refused as a format mismatch: it would never have been parsed.
/// </summary>
public sealed class NewSourcesRoutingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_route_" + Guid.NewGuid().ToString("N"));
    public NewSourcesRoutingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, data); return p; }

    private static EvidenceItem Item(string path, string type) =>
        new() { EvidenceId = "EV-R", CaseId = "C", Source = "test", SourceType = type, StoredPath = path, Timezone = "GTB Standard Time" };

    private static string Selected(string path, string type)
    {
        var sel = WindowsParsers.Registry.Select(Item(path, type), path);
        Assert.True(sel.Parsers.Count > 0, $"no parser selected for {type}: {sel.Problem}");
        return string.Join(",", sel.Parsers.Select(p => p.Descriptor.ParserId));
    }

    public static byte[] CustomDestinations(params byte[][] links)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(2)); b.AddRange(BitConverter.GetBytes(1)); b.AddRange(BitConverter.GetBytes(0)); b.AddRange(BitConverter.GetBytes(2));
        foreach (var l in links) b.AddRange(l);
        b.AddRange(BitConverter.GetBytes(0xBABFFBABu));
        return [.. b];
    }

    [Fact]
    public void The_native_USN_export_is_recognised_and_parsed_by_the_USN_parser()
    {
        var sw = new StringWriter();
        UsnJournalNative.WriteCsv(sw, new UsnJournalInfo(1, 0, 100), [], TimeZoneInfo.Utc, "volumul NTFS, seria 1A2B-3C4D");
        var path = Write("usn_E.csv", new UTF8Encoding(false).GetBytes(sw.ToString()));
        Assert.StartsWith("USN Journal ID", File.ReadAllText(path));
        Assert.Equal("UsnJournalParser", Selected(path, "usn_journal"));
    }

    [Fact]
    public void The_device_installation_log_is_recognised_and_parsed()
    {
        var path = Write("setupapi.dev.log", new UTF8Encoding(false).GetBytes("[Device Install Log]\r\n     OS Version = 10.0.26100\r\n[BeginLog]\r\n"));
        Assert.Equal("SetupApiLogParser", Selected(path, SetupApiLogParser.SourceTypeName));
    }

    [Fact]
    public void A_custom_Jump_List_is_recognised_and_every_link_in_it_becomes_a_row_with_its_volume_serial()
    {
        var path = Write("5f7b5f1e01b83767.customDestinations-ms", CustomDestinations(
            LnkParserTests.BuildLnk(@"E:\Raport secret.docx", "", @"E:\", "LAB-WS02", 0x1A2B3C4D, 2),   // the test builder writes an ANSI path
            LnkParserTests.BuildLnk(@"C:\Windows\notepad.exe", "/A", @"C:\Windows", "LAB-WS02")));
        Assert.Equal("CustomJumpListParser", Selected(path, CustomJumpListParser.SourceTypeName));

        var sink = new ListSink();
        var r = new CustomJumpListParser().Parse(Item(path, CustomJumpListParser.SourceTypeName), path, sink, default);
        Assert.Equal(2, r.Records);
        Assert.Equal(0, r.MalformedRecords);
        var doc = Assert.Single(sink.Events, e => e.Path.EndsWith("Raport secret.docx"));
        Assert.Equal("1A2B3C4D", doc.Fields["VolumeSerial"]);
        Assert.Equal("custom", doc.Fields["JumpListKind"]);
        Assert.Equal(2, CustomJumpListParser.LinkOffsets(File.ReadAllBytes(path)).Count);
    }

    [Fact]
    public void A_file_that_is_not_a_custom_Jump_List_is_refused_before_parsing()
    {
        var path = Write("x.customDestinations-ms", [1, 0, 0, 0, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9]);
        Assert.Empty(WindowsParsers.Registry.Select(Item(path, CustomJumpListParser.SourceTypeName), path).Parsers);
    }
}
