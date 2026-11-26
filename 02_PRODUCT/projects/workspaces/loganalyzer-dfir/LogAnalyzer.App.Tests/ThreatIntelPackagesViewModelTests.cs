using System;
using System.IO;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.ViewModels;
using Xunit;
namespace LogAnalyzer.App.Tests;

/// <summary>Master plan 6.12 on the Politici page: what the section says and refuses before any package is trusted.</summary>
public sealed class ThreatIntelPackagesViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_tivm_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class NoCms : ICmsDetachedVerifier { public CmsResult Verify(byte[] cms, byte[] content) => new(false, null, "semnătura nu se verifică"); }

    private ThreatIntelPackagesViewModel Vm(bool admin) =>
        new(() => new ThreatIntelStore(Path.Combine(_dir, "store")), () => new NoCms(), () => new QualifiedSignatureTrust([], []), () => [], () => admin);

    [Fact]
    public void With_no_package_the_section_says_none_is_used_and_a_missing_folder_is_named()
    {
        var vm = Vm(admin: true);
        vm.Refresh();
        Assert.StartsWith("Niciun pachet folosit: niciun pachet", vm.ActiveLine);
        vm.SourceFolder = Path.Combine(_dir, "nu-exista");
        vm.StageCommand.Execute(null);
        Assert.StartsWith("Alegeți folderul pachetului", vm.Status);
    }

    [Fact]
    public void A_package_whose_signature_does_not_verify_is_not_staged_and_approval_and_rollback_need_an_administrator()
    {
        var src = Path.Combine(_dir, "pkg");
        Directory.CreateDirectory(Path.Combine(src, "ioc"));
        File.WriteAllText(Path.Combine(src, ThreatIntelManifest.FileName), "{\"format\":\"loganalyzer.ti_package/1\",\"package_id\":\"CERT-X\",\"version\":1,\"files\":[{\"Path\":\"ioc/a.csv\",\"Sha256\":\"" + new string('a', 64) + "\"}]}");
        File.WriteAllText(Path.Combine(src, ThreatIntelManifest.SignatureName), "x");
        var vm = Vm(admin: false);
        vm.SourceFolder = src;
        vm.StageCommand.Execute(null);
        Assert.StartsWith("Refuzat:", vm.Status);
        Assert.Empty(vm.StagedRows);
        Assert.Contains(vm.CheckLines, l => l.StartsWith("Eșuat:", StringComparison.Ordinal));
        Assert.Contains(vm.HistoryLines, l => l.Contains("refuzat la verificare"));

        vm.SelectedStaged = new StagedPackage("ST-1", "CERT-X", 1, "", "ana", DateTimeOffset.UtcNow);
        vm.ApproveCommand.Execute(null);
        Assert.Contains("doar un administrator aprobă", vm.Status);
        vm.RollbackVersion = "CERT-X v1";
        vm.RollbackCommand.Execute(null);
        Assert.Contains("doar un administrator revine", vm.Status);
    }
}
