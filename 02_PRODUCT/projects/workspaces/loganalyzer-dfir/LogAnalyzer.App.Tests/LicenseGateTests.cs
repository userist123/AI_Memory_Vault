using System.IO;
using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Core.Services;
using LicenseService = LogAnalyzer.Core.Services.LicenseService;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>The gate is process-wide state: these tests run alone, never in parallel with a test that runs an operation of a view model.</summary>
[CollectionDefinition("LicenseGate", DisableParallelization = true)]
public sealed class LicenseGateCollection { }

/// <summary>
/// Planner #9 item 6: licence rights enforced inside the operations (not only at navigation) and the read-only state of an expired licence.
/// Keys are ephemeral, in memory; the licence file is a temp file; the clock of the gate is injected where the test needs another day.
/// </summary>
[Collection("LicenseGate")]
public sealed class LicenseGateTests : IDisposable
{
    private const string Hwid = "ABCD1234EFAB5678";
    private static readonly DateTime Past = new(2020, 1, 1);
    private static readonly DateTime Future = new(2099, 1, 1);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-licgate-" + Guid.NewGuid().ToString("N"));
    private string LicFile => Path.Combine(_dir, "license.lic");

    public LicenseGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        LicenseGate.Configure(null);
        LicenseGate.Refusal = null;
        CollectAllState.Handler = null;
        _key.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private LicenseService Service(StationPosture p = StationPosture.Standalone, string hw = Hwid) =>
        new([Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())], LicFile, () => hw, p);

    private string Issue(DateTime expiry, StationPosture p = StationPosture.Standalone, params string[] features) =>
        LicenseService.SignV2(_key, Hwid, expiry, LicenseFeatures.ModuleId(p), "Unitatea X", features.Length == 0 ? LicenseFeatures.DefaultFor(p) : features, "LIC-GATE");

    /// <summary>A service that did the start-up check of a licence file with this text, and the gate bound to it.</summary>
    private LicenseService Start(string licence, StationPosture p = StationPosture.Standalone, string hw = Hwid, Func<DateTime>? now = null)
    {
        File.WriteAllText(LicFile, licence);
        var svc = Service(p, hw);
        svc.CheckSaved();
        LicenseGate.Configure(svc, now);
        return svc;
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Unb64(string s) { var t = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4)); }

    private static string Tamper(string licence, string from, string to)
    {
        var parts = licence.Split('.');
        var json = Encoding.UTF8.GetString(Unb64(parts[1]));
        Assert.Contains(from, json, StringComparison.Ordinal);
        return $"LA2.{B64(Encoding.UTF8.GetBytes(json.Replace(from, to, StringComparison.Ordinal)))}.{parts[2]}";
    }

    // ---- read-only state: the decision table ----

    [Fact]
    public void An_expired_but_otherwise_valid_licence_starts_read_only_grants_nothing_and_shows_the_banner()
    {
        var svc = Start(Issue(Past));
        Assert.Equal(LicenseCheck.Expired, svc.CheckSaved());
        Assert.NotNull(svc.ExpiredClaims);
        Assert.Null(svc.VerifiedClaims);                                   // nothing is granted by it
        Assert.True(LicenseGate.IsReadOnly);
        Assert.Equal("Licență expirată — mod doar citire (data expirării 2020-01-01)", LicenseGate.ReadOnlyBanner);
        // every capability is refused with the read-only text: expired = no write capabilities
        foreach (var f in LicenseFeatures.All)
        {
            Assert.False(LicenseGate.Require(f, out var message), f);
            Assert.Contains("doar citire", message);
        }
        // reading stays possible: the pages open (the licence's rights are used for navigation only, never as authority)
        Assert.Equal(LicenseFeatures.DefaultFor(StationPosture.Standalone), MainViewModel.RightsFrom(svc));
    }

    public static IEnumerable<object[]> EveryOtherFailure()
    {
        yield return ["expired and tampered (exp changed, not re-signed)"];
        yield return ["expired, wrong module"];
        yield return ["expired, wrong hardware"];
        yield return ["malformed"];
        yield return ["missing file"];
        yield return ["tampered, not expired"];
    }

    [Theory]
    [MemberData(nameof(EveryOtherFailure))]
    public void Every_other_failure_is_not_read_only_and_everything_is_refused(string kind)
    {
        var svc = Service();
        switch (kind)
        {
            case "expired and tampered (exp changed, not re-signed)":
                File.WriteAllText(LicFile, Tamper(Issue(Past), "\"exp\":\"2020-01-01\"", "\"exp\":\"2021-01-01\"")); break;
            case "expired, wrong module":
                File.WriteAllText(LicFile, Issue(Past, StationPosture.Internet)); break;      // a continent licence on an insula build
            case "expired, wrong hardware":
                File.WriteAllText(LicFile, Issue(Past)); svc = Service(hw: "0000111122223333"); break;
            case "malformed":
                File.WriteAllText(LicFile, "LA2.abc.def"); break;
            case "missing file":
                break;
            case "tampered, not expired":
                File.WriteAllText(LicFile, Tamper(Issue(Future), "\"exp\":\"2099-01-01\"", "\"exp\":\"2098-01-01\"")); break;
        }
        var result = svc.CheckSaved();
        Assert.NotEqual(LicenseCheck.Valid, result);
        Assert.Null(svc.ExpiredClaims);                                    // the app keeps today's behaviour: activation window / shutdown
        Assert.Null(svc.VerifiedClaims);
        LicenseGate.Configure(svc);
        Assert.False(LicenseGate.IsReadOnly);
        Assert.Null(LicenseGate.ReadOnlyBanner);
        Assert.Empty(MainViewModel.RightsFrom(svc));
        Assert.All(LicenseFeatures.All, f => Assert.False(LicenseGate.Require(f, out _)));
    }

    [Fact]
    public void A_valid_licence_grants_exactly_its_capabilities_and_is_not_read_only()
    {
        Start(Issue(Future, StationPosture.Standalone, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal, LicenseFeatures.Registers));   // no report.pv
        Assert.False(LicenseGate.IsReadOnly);
        Assert.True(LicenseGate.Require(LicenseFeatures.ControlStation, out _));
        Assert.True(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));
        Assert.False(LicenseGate.Require(LicenseFeatures.ReportPv, out var message));
        Assert.Equal(LicenseGate.NotLicensedMessage, message);
        Assert.False(LicenseGate.Require(LicenseFeatures.ResponseHost, out _));
        Assert.False(LicenseGate.Require("made.up", out _));               // a name outside the catalogue is never granted
        Assert.False(LicenseGate.Require("", out _));
    }

    [Fact]
    public void A_licence_that_expires_while_the_application_runs_turns_read_only_without_a_restart()
    {
        var clock = new DateTime(2098, 12, 31, 23, 0, 0, DateTimeKind.Utc);
        Start(Issue(Future), now: () => clock);                            // valid through 2099-01-01 (that whole day)
        Assert.True(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));
        clock = new DateTime(2099, 1, 1, 23, 59, 0, DateTimeKind.Utc);
        Assert.True(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));
        clock = new DateTime(2099, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(LicenseGate.Require(LicenseFeatures.CaseLocal, out var message));
        Assert.Contains("doar citire", message);
        Assert.True(LicenseGate.IsReadOnly);
    }

    [Fact]
    public void Every_refusal_is_passed_to_the_audit_sink_with_the_capability_and_the_reason()
    {
        var seen = new List<(string, string)>();
        Start(Issue(Past));
        LicenseGate.Refusal = (c, r) => seen.Add((c, r));
        Assert.False(LicenseGate.Require(LicenseFeatures.ReportPv, out _));
        Assert.Equal([(LicenseFeatures.ReportPv, "expired")], seen);
        Start(Issue(Future, StationPosture.Standalone, LicenseFeatures.CaseLocal));
        seen.Clear(); LicenseGate.Refusal = (c, r) => seen.Add((c, r));
        Assert.False(LicenseGate.Require(LicenseFeatures.ReportPv, out _));
        Assert.True(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));
        Assert.Equal([(LicenseFeatures.ReportPv, "not_in_licence")], seen);   // a grant is not recorded as a refusal
    }

    [Fact]
    public void A_sink_that_fails_never_turns_a_refusal_into_a_grant()
    {
        Start(Issue(Past));
        LicenseGate.Refusal = (_, _) => throw new IOException("audit unavailable");
        Assert.False(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));
    }

    // ---- the operations refuse by themselves, called directly ----

    [Fact]
    public void The_proces_verbal_export_needs_report_pv_even_when_called_directly()
    {
        Start(Issue(Future, StationPosture.Standalone, LicenseFeatures.ControlStation, LicenseFeatures.CaseLocal, LicenseFeatures.Registers));
        var vm = new StationControlViewModel();
        vm.SaveReportCommand.Execute(null);                                // "Raport pentru proces-verbal (PDF)"
        Assert.Equal(LicenseGate.NotLicensedMessage, vm.Status);
        vm.Status = "";
        vm.SaveReportCopyCommand.Execute(null);
        Assert.Equal(LicenseGate.NotLicensedMessage, vm.Status);
        vm.Status = "";
        vm.ExportForOfficerCommand.Execute(null);
        Assert.Equal(LicenseGate.NotLicensedMessage, vm.Status);
    }

    [Fact]
    public async Task The_station_control_run_needs_control_station_and_collects_nothing_without_it()
    {
        Start(Issue(Future, StationPosture.Standalone, LicenseFeatures.CaseLocal, LicenseFeatures.ReportPv));
        var vm = new StationControlViewModel();
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(LicenseGate.NotLicensedMessage, vm.Status);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Checks);
        vm.Status = "";
        await vm.RunQuickAsync();
        Assert.Equal(LicenseGate.NotLicensedMessage, vm.Status);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Expired_licence_refuses_the_control_run_and_the_pv()
    {
        Start(Issue(Past));
        var vm = new StationControlViewModel();
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Contains("Licență expirată — mod doar citire (data expirării 2020-01-01)", vm.Status);
        Assert.Empty(vm.Checks);
        vm.SaveReportCommand.Execute(null);
        Assert.Contains("doar citire", vm.Status);
    }

    [Fact]
    public async Task Collection_needs_case_local_at_every_entry_point()
    {
        Start(Issue(Future, StationPosture.Standalone, LicenseFeatures.ControlStation, LicenseFeatures.ReportPv, LicenseFeatures.Registers));   // no case.local
        var handlerRan = false;
        CollectAllState.Handler = () => { handlerRan = true; return Task.CompletedTask; };
        await CollectAllState.RunAsync();                                   // „Colectează tot” / „Colectează acum”
        Assert.False(handlerRan);
        Assert.Equal(LicenseGate.NotLicensedMessage, CollectAllState.Instance.Status);

        var inv = new InvestigationViewModel { CollectFromThisStation = true };
        await inv.RunCommand.ExecuteAsync(null);                            // the investigation page's own collection / analysis
        Assert.Contains(LicenseGate.NotLicensedMessage, inv.Log);
        Assert.False(inv.IsBusy);
        Assert.False(inv.HasResult);
        inv.Log = "";
        await inv.CollectAllCommand.ExecuteAsync(null);
        Assert.Contains(LicenseGate.NotLicensedMessage, inv.Log);
        Assert.False(inv.HasResult);
    }

    [Fact]
    public void A_register_is_not_saved_without_registers_and_not_when_the_licence_is_expired()
    {
        foreach (var licence in new[] { Issue(Future, StationPosture.Standalone, LicenseFeatures.CaseLocal), Issue(Past) })
        {
            Start(licence);
            var path = Path.Combine(_dir, "users-" + Guid.NewGuid().ToString("N") + ".json");
            var vm = new UsersRegisterViewModel(path);
            vm.Data.Rows.Add("Ion Popescu", @"DOM\ion", "", "", "", "", "", "");
            vm.SaveCommand.Execute(null);
            Assert.False(File.Exists(path));
            Assert.True(vm.Status == LicenseGate.NotLicensedMessage || vm.Status.Contains("doar citire"), vm.Status);
        }
    }

    [Fact]
    public void A_register_is_saved_when_the_licence_grants_registers()
    {
        Start(Issue(Future));                                               // the control: with the right the same save works
        var path = Path.Combine(_dir, "users-ok.json");
        var vm = new UsersRegisterViewModel(path);
        vm.Data.Rows.Add("Ion Popescu", @"DOM\ion", "", "", "", "", "", "");
        vm.SaveCommand.Execute(null);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void The_interventions_register_is_not_saved_without_registers_or_when_the_licence_is_expired_and_is_saved_with_the_right()
    {
        string[] row = ["2026-11-01", "10:30", "S1", "software", "Program X", "Instalare", "Ion Popescu", "Maria Ionescu", "", "", "", ""];
        foreach (var licence in new[] { Issue(Future, StationPosture.Standalone, LicenseFeatures.CaseLocal), Issue(Past) })
        {
            Start(licence);
            var path = Path.Combine(_dir, "interv-" + Guid.NewGuid().ToString("N") + ".json");
            var vm = new InterventionRegisterViewModel(path);
            vm.Data.Rows.Add(row.Cast<object>().ToArray());
            vm.SaveCommand.Execute(null);
            Assert.False(File.Exists(path));
            Assert.True(vm.Status == LicenseGate.NotLicensedMessage || vm.Status.Contains("doar citire"), vm.Status);
        }
        Start(Issue(Future));                                               // the control: with the right the same save works
        var ok = Path.Combine(_dir, "interv-ok.json");
        var vm2 = new InterventionRegisterViewModel(ok);
        vm2.Data.Rows.Add(row.Cast<object>().ToArray());
        vm2.SaveCommand.Execute(null);
        Assert.True(File.Exists(ok), vm2.Status);
    }

    [Fact]
    public void Containment_of_the_station_needs_response_host_and_nothing_is_run_without_it()
    {
        Start(Issue(Past));
        // A target that is not an address: with the gate the answer is NOT_AUTHORIZED before the target is even looked at; without it the answer would be REJECTED (and no rule is created).
        var res = new HostDefense().BlockMaliciousIoC("not-an-address");
        Assert.Equal(LogAnalyzer.Core.Interfaces.DefenseActionResult.NotAuthorized, res.Status);
        Assert.Contains("doar citire", res.Message);
        Assert.False(res.Success);
    }

    // ---- structure: the gate is wired where it must be (source checks, as the other wiring tests of this assembly) ----

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Fact]
    public void The_start_up_binds_the_gate_and_an_expired_licence_still_gets_the_activation_window_with_a_read_only_choice()
    {
        var app = Src("LogAnalyzer.App", "App.xaml.cs");
        Assert.Contains("LicenseGate.Configure(licenseService)", app);
        // renewal stays possible: the window is shown for an expired licence too, never skipped
        Assert.Contains("if (!licenseService.IsActivated())", app);
        Assert.DoesNotContain("ExpiredClaims is null)", app);
        Assert.Contains("activationWindow.OfferReadOnly(", app);
        Assert.Contains("activated != true && !continueReadOnly", app);     // closing without a choice is the old cancel (shutdown)
        Assert.Contains("activationWindow.ReadOnlyChosen && licenseService.ExpiredClaims is not null", app);
        Assert.Contains("LicenseGate.IsConfigured", app);                   // production assert: never run the operations without the gate
        Assert.Contains("Continuă în mod doar citire", Src("LogAnalyzer.App", "Views", "ActivationWindow.xaml"));
        Assert.Contains("Reînnoiește licența", Src("LogAnalyzer.App", "MainWindow.xaml"));   // later renewal from the read-only banner
        Assert.Contains("licence.read_only_start", app);                    // the read-only start is a fact of the sealed audit
        Assert.Contains("LicenseGate.Refusal", app);
        Assert.Contains("Shutdown called due to license failure", app);     // every other failure still shuts down
    }

    [Fact]
    public void The_host_containment_entry_points_ask_the_gate_but_lifting_the_isolation_does_not()
    {
        var src = Src("LogAnalyzer.Response", "Defense", "SystemDefenseExecutionService.cs");
        Assert.Contains("IsolateHostFromNetwork() => NotLicensed() ??", src);
        Assert.Contains("BlockMaliciousIoC(string iocTarget) => NotLicensed() ??", src);
        Assert.Contains("RestoreNetworkAccess() => SystemDefenseExecutionService.RestoreNetworkAccess()", src);
        Assert.Contains("LicenseFeatures.ResponseHost", src);
        var contain = Src("LogAnalyzer.App", "Edition", "Unclassified", "ContainmentViewModel.cs");
        Assert.Contains("LicenseGate.Require(LogAnalyzer.Core.Services.LicenseFeatures.ResponseHost", contain);
    }

    [Fact]
    public void The_transfer_register_save_asks_the_gate()
    {
        Assert.Contains("LicenseGate.Require(LogAnalyzer.Core.Services.LicenseFeatures.Registers", Src("LogAnalyzer.App", "ViewModels", "TransferViewModel.cs"));
    }

    // ---- review: read-only on the pages the navigation opens ----

    [Fact]
    public void RequireWritable_refuses_only_when_expired_or_nothing_verified_and_a_write_is_audited()
    {
        Assert.True(LicenseGate.RequireWritable(out _));                    // unconfigured: no licence regime
        var seen = new List<(string, string)>();
        Start(Issue(Past));
        LicenseGate.Refusal = (c, r) => seen.Add((c, r));
        Assert.False(LicenseGate.RequireWritable(out var message));
        Assert.Contains("doar citire", message);
        Assert.Equal([("write", "expired")], seen);
        Start(Issue(Future, StationPosture.Standalone, LicenseFeatures.CaseLocal));   // needs no capability of its own
        Assert.True(LicenseGate.RequireWritable(out _));
        File.WriteAllText(LicFile, "garbage");
        var svc = Service(); svc.CheckSaved(); LicenseGate.Configure(svc);
        Assert.False(LicenseGate.RequireWritable(out _));                   // nothing verified: fail closed
    }

    [Fact]
    public void The_procedure_profile_cannot_be_saved_while_the_licence_is_expired()
    {
        Start(Issue(Past));
        var provider = new LogAnalyzer.Dfir.Profile.ProfileProvider(Path.Combine(_dir, "p.json"));
        var vm = new ProcedureProfileViewModel(provider);
        vm.Tables.First(t => t.Table == LogAnalyzer.Dfir.Profile.ProfileTable.Holidays).Data.Rows.Add("2026-12-25");
        vm.SaveCommand.Execute(null);
        Assert.False(File.Exists(provider.Path));
        Assert.Contains("doar citire", vm.Status);
    }

    [Fact]
    public void The_licence_floor_is_the_highest_time_ever_recorded_even_after_a_set_back_start_and_a_clock_correction()
    {
        // review round 2 HIGH: the clock baseline skips a record later than the next one and restarts at clock.ack, so from the second start after a
        // clock set back it no longer remembered February; the licence floor must.
        var audit = Path.Combine(_dir, "auth_audit.jsonl");
        var feb = new DateTimeOffset(2099, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var back = new DateTimeOffset(2098, 12, 1, 0, 0, 0, TimeSpan.Zero);
        LogAnalyzer.Dfir.Case.ClockMemory.RecordSeen(audit, feb, "start 0");
        LogAnalyzer.Dfir.Case.ClockMemory.RecordSeen(audit, back, "start 1 (ceas dat înapoi)");
        LogAnalyzer.Dfir.Case.ClockMemory.RecordSeen(audit, back.AddMinutes(5), "start 2");
        LogAnalyzer.Dfir.Case.ClockMemory.Acknowledge(audit, back.AddMinutes(6), "admin", "ora a fost corectată");
        Assert.NotEqual(feb, LogAnalyzer.Dfir.Case.ClockMemory.LatestRecorded(audit));   // the baseline forgot February (that was the hole)
        Assert.Equal(feb, LogAnalyzer.Dfir.Case.ClockMemory.HighestRecorded(audit));
        Assert.Null(LogAnalyzer.Dfir.Case.ClockMemory.HighestRecorded(Path.Combine(_dir, "none.jsonl")));
    }

    [Fact]
    public void A_clock_set_back_after_the_station_saw_a_later_time_stays_expired()
    {
        var clock = new DateTime(2098, 12, 1, 0, 0, 0, DateTimeKind.Utc);     // set back: before the expiry day 2099-01-01
        Start(Issue(Future), now: () => clock);
        Assert.True(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));   // control: without a recorded later time it is valid
        LicenseGate.RaiseClockFloor(new DateTime(2099, 2, 1, 0, 0, 0, DateTimeKind.Utc));   // the clock memory has seen February 2099
        Assert.False(LicenseGate.Require(LicenseFeatures.CaseLocal, out var message));
        Assert.Contains("doar citire", message);
        Assert.False(LicenseGate.RequireWritable(out _));
        Assert.True(LicenseGate.IsReadOnly);
        LicenseGate.RaiseClockFloor(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));   // the floor only rises
        Assert.False(LicenseGate.Require(LicenseFeatures.CaseLocal, out _));
    }

    public static IEnumerable<object[]> GatedOperations()
    {
        const string Req = "LicenseGate.Require(LogAnalyzer.Core.Services.LicenseFeatures.";
        const string Wr = "LicenseGate.RequireWritable(";
        object[] S(string file, string sig, string gate) => [file, sig, gate];
        yield return S("ViewModels/MainViewModel.cs", "private async Task StartCollectionAsync()", Req + "CaseLocal");
        yield return S("ViewModels/MainViewModel.cs", "private async Task SanitizeMediaAndGenerateCertPdfAsync()", Req + "CaseLocal");
        yield return S("ViewModels/MainViewModel.cs", "private void SealDfirCaseBundle()", Req + "CaseLocal");
        yield return S("ViewModels/MainViewModel.cs", "private void StartLiveMonitoring()", Req + "CollectLan");
        yield return S("ViewModels/MainViewModel.cs", "private void StartSyslog()", Req + "CollectLan");
        yield return S("ViewModels/MainViewModel.cs", "private void GenerateHostIsolationScript()", Req + "ResponseHost");
        yield return S("ViewModels/MainViewModel.cs", "private void GenerateKillProcessScript()", Req + "ResponseHost");
        yield return S("ViewModels/SignedMediaRegisterViewModel.cs", "private void EnrollConnected()", Req + "ControlStation");
        yield return S("ViewModels/SignedMediaRegisterViewModel.cs", "private void SignAndExport()", Req + "ControlStation");
        yield return S("ViewModels/SignedMediaRegisterViewModel.cs", "private void ImportSigned()", Req + "ControlStation");
        yield return S("ViewModels/MediaCenterViewModel.cs", "private async Task AddToRegister()", Req + "ControlStation");
        yield return S("Edition/Unclassified/DomainInvestigationViewModel.cs", "private async Task InventoryDomain()", Req + "CollectLan");
        yield return S("Edition/Unclassified/DomainInvestigationViewModel.cs", "private async Task InvestigateUser()", Req + "CollectLan");
        yield return S("Edition/Unclassified/DomainInvestigationViewModel.cs", "private async Task ImportMail()", Req + "CollectLan");
        yield return S("Edition/Unclassified/DomainInvestigationViewModel.cs", "private void ExportPdf()", Req + "CollectLan");
        yield return S("Edition/Unclassified/DomainInvestigationViewModel.cs", "private void SaveScript(bool online)", Req + "CollectLan");
        yield return S("ViewModels/StationControlViewModel.Officer.cs", "private void SignAsOfficer()", Req + "ReportPv");
        yield return S("ViewModels/StationControlViewModel.Officer.cs", "private void ExportAnchor()", Req + "ReportPv");
        yield return S("ViewModels/StationControlViewModel.Officer.cs", "private void ImportOfficerTrust()", Wr);
        yield return S("ViewModels/StationControlViewModel.Officer.cs", "private void ManageOfficers()", Wr);
        yield return S("ViewModels/StationControlViewModel.MediaHistory.cs", "private void ExportMediaHistory()", Req + "ReportPv");
        yield return S("ViewModels/CrossStationViewModel.cs", "private void ExportSignedReport()", Req + "ReportPv");
        yield return S("ViewModels/InvestigationViewModel.cs", "private void GenerateRemotePackage()", Req + "CaseLocal");
        yield return S("ViewModels/InvestigationViewModel.cs", "private void ImportRemotePackage()", Req + "CaseLocal");
        yield return S("ViewModels/InvestigationViewModel.Reports.cs", "private void SaveSigningSettings()", Wr);
        yield return S("ViewModels/ProcedureProfileViewModel.cs", "private void SaveHeader()", Wr);
        yield return S("ViewModels/ProcedureProfileViewModel.cs", "private void SaveTo(", Wr);
        yield return S("ViewModels/ProcedureProfileViewModel.cs", "private void SaveAs()", Wr);
        yield return S("ViewModels/PolicyViewModel.cs", "private void ApplyPlan()", Wr);
        yield return S("ViewModels/PolicyViewModel.cs", "private void WithPolicy(", Wr);
        foreach (var n in new[] { "Stage", "Approve", "Discard", "Rollback" }) yield return S("ViewModels/ThreatIntelPackagesViewModel.cs", $"private void {n}()", Wr);
        foreach (var n in new[] { "private void CreateAccount()", "private void ToggleRole()", "private async Task EnrollForMeAsync()", "private async Task EnrollForSelectedAsync()", "private void ImportCa()", "private void SavePolicy()", "private void ImportDataPack()" })
            yield return S("ViewModels/AuthViewModel.cs", n, Wr);
        yield return S("ViewModels/RegisterViewModel.cs", "private void Save()", Req + "Registers");
        yield return S("ViewModels/RegisterViewModel.cs", "private void Print()", Req + "ReportPv");
        yield return S("ViewModels/TransferViewModel.cs", "private void Register()", Req + "Registers");
        yield return S("ViewModels/StationControlViewModel.cs", "private async Task Run()", Req + "ControlStation");
        yield return S("ViewModels/StationControlViewModel.cs", "private string? GenerateReport()", Req + "ReportPv");
        // review round 2 (MEDIUM): case writes that were still open in read-only mode
        yield return S("ViewModels/InvestigationViewModel.Runs.cs", "private async System.Threading.Tasks.Task Reanalyze()", Req + "CaseLocal");
        yield return S("ViewModels/InvestigationViewModel.Memory.cs", "private async Task RunVolatility()", Req + "CaseLocal");
        yield return S("ViewModels/InvestigationViewModel.Reports.cs", "private async Task ImportCaseArchive()", Req + "CaseLocal");
        yield return S("ViewModels/InvestigationViewModel.RuleLab.cs", "private void AddRuleToCase()", Req + "CaseLocal");
        foreach (var n in new[] { "WorkflowAddParticipant", "WorkflowMove", "WorkflowClose", "WorkflowAddTask", "WorkflowTaskDone", "WorkflowTaskCancel", "WorkflowReview", "WorkflowAddNote" })
            yield return S("ViewModels/InvestigationViewModel.Workflow.cs", $"private void {n}()", Req + "CaseLocal");
        foreach (var n in new[] { "SaveTimelineView", "DeleteSavedView", "ToggleBookmark", "AddAnnotation" })
            yield return S("ViewModels/InvestigationViewModel.Timeline.cs", $"private void {n}()", Req + "CaseLocal");
        yield return S("Edition/Unclassified/ContainmentViewModel.cs", "private async Task TrustSelected()", Req + "ResponseHost");
        yield return S("Edition/Unclassified/ContainmentViewModel.cs", "private void EnableBlockedAudit()", Req + "ResponseHost");
    }

    [Theory]
    [MemberData(nameof(GatedOperations))]
    public void Every_write_on_a_page_the_read_only_state_opens_starts_with_its_licence_check(string file, string signature, string gate)
    {
        var src = Src(new[] { "LogAnalyzer.App" }.Concat(file.Split('/')).ToArray()).Replace("\r\n", "\n");
        var at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        // review round 2: the FIRST statement of the body must be the gate (not the second, not a comment or a string mentioning it)
        var body = src[at..];
        var open = body.IndexOf('{');
        Assert.True(open >= 0, signature);
        var first = body[(open + 1)..].Split('\n').Select(l => l.Trim()).First(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal));
        Assert.StartsWith("if (!", first);
        Assert.Contains(gate, first);
    }
}
