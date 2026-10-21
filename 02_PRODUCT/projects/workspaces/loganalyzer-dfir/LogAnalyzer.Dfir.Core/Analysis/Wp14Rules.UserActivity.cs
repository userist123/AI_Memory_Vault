using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// E7 slice 3: the user-activity artifacts (ShellBags, RecentDocs, Office MRU, Recycle Bin $I) read against the classified folders of the procedure profile and the
/// removable volumes of the case. The analysis is <see cref="UserActivityAnalysis"/>; this file turns it into findings. These are weak, indirect traces: every
/// finding is DE VERIFICAT (candidate), never NECONFORM from these alone; the severities are provisional and the owner decides later whether any is stronger.
/// </summary>
public static partial class Wp14Rules
{
    internal const string UserActivityClassifiedRuleId = "USER-ACTIVITY-CLASSIFIED";
    internal const string UserActivityRemovableRuleId = "USER-ACTIVITY-REMOVABLE";
    internal const string UserActivityDeletedRuleId = "USER-ACTIVITY-DELETED";

    /// <summary>
    /// The user-activity view of a case timeline: the classified folders of the procedure profile, the drive letters that the case's device facts (USB history,
    /// partition events, the station facts <paramref name="factLetters"/>) tie to removable media. The rules, the investigation page and the reports all use this.
    /// </summary>
    public static UserActivityReport UserActivityForCase(IReadOnlyList<TimelineEvent> events, LogAnalyzer.Dfir.Profile.ProcedureProfile? profile, IEnumerable<string>? factLetters = null)
    {
        var folders = profile?.ClassifiedFolders.Where(f => f.Path.Trim().Length > 0).Select(f => new ClassifiedRoot(f.Path.Trim(), null, f.Note)).ToList() ?? [];
        var letters = new List<string>();
        foreach (var m in ObservedUsb(events)) if (m.Letter.Length > 0) letters.Add(m.Letter);
        letters.AddRange(factLetters ?? []);
        return UserActivityAnalysis.Analyze(events, folders, letters);
    }

    private static string Times(IEnumerable<UserActivityItem> items, out DateTimeOffset? first, out DateTimeOffset? last)
    {
        var known = items.Where(i => i.TimeUtc is not null).Select(i => i.TimeUtc!.Value).ToList();
        first = known.Count > 0 ? known.Min() : null;
        last = known.Count > 0 ? known.Max() : null;
        return known.Count == 0 ? "fără oră în sursă" : $"{first:yyyy-MM-dd HH:mm} – {last:yyyy-MM-dd HH:mm} UTC";
    }

    private static string Listing(IEnumerable<UserActivityItem> items, int max = 10)
    {
        var l = items.Take(max + 1).ToList();
        return string.Join("; ", l.Take(max).Select(i => $"{i.SourceLabel}: {i.Path} — {i.TimeText} ({i.TimeMeaning}); {i.Who}")) + (l.Count > max ? "; …" : "");
    }

    private static string SourcesOf(IReadOnlyCollection<UserActivityItem> items) =>
        string.Join(", ", items.GroupBy(i => i.Source).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{UserActivityAnalysis.ShortLabel(g.Key)} {g.Count()}"));

    private static Finding UserActivityFinding(Ctx c, string rule, string title, Severity sev, Confidence conf, SemanticType semantic, IReadOnlyList<UserActivityItem> items,
        string description, string reason, string file, params string[] missing)
    {
        Times(items, out var first, out var last);
        return new Finding
        {
            FindingId = c.NextId(), RuleId = rule, Title = title, Severity = sev, Category = c.FindingCategory, Classification = Classification.Candidate, Confidence = conf,
            FirstSeenUtc = first, LastSeenUtc = last, File = file,
            User = string.Join(", ", items.Select(i => i.Who).Distinct(StringComparer.OrdinalIgnoreCase).Take(5)),
            Description = description, ClassificationReason = reason, SemanticType = semantic,
            SupportingEvidence = items.Select(i => i.Origin).Distinct().Take(20).Select(e => Ref(e, e.Source + ": " + e.Summary)).ToList(),
            MissingEvidence = [.. missing],
        };
    }

    internal static void UserActivityFindings(Ctx c, List<Finding> o)
    {
        if (!c.Events.Any(e => e.Source is UserActivityAnalysis.ShellBags or UserActivityAnalysis.RecentDocs or UserActivityAnalysis.OfficeMru or UserActivityAnalysis.RecycleBin)) return;
        var report = UserActivityForCase(c.Events, c.Profile, c.Facts?.RemovableLetters);

        // 1. classified folders seen in Explorer / recent documents (one finding per folder)
        foreach (var g in report.Classified.GroupBy(i => i.ClassifiedRoot, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var items = g.ToList();
            var when = Times(items, out _, out _);
            o.Add(UserActivityFinding(c, UserActivityClassifiedRuleId,
                $"Activitatea utilizatorului: folderul clasificat {g.Key} apare în urmele Explorer / documentelor recente ({items.Count})",
                Severity.Medium, Confidence.Medium, SemanticType.Presence, items,
                $"{items.Count} urme ({SourcesOf(items)}) pentru căi din {g.Key}, în perioada {when}. ShellBags arată că Explorer a afișat folderul (sau un subfolder) în profilul utilizatorului, nu că un fișier a fost citit; " +
                $"Office MRU arată ultima deschidere a unui fișier de către aplicația Office; RecentDocs păstrează doar numele. {Listing(items)}",
                "Urme ale profilului utilizatorului (ShellBags / Office MRU / RecentDocs) potrivite după cale cu un folder clasificat al profilului de proceduri; slabe și indirecte: arată navigarea sau deschiderea, nu accesul la conținut, și nu spun dacă a fost autorizată. DE VERIFICAT.",
                g.Key, "Auditul accesului la fișiere (4663) pentru aceeași perioadă", "Confirmarea că utilizatorul era autorizat pentru folder", "Conținutul fișierelor deschise"));
        }

        // 2. removable volumes (one finding per drive letter; confirmed by the device facts or only possible)
        var browse = report.Items.Where(i => !i.IsDeletion && i.Drive != DriveMatch.None).ToList();
        foreach (var g in browse.GroupBy(i => (i.DriveLetter, i.Drive)).OrderBy(g => g.Key.DriveLetter, StringComparer.Ordinal).ThenBy(g => g.Key.Drive))
        {
            var items = g.ToList();
            bool confirmed = g.Key.Drive == DriveMatch.Removable;
            var when = Times(items, out _, out _);
            o.Add(UserActivityFinding(c, UserActivityRemovableRuleId,
                confirmed ? $"Activitatea utilizatorului: urme de navigare sau deschidere pe volumul amovibil {g.Key.DriveLetter} ({items.Count})"
                          : $"Activitatea utilizatorului: urme pe unitatea {g.Key.DriveLetter}, posibil amovibilă ({items.Count})",
                confirmed ? Sev(c, Severity.Medium) : Severity.Low, confirmed ? Confidence.Medium : Confidence.Low, SemanticType.Presence, items,
                (confirmed ? $"Litera {g.Key.DriveLetter} apare în istoricul dispozitivelor din caz ca litera unui suport amovibil; o literă poate fi reutilizată de alt volum, iar urmele nu sunt datate pe volumul exact. "
                           : $"Litera {g.Key.DriveLetter} nu este asociată cu niciun suport amovibil din caz: poate fi un suport, un disc intern, o unitate optică sau una de rețea; incertitudine neînlăturată. ") +
                $"{items.Count} urme ({SourcesOf(items)}), în perioada {when}. ShellBags arată că Explorer a afișat folderul, nu că un fișier a fost citit sau copiat. {Listing(items)}",
                confirmed ? "Urme ale profilului utilizatorului pe o cale cu litera unui suport amovibil cunoscut din caz (potrivire după literă, nu după seria volumului); slabe, DE VERIFICAT."
                          : "Urme ale profilului utilizatorului pe o literă de unitate fără suport amovibil cunoscut în caz; potrivire doar după literă, cu incertitudinea declarată; DE VERIFICAT.",
                g.Key.DriveLetter + @"\", "Seria volumului din urmele LNK / ShellBags (legătura sigură cu un suport)", "Registrul de medii", "Conținutul și destinația oricărei copieri"));
        }

        // 3. Recycle Bin records: deleted from a classified folder or from a removable volume (one finding per folder / letter)
        var deleted = report.Deleted;
        var byRoot = deleted.Where(i => i.ClassifiedRoot.Length > 0).GroupBy(i => "F|" + i.ClassifiedRoot, StringComparer.OrdinalIgnoreCase);
        var byDrive = deleted.Where(i => i.ClassifiedRoot.Length == 0).GroupBy(i => "V|" + i.DriveLetter + "|" + i.Drive, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byRoot.Concat(byDrive).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var items = g.OrderByDescending(i => i.TimeUtc ?? DateTimeOffset.MinValue).ToList();
            bool classified = g.Key.StartsWith("F|", StringComparison.Ordinal);
            var first = items[0];
            bool removable = !classified && first.Drive == DriveMatch.Removable;
            var absent = items.Count(i => i.RContent == "absent");
            var when = Times(items, out _, out _);
            var where = classified ? $"din folderul clasificat {first.ClassifiedRoot}" : removable ? $"de pe volumul amovibil {first.DriveLetter}" : $"de pe unitatea {first.DriveLetter}, posibil amovibilă";
            o.Add(UserActivityFinding(c, UserActivityDeletedRuleId,
                $"Coș de reciclare: {items.Count} {(items.Count == 1 ? "element șters" : "elemente șterse")} {where}",
                classified ? Severity.Medium : removable ? Sev(c, Severity.Medium) : Severity.Low, classified ? Confidence.High : removable ? Confidence.Medium : Confidence.Low, SemanticType.Observation, items,
                $"Metadatele Coșului de reciclare ($I) arată {items.Count} ștergeri {where}, în perioada {when}; ora este cea a ștergerii (UTC), scrisă la mutarea în Coș. " +
                (absent > 0 ? $"Pentru {absent} dintre ele fișierul $R lipsește: conținut golit sau eliminat (nu înseamnă că elementul nu a existat). " : "") +
                (!classified && !removable ? "Litera unității nu este legată de un suport amovibil din caz; incertitudine neînlăturată. " : "") +
                $"Ștergerea nu dovedește scoaterea de informații și poate fi legitimă (distrugere aprobată). {Listing(items)}",
                classified ? "Înregistrarea Coșului ($I) spune direct calea originală și ora ștergerii; faptul că ștergerea a fost nepermisă se vede doar din documentele organizației. DE VERIFICAT."
                           : "Înregistrarea Coșului ($I) spune direct calea originală și ora ștergerii; legătura cu un suport amovibil este după litera unității. DE VERIFICAT.",
                classified ? first.ClassifiedRoot : first.DriveLetter + @"\", "Procesul-verbal de distrugere", "Conținutul elementului (dacă $R lipsește, din copii de umbră sau backup)", "Evenimentele de audit ale ștergerii (4660), dacă auditul era pornit"));
        }
    }
}
