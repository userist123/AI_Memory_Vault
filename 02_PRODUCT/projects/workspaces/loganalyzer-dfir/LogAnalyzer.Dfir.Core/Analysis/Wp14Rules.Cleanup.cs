using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// D18: traces of removable devices that were deleted from the registry. Two independent observations feed one finding:
/// (1) deleted keys and values recovered from the free space of the offline hives (event source "DeletedRegistryKey", written by the hive parsers);
/// (2) a device that Partition/Diagnostic 1006 logged as a USB disk but that has no device key left in the SYSTEM hive.
/// Deletion can be a clean-up or a normal uninstall; the hive keeps neither the time of the deletion nor who did it.
/// </summary>
public static partial class Wp14Rules
{
    internal const string DeletedTracesRuleId = "MEDIA-TRACES-DELETED";

    /// <summary>Value of <see cref="Finding.Signals"/> set when a device is in the 1006 history but its registry key is gone.</summary>
    public const string TracesMayBeCleanedSignal = "TRACES_MAY_BE_CLEANED";

    internal static void DeletedTraceFindings(Ctx c, List<Finding> o)
    {
        var deleted = c.Events.Where(e => e.Source == "DeletedRegistryKey").ToList();

        // Devices Windows logged as USB disks (bus type 7) with no device key left. Only judged when a SYSTEM hive was read, otherwise the registry says nothing.
        var hiveRead = c.Events.Any(e => e.Source is "USB" or "MountedVolume" || e.Source == "DeletedRegistryKey" && F(e, "Hive") == "SYSTEM");
        var present = c.Events.Where(e => e.Source == "USB" && F(e, "Interface") != "MountedDevices only")
            .Select(e => MediaRegister.NormalizeSerial(F(e, "Serial"))).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = hiveRead
            ? ObservedUsb(c.Events).Where(m => m.Serial.Length > 0 && !present.Contains(m.Serial))
                .Select(m => (Medium: m, Logged: m.Events.Where(e => Ev(e, "Microsoft-Windows-Partition/Diagnostic", 1006) && F(e, "BusType").Trim() == "7" && T(e) is not null).OrderBy(e => T(e)!.Value).ToList()))
                .Where(x => x.Logged.Count > 0).OrderBy(x => T(x.Logged[0])!.Value).ToList()
            : [];
        if (deleted.Count == 0 && missing.Count == 0) return;

        var knownSerials = ObservedUsb(c.Events).Select(m => m.Serial).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var correlated = missing.Count > 0 || deleted.Any(e => MediaRegister.NormalizeSerial(F(e, "Serial")) is { Length: > 0 } s && knownSerials.Contains(s));

        var lines = new List<string>();
        if (deleted.Count > 0)
        {
            var byArea = deleted.GroupBy(e => F(e, "Area")).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}: {g.Count(e => F(e, "Deleted") == "key")} chei și {g.Count(e => F(e, "Deleted") == "value")} valori");
            var serials = deleted.Select(e => F(e, "Serial")).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var times = deleted.Select(T).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
            lines.Add($"Au fost recuperate din spațiul liber al registry-ului {deleted.Count} înregistrări șterse legate de dispozitive ({string.Join("; ", byArea)}).");
            if (serials.Count > 0) lines.Add($"Serii de dispozitive găsite în înregistrările șterse: {string.Join(", ", serials.Take(10))}{(serials.Count > 10 ? $" și încă {serials.Count - 10}" : "")}.");
            if (times.Count > 0) lines.Add($"Ultima scriere a cheilor șterse, înainte de ștergere: de la {Time(times[0])} până la {Time(times[^1])}. Registry-ul nu păstrează ora ștergerii.");
        }
        if (missing.Count > 0)
        {
            var list = missing.Take(10).Select(x => $"{x.Medium.Serial} (conectat prima dată în jurnal la {Time(T(x.Logged[0]))})");
            lines.Add($"Dispozitive din jurnalul Partition/Diagnostic 1006 care nu mai au nicio cheie în registry (Enum\\USBSTOR): {string.Join("; ", list)}{(missing.Count > 10 ? $" și încă {missing.Count - 10}" : "")}. " +
                      "Semnal: este posibil ca urmele acestor dispozitive să fi fost curățate.");
        }
        lines.Add("O ștergere poate fi o curățare a urmelor, dar și o dezinstalare obișnuită a dispozitivului (Manager dispozitive, curățarea Plug and Play, o unealtă de întreținere, reinstalarea Windows). " +
                  "Registry-ul nu arată cine a șters și de ce; constatarea arată ce a rămas în urmă, nu o intenție.");

        var refs = new List<EvidenceRef>();
        refs.AddRange(deleted.Take(20).Select(e => Ref(e, e.Summary)));
        refs.AddRange(missing.SelectMany(x => x.Logged.Take(3)).Take(10).Select(e => Ref(e, "Partition/Diagnostic 1006 (dispozitiv fără cheie în registry)")));
        var allTimes = deleted.Select(T).Concat(missing.SelectMany(x => x.Logged.Select(T))).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();

        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = DeletedTracesRuleId,
            Title = "Urme de dispozitive șterse din registry",
            Severity = missing.Count > 0 ? Sev(c, Severity.Medium) : Severity.Low,
            Category = c.FindingCategory,
            Classification = correlated ? Classification.Correlated : Classification.Direct,
            Confidence = Confidence.Medium,
            FirstSeenUtc = allTimes.Count > 0 ? allTimes[0] : null, LastSeenUtc = allTimes.Count > 0 ? allTimes[^1] : null,
            Description = string.Join(" ", lines),
            ClassificationReason = correlated
                ? "Corelație între înregistrările recuperate din registry și jurnalul Partition/Diagnostic 1006 (sau alte surse care numesc același dispozitiv)."
                : "Observație directă în spațiul liber al registry-ului; nicio altă sursă colectată nu numește aceleași dispozitive.",
            SemanticType = SemanticType.Observation,
            Signals = missing.Count > 0 ? [TracesMayBeCleanedSignal] : null,
            SupportingEvidence = refs,
            AlternativeExplanations =
            [
                "Dezinstalarea normală a unui dispozitiv (Manager dispozitive, curățarea Plug and Play) șterge aceleași chei.",
                "Reinstalarea sau reîmprospătarea Windows poate elimina cheile de dispozitive fără nicio intenție de ascundere.",
                "Un dispozitiv logat în 1006, dar fără cheie, poate proveni dintr-un alt sistem de operare sau dintr-un hive mai vechi decât jurnalul.",
            ],
            MissingEvidence =
            [
                "Ora ștergerii și contul care a șters (registry-ul nu le păstrează; ar putea apărea în Security 4657 sau Sysmon 12/13 dacă erau activate).",
                "Fișierele jurnalului de tranzacții ale hive-ului (.LOG1/.LOG2) și copii de siguranță ale hive-ului, care pot păstra versiuni mai vechi.",
            ],
        });
    }
}
