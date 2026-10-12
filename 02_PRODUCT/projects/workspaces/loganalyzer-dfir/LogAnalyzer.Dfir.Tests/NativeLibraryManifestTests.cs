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
