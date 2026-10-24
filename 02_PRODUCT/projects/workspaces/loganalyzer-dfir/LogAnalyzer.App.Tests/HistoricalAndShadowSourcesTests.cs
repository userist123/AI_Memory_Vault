using System.IO;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>E1: archived logs and Windows.old found and imported with their origin; E10: shadow copies listed read-only (admin only) and imported with the copy in custody.</summary>
public sealed class HistoricalAndShadowSourcesTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    [Theory]
    [InlineData("Archive-Security-2026-10-01-08-00-00-123.evtx", "Security")]
    [InlineData("Archive-Microsoft-Windows-Partition%4Diagnostic-2026-01-02-03-04-05-006.evtx", "Microsoft-Windows-Partition/Diagnostic")]
    [InlineData("Microsoft-Windows-Kernel-PnP%4Configuration.evtx", "Microsoft-Windows-Kernel-PnP/Configuration")]
    [InlineData("System.evtx", "System")]
    public void Channel_names_come_from_live_and_archived_file_names(string file, string channel) =>
        Assert.Equal(channel, HistoricalSources.ChannelFromFileName(file));

    [Fact]
    public void Discover_finds_archived_logs_and_the_logs_hives_and_setupapi_of_windows_old()
    {
        _k.File(@"img\Windows\System32\winevt\Logs\Archive-Security-2026-01-02-03-04-05-678.evtx", "x");
        _k.File(@"img\Windows\System32\winevt\Logs\Security.evtx", "x");                         // the live log is not "historical"
        _k.File(@"img\Windows.old\Windows\System32\winevt\Logs\System.evtx", "x");
        _k.File(@"img\Windows.old\Windows\System32\config\SYSTEM", "x");
        _k.File(@"img\Windows.old\Windows\System32\config\SOFTWARE", "x");
        _k.File(@"img\Windows.old\Windows\INF\setupapi.dev.log", "x");
        _k.File(@"img\Windows.old\Users\u1\NTUSER.DAT", "x");
        var found = HistoricalSources.Discover(Path.Combine(_k.Dir, "img"));
        Assert.Equal(6, found.Count);
        Assert.Contains(found, s => s.Path.EndsWith("Archive-Security-2026-01-02-03-04-05-678.evtx") && s.Kind == "evtx" && s.Origin == "jurnal arhivat");
        Assert.Contains(found, s => s.Path.EndsWith(@"config\SYSTEM") && s.Kind == "system_hive" && s.Origin.Contains("Windows.old"));
        Assert.Contains(found, s => s.Path.EndsWith(@"config\SOFTWARE") && s.Kind == "software_hive");
        Assert.Contains(found, s => s.Path.EndsWith("NTUSER.DAT") && s.Kind == "ntuser_hive");
        Assert.Contains(found, s => s.Path.EndsWith("setupapi.dev.log"));
        Assert.DoesNotContain(found, s => s.Path.EndsWith(@"Logs\Security.evtx"));
    }

    [Fact]
    public void Importing_an_archived_log_names_its_channel_and_records_the_origin()
    {
        var f = _k.File(@"src\Archive-Security-2026-01-02-03-04-05-678.evtx", "not a real log");
        var ws = _k.Case();
        var item = Assert.Single(InvestigationPipeline.Import(ws, [f]));
        Assert.Equal("Import:Security", item.Source);
        Assert.Equal("EventLog:Security", item.SourceType);
        Assert.Contains("jurnal arhivat", item.Notes);
        Assert.Contains("jurnal arhivat", LogAnalyzer.Dfir.Analysis.EvidenceOrigin.Map(ws.LoadEvidence())[item.EvidenceId]);
    }

    [Fact]
    public void Shadow_copies_without_administrator_rights_are_undetermined_never_none()
    {
        var r = ShadowCopies.List(isAdministrator: false, query: () => throw new InvalidOperationException("must not query"));
        Assert.Equal(ShadowListStatus.NotAdministrator, r.Status);
        Assert.StartsWith("Nedeterminat", r.Message);
        Assert.Empty(r.Items);
    }

    [Fact]
    public void Shadow_copies_are_listed_from_wmi_rows_newest_first_and_bad_device_objects_are_dropped()
    {
        IReadOnlyDictionary<string, object?> Row(string id, string dev, string date) => new Dictionary<string, object?> { ["ID"] = id, ["DeviceObject"] = dev, ["VolumeName"] = @"\\?\Volume{x}\", ["InstallDate"] = date };
        var r = ShadowCopies.List(isAdministrator: true, query: () =>
        [
            Row("{A}", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1", "20260901080000.000000+180"),
            Row("{B}", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy2", "20261001080000.000000+000"),
            Row("{C}", @"C:\not-a-shadow", "20261002080000.000000+000"),
        ]);
        Assert.Equal(ShadowListStatus.Listed, r.Status);
        Assert.Equal(["{B}", "{A}"], r.Items.Select(i => i.Id));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 5, 0, 0, TimeSpan.Zero), r.Items[1].CreatedUtc);   // +180 minutes from UTC
        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy2\", r.Items[0].Root);
        Assert.Equal(ShadowListStatus.None, ShadowCopies.List(true, () => []).Status);
    }

    [Fact]
    public void Files_of_a_chosen_copy_are_found_and_imported_with_the_copy_recorded_in_custody()
    {
        // a folder standing in for \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7 (reading a real copy needs administrator rights)
        var root = Path.Combine(_k.Dir, "HarddiskVolumeShadowCopy7");
        _k.File(@"HarddiskVolumeShadowCopy7\Windows\System32\winevt\Logs\System.evtx", "x");
        _k.File(@"HarddiskVolumeShadowCopy7\Windows\System32\config\SYSTEM", "x");
        _k.File(@"HarddiskVolumeShadowCopy7\Windows\Prefetch\APP.EXE-12345678.pf", "x");
        _k.File(@"HarddiskVolumeShadowCopy7\Users\u1\NTUSER.DAT", "x");
        var copy = new ShadowCopyInfo("{S7}", root, @"\\?\Volume{x}\", DateTimeOffset.UtcNow);
        var files = ShadowCopies.ArtifactPaths(copy);
        Assert.Equal(4, files.Count);
        var ws = _k.Case();
        var items = InvestigationPipeline.Import(ws, files);
        Assert.All(items, i => Assert.Contains("copie de umbră (VSS) 7", i.Notes));
        var custody = File.ReadAllText(ws.CustodyJsonlPath);
        Assert.Contains("HarddiskVolumeShadowCopy7", custody);
        Assert.Contains("vss.import", File.ReadAllText(ws.AppAuditLogPath));
    }
}
