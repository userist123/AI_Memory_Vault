using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// P1 rules of research 2026-10-11 §4: what known USB / shortcut malware leaves behind, so a controller can spot it. Shortcuts on a medium that start a
/// system program (MEDIA-LNK-PAYLOAD), shortcut arguments hidden behind invisible characters (LNK-ARGS-PADDED), a hidden folder on a medium that fills
/// with recent documents (MEDIA-HIDDEN-MAILBOX), the same file on several media or in a profile (MEDIA-SAME-DROP), programs run from a medium
/// (EXEC-FROM-REMOVABLE), entries of the Startup folders (PERSIST-STARTUP-FOLDER) and pasted commands in the Run dialog or the Explorer address bar
/// (CLICKFIX-RUNMRU). Program names and command patterns come from the data file (<see cref="PayloadLists"/>), not from code. Every finding says
/// what the record shows and what it does not; none of them names a malware family or claims that data left the station.
/// </summary>
public static partial class Wp14Rules
{
    /// <summary>Folders Windows or other systems create on a volume by themselves (hidden by design, never a mailbox).</summary>
    private static readonly string[] SystemMediaFolders = ["System Volume Information", "$RECYCLE.BIN", "RECYCLER", ".Trashes", ".Spotlight-V100", ".fseventsd", ".TemporaryItems"];

    private static readonly Regex VolumeRx = new(@"\\VOLUME\{[0-9a-f]+-(?<s>[0-9a-f]{8})\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, LogAnalyzer.Dfir.RegexDefaults.RuleMatchTimeout);

    private static void PayloadFindings(Ctx c, List<Finding> o)
    {
        var letters = RemovableLetterSet(c);
        MediaShortcuts(c, o, letters);
        PaddedShortcuts(c, o, letters);
        HiddenMailbox(c, o);
        SameDrop(c, o);
        ExecFromRemovable(c, o, letters);
        StartupFolder(c, o);
        PastedCommands(c, o);
    }

    // ---- helpers ----

    private static HashSet<string> RemovableLetterSet(Ctx c)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (c.Facts is { } f)
        {
            foreach (var l in f.RemovableLetters.Select(x => x.Trim().TrimEnd(':')).Where(x => x.Length == 1 && char.IsAsciiLetter(x[0]))) s.Add(l.ToUpperInvariant());
            foreach (var m in f.MediaListings.Where(m => m.Letter.Length == 1)) s.Add(m.Letter.ToUpperInvariant());
        }
        foreach (var m in ObservedUsb(c.Events)) if (m.Letter.Length > 0) s.Add(m.Letter.ToUpperInvariant());
        return s;
    }

    /// <summary>Drive letter of an absolute path ("E:\x" -> "E"); "" otherwise.</summary>
    private static string PathLetter(string path)
    {
        var p = path.Trim().Trim('"');
        return p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] is '\\' or '/' ? char.ToUpperInvariant(p[0]).ToString() : "";
    }

    private static string ExeName(string path) => WinPath.GetFileName(path.Trim().Trim('"').Replace('/', '\\')).ToLowerInvariant();

    private static bool IsLolbin(Ctx c, string path) => ExeName(path) is { Length: > 0 } n && c.Data.Payload.Lolbins.Contains(n, StringComparer.OrdinalIgnoreCase);

    private static bool HasExtension(IEnumerable<string> list, string path)
    {
        var ext = Path.GetExtension(WinPath.GetFileName(path));
        return ext.Length > 0 && list.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static string Clip(string s, int max = 200) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>True when <paramref name="text"/> names <paramref name="rel"/> as a path or a word (not as part of a longer name).</summary>
    private static bool Mentions(string text, string rel)
    {
        if (rel.Length == 0) return false;
        static bool Edge(char ch) => char.IsWhiteSpace(ch) || ch is '"' or '\'' or '\\' or '/' or ':' or ',' or '&' or '|' or '(' or ')' or ';' or '=';
        for (int i = text.IndexOf(rel, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(rel, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            bool before = i == 0 || Edge(text[i - 1]);
            int end = i + rel.Length;
            bool after = end == text.Length || Edge(text[end]);
            if (before && after) return true;
        }
        return false;
    }

    private static bool IsSystemMediaFolder(string rel) => SystemMediaFolders.Any(s => rel.Equals(s, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase));

    private static List<string> HiddenDirs(MediaListingFact m) =>
        m.Entries.Where(e => e.IsDirectory && e.Hidden && !IsSystemMediaFolder(e.RelativePath)).Select(e => e.RelativePath).ToList();

    private static bool UnderAny(string rel, IEnumerable<string> dirs) => dirs.Any(d => rel.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>A finding outside the air-gap channels (persistence, execution): same shape as <see cref="Chan"/>, without the air-gap detail.</summary>
    private static Finding Plain(Ctx c, string ruleId, string title, Severity sev, string category, DateTimeOffset? first, DateTimeOffset? last, string who, string description, string reason,
        string[] alternatives, IReadOnlyList<EvidenceRef> evidence, string mitre, SemanticType semantic, Confidence confidence = Confidence.Medium, Classification classification = Classification.Direct) => new()
    {
        FindingId = c.NextId(), RuleId = ruleId, Title = title, Severity = sev, Category = category, Classification = classification, Confidence = confidence, MitreTechniqueId = mitre,
        FirstSeenUtc = first, LastSeenUtc = last ?? first, User = who.StartsWith("necunoscut", StringComparison.Ordinal) ? "" : who,
        Description = description, ClassificationReason = reason, SemanticType = semantic,
        SupportingEvidence = [.. evidence.Take(20)], AlternativeExplanations = [.. alternatives],
    };

    // ---- MEDIA-LNK-PAYLOAD ----

    private static void MediaShortcuts(Ctx c, List<Finding> o, HashSet<string> letters)
    {
        var l = c.Data.Payload;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in c.Facts?.MediaListings ?? [])
        {
            var hidden = HiddenDirs(m);
            bool InHidden(MediaEntryFact x) => (x.Hidden && !IsSystemMediaFolder(x.RelativePath)) || UnderAny(x.RelativePath, hidden);
            foreach (var e in m.Entries.Where(e => !e.IsDirectory && e.LnkTarget.Length > 0))
            {
                var args = LnkText.Depad(e.LnkArguments, l.PaddingMinRun, l.PaddingRatio).Text;
                bool lol = IsLolbin(c, e.LnkTarget);
                // a shortcut to a runnable file kept in a hidden folder of the same medium
                var onSame = PathLetter(e.LnkTarget).Equals(m.Letter, StringComparison.OrdinalIgnoreCase) && e.LnkTarget.Length > 3 ? e.LnkTarget.Trim().Trim('"')[3..] : "";
                bool hiddenRunnable = onSame.Length > 0 && HasExtension(l.RunnableExtensions, onSame) && m.Entries.Any(x => x.RelativePath.Equals(onSame, StringComparison.OrdinalIgnoreCase) && InHidden(x));
                if (!lol && !hiddenRunnable) continue;
                seen.Add($"{m.Letter}:\\{e.RelativePath}");
                var named = m.Entries.Where(x => InHidden(x) && Mentions(args, x.RelativePath)).Select(x => x.RelativePath).OrderByDescending(x => x.Length).Take(5).ToList();
                if (hiddenRunnable && !named.Contains(onSame, StringComparer.OrdinalIgnoreCase)) named.Insert(0, onSame);
                var name = WinPath.GetFileName(e.RelativePath);
                var desc = $"Pe suportul {m.Identity} se află scurtătura „{e.RelativePath}”, care nu deschide un document, ci pornește {(lol ? $"programul de sistem {ExeName(e.LnkTarget)}" : "un program")} ({e.LnkTarget}{(args.Length > 0 ? " " + Clip(args) : "")}). " +
                           (named.Count > 0 ? $"Argumentele numesc elemente ascunse de pe același suport: {string.Join(", ", named.Select(x => "„" + x + "”"))} (într-un folder ascuns sau ascunse ele însele). " : "") +
                           "O scurtătură cu numele sau iconița unui folder ori a unui document, lângă un folder ascuns cu scripturi, este felul în care se răspândesc programele dăunătoare de pe stick-uri: utilizatorul crede că deschide fișierele lui. " +
                           "Lista arată că scurtătura există pe suport și ce ar porni; nu arată că a fost deschisă pe această stație (căutați Prefetch, 4688 sau EXEC-FROM-REMOVABLE).";
                o.Add(Chan(c, "MEDIA-LNK-PAYLOAD", $"Scurtătură pe suport amovibil care pornește un program: {name}", lol || named.Count > 0 ? Sev(c, Severity.High) : Sev(c, Severity.Medium),
                    "PORTABLE STORAGE", "Listarea suportului amovibil (scurtături decodate)", $"scurtătura „{e.RelativePath}” pornește {ExeName(e.LnkTarget)}", e.LastWriteUtc ?? e.CreatedUtc, null,
                    "necunoscut: listarea suportului nu conține contul", $"scurtătura „{e.RelativePath}”", "necunoscută: scurtătura poate fi adusă pe suport de altă stație", $"suportul {m.Identity}", desc,
                    "Scurtătură (.lnk) din listarea suportului amovibil, decodată fără a fi deschisă, a cărei țintă este un program de sistem din lista de date (interpretor sau program care rulează cod din alt fișier) ori un program dintr-un folder ascuns al aceluiași suport.",
                    ["Unele programe portabile sau instalatoare puse pe stick au o scurtătură către un interpretor de comenzi pentru o sarcină legitimă (de exemplu un script de copiere).",
                     "Scurtătura poate fi creată de administrator pentru o procedură internă și documentată.", "Suportul poate fi infectat pe altă stație; prezența pe suport nu arată că stația analizată a rulat ceva."],
                    [FactRef(c, $"media/{m.Letter}/{e.RelativePath}", $"scurtătură {e.RelativePath} → {e.LnkTarget}")], [$"airgap_facts media/{m.Letter}/{e.RelativePath}"], "T1091", SemanticType.Presence));
            }
        }
        if (letters.Count == 0) return;
        foreach (var e in c.Events.Where(e => e.Source == "LNK"))
        {
            var lnk = F(e, "LnkPath");
            var letter = PathLetter(lnk);
            if (letter.Length == 0 || !letters.Contains(letter)) continue;
            var target = e.Path.Length > 0 ? e.Path : F(e, "Target", "TargetPath");
            if (!IsLolbin(c, target) || !seen.Add(lnk.Trim())) continue;
            var args = LnkText.Depad(F(e, "Arguments"), c.Data.Payload.PaddingMinRun, c.Data.Payload.PaddingRatio).Text;
            var desc = $"Scurtătura „{lnk}” se afla pe unitatea {letter}:, care este amovibilă pe această stație, și pornește programul de sistem {ExeName(target)} ({target}{(args.Length > 0 ? " " + Clip(args) : "")}), nu un document. " +
                       "Așa se răspândesc programele dăunătoare de pe stick-uri: scurtătura imită un folder sau un document, iar programul de sistem rulează un fișier ascuns de pe suport. " +
                       "Fișierul .lnk arată ce ar porni scurtătura; nu arată că a fost deschisă.";
            o.Add(Chan(c, "MEDIA-LNK-PAYLOAD", $"Scurtătură pe suport amovibil care pornește un program: {WinPath.GetFileName(lnk)}", Sev(c, Severity.High), "PORTABLE STORAGE", "Fișier .lnk de pe unitatea amovibilă",
                $"scurtătura „{lnk}” pornește {ExeName(target)}", T(e), null, WhoOf([e]), $"scurtătura „{lnk}”", "necunoscută: scurtătura poate fi adusă pe suport de altă stație", $"unitatea amovibilă {letter}:", desc,
                "Fișier .lnk aflat pe litera unei unități amovibile, cu ținta un program de sistem din lista de date.",
                ["Scurtătura poate fi creată de administrator pentru o procedură internă și documentată.", "Litera poate fi reatribuită între timp unui alt volum.", "Suportul poate fi infectat pe altă stație."],
                [Ref(e, $"LNK {lnk} → {target}")], [$"{e.Source} {e.Locator}"], "T1091", SemanticType.Presence));
        }
    }

    // ---- LNK-ARGS-PADDED ----

    private static void PaddedShortcuts(Ctx c, List<Finding> o, HashSet<string> letters)
    {
        var l = c.Data.Payload;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Desc(string where, string target, DepaddedText d) =>
            $"Argumentele scurtăturii {where} încep cu {d.Leading} caractere invizibile (spații, sfârșituri de rând, caractere de lățime zero); în total au fost eliminate {d.Removed} caractere invizibile. " +
            $"Fără umplutură, scurtătura pornește: {target} {Clip(d.Text)}. Umplutura împinge comanda în afara câmpului vizibil din fereastra Proprietăți, ca utilizatorul să nu o vadă (tehnică din campaniile recente cu scurtături, CVE-2025-9491). " +
            "Textul original rămâne neschimbat în probă; scurtătura arată ce ar porni, nu că a fost deschisă.";
        string[] alts = ["Un program legitim care generează scurtături poate lăsa spații în plus în argumente (rar, de obicei câteva, nu zeci).", "Argumentele pot conține o listă lungă de opțiuni separate prin spații multiple."];
        const string Reason = "Câmpul de argumente al scurtăturii începe cu cel puțin pragul de caractere invizibile din fișierul de date, sau caracterele invizibile depășesc proporția din fișierul de date.";
        foreach (var m in c.Facts?.MediaListings ?? [])
            foreach (var e in m.Entries.Where(e => !e.IsDirectory && e.LnkArguments.Length > 0))
            {
                var d = LnkText.Depad(e.LnkArguments, l.PaddingMinRun, l.PaddingRatio);
                if (!d.Padded || !seen.Add($"{m.Letter}:\\{e.RelativePath}")) continue;
                o.Add(Chan(c, "LNK-ARGS-PADDED", $"Scurtătură cu comanda ascunsă prin umplutură: {WinPath.GetFileName(e.RelativePath)}", Sev(c, Severity.High), "PORTABLE STORAGE", "Listarea suportului amovibil (scurtături decodate)",
                    $"{d.Leading} caractere invizibile înaintea argumentelor", e.LastWriteUtc ?? e.CreatedUtc, null, "necunoscut: listarea suportului nu conține contul", $"scurtătura „{e.RelativePath}”", "necunoscută", $"suportul {m.Identity}",
                    Desc($"„{e.RelativePath}” de pe suportul {m.Identity}", e.LnkTarget, d), Reason, alts,
                    [FactRef(c, $"media/{m.Letter}/{e.RelativePath}", $"scurtătură {e.RelativePath}: {d.Removed} caractere invizibile")], [$"airgap_facts media/{m.Letter}/{e.RelativePath}"], "T1027", SemanticType.Observation));
            }
        foreach (var e in c.Events.Where(e => e.Source == "LNK"))
        {
            var d = LnkText.Depad(F(e, "Arguments"), l.PaddingMinRun, l.PaddingRatio);
            var lnk = F(e, "LnkPath");
            if (!d.Padded || (lnk.Length > 0 && !seen.Add(lnk.Trim()))) continue;
            var target = e.Path.Length > 0 ? e.Path : F(e, "Target", "TargetPath");
            var name = lnk.Length > 0 ? WinPath.GetFileName(lnk) : WinPath.GetFileName(target);
            var where = lnk.Length > 0 ? $"„{lnk}”" : $"către „{target}”";
            var title = $"Scurtătură cu comanda ascunsă prin umplutură: {name}";
            var refs = new[] { Ref(e, $"LNK {(lnk.Length > 0 ? lnk : target)}: {d.Removed} caractere invizibile") };
            var letter = PathLetter(lnk);
            if (letter.Length > 0 && letters.Contains(letter))
                o.Add(Chan(c, "LNK-ARGS-PADDED", title, Sev(c, Severity.High), "PORTABLE STORAGE", "Fișier .lnk de pe unitatea amovibilă", $"{d.Leading} caractere invizibile înaintea argumentelor", T(e), null, WhoOf([e]),
                    $"scurtătura {where}", "necunoscută", $"unitatea amovibilă {letter}:", Desc(where, target, d), Reason, alts, refs, [$"{e.Source} {e.Locator}"], "T1027", SemanticType.Observation));
            else
                o.Add(Plain(c, "LNK-ARGS-PADDED", title, Sev(c, Severity.High), "Defense Evasion", T(e), null, WhoOf([e]), Desc(where, target, d), Reason, alts, refs, "T1027", SemanticType.Observation));
        }
    }

    // ---- MEDIA-HIDDEN-MAILBOX ----

    private static void HiddenMailbox(Ctx c, List<Finding> o)
    {
        var f = c.Facts;
        if (f is null) return;
        var l = c.Data.Payload;
        foreach (var m in f.MediaListings)
        {
            var hidden = HiddenDirs(m);
            var now = f.CollectedUtc != default ? f.CollectedUtc : m.Entries.Select(e => e.LastWriteUtc).Where(t => t is not null).Select(t => t!.Value).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
            // report the outermost hidden folders only (a hidden folder inside another one is part of the same finding)
            foreach (var dir in hidden.Where(d => !UnderAny(d, hidden)))
            {
                var files = m.Entries.Where(e => !e.IsDirectory && UnderAny(e.RelativePath, [dir])).ToList();
                var recent = files.Where(e => HasExtension(l.DataExtensions, e.RelativePath) && (e.LastWriteUtc ?? e.CreatedUtc) is { } t && t >= now.AddDays(-l.MailboxRecentDays) && t <= now.AddDays(1)).ToList();
                if (recent.Count == 0) continue;
                var runnable = files.Where(e => HasExtension(l.RunnableExtensions, e.RelativePath)).ToList();
                var times = recent.Select(e => (e.LastWriteUtc ?? e.CreatedUtc)!.Value).OrderBy(t => t).ToList();
                var size = files.Sum(e => e.Size);
                var desc = $"Pe suportul {m.Identity} există folderul ascuns „{dir}”, care conține {files.Count} fișiere ({size.ToString("N0", CultureInfo.InvariantCulture)} octeți), dintre care {recent.Count} fișiere de date (documente, arhive, imagini) modificate în ultimele {l.MailboxRecentDays} zile " +
                           $"({Time(times[0])} – {Time(times[^1])}): {string.Join(", ", recent.Take(6).Select(e => WinPath.GetFileName(e.RelativePath)))}{(recent.Count > 6 ? " …" : "")}" +
                           (runnable.Count > 0 ? $"; în același folder sunt și {runnable.Count} fișiere care rulează la deschidere ({string.Join(", ", runnable.Take(3).Select(e => WinPath.GetFileName(e.RelativePath)))})" : "") + ". " +
                           "Programele dăunătoare care trec granița unei rețele izolate pe stick-uri folosesc un folder ascuns de pe suport ca „cutie poștală”: copiază acolo documente de pe stația izolată și le scot când stick-ul ajunge pe o stație conectată. " +
                           "Listarea arată ce este în folder și când s-a modificat; nu arată ce stație a scris fișierele și nici ce conțin.";
                o.Add(Chan(c, "MEDIA-HIDDEN-MAILBOX", $"Folder ascuns cu {recent.Count} fișiere de date recente pe suportul {m.Identity}: {dir}", recent.Count >= l.MailboxHighCount ? Sev(c, Severity.High) : Sev(c, Severity.Medium),
                    "PORTABLE STORAGE", "Listarea suportului amovibil", $"{recent.Count} fișiere de date recente din {files.Count} în folderul ascuns „{dir}”", times[0], times[^1], "necunoscut: listarea suportului nu conține contul",
                    $"folderul ascuns „{dir}”", "posibilă scriere pe mediu (fișierele pot fi scrise pe această stație sau pe alta)", $"suportul {m.Identity}", desc,
                    "Folder cu atributul ascuns (altul decât folderele proprii ale sistemului) pe un suport amovibil, cu fișiere de date modificate în intervalul din fișierul de date înaintea colectării.",
                    ["Unele programe de sincronizare sau de copii de siguranță își țin datele într-un folder ascuns de pe stick.", "Utilizatorul poate ascunde un folder cu documente personale ca să nu fie văzut de alții.", "Datele fișierelor pot fi păstrate de la copierea de pe altă stație."],
                    [FactRef(c, $"media/{m.Letter}/{dir}", $"folder ascuns {dir}: {files.Count} fișiere, {recent.Count} recente"), .. recent.Take(5).Select(e => FactRef(c, $"media/{m.Letter}/{e.RelativePath}", $"fișier {e.RelativePath}"))],
                    [$"airgap_facts media/{m.Letter}/{dir}"], "T1074", SemanticType.Presence));
            }
        }
    }

    // ---- MEDIA-SAME-DROP ----

    private sealed record DropItem(string Location, bool OnMedia, string Display, string Name, long Size, string Sha, DateTimeOffset? Time, string Locator);

    private static void SameDrop(Ctx c, List<Finding> o)
    {
        var f = c.Facts;
        if (f is null) return;
        var min = c.Data.Payload.SameDropMinBytes;
        var items = new List<DropItem>();
        foreach (var m in f.MediaListings)
            foreach (var e in m.Entries.Where(e => !e.IsDirectory && e.Size >= min))
                items.Add(new($"suportul {m.Identity}", true, $"{m.Letter}:\\{e.RelativePath}", WinPath.GetFileName(e.RelativePath), e.Size, e.Sha256.ToUpperInvariant(), e.LastWriteUtc ?? e.CreatedUtc, $"media/{m.Letter}/{e.RelativePath}"));
        if (items.Count == 0) return;
        foreach (var p in f.ProfileFiles.Where(p => p.Size >= min))
            items.Add(new($"profilul {p.Profile}, folderul {p.Folder}", false, $"{p.Folder}\\{p.Name}", p.Name, p.Size, p.Sha256.ToUpperInvariant(), p.LastWriteUtc, $"profile/{p.Profile}/{p.Folder}/{p.Name}"));
        // union of items that share a name and size, or a SHA-256
        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var firstByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
            foreach (var key in new[] { $"n|{items[i].Name}|{items[i].Size}", items[i].Sha.Length > 0 ? "h|" + items[i].Sha : "" }.Where(k => k.Length > 0))
                if (firstByKey.TryGetValue(key, out var j)) parent[Find(i)] = Find(j); else firstByKey[key] = i;
        int reported = 0;
        foreach (var g in Enumerable.Range(0, items.Count).GroupBy(Find).Select(g => g.Select(i => items[i]).ToList()))
        {
            var locations = g.Select(x => x.Location).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (locations.Count < 2 || !g.Any(x => x.OnMedia)) continue;
            if (++reported > 20) break;
            var mediaLocs = g.Where(x => x.OnMedia).Select(x => x.Location).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            bool byHash = g.Where(x => x.Sha.Length > 0).GroupBy(x => x.Sha).Any(h => h.Select(x => x.Location).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
            var name = g[0].Name;
            var profile = g.FirstOrDefault(x => !x.OnMedia);
            var title = mediaLocs >= 2 ? $"Același fișier pe mai multe suporturi amovibile: {name}" : $"Același fișier pe un suport amovibil și în {profile!.Location}: {name}";
            var times = g.Select(x => x.Time).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
            var desc = $"Fișierul „{name}” ({g[0].Size.ToString("N0", CultureInfo.InvariantCulture)} octeți) apare în {locations.Count} locuri: {string.Join("; ", g.Take(8).Select(x => $"{x.Location} ({x.Display})"))}{(g.Count > 8 ? " …" : "")}. " +
                       $"Potrivire: {(byHash ? "același SHA-256 (același conținut, chiar dacă numele diferă)" : "același nume și aceeași dimensiune (fără hash comun, deci conținutul nu este confirmat)")}. " +
                       "Același fișier copiat pe mai multe suporturi sau adus de pe un suport în profilul utilizatorului este felul în care un program dăunător de pe stick-uri se răspândește sau în care un fișier trece dintr-o zonă în alta. " +
                       "Listarea nu arată cine a copiat fișierul, în ce ordine și pe ce stație.";
            o.Add(Chan(c, "MEDIA-SAME-DROP", title, Sev(c, Severity.Medium), "PORTABLE STORAGE", "Listarea suporturilor amovibile și a folderelor Downloads / Desktop",
                $"„{name}” în {locations.Count} locuri", times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null, "necunoscut: listarea nu conține contul", $"fișierul „{name}”",
                "necunoscută: listarea nu arată ordinea copierilor", string.Join(", ", locations), desc,
                "Fișiere cu același SHA-256, sau cu același nume și aceeași dimensiune (peste pragul din fișierul de date), pe suporturi amovibile diferite sau pe un suport și în Downloads / Desktop.",
                ["Același document distribuit oficial pe mai multe suporturi (de exemplu un kit de instalare aprobat) apare la fel.", "Utilizatorul își poate ține o copie de siguranță a aceluiași fișier pe două stick-uri.", "Fișiere generice (instalatoare comune) pot avea același nume și aceeași dimensiune fără a fi aceeași copie."],
                [.. g.Take(10).Select(x => FactRef(c, x.Locator, $"{x.Location}: {x.Display}"))], g.Take(5).Select(x => $"airgap_facts {x.Locator}").ToList(), "T1091", SemanticType.Presence));
        }
    }

    // ---- EXEC-FROM-REMOVABLE ----

    private static void ExecFromRemovable(Ctx c, List<Finding> o, HashSet<string> letters)
    {
        var serials = new HashSet<string>((c.Facts?.MediaListings ?? []).Select(m => m.VolumeSerial.Replace("-", "").Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
        if (letters.Count == 0 && serials.Count == 0) return;
        bool OnRemovable(string path) =>
            (PathLetter(path) is { Length: > 0 } x && letters.Contains(x)) || (VolumeRx.Match(path) is { Success: true } v && serials.Contains(v.Groups["s"].Value));
        var hits = new List<(TimelineEvent E, string Path, bool Exec, string User)>();
        foreach (var p in ProcessStarts(c.Events))
            if (OnRemovable(p.Image)) hits.Add((p.Event, p.Image, true, p.User));
        foreach (var e in c.Events.Where(e => !e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && e.Path.Length > 0))
        {
            var sem = AntiOverclaim.SemanticForArtifact(e.Source);
            if (sem == SemanticType.Execution && OnRemovable(e.Path)) hits.Add((e, e.Path, true, e.User));
            else if (e.Source is "Amcache" or "ShimCache" && OnRemovable(e.Path)) hits.Add((e, e.Path, false, e.User));
        }
        foreach (var g in hits.GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase))
        {
            var list = g.OrderBy(h => T(h.E)).ToList();
            bool exec = list.Any(h => h.Exec);
            var path = g.Key;
            var sources = list.Select(h => h.E.EventId.Length > 0 && h.E.Source.StartsWith("EventLog:", StringComparison.Ordinal) ? $"{h.E.Source[9..]} {h.E.EventId}" : h.E.Source).Distinct().ToList();
            var users = list.Select(h => h.User).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var times = list.Select(h => T(h.E)).Where(t => t is not null).Select(t => t!.Value).ToList();
            var desc = exec
                ? $"Programul „{path}” a rulat de pe un suport amovibil (literă sau volum al unui suport amovibil pe această stație); surse: {string.Join(", ", sources)}; {(times.Count > 0 ? $"{Time(times[0])} – {Time(times[^1])}" : "oră necunoscută")}. " +
                  "Un program pornit direct de pe stick ocolește instalarea controlată și lista de programe aprobate; este și pasul prin care programele dăunătoare de pe stick-uri ajung să ruleze. Înregistrarea arată execuția, nu cine a adus programul sau ce a făcut."
                : $"Programul „{path}” de pe un suport amovibil apare în {string.Join(", ", sources)}, fără o urmă de execuție (Prefetch, 4688, Sysmon 1) în probele colectate. Amcache / ShimCache arată că fișierul a fost văzut de sistem, nu că a rulat.";
            o.Add(Chan(c, "EXEC-FROM-REMOVABLE", exec ? $"Program rulat de pe un suport amovibil: {WinPath.GetFileName(path)}" : $"Program de pe un suport amovibil văzut de sistem (fără urmă de execuție): {WinPath.GetFileName(path)}",
                exec ? Sev(c, Severity.High) : Sev(c, Severity.Medium), "PORTABLE STORAGE", string.Join(" / ", sources), $"„{path}”", times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null,
                users.Count > 0 ? string.Join(", ", users) : UnknownWho, $"programul „{path}”", "citire de pe mediu (program rulat sau văzut de sistem)", "stația analizată", desc,
                exec ? "Eveniment de pornire de proces (Security 4688 / Sysmon 1) sau artefact de execuție (Prefetch, BAM, UserAssist) cu calea pe litera sau volumul unui suport amovibil." : "Intrare Amcache / ShimCache cu calea pe litera unui suport amovibil, fără artefact de execuție pentru aceeași cale.",
                ["Programul poate fi un utilitar portabil aprobat prin procedură (de exemplu un instrument de diagnostic).", "Litera poate fi reatribuită între timp unui alt volum; volumul din Prefetch identifică volumul, nu dispozitivul.", "Un instalator lansat de pe stick de un administrator produce aceleași urme."],
                [.. list.Take(10).Select(h => Ref(h.E, $"{h.E.Source} {path}"))], list.Take(5).Select(h => $"{h.E.Source} {h.E.EventId} {h.E.Locator}".Trim()).ToList(), "T1091",
                exec ? SemanticType.Execution : SemanticType.Presence, exec ? Confidence.High : Confidence.Medium));
        }
    }

    // ---- PERSIST-STARTUP-FOLDER ----

    private static bool UserWritable(string text) =>
        new[] { @"\Users\", @"\AppData\", @"\Temp\", @"\ProgramData\", @"\Public\", @"\Downloads\" }.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static bool InStartupFolder(string path) => path.Contains(@"\Start Menu\Programs\Startup\", StringComparison.OrdinalIgnoreCase);

    private static void StartupFolder(Ctx c, List<Finding> o)
    {
        var l = c.Data.Payload;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Report(string scope, string profile, string path, string target, string args, DateTimeOffset? created, bool? signed, DateTimeOffset? now, IReadOnlyList<EvidenceRef> refs, string who)
        {
            var name = WinPath.GetFileName(path);
            var reasons = new List<string>();
            int score = 0;
            if (created is { } t && now is { } n && t >= n.AddDays(-l.StartupNewDays)) { score++; reasons.Add($"intrarea este nouă (creată {Time(t)}, în ultimele {l.StartupNewDays} zile înaintea colectării)"); }
            if (IsLolbin(c, target)) { score += 2; reasons.Add($"pornește programul de sistem {ExeName(target)}, care rulează cod din alt fișier"); }
            if (UserWritable(target + " " + args)) { score++; reasons.Add("ținta sau argumentele sunt într-un folder în care utilizatorul poate scrie (profil, Temp, ProgramData)"); }
            if (signed == false) { score++; reasons.Add("ținta nu are semnătură digitală"); }
            if (!name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && HasExtension(l.RunnableExtensions, name)) { score++; reasons.Add("intrarea este un script pus direct în folderul Startup"); }
            var sev = score >= 3 ? Severity.High : score == 2 ? Severity.Medium : Severity.Low;
            var shownArgs = LnkText.Depad(args, l.PaddingMinRun, l.PaddingRatio).Text;
            var desc = $"În folderul Startup {(scope == "comun" ? "comun (pentru toți utilizatorii)" : $"al profilului „{(profile.Length > 0 ? profile : "necunoscut")}”")} se află „{name}” ({path}), care pornește la fiecare autentificare: {target}{(shownArgs.Length > 0 ? " " + Clip(shownArgs) : "")}" +
                       (signed is null ? "; semnătura țintei nu a fost verificată" : signed == true ? "; ținta are semnătură digitală" : "") + ". " +
                       (reasons.Count > 0 ? "De reținut: " + string.Join("; ", reasons) + ". " : "Nimic din intrare nu iese din comun (veche, semnată, într-un folder de programe). ") +
                       "Folderul Startup este un loc simplu de persistență, folosit și de programele dăunătoare aduse pe stick-uri; intrarea arată ce ar porni, nu că a pornit sau ce a făcut.";
            o.Add(Plain(c, "PERSIST-STARTUP-FOLDER", $"Intrare în folderul Startup: {name}", Sev(c, sev), "Persistence", created, null, who, desc,
                "Fișier din folderul Startup al unui profil sau din cel comun (altul decât fișierele scrise de Windows), cu ținta decodată, cântărit după vechime, semnătură, locul țintei și programul pornit.",
                ["Multe programe legitime (sincronizare, utilitare ale producătorului) își pun o scurtătură în Startup la instalare.", "Utilizatorul își poate pune singur o scurtătură către un program pe care îl folosește zilnic.", "Data creării poate fi refăcută de o copiere a profilului."],
                refs, "T1547.001", SemanticType.Presence));
        }
        if (c.Facts is { } f)
            foreach (var s in f.StartupEntries)
            {
                var name = WinPath.GetFileName(s.Path);
                if (name.Length == 0 || l.StartupBenignNames.Contains(name, StringComparer.OrdinalIgnoreCase) || !seen.Add(s.Path)) continue;
                var target = s.LnkTarget.Length > 0 ? s.LnkTarget : s.Path;
                Report(s.Scope, s.Profile, s.Path, target, s.LnkArguments, s.CreatedUtc ?? s.LastWriteUtc, s.TargetSigned, f.CollectedUtc == default ? null : f.CollectedUtc,
                    [FactRef(c, $"startup/{s.Scope}/{name}", $"Startup {name} → {target}")], "necunoscut: folderul nu conține contul");
            }
        foreach (var e in c.Events.Where(e => e.Source == "LNK"))
        {
            var lnk = F(e, "LnkPath");
            if (!InStartupFolder(lnk) || !seen.Add(lnk)) continue;
            var name = WinPath.GetFileName(lnk);
            if (l.StartupBenignNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var target = e.Path.Length > 0 ? e.Path : F(e, "Target", "TargetPath");
            bool common = lnk.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase);
            var profile = !common && lnk.IndexOf(@"\Users\", StringComparison.OrdinalIgnoreCase) is var i and >= 0 ? lnk[(i + 7)..].Split('\\')[0] : "";
            Report(common ? "comun" : "utilizator", profile, lnk, target, F(e, "Arguments"), null, null, null, [Ref(e, $"LNK {lnk} → {target}")], WhoOf([e]));
        }
    }

    // ---- CLICKFIX-RUNMRU ----

    private static List<(Regex Rx, WeightedPattern P)> ClickFixRegexes(PayloadLists l)
    {
        var list = new List<(Regex, WeightedPattern)>();
        foreach (var p in l.ClickFixPatterns.Where(p => p.Regex.Length > 0))
            try { list.Add((RulePatterns.Compile(p.Regex), p)); }
            catch (ArgumentException) { }   // an invalid pattern in a data pack is skipped, not fatal
        return list;
    }

    private static void PastedCommands(Ctx c, List<Finding> o)
    {
        var l = c.Data.Payload;
        var rows = c.Events.Where(e => e.Source is "RunMRU" or "TypedPaths" && F(e, "Command").Length > 0).ToList();
        if (rows.Count == 0) return;
        var patterns = ClickFixRegexes(l);
        var notEvaluated = new List<TimelineEvent>();
        foreach (var e in rows)
        {
            var cmd = F(e, "Command");
            var results = patterns.Select(p => (p.P, M: RulePatterns.Match(p.Rx, cmd))).ToList();
            var hit = results.Where(r => r.M == true).Select(r => r.P).ToList();
            if (hit.Sum(p => p.Weight) < l.ClickFixMinWeight)
            {
                // a timed-out pattern could have added the missing weight: the value is "neevaluat", not clean
                if (results.Any(r => r.M is null)) notEvaluated.Add(e);
                continue;
            }
            var consequences = new List<TimelineEvent>();
            if (T(e) is { } t0)
            {
                bool InWindow(TimelineEvent x) => T(x) is { } t && t >= t0 && t <= t0.AddMinutes(l.ClickFixMinutes);
                consequences.AddRange(c.Events.Where(x => InChannel(x, "PowerShell/Operational", 4104) && InWindow(x)).Take(5));
                consequences.AddRange(ProcessStarts(c.Events).Where(p => IsLolbin(c, p.Image) && InWindow(p.Event)).Select(p => p.Event).Take(5));
            }
            bool runDialog = e.Source == "RunMRU";
            var account = F(e, "Account");
            var shown = Clip(LnkText.Depad(cmd, l.PaddingMinRun, l.PaddingRatio).Text, 240);
            var labels = string.Join("; ", hit.Select(p => p.Label).Where(x => x.Length > 0).Distinct());
            var where = runDialog ? $"în fereastra Executare (Win+R) a profilului „{(account.Length > 0 ? account : "necunoscut")}”" : $"în bara de adrese a Explorer-ului (TypedPaths) a profilului „{(account.Length > 0 ? account : "necunoscut")}”";
            var desc = $"A fost tastat sau lipit {where} textul: „{shown}”. Semne: {labels}. " +
                       "Așa arată o momeală de tip ClickFix (o pagină îi cere utilizatorului să apese Win+R și să lipească o „comandă de verificare”) sau FileFix (aceeași comandă lipită în bara de adrese a Explorer-ului, ascunsă după spații și un comentariu care arată ca o cale de fișier). " +
                       (consequences.Count > 0
                           ? $"În cele {l.ClickFixMinutes} minute de după intrare există {consequences.Count} urme de rulare ({string.Join(", ", consequences.Select(x => $"{x.Source[(x.Source.IndexOf(':') + 1)..]} {x.EventId} la {Time(T(x))}".Trim()).Distinct().Take(4))}); legătura este în timp, nu dovedită. "
                           : T(e) is null ? "Ora intrării nu este cunoscută (doar intrarea cea mai nouă are ora cheii din registru), deci nu se poate căuta ce a urmat. " : $"În cele {l.ClickFixMinutes} minute de după intrare nu s-a găsit în probe o rulare de PowerShell sau a unui program de sistem. ") +
                       "Intrarea arată textul tastat, nu că s-a apăsat Enter sau ce a descărcat comanda.";
            var refs = new List<EvidenceRef> { Ref(e, $"{e.Source} {F(e, "ValueName")}") };
            refs.AddRange(consequences.Select(x => Ref(x, $"{x.Source} {x.EventId} după intrarea {e.Source}")));
            bool corr = consequences.Count > 0;
            o.Add(Plain(c, "CLICKFIX-RUNMRU", runDialog ? "Comandă suspectă lipită în fereastra Executare (Win+R)" : "Comandă suspectă lipită în bara de adrese a Explorer-ului", corr ? Sev(c, Severity.High) : Sev(c, Severity.Medium), "Execution",
                T(e), consequences.Count > 0 ? consequences.Max(x => T(x)) : null, account.Length > 0 ? account : "necunoscut", desc,
                (runDialog ? "Intrare RunMRU" : "Intrare TypedPaths") + " al cărei text adună cel puțin ponderea minimă a tiparelor din fișierul de date" + (corr ? ", urmată în fereastra din fișierul de date de un bloc de script PowerShell (4104) sau de pornirea unui program de sistem." : "."),
                ["Un administrator poate rula din Win+R o comandă PowerShell legitimă pentru o sarcină de întreținere.", "Comanda poate fi copiată dintr-o documentație internă de încredere.", "Textul poate fi lipit și apoi abandonat fără să fie rulat."],
                refs, "T1204.004", corr ? SemanticType.Correlation : SemanticType.Observation, Confidence.Medium, corr ? Classification.Correlated : Classification.Direct));
        }
        if (notEvaluated.Count > 0) o.Add(RulePatterns.NotEvaluated(c.NextId(), "CLICKFIX-RUNMRU", "Execution", notEvaluated));
    }
}
