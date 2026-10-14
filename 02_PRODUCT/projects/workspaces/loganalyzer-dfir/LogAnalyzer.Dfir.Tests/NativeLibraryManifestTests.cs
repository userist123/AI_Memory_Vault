using System.Security.Cryptography;
using LogAnalyzer.Dfir.IO;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #5: the published, elevated executable loads its native libraries from its own folder (no self-extraction to a user-writable
/// folder) and checks them at startup against the SHA-256 manifest written at publish. Any difference, missing or extra DLL fails closed.
/// </summary>
public sealed class NativeLibraryManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-native-" + Guid.NewGuid().ToString("N"));

    public NativeLibraryManifestTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string Dll(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private static string Hex(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private void Manifest(params string[] names) =>
        File.WriteAllLines(Path.Combine(_dir, NativeLibraryManifest.FileName), names.Select(n => $"{Hex(Path.Combine(_dir, n))}  {n}"));

    [Fact]
    public void Matching_folder_passes()
    {
        Dll("e_sqlcipher.dll", "native-1");
        Dll("wpfgfx_cor3.dll", "native-2");
        Manifest("e_sqlcipher.dll", "wpfgfx_cor3.dll");
        var r = NativeLibraryManifest.Verify(_dir);
        Assert.True(r.Ok, string.Join("; ", r.Problems));
    }

    [Fact]
    public void A_replaced_dll_fails_and_is_named()
    {
        var p = Dll("e_sqlcipher.dll", "native-1");
        Manifest("e_sqlcipher.dll");
        File.WriteAllText(p, "planted");
        var r = NativeLibraryManifest.Verify(_dir);
        Assert.False(r.Ok);
        Assert.Contains(r.Problems, x => x.Contains("e_sqlcipher.dll") && x.Contains("modificat"));
        Assert.Contains("Aplicația nu pornește", r.Message);
    }

    [Fact]
    public void A_missing_listed_dll_or_an_extra_unlisted_dll_fails()
    {
        Dll("a.dll", "1");
        Dll("b.dll", "2");
        Manifest("a.dll", "b.dll");
        File.Delete(Path.Combine(_dir, "b.dll"));
        Assert.Contains(NativeLibraryManifest.Verify(_dir).Problems, x => x.Contains("b.dll") && x.Contains("lipsește"));
        Manifest("a.dll");
        Dll("version.dll", "planted for search-order hijack");
        Assert.Contains(NativeLibraryManifest.Verify(_dir).Problems, x => x.Contains("version.dll") && x.Contains("nu este în manifest"));
    }

    [Fact]
    public void Missing_or_malformed_manifest_fails_closed()
    {
        Dll("a.dll", "1");
        Assert.False(NativeLibraryManifest.Verify(_dir).Ok);
        File.WriteAllText(Path.Combine(_dir, NativeLibraryManifest.FileName), "nu e un manifest\n");
        Assert.False(NativeLibraryManifest.Verify(_dir).Ok);
        File.WriteAllText(Path.Combine(_dir, NativeLibraryManifest.FileName), $"{new string('A', 64)}  ..\\evil.dll\n");
        Assert.Contains(NativeLibraryManifest.Verify(_dir).Problems, x => x.Contains("evil.dll"));
    }

    // ---- independent review, finding 3: the manifest embedded in the executable is the authority ----

    private string ManifestText(params string[] names) => string.Join("\n", names.Select(n => $"{Hex(Path.Combine(_dir, n))}  {n}"));

    [Fact]
    public void The_embedded_manifest_is_used_and_a_rewritten_file_next_to_the_exe_does_not_override_it()
    {
        var p = Dll("e_sqlcipher.dll", "native-1");
        var embedded = ManifestText("e_sqlcipher.dll");
        File.WriteAllText(p, "planted");
        Manifest("e_sqlcipher.dll");   // the attacker also rewrites the text file
        foreach (var debug in new[] { false, true })
            foreach (var singleFile in new[] { false, true })
            {
                var r = NativeLibraryManifest.VerifyInstall(_dir, embedded, debugBuild: debug, singleFile: singleFile);
                Assert.True(r.Checked);
                Assert.False(r.Ok);
                Assert.Contains(r.Problems, x => x.Contains("e_sqlcipher.dll") && x.Contains("modificat"));
            }
    }

    [Fact]
    public void Without_an_embedded_manifest_a_release_single_file_fails_closed_and_the_file_is_used_only_in_debug()
    {
        Dll("a.dll", "1");
        Manifest("a.dll");
        var release = NativeLibraryManifest.VerifyInstall(_dir, null, debugBuild: false, singleFile: true);
        Assert.True(release.Checked); Assert.False(release.Ok);
        Assert.Contains(release.Problems, x => x.Contains("încorporat"));
        var debug = NativeLibraryManifest.VerifyInstall(_dir, null, debugBuild: true, singleFile: true);
        Assert.True(debug.Checked); Assert.True(debug.Ok, string.Join("; ", debug.Problems));
        // Review of the remediations #4: a non-Debug build without the embedded manifest is a failure whether single-file or not
        // (framework-dependent publish embeds it too); only a Debug build without any manifest is "not checked".
        var releaseFromBin = NativeLibraryManifest.VerifyInstall(_dir, null, debugBuild: false, singleFile: false);
        Assert.False(releaseFromBin.Ok);
        Assert.True(releaseFromBin.ManifestMissing);
        File.Delete(Path.Combine(_dir, NativeLibraryManifest.FileName));
        var debugFromBin = NativeLibraryManifest.VerifyInstall(_dir, null, debugBuild: true, singleFile: false);
        Assert.False(debugFromBin.Checked);
    }

    private static readonly LogAnalyzer.Dfir.Auth.StoreAclResult Weak = new(false, ["LogAnalyzer: BUILTIN\\Users poate scrie"]);

    /// <summary>Review of the remediations #4: Debug/Release × manifest present/absent × elevated or not × folder protected or writable.</summary>
    [Theory]
    //          debug  manifest elevated protected  expected                     exit
    [InlineData(false, true,  true,  true,  NativeStartupVerdict.Start,  0)]
    [InlineData(false, true,  true,  false, NativeStartupVerdict.Refuse, 4)]
    [InlineData(false, true,  false, true,  NativeStartupVerdict.Start,  0)]
    [InlineData(false, true,  false, false, NativeStartupVerdict.Warn,   0)]
    [InlineData(false, false, true,  true,  NativeStartupVerdict.Refuse, 3)]
    [InlineData(false, false, true,  false, NativeStartupVerdict.Refuse, 3)]
    [InlineData(false, false, false, true,  NativeStartupVerdict.Warn,   0)]
    [InlineData(false, false, false, false, NativeStartupVerdict.Warn,   0)]
    [InlineData(true,  true,  true,  true,  NativeStartupVerdict.Start,  0)]
    [InlineData(true,  true,  true,  false, NativeStartupVerdict.Refuse, 4)]
    [InlineData(true,  true,  false, true,  NativeStartupVerdict.Start,  0)]
    [InlineData(true,  true,  false, false, NativeStartupVerdict.Warn,   0)]
    [InlineData(true,  false, true,  true,  NativeStartupVerdict.Start,  0)]
    [InlineData(true,  false, true,  false, NativeStartupVerdict.Refuse, 4)]   // elevated: the folder is checked even without a manifest
    [InlineData(true,  false, false, true,  NativeStartupVerdict.Start,  0)]
    [InlineData(true,  false, false, false, NativeStartupVerdict.Start,  0)]   // development build from bin, not elevated
    public void Startup_decision_matrix(bool debug, bool manifest, bool elevated, bool folderProtected, NativeStartupVerdict expected, int exit)
    {
        Dll("a.dll", "1");
        var embedded = manifest ? ManifestText("a.dll") : null;
        foreach (var singleFile in new[] { false, true })
        {
            var d = NativeLibraryManifest.DecideStartup(_dir, embedded, debug, singleFile, elevated,
                () => folderProtected ? LogAnalyzer.Dfir.Auth.StoreAclResult.Ok : Weak);
            Assert.Equal(expected, d.Verdict);
            Assert.Equal(exit, d.ExitCode);
            if (expected != NativeStartupVerdict.Start) Assert.False(string.IsNullOrWhiteSpace(d.Message));
            if (exit == 3) Assert.Contains("încorporat", d.Message);
            if (exit == 4) Assert.Contains("BUILTIN\\Users", d.Message);
        }
    }

    [Fact]
    public void Framework_dependent_publish_exempts_only_the_main_assembly_that_carries_the_manifest()
    {
        Dll("a.dll", "1");
        var embedded = ManifestText("a.dll");
        Dll("LogAnalyzer.App.dll", "main");          // the assembly the manifest is embedded in cannot list its own hash
        Assert.True(NativeLibraryManifest.VerifyInstall(_dir, embedded, false, false, selfFileName: "LogAnalyzer.App.dll").Ok);
        Assert.False(NativeLibraryManifest.VerifyInstall(_dir, embedded, false, false).Ok);
        Dll("planted.dll", "x");
        var r = NativeLibraryManifest.VerifyInstall(_dir, embedded, false, false, selfFileName: "LogAnalyzer.App.dll");
        Assert.False(r.Ok);
        Assert.Contains(r.Problems, p => p.Contains("planted.dll"));
        Assert.Equal(NativeStartupVerdict.Refuse,
            NativeLibraryManifest.DecideStartup(_dir, embedded, false, false, false, () => LogAnalyzer.Dfir.Auth.StoreAclResult.Ok, "LogAnalyzer.App.dll").Verdict);
    }

    [Fact]
    public void A_changed_library_refuses_the_start_elevated_or_not()
    {
        var p = Dll("a.dll", "1");
        var embedded = ManifestText("a.dll");
        File.WriteAllText(p, "planted");
        foreach (var elevated in new[] { false, true })
            Assert.Equal(NativeStartupVerdict.Refuse, NativeLibraryManifest.DecideStartup(_dir, embedded, false, true, elevated, () => LogAnalyzer.Dfir.Auth.StoreAclResult.Ok).Verdict);
    }

    [Fact]
    public void Startup_restricts_the_dll_search_order_and_uses_the_decision()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        // LogAnalyzer.App.Classified compiles every LogAnalyzer.App\**\*.cs (linked), so this one file serves both editions.
        var appCode = File.ReadAllText(Path.Combine(root, "LogAnalyzer.App", "DllSearchOrder.cs"));
        Assert.Contains("SetDefaultDllDirectories", appCode);
        Assert.Contains("[System.Runtime.CompilerServices.ModuleInitializer]", appCode);
        Assert.Contains("0x00000200", appCode);   // LOAD_LIBRARY_SEARCH_APPLICATION_DIR
        Assert.Contains("0x00000800", appCode);   // LOAD_LIBRARY_SEARCH_SYSTEM32
        var startup = File.ReadAllText(Path.Combine(root, "LogAnalyzer.App", "App.xaml.cs"));
        Assert.Contains("NativeLibraryManifest.DecideStartup", startup);
        Assert.DoesNotContain("if (!check.Checked) return;", startup);
        var targets = File.ReadAllText(Path.Combine(root, "eng", "NativeLibraryManifest.targets"));
        Assert.DoesNotContain("Condition=\"'$(PublishSingleFile)' == 'true'\"", targets);
    }

    [Fact]
    public void Legacy_root_projects_no_longer_ask_for_elevation()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var manifest = File.ReadAllText(Path.Combine(root, "app.manifest"));
        var active = System.Text.RegularExpressions.Regex.Replace(manifest, "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Contains("level=\"asInvoker\"", active);
        Assert.DoesNotContain("requireAdministrator", active);
        Assert.DoesNotContain("highestAvailable", active);
    }

    [Fact]
    public void Install_folder_writable_by_standard_users_refuses_an_elevated_start_and_warns_otherwise()
    {
        var weak = new LogAnalyzer.Dfir.Auth.StoreAclResult(false, ["LogAnalyzer: BUILTIN\\Users poate scrie"]);
        Assert.Equal(InstallFolderVerdict.RefuseElevated, NativeLibraryManifest.DecideInstallFolder(weak, elevated: true).Verdict);
        var warn = NativeLibraryManifest.DecideInstallFolder(weak, elevated: false);
        Assert.Equal(InstallFolderVerdict.Warn, warn.Verdict);
        Assert.Contains("BUILTIN\\Users", warn.Message);
        Assert.Equal(InstallFolderVerdict.Ok, NativeLibraryManifest.DecideInstallFolder(LogAnalyzer.Dfir.Auth.StoreAclResult.Ok, elevated: true).Verdict);
    }

    [Fact]
    public void The_publish_targets_embed_the_manifest_into_the_main_assembly()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var targets = File.ReadAllText(Path.Combine(root, "eng", "NativeLibraryManifest.targets"));
        Assert.Contains($"LogicalName=\"{NativeLibraryManifest.ResourceName}\"", targets);
        Assert.Contains("<EmbeddedResource", targets);
        var app = File.ReadAllText(Path.Combine(root, "LogAnalyzer.App", "App.xaml.cs"));
        Assert.Contains("NativeLibraryManifest.DecideStartup", app);   // DecideStartup runs VerifyInstall (review of the remediations #4)
        Assert.Contains("NativeLibraryManifest.ResourceName", app);
        Assert.DoesNotContain("if (!string.IsNullOrEmpty(typeof(App).Assembly.Location)) return;", app);
    }

    [Fact]
    public void Both_publish_profiles_ship_native_libraries_next_to_the_exe_and_write_the_manifest()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        foreach (var app in new[] { "LogAnalyzer.App", "LogAnalyzer.App.Classified" })
        {
            var pubxml = File.ReadAllText(Path.Combine(root, app, "Properties", "PublishProfiles", "win-x64-singlefile.pubxml"));
            Assert.Contains("<IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>", pubxml);
            Assert.DoesNotContain("<IncludeNativeLibrariesForSelfExtract>true", pubxml);
            Assert.DoesNotContain("IncludeAllContentForSelfExtract>true", pubxml);
            var csproj = File.ReadAllText(Path.Combine(root, app, app + ".csproj"));
            Assert.Contains("NativeLibraryManifest.targets", csproj);
        }
    }
}
