using System.Threading;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// WP14 usage of removable media: who used a medium (DIRECT / INFERRED / AMBIGUOUS / NOT_ATTRIBUTABLE) and which LNK files name it
/// (strong by volume serial, hint by drive letter, nothing outside a connection). Synthetic facts only; no real names or hives.
/// Lives in App.Tests because Dfir.Tests is not built in this worktree (Defender quarantine).
/// </summary>
public class MediaUsageAttributionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private const string Guid1 = "{11111111-2222-3333-4444-555555555555}";
    private const string Serial = "AA001";
    private static readonly Dictionary<string, string> Volumes = new(StringComparer.OrdinalIgnoreCase) { [Guid1] = Serial };

    private static MediumConnection Conn(double fromMin, double? toMin = null) =>
        new(T0.AddMinutes(fromMin), toMin is { } t ? T0.AddMinutes(t) : null);

    private static LogonSession Session(string account, string type, double fromMin, double? toMin = null) =>
        new(account, type, T0.AddMinutes(fromMin), toMin is { } t ? T0.AddMinutes(t) : null);

    // ---- A: who used the medium ----

    [Fact]
    public void One_profile_that_mounted_the_volume_is_DIRECT_and_says_it_is_not_proof_of_the_minute()
    {
        var mounts = new[] { new VolumeMount("profil-a", Guid1, T0.AddMinutes(5)) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, mounts, []));
        Assert.Equal(UsageStatus.Direct, r.Status);
        Assert.Equal(["profil-a"], r.Accounts);
        Assert.Contains("nu dovedește", r.Reason);
    }

    [Fact]
    public void Two_profiles_that_mounted_the_same_volume_are_AMBIGUOUS_and_both_are_listed()
    {
        var mounts = new[] { new VolumeMount("profil-a", Guid1, T0), new VolumeMount("profil-b", Guid1, T0.AddMinutes(9)) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, mounts, []));
        Assert.Equal(UsageStatus.Ambiguous, r.Status);
        Assert.Equal(["profil-a", "profil-b"], r.Accounts);
        Assert.Contains("Mai multe profile", r.Reason);
    }

    [Fact]
    public void A_mount_of_another_volume_does_not_count()
    {
        var mounts = new[] { new VolumeMount("profil-a", "{99999999-0000-0000-0000-000000000000}", T0) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, mounts, []));
        Assert.NotEqual(UsageStatus.Direct, r.Status);
        Assert.Empty(r.Accounts);
    }

    [Fact]
    public void Without_a_mount_one_open_interactive_session_is_INFERRED_not_DIRECT()
    {
        var sessions = new[] { Session("profil-b", "2", -10, 120) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, [], sessions));
        Assert.Equal(UsageStatus.Inferred, r.Status);
        Assert.Equal(["profil-b"], r.Accounts);
        Assert.Contains("presupunere", r.Reason);
    }

    [Fact]
    public void Service_and_network_logons_are_not_people_so_they_do_not_attribute()
    {
        var sessions = new[] { Session("svc", "5", -10, 120), Session("net", "3", -10, 120) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, [], sessions));
        Assert.Equal(UsageStatus.NotAttributable, r.Status);
        Assert.Empty(r.Accounts);
    }

    [Fact]
    public void Two_open_interactive_sessions_without_a_mount_are_AMBIGUOUS()
    {
        var sessions = new[] { Session("profil-a", "2", -10, 120), Session("profil-b", "10", -5, 120) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, [], sessions));
        Assert.Equal(UsageStatus.Ambiguous, r.Status);
        Assert.Equal(["profil-a", "profil-b"], r.Accounts);
    }

    [Fact]
    public void A_session_that_ended_before_the_connection_does_not_attribute()
    {
        var sessions = new[] { Session("profil-a", "2", -120, -30) };
        var r = Assert.Single(MediaUsageRules.Attribute(Serial, [Conn(0, 60)], Volumes, [], sessions));
        Assert.Equal(UsageStatus.NotAttributable, r.Status);
    }

    [Fact]
    public void Logon_and_logoff_records_pair_by_logon_id()
    {
        var recs = new[]
        {
            new LogonRecord("0x1", "profil-a", "2", T0, true),
            new LogonRecord("0x2", "profil-b", "10", T0.AddMinutes(1), true),
            new LogonRecord("0x1", "", "", T0.AddMinutes(30), false),
        };
        var s = MediaUsageRules.PairSessions(recs);
        Assert.Equal(2, s.Count);
        Assert.Equal("profil-a", s[0].Account);
        Assert.Equal(T0.AddMinutes(30), s[0].LogoffUtc);
        Assert.Null(s[1].LogoffUtc);   // no logoff seen: open-ended
    }

    // ---- B: files that name the medium ----

    private static LinkedFile<string> Lnk(string name, string path, string serial, string drive, double atMin) =>
        new(name, path, serial, drive, T0.AddMinutes(atMin));

    [Fact]
    public void LNK_naming_the_same_volume_serial_while_connected_is_a_strong_link()
    {
        var links = MediaUsageRules.Link(Serial, "E", [Conn(0, 60)], [Lnk("a", @"E:\raport.docx", Serial, "REMOVABLE", 30)]);
        var l = Assert.Single(links);
        Assert.Equal(FileLinkKind.OpenedFromMedium, l.Kind);
        Assert.Equal("a", l.Item);
        Assert.Contains("deschis de pe acest suport", l.Reason);
    }

    [Fact]
    public void Same_drive_letter_with_a_different_serial_is_a_hint_only()
    {
        var links = MediaUsageRules.Link(Serial, "E", [Conn(0, 60)], [Lnk("b", @"E:\alt.txt", "DEADBEEF", "REMOVABLE", 30)]);
        var l = Assert.Single(links);
        Assert.Equal(FileLinkKind.SameLetterHint, l.Kind);
        Assert.Contains("indiciu", l.Reason);
    }

    [Fact]
    public void Same_drive_letter_with_no_serial_in_the_link_is_still_only_a_hint()
    {
        var links = MediaUsageRules.Link(Serial, "E", [Conn(0, 60)], [Lnk("c", @"E:\x.pdf", "", "", 30)]);
        Assert.Equal(FileLinkKind.SameLetterHint, Assert.Single(links).Kind);
    }

    [Fact]
    public void A_link_written_after_the_disconnect_is_not_linked()
    {
        var links = MediaUsageRules.Link(Serial, "E", [Conn(0, 60)], [Lnk("d", @"E:\tarziu.docx", Serial, "REMOVABLE", 90)]);
        Assert.Empty(links);
    }

    [Fact]
    public void A_link_written_between_two_connections_is_not_linked_to_either()
    {
        // First connection removed at minute 40; the medium comes back at minute 100. Minute 50 is in the gap.
        var conns = new[] { Conn(0, 40), Conn(100) };
        var links = MediaUsageRules.Link(Serial, "E", conns, [Lnk("e", @"E:\y.txt", Serial, "REMOVABLE", 50)]);
        Assert.Empty(links);
    }

    [Fact]
    public void A_link_written_inside_a_connection_window_with_no_recorded_removal_is_linked_with_a_time_caveat()
    {
        var links = MediaUsageRules.Link(Serial, "E", [Conn(0)], [Lnk("f", @"E:\z.txt", Serial, "REMOVABLE", 500)]);
        var l = Assert.Single(links);
        Assert.Contains("Ora deconectării nu este cunoscută", l.Reason);
    }

    [Fact]
    public void Fixed_disk_links_are_never_linked_to_a_medium()
    {
        var links = MediaUsageRules.Link(Serial, "C", [Conn(0, 60)], [Lnk("g", @"C:\h.txt", Serial, "FIXED", 30)]);
        Assert.Empty(links);
    }

    // ---- pipeline: one MEDIA-USAGE finding per medium ----

    private static int _n;
    private static TimelineEvent Event(string source, double minute, string eventId = "", string evidence = "EV-USG", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute).UtcDateTime, "", "test"), Source = source, EventId = eventId, EvidenceId = evidence,
        Locator = $"row={Interlocked.Increment(ref _n)}", Summary = source + " " + eventId,
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Partition1006(double minute) =>
        Event("EventLog:Microsoft-Windows-Partition/Diagnostic", minute, "1006", f: [("SerialNumber", Serial), ("BusType", "7"), ("Capacity", "32000000000"), ("Manufacturer", "Kingston"), ("Model", "DT")]);

    private static TimelineEvent UsbRow(double minute, string letter = "E") =>
        Event("USB", minute, f: [("Serial", Serial), ("VidPid", "VID_0951&PID_1666"), ("FriendlyName", "Kingston DT"), ("DriveLetter", letter)]);

    private static TimelineEvent MountedVolume() =>
        Event("MountedVolume", 0, f: [("VolumeGuid", Guid1), ("Serial", Serial)]);

    private static TimelineEvent Mount(string account, double minute) =>
        Event("MountPoints2", minute, f: [("Account", account), ("VolumeGuid", Guid1)]);

    private static TimelineEvent Logon(string logonId, string user, string type, double minute, bool isLogon) =>
        Event("EventLog:Security", minute, isLogon ? "4624" : "4634", f: [("TargetLogonId", logonId), ("TargetUserName", user), ("LogonType", type)]);

    private static List<Finding> Run(params TimelineEvent[] events)
    {
        int n = 0;
        var input = new Wp14Input { Scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Unclassified } };
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", input).Where(f => f.RuleId == "MEDIA-USAGE").ToList();
    }

    [Fact]
    public void Pipeline_one_profile_gives_one_finding_naming_it()
    {
        var f = Assert.Single(Run(Partition1006(0), UsbRow(0), MountedVolume(), Mount("profil-a", 5)));
        Assert.Contains("profil identificat", f.Title);
        Assert.Equal("profil-a", f.User);
        Assert.NotEmpty(f.SupportingEvidence);
    }

    [Fact]
    public void Pipeline_two_profiles_is_ambiguous_and_names_both_never_one()
    {
        var f = Assert.Single(Run(Partition1006(0), UsbRow(0), MountedVolume(), Mount("profil-a", 5), Mount("profil-b", 9)));
        Assert.Contains("ambiguu", f.Title);
        Assert.Equal("profil-a, profil-b", f.User);
        Assert.Contains("„profil-a”", f.Description);
        Assert.Contains("„profil-b”", f.Description);
    }

    [Fact]
    public void Pipeline_without_mounts_uses_the_open_session_as_inference()
    {
        var f = Assert.Single(Run(Partition1006(0), UsbRow(0), Logon("0x7", "profil-c", "2", -5, true), Logon("0x7", "", "", 90, false)));
        Assert.Contains("doar o presupunere", f.Title);
        Assert.Equal("profil-c", f.User);
    }

    [Fact]
    public void Pipeline_without_any_data_says_not_attributable_and_does_not_invent_a_name()
    {
        var f = Assert.Single(Run(Partition1006(0), UsbRow(0)));
        Assert.Contains("nu se poate atribui", f.Title);
        Assert.Equal("", f.User);
    }

    [Fact]
    public void Pipeline_lnk_on_the_same_letter_is_reported_as_a_hint_and_the_missing_volume_serial_is_said()
    {
        var lnk = Event("LNK", 0, evidence: "EV-LNK", f: [("VolumeSerial", "DEADBEEF"), ("DriveType", "REMOVABLE"), ("LnkFileModifiedUtc", T0.AddMinutes(20).ToString("o"))]);
        lnk = new TimelineEvent { Time = lnk.Time, Source = "LNK", EvidenceId = "EV-LNK", Locator = "lnk", Summary = "lnk", Path = @"E:\dok.pdf", Fields = lnk.Fields };
        var f = Assert.Single(Run(Partition1006(0), UsbRow(0), lnk));
        Assert.Contains("doar aceeași literă (indiciu)", f.Description);
        Assert.Contains("nu este citită", f.Description);
        Assert.DoesNotContain("deschis de pe acest suport", f.Description);
    }

    // ---- medium detail (live): "Tot despre un dispozitiv" ----

    [Fact]
    public void Medium_detail_shows_who_used_it_per_connection_and_says_sessions_are_not_read_live()
    {
        var usage = MediaUsageRules.Attribute(Serial, [Conn(0)], Volumes, [new VolumeMount("profil-a", Guid1, null)], []);
        var device = new DeviceRecord { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\X\AA001", Serial = Serial, Usage = usage };
        var rows = DeviceDetails.For(new DeviceAssessment(device, RegisterMatch.NotRegistered, "", "v", "w"));
        var who = Assert.Single(rows, r => r.Label.StartsWith("Cine a folosit"));
        Assert.Equal("profil identificat: profil-a", who.Value);
        Assert.Single(rows, r => r.Label == "Sesiuni de logon");
    }

    [Fact]
    public void Medium_detail_without_usage_shows_no_attribution_rows()
    {
        var device = new DeviceRecord { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\X\AA001", Serial = Serial };
        var rows = DeviceDetails.For(new DeviceAssessment(device, RegisterMatch.NotRegistered, "", "v", "w"));
        Assert.DoesNotContain(rows, r => r.Label.StartsWith("Cine a folosit"));
    }
}
