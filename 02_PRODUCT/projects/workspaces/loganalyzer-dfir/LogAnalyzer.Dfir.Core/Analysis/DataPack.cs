using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>One file of a detection data pack and its SHA-256 (hex).</summary>
public sealed record DataPackFile(string Name, string Sha256);

/// <summary>The signed manifest of a data pack: id, monotonically increasing version, creation time and the files it carries.</summary>
public sealed record DataPackManifest(string PackId, long Version, DateTimeOffset CreatedUtc, IReadOnlyList<DataPackFile> Files);

public sealed record DataPackResult(bool Ok, string Message, DataPackManifest? Manifest = null);

/// <summary>
/// Offline update of the detection data (docs/dfir/ANALYSIS_2026-10-11_APPLICATION_UPDATES.md, option A): a ZIP with <c>manifest.json</c>,
/// <c>manifest.sig</c> (base64 ECDSA P-256 over the manifest bytes) and the JSON data files. Only data, never code. Import verifies the signature with a
/// public key compiled into the application, refuses a version not newer than the installed one, checks every file's SHA-256, then installs it;
/// at load time the installed pack is verified again and any failure falls back to the data shipped with the application.
/// </summary>
public static class DataPack
{
    /// <summary>Data files a pack may replace (the shipped embedded resources). Anything else in a pack is refused.</summary>
    public static readonly string[] AllowedFiles =
        ["security_agents.json", "remote_access_tools.json", "rule_lists.json", "airgap_lists.json", "sequence_rules.json"];

    /// <summary>
    /// Public keys (base64 SubjectPublicKeyInfo) that may sign packs. Empty until the owner generates the offline signing key; with no key, import says so.
    /// Two slots allow a key rotation (current + next).
    /// </summary>
    public static IReadOnlyList<string> TrustedKeys { get; set; } = [];

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "datapack");

    /// <summary>Overridable for tests.</summary>
    public static string InstallDir { get; set; } = DefaultDir;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static DataPackResult Verify(byte[] manifestBytes, string signatureBase64, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return new(false, "Aplicația nu are încă o cheie publică pentru pachetele de date; pachetul nu poate fi verificat, deci nu se instalează.");
        byte[] sig;
        try { sig = Convert.FromBase64String(signatureBase64.Trim()); }
        catch (FormatException) { return new(false, "Semnătura pachetului nu este validă (format greșit)."); }
        bool signed = false;
        foreach (var k in keys)
        {
            using var ecdsa = ECDsa.Create();
            try { ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k.Trim()), out _); }
            catch (Exception ex) when (ex is FormatException or CryptographicException) { continue; }
            if (ecdsa.VerifyData(manifestBytes, sig, HashAlgorithmName.SHA256)) { signed = true; break; }
        }
        if (!signed) return new(false, "Semnătura pachetului NU corespunde cheii aplicației: pachetul poate fi modificat sau provine din altă sursă. Nu se instalează.");
        DataPackManifest? m;
        try { m = JsonSerializer.Deserialize<DataPackManifest>(manifestBytes, Json); }
        catch (JsonException ex) { return new(false, "Manifestul pachetului nu poate fi citit: " + ex.Message); }
        if (m is null || m.Files is null || m.Files.Count == 0) return new(false, "Manifestul pachetului este gol.");
        var bad = m.Files.FirstOrDefault(f => !AllowedFiles.Contains(f.Name, StringComparer.OrdinalIgnoreCase));
        if (bad is not null) return new(false, $"Pachetul conține un fișier neașteptat („{bad.Name}”); se acceptă doar listele de detecție. Nu se instalează.");
        return new(true, "Semnătură verificată.", m);
    }

    /// <summary>The installed pack's manifest, verified again (signature and file hashes); null when none is installed or it no longer verifies.</summary>
    public static DataPackManifest? Installed(out string problem)
    {
        problem = "";
        var mf = Path.Combine(InstallDir, "manifest.json");
        if (!File.Exists(mf)) return null;
        try
        {
            var bytes = File.ReadAllBytes(mf);
            var v = Verify(bytes, File.ReadAllText(Path.Combine(InstallDir, "manifest.sig")), TrustedKeys);
            if (!v.Ok) { problem = v.Message; return null; }
            foreach (var f in v.Manifest!.Files)
                if (!HashMatches(Path.Combine(InstallDir, f.Name), f.Sha256)) { problem = $"Fișierul instalat „{f.Name}” a fost modificat după instalare; se folosesc listele livrate cu aplicația."; return null; }
            return v.Manifest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = "Pachetul instalat nu poate fi citit: " + ex.Message; return null; }
    }

    /// <summary>Imports a pack ZIP. Nothing is changed unless every check passes.</summary>
    public static DataPackResult Import(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            byte[] Bytes(string name)
            {
                var e = zip.GetEntry(name) ?? throw new InvalidDataException($"lipsește „{name}”");
                using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
            var manifest = Bytes("manifest.json");
            var v = Verify(manifest, Encoding.ASCII.GetString(Bytes("manifest.sig")), TrustedKeys);
            if (!v.Ok) return v;
            var installed = Installed(out _);
            if (installed is not null && v.Manifest!.Version <= installed.Version)
                return new(false, $"Pachetul are versiunea {v.Manifest.Version}, iar instalată este {installed.Version}: nu se instalează un pachet mai vechi sau la fel.", v.Manifest);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in v.Manifest!.Files)
            {
                var data = Bytes("data/" + f.Name);
                if (!Convert.ToHexString(SHA256.HashData(data)).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Amprenta fișierului „{f.Name}” nu corespunde manifestului: pachetul este deteriorat. Nu se instalează.", v.Manifest);
                files[f.Name] = data;
            }
            // install into a fresh folder, then swap, so a failure never leaves a half-installed pack
            var staging = InstallDir + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            foreach (var (n, d) in files) File.WriteAllBytes(Path.Combine(staging, n), d);
            File.WriteAllBytes(Path.Combine(staging, "manifest.json"), manifest);
            File.WriteAllBytes(Path.Combine(staging, "manifest.sig"), Bytes("manifest.sig"));
            var old = InstallDir + ".old";
            if (Directory.Exists(old)) Directory.Delete(old, true);
            if (Directory.Exists(InstallDir)) Directory.Move(InstallDir, old);
            Directory.Move(staging, InstallDir);
            return new(true, $"Pachetul de date „{v.Manifest.PackId}”, versiunea {v.Manifest.Version}, a fost verificat și instalat. Listele noi se folosesc de la următoarea pornire a aplicației.", v.Manifest);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(false, "Pachetul nu a putut fi citit: " + ex.Message);
        }
    }

    private static bool HashMatches(string path, string sha) =>
        File.Exists(path) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(sha, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a signed pack (used by the owner's signing tool and by tests).</summary>
    public static void Create(string zipPath, string packId, long version, IReadOnlyDictionary<string, string> files, ECDsa key)
    {
        var manifest = new DataPackManifest(packId, version, DateTimeOffset.UtcNow,
            files.Select(f => new DataPackFile(f.Key, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(f.Value))))).ToList());
        var mbytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        void Put(string name, byte[] data) { using var s = zip.CreateEntry(name).Open(); s.Write(data); }
        Put("manifest.json", mbytes);
        Put("manifest.sig", Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(mbytes, HashAlgorithmName.SHA256))));
        foreach (var (n, c) in files) Put("data/" + n, Encoding.UTF8.GetBytes(c));
    }
}

/// <summary>The detection data the rules read: the installed, verified data pack when there is one, otherwise the data shipped with the application.</summary>
public static class ShippedData
{
    private static readonly Lazy<(DataPackManifest? Pack, string Problem)> Pack = new(() => (DataPack.Installed(out var p), p));

    /// <summary>"livrată cu aplicația" or "pachet X versiunea N" — printed in reports so every result says which lists produced it.</summary>
    public static string Description => Pack.Value.Pack is { } m ? $"pachet de date „{m.PackId}” versiunea {m.Version} ({m.CreatedUtc:yyyy-MM-dd})" : "listele livrate cu aplicația";
    public static string Problem => Pack.Value.Problem;

    public static string Read(string file)
    {
        if (Pack.Value.Pack is { } m && m.Files.Any(f => f.Name.Equals(file, StringComparison.OrdinalIgnoreCase)))
            return File.ReadAllText(Path.Combine(DataPack.InstallDir, file));
        return ReadEmbedded(file);
    }

    /// <summary>The copy of a data file embedded in this assembly, ignoring any installed pack.</summary>
    public static string ReadEmbedded(string file)
    {
        var asm = typeof(ShippedData).Assembly;
        using var s = asm.GetManifestResourceStream("LogAnalyzer.Dfir.Analysis.Data." + file)
                      ?? throw new InvalidOperationException($"Resursa încorporată {file} lipsește din {asm.GetName().Name}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
