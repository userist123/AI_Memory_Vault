using System.IO;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Devices;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 item 3 (HG585 EXP-01 / MED-01): a written export records where it went and on which registered medium. Synthetic data only.</summary>
[Collection("OperationAuditMediumOf")]
public sealed class ExportDestinationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-expdest-" + Guid.NewGuid().ToString("N"));
    private readonly Func<string, OperationAudit.ExportMedium>? _saved = OperationAudit.MediumOf;

    public ExportDestinationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        OperationAudit.MediumOf = _saved;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static SignedMediaRegisterDoc Register(params SignedMediaEntry[] entries) => new() { Entries = [.. entries] };

    private static SignedMediaEntry Medium(string inv, string volume, string status = SignedMediaRegister.StatusActive) =>
        new() { InventoryNumber = inv, UsbSerial = "SER" + inv, VolumeSerial = volume, Status = status };

    private static (string, string) Removable(string _) => ("amovibil", "1234ABCD");

    [Fact]
    public void A_registered_active_medium_is_named_by_its_inventory_number_whatever_the_serial_spelling()
    {
        var m = ExportMediumResolver.Resolve(@"E:\export\raport.pdf", () => Register(Medium("INV-7", "1234-abcd")), Removable);
        Assert.Equal(new OperationAudit.ExportMedium("amovibil", "1234ABCD", "INV-7"), m);
    }

    [Fact]
    public void An_unregistered_retired_or_unknown_medium_is_never_given_an_inventory_number()
    {
        Assert.Equal(OperationAudit.MediumUnregistered, ExportMediumResolver.Resolve(@"E:\x.csv", () => Register(Medium("INV-1", "FFFF0000")), Removable).Medium);
        Assert.Equal(OperationAudit.MediumUnregistered, ExportMediumResolver.Resolve(@"E:\x.csv", () => Register(Medium("INV-2", "1234ABCD", SignedMediaRegister.StatusRetired)), Removable).Medium);
        // no verified register: absence from an unverified list proves nothing, so it is NOT "neînregistrat"
        Assert.Equal(OperationAudit.MediumRegisterUnavailable, ExportMediumResolver.Resolve(@"E:\x.csv", () => null, Removable).Medium);
        Assert.Equal(OperationAudit.MediumRegisterUnavailable, ExportMediumResolver.Resolve(@"E:\x.csv", () => throw new IOException("locked"), Removable).Medium);
        // no volume serial (network share, empty drive): the medium cannot be told
        Assert.Equal(OperationAudit.MediumUnknown, ExportMediumResolver.Resolve(@"\\srv\share\x.csv", () => Register(Medium("INV-1", "1234ABCD")), _ => ("rețea", "")).Medium);
    }

    [Fact]
    public void Two_active_entries_with_one_volume_serial_are_both_named()
    {
        var m = ExportMediumResolver.Resolve(@"E:\x.csv", () => Register(Medium("INV-1", "1234ABCD"), Medium("INV-9", "1234-ABCD")), Removable);
        Assert.Equal("INV-1|INV-9", m.Medium);
    }

    [Fact]
    public void The_written_export_detail_carries_the_full_path_and_the_medium_and_keeps_its_order()
    {
        var file = Path.Combine(_dir, "out.csv");
        File.WriteAllText(file, "a,b\n");
        OperationAudit.MediumOf = p => ExportMediumResolver.Resolve(p, () => Register(Medium("INV-7", "1234ABCD")), Removable);
        var d = OperationAudit.ExportWrittenDetail("C1", "csv", 1, file);
        Assert.Contains($"; path={Path.GetFullPath(file)}; drive=amovibil; volume=1234ABCD; medium=INV-7; phase=written", d);
        Assert.StartsWith("case=C1; format=csv; items=1; sha256=", d);

        var bundle = OperationAudit.ExportBundleFileDetail("C1", "pdf", _dir, file);
        Assert.Contains("medium=INV-7; folder=", bundle);
    }

    [Fact]
    public void A_failing_resolver_never_breaks_the_detail_and_separators_in_values_are_neutralised()
    {
        var file = Path.Combine(_dir, "out.csv");
        File.WriteAllText(file, "x");
        OperationAudit.MediumOf = _ => throw new UnauthorizedAccessException("denied");
        Assert.Contains("drive=necunoscut; volume=; medium=necunoscut; phase=written", OperationAudit.ExportWrittenDetail("C1", "csv", 1, file));

        OperationAudit.MediumOf = _ => new OperationAudit.ExportMedium("fix; medium=INV-FALS", "AB=CD", "x\ny");
        var d = OperationAudit.ExportWrittenDetail("C1", "csv", 1, file);
        Assert.Contains("drive=fix, medium-INV-FALS; volume=AB-CD; medium=x y; phase=written", d);
        Assert.Equal(2, d.Split("; medium=").Length);   // exactly one "; medium=" field: the injected one was neutralised
    }
}

[CollectionDefinition("OperationAuditMediumOf", DisableParallelization = true)]
public sealed class OperationAuditMediumOfCollection;
