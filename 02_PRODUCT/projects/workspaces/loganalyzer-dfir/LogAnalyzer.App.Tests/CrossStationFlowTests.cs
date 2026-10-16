using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner decision 2026-10-16: a station's media-history export signed by the officer at collection (real SignedCms), refused when tampered or unsigned, and the
/// officer's reconciliation report generated with the reserved field and signed with the card. The card is a software-key certificate issued by an in-memory
/// test CA; synthetic stations only. Test material only.
/// </summary>
public sealed class CrossStationFlowTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_xst_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { } }

    private static readonly X509Certificate2 Ca, Officer;
    private static readonly ParsedCrl Crl;

    static CrossStationFlowTests()
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
    private static readonly string H1 = new('a', 64);

    private static MediaHistoryDoc Station(string name, DateTimeOffset t, bool source)
    {
        var d = new MediaHistoryDoc
        {
            StationId = "HW-" + name, StationName = name, Edition = "Standalone", Posture = "Standalone", TimeZone = "UTC", ExportedUtc = SignedMediaRegister.FormatUtc(Now.AddMinutes(-1)),
            FileAuditing = "da",
        };
        var at = source ? t.AddMinutes(-5) : t.AddMinutes(40);
        d.Media.Add(new MediumHistory { Serial = "AA11", VidPid = "0951:1666", CapacityBytes = 16_000_000_000, Connections = [new MediumConnectionWindow { StartUtc = SignedMediaRegister.FormatUtc(at) }] });
        if (source)
            d.Transfers.Add(new MediaTransferEntry { RegistrationNumber = "TR-1", DateTime = t.UtcDateTime.ToString("yyyy-MM-dd HH:mm"), Direction = "ieșire", SourceStation = "STATIE-TRANSFER",
                                                     DestinationStation = "STATIE-DEST", MediumSerial = "AA11", Sha256 = H1, Approver = "Șef" });
        else d.PresentFiles.Add(new PresentFile { Path = @"D:\Clasificat\raport.docx", Sha256 = H1 });
        return d;
    }

    [Fact]
    public void An_export_is_signed_with_SignedCms_audited_first_and_accepted_tampered_or_unsigned_is_refused()
    {
        var t = new DateTimeOffset(Now.Year, Now.Month, Now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-1).AddHours(10);
        var signed = MediaHistoryFlows.Sign(Station("STATIE-TRANSFER", t, true), Officer, Officers);
        var audit = new List<(string Ev, string Detail)>();
        var path = MediaHistoryFlows.Export(Path.Combine(_dir, "mediu"), signed, (e, d) => { audit.Add((e, d)); return true; });
        var (ev, detail) = Assert.Single(audit);
        Assert.Equal(OperationAudit.MediaHistoryExported, ev);
        Assert.Contains("sha256=" + signed.Sha256, detail);
        Assert.True(File.Exists(path + ".p7s"));

        var ok = Assert.Single(MediaHistoryFlows.Load([path], Officers, Trust));
        Assert.True(ok.Accepted, ok.ToPlainText());

        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.UTF8.GetString(bytes).Replace("\"AA11\"", "\"AA12\"");
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        Assert.False(Assert.Single(MediaHistoryFlows.Load([path], Officers, Trust)).Accepted);
        File.WriteAllBytes(path, bytes);
        File.Delete(path + ".p7s");
        var unsigned = Assert.Single(MediaHistoryFlows.Load([path], Officers, Trust));
        Assert.False(unsigned.Accepted);
        Assert.Contains("nesemnat", unsigned.Headline);
    }

    [Fact]
    public void R5_the_export_carries_the_tamper_indicators_of_the_control_just_run()
    {
        var r = new LogAnalyzer.Dfir.Windows.Audit.ControlReport { Facts = new LogAnalyzer.Dfir.Windows.Audit.StationFacts { Host = "STATIE-DEST", CollectedUtc = Now, IsAdministrator = true } };
        r.Checks.Add(new("A05", "Audit", "Jurnale șterse", LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Neconform, "1102 la 2026-10-09", [], ""));
        r.Checks.Add(new("S05", "Software", "Ceasul", LogAnalyzer.Dfir.Windows.Audit.ControlStatus.Conform, "în ordine", [], ""));
        var doc = MediaHistoryFlows.FromControl(r, [], "HW-1", Now);
        Assert.Equal("NECONFORM", doc.Indicators.Single(i => i.Id == "A05").Status);
        Assert.Equal("1102 la 2026-10-09", doc.Indicators.Single(i => i.Id == "A05").Detail);
        Assert.Equal("CONFORM", doc.Indicators.Single(i => i.Id == "S05").Status);
        Assert.Equal(MediaHistoryExport.StatusMissing, doc.Indicators.Single(i => i.Id == "A11").Status);
    }

    [Fact]
    public void R5_paper_anchor_codes_are_typed_one_per_line_as_station_and_code()
    {
        var map = LogAnalyzer.UI.ViewModels.CrossStationViewModel.ParsePaperAnchors("STATIE-DEST = ABCDE-FGHIJ-KLMNO-PQRST\r\n\r\nstatie-transfer: 12345 67890 ABCDE FGHIJ\nfara-cod");
        Assert.Equal(2, map.Count);
        Assert.Equal("ABCDE-FGHIJ-KLMNO-PQRST", map["statie-dest"]);
        Assert.Equal("12345 67890 ABCDE FGHIJ", map["STATIE-TRANSFER"]);
    }

    [Fact]
    public void Without_the_audit_entry_nothing_is_written()
    {
        var signed = MediaHistoryFlows.Sign(Station("STATIE-TRANSFER", Now.AddDays(-1), true), Officer, Officers);
        Assert.Throws<InvalidOperationException>(() => MediaHistoryFlows.Export(Path.Combine(_dir, "mediu"), signed, (_, _) => false));
        Assert.False(Directory.Exists(Path.Combine(_dir, "mediu")));
    }

    [Fact]
    public void The_reconciliation_report_is_generated_with_the_reserved_field_and_signed_with_the_card()
    {
        var t = new DateTimeOffset(Now.Year, Now.Month, Now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-1).AddHours(10);
        var folder = Path.Combine(_dir, "mediu");
        var paths = new[] { Station("STATIE-TRANSFER", t, true), Station("STATIE-DEST", t, false) }
            .Select(d => MediaHistoryFlows.Export(folder, MediaHistoryFlows.Sign(d, Officer, Officers), (_, _) => true)).ToList();
        var checks = MediaHistoryFlows.Load(paths, Officers, Trust);
        var settings = new CrossStationSettings { FromUtc = Now.AddDays(-7), ToUtc = Now };
        var r = CrossStationReconciliation.Run(checks, null, "", settings);
        Assert.Contains(r.Findings, f => f.Kind == CrossStationReconciliation.KindTransferOk);

        var printed = new List<string>();
        LogAnalyzer.Dfir.Windows.Audit.CrossStationReportPdf.TextObserver.Value = printed.Add;
        var audit = new List<(string Ev, string Detail)>();
        var files = MediaHistoryFlows.ExportReport(Path.Combine(_dir, "raport"), r, settings, "STATIE-OFITER", "registru: niciunul", Officer, Officers, Now, (e, d) => { audit.Add((e, d)); return true; });
        LogAnalyzer.Dfir.Windows.Audit.CrossStationReportPdf.TextObserver.Value = null;

        Assert.Equal([OperationAudit.ReportGenerated, OperationAudit.MediaHistoryReconciled], audit.Select(a => a.Ev).ToArray());
        Assert.Contains(printed, p => p.StartsWith("CONFORM | STATIE-DEST | AA11 | TR-1", StringComparison.Ordinal));
        Assert.Contains(printed, p => p.Contains("Limitări"));
        var pdf = File.ReadAllBytes(files.SignedPdf);
        var check = PdfSignatureCheck.Check(pdf, SignedCmsVerifier.Instance);
        Assert.Null(check.Problem);
        Assert.Equal(OfficerFingerprint.Of(Officer), OfficerFingerprint.Of(check.Signer!));
        Assert.Contains("transfer_confirmat", File.ReadAllText(files.Csv));
    }
}
