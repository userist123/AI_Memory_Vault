using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Station checks P40-P47 (DISA STIG for Windows 11). StationFacts are built in memory; a value that could not be read is "" and never becomes Conform,
/// a value that does not exist is Absent and the check names the Windows default it assumed.
/// </summary>
public class StationChecksStigTests
{
    private const string Absent = StationFactCollector.Absent;
    private const string Admins = "S-1-5-32-544";

    private static StationFacts Station(bool isolated, params (string K, string V)[] settings)
    {
        var f = new StationFacts { Posture = isolated ? LogAnalyzer.Core.Services.Edition.StationPosture.Standalone : LogAnalyzer.Core.Services.Edition.StationPosture.Internet, IsAdministrator = true };
        foreach (var (k, v) in settings) f.Settings[k] = v;
        return f;
    }

    private static ControlCheck Check(StationFacts f, string id) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);

    private static readonly string[] NewIds = ["P40", "P41", "P42", "P43", "P44", "P45", "P46", "P47"];

    [Fact]
    public void Unreadable_settings_give_Nedeterminat_never_Conform()
    {
        var report = ControlEvaluator.Evaluate(Station(true));
        foreach (var id in NewIds)
        {
            var c = report.Checks.Single(x => x.Id == id);
            Assert.True(c.Status == ControlStatus.Nedeterminat, $"{id} gave {c.Status} with nothing readable");
            Assert.Contains("administrator", c.Detail);
        }
    }

    [Fact]
    public void Every_new_id_has_a_guide_with_all_sections_and_cites_its_stig_rule()
    {
        foreach (var id in NewIds)
        {
            var g = CheckGuides.For(id);
            Assert.DoesNotContain("nu are încă o descriere", g.What);
            Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 20, id));
        }
        Assert.Contains("WN11-SO-000145", CheckGuides.For("P40").How);
        Assert.Contains("WN11-SO-000195", CheckGuides.For("P41").How);
        Assert.Contains("WN11-UR-000015", CheckGuides.For("P42").How);
        Assert.Contains("WN11-CC-000315", CheckGuides.For("P43").How);
        Assert.Contains("WN11-00-000145", CheckGuides.For("P44").How);
        Assert.Contains("WN11-CC-000330", CheckGuides.For("P45").How);
        Assert.Contains("fără regulă STIG", CheckGuides.For("P46").How);
        Assert.Contains("WN11-00-000100", CheckGuides.For("P47").How);
    }

    [Fact]
    public void Every_new_id_applies_to_all_postures()
    {
        foreach (var id in NewIds)
            foreach (var posture in Enum.GetValues<LogAnalyzer.Core.Services.Edition.StationPosture>())
                Assert.True(ControlApplicability.Applies(id, posture), $"{id} {posture}");
    }

    // ---- P40 ----------------------------------------------------------------------------------------------------------------

    private static StationFacts P40(string sam, string ra, string everyone, string nullSess) =>
        Station(true, ("Stig.RestrictAnonymousSAM", sam), ("Stig.RestrictAnonymous", ra), ("Stig.EveryoneIncludesAnonymous", everyone), ("Stig.RestrictNullSessAccess", nullSess));

    [Fact]
    public void P40_all_values_as_required_is_Conform()
    {
        var c = Check(P40("1", "1", "0", "1"), "P40");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains(@"Control\Lsa → RestrictAnonymousSAM = 1"));
    }

    [Theory]
    [InlineData("0", "1", "0", "1", "RestrictAnonymousSAM = 0")]
    [InlineData("1", "0", "0", "1", "RestrictAnonymous = 0")]
    [InlineData("1", "1", "1", "1", "EveryoneIncludesAnonymous = 1")]
    [InlineData("1", "1", "0", "0", "RestrictNullSessAccess = 0")]
    public void P40_any_violating_value_is_Neconform(string sam, string ra, string everyone, string nullSess, string expected)
    {
        var c = Check(P40(sam, ra, everyone, nullSess), "P40");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(expected, c.Detail);
    }

    [Fact]
    public void P40_absent_values_use_the_windows_defaults_and_RestrictAnonymous_default_needs_verification()
    {
        var c = Check(P40(Absent, Absent, Absent, Absent), "P40");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("implicit Windows", c.Detail);
        Assert.Contains("presupuse", c.Detail);
        Assert.Equal(ControlStatus.Conform, Check(P40(Absent, "1", Absent, Absent), "P40").Status);
    }

    [Fact]
    public void P40_a_violation_wins_over_an_unreadable_value_and_a_hole_is_never_Conform()
    {
        Assert.Equal(ControlStatus.Neconform, Check(P40("0", "", "0", "1"), "P40").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(P40("1", "1", "", "1"), "P40").Status);
    }

    // ---- P41 ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P41_NoLMHash_1_and_level_5_is_Conform_and_absent_NoLMHash_is_the_default()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Stig.NoLMHash", "1"), ("Stig.LmCompatibilityLevel", "5")), "P41").Status);
        var c = Check(Station(true, ("Stig.NoLMHash", Absent), ("Stig.LmCompatibilityLevel", "5")), "P41");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("implicitul Windows 10/11 este 1", c.Detail);
    }

    [Theory]
    [InlineData("0", "LM sau NTLMv1")]
    [InlineData("2", "LM sau NTLMv1")]
    [InlineData("3", "STIG cere 5")]
    [InlineData("4", "STIG cere 5")]
    [InlineData(Absent, "implicitul Windows este 3")]
    public void P41_level_below_5_is_Neconform(string level, string text)
    {
        var c = Check(Station(true, ("Stig.NoLMHash", "1"), ("Stig.LmCompatibilityLevel", level)), "P41");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(text, c.Detail);
    }

    [Fact]
    public void P41_stored_LM_hash_is_Neconform_and_an_unreadable_half_is_Nedeterminat()
    {
        var c = Check(Station(true, ("Stig.NoLMHash", "0"), ("Stig.LmCompatibilityLevel", "5")), "P41");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("LAN Manager", c.Detail);
        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true, ("Stig.NoLMHash", ""), ("Stig.LmCompatibilityLevel", "5")), "P41").Status);
    }

    // ---- P42 ----------------------------------------------------------------------------------------------------------------

    private static StationFacts P42(string tcb, string token, string debug, string names = "") =>
        Station(true, ("UserRights.SeTcbPrivilege", tcb), ("UserRights.SeCreateTokenPrivilege", token), ("UserRights.SeDebugPrivilege", debug), ("UserRights.Names", names));

    [Fact]
    public void P42_no_holders_and_only_Administrators_for_debug_is_Conform()
    {
        Assert.Equal(ControlStatus.Conform, Check(P42("(niciunul)", "(niciunul)", Admins), "P42").Status);
        Assert.Equal(ControlStatus.Conform, Check(P42("(niciunul)", "(niciunul)", "(niciunul)"), "P42").Status);
    }

    [Fact]
    public void P42_a_holder_of_Tcb_or_CreateToken_or_an_extra_debug_holder_is_Neconform_and_named()
    {
        var c = Check(P42("S-1-5-21-1-2-3-1001", "(niciunul)", Admins, @"S-1-5-21-1-2-3-1001=PC\ion;S-1-5-32-544=BUILTIN\Administrators"), "P42");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(@"PC\ion (S-1-5-21-1-2-3-1001)", c.Detail);
        Assert.Contains("SeTcbPrivilege", c.Detail);
        Assert.Equal(ControlStatus.Neconform, Check(P42("(niciunul)", "S-1-5-18", Admins), "P42").Status);
        var dbg = Check(P42("(niciunul)", "(niciunul)", Admins + ";S-1-5-21-1-2-3-1002"), "P42");
        Assert.Equal(ControlStatus.Neconform, dbg.Status);
        Assert.Contains("S-1-5-21-1-2-3-1002", dbg.Detail);
        Assert.DoesNotContain("S-1-5-32-544", dbg.Detail);
    }

    [Fact]
    public void P42_unreadable_right_is_Nedeterminat_unless_another_one_already_violates()
    {
        Assert.Equal(ControlStatus.Nedeterminat, Check(P42("(niciunul)", "", Admins), "P42").Status);
        Assert.Equal(ControlStatus.Neconform, Check(P42("S-1-5-18", "", Admins), "P42").Status);
    }

    // ---- P43 ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1", Absent)]
    [InlineData(Absent, "1")]
    [InlineData("1", "1")]
    public void P43_either_value_1_is_Neconform(string hklm, string hkcu)
    {
        var c = Check(Station(true, ("Stig.AlwaysInstallElevatedHklm", hklm), ("Stig.AlwaysInstallElevatedHkcu", hkcu)), "P43");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("drepturi de SYSTEM", c.Detail);
    }

    [Fact]
    public void P43_absent_or_zero_is_Conform_and_says_hkcu_is_the_current_user_only()
    {
        var c = Check(Station(true, ("Stig.AlwaysInstallElevatedHklm", Absent), ("Stig.AlwaysInstallElevatedHkcu", "0")), "P43");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("utilizatorul care a pornit aplicația", string.Join(" ", c.Evidence));
        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true, ("Stig.AlwaysInstallElevatedHklm", Absent), ("Stig.AlwaysInstallElevatedHkcu", "")), "P43").Status);
    }

    // ---- P44 ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    public void P44_DEP_AlwaysOn_or_OptOut_with_SEHOP_on_is_Conform(string dep)
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Stig.DepSupportPolicy", dep), ("Stig.DisableExceptionChainValidation", "0")), "P44").Status);
    }

    [Theory]
    [InlineData("0", "0", "AlwaysOff")]
    [InlineData("2", "0", "OptIn")]
    [InlineData("3", "1", "SEHOP")]
    public void P44_weak_DEP_or_SEHOP_off_is_Neconform(string dep, string sehop, string text)
    {
        var c = Check(Station(true, ("Stig.DepSupportPolicy", dep), ("Stig.DisableExceptionChainValidation", sehop)), "P44");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(text, c.Detail);
    }

    [Fact]
    public void P44_absent_SEHOP_needs_verification_and_unread_DEP_is_Nedeterminat()
    {
        var c = Check(Station(true, ("Stig.DepSupportPolicy", "3"), ("Stig.DisableExceptionChainValidation", Absent)), "P44");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("valoarea 0 setată explicit", c.Detail);
        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true, ("Stig.DepSupportPolicy", ""), ("Stig.DisableExceptionChainValidation", "0")), "P44").Status);
    }

    // ---- P45 ----------------------------------------------------------------------------------------------------------------

    private static StationFacts P45(string cb, string cu, string sb, string su, string start = "3") =>
        Station(true, ("Stig.WinRmClientAllowBasic", cb), ("Stig.WinRmClientAllowUnencrypted", cu), ("Stig.WinRmServiceAllowBasic", sb), ("Stig.WinRmServiceAllowUnencrypted", su), ("WinRM.ServiceStart", start));

    [Fact]
    public void P45_all_zero_is_Conform()
    {
        Assert.Equal(ControlStatus.Conform, Check(P45("0", "0", "0", "0"), "P45").Status);
    }

    [Theory]
    [InlineData("1", "0", "0", "0", "Client AllowBasic")]
    [InlineData("0", "1", "0", "0", "Client AllowUnencryptedTraffic")]
    [InlineData("0", "0", "1", "0", "Service AllowBasic")]
    [InlineData("0", "0", "0", "1", "Service AllowUnencryptedTraffic")]
    public void P45_any_value_1_is_Neconform(string cb, string cu, string sb, string su, string text)
    {
        var c = Check(P45(cb, cu, sb, su), "P45");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(text, c.Detail);
    }

    [Fact]
    public void P45_unset_policy_is_never_Conform_even_with_the_service_off_as_STIG_counts_it_as_a_finding()
    {
        var off = Check(P45(Absent, Absent, Absent, Absent, "4"), "P45");
        Assert.Equal(ControlStatus.DeVerificat, off.Status);
        Assert.Contains("Serviciul WinRM este oprit", off.Detail);
        Assert.Contains("STIG cere valoarea 0 setată explicit", off.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(P45(Absent, Absent, Absent, Absent, Absent), "P45").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(P45(Absent, Absent, Absent, Absent, "2"), "P45").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(P45("0", "", "0", "0"), "P45").Status);
    }

    [Theory]
    [InlineData("2", "0", "0", "0")]
    [InlineData("0", "0", "(gol)", "0")]
    public void P45_a_value_other_than_0_or_1_is_never_Conform(string cb, string cu, string sb, string su)
    {
        var c = Check(P45(cb, cu, sb, su), "P45");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("Valori neașteptate", c.Detail);
    }

    [Theory]
    [InlineData("2", "0")]
    [InlineData("0", "(gol)")]
    public void P43_a_value_other_than_0_1_or_missing_is_never_Conform(string hklm, string hkcu)
    {
        var c = Check(Station(true, ("Stig.AlwaysInstallElevatedHklm", hklm), ("Stig.AlwaysInstallElevatedHkcu", hkcu)), "P43");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("valoare neașteptată", c.Detail);
    }

    [Fact]
    public void P47_iis_components_without_the_web_service_are_DeVerificat()
    {
        var c = Check(Station(true, ("Stig.W3svcKey", "nu"), ("Stig.W3svcStart", Absent), ("Stig.IisInetStpKey", "da")), "P47");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("componente IIS", c.Detail);
    }

    // ---- P46 ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P46_EnableMulticast_0_is_Conform()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Stig.LlmnrEnableMulticast", "0")), "P46").Status);
    }

    [Theory]
    [InlineData("1")]
    [InlineData(Absent)]
    public void P46_enabled_or_absent_is_DeVerificat_and_Neconform_on_an_isolated_station(string v)
    {
        var open = Check(Station(false, ("Stig.LlmnrEnableMulticast", v)), "P46");
        Assert.Equal(ControlStatus.DeVerificat, open.Status);
        Assert.Contains("fără regulă STIG", open.Detail);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("Stig.LlmnrEnableMulticast", v)), "P46").Status);
    }

    // ---- P47 ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P47_absent_service_is_Conform()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Stig.W3svcKey", "nu"), ("Stig.W3svcStart", Absent)), "P47").Status);
    }

    [Fact]
    public void P47_installed_IIS_is_Neconform_on_isolated_and_DeVerificat_otherwise()
    {
        var iso = Check(Station(true, ("Stig.W3svcKey", "da"), ("Stig.W3svcStart", "3")), "P47");
        Assert.Equal(ControlStatus.Neconform, iso.Status);
        Assert.Contains("pornire manuală", iso.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(false, ("Stig.W3svcKey", "da"), ("Stig.W3svcStart", "2")), "P47").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true, ("Stig.W3svcKey", "")), "P47").Status);
    }
}
