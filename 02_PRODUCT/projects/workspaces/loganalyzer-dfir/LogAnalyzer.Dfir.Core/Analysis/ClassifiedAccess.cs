using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// One record the classified-access analysis reads, independent of where it came from (the station's live event log or a timeline built from
/// exported evidence). <see cref="Kind"/> is "event" for an event-log record (<see cref="Channel"/> + <see cref="EventId"/>), or the name of a station
/// trace ("LNK", "JumpList", "USN") whose path is in <see cref="Path"/>. <see cref="Origin"/> is the caller's own object (for evidence references).
/// </summary>
public sealed record AccessRecord(string Kind, string Channel, int EventId, DateTimeOffset? TimeUtc, string Evidence, IReadOnlyDictionary<string, string> Data,
                                  string Path = "", object? Origin = null)
{
    public string V(string key) => Data.TryGetValue(key, out var v) ? v : "";
    public string V(params string[] keys)
    {
        foreach (var k in keys) if (Data.TryGetValue(k, out var v) && v.Length > 0) return v;
        return "";
    }
}

/// <summary>A classified folder of the procedure profile and the other names Windows may log for it (8.3 short name, junction / symbolic link target).</summary>
public sealed record ClassifiedRoot(string Path, IReadOnlyList<string>? Aliases = null, string Note = "");

/// <summary>A removable medium seen connected at a time (serial from Partition/Diagnostic 1006, USBSTOR or 6416).</summary>
public sealed record MediumSeen(DateTimeOffset TimeUtc, string Serial, string Label, string Letter = "");

/// <summary>
/// When and as whom the application itself read the classified files (to compute their SHA-256): its own reads must never be shown as someone's access.
/// Recorded in the sealed audit at every control ("control.hashing").
/// </summary>
public sealed record SelfActivityWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc, int ProcessId, string ImagePath, string Account, int Files = 0);

public enum ExitChannel { RemovableMedia, Print, Archive, CopyCommand, MoveOrRename, NetworkShare, OpenedFromMedium, TemporalCoincidence }
public enum ExitVerdict { Unjustified, ToVerify, Approved }

/// <summary>One access to a classified file: who, with which program, what kind of access, when, according to which record.</summary>
/// <param name="Weak">True for a station trace (shortcut, jump list, change journal): it shows that the file was opened or changed, not who did it.</param>
public sealed record ClassifiedAccessEntry(DateTimeOffset? TimeUtc, string Who, string Process, string Access, bool Weak, string Evidence, object? Origin = null);

/// <summary>A way a classified file may have left the station or its folder, with the chain of evidence and the transfer-register entry that covers it (if any).</summary>
public sealed record ClassifiedExit(ExitChannel Channel, ExitVerdict Verdict, DateTimeOffset? TimeUtc, string Who, string File, string Target, string Detail,
                                   IReadOnlyList<string> Chain, string RegisterEntry, IReadOnlyList<object?> Origins, bool Weak = false)
{
    public string VerdictText => ClassifiedAccess.VerdictText(Verdict);
    public string ChannelText => ClassifiedAccess.ChannelText(Channel);
}

/// <summary>A change to the permissions (4670) or to the audit rules (4907) of a classified object: whoever changes the SACL can switch the evidence off.</summary>
public sealed record ClassifiedSecurityChange(DateTimeOffset? TimeUtc, string Who, string Process, string Object, bool AuditRules, string Evidence, object? Origin = null);

/// <summary>Everything known about one classified file: accesses (timeline) and exits.</summary>
public sealed class ClassifiedFileReport
{
    public required string Path { get; init; }
    public List<ClassifiedAccessEntry> Accesses { get; } = [];
    public List<ClassifiedExit> Exits { get; } = [];
    public bool Deleted => Accesses.Any(a => a.Access.StartsWith("șters", StringComparison.Ordinal));
    public int Unjustified => Exits.Count(e => e.Verdict == ExitVerdict.Unjustified);
    public int ToVerify => Exits.Count(e => e.Verdict == ExitVerdict.ToVerify);
    public int Approved => Exits.Count(e => e.Verdict == ExitVerdict.Approved);
    /// <summary>0 = unjustified exit, 1 = something to verify (exit, deletion), 2 = approved exits only, 3 = accesses only: the order of the report and the UI tab.</summary>
    public int Rank => Unjustified > 0 ? 0 : ToVerify > 0 || Deleted ? 1 : Approved > 0 ? 2 : 3;
    public string StatusText => Rank switch { 0 => "IEȘIRE NEJUSTIFICATĂ", 1 => "DE VERIFICAT", 2 => "IEȘIRI APROBATE", _ => "DOAR ACCESE" };
    public string LastAccessText => Accesses.Where(a => a.TimeUtc is not null).Select(a => a.TimeUtc!.Value).DefaultIfEmpty().Max() is var t && t != default ? t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "-";
    public string WhoText => string.Join(", ", Accesses.Select(a => a.Who).Where(w => w.Length > 0 && w != "necunoscut").Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
    public string ExitsText => Exits.Count == 0 ? "-" : string.Join("; ", Exits.Select(e => $"{e.VerdictText}: {e.ChannelText} → {e.Target}"));
}

/// <summary>What the analysis was given.</summary>
public sealed class ClassifiedInput
{
    public IReadOnlyList<ClassifiedRoot> Folders { get; init; } = [];
    public IReadOnlyList<AccessRecord> Records { get; init; } = [];
    /// <summary>Volume device / GUID names → drive letter ("\Device\HarddiskVolume3" → "C:", "Volume{…}" → "C:"); from QueryDosDevice and MountedDevices.</summary>
    public IReadOnlyDictionary<string, string>? VolumeMap { get; init; }
    /// <summary>Drive letters known to be removable ("E:").</summary>
    public IReadOnlySet<string>? RemovableDrives { get; init; }
    public IReadOnlyList<MediumSeen> Media { get; init; } = [];
    /// <summary>The approved transfer register; null = not defined (no exit can be shown as approved).</summary>
    public TransferRegister? Transfers { get; init; }
    /// <summary>Time zone of the times typed in the transfer register (the station's declared zone); null = not declared, so no register time can be compared.</summary>
    public TimeZoneInfo? RegisterZone { get; init; }
    /// <summary>The application's own reading windows (hashing); records of its process in a window are listed apart, never as accesses.</summary>
    public IReadOnlyList<SelfActivityWindow> SelfWindows { get; init; } = [];
    /// <summary>Names of the files present now in the classified folders (to recognise a copy whose source read was not audited).</summary>
    public IReadOnlyList<string> KnownFiles { get; init; } = [];
    /// <summary>SHA-256 of the classified files present now, by full path (matched against the register's fingerprints).</summary>
    public IReadOnlyDictionary<string, string>? FileHashes { get; init; }
    /// <summary>SID → account, for records that only carry the SID.</summary>
    public IReadOnlyDictionary<string, string>? SidNames { get; init; }
    /// <summary>False on the Standalone posture: there is no network, so network-share access (5145) is not evaluated.</summary>
    public bool NetworkApplies { get; init; }
    /// <summary>Resolves an 8.3 short path to its long form when the file still exists; null = not available (offline evidence).</summary>
    public Func<string, string?>? LongPath { get; init; }
}

public sealed class ClassifiedAccessReport
{
    public List<ClassifiedFileReport> Files { get; } = [];
    public List<ClassifiedSecurityChange> SecurityChanges { get; } = [];
    public List<string> Notes { get; } = [];
    /// <summary>Records of the application's own reads (hashing), listed apart: "activitate proprie a aplicației (calcul amprente)".</summary>
    public List<ClassifiedAccessEntry> SelfActivity { get; } = [];
    public IEnumerable<ClassifiedExit> Exits => Files.SelectMany(f => f.Exits);
    public int Unjustified => Files.Sum(f => f.Unjustified);
    public int ToVerify => Files.Sum(f => f.ToVerify);
    public int Approved => Files.Sum(f => f.Approved);
    public int Deleted => Files.Count(f => f.Deleted);
    /// <summary>Number of audit records (4656/4663/4660/4670/4907) on classified objects that were read: 0 means the folders' auditing produced nothing usable.</summary>
    public int AuditRecords { get; set; }
    public List<ClassifiedFileReport> Ordered => [.. Files.OrderBy(f => f.Rank).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// Classified information: who accessed the files of the classified folders and whether any of them left the station (removable media, printer,
/// archive, copy command, move / rename out of the folder, network share, opened from a medium) without an entry in the transfer register.
/// Pure: no I/O. An exit is UNJUSTIFIED only on a record that shows the file itself going somewhere; a coincidence in time (file read, medium
/// connected) is only "DE VERIFICAT" - "corelare temporală, nu dovadă de copiere".
/// </summary>
public static class ClassifiedAccess
{
    public const string TemporalNote = "corelare temporală, nu dovadă de copiere";
    /// <summary>A read and a medium connected within this window are shown as a temporal coincidence.</summary>
    public static readonly TimeSpan CoincidenceWindow = TimeSpan.FromHours(2);
    /// <summary>The register time is typed by hand: an entry this far from the exit still matches.</summary>
    public static readonly TimeSpan RegisterWindow = TimeSpan.FromHours(12);
    /// <summary>An exit matches a register row only within this distance from the row's declared transfer time.</summary>
    public static readonly TimeSpan RegisterTolerance = TimeSpan.FromHours(2);

    // ---- access masks ------------------------------------------------------------------------------------------------

    private static readonly (int Bit, string Code, string Text)[] MaskBits =
    [
        (0x1, "%%4416", "citire (ReadData / listare)"), (0x2, "%%4417", "scriere (WriteData / adăugare fișier)"), (0x4, "%%4418", "adăugare (AppendData / subfolder)"),
        (0x20, "%%4421", "executare / traversare"), (0x40, "%%4422", "ștergere conținut folder (DeleteChild)"), (0x100, "%%4424", "schimbare atribute"),
        (0x10000, "%%1537", "ȘTERGERE (DELETE)"), (0x40000, "%%1539", "schimbare permisiuni (WRITE_DAC)"), (0x80000, "%%1540", "schimbare proprietar (WRITE_OWNER)"),
        (0x1000000, "%%1542", "acces la regulile de audit (ACCESS_SYS_SEC)"),
    ];

    public const int ReadData = 0x1, WriteData = 0x2, AppendData = 0x4, Delete = 0x10000, WriteDac = 0x40000;

    /// <summary>The access mask of a 4656 / 4663 / 5145 record: the hex AccessMask field, or the %%codes / names of the AccessList when the mask is absent.</summary>
    public static int Mask(AccessRecord r)
    {
        var m = r.V("AccessMask").Trim();
        if (m.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(m.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return v;
        if (int.TryParse(m, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
        var list = r.V("AccessList", "Accesses");
        int acc = 0;
        foreach (var (bit, code, _) in MaskBits) if (list.Contains(code, StringComparison.Ordinal)) acc |= bit;
        if (list.Contains("WriteData", StringComparison.OrdinalIgnoreCase) || list.Contains("AddFile", StringComparison.OrdinalIgnoreCase)) acc |= WriteData;
        if (list.Contains("ReadData", StringComparison.OrdinalIgnoreCase)) acc |= ReadData;
        if (list.Contains("DELETE", StringComparison.Ordinal)) acc |= Delete;
        return acc;
    }

    public static string MaskText(int mask)
    {
        var parts = MaskBits.Where(b => (mask & b.Bit) != 0).Select(b => b.Text).ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : mask == 0 ? "acces necunoscut" : $"alt acces (0x{mask:X})";
    }

    // ---- paths ---------------------------------------------------------------------------------------------------------

    private static readonly Regex DeviceVolume = new(@"^\\Device\\HarddiskVolume\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex VolumeGuid = new(@"^Volume\{[0-9A-Fa-f-]{36}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// The comparable form of a path as Windows logs it: "\\?\" / "\??\" / "\\.\" prefixes removed, "\Device\HarddiskVolumeN" and "Volume{GUID}" replaced by
    /// their drive letter when <paramref name="volumeMap"/> knows them, forward slashes and repeated or trailing backslashes removed, drive letter upper case.
    /// </summary>
    public static string Normalize(string path, IReadOnlyDictionary<string, string>? volumeMap = null)
    {
        var p = (path ?? "").Trim().Trim('"').Replace('/', '\\');
        if (p.Length == 0) return "";
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\??\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) p = p[4..];
        if (volumeMap is not null)
        {
            var m = DeviceVolume.Match(p);
            if (!m.Success) m = VolumeGuid.Match(p);
            if (m.Success && Lookup(volumeMap, m.Value) is { Length: > 0 } letter) p = letter.TrimEnd('\\') + p[m.Length..];
        }
        bool unc = p.StartsWith(@"\\", StringComparison.Ordinal);
        while (p.Contains(@"\\", StringComparison.Ordinal)) p = p.Replace(@"\\", @"\", StringComparison.Ordinal);
        if (unc) p = @"\" + p;
        if (p.Length > 3 || (p.Length == 3 && p[1] != ':')) p = p.TrimEnd('\\');
        if (p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0])) p = char.ToUpperInvariant(p[0]) + p[1..];
        if (p.Length == 2 && p[1] == ':') p += @"\";
        return p;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> map, string key)
    {
        if (map.TryGetValue(key, out var v)) return v;
        foreach (var (k, val) in map)
            if (Normalize(k).Equals(key, StringComparison.OrdinalIgnoreCase) || k.Trim('\\').EndsWith(key.Trim('\\'), StringComparison.OrdinalIgnoreCase)) return val;
        return null;
    }

    public static bool IsUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.EndsWith('\\') ? root : root + @"\", StringComparison.OrdinalIgnoreCase);

    public static string FileName(string path)
    {
        var p = path.TrimEnd('\\');
        var i = p.LastIndexOf('\\');
        return i >= 0 ? p[(i + 1)..] : p;
    }

    private static string Stem(string name) => name.LastIndexOf('.') is var i and > 0 ? name[..i] : name;

    /// <summary>The drive letter of a normalised path ("E:"), "" otherwise.</summary>
    public static string Drive(string path) => path.Length >= 2 && path[1] == ':' ? char.ToUpperInvariant(path[0]) + ":" : "";

    private sealed class Roots(ClassifiedInput input)
    {
        private readonly List<(string Root, string Canonical)> _all = Build(input);

        private static List<(string, string)> Build(ClassifiedInput input)
        {
            var l = new List<(string, string)>();
            foreach (var f in input.Folders)
            {
                var canon = Normalize(f.Path, input.VolumeMap);
                if (canon.Length == 0) continue;
                l.Add((canon, canon));
                foreach (var a in f.Aliases ?? []) if (Normalize(a, input.VolumeMap) is { Length: > 0 } n) l.Add((n, canon));
            }
            return [.. l.OrderByDescending(x => x.Item1.Length)];
        }

        /// <summary>The canonical path of a file inside a classified folder (an alias rewritten to the folder's own path); null when it is outside every folder.</summary>
        public string? Classify(string normalized, Func<string, string?>? longPath)
        {
            if (normalized.Length == 0) return null;
            foreach (var (root, canon) in _all)
                if (IsUnder(normalized, root)) return canon + normalized[root.Length..];
            if (longPath is not null && normalized.Contains('~') && longPath(normalized) is { Length: > 0 } lp && !lp.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return Classify(Normalize(lp), null);
            return null;
        }

        /// <summary>The path relative to its volume ("Clasificat\a.docx"), for change-journal records that carry no drive letter.</summary>
        public string? ClassifyRelative(string rel)
        {
            rel = rel.Trim('\\');
            foreach (var (root, canon) in _all)
            {
                var r = root.Length > 3 && root[1] == ':' ? root[3..] : root;
                if (r.Length > 0 && (rel.Equals(r, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(r + @"\", StringComparison.OrdinalIgnoreCase)))
                    return canon + rel[r.Length..];
            }
            return null;
        }
    }

    // ---- analysis ------------------------------------------------------------------------------------------------------

    private static readonly Regex Archiver = new(@"(\\|^)(7z|7za|7zr|7zg|rar|winrar|winzip\d*|wzzip|zip|tar|makecab|peazip|bandizip|compact)\.exe|Compress-Archive|ZipFile\]?::CreateFromDirectory",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex Copier = new(@"(\\|^)(robocopy|xcopy)\.exe|\b(copy|move|xcopy|robocopy|Copy-Item|Move-Item|cp|mv)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex DriveRoot = new(@"(?<![A-Za-z])([A-Za-z]):\\", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static ClassifiedAccessReport Analyze(ClassifiedInput input)
    {
        var rep = new ClassifiedAccessReport();
        if (input.Folders.Count == 0) { rep.Notes.Add("Profilul de proceduri nu definește foldere clasificate: accesul și ieșirile nu pot fi urmărite."); return rep; }
        var roots = new Roots(input);
        var files = new Dictionary<string, ClassifiedFileReport>(StringComparer.OrdinalIgnoreCase);
        ClassifiedFileReport File(string path) => files.TryGetValue(path, out var f) ? f : files[path] = new ClassifiedFileReport { Path = path };
        var removable = input.RemovableDrives ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var events = input.Records.Where(r => r.Kind == "event").OrderBy(r => r.TimeUtc ?? DateTimeOffset.MinValue).ToList();

        string Who(AccessRecord r)
        {
            var u = r.V("SubjectUserName");
            var d = r.V("SubjectDomainName");
            if (u.Length > 0 && u != "-") return d.Length > 0 && d != "-" ? $"{d}\\{u}" : u;
            var sid = r.V("SubjectUserSid", "UserSid");
            return sid.Length > 0 && input.SidNames?.GetValueOrDefault(sid) is { Length: > 0 } n ? n : sid.Length > 0 ? sid : "necunoscut";
        }
        string Proc(AccessRecord r) => r.V("ProcessName", "NewProcessName");
        string Handle(AccessRecord r) => r.V("HandleId") + "|" + r.V("ProcessId");

        // Names of the classified files: the ones accessed (filled below) and the ones present now in the folders.
        var classifiedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in input.KnownFiles)
            if (roots.Classify(Normalize(k, input.VolumeMap), input.LongPath) is { } kp) classifiedNames.TryAdd(FileName(kp), kp);

        // 1. Accesses (4656 handle requested, 4663 access used, 4660 deleted, 4658 handle closed) and permission / SACL changes (4670, 4907).
        var openHandles = new Dictionary<string, (string Path, AccessRecord Rec, int Mask)>(StringComparer.Ordinal);
        var deletes = new List<(string Path, AccessRecord Rec)>();
        var writesOutside = new List<(string Path, AccessRecord Rec, int Mask)>();
        var pending = new List<PendingExit>();
        foreach (var r in events.Where(e => e.Channel == "Security"))
        {
            if (r.EventId is 4656 or 4658 or 4660 or 4663 or 4670 or 4907 && IsSelf(input, r))
            {
                // the application's own read of the file (SHA-256 at a control): listed apart, never someone's access, never an exit or a coincidence
                if (r.EventId is 4656 or 4663 && roots.Classify(Normalize(r.V("ObjectName"), input.VolumeMap), input.LongPath) is { } own)
                    rep.SelfActivity.Add(new(r.TimeUtc, Who(r), Proc(r), $"{SelfLabel}: {own} — {MaskText(Mask(r))}", false, r.Evidence, r.Origin));
                continue;
            }
            switch (r.EventId)
            {
                case 4656 or 4663:
                {
                    var type = r.V("ObjectType");
                    if (type.Length > 0 && !type.Equals("File", StringComparison.OrdinalIgnoreCase)) break;
                    var norm = Normalize(r.V("ObjectName"), input.VolumeMap);
                    var mask = Mask(r);
                    if (roots.Classify(norm, input.LongPath) is { } path)
                    {
                        rep.AuditRecords++;
                        classifiedNames.TryAdd(FileName(path), path);
                        var what = (r.EventId == 4656 ? "cerere de acces: " : "") + MaskText(mask);
                        File(path).Accesses.Add(new(r.TimeUtc, Who(r), Proc(r), what, false, r.Evidence, r.Origin));
                        openHandles[Handle(r)] = (path, r, mask);
                        if ((mask & Delete) != 0 && r.EventId == 4663) deletes.Add((path, r));
                    }
                    else if ((mask & (WriteData | AppendData)) != 0 && norm.Length > 0) writesOutside.Add((norm, r, mask));
                    break;
                }
                case 4660:
                    if (openHandles.TryGetValue(Handle(r), out var del))
                    {
                        rep.AuditRecords++;
                        File(del.Path).Accesses.Add(new(r.TimeUtc, Who(r), Proc(r), "șters (obiectul a fost eliminat, 4660)", false, r.Evidence, r.Origin));
                        deletes.RemoveAll(d => d.Path.Equals(del.Path, StringComparison.OrdinalIgnoreCase) && Handle(d.Rec) == Handle(r));
                    }
                    break;
                case 4658:
                    if (openHandles.Remove(Handle(r), out var closed))
                    {
                        var list = File(closed.Path).Accesses;
                        var i = list.FindLastIndex(a => ReferenceEquals(a.Origin, closed.Rec.Origin) && a.Evidence == closed.Rec.Evidence);
                        if (i >= 0 && r.TimeUtc is { } tc) list[i] = list[i] with { Access = list[i].Access + $"; închis la {tc:HH:mm:ss} UTC (4658)" };
                    }
                    break;
                case 4670 or 4907:
                {
                    var norm = Normalize(r.V("ObjectName"), input.VolumeMap);
                    if (roots.Classify(norm, input.LongPath) is not { } obj) break;
                    rep.AuditRecords++;
                    rep.SecurityChanges.Add(new(r.TimeUtc, Who(r), Proc(r), obj, r.EventId == 4907, r.Evidence, r.Origin));
                    File(obj).Accesses.Add(new(r.TimeUtc, Who(r), Proc(r), r.EventId == 4907 ? "regulile de audit (SACL) au fost schimbate (4907)" : "permisiunile au fost schimbate (4670)", false, r.Evidence, r.Origin));
                    break;
                }
            }
        }

        // 2. Weaker station traces: shortcuts and jump lists (the file was opened), change journal (created, renamed, deleted).
        var usnByFile = input.Records.Where(r => r.Kind == "USN").GroupBy(r => r.V("FileId")).ToList();
        foreach (var r in input.Records.Where(r => r.Kind is "LNK" or "JumpList"))
        {
            var norm = Normalize(r.Path, input.VolumeMap);
            if (roots.Classify(norm, input.LongPath) is { } path)
            {
                classifiedNames.TryAdd(FileName(path), path);
                File(path).Accesses.Add(new(r.TimeUtc, r.V("Profile", "User"), "", $"deschis (urmă {r.Kind}: dovadă mai slabă, nu arată cine și nici ce s-a făcut cu fișierul)", true, r.Evidence, r.Origin));
            }
        }
        foreach (var r in input.Records.Where(r => r.Kind == "USN"))
            if (roots.ClassifyRelative(r.Path) is { } path)
                File(path).Accesses.Add(new(r.TimeUtc, "", "", $"jurnal de modificări NTFS: {r.V("Reason")} (dovadă mai slabă: nu arată cine)", true, r.Evidence, r.Origin));

        ClassifiedFileReport? ByName(string name) =>
            classifiedNames.TryGetValue(name, out var p) ? File(p) : null;

        // 3a. Writes of a classified file name outside the folders: on removable media (or an unknown volume) = a copy.
        foreach (var (path, r, mask) in writesOutside)
        {
            var name = FileName(path);
            if (ByName(name) is not { } fr) continue;
            var drive = Drive(path);
            bool rem = removable.Contains(drive) || r.V("_Task") == "12802" || (drive.Length == 0 && path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase));
            var medium = MediumAt(input, r.TimeUtc);
            var chain = new List<string>(fr.Accesses.Where(a => !a.Weak && a.TimeUtc <= r.TimeUtc).TakeLast(3).Select(a => $"{T(a.TimeUtc)} {a.Who} {a.Access} ({a.Evidence})"))
            {
                $"{T(r.TimeUtc)} {Who(r)} scrie {path} cu {Proc(r)} ({r.Evidence})",
            };
            if (medium is not null) chain.Add($"suport conectat: {medium.Label} SN {medium.Serial} la {T(medium.TimeUtc)}");
            var channel = rem ? ExitChannel.RemovableMedia : ExitChannel.MoveOrRename;
            var target = rem ? $"{path}" + (medium is null ? "" : $" (SN {medium.Serial})") : path;
            var detail = rem
                ? $"Fișierul „{name}” a fost scris pe un suport amovibil ({path})."
                : $"Un fișier cu numele „{name}” a fost scris în afara folderelor clasificate ({path}).";
            AddExit(input, pending, fr, channel, r.TimeUtc, Who(r), target, detail, chain, [r.Origin], medium?.Serial ?? "", forceVerify: !rem && !Moved(r));
            bool Moved(AccessRecord w) => deletes.Any(d => FileName(d.Path).Equals(name, StringComparison.OrdinalIgnoreCase) && d.Rec.TimeUtc is { } td && w.TimeUtc is { } tw && Math.Abs((tw - td).TotalMinutes) <= 2);
        }

        // 3d. DELETE without a 4660 that confirms the removal: renamed or moved; if no write elsewhere explains it, the destination is unknown.
        foreach (var (path, r) in deletes)
        {
            var name = FileName(path);
            if (writesOutside.Any(w => FileName(w.Path).Equals(name, StringComparison.OrdinalIgnoreCase) && w.Rec.TimeUtc is { } tw && r.TimeUtc is { } td && Math.Abs((tw - td).TotalMinutes) <= 2)) continue;
            AddExit(input, pending, File(path), ExitChannel.MoveOrRename, r.TimeUtc, Who(r), "destinație neînregistrată",
                $"Fișierul „{name}” a fost deschis pentru ștergere, dar Windows nu a înregistrat eliminarea lui (4660): a fost redenumit sau mutat. Noua cale nu apare în jurnale.",
                [$"{T(r.TimeUtc)} {Who(r)} {MaskText(Mask(r))} cu {Proc(r)} ({r.Evidence})"], [r.Origin], "", forceVerify: true);
        }

        // 3d'. Change journal: a rename whose old name is in a classified folder and whose new name is elsewhere or hides the file.
        foreach (var g in usnByFile.Where(g => g.Key.Length > 0))
        {
            var recs = g.OrderBy(r => r.TimeUtc ?? DateTimeOffset.MinValue).ToList();
            for (int i = 0; i < recs.Count; i++)
            {
                if (!recs[i].V("Reason").Contains("RENAME_OLD_NAME", StringComparison.OrdinalIgnoreCase) || roots.ClassifyRelative(recs[i].Path) is not { } from) continue;
                var to = recs.Skip(i + 1).FirstOrDefault(r => r.V("Reason").Contains("RENAME_NEW_NAME", StringComparison.OrdinalIgnoreCase));
                if (to is null) continue;
                bool outside = roots.ClassifyRelative(to.Path) is null;
                bool hidden = !outside && !Path_Ext(FileName(from)).Equals(Path_Ext(FileName(to.Path)), StringComparison.OrdinalIgnoreCase);
                if (!outside && !hidden) continue;
                AddExit(input, pending, File(from), ExitChannel.MoveOrRename, to.TimeUtc, "", to.Path,
                    outside ? $"Jurnalul de modificări NTFS arată că „{FileName(from)}” a fost mutat în afara folderului clasificat, ca „{to.Path}”."
                            : $"Jurnalul de modificări NTFS arată că „{FileName(from)}” a fost redenumit cu altă extensie („{FileName(to.Path)}”), ceea ce îl poate ascunde.",
                    [$"{T(recs[i].TimeUtc)} USN {recs[i].Path} RENAME_OLD_NAME ({recs[i].Evidence})", $"{T(to.TimeUtc)} USN {to.Path} RENAME_NEW_NAME ({to.Evidence})"],
                    [recs[i].Origin, to.Origin], "", forceVerify: !outside, weak: true);
            }
        }
        static string Path_Ext(string n) => n.LastIndexOf('.') is var i and >= 0 ? n[i..] : "";

        // 3b. Printing: PrintService/Operational 307 document names.
        foreach (var r in events.Where(e => e.Channel.Contains("PrintService", StringComparison.OrdinalIgnoreCase) && e.EventId == 307))
        {
            var doc = r.V("Param2", "DocumentName");
            if (doc.Length == 0) continue;
            var exact = classifiedNames.Keys.FirstOrDefault(n => doc.Contains(n, StringComparison.OrdinalIgnoreCase));
            var stem = exact is null ? classifiedNames.Keys.FirstOrDefault(n => Stem(n).Length >= 4 && doc.Contains(Stem(n), StringComparison.OrdinalIgnoreCase)) : null;
            if ((exact ?? stem) is not { } name) continue;
            var who = r.V("Param3") is { Length: > 0 } u ? u : Who(r);
            var printer = r.V("Param5");
            AddExit(input, pending, File(classifiedNames[name]), ExitChannel.Print, r.TimeUtc, who, "imprimanta " + printer,
                $"Documentul „{doc}” a fost tipărit pe {printer} ({r.V("Param7")} octeți, {r.V("Param8")} pagini)." + (exact is null ? " Numele se potrivește doar parțial (fără extensie)." : ""),
                [$"{T(r.TimeUtc)} {who} tipărește „{doc}” pe {printer} ({r.Evidence})"], [r.Origin], "", forceVerify: exact is null);
        }

        // 3c. Archives and copy commands whose command line names a classified folder or file (4688 with the command line).
        foreach (var r in events.Where(e => e.Channel == "Security" && e.EventId == 4688))
        {
            var cmd = r.V("CommandLine");
            if (cmd.Length == 0) continue;
            var image = r.V("NewProcessName");
            bool archive = Archiver.IsMatch(image) || Archiver.IsMatch(cmd);
            bool copy = !archive && Copier.IsMatch(cmd);
            if (!archive && !copy) continue;
            string? hit = null;
            foreach (var f in input.Folders)
                if (cmd.Contains(Normalize(f.Path), StringComparison.OrdinalIgnoreCase) || (f.Aliases ?? []).Any(a => cmd.Contains(Normalize(a), StringComparison.OrdinalIgnoreCase))) { hit = Normalize(f.Path); break; }
            hit ??= classifiedNames.Keys.FirstOrDefault(n => n.Length >= 5 && cmd.Contains(n, StringComparison.OrdinalIgnoreCase)) is { } nm ? classifiedNames[nm] : null;
            if (hit is null) continue;
            var drives = DriveRoot.Matches(cmd).Select(m => m.Groups[1].Value.ToUpperInvariant() + ":").Distinct().ToList();
            var remDrive = drives.FirstOrDefault(removable.Contains);
            var unc = cmd.Contains(@"\\", StringComparison.Ordinal) && !cmd.Contains(@"\\?\", StringComparison.Ordinal);
            var who = Who(r);
            var medium = remDrive is null ? null : MediumAt(input, r.TimeUtc);
            var target = remDrive is not null ? remDrive + (medium is null ? "" : $" (SN {medium.Serial})") : unc ? "cale de rețea din comandă" : "destinație de verificat în comandă";
            AddExit(input, pending, File(hit), archive ? ExitChannel.Archive : ExitChannel.CopyCommand, r.TimeUtc, who, target,
                (archive ? "Un program de arhivare a primit în comandă " : "O comandă de copiere a primit ") + $"fișiere clasificate ({hit})" +
                (remDrive is not null ? $" și un suport amovibil ({remDrive}) ca destinație." : unc ? " și o cale de rețea." : "; destinația nu este pe un suport amovibil cunoscut."),
                [$"{T(r.TimeUtc)} {who} pornește {image}: {cmd} ({r.Evidence})"], [r.Origin], medium?.Serial ?? "", forceVerify: remDrive is null && !unc);
        }

        // 3e. Network shares (only where a network exists): 5145 on a share whose local path is a classified folder.
        if (input.NetworkApplies)
            foreach (var r in events.Where(e => e.Channel == "Security" && e.EventId == 5145))
            {
                var local = Normalize(r.V("ShareLocalPath"), input.VolumeMap);
                var rel = r.V("RelativeTargetName").Trim('\\');
                if (local.Length == 0 || rel.Length == 0) continue;
                var mask = Mask(r);
                if ((mask & (ReadData | WriteData | AppendData | Delete)) == 0) continue;
                if (roots.Classify(Normalize(local.TrimEnd('\\') + @"\" + rel), input.LongPath) is not { } path) continue;
                rep.AuditRecords++;
                var ip = r.V("IpAddress");
                File(path).Accesses.Add(new(r.TimeUtc, Who(r), "rețea (" + ip + ")", "prin partajare: " + MaskText(mask), false, r.Evidence, r.Origin));
                if ((mask & ReadData) == 0) continue;
                AddExit(input, pending, File(path), ExitChannel.NetworkShare, r.TimeUtc, Who(r), $"{r.V("ShareName")} de la {ip}",
                    $"Fișierul a fost citit prin rețea, din partajarea {r.V("ShareName")}, de la adresa {ip}.",
                    [$"{T(r.TimeUtc)} {Who(r)} de la {ip}: {MaskText(mask)} pe {r.V("ShareName")}\\{rel} ({r.Evidence})"], [r.Origin], "");
            }

        // 3f. A classified file name opened from a removable medium (shortcut / jump list): it was on the medium.
        foreach (var r in input.Records.Where(r => r.Kind is "LNK" or "JumpList"))
        {
            var norm = Normalize(r.Path, input.VolumeMap);
            if (norm.Length == 0 || roots.Classify(norm, input.LongPath) is not null) continue;
            bool rem = r.V("DriveType").Equals("REMOVABLE", StringComparison.OrdinalIgnoreCase) || removable.Contains(Drive(norm));
            if (!rem || ByName(FileName(norm)) is not { } fr) continue;
            var serial = r.V("VolumeSerial");
            AddExit(input, pending, fr, ExitChannel.OpenedFromMedium, r.TimeUtc, r.V("Profile", "User"), norm + (serial.Length > 0 ? $" (serie volum {serial})" : ""),
                $"Un fișier cu același nume („{FileName(norm)}”) a fost deschis de pe un suport amovibil, deci a fost pe acel suport. Urma este un fișier {r.Kind}: arată deschiderea, nu cine l-a copiat.",
                [$"{T(r.TimeUtc)} {r.Kind}: {norm} ({r.Evidence})"], [r.Origin], "", forceVerify: true, weak: true);
        }

        // 3g. Temporal coincidence: a classified file read, and a medium connected within the window, with no record of the file going anywhere.
        foreach (var fr in files.Values.ToList())
        {
            var reads = fr.Accesses.Where(a => !a.Weak && a.TimeUtc is not null && a.Access.Contains("citire", StringComparison.Ordinal)).ToList();
            if (reads.Count == 0) continue;
            foreach (var m in input.Media)
            {
                var near = reads.Where(a => (m.TimeUtc - a.TimeUtc!.Value).Duration() <= CoincidenceWindow).ToList();
                if (near.Count == 0) continue;
                if (fr.Exits.Any(e => e.TimeUtc is { } et && (et - m.TimeUtc).Duration() <= CoincidenceWindow && e.Channel != ExitChannel.TemporalCoincidence)) continue;
                var a = near.OrderBy(x => (m.TimeUtc - x.TimeUtc!.Value).Duration()).First();
                AddExit(input, pending, fr, ExitChannel.TemporalCoincidence, m.TimeUtc, a.Who, $"{m.Label} SN {m.Serial}".Trim(),
                    $"Fișierul a fost citit la {T(a.TimeUtc)}, iar suportul {m.Label} (SN {m.Serial}) a fost conectat la {T(m.TimeUtc)}: {TemporalNote}. Nicio înregistrare nu arată fișierul scris pe suport.",
                    [$"{T(a.TimeUtc)} {a.Who} {a.Access} ({a.Evidence})", $"{T(m.TimeUtc)} suport conectat: {m.Label} SN {m.Serial}"], [a.Origin], m.Serial, forceVerify: true);
            }
        }

        ResolveVerdicts(input, pending);
        rep.Files.AddRange(files.Values);
        if (rep.AuditRecords == 0)
            rep.Notes.Add("Nu există nicio înregistrare de audit (4656/4663/4660/4670/4907) pe folderele clasificate: fie nu s-a lucrat cu ele, fie auditul lor (SACL și subcategoria „File System”) nu era pornit. Windows nu înregistrează retroactiv.");
        if (input.Transfers is not { IsDefined: true })
            rep.Notes.Add("Registrul transferurilor nu are rânduri: nicio ieșire nu poate fi arătată ca aprobată.");
        return rep;
    }

    public const string SelfLabel = "activitate proprie a aplicației (calcul amprente)";

    /// <summary>The process id of a record ("0x4d2" or "1234"); -1 when absent.</summary>
    private static int Pid(AccessRecord r)
    {
        var v = r.V("ProcessId").Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(v.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return h;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : -1;
    }

    /// <summary>True when the record was made by the application's own process (same image path, same process id) inside one of its recorded reading windows.</summary>
    private static bool IsSelf(ClassifiedInput input, AccessRecord r)
    {
        if (input.SelfWindows.Count == 0 || r.TimeUtc is not { } t) return false;
        var image = Normalize(r.V("ProcessName"));
        var pid = Pid(r);
        var slack = TimeSpan.FromMinutes(1);
        return input.SelfWindows.Any(w => pid == w.ProcessId && t >= w.StartUtc - slack && t <= w.EndUtc + slack && image.Equals(Normalize(w.ImagePath), StringComparison.OrdinalIgnoreCase));
    }

    private static string T(DateTimeOffset? t) => t is { } v ? v.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : "ora necunoscută";

    private static MediumSeen? MediumAt(ClassifiedInput input, DateTimeOffset? t) =>
        t is not { } at ? null : input.Media.Where(m => m.TimeUtc <= at && at - m.TimeUtc <= TimeSpan.FromDays(1)).OrderByDescending(m => m.TimeUtc).FirstOrDefault();

    /// <summary>An exit whose verdict is decided at the end, in time order, so each register row is consumed by the earliest exits it covers.</summary>
    private sealed record PendingExit(ClassifiedFileReport File, int Index, ExitChannel Channel, DateTimeOffset? Time, string Serial, bool ForceVerify, string Detail);

    private static void AddExit(ClassifiedInput input, List<PendingExit> pending, ClassifiedFileReport fr, ExitChannel ch, DateTimeOffset? t, string who, string target, string detail,
                                List<string> chain, List<object?> origins, string serial, bool forceVerify = false, bool weak = false)
    {
        fr.Exits.Add(new ClassifiedExit(ch, ExitVerdict.ToVerify, t, who, fr.Path, target, detail, chain, "", origins, weak));
        pending.Add(new PendingExit(fr, fr.Exits.Count - 1, ch, t, serial, forceVerify, detail));
    }

    private static void ResolveVerdicts(ClassifiedInput input, List<PendingExit> pending)
    {
        var left = new Dictionary<TransferRow, int>(ReferenceEqualityComparer.Instance);
        foreach (var p in pending.OrderBy(p => p.Time ?? DateTimeOffset.MaxValue))
        {
            var m = MatchRegister(input, p.File.Path, p.Channel, p.Time, p.Serial, left);
            string detail = p.Detail, entry = "";
            ExitVerdict verdict;
            if (m.Row is { } row)
            {
                left[row] = left.GetValueOrDefault(row, Copies(row)) - 1;
                verdict = ExitVerdict.Approved;
                entry = $"{row.RegistrationNumber} ({row.DateTime}, {row.Person}, {row.MediumType} {row.MediumSerial}, aprobat de {row.Approver})";
                detail += $" Corespunde transferului înregistrat {entry}.";
            }
            else
            {
                verdict = p.ForceVerify || m.Incomplete ? ExitVerdict.ToVerify : ExitVerdict.Unjustified;
                detail += " " + (m.Note.Length > 0 ? m.Note : "Nu există o intrare corespunzătoare în registrul transferurilor.");
            }
            p.File.Exits[p.Index] = p.File.Exits[p.Index] with { Verdict = verdict, Detail = detail, RegisterEntry = entry };
        }
    }

    /// <summary>The number of copies (exits) a register row covers: the declared number, 1 when not stated.</summary>
    public static int Copies(TransferRow row) => int.TryParse(row.Copies.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;

    private static readonly char[] ContentSeparators = [';', ',', '\n', '\r', '|', '\t'];

    /// <summary>True when the row names exactly this file (a listed name or path whose file name is equal, case-insensitive) or carries exactly its SHA-256.</summary>
    internal static bool NamesFile(TransferRow row, string name, string hash)
    {
        if (row.Content.Split(ContentSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Any(tok => FileName(Normalize(tok)).Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        return hash.Length == 64 && row.Sha256.Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                                              .Any(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Exits that put the file on a medium: the register entry must name the medium and the medium must be known.</summary>
    private static bool MediumChannel(ExitChannel ch) => ch is ExitChannel.RemovableMedia or ExitChannel.Archive or ExitChannel.CopyCommand or ExitChannel.OpenedFromMedium or ExitChannel.TemporalCoincidence;

    internal sealed record RegisterMatch(TransferRow? Row, string Note, bool Incomplete);

    /// <summary>
    /// The register entry that covers an exit. A row can approve only when it: is an "ieșire"; names exactly this file (or its exact SHA-256); is complete
    /// (date and time, approver, the medium - serial or register number - or, for a print / network exit, the printer or destination); fits the channel;
    /// has the same medium serial; is within <see cref="RegisterTolerance"/> of the exit in the station's declared time zone; and still has copies left.
    /// An exit with an unknown time, or an unknown medium on a medium channel, is never approved. A row that names the file but is incomplete, or an exit that
    /// cannot be compared, gives DE VERIFICAT; a row that is spent or does not fit gives the reason.
    /// </summary>
    internal static RegisterMatch MatchRegister(ClassifiedInput input, string path, ExitChannel ch, DateTimeOffset? t, string serial, IDictionary<TransferRow, int>? left = null)
    {
        if (input.Transfers is not { IsDefined: true } reg) return new(null, "", false);
        var name = FileName(path);
        var hash = input.FileHashes?.GetValueOrDefault(path) ?? "";
        if (ch == ExitChannel.TemporalCoincidence)
        {
            // a coincidence is not an exit: it is never approved and never uses up a register entry; the entries naming the file are only pointed out
            var named = reg.Rows.Where(r => r.DirectionValue == TransferDirection.Out && NamesFile(r, name, hash)).Select(r => r.RegistrationNumber).ToList();
            return new(null, named.Count > 0 ? $"În registrul transferurilor fișierul apare la {string.Join(", ", named)}; verificați dacă acoperă această conectare." : "", false);
        }
        string mismatch = "", incomplete = "", spent = "";
        foreach (var row in reg.Rows.Where(r => r.DirectionValue == TransferDirection.Out && NamesFile(r, name, hash)))
        {
            var reasons = new List<string>();
            var kind = (row.MediumType + " " + row.Destination).ToLowerInvariant();
            bool printRow = kind.Contains("imprim") || kind.Contains("print") || kind.Contains("tipăr") || kind.Contains("tipar") || kind.Contains("hârtie") || kind.Contains("hartie");
            bool netRow = kind.Contains("rețea") || kind.Contains("retea") || kind.Contains("network") || kind.Contains("partajare");
            if (ch == ExitChannel.Print && !printRow) reasons.Add("intrarea nu este o tipărire");
            if (ch == ExitChannel.NetworkShare && !netRow) reasons.Add("intrarea nu este un transfer prin rețea");
            if (MediumChannel(ch) && printRow) reasons.Add("intrarea este o tipărire, nu un suport");
            if (serial.Length > 0 && row.MediumSerial.Trim().Length > 0 && MediaRegister.NormalizeSerial(row.MediumSerial) != MediaRegister.NormalizeSerial(serial))
                reasons.Add($"suportul diferă (registru {row.MediumSerial}, observat {serial})");
            bool timed = TransferRegister.TryDateTime(row.DateTime, out var local);
            if (timed && t is { } at && input.RegisterZone is { } zone)
            {
                var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
                if ((utc - at).Duration() > RegisterTolerance) reasons.Add($"ora diferă (registru {row.DateTime}, observat {at:yyyy-MM-dd HH:mm} UTC, toleranță {RegisterTolerance.TotalHours:0} ore)");
            }
            if (reasons.Count > 0) { mismatch = $"În registrul transferurilor există {row.RegistrationNumber} pentru acest fișier, dar nu acoperă această ieșire: {string.Join("; ", reasons)}."; continue; }

            var missing = new List<string>();
            if (!timed) missing.Add("data și ora transferului");
            if (row.Approver.Trim().Length == 0) missing.Add("aprobarea");
            if (MediumChannel(ch) ? row.MediumSerial.Trim().Length == 0 && row.MediumRegistration.Trim().Length == 0
                                  : row.MediumSerial.Trim().Length == 0 && row.MediumRegistration.Trim().Length == 0 && row.Destination.Trim().Length == 0)
                missing.Add(MediumChannel(ch) ? "seria sau numărul suportului" : "imprimanta sau destinația");
            if (MediumChannel(ch) && row.MediumSerial.Trim().Length == 0 && row.MediumRegistration.Trim().Length > 0 && serial.Length > 0)
                missing.Add("seria suportului (doar numărul din registrul de medii nu se poate compara cu seria observată)");
            var unknown = new List<string>();
            if (t is null) unknown.Add("ora ieșirii nu este cunoscută");
            if (MediumChannel(ch) && serial.Length == 0) unknown.Add("seria suportului pe care a ieșit fișierul nu este cunoscută");
            if (input.RegisterZone is null) unknown.Add("fusul orar al stației nu este declarat, deci ora din registru nu poate fi comparată");
            if (missing.Count > 0) { incomplete = $"Ieșirea nu poate fi confirmată ca aprobată: înregistrare incompletă în registru ({row.RegistrationNumber}: lipsește {string.Join(", ", missing)})."; continue; }
            if (unknown.Count > 0) { incomplete = $"Intrarea {row.RegistrationNumber} din registru numește fișierul, dar ieșirea nu poate fi confirmată: {string.Join("; ", unknown)}."; continue; }
            var remaining = left?.TryGetValue(row, out var n) == true ? n : Copies(row);
            if (remaining <= 0) { spent = $"Intrarea {row.RegistrationNumber} din registru acoperă {Copies(row)} {(Copies(row) == 1 ? "copie" : "copii")}, deja folosite de ieșiri anterioare: această ieșire este în plus."; continue; }
            return new(row, "", false);
        }
        if (incomplete.Length > 0) return new(null, incomplete, true);
        return new(null, spent.Length > 0 ? spent : mismatch, false);
    }

    public static string VerdictText(ExitVerdict v) => v switch
    {
        ExitVerdict.Unjustified => "NEJUSTIFICATĂ",
        ExitVerdict.Approved => "APROBATĂ",
        _ => "DE VERIFICAT",
    };

    public static string ChannelText(ExitChannel c) => c switch
    {
        ExitChannel.RemovableMedia => "scriere pe suport amovibil",
        ExitChannel.Print => "tipărire",
        ExitChannel.Archive => "arhivare",
        ExitChannel.CopyCommand => "comandă de copiere",
        ExitChannel.MoveOrRename => "mutare sau redenumire",
        ExitChannel.NetworkShare => "citire prin rețea (partajare)",
        ExitChannel.OpenedFromMedium => "deschis de pe un suport amovibil",
        _ => TemporalNote,
    };
}
