using System.Diagnostics;
using System.Text.Json;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Tests that set the process-wide operating mode run alone.</summary>
[CollectionDefinition("AppMode", DisableParallelization = true)]
public sealed class AppModeCollection;

[Collection("AppMode")]
public sealed class RemoteCollectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ladfir_remote_" + Guid.NewGuid().ToString("N"));

    public RemoteCollectionTests()
    {
        // The whole remote path must work on an isolated station: the application never opens a connection for it.
        AppModeContext.ResetForTests();
        AppModeContext.Initialize(new ModeDecision(AppMode.AirGapped, true, "test", ConnectivitySnapshot.Unknown("test")));
    }

    public void Dispose()
    {
        AppModeContext.ResetForTests();
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() => CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "CASE-REMOTE", Name = "remote", CreatedAtUtc = DateTimeOffset.UtcNow , Scope = TestScopes.Valid() });

    /// <summary>Windows PowerShell 5.1 as an operator starts it. PSModulePath is dropped so a PowerShell 7 parent (the CI
    /// runner's default shell) cannot hand it PS7-only module paths.</summary>
    private static ProcessStartInfo PowerShell(string file)
    {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }) psi.ArgumentList.Add(a);
        psi.Environment.Remove("PSModulePath");
        return psi;
    }

    /// <summary>Runs the generated package with Windows PowerShell, as an operator would on the target, and returns the package folder.</summary>
    private string RunPackage(string script)
    {
        var dir = Path.Combine(_root, "target");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "collect.ps1");
        File.WriteAllText(file, script, new System.Text.UTF8Encoding(true));
        using var p = Process.Start(PowerShell(file))!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}: {stderr}");
        return stdout.Trim().Split('\n').Last().Trim();
    }

    [Fact]
    public void Package_collects_on_this_station_hashes_on_target_and_imports_with_custody()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["evtx:Application", "hive:SYSTEM"]);
        var pkg = RunPackage(RemoteCollection.PackageScript(req, sha));

        var check = RemoteCollection.Verify(ws, pkg);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var m = check.Manifest!;
        Assert.Equal((req.RequestId, Environment.MachineName.ToUpperInvariant()), (m.RequestId, m.Host.ToUpperInvariant()));
        Assert.Equal("ok", m.Steps.Single(s => s.Artifact == "evtx:Application").Status);
        // Without elevation reg save cannot run; the package records that instead of hiding it.
        if (!m.Elevated) Assert.Equal("failed", m.Steps.Single(s => s.Artifact == "hive:SYSTEM").Status);

        var items = RemoteCollection.Import(ws, check);
        Assert.Equal(m.Files.Count + 1, items.Count);
        Assert.All(items.Skip(1), i => Assert.Equal(items[0].EvidenceId, i.ParentEvidenceId));
        Assert.All(items, i => Assert.Equal("remote:" + m.Host, i.Source));
        var evtx = items.Single(i => i.SourceType == "evtx");
        Assert.Equal(m.Files.Single(f => f.Path.EndsWith("Application.evtx")).Sha256, evtx.Sha256, StringComparer.OrdinalIgnoreCase);
        var custody = File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Contains(custody, c => c.GetProperty("action").GetString() == "remote-collected" && c.GetProperty("evidenceId").GetString() == evtx.EvidenceId);
        Assert.Contains(custody, c => c.GetProperty("action").GetString() == "authorized");
    }

    [Fact]
    public void Tampered_missing_extra_unauthorized_or_foreign_packages_are_refused()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, "operator", "test", ["evtx:Application"]);
        var pkg = RunPackage(RemoteCollection.PackageScript(req, sha));
        Assert.True(RemoteCollection.Verify(ws, pkg).Ok);
        var evtx = Directory.GetFiles(pkg, "*.evtx", SearchOption.AllDirectories).Single();

        File.WriteAllText(Path.Combine(pkg, "files", "extra.txt"), "x");
        Assert.Contains(RemoteCollection.Verify(ws, pkg).Problems, p => p.Contains("extra.txt"));
        File.Delete(Path.Combine(pkg, "files", "extra.txt"));

        var bytes = File.ReadAllBytes(evtx);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(evtx, bytes);
        var bad = RemoteCollection.Verify(ws, pkg);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Problems, p => p.Contains("SHA-256 diferit de cel calculat pe țintă"));
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, bad));

        File.Delete(evtx);
        Assert.Contains(RemoteCollection.Verify(ws, pkg).Problems, p => p.StartsWith("lipsește"));

        var manifest = Path.Combine(pkg, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(Environment.MachineName, "ALTHOST", StringComparison.OrdinalIgnoreCase));
        var forged = RemoteCollection.Verify(ws, pkg).Problems;
        Assert.Contains(forged, p => p.Contains("modificat după colectare"));
        Assert.Contains(forged, p => p.Contains("autorizat pentru"));

        // A package from another case (its request is not here) is not imported.
        var other = CaseWorkspace.Create(Path.Combine(_root, "other"), new CaseInfo { CaseId = "CASE-OTHER", Name = "o", CreatedAtUtc = DateTimeOffset.UtcNow , Scope = TestScopes.Valid() });
        Assert.Contains(RemoteCollection.Verify(other, pkg).Problems, p => p.Contains("nu a fost autorizată aici"));
    }

    [Fact]
    public void Package_refuses_another_host_and_requests_are_validated()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "ALTA-STATIE", "operator", "test", ["evtx:Application"]);
        var script = RemoteCollection.PackageScript(req, sha);
        var file = Path.Combine(_root, "t2", "collect.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, script);
        using (var p = Process.Start(PowerShell(file))!) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Assert.Equal(2, p.ExitCode); }
        Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(file)!)); // nothing collected

        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "host'; Remove-Item C:\\", "op", "j", ["evtx:Application"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "10.0.0.5", "op", "j", ["evtx:Application"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", " ", ["evtx:Application"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", ["hive:SAM"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", []));
    }

    [Fact]
    public void Package_contains_no_network_command_and_no_credential_store()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", RemoteCollection.KnownArtifacts);
        var script = RemoteCollection.PackageScript(req, sha);
        Assert.DoesNotContain("Get-FileHash", script, StringComparison.OrdinalIgnoreCase); // hashing must not depend on a module
        foreach (var forbidden in new[] { "Invoke-WebRequest", "Invoke-RestMethod", "Invoke-Command", "New-PSSession", "Enter-PSSession", "Start-BitsTransfer",
                                          "Net.WebClient", "Net.Sockets", "HttpClient", "curl", "wget", "ftp", "Send-MailMessage", "\\\\\\\\", "Copy-Item -ToSession",
                                          "HKLM\\SAM", "HKLM\\SECURITY", "Cookies", "Login Data" })
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RemoteCollection.KnownArtifacts, a => a.Contains("SAM") || a.Contains("SECURITY"));
        Assert.True(AppModeContext.IsAirGapped);
    }
}
