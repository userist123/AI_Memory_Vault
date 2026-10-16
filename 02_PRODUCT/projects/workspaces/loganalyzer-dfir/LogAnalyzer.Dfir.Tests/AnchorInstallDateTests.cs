using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The application install date is part of the anchored state: recorded with each audit.anchor, printed next to the anchor code, compared at the next control (A14).</summary>
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
        _audit.AuditOperation(null, OperationAudit.AnchorState, OperationAudit.AnchorStateDetail(installUtc, basis));
        var a = AuditAnchor.Current(_audit.AuditPath)!;
        _audit.AuditOperation(null, OperationAudit.AnchorRecorded, OperationAudit.AnchorRecordedDetail(a, "control"));
        return a.Seq;
    }

    private static StationFacts Facts(DateTimeOffset? nowInstalled, AnchoredInstall? atAnchor, StationPosture posture = StationPosture.Standalone)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, CollectedUtc = Now, IsAdministrator = true, Posture = posture,
                                   AppInstalledUtc = nowInstalled, AppInstallBasis = "primul start al aplicației", InstallAtPaperAnchor = atAnchor };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Now.AddDays(-90), 100, true, ""));
        return f;
    }

    private static ControlCheck A14(StationFacts f) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A14");

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
    public void A14_later_install_date_than_at_the_paper_anchor_is_NECONFORM()
    {
        var at = AnchoredInstallDate.At(_audit.AuditPath, Anchor(Installed));
        var a14 = A14(Facts(Installed.AddDays(20), at));
        Assert.Equal(ControlStatus.Neconform, a14.Status);
        Assert.Contains("data instalării s-a schimbat după controlul anterior (posibilă reinițializare sau reinstalare)", a14.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ControlStatus.Neconform, A14(Facts(null, at)).Status);   // the date cannot be established any more: the traces were removed
    }

    [Fact]
    public void A14_earlier_date_is_to_verify_same_date_is_conform_and_no_typed_code_is_undetermined()
    {
        var at = AnchoredInstallDate.At(_audit.AuditPath, Anchor(Installed));
        Assert.Equal(ControlStatus.DeVerificat, A14(Facts(Installed.AddDays(-40), at)).Status);
        Assert.Equal(ControlStatus.Conform, A14(Facts(Installed, at)).Status);
        Assert.Equal(ControlStatus.Nedeterminat, A14(Facts(Installed, AnchoredInstallDate.At(_audit.AuditPath, null))).Status);
        Assert.Equal(ControlStatus.Nedeterminat, A14(Facts(Installed, null)).Status);
        // an anchor recorded before this check existed carries no date: nothing can be compared
        _audit.AuditOperation(null, OperationAudit.ControlFinished, "host=ST-1");
        var old = AuditAnchor.Current(_audit.AuditPath)!;
        _audit.AuditOperation(null, OperationAudit.AnchorRecorded, $"seq={old.Seq}; anchor={old.Code}; chain={old.ChainId}; reason=control");
        Assert.Equal(ControlStatus.Nedeterminat, A14(Facts(Installed, AnchoredInstallDate.At(_audit.AuditPath, old.Seq))).Status);
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

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void A14_applies_in_every_edition_and_has_a_guide(StationPosture posture)
    {
        Assert.True(ControlApplicability.Applies("A14", posture));
        Assert.Contains("A14", CheckGuides.Ids);
    }

    [Fact]
    public void The_report_prints_the_install_date_next_to_the_anchor_code()
    {
        var r = ControlEvaluator.Evaluate(Facts(Installed, null));
        r.Anchor = new AuditAnchorPoint("0123456789abcdef", 12, new string('a', 64), AuditAnchor.Code("0123456789abcdef", 12, new string('a', 64)));
        var lines = ControlReportPdf.OfficerSectionLines(r);
        Assert.Contains(lines, l => l.Contains(r.Anchor.Code));
        Assert.Contains(lines, l => l.Contains("Data instalării aplicației") && l.Contains("2026-09-01 08:30 UTC") && l.Contains("primul start"));
    }
}
