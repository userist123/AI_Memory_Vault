using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP14a on the production path: the pipeline uses the registers, copies them into the case with their SHA-256 in custody, and reports the air-gap category.</summary>
public sealed class Wp14PipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_wp14_{Guid.NewGuid():N}");
    public Wp14PipelineTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private CaseWorkspace NewCase(CaseScope? scope = null)
    {
        var sample = Path.Combine(_dir, "sample.bin");
        File.WriteAllText(sample, "MZ not really a program");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases" + Guid.NewGuid().ToString("N")[..4]), "wp14", scope ?? TestScopes.Valid());
        InvestigationPipeline.Import(ws, [sample]);
        return ws;
    }

    private static MediaRegister Reg() { var r = new MediaRegister(); r.SetCells([["M-1", "AA001", "", "USB", "NATO SECRET", "", "", "", "", "active", ""]]); return r; }

    private static List<JsonElement> Outputs(CaseWorkspace ws) =>
        File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement.Clone()).Where(e => e.GetProperty("action").GetString() == "output.written").ToList();

    [Fact]
    public void Run_with_registers_copies_them_into_the_case_and_records_the_sha256_in_custody()
    {
        var ws = NewCase();
        var users = new UsersRegister(); users.SetCells([["Ion", @"CORP\ion", "", "NATO SECRET", "", "", "", ""]]);
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, mediaRegister: Reg(), usersRegister: users);
        var path = ws.FullPath("Analysis/media_register.json");
        Assert.True(File.Exists(path));
        Assert.Equal(Hashing.Sha256File(path), r.MediaRegisterSha256);
        Assert.Equal(Hashing.Sha256File(path), Outputs(ws).Last(e => e.GetProperty("to").GetString() == "Analysis/media_register.json").GetProperty("sha256").GetString());
        Assert.True(File.Exists(ws.FullPath("Analysis/users_register.json")));
        Assert.Contains("Registru medii: definit", r.RegisterLine);
        Assert.Contains("Registru utilizatori: definit", r.RegisterLine);
    }

    [Fact]
    public void Run_without_registers_says_nedefinit_and_never_conform()
    {
        var ws = NewCase();
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, mediaRegister: new MediaRegister(), usersRegister: new UsersRegister());
        Assert.False(File.Exists(ws.FullPath("Analysis/media_register.json")));
        Assert.Equal("", r.MediaRegisterSha256);
        Assert.Contains("Registru medii: nedefinit", r.RegisterLine);
        Assert.DoesNotContain("conform", r.RegisterLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("register.undefined") && l.Contains("media"));
    }

    [Fact]
    public void The_analysis_step_snapshots_the_registers_and_returns_the_findings_for_the_timeline()
    {
        var v = TestScopes.Valid();
        var ws = NewCase(new CaseScope { Purpose = v.Purpose, PeriodFromUtc = v.PeriodFromUtc, PeriodToUtc = v.PeriodToUtc, SystemsInScope = v.SystemsInScope, Approver = v.Approver, LegalBasis = v.LegalBasis,
            Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified });
        int n = 0;
        var events = new List<TimelineEvent> { W14.Usb("ZZ999"), Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 5, "Microsoft-Windows-WLAN-AutoConfig", ("SSID", "Hotspot")) };
        var res = Wp14Analysis.Run(ws, events, () => $"F-{++n:D4}", Reg(), null, null, "");
        Assert.Contains(res.Findings, f => f.RuleId == "MEDIA-UNREGISTERED");
        Assert.Contains(res.Findings, f => f.RuleId == "AIRGAP-WIFI-ASSOCIATED" && f.Category == "Air-gap integrity");
        Assert.Equal(64, res.MediaSha256.Length);
        Assert.Equal("", res.UsersSha256);
        Assert.Contains("Registru utilizatori: nedefinit", res.Line);
    }

    [Fact]
    public void An_unreadable_register_file_is_reported_in_the_audit_and_treated_as_not_defined_never_as_clean()
    {
        var ws = NewCase();
        int n = 0;
        var bad = Path.Combine(_dir, "media.json"); File.WriteAllText(bad, "{ not json");
        var res = Wp14Analysis.Run(ws, [], () => $"F-{++n:D4}", null, null, null, "", mediaPath: bad);
        Assert.Contains("Registru medii: nedefinit", res.Line);
        Assert.Contains(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("register.issue"));
    }

    [Fact]
    public void Findings_json_carries_the_air_gap_detail_and_other_findings_do_not()
    {
        var f = new Finding { FindingId = "F-1", RuleId = "X", Title = "t", Description = "d" };
        Assert.DoesNotContain("airGap", JsonSerializer.Serialize(f, Json.Options), StringComparison.OrdinalIgnoreCase);
        var a = W14.Go([Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 1, "x", ("SSID", "H"))], W14.Classified()).Single();
        var json = JsonSerializer.Serialize(a, Json.Options);
        Assert.Contains("\"airGap\"", json);
        Assert.Contains("\"authorized\": \"UNDEFINED\"", json);
    }

    [Fact]
    public void The_new_event_log_channels_are_collected_once_each_and_an_absent_channel_stays_an_unavailable_source()
    {
        foreach (var ch in new[] { "Microsoft-Windows-Dhcp-Client/Operational", "Microsoft-Windows-Kernel-PnP/Configuration", "Microsoft-Windows-DriverFrameworks-UserMode/Operational", "Microsoft-Windows-VHDMP-Operational" })
            Assert.Contains(ch, EventLogCollector.Channels);
        Assert.Equal(EventLogCollector.Channels.Length, EventLogCollector.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Evtx_describes_the_channels_the_air_gap_rules_read()
    {
        var caps = new LogAnalyzer.Dfir.Windows.Parsers.EvtxParser().Descriptor.Capabilities;
        Assert.NotNull(caps);
        (string Channel, int Id)[] needed =
        [
            ("Microsoft-Windows-Partition/Diagnostic", 1006), ("Security", 6416), ("Security", 4663),
            ("Microsoft-Windows-Kernel-PnP/Configuration", 400), ("Microsoft-Windows-DriverFrameworks-UserMode/Operational", 2003), ("Microsoft-Windows-DriverFrameworks-UserMode/Operational", 2100),
            ("Microsoft-Windows-DriverFrameworks-UserMode/Operational", 2102),
            ("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001), ("Microsoft-Windows-WLAN-AutoConfig/Operational", 11001),
            ("Microsoft-Windows-NetworkProfile/Operational", 10001),
            ("Microsoft-Windows-Dhcp-Client/Operational", 50036), ("Microsoft-Windows-Dhcp-Client/Operational", 50037), ("Microsoft-Windows-Dhcp-Client/Operational", 50065), ("Microsoft-Windows-Dhcp-Client/Operational", 1103),
        ];
        foreach (var (ch, id) in needed)
        {
            var c = caps!.ChannelFor(ch, id);
            Assert.True(c is not null, $"{ch} {id}: no ChannelCapability");
            Assert.NotEmpty(c!.CanProve); Assert.NotEmpty(c.CannotProve); Assert.NotEmpty(c.CorrelationSources);
        }
    }
}
