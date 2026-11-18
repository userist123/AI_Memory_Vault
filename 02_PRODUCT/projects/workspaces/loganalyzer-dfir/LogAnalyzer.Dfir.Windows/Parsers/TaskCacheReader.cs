using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Task Scheduler cache of an offline SOFTWARE hive: <c>Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache</c> (Tree, Tasks, Plain, Logon, Boot,
/// Maintenance). One event per action of every task found under <c>Tasks</c>. The hive is attacker-controlled input: every length is bounded by
/// the bytes left in its blob, the Tree walk is iterative with a depth and key limit and a visited set, and a damaged part becomes an
/// <see cref="EvidenceGap"/> while the rest is still read. Nothing here throws out of <see cref="Emit"/> (except cancellation).
/// Field meanings were derived from a reference station (226 tasks, hive and Get-ScheduledTask export taken at the same moment):
/// Actions blob parsed completely for all 226 (135 ComHandler); DynamicInfo FILETIME at offset 12 equals LastRunTime for 226/226 (to the second,
/// zero = never run); u32 at offset 24 equals LastTaskResult for 93/98 tasks that had run (the 5 others were running at capture time, where the
/// service reports 267009 and the cache holds 0); FILETIME at offset 4 is the moment the task was (re-)registered in the cache, NOT proven to be the
/// creation of the task: it matched to the millisecond for one task created on purpose, but on the reference station 194 of 226 values fall in
/// one single minute although those tasks were registered between 2006 and 2021 (a re-registration, e.g. during servicing); FILETIME at offset 28 is only observed (a few milliseconds after offset 12 for tasks that ran) and is emitted under a neutral name.
/// </summary>
public static class TaskCacheReader
{
    public const string KeyPath = @"Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache";
    public const int MaxTreeDepth = 32;
    public const int MaxKeys = 200_000;
    /// <summary>Most events one hive may yield from TaskCache. Hostile hives can make many Tasks keys share one Actions data cell (4096 actions each).</summary>
    public const int MaxEvents = 50_000;
    private const int MaxActionsPerBlob = 4096;
    private const int MaxSummaryText = 300;
    private const int DynamicInfoLength = 36;
    private static readonly string[] Categories = ["Plain", "Logon", "Boot", "Maintenance"];

    public sealed record ActionRecord(string Type, string TypeRaw, string Command, string Arguments, string WorkingDirectory, string ClassId, string Data);

    public sealed record ActionsBlob(int Version, string Context, IReadOnlyList<ActionRecord> Actions, string? Gap);

    public sealed record DynamicInfoRecord(long CacheRegisteredFileTime, long LastRunFileTime, uint LastResultRaw, long Time28FileTime);

    // ---------------------------------------------------------------- blob parsers

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _b;
        public int Pos;
        public Cursor(ReadOnlySpan<byte> b) { _b = b; Pos = 0; }
        public readonly int Left => _b.Length - Pos;

        public bool U16(out ushort v)
        {
            v = 0;
            if (Left < 2) return false;
            v = BinaryPrimitives.ReadUInt16LittleEndian(_b[Pos..]);
            Pos += 2;
            return true;
        }

        /// <summary>u32 byte length then UTF-16LE text. Fails (without allocating) when the length exceeds the bytes left or is odd.</summary>
        public bool Str(out string s)
        {
            s = "";
            if (Left < 4) return false;
            uint n = BinaryPrimitives.ReadUInt32LittleEndian(_b[Pos..]);
            if (n > (uint)(Left - 4) || (n & 1) != 0) return false;
            s = Encoding.Unicode.GetString(_b.Slice(Pos + 4, (int)n)).TrimEnd('\0');
            Pos += 4 + (int)n;
            return true;
        }

        public bool Guid(out string g)
        {
            g = "";
            if (Left < 16) return false;
            g = new System.Guid(_b.Slice(Pos, 16)).ToString("B").ToUpperInvariant();
            Pos += 16;
            return true;
        }
    }

    /// <summary>
    /// Actions value: u16 version (3), u32-length UTF-16 context, then actions until the end. Exec = u16 0x6666, id, command, arguments, working directory
    /// (each u32-length UTF-16) and a u16 0. ComHandler = u16 0x7777, id, 16-byte CLSID, data (u32-length UTF-16). Any other type or version stops the
    /// blob: the actions read so far are returned together with a gap text.
    /// </summary>
    public static ActionsBlob ParseActions(ReadOnlySpan<byte> blob)
    {
        var list = new List<ActionRecord>();
        var c = new Cursor(blob);
        if (!c.U16(out var version)) return new(0, "", list, "blob Actions prea scurt (fără versiune)");
        if (version != 3) return new(version, "", list, $"versiune Actions necunoscută: {version}");
        if (!c.Str(out var context)) return new(version, "", list, "contextul din Actions este trunchiat sau are lungime invalidă");
        while (c.Left > 0)
        {
            if (list.Count >= MaxActionsPerBlob) return new(version, context, list, $"mai mult de {MaxActionsPerBlob} acțiuni; restul ignorat");
            int at = c.Pos;
            if (!c.U16(out var type)) return new(version, context, list, $"octeți rămași fără tip de acțiune la poziția {at}");
            switch (type)
            {
                case 0x6666:
                    if (!c.Str(out _) || !c.Str(out var cmd) || !c.Str(out var args) || !c.Str(out var wd) || !c.U16(out _))
                        return new(version, context, list, $"acțiune Exec trunchiată sau cu lungime invalidă la poziția {at}");
                    list.Add(new("Exec", "0x6666", cmd, args, wd, "", ""));
                    break;
                case 0x7777:
                    if (!c.Str(out _) || !c.Guid(out var clsid) || !c.Str(out var data))
                        return new(version, context, list, $"acțiune ComHandler trunchiată sau cu lungime invalidă la poziția {at}");
                    list.Add(new("ComHandler", "0x7777", "", "", "", clsid, data));
                    break;
                default:
                    var raw = "0x" + type.ToString("X4", CultureInfo.InvariantCulture);
                    list.Add(new(raw, raw, "", "", "", "", ""));
                    return new(version, context, list, $"tip de acțiune necunoscut {raw} la poziția {at}; restul blobului nu a fost interpretat");
            }
        }
        return new(version, context, list, null);
    }

    /// <summary>
    /// DynamicInfo value (36 bytes): u32 version 3, FILETIME @4 (moment of (re-)registration in the cache, not proven task creation), FILETIME @12 (last run, 0 = never), u32 @20 (observed 0 on every task of the
    /// reference), u32 @24 (last task result), FILETIME @28 (meaning not proven). Null with a gap text when shorter than 36 bytes or another version.
    /// </summary>
    public static (DynamicInfoRecord? Info, string? Gap) ParseDynamicInfo(ReadOnlySpan<byte> b)
    {
        if (b.Length < DynamicInfoLength) return (null, $"DynamicInfo are {b.Length} octeți (așteptat {DynamicInfoLength})");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(b);
        if (version != 3) return (null, $"versiune DynamicInfo necunoscută: {version}");
        return (new(BinaryPrimitives.ReadInt64LittleEndian(b[4..]), BinaryPrimitives.ReadInt64LittleEndian(b[12..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(b[24..]), BinaryPrimitives.ReadInt64LittleEndian(b[28..])), null);
    }

    // ---------------------------------------------------------------- hive walk

    private sealed record TreeEntry(string Name, string Id, string Index, bool HasSd);

    private sealed class Budget(int maxEvents, int maxKeys)
    {
        public int MaxEvents { get; } = maxEvents;
        public int MaxKeys { get; } = maxKeys;
        public int Count;
        public bool Exhausted;
        public bool Take() { if (Count >= MaxEvents) { Exhausted = true; return false; } Count++; return true; }
    }

    private sealed class Gaps
    {
        private readonly Dictionary<string, (string Impact, List<string> Examples, int Count)> _g = new();
        public void Add(string kind, string impact, string example)
        {
            if (!_g.TryGetValue(kind, out var e)) _g[kind] = e = (impact, [], 0);
            if (e.Examples.Count < 5) e.Examples.Add(example);
            _g[kind] = (e.Impact, e.Examples, e.Count + 1);
        }
        public void Flush(ParseResult result)
        {
            foreach (var (kind, e) in _g)
                result.Gaps.Add(new EvidenceGap("TaskCache", EvidenceStatus.Partial, $"{kind} ({e.Count})", e.Impact,
                    "Task Scheduler pe stația vie (schtasks /query /v), System32\\Tasks, jurnalul TaskScheduler/Operational", "Posibil",
                    "Exemple: " + string.Join("; ", e.Examples)));
        }
    }

    private static string Guid(string s) => s.Trim().ToUpperInvariant();

    private static string Flat(string s, int max = MaxSummaryText)
    {
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (var ch in s)
        {
            if (sb.Length >= max) { sb.Append('…'); break; }
            sb.Append(char.IsControl(ch) || ch == (char)0x2028 || ch == (char)0x2029 ? ' ' : ch);
        }
        return LogAnalyzer.Dfir.Language.ReportText.Clean(sb.ToString());   // bidi/invisible controls removed and named, as the report writers do
    }

    private static string Iso(long ft) => ft <= 0 || ft > Timestamp.MaxFileTime ? "" : DateTime.FromFileTimeUtc(ft).ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Reads Tree iteratively. Depth, key count and revisits are bounded.</summary>
    private static List<TreeEntry> WalkTree(RawRegistry reg, Gaps gaps, int maxKeys, CancellationToken ct)
    {
        var entries = new List<TreeEntry>();
        var treePath = KeyPath + @"\Tree";
        var root = reg.OpenKey(treePath);
        if (root is null) { gaps.Add("cheia Tree lipsește", "Nu se poate verifica dacă activitățile din Tasks sunt înregistrate în Tree", treePath); return entries; }
        var visited = new HashSet<int> { root.CellOffset };
        var stack = new Stack<(RawKey Key, int Depth)>();
        stack.Push((root, 0));
        int keys = 0;
        bool depthReported = false, cycleReported = false;
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (key, depth) = stack.Pop();
            if (++keys > maxKeys) { gaps.Add("Tree depășește limita de chei", "Restul arborelui Tree nu a fost citit", $"{maxKeys} chei"); break; }
            if (key.Value("Id") is { } idv)
            {
                var name = key.Path.Length > treePath.Length ? key.Path[treePath.Length..] : "\\";
                entries.Add(new(name, Guid(idv.AsText), key.Value("Index")?.AsText ?? "", key.Value("SD") is not null));
            }
            if (key.SubkeyNames.Count == 0) continue;
            if (depth >= MaxTreeDepth)
            {
                if (!depthReported) { gaps.Add("Tree depășește adâncimea maximă", "Subarborele mai adânc nu a fost citit", key.Path); depthReported = true; }
                continue;
            }
            IReadOnlyList<int> offsets;
            try { offsets = reg.ChildOffsets(key); }
            catch (InvalidDataException ex) { gaps.Add("listă de subchei Tree ilizibilă", "Subarborele nu a fost citit", $"{key.Path}: {ex.Message}"); continue; }
            foreach (var off in offsets)
            {
                if (!visited.Add(off))
                {
                    if (!cycleReported) { gaps.Add("Tree conține o celulă vizitată de două ori (ciclu sau listă duplicată)", "Celula nu a fost citită a doua oară", key.Path); cycleReported = true; }
                    continue;
                }
                try { stack.Push((reg.ReadChild(key, off), depth + 1)); }
                catch (InvalidDataException ex) { gaps.Add("subcheie Tree ilizibilă", "Intrarea nu a fost citită", $"{key.Path}: {ex.Message}"); }
            }
        }
        return entries;
    }

    /// <summary><paramref name="maxEvents"/> and <paramref name="maxKeys"/> default to <see cref="MaxEvents"/> and <see cref="MaxKeys"/>; lower values exist for tests.</summary>
    public static void Emit(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct, int maxEvents = MaxEvents, int maxKeys = MaxKeys)
    {
        var gaps = new Gaps();
        try
        {
            EmitCore(reg, item, sink, result, gaps, new Budget(maxEvents, maxKeys), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            gaps.Add("citirea TaskCache s-a oprit", "Activitățile rămase din cache nu au fost citite", $"{ex.GetType().Name}: {ex.Message}");
        }
        gaps.Flush(result);
    }

    private static void EmitCore(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result, Gaps gaps, Budget budget, CancellationToken ct)
    {
        var root = reg.OpenKey(KeyPath);
        if (root is null)
        {
            result.Gaps.Add(new EvidenceGap("TaskCache", EvidenceStatus.NotAvailable, "cheia TaskCache lipsește din hive", "Activitățile planificate din registru sunt necunoscute",
                "System32\\Tasks, Task Scheduler pe stația vie", "Posibil"));
            return;
        }

        var tree = WalkTree(reg, gaps, budget.MaxKeys, ct);
        var treeById = new Dictionary<string, TreeEntry>(StringComparer.OrdinalIgnoreCase);
        var duplicatePaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tree)
        {
            if (treeById.TryAdd(t.Id, t)) continue;
            if (!duplicatePaths.TryGetValue(t.Id, out var paths)) duplicatePaths[t.Id] = paths = [treeById[t.Id].Name];
            paths.Add(t.Name);
        }
        foreach (var (id, paths) in duplicatePaths)
            gaps.Add("aceeași activitate (Id) apare în mai multe locuri din Tree", "O activitate poate fi înregistrată sub mai multe căi; doar prima este atașată în câmpul TreeName", $"{id}: {string.Join(" | ", paths.Select(p => Flat(p, 120)))}");

        var categoriesOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in Categories)
        {
            RawKey? ck;
            try { ck = reg.OpenKey($@"{KeyPath}\{cat}"); }
            catch (InvalidDataException ex) { gaps.Add("categorie ilizibilă", "Categoria nu a fost atașată activităților", $"{cat}: {ex.Message}"); continue; }
            if (ck is null) continue;
            foreach (var name in ck.SubkeyNames)
            {
                if (!categoriesOf.TryGetValue(Guid(name), out var l)) categoriesOf[Guid(name)] = l = [];
                if (!l.Contains(cat)) l.Add(cat);
            }
        }

        var tasksKey = reg.OpenKey(KeyPath + @"\Tasks");
        var seenTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        if (tasksKey is null)
            gaps.Add("cheia Tasks lipsește", "Nu se știe ce acțiuni rulează activitățile din Tree", KeyPath + @"\Tasks");
        else
        {
            IReadOnlyList<int> offsets;
            try { offsets = reg.ChildOffsets(tasksKey); }
            catch (InvalidDataException ex) { gaps.Add("lista de chei Tasks este ilizibilă", "Activitățile nu au fost citite", ex.Message); offsets = []; }
            var visited = new HashSet<int>();
            var parsedActions = new Dictionary<(int Cell, int Length), ActionsBlob>();   // by data cell offset AND declared length (review r2: two values on one cell with different sizes hold different blobs)
            foreach (var off in offsets)
            {
                ct.ThrowIfCancellationRequested();
                if (++total > budget.MaxKeys) { gaps.Add("Tasks depășește limita de chei", "Restul activităților nu a fost citit", $"{budget.MaxKeys} chei"); break; }
                if (!visited.Add(off)) { gaps.Add("cheie Tasks citită de două ori", "A doua apariție ignorată", off.ToString(CultureInfo.InvariantCulture)); continue; }
                RawKey task;
                try { task = reg.ReadChild(tasksKey, off); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {   // one unreadable task (including a hostile value size) must not stop the others
                    string label = $"cheie la 0x{off:X}";
                    try { var id = Guid(reg.ChildName(off)); seenTaskIds.Add(id); label = id; } catch (Exception) { /* name unreadable too: the offset stays the label */ }
                    gaps.Add("cheie Tasks ilizibilă", "Activitatea nu a fost citită; celelalte au fost citite", $"{label}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                var guid = Guid(task.Path[(task.Path.LastIndexOf('\\') + 1)..]);
                seenTaskIds.Add(guid);
                EmitTask(task, guid, treeById.GetValueOrDefault(guid), duplicatePaths.GetValueOrDefault(guid), categoriesOf.GetValueOrDefault(guid), item, sink, result, gaps, budget, parsedActions);
                if (budget.Exhausted) break;
            }
        }

        foreach (var t in tree)
        {
            if (budget.Exhausted) break;
            if (seenTaskIds.Contains(t.Id)) continue;
            if (!budget.Take()) break;
            gaps.Add("intrare Tree cu Id fără cheie în Tasks", "Nu se știe ce rulează activitatea", $"{t.Name} -> {t.Id}");
            sink.Add(new TimelineEvent
            {
                Time = Hive.KeyTime(root), TimeSemantics = "TaskCache key last written (no Tasks key for this entry)",
                Source = "TaskCache", EvidenceId = item.EvidenceId, Task = t.Name,
                Summary = $"Intrare în cache-ul activităților planificate fără cheie în Tasks: {Flat(t.Name)} (Id {Flat(t.Id, 64)}); acțiunile nu pot fi citite",
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $@"SOFTWARE\{KeyPath}\Tree{t.Name}",
                Fields =
                {
                    ["TaskUri"] = t.Name, ["TaskId"] = t.Id, ["TreeIndex"] = t.Index, ["TreeHasSd"] = t.HasSd ? "true" : "false",
                    ["Anomaly"] = "TreeIdWithoutTasksKey", ["ActionType"] = "",
                    ["TreeDuplicateIdPaths"] = duplicatePaths.TryGetValue(t.Id, out var dp) ? string.Join(" | ", dp) : "",
                },
            });
            result.Records++;
        }

        if (budget.Exhausted)
            gaps.Add($"limită de {budget.MaxEvents} evenimente TaskCache atinsă", "Activitățile și acțiunile rămase din cache nu au fost emise", $"{budget.MaxEvents} evenimente");
    }

    private static void EmitTask(RawKey task, string guid, TreeEntry? tree, List<string>? duplicateTreePaths, List<string>? cats, EvidenceItem item, IEventSink sink, ParseResult result, Gaps gaps,
                                 Budget budget, Dictionary<(int Cell, int Length), ActionsBlob> parsedActions)
    {
        string V(string n) => task.Value(n)?.AsText ?? "";
        var path = V("Path");
        var uri = V("URI") is { Length: > 0 } u ? u : path.Length > 0 ? path : tree?.Name ?? guid;
        var anomalies = new List<string>();
        if (tree is null) { anomalies.Add("TasksKeyWithoutTreeEntry"); gaps.Add("cheie Tasks fără intrare în Tree", "Activitatea nu apare în listări (Tree); lipsește și SD-ul din Tree", $"{uri} ({guid})"); }
        if (duplicateTreePaths is not null) { anomalies.Add("TreeDuplicateId"); }
        if (tree is not null && !tree.HasSd) { anomalies.Add("TreeEntryWithoutSd"); gaps.Add("intrare Tree fără valoarea SD", "lipsește SD; activitatea nu apare în listări", $"{tree.Name} ({guid})"); }

        DynamicInfoRecord? dyn = null;
        if (task.Value("DynamicInfo") is { } dv)
        {
            var (info, gap) = ParseDynamicInfo(dv.Data);
            dyn = info;
            if (gap is not null) { anomalies.Add("DynamicInfoUnreadable"); gaps.Add("DynamicInfo nu a putut fi interpretat", "Momentele de înregistrare și ultima rulare lipsesc", $"{uri}: {gap}"); }
        }

        ActionsBlob? blob = null;
        if (task.Value("Actions") is { } av)
        {
            if (av.DataCellOffset >= 0 && parsedActions.TryGetValue((av.DataCellOffset, av.Data.Length), out var known)) blob = known;
            else
            {
                blob = ParseActions(av.Data);
                if (av.DataCellOffset >= 0) parsedActions[(av.DataCellOffset, av.Data.Length)] = blob;
            }
            if (blob.Gap is not null) { anomalies.Add("ActionsUnreadable"); gaps.Add("blobul Actions nu a putut fi interpretat complet", "Acțiunile activității pot fi incomplete", $"{uri}: {blob.Gap}"); }
        }
        else { anomalies.Add("ActionsMissing"); gaps.Add("activitatea nu are valoarea Actions", "Nu se știe ce rulează activitatea", uri); }

        var time = dyn is not null && Timestamp.FromFileTime(dyn.CacheRegisteredFileTime, "DynamicInfo FILETIME @4 (moment of registration in the cache)") is { IsKnown: true } ts
            ? ts : Hive.KeyTime(task);
        var semantics = time.IsKnown && dyn is not null && time.Raw == dyn.CacheRegisteredFileTime.ToString(CultureInfo.InvariantCulture)
            ? "TaskCache DynamicInfo FILETIME at offset 4: momentul înregistrării în cache (poate fi o reînregistrare la actualizare), nu crearea activității"
            : "Tasks subkey last written (DynamicInfo registration time unavailable)";

        var common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaskUri"] = uri, ["TaskPath"] = path, ["TaskId"] = guid, ["Author"] = V("Author"), ["Description"] = V("Description"),
            ["RegistrationDate"] = V("Date"), ["TaskSource"] = V("Source"), ["Schema"] = V("Schema"),
            ["ActionContext"] = blob?.Context ?? "", ["Categories"] = cats is null ? "" : string.Join(",", cats),
            ["CacheRegisteredUtc"] = dyn is null ? "" : Iso(dyn.CacheRegisteredFileTime),
            ["LastRunUtc"] = dyn is null || dyn.LastRunFileTime == 0 ? "" : Iso(dyn.LastRunFileTime),
            ["LastRunNever"] = dyn is null ? "" : dyn.LastRunFileTime == 0 ? "true" : "false",
            // Equals LastTaskResult for tasks that ran and were not running at capture; for never-run or running tasks the service reports its own codes.
            ["LastResultRaw"] = dyn is null || dyn.LastRunFileTime == 0 ? "" : dyn.LastResultRaw.ToString(CultureInfo.InvariantCulture),
            ["DynamicInfoFileTime28Utc"] = dyn is null ? "" : Iso(dyn.Time28FileTime),
            ["TreeName"] = tree?.Name ?? "", ["TreeIndex"] = tree?.Index ?? "", ["TreeHasSd"] = tree is null ? "" : tree.HasSd ? "true" : "false",
            ["TasksKeyLastWriteUtc"] = task.LastWriteUtc.ToString("o", CultureInfo.InvariantCulture),
            ["TriggersLength"] = task.Value("Triggers")?.Data.Length.ToString(CultureInfo.InvariantCulture) ?? "",
            ["TreeDuplicateIdPaths"] = duplicateTreePaths is null ? "" : string.Join(" | ", duplicateTreePaths),
            ["Anomaly"] = string.Join(",", anomalies),
        };
        var locator = $@"SOFTWARE\{KeyPath}\Tasks\{guid}";
        var actions = blob is { Actions.Count: > 0 } ? blob.Actions : [new ActionRecord("", "", "", "", "", "", "")];
        int index = 0;
        foreach (var a in actions)
        {
            if (!budget.Take()) return;
            index++;
            var fields = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
            {
                ["ActionType"] = a.Type, ["Command"] = a.Command, ["Arguments"] = a.Arguments, ["WorkingDirectory"] = a.WorkingDirectory,
                ["ComClassId"] = a.ClassId, ["ComData"] = a.Data, ["ActionIndex"] = index.ToString(CultureInfo.InvariantCulture),
            };
            var exe = a.Command.Trim().Trim('"');
            var what = a.Command.Length > 0 ? $"{a.Command} {a.Arguments}".Trim()
                     : a.ClassId.Length > 0 ? $"handler COM {a.ClassId}" + (a.Data.Length > 0 ? $" {a.Data}" : "")
                     : a.Type.Length > 0 ? $"acțiune de tip {a.Type} neinterpretată" : "fără acțiune lizibilă";
            sink.Add(new TimelineEvent
            {
                Time = time, TimeSemantics = semantics,
                Source = "TaskCache", EvidenceId = item.EvidenceId, Task = uri,
                Path = exe, Process = exe.Length > 0 ? WinPath.GetFileName(exe) : "",
                Summary = $"Activitate planificată în cache-ul din registru {Flat(uri)}: {Flat(what)}" + (anomalies.Count > 0 ? $" ({string.Join(", ", anomalies)})" : ""),
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = blob is null ? locator : $@"{locator}\Actions[{index}]", Fields = fields,
            });
            result.Records++;
        }
    }
}
