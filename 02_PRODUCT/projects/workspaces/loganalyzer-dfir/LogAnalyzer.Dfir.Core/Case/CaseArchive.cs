using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Reporting;

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

public sealed record CaseArchiveResult(string ZipPath, string ZipSha256, string ManifestSha256, int FileCount, long Bytes, bool Signed = false, string SignNote = "");

/// <summary>How the manifest of an archive was authenticated. Integrity alone (every file matches the manifest) proves nothing about who built the manifest.</summary>
public enum CaseArchiveAuth
{
    /// <summary>Nothing outside the archive vouches for the manifest: whoever holds the files can rebuild manifest, hash file and sidecar. The archive is "neverificabilă".</summary>
    None,
    /// <summary>The operator supplied the manifest SHA-256 (e.g. from the custody register) and it matches.</summary>
    ManifestHashMatched,
    /// <summary>manifest.json.sig is a valid ECDSA signature by a trusted key.</summary>
    SignatureValid,
}

public sealed record CaseArchiveVerification(bool Ok, string Summary, IReadOnlyList<string> Problems, string ManifestSha256, int FileCount, CaseArchiveManifest? Manifest,
                                             CaseArchiveAuth Auth = CaseArchiveAuth.None, string AuthDetail = "")
{
    public bool Authenticated => Auth != CaseArchiveAuth.None;
}

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

    public const string SignatureName = "manifest.json.sig";

    public static CaseArchiveResult Export(CaseWorkspace ws, string zipPath, string tool = "LogAnalyzer", ReportSigningSettings? signing = null)
    {
        ws.Audit("case.archive", "export spre " + Path.GetFileName(zipPath));
        var r = Export(ws.Root, ws.Info.CaseId, zipPath, tool, signing);
        ws.Audit("case.archive_done", $"{Path.GetFileName(zipPath)} files={r.FileCount} manifest_sha256={r.ManifestSha256} zip_sha256={r.ZipSha256} signed={r.Signed}");
        return r;
    }

    /// <summary>Exports the case folder. When <paramref name="signing"/> has a usable private key, manifest.json is signed (same ECDSA P-256 mechanism as the reports) into manifest.json.sig.</summary>
    public static CaseArchiveResult Export(string caseRoot, string caseId, string zipPath, string tool = "LogAnalyzer", ReportSigningSettings? signing = null)
    {
        caseRoot = Path.GetFullPath(caseRoot);
        var zipFull = Path.GetFullPath(zipPath);
        if (zipFull.StartsWith(caseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Arhiva nu se poate scrie în interiorul folderului cazului pe care îl arhivează.");
        var manifest = new CaseArchiveManifest { CaseId = caseId, CreatedUtc = DateTimeOffset.UtcNow, Tool = tool };
        if (File.Exists(zipFull)) File.Delete(zipFull);
        long bytes = 0;
        string manifestSha;
        bool signed = false; string signNote = signing is null || signing.PrivateKeyPath.Length == 0 ? "arhiva nu este semnată: nu este configurată o cheie de semnare" : "";
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
            if (signing is not null && signing.PrivateKeyPath.Length > 0)
            {
                var sig = SignManifest(mbytes, signing, out signNote);
                if (sig is not null) { Put(zip, SignatureName, sig); signed = true; signNote = ""; }
            }
            Put(zip, ReadmeName, Encoding.UTF8.GetBytes(Readme(manifest, msha)));
            manifestSha = msha;
        }
        var zsha = Sha256File(zipFull);
        File.WriteAllText(SidecarPath(zipFull), $"{zsha}  {Path.GetFileName(zipFull)}\r\n{manifestSha}  {ManifestName}\r\n", Encoding.ASCII);
        return new CaseArchiveResult(zipFull, zsha, manifestSha, manifest.Files.Count, bytes, signed, signNote);
    }

    /// <summary>The detached signature (JSON bytes) of manifest.json, or null with the reason when the key is missing or unreadable.</summary>
    private static byte[]? SignManifest(byte[] manifestBytes, ReportSigningSettings signing, out string note)
    {
        note = "";
        if (!File.Exists(signing.PrivateKeyPath)) { note = "arhiva nu este semnată: fișierul cheii de semnare nu este accesibil"; return null; }
        var tmp = Path.Combine(Path.GetTempPath(), "la-arch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var mp = Path.Combine(tmp, ManifestName);
            File.WriteAllBytes(mp, manifestBytes);
            using var key = ReportSignature.LoadPrivateKey(File.ReadAllText(signing.PrivateKeyPath));
            ReportSignature.Sign(mp, key, signing.Signer.Length > 0 ? signing.Signer : Environment.UserName);
            return File.ReadAllBytes(ReportSignature.SigPath(mp));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        {
            note = "arhiva nu este semnată: cheia de semnare nu a putut fi citită (" + ex.Message + ")";
            return null;
        }
        finally { try { Directory.Delete(tmp, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
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
        "Dacă arhiva conține manifest.json.sig, manifestul este semnat (ECDSA P-256): semnătura se verifică doar cu cheia publică de încredere primită de la emitent.",
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

    /// <summary>
    /// Re-hashes every file of the archive against its manifest; also reports files that are not listed and a manifest that does not match manifest.sha256.
    /// Integrity is not authenticity: <see cref="CaseArchiveVerification.Auth"/> says whether anything outside the archive vouches for the manifest, i.e. a valid
    /// signature (manifest.json.sig) by one of <paramref name="trustedPublicKeys"/>, or a manifest SHA-256 the operator supplied in <paramref name="expectedManifestSha256"/>
    /// (from the custody register). The sidecar ".sha256" next to the ZIP is cross-checked but never authenticates: whoever rebuilds the archive rebuilds it too.
    /// </summary>
    public static CaseArchiveVerification Verify(string zipPath, string? expectedManifestSha256 = null, IReadOnlyList<string>? trustedPublicKeys = null)
    {
        var problems = new List<string>();
        try
        {
            if (!File.Exists(zipPath)) return new(false, "Arhiva nu există.", ["arhiva lipsește"], "", 0, null);
            string? sidecarManifest = null;
            if (File.Exists(SidecarPath(zipPath)))
            {
                var lines = File.ReadAllLines(SidecarPath(zipPath));
                var zl = lines.FirstOrDefault(l => l.EndsWith(Path.GetFileName(zipPath), StringComparison.OrdinalIgnoreCase));
                if (zl is not null && !zl.Split(' ')[0].Equals(Sha256File(zipPath), StringComparison.OrdinalIgnoreCase))
                    problems.Add("SHA-256 al arhivei diferă de cel din fișierul alăturat .sha256");
                sidecarManifest = lines.FirstOrDefault(l => l.EndsWith(ManifestName, StringComparison.Ordinal))?.Split(' ')[0];
            }
            using var zip = ZipFile.OpenRead(zipPath);
            var me = zip.GetEntry(ManifestName);
            if (me is null) return new(false, "Arhiva nu conține manifest.json: nu este o arhivă de caz verificabilă.", ["manifest.json lipsește"], "", 0, null);
            byte[] mbytes;
            using (var ms = new MemoryStream()) { using (var s = me.Open()) s.CopyTo(ms); mbytes = ms.ToArray(); }
            var msha = Convert.ToHexString(SHA256.HashData(mbytes));
            CaseArchiveManifest? manifest;
            try { manifest = JsonSerializer.Deserialize<CaseArchiveManifest>(mbytes); }
            catch (JsonException) { manifest = null; }
            if (manifest is null || manifest.Schema != Schema) return new(false, "manifest.json are un format necunoscut.", ["schema necunoscută"], msha, 0, null);
            if (manifest.Files is null || manifest.Files.Any(f => f is null || string.IsNullOrEmpty(f.Path) || string.IsNullOrEmpty(f.Sha256)))
                return new(false, "manifest.json este invalid: lista de fișiere lipsește sau conține intrări goale.", ["lista de fișiere din manifest este invalidă"], msha, 0, null);
            manifest.CaseId ??= "";
            if (zip.GetEntry(ManifestHashName) is { } he)
            {
                using var r = new StreamReader(he.Open());
                var declared = r.ReadToEnd().Split(' ', '\r', '\n')[0];
                if (!declared.Equals(msha, StringComparison.OrdinalIgnoreCase)) problems.Add("manifest.json nu corespunde cu manifest.sha256");
            }
            else problems.Add("manifest.sha256 lipsește");
            if (sidecarManifest is { Length: > 0 } sc && !sc.Equals(msha, StringComparison.OrdinalIgnoreCase))
                problems.Add($"SHA-256 al manifestului ({msha}) diferă de cel din fișierul alăturat .sha256 ({sc})");
            var auth = CaseArchiveAuth.None; var authDetail = "";
            if (expectedManifestSha256 is { Length: > 0 } exp)
            {
                if (exp.Trim().Equals(msha, StringComparison.OrdinalIgnoreCase)) { auth = CaseArchiveAuth.ManifestHashMatched; authDetail = "SHA-256 al manifestului coincide cu valoarea furnizată de operator"; }
                else problems.Add($"SHA-256 al manifestului ({msha}) diferă de cel așteptat ({exp})");
            }
            if (zip.GetEntry(SignatureName) is { } se)
            {
                var sr = VerifyManifestSignature(mbytes, se, trustedPublicKeys ?? []);
                if (sr.Status == ReportSignatureStatus.Valid) { auth = CaseArchiveAuth.SignatureValid; authDetail = sr.Message; }
                else if (sr.Status == ReportSignatureStatus.UnknownKey) { if (auth == CaseArchiveAuth.None) authDetail = sr.Message; }
                else problems.Add("semnătura manifestului: " + sr.Message);
            }
            else if (auth == CaseArchiveAuth.None) authDetail = "arhiva nu este semnată și nu s-a furnizat SHA-256 al manifestului din registrul de custodie";

            // two entries with one name (also across case and / versus \) would let the file that is hashed differ from the file that is read back by name
            foreach (var dup in zip.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\')).GroupBy(e => e.FullName.Replace('\\', '/').ToUpperInvariant()).Where(g => g.Count() > 1))
                problems.Add("intrare dublată în arhivă: " + dup.First().FullName);
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
                if (!listed.Contains(e.FullName) && e.FullName is not (ManifestName or ManifestHashName or ReadmeName or SignatureName) && !e.FullName.EndsWith('/'))
                    problems.Add("ÎN PLUS (nu e în manifest): " + e.FullName);
            var ok = problems.Count == 0;
            var summary = !ok ? $"Arhiva NU este verificată: {problems.Count} probleme din {manifest.Files.Count} fișiere listate."
                : auth == CaseArchiveAuth.None ? $"Arhivă integră, dar neautentificată (nimeni nu garantează pentru ea): {manifest.Files.Count} fișiere, fiecare cu SHA-256 identic cu manifestul; SHA-256 manifest {msha}. Autenticitate NEVERIFICATĂ: {authDetail}."
                : $"Arhivă verificată: {manifest.Files.Count} fișiere, fiecare cu SHA-256 identic cu manifestul; SHA-256 manifest {msha}. Autenticitate: {authDetail}.";
            return new(ok, summary, problems, msha, manifest.Files.Count, manifest, auth, authDetail);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(false, "Arhiva nu poate fi citită: " + ex.Message, [ex.Message], "", 0, null);
        }
    }

    private static ReportSignatureResult VerifyManifestSignature(byte[] manifestBytes, ZipArchiveEntry sigEntry, IReadOnlyList<string> trusted)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "la-arch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var mp = Path.Combine(tmp, ManifestName); var sp = Path.Combine(tmp, SignatureName);
            File.WriteAllBytes(mp, manifestBytes);
            using (var s = sigEntry.Open()) using (var f = File.Create(sp)) s.CopyTo(f);
            return ReportSignature.Verify(mp, trusted, sp);
        }
        finally { try { Directory.Delete(tmp, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    /// <summary>
    /// Verifies, then extracts the case into a new folder under <paramref name="casesRoot"/> and opens it (the open re-checks it again). A failed verification imports nothing.
    /// An archive nobody vouches for (no valid signature by a trusted key, no operator-supplied manifest SHA-256) is "neverificabilă" and is refused unless the operator
    /// explicitly chooses <paramref name="importAsUnverified"/>, which is recorded in the audit log and in custody. Every extracted file is hashed while it is written and again
    /// from disk and compared with the manifest, so a swap between verification and extraction is detected; on any mismatch the partial import is deleted.
    /// <paramref name="afterExtract"/> is a test seam called with the folder once all files are written.
    /// </summary>
    public static (CaseWorkspace? Case, CaseArchiveVerification Check) Import(string zipPath, string casesRoot, string? expectedManifestSha256 = null,
        IReadOnlyList<string>? trustedPublicKeys = null, bool importAsUnverified = false) =>
        ImportCore(zipPath, casesRoot, expectedManifestSha256, trustedPublicKeys, importAsUnverified, null);

    internal static (CaseWorkspace? Case, CaseArchiveVerification Check) ImportCore(string zipPath, string casesRoot, string? expectedManifestSha256,
        IReadOnlyList<string>? trustedPublicKeys, bool importAsUnverified, Action<string>? afterExtract)
    {
        var check = Verify(zipPath, expectedManifestSha256, trustedPublicKeys);
        if (!check.Ok) return (null, check);
        if (!check.Authenticated && !importAsUnverified)
        {
            var msg = "Arhivă neverificabilă: " + check.AuthDetail + ". Importul este refuzat; furnizați SHA-256 al manifestului din registrul de custodie sau cheia publică a emitentului, ori alegeți explicit „importă ca neverificată”.";
            return (null, check with { Ok = false, Summary = msg, Problems = [.. check.Problems, "arhivă neverificabilă"] });
        }
        var rawId = check.Manifest!.CaseId;
        if (rawId.Length > 0 && (rawId.All(ch => ch == '.') || rawId.IndexOfAny(['/', '\\', ':']) >= 0))
            return (null, check with { Ok = false, Summary = "Arhiva NU a fost importată: identificatorul cazului din manifest nu este un nume de folder permis.", Problems = [.. check.Problems, "identificator de caz nepermis în manifest: " + rawId] });
        var id = string.Concat(rawId.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim().TrimEnd('.');
        if (id.Length == 0) id = "CASE-IMPORT";
        var dest = Path.GetFullPath(Path.Combine(casesRoot, id));
        for (int i = 2; Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any(); i++) dest = Path.GetFullPath(Path.Combine(casesRoot, $"{id}-import{i}"));
        Directory.CreateDirectory(dest);
        var bad = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var f in check.Manifest.Files)
            {
                var rel = f.Path[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(dest, rel));
                if (!target.StartsWith(dest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { bad.Add("cale în afara destinației: " + f.Path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var entry = zip.GetEntry(f.Path);
                if (entry is null) { bad.Add("LIPSĂ la extragere: " + f.Path); continue; }
                string written;
                using (var src = entry.Open())
                using (var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buf = new byte[81920]; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0) { sha.AppendData(buf, 0, n); dst.Write(buf, 0, n); }
                    written = Convert.ToHexString(sha.GetHashAndReset());
                }
                if (!written.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) bad.Add("MODIFICAT între verificare și extragere: " + f.Path);
            }
            afterExtract?.Invoke(dest);
            foreach (var f in check.Manifest.Files)
            {
                var target = Path.GetFullPath(Path.Combine(dest, f.Path[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(target)) { if (!bad.Any(b => b.EndsWith(f.Path, StringComparison.Ordinal))) bad.Add("LIPSĂ după extragere: " + f.Path); continue; }
                if (!Sha256File(target).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase) && !bad.Any(b => b.EndsWith(f.Path, StringComparison.Ordinal)))
                    bad.Add("MODIFICAT după extragere: " + f.Path);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            bad.Add("extragerea a eșuat: " + ex.Message);
        }
        if (bad.Count > 0)
        {
            try { Directory.Delete(dest, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return (null, check with { Ok = false, Summary = $"Arhiva NU a fost importată: {bad.Count} fișiere nu corespund manifestului la extragere.", Problems = [.. check.Problems, .. bad] });
        }
        var ws = CaseWorkspace.Open(dest);
        ws.Audit("case.imported_archive", $"{Path.GetFileName(zipPath)} files={check.FileCount} manifest_sha256={check.ManifestSha256} auth={check.Auth}");
        if (!check.Authenticated)
        {
            ws.Audit("case.imported_unverified_archive", $"{Path.GetFileName(zipPath)} importată ca NEVERIFICATĂ la alegerea operatorului; manifest_sha256={check.ManifestSha256}");
            ws.Custody(new CustodyEntry(DateTimeOffset.UtcNow, LogAnalyzer.Dfir.Auth.OperatorIdentity.Who, "archive.imported_unverified", "", Path.GetFileName(zipPath), ws.Info.CaseId, check.ManifestSha256,
                "LogAnalyzer", "", "", "arhivă neverificabilă importată la alegerea explicită a operatorului: " + check.AuthDetail));
            return (ws, check with { Summary = $"Importată ca NEVERIFICATĂ: {check.FileCount} fișiere identice cu manifestul, dar nimeni nu garantează pentru manifest ({check.AuthDetail}); SHA-256 manifest {check.ManifestSha256}." });
        }
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
