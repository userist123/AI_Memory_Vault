using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// WP14 usage of removable media: who used each medium (MountPoints2 per profile, interactive logon sessions) and which LNK / Jump List files name it.
/// One MEDIA-USAGE finding per medium, with the evidence it rests on. Ambiguity is listed, never resolved to one name.
/// </summary>
public static partial class Wp14Rules
{
    internal const string UsageRuleId = "MEDIA-USAGE";

    internal static void MediaUsageFindings(Ctx c, List<Finding> o)
    {
        var media = ObservedUsb(c.Events).Where(m => m.Serial.Length > 0).OrderBy(m => m.Times.DefaultIfEmpty(DateTimeOffset.MaxValue).Min()).ToList();
        if (media.Count == 0) return;

        // MountedDevices: volume GUID -> disk serial. The disk serial is not the volume serial that LNK files carry; that one comes from the boot record logged in 1006 (Observed.VolumeSerials).
        var volumeToSerial = c.Events.Where(e => e.Source == "MountedVolume" && F(e, "VolumeGuid").Length > 0)
            .GroupBy(e => F(e, "VolumeGuid"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => MediaRegister.NormalizeSerial(F(g.First(), "Serial")), StringComparer.OrdinalIgnoreCase);
        var mountEvents = c.Events.Where(e => e.Source == "MountPoints2" && F(e, "VolumeGuid").Length > 0 && T(e) is not null).ToList();
        var mounts = mountEvents.Select(e => new VolumeMount(F(e, "Account"), F(e, "VolumeGuid"), T(e))).ToList();
        var logonEvents = c.Events.Where(e => Ev(e, "Security", 4624, 4634, 4647) && T(e) is not null).ToList();
        var sessions = MediaUsageRules.PairSessions(logonEvents.Select(e =>
        {
            var name = F(e, "TargetUserName");
            return new LogonRecord(F(e, "TargetLogonId", "LogonId"), name.Length > 0 && !IsMachineAccount(name) ? name : "", F(e, "LogonType"), T(e)!.Value, e.EventId == "4624");
        })).ToList();
        var files = c.Events.Where(e => e.Source is "LNK" or "JumpList")
            .Select(e => new LinkedFile<TimelineEvent>(e, e.Path, F(e, "VolumeSerial"), F(e, "DriveType"), e.Source == "LNK" ? ParseIso(F(e, "LnkFileModifiedUtc")) : T(e)))
            .ToList();

        foreach (var m in media)
        {
            var arrivals = m.Events.Where(e => Ev(e, "Microsoft-Windows-Partition/Diagnostic", 1006) && T(e) is not null).Select(e => T(e)!.Value).Distinct().OrderBy(t => t).ToList();
            if (arrivals.Count == 0) arrivals = m.Times.Distinct().OrderBy(t => t).ToList();
            if (arrivals.Count == 0) continue;
            var connections = arrivals.Select(t => new MediumConnection(t, null)).ToList();   // a removal is not read yet: the window ends at the next arrival

            var verdicts = MediaUsageRules.Attribute(m.Serial, connections, volumeToSerial, mounts, sessions);
            var volumes = volumeToSerial.Where(kv => kv.Value.Equals(m.Serial, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var links = MediaUsageRules.Link<TimelineEvent>(m.VolumeSerials, m.Letter, connections, files);
            // Nothing to say (no profile, no session, no linked file): no extra finding; the medium's own finding already reports the presence.
            if (links.Count == 0 && verdicts.All(v => v.Status == UsageStatus.NotAttributable)) continue;
            var strong = links.Count(l => l.Kind == FileLinkKind.OpenedFromMedium);
            var hints = links.Count - strong;

            var accounts = verdicts.SelectMany(v => v.Accounts).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            var summary = verdicts.Any(v => v.Status == UsageStatus.Ambiguous) ? "ambiguu, mai mulți candidați"
                : verdicts.Any(v => v.Status == UsageStatus.Direct) ? "profil identificat (din registru)"
                : verdicts.Any(v => v.Status == UsageStatus.Inferred) ? "doar o presupunere"
                : "nu se poate atribui";
            var lines = verdicts.Take(6).Select(v => $"{Time(v.Connection.ConnectUtc)} — {StatusWord(v.Status)}: {v.Reason}");
            var linkLines = links.Take(8).Select(l => $"{Time(l.WhenUtc)} {l.Path} — {(l.Kind == FileLinkKind.OpenedFromMedium ? "deschis de pe acest suport" : "doar aceeași literă (indiciu)")}");
            var serialText = string.Join(", ", m.VolumeSerials.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            var noSerial = m.VolumeSerials.Count == 0
                ? "Seria volumului suportului (cea din scurtăturile LNK) nu este citită din sursele analizate (sectorul de boot logat în Partition/Diagnostic 1006 lipsește sau nu este recunoscut): " +
                  "legăturile sigure după serie nu pot fi făcute; sunt afișate doar indiciile după litera de unitate, care nu dovedesc nimic singure."
                : $"Seria volumului suportului este {serialText} (din sectorul de boot logat în Partition/Diagnostic 1006), dar nicio scurtătură nu o poartă: " +
                  "indiciile după litera de unitate nu dovedesc singure că fișierele sunt de pe acest suport.";
            var serialLine = m.VolumeSerials.Count == 0 ? "" : $" Seria volumului (din sectorul de boot logat în 1006): {serialText}.";

            var desc = $"Suportul {(m.Serial)} ({(m.Letter.Length > 0 ? "litera " + m.Letter + ":" : "fără literă cunoscută")}): {verdicts.Count} conectări.{serialLine} " +
                       $"Cine a folosit: {summary}" + (accounts.Count > 0 ? $" ({string.Join(", ", accounts)})" : "") + ". " +
                       string.Join(" ", lines) + (verdicts.Count > 6 ? $" (și încă {verdicts.Count - 6} conectări, vezi evidența)." : "") + " " +
                       (links.Count == 0 ? "Fișiere legate de acest suport: niciunul găsit în scurtăturile LNK sau în Jump Lists."
                                         : $"Fișiere legate de acest suport: {strong} deschise de pe acest suport (serie de volum), {hints} doar aceeași literă (indiciu, nu dovadă). " +
                                           string.Join("; ", linkLines) + (links.Count > 8 ? $"; și încă {links.Count - 8}." : ".")) + " " +
                       (strong == 0 && links.Count > 0 ? noSerial : "");

            var refs = new List<EvidenceRef>();
            refs.AddRange(mountEvents.Where(e => volumes.Contains(F(e, "VolumeGuid"))).Take(10).Select(e => Ref(e, e.Summary)));
            refs.AddRange(c.Events.Where(e => e.Source == "MountedVolume" && volumes.Contains(F(e, "VolumeGuid"))).Take(5).Select(e => Ref(e, e.Summary)));
            refs.AddRange(m.Events.Where(e => Ev(e, "Microsoft-Windows-Partition/Diagnostic", 1006)).Take(10).Select(e => Ref(e, "Partition/Diagnostic 1006 (conectare)")));
            refs.AddRange(logonEvents.Take(10).Select(e => Ref(e, $"{e.EventId} {F(e, "TargetUserName")} tip {F(e, "LogonType")} (sesiune la conectare)")));
            refs.AddRange(links.Take(10).Select(l => Ref(l.Item, $"{(l.Kind == FileLinkKind.OpenedFromMedium ? "legătură după serie" : "indiciu după literă")}: {l.Path}")));

            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = UsageRuleId,
                Title = $"Cine a folosit suportul {m.Serial}: {summary}",
                Severity = verdicts.Any(v => v.Status == UsageStatus.Ambiguous) ? Sev(c, Severity.Medium) : Severity.Low,
                Category = c.FindingCategory, Classification = Classification.Correlated, Confidence = Confidence.Medium,
                FirstSeenUtc = arrivals[0], LastSeenUtc = arrivals[^1], User = string.Join(", ", accounts),
                Description = desc,
                ClassificationReason = "Corelație între registrul fiecărui profil (MountPoints2), MountedDevices, sesiunile de logon și scurtăturile LNK. " +
                                       "Nicio sursă nu spune singură cine a conectat stick-ul; regula arată ce se poate afirma și ce nu.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = refs,
                AlternativeExplanations =
                [
                    "MountPoints2 arată cine a montat volumul, nu ora conectării: un profil poate fi folosit de mai multe persoane.",
                    "O sesiune deschisă la conectare nu dovedește că persoana a conectat stick-ul.",
                    "Un fișier LNK poate fi creat la deschiderea unui fișier de pe un alt suport cu aceeași literă.",
                ],
            });
        }
    }

    private static string StatusWord(UsageStatus s) => s switch
    {
        UsageStatus.Direct => "un profil a montat volumul",
        UsageStatus.Inferred => "presupunere: o singură sesiune deschisă",
        UsageStatus.Ambiguous => "nu se poate stabili, mai mulți candidați",
        _ => "nu se poate atribui",
    };

    private static string Time(DateTimeOffset? t) => t is { } v ? v.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "ora necunoscută";

    private static DateTimeOffset? ParseIso(string s) =>
        DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;
}
