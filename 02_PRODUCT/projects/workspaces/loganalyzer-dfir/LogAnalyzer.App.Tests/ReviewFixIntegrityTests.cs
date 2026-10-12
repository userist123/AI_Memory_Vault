using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Regression tests for the independent review of signatures and case archives: masked signer, null fields in .sig / manifest, I/O errors in the verify paths,
/// authenticated archive import (signature or custody-register hash), re-hash after extraction, protected report key. Test keys and synthetic files only.
/// </summary>
public sealed class ReviewFixIntegrityTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private string Pdf(string name = "raport.pdf")
    {
        var p = Path.Combine(_k.Dir, name);
        File.WriteAllBytes(p, Encoding.ASCII.GetBytes("%PDF-1.7\n% test report body\n%%EOF\n"));
        return p;
    }

    private (ReportSigningSettings Settings, string Pub) Signing(string signer = "Laborator Test")
    {
        var (priv, pub) = ReportSignature.GenerateKey();
        var keyFile = Path.Combine(_k.Dir, "keys", Guid.NewGuid().ToString("N") + ".p8");
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        File.WriteAllText(keyFile, priv);
        return (new ReportSigningSettings { PrivateKeyPath = keyFile, Signer = signer, TrustedPublicKeys = [pub] }, pub);
    }

    // ---- 2: masked export must not carry the Windows user name ----

    [Fact]
    public void A_masked_report_is_signed_with_the_given_pseudonym_never_the_windows_user_name()
    {
        var (settings, pub) = Signing(signer: "");           // no configured label: the default would be Environment.UserName
        var pdf = Pdf();
        var line = settings.SignIfConfigured(pdf, "Utilizator-1");
        Assert.Contains("Raport semnat digital", line);
        var sig = File.ReadAllText(pdf + ".sig");
        Assert.Equal("Utilizator-1", JsonDocument.Parse(sig).RootElement.GetProperty("signer").GetString());
        Assert.DoesNotContain(Environment.UserName, sig, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, line, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ReportSignatureStatus.Valid, ReportSignature.Verify(pdf, [pub]).Status);
    }

    [Fact]
    public void The_masked_export_path_of_the_view_model_signs_with_the_pseudonym_not_the_operator_name()
    {
        var (settings, pub) = Signing(signer: "");
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel { SigningKeyPath = settings.PrivateKeyPath, SignerName = "", TrustedKeysText = pub };
        var masker = new LogAnalyzer.Dfir.Privacy.PiiMasker();
        var signer = vm.MaskedSigner(masker);
        Assert.StartsWith("Utilizator-", signer);
        var pdf = Pdf();
        vm.SignReport(pdf, null, signer);
        var sig = File.ReadAllText(pdf + ".sig");
        Assert.Equal(signer, JsonDocument.Parse(sig).RootElement.GetProperty("signer").GetString());
        Assert.DoesNotContain(Environment.UserName, sig, StringComparison.OrdinalIgnoreCase);
        // a configured label goes through the masker too
        vm.SignerName = "Laborator DFIR";
        Assert.Equal("Laborator DFIR", vm.MaskedSigner(masker));
    }

    // ---- 5: malformed input fails cleanly ----

    [Theory]
    [InlineData("{\"schema\":\"loganalyzer.report-signature/1\",\"signature\":null,\"public_key\":null,\"sha256\":null,\"signer\":null,\"key_id\":null,\"file\":null,\"algorithm\":null}")]
    [InlineData("{\"schema\":\"loganalyzer.report-signature/1\",\"signature\":\"AAAA\",\"public_key\":\"AAAA\",\"sha256\":null}")]
    [InlineData("{\"schema\":null}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void A_signature_file_with_null_fields_is_INVALID_not_a_crash(string json)
    {
        var (_, pub) = Signing();
        var pdf = Pdf();
        File.WriteAllText(pdf + ".sig", json);
        var r = ReportSignature.Verify(pdf, [pub]);
        Assert.Equal(ReportSignatureStatus.Invalid, r.Status);
        Assert.Equal("INVALID", r.Word);
    }

    [Fact]
    public void A_null_signer_or_key_id_with_a_real_key_is_handled_without_an_exception()
    {
        var (_, pub) = Signing();
        var pdf = Pdf();
        var json = "{\"schema\":\"loganalyzer.report-signature/1\",\"file\":null,\"sha256\":\"AA\",\"size\":1,\"signer\":null,\"key_id\":null,\"public_key\":\"" + pub + "\",\"signature\":\"AAAA\",\"signed_utc\":\"2026-10-01T10:00:00+00:00\"}";
        File.WriteAllText(pdf + ".sig", json);
        var r = ReportSignature.Verify(pdf, [pub]);
        Assert.NotEqual(ReportSignatureStatus.Valid, r.Status);
    }

    [Fact]
    public void An_unreadable_signature_file_is_a_clear_result_and_a_nonzero_exit_on_the_command_line()
    {
        var (_, pub) = Signing();
        var pdf = Pdf();
        File.WriteAllText(pdf + ".sig", "{}");
        using var hold = new FileStream(pdf + ".sig", FileMode.Open, FileAccess.ReadWrite, FileShare.None);    // another process holds it: IOException on read
        var r = ReportSignature.Verify(pdf, [pub]);
        Assert.Equal(ReportSignatureStatus.Invalid, r.Status);
        Assert.Contains("nu poate fi citit", r.Message);
        var o = new StringWriter();
        Assert.NotEqual(0, ReportCli.Verify(["verify-report", pdf, pub], o));
        Assert.Contains("INVALID", o.ToString());
    }

    // ---- 4 / 5: case archive ----

    private (CaseWorkspace Ws, string Zip) MakeCase(ReportSigningSettings? signing, out CaseArchiveResult result)
    {
        var ws = _k.Case(_k.File("in/a.bin", "alpha"), _k.File("in/b.bin", "beta"));
        var zip = Path.Combine(_k.Dir, "out", Guid.NewGuid().ToString("N")[..6] + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        result = CaseArchive.Export(ws, zip, signing: signing);
        return (ws, zip);
    }

    private static void ReplaceEntry(string zip, string entry, string content)
    {
        using var z = ZipFile.Open(zip, ZipArchiveMode.Update);
        z.GetEntry(entry)?.Delete();
        using var w = new StreamWriter(z.CreateEntry(entry).Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static void DeleteEntry(string zip, string entry)
    {
        using var z = ZipFile.Open(zip, ZipArchiveMode.Update);
        z.GetEntry(entry)?.Delete();
    }

    /// <summary>The attacker: changes a case file, rebuilds manifest.json and manifest.sha256, and removes manifest.json.sig and the sidecar.</summary>
    private static void Forge(string zip)
    {
        string target;
        CaseArchiveManifest m;
        using (var z = ZipFile.OpenRead(zip))
        {
            using var r = new StreamReader(z.GetEntry("manifest.json")!.Open());
            m = JsonSerializer.Deserialize<CaseArchiveManifest>(r.ReadToEnd())!;
        }
        target = m.Files.First(f => f.Path.StartsWith("case/Raw/", StringComparison.Ordinal)).Path;
        const string forged = "continut falsificat";
        ReplaceEntry(zip, target, forged);
        var files = m.Files.Select(f => f.Path == target ? new CaseArchiveFile(f.Path, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(forged))), Encoding.UTF8.GetByteCount(forged)) : f).ToList();
        m.Files = files;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, new JsonSerializerOptions { WriteIndented = true });
        ReplaceEntry(zip, "manifest.json", Encoding.UTF8.GetString(bytes));
        ReplaceEntry(zip, "manifest.sha256", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)))) + "  manifest.json\r\n");
        DeleteEntry(zip, "manifest.json.sig");
        if (File.Exists(zip + ".sha256")) File.Delete(zip + ".sha256");
    }

    [Fact]
    public void A_rebuilt_manifest_with_the_side_files_removed_is_refused_as_unverifiable()
    {
        var (settings, _) = Signing();
        var (_, zip) = MakeCase(settings, out var original);
        Forge(zip);
        // the forged archive is internally consistent...
        var plain = CaseArchive.Verify(zip);
        Assert.True(plain.Ok, string.Join("; ", plain.Problems));
        Assert.False(plain.Authenticated);
        // ...but nothing vouches for it: refused, even with the trusted key configured
        var dest = Path.Combine(_k.Dir, "imported");
        var (ws, check) = CaseArchive.Import(zip, dest, trustedPublicKeys: settings.TrustedPublicKeys);
        Assert.Null(ws);
        Assert.Contains("Arhivă neverificabilă", check.Summary);
        Assert.False(Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any());
        // the manifest hash the operator wrote in the custody register at export exposes the replacement
        var (ws2, check2) = CaseArchive.Import(zip, dest, original.ManifestSha256, settings.TrustedPublicKeys);
        Assert.Null(ws2);
        Assert.Contains(check2.Problems, p => p.Contains("diferă de cel așteptat"));
    }

    [Fact]
    public void A_forged_manifest_next_to_the_original_signature_is_a_signature_failure()
    {
        var (settings, _) = Signing();
        var (_, zip) = MakeCase(settings, out _);
        string sig;
        using (var z = ZipFile.OpenRead(zip)) { using var r = new StreamReader(z.GetEntry("manifest.json.sig")!.Open()); sig = r.ReadToEnd(); }
        Forge(zip);
        ReplaceEntry(zip, "manifest.json.sig", sig);       // the attacker keeps the signature of the original manifest
        var check = CaseArchive.Verify(zip, trustedPublicKeys: settings.TrustedPublicKeys);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.StartsWith("semnătura manifestului", StringComparison.Ordinal));
        Assert.Null(CaseArchive.Import(zip, Path.Combine(_k.Dir, "imported"), trustedPublicKeys: settings.TrustedPublicKeys).Case);
    }

    [Fact]
    public void A_validly_signed_archive_imports_and_a_signature_by_an_untrusted_key_does_not_authenticate()
    {
        var (settings, pub) = Signing();
        var (ws, zip) = MakeCase(settings, out var res);
        Assert.True(res.Signed);
        var check = CaseArchive.Verify(zip, trustedPublicKeys: [pub]);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        Assert.Equal(CaseArchiveAuth.SignatureValid, check.Auth);

        var (imported, c2) = CaseArchive.Import(zip, Path.Combine(_k.Dir, "imported"), trustedPublicKeys: [pub]);
        Assert.NotNull(imported);
        Assert.True(c2.Authenticated);
        Assert.Equal(ws.Info.CaseId, imported!.Info.CaseId);
        Assert.Equal(0, imported.LastRecheck!.ModifiedCount + imported.LastRecheck.MissingCount);

        var (_, otherPub) = ReportSignature.GenerateKey();
        var strange = CaseArchive.Verify(zip, trustedPublicKeys: [otherPub]);
        Assert.True(strange.Ok);
        Assert.False(strange.Authenticated);
        Assert.Null(CaseArchive.Import(zip, Path.Combine(_k.Dir, "imported2"), trustedPublicKeys: [otherPub]).Case);
    }

    [Fact]
    public void An_unsigned_archive_imports_with_the_custody_register_hash_or_as_unverified_recorded_in_custody()
    {
        var (_, zip) = MakeCase(null, out var res);
        Assert.False(res.Signed);
        var refused = CaseArchive.Import(zip, Path.Combine(_k.Dir, "i0"));
        Assert.Null(refused.Case);
        Assert.Contains("Arhivă neverificabilă", refused.Check.Summary);

        var byHash = CaseArchive.Import(zip, Path.Combine(_k.Dir, "i1"), res.ManifestSha256);
        Assert.NotNull(byHash.Case);
        Assert.Equal(CaseArchiveAuth.ManifestHashMatched, byHash.Check.Auth);

        var (unver, check) = CaseArchive.Import(zip, Path.Combine(_k.Dir, "i2"), importAsUnverified: true);
        Assert.NotNull(unver);
        Assert.False(check.Authenticated);
        Assert.Contains("case.imported_unverified_archive", File.ReadAllText(unver!.AppAuditLogPath));
        Assert.Contains("archive.imported_unverified", File.ReadAllText(unver.CustodyJsonlPath));
    }

    [Fact]
    public void A_file_swapped_after_verification_is_detected_after_extraction_and_the_import_is_removed()
    {
        var (_, zip) = MakeCase(null, out var res);
        var dest = Path.Combine(_k.Dir, "imported");
        var (ws, check) = CaseArchive.ImportCore(zip, dest, res.ManifestSha256, null, false, afterExtract: folder =>
        {
            var victim = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).First(f => f.Contains("Raw", StringComparison.Ordinal));
            File.SetAttributes(victim, FileAttributes.Normal);
            File.WriteAllText(victim, "schimbat dupa extragere");
        });
        Assert.Null(ws);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.StartsWith("MODIFICAT după extragere", StringComparison.Ordinal));
        Assert.False(Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any());
    }

    [Theory]
    [InlineData("{\"schema\":\"loganalyzer.case-archive/1\",\"case_id\":\"X\",\"files\":null}")]
    [InlineData("{\"schema\":\"loganalyzer.case-archive/1\",\"case_id\":\"X\",\"files\":[null]}")]
    [InlineData("{\"schema\":\"loganalyzer.case-archive/1\",\"case_id\":\"X\",\"files\":[{\"path\":null,\"sha256\":null,\"size\":1}]}")]
    [InlineData("null")]
    public void A_manifest_with_null_files_or_entries_is_an_invalid_archive_not_a_crash(string manifestJson)
    {
        var (_, zip) = MakeCase(null, out var res);
        ReplaceEntry(zip, "manifest.json", manifestJson);
        File.Delete(zip + ".sha256");
        var check = CaseArchive.Verify(zip, res.ManifestSha256);
        Assert.False(check.Ok);
        Assert.Null(CaseArchive.Import(zip, Path.Combine(_k.Dir, "imported"), importAsUnverified: true).Case);
    }

    [Fact]
    public void An_unreadable_archive_is_a_clear_failure_in_the_library_and_exit_2_on_the_command_line()
    {
        var (_, zip) = MakeCase(null, out _);
        using var hold = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var check = CaseArchive.Verify(zip);
        Assert.False(check.Ok);
        Assert.Contains("nu poate fi citită", check.Summary);
        var o = new StringWriter();
        Assert.Equal(2, ReportCli.VerifyArchive(["verify-archive", zip], o));
        Assert.StartsWith("NOT VERIFIED", o.ToString());
    }

    [Fact]
    public void The_command_line_says_UNAUTHENTICATED_for_an_intact_archive_nobody_vouches_for()
    {
        var (_, zip) = MakeCase(null, out var res);
        var o = new StringWriter();
        Assert.Equal(5, ReportCli.VerifyArchive(["verify-archive", zip], o));
        Assert.StartsWith("UNAUTHENTICATED", o.ToString());
        var ok = new StringWriter();
        Assert.Equal(0, ReportCli.VerifyArchive(["verify-archive", zip, res.ManifestSha256], ok));
        Assert.StartsWith("VERIFIED", ok.ToString());
        var (settings, pub) = Signing();
        var (_, signedZip) = MakeCase(settings, out _);
        var sk = new StringWriter();
        Assert.Equal(0, ReportCli.VerifyArchive(["verify-archive", signedZip, pub], sk));
    }

    // ---- 7: the report signing key is protected ----

    [Fact]
    public void Report_keygen_writes_a_DPAPI_protected_key_readable_only_by_the_current_user_that_still_signs()
    {
        var priv = Path.Combine(_k.Dir, "keys", "semnare.key"); var pubFile = Path.Combine(_k.Dir, "keys", "semnare.pub");
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.Keygen(["report-keygen", priv, pubFile], o));
        var text = File.ReadAllText(priv);
        Assert.StartsWith(ReportKeyProtection.DpapiPrefix, text);
        Assert.DoesNotContain(text[ReportKeyProtection.DpapiPrefix.Length..], o.ToString());
        var raw = Convert.FromBase64String(text[ReportKeyProtection.DpapiPrefix.Length..]);
        Assert.NotEmpty(raw);
        // ACL: inheritance removed, a single allow rule, for the current user
        var sec = new FileInfo(priv).GetAccessControl();
        Assert.True(sec.AreAccessRulesProtected);
        var rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.Single(rules);
        Assert.Equal(WindowsIdentity.GetCurrent().User, rules[0].IdentityReference);
        Assert.Equal(AccessControlType.Allow, rules[0].AccessControlType);
        // it signs through the normal path
        var pdf = Pdf();
        Assert.Equal(0, ReportCli.Sign(["report-sign", priv, pdf, "Operator Test"], new StringWriter()));
        Assert.Equal(ReportSignatureStatus.Valid, ReportSignature.Verify(pdf, [File.ReadAllText(pubFile)]).Status);
        Assert.Contains("Raport semnat digital", new ReportSigningSettings { PrivateKeyPath = priv, Signer = "S" }.SignIfConfigured(Pdf("al-doilea.pdf")));
    }

    [Fact]
    public void Report_keygen_can_protect_the_key_with_a_passphrase_and_a_wrong_one_fails()
    {
        var priv = Path.Combine(_k.Dir, "keys", "parola.key"); var pubFile = Path.Combine(_k.Dir, "keys", "parola.pub");
        Assert.Equal(0, ReportCli.Keygen(["report-keygen", priv, pubFile, "--passphrase"], new StringWriter(), new StringReader("parola de test 123\nparola de test 123\n")));   // typed twice
        var text = File.ReadAllText(priv);
        Assert.StartsWith(ReportKeyProtection.PassphrasePrefix, text);
        Assert.True(ReportKeyProtection.NeedsPassphrase(text));
        using (var key = ReportSignature.LoadPrivateKey(text, "parola de test 123")) Assert.Equal(ReportSignature.KeyId(File.ReadAllText(pubFile)), ReportSignature.KeyId(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        Assert.ThrowsAny<CryptographicException>(() => ReportSignature.LoadPrivateKey(text, "alta parola"));
        Assert.ThrowsAny<CryptographicException>(() => ReportSignature.LoadPrivateKey(text));
        // without the passphrase the app says it did not sign, it does not crash
        Assert.Contains("NU este semnat", new ReportSigningSettings { PrivateKeyPath = priv }.SignIfConfigured(Pdf()));
        var pdf = Pdf("p.pdf");
        Assert.Equal(0, ReportCli.Sign(["report-sign", priv, pdf, "Operator Test"], new StringWriter(), new StringReader("parola de test 123\n")));
        Assert.Equal(ReportSignatureStatus.Valid, ReportSignature.Verify(pdf, [File.ReadAllText(pubFile)]).Status);
    }

    [Fact]
    public void A_bare_key_file_from_before_still_loads_and_a_key_file_is_never_overwritten()
    {
        var (priv, _) = ReportSignature.GenerateKey();
        using (ReportSignature.LoadPrivateKey(priv)) { }
        var path = Path.Combine(_k.Dir, "keys", "x.key");
        Assert.Equal(0, ReportCli.Keygen(["report-keygen", path, "--plain"], new StringWriter()));
        Assert.False(ReportKeyProtection.IsProtected(File.ReadAllText(path)));
        Assert.Equal(1, ReportCli.Keygen(["report-keygen", path], new StringWriter()));
    }
}
