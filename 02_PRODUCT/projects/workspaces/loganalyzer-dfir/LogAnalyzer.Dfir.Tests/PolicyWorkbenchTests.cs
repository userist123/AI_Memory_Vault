using System.Text;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Policy;
using Microsoft.Win32;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The workbench the application uses, end to end, with the Windows account replaced by two named identities.</summary>
[Collection("HKCU sandbox")] // share HKCU\Software\LogAnalyzerDfirTest; never run in parallel
public sealed class PolicyWorkbenchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ladfir_bench_" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\LogAnalyzerDfirTest\" + Guid.NewGuid().ToString("N");
    private string _who = @"STATIE\operator";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\LogAnalyzerDfirTest"))
            if (parent is { SubKeyCount: 0, ValueCount: 0 }) { parent.Dispose(); Registry.CurrentUser.DeleteSubKey(@"Software\LogAnalyzerDfirTest", throwOnMissingSubKey: false); }
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private PolicyWorkbench Bench() => new(_root, [new RegistrySettingProvider(new LogAnalyzer.Response.Policy.RegistryValueWriter())], () => _who);

    [Fact]
    public void Lgpo_text_is_imported_approved_by_another_account_applied_and_rolled_back()
    {
        var src = Path.Combine(_root, "in", "policy.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, $"User\n{_key}\nScreenSaverIsSecure\nSZ:1\n");

        var bench = Bench();
        var opened = bench.Open(src);
        Assert.StartsWith(bench.LibraryDir, opened.LibraryPath);
        Assert.Equal(opened.Policy.Sha256, PolicyDocument.Hash(File.ReadAllText(opened.LibraryPath)));
        Assert.Equal(opened.Policy.Sha256, bench.Open(src).Policy.Sha256); // reopening gives the same text

        var p = opened.Policy;
        bench.Register(p);
        bench.Validate(p);
        var (dry, tested) = bench.DryRun(p);
        Assert.Equal((PolicyStatus.Tested, 1), (tested.Status, dry.ToChange));
        Assert.Throws<PolicyLifecycleException>(() => bench.Approve(p, "eu însumi"));

        _who = @"STATIE\aprobator";
        Assert.Equal(PolicyStatus.Approved, Bench().Approve(p, "verificat").Status);

        _who = @"STATIE\operator";
        bench = Bench();
        var plan = bench.Plan(p);
        var exec = bench.Apply(p, plan, plan.Sha256);
        Assert.Equal((ExecutionStatus.Verified, @"STATIE\operator", @"STATIE\aprobator"), (exec.Status, exec.Operator, exec.Approver));
        using (var k = Registry.CurrentUser.OpenSubKey(_key)) Assert.Equal("1", k!.GetValue("ScreenSaverIsSecure"));
        Assert.Equal(PolicyStatus.Verified, bench.Store.Get(p)!.Status);

        var rb = bench.Rollback(p, bench.Executions(p).Single().ExecutionId);
        Assert.Equal(ExecutionStatus.Verified, rb.Status);
        using (var k = Registry.CurrentUser.OpenSubKey(_key)) Assert.Null(k!.GetValue("ScreenSaverIsSecure"));
        Assert.Equal(2, bench.Executions(p).Count);
    }

    [Fact]
    public void Library_copy_with_other_text_is_not_overwritten_and_unknown_formats_are_refused()
    {
        var src = Path.Combine(_root, "in", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, $"Computer\n{_key}\nV\nDWORD:1\n");
        var bench = Bench();
        var lib = bench.Open(src).LibraryPath;
        File.AppendAllText(lib, "# editat\n", Encoding.UTF8);
        Assert.Throws<PolicyLifecycleException>(() => bench.Open(src));

        var other = Path.Combine(_root, "in", "x.docx");
        File.WriteAllText(other, "x");
        Assert.Throws<NotSupportedException>(() => bench.Open(other));
    }
}
