using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>How a path relates to the removable media known in the case.</summary>
public enum DriveMatch
{
    /// <summary>Not on a drive letter, or on C: (assumed to be the system volume), or the letter is unknown.</summary>
    None,
    /// <summary>The letter was seen with a removable medium in the case's device facts. A letter can be reused by another volume later; the facts do not date the assignment of every event.</summary>
    Removable,
    /// <summary>Another drive letter that no device fact explains: it may be a removable medium, a second internal disk, an optical drive or a mapped network drive. Never stronger than "possible".</summary>
    Possible,
}

/// <summary>One row of the user-activity view: an event of ShellBags, RecentDocs, Office MRU or the Recycle Bin ($I) with what it means and how it relates to the classified folders and removable volumes.</summary>
public sealed class UserActivityItem
{
    public required TimelineEvent Origin { get; init; }
    public required string Source { get; init; }
    public required string SourceLabel { get; init; }
    public required string Path { get; init; }
    public DateTimeOffset? TimeUtc { get; init; }
    public required string TimeText { get; init; }
    /// <summary>What the time means for THIS source (printed next to the time everywhere): never "access time".</summary>
    public required string TimeMeaning { get; init; }
    public required string Who { get; init; }
    public required string Detail { get; init; }
    /// <summary>The classified folder the path is in or equal to ("" = none).</summary>
    public string ClassifiedRoot { get; init; } = "";
    public DriveMatch Drive { get; init; }
    /// <summary>"E:" when the path is on a drive letter, else "".</summary>
    public string DriveLetter { get; init; } = "";
    /// <summary>Plain words for the "Relevanță" column.</summary>
    public required string Relevance { get; init; }
    /// <summary>Recycle Bin only: the $R state (present, present_folder, absent, not_checked); "" for the other sources.</summary>
    public string RContent { get; init; } = "";

    public bool IsDeletion => Source == UserActivityAnalysis.RecycleBin;
    public bool IsRelevant => ClassifiedRoot.Length > 0 || Drive != DriveMatch.None;
}

/// <summary>The result of <see cref="UserActivityAnalysis.Analyze"/>: every user-activity row of the timeline, the relevant ones first.</summary>
public sealed class UserActivityReport
{
    public IReadOnlyList<UserActivityItem> Items { get; init; } = [];
    public IReadOnlyList<UserActivityItem> Classified => [.. Items.Where(i => !i.IsDeletion && i.ClassifiedRoot.Length > 0)];
    public IReadOnlyList<UserActivityItem> Removable => [.. Items.Where(i => !i.IsDeletion && i.Drive == DriveMatch.Removable)];
    public IReadOnlyList<UserActivityItem> PossibleRemovable => [.. Items.Where(i => !i.IsDeletion && i.Drive == DriveMatch.Possible)];
    /// <summary>Recycle Bin records whose original path was in a classified folder or on a (possibly) removable volume.</summary>
    public IReadOnlyList<UserActivityItem> Deleted => [.. Items.Where(i => i.IsDeletion && i.IsRelevant)];
    public int Total => Items.Count;
    public int Count(string source) => Items.Count(i => i.Source == source);
    public bool HasFolders { get; init; }

    /// <summary>One line for the page header and the reports.</summary>
    public string Summary =>
        Total == 0
            ? "Activitatea utilizatorului: nicio urmă ShellBags, RecentDocs, Office MRU sau Coș de reciclare ($I) în sursele citite; asta nu dovedește că nu s-a întâmplat nimic (hive-urile utilizatorilor sau Coșul pot să nu fi fost colectate: profilul „Complet”)."
            : $"Activitatea utilizatorului: {Total:N0} urme (ShellBags {Count(UserActivityAnalysis.ShellBags):N0}, RecentDocs {Count(UserActivityAnalysis.RecentDocs):N0}, Office MRU {Count(UserActivityAnalysis.OfficeMru):N0}, Coș de reciclare {Count(UserActivityAnalysis.RecycleBin):N0}) · " +
              (HasFolders ? $"în foldere clasificate: {Classified.Count:N0} · " : "foldere clasificate: nedefinite în profil · ") +
              $"pe volume amovibile (confirmate de istoricul dispozitivelor): {Removable.Count:N0} · posibil amovibile: {PossibleRemovable.Count:N0} · șterse din foldere clasificate sau de pe volume amovibile: {Deleted.Count:N0}.";
}

/// <summary>
/// E7 slice 3: what the user-activity artifacts say about classified folders and removable volumes. Pure function of the timeline (no I/O); the rules, the
/// investigation page and the reports all read the same result.
/// Evidence semantics (binding): a ShellBags time is the last update of the folder's entry by Explorer, not the first opening and not proof that a file was
/// read; RecentDocs gives a time only to the newest entry of a key; an Office MRU time is the last open by that Office application; a Recycle Bin time is the
/// deletion time and a missing $R means emptied or removed content, not "never existed". None of these is, alone, a finding of a breach: the severity of the
/// rules is DE VERIFICAT (candidate) and the owner decides later whether any of them is stronger.
/// </summary>
public static class UserActivityAnalysis
{
    public const string ShellBags = "ShellBags", RecentDocs = "RecentDocs", OfficeMru = "OfficeMRU", RecycleBin = "RecycleBin";

    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal) { ShellBags, RecentDocs, OfficeMru, RecycleBin };

    /// <summary>The meaning of each source's time, printed beside the time. <paramref name="timeKnown"/> false = the row has no time of its own.</summary>
    public static string TimeMeaningFor(string source, bool timeKnown) => source switch
    {
        ShellBags when timeKnown => "ultima actualizare a intrării folderului de către Explorer (ora cheii BagMRU); nu este prima deschidere și nu dovedește că un fișier a fost citit",
        ShellBags => "fără oră proprie: doar cea mai nouă intrare din cheia BagMRU primește ora cheii",
        RecentDocs when timeKnown => "ora ultimei scrieri a cheii RecentDocs: se aplică doar celei mai noi intrări; nu este ora deschiderii fiecărui document",
        RecentDocs => "fără oră proprie: doar cea mai nouă intrare din cheia RecentDocs primește ora cheii",
        OfficeMru when timeKnown => "ultima deschidere de către aplicația Office (câmpul [T], UTC); nu dovedește ce s-a făcut cu documentul",
        OfficeMru => "ora [T] lipsește sau nu este validă",
        RecycleBin when timeKnown => "ora ștergerii, scrisă când elementul a fost mutat în Coșul de reciclare ($I, UTC)",
        RecycleBin => "ora ștergerii este necunoscută în metadate",
        _ => "",
    };

    public static string SourceLabel(string source) => source switch
    {
        ShellBags => "ShellBags (folder văzut în Explorer)",
        RecentDocs => "RecentDocs (document recent)",
        OfficeMru => "Office MRU (fișier deschis în Office)",
        RecycleBin => "Coș de reciclare ($I)",
        _ => source,
    };

    /// <summary>The source's short name, for counts.</summary>
    public static string ShortLabel(string source) => source switch { OfficeMru => "Office MRU", RecycleBin => "Coș de reciclare", _ => source };

    /// <summary>The $R state in words; the absent case says what it does and does not mean.</summary>
    public static string RContentText(string state) => state switch
    {
        "present" => "conținutul ($R) există în Coș",
        "present_folder" => "folderul șters ($R) există în Coș",
        "absent" => "conținut golit sau eliminat (fișierul $R lipsește; nu înseamnă că elementul nu a existat)",
        _ => "starea fișierului $R nu a fost verificată",
    };

    /// <summary>"E:" (upper case) for a path on a drive letter, else "".</summary>
    public static string DriveOf(string normalizedPath) => ClassifiedAccess.Drive(normalizedPath);

    /// <summary>
    /// Builds the view over the timeline. <paramref name="folders"/> are the classified folders of the procedure profile (with aliases); <paramref name="removableLetters"/> the
    /// drive letters ("E" or "E:") that the case's device facts tie to removable media; any other letter than C: is "possible" with the uncertainty stated.
    /// </summary>
    public static UserActivityReport Analyze(IReadOnlyList<TimelineEvent> events, IEnumerable<ClassifiedRoot>? folders = null, IEnumerable<string>? removableLetters = null,
        IReadOnlyDictionary<string, string>? volumeMap = null)
    {
        var roots = new List<(string Root, string Canonical)>();
        foreach (var f in folders ?? [])
        {
            var canonical = ClassifiedAccess.Normalize(f.Path, volumeMap);
            if (canonical.Length == 0) continue;
            roots.Add((canonical, canonical));
            foreach (var a in f.Aliases ?? [])
                if (ClassifiedAccess.Normalize(a, volumeMap) is { Length: > 0 } alias) roots.Add((alias, canonical));
        }
        var removable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in removableLetters ?? [])
        {
            var t = (l ?? "").Trim().TrimEnd(':');
            if (t.Length == 1 && char.IsAsciiLetter(t[0])) removable.Add(char.ToUpperInvariant(t[0]) + ":");
        }

        var items = new List<UserActivityItem>();
        foreach (var e in events)
        {
            if (!Sources.Contains(e.Source)) continue;
            items.Add(Build(e, roots, removable, volumeMap));
        }
        // the relevant rows first (classified, then removable, then possible), newest first; undated rows last
        var ordered = items
            .OrderBy(i => i.ClassifiedRoot.Length > 0 ? 0 : i.Drive == DriveMatch.Removable ? 1 : i.Drive == DriveMatch.Possible ? 2 : 3)
            .ThenByDescending(i => i.TimeUtc ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new UserActivityReport { Items = ordered, HasFolders = roots.Count > 0 };
    }

    private static string F(TimelineEvent e, string key) => e.Fields.TryGetValue(key, out var v) ? v : "";

    private static UserActivityItem Build(TimelineEvent e, List<(string Root, string Canonical)> roots, HashSet<string> removable, IReadOnlyDictionary<string, string>? volumeMap)
    {
        var path = e.Source == RecycleBin && F(e, "OriginalPath").Length > 0 ? F(e, "OriginalPath") : e.Path;
        var normalized = ClassifiedAccess.Normalize(path, volumeMap);
        var root = "";
        foreach (var (r, canonical) in roots)
            if (ClassifiedAccess.IsUnder(normalized, r)) { root = canonical; break; }
        var letter = DriveOf(normalized);
        // a path in a classified folder of the profile is on the volume the profile declares for it: its letter is "possible" only if nothing says so, so it is not guessed
        var drive = letter.Length == 0 || letter == "C:" ? DriveMatch.None : removable.Contains(letter) ? DriveMatch.Removable : root.Length > 0 ? DriveMatch.None : DriveMatch.Possible;
        var known = e.Time.Utc is not null;
        var time = e.Time.Utc;
        var who = e.Source == RecycleBin
            ? (F(e, "OwnerSid").Length > 0 ? "SID " + F(e, "OwnerSid") : e.User)
            : F(e, "Account") is { Length: > 0 } acc ? acc : e.User;
        var rcontent = e.Source == RecycleBin ? F(e, "RContent") : "";
        var relevance = root.Length > 0 ? $"Folder clasificat: {root}" : "";
        if (drive == DriveMatch.Removable)
            relevance += (relevance.Length > 0 ? "; " : "") + $"Volum amovibil {letter} (litera apare în istoricul dispozitivelor din caz)";
        else if (drive == DriveMatch.Possible)
            relevance += (relevance.Length > 0 ? "; " : "") + $"Posibil volum amovibil {letter} (nicio dovadă că litera a fost un suport amovibil; poate fi disc intern, unitate optică sau de rețea)";
        if (relevance.Length == 0) relevance = "—";
        return new UserActivityItem
        {
            Origin = e, Source = e.Source, SourceLabel = SourceLabel(e.Source), Path = path, TimeUtc = time,
            TimeText = time is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : "fără oră",
            TimeMeaning = TimeMeaningFor(e.Source, known), Who = who.Length > 0 ? who : "necunoscut în sursă", Detail = DetailOf(e, rcontent),
            ClassifiedRoot = root, Drive = drive, DriveLetter = letter, Relevance = relevance, RContent = rcontent,
        };
    }

    private static string DetailOf(TimelineEvent e, string rcontent)
    {
        var parts = new List<string>();
        switch (e.Source)
        {
            case ShellBags:
                parts.Add($"tip element {F(e, "ShellItemType")} ({F(e, "ShellItemKind")})");
                if (F(e, "MruPosition") is { Length: > 0 } p && int.TryParse(p, out var pos)) parts.Add($"poziția {pos + 1} în lista cheii");
                if (F(e, "ShellModifiedLocal") is { Length: > 0 } m) parts.Add($"ora proprie a folderului (modificat, ora locală fără fus, rezoluție 2 s): {m}; nu este ora accesului");
                break;
            case RecentDocs:
                parts.Add("RecentDocs păstrează numele, nu calea întreagă a fișierului");
                break;
            case OfficeMru:
                parts.Add($"{F(e, "App")} {F(e, "OfficeVersion")}, {F(e, "MruKind")}");
                break;
            case RecycleBin:
                var size = F(e, "OriginalSize");
                parts.Add(long.TryParse(size, out var n) && n >= 0 ? $"{n:N0} octeți" : "dimensiune necunoscută");
                parts.Add(RContentText(rcontent));
                break;
        }
        if (F(e, "ItemIssues") is { Length: > 0 } issues) parts.Add("avertismente la decodare: " + issues);
        return string.Join("; ", parts);
    }

    /// <summary>
    /// The lines of the user-activity part of a report (investigation PDF and control PDF, section 3b): counts, the strongest rows with their time and what it means,
    /// the limits. <paramref name="report"/> null = not evaluated by this report, said as such. Pure: the tests read it without parsing the PDF.
    /// </summary>
    public static List<(string Text, bool Head)> ReportLines(UserActivityReport? report, int maxItems = 15)
    {
        var l = new List<(string, bool)>();
        if (report is null)
        {
            l.Add(("Activitatea utilizatorului (ShellBags, RecentDocs, Office MRU, Coș de reciclare $I) nu a fost evaluată de acest raport: ea provine din investigația stației cu profilul „Complet” (pagina Investigație, fila „Activitatea utilizatorului”).", false));
            return l;
        }
        l.Add(("Activitatea utilizatorului — urme ale folosirii folderelor și fișierelor", true));
        l.Add((report.Summary, false));
        if (report.Total == 0) return l;
        var strong = report.Items.Where(i => i.IsRelevant).Take(maxItems).ToList();
        foreach (var i in strong)
            l.Add(($"  {i.SourceLabel} · {i.Path} · {i.TimeText} ({i.TimeMeaning}) · {i.Who} · {i.Relevance}" + (i.IsDeletion ? $" · {RContentText(i.RContent)}" : ""), false));
        var more = report.Items.Count(i => i.IsRelevant) - strong.Count;
        if (more > 0) l.Add(($"  … încă {more} urme relevante (în pagina Investigație, fila „Activitatea utilizatorului”).", false));
        l.Add(("Limită: aceste urme arată că Explorer a afișat un folder, că un document a fost deschis recent sau că un element a fost șters; nu dovedesc citirea, copierea sau scoaterea unui fișier. Rezultatul este DE VERIFICAT, nu NECONFORM; severitatea o stabilește proprietarul procedurii.", false));
        return l;
    }
}
