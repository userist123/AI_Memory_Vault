using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner decision 2026-10-16: the media register is enrolled on the transfer station, signed by the security officer's qualified card certificate and
/// imported on every station; versions are chained (anti-rollback); the control (D04) and the transfer register use only the OFICIAL register in force.
/// The CMS is faked here (a SHA-256 of the content stands for the signature); the real SignedCms path is in LogAnalyzer.App.Tests. Test material only.
/// </summary>
public sealed class SignedMediaRegisterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_smr_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { } }

    // ───────── test PKI: a CA, an officer certificate with non-repudiation, a CRL ─────────

    private sealed class Pki
    {
        public readonly X509Certificate2 Ca, Officer, Other;
        public readonly ParsedCrl Crl;
        public Pki()
        {
            var caKey = RSA.Create(2048);
            var caReq = new CertificateRequest("CN=Test QTSP CA, O=Test", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            caReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caReq.PublicKey, false));
            Ca = caReq.CreateSelfSigned(Now.AddYears(-1), Now.AddYears(5));
            Officer = Issue("Ofiter Test"); Other = Issue("Altcineva Test");
            Crl = ParsedCrl.Parse(new CertificateRevocationListBuilder().Build(Ca, 1, Now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, Now.AddDays(-1)));
        }

        private X509Certificate2 Issue(string cn)
        {
            var key = RSA.Create(2048);
            var req = new CertificateRequest($"CN={cn}, O=Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature, true));
            req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Ca, true, false));
            return req.Create(Ca, Now.AddYears(-1), Now.AddYears(2), RandomNumberGenerator.GetBytes(8));
        }

        public QualifiedSignatureTrust Trust(bool withCrl = true) => new([Ca], withCrl ? [Crl] : []);
        public IReadOnlyList<SecurityOfficerRow> Officers => [OfficerRegistry.Identity(Officer)];
    }

    private static readonly Pki P = new();

    /// <summary>Stands for SignedCms: the "signature" is SHA-256 of the signed bytes, tied to one certificate.</summary>
    private sealed class FakeCms(X509Certificate2 signer) : ICmsDetachedVerifier
    {
        public static byte[] Sign(byte[] content) => SHA256.HashData(content);
        public CmsResult Verify(byte[] cms, byte[] content) =>
            cms.AsSpan().SequenceEqual(SHA256.HashData(content)) ? new(true, signer, "") : new(false, null, "semnătura nu corespunde conținutului");
    }

    private static SignedMediaEntry E(string inv, string serial, string status = "activ", string changed = "", long cap = 32_000_000_000, string vidpid = "0951:1666", string bl = "") => new()
    {
        InventoryNumber = inv, UsbSerial = serial, VidPid = vidpid, Vendor = "Kingston", Model = "DataTraveler", CapacityBytes = cap, VolumeSerial = "1A2B-3C4D",
        BitLockerStatus = "criptat", BitLockerVolumeId = bl, Classification = "SECRET DE SERVICIU", Holder = "Ion Popescu", Status = status, StatusChangedUtc = changed,
        EnrolledUtc = "2026-10-15T08:00:00Z",
    };

    private static (SignedMediaRegisterDoc Doc, byte[] Json, byte[] Cms) Issue(SignedMediaRegisterDoc? prev, string? prevSha, params SignedMediaEntry[] entries)
    {
        var doc = SignedMediaRegister.NextVersion(prev, prevSha, entries, "STATIE-TRANSFER", Now);
        var json = SignedMediaRegister.Canonical(doc);
        return (doc, json, FakeCms.Sign(json));
    }

    private static string PaperFp => OfficerFingerprint.Of(P.Officer);

    // ───────── canonical form ─────────

    [Fact]
    public void Canonical_form_has_sorted_keys_no_whitespace_no_bom_and_round_trips()
    {
        var (doc, json, _) = Issue(null, null, E("INV-1", "AA11"), E("INV-2", "ȘĂ22"));
        Assert.False(json.Length >= 3 && json[0] == 0xEF);
        var text = Encoding.UTF8.GetString(json);
        Assert.StartsWith("{\"entries\":[{\"bitlocker_status\":\"criptat\",\"bitlocker_volume_id\":\"\",\"capacity_bytes\":32000000000,", text);
        Assert.EndsWith(",\"format\":\"loganalyzer.media_register.v1\",\"issued_utc\":\"2026-10-16T09:00:00Z\",\"previous_version_sha256\":\"\",\"station_id\":\"STATIE-TRANSFER\",\"version\":1}", text);
        Assert.DoesNotContain(" \"", text);
        Assert.Contains("ȘĂ22", text);   // UTF-8, diacritics not escaped
        var back = SignedMediaRegister.Parse(json);
        Assert.Equal(json, SignedMediaRegister.Canonical(back));
        Assert.Equal(2, back.Entries.Count);
        Assert.Empty(SignedMediaRegister.Problems(doc));
    }

    [Fact]
    public void Non_canonical_bytes_are_refused_bom_whitespace_extra_key()
    {
        var (_, json, _) = Issue(null, null, E("INV-1", "AA11"));
        Assert.Throws<FormatException>(() => SignedMediaRegister.Parse([0xEF, 0xBB, 0xBF, .. json]));
        Assert.Throws<FormatException>(() => SignedMediaRegister.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("\"version\":1", "\"version\": 1"))));
        Assert.Throws<FormatException>(() => SignedMediaRegister.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("{\"entries\"", "{\"aaa\":\"x\",\"entries\""))));
    }

    [Fact]
    public void Content_problems_are_named()
    {
        var doc = SignedMediaRegister.NextVersion(null, null, [E("INV-1", "AA11"), E("INV-1", ""), E("INV-3", "CC33", status: "retras")], "T", Now);
        doc.Entries[2].Holder = "";
        var p = string.Join(" | ", SignedMediaRegister.Problems(doc));
        Assert.Contains("apare de mai multe ori", p);
        Assert.Contains("seria USB lipsește", p);
        Assert.Contains("nu are data schimbării stării", p);
        Assert.Contains("deținătorul lipsește", p);
        Assert.Contains("NU este criptat", string.Join(" ", SignedMediaRegister.Warnings(SignedMediaRegister.NextVersion(null, null, [E("I", "S") .With(e => e.BitLockerStatus = "necriptat")], "T", Now))));
    }

    // ───────── import: signature, officer, paper fingerprint ─────────

    [Fact]
    public void Official_with_paper_fingerprint_and_crl_is_installed_and_kept_in_history()
    {
        var (_, json, cms) = Issue(null, null, E("INV-1", "AA11"));
        var c = SignedMediaRegisterImport.Verify(json, cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), null);
        Assert.Equal(MediaRegisterVerdict.Oficial, c.Verdict);
        Assert.True(c.Installable, c.ToPlainText());
        var store = new SignedMediaRegisterStore(_dir);
        store.Record(c, json, cms, PaperFp, Now);
        var active = store.LoadActive(new FakeCms(P.Officer), 1);
        Assert.True(active.Usable, active.Problem);
        Assert.Equal(1, active.Doc!.Version);
        Assert.Single(store.LoadState().History);
    }

    [Fact]
    public void Without_paper_fingerprint_the_register_is_neoficial_and_not_installed()
    {
        var (_, json, cms) = Issue(null, null, E("INV-1", "AA11"));
        var c = SignedMediaRegisterImport.Verify(json, cms, new FakeCms(P.Officer), P.Officers, null, P.Trust(), null);
        Assert.Equal(MediaRegisterVerdict.Neoficial, c.Verdict);
        Assert.False(c.Installable);
        var store = new SignedMediaRegisterStore(_dir);
        store.Record(c, json, cms, null, Now);
        Assert.False(store.LoadActive(new FakeCms(P.Officer), null).Usable);   // a neoficial register is never used for compliance
        Assert.Equal("NEOFICIAL", Assert.Single(store.LoadState().History).Verdict);
        // without the CRL the chain/revocation cannot be confirmed: neoficial too
        Assert.Equal(MediaRegisterVerdict.Neoficial, SignedMediaRegisterImport.Verify(json, cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(withCrl: false), null).Verdict);
    }

    [Fact]
    public void Wrong_signer_or_wrong_paper_fingerprint_is_invalid()
    {
        var (_, json, cms) = Issue(null, null, E("INV-1", "AA11"));
        var notOfficer = SignedMediaRegisterImport.Verify(json, cms, new FakeCms(P.Other), P.Officers, null, P.Trust(), null);
        Assert.Equal(MediaRegisterVerdict.Invalid, notOfficer.Verdict);
        var wrongPaper = SignedMediaRegisterImport.Verify(json, cms, new FakeCms(P.Officer), P.Officers, OfficerFingerprint.Of(P.Other), P.Trust(), null);
        Assert.Equal(MediaRegisterVerdict.Invalid, wrongPaper.Verdict);
        Assert.Contains("procesul-verbal", wrongPaper.Headline);
    }

    [Fact]
    public void A_tampered_entry_breaks_the_signature()
    {
        var (doc, json, cms) = Issue(null, null, E("INV-1", "AA11"));
        doc.Entries[0].UsbSerial = "FALS99";
        var tampered = SignedMediaRegister.Canonical(doc);   // still canonical, but not what was signed
        var c = SignedMediaRegisterImport.Verify(tampered, cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), null);
        Assert.Equal(MediaRegisterVerdict.Invalid, c.Verdict);
        Assert.False(c.Installable);
    }

    [Fact]
    public void An_installed_file_changed_on_disk_is_reported_not_used()
    {
        var (_, json, cms) = Issue(null, null, E("INV-1", "AA11"));
        var store = new SignedMediaRegisterStore(_dir);
        store.Record(SignedMediaRegisterImport.Verify(json, cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), null), json, cms, PaperFp, Now);
        var file = Path.Combine(_dir, "versions", SignedMediaRegister.FileName(1));
        File.WriteAllText(file, Encoding.UTF8.GetString(json).Replace("AA11", "BB22"));
        var a = store.LoadActive(new FakeCms(P.Officer), 1);
        Assert.False(a.Usable);
        Assert.Contains("modificat după instalare", a.Problem);
    }

    // ───────── anti-rollback ─────────

    [Fact]
    public void Versions_must_increase_and_continue_the_installed_hash()
    {
        var store = new SignedMediaRegisterStore(_dir);
        var v1 = Issue(null, null, E("INV-1", "AA11"));
        store.Record(SignedMediaRegisterImport.Verify(v1.Json, v1.Cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), null), v1.Json, v1.Cms, PaperFp, Now);
        var sha1 = SignedMediaRegister.Sha256Hex(v1.Json);
        var v2 = Issue(v1.Doc, sha1, E("INV-1", "AA11", status: "retras", changed: "2026-10-16T08:00:00Z"), E("INV-2", "BB22"));
        var c2 = SignedMediaRegisterImport.Verify(v2.Json, v2.Cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), store.Installed());
        Assert.True(c2.Installable, c2.ToPlainText());
        store.Record(c2, v2.Json, v2.Cms, PaperFp, Now);

        // replaying version 1 is refused
        var replay = SignedMediaRegisterImport.Verify(v1.Json, v1.Cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), store.Installed());
        Assert.False(replay.Installable);
        Assert.Contains("mai veche", replay.Headline);

        // a version 3 that does not continue version 2 (forked from version 1) is refused
        var fork = Issue(v1.Doc, sha1, E("INV-9", "ZZ99"));
        fork.Doc.Version = 3; var forkJson = SignedMediaRegister.Canonical(fork.Doc);
        var forked = SignedMediaRegisterImport.Verify(forkJson, FakeCms.Sign(forkJson), new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), store.Installed());
        Assert.False(forked.Installable);
        Assert.Contains("lanțul versiunilor", forked.Headline);
        Assert.Equal(2, store.LoadActive(new FakeCms(P.Officer), 2).Doc!.Version);
    }

    [Fact]
    public void An_older_version_put_back_by_hand_is_caught_by_the_audit()
    {
        var store = new SignedMediaRegisterStore(_dir);
        var v1 = Issue(null, null, E("INV-1", "AA11"));
        store.Record(SignedMediaRegisterImport.Verify(v1.Json, v1.Cms, new FakeCms(P.Officer), P.Officers, PaperFp, P.Trust(), null), v1.Json, v1.Cms, PaperFp, Now);
        var details = new[]
        {
            OperationAudit.MediaRegisterDetail(1, "a", "OFICIAL", true, PaperFp, "ok"),
            OperationAudit.MediaRegisterDetail(2, "b", "OFICIAL", true, PaperFp, "ok"),
            OperationAudit.MediaRegisterDetail(3, "c", "NEOFICIAL", false, PaperFp, "x; y"),
        };
        Assert.Equal(2, SignedMediaRegisterStore.LastInstalledVersion(details));
        var a = store.LoadActive(new FakeCms(P.Officer), SignedMediaRegisterStore.LastInstalledVersion(details));
        Assert.False(a.Usable);
        Assert.Contains("versiune mai veche a fost repusă", a.Problem);
        Assert.Contains("șters în afara aplicației", new SignedMediaRegisterStore(Path.Combine(_dir, "gol")).LoadActive(new FakeCms(P.Officer), 2).Problem);
    }

    // ───────── identity matching ─────────

    private static SeenMedium M(string serial, string vidpid = "0951:1666", long? cap = 32_010_000_000, string bl = "", bool now = false, params DateTimeOffset[] seen) =>
        new(serial, vidpid, cap, bl, "Kingston DataTraveler", now, seen);

    [Fact]
    public void Identity_uses_several_attributes_and_flags_a_cloned_serial()
    {
        SignedMediaEntry[] reg = [E("INV-1", "AA11", bl: "{1111-AAAA}")];
        Assert.Equal(SignedMediaMatchKind.Registered, SignedMediaMatcher.Match(reg, M("aa11&0")).Kind);   // USBSTOR suffix, case
        Assert.Equal(SignedMediaMatchKind.Registered, SignedMediaMatcher.Match(reg, M("AA11", bl: "{1111-aaaa}")).Kind);
        Assert.Equal(SignedMediaMatchKind.NotRegistered, SignedMediaMatcher.Match(reg, M("XX99")).Kind);
        Assert.Equal(SignedMediaMatchKind.NotRegistered, SignedMediaMatcher.Match(reg, M("")).Kind);
        var clone = SignedMediaMatcher.Match(reg, M("AA11", vidpid: "abcd:0001"));
        Assert.Equal(SignedMediaMatchKind.CloneSuspected, clone.Kind);
        Assert.Contains("posibilă clonare a serialului", clone.Text);
        Assert.Equal(SignedMediaMatchKind.CloneSuspected, SignedMediaMatcher.Match(reg, M("AA11", cap: 64_000_000_000)).Kind);
        Assert.Equal(SignedMediaMatchKind.CloneSuspected, SignedMediaMatcher.Match(reg, M("AA11", bl: "{2222-BBBB}")).Kind);
    }

    [Fact]
    public void A_retired_medium_seen_after_its_date_is_a_violation_before_it_is_not()
    {
        SignedMediaEntry[] reg = [E("INV-1", "AA11", status: "retras", changed: "2026-10-10T00:00:00Z")];
        Assert.Equal(SignedMediaMatchKind.UsedAfterRetirement, SignedMediaMatcher.Match(reg, M("AA11", seen: new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero))).Kind);
        Assert.Equal(SignedMediaMatchKind.UsedAfterRetirement, SignedMediaMatcher.Match(reg, M("AA11", now: true)).Kind);
        Assert.Equal(SignedMediaMatchKind.Registered, SignedMediaMatcher.Match(reg, M("AA11", seen: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))).Kind);
        Assert.Equal(SignedMediaMatchKind.RetiredTimeUnknown, SignedMediaMatcher.Match(reg, M("AA11")).Kind);
    }

    // ───────── control check D04 ─────────

    private static ControlCheck D04(StationPosture posture, ActiveMediaRegister? reg, params SeenMedium[] seen) =>
        ControlEvaluator.Evaluate(new StationFacts { Posture = posture, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, ObservedMedia = [.. seen], SignedMedia = reg })
                        .Checks.Single(c => c.Id == "D04");

    private static ActiveMediaRegister Active(params SignedMediaEntry[] e) => new(SignedMediaRegister.NextVersion(null, null, e, "T", Now), "abc", "");

    [Fact]
    public void D04_results_per_posture_and_case()
    {
        var reg = Active(E("INV-1", "AA11"), E("INV-2", "BB22", status: "distrus", changed: "2026-10-01T00:00:00Z"));
        Assert.Equal(ControlStatus.Conform, D04(StationPosture.Standalone, reg, M("AA11")).Status);
        var unknown = D04(StationPosture.Standalone, reg, M("AA11"), M("XX99"));
        Assert.Equal(ControlStatus.Neconform, unknown.Status);
        Assert.Contains(unknown.Evidence, e => e.Contains("mediu neînregistrat"));
        Assert.Equal(ControlStatus.Neconform, D04(StationPosture.IsolatedNetwork, reg, M("XX99")).Status);
        Assert.Equal(ControlStatus.Neconform, D04(StationPosture.Standalone, reg, M("BB22", seen: new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero))).Status);
        Assert.Equal(ControlStatus.DeVerificat, D04(StationPosture.Standalone, reg, M("AA11", vidpid: "1234:5678")).Status);
        Assert.Equal(ControlStatus.DeVerificat, D04(StationPosture.Standalone, reg).Status);   // nothing seen is never "conform"
        Assert.Equal(ControlStatus.NuSeAplica, D04(StationPosture.Internet, reg, M("XX99")).Status);
        // no register in force: NECONFORM as soon as a medium was seen; not checked / not read: NEDETERMINAT
        Assert.Equal(ControlStatus.Neconform, D04(StationPosture.Standalone, ActiveMediaRegister.None("niciun registru"), M("AA11")).Status);
        Assert.Equal(ControlStatus.Neconform, D04(StationPosture.Standalone, new ActiveMediaRegister(null, "f00", "modificat")).Status);
        Assert.Equal(ControlStatus.Nedeterminat, D04(StationPosture.Standalone, null, M("AA11")).Status);
        Assert.Equal(ControlStatus.Nedeterminat, ControlEvaluator.Evaluate(new StationFacts { Posture = StationPosture.Standalone, SignedMedia = reg }).Checks.Single(c => c.Id == "D04").Status);
    }

    // ───────── transfer register linkage ─────────

    [Fact]
    public void A_transfer_row_approves_only_a_medium_of_the_signed_register()
    {
        var t0 = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
        AccessRecord A(int id, DateTimeOffset t, string obj, string mask, string handle) => new("event", "Security", id, t, $"Security {id} RecordID {id}{t:HHmmss}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SubjectUserName"] = "ion", ["SubjectDomainName"] = "STATIE", ["ObjectType"] = "File", ["ObjectName"] = obj, ["AccessMask"] = mask,
                ["HandleId"] = handle, ["ProcessId"] = "0x123", ["ProcessName"] = @"C:\Windows\explorer.exe",
            });
        ClassifiedInput Input(SignedMediaSet? signed) => new()
        {
            Folders = [new ClassifiedRoot(@"D:\Clasificat")], Records = [A(4663, t0, @"D:\Clasificat\plan.docx", "0x1", "0x1a4"), A(4663, t0.AddMinutes(1), @"E:\plan.docx", "0x2", "0x9")],
            Media = [new MediumSeen(t0.AddMinutes(-3), "SN1", "Kingston")], RemovableDrives = new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase), RegisterZone = TimeZoneInfo.Utc,
            Transfers = new TransferRegister { Rows = [new TransferRow { RegistrationNumber = "TR-1", DateTime = "2026-10-14 09:05", Direction = "ieșire", Content = "plan.docx", MediumSerial = "SN1", MediumType = "stick USB", Person = "ion", Approver = "sef" }] },
            SignedMedia = signed,
        };
        Assert.Equal(ExitVerdict.Approved, Assert.Single(ClassifiedAccess.Analyze(Input(SignedMediaMatcher.ActiveSet(SignedMediaRegister.NextVersion(null, null, [E("INV-1", "SN1")], "T", Now)))).Exits).Verdict);
        var notIn = Assert.Single(ClassifiedAccess.Analyze(Input(SignedMediaMatcher.ActiveSet(SignedMediaRegister.NextVersion(null, null, [E("INV-1", "ALT")], "T", Now)))).Exits);
        Assert.Equal(ExitVerdict.Unjustified, notIn.Verdict);
        Assert.Contains("mediul nu este în registrul semnat", notIn.Detail);
        Assert.Equal(ExitVerdict.Unjustified, Assert.Single(ClassifiedAccess.Analyze(Input(SignedMediaMatcher.ActiveSet(null))).Exits).Verdict);   // no register in force
        Assert.Equal(ExitVerdict.Approved, Assert.Single(ClassifiedAccess.Analyze(Input(null)).Exits).Verdict);   // not supplied (offline case): unchanged
    }
}

internal static class SignedMediaTestExtensions
{
    public static SignedMediaEntry With(this SignedMediaEntry e, Action<SignedMediaEntry> change) { change(e); return e; }
}
