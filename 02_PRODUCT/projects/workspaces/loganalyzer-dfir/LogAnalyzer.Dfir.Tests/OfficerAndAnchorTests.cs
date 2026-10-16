using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner decisions 2026-10-16 (library side, no CMS): the audit-log anchor (A11, typed vs proposed code), the Windows tamper indicators (A05, A06, A07, A12),
/// the officer-list check (A13), the officer list kept out of the profile, and the PDF structure of the in-place PAdES update (ByteRange, xref, original prefix).
/// The CMS signature itself (SignedCms, WPF framework) is tested in LogAnalyzer.App.Tests.
/// </summary>
public sealed class OfficerAndAnchorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "officer_" + Guid.NewGuid().ToString("N"));
    public OfficerAndAnchorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Log(int entries, string name = "auth_audit.jsonl")
    {
        var p = Path.Combine(_dir, name);
        var c = new HashChain(p);
        for (int i = 1; i <= entries; i++) c.Append(new { Event = "e" + i, Detail = "d" + i });
        return p;
    }

    /// <summary>What an administrator with the sealing key can do: rewrite one entry and recompute every hash after it, so the plain chain is valid again.</summary>
    private static void RewriteEntry(string logPath, int seq, string newDetail)
    {
        var lines = File.ReadAllLines(logPath).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        File.Delete(logPath);
        var c = new HashChain(logPath);
        foreach (var o in lines)
        {
            JsonNode F(string name) => o.First(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value!;
            var detail = o["seq"]!.GetValue<long>() == seq ? newDetail : F("Detail").GetValue<string>();
            c.Append(new { Event = F("Event").GetValue<string>(), Detail = detail });
        }
    }

    // ───────── anchor code ─────────

    [Fact]
    public void Anchor_code_is_four_groups_of_five_and_a_typed_variant_is_accepted_but_a_wrong_check_character_is_not()
    {
        var a = AuditAnchor.Current(Log(3))!;
        Assert.Equal(3, a.Seq);
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}$", a.Code);
        Assert.Equal($"Cod de ancorare: {a.Code} (înregistrarea nr. 3)", a.Line);
        Assert.True(AuditAnchor.TryNormalize(" " + a.Code.Replace("-", " ").ToLowerInvariant() + " ", out var n));
        Assert.Equal(a.Code, n);
        var wrong = a.Code[..^1] + (a.Code[^1] == 'A' ? 'B' : 'A');
        Assert.False(AuditAnchor.TryNormalize(wrong, out _));
        Assert.Equal(AnchorContinuityStatus.InvalidCode, AuditAnchor.Verify(Path.Combine(_dir, "auth_audit.jsonl"), wrong).Status);
    }

    [Fact]
    public void Appending_after_the_anchor_keeps_continuity_and_says_where()
    {
        var log = Log(3);
        var code = AuditAnchor.Current(log)!.Code;
        new HashChain(log).Append(new { Event = "e4", Detail = "d4" });
        new HashChain(log).Append(new { Event = "e5", Detail = "d5" });
        var r = AuditAnchor.Verify(log, code);
        Assert.Equal(AnchorContinuityStatus.Continuous, r.Status);
        Assert.Equal(3, r.MatchedSeq);
        Assert.Contains("2 înregistrări noi", r.Detail);
    }

    [Fact]
    public void A_rewritten_and_rechained_log_no_longer_contains_the_anchor()
    {
        var log = Log(4);
        var code = AuditAnchor.Current(log)!.Code;
        RewriteEntry(log, 2, "control.started ascuns");
        Assert.Equal(ChainStatus.Valid, new HashChain(log).VerifyPlainChain().Status);   // the plain chain alone cannot see it
        var r = AuditAnchor.Verify(log, code);
        Assert.Equal(AnchorContinuityStatus.Rewritten, r.Status);
        Assert.Contains("rescris după controlul anterior", r.Detail);
    }

    [Fact]
    public void A_log_truncated_before_the_anchor_or_started_again_is_rewritten()
    {
        var log = Log(5);
        var code = AuditAnchor.Current(log)!.Code;
        File.Delete(log);
        Log(3);
        Assert.Equal(AnchorContinuityStatus.Rewritten, AuditAnchor.Verify(log, code).Status);
        File.Delete(log);
        Assert.Equal(AnchorContinuityStatus.Rewritten, AuditAnchor.Verify(log, code).Status);
    }

    [Fact]
    public void No_code_is_not_given_and_never_continuous()
    {
        var log = Log(2);
        Assert.Equal(AnchorContinuityStatus.NotGiven, AuditAnchor.Verify(log, "").Status);
        Assert.Equal(AnchorContinuityStatus.NotGiven, AuditAnchor.Verify(log, null).Status);
        Assert.Equal(AnchorContinuityStatus.InvalidCode, AuditAnchor.Verify(log, "ABC").Status);
    }

    [Fact]
    public void Export_copies_the_log_and_writes_the_anchor_file_without_touching_the_source()
    {
        var log = Log(3);
        var before = File.ReadAllBytes(log);
        var x = AuditAnchor.Export(log, Path.Combine(_dir, "cdr"), "ST-1", Now);
        Assert.Equal(before, File.ReadAllBytes(log));
        Assert.Equal(before, File.ReadAllBytes(x.LogCopy));
        var text = File.ReadAllText(x.AnchorFile);
        Assert.Contains(x.Anchor.Line, text);
        Assert.Contains(x.LogSha256, text);
        Assert.Equal(AnchorContinuityStatus.Continuous, AuditAnchor.Verify(x.LogCopy, x.Anchor.Code).Status);
        Assert.Throws<InvalidOperationException>(() => AuditAnchor.Export(Path.Combine(_dir, "empty.jsonl"), Path.Combine(_dir, "cdr"), "ST-1", Now));
    }

    [Fact]
    public void The_station_hint_is_the_last_recorded_anchor_and_officer_changes_since_the_paper_anchor_are_listed()
    {
        var s = new AuthService(Path.Combine(_dir, "auth"));
        var empty = OfficerState.Compute(Path.Combine(_dir, "none.json"), Path.Combine(_dir, "none")).Detail;
        s.AuditOperation(null, OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail("AAAA", "Unu", OfficerRegistry.BasisFirst, "") + "; " + empty);
        s.AuditOperation(null, OperationAudit.AnchorState, OperationAudit.AnchorStateDetail(null, "test") + "; " + empty);
        var a = AuditAnchor.Current(s.AuditPath)!;
        s.AuditOperation(null, OperationAudit.AnchorRecorded, OperationAudit.AnchorRecordedDetail(a, "control") + "; " + empty);
        Assert.Equal(a.Code, AuditAnchor.LastRecorded(s.AuditPath));
        s.AuditOperation(null, OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail("BBBB", "Doi", OfficerRegistry.BasisApproved, "AAAA") + "; " + empty);
        var assessed = OfficerState.Assess(s.AuditPath, a.Seq, empty);
        Assert.True(assessed.Determinable);
        Assert.Empty(assessed.Outside);
        Assert.Contains("Doi", Assert.Single(assessed.Changes));
        Assert.Equal(ChainStatus.Valid, s.VerifyAudit().Status);
    }

    // ───────── checks A06, A11, A12, A13 ─────────

    private static StationFacts Facts(StationPosture posture = StationPosture.Standalone, bool securityReadable = true)
    {
        var s = Now.AddDays(-30);
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = s, PeriodEndUtc = Now, CollectedUtc = Now, IsAdministrator = true, Posture = posture };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, s.AddDays(-60), 100, securityReadable || ch != "Security", ""));
        return f;
    }

    private static LogFact Ev(string channel, int id, DateTimeOffset t, long rec) =>
        new(channel, id, t, rec, "S-1-5-21-1-2-3-500", new Dictionary<string, string> { ["SubjectUserName"] = "admin", ["SubjectDomainName"] = "ST-1" });

    private static ControlCheck Check(ControlReport r, string id) => Assert.Single(r.Checks, c => c.Id == id);

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void A11_follows_the_anchor_result_says_where_the_code_came_from_and_applies_in_every_edition(StationPosture posture)
    {
        var f = Facts(posture);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(f), "A11").Status);
        f.PreviousAnchor = new AnchorContinuity(AnchorContinuityStatus.Continuous, "conține înregistrarea nr. 7.", "AAAAA-AAAAA-AAAAA-AAAAA", 7);
        var typed = Check(ControlEvaluator.Evaluate(f), "A11");
        Assert.Equal(ControlStatus.Conform, typed.Status);
        Assert.Contains("tastat de pe procesul-verbal", typed.Detail);
        f.PreviousAnchorFromStation = true;
        var proposed = Check(ControlEvaluator.Evaluate(f), "A11");
        Assert.Contains("propus de stație (nesigur)", proposed.Detail);
        f.PreviousAnchor = new AnchorContinuity(AnchorContinuityStatus.Rewritten, "nicio înregistrare nu corespunde.", "AAAAA-AAAAA-AAAAA-AAAAA", null);
        var a11 = Check(ControlEvaluator.Evaluate(f), "A11");
        Assert.Equal(ControlStatus.Neconform, a11.Status);
        Assert.Contains("jurnalul aplicației a fost rescris după controlul anterior", a11.Detail, StringComparison.OrdinalIgnoreCase);
        foreach (var id in new[] { "A11", "A12", "A13" }) Assert.True(ControlApplicability.Applies(id, posture));
    }

    [Fact]
    public void A12_needs_an_actual_shutdown_and_a_cancelled_shutdown_explains_nothing()
    {
        var f = Facts();
        Assert.Equal(ControlStatus.Conform, Check(ControlEvaluator.Evaluate(f), "A12").Status);
        // a normal shutdown: 1074, 1100, 6006, then the next boot 6005
        f.Logs.Add(Ev("System", 1074, Now.AddDays(-5).AddMinutes(-2), 10));
        f.Logs.Add(Ev("Security", 1100, Now.AddDays(-5), 11));
        f.Logs.Add(Ev("System", 6006, Now.AddDays(-5).AddSeconds(5), 12));
        f.Logs.Add(Ev("System", 6005, Now.AddDays(-5).AddMinutes(3), 13));
        Assert.Equal(ControlStatus.Conform, Check(ControlEvaluator.Evaluate(f), "A12").Status);
        // a requested shutdown that was cancelled (shutdown /a): no 6006 and no boot after the stop
        f.Logs.Add(Ev("System", 1074, Now.AddDays(-3).AddMinutes(-1), 20));
        f.Logs.Add(Ev("Security", 1100, Now.AddDays(-3), 21));
        var r = ControlEvaluator.Evaluate(f);
        Assert.Equal(ControlStatus.DeVerificat, Check(r, "A12").Status);
        Assert.Single(Check(r, "A12").Evidence);
        Assert.Contains(r.Actions, a => a.Action == "a oprit serviciul de jurnalizare");
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Facts(securityReadable: false)), "A12").Status);
        Assert.Contains(1100, StationFactCollector.Queries["Security"]);
    }

    [Fact]
    public void A13_lists_officer_list_changes_for_review()
    {
        var f = Facts();
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(f), "A13").Status);
        f.OfficerAssessment = new OfficerStateAssessment(true, [], [], "de la înregistrarea nr. 7 (codul de pe procesul-verbal)");
        Assert.Equal(ControlStatus.Conform, Check(ControlEvaluator.Evaluate(f), "A13").Status);
        f.OfficerAssessment = new OfficerStateAssessment(true, [], ["înregistrarea nr. 9, profile.officer_registered: cert_fingerprint=AAAA"], "de la înregistrarea nr. 7");
        var a13 = Check(ControlEvaluator.Evaluate(f), "A13");
        Assert.Equal(ControlStatus.DeVerificat, a13.Status);
        Assert.Single(a13.Evidence);
    }

    [Fact]
    public void Tamper_indicators_are_always_in_the_report_even_when_nothing_happened()
    {
        var r = ControlEvaluator.Evaluate(Facts());
        foreach (var id in new[] { "A05", "A06", "A07", "A12" }) Assert.Equal(ControlStatus.Conform, Check(r, id).Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Facts(securityReadable: false)), "A06").Status);
        foreach (var id in new[] { "A11", "A12", "A13" }) Assert.Contains(id, CheckGuides.Ids);
    }

    // ───────── officer list outside the profile ─────────

    [Fact]
    public void A_profile_import_never_carries_officers_or_the_anchor_code_and_says_so()
    {
        var json = ProfileStore.ToJson(new ProcedureProfile { Name = "p" }).TrimEnd().TrimEnd('}') +
                   ",\"securityOfficers\":[{\"name\":\"Admin\",\"fingerprint\":\"AAAA\",\"basis\":\"" + OfficerRegistry.BasisFirst + "\"}],\"lastAnchorCode\":\"AAAAA-AAAAA-AAAAA-AAAAA\"}";
        var r = ProfileImport.FromJson(json);
        Assert.NotNull(r.Profile);
        var w = Assert.Single(r.Issues, i => i.Message == ProfileImport.OfficerEntriesIgnored);
        Assert.False(w.IsError);
        Assert.DoesNotContain("securityOfficers", ProfileStore.ToJson(r.Profile!), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(r.Issues, i => i.Message == ProfileImport.OfficerEntriesIgnored && ProfileImport.FromJson(ProfileStore.ToJson(new ProcedureProfile())).Issues.Count > 0);
    }

    [Fact]
    public void The_officer_store_is_its_own_file_and_round_trips()
    {
        var store = new OfficerStore(Path.Combine(_dir, "officers", "security_officers.json"));
        Assert.Empty(store.Load().Officers);
        var d = store.Load();
        d.Officers.Add(new SecurityOfficerRow { Name = "Unu", Fingerprint = "AB", Certificate = "", Basis = OfficerRegistry.BasisFirst });
        store.Save(d);
        Assert.Equal("Unu", Assert.Single(store.Load().Officers).Name);
        Assert.NotEqual(Path.GetFullPath(ProfileStore.DefaultPath), Path.GetFullPath(OfficerStore.Shared.Path));
    }

    // ───────── PAdES incremental update: structure (CMS faked) ─────────

    private sealed class FakeCms(byte[] sig) : ICmsDetachedSigner, ICmsDetachedVerifier
    {
        public byte[]? Signed;
        public byte[] Sign(byte[] content) { Signed = content; return sig; }
        public CmsResult Verify(byte[] cms, byte[] content) => new(cms.AsSpan().SequenceEqual(sig) && Signed is not null && content.AsSpan().SequenceEqual(Signed), TestCert(), "fals");
    }

    private static byte[] Report()
    {
        var path = Path.Combine(Path.GetTempPath(), "r_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var r = ControlEvaluator.Evaluate(Facts());
            ControlReportPdf.Write(r, path, "Inspector Test");
            return File.ReadAllBytes(path);
        }
        finally { File.Delete(path); }
    }

    private static readonly SignatureAppearance Look = new("Ofițer Țest", "Test QTSP CA", Now, "ST-1", "Raport de control");

    [Fact]
    public void The_signature_is_one_incremental_update_whose_ByteRange_covers_everything_but_Contents_and_whose_xref_offsets_are_right()
    {
        var original = Report();
        var cms = new FakeCms([0x30, 0x03, 0x02, 0x01, 0x05]);
        var signed = PdfIncrementalSigner.Sign(original, Look, cms);

        Assert.True(signed.AsSpan(0, original.Length).SequenceEqual(original));      // the original is an exact prefix
        var text = Encoding.Latin1.GetString(signed);
        var br = Regex.Match(text, @"/ByteRange \[(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        long a = long.Parse(br.Groups[1].Value), b = long.Parse(br.Groups[2].Value), c = long.Parse(br.Groups[3].Value), d = long.Parse(br.Groups[4].Value);
        Assert.Equal(0, a);
        Assert.Equal(signed.Length, c + d);
        Assert.Equal((byte)'<', signed[b]);
        Assert.Equal((byte)'>', signed[c - 1]);
        Assert.Equal((c - b - 2) / 2, PdfIncrementalSigner.ContentsBytes);
        Assert.StartsWith("3003020105", text.Substring((int)b + 1, 10));
        Assert.Equal(signed.Length - (c - b), cms.Signed!.Length);                       // exactly everything except /Contents was signed
        Assert.Contains("/SubFilter /ETSI.CAdES.detached", text);
        Assert.Contains("/M (D:20261016090000+00'00')", text);
        Assert.Contains("Semnat de Ofiter Test", text);                                // visible appearance, diacritics transliterated
        Assert.Matches(@"/Prev \d+", text[original.Length..]);

        // the new xref section: every entry points at "N 0 obj"
        int sx = text.LastIndexOf("startxref", StringComparison.Ordinal);
        long xref = long.Parse(Regex.Match(text[sx..], @"startxref\s+(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", text[(int)xref..]);
        var section = text[(int)xref..text.IndexOf("trailer", (int)xref, StringComparison.Ordinal)];
        int checkedEntries = 0, cur = 0;
        foreach (var line in section.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var p = line.Trim().Split(' ');
            if (p.Length == 2) { cur = int.Parse(p[0]); continue; }
            Assert.StartsWith($"{cur} 0 obj", text[(int)long.Parse(p[0])..]);
            cur++; checkedEntries++;
        }
        Assert.Equal(4, checkedEntries);                                               // sig, the reserved widget (filled), appearance, font
        Assert.DoesNotContain("/AcroForm", text[original.Length..]);                   // catalog and page are not touched
        Assert.DoesNotContain("/Annots", text[original.Length..]);
        Assert.Contains("/V ", text[original.Length..]);

        var check = PdfSignatureCheck.Check(signed, cms);
        Assert.Null(check.Problem);
        Assert.Contains((long)original.Length, check.OriginalLengths);
    }

    [Fact]
    public void Anything_appended_after_the_signature_or_a_second_signature_is_refused()
    {
        var cms = new FakeCms([0x30, 0x03, 0x02, 0x01, 0x05]);
        var signed = PdfIncrementalSigner.Sign(Report(), Look, cms);
        var appended = signed.Concat(Encoding.ASCII.GetBytes("1 0 obj\n<<>>\nendobj\n%%EOF\n")).ToArray();
        var c1 = PdfSignatureCheck.Check(appended, cms);
        Assert.True(c1.ModifiedAfterSigning);
        Assert.Contains("modificat după semnare", c1.Problem);
        Assert.Throws<NotSupportedException>(() => PdfIncrementalSigner.Sign(signed, Look, cms));      // never signs a signed file again
        var unsigned = PdfSignatureCheck.Check(Report(), cms);
        Assert.True(unsigned.NotSigned);
    }

    [Fact]
    public void A_request_pdf_carries_its_marker_in_the_original_bytes_and_can_be_signed()
    {
        var path = Path.Combine(_dir, "cerere.pdf");
        var cert = TestCert();
        var row = new SecurityOfficerRow { Name = "Doi", Certificate = Convert.ToBase64String(cert.RawData), Fingerprint = OfficerFingerprint.Of(cert) };
        OfficerRequestPdf.Write(path, row, "ST-1", Now);
        var bytes = File.ReadAllBytes(path);
        Assert.Contains(OfficerRegistry.RequestMarker(row.Fingerprint), Encoding.ASCII.GetString(bytes));
        var cms = new FakeCms([0x30, 0x03, 0x02, 0x01, 0x05]);
        var signed = PdfIncrementalSigner.Sign(bytes, Look, cms);
        var check = PdfSignatureCheck.Check(signed, cms);
        Assert.Null(check.Problem);
        Assert.True(check.PrefixContains(bytes.Length, OfficerRegistry.RequestMarker(row.Fingerprint)));
        Assert.False(check.PrefixContains(bytes.Length, OfficerRegistry.RequestMarker(new string((char)65, 64))));
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 TestCert()
    {
        using var k = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Test", k, System.Security.Cryptography.HashAlgorithmName.SHA256);
        return req.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(10));
    }

    [Fact]
    public void Officer_and_anchor_operations_are_written_to_the_sealed_audit()
    {
        var s = new AuthService(Path.Combine(_dir, "auth"));
        s.AuditOperation(null, OperationAudit.ReportGenerated, OperationAudit.ReportGeneratedDetail("r.pdf", new string('A', 64)));
        s.AuditOperation(null, OperationAudit.ReportOfficerSigned, OperationAudit.OfficerSignedDetail("r_semnat.pdf", new string('A', 64), new string('B', 64), "AAAA", "Ofițer", "OFICIAL"));
        s.AuditOperation(null, OperationAudit.AnchorExported, OperationAudit.AnchorExportedDetail(3, "AAAAA-AAAAA-AAAAA-AAAAA", "cd", "E:\\"));
        var events = LogAnalyzer.Dfir.IO.Json.ReadLines<AuthAuditEntry>(s.AuditPath).Select(e => e.Event).ToList();
        Assert.Equal(["report.generated", "report.officer_signed", "audit.anchor_exported"], events);
        Assert.Single(AuditAnchor.EventDetails(s.AuditPath, OperationAudit.ReportGenerated));
        Assert.Equal(ChainStatus.Valid, s.VerifyAudit().Status);
    }
}
