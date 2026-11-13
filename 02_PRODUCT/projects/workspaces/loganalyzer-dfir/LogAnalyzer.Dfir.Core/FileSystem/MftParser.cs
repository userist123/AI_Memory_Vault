using System.Globalization;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// $MFT exported as a file (the $DATA of record 0). Streams the file twice: pass 1 builds the directory index (for paths), the set of the newest deleted
/// records and the extension-record names; pass 2 emits by the policy in docs/dfir/NTFS_MFT_DESIGN.md ("Politica de emitere").
/// Every byte is hostile: nothing read from the input sizes an allocation without a cap and a damaged record is counted, never fatal.
/// </summary>
public sealed class MftParser : EvidenceParserBase
{
    public const string SourceTypeName = "ntfs_mft";
    public const string FingerprintName = "ntfs_mft";
    public const int MaxDeletedEvents = 20_000, MaxMismatchEvents = 5_000, MaxRecentEvents = 100_000;
    public const int MaxIncompleteEvents = 5_000, MaxSystemMismatchRecentEvents = 20_000;
    public const long MaxIndexNameChars = 64L * 1024 * 1024;
    public const int MaxDirectoryIndex = 2_000_000, MaxExtensionNames = 200_000, MaxPathDepth = 64;
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);
    private readonly int _maxDirectoryIndex;
    private readonly long _maxIndexNameChars;

    /// <summary>The limits default to the production caps; tests lower them to reach the over-cap paths.</summary>
    public MftParser(int maxDirectoryIndex = MaxDirectoryIndex, long maxIndexNameChars = MaxIndexNameChars)
    { _maxDirectoryIndex = maxDirectoryIndex; _maxIndexNameChars = maxIndexNameChars; }

    private static readonly FileNameAttribute NoName = new(default, 0, 0, 0, 0, 0, 0, 0, "", 1, false);
    private static readonly string[] BootSiblings = ["$Boot", "boot.bin", "$Boot.bin"];

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "MftParser", Version = "1.0", Artifact = "$MFT exportat (tabela principală de fișiere NTFS)",
        SourceTypes = [SourceTypeName], FileNames = ["$MFT", ".mft"], Fingerprints = [FingerprintName],
        MaxInputBytes = 0,   // streamed record by record
        SupportedOs = "Windows (NTFS 3.x), înregistrări de 1.024 sau 4.096 octeți",
        FormatVersions = ["NTFS 3.1 FILE record, fixup 512 octeți, $STANDARD_INFORMATION 48/72, $FILE_NAME, înregistrări de extensie legate prin base reference"],
        Limitations =
        [
            "Se emit doar: înregistrări șterse (cel mult 20.000, cele mai noi), nepotriviri $SI/$FN (cel mult 5.000), fișiere create/modificate în 30 de zile înainte de achiziție (cel mult 100.000) și un rezumat; restul înregistrărilor sunt doar numărate.",
            "Nepotrivirile $SI/$FN ale directoarelor și ale fișierelor din arborii gestionați de sistem (\\Windows\\, \\Program Files\\, \\Program Files (x86)\\, \\ProgramData\\Microsoft\\, \\Users\\Default\\, \\$Recycle.Bin\\, \\PerfLogs\\, \\Recovery\\, \\System Volume Information\\) sunt doar numărate în rezumat (tiparul normal al instalării și actualizărilor), plus \\Users\\*\\AppData\\Local\\Packages\\ (\\Users\\Public\\ nu este arbore de sistem); cele cu $FILE_NAME creat în ultimele 30 de zile merg pe un canal separat (categoria system_mismatch_recent, cel mult 20.000, cele mai noi), folosit doar de calea cu jurnalul USN.",
            "O nepotrivire $SI/$FN este un semnal slab (copiere, mutare între volume, arhive o produc legitim); nu dovedește timestomp.",
            "O înregistrare ștearsă păstrează numele și orele de la ultima folosire; calea ei depinde de părintele încă în uz cu aceeași secvență.",
            "Parserul NU citește $ATTRIBUTE_LIST (nici rezident, nici nerezident). Leagă înregistrările de extensie de înregistrarea de bază prin base reference și secvența ei (extensia trebuie să fie în uz) și atașează doar $FILE_NAME din ele; alte atribute aflate în extensii nu sunt citite.",
            "Fără ADS, $EA, $Bitmap, $Secure, conținut șters sau $I30.",
            "Înregistrări cu fixup nepotrivit, BAAD sau antet incoerent sunt numărate (MalformedRecords) și nu produc evenimente. O înregistrare cu atribute ilizibile (lungime nevalidă etc.) este numărată; ce s-a citit înainte de problemă se emite cu AttributeWalk=incomplete.",
            "Indexul de căi are plafon de directoare și de caractere de nume; peste plafon, căile rămase încep cu „…\\”.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "MftParserTests: Corpus/Ntfs (mft.bin scris de Windows 11) comparat cu truth.json (Get-Item/fsutil): căi, mărimi, ore $SI la 100 ns, ore $FN ale fișierului cu timestomp, 17 înregistrări libere; plus 11 probe ostile (P1–P11) și corecturile din review (MftParserReviewTests)",
    };

    /// <summary>Trees the system manages (installation, servicing, updates): a $SI/$FN mismatch there is the normal pattern of an image, not a signal by itself.</summary>
    public static readonly IReadOnlyList<string> SystemTrees =
        ["\\Windows\\", "\\Program Files\\", "\\Program Files (x86)\\", "\\ProgramData\\Microsoft\\", "\\Users\\Default\\", "\\$Recycle.Bin\\", "\\PerfLogs\\", "\\Recovery\\", "\\System Volume Information\\", PackagesRoot];

    public const string PackagesRoot = "\\Users\\*\\AppData\\Local\\Packages\\";

    /// <summary>The system tree (prefix, case-insensitive) a reconstructed path is in; null otherwise. An incomplete path (starting with an ellipsis) is never in one.</summary>
    /// <summary>
    /// Places inside the system trees that any user can write to and that are usual staging places (review 2026-11-13, S3 round 2): never filtered,
    /// a mismatch there is examined like anywhere else.
    /// </summary>
    public static readonly IReadOnlyList<string> UserWritableInsideSystemTrees =
        ["\\Windows\\Temp\\", "\\Windows\\Tasks\\", "\\Windows\\Tracing\\", "\\Windows\\System32\\spool\\drivers\\color\\", "\\Windows\\System32\\Tasks\\", "\\$Recycle.Bin\\"];

    public static string? SystemTreeOf(string path)
    {
        foreach (var w in UserWritableInsideSystemTrees) if (path.StartsWith(w, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var t in SystemTrees) if (path.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return t;
        // \Users\<any profile>\AppData\Local\Packages\ (app data the system provisions); \Users\Public\ is deliberately not a system tree
        const string Users = "\\Users\\", Tail = "\\AppData\\Local\\Packages\\";
        if (path.StartsWith(Users, StringComparison.OrdinalIgnoreCase))
        {
            int end = path.IndexOf('\\', Users.Length);
            if (end > Users.Length && string.Compare(path, end, Tail, 0, Tail.Length, StringComparison.OrdinalIgnoreCase) == 0) return PackagesRoot;
            // The shell's own files that the image provisions in \Users and \Users\Public (lab WS01, clean install: the only 10 mismatches
            // left outside the trees). Exact names only: any other file in \Users\Public stays examined (a usual staging place for attackers).
            if (IsShellDefault(path)) return ShellDefaultsRoot;
        }
        return null;
    }

    public const string ShellDefaultsRoot = "\\Users\\(desktop.ini, RecordedTV.library-ms)";

    private static bool IsShellDefault(string path)
    {
        var name = Path.GetFileName(path);
        // A .library-ms in \Users\Public is a known NTLM-hash lure (review 2026-11-13, S3 round 2), so only the ONE default the image provisions,
        // at its exact path, is a shell default; any other .library-ms stays examined.
        if (path.Equals("\\Users\\Public\\Libraries\\RecordedTV.library-ms", StringComparison.OrdinalIgnoreCase)) return true;
        if (!name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return false;
        return path.Equals("\\Users\\desktop.ini", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("\\Users\\Public\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSystemTreePath(string path) => SystemTreeOf(path) is not null;

    private sealed record DirEntry(ushort Sequence, MftReference Parent, string Name);
    private sealed record ExtNames(ulong BaseNumber, ushort BaseSequence, ulong ExtNumber, List<FileNameAttribute> Names);

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        int recordSize = ResolveRecordSize(fullPath, fs, out string sizeSource, out string? bootGap);
        var buf = new byte[recordSize];

        // ---- pass 1 ----
        var dirs = new Dictionary<ulong, DirEntry>();
        var ext = new Dictionary<ulong, List<ExtNames>>();   // base number → names found in extension records
        int extCount = 0;
        var deletedHeap = new PriorityQueue<ulong, (long Time, long NegNumber)>();   // min-heap on the chosen deletion time (implausible = lowest), then on -number: keeps the newest MaxDeletedEvents, lowest numbers on ties
        long attrProblems = 0, implausibleDeleted = 0, nameChars = 0, overCount = 0, overBudget = 0;
        long total = 0, inUse = 0, deleted = 0, directories = 0, fixupErrors = 0, baad = 0, invalid = 0, blank = 0, dirOverflow = 0, extensions = 0;
        bool truncatedTail = false;
        ulong number = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int got = fs.ReadAtLeast(buf, recordSize, throwOnEndOfStream: false);
            if (got == 0) break;
            if (got < recordSize) { truncatedTail = true; break; }
            var rec = NtfsFileRecord.Parse(buf, recordSize);
            total++;
            switch (rec.State)
            {
                case NtfsRecordState.NotRecord: blank++; number++; continue;
                case NtfsRecordState.Torn: fixupErrors++; result.MalformedRecords++; number++; continue;
                case NtfsRecordState.Baad: baad++; result.MalformedRecords++; number++; continue;
                case NtfsRecordState.Invalid: invalid++; result.MalformedRecords++; number++; continue;
            }
            if (rec.Attributes.Count >= 0 && rec.AttributeProblems > 0) { attrProblems++; result.MalformedRecords++; }
            if (rec.InUse) inUse++; else deleted++;
            if (rec.IsDirectory && rec.InUse) directories++;
            if (!rec.IsBaseRecord)
            {
                extensions++;
                if (rec.InUse && extCount < MaxExtensionNames)
                {
                    var names = Names(rec);
                    if (names.Count > 0)
                    {
                        if (!ext.TryGetValue(rec.BaseReference.Number, out var l)) ext[rec.BaseReference.Number] = l = [];
                        l.Add(new ExtNames(rec.BaseReference.Number, rec.BaseReference.Sequence, number, names)); extCount++;
                    }
                }
                number++; continue;
            }
            var fns = Names(rec);
            if (rec.IsDirectory && rec.InUse && fns.Count > 0)
            {
                var best = Best(fns);
                if (dirs.Count >= _maxDirectoryIndex) { dirOverflow++; overCount++; }
                else if (nameChars + best.Name.Length > _maxIndexNameChars) { dirOverflow++; overBudget++; }
                else { dirs[number] = new DirEntry(rec.Sequence, best.Parent, best.Name); nameChars += best.Name.Length; }
            }
            if (!rec.InUse && fns.Count > 0)
            {
                long t = DeletedTime(StandardInfo(rec), Best(fns));
                if (t == long.MinValue) implausibleDeleted++;
                deletedHeap.Enqueue(number, (t, -(long)number));
                if (deletedHeap.Count > MaxDeletedEvents) deletedHeap.Dequeue();
            }
            number++;
        }
        var deletedKeep = new HashSet<ulong>();
        long keptImplausible = 0;
        while (deletedHeap.TryDequeue(out var n, out var pr)) { deletedKeep.Add(n); if (pr.Time == long.MinValue) keptImplausible++; }
        if (truncatedTail)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, "Fișierul $MFT se termină în mijlocul unei înregistrări",
                $"Înregistrările de după offset {total * recordSize:N0} lipsesc", "Reexportul $MFT întreg", "Posibil"));
        }
        if (bootGap is not null)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, bootGap,
                "Mărimea înregistrării a fost dedusă din antetele FILE", "Sectorul de boot al volumului acestui $MFT", "Posibil"));
        if (attrProblems > 0)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, $"{attrProblems:N0} înregistrări cu atribute ilizibile (lungime, nume sau valoare în afara înregistrării)",
                "Atributele de după problemă nu au fost citite; ce s-a citit înainte se emite cu AttributeWalk=incomplete", "Un alt export al aceluiași $MFT", "Posibil"));
        if (dirOverflow > 0)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, $"{dirOverflow:N0} directoare peste plafonul indexului de căi ({overCount:N0} peste {_maxDirectoryIndex:N0} directoare, {overBudget:N0} peste bugetul de {_maxIndexNameChars:N0} caractere de nume)",
                "Unele căi încep cu „…\\”", "Un export filtrat", "Posibil"));

        // ---- pass 2 ----
        fs.Position = 0;
        DateTimeOffset? acquired = item.AcquiredAtUtc == default ? null : item.AcquiredAtUtc;
        long windowStart = acquired is { } a ? a.UtcDateTime.Add(-RecentWindow).ToFileTimeUtc() : long.MaxValue;
        long windowEnd = acquired is { } a2 ? a2.UtcDateTime.ToFileTimeUtc() : long.MinValue;
        long channelTotal = 0, channelEmitted = 0; var channelHeap = new PriorityQueue<ulong, (long Time, long NegNumber)>();
        long mismatchSystem = 0, mismatchDirs = 0; var systemByRoot = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long emittedDeleted = 0, emittedMismatch = 0, emittedRecent = 0, mismatchOver = 0, recentOver = 0, mismatchTotal = 0, recentTotal = 0, deletedOver = 0, emittedIncomplete = 0, incompleteOver = 0, siModifiedOnlyTotal = 0;
        number = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int got = fs.ReadAtLeast(buf, recordSize, throwOnEndOfStream: false);
            if (got < recordSize) break;
            ulong num = number++;
            var rec = NtfsFileRecord.Parse(buf, recordSize);
            if (rec.State != NtfsRecordState.Ok || !rec.IsBaseRecord) continue;
            var fns = Names(rec);
            if (ext.TryGetValue(num, out var extList))
                foreach (var e in extList.Where(e => e.BaseSequence == rec.Sequence).Take(64)) fns.AddRange(e.Names);
            var si = StandardInfo(rec);
            if (!rec.InUse)
            {
                if (fns.Count == 0 || !deletedKeep.Contains(num)) { if (fns.Count > 0) deletedOver++; continue; }
                Emit("deleted", rec, num, fns, si);
                emittedDeleted++;
                continue;
            }
            if (fns.Count == 0 || si is null)
            {
                if (rec.AttributeProblems > 0 && (fns.Count > 0 || si is not null))
                {
                    if (emittedIncomplete < MaxIncompleteEvents) { Emit("incomplete", rec, num, fns, si); emittedIncomplete++; }
                    else incompleteOver++;
                }
                continue;
            }
            var signals = Signals(si, fns);
            bool filteredMismatch = false;
            if (SiModifiedBeforeFnCreated(si, fns)) siModifiedOnlyTotal++;   // the pattern of any copy or extraction: counted, never emitted by itself
            if (signals.Count > 0)
            {
                mismatchTotal++;
                // Directories and files in system-managed trees mismatch by the thousand on every installation: counted, not emitted (a recent one still
                // reaches the USN path through the "recent" category below). The cap applies to the rest.
                string? root = null; filteredMismatch = true;
                if (rec.IsDirectory) mismatchDirs++;
                else if ((root = SystemTreeOf(ParentPath(Best(fns).Parent, dirs).Path + Best(fns).Name)) is not null) { mismatchSystem++; systemByRoot[root] = systemByRoot.GetValueOrDefault(root) + 1; }
                else
                {
                    if (emittedMismatch < MaxMismatchEvents) { Emit("mismatch", rec, num, fns, si, signals); emittedMismatch++; }
                    else mismatchOver++;
                    continue;
                }
                // Separate channel: a filtered mismatch whose $FN creation is in the 30 days before acquisition (what a timestomp of a system file looks like in
                // the MFT) is kept, newest first, under its own cap, only for the USN path of the rule (never Medium or Low).
                if (acquired is not null && InWindow(Best(fns).Created, windowStart, windowEnd))
                {
                    channelTotal++;
                    channelHeap.Enqueue(num, (Best(fns).Created, -(long)num));
                    if (channelHeap.Count > MaxSystemMismatchRecentEvents) channelHeap.Dequeue();
                }
            }
            if (acquired is not null)
            {
                // A timestomp hides the $SI times, not the $FN creation or the MFT change time: any of them in the window keeps the file in view
                // (the rule NTFS-TIMESTOMP reads the $SI times of such a record against the USN journal; after a rename Windows copies $SI into $FN).
                // A filtered mismatch (directory / system tree) has an old $SI, a $FN created at installation and an $SI change time touched by the system
                // (measured on a fresh install: 99,049 extra "recent" records, over the cap, if the $FN creation or the change time counted). For those only a
                // recent $SI creation or modification keeps them in view; the others are counted in the summary only.
                bool recent = filteredMismatch ? InWindow(si.Created, windowStart, windowEnd) || InWindow(si.Modified, windowStart, windowEnd)
                    : InWindow(si.Created, windowStart, windowEnd) || InWindow(si.Modified, windowStart, windowEnd)
                              || InWindow(Best(fns).Created, windowStart, windowEnd) || InWindow(si.MftModified, windowStart, windowEnd);
                if (!recent) continue;
                recentTotal++;
                if (emittedRecent < MaxRecentEvents) { Emit("recent", rec, num, fns, si); emittedRecent++; }
                else recentOver++;
            }
        }

        // ---- pass 3: the channel (only when there is something to emit) ----
        if (channelHeap.Count > 0)
        {
            var keep = new HashSet<ulong>();
            while (channelHeap.TryDequeue(out var cn, out _)) keep.Add(cn);
            fs.Position = 0;
            number = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int got = fs.ReadAtLeast(buf, recordSize, throwOnEndOfStream: false);
                if (got < recordSize) break;
                ulong num = number++;
                if (!keep.Contains(num)) continue;
                var rec = NtfsFileRecord.Parse(buf, recordSize);
                if (rec.State != NtfsRecordState.Ok || !rec.IsBaseRecord || !rec.InUse) continue;
                var fns = Names(rec);
                if (ext.TryGetValue(num, out var extList))
                    foreach (var e in extList.Where(e => e.BaseSequence == rec.Sequence).Take(64)) fns.AddRange(e.Names);
                var si = StandardInfo(rec);
                if (fns.Count == 0 || si is null) continue;
                Emit("system_mismatch_recent", rec, num, fns, si, Signals(si, fns));
                channelEmitted++;
            }
        }

        sink.Add(new TimelineEvent
        {
            Time = acquired is { } acq ? Timestamp.FromUtc(acq.UtcDateTime, acq.ToString("O", CultureInfo.InvariantCulture), "data achiziției probei") : Timestamp.Unknown(),
            Source = "MFT", EvidenceId = item.EvidenceId, Process = "", Path = "",
            Summary = $"$MFT: {total:N0} înregistrări ({inUse:N0} în uz, {deleted:N0} șterse, {directories:N0} directoare), {result.MalformedRecords:N0} nevalide",
            TimeSemantics = "rezumatul analizei $MFT", TemporalType = TemporalType.Derived, Classification = Classification.Direct, Confidence = Confidence.High,
            Locator = "summary",
            Fields =
            {
                ["Category"] = "summary", ["TotalRecords"] = total.ToString(CultureInfo.InvariantCulture), ["InUse"] = inUse.ToString(CultureInfo.InvariantCulture),
                ["Deleted"] = deleted.ToString(CultureInfo.InvariantCulture), ["Directories"] = directories.ToString(CultureInfo.InvariantCulture),
                ["FixupErrors"] = fixupErrors.ToString(CultureInfo.InvariantCulture), ["BaadRecords"] = baad.ToString(CultureInfo.InvariantCulture),
                ["InvalidRecords"] = invalid.ToString(CultureInfo.InvariantCulture), ["BlankRecords"] = blank.ToString(CultureInfo.InvariantCulture),
                ["ExtensionRecords"] = extensions.ToString(CultureInfo.InvariantCulture), ["RecordSize"] = recordSize.ToString(CultureInfo.InvariantCulture),
                ["RecordSizeSource"] = sizeSource, ["TruncatedFile"] = truncatedTail ? "true" : "false",
                ["DeletedEmitted"] = emittedDeleted.ToString(CultureInfo.InvariantCulture),
                ["DeletedImplausibleEmitted"] = keptImplausible.ToString(CultureInfo.InvariantCulture), ["DeletedImplausibleNotEmitted"] = (implausibleDeleted - keptImplausible).ToString(CultureInfo.InvariantCulture),
                ["AttributeWalkProblems"] = attrProblems.ToString(CultureInfo.InvariantCulture), ["IncompleteEmitted"] = emittedIncomplete.ToString(CultureInfo.InvariantCulture), ["DeletedNotEmitted"] = deletedOver.ToString(CultureInfo.InvariantCulture),
                ["MismatchTotal"] = mismatchTotal.ToString(CultureInfo.InvariantCulture), ["MismatchEmitted"] = emittedMismatch.ToString(CultureInfo.InvariantCulture),
                ["MismatchSystemTrees"] = mismatchSystem.ToString(CultureInfo.InvariantCulture), ["MismatchDirectories"] = mismatchDirs.ToString(CultureInfo.InvariantCulture),
                ["MismatchSystemTreesByRoot"] = string.Join(";", systemByRoot.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")),
                ["SiModifiedBeforeFnCreatedTotal"] = siModifiedOnlyTotal.ToString(CultureInfo.InvariantCulture),
                ["SystemMismatchRecentTotal"] = channelTotal.ToString(CultureInfo.InvariantCulture), ["SystemMismatchRecentEmitted"] = channelEmitted.ToString(CultureInfo.InvariantCulture),
                ["SystemMismatchRecentCategory"] = acquired is null ? "omisă: proba nu are data achiziției" : "emisă",
                ["RecentTotal"] = recentTotal.ToString(CultureInfo.InvariantCulture), ["RecentEmitted"] = emittedRecent.ToString(CultureInfo.InvariantCulture),
                ["RecentCategory"] = acquired is null ? "omisă: proba nu are data achiziției" : "emisă",
            },
            Notes = acquired is null ? "Fără dată de achiziție: categoria „fișiere recente” nu a fost emisă." : "",
        });
        result.Records++;
        if (channelTotal > channelEmitted)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial,
                $"{channelTotal - channelEmitted:N0} nepotriviri $SI/$FN din arborii de sistem sau directoare, create în ultimele 30 de zile, peste plafonul canalului de {MaxSystemMismatchRecentEvents:N0}",
                "Doar cele mai noi după $FILE_NAME creare sunt emise pentru calea cu jurnalul USN", "Un export filtrat", "Posibil"));
        if (deletedOver > 0 || mismatchOver > 0 || recentOver > 0 || incompleteOver > 0)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial,
                $"Plafoane de emitere atinse: {deletedOver:N0} șterse neemise (din care {implausibleDeleted - keptImplausible:N0} cu ore neplauzibile; {keptImplausible:N0} dintre cele emise au ore neplauzibile), {mismatchOver:N0} nepotriviri, {recentOver:N0} recente, {incompleteOver:N0} incomplete neemise", "Evenimentele neemise sunt doar numărate în rezumat", "Un export filtrat", "Posibil"));

        // ---- local helpers ----
        void Emit(string category, NtfsFileRecord rec, ulong num, List<FileNameAttribute> fns, StandardInformation? si, List<string>? signals = null)
        {
            bool named = fns.Count > 0;
            bool mm = category is "mismatch" or "system_mismatch_recent";
            var best = named ? Best(fns) : NoName;
            var (parentPath, flag) = named ? ParentPath(best.Parent, dirs) : ("", "fără $FILE_NAME citibil");
            string path = parentPath + best.Name;
            var (size, sizeKnown) = SizeOf(rec);
            var reference = MftReference.From(num, rec.Sequence);
            long when = category == "deleted" ? (DeletedTime(si, best) is var dt && dt != long.MinValue ? dt : best.MftModified)
                      : mm || category == "incomplete" ? (si?.Created ?? best.Created)
                      : si is null ? 0
                      : InWindow(si.Modified, windowStart, windowEnd) ? si.Modified
                      : InWindow(si.Created, windowStart, windowEnd) ? si.Created
                      : InWindow(best.Created, windowStart, windowEnd) ? best.Created
                      : si.MftModified;
            var ev = new TimelineEvent
            {
                Time = NtfsTime.ToTimestamp(when, "$MFT, FILETIME UTC"), Source = "MFT", EvidenceId = item.EvidenceId, Process = "", Path = path,
                Summary = category switch
                {
                    "deleted" => $"$MFT: înregistrare ștearsă {SafeText(path)}",
                    "incomplete" => $"$MFT: înregistrarea #{num} are atribute ilizibile ({SafeText(path)})",
                    "mismatch" or "system_mismatch_recent" => $"$MFT: nepotrivire $SI/$FN la {SafeText(path)} ({string.Join(", ", signals ?? [])})",
                    _ => $"$MFT: fișier recent {SafeText(path)}",
                },
                TimeSemantics = category switch { "deleted" => "ultima modificare a înregistrării MFT (înregistrare nefolosită)", "mismatch" or "system_mismatch_recent" => "$STANDARD_INFORMATION creare", _ => "$STANDARD_INFORMATION creare/modificare" },
                TemporalType = TemporalType.Historical,
                Classification = mm ? Classification.Candidate : Classification.Direct,
                Confidence = mm ? Confidence.Low : category == "incomplete" ? Confidence.Medium : Confidence.High,
                Locator = $"MFT#{num}",
                Notes = category == "incomplete" ? "Atributele înregistrării nu au putut fi citite integral; ce s-a citit înainte de problemă este raportat." : mm ? "Semnal slab: o arhivă care restaurează ora de creare produce aceeași nepotrivire; nu dovedește timestomp (regula NTFS-TIMESTOMP o judecă împreună cu jurnalul USN)." : "",
            };
            var f = ev.Fields;
            f["Category"] = category; f["RecordNumber"] = num.ToString(CultureInfo.InvariantCulture);
            f["Sequence"] = rec.Sequence.ToString(CultureInfo.InvariantCulture); f["FileId"] = reference.FileId;
            f["InUse"] = rec.InUse ? "true" : "false"; f["IsDirectory"] = rec.IsDirectory ? "true" : "false";
            f["FileName"] = best.Name; f["Namespace"] = best.Namespace.ToString(CultureInfo.InvariantCulture);
            f["ParentFileId"] = best.Parent.FileId; f["PathComplete"] = flag.Length == 0 ? "true" : "false";
            if (flag.Length > 0) f["PathProblem"] = flag;
            if (fns.Any(n => n.NameHadInvalidUtf16)) f["NameInvalidUtf16"] = "true";
            f["Size"] = size.ToString(CultureInfo.InvariantCulture); f["SizeKnown"] = sizeKnown ? "true" : "false";
            f["FileNameCount"] = fns.Count.ToString(CultureInfo.InvariantCulture);
            f["HasDosName"] = fns.Any(n => n.Namespace is 2 or 3) ? "true" : "false";   // a short (8.3) name exists: NTFS tunnelling is possible for this file
            if (si is not null)
            {
                f["SiCreated"] = NtfsTime.Text(si.Created); f["SiModified"] = NtfsTime.Text(si.Modified);
                f["SiMftModified"] = NtfsTime.Text(si.MftModified); f["SiAccessed"] = NtfsTime.Text(si.Accessed);
            }
            if (named)
            {
                f["FnCreated"] = NtfsTime.Text(best.Created); f["FnModified"] = NtfsTime.Text(best.Modified);
                f["FnMftModified"] = NtfsTime.Text(best.MftModified); f["FnAccessed"] = NtfsTime.Text(best.Accessed);
            }
            if (rec.AttributeProblems > 0) f["AttributeWalk"] = "incomplete";
            if (signals is { Count: > 0 }) f["Signals"] = string.Join(";", signals);
            sink.Add(ev);
            result.Records++;
        }
    }

    /// <summary>
    /// A file name for a one-line text: every Unicode control (Cc) and format (Cf) character, U+034F, and the line and paragraph separators
    /// are shown as backslash-u plus four hex digits (backslash-U plus eight for characters outside the BMP). The verbatim name stays in Path and Fields.
    /// </summary>
    public static string SafeText(string s)
    {
        System.Text.StringBuilder? sb = null;
        int index = 0;
        foreach (var r in s.EnumerateRunes())
        {
            bool bad = System.Text.Rune.IsControl(r) || System.Text.Rune.GetUnicodeCategory(r) == System.Globalization.UnicodeCategory.Format
                       || r.Value is 0x034F or 0x2028 or 0x2029;
            if (bad)
            {
                sb ??= new System.Text.StringBuilder(s[..index], s.Length + 16);
                sb.Append((char)92);
                if (r.Value <= 0xFFFF) sb.Append('u').Append(r.Value.ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append('U').Append(r.Value.ToString("X8", CultureInfo.InvariantCulture));
            }
            else sb?.Append(r.ToString());
            index += r.Utf16SequenceLength;
        }
        return sb?.ToString() ?? s;
    }

    /// <summary>Time that ranks and dates a deleted record: $SI MFT-modified if plausible, else $FN MFT-modified if plausible, else long.MinValue (ranks last).</summary>
    private static long DeletedTime(StandardInformation? si, FileNameAttribute best)
        => si is not null && NtfsTime.IsPlausible(si.MftModified) ? si.MftModified : NtfsTime.IsPlausible(best.MftModified) ? best.MftModified : long.MinValue;

    private static bool InWindow(long ft, long start, long end) => NtfsTime.IsPlausible(ft) && ft >= start && ft <= end;

    internal static List<FileNameAttribute> Names(NtfsFileRecord rec)
    {
        var l = new List<FileNameAttribute>();
        foreach (var a in rec.Attributes)
            if (a.Type == NtfsAttributeType.FileName && !a.NonResident && a.Name.Length == 0 && FileNameAttribute.TryParse(rec.ValueOf(a)) is { } fn) l.Add(fn);
        return l;
    }

    private static StandardInformation? StandardInfo(NtfsFileRecord rec)
    {
        foreach (var a in rec.Attributes)
            if (a.Type == NtfsAttributeType.StandardInformation && !a.NonResident) return StandardInformation.TryParse(rec.ValueOf(a));
        return null;
    }

    private static FileNameAttribute Best(List<FileNameAttribute> fns)
    {
        var best = fns[0];
        foreach (var n in fns) if (n.Rank > best.Rank) best = n;
        return best;
    }

    /// <summary>Size of the unnamed $DATA (the real size; $FILE_NAME can be stale). Directories and records without $DATA: 0, size unknown flagged only when $DATA exists but is not usable.</summary>
    private static (long Size, bool Known) SizeOf(NtfsFileRecord rec)
    {
        foreach (var a in rec.Attributes)
        {
            if (a.Type != NtfsAttributeType.Data || a.Name.Length != 0) continue;
            if (!a.NonResident) return (a.ValueLength, true);
            if (a.LowestVcn != 0 || a.RealSize < 0) continue;
            return (a.RealSize, true);
        }
        return (0, rec.IsDirectory);
    }

    /// <summary>Directory path of a parent reference ending with a backslash. Root = "\"; a reused parent (sequence differs), a missing parent, a cycle or depth over 64 gives "…\" and a reason.</summary>
    private static (string Path, string Problem) ParentPath(MftReference parent, Dictionary<ulong, DirEntry> dirs)
    {
        var parts = new List<string>();
        var seen = new HashSet<ulong>();
        var cur = parent;
        string problem = "";
        for (int depth = 0; ; depth++)
        {
            if (depth >= MaxPathDepth) { problem = "adâncime peste 64"; break; }
            if (!dirs.TryGetValue(cur.Number, out var d)) { problem = "părinte lipsă sau nefolosit"; break; }
            if (d.Sequence != cur.Sequence) { problem = "părinte reutilizat (secvență diferită)"; break; }
            if (!seen.Add(cur.Number)) { problem = "ciclu în lanțul de părinți"; break; }
            if (cur.Number == 5 && d.Parent.Number == 5) { parts.Reverse(); return ("\\" + string.Concat(parts.Select(p => p + "\\")), ""); }
            parts.Add(d.Name);
            cur = d.Parent;
        }
        parts.Reverse();
        return ("…\\" + string.Concat(parts.Select(p => p + "\\")), problem);
    }

    /// <summary>Largest $FN-after-$SI creation gap that is still ordinary: bsdtar truncates to the second, copies land within a few seconds (calibration 2026-11-13, README of Corpus/NtfsCalib).</summary>
    public const long CreatedToleranceTicks = 60L * 10_000_000;

    /// <summary>
    /// Weak $SI/$FN signals (format card §9). A mismatch is a plausible $SI Created more than <see cref="CreatedToleranceTicks"/> before $FN Created; then
    /// COPIED_CHANGE_TIME (the $SI MFT change time is before $FN Created: robocopy /COPY:DAT and Copy-Item copy it from the source) and the informative
    /// SI_CREATED_ZERO_FRACTION can be added. None is proof; an empty list = no mismatch.
    /// </summary>
    public static List<string> Signals(StandardInformation si, List<FileNameAttribute> fns)
    {
        var s = new List<string>();
        var fn = Best(fns);
        if (!NtfsTime.IsPlausible(si.Created) || !NtfsTime.IsPlausible(fn.Created)) return s;
        if (fn.Created - si.Created <= CreatedToleranceTicks) return s;
        s.Add("SI_CREATED_BEFORE_FN_CREATED");
        if (NtfsTime.IsPlausible(si.MftModified) && si.MftModified < fn.Created) s.Add("COPIED_CHANGE_TIME");
        if (si.Created % 10_000_000 == 0 && fn.Created % 10_000_000 != 0) s.Add("SI_CREATED_ZERO_FRACTION");
        return s;
    }

    /// <summary>$SI Modified before $FN Created: what every copy and every extraction looks like. Only counted in the summary, never an event by itself.</summary>
    public static bool SiModifiedBeforeFnCreated(StandardInformation si, List<FileNameAttribute> fns)
    {
        var fn = Best(fns);
        return NtfsTime.IsPlausible(si.Modified) && NtfsTime.IsPlausible(fn.Created) && si.Modified < fn.Created;
    }

    /// <summary>
    /// Record size from the first valid FILE header among the first 64 positions of 1,024 bytes (USA count = size/512 + 1). A boot sector next to the
    /// file is used only to confirm it: if it says something else it belongs to another volume and is reported in <paramref name="bootGap"/>.
    /// </summary>
    private static int ResolveRecordSize(string fullPath, FileStream fs, out string source, out string? bootGap)
    {
        bootGap = null;
        var head = new byte[NtfsFileRecord.DetectPositions * 1024 + 8];
        fs.Position = 0;
        int got = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        fs.Position = 0;
        int size = NtfsFileRecord.DetectRecordSize(head.AsSpan(0, got), out int offset, out var votes);
        if (size == 0) throw new InvalidDataException("Nicio înregistrare FILE validă în primele 64 de poziții; nu este un $MFT.");
        string Tally() => string.Join(", ", votes.OrderBy(v => v.Key).Select(v => $"{v.Key} octeți: {v.Value}"));
        bool conflict = votes.Count > 1;
        source = conflict ? $"majoritatea antetelor (USA count; {Tally()})"
               : offset == 0 ? "primul antet (USA count)" : $"primul antet valid, la offset {offset} (USA count)";
        NtfsBootSector? boot = null; string? bootName = null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (dir is not null)
            foreach (var n in BootSiblings)
            {
                var p = Path.Combine(dir, n);
                if (!File.Exists(p) || new FileInfo(p).Length is < 512 or > 64 * 1024) continue;
                boot = NtfsBootSector.TryParse(File.ReadAllBytes(p), out var err);
                bootName = n;
                if (boot is null) bootGap = "Sectorul de boot furnizat alături nu a putut fi folosit: " + err;
                break;
            }
        if (conflict)
        {
            // Headers disagree: a damaged or planted header. The boot sector is evidence too, so it breaks a TIE only; it never outvotes the majority
            // (a planted boot sector plus one planted header must not impose their size on every other record).
            if (boot is not null && boot.MftRecordSize != size && votes.TryGetValue(boot.MftRecordSize, out var bv) && bv == votes[size])
            {
                size = boot.MftRecordSize;
                source = $"boot sector ({bootName}), între antete în conflict";
            }
            bootGap = $"Antete FILE în conflict asupra mărimii înregistrării ({Tally()}); s-a folosit {size} octeți" +
                      (boot is null ? ", după majoritate." : boot.MftRecordSize == size ? $", confirmat de sectorul de boot ({bootName})." : $"; sectorul de boot ({bootName}) spune {boot.MftRecordSize}, mărime pe care majoritatea antetelor nu o confirmă.");
        }
        else if (boot is not null)
        {
            if (boot.MftRecordSize == size) source = $"boot sector ({bootName}), confirmat de antete";
            else bootGap = $"Sectorul de boot de lângă fișier nu corespunde acestui $MFT ({boot.MftRecordSize} față de {size}); s-a folosit mărimea din antete";
        }
        return size;
    }
}
