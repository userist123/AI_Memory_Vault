using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Auth;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.12: signed offline threat-intelligence packages, staged, approved, rolled back, verified again on every load.</summary>
public sealed class ThreatIntelPackagesTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_ti_" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private sealed class Pki
    {
        public readonly X509Certificate2 Ca, Publisher, Other;
        public readonly ParsedCrl Crl;
        public Pki()
        {
            var caKey = RSA.Create(2048);
            var caReq = new CertificateRequest("CN=Test TI CA, O=Test", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            caReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caReq.PublicKey, false));
            Ca = caReq.CreateSelfSigned(Now.AddYears(-1), Now.AddYears(5));
            Publisher = Issue("CERT Test"); Other = Issue("Altcineva Test");
            Crl = ParsedCrl.Parse(new CertificateRevocationListBuilder().Build(Ca, 1, Now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, Now.AddDays(-1)));
        }
        private X509Certificate2 Issue(string cn)
        {
            var req = new CertificateRequest($"CN={cn}, O=Test", RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature, true));
            req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Ca, true, false));
            return req.Create(Ca, Now.AddYears(-1), Now.AddYears(2), RandomNumberGenerator.GetBytes(8));
        }
        public QualifiedSignatureTrust Trust() => new([Ca], [Crl]);
        public IReadOnlyList<string> Trusted => [OfficerFingerprint.Of(Publisher)];
    }

    private static readonly Pki P = new();

    private sealed class FakeCms(X509Certificate2 signer) : ICmsDetachedVerifier
    {
        public static byte[] Sign(byte[] content) => SHA256.HashData(content);
        public CmsResult Verify(byte[] cms, byte[] content) =>
            cms.AsSpan().SequenceEqual(SHA256.HashData(content)) ? new(true, signer, "") : new(false, null, "semnătura nu corespunde conținutului");
    }

    private static readonly FakeCms Cms = new(P.Publisher);

    /// <summary>Writes a package folder: one IOC list, its manifest and the "signature".</summary>
    private string Package(string name, int version, string previousSha = "", string ioc = "Type,Value,Context\nsha256,aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,test\n", string? path = null)
    {
        var dir = Path.Combine(_dir, "src", name);
        Directory.CreateDirectory(Path.Combine(dir, "ioc"));
        var rel = path ?? "ioc/cert.csv";
        File.WriteAllText(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)), ioc);
        var m = new ThreatIntelManifest
        {
            Format = ThreatIntelManifest.FormatId, PackageId = "CERT-TEST", Version = version, PreviousVersionSha256 = previousSha, IssuedUtc = Now.AddMinutes(-5),
            ValidUntilUtc = Now.AddDays(30), Publisher = "CERT Test",
            Files = [new ThreatIntelFile(rel, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ioc))))],
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(m);
        File.WriteAllBytes(Path.Combine(dir, ThreatIntelManifest.FileName), json);
        File.WriteAllBytes(Path.Combine(dir, ThreatIntelManifest.SignatureName), FakeCms.Sign(json));
        return dir;
    }

    private ThreatIntelStore Store() => new(Path.Combine(_dir, "store"));

    private static string ShaOf(string dir) => ThreatIntelStore.Sha(File.ReadAllBytes(Path.Combine(dir, ThreatIntelManifest.FileName)));

    [Fact]
    public void Nothing_is_used_before_approval_then_the_approved_rules_load_into_the_detection_engine()
    {
        var s = Store();
        var (check, staged) = s.Stage(Package("v1", 1), "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(check.Ok, check.Headline);
        Assert.NotNull(staged);
        Assert.Null(s.LoadActive(Cms, P.Trust(), P.Trusted).RulesRoot);

        Assert.Contains("motiv", s.Approve(staged!.Id, "ioana", "", Cms, P.Trust(), P.Trusted).Result.Reason);
        Assert.True(s.Approve(staged.Id, "ioana", "pachet lunar CERT", Cms, P.Trust(), P.Trusted).Result.Ok);
        var active = s.LoadActive(Cms, P.Trust(), P.Trusted);
        Assert.Equal(("CERT-TEST", 1), (active.PackageId, active.Version));
        var engine = DetectionEngine.Load(active.RulesRoot!);
        Assert.Contains(engine.Rules, r => r.RuleId.Contains("cert.csv", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ChainStatus.Valid, s.VerifyHistory().Status);
    }

    [Fact]
    public void A_package_signed_by_someone_off_the_list_or_with_a_changed_file_or_a_bad_path_is_refused_and_not_staged()
    {
        var s = Store();
        var (c1, st1) = s.Stage(Package("x", 1), "ana", new FakeCms(P.Other), P.Trust(), P.Trusted);
        Assert.Equal("semnatarul nu este publicator de încredere", c1.Headline);
        Assert.Null(st1);

        var tampered = Package("t", 1);
        File.AppendAllText(Path.Combine(tampered, "ioc", "cert.csv"), "sha256,bbbb,adăugat\n");
        Assert.Equal("un fișier de reguli a fost modificat", s.Stage(tampered, "ana", Cms, P.Trust(), P.Trusted).Check.Headline);

        Assert.Equal("manifestul nu este valid", s.Stage(Package("p", 1, path: "ioc/../../evil.csv"), "ana", Cms, P.Trust(), P.Trusted).Check.Headline);
        Assert.Empty(s.Staged());
        Assert.Equal(3, s.HistoryEvents().Count(e => e.Kind == "refused"));
    }

    [Fact]
    public void Only_listed_files_are_copied_and_a_staged_copy_changed_while_waiting_is_not_approved()
    {
        var s = Store();
        var src = Package("v1", 1);
        File.WriteAllText(Path.Combine(src, "ioc", "extra.csv"), "Type,Value\nip,1.2.3.4\n");     // not in the manifest
        var (_, staged) = s.Stage(src, "ana", Cms, P.Trust(), P.Trusted);
        var stagedDir = Path.Combine(s.Root, "Staging", staged!.Id);
        Assert.False(File.Exists(Path.Combine(stagedDir, "ioc", "extra.csv")));
        File.AppendAllText(Path.Combine(stagedDir, "ioc", "cert.csv"), "ip,6.6.6.6,strecurat\n");
        var (r, _) = s.Approve(staged.Id, "ioana", "ok", Cms, P.Trust(), P.Trusted);
        Assert.Contains("nu se mai verifică", r.Reason);
        Assert.Null(s.LoadActive(Cms, P.Trust(), P.Trusted).RulesRoot);
    }

    [Fact]
    public void A_new_version_must_continue_the_highest_approved_and_rollback_is_explicit_and_does_not_reopen_old_versions()
    {
        var s = Store();
        var v1 = Package("v1", 1);
        var (_, st1) = s.Stage(v1, "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st1!.Id, "ioana", "v1", Cms, P.Trust(), P.Trusted).Result.Ok);

        Assert.Contains("nu continuă", s.Stage(Package("fork", 2, previousSha: new string('0', 64)), "ana", Cms, P.Trust(), P.Trusted).Check.Headline);
        var (_, st2) = s.Stage(Package("v2", 2, previousSha: ShaOf(v1), ioc: "Type,Value\nip,10.0.0.9,c2\n"), "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st2!.Id, "ana", "v2", Cms, P.Trust(), P.Trusted).Result.Ok);
        Assert.Contains(s.HistoryEvents(), e => e.Kind == "approved" && e.Detail.Contains("aceeași persoană"));
        Assert.Equal(2, s.LoadActive(Cms, P.Trust(), P.Trusted).Version);

        Assert.Contains("nu este mai nouă", s.Stage(Package("replay", 1), "ana", Cms, P.Trust(), P.Trusted).Check.Headline);   // re-import of v1 refused
        Assert.Contains("motiv", s.Rollback("CERT-TEST", 1, "ioana", "").Reason);
        Assert.True(s.Rollback("CERT-TEST", 1, "ioana", "v2 dă fals pozitive").Ok);
        Assert.Equal(1, s.LoadActive(Cms, P.Trust(), P.Trusted).Version);
        Assert.Contains("nu este mai nouă", s.Stage(Package("v2again", 2, previousSha: ShaOf(v1)), "ana", Cms, P.Trust(), P.Trusted).Check.Headline);
        Assert.Contains("nu a fost aprobată", s.Rollback("CERT-TEST", 7, "ioana", "x").Reason);
    }

    [Fact]
    public void The_active_package_is_not_used_when_its_files_its_pointer_or_the_history_were_changed()
    {
        var s = Store();
        var (_, st) = s.Stage(Package("v1", 1), "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st!.Id, "ioana", "v1", Cms, P.Trust(), P.Trusted).Result.Ok);
        var csv = Path.Combine(s.Root, "Versions", "CERT-TEST-v1", "ioc", "cert.csv");
        var original = File.ReadAllText(csv);
        File.AppendAllText(csv, "ip,6.6.6.6,strecurat\n");
        Assert.Contains("nu se mai verifică", s.LoadActive(Cms, P.Trust(), P.Trusted).Problem);
        File.WriteAllText(csv, original);
        Assert.NotNull(s.LoadActive(Cms, P.Trust(), P.Trusted).RulesRoot);

        File.WriteAllText(Path.Combine(s.Root, "active.json"), File.ReadAllText(Path.Combine(s.Root, "active.json")).Replace("\"version\": 1", "\"version\": 9"));
        Assert.Contains("nu corespunde ultimei aprobări", s.LoadActive(Cms, P.Trust(), P.Trusted).Problem);

        var lines = File.ReadAllLines(s.HistoryPath);
        lines[0] = lines[0].Replace("\"ana\"", "\"mallory\"");
        File.WriteAllLines(s.HistoryPath, lines);
        Assert.Contains("compromis", s.LoadActive(Cms, P.Trust(), P.Trusted).Problem);
    }

    [Fact]
    public void The_pipeline_uses_the_approved_package_records_it_in_the_run_and_shows_a_package_that_did_not_verify_as_a_gap()
    {
        var s = Store();
        var (_, st) = s.Stage(Package("v1", 1), "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st!.Id, "ioana", "v1", Cms, P.Trust(), P.Trusted).Result.Ok);
        var active = s.LoadActive(Cms, P.Trust(), P.Trusted);

        var evidence = Path.Combine(_dir, "note.txt");
        File.WriteAllText(evidence, "x");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "ti", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [evidence]);
        var r = new InvestigationPipeline { ThreatIntel = active }.Run(ws, CollectionProfile.Quick, collect: false);
        Assert.Contains(r.RulesUsed, x => x.RuleId.Contains("cert.csv", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("CERT-TEST v1", r.Run!.Settings["pachet_ti"]);
        Assert.Contains("threat_intel.used", File.ReadAllText(ws.AppAuditLogPath));

        var r2 = new InvestigationPipeline { ThreatIntel = ActiveThreatIntel.None("pachetul activ nu se mai verifică: un fișier de reguli a fost modificat") }
            .Run(ws, CollectionProfile.Quick, collect: false);
        Assert.Contains(r2.Gaps, g => g.Artifact == "Pachetul de informații despre amenințări" && g.Reason.Contains("modificat"));
        Assert.DoesNotContain(r2.RulesUsed, x => x.RuleId.Contains("cert.csv", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("setarea pachet_ti CERT-TEST v1 -> nu", r2.RunDiff!.ToolchainChanges);
    }

    // ---- review 2026-11-08: regressions ----

    private (ThreatIntelStore Store, ActiveThreatIntel Active) Approved(ThreatIntelStore? store = null)
    {
        var s = store ?? Store();
        var (_, st) = s.Stage(Package("v1", 1), "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st!.Id, "ioana", "v1", Cms, P.Trust(), P.Trusted).Result.Ok);
        return (s, s.LoadActive(Cms, P.Trust(), P.Trusted, Path.Combine(_dir, "snap")));
    }

    [Fact]
    public void P1_a_rule_file_planted_in_the_version_folder_stops_the_package_and_the_engine_reads_only_a_snapshot_of_verified_bytes()
    {
        var (s, active) = Approved();
        Assert.StartsWith(Path.Combine(_dir, "snap"), active.RulesRoot);                                      // never the store folder
        Assert.Single(Directory.EnumerateFiles(active.RulesRoot!, "*", SearchOption.AllDirectories));
        File.WriteAllText(Path.Combine(s.Root, "Versions", "CERT-TEST-v1", "ioc", "plantat.csv"), "Type,Value\nip,6.6.6.6\n");
        Assert.Contains("fișiere nesemnate", s.LoadActive(Cms, P.Trust(), P.Trusted).Problem);
    }

    [Fact]
    public void P2_with_a_sealed_history_cutting_its_end_to_bring_back_an_older_version_breaks_it()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var s = new ThreatIntelStore(Path.Combine(_dir, "sealed"), key, @sealed: true);
        var v1 = Package("v1", 1);
        var (_, st1) = s.Stage(v1, "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st1!.Id, "ioana", "v1", Cms, P.Trust(), P.Trusted).Result.Ok);
        var oldActive = File.ReadAllText(Path.Combine(s.Root, "active.json"));
        var linesAtV1 = File.ReadAllLines(s.HistoryPath);
        var (_, st2) = s.Stage(Package("v2", 2, previousSha: ShaOf(v1)), "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st2!.Id, "ioana", "v2", Cms, P.Trust(), P.Trusted).Result.Ok);

        File.WriteAllLines(s.HistoryPath, linesAtV1);
        File.WriteAllText(Path.Combine(s.Root, "active.json"), oldActive);
        Assert.Equal(ChainStatus.Broken, s.VerifyHistory().Status);
        Assert.Contains("compromis", s.LoadActive(Cms, P.Trust(), P.Trusted).Problem);
    }

    [Fact]
    public void P3_an_active_pointer_without_history_is_not_used()
    {
        var (s, _) = Approved();
        File.Delete(s.HistoryPath);
        Assert.Contains("istoricul pachetelor lipsește", s.LoadActive(Cms, P.Trust(), P.Trusted).Problem);
    }

    [Fact]
    public void P5_P6_malformed_signed_or_unsigned_input_is_a_refusal_never_an_exception_and_leaves_no_staging_folder()
    {
        var s = Store();
        var bad = Path.Combine(_dir, "src", "nulls");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, ThreatIntelManifest.FileName), "{\"format\":\"loganalyzer.ti_package/1\",\"package_id\":\"X\",\"version\":1,\"files\":null}");
        File.WriteAllText(Path.Combine(bad, ThreatIntelManifest.SignatureName), "x");
        Assert.False(s.Stage(bad, "ana", Cms, P.Trust(), P.Trusted).Check.Ok);
        File.WriteAllText(Path.Combine(bad, ThreatIntelManifest.FileName), "{\"format\":\"loganalyzer.ti_package/1\",\"package_id\":\"X\",\"version\":1,\"files\":[null]}");
        Assert.False(s.Stage(bad, "ana", Cms, P.Trust(), P.Trusted).Check.Ok);
        Assert.Contains("identificatorul pachetului", string.Join(" ", new ThreatIntelManifest { Format = ThreatIntelManifest.FormatId, PackageId = "A|B*", Version = 1,
            Files = [new ThreatIntelFile("ioc/a.csv", new string('a', 64))] }.Problems()));
        var staging = Path.Combine(s.Root, "Staging");
        Assert.True(!Directory.Exists(staging) || Directory.GetDirectories(staging).Length == 0);
    }

    [Fact]
    public void An_expired_package_is_used_but_says_so()
    {
        var s = Store();
        var dir = Package("old", 1);
        var m = ThreatIntelManifest.Parse(File.ReadAllBytes(Path.Combine(dir, ThreatIntelManifest.FileName)));
        m.ValidUntilUtc = Now.AddDays(-1);
        var json = JsonSerializer.SerializeToUtf8Bytes(m);
        File.WriteAllBytes(Path.Combine(dir, ThreatIntelManifest.FileName), json);
        File.WriteAllBytes(Path.Combine(dir, ThreatIntelManifest.SignatureName), FakeCms.Sign(json));
        var (_, st) = s.Stage(dir, "ana", Cms, P.Trust(), P.Trusted);
        Assert.True(s.Approve(st!.Id, "ioana", "x", Cms, P.Trust(), P.Trusted).Result.Ok);
        var a = s.LoadActive(Cms, P.Trust(), P.Trusted);
        Assert.NotNull(a.RulesRoot);
        Assert.Contains("a expirat", a.Problem);
    }

    [Fact]
    public void Round2_a_missing_seal_key_or_an_unsafe_store_is_said_as_such_and_refuses_everything_and_snapshots_do_not_pile_up()
    {
        var noKey = new ThreatIntelStore(Path.Combine(_dir, "nokey"), null, @sealed: true);
        Assert.Contains("cheia de sigilare", noKey.LoadActive(Cms, P.Trust(), P.Trusted).Problem);
        Assert.Contains("cheia de sigilare", noKey.Stage(Package("k", 1), "ana", Cms, P.Trust(), P.Trusted).Check.Headline);

        var unsafeStore = new ThreatIntelStore(Path.Combine(_dir, "unsafe"), refusal: "depozitul pachetelor are drepturi nesigure");
        Assert.Equal("depozitul pachetelor are drepturi nesigure", unsafeStore.Stage(Package("u", 1), "ana", Cms, P.Trust(), P.Trusted).Check.Headline);
        Assert.Contains("drepturi nesigure", unsafeStore.Rollback("CERT-TEST", 1, "ioana", "x").Reason);

        var (s, _) = Approved();
        s.LoadActive(Cms, P.Trust(), P.Trusted, Path.Combine(_dir, "snap"));
        s.LoadActive(Cms, P.Trust(), P.Trusted, Path.Combine(_dir, "snap"));
        Assert.Single(Directory.GetDirectories(Path.Combine(_dir, "snap")));
    }
}
