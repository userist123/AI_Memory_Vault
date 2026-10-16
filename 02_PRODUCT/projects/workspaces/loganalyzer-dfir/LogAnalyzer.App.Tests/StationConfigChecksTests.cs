using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Air-gap station requirements (comparison 2026-10-12, rows A2, A3, A4, A5, A7, A14, A16, B8, C8): each row is a read-only station check with a
/// plain-Romanian guide. StationFacts are built in memory; a source that could not be read is "" (or a null list) and must give NEDETERMINAT, never
/// CONFORM. (These tests live here because the compiled LogAnalyzer.Dfir.Tests assembly is quarantined by Defender on the development machine.)
/// </summary>
public sealed class StationConfigChecksTests
{
    private const string Absent = StationFactCollector.Absent;
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static StationFacts Station(bool isolated = true, params (string K, string V)[] settings)
    {
        var f = new StationFacts { Posture = isolated ? LogAnalyzer.Core.Services.Edition.StationPosture.Standalone : LogAnalyzer.Core.Services.Edition.StationPosture.Internet, IsAdministrator = true, CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-90), PeriodEndUtc = Now };
        foreach (var (k, v) in settings) f.Settings[k] = v;
        return f;
    }

    private static ControlCheck Check(StationFacts f, string id) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);

    private static readonly string[] NewIds = ["P32", "P33", "P34", "P35", "P36", "P37", "A09", "P38", "P39", "N06", "N07", "N08", "N09", "N10", "A10", "D02", "D03", "S04"];

    private static LogFact Log(string channel, int id, DateTimeOffset t, params (string K, string V)[] data) =>
        new(channel, id, t, id * 10, "", data.ToDictionary(d => d.K, d => d.V, StringComparer.OrdinalIgnoreCase));

    private static ChannelCoverage Readable(string channel) => new(channel, Now.AddDays(-200), 10, true, "");

    // ---- unreadable sources ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_readable_gives_Nedeterminat_with_the_administrator_hint_for_every_new_check()
    {
        var report = ControlEvaluator.Evaluate(Station());
        foreach (var id in NewIds)
        {
            var c = report.Checks.Single(x => x.Id == id);
            Assert.True(c.Status == ControlStatus.Nedeterminat, $"{id} gave {c.Status} with nothing readable");
            // A10 without folders from the procedure profile is not a rights problem (owner report 2026-10-12): it names the profile instead
            Assert.Contains(id == "A10" ? "profilul de proceduri" : "administrator", c.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- A2 / B8 BitLocker -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P32_every_fixed_volume_encrypted_and_system_drive_with_pin_is_Conform_and_reported_per_volume()
    {
        var f = Station();
        f.Volumes = [new("C:", VolumeKind.Sistem, 1, 1, [4, 3]), new("D:", VolumeKind.Fix, 1, 1, [3])];
        var c = Check(f, "P32");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains(c.Evidence, e => e.StartsWith("C: (discul de sistem): protejat, criptat complet, protecții: TPM și PIN", StringComparison.Ordinal));
        Assert.Contains(c.Evidence, e => e.StartsWith("D: (disc fix): protejat", StringComparison.Ordinal));
    }

    [Fact]
    public void P32_an_unencrypted_data_volume_or_a_system_drive_without_pin_is_Neconform()
    {
        var f = Station();
        f.Volumes = [new("C:", VolumeKind.Sistem, 1, 1, [1, 3]), new("D:", VolumeKind.Fix, 0, 0, [])];
        var c = Check(f, "P32");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("D: (disc fix) NU este criptat", c.Detail);
        Assert.Contains("fără PIN", c.Detail);
    }

    [Fact]
    public void P32_a_volume_whose_state_was_not_reported_is_Nedeterminat_and_unread_protectors_are_DeVerificat()
    {
        var f = Station();
        f.Volumes = [new("C:", VolumeKind.Sistem, 1, 1, [4]), new("E:", VolumeKind.Fix, null, null, null, "Windows nu raportează starea BitLocker pentru această unitate")];
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, "P32").Status);
        f.Volumes = [new("C:", VolumeKind.Sistem, 1, 1, null)];
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "P32").Status);
    }

    [Fact]
    public void D02_removable_media_unencrypted_with_write_allowed_is_Neconform_and_the_deny_policy_makes_it_Conform()
    {
        var f = Station(true, ("BitLocker.RDVDenyWriteAccess", Absent));
        f.Volumes = [new("C:", VolumeKind.Sistem, 1, 1, [4]), new("E:", VolumeKind.Amovibil, 0, 0, [])];
        var c = Check(f, "D02");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("E:", c.Detail);
        Assert.Contains(c.Evidence, e => e.StartsWith("E: (suport amovibil): NEPROTEJAT", StringComparison.Ordinal));

        f.Settings["BitLocker.RDVDenyWriteAccess"] = "1";
        Assert.Equal(ControlStatus.Conform, Check(f, "D02").Status);

        var g = Station(true, ("BitLocker.RDVDenyWriteAccess", Absent));
        g.Volumes = [new("E:", VolumeKind.Amovibil, 1, 1, [8])];
        Assert.Equal(ControlStatus.Conform, Check(g, "D02").Status);
    }

    [Fact]
    public void D02_without_administrator_a_connected_medium_is_Nedeterminat_and_no_medium_without_policy_is_DeVerificat()
    {
        var f = new StationFacts { RemovableDrivesNow = ["F:"], CollectedUtc = Now };
        f.Settings["BitLocker.RDVDenyWriteAccess"] = Absent;
        var c = Check(f, "D02");
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("F:", c.Detail);

        var none = Station(true, ("BitLocker.RDVDenyWriteAccess", Absent));
        none.Volumes = [new("C:", VolumeKind.Sistem, 1, 1, [4])];
        Assert.Equal(ControlStatus.DeVerificat, Check(none, "D02").Status);
    }

    // ---- A3 TPM, DMA, VBS --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P33_tpm_states()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("TPM.Present", "da"), ("TPM.Enabled", "True"), ("TPM.Activated", "True"), ("TPM.SpecVersion", "2.0, 0, 1.59"), ("TPM.Ready", "True")), "P33").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("TPM.Present", "nu")), "P33").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("TPM.Present", "da"), ("TPM.Enabled", "False"), ("TPM.Activated", "False")), "P33").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("TPM.Present", "da"), ("TPM.Enabled", "True"), ("TPM.Activated", "True"), ("TPM.SpecVersion", "1.2, 2, 3"), ("TPM.Ready", "True")), "P33").Status);
    }

    [Fact]
    public void P34_kernel_dma_protection_off_on_an_isolated_station_is_Neconform()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("DMA.KernelProtection", "1"), ("DMA.EnumerationPolicy", "0")), "P34").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("DMA.KernelProtection", "1"), ("DMA.EnumerationPolicy", "2")), "P34").Status);
        var off = Check(Station(true, ("DMA.KernelProtection", "0"), ("DMA.EnumerationPolicy", Absent)), "P34");
        Assert.Equal(ControlStatus.Neconform, off.Status);
        Assert.Contains("Thunderbolt", off.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(false, ("DMA.KernelProtection", "0")), "P34").Status);
    }

    [Fact]
    public void P35_vbs_running_configured_and_off()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("DeviceGuard.VbsStatus", "2"), ("DeviceGuard.ServicesRunning", "1,2")), "P35").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("DeviceGuard.VbsStatus", "1")), "P35").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("DeviceGuard.VbsStatus", "0")), "P35").Status);
    }

    // ---- A4 password on wake -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P36_password_on_wake()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Wake.PolicyAC", "1"), ("Wake.PolicyDC", "1"), ("Wake.EffectiveAC", "1"), ("Wake.EffectiveDC", "1")), "P36").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("Wake.PolicyAC", "0"), ("Wake.PolicyDC", "1")), "P36").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("Wake.PolicyAC", Absent), ("Wake.PolicyDC", Absent), ("Wake.EffectiveAC", "0"), ("Wake.EffectiveDC", "1")), "P36").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("Wake.PolicyAC", Absent), ("Wake.PolicyDC", Absent), ("Wake.EffectiveAC", "1"), ("Wake.EffectiveDC", "1"), ("Wake.DelayLockInterval", "-1")), "P36").Status);
        var notEnforced = Check(Station(true, ("Wake.PolicyAC", Absent), ("Wake.PolicyDC", Absent), ("Wake.EffectiveAC", "1"), ("Wake.EffectiveDC", "1"), ("Wake.DelayLockInterval", "0")), "P36");
        Assert.Equal(ControlStatus.DeVerificat, notEnforced.Status);
        Assert.Contains("nu este impusă prin politică", notEnforced.Detail);
    }

    // ---- A5 App Control and blocked attempts -------------------------------------------------------------------------------------------------

    [Fact]
    public void P37_app_control_modes_and_the_fallback_to_applocker()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("CI.UserModeEnforcement", "2")), "P37").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("CI.UserModeEnforcement", "1")), "P37").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("CI.UserModeEnforcement", "0"), ("AppLocker.Collections", Absent)), "P37").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("CI.UserModeEnforcement", "0"), ("AppLocker.Collections", "Exe: mod 1, 3 reguli"), ("AppLocker.AppIDSvcStart", "2")), "P37").Status);
    }

    [Fact]
    public void A09_counts_blocked_and_audited_programs_in_the_period()
    {
        var f = Station(true, ("CI.UserModeEnforcement", "2"));
        f.Coverage.Add(Readable(StationFactCollector.AppLockerExeChannel));
        f.Coverage.Add(Readable(StationFactCollector.CodeIntegrityChannel));
        Assert.Equal(ControlStatus.Conform, Check(f, "A09").Status);

        f.Logs.Add(Log(StationFactCollector.AppLockerExeChannel, 8004, Now.AddDays(-3), ("FullFilePath", @"E:\tool.exe")));
        f.Logs.Add(Log(StationFactCollector.CodeIntegrityChannel, 3076, Now.AddDays(-2), ("File Name", @"C:\Users\Public\x.dll")));
        var r = ControlEvaluator.Evaluate(f);
        var c = r.Checks.Single(x => x.Id == "A09");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("1 pornire oprită", c.Detail);
        Assert.Contains(c.Evidence, e => e.StartsWith("AppLocker 8004", StringComparison.Ordinal) && e.Contains(@"E:\tool.exe"));
        Assert.Contains(r.Actions, a => a.Action.Contains("lista albă") && a.Detail == @"E:\tool.exe");

        var noList = Station(true, ("CI.UserModeEnforcement", "0"), ("AppLocker.Collections", Absent));
        noList.Coverage.Add(Readable(StationFactCollector.AppLockerExeChannel));
        Assert.Equal(ControlStatus.DeVerificat, Check(noList, "A09").Status);
    }

    // ---- A7 antivirus ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void P38_last_full_and_quick_scan()
    {
        string D(int daysAgo) => Now.AddDays(-daysAgo).ToString("o");
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Defender.FullScanEndUtc", D(10)), ("Defender.QuickScanEndUtc", D(1))), "P38").Status);
        var never = Check(Station(true, ("Defender.FullScanEndUtc", Absent), ("Defender.QuickScanEndUtc", D(1))), "P38");
        Assert.Equal(ControlStatus.Neconform, never.Status);
        Assert.Contains("NICIODATĂ", never.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("Defender.FullScanEndUtc", D(45)), ("Defender.QuickScanEndUtc", D(1))), "P38").Status);
    }

    [Fact]
    public void P39_third_party_antivirus_from_security_center()
    {
        // productState: 0x41000 = on, signatures current (as reported for an enabled product); 0x40000 = off; 0x41010 = on, signatures out of date.
        var f = Station();
        f.AntivirusProducts = [new("Antivirus Exemplu", 0x41000u), new("Windows Defender", 0x60100u)];
        var c = Check(f, "P39");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("Antivirus Exemplu", c.Detail);
        f.AntivirusProducts = [new("Antivirus Exemplu", 0x40000u)];
        Assert.Equal(ControlStatus.Neconform, Check(f, "P39").Status);
        f.AntivirusProducts = [];
        Assert.Equal(ControlStatus.Neconform, Check(f, "P39").Status);
        f.AntivirusProducts = [new("Antivirus Exemplu", 0x41010u)];
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "P39").Status);
    }

    // ---- A14 interconnection paths -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void N06_only_default_admin_shares_is_Conform_and_a_created_share_on_an_isolated_station_is_Neconform()
    {
        var f = Station();
        f.Shares = [new("ADMIN$", @"C:\Windows", 0x80000000u), new("C$", @"C:\", 0x80000000u), new("IPC$", "", 0x80000003u)];
        Assert.Equal(ControlStatus.Conform, Check(f, "N06").Status);
        f.Shares.Add(new("Schimb", @"D:\Schimb", 0));
        var c = Check(f, "N06");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Schimb", c.Detail);
    }

    [Fact]
    public void N07_remote_assistance()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("RA.PolicyAllowToGetHelp", "0"), ("RA.AllowToGetHelp", "1")), "N07").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("RA.PolicyAllowToGetHelp", Absent), ("RA.AllowToGetHelp", "1")), "N07").Status);
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("RA.PolicyAllowToGetHelp", Absent), ("RA.AllowToGetHelp", "0")), "N07").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("RA.PolicyAllowToGetHelp", Absent), ("RA.AllowToGetHelp", Absent)), "N07").Status);
    }

    [Fact]
    public void N08_winrm()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("WinRM.ServiceStart", "3"), ("WinRM.Listeners", Absent), ("WinRM.State", "Stopped")), "N08").Status);
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("WinRM.ServiceStart", "4"), ("WinRM.Listeners", ""), ("WinRM.State", "")), "N08").Status);
        var on = Check(Station(true, ("WinRM.ServiceStart", "2"), ("WinRM.Listeners", "*+HTTP"), ("WinRM.State", "Running")), "N08");
        Assert.Equal(ControlStatus.Neconform, on.Status);
        Assert.Contains("*+HTTP", on.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(false, ("WinRM.ServiceStart", "2"), ("WinRM.State", "Running")), "N08").Status);
    }

    [Fact]
    public void N09_portproxy_and_N10_internet_connection_sharing()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("PortProxy.Entries", Absent)), "N09").Status);
        var pp = Check(Station(true, ("PortProxy.Entries", "v4tov4/tcp: 0.0.0.0/8080 → 10.0.0.5/80")), "N09");
        Assert.Equal(ControlStatus.Neconform, pp.Status);
        Assert.Contains("10.0.0.5/80", pp.Detail);

        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("ICS.Shared", "nu"), ("ICS.PolicyShowUI", "0")), "N10").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("ICS.Shared", "da: Ethernet (își partajează conexiunea)")), "N10").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("ICS.Shared", ""), ("ICS.PolicyShowUI", "0")), "N10").Status);
    }

    // ---- A16 SACL on classified folders ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A10_folders_with_audit_rules_and_file_system_auditing_are_Conform()
    {
        var f = Station();
        f.AuditPolicy = new() { ["File System"] = AuditSetting.Success | AuditSetting.Failure };
        // read, write and delete for Everyone, success and failure, inherited by the files (A10 extended 2026-10-15)
        f.FolderAudits = [new(@"D:\Clasificat", true, 1, "Everyone: Modify (Success, Failure)", "", [new AuditRuleFact("S-1-1-0", "Everyone", 0x301BF, true, true, true)])];
        Assert.Equal(ControlStatus.Conform, Check(f, "A10").Status);
    }

    [Fact]
    public void A10_a_folder_without_audit_rules_or_file_system_auditing_off_is_Neconform_and_names_the_folder()
    {
        var f = Station();
        f.AuditPolicy = new() { ["File System"] = AuditSetting.Success };
        f.FolderAudits = [new(@"D:\Clasificat", true, 2, "x", ""), new(@"E:\Secret", true, 0, "", "")];
        var c = Check(f, "A10");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(@"E:\Secret", c.Detail);
        Assert.DoesNotContain(@"D:\Clasificat nu are", c.Detail);

        f.AuditPolicy = new() { ["File System"] = AuditSetting.None };
        f.FolderAudits = [new(@"D:\Clasificat", true, 2, "x", "")];
        Assert.Equal(ControlStatus.Neconform, Check(f, "A10").Status);
    }

    [Fact]
    public void A10_unread_rules_and_an_empty_profile_are_Nedeterminat_never_Conform()
    {
        var f = Station();
        f.AuditPolicy = new() { ["File System"] = AuditSetting.Success };
        f.FolderAudits = [new(@"D:\Clasificat", true, null, "", "regulile de audit nu au putut fi citite (necesită administrator)")];
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, "A10").Status);
        f.FolderAudits = [];
        var empty = Check(f, "A10");
        Assert.Equal(ControlStatus.Nedeterminat, empty.Status);   // owner rule 2026-10-12: no folders in the profile = cannot be determined, not "to verify"
        Assert.Contains("Foldere clasificate", empty.Detail);
        f.FolderAudits = [new(@"Z:\Lipsa", false, null, "", "")];
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "A10").Status);
    }

    // ---- C8 inbound controls -----------------------------------------------------------------------------------------------------------------

    private static StationFacts MediumStation(bool scanned, bool detection = false)
    {
        var f = Station();
        f.Coverage.Add(Readable(StationFactCollector.DefenderChannel));
        f.Coverage.Add(Readable("Microsoft-Windows-Partition/Diagnostic"));
        var t = Now.AddDays(-5);
        f.Logs.Add(Log("Microsoft-Windows-Partition/Diagnostic", 1006, t, ("Capacity", "16000000000"), ("BusType", "7"), ("Manufacturer", "Kingston"), ("Model", "DataTraveler"), ("SerialNumber", "SN-TEST-0001")));
        if (scanned) f.Logs.Add(Log(StationFactCollector.DefenderChannel, 1000, t.AddMinutes(2), ("Scan Resources", @"E:\")));
        if (detection) f.Logs.Add(Log(StationFactCollector.DefenderChannel, 1116, t.AddMinutes(3), ("Threat Name", "Trojan:Test/Exemplu"), ("Path", @"E:\a.exe")));
        return f;
    }

    [Fact]
    public void D03_a_medium_scanned_right_after_connection_is_Conform_and_an_unscanned_one_is_DeVerificat()
    {
        var ok = Check(MediumStation(scanned: true), "D03");
        Assert.Equal(ControlStatus.Conform, ok.Status);
        Assert.Contains(ok.Evidence, e => e.Contains("scanat: Defender 1000"));
        var missing = Check(MediumStation(scanned: false), "D03");
        Assert.Equal(ControlStatus.DeVerificat, missing.Status);
        Assert.Contains("decontaminare", missing.Detail);
    }

    [Fact]
    public void D03_a_detection_after_a_medium_is_Neconform()
    {
        var c = Check(MediumStation(scanned: true, detection: true), "D03");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Trojan:Test/Exemplu", c.Detail);
    }

    [Fact]
    public void S04_installed_updates_compared_with_the_offline_register()
    {
        var f = Station();
        f.InstalledUpdates = [new("KB5031111", Now.AddDays(-20).UtcDateTime, "Security Update"), new("KB5020000", Now.AddDays(-400).UtcDateTime, "Update")];
        f.OfflineUpdateRegister = [];
        var none = Check(f, "S04");
        Assert.Equal(ControlStatus.DeVerificat, none.Status);
        Assert.Contains("M05", none.Detail);

        f.OfflineUpdateRegister = [new UpdateRow { Date = "2026-09-22", Kind = "windows", Package = "Actualizare cumulativă KB5031111", BroughtBy = "Persoana A" }];
        Assert.Equal(ControlStatus.Conform, Check(f, "S04").Status);

        f.InstalledUpdates.Add(new("KB5032222", Now.AddDays(-3).UtcDateTime, "Security Update"));
        var c = Check(f, "S04");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("KB5032222", c.Detail);
        Assert.Contains(c.Evidence, e => e.StartsWith("KB5032222") && e.EndsWith("NU este în registru"));
    }

    // ---- C6 approved transfer channels -------------------------------------------------------------------------------------------------------

    private static ProcedureProfile Channels() => new()
    {
        Zones = [new() { Name = "ROSU" }, new() { Name = "PUBLIC" }],
        TransferChannels = [new() { Name = "Export aprobat", FromZone = "ROSU", ToZone = "PUBLIC", Medium = "Stick USB criptat" }],
    };

    [Fact]
    public void C6_a_transfer_on_an_approved_channel_is_accepted()
    {
        var r = TransferChannelCheck.Check(new TransferRow { Source = "rosu", Destination = "PUBLIC", MediumType = "Stick / disc USB" }, Channels());
        Assert.True(r.Approved);
        Assert.False(r.NeedsConfirmation);
        Assert.Equal("Export aprobat", r.Channel);
    }

    [Fact]
    public void C6_a_wrong_route_or_medium_is_flagged_with_a_plain_message()
    {
        var route = TransferChannelCheck.Check(new TransferRow { Source = "PUBLIC", Destination = "ROSU", MediumType = "Stick / disc USB" }, Channels());
        Assert.True(route.NeedsConfirmation);
        Assert.Contains("Nu există un canal de transfer aprobat de la „PUBLIC” la „ROSU”", route.Message);
        var medium = TransferChannelCheck.Check(new TransferRow { Source = "ROSU", Destination = "PUBLIC", MediumType = "CD/DVD" }, Channels());
        Assert.True(medium.NeedsConfirmation);
        Assert.Contains("nu este aprobat", medium.Message);
        Assert.Contains("Stick USB criptat", medium.Message);
    }

    [Fact]
    public void C6_without_channels_in_the_profile_nothing_is_approved_and_the_operator_is_told()
    {
        var r = TransferChannelCheck.Check(new TransferRow { Source = "ROSU", Destination = "PUBLIC", MediumType = "USB" }, null);
        Assert.False(r.Defined);
        Assert.False(r.Approved);
        Assert.False(r.NeedsConfirmation);
        Assert.Contains("nu definește canale", r.Message);
    }

    // ---- profile and register ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_profile_has_a_classified_folders_table_that_requires_full_paths()
    {
        Assert.Contains(ProfileTable.ClassifiedFolders, ProfileTables.All);
        Assert.Equal("Foldere clasificate", ProfileTables.Title(ProfileTable.ClassifiedFolders));
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.ClassifiedFolders, [[@"D:\Clasificat", "documente"]]);
        Assert.Equal(@"D:\Clasificat", p.ClassifiedFolders.Single().Path);
        Assert.Empty(ProfileTables.ValidateRow(ProfileTable.ClassifiedFolders, [@"D:\Clasificat", ""]));
        Assert.Contains(ProfileTables.ValidateRow(ProfileTable.ClassifiedFolders, ["Clasificat", ""]), m => m.IsError && m.Message.Contains("calea"));
        Assert.Contains(ProfileOps.Status(p), s => s.Section == "Foldere clasificate" && s.Defined);
    }

    [Fact]
    public void The_offline_update_register_validates_rows_and_takes_installed_updates_without_changing_existing_rows()
    {
        var reg = new UpdateRegister();
        Assert.DoesNotContain(reg.ValidateRow(["2026-10-01", "windows", "KB5031111", new string('a', 64), "catalog", "Persoana A", "M-01", ""], []), m => m.IsError);
        var bad = reg.ValidateRow(["01.10.2026", "altceva", "", "xyz", "", "", "", ""], []);
        Assert.Contains(bad, m => m.Message.Contains("yyyy-MM-dd"));
        Assert.Contains(bad, m => m.Message.Contains("windows | antivirus | aplicatie"));
        Assert.Contains(bad, m => m.Message.Contains("SHA-256"));
        Assert.Contains(bad, m => m.Message.Contains("adus suportul"));

        reg.Rows.Add(new UpdateRow { Date = "2026-09-01", Kind = "windows", Package = "KB5031111", BroughtBy = "Persoana A" });
        var (added, present) = reg.MergeInstalled([("KB5031111", new DateTime(2026, 9, 1)), ("KB5032222", new DateTime(2026, 10, 2)), ("", null)]);
        Assert.Equal((1, 1), (added, present));
        Assert.Equal("Persoana A", reg.Rows[0].BroughtBy);
        Assert.Equal(("2026-10-02", "KB5032222"), (reg.Rows[1].Date, reg.Rows[1].Package));
    }

    // ---- guides and codes --------------------------------------------------------------------------------------------------------------------

    /// <summary>All the files of the evaluator (ControlEvaluator.cs and its partial files, e.g. ControlEvaluator.Classified.cs).</summary>
    private static string EvaluatorSource()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx")))
                return string.Join("\n", Directory.GetFiles(Path.Combine(d.FullName, "LogAnalyzer.Dfir.Windows", "Audit"), "ControlEvaluator*.cs").OrderBy(x => x).Select(File.ReadAllText));
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    private static string Source(params string[] relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return File.ReadAllText(Path.Combine([d.FullName, .. relative]));
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    /// <summary>The station-check guide sync (the same rule as CheckGuideTests in LogAnalyzer.Dfir.Tests): every id the evaluator can emit has a guide.</summary>
    [Fact]
    public void Every_station_check_the_evaluator_can_emit_has_a_written_guide()
    {
        var ids = Regex.Matches(EvaluatorSource(), @"Add\(r, ""([A-Z]\d\d)""")
                       .Select(m => m.Groups[1].Value).Concat(["A03", "A04"]).Distinct().OrderBy(x => x).ToList();
        Assert.True(ids.Count >= 60, $"only {ids.Count} ids parsed");
        Assert.All(NewIds, id => Assert.Contains(id, ids));
        var missing = ids.Where(id => !CheckGuides.Ids.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "checks without a guide: " + string.Join(", ", missing));
        foreach (var id in ids)
        {
            var g = CheckGuides.For(id);
            Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 10, $"{id}: empty section"));
        }
    }

    /// <summary>
    /// Two branches merged on the same day both used P28-P31 and N05 for different checks. Every id the evaluator emits must appear once per
    /// report (a reused id gives two checks with the same code) and must have exactly one guide entry, so the guide shown is the right one.
    /// </summary>
    [Fact]
    public void Every_emitted_check_id_is_unique_and_has_exactly_one_guide()
    {
        var reports = new[]
        {
            ControlEvaluator.Evaluate(Station(true)),
            ControlEvaluator.Evaluate(Station(false)),
            ControlEvaluator.Evaluate(Station(true, ("Net.AdapterCount", "2"), ("Optical.WriterCount", "1"), ("Print.PrinterCount", "1"), ("Print.LogEnabled", "1"),
                ("TPM.Present", "da"), ("DMA.KernelProtection", "1"), ("DeviceGuard.VbsStatus", "2"), ("PortProxy.Entries", Absent), ("ICS.Shared", "nu"))),
        };
        foreach (var report in reports)
        {
            var dup = report.Checks.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key} ({string.Join(" | ", g.Select(c => c.Title))})").ToList();
            Assert.True(dup.Count == 0, "check ids emitted more than once: " + string.Join("; ", dup));
        }

        var guideSource = Source("LogAnalyzer.Dfir.Windows", "Audit", "CheckGuide.cs");
        var guideCounts = Regex.Matches(guideSource, @"\bG\(""([A-Z]{1,2}\d\d)""").GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.Count());
        var atRuntime = reports.SelectMany(r => r.Checks).Select(c => c.Id).Distinct().ToList();
        Assert.True(atRuntime.Count >= 60, $"only {atRuntime.Count} ids emitted");
        // Some checks appear only when their facts exist (A03/A04 per channel, ...): add every id written in an Add(r, "...") call.
        var inSource = Regex.Matches(EvaluatorSource(), @"Add\(r, ""([A-Z]\d\d)""").Select(m => m.Groups[1].Value);
        var emitted = atRuntime.Concat(inSource).Concat(["A03", "A04"]).Distinct().OrderBy(x => x).ToList();
        Assert.True(emitted.Count >= 78, $"only {emitted.Count} ids found");
        var wrong = emitted.Where(id => !guideCounts.TryGetValue(id, out var n) || n != 1).Select(id => $"{id}: {(guideCounts.TryGetValue(id, out var n) ? n : 0)} guides").ToList();
        Assert.True(wrong.Count == 0, "ids without exactly one guide: " + string.Join(", ", wrong));
    }

    [Fact]
    public void The_new_codes_are_decoded_in_plain_words()
    {
        var found = CodeExplainer.FindIn(@"AppLocker 8004 RecordID 1: E:\x.exe; CodeIntegrity 3077; Defender 1116; HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE → RDVDenyWriteAccess = 1; " +
                                         @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services → fAllowToGetHelp = 0; HKLM\SYSTEM\CurrentControlSet\Services\PortProxy → x");
        Assert.Contains(found, x => x.Code == "AppLocker 8004" && x.What.Contains("OPRIT"));
        Assert.Contains(found, x => x.Code == "CodeIntegrity 3077");
        Assert.Contains(found, x => x.Code == "Defender 1116" && x.What.Contains("rău-intenționat"));
        Assert.Contains(found, x => x.Code == @"FVE\RDVDenyWriteAccess");
        Assert.Contains(found, x => x.Code == @"Terminal Services\fAllowToGetHelp");
        Assert.Contains(found, x => x.Code == @"Services\PortProxy");

        var e = CheckGuides.Explain(Check(MediumStation(scanned: true), "D03"));
        Assert.Contains(e.Codes, x => x.Code == "Partition 1006");
        Assert.Contains(e.Codes, x => x.Code == "Defender 1000");
        Assert.Contains("Ce verificăm:", e.ToPlainText());
    }
}
