using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public class ControlAuditTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ReadableChannels = StationFactCollector.Queries.Keys.ToArray();

    private static LogFact L(string ch, int id, int day, int hour, params (string K, string V)[] data) =>
        new(ch, id, Start.AddDays(day).AddHours(hour), 1000 + id + day, "S-1-5-21-1-2-3-1001", data.ToDictionary(d => d.K, d => d.V));

    private static StationFacts IsolatedStation(bool securityReadable = true)
    {
        var f = new StationFacts
        {
            Host = "ST-IZOLAT-07", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30),
            IsAdministrator = securityReadable, StationShouldBeIsolated = true,
            PasswordPolicy = new PasswordPolicy(6, null, TimeSpan.Zero, 0, 0, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)),
            AuditPolicy = AuditPolicy.Subcategories.Keys.ToDictionary(k => k, k => k == "Process Creation" ? AuditSetting.None : AuditSetting.Success | AuditSetting.Failure),
        };
        f.Accounts.Add(new LocalAccount("Administrator", "S-1-5-21-1-2-3-500", false, true, true, false, null, true, false, ""));
        f.Accounts.Add(new LocalAccount("Guest", "S-1-5-21-1-2-3-501", true, false, false, false, null, false, false, ""));
        f.Accounts.Add(new LocalAccount("ion", "S-1-5-21-1-2-3-1001", false, true, true, false, Start.AddDays(5), true, false, ""));
        f.Settings["UAC.EnableLUA"] = "1";
        f.Settings["Winlogon.AutoAdminLogon"] = "1";
        f.Settings["Winlogon.DefaultUserName"] = "ion";
        f.Settings["Winlogon.DefaultPasswordPresent"] = "da";
        f.Settings["RDP.fDenyTSConnections"] = "0";
        f.Settings["SecurityLog.MaxSizeMB"] = "20";
        f.NetworkProfiles.Add(new NetworkProfileFact("Hotspot-Telefon", "Wi-Fi", new DateTime(2026, 9, 12, 14, 0, 0), new DateTime(2026, 9, 12, 14, 5, 0), ""));
        f.UsbDevices.Add(new UsbDeviceFact("Disk&Ven_Kingston&Prod_DataTraveler", "AA00112233&0", "Kingston DataTraveler USB Device", "USBSTOR"));
        foreach (var ch in ReadableChannels)
            f.Coverage.Add(new ChannelCoverage(ch, Start.AddDays(-60), 100, ch != "Security" || securityReadable, ""));
        if (!securityReadable) return f;

        f.Logs.Add(L("Security", 1102, 10, 23, ("SubjectUserName", "ion"), ("SubjectDomainName", "ST-IZOLAT-07")));
        f.Logs.Add(L("Security", 4732, 3, 9, ("SubjectUserName", "Administrator"), ("SubjectDomainName", "ST-IZOLAT-07"),
            ("MemberName", "-"), ("MemberSid", "S-1-5-21-1-2-3-1001"), ("TargetUserName", "Administrators"), ("TargetSid", "S-1-5-32-544")));
        f.Logs.Add(L("Security", 4624, 4, 8, ("TargetUserName", "ion"), ("TargetDomainName", "ST-IZOLAT-07"), ("LogonType", "2")));
        f.Logs.Add(L("Security", 4624, 12, 15, ("TargetUserName", "ion"), ("TargetDomainName", "ST-IZOLAT-07"), ("LogonType", "10"), ("IpAddress", "192.168.43.17")));
        for (int i = 0; i < 6; i++)
            f.Logs.Add(L("Security", 4625, 7, 2, ("TargetUserName", "Administrator"), ("TargetDomainName", "ST-IZOLAT-07"), ("IpAddress", "-")));
        f.Logs.Add(L("Security", 4616, 15, 10, ("SubjectUserName", "ion"), ("ProcessName", @"C:\Windows\System32\SystemSettingsAdminFlows.exe"),
            ("PreviousTime", "2026-09-16T10:00:00Z"), ("NewTime", "2026-09-14T10:00:00Z")));
        f.Logs.Add(L("Microsoft-Windows-Partition/Diagnostic", 1006, 12, 15, ("Capacity", "32000000000"), ("BusType", "7"),
            ("Manufacturer", "Kingston"), ("Model", "DataTraveler"), ("SerialNumber", "AA00112233")));
        f.Logs.Add(L("Microsoft-Windows-Partition/Diagnostic", 1006, 12, 15, ("Capacity", "1000000000000"), ("BusType", "17"), ("Model", "internal NVMe")));
        f.Logs.Add(L("System", 7045, 20, 11, ("ServiceName", "UpdSvc"), ("ImagePath", @"C:\ProgramData\upd\svc.exe"), ("ServiceType", "user mode service"), ("AccountName", "LocalSystem")));
        return f;
    }

    private static ControlCheck Check(ControlReport r, string id) => Assert.Single(r.Checks, c => c.Id == id);

    [Fact]
    public void Isolated_station_violations_are_reported_with_who_and_when()
    {
        var r = ControlEvaluator.Evaluate(IsolatedStation());

        Assert.Equal(ControlStatus.Neconform, Check(r, "N01").Status);              // Wi-Fi hotspot on an isolated station
        Assert.Contains(Check(r, "N01").Evidence, e => e.Contains("Hotspot-Telefon"));
        Assert.Equal(ControlStatus.Neconform, Check(r, "A05").Status);              // Security log cleared
        Assert.Contains(Check(r, "A05").Evidence, e => e.Contains("ion"));
        Assert.Equal(ControlStatus.DeVerificat, Check(r, "U01").Status);            // admin group change
        Assert.Equal(ControlStatus.Neconform, Check(r, "U03").Status);              // RDP on isolated station
        Assert.Equal(ControlStatus.DeVerificat, Check(r, "U04").Status);            // 6 failures in one hour
        Assert.Equal(ControlStatus.Neconform, Check(r, "P05").Status);              // autologon with stored password
        Assert.Equal(ControlStatus.Neconform, Check(r, "P06").Status);              // RDP enabled
        Assert.Equal(ControlStatus.Neconform, Check(r, "P01").Status);              // min length 6
        Assert.Equal(ControlStatus.Neconform, Check(r, "P02").Status);              // no lockout
        Assert.Equal(ControlStatus.Neconform, Check(r, "A01").Status);              // process creation not audited
        Assert.Equal(ControlStatus.Neconform, Check(r, "A02").Status);              // 20 MB log
        Assert.Equal(ControlStatus.DeVerificat, Check(r, "A07").Status);            // manual time change
        Assert.Equal(ControlStatus.Conform, Check(r, "C01").Status);                // guest disabled
        Assert.Equal(ControlStatus.DeVerificat, Check(r, "C03").Status);            // builtin admin enabled

        var d01 = Check(r, "D01");
        Assert.Contains("1 conectări", d01.Detail);                                // internal NVMe (bus 17) excluded
        Assert.Contains(r.Actions, a => a.Action == "a șters un jurnal" && a.Who.EndsWith("ion"));
        Assert.Contains(r.Actions, a => a.Action == "a adăugat în grupul local" && a.Detail.Contains("Administrators"));
        Assert.Contains(r.Actions, a => a.Action == "serviciu nou instalat" && a.Detail.Contains("UpdSvc"));
        Assert.True(r.Actions.SequenceEqual(r.Actions.OrderBy(a => a.TimeUtc)));

        var ion = Assert.Single(r.Users, u => u.User.EndsWith("ion"));
        Assert.Equal(1, ion.InteractiveLogons);
        Assert.Equal(1, ion.RemoteLogons);
        Assert.Contains("192.168.43.17", ion.RemoteSources);
    }

    [Fact]
    public void Unreadable_security_log_gives_undetermined_not_clean()
    {
        var r = ControlEvaluator.Evaluate(IsolatedStation(securityReadable: false));
        foreach (var id in new[] { "A05", "U01", "U02", "U03", "U04" })
        {
            var c = Check(r, id);
            Assert.Equal(ControlStatus.Nedeterminat, c.Status);
            Assert.DoesNotContain("Nicio", c.Detail);
        }
    }

    [Fact]
    public void Network_list_dates_are_decoded_from_systemtime()
    {
        // 2026-09-12 (Saturday=6) 14:05:30.000
        byte[] st = [0xEA, 0x07, 9, 0, 6, 0, 12, 0, 14, 0, 5, 0, 30, 0, 0, 0];
        Assert.Equal(new DateTime(2026, 9, 12, 14, 5, 30), StationFactCollector.SystemTime(st));
        Assert.Null(StationFactCollector.SystemTime(new byte[4]));
    }

    [Fact]
    public void Report_is_saved_in_the_case_as_evidence_with_pdf()
    {
        var root = Path.Combine(Path.GetTempPath(), "la-control-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ws = CaseWorkspace.Create(root, new CaseInfo { CaseId = "CASE-CTRL", Name = "control", CreatedAtUtc = DateTimeOffset.UtcNow , Scope = TestScopes.Valid() });
            var (json, pdf) = ControlReportPdf.SaveToCase(ControlEvaluator.Evaluate(IsolatedStation()), ws, "inspector test", "control anual");
            Assert.True(File.Exists(json));
            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5));
            Assert.Equal(2, ws.LoadEvidence().Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Live_collection_on_this_station_completes_and_reports_its_own_gaps()
    {
        var facts = StationFactCollector.Collect(DateTimeOffset.UtcNow.AddDays(-2), stationShouldBeIsolated: false, maxPerId: 200);
        Assert.NotEmpty(facts.Accounts);
        Assert.NotEmpty(facts.Coverage);
        if (!facts.IsAdministrator) Assert.Contains(facts.Gaps, g => g.Artifact.Contains("Security") || g.Artifact.Contains("audit"));
        var r = ControlEvaluator.Evaluate(facts);
        Assert.NotEmpty(r.Checks);
    }
}
