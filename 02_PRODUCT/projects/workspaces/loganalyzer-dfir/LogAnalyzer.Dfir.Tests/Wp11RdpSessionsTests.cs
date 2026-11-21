using System.Diagnostics;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>RDP-SESSIONS (RESEARCH_GAPS C.5): session reconstruction from LocalSessionManager 21/22/23/24/25 with hostile probes.</summary>
// not parallelised with other classes: the 10,000-event timing probe must not be inflated by other tests' CPU load
[CollectionDefinition("RdpSessionsTiming", DisableParallelization = true)]
public class RdpSessionsTimingCollection;

[Collection("RdpSessionsTiming")]
public class Wp11RdpSessionsTests
{
    private const string TsLsm = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";
    private const string TsRcm = "Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational";
    private static int _rec = 500_000;

    private static TimelineEvent Lsm(int id, double minute, string sid = "3", string addr = "10.0.0.9", string user = @"CORP\bob", string host = "", bool noTime = false, bool noSid = false)
    {
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["User"] = user, ["Address"] = addr };
        if (!noSid) f["SessionID"] = sid;
        return new TimelineEvent
        {
            Time = noTime ? Timestamp.Unknown() : Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = "EventLog:" + TsLsm, EventId = id.ToString(),
            Provider = "Microsoft-Windows-TerminalServices-LocalSessionManager", Host = host, EvidenceId = "EV-1",
            Locator = $"EventRecordID={Interlocked.Increment(ref _rec)}", Summary = "t", Fields = f,
        };
    }

    private static Finding Sessions(IEnumerable<TimelineEvent> ev) => One(Run(ev), "RDP-SESSIONS");

    [Fact]
    public void Basic_session_has_user_address_interval_and_contract_fields()
    {
        var f = Sessions([Lsm(21, 0), Lsm(22, 0.1), Lsm(24, 5), Lsm(25, 10), Lsm(23, 12)]);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal(SemanticType.Observation, f.SemanticType);
        Assert.Equal("T1021.001", f.MitreTechniqueId);
        Assert.Equal("Lateral Movement", f.Category);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Contains("CORP\\bob", f.Description);
        Assert.Contains("10.0.0.9", f.Description);
        Assert.Contains("7 minute", f.Description);   // 5 + 2 connected, the 24-25 gap is not counted
        Assert.Contains("reconectări: 1", f.Description);
        Assert.Contains("încheiată", f.Description);
        Assert.Contains("(1 sesiune)", f.Title);
    }

    [Fact]
    public void Reused_session_id_after_logoff_or_time_jump_starts_a_new_session()
    {
        var afterLogoff = Sessions([Lsm(21, 0), Lsm(23, 10), Lsm(21, 20), Lsm(24, 30)]);
        Assert.Contains("(2 sesiuni)", afterLogoff.Title);
        // no reboot trace at all (no 23): a second 21 on the same ID is still a new session, and the first one has no recorded end
        var noLogoff = Sessions([Lsm(21, 10), Lsm(24, 20), Lsm(21, 100), Lsm(24, 110)]);
        Assert.Contains("(2 sesiuni)", noLogoff.Title);
        Assert.Contains("ID-ul a fost refolosit de o sesiune nouă", noLogoff.Description);
        Assert.Equal(2, noLogoff.Description.Split("10 minute").Length - 1);
    }

    [Fact]
    public void Disconnect_without_logon_is_before_the_log_and_gets_no_invented_duration()
    {
        var f = Sessions([Lsm(24, 5), Lsm(25, 8), Lsm(23, 9)]);
        Assert.Contains("începută înainte de jurnal", f.Description);
        Assert.Contains("un minut", f.Description);   // only the complete 25 -> 23 interval is counted
        var only24 = Sessions([Lsm(24, 5)]);
        Assert.Contains("începută înainte de jurnal", only24.Description);
        Assert.Contains("durată necunoscută", only24.Description);
    }

    [Fact]
    public void Logoff_with_nothing_before_and_no_address_creates_no_session()
    {
        Assert.DoesNotContain(Run([Lsm(23, 5, addr: "")]), x => x.RuleId == "RDP-SESSIONS");
        var f = Sessions([Lsm(23, 5)]);   // with a network address: a closed session from before the log, no duration
        Assert.Contains("începută înainte de jurnal", f.Description);
        Assert.Contains("durată necunoscută", f.Description);
    }

    [Fact]
    public void A_disconnect_before_the_logon_is_a_pre_log_session_plus_a_new_one_whatever_the_input_order()
    {
        foreach (var f in new[] { Sessions([Lsm(21, 10), Lsm(24, 5)]), Sessions([Lsm(24, 5), Lsm(21, 10)]) })
        {
            Assert.Contains("(2 sesiuni)", f.Title);
            Assert.Contains("începută înainte de jurnal", f.Description);
            Assert.Contains("ID-ul a fost refolosit de o sesiune nouă", f.Description);
            Assert.Contains("fără sfârșit în jurnal", f.Description);
            Assert.DoesNotContain("ore inversate", f.Description);
        }
    }

    [Fact]
    public void Reconnect_from_another_address_lists_both_and_says_so()
    {
        var f = Sessions([Lsm(21, 0, addr: "10.0.0.9"), Lsm(24, 5, addr: "10.0.0.9"), Lsm(25, 6, addr: "10.0.0.77")]);
        Assert.Contains("10.0.0.9", f.Description);
        Assert.Contains("10.0.0.77", f.Description);
        Assert.Contains("reconectare din altă adresă", f.Description);
        Assert.Contains("fără sfârșit în jurnal", f.Description);
    }

    [Fact]
    public void LOCAL_console_empty_and_invalid_addresses_are_excluded()
    {
        Assert.DoesNotContain(Run([Lsm(21, 0, addr: "LOCAL"), Lsm(24, 5, addr: "LOCAL"), Lsm(21, 6, "4", addr: ""), Lsm(21, 7, "5", addr: "not-an-ip")]), x => x.RuleId == "RDP-SESSIONS");
        var f = Sessions([Lsm(21, 0, "1", "LOCAL"), Lsm(21, 1, "2", "10.0.0.9")]);
        Assert.Contains("(1 sesiune)", f.Title);
        Assert.Contains("excluse", f.Description);
    }

    [Fact]
    public void Events_without_session_id_or_time_are_counted_not_invented()
    {
        var f = Sessions([Lsm(21, 0), Lsm(24, 5, noSid: true), Lsm(24, 6, noTime: true)]);
        Assert.Contains("fără număr de sesiune", f.Description);
        Assert.Contains("fără oră", f.Description);
        Assert.Contains("durată necunoscută", f.Description);
    }

    [Fact]
    public void Ten_thousand_events_are_fast_and_caps_hold()
    {
        var ev = new List<TimelineEvent>();
        for (int i = 0; i < 5000; i++) { ev.Add(Lsm(21, i * 0.01, i.ToString())); ev.Add(Lsm(24, i * 0.01 + 0.005, i.ToString())); }
        // best of three: the full suite runs test classes in parallel, so a single wall-clock sample can be inflated by other tests' CPU load
        long best = long.MaxValue;
        Finding f = null!;
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            f = One(Run(ev), "RDP-SESSIONS");
            sw.Stop();
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }
        Assert.True(best < 2000, $"best of 3 took {best} ms");
        Assert.Contains(RoText.Count(5000, "sesiune", "sesiuni"), f.Title);          // the real total, not attempts
        Assert.Contains("S-au păstrat cele mai recente 2000 din 5000 sesiuni", f.Description);
        Assert.Contains("cele mai recente 15 din 2000", f.Description);
        Assert.DoesNotContain("fără sesiune cunoscută", f.Description);              // no orphans: a 24 over the cap is not an orphan
        Assert.Contains(f.SupportingEvidence, r => r.Description.Contains("sesiunea 4999"));    // the most recent are kept
        Assert.DoesNotContain(f.SupportingEvidence, r => r.Description.EndsWith("sesiunea 0"));
        Assert.Equal(15, f.Description.Split("; ").Length);
        Assert.True(f.SupportingEvidence.Count <= 30);
    }

    [Fact]
    public void Hostile_user_names_are_cleaned_everywhere()
    {
        var bad = "CORP\\bo‮b\u0001\r\nx\u0007";
        var f = Sessions([Lsm(21, 0, user: bad), Lsm(24, 1, user: bad)]);
        foreach (var s in new[] { f.Title, f.Description, f.PlainWhat, f.User, f.Ip })
        {
            Assert.DoesNotContain('‮', s);
            Assert.DoesNotContain('\u0001', s);
            Assert.DoesNotContain('\u0007', s);
            Assert.DoesNotContain('\n', s);
            Assert.DoesNotContain('\r', s);
        }
    }

    [Fact]
    public void Network_authentication_without_a_session_creates_nothing_and_is_counted()
    {
        TimelineEvent Rcm(double m, string user = "bob", string ip = "10.0.0.9") => Ev(TsRcm, 1149, m, "x", ("Param1", user), ("Param2", "CORP"), ("Param3", ip));
        Assert.DoesNotContain(Run([Rcm(0)]), x => x.RuleId == "RDP-SESSIONS");
        var f = Sessions([Lsm(21, 10), Rcm(0), Rcm(9.5)]);
        Assert.Contains("1 autentificare de rețea (1149) fără sesiune", f.Description);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Contains("autentificare de rețea reușită confirmată", f.Description);
        // wrong user, wrong address and a 1149 later than the session do not corroborate
        var none = Sessions([Lsm(21, 10), Rcm(9.5, "eve"), Rcm(9.5, "bob", "10.0.0.10"), Rcm(10.5, "BOB")]);
        Assert.Equal(Classification.Direct, none.Classification);
    }

    [Fact]
    public void Exact_duplicate_events_do_not_double_intervals()
    {
        var f = Sessions([Lsm(21, 0), Lsm(21, 0), Lsm(24, 5), Lsm(24, 5)]);
        Assert.Contains("(1 sesiune)", f.Title);
        Assert.Contains("5 minute", f.Description);
        Assert.Contains("2 duplicate exacte", f.Description);
    }

    [Fact]
    public void A_session_replaced_by_a_reused_id_takes_no_Security_event_after_the_reuse()
    {
        // review r2: the replaced session had an open upper bound and took a 4779 written long after the new session reused its ID
        var f = Sessions([Lsm(21, 0), Lsm(21, 10), Lsm(24, 2000, addr: "10.0.0.9"),
            Sec(4779, 1995, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9"), ("SessionName", "RDP-Tcp#1"))]);
        Assert.Contains("ID-ul a fost refolosit", f.Description);
        // the 4779 belongs to the new session (open until 2000), never to the replaced one (ended at the reuse, minute 10)
        Assert.Contains("1 deconectare (4779)", f.Description);
        // the new session comes from another address, so the replaced one is the only candidate for a late 4779 from the old address: it must not take it
        var g = Sessions([Lsm(21, 0, addr: "10.0.0.9"), Lsm(21, 10, addr: "10.0.0.8"), Lsm(24, 2000, addr: "10.0.0.8"),
            Sec(4779, 1995, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9"), ("SessionName", "RDP-Tcp#1"))]);
        Assert.DoesNotContain("deconectare (4779)", g.Description);
        Assert.Contains("1 eveniment Security 4778/4779 fără sesiune corespunzătoare", g.Description);
    }

    [Fact]
    public void The_last_seen_time_is_the_last_event_of_the_sessions_not_the_last_evidence_reference()
    {
        // review r2: only the first 3 events of a session were kept as references, so LastSeenUtc stopped at the third one
        var f = Sessions([Lsm(21, 0), Lsm(22, 0.1), Lsm(24, 1), Lsm(25, 100), Lsm(24, 200), Lsm(25, 300), Lsm(23, 500)]);
        Assert.NotNull(f.FirstSeenUtc);
        Assert.Equal(TimeSpan.FromMinutes(500), f.LastSeenUtc!.Value - f.FirstSeenUtc!.Value);
    }

    [Fact]
    public void Security_4624_and_4778_4779_corroborate_and_are_counted()
    {
        var f = Sessions([Lsm(21, 0), Sec(4624, -0.5, ("TargetUserName", "bob"), ("LogonType", "10"), ("IpAddress", "10.0.0.9")),
            Lsm(24, 5), Lsm(25, 6), Sec(4624, 6.1, ("TargetUserName", "bob"), ("LogonType", "7"), ("IpAddress", "10.0.0.9")),
            Sec(4779, 5, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9"), ("SessionName", "RDP-Tcp#1")),
            Sec(4778, 6, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9"), ("SessionName", "RDP-Tcp#1"))]);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Contains("2 × autentificare Security 4624", f.Description);
        Assert.Contains("1 reconectare (4778)", f.Description);
        Assert.Contains("1 deconectare (4779)", f.Description);
        // type 7 does not corroborate the first logon
        var t7 = Sessions([Lsm(21, 0), Sec(4624, 0, ("TargetUserName", "bob"), ("LogonType", "7"), ("IpAddress", "10.0.0.9"))]);
        Assert.Equal(Classification.Direct, t7.Classification);
    }

    [Fact]
    public void Same_session_id_on_different_stations_is_not_mixed()
    {
        var f = Sessions([Lsm(21, 0, host: "SRV1"), Lsm(21, 1, host: "SRV2"), Lsm(24, 5, host: "SRV1"), Lsm(24, 9, host: "SRV2")]);
        Assert.Contains("(2 sesiuni)", f.Title);
        Assert.Contains("5 minute", f.Description);
        Assert.Contains("8 minute", f.Description);
    }

    [Fact]
    public void Each_4778_4779_belongs_to_exactly_one_session_the_nearest()
    {
        // two sessions of the same account and address: the 4778 at minute 61 is nearer the second one and is counted once
        var f = Sessions([Lsm(21, 0, "1"), Lsm(23, 10, "1"), Lsm(21, 60, "2"), Lsm(24, 70, "2"),
            Sec(4778, 61, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9")), Sec(4779, 11.5, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9"))]);
        Assert.Contains("Security a confirmat 1 reconectare (4778) și 1 deconectare (4779)", f.Description);
        Assert.Equal(2, f.Description.Split("Security: ").Length - 1);
        Assert.DoesNotContain("fără sesiune corespunzătoare", f.Description);
        var far = Sessions([Lsm(21, 0), Lsm(24, 5), Sec(4778, 30, ("AccountName", "bob"), ("ClientAddress", "10.0.0.9"))]);
        Assert.Contains("1 eveniment Security 4778/4779 fără sesiune corespunzătoare", far.Description);
    }

    [Fact]
    public void One_4624_is_not_counted_for_two_sessions()
    {
        var f = Sessions([Lsm(21, 0, "1"), Lsm(21, 1, "2"), Sec(4624, 0.5, ("TargetUserName", "bob"), ("LogonType", "10"), ("IpAddress", "10.0.0.9"))]);
        Assert.Equal(1, f.Description.Split("1 × autentificare Security 4624").Length - 1);
        Assert.DoesNotContain("2 × autentificare", f.Description);
    }

    [Fact]
    public void Wording_for_replaced_open_and_unrecorded_endings()
    {
        var replaced = Sessions([Lsm(21, 0), Lsm(24, 5), Lsm(21, 50), Lsm(24, 55)]);
        Assert.Contains("sfârșit neînregistrat (ID-ul a fost refolosit de o sesiune nouă", replaced.Description);
        Assert.DoesNotContain("păstrată, neînchisă", replaced.Description);
        var open = Sessions([Lsm(21, 0)]);
        Assert.Contains("fără sfârșit în jurnal", open.Description);
        Assert.DoesNotContain("încă deschisă", open.Description);
        // 25 with no 24 before it: the earlier interval has no recorded end, the pause is not added to the duration
        var gap = Sessions([Lsm(21, 0), Lsm(25, 30), Lsm(24, 35)]);
        Assert.Contains("durată totală din intervalele cu început și sfârșit înregistrate: 5 minute", gap.Description);
        Assert.Contains("fără deconectare înregistrată înainte", gap.Description);
    }

    [Fact]
    public void An_event_without_a_station_never_corroborates_a_named_station_and_names_keep_their_case()
    {
        TimelineEvent Rcm(double m) => Ev(TsRcm, 1149, m, "x", ("Param1", "bob"), ("Param2", "CORP"), ("Param3", "10.0.0.9"));   // Host empty
        var named = Sessions([Lsm(21, 10, host: "Srv-One"), Rcm(9.5)]);
        Assert.Equal(Classification.Direct, named.Classification);
        Assert.Contains("stația Srv-One", named.Description);
        Assert.Contains("1 autentificare de rețea (1149) fără sesiune", named.Description);
        var unnamed = Sessions([Lsm(21, 10), Rcm(9.5)]);
        Assert.Equal(Classification.Correlated, unnamed.Classification);
        // unknown station and a named one with the same ID are different sessions
        var two = Sessions([Lsm(21, 0, "3"), Lsm(21, 1, "3", host: "SRV1"), Lsm(24, 5, "3"), Lsm(24, 9, "3", host: "srv1")]);
        Assert.Contains("(2 sesiuni)", two.Title);
        Assert.Contains("stație necunoscută", two.Description);
        Assert.Contains("5 minute", two.Description);
        Assert.Contains("8 minute", two.Description);
    }
}
