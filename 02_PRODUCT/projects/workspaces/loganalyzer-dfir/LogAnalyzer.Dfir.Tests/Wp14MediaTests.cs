using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP14a items 4 and 5: observed media against the register (AUTHORIZED / REGISTERED / UNREGISTERED / UNAUTHORIZED / UNKNOWN), USB completeness, CD/DVD as a separate artifact.</summary>
public class Wp14MediaTests
{
    private static Finding Only(List<Finding> f, string rule) => Assert.Single(f, x => x.RuleId == rule);
    private static List<Finding> Media(List<Finding> f) => f.Where(x => x.RuleId.StartsWith("MEDIA-", StringComparison.Ordinal)).ToList();

    // ---- the five statuses ----

    [Fact]
    public void Authorized_means_registered_active_valid_at_the_observed_time_and_nothing_to_contradict()
    {
        var f = Go([Usb("AA001")], media: W14.Media(Row("AA001")));
        var x = Only(f, "MEDIA-AUTHORIZED");
        Assert.Equal(Severity.Info, x.Severity);
        Assert.Equal(AirGapAuthorization.Authorized, x.AirGap!.Authorized);
        Assert.Contains("M-AA001", x.Description);
    }

    [Fact]
    public void Authorized_also_needs_the_assigned_user_and_zone_to_match_when_the_register_names_them()
    {
        var m = W14.Media(Row("AA001", user: @"CORP\ion", zone: "ROSU"));
        var ion = Sec(6416, 0, ("DeviceId", @"USBSTOR\DISK&VEN_KINGSTON&PROD_DT&REV_1\AA001&0"), ("ClassName", "DiskDrive"), ("SubjectUserName", "ion"), ("SubjectDomainName", "CORP"));
        Assert.Single(Go([Usb("AA001"), ion], media: m, zone: "ROSU"), x => x.RuleId == "MEDIA-AUTHORIZED");
        // right user, wrong zone
        var wrongZone = Go([Usb("AA001"), ion], media: m, zone: "PUBLIC");
        Assert.Single(wrongZone, x => x.RuleId == "MEDIA-REGISTERED" && x.Description.Contains("zona"));
        // zone not stated by the operator: cannot be confirmed
        var noZone = Go([Usb("AA001"), ion], media: m);
        Assert.Single(noZone, x => x.RuleId == "MEDIA-REGISTERED" && x.Description.Contains("zona sistemului nu este declarată"));
        // another user
        var maria = Sec(6416, 0, ("DeviceId", @"USBSTOR\DISK&VEN_KINGSTON&PROD_DT&REV_1\AA001&0"), ("ClassName", "DiskDrive"), ("SubjectUserName", "maria"), ("SubjectDomainName", "CORP"));
        Assert.Single(Go([Usb("AA001"), maria], media: m, zone: "ROSU"), x => x.RuleId == "MEDIA-REGISTERED" && x.Description.Contains("utilizator"));
    }

    [Fact]
    public void Registered_when_the_user_who_used_the_medium_cannot_be_established_and_the_register_assigns_one()
    {
        var x = Only(Go([Usb("AA001")], media: W14.Media(Row("AA001", user: @"CORP\ion"))), "MEDIA-REGISTERED");
        Assert.Contains("utilizatorul care a folosit mediul nu a putut fi stabilit", x.Description);
        Assert.Equal(AirGapAuthorization.NotAuthorized, x.AirGap!.Authorized);
    }

    [Fact]
    public void Registered_when_the_vid_pid_differs_from_the_register()
    {
        var x = Only(Go([Usb("AA001", vidpid: "VID_AAAA&PID_BBBB")], media: W14.Media(Row("AA001", vidpid: "0951:1666"))), "MEDIA-REGISTERED");
        Assert.Contains("VID/PID", x.Description);
    }

    [Fact]
    public void Unregistered_when_the_serial_is_not_in_a_defined_register_Medium_on_a_classified_scope_Low_otherwise()
    {
        var m = W14.Media(Row("OTHER"));
        Assert.Equal(Severity.Medium, Only(Go([Usb("AA001")], Classified(), m), "MEDIA-UNREGISTERED").Severity);
        Assert.Equal(Severity.Low, Only(Go([Usb("AA001")], Unclassified(), m), "MEDIA-UNREGISTERED").Severity);
    }

    [Theory]
    [InlineData("withdrawn")]
    [InlineData("destroyed")]
    public void Unauthorized_when_the_medium_is_withdrawn_or_destroyed_High_on_classified_Medium_otherwise(string status)
    {
        var m = W14.Media(Row("AA001", status: status));
        var hi = Only(Go([Usb("AA001")], Classified(), m), "MEDIA-UNAUTHORIZED");
        Assert.Equal(Severity.High, hi.Severity);
        Assert.Contains(status == "withdrawn" ? "retras" : "distrus", hi.Description);
        Assert.Equal(Severity.Medium, Only(Go([Usb("AA001")], Unclassified(), m), "MEDIA-UNAUTHORIZED").Severity);
    }

    [Fact]
    public void Unauthorized_when_an_observation_is_outside_the_validity_period()
    {
        var m = W14.Media(Row("AA001", from: "2026-01-01", to: "2026-09-30"));     // observed 2026-10-01
        var x = Only(Go([Usb("AA001")], media: m), "MEDIA-UNAUTHORIZED");
        Assert.Contains("valabilit", x.Description);
        var before = W14.Media(Row("AA001", from: "2026-10-02", to: "2026-12-31"));
        Assert.Single(Go([Usb("AA001")], media: before), y => y.RuleId == "MEDIA-UNAUTHORIZED");
    }

    [Fact]
    public void A_validity_period_with_no_observation_time_cannot_be_confirmed_so_the_medium_is_registered_not_authorized()
    {
        var undated = new TimelineEvent
        {
            Time = Timestamp.Unknown(), Source = "USB", EvidenceId = "EV-2", Locator = "row=900", Summary = "t",
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Serial"] = "AA001", ["VidPid"] = "", ["FriendlyName"] = "x" },
        };
        var x = Only(Go([undated], media: W14.Media(Row("AA001"))), "MEDIA-REGISTERED");
        Assert.Contains("ora observării este necunoscută", x.Description);
    }

    [Fact]
    public void Unknown_when_the_observation_has_no_serial()
    {
        var x = Only(Go([Usb("")], media: W14.Media(Row("AA001"))), "MEDIA-UNKNOWN");
        Assert.Contains("fără serie", x.Description);
        Assert.Equal(AirGapAuthorization.Undefined, x.AirGap!.Authorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_register_or_an_empty_register_is_registru_nedefinit_and_never_conform_or_authorized(bool emptyRegister)
    {
        var f = Go([Usb("AA001")], media: emptyRegister ? new LogAnalyzer.Dfir.Registers.MediaRegister() : null);
        var x = Only(f, "MEDIA-UNKNOWN");
        Assert.Contains("registru nedefinit", x.Description);
        Assert.DoesNotContain(f, y => y.RuleId is "MEDIA-AUTHORIZED" or "MEDIA-UNREGISTERED" or "MEDIA-UNAUTHORIZED");
        Assert.DoesNotContain("conform", x.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AirGapAuthorization.Undefined, x.AirGap!.Authorized);
    }

    [Fact]
    public void Insufficient_clearance_of_the_assigned_user_makes_the_medium_registered_not_authorized()
    {
        var m = W14.Media(Row("AA001", user: @"CORP\ion", cls: "NATO SECRET"));
        var ion = Sec(6416, 0, ("DeviceId", @"USBSTOR\DISK&VEN_K&PROD_D&REV_1\AA001&0"), ("ClassName", "DiskDrive"), ("SubjectUserName", "ion"), ("SubjectDomainName", "CORP"));
        // user's clearance is SECRET (national, equivalent to NATO CONFIDENTIAL), the medium is NATO SECRET
        var low = Users(["Ion", @"CORP\ion", "", "SECRET", "2026-01-01", "2027-01-01", "", ""]);
        var x = Only(Go([Usb("AA001"), ion], media: m, users: low), "MEDIA-REGISTERED");
        Assert.Contains("abilitare", x.Description);
        // STRICT SECRET is equivalent to NATO SECRET: enough
        var enough = Users(["Ion", @"CORP\ion", "", "STRICT SECRET", "2026-01-01", "2027-01-01", "", ""]);
        Assert.Single(Go([Usb("AA001"), ion], media: m, users: enough), y => y.RuleId == "MEDIA-AUTHORIZED");
        // an expired clearance does not count
        var expired = Users(["Ion", @"CORP\ion", "", "STRICT SECRET", "2020-01-01", "2021-01-01", "", ""]);
        Assert.Single(Go([Usb("AA001"), ion], media: m, users: expired), y => y.RuleId == "MEDIA-REGISTERED" && y.Description.Contains("abilitare"));
    }

    // ---- USB completeness ----

    [Fact]
    public void One_device_seen_by_several_sources_is_one_medium_with_first_and_last_connection()
    {
        var events = new List<TimelineEvent>
        {
            Usb("AA001", 0),
            Ev("Microsoft-Windows-Partition/Diagnostic", 1006, 10, "Microsoft-Windows-Partition", ("Capacity", "32000000000"), ("BusType", "7"), ("Manufacturer", "Kingston"), ("Model", "DT"), ("SerialNumber", "AA001")),
            Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 20, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"USBSTOR\DISK&VEN_KINGSTON&PROD_DT&REV_1\AA001&0")),
            Ev("Microsoft-Windows-DriverFrameworks-UserMode/Operational", 2100, 30, "Microsoft-Windows-DriverFrameworks-UserMode", ("InstanceId", @"USBSTOR\DISK&VEN_KINGSTON&PROD_DT&REV_1\AA001&0")),
            Ev("Microsoft-Windows-DriverFrameworks-UserMode/Operational", 2102, 45, "Microsoft-Windows-DriverFrameworks-UserMode", ("InstanceId", @"USBSTOR\DISK&VEN_KINGSTON&PROD_DT&REV_1\AA001&0")),
        };
        var x = Only(Go(events, media: W14.Media(Row("AA001"))), "MEDIA-AUTHORIZED");
        Assert.Equal(T0.AddMinutes(0), x.FirstSeenUtc!.Value.UtcDateTime);
        Assert.Equal(T0.AddMinutes(45), x.LastSeenUtc!.Value.UtcDateTime);
        Assert.Equal(5, x.SupportingEvidence.Count);
        Assert.Contains("Partition/Diagnostic", x.AirGap!.Evidence.Aggregate("", (a, b) => a + "|" + b));
    }

    [Fact]
    public void Internal_disks_in_Partition_Diagnostic_are_not_removable_media()
    {
        var internalDisk = Ev("Microsoft-Windows-Partition/Diagnostic", 1006, 1, "Microsoft-Windows-Partition", ("Capacity", "1000000000000"), ("BusType", "17"), ("Model", "NVMe"), ("SerialNumber", "NVME1"));
        Assert.Empty(Media(Go([internalDisk], media: W14.Media(Row("NVME1")))));
    }

    [Fact]
    public void Usbstor_instance_suffix_does_not_split_a_device_in_two()
    {
        var f = Go([Usb("AA001&0", 0), Usb("AA001", 1)], media: W14.Media(Row("AA001")));
        Assert.Single(Media(f));
    }

    // ---- presence is not copying ----

    [Fact]
    public void Without_write_evidence_the_finding_says_presence_not_copy_and_there_is_no_file_activity_finding()
    {
        var f = Go([Usb("AA001", letter: "E:")], media: W14.Media(Row("AA001")));
        Assert.Contains("prezență, nu copiere", Only(f, "MEDIA-AUTHORIZED").Description);
        Assert.DoesNotContain(f, x => x.RuleId == "MEDIA-FILE-ACTIVITY");
    }

    [Fact]
    public void A_write_access_event_on_the_drive_letter_is_claimed_as_write_evidence_and_nothing_stronger()
    {
        var w = Sec(4663, 5, ("ObjectName", @"E:\doc\plan.docx"), ("AccessMask", "0x2"), ("SubjectUserName", "ion"), ("ProcessName", @"C:\Windows\explorer.exe"));
        var f = Go([Usb("AA001", letter: "E:"), w], media: W14.Media(Row("AA001")));
        var a = Only(f, "MEDIA-FILE-ACTIVITY");
        Assert.Equal(Severity.Medium, a.Severity);
        Assert.Contains("scriere", a.Description);
        Assert.Contains("litera de unitate", string.Join(" ", a.Limitations.Concat(a.AlternativeExplanations)) + a.Description);
        Assert.DoesNotContain("prezență, nu copiere", Only(f, "MEDIA-AUTHORIZED").Description);
    }

    [Fact]
    public void An_unregistered_medium_with_write_evidence_on_a_classified_scope_is_High()
    {
        var w = Sec(4663, 5, ("ObjectName", @"E:\a.txt"), ("AccessMask", "0x2"));
        Assert.Equal(Severity.High, Only(Go([Usb("ZZ", letter: "E:"), w], Classified(), W14.Media(Row("AA001"))), "MEDIA-FILE-ACTIVITY").Severity);
    }

    [Fact]
    public void An_lnk_target_on_the_drive_is_access_evidence_Low_and_never_called_a_copy()
    {
        var lnk = W11.Row("LNK", 6, path: @"E:\doc\plan.docx", f: [("DriveType", "REMOVABLE")]);
        var a = Only(Go([Usb("AA001", letter: "E:"), lnk], media: W14.Media(Row("AA001"))), "MEDIA-FILE-ACTIVITY");
        Assert.Equal(Severity.Low, a.Severity);
        Assert.Contains("nu arată scriere", a.Description);
        Assert.DoesNotContain("copiat", a.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Usn_create_on_the_drive_is_write_evidence_and_a_read_only_4663_is_not()
    {
        var usn = W11.Row("USN", 7, path: @"E:\new.docx", f: [("Reason", "FILE_CREATE|CLOSE"), ("FileName", "new.docx")]);
        Assert.Contains("scriere", Only(Go([Usb("AA001", letter: "E:"), usn], media: W14.Media(Row("AA001"))), "MEDIA-FILE-ACTIVITY").Description);
        var read = Sec(4663, 5, ("ObjectName", @"E:\a.txt"), ("AccessMask", "0x1"));
        var a = Only(Go([Usb("AA001", letter: "E:"), read], media: W14.Media(Row("AA001"))), "MEDIA-FILE-ACTIVITY");
        Assert.Equal(Severity.Low, a.Severity);
    }

    [Fact]
    public void Activity_on_another_drive_letter_is_not_attributed_to_the_medium()
    {
        var w = Sec(4663, 5, ("ObjectName", @"F:\a.txt"), ("AccessMask", "0x2"));
        Assert.DoesNotContain(Go([Usb("AA001", letter: "E:"), w], media: W14.Media(Row("AA001"))), x => x.RuleId == "MEDIA-FILE-ACTIVITY");
    }

    // ---- CD/DVD is a separate artifact ----

    [Fact]
    public void Cd_dvd_activity_is_its_own_finding_with_the_optical_subcategory_not_a_usb_one()
    {
        var events = new List<TimelineEvent>
        {
            Ev("Application", 1, 1, "IMAPI2"),
            W11.Row("LNK", 2, path: @"D:\setup.exe", f: [("DriveType", "CDROM")]),
            W11.Row("Prefetch", 3, "IMGBURN.EXE", @"C:\Program Files\ImgBurn\ImgBurn.exe", f: [("RunCount", "1")]),
        };
        var f = Go(events, media: W14.Media(Row("AA001")));
        var o = Only(f, "MEDIA-OPTICAL-ACTIVITY");
        Assert.Equal("OPTICAL MEDIA", o.AirGap!.Subcategory);
        Assert.Contains("IMAPI", o.Description);
        Assert.Contains("ImgBurn", o.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(f, x => x.RuleId is "MEDIA-AUTHORIZED" or "MEDIA-UNREGISTERED" or "MEDIA-UNKNOWN" or "MEDIA-REGISTERED" or "MEDIA-UNAUTHORIZED");
        Assert.Equal(Severity.Medium, o.Severity);     // classified scope + burn evidence
    }

    [Fact]
    public void An_optical_drive_or_a_mounted_iso_alone_is_presence_not_burning()
    {
        var events = new List<TimelineEvent>
        {
            Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 1, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"SCSI\CDROM&VEN_HL-DT-ST&PROD_DVDRAM_GH24NSD1\4&1&0")),
            Ev("Microsoft-Windows-VHDMP-Operational", 1, 2, "Microsoft-Windows-VHDMP", ("Path", @"C:\Users\ion\Downloads\setup.iso")),
        };
        var o = Only(Go(events), "MEDIA-OPTICAL-ACTIVITY");
        Assert.Equal(Severity.Low, o.Severity);
        Assert.Contains("prezență, nu inscripționare", o.Description);
        Assert.Contains(".iso", o.Description);
    }

    [Fact]
    public void Usb_events_never_create_an_optical_finding()
    {
        Assert.DoesNotContain(Go([Usb("AA001", letter: "E:")], media: W14.Media(Row("AA001"))), x => x.RuleId == "MEDIA-OPTICAL-ACTIVITY");
    }

    // ---- category and carried fields ----

    [Fact]
    public void Media_findings_on_an_air_gapped_scope_are_in_the_air_gap_integrity_category_with_every_field()
    {
        var x = Only(Go([Usb("AA001")], Classified(), W14.Media(Row("AA001", cls: "NATO SECRET"))), "MEDIA-AUTHORIZED");
        Assert.Equal("Air-gap integrity", x.Category);
        var d = x.AirGap!;
        Assert.Equal("USB", d.Subcategory);
        Assert.Contains("USB", d.Channel);
        Assert.Contains("AA001", d.ObjectName);
        Assert.Contains("clasificat", d.CaseClassification);
        Assert.Contains("NATO SECRET", d.RegisterClassification);
        Assert.NotEmpty(d.TransferDirection); Assert.NotEmpty(d.Destination); Assert.NotEmpty(d.Evidence); Assert.NotEmpty(d.Observed); Assert.NotEmpty(d.Who);
        Assert.Contains(d.Subcategory, Wp14Data.Default.Subcategories);
    }

    [Fact]
    public void On_a_connected_scope_the_category_is_not_air_gap_integrity_but_the_media_register_still_applies()
    {
        var x = Only(Go([Usb("AA001")], Classified(NetworkCategory.Connected), W14.Media(Row("OTHER"))), "MEDIA-UNREGISTERED");
        Assert.Equal("Removable media", x.Category);
    }

    [Fact]
    public void The_subcategory_list_has_the_nineteen_entries_of_lessons_learned_row_50()
    {
        Assert.Equal(19, Wp14Data.Default.Subcategories.Count);
        Assert.Contains("OPTICAL MEDIA", Wp14Data.Default.Subcategories);
        Assert.Contains("PRINT/SCAN", Wp14Data.Default.Subcategories);
    }
}
