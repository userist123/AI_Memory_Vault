using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp14Rules
{
    private sealed record OpticalHit(TimelineEvent Event, string Kind, string Text);

    /// <summary>
    /// MEDIA-OPTICAL-ACTIVITY: CD/DVD as an artifact of its own, never merged with USB: an optical drive device node (Kernel-PnP / Security 6416 with the
    /// cdrom class), IMAPI events, an LNK whose target volume is DriveType CDROM, an .iso mount (VHDMP / Storage) and burn tools from the data list.
    /// Writing to a disc is claimed only from IMAPI or an executed burn tool; a drive or an image alone is "prezență, nu inscripționare". An optical disc has
    /// no serial in these sources, so it cannot be matched to the media register.
    /// </summary>
    private static void Optical(Ctx c, List<Finding> o)
    {
        var d = c.Data;
        var hits = new List<OpticalHit>();
        bool burnExecuted = false, imapi = false, drive = false, image = false;
        string Leaf(string p) => p.Length == 0 ? "" : WinPath.GetFileName(p.TrimEnd('\\'));
        bool IsBurn(string name) => name.Length > 0 && d.BurnTools.Executables.Contains(name, StringComparer.OrdinalIgnoreCase);
        bool IsImage(string text) => d.Optical.ImageExtensions.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
        foreach (var e in c.Events)
        {
            if (e.Provider.Length > 0 && d.BurnTools.ImapiProviders.Any(p => e.Provider.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            { hits.Add(new(e, "IMAPI", $"IMAPI (serviciul de inscripționare Windows): {e.Provider} {e.EventId}")); imapi = true; continue; }
            if (InChannel(e, "Kernel-PnP", 400, 410) || Ev(e, "Security", 6416))
            {
                var inst = Instance(e);
                if (inst.Length > 0 && !inst.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) && d.Optical.DeviceWords.Any(w => inst.Contains(w, StringComparison.OrdinalIgnoreCase)))
                { hits.Add(new(e, "unitate optică", $"unitate optică {inst}")); drive = true; continue; }
            }
            if (e.Source == "LNK" && F(e, "DriveType").Equals("CDROM", StringComparison.OrdinalIgnoreCase))
            { hits.Add(new(e, "LNK către CD/DVD", $"LNK către {e.Path} (volum CDROM)")); drive = true; continue; }
            if (e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && d.Optical.MountChannelWords.Any(w => e.Source.Contains(w, StringComparison.OrdinalIgnoreCase)) && IsImage(AllText(e)))
            { hits.Add(new(e, "imagine montată", $"montare de imagine .iso/.img ({e.Source[9..]} {e.EventId}): {Shorten(AllText(e))}")); image = true; continue; }
            if (e.Source is "LNK" or "JumpList" && IsImage(e.Path))
            { hits.Add(new(e, "imagine referită", $"{e.Source} către imagine {e.Path}")); image = true; continue; }
            var name = Leaf(e.Process.Length > 0 ? e.Process : e.Path);
            if (e.Source is "Prefetch" or "BAM" or "UserAssist" && IsBurn(name)) { hits.Add(new(e, "instrument de inscripționare executat", $"{name} executat ({e.Source})")); burnExecuted = true; }
            else if (e.Source is "Amcache" or "ShimCache" && IsBurn(name)) hits.Add(new(e, "instrument de inscripționare prezent", $"{name} prezent ({e.Source}; prezent nu înseamnă executat)"));
        }
        foreach (var p in ProcessStarts(c.Events))
            if (IsBurn(Leaf(p.Image))) { hits.Add(new(p.Event, "instrument de inscripționare executat", $"{Leaf(p.Image)} pornit ({p.Event.Source[9..]} {p.Event.EventId})")); burnExecuted = true; }
        if (hits.Count == 0) return;

        var ordered = hits.OrderBy(h => T(h.Event) ?? DateTimeOffset.MaxValue).ToList();
        var times = ordered.Select(h => T(h.Event)).Where(t => t is not null).Select(t => t!.Value).ToList();
        bool writeClaim = imapi || burnExecuted;
        var kinds = ordered.Select(h => h.Kind).Distinct().ToList();
        var regNote = c.Input.Media is { IsDefined: true } m && m.Rows.Count(r => r.IsOptical) is var n && n > 0
            ? $" Registrul conține {n} medii optice, dar un disc optic nu are serie în aceste surse: observația nu poate fi asociată cu niciunul."
            : " Un disc optic nu are serie în aceste surse, deci nu poate fi comparat cu registrul de medii (stare: UNKNOWN).";
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = "MEDIA-OPTICAL-ACTIVITY",
            Title = "CD/DVD: " + string.Join(", ", kinds),
            Severity = writeClaim ? Sev(c, Severity.Medium) : Sev(c, Severity.Low),
            Category = c.FindingCategory, Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1052",
            FirstSeenUtc = times.Count > 0 ? times.Min() : null, LastSeenUtc = times.Count > 0 ? times.Max() : null,
            Description = $"{ordered.Count} urme optice ({string.Join(", ", kinds)}): " + string.Join("; ", ordered.Take(8).Select(h => h.Text)) + ". " +
                          (writeClaim ? "Inscripționarea pe disc este susținută doar de IMAPI sau de un instrument de inscripționare executat; conținutul și discul nu sunt cunoscute."
                                      : "Doar prezență, nu inscripționare: o unitate optică, o imagine sau un instrument prezent nu arată că s-a scris un disc.") + regNote,
            ClassificationReason = "Artefacte de tip CD/DVD (unitate, IMAPI, LNK cu volum CDROM, montare de imagine, instrument de inscripționare) tratate separat de USB.",
            SemanticType = SemanticType.Presence,
            SupportingEvidence = ordered.Take(20).Select(h => Ref(h.Event, h.Text)).ToList(),
            AlternativeExplanations = ["O unitate optică virtuală (hipervizor) sau o imagine .iso montată pentru instalare produc aceleași urme.", "IMAPI rulează și la simpla listare a unei unități; poate să nu însemne scriere."],
            AirGap = new AirGapDetail("OPTICAL MEDIA", string.Join("; ", kinds), AirGapAuthorization.Undefined, "mediile optice nu pot fi comparate cu registrul (fără serie): autorizarea nu poate fi stabilită",
                $"{ordered.Count} urme optice ({(writeClaim ? "cu indicii de inscripționare" : "doar prezență")})", times.Count > 0 ? times.Min() : null,
                "necunoscut: urmele optice nu conțin contul", image ? "imagine .iso/.img" : drive ? "unitate optică" : "instrument de inscripționare", c.CaseClassText,
                "nu se aplică (disc optic fără serie)", writeClaim ? "scriere pe disc posibilă (IMAPI / instrument executat)" : "necunoscută: prezența nu arată direcția", "disc CD/DVD",
                ordered.Take(5).Select(h => $"{h.Event.Source} {h.Event.EventId} {h.Event.Locator}".Trim()).ToList()),
        });
    }

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120] + "…";
}
