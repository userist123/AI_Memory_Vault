using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
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
        var approved = Assert.Single(ClassifiedAccess.Analyze(Input(recs, Register("plan.docx", "2026-10-14 09:00", serial: "HP-Birou", type: "imprimantă"))).Exits);
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

    // ---- investigation timeline (WP14 rules, production consumer: Wp14Analysis in the investigation pipeline) -------------------------------------

    private static TimelineEvent Row(int id, DateTimeOffset t, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(t.UtcDateTime, t.ToString("o"), "test"), Source = "EventLog:Security", EvidenceId = "EV-1", EventId = id.ToString(),
        Summary = $"Security {id}", Locator = $"RecordID={id}", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void The_timeline_rules_emit_unjustified_approved_access_and_audit_change_findings_with_evidence()
    {
        var profile = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = Root }] };
        var events = new List<TimelineEvent>
        {
            Row(4663, T0, ("SubjectUserName", "ion"), ("ObjectType", "File"), ("ObjectName", @"D:\Clasificat\plan.docx"), ("AccessMask", "0x1"), ("HandleId", "0x1"), ("ProcessId", "0x2"), ("ProcessName", "winword.exe")),
            Row(4663, T0.AddMinutes(1), ("SubjectUserName", "ion"), ("ObjectType", "File"), ("ObjectName", @"\Device\HarddiskVolume9\plan.docx"), ("AccessMask", "0x2"), ("HandleId", "0x3"), ("ProcessId", "0x2"), ("ProcessName", "explorer.exe")),
            Row(4907, T0.AddMinutes(2), ("SubjectUserName", "admin"), ("ObjectType", "File"), ("ObjectName", Root), ("ProcessName", "x.exe")),
        };
        int n = 0;
        var scope = new CaseScope { Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Classified };
        var f = Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input { Scope = scope, Profile = profile });
        var bad = Assert.Single(f, x => x.RuleId == "CLASSIFIED-EXPORT-UNJUSTIFIED");
        Assert.Equal(Severity.High, bad.Severity);
        Assert.NotEmpty(bad.SupportingEvidence);
        Assert.Single(f, x => x.RuleId == "CLASSIFIED-AUDIT-CHANGED");
        Assert.Contains(f, x => x.RuleId == "CLASSIFIED-ACCESS");
        Assert.All(f.Where(x => x.RuleId.StartsWith("CLASSIFIED-")), x => Assert.NotNull(RuleContracts.For(x.RuleId)));

        var reg = new LogAnalyzer.Dfir.Registers.TransferRegister { Rows = [new() { RegistrationNumber = "TR-1", DateTime = T0.ToString("yyyy-MM-dd HH:mm"), Direction = "ieșire", Content = "plan.docx", MediumSerial = "SN9", Approver = "sef" }] };
        var withMedium = events.Append(new TimelineEvent
        {
            Time = Timestamp.FromUtc(T0.AddMinutes(-5).UtcDateTime, "", "test"), Source = "EventLog:Microsoft-Windows-Partition/Diagnostic", EvidenceId = "EV-1", EventId = "1006", Summary = "1006", Locator = "RecordID=1",
            Fields = new(StringComparer.OrdinalIgnoreCase) { ["SerialNumber"] = "SN9", ["Capacity"] = "16000000000", ["BusType"] = "7", ["Model"] = "stick" },
        }).ToList();
        var ok = Wp14Rules.Run(withMedium, () => $"F-{++n:D4}", new Wp14Input { Scope = scope, Profile = profile, Transfers = reg, StationZone = TimeZoneInfo.Utc });
        Assert.Single(ok, x => x.RuleId == "CLASSIFIED-EXPORT-APPROVED");
        Assert.DoesNotContain(ok, x => x.RuleId == "CLASSIFIED-EXPORT-UNJUSTIFIED");
        // the case does not declare the station's time zone: the register's hand-typed time cannot be compared, so nothing is approved
        var noZone = Wp14Rules.Run(withMedium, () => $"F-{++n:D4}", new Wp14Input { Scope = scope, Profile = profile, Transfers = reg });
        Assert.DoesNotContain(noZone, x => x.RuleId == "CLASSIFIED-EXPORT-APPROVED");
        // without classified folders in the profile the rules say nothing (A10 of the station control reports the missing table)
        Assert.DoesNotContain(Wp14Rules.Run(events, () => "F", new Wp14Input { Scope = scope }), x => x.RuleId.StartsWith("CLASSIFIED-"));
    }

    // ---- independent review of PR #291: no false APPROVED (probes Q1-Q5), copies consumed, the application's own reads ------------------------

    private static readonly AccessRecord[] CopyToStick =
        [Access(4663, T0, @"D:\Clasificat\a.docx", "0x1"), Access(4663, T0.AddMinutes(1), @"E:\a.docx", "0x2", handle: "0x9")];
    private static readonly MediumSeen[] Stick = [new MediumSeen(T0.AddMinutes(-3), "SN1", "Kingston")];

    private static ClassifiedExit One(TransferRegister reg, AccessRecord[]? recs = null, MediumSeen[]? media = null, TimeZoneInfo? zone = null, bool noZone = false)
    {
        var input = Input(recs ?? CopyToStick, reg, media: media ?? Stick);
        input = new ClassifiedInput
        {
            Folders = input.Folders, Records = input.Records, Transfers = input.Transfers, Media = input.Media, RemovableDrives = input.RemovableDrives,
            RegisterZone = noZone ? null : zone ?? TimeZoneInfo.Utc,
        };
        return Assert.Single(ClassifiedAccess.Analyze(input).Exits);
    }

    [Fact]
    public void Q1_a_register_row_for_data_docx_does_not_approve_a_docx_names_match_exactly()
    {
        var e = One(Register("data.docx", "2026-10-14 09:01"));
        Assert.Equal(ExitVerdict.Unjustified, e.Verdict);
        Assert.Equal(ExitVerdict.Approved, One(Register(@"D:\Clasificat\A.DOCX; altul.pdf", "2026-10-14 09:01")).Verdict);   // exact name, case-insensitive, path ignored
    }

    [Fact]
    public void Q1_an_exact_sha256_in_the_register_approves_when_the_name_differs()
    {
        var hash = new string('a', 64);
        var input = new ClassifiedInput
        {
            Folders = [new ClassifiedRoot(Root)], Records = CopyToStick, Media = Stick, RemovableDrives = new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase), RegisterZone = TimeZoneInfo.Utc,
            FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"D:\Clasificat\a.docx"] = hash },
            Transfers = new TransferRegister { Rows = [new TransferRow { RegistrationNumber = "TR-9", DateTime = "2026-10-14 09:01", Direction = "ieșire", Content = "raport final", Sha256 = hash, MediumSerial = "SN1", Approver = "sef" }] },
        };
        Assert.Equal(ExitVerdict.Approved, Assert.Single(ClassifiedAccess.Analyze(input).Exits).Verdict);
        var partial = new TransferRegister { Rows = [new TransferRow { RegistrationNumber = "TR-9", DateTime = "2026-10-14 09:01", Direction = "ieșire", Content = "raport final", Sha256 = hash[..63] + "b", MediumSerial = "SN1", Approver = "sef" }] };
        Assert.Equal(ExitVerdict.Unjustified, Assert.Single(ClassifiedAccess.Analyze(new ClassifiedInput
        {
            Folders = input.Folders, Records = input.Records, Media = input.Media, RemovableDrives = input.RemovableDrives, RegisterZone = input.RegisterZone, FileHashes = input.FileHashes, Transfers = partial,
        }).Exits).Verdict);
    }

    [Fact]
    public void Q2_one_register_row_covers_its_declared_number_of_copies_the_rest_are_unjustified()
    {
        AccessRecord[] twice = [.. CopyToStick, Access(4663, T0.AddMinutes(40), @"E:\a.docx", "0x2", handle: "0x10")];
        var rep = ClassifiedAccess.Analyze(Input(twice, Register("a.docx", "2026-10-14 09:01"), media: Stick));
        var exits = rep.Exits.OrderBy(x => x.TimeUtc).ToList();
        Assert.Equal(2, exits.Count);
        Assert.Equal(ExitVerdict.Approved, exits[0].Verdict);
        Assert.Equal(ExitVerdict.Unjustified, exits[1].Verdict);
        Assert.Contains("deja", exits[1].Detail);

        var two = Register("a.docx", "2026-10-14 09:01");
        two.Rows[0].Copies = "2";
        Assert.All(ClassifiedAccess.Analyze(Input(twice, two, media: Stick)).Exits, x => Assert.Equal(ExitVerdict.Approved, x.Verdict));
    }

    [Fact]
    public void Q2_the_window_is_the_declared_time_plus_minus_two_hours()
    {
        Assert.Equal(ExitVerdict.Approved, One(Register("a.docx", "2026-10-14 10:55")).Verdict);
        var late = One(Register("a.docx", "2026-10-14 11:30"));
        Assert.Equal(ExitVerdict.Unjustified, late.Verdict);
        Assert.Contains("ora diferă", late.Detail);
        Assert.Equal(TimeSpan.FromHours(2), ClassifiedAccess.RegisterTolerance);
    }

    [Fact]
    public void Q3_a_row_without_a_medium_serial_does_not_soften_the_exit_an_exit_with_an_unknown_medium_is_only_to_verify()
    {
        var noSerial = One(Register("a.docx", "2026-10-14 09:01", serial: ""));
        Assert.Equal(ExitVerdict.Unjustified, noSerial.Verdict);
        Assert.Contains("există în registru o înregistrare incompletă pentru acest fișier", noSerial.Detail);
        var unknownMedium = One(Register("a.docx", "2026-10-14 09:01"), media: []);
        Assert.Equal(ExitVerdict.ToVerify, unknownMedium.Verdict);
        Assert.Contains("seria suportului", unknownMedium.Detail);
    }

    [Fact]
    public void Q4_a_row_without_an_approver_never_approves()
    {
        var reg = Register("a.docx", "2026-10-14 09:01");
        reg.Rows[0].Approver = "";
        var e = One(reg);
        Assert.Equal(ExitVerdict.Unjustified, e.Verdict);   // R1: whoever edits the register cannot neutralise A15 with an incomplete row
        Assert.Contains("există în registru o înregistrare incompletă pentru acest fișier", e.Detail);
        Assert.Contains("aprobarea", e.Detail);
        Assert.DoesNotContain("necompletat", e.Detail);
    }

    [Fact]
    public void Q5_an_exit_without_a_time_or_a_station_without_a_declared_zone_is_never_approved()
    {
        AccessRecord[] untimed = [Access(4663, T0, @"D:\Clasificat\a.docx", "0x1"),
            new("event", "Security", 4663, null, "Security 4663", new Dictionary<string, string> { ["SubjectUserName"] = "ion", ["ObjectType"] = "File", ["ObjectName"] = @"E:\a.docx", ["AccessMask"] = "0x2", ["HandleId"] = "0x9", ["ProcessId"] = "0x1" })];
        var all = ClassifiedAccess.Analyze(Input(untimed, Register("a.docx", "2026-10-14 09:01"), media: Stick)).Exits.ToList();
        var write = Assert.Single(all, x => x.Channel == ExitChannel.RemovableMedia);
        Assert.Equal(ExitVerdict.ToVerify, write.Verdict);
        Assert.Contains("ora ieșirii nu este cunoscută", write.Detail);
        Assert.DoesNotContain(all, x => x.Verdict == ExitVerdict.Approved);   // the temporal coincidence it also produces is never approved either
        var noZone = One(Register("a.docx", "2026-10-14 09:01"), noZone: true);
        Assert.Equal(ExitVerdict.ToVerify, noZone.Verdict);
        Assert.Contains("fusul orar", noZone.Detail);
    }

    [Fact]
    public void The_applications_own_hashing_reads_are_listed_apart_and_never_become_accesses_exits_or_coincidences()
    {
        const string image = @"C:\Program Files\LogAnalyzer\LogAnalyzer.exe";
        var window = new SelfActivityWindow(T0.AddMinutes(-1), T0.AddMinutes(5), 0x4d2, image, "STATIE\\inspector", 1);
        var own = Access(4663, T0, @"D:\Clasificat\a.docx", "0x1", proc: image) with { Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["SubjectUserName"] = "inspector", ["ObjectType"] = "File", ["ObjectName"] = @"D:\Clasificat\a.docx", ["AccessMask"] = "0x1", ["HandleId"] = "0x5", ["ProcessId"] = "0x4d2", ["ProcessName"] = image } };
        var otherPid = own with { Data = new Dictionary<string, string>(own.Data, StringComparer.OrdinalIgnoreCase) { ["ProcessId"] = "0x99" } };
        var input = new ClassifiedInput
        {
            Folders = [new ClassifiedRoot(Root)], Records = [own], Media = [new MediumSeen(T0.AddMinutes(10), "SN1", "stick")], SelfWindows = [window], RegisterZone = TimeZoneInfo.Utc,
        };
        var rep = ClassifiedAccess.Analyze(input);
        Assert.Empty(rep.Files);
        Assert.Empty(rep.Exits);
        var self = Assert.Single(rep.SelfActivity);
        Assert.Contains("activitate proprie a aplicației (calcul amprente)", self.Access);
        // the same image with another process id, or outside the window, is a real access
        Assert.Single(ClassifiedAccess.Analyze(new ClassifiedInput { Folders = input.Folders, Records = [otherPid], SelfWindows = [window], RegisterZone = TimeZoneInfo.Utc }).Files);
        Assert.Single(ClassifiedAccess.Analyze(new ClassifiedInput { Folders = input.Folders, Records = [own with { TimeUtc = T0.AddHours(1) }], SelfWindows = [window], RegisterZone = TimeZoneInfo.Utc }).Files);
    }

    [Fact]
    public void The_hashing_window_round_trips_through_the_audit_detail()
    {
        var w = new SelfActivityWindow(T0, T0.AddMinutes(3), 1234, @"C:\x\LogAnalyzer.exe", "STATIE\\inspector", 12, "run-1");
        var detail = LogAnalyzer.Dfir.Auth.OperationAudit.ControlHashingDetail(w);
        Assert.True(LogAnalyzer.Dfir.Auth.OperationAudit.TryParseControlHashing(detail, out var back));
        Assert.Equal(w with { Files = 0 }, back with { Files = 0 });
    }

    // ---- re-review of PR #291: R1, R2, R4, R5 -----------------------------------------------------------------------------------------------------

    [Fact]
    public void R4_a_register_time_in_the_skipped_dst_hour_does_not_crash_and_does_not_approve()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        var t = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);   // 03:30 local does not exist that night
        AccessRecord[] recs = [Access(4663, t, @"D:\Clasificat\a.docx", "0x1"), Access(4663, t.AddMinutes(1), @"E:\a.docx", "0x2", handle: "0x9")];
        var reg = Register("a.docx", "2026-03-29 03:30");
        var input = new ClassifiedInput
        {
            Folders = [new ClassifiedRoot(Root)], Records = recs, Transfers = reg, Media = [new MediumSeen(t.AddMinutes(-3), "SN1", "k")],
            RemovableDrives = new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase), RegisterZone = zone,
        };
        var e = Assert.Single(ClassifiedAccess.Analyze(input).Exits, x => x.Channel == ExitChannel.RemovableMedia);
        Assert.Equal(ExitVerdict.Unjustified, e.Verdict);
        Assert.Contains("oră inexistentă la schimbarea orei", e.Detail);
        // an ambiguous hour (autumn) still compares: either offset within the tolerance matches
        var amb = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero);   // 03:30 local, first occurrence (UTC+3)
        AccessRecord[] recs2 = [Access(4663, amb, @"D:\Clasificat\a.docx", "0x1"), Access(4663, amb.AddMinutes(1), @"E:\a.docx", "0x2", handle: "0x9")];
        var ok = Assert.Single(ClassifiedAccess.Analyze(new ClassifiedInput
        {
            Folders = input.Folders, Records = recs2, Transfers = Register("a.docx", "2026-10-25 03:30"), Media = [new MediumSeen(amb.AddMinutes(-3), "SN1", "k")],
            RemovableDrives = input.RemovableDrives, RegisterZone = zone,
        }).Exits, x => x.Channel == ExitChannel.RemovableMedia);
        Assert.Equal(ExitVerdict.Approved, ok.Verdict);
    }

    private static ClassifiedInput Hashed(TransferRegister reg, string? fileHash) => new()
    {
        Folders = [new ClassifiedRoot(Root)], Records = CopyToStick, Media = Stick, RemovableDrives = new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase), RegisterZone = TimeZoneInfo.Utc,
        Transfers = reg, FileHashes = fileHash is null ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"D:\Clasificat\a.docx"] = fileHash },
    };

    [Fact]
    public void R2_a_row_whose_sha256_differs_from_the_file_does_not_approve_and_an_unknown_file_hash_is_only_to_verify()
    {
        var reg = Register("a.docx", "2026-10-14 09:01");
        reg.Rows[0].Sha256 = new string('b', 64);
        var bad = Assert.Single(ClassifiedAccess.Analyze(Hashed(reg, new string('a', 64))).Exits);
        Assert.Equal(ExitVerdict.Unjustified, bad.Verdict);
        Assert.Contains("amprenta din registru nu corespunde fișierului", bad.Detail);
        Assert.Equal(ExitVerdict.ToVerify, Assert.Single(ClassifiedAccess.Analyze(Hashed(reg, null)).Exits).Verdict);
        // a row without a fingerprint may still approve by exact name
        Assert.Equal(ExitVerdict.Approved, Assert.Single(ClassifiedAccess.Analyze(Hashed(Register("a.docx", "2026-10-14 09:01"), new string('a', 64))).Exits).Verdict);
    }

    private const string Image = @"C:\Program Files\LogAnalyzer\LogAnalyzer.exe";

    private static AccessRecord Own(DateTimeOffset t, string obj, string mask, string handle = "0x5") =>
        new("event", "Security", 4663, t, "Security 4663 own", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["SubjectUserName"] = "inspector", ["ObjectType"] = "File", ["ObjectName"] = obj, ["AccessMask"] = mask, ["HandleId"] = handle, ["ProcessId"] = "0x4d2", ["ProcessName"] = Image });

    [Fact]
    public void R5_a_hashing_window_hides_only_reads_under_the_classified_folders_never_writes_or_removable_media()
    {
        var w = new SelfActivityWindow(T0.AddMinutes(-1), T0.AddMinutes(10), 0x4d2, Image, "inspector", 5, "run-1");
        var input = new ClassifiedInput
        {
            Folders = [new ClassifiedRoot(Root)], SelfWindows = [w], RegisterZone = TimeZoneInfo.Utc, RemovableDrives = new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase),
            Records = [Own(T0, @"D:\Clasificat\a.docx", "0x120089"), Own(T0.AddMinutes(1), @"D:\Clasificat\a.docx", "0x2", "0x6"), Own(T0.AddMinutes(2), @"E:\a.docx", "0x2", "0x7")],
        };
        var rep = ClassifiedAccess.Analyze(input);
        Assert.Single(rep.SelfActivity);                                       // only the read
        Assert.Contains(rep.Files.Single().Accesses, a => a.Access.Contains("scriere"));   // the write by the same process is a real access
        Assert.Contains(rep.Exits, x => x.Channel == ExitChannel.RemovableMedia);           // and the write to the stick a real exit
    }

    [Fact]
    public void R5_a_window_that_reads_more_files_than_it_hashed_is_invalid_and_hides_nothing()
    {
        var w = new SelfActivityWindow(T0.AddMinutes(-1), T0.AddMinutes(10), 0x4d2, Image, "inspector", 1, "run-1");
        var rep = ClassifiedAccess.Analyze(new ClassifiedInput
        {
            Folders = [new ClassifiedRoot(Root)], SelfWindows = [w], RegisterZone = TimeZoneInfo.Utc,
            Records = [Own(T0, @"D:\Clasificat\a.docx", "0x1"), Own(T0.AddMinutes(1), @"D:\Clasificat\b.docx", "0x1", "0x6")],
        });
        Assert.Empty(rep.SelfActivity);
        Assert.Equal(2, rep.Files.Count);
        Assert.Contains(rep.InvalidSelfWindows, x => x.Contains("fereastră de calcul amprente nevalidă"));
    }

    private static string AuditLine(string ev, DateTimeOffset when, string detail) =>
        System.Text.Json.JsonSerializer.Serialize(new { Event = ev, WhenUtc = when, Detail = detail });

    [Fact]
    public void R5_hashing_windows_are_accepted_only_inside_their_own_sealed_control()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "classified-audit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "audit.jsonl");
        var ok = new SelfActivityWindow(T0, T0.AddMinutes(3), 77, Image, "inspector", 2, "run-A");
        var outside = ok with { StartUtc = T0.AddHours(-5), EndUtc = T0.AddHours(-4.9), RunId = "run-A" };
        var tooLong = ok with { EndUtc = T0.AddHours(3), RunId = "run-B" };
        var noRun = ok with { RunId = "" };
        File.WriteAllLines(path,
        [
            AuditLine("control.started", T0.AddMinutes(-1), LogAnalyzer.Dfir.Auth.OperationAudit.ControlStartedDetail("Standalone", "Standalone", true, 30, 1, "run-A")),
            AuditLine("control.hashing", T0.AddMinutes(4), LogAnalyzer.Dfir.Auth.OperationAudit.ControlHashingDetail(ok)),
            AuditLine("control.hashing", T0.AddMinutes(4), LogAnalyzer.Dfir.Auth.OperationAudit.ControlHashingDetail(outside)),
            AuditLine("control.hashing", T0.AddMinutes(4), LogAnalyzer.Dfir.Auth.OperationAudit.ControlHashingDetail(noRun)),
            AuditLine("control.finished", T0.AddMinutes(5), LogAnalyzer.Dfir.Auth.OperationAudit.ControlFinishedDetail("WS", true, 1, 0, 0, 0, "run-A")),
            AuditLine("control.started", T0.AddMinutes(-1), LogAnalyzer.Dfir.Auth.OperationAudit.ControlStartedDetail("Standalone", "Standalone", true, 30, 1, "run-B")),
            AuditLine("control.hashing", T0.AddHours(3), LogAnalyzer.Dfir.Auth.OperationAudit.ControlHashingDetail(tooLong)),
            AuditLine("control.finished", T0.AddHours(3.1), LogAnalyzer.Dfir.Auth.OperationAudit.ControlFinishedDetail("WS", true, 1, 0, 0, 0, "run-B")),
        ]);
        var read = LogAnalyzer.Dfir.Auth.OperationAudit.ReadControlHashing(path, chainValid: true);
        Assert.Equal(ok, Assert.Single(read.Valid));
        Assert.Equal(3, read.Invalid.Count);
        Assert.All(read.Invalid, x => Assert.Contains("fereastră de calcul amprente nevalidă", x));
        var broken = LogAnalyzer.Dfir.Auth.OperationAudit.ReadControlHashing(path, chainValid: false);
        Assert.Empty(broken.Valid);
        Assert.Equal(4, broken.Invalid.Count);
        Directory.Delete(dir, true);
    }
}
