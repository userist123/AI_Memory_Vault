using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.HashSets;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner 2026-11-06, wave 2: the classified-information, user-activity, cleanup, SMB, DNS, VSS, remote-access, correlation, policy and hash-set findings told
/// in plain words (<see cref="Finding.PlainWhat"/>). Built from the data each rule has; no event codes, rule ids, SIDs, technique ids or "UTC" in the sentence.
/// Names, accounts and addresses are invented (LAB-WS01, ion.popescu, 10.0.0.x).
/// </summary>
public class PlainWhatRulesWave2Tests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
    private static readonly Regex Jargon = new(@"\b(?:\d{4}|T1\d{3}|UTC)\b|S-1-5-|[A-Z]{2,}-[A-Z]{2,}", RegexOptions.CultureInvariant);

    /// <summary>PlainWhat is not empty, carries the key facts, and the whole sentence has no codes (the year of the date is allowed).</summary>
    private static string Plain(Finding f, params string[] facts)
    {
        Assert.NotEmpty(f.PlainWhat);
        foreach (var fact in facts) Assert.Contains(fact, f.PlainWhat);
        Assert.DoesNotMatch(Jargon, f.PlainWhat);
        var story = PlainStory.Tell(f, null, Zone);
        Assert.DoesNotMatch(Jargon, Regex.Replace(story, @"\b20\d\d\b", ""));
        Assert.DoesNotContain("vinovat", story, StringComparison.OrdinalIgnoreCase);
        return story;
    }

    private static Finding One(IEnumerable<Finding> all, string rule, Func<Finding, bool>? where = null) => Assert.Single(all, f => f.RuleId == rule && (where?.Invoke(f) ?? true));

    // ================================================================ classified information (Wp14Rules.Classified)

    private static readonly DateTimeOffset C0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private const string Root = @"D:\Clasificat";

    private static TimelineEvent Sec4(int id, DateTimeOffset t, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(t.UtcDateTime, t.ToString("o"), "test"), Source = "EventLog:Security", EvidenceId = "EV-1", EventId = id.ToString(),
        Summary = $"Security {id}", Locator = $"RecordID={id}", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static List<Finding> Classified(List<TimelineEvent> events, TransferRegister? reg = null)
    {
        int n = 0;
        var profile = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = Root }] };
        var scope = new CaseScope { Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Classified };
        return Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input { Scope = scope, Profile = profile, Transfers = reg, StationZone = TimeZoneInfo.Utc });
    }

    private static List<TimelineEvent> CopyToStick(bool withMedium)
    {
        var l = new List<TimelineEvent>
        {
            Sec4(4663, C0, ("SubjectUserName", "ion.popescu"), ("ObjectType", "File"), ("ObjectName", @"D:\Clasificat\plan.docx"), ("AccessMask", "0x1"), ("HandleId", "0x1"), ("ProcessId", "0x2"), ("ProcessName", "winword.exe")),
            Sec4(4663, C0.AddMinutes(1), ("SubjectUserName", "ion.popescu"), ("ObjectType", "File"), ("ObjectName", @"\Device\HarddiskVolume9\plan.docx"), ("AccessMask", "0x2"), ("HandleId", "0x3"), ("ProcessId", "0x2"), ("ProcessName", "explorer.exe")),
        };
        if (withMedium)
            l.Add(new TimelineEvent
            {
                Time = Timestamp.FromUtc(C0.AddMinutes(-5).UtcDateTime, "", "test"), Source = "EventLog:Microsoft-Windows-Partition/Diagnostic", EvidenceId = "EV-1", EventId = "1006", Summary = "1006", Locator = "RecordID=1",
                Fields = new(StringComparer.OrdinalIgnoreCase) { ["SerialNumber"] = "SN9", ["Capacity"] = "16000000000", ["BusType"] = "7", ["Model"] = "stick" },
            });
        return l;
    }

    [Fact]
    public void CLASSIFIED_EXPORT_UNJUSTIFIED_names_the_document_the_medium_and_that_the_register_has_no_entry()
    {
        var f = One(Classified(CopyToStick(false)), "CLASSIFIED-EXPORT-UNJUSTIFIED");
        var story = Plain(f, "plan.docx", "registrul transferurilor nu are nicio intrare");
        Assert.StartsWith("a ", f.PlainWhat);
        Assert.Equal("Pe 14 octombrie 2026, la ora 12:01, utilizatorul cu contul „ion.popescu” a scris pe un volum neidentificat (posibil un suport amovibil) un fișier cu același nume ca fișierul clasificat „plan.docx”; registrul transferurilor nu are nicio intrare care să acopere această posibilă ieșire.", story);
    }

    [Fact]
    public void CLASSIFIED_EXPORT_APPROVED_says_the_register_entry_covers_the_exit()
    {
        var reg = new TransferRegister { Rows = [new() { RegistrationNumber = "TR-1", DateTime = C0.ToString("yyyy-MM-dd HH:mm"), Direction = "ieșire", Content = "plan.docx", MediumSerial = "SN9", Approver = "sef" }] };
        var f = One(Classified(CopyToStick(true), reg), "CLASSIFIED-EXPORT-APPROVED");
        Plain(f, "plan.docx", "corespunde unei intrări din registrul transferurilor", "TR-1");
    }

    [Fact]
    public void CLASSIFIED_EXPORT_TO_VERIFY_says_to_check_the_register()
    {
        // a classified name opened from a removable medium: a weak trace, to be verified
        TimelineEvent Lnk(string path, string drive) => new()
        {
            Time = Timestamp.FromUtc(C0.UtcDateTime, "", "test"), Source = "LNK", EvidenceId = "EV-2", Summary = "lnk", Locator = "x.lnk", Path = path,
            Fields = new(StringComparer.OrdinalIgnoreCase) { ["DriveType"] = drive, ["VolumeSerial"] = "ABCD-1234" },
        };
        var f = One(Classified([Lnk(@"D:\Clasificat\plan.docx", "FIXED"), Lnk(@"F:\plan.docx", "REMOVABLE")]), "CLASSIFIED-EXPORT-TO-VERIFY");
        Plain(f, "plan.docx", "trebuie verificat dacă registrul transferurilor are o intrare");
    }

    [Fact]
    public void CLASSIFIED_ACCESS_says_how_many_times_and_CLASSIFIED_DELETED_who_deleted_which_file()
    {
        var events = CopyToStick(false);
        events.Add(Sec4(4663, C0.AddMinutes(2), ("SubjectUserName", "ion.popescu"), ("ObjectType", "File"), ("ObjectName", @"D:\Clasificat\vechi.docx"), ("AccessMask", "0x10000"), ("HandleId", "0x7"), ("ProcessId", "0x2"), ("ProcessName", "explorer.exe")));
        events.Add(Sec4(4660, C0.AddMinutes(2), ("SubjectUserName", "ion.popescu"), ("ObjectType", "File"), ("HandleId", "0x7"), ("ProcessId", "0x2")));
        var all = Classified(events);
        var access = One(all, "CLASSIFIED-ACCESS", f => f.File.EndsWith("plan.docx"));
        Plain(access, "plan.docx");
        Assert.StartsWith("a avut acces la fișierul clasificat „plan.docx”", access.PlainWhat);
        var del = One(all, "CLASSIFIED-DELETED");
        Assert.Equal("Pe 14 octombrie 2026, la ora 12:02, utilizatorul cu contul „ion.popescu” a șters fișierul clasificat „vechi.docx”; ștergerea este înregistrată direct de Windows.", Plain(del, "vechi.docx"));
    }

    [Fact]
    public void CLASSIFIED_AUDIT_CHANGED_says_what_was_changed_on_which_object()
    {
        var events = new List<TimelineEvent>
        {
            Sec4(4907, C0, ("SubjectUserName", "labadmin"), ("ObjectType", "File"), ("ObjectName", Root), ("ProcessName", "x.exe")),
            Sec4(4670, C0.AddMinutes(1), ("SubjectUserName", "labadmin"), ("ObjectType", "File"), ("ObjectName", Root), ("ProcessName", "x.exe")),
        };
        var all = Classified(events).Where(f => f.RuleId == "CLASSIFIED-AUDIT-CHANGED").ToList();
        var audit = Assert.Single(all, f => f.PlainWhat.Contains("regulile de audit"));
        Plain(audit, @"„D:\Clasificat”", "poate opri înregistrarea accesului");
        var perms = Assert.Single(all, f => f.PlainWhat.Contains("permisiunile"));
        Plain(perms, @"„D:\Clasificat”");
    }

    // ================================================================ user activity (Wp14Rules.UserActivity) and cleanup

    private const string UaRoot = @"D:\Clasificat\Proiect";
    private static readonly DateTimeOffset U0 = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Bag(string path, DateTimeOffset time) => new()
    {
        Time = new Timestamp(time, "", "test"), TimeSemantics = "parent BagMRU key last written", Source = "ShellBags", EvidenceId = "EV-1", Path = path,
        Summary = $"ShellBags: {path}", Locator = @"UsrClass\BagMRU\0\1",
        Fields = { ["Account"] = "ion.popescu", ["ShellItemType"] = "0x31", ["ShellItemKind"] = "Folder", ["MruPosition"] = "0", ["BagPath"] = path },
    };

    private static TimelineEvent Bin(string original, DateTimeOffset time) => new()
    {
        Time = new Timestamp(time, "", "test"), Source = "RecycleBin", EvidenceId = "EV-4", Path = original, Summary = "Recycle " + original, Locator = "$IABCDEF.docx (v2)",
        Fields = { ["OriginalPath"] = original, ["OriginalSize"] = "2048", ["RContent"] = "present", ["OwnerSid"] = "S-1-5-21-1-2-3-1105" },
    };

    private static TimelineEvent UsbOn(string letter) => new()
    {
        Time = new Timestamp(U0, "", "test"), Source = "USB", EvidenceId = "EV-9", Summary = "USB", Locator = "L",
        Fields = { ["Serial"] = "AA11BB22", ["VidPid"] = "0951:1666", ["FriendlyName"] = "Stick de test", ["DriveLetter"] = letter },
    };

    private static List<Finding> UserActivity(params TimelineEvent[] events)
    {
        int n = 0;
        var profile = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = UaRoot }] };
        return Wp14Rules.Run([.. events], () => $"F-{++n:D4}", new Wp14Input { Scope = new CaseScope(), Profile = profile });
    }

    [Fact]
    public void USER_ACTIVITY_CLASSIFIED_says_the_folder_was_shown_not_that_a_file_was_read()
    {
        var f = One(UserActivity(Bag(UaRoot, U0), Bag(UaRoot + @"\Anexe", U0.AddMinutes(5))), "USER-ACTIVITY-CLASSIFIED");
        var story = Plain(f, "2 urme", @"„D:\Clasificat\Proiect”", "nu ce s-a făcut cu conținutul");
        Assert.Equal("Pe 19 septembrie 2026, între orele 18:00 și 18:05, utilizatorul cu contul „ion.popescu” are în profil 2 urme de navigare sau de deschidere în folderul clasificat „D:\\Clasificat\\Proiect”; ele arată că folderul a fost afișat sau un document deschis, nu ce s-a făcut cu conținutul.", story);
    }

    [Fact]
    public void USER_ACTIVITY_REMOVABLE_names_the_drive_and_whether_it_is_a_known_removable_medium()
    {
        var f = One(UserActivity(UsbOn("E:"), Bag(@"E:\Documente", U0)), "USER-ACTIVITY-REMOVABLE");
        Plain(f, "unitatea E:", "suport amovibil", "nu citirea sau copierea");
        var unknown = One(UserActivity(Bag(@"F:\Date", U0)), "USER-ACTIVITY-REMOVABLE");
        Plain(unknown, "unitatea F:", "nu spun dacă este un suport amovibil");
    }

    [Fact]
    public void USER_ACTIVITY_DELETED_names_the_folder_and_that_the_bin_keeps_path_and_time()
    {
        var f = One(UserActivity(Bin(UaRoot + @"\vechi.docx", U0)), "USER-ACTIVITY-DELETED");
        Plain(f, "un element", @"„D:\Clasificat\Proiect”", "poate fi legitimă");
        var removable = One(UserActivity(UsbOn("E:"), Bin(@"E:\vechi.docx", U0)), "USER-ACTIVITY-DELETED");
        Plain(removable, "suportul amovibil E:");
    }

    [Fact]
    public void MEDIA_TRACES_DELETED_counts_the_deleted_records_and_devices_without_a_key()
    {
        int n = 0;
        TimelineEvent Row(string source, double minute, string eventId, params (string K, string V)[] f) => new()
        {
            Time = Timestamp.FromUtc(U0.AddMinutes(minute).UtcDateTime, "", "test"), Source = source, EventId = eventId, EvidenceId = "EV-R",
            Locator = $"row={++n}", Summary = source, Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
        };
        var events = new[]
        {
            Row("USB", 0, "", ("Serial", "AA11"), ("VidPid", "VID_1234&PID_5678"), ("FriendlyName", "Stick"), ("Interface", "USBSTOR")),
            Row("EventLog:Microsoft-Windows-Partition/Diagnostic", 1, "1006", ("SerialNumber", "ZZ99"), ("BusType", "7"), ("Capacity", "32000000000"), ("Manufacturer", "Acme"), ("Model", "Stick")),
            Row(DeletedRegistryTraces.EventSource, -30, "", ("Hive", "SYSTEM"), ("Area", "USBSTOR"), ("KeyPath", @"ControlSet001\Enum\USBSTOR\X\ZZ99"), ("ValueName", ""), ("Serial", "ZZ99"), ("VolumeGuid", ""), ("Deleted", "key")),
        };
        int m = 0;
        var input = new Wp14Input { Scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified } };
        var f = One(Wp14Rules.Run(events, () => $"F-{++m:D4}", input), "MEDIA-TRACES-DELETED");
        var story = Plain(f, "o înregistrare ștearsă", "un dispozitiv USB", "nu arată cine a șters");
        Assert.StartsWith("Pe 19 septembrie 2026, ", story);
        Assert.Contains("există în registrul calculatorului o înregistrare ștearsă despre dispozitive USB și un dispozitiv USB", story);
    }

    // ================================================================ SMB, DNS, VSS, remote (Wp11Rules)

    private static TimelineEvent Share5140(double m, string share, string user = "ion.popescu", string ip = "10.0.0.5") =>
        W11.Sec(5140, m, ("ShareName", $@"\\*\{share}"), ("SubjectUserName", user), ("SubjectDomainName", "LAB"), ("IpAddress", ip));

    private static TimelineEvent Obj5145(double m, string share, string target, string user = "ion.popescu", string ip = "10.0.0.5") =>
        W11.Sec(5145, m, ("ShareName", $@"\\*\{share}"), ("RelativeTargetName", target), ("SubjectUserName", user), ("SubjectDomainName", "LAB"), ("IpAddress", ip), ("AccessMask", "0x2"));

    [Fact]
    public void SMB_ADMIN_SHARE_names_the_share_the_address_and_the_count()
    {
        var f = W11.One(W11.Run([Share5140(0, "C$"), Share5140(1, "C$"), Obj5145(1, "C$", @"Users\bob\notes.txt")]), "SMB-ADMIN-SHARE");
        var story = Plain(f, "„C$”", "10.0.0.5", "de 3 ori");
        Assert.Equal("Pe 1 octombrie 2026, între orele 13:00 și 13:01, utilizatorul cu contul „ion.popescu” a accesat de 3 ori, de la adresa 10.0.0.5, partajarea administrativă „C$” a acestui calculator.", story);
        var exe = W11.One(W11.Run([Share5140(0, "ADMIN$"), Obj5145(0, "ADMIN$", "abcd1234.exe")]), "SMB-ADMIN-SHARE");
        Plain(exe, "abcd1234.exe", "executabile sau script");
        var reg = W11.One(W11.Run([Share5140(0, "IPC$"), Obj5145(0, "IPC$", "winreg")]), "SMB-ADMIN-SHARE");
        Plain(reg, "registrul calculatorului");
    }

    [Fact]
    public void SMB_SHARE_PERMS_CHANGED_counts_the_objects_and_says_when_everyone_got_access()
    {
        TimelineEvent Perm(double m, string type, string name, string newSd = "D:PAI(A;;FA;;;BA)") =>
            W11.Sec(4670, m, ("ObjectType", type), ("ObjectName", name), ("OldSd", "D:PAI(A;;FA;;;BA)"), ("NewSd", newSd), ("SubjectUserName", "ion.popescu"), ("SubjectDomainName", "LAB"), ("ProcessName", @"C:\Windows\explorer.exe"));
        var f = W11.One(W11.Run([Perm(0, "File", @"D:\Shares\Finance\plan.xlsx"), Perm(1, "Directory", @"D:\Shares\Finance")]), "SMB-SHARE-PERMS-CHANGED");
        Plain(f, "2 obiecte", "„plan.xlsx”", "„Finance”");
        Assert.DoesNotContain("Everyone", f.PlainWhat);
        var broad = W11.One(W11.Run([Perm(0, "Directory", @"D:\Shares\Finance", "D:PAI(A;;FA;;;BA)(A;OICI;FA;;;WD)")]), "SMB-SHARE-PERMS-CHANGED");
        Plain(broad, "Everyone", "tuturor utilizatorilor");
    }

    [Fact]
    public void DNS_RARE_DOMAIN_names_the_program_and_the_domain_and_says_a_lookup_is_not_a_connection()
    {
        var f = W11.One(W11.Run([W11.Sysmon(22, 0, ("QueryName", "update-check.evil-example.net"), ("Image", @"C:\Windows\System32\rundll32.exe"), ("ProcessId", "4242"))]), "DNS-RARE-DOMAIN");
        var story = Plain(f, "„rundll32.exe”", "„update-check.evil-example.net”", "nu dovedește o conexiune");
        Assert.Equal("Pe 1 octombrie 2026, la ora 13:00, programul „rundll32.exe” (o unealtă Windows adesea folosită abuziv) a căutat în DNS adresa rar întâlnită „update-check.evil-example.net”, fiecare apărând în tot cazul cel mult de 2 ori; căutarea unei adrese nu dovedește o conexiune.", story);
    }

    [Fact]
    public void DNS_SERVER_CHANGED_names_the_old_and_the_new_servers_without_the_interface_id()
    {
        TimelineEvent Ns(double m, string servers) => W11.Row("SystemConfig", m, f: [("Interface", "{A1B2C3D4-0000-1111-2222-333344445555}"), ("NameServer", servers)]);
        var f = W11.One(W11.Run([Ns(0, "10.0.0.1"), Ns(60, "8.8.8.8")]), "DNS-SERVER-CHANGED");
        var story = Plain(f, "8.8.8.8", "10.0.0.1", "adresă publică");
        Assert.DoesNotContain("A1B2C3D4", story);
        Assert.Contains("calculatorul folosește acum serverele DNS", story);
    }

    [Fact]
    public void VSS_SNAPSHOT_DELETED_says_a_backup_deleting_command_was_started_not_that_it_succeeded()
    {
        var f = W11.One(W11.Run([W11.Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\vssadmin.exe"), ("CommandLine", "vssadmin.exe delete shadows /all /quiet"), ("SubjectUserName", "ion.popescu"), ("SubjectDomainName", "LAB"))]), "VSS-SNAPSHOT-DELETED");
        var story = Plain(f, "copiile de rezervă", "nu că ștergerea a reușit");
        Assert.Equal("Pe 1 octombrie 2026, la ora 13:00, utilizatorul cu contul „ion.popescu” a lansat o comandă care șterge copiile de rezervă ale discului (copiile shadow, din care se pot recupera fișierele); jurnalul arată că a fost lansată, nu că ștergerea a reușit.", story);
        var resize = W11.One(W11.Run([W11.Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\vssadmin.exe"), ("CommandLine", "vssadmin resize shadowstorage /for=C: /on=C: /maxsize=401MB"), ("SubjectUserName", "ion.popescu"))]), "VSS-SNAPSHOT-DELETED");
        Plain(resize, "reduce spațiul");
        var volsnap = W11.One(W11.Run([W11.Sys(33, 0, "volsnap")]), "VSS-SNAPSHOT-DELETED");
        Assert.StartsWith("Windows a șters cea mai veche copie de rezervă", volsnap.PlainWhat);
        Plain(volsnap, "limita de spațiu");
    }

    [Fact]
    public void REMOTE_SSH_names_the_address_and_counts_the_accepted_logons()
    {
        TimelineEvent Ssh(double m, string payload) => W11.Ev("OpenSSH/Operational", 4, m, "OpenSSH", ("process", "sshd"), ("payload", payload));
        var f = W11.One(W11.Run([Ssh(0, "Failed password for alice from 10.0.0.5 port 1 ssh2"), Ssh(1, "Accepted password for alice from 10.0.0.5 port 3 ssh2"), Ssh(2, "Accepted password for alice from 10.0.0.5 port 4 ssh2")]), "REMOTE-SSH");
        var story = Plain(f, "SSH", "10.0.0.5", "de 2 ori", "o încercare eșuată");
        Assert.Equal("Pe 1 octombrie 2026, între orele 13:01 și 13:02, utilizatorul cu contul „alice” s-a conectat la calculator de la distanță prin SSH (terminal în mod text, prin rețea), de la adresa 10.0.0.5, de 2 ori; în același jurnal apare și o încercare eșuată.", story);
        var ext = W11.One(W11.Run([Ssh(0, "Accepted password for root from 8.8.8.8 port 4000 ssh2")]), "REMOTE-SSH");
        Plain(ext, "adresă este publică");
    }

    [Fact]
    public void REMOTE_WINRM_and_REMOTE_PSEXEC_are_told_in_plain_words()
    {
        var winrm = W11.One(W11.Run([W11.Ev("Microsoft-Windows-WinRM/Operational", 91, 0, "Microsoft-Windows-WinRM")]), "REMOTE-WINRM");
        Plain(winrm, "de la distanță");
        TimelineEvent Svc(double m, string name, string image) => W11.Sys(7045, m, "Service Control Manager", ("ServiceName", name), ("ImagePath", image), ("AccountName", "LocalSystem"));
        var admin = W11.Sec(5145, 0, ("ShareName", @"\\*\ADMIN$"), ("RelativeTargetName", "PSEXESVC.exe"), ("SubjectUserName", "bob"), ("SubjectDomainName", "LAB"), ("IpAddress", "10.0.0.7"), ("AccessMask", "0x2"));
        var paired = W11.One(W11.Run([admin, Svc(1, "PSEXESVC", @"%SystemRoot%\PSEXESVC.exe")]), "REMOTE-PSEXEC");
        var story = Plain(paired, "„PSEXESVC”", "„PSEXESVC.exe”", "10.0.0.7", "nu arată ce s-a rulat");
        Assert.StartsWith("Pe 1 octombrie 2026, ", story);
        var alone = W11.One(W11.Run([W11.Sec(5145, 0, ("ShareName", @"\\*\ADMIN$"), ("RelativeTargetName", "tool.exe"), ("SubjectUserName", "bob"), ("SubjectDomainName", "LAB"), ("IpAddress", "10.0.0.7"), ("AccessMask", "0x2"))]), "REMOTE-PSEXEC");
        Plain(alone, "„tool.exe”", "nu arată că fișierul a fost rulat");
    }

    // ================================================================ Correlation.cs

    private static readonly DateTime G0 = new(2026, 9, 19, 14, 50, 0, DateTimeKind.Utc);

    private static TimelineEvent E(string source, string id, int minute, string ev = "EV-1", string locator = "", string process = "", string path = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(G0.AddMinutes(minute), "", "test"), Source = source, EventId = id, EvidenceId = ev,
        Locator = locator.Length > 0 ? locator : $"EventRecordID={1000 + minute}", Process = process, Path = path, Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static List<Finding> Incident() => Correlation.Run(
    [
        E("Prefetch", "", 3, "EV-PF1", "pf", "SETUP.EXE", "", ("PrefetchHash", "C0F23475"), ("RunCount", "4"),
          ("ReferencedFiles", @"\VOLUME{x}\USERS\U\DOWNLOADS\TOOL_302044\SETUP.EXE|\VOLUME{x}\WINDOWS\SYSTEM32\NTDLL.DLL")),
        E("EventLog:Microsoft-Windows-Windows Defender/Operational", "1116", 14, "EV-DEF", "", "", "",
          ("Threat Name", "Behavior:Win32/GenCodeInjected.H"), ("Path", @"process:_pid:52196; file:_C:\Users\U\Downloads\TOOL_302044\VendorAgent32.exe"), ("Action Name", "Not Applicable")),
        E("Prefetch", "", 19, "EV-PF2", "pf", "MSIEXEC.EXE", "", ("PrefetchHash", "8FFB1633"), ("RunCount", "9"),
          ("ReferencedFiles", @"\VOLUME{x}\WINDOWS\SYSTEM32\MSIEXEC.EXE|\VOLUME{x}\USERS\U\APPDATA\LOCAL\VENDORX\VXUTILSVC HELPER\VENDOR_HELPER.CMD")),
        E("SRUM", "", 25, "EV-SRUM", "row1", "msbuild.exe", @"\device\harddiskvolume3\windows\microsoft.net\framework\v4.0.30319\msbuild.exe",
          ("BytesSent", "7764746"), ("BytesRecvd", "100581358")),
    ]);

    [Fact]
    public void DEF_DETECTION_EXEC_SCRIPT_NET_LOLBIN_and_EXEC_USERPATH_CORRELATED_are_told_as_whole_clauses()
    {
        var all = Incident();
        var def = One(all, "DEF-DETECTION");
        var story = Plain(def, "„Behavior:Win32/GenCodeInjected.H”", "„VendorAgent32.exe”");
        Assert.Equal("Pe 19 septembrie 2026, la ora 18:04, Microsoft Defender (antivirusul Windows) a detectat „Behavior:Win32/GenCodeInjected.H” în „VendorAgent32.exe”.", story);
        var script = One(all, "EXEC-SCRIPT-VIA-LOLBIN");
        Plain(script, "„MSIEXEC.EXE”", "„VENDOR_HELPER.CMD”", "foldere în care orice utilizator poate scrie");
        var net = One(all, "NET-LOLBIN-TRAFFIC");
        Plain(net, "„msbuild.exe”", "7,8 MB", "100,6 MB", "nu arată cu ce adrese");
        var corr = One(all, "EXEC-USERPATH-CORRELATED");
        Plain(corr, "„SETUP.EXE”", "a rulat", "altă constatare gravă");
        // the incident chain tells each step the way its own finding tells it
        var chain = One(all, "INCIDENT-CHAIN");
        Assert.Contains("Microsoft Defender (antivirusul Windows) a detectat", chain.PlainWhat);
    }

    [Fact]
    public void EXEC_USERPATH_without_other_signals_is_one_summary_clause()
    {
        var f = One(Correlation.Run([E("Prefetch", "", 3, "EV-PF1", "pf", "TOOL.EXE", "", ("PrefetchHash", "C0F23475"), ("RunCount", "4"), ("ReferencedFiles", @"\VOLUME{x}\USERS\U\APPDATA\LOCAL\VENDORX\TOOL.EXE"))]), "EXEC-USERPATH");
        var story = Plain(f, "un program", "care a rulat", "foldere în care orice utilizator poate scrie");
        Assert.StartsWith("Pe 19 septembrie 2026, la ora 17:53, a fost găsit un program care a rulat", story);
    }

    [Fact]
    public void LOG_GAP_counts_the_holes_and_the_missing_records()
    {
        var gap = One(Correlation.Run(
        [
            E("EventLog:Security", "4624", 1, "EV-SEC", "EventRecordID=100"),
            E("EventLog:Security", "4624", 2, "EV-SEC", "EventRecordID=101"),
            E("EventLog:Security", "4624", 30, "EV-SEC", "EventRecordID=900"),
        ]), "LOG-GAP");
        Plain(gap, "„Security”", "un gol", "798 de înregistrări", "ștergere selectivă");
    }

    [Fact]
    public void FIREWALL_IFEO_and_WINLOGON_name_the_program_or_the_setting()
    {
        var fw = new TimelineEvent
        {
            Time = Timestamp.FromUtc(G0, "", "test"), Source = "EventLog:Microsoft-Windows-Windows Firewall With Advanced Security/Firewall", EventId = "2097", EvidenceId = "EV-FW",
            Locator = "EventRecordID=1", Summary = "fw",
            Fields = { ["RuleName"] = "r1", ["ApplicationPath"] = @"C:\Users\ion.popescu\AppData\Local\VendorX\pro_cmd_core.exe", ["Action"] = "3", ["Direction"] = "1", ["ModifyingApplication"] = @"C:\x\setup.exe" },
        };
        var f = One(Correlation.Run([fw]), "FIREWALL-RULE-USERPATH");
        Plain(f, "„pro_cmd_core.exe”", "primească conexiuni din rețea", "S-a adăugat sau s-a modificat");
        var ifeo = new TimelineEvent { Time = Timestamp.FromUtc(G0, "", "test"), Source = "IFEO", EvidenceId = "EV-R", Locator = "ifeo", Summary = "ifeo", Process = "cmd.exe", Path = @"C:\Windows\System32\cmd.exe", Fields = { ["Target"] = "sethc.exe", ["Command"] = @"C:\Windows\System32\cmd.exe" } };
        var i = One(Correlation.Run([ifeo]), "PERSIST-IFEO-DEBUGGER");
        Plain(i, "„cmd.exe”", "„sethc.exe”", "poate fi legitimă");
        var wl = new TimelineEvent { Time = Timestamp.FromUtc(G0, "", "test"), Source = "Winlogon", EvidenceId = "EV-R", Locator = "wl", Summary = "wl", Path = "Winlogon", Fields = { ["ValueName"] = "Userinit", ["NonDefault"] = "true", ["Command"] = @"C:\Users\Public\x.exe" } };
        var w = One(Correlation.Run([wl]), "PERSIST-WINLOGON");
        Plain(w, "„Userinit”", @"„C:\Users\Public\x.exe”");
    }

    [Fact]
    public void CRED_SPRAY_names_the_address_and_the_number_of_accounts()
    {
        var events = Enumerable.Range(0, 10).Select(i => E("EventLog:Security", "4625", i, "EV-S", "", "", "", ("TargetUserName", "user" + i), ("IpAddress", "203.0.113.9"))).ToList();
        var f = One(Correlation.Run(events), "CRED-SPRAY");
        var story = Plain(f, "10 conturi diferite", "203.0.113.9", "„user0”");
        Assert.StartsWith("Pe 19 septembrie 2026, ", story);
        Assert.Contains("cineva a încercat parole pe 10 conturi diferite", story);
    }

    [Fact]
    public void DOWNLOAD_THEN_EXEC_names_the_file_the_site_and_the_program_started_from_the_same_folder()
    {
        var dl = new TimelineEvent
        {
            Time = Timestamp.FromUtc(G0, "", "test"), Source = "BrowserDownload", EvidenceId = "EV-B", Locator = "dl", Summary = "dl", Path = @"C:\Users\ion.popescu\Downloads\TOOL_302044.zip", Dns = "files.example.net",
            Fields = { ["TabUrl"] = "https://files.example.net/x", ["UrlChain"] = "https://files.example.net/x" },
        };
        var run = new TimelineEvent { Time = Timestamp.FromUtc(G0.AddMinutes(4), "", "test"), Source = "Prefetch", EvidenceId = "EV-PF", Locator = "pf", Process = "SETUP.EXE", Summary = "run", Path = @"C:\Users\ion.popescu\Downloads\SETUP.EXE", Fields = { ["ReferencedFiles"] = @"C:\Users\ion.popescu\Downloads\SETUP.EXE", ["RunCount"] = "1" } };
        var f = One(Correlation.Run([dl, run]), "DOWNLOAD-THEN-EXEC");
        var story = Plain(f, "„TOOL_302044.zip”", "files.example.net", "„SETUP.EXE”", "4 minute");
        Assert.Equal("Pe 19 septembrie 2026, între orele 17:50 și 17:54, s-a descărcat din browser fișierul „TOOL_302044.zip” (de pe files.example.net), iar după 4 minute a pornit, din același folder, programul „SETUP.EXE”.", story);
    }

    // ================================================================ policy, not evaluated, hash set

    [Fact]
    public void POLICY_CONTROL_GAP_says_which_setting_is_configured_but_not_seen()
    {
        var gap = new ControlGap { SettingId = "audit.logon", Title = "Audit Logon", BrokenLevel = "observed", Severity = Severity.High, DisablesAuditing = true, Summary = "x", WindowStart = new DateTimeOffset(G0), WindowEnd = new DateTimeOffset(G0.AddHours(1)) };
        var f = Assert.Single(new PolicyTimelineResult { Gaps = [gap] }.ToFindings(() => "F-1"));
        var story = Plain(f, "„Audit Logon”", "în jurnale nu apar evenimentele", "jurnalele pot să nu mai arate");
        Assert.Equal("Pe 19 septembrie 2026, între orele 17:50 și 18:50, există o diferență între ce cere politica de securitate și ce se vede pe calculator: setarea „Audit Logon” este configurată, dar în jurnale nu apar evenimentele pe care ar trebui să le producă; astfel jurnalele pot să nu mai arate o parte din activitate; probele nu arată de ce (poate fi o întârziere de aplicare, o filtrare sau un calculator oprit).", story);
        var applied = Assert.Single(new PolicyTimelineResult { Gaps = [new ControlGap { Title = "X", BrokenLevel = "applied" }] }.ToFindings(() => "F-2"));
        Plain(applied, "nu se vede că a fost preluată");
    }

    [Fact]
    public void RULE_NOT_EVALUATED_does_not_name_the_rule_and_says_the_rows_are_not_clean()
    {
        var rows = new List<TimelineEvent> { E("EventLog:Security", "4688", 1), E("EventLog:Security", "4688", 2) };
        var f = RulePatterns.NotEvaluated("F-1", "VSS-SNAPSHOT-DELETED", "Impact", rows);
        var story = Plain(f, "2 intrări", "nu înseamnă că sunt curate");
        Assert.StartsWith("Pe 19 septembrie 2026, între orele 17:51 și 17:52, nu s-au putut verifica 2 intrări din probe", story);
    }

    [Fact]
    public void HASHSET_KNOWN_BAD_names_the_file_and_the_list()
    {
        var m = new HashSetMatch("S1", "Lista operatorului", HashSetCategory.KnownBad, "lista.csv", new string('a', 64), "EV-1", @"C:\Users\ion.popescu\Downloads\tool.exe", "row=1", "SHA-256", new string('b', 64), "Prefetch", new string('c', 64));
        var f = Assert.Single(HashSetMatcher.ToFindings([m], () => "F-1"));
        var story = Plain(f, "„tool.exe”", "„Lista operatorului”", "sursa ei trebuie verificată");
        Assert.StartsWith("Există în probe fișierul „tool.exe”", story);
    }
}
