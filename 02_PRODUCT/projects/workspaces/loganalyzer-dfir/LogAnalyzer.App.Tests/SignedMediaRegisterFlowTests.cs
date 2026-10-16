using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner decision 2026-10-16: the signed media register through the real CMS path (SignedCms, the same signer as the officer's report signature). The card
/// is replaced by a software-key certificate with non-repudiation issued by an in-memory test CA; a CRL from it makes the chain confirmable. Test material only.
/// </summary>
public sealed class SignedMediaRegisterFlowTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_smrf_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { } }

    private static readonly X509Certificate2 Ca, Officer;
    private static readonly ParsedCrl Crl;

    static SignedMediaRegisterFlowTests()
    {
        var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=Test QTSP CA, O=Test", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caReq.PublicKey, false));
        Ca = caReq.CreateSelfSigned(Now.AddDays(-30), Now.AddYears(5));
        var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=Ofiter Test, O=Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Ca, true, false));
        using var pub = req.Create(Ca, Now.AddDays(-10), Now.AddYears(2), RandomNumberGenerator.GetBytes(8));
        Officer = pub.CopyWithPrivateKey(key);
        Crl = ParsedCrl.Parse(new CertificateRevocationListBuilder().Build(Ca, 1, Now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, Now.AddDays(-1)));
    }

    private static IReadOnlyList<SecurityOfficerRow> Officers => [OfficerRegistry.Identity(Officer)];
    private static QualifiedSignatureTrust Trust => new([Ca], [Crl]);

    private static SignedMediaEntry E(string inv, string serial) => new()
    {
        InventoryNumber = inv, UsbSerial = serial, VidPid = "0951:1666", CapacityBytes = 16_000_000_000, Classification = "SECRET DE SERVICIU", Holder = "Ion Popescu",
        BitLockerStatus = "criptat", EnrolledUtc = SignedMediaRegister.FormatUtc(Now),
    };

    [Fact]
    public void Signed_with_SignedCms_exported_imported_official_and_active()
    {
        var doc = SignedMediaRegister.NextVersion(null, null, [E("INV-1", "AA11")], "STATIE-TRANSFER", Now);
        var signed = MediaRegisterFlows.Sign(doc, Officer, Officers);
        var path = MediaRegisterFlows.Export(Path.Combine(_dir, "export"), signed);
        Assert.True(File.Exists(path + ".p7s"));
        var store = new SignedMediaRegisterStore(Path.Combine(_dir, "station"));
        var audit = new List<(string Ev, string Detail)>();
        var c = MediaRegisterFlows.Import(path, OfficerFingerprint.Of(Officer), store, Officers, Trust, Now, (e, d) => { audit.Add((e, d)); return true; });
        Assert.Equal(MediaRegisterVerdict.Oficial, c.Verdict);
        Assert.True(c.Installable, c.ToPlainText());
        var (ev, detail) = Assert.Single(audit);
        Assert.Equal(OperationAudit.MediaRegisterImported, ev);
        Assert.Contains("installed=da", detail);
        var active = MediaRegisterFlows.Active(store, null);
        Assert.True(active.Usable, active.Problem);
        Assert.Equal("AA11", Assert.Single(active.Doc!.Entries).UsbSerial);
    }

    [Fact]
    public void A_tampered_file_or_missing_signature_or_failed_audit_never_installs()
    {
        var doc = SignedMediaRegister.NextVersion(null, null, [E("INV-1", "AA11")], "STATIE-TRANSFER", Now);
        var signed = MediaRegisterFlows.Sign(doc, Officer, Officers);
        var path = MediaRegisterFlows.Export(Path.Combine(_dir, "export"), signed);
        var store = new SignedMediaRegisterStore(Path.Combine(_dir, "station"));
        // audit cannot be written: the version is not installed
        var noAudit = MediaRegisterFlows.Import(path, OfficerFingerprint.Of(Officer), store, Officers, Trust, Now, (_, _) => false);
        Assert.False(noAudit.Installable);
        Assert.False(MediaRegisterFlows.Active(store, null).Usable);
        // tampered entry (re-serialised canonically): the SignedCms signature does not verify
        doc.Entries[0].UsbSerial = "FALS99";
        File.WriteAllBytes(path, SignedMediaRegister.Canonical(doc));
        var tampered = MediaRegisterFlows.Import(path, OfficerFingerprint.Of(Officer), store, Officers, Trust, Now, (_, _) => true);
        Assert.Equal(MediaRegisterVerdict.Invalid, tampered.Verdict);
        File.Delete(path + ".p7s");
        Assert.Throws<FileNotFoundException>(() => MediaRegisterFlows.Import(path, OfficerFingerprint.Of(Officer), store, Officers, Trust, Now, (_, _) => true));
    }

    [Fact]
    public void A_certificate_that_is_not_a_registered_officer_cannot_sign()
    {
        var doc = SignedMediaRegister.NextVersion(null, null, [E("INV-1", "AA11")], "T", Now);
        Assert.Throws<InvalidOperationException>(() => MediaRegisterFlows.Sign(doc, Officer, []));
        var bad = SignedMediaRegister.NextVersion(null, null, [E("", "AA11")], "T", Now);
        Assert.Throws<InvalidOperationException>(() => MediaRegisterFlows.Sign(bad, Officer, Officers));
    }

    [Fact]
    public void Enrollment_takes_every_connected_removable_medium_with_its_identity_and_warns_on_unencrypted()
    {
        DeviceRecord[] devices =
        [
            new() { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\DISK&VEN_KINGSTON\AA11&0", Serial = "AA11", VidPid = "0951:1666", Manufacturer = "Kingston", Model = "DT", CapacityBytes = 16_000_000_000,
                    Encryption = "E: necriptat", ConnectedNow = true, VolumeSerials = ["1A2B-3C4D"] },
            new() { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\DISK&VEN_X\BB22&0", Serial = "BB22", Encryption = "F: criptat BitLocker", BitLockerVolumeId = "{AAAA}", ConnectedNow = true },
            new() { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\DISK&VEN_X\CC33&0", Serial = "CC33", ConnectedNow = false },   // history only: not enrolled
            new() { Kind = DeviceKind.InternalDisk, InstanceId = @"SCSI\DISK\1", Serial = "SYS1", ConnectedNow = true },               // not removable
            new() { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\DISK&VEN_X\7&1A2B&0", Serial = "", ConnectedNow = true },    // no unique serial
        ];
        var vm = new SignedMediaRegisterViewModel(new SignedMediaRegisterStore(Path.Combine(_dir, "ts")), () => devices);
        vm.EnrollConnectedCommand.Execute(null);
        Assert.Equal(["AA11", "BB22"], vm.Draft.Select(r => r.Entry.UsbSerial).ToArray());
        var aa = vm.Draft[0].Entry;
        Assert.Equal("necriptat", aa.BitLockerStatus);
        Assert.Equal(16_000_000_000, aa.CapacityBytes);
        Assert.Equal("1A2B-3C4D", aa.VolumeSerial);
        Assert.Equal("{AAAA}", vm.Draft[1].Entry.BitLockerVolumeId);
        Assert.Contains("NECRIPTAT", vm.Warnings);
        Assert.Contains("fără serie unică", vm.Status);
        // enrolling again does not duplicate
        vm.EnrollConnectedCommand.Execute(null);
        Assert.Equal(2, vm.Draft.Count);
        // a status change in the draft is dated
        vm.Draft[0].Status = "retras";
        var e = vm.Draft[0].ToEntry(Now);
        Assert.Equal("retras", e.Status);
        Assert.Equal(SignedMediaRegister.FormatUtc(Now), e.StatusChangedUtc);
    }
}
