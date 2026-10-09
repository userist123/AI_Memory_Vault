using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>P0: source preflight, fingerprint and mutation detection (docs/dfir/EVIDENCE_MODEL.md).</summary>
public sealed class EvidenceIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_integrity_" + Guid.NewGuid().ToString("N"));

    public EvidenceIntegrityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static byte[] At(int offset, byte[] magic, int size = 4096)
    {
        var b = new byte[size];
        magic.CopyTo(b, offset);
        return b;
    }

    [Theory]
    [InlineData("evtx", 0, "ElfFile\0")]
    [InlineData("prefetch_mam", 0, "MAM\u0004")]
    [InlineData("prefetch", 4, "SCCA")]
    [InlineData("regf", 0, "regf")]
    public void Fingerprint_recognises_format_by_content(string expected, int offset, string magic)
    {
        var f = Path.Combine(_dir, "x.bin");
        File.WriteAllBytes(f, At(offset, Encoding.Latin1.GetBytes(magic)));
        Assert.Equal(expected, EvidenceFingerprint.Detect(f));
    }

    [Fact]
    public void Fingerprint_recognises_ese_pcapng_json_and_unknown()
    {
        var f = Path.Combine(_dir, "x.bin");
        File.WriteAllBytes(f, At(4, [0xEF, 0xCD, 0xAB, 0x89]));
        Assert.Equal("ese", EvidenceFingerprint.Detect(f));
        File.WriteAllBytes(f, At(0, [0x0A, 0x0D, 0x0D, 0x0A]));
        Assert.Equal("pcapng", EvidenceFingerprint.Detect(f));
        File.WriteAllText(f, "  {\"a\":1}");
        Assert.Equal("json", EvidenceFingerprint.Detect(f));
        File.WriteAllBytes(f, At(0, [1, 2, 3, 4]));
        Assert.Equal("unknown", EvidenceFingerprint.Detect(f));
        File.WriteAllBytes(f, []);
        Assert.Equal("empty", EvidenceFingerprint.Detect(f));
    }

    private (CaseWorkspace ws, EvidenceItem ev) CaseWithEvtx(byte[] content)
    {
        var src = Path.Combine(_dir, "Security.evtx");
        File.WriteAllBytes(src, content);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "integrity", TestScopes.Valid());
        var ev = Assert.Single(InvestigationPipeline.Import(ws, [src]));
        return (ws, ev);
    }

    private static void Tamper(string path)
    {
        File.SetAttributes(path, FileAttributes.Normal);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        fs.Position = 100;
        fs.WriteByte(0x42);
    }

    [Fact]
    public void Preflight_accepts_intact_evidence_and_reports_hash_and_format()
    {
        var (ws, ev) = CaseWithEvtx(At(0, Encoding.Latin1.GetBytes("ElfFile\0")));
        var p = EvidencePreflight.Check(ev, ws.FullPath(ev.StoredPath));
        Assert.Equal(PreflightStatus.Ok, p.Status);
        Assert.True(p.CanParse);
        Assert.Equal(ev.Sha256, p.Sha256);
        Assert.Equal("evtx", p.Fingerprint);
        Assert.Equal(4096, p.Size);
    }

    [Fact]
    public void Preflight_detects_mutation_after_acquisition()
    {
        var (ws, ev) = CaseWithEvtx(At(0, Encoding.Latin1.GetBytes("ElfFile\0")));
        Tamper(ws.FullPath(ev.StoredPath));
        var p = EvidencePreflight.Check(ev, ws.FullPath(ev.StoredPath));
        Assert.Equal(PreflightStatus.HashMismatch, p.Status);
        Assert.False(p.CanParse);
        Assert.NotEqual(ev.Sha256, p.Sha256);
        Assert.Equal(ev.Sha256, p.ExpectedSha256);
    }

    [Fact]
    public void Preflight_reports_missing_and_wrong_format()
    {
        var (ws, ev) = CaseWithEvtx(At(0, Encoding.Latin1.GetBytes("regf")));
        Assert.Equal(PreflightStatus.FormatMismatch, EvidencePreflight.Check(ev, ws.FullPath(ev.StoredPath)).Status);

        var path = ws.FullPath(ev.StoredPath);
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        Assert.Equal(PreflightStatus.Missing, EvidencePreflight.Check(ev, path).Status);
    }

    [Fact]
    public void Pipeline_refuses_to_parse_mutated_evidence_and_records_the_gap()
    {
        var (ws, ev) = CaseWithEvtx(At(0, Encoding.Latin1.GetBytes("ElfFile\0")));
        Tamper(ws.FullPath(ev.StoredPath));

        var r = new InvestigationPipeline().Run(ws, Windows.Acquisition.CollectionProfile.Quick, collect: false);

        var pr = Assert.Single(r.Parsing, p => p.EvidenceId == ev.EvidenceId);
        Assert.Equal(EvidenceStatus.Failed, pr.Status);
        Assert.Contains("EVIDENCE_MUTATED", pr.Error);
        Assert.Equal(ev.Sha256, pr.ExpectedSha256);
        Assert.NotEqual(ev.Sha256, pr.SourceSha256Before);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("EVIDENCE_MUTATED"));
        Assert.DoesNotContain(r.Timeline, e => e.EvidenceId == ev.EvidenceId);
        Assert.Contains("evidence.mutated", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void Pipeline_records_source_hash_and_fingerprint_for_parsed_evidence()
    {
        var (ws, ev) = CaseWithEvtx(At(0, Encoding.Latin1.GetBytes("ElfFile\0")));

        var r = new InvestigationPipeline().Run(ws, Windows.Acquisition.CollectionProfile.Quick, collect: false);

        var pr = Assert.Single(r.Parsing, p => p.EvidenceId == ev.EvidenceId);
        Assert.Equal(ev.Sha256, pr.SourceSha256Before);
        Assert.Equal(ev.Sha256, pr.SourceSha256After);
        Assert.Equal("evtx", pr.SourceFingerprint);
        Assert.DoesNotContain("EVIDENCE_MUTATED", pr.Error);
    }
}
