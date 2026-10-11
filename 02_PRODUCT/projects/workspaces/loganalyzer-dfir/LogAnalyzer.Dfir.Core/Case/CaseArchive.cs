using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Case;

public sealed record CaseArchiveFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size);

public sealed class CaseArchiveManifest
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = CaseArchive.Schema;
    [JsonPropertyName("case_id")] public string CaseId { get; set; } = "";
    [JsonPropertyName("created_utc")] public DateTimeOffset CreatedUtc { get; set; }
    [JsonPropertyName("tool")] public string Tool { get; set; } = "";
    [JsonPropertyName("files")] public List<CaseArchiveFile> Files { get; set; } = [];
}

public sealed record CaseArchiveResult(string ZipPath, string ZipSha256, string ManifestSha256, int FileCount, long Bytes);

public sealed record CaseArchiveVerification(bool Ok, string Summary, IReadOnlyList<string> Problems, string ManifestSha256, int FileCount, CaseArchiveManifest? Manifest);

/// <summary>
/// F4: verifiable case archive. A ZIP of the whole case folder (under "case/"), with manifest.json listing every file's SHA-256 and size, manifest.sha256
/// (the SHA-256 of manifest.json) and README.txt with manual verification commands (PowerShell Get-FileHash and certutil) that work without this
/// application. A sidecar "&lt;zip&gt;.sha256" holds the archive's own SHA-256 and the manifest's. Import verifies everything first and refuses a mismatch.
/// </summary>
public static class CaseArchive
{
    public const string Schema = "loganalyzer.case-archive/1";
    public const string ManifestName = "manifest.json", ManifestHashName = "manifest.sha256", ReadmeName = "README.txt", Prefix = "case/";

    public static string SidecarPath(string zipPath) => zipPath + ".sha256";

    public static CaseArchiveResult Export(CaseWorkspace ws, string zipPath, string tool = "LogAnalyzer")
    {
        ws.Audit("case.archive", "export spre " + Path.GetFileName(zipPath));
        var r = Export(ws.Root, ws.Info.CaseId, zipPath, tool);
        ws.Audit("case.archive_done", $"{Path.GetFileName(zipPath)} files={r.FileCount} manifest_sha256={r.ManifestSha256} zip_sha256={r.ZipSha256}");
        return r;
    }

    public static CaseArchiveResult Export(string caseRoot, string caseId, string zipPath, string tool = "LogAnalyzer")
    {
        caseRoot = Path.GetFullPath(caseRoot);
        var zipFull = Path.GetFullPath(zipPath);
        if (zipFull.StartsWith(caseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Arhiva nu se poate scrie în interiorul folderului cazului pe care îl arhivează.");
        var manifest = new CaseArchiveManifest { CaseId = caseId, CreatedUtc = DateTimeOffset.UtcNow, Tool = tool };
        if (File.Exists(zipFull)) File.Delete(zipFull);
        long bytes = 0;
        string manifestSha;
        using (var zip = ZipFile.Open(zipFull, ZipArchiveMode.Create))
        {
            foreach (var f in Directory.EnumerateFiles(caseRoot, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Prefix + Path.GetRelativePath(caseRoot, f).Replace('\\', '/');
                var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(f);
                using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = entry.Open();
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buf = new byte[81920]; int n; long size = 0;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) { sha.AppendData(buf, 0, n); dst.Write(buf, 0, n); size += n; }
                manifest.Files.Add(new CaseArchiveFile(rel, Convert.ToHexString(sha.GetHashAndReset()), size));
                bytes += size;
            }
            var mbytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            var msha = Convert.ToHexString(SHA256.HashData(mbytes));
            Put(zip, ManifestName, mbytes);
            Put(zip, ManifestHashName, Encoding.ASCII.GetBytes($"{msha}  {ManifestName}\r\n"));
            Put(zip, ReadmeName, Encoding.UTF8.GetBytes(Readme(manifest, msha)));
            manifestSha = msha;
        }
        var zsha = Sha256File(zipFull);
        File.WriteAllText(SidecarPath(zipFull), $"{zsha}  {Path.GetFileName(zipFull)}\r\n{manifestSha}  {ManifestName}\r\n", Encoding.ASCII);
        return new CaseArchiveResult(zipFull, zsha, manifestSha, manifest.Files.Count, bytes);
    }

    private static void Put(ZipArchive zip, string name, byte[] data)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(data);
    }

    /// <summary>The README inside the archive: what it is and how to verify it by hand on any Windows machine.</summary>
    public static string Readme(CaseArchiveManifest m, string manifestSha256) => string.Join("\r\n",
    [
        $"ARHIVA VERIFICABILĂ A CAZULUI {m.CaseId}",
        $"Creată (UTC): {m.CreatedUtc:yyyy-MM-dd HH:mm:ss} cu {m.Tool}. Fișiere: {m.Files.Count}.",
        "",
        "Conținut: case/ = folderul complet al cazului (probe, analiză, jurnale de custodie și audit);",
        "manifest.json = lista fiecărui fișier cu SHA-256 și dimensiune; manifest.sha256 = SHA-256 al lui manifest.json.",
        "",
        $"SHA-256 al lui manifest.json: {manifestSha256}",
        "Notați această valoare și în registrul de custodie: o copie în afara arhivei este ce dovedește că manifestul nu a fost înlocuit.",
        "",
        "VERIFICARE FĂRĂ LOGANALYZER (PowerShell, Windows 10/11):",
        "  Expand-Archive -LiteralPath .\\<arhiva>.zip -DestinationPath .\\verificare",
        "  Set-Location .\\verificare",
        "  (Get-FileHash -LiteralPath .\\manifest.json -Algorithm SHA256).Hash        # trebuie să fie valoarea de mai sus",
        "  $m = Get-Content -LiteralPath .\\manifest.json -Raw -Encoding UTF8 | ConvertFrom-Json",
        "  $bad = $m.files | Where-Object { (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256 }",
        "  if ($bad) { 'NEVERIFICAT:'; $bad.path } else { \"VERIFICAT: $($m.files.Count) fișiere identice\" }",
        "",
        "VERIFICARE CU CERTUTIL (cmd.exe; certutil afișează hash-ul cu litere mici, comparați fără a ține cont de majuscule):",
        "  certutil -hashfile manifest.json SHA256",
        "  certutil -hashfile \"case\\<cale din manifest>\" SHA256        (pentru fiecare fișier din manifest.json)",
        "",
        "VERIFICARE CU LOGANALYZER: LogAnalyzer.KeyGen verify-archive <arhiva>.zip, sau în aplicație „Importă arhivă de caz”, care verifică tot înainte de import.",
        "Fișierul <arhiva>.zip.sha256, ținut alături, conține SHA-256 al arhivei întregi (Get-FileHash <arhiva>.zip).",
        "",
    ]);

    /// <summary>Re-hashes every file of the archive against its manifest; also reports files that are not listed and a manifest that does not match manifest.sha256 or the sidecar.</summary>
    public static CaseArchiveVerification Verify(string zipPath, string? expectedManifestSha256 = null)
    {
        var problems = new List<string>();
        if (!File.Exists(zipPath)) return new(false, "Arhiva nu există.", ["arhiva lipsește"], "", 0, null);
        if (expectedManifestSha256 is null && File.Exists(SidecarPath(zipPath)))
        {
            var lines = File.ReadAllLines(SidecarPath(zipPath));
            var zl = lines.FirstOrDefault(l => l.EndsWith(Path.GetFileName(zipPath), StringComparison.OrdinalIgnoreCase));
            if (zl is not null && !zl.Split(' ')[0].Equals(Sha256File(zipPath), StringComparison.OrdinalIgnoreCase))
                problems.Add("SHA-256 al arhivei diferă de cel din fișierul alăturat .sha256");
            expectedManifestSha256 = lines.FirstOrDefault(l => l.EndsWith(ManifestName, StringComparison.Ordinal))?.Split(' ')[0];
        }
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var me = zip.GetEntry(ManifestName);
            if (me is null) return new(false, "Arhiva nu conține manifest.json: nu este o arhivă de caz verificabilă.", ["manifest.json lipsește"], "", 0, null);
            byte[] mbytes;
            using (var ms = new MemoryStream()) { using (var s = me.Open()) s.CopyTo(ms); mbytes = ms.ToArray(); }
            var msha = Convert.ToHexString(SHA256.HashData(mbytes));
            var manifest = JsonSerializer.Deserialize<CaseArchiveManifest>(mbytes);
            if (manifest is null || manifest.Schema != Schema) return new(false, "manifest.json are un format necunoscut.", ["schema necunoscută"], msha, 0, null);
            if (zip.GetEntry(ManifestHashName) is { } he)
            {
                using var r = new StreamReader(he.Open());
                var declared = r.ReadToEnd().Split(' ', '\r', '\n')[0];
                if (!declared.Equals(msha, StringComparison.OrdinalIgnoreCase)) problems.Add("manifest.json nu corespunde cu manifest.sha256");
            }
            else problems.Add("manifest.sha256 lipsește");
            if (expectedManifestSha256 is { Length: > 0 } exp && !exp.Equals(msha, StringComparison.OrdinalIgnoreCase))
                problems.Add($"SHA-256 al manifestului ({msha}) diferă de cel așteptat ({exp})");

            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in manifest.Files)
            {
                listed.Add(f.Path);
                if (!SafeEntry(f.Path)) { problems.Add("cale nepermisă în manifest: " + f.Path); continue; }
                var e = zip.GetEntry(f.Path);
                if (e is null) { problems.Add("LIPSĂ: " + f.Path); continue; }
                using var s = e.Open();
                var h = Convert.ToHexString(SHA256.HashData(s));
                if (!h.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) problems.Add("MODIFICAT: " + f.Path);
            }
            foreach (var e in zip.Entries)
                if (!listed.Contains(e.FullName) && e.FullName is not (ManifestName or ManifestHashName or ReadmeName) && !e.FullName.EndsWith('/'))
                    problems.Add("ÎN PLUS (nu e în manifest): " + e.FullName);
            var ok = problems.Count == 0;
            var summary = ok ? $"Arhivă verificată: {manifest.Files.Count} fișiere, fiecare cu SHA-256 identic cu manifestul; SHA-256 manifest {msha}."
                             : $"Arhiva NU este verificată: {problems.Count} probleme din {manifest.Files.Count} fișiere listate.";
            return new(ok, summary, problems, msha, manifest.Files.Count, manifest);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return new(false, "Arhiva nu poate fi citită: " + ex.Message, [ex.Message], "", 0, null);
        }
    }

    /// <summary>Verifies, then extracts the case into a new folder under <paramref name="casesRoot"/> and opens it (the open re-checks it again). A failed verification imports nothing.</summary>
    public static (CaseWorkspace? Case, CaseArchiveVerification Check) Import(string zipPath, string casesRoot)
    {
        var check = Verify(zipPath);
        if (!check.Ok) return (null, check);
        var id = string.Concat(check.Manifest!.CaseId.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (id.Length == 0) id = "CASE-IMPORT";
        var dest = Path.GetFullPath(Path.Combine(casesRoot, id));
        for (int i = 2; Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any(); i++) dest = Path.GetFullPath(Path.Combine(casesRoot, $"{id}-import{i}"));
        Directory.CreateDirectory(dest);
        using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var f in check.Manifest.Files)
            {
                var rel = f.Path[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(dest, rel));
                if (!target.StartsWith(dest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                zip.GetEntry(f.Path)!.ExtractToFile(target, overwrite: false);
            }
        var ws = CaseWorkspace.Open(dest);
        ws.Audit("case.imported_archive", $"{Path.GetFileName(zipPath)} files={check.FileCount} manifest_sha256={check.ManifestSha256}");
        return (ws, check);
    }

    private static bool SafeEntry(string p) =>
        p.StartsWith(Prefix, StringComparison.Ordinal) && !p.Split('/').Any(seg => seg is ".." or ".") && !p.Contains(':') && !p.Contains('\\');

    private static string Sha256File(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(f));
    }
}
