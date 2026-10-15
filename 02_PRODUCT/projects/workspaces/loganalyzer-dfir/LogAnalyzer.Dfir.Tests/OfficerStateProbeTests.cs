using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Security review of PR #289, probe 2: the officer store and the qualified CA folder edited by hand, outside the application, must show in A13.</summary>
public sealed class OfficerStateProbeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "offstate_" + Guid.NewGuid().ToString("N"));
    public OfficerStateProbeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static StationFacts Facts()
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, CollectedUtc = Now, IsAdministrator = true };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Now.AddDays(-90), 100, true, ""));
        return f;
    }

    [Fact]
    public void A_hand_written_officer_row_or_a_dropped_CA_file_is_NECONFORM_in_A13()
    {
        var store = new OfficerStore(Path.Combine(_dir, "officers", "security_officers.json"));
        var trustDir = Path.Combine(_dir, "officer_trust");
        var audit = new AuthService(Path.Combine(_dir, "auth"));
        var d = store.Load();
        store.Save(d);
        // the application records the state with every officer / trust event and every anchor
        audit.AuditOperation(null, OperationAudit.AnchorRecorded, "seq=1; anchor=X; chain=c; reason=control; " + OfficerState.Compute(store.Path, trustDir).Detail);
        var recorded = OfficerState.LastRecorded(audit.AuditPath);
        Assert.Equal(OfficerState.Compute(store.Path, trustDir).Detail, recorded);

        var f = Facts();
        f.OfficerListChanges = [];
        f.OfficerStateRecorded = recorded; f.OfficerStateNow = OfficerState.Compute(store.Path, trustDir).Detail;
        Assert.Equal(ControlStatus.Conform, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A13").Status);

        // probe: a first-officer row written by hand into the store
        File.WriteAllText(store.Path, "{\"officers\":[{\"name\":\"Admin\",\"fingerprint\":\"AA\",\"certificate\":\"\",\"basis\":\"" + OfficerRegistry.BasisFirst + "\"}],\"pending\":[]}");
        f.OfficerStateNow = OfficerState.Compute(store.Path, trustDir).Detail;
        var a13 = Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A13");
        Assert.Equal(ControlStatus.Neconform, a13.Status);
        Assert.Contains("modificate în afara aplicației", a13.Detail);
        Assert.Contains(a13.Evidence, e => e.Contains("lista ofițerilor"));

        // probe: a CA certificate dropped into the trust folder
        File.WriteAllText(store.Path, "{\"officers\":[],\"pending\":[]}");
        store.Save(store.Load());
        audit.AuditOperation(null, OperationAudit.OfficerTrustImported, "file=x; " + OfficerState.Compute(store.Path, trustDir).Detail);
        Directory.CreateDirectory(Path.Combine(trustDir, "ca"));
        File.WriteAllBytes(Path.Combine(trustDir, "ca", "admin.cer"), [1, 2, 3]);
        f.OfficerStateRecorded = OfficerState.LastRecorded(audit.AuditPath);
        f.OfficerStateNow = OfficerState.Compute(store.Path, trustDir).Detail;
        var again = Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A13");
        Assert.Equal(ControlStatus.Neconform, again.Status);
        Assert.Contains(again.Evidence, e => e.Contains("autoritățile de încredere") && e.Contains("admin.cer"));
    }

    [Fact]
    public void A_store_present_but_never_recorded_is_NECONFORM_and_nothing_at_all_is_CONFORM()
    {
        var store = Path.Combine(_dir, "officers", "security_officers.json");
        var trustDir = Path.Combine(_dir, "officer_trust");
        var f = Facts();
        f.OfficerListChanges = [];
        f.OfficerStateRecorded = null; f.OfficerStateNow = OfficerState.Compute(store, trustDir).Detail;
        Assert.Equal(ControlStatus.Conform, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A13").Status);
        Directory.CreateDirectory(Path.GetDirectoryName(store)!);
        File.WriteAllText(store, "{\"officers\":[],\"pending\":[]}");
        f.OfficerStateNow = OfficerState.Compute(store, trustDir).Detail;
        Assert.Equal(ControlStatus.Neconform, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "A13").Status);
    }
}
