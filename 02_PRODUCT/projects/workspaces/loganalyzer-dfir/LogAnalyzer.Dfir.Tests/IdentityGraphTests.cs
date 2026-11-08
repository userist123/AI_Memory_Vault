using LogAnalyzer.Dfir.Identity;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.10, the account's path: session → process → resource joined only by host + logon id inside the session, and by ProcessGuid.</summary>
public sealed class IdentityGraphTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
    private const string Sid = "S-1-5-21-1-2-3-1105";
    private static int _n;

    private static TimelineEvent Sec(int minute, string id, string host, params (string K, string V)[] f)
    {
        var t = T0.AddMinutes(minute);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "t"), Source = "EventLog:Security", Provider = "Microsoft-Windows-Security-Auditing", EvidenceId = "EV-1", EventId = id, Host = host, Summary = id, Locator = $"EventRecordID={++_n}" };
        foreach (var (k, v) in f) e.Fields[k] = v;
        return e;
    }

    private static TimelineEvent Sys(int minute, string id, string host, params (string K, string V)[] f)
    {
        var t = T0.AddMinutes(minute);
        var e = new TimelineEvent { Time = Timestamp.FromUtc(t, t.ToString("o"), "t"), Source = "EventLog:Microsoft-Windows-Sysmon/Operational", EvidenceId = "EV-2", EventId = id, Host = host, Summary = id, Locator = $"EventRecordID={++_n}" };
        foreach (var (k, v) in f) e.Fields[k] = v;
        return e;
    }

    private static TimelineEvent Logon(int minute, string host, string lid) =>
        Sec(minute, "4624", host, ("TargetUserSid", Sid), ("TargetUserName", "ana"), ("TargetDomainName", "LAB"), ("TargetLogonId", lid), ("LogonType", "10"), ("IpAddress", "10.0.0.5"));
    private static TimelineEvent Logoff(int minute, string host, string lid) => Sec(minute, "4634", host, ("TargetUserSid", Sid), ("TargetLogonId", lid));

    private static IdentityGraphReport Build(List<TimelineEvent> tl) => IdentityGraph.Build(IdentityReconstruction.Build(tl), tl);

    private static IEnumerable<IdentityPathNode> All(IEnumerable<IdentityPathNode> n) => n.SelectMany(x => new[] { x }.Concat(All(x.Children)));

    [Fact]
    public void A_process_and_its_resources_hang_under_the_session_of_the_same_host_and_logon_id()
    {
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x1A2B"),
            Sec(1, "4688", "WS01", ("SubjectLogonId", "0x3e7"), ("TargetLogonId", "0x0000001a2b"), ("NewProcessName", @"C:\Windows\System32\cmd.exe"), ("NewProcessId", "0x1f4"), ("CommandLine", "cmd /c whoami")),
            Sys(2, "1", "WS01", ("LogonId", "0x1a2b"), ("Image", @"C:\Users\Public\x.exe"), ("ProcessId", "600"), ("ProcessGuid", "{g-1}")),
            Sys(3, "3", "WS01", ("ProcessGuid", "{g-1}"), ("DestinationIp", "203.0.113.9"), ("DestinationPort", "443")),
            Sys(4, "3", "WS01", ("ProcessGuid", "{g-1}"), ("DestinationIp", "203.0.113.9"), ("DestinationPort", "443")),
            Sec(5, "4663", "WS01", ("SubjectLogonId", "0x1a2b"), ("ProcessId", "0x1f4"), ("ObjectName", @"C:\secret\plan.docx"), ("ProcessName", @"C:\Windows\System32\cmd.exe")),
            Sec(6, "5145", "WS01", ("SubjectLogonId", "0x1a2b"), ("ShareName", @"\\*\C$"), ("RelativeTargetName", @"Users\x"), ("IpAddress", "10.0.0.5")),
            Logoff(30, "WS01", "0x1a2b"),
        };
        var g = Build(tl);
        var host = Assert.Single(g.Paths[Sid]);
        var session = Assert.Single(host.Children);
        Assert.Contains("0x1a2b", session.Text);
        var procs = session.Children.Where(c => c.Kind == "process").ToList();
        Assert.Equal(2, procs.Count);
        Assert.All(procs, p => Assert.Contains("același LogonId 0x1a2b", p.Reason));
        var cmd = Assert.Single(procs, p => p.Text.Contains("cmd.exe"));
        Assert.Contains("PID 500", cmd.Text);
        Assert.Contains(cmd.Children, r => r.Text.Contains("plan.docx") && r.Reason.Contains("PID 500"));     // 4663 joins the process by PID inside the session
        var x = Assert.Single(procs, p => p.Text.Contains("x.exe"));
        var conn = Assert.Single(x.Children);                                                                  // two connections to the same place: one node, counted twice
        Assert.Equal(2, conn.Count);
        Assert.Contains("ProcessGuid {g-1}", conn.Reason);
        Assert.Contains(session.Children, r => r.Kind == "resource" && r.Text.Contains(@"C$\Users\x"));
        Assert.Equal((2, 3), (g.ProcessesLinked, g.ResourcesLinked));
    }

    [Fact]
    public void The_same_logon_id_on_another_host_or_outside_the_session_is_not_linked_and_is_counted()
    {
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x1a2b"), Logoff(10, "WS01", "0x1a2b"),
            Sec(5, "4688", "WS02", ("SubjectLogonId", "0x1a2b"), ("NewProcessName", @"C:\a.exe"), ("NewProcessId", "0x10")),   // other host: no link
            Sec(20, "4688", "WS01", ("SubjectLogonId", "0x1a2b"), ("NewProcessName", @"C:\b.exe"), ("NewProcessId", "0x11")),  // after the logoff: no link
            Sec(6, "4688", "WS01", ("SubjectLogonId", "0x3e7"), ("NewProcessName", @"C:\svc.exe"), ("NewProcessId", "0x12")),   // SYSTEM: never a person's
        };
        var g = Build(tl);
        Assert.Equal(0, g.ProcessesLinked);
        Assert.DoesNotContain(All(g.Paths[Sid]), n => n.Kind == "process");
        Assert.Contains(g.Gaps, x => x.What.StartsWith("1 evenimente de proces", StringComparison.Ordinal));
        Assert.Contains(g.Gaps, x => x.What.Contains("în afara oricărei sesiuni"));
        Assert.Contains(g.Gaps, x => x.What.Contains("conturilor de sistem"));
    }

    [Fact]
    public void A_reused_logon_id_goes_to_the_session_that_holds_the_event_and_a_twice_imported_log_counts_once()
    {
        var first = Logon(0, "WS01", "0x50");
        var proc = Sec(70, "4688", "WS01", ("TargetLogonId", "0x50"), ("NewProcessName", @"C:\late.exe"), ("NewProcessId", "0x20"));
        var copy = new TimelineEvent { Time = proc.Time, Source = proc.Source, Provider = proc.Provider, EvidenceId = "EV-COPY", EventId = "4688", Host = "WS01", Summary = "4688", Locator = proc.Locator };
        foreach (var kv in proc.Fields) copy.Fields[kv.Key] = kv.Value;                                                      // the same record from a second copy of the log
        var tl = new List<TimelineEvent> { first, Logon(60, "WS01", "0x50"), proc, copy };                                   // reboot: the id comes again, no logoff between
        var g = Build(tl);
        var sessions = Assert.Single(g.Paths[Sid]).Children;
        Assert.Equal(2, sessions.Count);
        Assert.Empty(sessions.OrderBy(s => s.Utc, StringComparer.Ordinal).First().Children);
        Assert.Single(sessions.OrderBy(s => s.Utc, StringComparer.Ordinal).Last().Children);
        Assert.Equal(1, g.ProcessesLinked);
    }

    [Fact]
    public void A_sysmon_event_whose_process_is_not_in_a_session_is_a_gap_not_a_guess()
    {
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x77"),
            Sys(1, "3", "WS01", ("ProcessGuid", "{unknown}"), ("DestinationIp", "198.51.100.1"), ("DestinationPort", "80")),
        };
        var g = Build(tl);
        Assert.Equal(0, g.ResourcesLinked);
        Assert.Contains(g.Gaps, x => x.What.Contains("ProcessGuid"));
        Assert.Equal("", IdentityGraph.NormLogon("0x0"));
        Assert.Equal("0x3e7", IdentityGraph.NormLogon("999"));
    }

    [Fact]
    public void The_report_section_names_privileged_accounts_with_their_path_and_the_gaps_first()
    {
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x1a2b"),
            Sec(0, "4672", "WS01", ("SubjectUserSid", Sid), ("SubjectUserName", "ana"), ("SubjectDomainName", "LAB"), ("SubjectLogonId", "0x1a2b")),
            Sec(1, "4688", "WS01", ("TargetLogonId", "0x1a2b"), ("NewProcessName", @"C:\x.exe"), ("NewProcessId", "0x1f4")),
        };
        var rep = IdentityReconstruction.Build(tl);
        var lines = LogAnalyzer.Dfir.Windows.Investigation.InvestigationReportText.IdentityLines(rep, IdentityGraph.Build(rep, tl));
        Assert.Contains(lines, l => l.Warn && l.Text.Contains("controler"));                                  // no DC log: said before the accounts
        var row = Assert.Single(lines, l => l.Text.StartsWith(@"LAB\ana", StringComparison.Ordinal));
        Assert.Contains("1 sesiune, 1 proces, 0 resurse", row.Text);
        Assert.True(lines.ToList().FindIndex(l => l.Warn) < lines.ToList().IndexOf(row));
        Assert.Contains(LogAnalyzer.Dfir.Windows.Investigation.InvestigationReportText.IdentityLines(null, null), l => l.Text.Contains("versiune anterioară"));
    }

    // ---- independent review, round 1 (2026-11-08) ----

    [Fact]
    public void A_reused_pid_gives_the_access_to_the_process_that_held_it_then_never_to_a_later_one()
    {
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x1a2b"),
            Sec(1, "4688", "WS01", ("TargetLogonId", "0x1a2b"), ("NewProcessName", @"C:\cmd.exe"), ("NewProcessId", "0x1f4")),
            Sec(5, "4663", "WS01", ("SubjectLogonId", "0x1a2b"), ("ProcessId", "0x1f4"), ("ObjectName", @"C:\secret.docx")),
            Sec(6, "4689", "WS01", ("SubjectLogonId", "0x1a2b"), ("ProcessId", "0x1f4")),
            Sec(7, "4663", "WS01", ("SubjectLogonId", "0x1a2b"), ("ProcessId", "0x1f4"), ("ObjectName", @"C:\between.docx")),   // PID ended, not yet reused
            Sec(10, "4688", "WS01", ("TargetLogonId", "0x1a2b"), ("NewProcessName", @"C:\evil.exe"), ("NewProcessId", "0x1f4")),
            Sec(12, "4663", "WS01", ("SubjectLogonId", "0x1a2b"), ("ProcessId", "0x1f4"), ("ObjectName", @"C:\later.docx")),
        };
        var session = Assert.Single(Assert.Single(Build(tl).Paths[Sid]).Children);
        var cmd = Assert.Single(session.Children, c => c.Text.Contains("cmd.exe"));
        var evil = Assert.Single(session.Children, c => c.Text.Contains("evil.exe"));
        Assert.Equal(["C:\\secret.docx"], cmd.Children.Select(c => c.Text.Replace("Obiect accesat: ", "")));
        Assert.Equal(["C:\\later.docx"], evil.Children.Select(c => c.Text.Replace("Obiect accesat: ", "")));
        Assert.Contains(session.Children, c => c.Kind == "resource" && c.Text.Contains("between.docx"));      // no live process with that PID: under the session
    }

    [Fact]
    public void Events_without_time_logon_id_or_process_guid_are_counted_not_dropped()
    {
        var noTime = new TimelineEvent { Time = Timestamp.Unknown("x"), Source = "EventLog:Security", Provider = "Microsoft-Windows-Security-Auditing", EvidenceId = "EV-1", EventId = "4688", Host = "WS01", Summary = "4688", Locator = "EventRecordID=nt" };
        noTime.Fields["TargetLogonId"] = "0x1a2b";
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x1a2b"), noTime,
            Sec(1, "4688", "WS01", ("SubjectLogonId", "-"), ("NewProcessName", @"C:\a.exe")),
            Sys(2, "3", "WS01", ("DestinationIp", "198.51.100.1"), ("DestinationPort", "80")),
        };
        var g = Build(tl);
        Assert.Contains(g.Gaps, x => x.What.StartsWith("1 evenimente de proces sau de acces fără oră", StringComparison.Ordinal));
        Assert.Contains(g.Gaps, x => x.What.StartsWith("1 evenimente de proces sau de acces nu au un LogonId", StringComparison.Ordinal));
        Assert.Contains(g.Gaps, x => x.What.StartsWith("1 evenimente Sysmon (rețea, fișier, DNS) nu au ProcessGuid", StringComparison.Ordinal));
    }

    [Fact]
    public void A_capped_process_is_reported_as_capped_and_its_resources_too_not_as_missing()
    {
        var tl = new List<TimelineEvent> { Logon(0, "WS01", "0x1a2b") };
        for (int i = 0; i <= IdentityGraph.MaxProcessesPerSession; i++)
            tl.Add(Sys(1, "1", "WS01", ("LogonId", "0x1a2b"), ("Image", $@"C:\p{i}.exe"), ("ProcessId", $"{1000 + i}"), ("ProcessGuid", $"{{g-{i}}}")));
        tl.Add(Sys(2, "3", "WS01", ("ProcessGuid", $"{{g-{IdentityGraph.MaxProcessesPerSession}}}"), ("DestinationIp", "203.0.113.9"), ("DestinationPort", "443")));
        var g = Build(tl);
        Assert.Equal(IdentityGraph.MaxProcessesPerSession, g.ProcessesLinked);
        Assert.Contains(g.Gaps, x => x.What.StartsWith("2 legături nu au fost afișate", StringComparison.Ordinal));
        Assert.DoesNotContain(g.Gaps, x => x.What.Contains("nu este legat de nicio sesiune"));
    }

    [Fact]
    public void A_session_without_its_logon_reaches_back_only_to_the_station_boot_and_says_so()
    {
        var boot = Sec(60, "4608", "WS01");
        var tl = new List<TimelineEvent>
        {
            boot,
            Sec(10, "4688", "WS01", ("TargetLogonId", "0x99"), ("NewProcessName", @"C:\before_boot.exe"), ("NewProcessId", "0x1")),
            Sec(70, "4688", "WS01", ("TargetLogonId", "0x99"), ("NewProcessName", @"C:\after_boot.exe"), ("NewProcessId", "0x2")),
            Logoff(90, "WS01", "0x99"),
        };
        var g = Build(tl);
        var session = Assert.Single(Assert.Single(g.Paths[Sid]).Children);
        var p = Assert.Single(session.Children);
        Assert.Contains("after_boot", p.Text);
        Assert.Contains("după pornirea stației", p.Reason);
        Assert.Contains(g.Gaps, x => x.What.Contains("în afara oricărei sesiuni"));
    }

    [Fact]
    public void Logon_ids_written_with_leading_zeros_pair_and_another_spelling_of_the_host_is_a_named_gap()
    {
        var tl = new List<TimelineEvent>
        {
            Logon(0, "WS01", "0x0050"), Logoff(10, "WS01", "0x50"),
            Sec(20, "4688", "WS01", ("TargetLogonId", "0x50"), ("NewProcessName", @"C:\after_logoff.exe"), ("NewProcessId", "0x3")),
            Sys(5, "1", "WS01.LAB.LOCAL", ("LogonId", "0x50"), ("Image", @"C:\fqdn.exe"), ("ProcessId", "7"), ("ProcessGuid", "{f}")),
        };
        var g = Build(tl);
        Assert.Equal(0, g.ProcessesLinked);                                                                      // the logoff closed the session
        Assert.Contains(g.Gaps, x => x.What.Contains("în afara oricărei sesiuni"));
        Assert.Contains(g.Gaps, x => x.What.Contains("stație scrisă altfel"));
        Assert.Equal("CN={31B2F340},CN=Policies,CN=System (domeniu omis)",
            LogAnalyzer.Dfir.Windows.Investigation.InvestigationReportText.WithoutDomain("CN={31B2F340},CN=Policies,CN=System,DC=lab,DC=local"));
    }
}