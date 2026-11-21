using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression tests for defects found in the lab on 2026-11-10 (real VMs): BitLocker missing on Server, domain controller C01, Secure Boot without firmware type, blank banner, Server antivirus list, count grammar.</summary>
public class LabFixes20261110Tests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Absent = StationFactCollector.Absent;

    private static StationFacts Station(StationPosture p = StationPosture.Standalone) => new()
    {
        Host = "ST-LAB", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = true, Posture = p,
    };

    private static ControlCheck Check(StationFacts f, string id) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == id);

    private static LogFact L(string ch, int id, int n, params (string K, string V)[] data) =>
        new(ch, id, Start.AddDays(1).AddMinutes(n), 5000 + n, "S-1-5-21-1-2-3-1001", data.ToDictionary(d => d.K, d => d.V));

    // ---- 2. BitLocker feature not installed -----------------------------------------------------------------------

    [Theory]
    [InlineData("P11")]
    [InlineData("P32")]
    public void BitLocker_not_installed_is_neconform_not_unreadable(string id)
    {
        var f = Station();
        f.Settings[StationFactCollector.BitLockerNotInstalledKey] = "1";
        f.Settings["BitLocker.SystemDrive"] = "";
        f.Volumes = null;
        var c = Check(f, id);
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Componenta BitLocker nu este instalată", c.Detail);
        Assert.DoesNotContain("drepturi de administrator", c.Detail);
    }

    [Theory]
    [InlineData("P11")]
    [InlineData("P32")]
    public void A_failed_BitLocker_read_without_the_flag_stays_nedeterminat(string id)
    {
        var f = Station();
        f.Settings["BitLocker.SystemDrive"] = "";
        f.Volumes = null;
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, id).Status);
    }

    // ---- 3. C01 on a domain controller ----------------------------------------------------------------------------

    [Fact]
    public void C01_on_a_domain_controller_is_de_verificat_with_a_pointer_to_Active_Directory()
    {
        var f = Station();
        f.Settings["OS.ProductType"] = "LanmanNT";
        var c = Check(f, "C01");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("controler de domeniu", c.Detail);
        Assert.Contains("Active Directory", c.Detail);
    }

    [Theory]
    [InlineData("WinNT")]
    [InlineData("ServerNT")]
    public void C01_with_no_accounts_on_other_products_stays_nedeterminat(string productType)
    {
        var f = Station();
        f.Settings["OS.ProductType"] = productType;
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, "C01").Status);
    }

    [Fact]
    public void C01_on_a_domain_controller_with_a_local_accounts_gap_stays_nedeterminat()
    {
        var f = Station();
        f.Settings["OS.ProductType"] = "LanmanNT";
        f.Gaps.Add(new EvidenceGap("Conturi locale", EvidenceStatus.Failed, "acces refuzat", "impact", "alt", "Posibil"));
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, "C01").Status);
    }

    // ---- 4. P15 firmware type inferred from Secure Boot -----------------------------------------------------------

    [Theory]
    [InlineData(Absent)]
    [InlineData("")]
    public void P15_with_Secure_Boot_on_and_no_firmware_type_says_UEFI_inferred(string fw)
    {
        var f = Station();
        f.Settings["SecureBoot.Enabled"] = "1";
        f.Settings["Firmware.Type"] = fw;
        var c = Check(f, "P15");
        Assert.Contains("UEFI (dedus", c.Detail);
        Assert.DoesNotContain("necunoscut", c.Detail);
        Assert.DoesNotContain("necitit", c.Detail);
    }

    [Fact]
    public void P15_with_firmware_type_2_says_UEFI_modern()
    {
        var f = Station();
        f.Settings["SecureBoot.Enabled"] = "1";
        f.Settings["Firmware.Type"] = "2";
        Assert.Contains("UEFI (modern)", Check(f, "P15").Detail);
    }

    [Fact]
    public void P15_with_Secure_Boot_off_and_no_firmware_type_stays_necunoscut()
    {
        var f = Station();
        f.Settings["SecureBoot.Enabled"] = "0";
        f.Settings["Firmware.Type"] = Absent;
        var c = Check(f, "P15");
        Assert.Contains("necunoscut", c.Detail);
        Assert.DoesNotContain("UEFI (dedus", c.Detail);
    }

    // ---- 5. P26 blank banner --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StationPosture.Standalone, " ")]
    [InlineData(StationPosture.Standalone, "\0")]
    [InlineData(StationPosture.Standalone, "\r\n")]
    [InlineData(StationPosture.IsolatedNetwork, " ")]
    [InlineData(StationPosture.IsolatedNetwork, "\0")]
    [InlineData(StationPosture.IsolatedNetwork, "\r\n")]
    public void P26_with_a_blank_text_is_neconform(StationPosture posture, string text)
    {
        var f = Station(posture);
        f.Settings["LegalNotice.Caption"] = Absent;
        f.Settings["LegalNotice.Text"] = text;
        var c = Check(f, "P26");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("nu apare niciun mesaj", c.Detail);
    }

    [Fact]
    public void P26_with_a_real_text_is_de_verificat_with_the_character_count()
    {
        var f = Station();
        f.Settings["LegalNotice.Caption"] = Absent;
        f.Settings["LegalNotice.Text"] = "STRICT SECRET";
        var c = Check(f, "P26");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("13", c.Detail);
    }

    // ---- 6. P39 on Windows Server ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("ServerNT")]
    [InlineData("LanmanNT")]
    public void P39_on_server_with_Defender_realtime_on_is_conform(string productType)
    {
        var f = Station();
        f.Settings["OS.ProductType"] = productType;
        f.AntivirusProducts = [];
        f.Settings["Defender.AntivirusEnabled"] = "True";
        f.Settings["Defender.RealTime"] = "True";
        var c = Check(f, "P39");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("Windows Server nu are Centrul de securitate", c.Detail);
    }

    [Theory]
    [InlineData("ServerNT")]
    [InlineData("LanmanNT")]
    public void P39_on_server_with_Defender_realtime_off_is_de_verificat(string productType)
    {
        var f = Station();
        f.Settings["OS.ProductType"] = productType;
        f.AntivirusProducts = [];
        f.Settings["Defender.AntivirusEnabled"] = "True";
        f.Settings["Defender.RealTime"] = "False";
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "P39").Status);
    }

    [Fact]
    public void P39_on_a_workstation_with_an_empty_list_is_still_neconform()
    {
        var f = Station();
        f.Settings["OS.ProductType"] = "WinNT";
        f.AntivirusProducts = [];
        var c = Check(f, "P39");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Niciun antivirus nu este înregistrat", c.Detail);
    }

    // ---- 7. S02 grammatical agreement -----------------------------------------------------------------------------

    private static StationFacts WithInstalls(int services, int tasks, int drivers)
    {
        var f = Station();
        for (int i = 0; i < services; i++) f.Logs.Add(L("System", 7045, i, ("ServiceName", "Svc" + i), ("ImagePath", @"C:\ProgramData\s" + i + ".exe"), ("ServiceType", "user mode service"), ("AccountName", "LocalSystem")));
        for (int i = 0; i < drivers; i++) f.Logs.Add(L("System", 7045, 100 + i, ("ServiceName", "Drv" + i), ("ImagePath", @"C:\Windows\d" + i + ".sys"), ("ServiceType", "kernel mode driver"), ("AccountName", "")));
        for (int i = 0; i < tasks; i++) f.Logs.Add(L("Security", 4698, 200 + i, ("TaskName", @"\Task" + i), ("SubjectUserName", "ion"), ("SubjectDomainName", "ST-LAB")));
        return f;
    }

    [Fact]
    public void S02_agrees_with_one_of_each()
    {
        var d = Check(WithInstalls(1, 1, 1), "S02").Detail;
        Assert.Contains("nou", d);
        Assert.Contains("creată", d);
        Assert.Contains("instalat, listat", d);
        Assert.DoesNotContain("1 driver instalate", d);
        Assert.DoesNotContain("un driver instalate", d);
    }

    [Fact]
    public void S02_agrees_with_two_of_each()
    {
        var d = Check(WithInstalls(2, 2, 2), "S02").Detail;
        Assert.Contains("noi", d);
        Assert.Contains("create", d);
        Assert.Contains("instalate, listate", d);
    }
}
