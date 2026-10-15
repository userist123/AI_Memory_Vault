using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Classified information access and exits (owner request 2026-10-15): synthetic records shaped like the EVTX EventData of Security 4656 / 4658 / 4660 /
/// 4663 / 4670 / 4907 / 4688 / 5145 and PrintService 307. An exit is UNJUSTIFIED only on a record of the file going somewhere and no register entry;
/// a temporal coincidence is never more than "de verificat".
/// </summary>
public sealed class ClassifiedAccessTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private const string Root = @"D:\Clasificat";

    private static AccessRecord Sec(int id, DateTimeOffset t, params (string K, string V)[] data) =>
        new("event", "Security", id, t, $"Security {id} RecordID {id}{t:HHmmss}", data.ToDictionary(d => d.K, d => d.V, StringComparer.OrdinalIgnoreCase));

    private static AccessRecord Access(int id, DateTimeOffset t, string obj, string mask, string handle = "0x1a4", string user = "ion", string proc = @"C:\Windows\explorer.exe") =>
        Sec(id, t, ("SubjectUserName", user), ("SubjectDomainName", "STATIE"), ("ObjectType", "File"), ("ObjectName", obj), ("AccessMask", mask),
            ("HandleId", handle), ("ProcessId", "0x123"), ("ProcessName", proc));

    private static ClassifiedInput Input(IEnumerable<AccessRecord> recs, TransferRegister? reg = null, bool network = false, IEnumerable<MediumSeen>? media = null,
                                         IReadOnlyDictionary<string, string>? volumes = null, IEnumerable<ClassifiedRoot>? roots = null) => new()
    {
        Folders = (roots ?? [new ClassifiedRoot(Root)]).ToList(), Records = recs.ToList(), Transfers = reg, NetworkApplies = network,
        Media = (media ?? []).ToList(), RemovableDrives = new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase), VolumeMap = volumes, RegisterZone = TimeZoneInfo.Utc,
    };

    private static TransferRegister Register(string content, string when, string serial = "SN1", string type = "stick USB") => new()
    {
        Rows = [new TransferRow { RegistrationNumber = "TR-2026-0001", DateTime = when, Direction = "ieșire", Content = content, MediumSerial = serial, MediumType = type, Person = "ion", Approver = "sef" }],
    };

    [Fact]
    public void Paths_are_normalised_prefixes_volumes_case_and_slashes()
    {
        var map = new Dictionary<string, string> { [@"\Device\HarddiskVolume5"] = "E:", [@"Volume{11111111-2222-3333-4444-555555555555}"] = "D:" };
        Assert.Equal(@"D:\Clasificat\a.docx", ClassifiedAccess.Normalize(@"\\?\d:\Clasificat\\a.docx\"));
        Assert.Equal(@"D:\Clasificat\a.docx", ClassifiedAccess.Normalize(@"\??\D:/Clasificat/a.docx"));
        Assert.Equal(@"E:\x.docx", ClassifiedAccess.Normalize(@"\Device\HarddiskVolume5\x.docx", map));
        Assert.Equal(@"D:\Clasificat\a.docx", ClassifiedAccess.Normalize(@"\\?\Volume{11111111-2222-3333-4444-555555555555}\Clasificat\a.docx", map));
        Assert.Equal(@"\\srv\share\a", ClassifiedAccess.Normalize(@"\\?\UNC\srv\share\a"));
        Assert.True(ClassifiedAccess.IsUnder(@"D:\CLASIFICAT\a.docx", Root));
        Assert.False(ClassifiedAccess.IsUnder(@"D:\Clasificat2\a.docx", Root));
    }

    [Fact]
    public void Access_masks_are_decoded_from_the_hex_mask_or_the_access_list()
    {
        Assert.Equal(ClassifiedAccess.ReadData, ClassifiedAccess.Mask(Sec(4663, T0, ("AccessMask", "0x1"))));
        Assert.Equal(ClassifiedAccess.Delete | ClassifiedAccess.WriteData, ClassifiedAccess.Mask(Sec(4663, T0, ("AccessList", "%%1537 %%4417"))));
        Assert.Contains("ȘTERGERE", ClassifiedAccess.MaskText(0x10000));
        Assert.Contains("permisiuni", ClassifiedAccess.MaskText(0x40000));
    }

    [Fact]
    public void Accesses_are_listed_per_file_with_who_process_and_access_type_also_through_an_alias()
    {
        var rep = ClassifiedAccess.Analyze(Input([
            Access(4656, T0, @"D:\Clasificat\plan.docx", "0x1"),
            Access(4663, T0.AddSeconds(1), @"\\?\D:\Clasificat\plan.docx", "0x1"),
            Sec(4658, T0.AddMinutes(5), ("HandleId", "0x1a4"), ("ProcessId", "0x123")),
            Access(4663, T0.AddMinutes(6), @"C:\Junction\note.txt", "0x2", handle: "0x2"),
            Access(4663, T0.AddMinutes(7), @"D:\Altceva\x.txt", "0x1", handle: "0x3"),
        ], roots: [new ClassifiedRoot(Root, [@"C:\Junction"])]));
        var plan = Assert.Single(rep.Files, f => f.Path == @"D:\Clasificat\plan.docx");
        Assert.Equal(2, plan.Accesses.Count);
        Assert.All(plan.Accesses, a => Assert.Equal(@"STATIE\ion", a.Who));
        Assert.Contains(plan.Accesses, a => a.Access.Contains("citire") && a.Access.Contains("închis la"));
        Assert.Contains(rep.Files, f => f.Path == @"D:\Clasificat\note.txt");
        Assert.DoesNotContain(rep.Files, f => f.Path.Contains("Altceva"));
        Assert.Equal(3, rep.AuditRecords);
        Assert.Empty(rep.Exits);
    }

    [Fact]
    public void A_copy_to_removable_media_without_a_register_entry_is_unjustified_with_the_medium_and_the_chain()
    {
        var rep = ClassifiedAccess.Analyze(Input([
            Access(4663, T0, @"D:\Clasificat\plan.docx", "0x1"),
            Access(4663, T0.AddMinutes(1), @"E:\plan.docx", "0x2", handle: "0x9"),
        ], media: [new MediumSeen(T0.AddMinutes(-3), "SN1", "Kingston")]));
        var exit = Assert.Single(rep.Exits);
        Assert.Equal(ExitChannel.RemovableMedia, exit.Channel);
        Assert.Equal(ExitVerdict.Unjustified, exit.Verdict);
        Assert.Contains("SN1", exit.Target);
        Assert.True(exit.Chain.Count >= 3);   // the read, the write, the medium
        Assert.Contains("Nu există o intrare", exit.Detail);
        Assert.Equal(0, rep.Ordered[0].Rank);
    }

    [Fact]
    public void A_copy_covered_by_the_register_is_approved_and_a_row_with_another_medium_or_time_is_not()
    {
        AccessRecord[] recs = [Access(4663, T0, @"D:\Clasificat\plan.docx", "0x1"), Access(4663, T0.AddMinutes(1), @"E:\plan.docx", "0x2", handle: "0x9")];
        var media = new[] { new MediumSeen(T0.AddMinutes(-3), "SN1", "Kingston") };
        var ok = Assert.Single(ClassifiedAccess.Analyze(Input(recs, Register("plan.docx", "2026-10-14 09:05"), media: media)).Exits);
        Assert.Equal(ExitVerdict.Approved, ok.Verdict);
        Assert.Contains("TR-2026-0001", ok.RegisterEntry);

        var other = Assert.Single(ClassifiedAccess.Analyze(Input(recs, Register("plan.docx", "2026-10-14 09:05", serial: "ALT"), media: media)).Exits);
        Assert.Equal(ExitVerdict.Unjustified, other.Verdict);
        Assert.Contains("suportul diferă", other.Detail);
        var late = Assert.Single(ClassifiedAccess.Analyze(Input(recs, Register("plan.docx", "2026-10-20 09:05"), media: media)).Exits);
        Assert.Equal(ExitVerdict.Unjustified, late.Verdict);
        Assert.Contains("ora diferă", late.Detail);
    }

    [Fact]
    public void A_read_followed_by_a_medium_connection_is_only_a_temporal_coincidence_never_unjustified()
    {
        var rep = ClassifiedAccess.Analyze(Input([Access(4663, T0, @"D:\Clasificat\plan.docx", "0x1")], media: [new MediumSeen(T0.AddMinutes(30), "SN7", "stick")]));
        var exit = Assert.Single(rep.Exits);
        Assert.Equal(ExitChannel.TemporalCoincidence, exit.Channel);
        Assert.Equal(ExitVerdict.ToVerify, exit.Verdict);
        Assert.Contains(ClassifiedAccess.TemporalNote, exit.Detail);
        Assert.Equal(0, rep.Unjustified);
        // outside the window: nothing
        Assert.Empty(ClassifiedAccess.Analyze(Input([Access(4663, T0, @"D:\Clasificat\plan.docx", "0x1")], media: [new MediumSeen(T0.AddHours(5), "SN7", "stick")])).Exits);
    }

    [Fact]
    public void Printing_a_classified_document_is_an_exit_matched_against_a_print_entry()
    {
        var print = new AccessRecord("event", "Microsoft-Windows-PrintService/Operational", 307, T0.AddMinutes(2), "PrintService 307",
            new Dictionary<string, string> { ["Param2"] = "Microsoft Word - plan.docx", ["Param3"] = "ion", ["Param5"] = "HP-Birou", ["Param7"] = "1000", ["Param8"] = "3" });
        AccessRecord[] recs = [Access(4663, T0, @"D:\Clasificat\plan.docx", "0x1"), print];
        var exit = Assert.Single(ClassifiedAccess.Analyze(Input(recs)).Exits);
        Assert.Equal((ExitChannel.Print, ExitVerdict.Unjustified), (exit.Channel, exit.Verdict));
        var approved = Assert.Single(ClassifiedAccess.Analyze(Input(recs, Register("plan.docx", "2026-10-14 09:00", serial: "", type: "imprimantă"))).Exits);
        Assert.Equal(ExitVerdict.Approved, approved.Verdict);
        // a stick entry for the same file does not cover a print
        Assert.Equal(ExitVerdict.Unjustified, Assert.Single(ClassifiedAccess.Analyze(Input(recs, Register("plan.docx", "2026-10-14 09:00", serial: ""))).Exits).Verdict);
    }

    [Fact]
    public void Archiving_or_copying_the_classified_folder_to_a_removable_drive_is_unjustified_elsewhere_to_verify()
    {
        var zip = Sec(4688, T0, ("SubjectUserName", "ion"), ("NewProcessName", @"C:\Program Files\7-Zip\7z.exe"), ("CommandLine", @"7z.exe a E:\x.7z D:\Clasificat\*"));
        var e = Assert.Single(ClassifiedAccess.Analyze(Input([zip])).Exits);
        Assert.Equal((ExitChannel.Archive, ExitVerdict.Unjustified), (e.Channel, e.Verdict));
        var local = Sec(4688, T0, ("SubjectUserName", "ion"), ("NewProcessName", @"C:\Windows\System32\robocopy.exe"), ("CommandLine", @"robocopy D:\Clasificat C:\Temp"));
        var l = Assert.Single(ClassifiedAccess.Analyze(Input([local])).Exits);
        Assert.Equal((ExitChannel.CopyCommand, ExitVerdict.ToVerify), (l.Channel, l.Verdict));
        var unrelated = Sec(4688, T0, ("NewProcessName", @"C:\x\7z.exe"), ("CommandLine", @"7z.exe a E:\x.7z C:\Altceva"));
        Assert.Empty(ClassifiedAccess.Analyze(Input([unrelated])).Exits);
    }

    [Fact]
    public void Delete_with_4660_is_a_deletion_and_without_it_a_rename_or_move_to_verify_or_out_of_the_folder()
    {
        var deleted = ClassifiedAccess.Analyze(Input([
            Access(4663, T0, @"D:\Clasificat\plan.docx", "0x10000"),
            Sec(4660, T0.AddSeconds(1), ("HandleId", "0x1a4"), ("ProcessId", "0x123"), ("SubjectUserName", "ion")),
        ]));
        Assert.True(Assert.Single(deleted.Files).Deleted);
        Assert.Empty(deleted.Exits);

        var renamed = ClassifiedAccess.Analyze(Input([Access(4663, T0, @"D:\Clasificat\plan.docx", "0x10000")]));
        var r = Assert.Single(renamed.Exits);
        Assert.Equal((ExitChannel.MoveOrRename, ExitVerdict.ToVerify), (r.Channel, r.Verdict));

        var moved = ClassifiedAccess.Analyze(Input([Access(4663, T0, @"D:\Clasificat\plan.docx", "0x10000"), Access(4663, T0.AddSeconds(20), @"C:\Users\ion\Desktop\plan.docx", "0x2", handle: "0x5")]));
        var m = Assert.Single(moved.Exits);
        Assert.Equal((ExitChannel.MoveOrRename, ExitVerdict.Unjustified), (m.Channel, m.Verdict));
    }

    [Fact]
    public void Change_journal_rename_out_of_the_folder_or_to_another_extension_is_reported_as_weaker_evidence()
    {
        AccessRecord Usn(string path, string reason, int s) => new("USN", "", 0, T0.AddSeconds(s), "USN", new Dictionary<string, string> { ["FileId"] = "42", ["Reason"] = reason }, path);
        var rep = ClassifiedAccess.Analyze(Input([Usn(@"Clasificat\plan.docx", "RENAME_OLD_NAME", 0), Usn(@"Clasificat\poze.jpg", "RENAME_NEW_NAME", 1)]));
        var e = Assert.Single(rep.Exits);
        Assert.True(e.Weak);
        Assert.Contains("altă extensie", e.Detail);
    }

    [Fact]
    public void Permission_and_audit_rule_changes_on_classified_objects_are_listed()
    {
        var rep = ClassifiedAccess.Analyze(Input([Sec(4907, T0, ("SubjectUserName", "admin"), ("ObjectType", "File"), ("ObjectName", Root), ("ProcessName", "x.exe"))]));
        var c = Assert.Single(rep.SecurityChanges);
        Assert.True(c.AuditRules);
        Assert.Equal(Root, c.Object);
    }

    [Fact]
    public void Network_share_reads_are_evaluated_only_where_a_network_applies()
    {
        var smb = Sec(5145, T0, ("SubjectUserName", "ana"), ("ShareName", @"\\*\Clasificat"), ("ShareLocalPath", @"\??\D:\Clasificat"), ("RelativeTargetName", "plan.docx"), ("AccessMask", "0x1"), ("IpAddress", "10.0.0.5"));
        Assert.Empty(ClassifiedAccess.Analyze(Input([smb], network: false)).Exits);
        var e = Assert.Single(ClassifiedAccess.Analyze(Input([smb], network: true)).Exits);
        Assert.Equal((ExitChannel.NetworkShare, ExitVerdict.Unjustified), (e.Channel, e.Verdict));
    }

    [Fact]
    public void Shortcuts_are_weaker_evidence_and_a_classified_name_opened_from_a_medium_is_to_verify()
    {
        AccessRecord Lnk(string path, string drive) => new("LNK", "", 0, T0, "LNK x.lnk", new Dictionary<string, string> { ["DriveType"] = drive, ["VolumeSerial"] = "ABCD-1234" }, path);
        var rep = ClassifiedAccess.Analyze(Input([Lnk(@"D:\Clasificat\plan.docx", "FIXED"), Lnk(@"F:\plan.docx", "REMOVABLE")]));
        var f = Assert.Single(rep.Files);
        Assert.True(Assert.Single(f.Accesses).Weak);
        var e = Assert.Single(rep.Exits);
        Assert.Equal((ExitChannel.OpenedFromMedium, ExitVerdict.ToVerify), (e.Channel, e.Verdict));
        Assert.Contains("ABCD-1234", e.Target);
    }

    [Fact]
    public void No_folders_or_no_audit_records_are_said_never_reported_as_clean()
    {
        var none = ClassifiedAccess.Analyze(new ClassifiedInput());
        Assert.Contains(none.Notes, n => n.Contains("nu definește foldere"));
        var empty = ClassifiedAccess.Analyze(Input([]));
        Assert.Contains(empty.Notes, n => n.Contains("Windows nu înregistrează retroactiv"));
        Assert.Contains(empty.Notes, n => n.Contains("Registrul transferurilor"));
    }
}
