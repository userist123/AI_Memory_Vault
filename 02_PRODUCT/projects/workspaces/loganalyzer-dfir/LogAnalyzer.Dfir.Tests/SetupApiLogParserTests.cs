using System.Text;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>setupapi.dev.log (research 2026-11-07, plan P2.3): the first installation of each device, in local time converted to UTC. Synthetic devices only.</summary>
public sealed class SetupApiLogParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_setupapi_" + Guid.NewGuid().ToString("N"));
    public SetupApiLogParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private const string Log = """
        [Device Install Log]
             OS Version = 10.0.26100
        [BeginLog]

        [Boot Session: 2026/10/17 09:00:00.000]

        >>>  [Device Install (Hardware initiated) - USBSTOR\Disk&Ven_TestVen&Prod_TestStick&Rev_1.00\4C5300TEST01&0]
        >>>  Section start 2026/11/06 15:15:01.937
             dvi: {Build Driver List} 15:15:01.950
        <<<  Section end 2026/11/06 15:15:03.111
        <<<  [Exit status: SUCCESS]

        >>>  [Device Install (DiInstallDevice) - USBSTOR\Disk&Ven_TestVen&Prod_TestStick&Rev_1.00\4C5300TEST01&0]
        >>>  Section start 2026/11/06 15:15:02.000
        <<<  [Exit status: SUCCESS]

        >>>  [SetupCopyOEMInf - C:\Windows\INF\oem12.inf]
        >>>  Section start 2026/11/06 15:20:00.000
        <<<  [Exit status: SUCCESS]

        >>>  [Device Install (Hardware initiated) - USB\VID_04E8&PID_6860\R58MTEST0001]
        >>>  Section start 2026/11/07 10:14:38.333
        <<<  [Exit status: FAILURE(0xE0000203)]

        >>>  [Device Install (Hardware initiated) - USB\VID_046D&PID_C31C\6&1a2b3c4d&0&2]
        >>>  Section start 2026/11/07 10:20:00.000
        """;

    private (List<TimelineEvent> Events, ParseResult Result) Parse(string text, string tz = "GTB Standard Time")
    {
        var p = Path.Combine(_dir, "setupapi.dev.log");
        File.WriteAllText(p, text, new UTF8Encoding(false));
        var sink = new ListSink();
        var r = new SetupApiLogParser().Parse(new EvidenceItem { EvidenceId = "EV-S", CaseId = "C", Source = "SetupAPI", SourceType = SetupApiLogParser.SourceTypeName, StoredPath = p, Timezone = tz }, p, sink, default);
        return (sink.Events, r);
    }

    [Fact]
    public void Only_hardware_initiated_installs_become_events_with_UTC_time_serial_and_result()
    {
        var (events, r) = Parse(Log);
        Assert.Equal(3, r.Records);
        var stick = events[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 13, 15, 1, 937, TimeSpan.Zero), stick.Time.Utc);
        Assert.Contains("ora locală a stației (GTB Standard Time)", stick.Time.ConversionMethod);
        Assert.Equal("4C5300TEST01", stick.Fields["Serial"]);
        Assert.Equal("suport de stocare USB", stick.Fields["DeviceKind"]);
        Assert.Equal("SUCCESS", stick.Fields["ExitStatus"]);
        Assert.StartsWith("Prima instalare a unui suport de stocare USB", stick.Summary);

        Assert.Equal("R58MTEST0001", events[1].Fields["Serial"]);
        Assert.Equal("FAILURE(0xE0000203)", events[1].Fields["ExitStatus"]);
        // the last section has no exit line (log cut): kept, with no serial, since Windows made the instance id
        Assert.False(events[2].Fields.ContainsKey("Serial"));
        Assert.Equal("", events[2].Fields["ExitStatus"]);
    }

    [Fact]
    public void Serials_are_taken_only_from_ids_that_carry_one()
    {
        Assert.Equal("4C5300TEST01", SetupApiLogParser.SerialOf(@"USBSTOR\Disk&Ven_A&Prod_B&Rev_1.00\4C5300TEST01&0"));
        Assert.Equal("4C5300TEST01", SetupApiLogParser.SerialOf(@"SWD\WPDBUSENUM\_??_USBSTOR#Disk&Ven_A&Prod_B&Rev_1.00#4C5300TEST01&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}"));
        Assert.Equal("", SetupApiLogParser.SerialOf(@"USB\VID_046D&PID_C31C\6&1a2b3c4d&0&2"));
        Assert.Equal("", SetupApiLogParser.SerialOf(@"PCI\VEN_8086&DEV_15BB\3&11583659&0&FE"));
    }

    [Fact]
    public void A_bad_time_is_unknown_and_counted_and_an_overlong_line_is_skipped()
    {
        var (events, r) = Parse(">>>  [Device Install (Hardware initiated) - USB\\VID_0001&PID_0002\\SER1]\n>>>  Section start 2026/13/45 99:00:00\n<<<  [Exit status: SUCCESS]\n" +
                                new string('x', SetupApiLogParser.MaxLineChars + 10) + "\n");
        var e = Assert.Single(events);
        Assert.Null(e.Time.Utc);
        Assert.Equal(2, r.MalformedRecords);
    }
}
