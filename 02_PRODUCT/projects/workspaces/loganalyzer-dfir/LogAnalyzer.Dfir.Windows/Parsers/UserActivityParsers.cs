using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Readers of what a user opened or browsed, shared by <see cref="UserHiveParser"/> (NTUSER.DAT) and <see cref="ShellBagsParser"/> (UsrClass.dat):
/// ShellBags (BagMRU), RecentDocs and the Office MRU lists. Hostile hives are the expected input: every walk is bounded (depth, nodes, entries),
/// uses an explicit stack, and a corrupt structure is a reported gap, never an exception to the caller.
/// </summary>
internal static class UserActivity
{
    /// <summary>Deepest BagMRU nesting followed. Real profiles stay below about 30.</summary>
    internal const int MaxBagDepth = 48;
    /// <summary>Most BagMRU keys visited in one hive.</summary>
    internal const int MaxBagNodes = 20000;
    /// <summary>Most subkey reads in one walk; each read also reads the names of that key's children, so this bounds the work, not only the node count.</summary>
    internal const int MaxBagReads = 40000;
    private const int MaxRecentDocsSubkeys = 1000;
    private const int MaxOfficeApps = 200;
    private const int MaxOfficeUserIds = 100;

    internal const string RecentDocsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs";
    internal const string OfficeKey = @"Software\Microsoft\Office";
    internal static readonly string[] BagRootsUsrClass =
        [@"Local Settings\Software\Microsoft\Windows\Shell\BagMRU", @"Wow6432Node\Local Settings\Software\Microsoft\Windows\Shell\BagMRU"];
    internal static readonly string[] BagRootsNtUser =
        [@"Software\Microsoft\Windows\Shell\BagMRU", @"Software\Microsoft\Windows\ShellNoRoam\BagMRU"];

    private const string BagTimeSemantics = "BagMRU key last written: the last time Explorer updated this folder's entry (not the first time it was opened, and not proof that a file was read)";
    private const string BagLeafTimeSemantics = "parent BagMRU key last written: the newest entry of that key (only the first entry of MRUListEx carries it); not the first time it was opened, and not proof that a file was read";
    private const string BagUndatedSemantics = "no time of its own: only the newest entry of the parent BagMRU key has the key's time";
    internal const string DosTimesNote = "Orele din elementul shell sunt DOS (oră locală fără fus orar, rezoluție 2 secunde): MAC-ul propriu al folderului când a fost răsfoit, nu momente de acces";

    /// <summary>Runs one reader; a corrupt structure becomes a reported gap and the rest of the hive is still read.</summary>
    internal static void Guarded(string artifact, ParseResult result, Action read)
    {
        try { read(); }
        catch (InvalidDataException ex)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial, $"structură coruptă în hive: {ex.Message}",
                $"Intrările {artifact} citite după acest punct pot lipsi", "Alt exemplar al hive-ului (VSS, Windows.old, copie de rezervă)", "Posibil, din alt exemplar"));
        }
    }

    // ---- MRU order ----

    /// <summary>
    /// Numbered entries of an MRU key in MRUListEx order (newest first). An index with no value, a repeated index and a list without the
    /// 0xFFFFFFFF terminator are counted in <c>Anomalies</c>; numbered values the list does not mention follow in numeric order.
    /// </summary>
    internal static (List<(string Name, RawValue Value)> Ordered, int Anomalies, bool OrderKnown) MruOrder(RawKey key)
    {
        var entries = new Dictionary<string, RawValue>(StringComparer.Ordinal);
        foreach (var v in key.Values)
            if (v.Name.Length is > 0 and <= 9 && v.Name.All(char.IsAsciiDigit)) entries.TryAdd(int.Parse(v.Name, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), v);
        var ordered = new List<(string, RawValue)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int anomalies = 0;
        var list = key.Value("MRUListEx");
        if (list is not null)
        {
            var d = list.Data;
            bool terminated = false;
            for (int i = 0; i + 4 <= d.Length; i += 4)
            {
                uint idx = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i));
                if (idx == 0xFFFFFFFF) { terminated = true; break; }
                var name = idx.ToString(CultureInfo.InvariantCulture);
                if (!entries.TryGetValue(name, out var v) || !seen.Add(name)) { anomalies++; continue; }
                ordered.Add((name, v));
            }
            if (!terminated) anomalies++;
        }
        else if (entries.Count > 0) anomalies++;   // no MRUListEx: the order of the entries is not known
        foreach (var n in entries.Keys.OrderBy(k => int.Parse(k, CultureInfo.InvariantCulture)))
            if (seen.Add(n)) ordered.Add((n, entries[n]));
        return (ordered, anomalies, list is not null);
    }

    private static void NoteAnomalies(ParseResult result, string artifact, string keyPath, int anomalies)
    {
        if (anomalies == 0) return;
        result.MalformedRecords++;
        result.Gaps.Add(new EvidenceGap(artifact, EvidenceStatus.Partial,
            $"MRUListEx din {keyPath} are {LogAnalyzer.Dfir.Language.RoText.Count(anomalies, "anomalie", "anomalii")} (index fără valoare, index repetat sau fără terminator)",
            "Ordinea intrărilor din această cheie poate fi incompletă sau aproximativă", "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
    }

    private static string DosIso(DosDateTime? t) => t?.LocalIso ?? "";

    private static void AddShellFields(Dictionary<string, string> f, ShellItem s)
    {
        f["ShellItemType"] = $"0x{s.TypeByte:X2}";
        f["ShellItemKind"] = s.Kind.ToString();
        f["ShortName"] = s.ShortName;
        if (s.Modified is not null) f["ShellModifiedLocal"] = DosIso(s.Modified);
        if (s.Created is not null) f["ShellCreatedLocal"] = DosIso(s.Created);
        if (s.Accessed is not null) f["ShellAccessedLocal"] = DosIso(s.Accessed);
        if (s.Modified is not null || s.Created is not null || s.Accessed is not null) f["ShellTimesNote"] = DosTimesNote;
        if (s.MftEntry is { } e) f["MftEntry"] = e.ToString(CultureInfo.InvariantCulture);
        if (s.MftSequence is { } q) f["MftSequence"] = q.ToString(CultureInfo.InvariantCulture);
        if (s.Guid.Length > 0) f["Guid"] = s.Guid;
        if (s.Kind == ShellItemKind.Unknown || s.Truncated) f["ItemHex"] = s.Hex;
        if (s.Truncated) f["ItemTruncated"] = "true";
    }

    // ---- ShellBags ----

    private sealed record Frame(RawKey Key, string[] Segments, int Depth);

    /// <summary>Walks one BagMRU tree: one event per folder entry with its full path; shell-item times go in the details, never in the event time.</summary>
    internal static void ShellBags(RawRegistry reg, string hiveLabel, string rootPath, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var root = reg.OpenKey(rootPath);
        if (root is null) return;
        var account = UserHiveParser.ProfileName(item.OriginalPath);
        var stack = new Stack<Frame>();
        // Visited by cell offset, not by path: a hive whose subkey lists loop back to an ancestor reaches the same cell twice.
        var visited = new HashSet<int> { root.CellOffset };
        stack.Push(new Frame(root, [], 0));
        int nodes = 0, orphans = 0, tooDeep = 0, cycles = 0, damaged = 0, reads = 0;
        bool capped = false;
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (nodes >= MaxBagNodes) { capped = true; break; }
            nodes++;
            var frame = stack.Pop();
            var key = frame.Key;
            var (ordered, anomalies, orderKnown) = MruOrder(key);
            NoteAnomalies(result, "ShellBags", key.Path, anomalies);

            // The children are read once per node, by offset: reading each by path would rescan the siblings every time.
            var children = new Dictionary<string, RawKey>(StringComparer.OrdinalIgnoreCase);
            foreach (var off in reg.ChildOffsets(key))
            {
                ct.ThrowIfCancellationRequested();
                if (children.Count >= MaxBagNodes) break;
                // Visited is checked before the key is read: a shared or looping subkey list must not cost a read per reference.
                if (!visited.Add(off)) { cycles++; continue; }
                if (++reads > MaxBagReads) { capped = true; break; }
                try
                {
                    var child = reg.ReadChild(key, off);
                    var childName = child.Path[(child.Path.LastIndexOf('\\') + 1)..];
                    children.TryAdd(childName, child);
                }
                catch (InvalidDataException) { damaged++; }   // one damaged subkey must not hide its siblings
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int pos = 0;
            foreach (var (name, value) in ordered)
            {
                var list = ShellItems.ParseList(value.Data);
                if (list.Items.Count == 0)
                {
                    result.MalformedRecords++;
                    result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.Partial, $"valoarea {name} din {key.Path} nu conține niciun element shell citibil",
                        "Un folder răsfoit de utilizator lipsește din cronologie", "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
                    pos++;
                    continue;
                }
                var shell = list.Items[0];
                var segments = frame.Segments.Append(shell.PathSegment).ToArray();
                var full = ShellItems.JoinPath(segments);
                Timestamp time;
                string semantics, uncertainty;
                if (children.TryGetValue(name, out var childKey))
                {
                    used.Add(name);
                    time = Hive.KeyTime(childKey); semantics = BagTimeSemantics;
                    uncertainty = "cheie de registru: ora ultimei actualizări a intrării de către Explorer, nu momentul primei deschideri";
                    if (frame.Depth + 1 >= MaxBagDepth) tooDeep++;
                    else stack.Push(new Frame(childKey, segments, frame.Depth + 1));
                }
                else if (pos == 0 && orderKnown)
                {
                    time = Hive.KeyTime(key); semantics = BagLeafTimeSemantics;
                    uncertainty = "ora cheii părinte: se aplică doar celei mai noi intrări din cheie";
                }
                else { time = Timestamp.Unknown(); semantics = BagUndatedSemantics; uncertainty = ""; }

                var displayPath = segments.Length > 1 && segments[0] == "Acest PC" ? ShellItems.JoinPath(segments.Skip(1)) : full;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BagPath"] = full, ["Hive"] = hiveLabel, ["BagMruKey"] = key.Path, ["ValueName"] = name,
                    ["MruPosition"] = pos.ToString(CultureInfo.InvariantCulture), ["Depth"] = (frame.Depth + 1).ToString(CultureInfo.InvariantCulture),
                    ["Account"] = account, ["ExtensionVersion"] = shell.ExtensionVersion.ToString(CultureInfo.InvariantCulture),
                };
                AddShellFields(fields, shell);
                if (list.Issues.Count > 0) fields["ItemIssues"] = string.Join("; ", list.Issues);
                sink.Add(new TimelineEvent
                {
                    Time = time, TimeSemantics = semantics, TimeUncertainty = uncertainty,
                    Source = "ShellBags", EvidenceId = item.EvidenceId, Path = displayPath, Process = "",
                    Summary = $"ShellBags: Explorer are o intrare pentru „{full}” în profilul „{account}” (poziția {pos + 1} în lista cheii)",
                    TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                    Locator = $@"{hiveLabel}\{key.Path}\{name}", Fields = fields,
                });
                result.Records++;
                pos++;
            }
            orphans += children.Keys.Count(n => n.Length is > 0 and <= 9 && n.All(char.IsAsciiDigit) && !used.Contains(n));
        }
        if (capped)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.Partial, $"s-au citit {MaxBagNodes:N0} chei BagMRU, limita parserului; restul nu a fost parcurs",
                "Foldere răsfoite de utilizator pot lipsi din cronologie", "Un instrument dedicat pentru ShellBags", "Posibil, cu alt instrument"));
        }
        if (tooDeep > 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.Partial, $"{LogAnalyzer.Dfir.Language.RoText.Count(tooDeep, "ramură", "ramuri")} BagMRU depășesc adâncimea de {MaxBagDepth}; subfolderele lor nu au fost citite",
                "Foldere foarte adânci pot lipsi din cronologie", "Un instrument dedicat pentru ShellBags", "Posibil, cu alt instrument"));
        }
        if (cycles > 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.Partial, $"{LogAnalyzer.Dfir.Language.RoText.Count(cycles, "subcheie BagMRU", "subchei BagMRU")} indică spre o cheie deja citită (listă de subchei circulară sau repetată); au fost ignorate",
                "Hive-ul este corupt sau manipulat; subfolderele acelor ramuri nu au fost citite", "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
        }
        if (damaged > 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.Partial, $"{LogAnalyzer.Dfir.Language.RoText.Count(damaged, "subcheie BagMRU", "subchei BagMRU")} nu au putut fi citite (celule corupte)",
                "Subfolderele acelor ramuri lipsesc din cronologie", "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
        }
        if (orphans > 0)
            result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.Partial, $"{LogAnalyzer.Dfir.Language.RoText.Count(orphans, "subcheie BagMRU", "subchei BagMRU")} nu au valoarea de element corespunzătoare și nu au fost parcurse",
                "Folderele din aceste ramuri nu au un nume cunoscut", "Un instrument dedicat pentru ShellBags", "Posibil, cu alt instrument"));
    }

    // ---- RecentDocs ----

    internal static void RecentDocs(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var account = UserHiveParser.ProfileName(item.OriginalPath);
        var root = reg.OpenKey(RecentDocsKey);
        if (root is null) return;
        RecentDocsKeyEvents(root, "", account, item, sink, result);
        int n = 0, damaged = 0;
        // By offset, one subkey at a time: a damaged extension key is reported and does not hide the others.
        foreach (var off in reg.ChildOffsets(root))
        {
            ct.ThrowIfCancellationRequested();
            if (++n > MaxRecentDocsSubkeys)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap("RecentDocs", EvidenceStatus.Partial, $"peste {MaxRecentDocsSubkeys:N0} subchei RecentDocs; restul nu a fost citit",
                    "Documente recente pot lipsi din cronologie", "LNK din Recent, Jump Lists", "Posibil"));
                break;
            }
            RawKey sub;
            try { sub = reg.ReadChild(root, off); }
            catch (InvalidDataException) { damaged++; continue; }
            var ext = sub.Path[(sub.Path.LastIndexOf('\\') + 1)..];
            RecentDocsKeyEvents(sub, ext, account, item, sink, result);
        }
        if (damaged > 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("RecentDocs", EvidenceStatus.Partial, $"{LogAnalyzer.Dfir.Language.RoText.Count(damaged, "subcheie RecentDocs", "subchei RecentDocs")} nu au putut fi citite (celule corupte)",
                "Documente recente pentru acele extensii pot lipsi din cronologie", "LNK din Recent, Jump Lists", "Posibil"));
        }
    }

    private static void RecentDocsKeyEvents(RawKey key, string extension, string account, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var (ordered, anomalies, orderKnown) = MruOrder(key);
        NoteAnomalies(result, "RecentDocs", key.Path, anomalies);
        int pos = 0;
        foreach (var (valueName, value) in ordered)
        {
            var d = value.Data;
            // UTF-16 display name, NUL-terminated, then the shell item list of the document (Windows 10 may append more data after it).
            int end = 0;
            while (end + 2 <= d.Length && (d[end] != 0 || d[end + 1] != 0)) end += 2;
            var name = Encoding.Unicode.GetString(d, 0, end);
            var shell = end + 2 < d.Length ? ShellItems.ParseList(d.AsSpan(end + 2)) : new ShellItemList([], false, 0, []);
            bool first = pos == 0 && orderKnown;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = name, ["Extension"] = extension, ["Key"] = key.Path, ["ValueName"] = valueName,
                ["MruPosition"] = pos.ToString(CultureInfo.InvariantCulture), ["Account"] = account,
            };
            if (shell.Items.Count > 0) AddShellFields(fields, shell.Items[0]);
            if (shell.Issues.Count > 0) fields["ItemIssues"] = string.Join("; ", shell.Issues);
            var what = extension.Length == 0 ? "toate tipurile" : extension.Equals("Folder", StringComparison.OrdinalIgnoreCase) ? "foldere" : $"extensia {extension}";
            sink.Add(new TimelineEvent
            {
                Time = first ? Hive.KeyTime(key) : Timestamp.Unknown(),
                TimeSemantics = first ? "RecentDocs key last written (time of the newest entry only)" : "no time of its own (only the newest entry of the key has the key's time)",
                TimeUncertainty = first ? "cheie de registru: ora celei mai noi intrări, nu a fiecărei intrări" : "",
                Source = "RecentDocs", EvidenceId = item.EvidenceId, Path = name, Process = "",
                Summary = $"Document recent (RecentDocs, {what}) în profilul „{account}” (poziția {pos + 1}): {(name.Length > 0 ? name : "(nume gol)")}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $@"NTUSER\{key.Path}\{valueName}", Fields = fields,
            });
            result.Records++;
            pos++;
        }
    }

    // ---- Office MRU ----

    internal static void OfficeMru(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var account = UserHiveParser.ProfileName(item.OriginalPath);
        foreach (var ver in reg.SubKeys(OfficeKey))
        {
            ct.ThrowIfCancellationRequested();
            var version = Leaf(ver.Path);
            if (version.Length == 0 || !char.IsAsciiDigit(version[0])) continue;
            int apps = 0;
            foreach (var appName in ver.SubkeyNames)
            {
                if (++apps > MaxOfficeApps) break;
                var appPath = $@"{ver.Path}\{appName}";
                foreach (var kind in new[] { "File MRU", "Place MRU" })
                    if (reg.OpenKey($@"{appPath}\{kind}") is { } k) OfficeKeyEvents(k, version, appName, kind, "", account, item, sink, result);
                var userMru = reg.OpenKey($@"{appPath}\User MRU");
                if (userMru is null) continue;
                int ids = 0;
                foreach (var id in userMru.SubkeyNames)
                {
                    if (++ids > MaxOfficeUserIds) break;
                    foreach (var kind in new[] { "File MRU", "Place MRU" })
                        if (reg.OpenKey($@"{userMru.Path}\{id}\{kind}") is { } k) OfficeKeyEvents(k, version, appName, kind, id, account, item, sink, result);
                }
            }
        }
    }

    private static string Leaf(string path) => path[(path.LastIndexOf('\\') + 1)..];

    private static void OfficeKeyEvents(RawKey key, string version, string app, string kind, string userId, string account, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var entries = key.Values
            .Where(v => v.Name.StartsWith("Item ", StringComparison.OrdinalIgnoreCase) && int.TryParse(v.Name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .OrderBy(v => int.Parse(v.Name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture)).ToList();
        foreach (var v in entries)
        {
            var text = v.AsText;
            if (!TryParseOfficeItem(text, out var path, out var flags, out var rawTime, out long ft))
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap("Office MRU", EvidenceStatus.Partial, $"{v.Name} din {key.Path} nu are forma [F…][T…][O…]*cale",
                    "O intrare Office recentă lipsește din cronologie", "LNK din Recent, Jump Lists", "Posibil"));
                continue;
            }
            var time = Timestamp.FromFileTime(ft, "Office MRU [T] FILETIME");
            bool place = kind.StartsWith("Place", StringComparison.Ordinal);
            sink.Add(new TimelineEvent
            {
                Time = time, TimeSemantics = "last open by that Office application ([T] FILETIME, UTC)",
                Source = "OfficeMRU", EvidenceId = item.EvidenceId, Path = path, Process = "",
                Summary = $"Office {version} {app}: {(place ? "folder recent" : "fișier recent")} în profilul „{account}”: {path}" + (time.IsKnown ? "" : " (ora [T] lipsește sau nu este validă)"),
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $@"NTUSER\{key.Path}\{v.Name}",
                Fields =
                {
                    ["App"] = app, ["OfficeVersion"] = version, ["MruKind"] = kind, ["UserMruId"] = userId, ["Item"] = v.Name, ["Path"] = path,
                    ["Flags"] = flags, ["RawTime"] = rawTime, ["Account"] = account,
                },
            });
            result.Records++;
        }
    }

    /// <summary>Parses "[F00000000][T01D...][O00000000]*C:\path". The path is what follows the first '*'; a bad or missing [T] leaves the time unknown (raw kept).</summary>
    internal static bool TryParseOfficeItem(string text, out string path, out string flags, out string rawTime, out long fileTime)
    {
        path = flags = rawTime = "";
        fileTime = 0;
        int star = text.IndexOf('*');
        if (star < 0 || star + 1 >= text.Length || !text.StartsWith('[')) return false;
        path = text[(star + 1)..];
        var head = text[..star];
        foreach (var part in head.Split('[', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.TrimEnd(']');
            if (p.Length == 0) continue;
            if (p[0] == 'F') flags = p[1..];
            else if (p[0] == 'T')
            {
                rawTime = p[1..];
                if (rawTime.Length is > 0 and <= 16 && ulong.TryParse(rawTime, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var u) && u <= long.MaxValue) fileTime = (long)u;
            }
        }
        return path.Length > 0;
    }
}

/// <summary>
/// UsrClass.dat (HKCU\Software\Classes): ShellBags, the folders Explorer remembers the user browsed (local, removable and network), read from
/// Local Settings\Software\Microsoft\Windows\Shell\BagMRU. The BagMRU key time is the last time Explorer updated that folder's entry.
/// </summary>
public sealed class ShellBagsParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "ShellBagsParser", Version = "1.0", Artifact = "Hive UsrClass.dat — ShellBags (foldere răsfoite în Explorer)",
        SourceTypes = ["usrclass_hive"], FileNames = ["UsrClass.dat", "UsrClass*.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Windows Vista și ulterioare (citire offline cu RawRegistry)",
        FormatVersions = ["BagMRU (Local Settings și Wow6432Node\\Local Settings), elemente shell: rădăcină, volum, folder/fișier, panou de control, URI, rețea, delegat (best effort)", "MRUListEx cu terminator 0xFFFFFFFF"],
        Limitations =
        [
            "Ora cheii BagMRU este ultima actualizare a intrării folderului de către Explorer: nu este prima deschidere și nu dovedește că un fișier a fost citit.",
            "Orele din elementul shell sunt DOS (oră locală fără fus orar, rezoluție 2 secunde): MAC-ul propriu al folderului când a fost răsfoit, nu momente de acces.",
            "Doar intrarea cea mai nouă dintr-o cheie are ora cheii; celelalte sunt ordonate, fără oră.",
            "Elementele neînțelese sunt raportate ca „element necunoscut (tip 0x..)” cu hex; tipurile ZIP/MTP/URI sunt decodate doar aproximativ.",
            "Numele lungi din blocul de extensie sunt validate doar pe date sintetice; jurnalele de tranzacții ale hive-ului nu sunt aplicate.",
            "Limite de siguranță: adâncime 48, 20.000 de chei, 40.000 de citiri de subchei; depășirea lor este raportată ca lacună.",
            "Dacă MRUListEx lipsește, ordinea intrărilor este necunoscută: nicio intrare nu primește ora cheii ca cea mai nouă.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "UserActivityParserTests: hive-uri sintetice (RegfBuilder) cu căi, ore și cazuri ostile (cicluri, adâncime, elemente trunchiate, MRUListEx invalid); fără corpus real",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        foreach (var root in UserActivity.BagRootsUsrClass)
            UserActivity.Guarded("ShellBags", result, () => UserActivity.ShellBags(reg, "UsrClass", root, item, sink, result, ct));
        if (result.Records == 0 && result.MalformedRecords == 0)
            result.Gaps.Add(new EvidenceGap("ShellBags", EvidenceStatus.NotAvailable, "cheia BagMRU lipsește sau este goală în UsrClass.dat",
                "Nu se știe ce foldere a răsfoit utilizatorul în Explorer", "NTUSER.DAT (Shell\\BagMRU), LNK, Jump Lists", "Nu"));
    }
}
