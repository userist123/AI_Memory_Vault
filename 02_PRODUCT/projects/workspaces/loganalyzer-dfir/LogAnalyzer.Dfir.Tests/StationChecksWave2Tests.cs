using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Second wave of read-only station checks (radios, boot, screen lock, allow-list, autorun, version, privacy, Recall, credentials, PowerShell,
/// data left on disk, logon banner, time, removable-media audit). StationFacts are built in memory; a value that could not be read is "" and
/// must never become Conform.
/// </summary>
public class StationChecksWave2Tests
{
    private const string Absent = StationFactCollector.Absent;

    private static StationFacts Station(bool isolated, params (string K, string V)[] settings)
    {
        var f = new StationFacts { Posture = isolated ? LogAnalyzer.Core.Services.Edition.StationPosture.Standalone : LogAnalyzer.Core.Services.Edition.StationPosture.Internet, IsAdministrator = true };
        foreach (var (k, v) in settings) f.Settings[k] = v;
        return f;
    }

    private static ControlCheck Check(StationFacts f, string id) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);

    private static readonly string[] NewIds = ["N03", "N04", "P15", "P16", "P17", "P18", "P19", "P20", "P21", "P22", "P23", "P24", "P25", "P26", "P27", "A08", "S03"];

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
    public void Every_new_id_has_a_guide_with_all_sections()
    {
        foreach (var id in NewIds)
        {
            var g = CheckGuides.For(id);
            Assert.DoesNotContain("nu are încă o descriere", g.What);
            Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 20, id));
        }
    }

    [Fact]
    public void Wifi_adapter_up_on_isolated_station_is_Neconform_and_absent_adapter_is_Conform()
    {
        var up = Check(Station(true, ("Wifi.AdapterCount", "1"), ("Wifi.UpCount", "1"), ("Wifi.Adapters", "Intel AX201 (pornit)"), ("Wlan.ServiceStart", "2"), ("Wlan.ProfileCount", "3")), "N03");
        Assert.Equal(ControlStatus.Neconform, up.Status);
        Assert.Contains("3 profiluri Wi-Fi salvate", up.Detail);
        var off = Check(Station(true, ("Wifi.AdapterCount", "1"), ("Wifi.UpCount", "0"), ("Wlan.ServiceStart", "4"), ("Wlan.ProfileCount", "0")), "N03");
        Assert.Equal(ControlStatus.DeVerificat, off.Status);
        var none = Check(Station(true, ("Wifi.AdapterCount", "0"), ("Wlan.ServiceStart", Absent), ("Wlan.ProfileCount", "0")), "N03");
        Assert.Equal(ControlStatus.Conform, none.Status);
    }

    [Fact]
    public void Bluetooth_present_with_service_allowed_is_Neconform_on_isolated_station()
    {
        var on = Check(Station(true, ("Bluetooth.ComponentCount", "2"), ("Bluetooth.Components", "Intel Wireless Bluetooth"), ("Bluetooth.ServiceStart", "3")), "N04");
        Assert.Equal(ControlStatus.Neconform, on.Status);
        Assert.Contains("fișiere fără cablu", on.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("Bluetooth.ComponentCount", "2"), ("Bluetooth.ServiceStart", "4")), "N04").Status);
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Bluetooth.ComponentCount", "0"), ("Bluetooth.ServiceStart", Absent)), "N04").Status);
    }

    [Fact]
    public void Secure_boot_screen_lock_and_autorun()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("SecureBoot.Enabled", "1"), ("Firmware.Type", "2")), "P15").Status);
        var off = Check(Station(true, ("SecureBoot.Enabled", "0"), ("Firmware.Type", "2")), "P15");
        Assert.Equal(ControlStatus.Neconform, off.Status);
        Assert.Contains("stick", off.Detail);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("SecureBoot.Enabled", Absent), ("Firmware.Type", "1")), "P15").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("SecureBoot.Enabled", Absent), ("Firmware.Type", Absent)), "P15").Status);

        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("ScreenLock.InactivityTimeoutSecs", "600")), "P16").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("ScreenLock.InactivityTimeoutSecs", "1800")), "P16").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("ScreenLock.InactivityTimeoutSecs", "0")), "P16").Status);
        var missing = Check(Station(true, ("ScreenLock.InactivityTimeoutSecs", Absent)), "P16");
        Assert.Equal(ControlStatus.DeVerificat, missing.Status);
        Assert.Contains("screensaver", missing.Detail);

        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("AutoRun.NoDriveTypeAutoRun", "255"), ("AutoRun.NoAutorun", Absent)), "P18").Status);
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("AutoRun.NoDriveTypeAutoRun", Absent), ("AutoRun.NoAutorun", "1")), "P18").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("AutoRun.NoDriveTypeAutoRun", "145"), ("AutoRun.NoAutorun", "0")), "P18").Status);   // 0x91: neither bit 0x04 nor 0x20
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("AutoRun.NoDriveTypeAutoRun", Absent), ("AutoRun.NoAutorun", Absent)), "P18").Status);
    }

    [Fact]
    public void Allow_list_reports_presence_and_mode_only()
    {
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("AppLocker.Collections", Absent), ("AppLocker.AppIDSvcStart", "3")), "P17").Status);
        var audit = Check(Station(true, ("AppLocker.Collections", "Exe: mod 0, 4 reguli"), ("AppLocker.AppIDSvcStart", "3")), "P17");
        Assert.Equal(ControlStatus.DeVerificat, audit.Status);
        Assert.Contains("doar în mod audit", audit.Detail);
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("AppLocker.Collections", "Exe: mod 1, 4 reguli; Script: mod 1, 2 reguli"), ("AppLocker.AppIDSvcStart", "3")), "P17").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("AppLocker.Collections", "Exe: mod 1, 4 reguli"), ("AppLocker.AppIDSvcStart", "4")), "P17").Status);
    }

    [Fact]
    public void Version_is_facts_only_and_says_the_support_table_is_missing()
    {
        var c = Check(Station(true, ("OS.ProductName", "Windows 10 Pro"), ("OS.DisplayVersion", "24H2"), ("OS.Build", "26100"), ("OS.UBR", "4349"), ("OS.LastUpdateDate", "2026-09-09")), "S03");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("Windows 11", c.Detail);
        Assert.Contains("26100.4349", c.Detail);
        Assert.Contains("2026-09-09", c.Detail);
        Assert.Contains("tabelul oficial", c.Detail);
    }

    [Fact]
    public void Install_dates_are_parsed_from_text_and_hex_filetime()
    {
        Assert.Equal(new DateTime(2026, 9, 9), StationFactCollector.ParseInstalledOn("9/9/2026"));
        Assert.Equal(new DateTime(2026, 9, 9), StationFactCollector.ParseInstalledOn("2026-09-09"));
        Assert.Equal(new DateTime(2026, 9, 9), StationFactCollector.ParseInstalledOn("0x" + new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc().ToString("X")));
        Assert.Null(StationFactCollector.ParseInstalledOn(""));
        Assert.Null(StationFactCollector.ParseInstalledOn("not a date"));
    }

    [Fact]
    public void Privacy_and_recall_policies()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Privacy.Webcam", "Deny"), ("Privacy.Microphone", "Deny")), "P19").Status);
        var allow = Check(Station(true, ("Privacy.Webcam", "Allow"), ("Privacy.Microphone", Absent)), "P19");
        Assert.Equal(ControlStatus.DeVerificat, allow.Status);
        Assert.Contains("fizic", allow.Detail);

        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("AI.DisableAIDataAnalysis", "1"), ("AI.AllowRecallEnablement", Absent), ("AI.CopilotTurnOffHklm", "1"), ("AI.CopilotTurnOffHkcu", Absent)), "P20").Status);
        var recall = Check(Station(true, ("AI.DisableAIDataAnalysis", "0"), ("AI.AllowRecallEnablement", Absent), ("AI.CopilotTurnOffHklm", "1"), ("AI.CopilotTurnOffHkcu", Absent)), "P20");
        Assert.Equal(ControlStatus.Neconform, recall.Status);
        Assert.Contains("poze periodice", recall.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("AI.DisableAIDataAnalysis", Absent), ("AI.AllowRecallEnablement", Absent), ("AI.CopilotTurnOffHklm", Absent), ("AI.CopilotTurnOffHkcu", Absent)), "P20").Status);
    }

    [Fact]
    public void Credential_protection_lsa_and_credential_guard()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("LSA.RunAsPPL", "1"), ("LSA.KeyReadable", "da")), "P21").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("LSA.RunAsPPL", "0"), ("LSA.KeyReadable", "da")), "P21").Status);
        // value empty and key readable = not set; value empty and key unreadable = unknown
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("LSA.RunAsPPL", ""), ("LSA.KeyReadable", "da")), "P21").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true, ("LSA.RunAsPPL", ""), ("LSA.KeyReadable", "nu")), "P21").Status);

        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("DeviceGuard.ServicesRunning", "1,2")), "P22").Status);
        var notRunning = Check(Station(true, ("DeviceGuard.ServicesRunning", "niciunul")), "P22");
        Assert.Equal(ControlStatus.DeVerificat, notRunning.Status);
        Assert.Contains("domeniu", notRunning.Detail);
    }

    [Fact]
    public void PowerShell_logging_and_version_2()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("PS.ScriptBlockLogging", "1"), ("PS.Transcription", "1")), "P23").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("PS.ScriptBlockLogging", "1"), ("PS.Transcription", Absent)), "P23").Status);
        var off = Check(Station(true, ("PS.ScriptBlockLogging", Absent), ("PS.Transcription", Absent)), "P23");
        Assert.Equal(ControlStatus.Neconform, off.Status);
        Assert.Contains("nicio urmă", off.Detail);

        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("PS.V2Engine", Absent)), "P24").Status);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("PS.V2Engine", "2.0")), "P24").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(Station(true, ("PS.V2Engine", "5.1.26100.1")), "P24").Status);
    }

    [Fact]
    public void Data_left_on_disk()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(true, ("Disk.ClearPageFile", "1"), ("Disk.HibernateEnabled", "0"), ("Disk.CrashDumpEnabled", "0")), "P25").Status);
        var bad = Check(Station(true, ("Disk.ClearPageFile", "0"), ("Disk.HibernateEnabled", "1"), ("Disk.CrashDumpEnabled", "7")), "P25");
        Assert.Equal(ControlStatus.DeVerificat, bad.Status);
        Assert.Contains("fișierul de paginare NU se șterge", bad.Detail);
        Assert.Contains("hibernarea", bad.Detail);
        // one setting unreadable and no problem found among the others: not Conform
        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true, ("Disk.ClearPageFile", "1"), ("Disk.HibernateEnabled", ""), ("Disk.CrashDumpEnabled", "0")), "P25").Status);
    }

    [Fact]
    public void Logon_banner_shows_the_caption_and_missing_banner_is_Neconform()
    {
        var shown = Check(Station(true, ("LegalNotice.Caption", "ATENȚIE: SECRET DE SERVICIU"), ("LegalNotice.Text", "Acces permis doar personalului autorizat.")), "P26");
        Assert.Equal(ControlStatus.DeVerificat, shown.Status);
        Assert.Contains("ATENȚIE: SECRET DE SERVICIU", shown.Detail);
        Assert.Equal(ControlStatus.Neconform, Check(Station(true, ("LegalNotice.Caption", Absent), ("LegalNotice.Text", StationFactCollector.EmptyValue)), "P26").Status);
    }

    [Fact]
    public void Time_source_is_facts_only()
    {
        var c = Check(Station(true, ("Time.Type", "NoSync"), ("Time.NtpServer", Absent)), "P27");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("nu se sincronizează deloc", c.Detail);
        Assert.Contains("nu măsoară abaterea", c.Detail);
    }

    [Fact]
    public void Removable_media_audit_says_plainly_whether_copies_are_recorded()
    {
        var none = Station(true, ("Storage.HotPlugSecureOpen", Absent));
        none.AuditPolicy = new Dictionary<string, AuditSetting> { ["Removable Storage"] = AuditSetting.None };
        var bad = Check(none, "A08");
        Assert.Equal(ControlStatus.Neconform, bad.Status);
        Assert.Contains("NU notează ce fișiere se copiază", bad.Detail);

        var ok = Station(true, ("Storage.HotPlugSecureOpen", "1"));
        ok.AuditPolicy = new Dictionary<string, AuditSetting> { ["Removable Storage"] = AuditSetting.Success };
        Assert.Equal(ControlStatus.Conform, Check(ok, "A08").Status);

        Assert.Equal(ControlStatus.Nedeterminat, Check(Station(true), "A08").Status);
    }
}
