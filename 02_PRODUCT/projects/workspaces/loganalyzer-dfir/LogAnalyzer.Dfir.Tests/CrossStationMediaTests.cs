using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner decision 2026-10-16: every station exports a signed history of removable media; the security officer reconciles the exports of all stations.
/// Synthetic stations only (STATIE-TRANSFER, STATIE-DEST, STATIE-X, STATIE-LIPSA); the CMS is faked (SHA-256 of the content tied to one certificate),
/// the real SignedCms path is in LogAnalyzer.App.Tests. Test material only.
/// </summary>
public sealed class CrossStationMediaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly string H1 = new('a', 64), H2 = new('b', 64);

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

    private sealed class FakeCms(X509Certificate2 signer) : ICmsDetachedVerifier
    {
        public static byte[] Sign(byte[] content) => SHA256.HashData(content);
        public CmsResult Verify(byte[] cms, byte[] content) =>
            cms.AsSpan().SequenceEqual(SHA256.HashData(content)) ? new(true, signer, "") : new(false, null, "semnătura nu corespunde conținutului");
    }

    // ───────── synthetic register and stations ─────────

    private static SignedMediaEntry E(string inv, string serial) => new()
    {
        InventoryNumber = inv, UsbSerial = serial, VidPid = "0951:1666", Vendor = "Kingston", Model = "DataTraveler", CapacityBytes = 32_000_000_000, BitLockerStatus = "criptat",
        Classification = "SECRET DE SERVICIU", Holder = "Ion Popescu", EnrolledUtc = "2026-10-01T08:00:00Z",
    };

    private static readonly SignedMediaRegisterDoc RegV1 = SignedMediaRegister.NextVersion(null, null, [E("INV-1", "AA11")], "STATIE-TRANSFER", Now.AddDays(-20));
    private static readonly string RegV1Sha = SignedMediaRegister.Sha256Hex(SignedMediaRegister.Canonical(RegV1));
    private static readonly SignedMediaRegisterDoc RegV2 = SignedMediaRegister.NextVersion(RegV1, RegV1Sha, [E("INV-1", "AA11"), E("INV-2", "BB22")], "STATIE-TRANSFER", Now.AddDays(-10));
    private static readonly string RegV2Sha = SignedMediaRegister.Sha256Hex(SignedMediaRegister.Canonical(RegV2));

    private static MediumHistory M(string serial, params (DateTimeOffset Start, DateTimeOffset? End)[] windows) => new()
    {
        Serial = serial, VidPid = "0951:1666", CapacityBytes = 32_000_000_000, Description = "Kingston DataTraveler",
        Connections = [.. windows.Select(w => new MediumConnectionWindow { StartUtc = SignedMediaRegister.FormatUtc(w.Start), EndUtc = w.End is { } e ? SignedMediaRegister.FormatUtc(e) : "" })],
        FirstConnectedUtc = SignedMediaRegister.FormatUtc(windows.Min(w => w.Start)), LastConnectedUtc = SignedMediaRegister.FormatUtc(windows.Max(w => w.Start)),
    };

    private static MediaHistoryDoc Station(string name, int regVersion = 2, string audit = "da", DateTimeOffset? exported = null) => new()
    {
        StationId = "HW-" + name, StationName = name, Edition = "Standalone", Posture = "Standalone", TimeZone = "UTC",
        ExportedUtc = SignedMediaRegister.FormatUtc(exported ?? Now), AnchorCode = AnchorOf(name), AnchorSeq = 42,
        RegisterVersion = regVersion, RegisterSha256 = regVersion == 2 ? RegV2Sha : regVersion == 1 ? RegV1Sha : "", FileAuditing = audit,
        HistoryFromUtc = "2026-09-01T00:00:00Z",
        Indicators = [.. MediaHistoryExport.IndicatorIds.Select(id => new StationIndicator { Id = id, Title = id, Status = MediaHistoryExport.StatusConform })],
    };

    private static string AnchorOf(string name) => LogAnalyzer.Dfir.Case.AuditAnchor.Code("chain-" + name, 42, new string('c', 64));

    private static void SetIndicator(MediaHistoryDoc d, string id, string status, string detail = "")
    {
        var x = d.Indicators.First(i => i.Id == id);
        x.Status = status; x.Detail = detail;
    }

    private static MediaTransferEntry Row(string no, string when, string src, string dst, string serial, string sha) => new()
    {
        RegistrationNumber = no, DateTime = when, Direction = "ieșire", SourceStation = src, DestinationStation = dst, MediumSerial = serial, Content = "raport.docx", Sha256 = sha,
        Person = "Ion Popescu", Approver = "Șef compartiment",
    };

    private static MediaHistoryCheck Accept(MediaHistoryDoc d)
    {
        var json = MediaHistoryExport.Canonical(d);
        var c = MediaHistoryVerify.Verify(MediaHistoryExport.FileName(d), json, FakeCms.Sign(json), new FakeCms(P.Officer), P.Officers, P.Trust());
        Assert.True(c.Accepted, c.ToPlainText());
        return c;
    }

    private static readonly CrossStationSettings Period = new() { FromUtc = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), ToUtc = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero) };

    /// <summary>A clean transfer: the medium leaves STATIE-TRANSFER at 10:00 and reaches STATIE-DEST at 11:00, where the file is present.</summary>
    private static (MediaHistoryDoc Src, MediaHistoryDoc Dst) CleanTransfer()
    {
        var src = Station("STATIE-TRANSFER");
        src.Media.Add(M("AA11", (T0.AddMinutes(-10), T0.AddMinutes(5))));
        src.Transfers.Add(Row("TR-2026-0001", "2026-10-10 10:00", "STATIE-TRANSFER", "STATIE-DEST", "AA11", H1));
        var dst = Station("STATIE-DEST");
        dst.Media.Add(M("AA11", (T0.AddHours(1), T0.AddHours(1).AddMinutes(10))));
        dst.PresentFiles.Add(new PresentFile { Path = @"D:\Clasificat\raport.docx", Sha256 = H1 });
        return (src, dst);
    }

    // ───────── export: canonical form, build, verification ─────────

    [Fact]
    public void Export_is_canonical_and_round_trips_and_any_change_breaks_it()
    {
        var (src, _) = CleanTransfer();
        var json = MediaHistoryExport.Canonical(src);
        Assert.False(json[0] == 0xEF);
        var text = Encoding.UTF8.GetString(json);
        Assert.StartsWith("{\"anchor_code\":\"" + AnchorOf("STATIE-TRANSFER") + "\",\"anchor_seq\":42,", text);
        Assert.DoesNotContain(" \"", text);
        var back = MediaHistoryExport.Parse(json);
        Assert.Equal(json, MediaHistoryExport.Canonical(back));
        Assert.Equal("AA11", back.Media[0].Serial);
        Assert.Single(back.Transfers);

        var indented = Encoding.UTF8.GetBytes(text.Replace(",\"anchor_seq\"", ", \"anchor_seq\""));
        Assert.Throws<FormatException>(() => MediaHistoryExport.Parse(indented));
        var extra = Encoding.UTF8.GetBytes(text.Replace("{\"anchor_code\"", "{\"aaa\":\"x\",\"anchor_code\""));
        Assert.Throws<FormatException>(() => MediaHistoryExport.Parse(extra));
    }

    [Fact]
    public void Build_takes_every_removable_medium_its_windows_and_the_file_activity_on_it()
    {
        var device = new DeviceRecord
        {
            Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\X", Serial = "aa11", VidPid = "0951:1666", CapacityBytes = 32_000_000_000, Manufacturer = "Kingston", Model = "DataTraveler",
            DriveLetters = "E:", Connections = [T0.AddHours(-30), T0], LastRemovedUtc = T0.AddMinutes(20), VolumeSerials = ["1a2b-3c4d"],
        };
        var disk = new DeviceRecord { Kind = DeviceKind.InternalDisk, InstanceId = "DISK", Serial = "SSD1" };
        var rep = new ClassifiedAccessReport();
        var f = new ClassifiedFileReport { Path = @"D:\Clasificat\raport.docx" };
        f.Exits.Add(new ClassifiedExit(ExitChannel.RemovableMedia, ExitVerdict.Unjustified, T0.AddMinutes(3), "STATIE\\op", f.Path, @"E:\raport.docx (SN AA11)", "", [], "", []));
        f.Exits.Add(new ClassifiedExit(ExitChannel.OpenedFromMedium, ExitVerdict.ToVerify, T0.AddMinutes(4), "op", f.Path, @"F:\raport.docx (serie volum 1A2B-3C4D)", "", [], "", []));
        f.Exits.Add(new ClassifiedExit(ExitChannel.RemovableMedia, ExitVerdict.ToVerify, T0.AddMinutes(5), "op", f.Path, @"G:\raport.docx", "", [], "", []));
        f.Exits.Add(new ClassifiedExit(ExitChannel.Print, ExitVerdict.Unjustified, T0, "op", f.Path, "imprimanta", "", [], "", []));
        rep.Files.Add(f);
        var transfers = new TransferRegister();
        transfers.Rows.Add(new TransferRow { RegistrationNumber = "TR-1", DateTime = "2026-10-10 10:00", MediumSerial = "AA11", Approver = "Șef" });
        transfers.Rows.Add(new TransferRow { RegistrationNumber = "TR-2", DateTime = "2026-10-10 11:00", Destination = "imprimanta" });

        var doc = MediaHistoryExport.Build(new MediaHistoryInput
        {
            StationId = "0123456789ABCDEF", StationName = "STATIE-TRANSFER", NowUtc = Now, Devices = [device, disk], Classified = rep, Transfers = transfers,
            FileHashes = new Dictionary<string, string> { [f.Path] = H1.ToUpperInvariant() }, FileAuditing = "da", TimeZone = "UTC",
        });

        var m = Assert.Single(doc.Media);
        Assert.Equal("AA11", m.Serial);
        Assert.Equal(2, m.Connections.Count);
        Assert.Equal("", m.Connections[0].EndUtc);                                 // only the last removal is known
        Assert.Equal("2026-10-10T10:20:00Z", m.Connections[1].EndUtc);
        Assert.Equal(2, m.Files.Count);
        Assert.Contains(m.Files, x => x.Direction == MediaHistoryExport.ToMedium && x.Sha256 == H1);
        Assert.Contains(m.Files, x => x.Direction == MediaHistoryExport.FromMedium);
        Assert.Single(doc.UnattributedFiles);                                     // G: has no serial in the record
        Assert.Single(doc.Transfers);                                             // the print row is not a medium transfer
        Assert.Equal(H1, Assert.Single(doc.PresentFiles).Sha256);
        Assert.Equal(0, doc.RegisterVersion);
        Assert.Contains(doc.Notes, n => n.Contains("ancorare"));
        Assert.Equal(MediaHistoryExport.Canonical(doc), MediaHistoryExport.Canonical(MediaHistoryExport.Parse(MediaHistoryExport.Canonical(doc))));
    }

    [Fact]
    public void Tampered_unsigned_foreign_signer_and_unconfirmed_chain_exports_are_rejected()
    {
        var (src, _) = CleanTransfer();
        var json = MediaHistoryExport.Canonical(src);
        var cms = FakeCms.Sign(json);

        // one character changed after signing: still canonical, but the signature does not match
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("\"AA11\"", "\"AA12\""));
        var t = MediaHistoryVerify.Verify("x.json", tampered, cms, new FakeCms(P.Officer), P.Officers, P.Trust());
        Assert.False(t.Accepted);
        Assert.Contains("modificat", t.Headline);

        Assert.False(MediaHistoryVerify.Verify("x.json", json, null, new FakeCms(P.Officer), P.Officers, P.Trust()).Accepted);
        var foreign = MediaHistoryVerify.Verify("x.json", json, cms, new FakeCms(P.Other), P.Officers, P.Trust());
        Assert.False(foreign.Accepted);
        Assert.Contains("nu este un ofițer", foreign.Headline);
        var noCa = MediaHistoryVerify.Verify("x.json", json, cms, new FakeCms(P.Officer), P.Officers, new QualifiedSignatureTrust([], []));
        Assert.False(noCa.Accepted);
        Assert.True(MediaHistoryVerify.Verify("x.json", json, cms, new FakeCms(P.Officer), P.Officers, P.Trust()).Accepted);
    }

    // ───────── reconciliation ─────────

    [Fact]
    public void A_clean_transfer_is_confirmed_at_source_destination_and_by_file_hash()
    {
        var (src, dst) = CleanTransfer();
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        var ok = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindTransferOk);
        Assert.Contains("văzut la sursa STATIE-TRANSFER", ok.Text);
        Assert.Contains("fișierele sunt pe destinație (1/1", ok.Text);
        Assert.DoesNotContain(r.Findings, f => f.Severity < CrossSeverity.Nedeterminat);
        Assert.Equal(1, r.TransfersChecked);
    }

    [Fact]
    public void A_medium_inserted_in_an_unauthorised_station_during_the_transfer_with_the_files_copied_is_grave()
    {
        var (src, dst) = CleanTransfer();
        var x = Station("STATIE-X");
        var med = M("AA11", (T0.AddMinutes(30), T0.AddMinutes(40)));
        med.Files.Add(new MediumFileActivity { File = "raport.docx", Sha256 = H1, TimeUtc = SignedMediaRegister.FormatUtc(T0.AddMinutes(35)), Direction = MediaHistoryExport.FromMedium, Source = "A15" });
        x.Media.Add(med);
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x)], RegV2, RegV2Sha, Period);

        var elsewhere = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindElsewhere);
        Assert.Equal(CrossSeverity.Grav, elsewhere.Severity);
        Assert.Equal("STATIE-X", elsewhere.Station);
        Assert.Contains("introdus și în stația STATIE-X", elsewhere.Text);
        Assert.Contains("în timpul transferului TR-2026-0001", elsewhere.Text);
        var copied = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindFilesElsewhere);
        Assert.Equal(CrossSeverity.Grav, copied.Severity);
        Assert.Equal("TR-2026-0001", copied.Transfer);
        Assert.Equal(CrossSeverity.Grav, r.Ordered[0].Severity);               // sorted by severity
    }

    [Fact]
    public void A_medium_seen_on_another_station_outside_any_transfer_is_listed_and_without_auditing_says_so()
    {
        var (src, dst) = CleanTransfer();
        var x = Station("STATIE-X", audit: "nu");
        x.Media.Add(M("AA11", (T0.AddDays(2), null)));
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x)], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindElsewhere);
        Assert.Equal(CrossSeverity.Ridicat, f.Severity);
        Assert.Contains("nu se știe dacă s-au copiat fișiere", f.Text);
        Assert.Contains(r.Limits, l => l.Contains("STATIE-X NU are auditarea"));
    }

    [Fact]
    public void Missing_destination_files_are_to_verify_and_the_transfer_is_not_confirmed()
    {
        var (src, dst) = CleanTransfer();
        src.Transfers[0].Sha256 = H1 + ";" + H2;
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindFilesMissing);
        Assert.Contains("1 din 2", f.Text);
        Assert.DoesNotContain(r.Findings, f => f.Kind == CrossStationReconciliation.KindTransferOk);
    }

    [Fact]
    public void A_station_with_an_older_register_is_flagged_stale()
    {
        var (src, dst) = CleanTransfer();
        var old = Station("STATIE-DEST", regVersion: 1);
        old.Media.AddRange(dst.Media); old.PresentFiles.AddRange(dst.PresentFiles);
        var r = CrossStationReconciliation.Run([Accept(src), Accept(old)], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindStale);
        Assert.Equal(CrossSeverity.Ridicat, f.Severity);
        Assert.Contains("registru învechit", f.Text);
        Assert.Equal("STATIE-DEST", f.Station);
    }

    [Fact]
    public void A_tampered_export_is_rejected_and_listed_as_grave_and_its_station_counts_as_missing()
    {
        var (src, dst) = CleanTransfer();
        var json = MediaHistoryExport.Canonical(dst);
        var cms = FakeCms.Sign(json);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace(H1, H2));
        var bad = MediaHistoryVerify.Verify("istoric_dest.json", tampered, cms, new FakeCms(P.Officer), P.Officers, P.Trust());
        var r = CrossStationReconciliation.Run([Accept(src), bad], RegV2, RegV2Sha, Period);
        var rej = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindRejected);
        Assert.Equal(CrossSeverity.Grav, rej.Severity);
        Assert.Contains(r.Findings, f => f.Kind == CrossStationReconciliation.KindMissing && f.Station == "STATIE-DEST" && f.Text.Contains("export respins"));
        Assert.DoesNotContain(r.Findings, f => f.Kind == CrossStationReconciliation.KindTransferOk);
    }

    [Fact]
    public void The_same_serial_on_two_stations_too_close_in_time_or_with_other_attributes_is_a_possible_clone()
    {
        var (src, dst) = CleanTransfer();
        var at = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        dst.Media.Add(M("BB22", (at, at.AddMinutes(1))));
        var x = Station("STATIE-X");
        x.Media.Add(M("BB22", (at.AddMinutes(3), null)));                      // 2 minutes after leaving STATIE-DEST
        var y = Station("STATIE-Y");
        var other = M("AA11", (new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero), null));
        other.CapacityBytes = 8_000_000_000;                                     // the serial of INV-1 on a smaller stick
        y.Media.Add(other);
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x), Accept(y)], RegV2, RegV2Sha, Period);
        var clones = r.Findings.Where(f => f.Kind == CrossStationReconciliation.KindClone).ToList();
        Assert.Contains(clones, f => f.Medium == "BB22" && f.Text.Contains("mai puțin decât timpul minim de mutare"));
        Assert.Contains(clones, f => f.Medium == "AA11" && f.Station == "STATIE-Y" && f.Text.Contains("capacitatea diferă"));

        // a slower move is not a clone
        var r2 = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x)], RegV2, RegV2Sha, Period with { MinMoveTime = TimeSpan.FromMinutes(1) });
        Assert.DoesNotContain(r2.Findings, f => f.Kind == CrossStationReconciliation.KindClone && f.Medium == "BB22");
    }

    [Fact]
    public void A_station_named_in_the_register_that_did_not_export_is_undetermined_and_unregistered_media_are_listed()
    {
        var (src, dst) = CleanTransfer();
        src.Transfers.Add(Row("TR-2026-0002", "2026-10-11 09:00", "STATIE-TRANSFER", "STATIE-LIPSA", "AA11", H1));
        src.Media[0].Connections.Add(new MediumConnectionWindow { StartUtc = "2026-10-11T08:55:00Z" });
        dst.Media.Add(M("CC33", (T0.AddDays(1), null)));
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period with { ExpectedStations = ["STATIE-ARHIVA"] });
        Assert.Contains(r.Findings, f => f.Kind == CrossStationReconciliation.KindMissing && f.Station == "STATIE-LIPSA" && f.Severity == CrossSeverity.Nedeterminat);
        Assert.Contains(r.Findings, f => f.Kind == CrossStationReconciliation.KindMissing && f.Station == "STATIE-ARHIVA");
        Assert.Contains(r.Limits, l => l.Contains("STATIE-LIPSA nu are un export acceptat"));
        var un = Assert.Single(r.Findings, f => f.Kind == CrossStationReconciliation.KindUnregistered);
        Assert.Equal("CC33", un.Medium);
    }

    [Fact]
    public void Honest_limits_name_old_exports_short_histories_and_media_without_serial()
    {
        var (src, dst) = CleanTransfer();
        var old = Station("STATIE-DEST", exported: new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));
        old.Media.AddRange(dst.Media); old.PresentFiles.AddRange(dst.PresentFiles);
        old.HistoryFromUtc = "2026-10-05T00:00:00Z";
        old.Media.Add(new MediumHistory { Serial = "", Description = "card SD" });
        var r = CrossStationReconciliation.Run([Accept(src), Accept(old)], null, "", Period);
        Assert.Contains(r.Limits, l => l.Contains("înainte de sfârșitul perioadei"));
        Assert.Contains(r.Limits, l => l.Contains("începe la 2026-10-05"));
        Assert.Contains(r.Limits, l => l.Contains("fără serie unică"));
        Assert.Contains(r.Limits, l => l.Contains("nu există un registru de medii semnat"));
    }

    [Fact]
    public void A_register_time_is_read_in_the_station_time_zone()
    {
        var u = CrossStationReconciliation.ToUtc(new DateTime(2026, 7, 1, 12, 0, 0), "GTB Standard Time", out var known);
        Assert.True(known);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), u);
        CrossStationReconciliation.ToUtc(new DateTime(2026, 7, 1, 12, 0, 0), "nu-exista", out var unknown);
        Assert.False(unknown);
    }

    // ───────── review of PR #297: R1–R5 ─────────

    private static string Sig(CrossStationResult r) =>
        string.Join("\n", r.Ordered.Select(f => $"{f.Severity}|{f.Kind}|{f.Station}|{f.Medium}|{f.Transfer}|{f.Text}")) + "\n--\n" + string.Join("\n", r.Limits);

    [Fact]
    public void R1_contradictory_rows_for_one_transfer_are_grave_the_source_row_rules_and_the_result_does_not_depend_on_load_order()
    {
        var (src, dst) = CleanTransfer();
        // the destination's register holds the same transfer number with another route and another file
        dst.Transfers.Add(Row("TR-2026-0001", "2026-10-10 10:00", "STATIE-TRANSFER", "STATIE-X", "AA11", H2));
        var a = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        var b = CrossStationReconciliation.Run([Accept(dst), Accept(src)], RegV2, RegV2Sha, Period);
        Assert.Equal(Sig(a), Sig(b));
        var c = Assert.Single(a.Findings, f => f.Kind == CrossStationReconciliation.KindContradictory);
        Assert.Equal(CrossSeverity.Grav, c.Severity);
        Assert.Contains("înregistrări contradictorii pentru transferul TR-2026-0001 între stațiile STATIE-DEST și STATIE-TRANSFER", c.Text);
        Assert.Contains("STATIE-X", c.Text);
        // the source station's row (to STATIE-DEST, file H1) is the one evaluated
        Assert.Contains(a.Findings, f => f.Kind == CrossStationReconciliation.KindTransferOk && f.Transfer == "TR-2026-0001");
        Assert.Equal(1, a.TransfersChecked);
    }

    [Fact]
    public void R1_the_same_row_held_by_both_stations_is_not_a_contradiction()
    {
        var (src, dst) = CleanTransfer();
        dst.Transfers.Add(Row("TR-2026-0001", "2026-10-10 10:00", "statie-transfer", "STATIE-DEST", "aa11", H1.ToUpperInvariant()));
        var r = CrossStationReconciliation.Run([Accept(dst), Accept(src)], RegV2, RegV2Sha, Period);
        Assert.DoesNotContain(r.Findings, f => f.Kind == CrossStationReconciliation.KindContradictory);
        Assert.Equal(1, r.TransfersChecked);
    }

    [Fact]
    public void R5_tamper_indicators_travel_in_the_export_and_a_dirty_one_is_a_finding_for_that_station()
    {
        var doc = MediaHistoryExport.Build(new MediaHistoryInput
        {
            StationId = "HW-1", StationName = "STATIE-DEST", NowUtc = Now,
            Indicators = [new StationIndicator { Id = "A05", Title = "Jurnale șterse", Status = "NECONFORM", Detail = "1102 la 2026-10-09" }],
        });
        Assert.Equal(MediaHistoryExport.IndicatorIds, doc.Indicators.Select(i => i.Id));
        Assert.Equal("NECONFORM", doc.Indicators.Single(i => i.Id == "A05").Status);
        Assert.Equal(MediaHistoryExport.StatusMissing, doc.Indicators.Single(i => i.Id == "A11").Status);
        Assert.Equal("1102 la 2026-10-09", MediaHistoryExport.Parse(MediaHistoryExport.Canonical(doc)).Indicators.Single(i => i.Id == "A05").Detail);

        var (src, dst) = CleanTransfer();
        SetIndicator(dst, "A05", "NECONFORM", "jurnalul Security a fost golit (1102) la 2026-10-09");
        SetIndicator(dst, "A11", "DE VERIFICAT", "codul anterior nu a fost tastat");
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindIndicators);
        Assert.Equal("STATIE-DEST", f.Station);
        Assert.Equal(CrossSeverity.Ridicat, f.Severity);
        Assert.Contains("istoricul acestei stații poate fi incomplet", f.Text);
        Assert.Contains("A05 NECONFORM", f.Text);
        Assert.Contains("A11 DE VERIFICAT", f.Text);
        Assert.Contains(r.Limits, l => l.Contains("un administrator poate șterge istoricul dispozitivelor unei stații; ștergerea lasă de regulă urme (104/1102, A11)", StringComparison.OrdinalIgnoreCase));
        // a confirmed transfer always says whether each involved station's indicators are clean
        var ok = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindTransferOk);
        Assert.Contains("STATIE-TRANSFER: indicatori de integritate curați", ok.Text);
        Assert.Contains("STATIE-DEST: indicatori de integritate NECURAȚI (A05, A11)", ok.Text);
    }

    [Fact]
    public void R5_an_export_without_indicators_is_not_taken_as_clean()
    {
        var (src, dst) = CleanTransfer();
        dst.Indicators.Clear();
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindIndicators);
        Assert.Equal("STATIE-DEST", f.Station);
        Assert.Contains("lipsă", f.Text);
    }

    [Fact]
    public void R5_the_paper_anchor_code_is_compared_mismatch_is_grave_and_a_missing_one_is_noted()
    {
        var (src, dst) = CleanTransfer();
        var other = LogAnalyzer.Dfir.Case.AuditAnchor.Code("alt-lant", 7, new string('d', 64));
        var paper = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATIE-DEST"] = other, ["STATIE-TRANSFER"] = AnchorOf("STATIE-TRANSFER").Replace("-", " ").ToLowerInvariant(),
        };
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period with { PaperAnchors = paper });
        var g = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindAnchorMismatch);
        Assert.Equal(CrossSeverity.Grav, g.Severity);
        Assert.Equal("STATIE-DEST", g.Station);

        var none = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        Assert.DoesNotContain(none.Findings, x => x.Kind == CrossStationReconciliation.KindAnchorMismatch);
        Assert.Contains(none.Limits, l => l.Contains("STATIE-DEST") && l.Contains("nu a fost comparat cu procesul-verbal"));
    }

    [Fact]
    public void A_paper_anchor_code_for_a_station_without_an_imported_export_is_to_verify()
    {
        var (src, dst) = CleanTransfer();
        var paper = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["STATIE-DSET"] = AnchorOf("STATIE-DEST"), ["HW-STATIE-DEST"] = AnchorOf("STATIE-DEST") };
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period with { PaperAnchors = paper });
        var f = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindAnchorUnknownStation);
        Assert.Equal(CrossSeverity.DeVerificat, f.Severity);
        Assert.Contains("cod de pe procesul-verbal pentru o stație necunoscută: STATIE-DSET (verificați numele)", f.Text);
        Assert.Contains(r.Limits, l => l.Contains("indicatorii de integritate sunt calculați de stația verificată însăși, la export", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void R2_an_expected_station_whose_export_predates_the_period_end_is_to_verify()
    {
        var (src, dst) = CleanTransfer();
        var old = Station("STATIE-DEST", exported: new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));
        old.Media.AddRange(dst.Media); old.PresentFiles.AddRange(dst.PresentFiles);
        var r = CrossStationReconciliation.Run([Accept(src), Accept(old)], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindStaleExport);
        Assert.Equal(CrossSeverity.DeVerificat, f.Severity);
        Assert.Equal("STATIE-DEST", f.Station);
        Assert.Contains("export învechit", f.Text);
    }

    [Fact]
    public void R3_a_rejected_export_shows_the_file_name_and_marks_the_station_name_unverified()
    {
        var (src, dst) = CleanTransfer();
        var json = MediaHistoryExport.Canonical(dst);
        var bad = MediaHistoryVerify.Verify("istoric_dest.json", json, null, new FakeCms(P.Officer), P.Officers, P.Trust());
        Assert.Equal("STATIE-DEST (nume din fișier neverificat)", bad.StationLabel);
        var r = CrossStationReconciliation.Run([Accept(src), bad], RegV2, RegV2Sha, Period);
        var f = Assert.Single(r.Findings, x => x.Kind == CrossStationReconciliation.KindRejected);
        Assert.Equal("STATIE-DEST (nume din fișier neverificat)", f.Station);
        Assert.Contains("istoric_dest.json", f.Text);
    }

    [Fact]
    public void R4_clone_by_time_is_only_to_verify_when_a_clock_may_be_off()
    {
        var (src, dst) = CleanTransfer();
        var at = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        dst.Media.Add(M("BB22", (at, at.AddMinutes(1))));
        var x = Station("STATIE-X");
        x.Media.Add(M("BB22", (at.AddMinutes(3), null)));
        var clean = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x)], RegV2, RegV2Sha, Period);
        Assert.Equal(CrossSeverity.Ridicat, Assert.Single(clean.Findings, f => f.Kind == CrossStationReconciliation.KindClone).Severity);

        SetIndicator(x, "S05", "DE VERIFICAT", "ceasul a fost dat înapoi cu 3 ore");
        var skew = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x)], RegV2, RegV2Sha, Period);
        var f2 = Assert.Single(skew.Findings, f => f.Kind == CrossStationReconciliation.KindClone);
        Assert.Equal(CrossSeverity.DeVerificat, f2.Severity);
        Assert.Contains("ceasul stației STATIE-X poate fi decalat", f2.Text);
        Assert.Contains("ceasul a fost dat înapoi cu 3 ore", f2.Text);

        x.Indicators.RemoveAll(i => i.Id == "S05");                                 // unknown clock state
        var unknown = CrossStationReconciliation.Run([Accept(src), Accept(dst), Accept(x)], RegV2, RegV2Sha, Period);
        Assert.Equal(CrossSeverity.DeVerificat, Assert.Single(unknown.Findings, f => f.Kind == CrossStationReconciliation.KindClone).Severity);
    }

    [Theory]
    [InlineData(-120, true)]
    [InlineData(120, true)]
    [InlineData(-121, false)]
    [InlineData(121, false)]
    public void R4_the_source_window_is_plus_minus_two_hours_inclusive(int minutes, bool seen)
    {
        var (src, dst) = CleanTransfer();
        src.Media[0].Connections = [new MediumConnectionWindow { StartUtc = SignedMediaRegister.FormatUtc(T0.AddMinutes(minutes)) }];
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        Assert.Equal(!seen, r.Findings.Any(f => f.Kind == CrossStationReconciliation.KindNotAtSource));
    }

    [Theory]
    [InlineData(72 * 60, true)]
    [InlineData(-120, true)]
    [InlineData(72 * 60 + 1, false)]
    [InlineData(-121, false)]
    public void R4_the_destination_window_is_from_two_hours_before_to_72_hours_after_inclusive(int minutes, bool seen)
    {
        var (src, dst) = CleanTransfer();
        dst.Media[0].Connections = [new MediumConnectionWindow { StartUtc = SignedMediaRegister.FormatUtc(T0.AddMinutes(minutes)) }];
        var r = CrossStationReconciliation.Run([Accept(src), Accept(dst)], RegV2, RegV2Sha, Period);
        Assert.Equal(!seen, r.Findings.Any(f => f.Kind == CrossStationReconciliation.KindNotAtDestination));
    }
}
