using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// The common Open/Save dialog lists of NTUSER.DAT (Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32): OpenSavePidlMRU (one key per extension,
/// "*" = all, each value a shell item list), LastVisitedPidlMRU (program name + the folder the dialog last showed for it) and CIDSizeMRU (program name only;
/// the size data after the name is not interpreted). Only the newest entry of a key (first in MRUListEx) gets the key's last write time.
/// An entry shows that a file or folder was chosen in a common dialog under this account at some point; it does not prove the file was read or written, or by whom.
/// </summary>
internal static partial class UserActivity
{
    internal const string ComDlgKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32";
    internal const string OpenSaveSource = "OpenSaveMRU", LastVisitedSource = "LastVisitedMRU", CidSizeSource = "CIDSizeMRU";
    /// <summary>Most values read from one ComDlg32 list (real lists hold at most a few dozen).</summary>
    internal const int MaxComDlgValues = 1000;
    /// <summary>Longest value read as a shell item list; a longer one is cut here and the cut is noted.</summary>
    internal const int MaxComDlgPidlBytes = 32 * 1024;
    internal const int MaxComDlgSubkeys = 1000;
    /// <summary>Bytes of the item list shown as hex (the whole list is never copied into the event).</summary>
    internal const int PidlHexBytes = 96;
    private const int MaxExeNameChars = 260;
    internal const string ComDlgLimitation = "arată că un fișier sau folder a fost ales într-o fereastră comună Deschide/Salvează sub acest cont, la un moment nedatat; nu dovedește că fișierul a fost citit sau scris și nu arată cine era la tastatură";
    private const string ComDlgFirstSemantics = "ComDlg32 key last written: the moment of the most recent choice in this list (the value named first in MRUListEx)";
    private const string ComDlgOtherSemantics = "no time of its own (only the newest entry of the key, the one named first in MRUListEx, has the key's time)";

    /// <summary>LastVisitedPidlMRU alone, so one corrupt key does not abort the other two (each is run under its own Guarded call).</summary>
    internal static void ComDlgLastVisited(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        if (reg.OpenKey(ComDlgKey + @"\LastVisitedPidlMRU") is { } k)
            ComDlgKeyEvents(k, LastVisitedSource, "", UserHiveParser.ProfileName(item.OriginalPath), KnownFolders.ProfileDirOf(item), item, sink, result);
    }

    internal static void ComDlgCidSize(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        if (reg.OpenKey(ComDlgKey + @"\CIDSizeMRU") is { } k)
            ComDlgKeyEvents(k, CidSizeSource, "", UserHiveParser.ProfileName(item.OriginalPath), KnownFolders.ProfileDirOf(item), item, sink, result);
    }

    internal static void ComDlgOpenSave(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var account = UserHiveParser.ProfileName(item.OriginalPath);
        var profileDir = KnownFolders.ProfileDirOf(item);
        var os = reg.OpenKey(ComDlgKey + @"\OpenSavePidlMRU");
        if (os is null) return;
        ComDlgKeyEvents(os, OpenSaveSource, "", account, profileDir, item, sink, result);
        int n = 0, damaged = 0;
        foreach (var off in reg.ChildOffsets(os))
        {
            ct.ThrowIfCancellationRequested();
            if (++n > MaxComDlgSubkeys)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap("OpenSaveMRU", EvidenceStatus.Partial, $"peste {MaxComDlgSubkeys:N0} subchei OpenSavePidlMRU; restul nu a fost citit",
                    "Fișiere alese în ferestrele Deschide/Salvează pot lipsi din cronologie", "LNK din Recent, Jump Lists, ShellBags", "Posibil"));
                break;
            }
            RawKey sub;
            try { sub = reg.ReadChild(os, off); }
            catch (InvalidDataException) { damaged++; continue; }
            ComDlgKeyEvents(sub, OpenSaveSource, sub.Path[(sub.Path.LastIndexOf('\\') + 1)..], account, profileDir, item, sink, result);
        }
        if (damaged > 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("OpenSaveMRU", EvidenceStatus.Partial, $"{LogAnalyzer.Dfir.Language.RoText.Count(damaged, "subcheie OpenSavePidlMRU", "subchei OpenSavePidlMRU")} nu au putut fi citite (celule corupte)",
                "Intrările pentru acele extensii pot lipsi din cronologie", "LNK din Recent, Jump Lists, ShellBags", "Posibil"));
        }
    }

    /// <summary>Indexes MRUListEx names before the 0xFFFFFFFF terminator (only those; anything else is "unlisted"). Bounded by the value's own length.</summary>
    private static HashSet<string> ListedIndexes(RawKey key)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (key.Value("MRUListEx") is not { } list) return set;
        var d = list.Data;
        for (int i = 0; i + 4 <= d.Length; i += 4)
        {
            uint idx = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i));
            if (idx == 0xFFFFFFFF) break;
            set.Add(idx.ToString(CultureInfo.InvariantCulture));
        }
        return set;
    }

    /// <summary>
    /// The path text of a shell item list as shown to the user, with the absolute path when the root is a known folder; <paramref name="notes"/> collects what is partial.
    /// <c>ResolutionNote</c> says why a known folder could not be placed on disk (unknown profile folder); <c>ItemCapped</c> is true when the list had more than <see cref="ShellItems.MaxItems"/> items.
    /// </summary>
    internal static (string Path, string Resolved, int Items, string ResolutionNote, bool ItemCapped) DecodePidl(ReadOnlySpan<byte> pidl, string profileDir, List<string> notes)
    {
        var list = ShellItems.ParseList(pidl);
        notes.AddRange(list.Issues);
        bool capped = list.Issues.Any(i => i.Contains($"mai mult de {ShellItems.MaxItems}", StringComparison.Ordinal));
        if (list.Items.Count == 0) { notes.Add("niciun element shell citibil"); return ("", "", 0, "", capped); }
        var segments = list.Items.Select(i => i.PathSegment).ToArray();
        var full = ShellItems.JoinPath(segments);
        var path = segments.Length > 1 && segments[0] == "Acest PC" ? ShellItems.JoinPath(segments.Skip(1)) : full;
        int unknown = list.Items.Count(i => i.Kind == ShellItemKind.Unknown);
        if (unknown > 0) notes.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(unknown, "element necunoscut", "elemente necunoscute")} în cale; calea este parțială");
        string resolved = "", resolutionNote = "";
        int rootIdx = -1;
        for (int i = 0; i < list.Items.Count; i++)
            if (list.Items[i].Kind == ShellItemKind.RootFolder && KnownFolders.IsKnownRoot(list.Items[i].Guid)) { rootIdx = i; break; }
        if (rootIdx >= 0)
        {
            var guid = list.Items[rootIdx].Guid;
            if (KnownFolders.Resolve(guid, segments[rootIdx..], profileDir) is { Length: > 0 } r) resolved = r;
            else if (KnownFolders.IsProfileRoot(guid))
                resolutionNote = "folderele cunoscute (Documente, Desktop, Descărcări...) nu au putut fi plasate pe discul stației: calea profilului nu provine dintr-o colectare de pe stație (hive importat sau profil necunoscut)";
        }
        return (path, resolved, list.Items.Count, resolutionNote, capped);
    }

    private static void ComDlgKeyEvents(RawKey key, string source, string extension, string account, string profileDir, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var (ordered, anomalies, orderKnown) = MruOrder(key);
        if (ordered.Count == 0 && anomalies == 0) return;
        NoteAnomalies(result, source, key.Path, anomalies);
        var listed = ListedIndexes(key);
        // Only the value named by the FIRST MRUListEx index carries the key's time. If that value does not exist (deleted), the key time is the deletion time and nobody gets it.
        string? topName = null;
        if (orderKnown && key.Value("MRUListEx") is { } mru && mru.Data.Length >= 4)
        {
            uint top = BinaryPrimitives.ReadUInt32LittleEndian(mru.Data.AsSpan());
            if (top != 0xFFFFFFFF) topName = top.ToString(CultureInfo.InvariantCulture);
        }
        if (topName is not null && !ordered.Any(o => o.Name == topName))
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(source, EvidenceStatus.Partial, $"intrarea cea mai nouă din MRUListEx din {key.Path} (valoarea {topName}) nu există; ora cheii nu a fost atribuită niciunei intrări",
                "Ora ultimei alegeri din această listă nu poate fi legată de o intrare (valoarea a fost ștearsă sau hive-ul a fost modificat)", "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
        }
        if (ordered.Count > MaxComDlgValues)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(source, EvidenceStatus.Partial, $"{key.Path} are {ordered.Count:N0} valori; primele {MaxComDlgValues:N0} au fost citite",
                "Intrări mai vechi din această listă pot lipsi din cronologie", "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
        }
        List<string> undecodable = [], cutLists = [], cappedLists = [], cappedNames = [];
        int pos = 0;
        foreach (var (valueName, value) in ordered.Take(MaxComDlgValues))
        {
            bool isListed = listed.Contains(valueName);
            bool first = topName is not null && valueName == topName;
            var d = value.Data;
            var notes = new List<string>();
            string exe = "";
            int pidlStart = 0;
            bool nameCapped = false;
            if (source != OpenSaveSource)
            {
                // UTF-16 program name, NUL-terminated; for LastVisitedPidlMRU the item list follows, for CIDSizeMRU only window-size data.
                int end = 0;
                while (end + 2 <= d.Length && (d[end] != 0 || d[end + 1] != 0) && end / 2 < MaxExeNameChars) end += 2;
                exe = Encoding.Unicode.GetString(d, 0, end);
                bool terminatedName = end + 2 <= d.Length && d[end] == 0 && d[end + 1] == 0;
                if (!terminatedName)
                {
                    nameCapped = end / 2 >= MaxExeNameChars;
                    notes.Add(nameCapped ? $"numele programului depășește limita de {MaxExeNameChars} caractere" : "numele programului nu are terminator NUL");
                    if (nameCapped) cappedNames.Add(valueName);
                }
                pidlStart = Math.Min(d.Length, end + 2);
            }
            string path = "", resolved = "", pidlHex = "", resolutionNote = "";
            if (source != CidSizeSource && !nameCapped)   // a PIDL is never decoded from inside the bytes of an over-long name
            {
                int len = d.Length - pidlStart;
                if (len > MaxComDlgPidlBytes) { notes.Add($"lista de elemente are {len:N0} octeți; s-au citit primii {MaxComDlgPidlBytes:N0}"); len = MaxComDlgPidlBytes; cutLists.Add(valueName); }
                var pidl = d.AsSpan(pidlStart, Math.Max(len, 0));
                pidlHex = Convert.ToHexString(pidl[..Math.Min(pidl.Length, PidlHexBytes)]) + (pidl.Length > PidlHexBytes ? "…" : "");
                if (pidl.Length == 0) { notes.Add("valoarea nu conține listă de elemente shell"); undecodable.Add(valueName); }
                else
                {
                    int items;
                    bool itemCap;
                    (path, resolved, items, resolutionNote, itemCap) = DecodePidl(pidl, profileDir, notes);
                    if (items == 0) undecodable.Add(valueName);
                    if (itemCap) cappedLists.Add(valueName);
                }
            }
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Key"] = key.Path, ["Extension"] = extension, ["ValueName"] = valueName,
                ["MruPosition"] = pos.ToString(CultureInfo.InvariantCulture), ["MruListed"] = isListed ? "true" : "false",
                ["ExeName"] = exe, ["Path"] = path, ["PidlHex"] = pidlHex, ["Account"] = account,
                ["DecodeNote"] = notes.Count == 0 ? "" : string.Join("; ", notes),
                ["Limitation"] = ComDlgLimitation,
            };
            if (resolved.Length > 0) fields["ResolvedPath"] = resolved;
            else if (resolutionNote.Length > 0) fields["ResolutionNote"] = resolutionNote;
            string what = source switch
            {
                OpenSaveSource => extension.Length == 0 ? "lista de bază" : extension == "*" ? "toate extensiile" : $"extensia {extension}",
                LastVisitedSource => "ultimul folder al programului",
                _ => "dimensiunea ferestrei programului",
            };
            string subject = source == CidSizeSource ? $"programul „{exe}”" : source == LastVisitedSource ? $"programul „{exe}”, folder „{path}”" : $"„{path}”";
            sink.Add(new TimelineEvent
            {
                Time = first ? Hive.KeyTime(key) : Timestamp.Unknown(),
                TimeSemantics = first ? ComDlgFirstSemantics : ComDlgOtherSemantics,
                TimeUncertainty = first ? "cheie de registru: ora celei mai noi intrări din listă, nu a fiecărei intrări" : "",
                Source = source, EvidenceId = item.EvidenceId, Path = path, Process = exe,
                Summary = $"Fereastra Deschide/Salvează ({source}, {what}) în profilul „{account}”: {subject} ({(isListed ? $"poziția {pos + 1}" : "neinclusă în MRUListEx")}); {ComDlgLimitation}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Low,
                Locator = $@"NTUSER\{key.Path}\{valueName}", Fields = fields,
            });
            result.Records++;
            pos++;
        }
        KeyGap(result, source, key.Path, undecodable, "nu conțin niciun element shell citibil", "Fișiere sau foldere alese în fereastră pot lipsi din cronologie");
        KeyGap(result, source, key.Path, cutLists, $"depășesc {MaxComDlgPidlBytes:N0} octeți; s-au citit doar primii {MaxComDlgPidlBytes:N0}", "Calea poate fi incompletă");
        KeyGap(result, source, key.Path, cappedLists, $"au mai mult de {ShellItems.MaxItems} elemente shell; restul nu a fost citit", "Calea poate fi incompletă");
        KeyGap(result, source, key.Path, cappedNames, $"au numele programului mai lung de {MaxExeNameChars} caractere; lista de elemente nu a fost decodată", "Folderul ales poate lipsi din cronologie");
    }

    /// <summary>One gap per key and problem, with at most three example value names (the count is exact, the examples are bounded).</summary>
    private static void KeyGap(ParseResult result, string source, string keyPath, List<string> names, string what, string impact)
    {
        if (names.Count == 0) return;
        result.MalformedRecords++;
        var examples = string.Join(", ", names.Take(3)) + (names.Count > 3 ? ", …" : "");
        result.Gaps.Add(new EvidenceGap(source, EvidenceStatus.Partial,
            $"{LogAnalyzer.Dfir.Language.RoText.Count(names.Count, "valoare", "valori")} din {keyPath} {what} (ex.: {examples})",
            impact, "Alt exemplar al hive-ului", "Posibil, din alt exemplar"));
    }
}
