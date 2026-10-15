using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security review of PR #289, probes 2 and P12: the officer store and the qualified CA folder edited by hand, outside the application, must show in A13,
/// and a later anchor written by the application must not "launder" a hand-edited state. The baseline is the anchor typed from the paper.
/// </summary>
public sealed class OfficerStateProbeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "offstate_" + Guid.NewGuid().ToString("N"));
    private readonly OfficerStore _store;
    private readonly string _trust;
    private readonly AuthService _audit;

    public OfficerStateProbeTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new OfficerStore(Path.Combine(_dir, "officers", "security_officers.json"));
        _trust = Path.Combine(_dir, "officer_trust");
        _audit = new AuthService(Path.Combine(_dir, "auth"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string State() => OfficerState.Compute(_store.Path, _trust).Detail;

    /// <summary>What the application does at the end of a control: anchor the head, then record "audit.anchor" with the state. Returns the anchored seq (the paper code).</summary>
    private long Anchor()
    {
        _audit.AuditOperation(null, OperationAudit.ControlFinished, "host=ST-1");
        var a = AuditAnchor.Current(_audit.AuditPath)!;
        _audit.AuditOperation(null, OperationAudit.AnchorRecorded, OperationAudit.AnchorRecordedDetail(a, "control") + "; " + State());
        return a.Seq;
    }

    private static StationFacts Facts(OfficerStateAssessment a)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, CollectedUtc = Now, IsAdministrator = true, OfficerAssessment = a };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Now.AddDays(-90), 100, true, ""));
        return f;
    }

    private static ControlCheck A13(OfficerStateAssessment a) => Assert.Single(ControlEvaluator.Evaluate(Facts(a)).Checks, c => c.Id == "A13");

    [Fact]
    public void Nothing_changed_since_the_paper_anchor_is_CONFORM_and_no_typed_code_is_NEDETERMINAT()
    {
        var paper = Anchor();
        Assert.Equal(ControlStatus.Conform, A13(OfficerState.Assess(_audit.AuditPath, paper, State())).Status);
        Assert.Equal(ControlStatus.Nedeterminat, A13(OfficerState.Assess(_audit.AuditPath, null, State())).Status);
    }

    [Fact]
    public void A_hand_written_officer_row_or_a_dropped_CA_file_is_NECONFORM_in_A13()
    {
        var paper = Anchor();
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, "{\"officers\":[{\"name\":\"Admin\",\"fingerprint\":\"AA\",\"certificate\":\"\",\"basis\":\"" + OfficerRegistry.BasisFirst + "\"}],\"pending\":[]}");
        var a13 = A13(OfficerState.Assess(_audit.AuditPath, paper, State()));
        Assert.Equal(ControlStatus.Neconform, a13.Status);
        Assert.Contains("modificate în afara aplicației", a13.Detail);
        Assert.Contains(a13.Evidence, e => e.Contains("lista ofițerilor"));

        File.Delete(_store.Path);
        Directory.CreateDirectory(Path.Combine(_trust, "ca"));
        File.WriteAllBytes(Path.Combine(_trust, "ca", "admin.cer"), [1, 2, 3]);
        var again = A13(OfficerState.Assess(_audit.AuditPath, paper, State()));
        Assert.Equal(ControlStatus.Neconform, again.Status);
        Assert.Contains(again.Evidence, e => e.Contains("autoritățile de încredere") && e.Contains("admin.cer"));
    }

    [Fact]
    public void P12_a_later_anchor_written_by_the_application_does_not_launder_a_hand_edited_state()
    {
        var paper = Anchor();
        // the administrator edits the store by hand, then runs any in-app action that writes an anchor with the new state
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, "{\"officers\":[{\"name\":\"Admin\"}],\"pending\":[]}");
        Anchor();
        Anchor();
        var a13 = A13(OfficerState.Assess(_audit.AuditPath, paper, State()));
        Assert.Equal(ControlStatus.Neconform, a13.Status);
        Assert.Contains(a13.Evidence, e => e.Contains("fără o operație a aplicației"));
    }

    [Fact]
    public void A_change_made_through_the_application_is_listed_for_review_not_NECONFORM()
    {
        var paper = Anchor();
        var d = _store.Load();
        d.Officers.Add(new SecurityOfficerRow { Name = "Unu", Fingerprint = "AB", Basis = OfficerRegistry.BasisFirst });
        _store.Save(d);
        _audit.AuditOperation(null, OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail("AB", "Unu", OfficerRegistry.BasisFirst, "") + "; " + State());
        Anchor();
        var a13 = A13(OfficerState.Assess(_audit.AuditPath, paper, State()));
        Assert.Equal(ControlStatus.DeVerificat, a13.Status);
        Assert.Single(a13.Evidence);
        Assert.Contains("profile.officer_registered", a13.Evidence[0]);
        Assert.DoesNotContain(OperationAudit.OfficerImportIgnored, OfficerState.ChangeEvents);   // a refused import never accepts a new state
    }
}
