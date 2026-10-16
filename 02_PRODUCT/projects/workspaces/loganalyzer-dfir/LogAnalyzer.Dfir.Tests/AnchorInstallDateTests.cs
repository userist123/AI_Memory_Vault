using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The application install date is part of the anchored state: recorded with each audit.anchor, printed next to the anchor code, compared at the next control (A16).</summary>
public sealed class AnchorInstallDateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Installed = new(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "anchorinst_" + Guid.NewGuid().ToString("N"));
    private readonly AuthService _audit;

    public AnchorInstallDateTests()
    {
        Directory.CreateDirectory(_dir);
        _audit = new AuthService(Path.Combine(_dir, "auth"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>The end of a control: record the anchored state (install date), anchor the head (that state entry), record "audit.anchor". Returns the anchored seq (the paper code).</summary>
    private long Anchor(DateTimeOffset? installUtc, string basis = "primul start al aplicației; fișierele programului")
    {
        _audit.AuditOperation(null, OperationAudit.ControlFinished, "host=ST-1");
        _audit.AuditOperation(null, OperationAudit.AnchorState, OperationAudit.AnchorStateDetail(installUtc, basis, AuditAnchor.ChainIdentityOf(_audit.AuditPath)));
        var a = AuditAnchor.Current(_audit.AuditPath)!;
        _audit.AuditOperation(null, OperationAudit.AnchorRecorded, OperationAudit.AnchorRecordedDetail(a, "control"));
        return a.Seq;
    }

    private StationFacts Facts(DateTimeOffset? nowInstalled, AnchoredInstall? atAnchor, StationPosture posture = StationPosture.Standalone, ChainIdentity? chainNow = null)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, CollectedUtc = Now, IsAdministrator = true, Posture = posture,
                                   AppInstalledUtc = nowInstalled, AppInstallBasis = "primul start al aplicației", InstallAtPaperAnchor = atAnchor,
                                   CurrentChain = chainNow ?? AuditAnchor.ChainIdentityOf(_audit.AuditPath) };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Now.AddDays(-90), 100, true, ""));
        return f;
    }

    private ControlCheck A16(StationFacts f) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A16");

    [Fact]
    public void The_anchor_event_carries_the_install_date_and_it_is_read_back_at_the_paper_anchor()
    {
        var paper = Anchor(Installed, "basis; with; separators");
        Assert.Contains("install_utc=2026-09-01T08:30:00Z", OperationAudit.AnchorStateDetail(Installed, "b"));
        var at = AnchoredInstallDate.At(_audit.AuditPath, paper);
        Assert.True(at.Determinable && at.Recorded);
        Assert.Equal(Installed, at.Utc);
        Assert.Contains("basis", at.Basis);
        Assert.False(AnchoredInstallDate.At(_audit.AuditPath, null).Determinable);
        var unknown = Anchor(null, "lanțul de audit a fost reinițializat");
        var u = AnchoredInstallDate.At(_audit.AuditPath, unknown);
        Assert.True(u.Determinable && u.Recorded);
        Assert.Null(u.Utc);
    }

    [Fact]
    public void Reviewer_probe_an_upgrade_or_Prefetch_eviction_moves_the_trace_date_later_and_is_only_to_verify()
    {
        var at = AnchoredInstallDate.At(_audit.AuditPath, Anchor(Installed));
        // upgrade: the program files are recreated later, the earliest trace moves later; same audit chain
        var upgrade = A16(Facts(Installed.AddDays(20), at));
        Assert.Equal(ControlStatus.DeVerificat, upgrade.Status);
        Assert.Contains("s-a mutat mai târziu", upgrade.Detail);
        Assert.DoesNotContain("incident", upgrade.Detail + upgrade.Recommendation, StringComparison.OrdinalIgnoreCase);
        // Prefetch eviction or a control under another Windows account: the date cannot be computed now
        Assert.Equal(ControlStatus.DeVerificat, A16(Facts(null, at)).Status);
        // within a day: the same date
        Assert.Equal(ControlStatus.Conform, A16(Facts(Installed.AddHours(12), at)).Status);
    }

    [Fact]
    public void Reviewer_probe_a_reinitialised_audit_chain_changes_its_identity_and_is_NECONFORM()
    {
        var at = AnchoredInstallDate.At(_audit.AuditPath, Anchor(Installed));
        Assert.NotNull(at.Chain);
        var other = new ChainIdentity(new string('b', 64), "2026-10-10T07:00:00Z", "app.first_start");
        var a16 = A16(Facts(Installed, at, chainNow: other));
        Assert.Equal(ControlStatus.Neconform, a16.Status);
        Assert.Contains("jurnalul aplicației a fost reinițializat după controlul anterior (identitatea jurnalului s-a schimbat)", a16.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ControlStatus.Conform, A16(Facts(Installed, at)).Status);   // the same chain
    }

    [Fact]
    public void The_anchor_is_not_issued_when_the_state_entry_cannot_be_written_and_the_stored_values_are_read_back()
    {
        _audit.AuditOperation(null, OperationAudit.ControlFinished, "host=ST-1");    // the chain already has records (app.first_start in a real station)
        var failed = AnchorWriter.Write(_audit.AuditPath, (ev, detail) => false, Installed, "temei", "officer_state=store:none,trust:none,files:", "control");
        Assert.Null(failed.Anchor);
        Assert.Contains("nu a fost emis", failed.Error);
        var ok = AnchorWriter.Write(_audit.AuditPath, (ev, detail) => { _audit.AuditOperation(null, ev, detail); return true; }, Installed, "temei; cu separatori",
                                    "officer_state=store:none,trust:none,files:", "control");
        Assert.NotNull(ok.Anchor);
        Assert.Null(ok.Error);
        Assert.Equal(OperationAudit.AnchorState, AuditAnchor.Events(_audit.AuditPath).First(e => e.Seq == ok.Anchor!.Seq).Event);
        Assert.Equal(Installed, ok.Stored!.Utc);
        Assert.Contains("temei", ok.Stored.Basis);
        Assert.NotNull(ok.Stored.Chain);
    }

    [Fact]
    public void A16_earlier_date_is_to_verify_same_date_is_conform_and_no_typed_code_is_undetermined()
    {
        var at = AnchoredInstallDate.At(_audit.AuditPath, Anchor(Installed));
        Assert.Equal(ControlStatus.DeVerificat, A16(Facts(Installed.AddDays(-40), at)).Status);
        Assert.Equal(ControlStatus.Conform, A16(Facts(Installed, at)).Status);
        Assert.Equal(ControlStatus.Nedeterminat, A16(Facts(Installed, AnchoredInstallDate.At(_audit.AuditPath, null))).Status);
        Assert.Equal(ControlStatus.Nedeterminat, A16(Facts(Installed, null)).Status);
        // an anchor recorded before this check existed carries no date: nothing can be compared
        _audit.AuditOperation(null, OperationAudit.ControlFinished, "host=ST-1");
        var old = AuditAnchor.Current(_audit.AuditPath)!;
        _audit.AuditOperation(null, OperationAudit.AnchorRecorded, $"seq={old.Seq}; anchor={old.Code}; chain={old.ChainId}; reason=control");
        Assert.Equal(ControlStatus.Nedeterminat, A16(Facts(Installed, AnchoredInstallDate.At(_audit.AuditPath, old.Seq))).Status);
    }

    [Fact]
    public void The_install_date_is_the_anchored_entry_itself_so_a_value_written_after_the_anchor_is_never_used_and_a_rewrite_breaks_A11()
    {
        var paper = Anchor(Installed);
        var code = AuditAnchor.Current(_audit.AuditPath) is { } _ ? AuditAnchor.Events(_audit.AuditPath).First(e => e.Seq == paper) : null;
        Assert.Equal(OperationAudit.AnchorState, code!.Event);
        // a later state event with another date (e.g. written after a reinstall) does not change the anchored value
        _audit.AuditOperation(null, OperationAudit.AnchorState, OperationAudit.AnchorStateDetail(Installed.AddDays(30), "reinstalare"));
        Assert.Equal(Installed, AnchoredInstallDate.At(_audit.AuditPath, paper).Utc);
    }

    [Fact]
    public void A_code_typed_from_paper_that_the_log_no_longer_contains_is_not_reported_as_missing()
    {
        Anchor(Installed);
        var code = AuditAnchor.Current(_audit.AuditPath)!.Code;      // the code the officer has on paper (any entry of the old log)
        // the log is reinitialised: a new chain from zero
        File.Delete(_audit.AuditPath);
        File.Delete(HashChain.HeadPath(_audit.AuditPath));
        _audit.AuditOperation(null, OperationAudit.ControlFinished, "host=ST-1");
        var cont = AuditAnchor.Verify(_audit.AuditPath, code);
        Assert.Equal(AnchorContinuityStatus.Rewritten, cont.Status);
        var at = AnchoredInstallDate.For(_audit.AuditPath, cont, fromStation: false);
        Assert.False(at.Determinable);
        Assert.Contains("nu mai conține codul de pe procesul-verbal (vezi A11)", at.Why);
        var a16 = A16(Facts(Installed, at));
        Assert.Equal(ControlStatus.Nedeterminat, a16.Status);
        Assert.Contains("nu mai conține codul de pe procesul-verbal (vezi A11)", a16.Detail);
        Assert.DoesNotContain("nu s-a introdus", a16.Detail);
        // no code typed: still said as such
        Assert.Contains("nu s-a introdus", AnchoredInstallDate.For(_audit.AuditPath, AuditAnchor.Verify(_audit.AuditPath, ""), false).Why);
        // the station's proposal is not a typed code
        Assert.Contains("propus de stație", AnchoredInstallDate.For(_audit.AuditPath, cont with { Status = AnchorContinuityStatus.Continuous, MatchedSeq = 1 }, fromStation: true).Why);
    }

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void A16_applies_in_every_edition_and_has_a_guide(StationPosture posture)
    {
        Assert.True(ControlApplicability.Applies("A16", posture));
        Assert.Contains("A16", CheckGuides.Ids);
    }

    [Fact]
    public void The_report_prints_the_values_stored_in_the_anchored_state_entry_not_a_fresh_computation()
    {
        var r = ControlEvaluator.Evaluate(Facts(Installed.AddDays(5), null));            // computed now: another date
        r.Anchor = new AuditAnchorPoint("0123456789abcdef", 12, new string('a', 64), AuditAnchor.Code("0123456789abcdef", 12, new string('a', 64)));
        r.AnchoredState = new AnchoredInstall(true, true, Installed, "primul start al aplicației", "", new ChainIdentity(new string('c', 64), "2026-09-01T08:30:00Z", "app.first_start"));
        var lines = ControlReportPdf.OfficerSectionLines(r);
        Assert.Contains(lines, l => l.Contains(r.Anchor.Code));
        Assert.Contains(lines, l => l.Contains("Data instalării aplicației") && l.Contains("2026-09-01 08:30 UTC") && l.Contains("primul start") && l.Contains("cccccccccccccccc"));
        Assert.DoesNotContain(lines, l => l.Contains("2026-09-06"));
    }
}
