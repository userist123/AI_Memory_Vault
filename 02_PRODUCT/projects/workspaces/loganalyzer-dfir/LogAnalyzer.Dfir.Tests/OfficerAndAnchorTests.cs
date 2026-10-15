using System.Security.Cryptography;
using System.Text.Json.Nodes;
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
/// Owner decisions 2026-10-16: the official report is signed by the security officer's own key (verified against a pinned fingerprint), the application's
/// audit log is anchored by a short code on the signed paper (check A11), and Windows tamper indicators are always in the report (A05, A06, A07, A12).
/// Test keys only.
/// </summary>
public sealed class OfficerAndAnchorTests : IDisposable
{
    private const string Pass = "parola-ofiter-1", OtherPass = "alta-parola-2";
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

    private string Pdf(string name = "raport.pdf")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, "%PDF-1.7\n% test\n%%EOF\n"u8.ToArray());
        return p;
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
        var last = a.Code[^1];
        var wrong = a.Code[..^1] + (last == 'A' ? 'B' : 'A');
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
        Log(3);   // same file name, started again from zero
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
        var dest = Path.Combine(_dir, "cdr");
        var x = AuditAnchor.Export(log, dest, "ST-1", Now);
        Assert.Equal(before, File.ReadAllBytes(log));
        Assert.Equal(before, File.ReadAllBytes(x.LogCopy));
        var text = File.ReadAllText(x.AnchorFile);
        Assert.Contains(x.Anchor.Line, text);
        Assert.Contains(x.LogSha256, text);
        Assert.Equal(AnchorContinuityStatus.Continuous, AuditAnchor.Verify(x.LogCopy, x.Anchor.Code).Status);
        Assert.Throws<InvalidOperationException>(() => AuditAnchor.Export(Path.Combine(_dir, "empty.jsonl"), dest, "ST-1", Now));
    }

    // ───────── checks A06, A11, A12 ─────────

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
    public void A11_follows_the_anchor_result_and_applies_in_every_edition(StationPosture posture)
    {
        var f = Facts(posture);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(f), "A11").Status);
        f.PreviousAnchor = new AnchorContinuity(AnchorContinuityStatus.NotGiven, "Nu s-a introdus codul de ancorare.", "", null);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(f), "A11").Status);
        f.PreviousAnchor = new AnchorContinuity(AnchorContinuityStatus.Continuous, "conține înregistrarea nr. 7", "AAAAA-AAAAA-AAAAA-AAAAA", 7);
        Assert.Equal(ControlStatus.Conform, Check(ControlEvaluator.Evaluate(f), "A11").Status);
        f.PreviousAnchor = new AnchorContinuity(AnchorContinuityStatus.Rewritten, "nicio înregistrare nu corespunde", "AAAAA-AAAAA-AAAAA-AAAAA", null);
        var a11 = Check(ControlEvaluator.Evaluate(f), "A11");
        Assert.Equal(ControlStatus.Neconform, a11.Status);
        Assert.Contains("jurnalul aplicației a fost rescris după controlul anterior", a11.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(ControlApplicability.Applies("A11", posture));
        Assert.True(ControlApplicability.Applies("A12", posture));
    }

    [Fact]
    public void A12_explains_a_log_service_stop_by_a_requested_shutdown_and_flags_the_others()
    {
        var f = Facts();
        Assert.Equal(ControlStatus.Conform, Check(ControlEvaluator.Evaluate(f), "A12").Status);
        f.Logs.Add(Ev("System", 1074, Now.AddDays(-5).AddMinutes(-2), 10));
        f.Logs.Add(Ev("Security", 1100, Now.AddDays(-5), 11));
        var r = ControlEvaluator.Evaluate(f);
        Assert.Equal(ControlStatus.Conform, Check(r, "A12").Status);
        Assert.Contains("toate la o închidere", Check(r, "A12").Detail);
        f.Logs.Add(Ev("Security", 1100, Now.AddDays(-3), 12));
        r = ControlEvaluator.Evaluate(f);
        Assert.Equal(ControlStatus.DeVerificat, Check(r, "A12").Status);
        Assert.Single(Check(r, "A12").Evidence);
        Assert.Contains(r.Actions, a => a.Action == "a oprit serviciul de jurnalizare");
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Facts(securityReadable: false)), "A12").Status);
        Assert.Contains(1100, StationFactCollector.Queries["Security"]);
    }

    [Fact]
    public void Tamper_indicators_are_always_in_the_report_even_when_nothing_happened()
    {
        var r = ControlEvaluator.Evaluate(Facts());
        foreach (var id in new[] { "A05", "A06", "A07", "A12" }) Assert.Equal(ControlStatus.Conform, Check(r, id).Status);
        var unreadable = ControlEvaluator.Evaluate(Facts(securityReadable: false));
        Assert.Equal(ControlStatus.Nedeterminat, Check(unreadable, "A06").Status);
        foreach (var id in new[] { "A11", "A12" }) Assert.Contains(id, CheckGuides.Ids);
    }

    // ───────── officer key and signature ─────────

    private static (string FileText, string Pub) NewOfficer(string pass = Pass) => OfficerKey.Generate(pass);

    [Fact]
    public void Officer_key_opens_only_with_its_passphrase_and_a_station_report_key_is_not_an_officer_key()
    {
        var (file, pub) = NewOfficer();
        Assert.StartsWith(OfficerKey.Prefix, file);
        using (var k = OfficerKey.Load(file, Pass)) Assert.Equal(pub, OfficerKey.PublicKeyOf(k));
        Assert.ThrowsAny<CryptographicException>(() => OfficerKey.Load(file, OtherPass));
        var (stationPriv, _) = ReportSignature.GenerateKey();
        Assert.ThrowsAny<CryptographicException>(() => OfficerKey.Load(ReportKeyProtection.ProtectWithPassphrase(stationPriv, Pass), Pass));
        Assert.ThrowsAny<CryptographicException>(() => OfficerKey.Load(stationPriv, Pass));
        Assert.Throws<ArgumentException>(() => OfficerKey.Generate("scurt"));
        Assert.Matches("^([0-9A-F]{4}-){7}[0-9A-F]{4}$", OfficerKey.Fingerprint(pub));
    }

    [Fact]
    public void Officer_signature_is_valid_only_against_the_pinned_fingerprint()
    {
        var (file, pub) = NewOfficer();
        var fp = OfficerKey.Fingerprint(pub);
        var pdf = Pdf();
        using (var k = OfficerKey.Load(file, Pass)) OfficerSignature.Sign(pdf, k, "Ofițer Test", "AAAAA-AAAAA-AAAAA-AAAAA", Now);
        var ok = OfficerSignature.Verify(pdf, [fp.ToLowerInvariant().Replace("-", "")]);
        Assert.Equal(ReportSignatureStatus.Valid, ok.Status);
        Assert.Contains("SEMNĂTURĂ VALIDĂ A OFIȚERULUI DE SECURITATE", ok.Message);
        Assert.Equal(ReportSignatureStatus.UnknownKey, OfficerSignature.Verify(pdf, []).Status);

        // the station administrator re-signs a modified report with a key of their own: the signature is cryptographically fine but not the officer's
        var (adminFile, _) = NewOfficer(OtherPass);
        File.AppendAllText(pdf, "% schimbat\n");
        using (var k = OfficerKey.Load(adminFile, OtherPass)) OfficerSignature.Sign(pdf, k, "Ofițer Test", "", Now);
        var forged = OfficerSignature.Verify(pdf, [fp]);
        Assert.Equal(ReportSignatureStatus.UnknownKey, forged.Status);
        Assert.Contains("NU este amprenta ofițerului", forged.Message);
    }

    [Fact]
    public void Officer_signature_detects_a_changed_report_and_a_changed_signature_file()
    {
        var (file, pub) = NewOfficer();
        var fp = OfficerKey.Fingerprint(pub);
        var pdf = Pdf();
        using (var k = OfficerKey.Load(file, Pass)) OfficerSignature.Sign(pdf, k, "Ofițer Test", "", Now);
        var sig = OfficerSignature.SigPath(pdf);
        var original = File.ReadAllText(sig);
        Assert.Contains("Test", original);
        File.WriteAllText(sig, original.Replace("Test", "Tost"));
        Assert.Equal(ReportSignatureStatus.Modified, OfficerSignature.Verify(pdf, [fp]).Status);
        File.WriteAllText(sig, original);
        var bytes = File.ReadAllBytes(pdf); bytes[10] ^= 1; File.WriteAllBytes(pdf, bytes);
        Assert.Equal(ReportSignatureStatus.Modified, OfficerSignature.Verify(pdf, [fp]).Status);
        File.Delete(sig);
        Assert.Equal(ReportSignatureStatus.Missing, OfficerSignature.Verify(pdf, [fp]).Status);
    }

    // ───────── officer registry ─────────

    [Fact]
    public void First_officer_on_trust_next_ones_only_with_an_officers_approval()
    {
        var p = new ProcedureProfile();
        var (f1, pub1) = NewOfficer();
        var (f2, pub2) = NewOfficer(OtherPass);
        var first = OfficerSigning.Register(p, "Ofițer Unu", f1, Pass, null, default, Now);
        Assert.Equal(OfficerRegistry.BasisFirst, first.Basis);
        Assert.Throws<InvalidOperationException>(() => OfficerSigning.Register(p, "Ofițer Doi", f2, OtherPass, null, default, Now));
        // the "approver" is the new officer's own key: refused
        Assert.Throws<InvalidOperationException>(() => OfficerSigning.Register(p, "Ofițer Doi", f2, OtherPass, f2, OtherPass, Now));
        var second = OfficerSigning.Register(p, "Ofițer Doi", f2, OtherPass, f1, Pass, Now);
        Assert.Equal(OfficerRegistry.BasisApproved, second.Basis);
        Assert.True(OfficerRegistry.IsTrusted(p, OfficerKey.Fingerprint(pub1)));
        Assert.True(OfficerRegistry.IsTrusted(p, OfficerKey.Fingerprint(pub2)));
        // a row added to the profile file by hand, with no valid approval, is not trusted
        var (_, pub3) = NewOfficer("a-treia-parola");
        p.SecurityOfficers.Add(new SecurityOfficerRow { Name = "Admin", PublicKey = pub3, Fingerprint = OfficerKey.Fingerprint(pub3), Basis = OfficerRegistry.BasisApproved, ApprovedBy = first.Fingerprint, Approval = second.Approval });
        p.SecurityOfficers.Add(new SecurityOfficerRow { Name = "Admin2", PublicKey = pub3, Fingerprint = OfficerKey.Fingerprint(pub3), Basis = OfficerRegistry.BasisFirst });
        Assert.False(OfficerRegistry.IsTrusted(p, OfficerKey.Fingerprint(pub3)));
        Assert.Equal(2, OfficerRegistry.Trusted(p, out var problems).Count);
        Assert.Equal(2, problems.Count);
    }

    // ───────── official report ─────────

    private static ControlReport Report()
    {
        var r = ControlEvaluator.Evaluate(Facts());
        r.Anchor = new AuditAnchorPoint("0123456789abcdef", 12, new string('a', 64), AuditAnchor.Code("0123456789abcdef", 12, new string('a', 64)));
        return r;
    }

    private static List<string> PrintedText(ControlReport r, string path)
    {
        var seen = new List<string>();
        ControlReportPdf.TextObserver.Value = seen.Add;
        try { ControlReportPdf.Write(r, path, "Inspector Test"); }
        finally { ControlReportPdf.TextObserver.Value = null; }
        return seen;
    }

    [Fact]
    public void Draft_is_labelled_unsigned_with_the_anchor_and_the_registered_fingerprints()
    {
        var p = new ProcedureProfile();
        var (f1, pub1) = NewOfficer();
        OfficerSigning.Register(p, "Ofițer Unu", f1, Pass, null, default, Now);
        var r = Report();
        r.RegisteredOfficers = OfficerRegistry.Trusted(p, out _);
        var text = PrintedText(r, Path.Combine(_dir, "draft.pdf"));
        Assert.Contains(text, t => t.Contains(r.Anchor!.Line));
        Assert.Contains(text, t => t.Contains(OfficerSignature.UnsignedLabel));
        Assert.Contains(text, t => t.Contains(OfficerKey.Fingerprint(pub1)) && t.Contains("primul ofițer"));
    }

    [Fact]
    public void Signing_as_officer_renders_the_official_copy_with_the_fingerprint_and_signs_exactly_it()
    {
        var p = new ProcedureProfile();
        var (f1, pub1) = NewOfficer();
        OfficerSigning.Register(p, "Ofițer Unu", f1, Pass, null, default, Now);
        var r = Report();
        List<string>? printed = null;
        var res = OfficerSigning.SignOfficial(r, p, f1, Pass, rep => { var path = Path.Combine(_dir, "oficial.pdf"); printed = PrintedText(rep, path); return path; }, Now);
        Assert.Null(r.Officer);   // the in-memory report stays a draft
        Assert.Equal(OfficerKey.Fingerprint(pub1), res.Fingerprint);
        Assert.Contains(printed!, t => t.Contains("Amprenta cheii ofițerului: " + res.Fingerprint));
        Assert.DoesNotContain(printed!, t => t.Contains(OfficerSignature.UnsignedLabel));
        Assert.Equal(r.Anchor!.Code, res.AnchorCode);
        Assert.Equal(ReportSignatureStatus.Valid, OfficerSignature.Verify(res.Pdf, [res.Fingerprint]).Status);

        var (stranger, _) = NewOfficer(OtherPass);
        var ex = Assert.Throws<InvalidOperationException>(() => OfficerSigning.SignOfficial(r, p, stranger, OtherPass, _ => throw new InvalidOperationException("nu trebuia randat"), Now));
        Assert.Contains("nu aparține unui ofițer de securitate înregistrat", ex.Message);
    }

    [Fact]
    public void Officer_and_anchor_operations_are_written_to_the_sealed_audit()
    {
        var s = new AuthService(Path.Combine(_dir, "auth"));
        s.AuditOperation(null, OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail("AAAA-BBBB", OfficerRegistry.BasisFirst, ""));
        s.AuditOperation(null, OperationAudit.ReportOfficerSigned, OperationAudit.OfficerSignedDetail("r.pdf", "ab", "AAAA-BBBB", "AAAAA-AAAAA-AAAAA-AAAAA"));
        s.AuditOperation(null, OperationAudit.AnchorExported, OperationAudit.AnchorExportedDetail(3, "AAAAA-AAAAA-AAAAA-AAAAA", "cd", "E:\\"));
        var events = LogAnalyzer.Dfir.IO.Json.ReadLines<AuthAuditEntry>(s.AuditPath).Select(e => e.Event).ToList();
        Assert.Contains("profile.officer_registered", events);
        Assert.Contains("report.officer_signed", events);
        Assert.Contains("audit.anchor_exported", events);
        Assert.Equal(ChainStatus.Valid, s.VerifyAudit().Status);
    }
}
