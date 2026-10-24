using System.IO.Compression;
using System.Security.Cryptography;
using LogAnalyzer.Dfir.Analysis;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Offline signed data packs (ANALYSIS_2026-10-11_APPLICATION_UPDATES.md option A): only signed, newer, intact packs of known data files install.</summary>
[Collection("DataPack global state")]
public sealed class DataPackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-pack-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly IReadOnlyList<string> _keys;
    private readonly string _installDir;

    public DataPackTests()
    {
        Directory.CreateDirectory(_dir);
        _keys = DataPack.TrustedKeys; _installDir = DataPack.InstallDir;
        DataPack.TrustedKeys = [Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())];
        DataPack.InstallDir = Path.Combine(_dir, "installed");
    }

    public void Dispose()
    {
        DataPack.TrustedKeys = _keys; DataPack.InstallDir = _installDir;
        Directory.Delete(_dir, true);
    }

    private string Pack(long version, ECDsa? key = null, string name = "rule_lists.json", string content = "{\"x\":1}")
    {
        var path = Path.Combine(_dir, $"p{version}-{Guid.NewGuid():N}.zip");
        DataPack.Create(path, "test", version, new Dictionary<string, string> { [name] = content }, key ?? _key);
        return path;
    }

    [Fact]
    public void A_signed_newer_pack_installs_and_verifies_again_on_load()
    {
        var r = DataPack.Import(Pack(1));
        Assert.True(r.Ok, r.Message);
        Assert.Equal(1, DataPack.Installed(out var problem)!.Version);
        Assert.Equal("", problem);
        Assert.False(DataPack.Import(Pack(1)).Ok);                       // same version refused
        Assert.True(DataPack.Import(Pack(2)).Ok);
        Assert.Contains("mai vechi", DataPack.Import(Pack(1)).Message);  // downgrade refused
    }

    [Fact]
    public void A_pack_signed_with_another_key_or_without_any_trusted_key_never_installs()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Contains("NU corespunde", DataPack.Import(Pack(1, other)).Message);
        DataPack.TrustedKeys = [];
        Assert.Contains("nu are încă o cheie publică", DataPack.Import(Pack(1)).Message);
        Assert.Null(DataPack.Installed(out _));
    }

    [Fact]
    public void Unknown_files_and_tampered_data_are_refused_and_tampering_after_install_is_detected()
    {
        Assert.Contains("neașteptat", DataPack.Import(Pack(1, name: "evil.dll")).Message);

        var path = Pack(1);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("data/rule_lists.json")!.Delete();
            using var s = zip.CreateEntry("data/rule_lists.json").Open(); s.Write("{\"x\":2}"u8);
        }
        Assert.Contains("deteriorat", DataPack.Import(path).Message);

        Assert.True(DataPack.Import(Pack(3)).Ok);
        File.WriteAllText(Path.Combine(DataPack.InstallDir, "rule_lists.json"), "{\"changed\":true}");
        Assert.Null(DataPack.Installed(out var problem));
        Assert.Contains("modificat după instalare", problem);
    }
}

[CollectionDefinition("DataPack global state", DisableParallelization = true)]
public sealed class DataPackCollection;
