using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review finding: an unread source is never "nothing there". N11 is never CONFORM when one of its sources could not be read (NECONFORM still wins
/// when a path was found), and the account checks C01–C06 are never CONFORM on a partial account list.
/// </summary>
public class ControlMissingSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static StationFacts Facts(StationPosture posture = StationPosture.IsolatedNetwork, bool readable = true)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = true, Posture = posture };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Start.AddDays(-60), 100, true, ""));
        f.EgressPaths = [];
        if (readable) N11SourcesRead(f);
        return f;
    }

    /// <summary>The values N11 reads from the settings when every source was read and nothing was found.</summary>
    internal static void N11SourcesRead(StationFacts f)
    {
        f.Settings["Wlan.ProfileCount"] = "0";
        f.Settings["ICS.Shared"] = "nu";
        f.Settings["Net.AdapterCount"] = "1";
        f.Settings["Net.CellularAdapters"] = "";
        f.Settings["Net.PhoneAdapters"] = "";
    }

    private static ControlCheck Check(StationFacts f, string id) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == id);

    // ---- N11 --------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Standalone)]
    public void N11_is_conform_only_when_every_source_was_read(StationPosture posture) =>
        Assert.Equal(ControlStatus.Conform, Check(Facts(posture), "N11").Status);

    [Theory]
    [InlineData("Wlan.ProfileCount", "profilurile Wi-Fi salvate")]
    [InlineData("ICS.Shared", "partajarea conexiunii")]
    [InlineData("Net.AdapterCount", "lista adaptoarelor de rețea")]
    public void N11_is_undetermined_when_a_settings_source_was_not_read(string key, string named)
    {
        var f = Facts();
        f.Settings.Remove(key);
        var c = Check(f, "N11");
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains(named, c.Detail);
    }

    [Fact]
    public void N11_wifi_profiles_unread_names_the_reason()
    {
        var f = Facts();
        f.Settings.Remove("Wlan.ProfileCount");
        Assert.Contains("profilurile Wi-Fi salvate nu au putut fi citite", Check(f, "N11").Detail);
    }

    [Fact]
    public void N11_is_undetermined_when_a_collector_source_failed()
    {
        var f = Facts();
        f.EgressUnread.Add("setările proxy (Access is denied)");
        var c = Check(f, "N11");
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("setările proxy (Access is denied)", c.Detail);
    }

    [Fact]
    public void N11_non_conform_still_wins_over_an_unread_source()
    {
        var f = Facts();
        f.Settings.Remove("Wlan.ProfileCount");
        f.EgressPaths = [new EgressPathFact("Server DNS", "8.8.8.8", false, true)];
        var c = Check(f, "N11");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("profilurile Wi-Fi salvate", c.Detail);   // what was not read is still said
    }

    [Fact]
    public void N11_ics_unread_but_forbidden_by_policy_is_a_review_like_N10()
    {
        var f = Facts();
        f.Settings["ICS.Shared"] = "";
        f.Settings["ICS.PolicyShowUI"] = "0";
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "N11").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "N10").Status);
    }

    [Fact]
    public void Collector_records_a_failed_source_instead_of_swallowing_it()
    {
        var unread = new List<string>();
        EgressPathCollector.Guard(unread, "setările proxy", () => throw new UnauthorizedAccessException("Access is denied"));
        EgressPathCollector.Guard(unread, "rute permanente", () => { });
        Assert.Equal(["setările proxy (Access is denied)"], unread);
    }

    // ---- C01–C06 on a partial account list ----------------------------------------------------------------------------------------

    private static LocalAccount Acc(string name, string rid, bool disabled = false, bool pwdRequired = true, bool expires = true, bool admin = false, bool rdp = false) =>
        new(name, "S-1-5-21-1-2-3-" + rid, disabled, pwdRequired, expires, false, null, admin, rdp, "");

    private static StationFacts PartialAccounts()
    {
        var f = Facts();
        f.Accounts.Add(Acc("ops", "1001", admin: true));
        f.Gaps.Add(new EvidenceGap("Conturi locale", EvidenceStatus.Failed, "WMI a întrerupt enumerarea", "Verificările care depind de această sursă sunt NEDETERMINATE", "Rulare ca administrator", "Posibil"));
        return f;
    }

    [Fact]
    public void Account_checks_are_never_conform_on_a_partial_list()
    {
        var r = ControlEvaluator.Evaluate(PartialAccounts());
        var c = r.Checks.Where(c => c.Id is "C01" or "C02" or "C03" or "C04" or "C05" or "C06").ToList();
        Assert.NotEmpty(c);
        Assert.DoesNotContain(c, x => x.Status == ControlStatus.Conform);
        foreach (var id in new[] { "C01", "C03", "C04", "C05", "C06" })
        {
            var x = Assert.Single(c, x => x.Id == id);
            Assert.Equal(ControlStatus.Nedeterminat, x.Status);
            Assert.Contains("WMI a întrerupt enumerarea", x.Detail);
        }
        Assert.Contains("incompletă", Assert.Single(c, x => x.Id == "C02").Detail);
    }

    [Fact]
    public void A_fact_found_on_a_partial_list_is_still_reported()
    {
        var f = PartialAccounts();
        f.Accounts.Add(Acc("Guest", "501"));                    // active guest
        Assert.Equal(ControlStatus.Neconform, Check(f, "C01").Status);
    }

    [Fact]
    public void A_complete_account_list_still_gives_conform()
    {
        var f = Facts();
        f.Accounts.Add(Acc("ops", "1001", admin: true));
        f.Accounts.Add(Acc("Guest", "501", disabled: true));
        Assert.Equal(ControlStatus.Conform, Check(f, "C01").Status);
        Assert.Equal(ControlStatus.Conform, Check(f, "C04").Status);
    }
}
