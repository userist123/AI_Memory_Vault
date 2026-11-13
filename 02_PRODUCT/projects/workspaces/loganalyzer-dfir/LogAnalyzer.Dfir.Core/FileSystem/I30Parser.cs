using System.Globalization;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// $I30 index allocation (INDX blocks of a directory) exported as a file. Emits the allocated entries and the residues found in the slack of each block,
/// and classifies each slack entry against a neighbouring $MFT when there is one. A slack entry is a leftover, never proof that a file was deleted.
/// Every byte is hostile: nothing read from the input sizes an allocation without a cap and a damaged block is counted, never fatal.
/// </summary>
public sealed class I30Parser : EvidenceParserBase
{
    public const string SourceTypeName = "ntfs_i30";
    public const string FingerprintName = "ntfs_i30";
    public const int MaxEvents = 50_000;
    public const int MaxBlocks = 1_000_000;
    /// <summary>Share of the allocated entries that may be missing (live, same sequence, same name) in the neighbouring $MFT before it is judged to be from another volume or moment.</summary>
    public const int GateTolerancePercent = 2;
    private readonly int _maxEvents, _maxBlocks;

    /// <summary>The limits default to the production caps; tests lower them to reach the over-cap paths.</summary>
    public I30Parser(int maxEvents = MaxEvents, int maxBlocks = MaxBlocks) { _maxEvents = maxEvents; _maxBlocks = maxBlocks; }

    private static readonly string[] BootSiblings = ["$Boot", "boot.bin", "$Boot.bin"];
    private static readonly string[] MftSiblings = ["$MFT", "mft.bin", "$MFT.bin"];

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "I30Parser", Version = "1.0", Artifact = "$I30 / $INDEX_ALLOCATION exportat (blocuri INDX ale unui director NTFS)",
        SourceTypes = [SourceTypeName], FileNames = ["$I30", "*.i30", "*$INDEX_ALLOCATION*"], Fingerprints = [FingerprintName],
        MaxInputBytes = 0,   // streamed block by block
        SupportedOs = "Windows (NTFS 3.x), blocuri de index de 512 la 65.536 octeți (peste, matricea USA nu mai încape sub octetul 510)",
        FormatVersions = ["NTFS 3.1 INDX, fixup 512 octeți, node header la +24, intrări cu cheie $FILE_NAME"],
        Limitations =
        [
            "Intrările din $INDEX_ROOT (rezident în înregistrarea MFT a directorului: separatorii unui director mare și toate numele unui director mic) sunt citite doar dacă există un $MFT alături care trece verificarea de coerență; ele apar ca „allocated_root”, fără căutare de slack în rădăcină. Fără $MFT, lista de nume alocate poate fi incompletă (gap explicit).",
            "Un $MFT alăturat este folosit doar după o verificare de coerență: toate cheile alocate au același părinte, iar acesta e un director viu cu aceeași secvență; cel puțin 98% dintre intrările alocate sunt vii în $MFT cu aceeași secvență și același nume. Dacă verificarea pică (alt volum sau moment), tot slack-ul rămâne neclasat, cu gap și motiv.",
            "O intrare din slack este un rest al indexului (rămas după mutarea sau ștergerea unor intrări); NU dovedește că fișierul a fost șters și nu are dată de ștergere. Clasarea față de $MFT spune doar ce este acum înregistrarea referită: copie a unei intrări vii, nume istoric (înregistrare reutilizată sau liberă), nume anterior posibil (vie cu alt nume în director), mutată din director (vie sub alt părinte) sau rest cu părinte străin.",
            "Resturile din slack sunt căutate la fiecare multiplu de 8 octeți cu validare strictă (lungimi coerente, namespace ≤ 3, nume fără caractere de control, cel puțin 2 ore plauzibile, secvență ≠ 0). Un rest al cărui antet (referință, lungimi) a fost suprascris, de pildă de marcajul de sfârșit, nu este recuperat deși cheia lui poate fi încă în octeți.",
            "Orele din cheie sunt ora $FILE_NAME copiată în index la creare sau redenumire; pot fi mai vechi decât orele reale ale fișierului.",
            "Mărimea blocului vine din antetele blocurilor (votate, maximum 65.536); sectorul de boot de alături doar o confirmă. Fără niciuna se presupune 4.096 octeți, cu un gap.",
            "Plafoane: 50.000 de evenimente (alocate, din rădăcină și slack la un loc, în ordinea fișierului) și 1.000.000 de blocuri; peste ele intrările sunt doar numărate.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "I30ParserTests: Corpus/Ntfs (many_i30.bin + mft.bin + boot.bin scrise de Windows 11) comparat cu truth.json (41 de fișiere vii: 39 din INDX + 2 din $INDEX_ROOT; 34 de resturi: 24 copii vii + 10 nume cu înregistrare liberă); probele ostile Q1–Q10 și cele din review (P1, P1b, P2, P3)",
    };

    private sealed record Found(long Vcn, long BlockOffset, IndexEntry Entry, bool Slack, bool IsRoot = false);
    private sealed record MftInfo(NtfsRecordState State, bool IsBase, bool InUse, bool IsDirectory, ushort Sequence, List<FileNameAttribute> Names, byte[]? IndexRoot);

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        string artifact = Path.GetFileName(fullPath);
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        long fileLength = fs.Length;
        int blockSize = ResolveBlockSize(fullPath, fs, out string sizeSource, out string? sizeGap);
        if (sizeGap is not null)
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, sizeGap, "Mărimea blocului a fost dedusă din antetele INDX sau presupusă", "Sectorul de boot al volumului acestui $I30", "Posibil"));

        var found = new List<Found>();
        long blocks = 0, blank = 0, malformed = 0, fixupErrors = 0, badEntryBlocks = 0, allocTotal = 0, slackTotal = 0, blocksNotRead = 0;
        bool truncatedTail = false;
        var buf = new byte[blockSize];
        fs.Position = 0;
        long blockIndex = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int got = fs.ReadAtLeast(buf, blockSize, throwOnEndOfStream: false);
            if (got == 0) break;
            if (got < blockSize) { truncatedTail = true; break; }
            if (blockIndex >= _maxBlocks) { blocksNotRead = (fileLength - blockIndex * (long)blockSize + blockSize - 1) / blockSize; break; }
            long blockOffset = blockIndex * (long)blockSize;
            blockIndex++; blocks++;
            var rec = IndexRecord.Parse(buf, blockSize);
            switch (rec.State)
            {
                case IndexRecordState.Blank: blank++; continue;
                case IndexRecordState.Torn: fixupErrors++; malformed++; result.MalformedRecords++; continue;
                case IndexRecordState.Ok: break;
                default: malformed++; result.MalformedRecords++; continue;
            }
            // allocated entries: from the first entry to "used", stopping at the last-entry marker or at the first entry that cannot be trusted to advance
            int pos = rec.EntriesStart;
            bool badEntry = false;
            while (pos < rec.UsedEnd)
            {
                var e = rec.TryReadAllocated(pos, out _);
                if (e is null) { badEntry = true; break; }
                if (e.IsLast) break;
                allocTotal++;
                if (found.Count < _maxEvents) found.Add(new Found(rec.Vcn, blockOffset, e, false));
                pos += e.Length;   // at least 16: the loop always advances
            }
            if (badEntry) { badEntryBlocks++; malformed++; result.MalformedRecords++; }
            // slack: every multiple of 8 from "used" to "allocated"
            pos = (rec.UsedEnd + 7) & ~7;
            while (pos + 16 + 66 <= rec.AllocatedEnd)
            {
                var e = rec.TryReadSlack(pos);
                if (e is null) { pos += 8; continue; }
                slackTotal++;
                if (found.Count < _maxEvents) found.Add(new Found(rec.Vcn, blockOffset, e, true));
                pos += e.Length;
            }
        }
        if (truncatedTail)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, "Fișierul $I30 se termină în mijlocul unui bloc INDX",
                $"Blocul de după offset {blockIndex * (long)blockSize:N0} lipsește sau e incomplet", "Reexportul $INDEX_ALLOCATION întreg", "Posibil"));
        }
        if (malformed > 0)
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, $"{malformed:N0} blocuri INDX nevalide ({fixupErrors:N0} cu fixup nepotrivit, {badEntryBlocks:N0} cu o intrare ilizibilă; antet, USA sau node header incoerente)",
                "Intrările acestor blocuri nu au fost citite (cele dinainte de o intrare ilizibilă se emit)", "Un alt export al aceluiași $I30", "Posibil"));
        if (blocksNotRead > 0)
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, $"Plafon de blocuri atins: {_maxBlocks:N0} blocuri citite, {blocksNotRead:N0} blocuri necitite",
                "Intrările blocurilor necitite lipsesc", "Un export filtrat", "Posibil"));

        // ---- the directory this index belongs to: the common parent of the allocated keys ----
        var allocated = found.Where(f => !f.Slack).Select(f => f.Entry).ToList();
        var parents = allocated.Select(e => e.Key!.Parent.Raw).Distinct().Take(8).ToList();
        MftReference? dirRef = parents.Count == 1 ? new MftReference(parents[0]) : null;

        // ---- neighbouring $MFT: coherence gate first, then classification and the resident $INDEX_ROOT ----
        var needed = found.Select(f => f.Entry.Reference.Number).ToHashSet();
        if (dirRef is { } dr0) needed.Add(dr0.Number);
        var mft = LoadMft(fullPath, needed, dirRef?.Number, ct, out string mftState, out string? mftGap, out string mftName);
        if (mftGap is not null)
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, mftGap, "Intrările din slack rămân neclasate", "Exportul $MFT al aceluiași volum, alături de $I30", "Posibil"));
        string gate = "not run", gateReason = "";
        string? gateWarning = null;
        if (mft is not null)
        {
            gateReason = CheckGate(dirRef, parents.Count, allocated, mft, out bool passed, out gateWarning);
            gate = passed ? "passed" : "failed";
            if (!passed)
                result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, gateReason, "$MFT-ul alăturat nu a fost folosit: intrările din slack sunt neclasate și rădăcina indexului nu a fost citită", "Exportul $MFT al aceluiași volum și moment, alături de $I30", "Posibil"));
            else if (gateWarning is not null)
                result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, gateWarning, "Intrările alocate lipsă nu au putut fi verificate; $MFT-ul și $I30 copiate separat nu sunt atomice", "O copie de umbră a volumului", "Posibil"));
        }
        else if (mftGap is null && slackTotal > 0)
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, "Nu există un $MFT alături de $I30 (mft.bin, $MFT sau $MFT.bin în același folder)",
                "Intrările din slack sunt neclasate: nu se știe dacă referința mai e vie, reutilizată sau liberă", "Exportul $MFT al aceluiași volum", "Posibil"));
        bool gatePassed = gate == "passed";

        // ---- $INDEX_ROOT of the directory record: allocated entries too, never carved ----
        long rootTotal = 0, rootRejected = 0;
        string rootStatus;
        if (gatePassed && dirRef is { } dir && mft![dir.Number].IndexRoot is { } rootValue)
        {
            var rootEntries = IndexRootNode.Read(rootValue, out string? rootProblem);
            foreach (var e in rootEntries)
            {
                if (e.Key!.Parent.Number != dir.Number || e.Key.Parent.Sequence != dir.Sequence) { rootRejected++; continue; }
                rootTotal++;
                if (found.Count < _maxEvents) found.Add(new Found(-1, 0, e, false, true));
            }
            rootStatus = rootProblem is null ? "read" : "read with problems";
            if (rootProblem is not null || rootRejected > 0)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial,
                    $"$INDEX_ROOT al directorului {dir.FileId}: " + string.Join("; ", new[]
                    {
                        rootProblem is null ? null : $"nu a putut fi citit integral ({rootProblem})",
                        rootRejected > 0 ? $"{rootRejected:N0} intrări cu alt părinte nu au fost emise" : null,
                    }.Where(s => s is not null)),
                    "Intrările de după problemă din rădăcina indexului lipsesc", "Un alt export al $MFT", "Posibil"));
            }
        }
        else if (gatePassed && dirRef is not null)
        {
            rootStatus = "absent";
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, "Înregistrarea directorului din $MFT nu are un $INDEX_ROOT „$I30” rezident: rădăcina indexului ($INDEX_ROOT) nu a fost citită: lista de nume alocate poate fi incompletă",
                "Numele din rădăcina indexului (separatori și intrări ale directoarelor mici) pot lipsi", "Un alt export al $MFT", "Posibil"));
        }
        else
        {
            rootStatus = "not read";
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, "rădăcina indexului ($INDEX_ROOT) nu a fost citită: lista de nume alocate poate fi incompletă" + (gate == "failed" ? " ($MFT-ul alăturat nu a trecut verificarea de coerență)" : mft is null && mftGap is null ? " (nu există $MFT alături)" : " ($MFT-ul alăturat nu a putut fi folosit)"),
                "Numele din rădăcina indexului (în $MFT, nu în $INDEX_ALLOCATION) lipsesc din rezultat", "Exportul $MFT al aceluiași volum, alături de $I30", "Posibil"));
        }

        var classes = new Dictionary<string, long> { ["live_copy"] = 0, ["historical_reused"] = 0, ["historical_free"] = 0, ["historical_renamed"] = 0, ["moved_out"] = 0, ["foreign_parent"] = 0, ["unclassified"] = 0 };
        long emitted = 0;
        foreach (var f in found)
        {
            ct.ThrowIfCancellationRequested();
            string cls = "", movedTo = "";
            if (f.Slack) { cls = Classify(f.Entry, dirRef, gatePassed ? mft : null, out movedTo); classes[cls]++; }
            Emit(f, cls, movedTo);
            emitted++;
        }
        long notEmitted = allocTotal + slackTotal + rootTotal - emitted;
        if (notEmitted > 0)
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, $"Plafon de evenimente atins: {emitted:N0} emise din {allocTotal + slackTotal + rootTotal:N0} intrări ({notEmitted:N0} neemise, în ordinea fișierului)",
                "Intrările neemise sunt doar numărate în rezumat", "Un export filtrat", "Posibil"));

        sink.Add(new TimelineEvent
        {
            Time = Timestamp.Unknown(), Source = "I30", EvidenceId = item.EvidenceId, Process = "", Path = "",
            Summary = $"$I30: {blocks:N0} blocuri de {blockSize:N0} octeți, {allocTotal:N0} intrări alocate (+{rootTotal:N0} din $INDEX_ROOT), {slackTotal:N0} resturi în slack, {malformed:N0} blocuri nevalide; verificare $MFT: {gate}",
            TimeSemantics = "rezumatul analizei $I30", TemporalType = TemporalType.Derived, Classification = Classification.Direct, Confidence = Confidence.High,
            Locator = "summary",
            Fields =
            {
                ["Category"] = "summary", ["Blocks"] = N(blocks), ["BlockSize"] = N(blockSize), ["BlockSizeSource"] = sizeSource,
                ["AllocatedEntries"] = N(allocTotal), ["RootEntries"] = N(rootTotal), ["RootStatus"] = rootStatus, ["SlackEntries"] = N(slackTotal), ["EntriesNotEmitted"] = N(notEmitted),
                ["MalformedBlocks"] = N(malformed), ["FixupErrors"] = N(fixupErrors), ["BadEntryBlocks"] = N(badEntryBlocks), ["BlankBlocks"] = N(blank),
                ["BlocksNotRead"] = N(blocksNotRead), ["TruncatedFile"] = truncatedTail ? "true" : "false",
                ["DirectoryFileId"] = dirRef?.FileId ?? "",
                ["MftUsed"] = gatePassed ? "true" : "false", ["MftState"] = mftState, ["MftFile"] = mftName, ["MftGate"] = gate, ["MftGateReason"] = gateReason,
                ["SlackLiveCopy"] = N(classes["live_copy"]), ["SlackReused"] = N(classes["historical_reused"]), ["SlackFree"] = N(classes["historical_free"]),
                ["SlackRenamed"] = N(classes["historical_renamed"]), ["SlackMovedOut"] = N(classes["moved_out"]), ["SlackForeignParent"] = N(classes["foreign_parent"]),
                ["SlackUnclassified"] = N(classes["unclassified"]),
            },
            Notes = "O intrare din slack nu dovedește ștergerea fișierului. Clasarea față de $MFT se face doar dacă $MFT-ul alăturat trece verificarea de coerență.",
        });
        result.Records++;

        // ---- local helpers ----
        void Emit(Found f, string cls, string movedTo)
        {
            var key = f.Entry.Key!;
            var reference = f.Entry.Reference;
            long when = new[] { key.Created, key.Modified, key.MftModified, key.Accessed }.Cast<long?>().FirstOrDefault(t => NtfsTime.IsPlausible(t!.Value)) ?? 0;
            string safe = MftParser.SafeText(key.Name);
            string summary = f.IsRoot ? $"$I30: intrare alocată în $INDEX_ROOT {safe} (#{reference.Number})"
                : !f.Slack ? $"$I30: intrare alocată {safe} (#{reference.Number})"
                : cls switch
                {
                    "live_copy" => $"$I30 slack: copie a unei intrări vii {safe} (#{reference.Number})",
                    "historical_reused" => $"$I30 slack: nume istoric {safe}, înregistrare reutilizată (#{reference.Number})",
                    "historical_free" => $"$I30 slack: nume istoric {safe}, înregistrare liberă (#{reference.Number})",
                    "historical_renamed" => $"$I30 slack: nume anterior posibil {safe}; înregistrarea referită e vie cu alt nume în acest director (#{reference.Number})",
                    "moved_out" => $"$I30 slack: numele {safe} a fost în acest director; înregistrarea e acum sub părintele {movedTo} (#{reference.Number})",
                    "foreign_parent" => $"$I30 slack: intrare cu părinte străin {safe} (părinte {key.Parent.FileId}, #{reference.Number})",
                    _ => $"$I30 slack: intrare neclasată {safe} (#{reference.Number})",
                };
            var ev = new TimelineEvent
            {
                Time = NtfsTime.ToTimestamp(when, "$I30, ora $FILE_NAME din cheie, FILETIME UTC"), Source = "I30", EvidenceId = item.EvidenceId, Process = "", Path = safe,
                Summary = summary,
                TimeSemantics = "ora $FILE_NAME copiată în index (poate fi mai veche decât orele reale)",
                TemporalType = TemporalType.Historical,
                Classification = f.Slack ? Classification.Candidate : Classification.Direct,
                Confidence = !f.Slack ? Confidence.Medium : cls is "live_copy" or "historical_free" or "moved_out" ? Confidence.Medium : Confidence.Low,
                Locator = f.IsRoot ? $"INDEX_ROOT+0x{f.Entry.Offset:X}" : $"VCN {f.Vcn.ToString(CultureInfo.InvariantCulture)}+0x{f.Entry.Offset:X}",
                Notes = f.Slack ? "Rest din slack-ul unui bloc INDX: nu dovedește ștergerea fișierului și nu are dată de ștergere." : "",
            };
            var fl = ev.Fields;
            fl["Category"] = f.IsRoot ? "allocated_root" : f.Slack ? "slack" : "allocated";
            fl["FileName"] = key.Name; fl["Namespace"] = N(key.Namespace);
            fl["RecordNumber"] = N((long)reference.Number); fl["Sequence"] = N(reference.Sequence); fl["FileId"] = reference.FileId;
            fl["ParentFileId"] = key.Parent.FileId;
            fl["FnCreated"] = NtfsTime.Text(key.Created); fl["FnModified"] = NtfsTime.Text(key.Modified);
            fl["FnMftModified"] = NtfsTime.Text(key.MftModified); fl["FnAccessed"] = NtfsTime.Text(key.Accessed);
            fl["Size"] = N(key.RealSize); fl["AllocatedSize"] = N(key.AllocatedSize); fl["FileFlags"] = "0x" + key.Flags.ToString("X8", CultureInfo.InvariantCulture);
            fl["Vcn"] = f.IsRoot ? "root" : N(f.Vcn); fl["BlockOffset"] = N(f.BlockOffset); fl["EntryOffset"] = N(f.Entry.Offset);
            if (key.NameHadInvalidUtf16) fl["NameInvalidUtf16"] = "true";
            if (f.Slack) fl["SlackClass"] = cls;
            if (movedTo.Length > 0) fl["MovedToParentFileId"] = movedTo;
            sink.Add(ev);
            result.Records++;
        }
    }

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Coherence of the neighbouring $MFT with this $I30, checked BEFORE any classification (a $MFT of another volume or moment would otherwise label the
    /// residues with the state of unrelated records). (1) the allocated keys share one parent and it is a live directory with the same sequence;
    /// (2) the allocated references are live with the same sequence and name, up to <see cref="GateTolerancePercent"/> percent (copying the two files is not atomic).
    /// </summary>
    private static string CheckGate(MftReference? dirRef, int distinctParents, List<IndexEntry> allocated, Dictionary<ulong, MftInfo> mft, out bool passed, out string? warning)
    {
        passed = false; warning = null;
        if (allocated.Count == 0) return "Nu există intrări alocate în $I30: coerența cu $MFT-ul alăturat nu poate fi verificată";
        if (dirRef is not { } dir) return $"Intrările alocate au {(distinctParents >= 8 ? "peste 7" : distinctParents.ToString(CultureInfo.InvariantCulture))} părinți diferiți: $I30 nu e indexul unui singur director, coerența cu $MFT-ul alăturat nu poate fi verificată";
        if (!mft.TryGetValue(dir.Number, out var d) || d.State != NtfsRecordState.Ok || !d.IsBase || !d.InUse || !d.IsDirectory || d.Sequence != dir.Sequence)
            return $"Părintele intrărilor alocate ({dir.FileId}) nu este un director viu cu aceeași secvență în $MFT-ul alăturat: nu sunt din același volum sau moment";
        int ok = 0;
        foreach (var e in allocated)
            if (mft.TryGetValue(e.Reference.Number, out var i) && i.State == NtfsRecordState.Ok && i.IsBase && i.InUse && i.Sequence == e.Reference.Sequence
                && i.Names.Any(n => string.Equals(n.Name, e.Key!.Name, StringComparison.Ordinal)))
                ok++;
        int missing = allocated.Count - ok;
        if (missing * 100 > allocated.Count * GateTolerancePercent)
            return $"{ok} din {allocated.Count} intrări alocate se regăsesc vii în $MFT-ul alăturat: nu sunt din același volum sau moment";
        passed = true;
        if (missing > 0) warning = $"{missing} din {allocated.Count} intrări alocate nu se regăsesc vii în $MFT-ul alăturat (sub pragul de {GateTolerancePercent}%): $MFT și $I30 au fost copiate la momente diferite";
        return "";
    }

    /// <summary>Slack entry against the $MFT record its reference points to (and against the directory of the index). Without a coherent $MFT: "unclassified".</summary>
    private static string Classify(IndexEntry e, MftReference? dirRef, Dictionary<ulong, MftInfo>? mft, out string movedTo)
    {
        movedTo = "";
        var key = e.Key!;
        if (dirRef is { } dir && (key.Parent.Number != dir.Number || key.Parent.Sequence != dir.Sequence)) return "foreign_parent";
        if (mft is null || dirRef is not { } dir2) return "unclassified";
        var r = e.Reference;
        if (!mft.TryGetValue(r.Number, out var info) || info.State != NtfsRecordState.Ok || !info.IsBase) return "unclassified";
        if (!info.InUse) return "historical_free";
        if (info.Sequence != r.Sequence) return "historical_reused";
        bool InDir(FileNameAttribute n) => n.Parent.Number == dir2.Number && n.Parent.Sequence == dir2.Sequence;
        if (info.Names.Any(n => InDir(n) && string.Equals(n.Name, key.Name, StringComparison.Ordinal))) return "live_copy";
        if (info.Names.Any(InDir)) return "historical_renamed";
        var best = info.Names.Count == 0 ? null : info.Names.MaxBy(n => n.Rank);
        if (best is null) return "unclassified";
        movedTo = best.Parent.FileId;
        return "moved_out";
    }

    /// <summary>
    /// Reads the records of a neighbouring $MFT that the entries point to (and the names of their extension records), plus the resident $INDEX_ROOT "$I30" of the
    /// directory record <paramref name="dirNumber"/>. Null when there is no $MFT or it cannot be used (<paramref name="gap"/> says why); a record that cannot be read
    /// is present with its state or absent.
    /// </summary>
    private static Dictionary<ulong, MftInfo>? LoadMft(string fullPath, HashSet<ulong> needed, ulong? dirNumber, CancellationToken ct, out string state, out string? gap, out string name)
    {
        gap = null; name = ""; state = "absent";
        var dir = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        string? path = null;
        if (dir is not null)
            foreach (var n in MftSiblings)
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p) && !string.Equals(Path.GetFullPath(p), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase)) { path = p; name = n; break; }
            }
        if (path is null) return null;
        if (needed.Count == 0) { state = "not needed (no entries)"; return null; }
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var head = new byte[NtfsFileRecord.DetectPositions * 1024 + 8];
            int got = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            int size = NtfsFileRecord.DetectRecordSize(head.AsSpan(0, got), out _);
            if (size == 0)
            {
                state = "unusable";
                gap = $"$MFT-ul de alături ({name}) nu are nicio înregistrare FILE validă în primele 64 de poziții; nu a fost folosit";
                return null;
            }
            fs.Position = 0;
            var result = new Dictionary<ulong, MftInfo>();
            var ext = new Dictionary<ulong, List<(ushort Seq, FileNameAttribute Name)>>();
            var buf = new byte[size];
            ulong number = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (fs.ReadAtLeast(buf, size, throwOnEndOfStream: false) < size) break;
                ulong num = number++;
                bool want = needed.Contains(num);
                bool maybeExt = !want && buf[0] == (byte)'F' && buf[1] == (byte)'I' && buf[2] == (byte)'L' && buf[3] == (byte)'E'
                                && new MftReference(BitConverter.ToUInt64(buf, 32)) is { Raw: not 0 } br && needed.Contains(br.Number);
                if (!want && !maybeExt) continue;
                var rec = NtfsFileRecord.Parse(buf, size);
                if (want)
                {
                    result[num] = rec.State == NtfsRecordState.Ok
                        ? new MftInfo(rec.State, rec.IsBaseRecord, rec.InUse, rec.IsDirectory, rec.Sequence, MftParser.Names(rec), dirNumber == num ? IndexRootOf(rec) : null)
                        : new MftInfo(rec.State, false, false, false, 0, [], null);
                }
                else if (rec.State == NtfsRecordState.Ok && rec.InUse && !rec.IsBaseRecord)
                {
                    if (!ext.TryGetValue(rec.BaseReference.Number, out var l)) ext[rec.BaseReference.Number] = l = [];
                    foreach (var n in MftParser.Names(rec)) l.Add((rec.BaseReference.Sequence, n));
                }
            }
            foreach (var (n, l) in ext)
                if (result.TryGetValue(n, out var info) && info.State == NtfsRecordState.Ok)
                    foreach (var (seq, nm) in l) if (seq == info.Sequence) info.Names.Add(nm);
            state = $"used ({number:N0} înregistrări de {size:N0} octeți)";
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            state = "unreadable";
            gap = $"$MFT-ul de alături ({name}) nu a putut fi citit: {ex.GetType().Name}";
            return null;
        }
    }

    /// <summary>The resident, named "$I30" $INDEX_ROOT value of a directory record, or null.</summary>
    private static byte[]? IndexRootOf(NtfsFileRecord rec)
    {
        foreach (var a in rec.Attributes)
            if (a.Type == NtfsAttributeType.IndexRoot && !a.NonResident && a.Name == "$I30") return rec.ValueOf(a).ToArray();
        return null;
    }

    /// <summary>
    /// Block size from the headers (see <see cref="IndexRecord.DetectBlockSize"/>). A boot sector next to the file only confirms it: if it says something else it
    /// belongs to another volume and is reported in <paramref name="gap"/>. With no usable header the boot sector decides, else 4,096 is assumed (reported).
    /// </summary>
    private static int ResolveBlockSize(string fullPath, FileStream fs, out string source, out string? gap)
    {
        gap = null;
        var head = new byte[IndexRecord.DetectPositions * 512 + 40];
        fs.Position = 0;
        int got = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        fs.Position = 0;
        int size = IndexRecord.DetectBlockSize(head.AsSpan(0, got), out var votes);
        NtfsBootSector? boot = null; string? bootName = null, bootErr = null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (dir is not null)
            foreach (var n in BootSiblings)
            {
                var p = Path.Combine(dir, n);
                if (!File.Exists(p) || new FileInfo(p).Length is < 512 or > 64 * 1024) continue;
                boot = NtfsBootSector.TryParse(File.ReadAllBytes(p), out bootErr);
                bootName = n;
                break;
            }
        string Tally() => string.Join(", ", votes.OrderBy(v => v.Key).Select(v => $"{v.Key} octeți: {v.Value}"));
        if (size == 0)
        {
            if (boot is not null && boot.IndexRecordSize <= IndexRecord.MaxBlockSize) { source = $"boot sector ({bootName}); niciun antet INDX valid"; gap = "Niciun antet INDX valid în primele 64 de poziții; mărimea blocului vine din sectorul de boot de alături"; return boot.IndexRecordSize; }
            source = "presupus 4096 (niciun antet INDX valid, fără sector de boot)";
            gap = "Mărimea blocului nu a putut fi dedusă (niciun antet INDX valid, fără sector de boot utilizabil); s-a presupus 4.096 octeți" + (bootErr is not null ? $" (boot: {bootErr})" : "");
            return 4096;
        }
        bool conflict = votes.Count > 1;
        source = conflict ? $"majoritatea antetelor ({Tally()})" : "antetele blocurilor (allocated + 24)";
        if (conflict) gap = $"Antete INDX în conflict asupra mărimii blocului ({Tally()}); s-a folosit {size} octeți după majoritate";
        if (boot is not null)
        {
            if (boot.IndexRecordSize == size) source = $"boot sector ({bootName}), confirmat de antete";
            else gap = (gap is null ? "" : gap + ". ") + $"Sectorul de boot de lângă fișier ({bootName}) nu corespunde acestui $I30 ({boot.IndexRecordSize} față de {size}); s-a folosit mărimea din antete";
        }
        else if (bootErr is not null)
            gap = (gap is null ? "" : gap + ". ") + $"Sectorul de boot ({bootName}) de alături nu a putut fi folosit: {bootErr}";
        return size;
    }
}
