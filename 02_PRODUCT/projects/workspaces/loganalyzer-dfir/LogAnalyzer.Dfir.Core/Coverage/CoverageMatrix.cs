using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Coverage;

/// <summary>State of one artifact family in a case. NOT_SUPPORTED is a limit of the application; the others are about this case.</summary>
public enum CoverageState { Collected, Partial, Unavailable, NotCollected, NotSupported }

/// <summary>Overall coverage of a case (rule in <see cref="CoverageMatrix.Build"/>).</summary>
public enum OverallCoverage { Full, Partial, Minimal, Unknown }

/// <summary>One collector run as the coverage matrix needs it (the pipeline's collection row, without the dependency on the Windows project).</summary>
public sealed record CollectorRun(string Collector, EvidenceStatus Status, int EvidenceCount, string Errors);

/// <summary>Everything the matrix is computed from. All of it is produced by the pipeline (and read back by the case loader).</summary>
public sealed record CoverageInputs(
    IReadOnlyList<EvidenceItem> Evidence, IReadOnlyList<ParseResult> Parsing, IReadOnlyList<CollectorRun> Collection,
    IReadOnlyList<EvidenceGap> Gaps, IReadOnlyList<ParserDescriptor> Parsers);

/// <summary>One row of the coverage matrix: the family, its state, why, and how far its parser is validated.</summary>
public sealed record CoverageRow(string FamilyId, string Family, CoverageState State, string Reason, string ParserStatus, IReadOnlyList<string> ParserIds)
{
    public string StateName => CoverageNames.Spec(State);
    public string StateLabel => CoverageNames.Label(State);
    /// <summary>WP6a (R18): the plain-language phrase of the family when the glossary has one (BAM, Amcache, EVTX, Prefetch); otherwise the label unchanged. <see cref="Family"/> stays the technical label.</summary>
    public string HumanFamily => LogAnalyzer.Dfir.Language.Glossary.Find(FamilyId) is { } t ? t.Romanian : Family;
    /// <summary>Tooltip for <see cref="HumanFamily"/>: the technical name and the explanation; the label alone when there is no glossary entry.</summary>
    public string FamilyTooltip => LogAnalyzer.Dfir.Language.Glossary.Find(FamilyId) is { } ? LogAnalyzer.Dfir.Language.Glossary.Tooltip(FamilyId) : Family;
}

public static class CoverageNames
{
    public static string Spec(CoverageState s) => s switch
    {
        CoverageState.Collected => "COLLECTED", CoverageState.Partial => "PARTIAL", CoverageState.Unavailable => "UNAVAILABLE",
        CoverageState.NotCollected => "NOT_COLLECTED", _ => "NOT_SUPPORTED",
    };

    public static string Label(CoverageState s) => s switch
    {
        CoverageState.Collected => "Colectat", CoverageState.Partial => "Parțial", CoverageState.Unavailable => "Indisponibil",
        CoverageState.NotCollected => "Necolectat", _ => "Nesuportat",
    };

    public static string Spec(OverallCoverage o) => o.ToString().ToUpperInvariant();

    public static string Label(OverallCoverage o) => o switch
    {
        OverallCoverage.Full => "Completă (pentru familiile suportate)", OverallCoverage.Partial => "Parțială",
        OverallCoverage.Minimal => "Minimă", _ => "Nedeterminată",
    };
}

/// <summary>
/// U21: one row per artifact family with COLLECTED / PARTIAL / UNAVAILABLE / NOT_COLLECTED / NOT_SUPPORTED, the reason and the parser status, and an
/// overall coverage. A partial or empty result is never a clean system: the matrix says what was NOT looked at, and nothing here reads "no problem".
/// </summary>
public sealed class CoverageMatrix
{
    /// <summary>Overall coverage is MINIMAL when fewer than this share of the supported families is covered (COLLECTED counts 1, PARTIAL counts 0.5).</summary>
    public const double MinimalBelow = 1.0 / 3.0;

    public IReadOnlyList<CoverageRow> Rows { get; }
    public OverallCoverage Overall { get; }
    public string OverallReason { get; }

    private CoverageMatrix(IReadOnlyList<CoverageRow> rows, OverallCoverage overall, string reason) { Rows = rows; Overall = overall; OverallReason = reason; }

    public string OverallName => CoverageNames.Spec(Overall);
    public string OverallLabel => CoverageNames.Label(Overall);

    /// <summary>Families the application cannot read at all (listed so they are never taken for "looked at, nothing found").</summary>
    public IReadOnlyList<CoverageRow> NotSupported => Rows.Where(r => r.State == CoverageState.NotSupported).ToList();

    /// <summary>Families of this case that are not fully covered (PARTIAL, UNAVAILABLE, NOT_COLLECTED).</summary>
    public IReadOnlyList<CoverageRow> Gaps => Rows.Where(r => r.State is CoverageState.Partial or CoverageState.Unavailable or CoverageState.NotCollected).ToList();

    /// <summary>The matrix of a case that is not known (no case, or nothing read): every family is NOT_COLLECTED and the overall coverage is UNKNOWN.</summary>
    public static CoverageMatrix Unknown(IReadOnlyList<ParserDescriptor> parsers) =>
        Build(new CoverageInputs([], [], [], [], parsers));

    private sealed record Family(string Id, string Label, string[] ParserIds, string[] Collectors, string[] ExtraSourceTypes, string[] GapPrefixes, string Note = "");

    private static readonly Family[] Families =
    [
        new("evtx", "Jurnale de evenimente Windows (canale EVTX)", ["EvtxParser"], ["EventLogCollector"], [], ["Security "]),
        new("prefetch", "Prefetch", ["PrefetchParser"], ["PrefetchCollector"], [], []),
        new("amcache", "Amcache", ["AmcacheParser"], ["ExecutionArtifactsCollector"], [], [], "prezență în Amcache nu înseamnă execuție"),
        new("shimcache", "ShimCache (AppCompatCache)", ["SystemHiveExecutionParser"], ["ExecutionArtifactsCollector"], [], [], "citit de același parser ca BAM"),
        new("bam", "BAM", ["SystemHiveExecutionParser"], ["ExecutionArtifactsCollector"], [], [], "citit de același parser ca ShimCache"),
        new("srum", "SRUM", ["SrumNetworkParser"], ["SrumCollector"], [], [], "se citește doar utilizarea rețelei per aplicație"),
        new("registry", "Hive-uri de registru (NTUSER, SOFTWARE)", ["UserHiveParser", "SoftwareHiveParser"], ["ExecutionArtifactsCollector"], [], []),
        new("services", "Servicii și drivere (hive SYSTEM)", ["ServicesParser"], [], [], []),
        new("usn", "Jurnal USN", ["UsnJournalParser"], [], [], []),
        new("lnk_jumplist", "LNK și Jump Lists", ["LnkParser", "JumpListParser"], [], [], []),
        new("usb", "Istoric dispozitive USB", ["UsbDevicesParser"], [], [], []),
        new("network_profiles", "Profiluri de rețea", [], [], [], [], "niciun parser pentru profilurile de rețea"),
        new("network_capture", "Captură de rețea (pcapng)", ["PcapngParser"], [], [], []),
        new("browser", "Istoric browser (Chromium, Firefox)", ["BrowserHistoryParser", "FirefoxHistoryParser"], [], [], []),
        new("tasks", "Sarcini programate", ["ScheduledTaskParser"], [], [], []),
        new("live_state", "Starea live (procese, conexiuni, rulare automată)", [], ["LiveStateCollector"], ["live_snapshot"], [], "analizor propriu, fără parser"),
    ];

    public static CoverageMatrix Build(CoverageInputs inp)
    {
        var rows = Families.Select(f => Row(f, inp)).ToList();

        bool anything = inp.Evidence.Count > 0 || inp.Collection.Count > 0 || inp.Parsing.Count > 0;
        var supported = rows.Where(r => r.State != CoverageState.NotSupported).ToList();
        if (!anything || supported.Count == 0)
            return new CoverageMatrix(rows, OverallCoverage.Unknown, "nicio sursă cunoscută în caz (nici probe, nici colectări, nici parsări): acoperirea nu poate fi evaluată");

        int collected = supported.Count(r => r.State == CoverageState.Collected);
        int partial = supported.Count(r => r.State == CoverageState.Partial);
        double score = (collected + 0.5 * partial) / supported.Count;
        string counts = $"{collected} colectate, {partial} parțiale, {supported.Count(r => r.State == CoverageState.Unavailable)} indisponibile, " +
                        $"{supported.Count(r => r.State == CoverageState.NotCollected)} necolectate din {supported.Count} familii suportate";
        int unsupported = rows.Count - supported.Count;
        string tail = unsupported > 0 ? $"; {unsupported} familii nesuportate de aplicație" : "";
        if (collected == supported.Count) return new CoverageMatrix(rows, OverallCoverage.Full, "toate familiile suportate sunt colectate (" + counts + ")" + tail);
        if (score < MinimalBelow) return new CoverageMatrix(rows, OverallCoverage.Minimal, counts + tail);
        return new CoverageMatrix(rows, OverallCoverage.Partial, counts + tail);
    }

    private static CoverageRow Row(Family f, CoverageInputs inp)
    {
        var registered = f.ParserIds.Where(id => inp.Parsers.Any(d => d.ParserId == id)).ToList();
        string status = ParserStatusOf(f, inp.Parsers, registered);
        CoverageRow R(CoverageState s, string reason) => new(f.Id, f.Label, s, reason, status, f.ParserIds);

        bool ownAnalyzer = f.ExtraSourceTypes.Length > 0;
        if (registered.Count == 0 && !ownAnalyzer) return R(CoverageState.NotSupported, f.Note.Length > 0 ? f.Note : "aplicația nu are parser pentru această familie");

        var descriptors = inp.Parsers.Where(d => registered.Contains(d.ParserId)).ToList();
        var ev = inp.Evidence.Where(e => descriptors.Any(d => d.Accepts(e)) || f.ExtraSourceTypes.Contains(e.SourceType)).ToList();
        var runs = inp.Collection.Where(c => f.Collectors.Contains(c.Collector)).ToList();
        var prs = inp.Parsing.Where(p => registered.Contains(p.Parser) && p.Status != EvidenceStatus.SkippedByDesign).ToList();
        var famGaps = inp.Gaps.Where(g => f.GapPrefixes.Any(p => g.Artifact.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                                         && g.Status is EvidenceStatus.Partial or EvidenceStatus.NotAvailable or EvidenceStatus.Failed).ToList();

        if (ev.Count == 0)
        {
            var bad = runs.Where(c => c.Status is EvidenceStatus.Failed or EvidenceStatus.NotAvailable).ToList();
            if (bad.Count > 0)
                return R(CoverageState.Unavailable, string.Join("; ", bad.Select(c => $"{c.Collector}: {c.Status.ToSpec()}" + (c.Errors.Length > 0 ? $" ({Trim(c.Errors)})" : ""))));
            if (runs.Count > 0)
                return R(CoverageState.Unavailable, $"colectorul {runs[0].Collector} a rulat, dar nu a produs această sursă (lipsa sursei nu înseamnă că activitatea nu a avut loc)");
            return R(CoverageState.NotCollected, "sursa nu a fost colectată sau importată în acest caz");
        }

        CoverageRow Result;
        if (ownAnalyzer && registered.Count == 0)
            Result = R(CoverageState.Collected, $"{ev.Count} fotografii ale stării live, citite de analizorul propriu");
        else if (prs.Count == 0)
            Result = R(CoverageState.Partial, $"{ev.Count} probe prezente, dar fără niciun rezultat de parser (neparsate)");
        else
        {
            int ok = prs.Count(p => p.Status == EvidenceStatus.Success);
            int part = prs.Count(p => p.Status == EvidenceStatus.Partial);
            int empty = prs.Count(p => p.Status == EvidenceStatus.Empty);
            int failed = prs.Count(p => p.Status is EvidenceStatus.Failed or EvidenceStatus.NotAvailable);
            int parsedEvidence = prs.Select(p => p.EvidenceId).Distinct(StringComparer.Ordinal).Count(id => ev.Any(e => e.EvidenceId == id));
            int unparsed = Math.Max(0, ev.Count - parsedEvidence);
            if (failed == prs.Count)
                Result = R(CoverageState.Unavailable, $"toate parsările au eșuat ({failed}): {Trim(prs.Select(p => p.Error).FirstOrDefault(e => e.Length > 0) ?? "fără detaliu")}");
            else if (empty == prs.Count)
                Result = R(CoverageState.Partial, "sursa a fost citită, dar nu conține înregistrări: asta nu dovedește absența activității");
            else if (part == 0 && empty == 0 && failed == 0 && unparsed == 0)
                Result = R(CoverageState.Collected, $"{ok} probe parsate");
            else
            {
                var bits = new List<string>();
                if (ok > 0) bits.Add($"{ok} parsate");
                if (part > 0) bits.Add($"{part} parțiale");
                if (empty > 0) bits.Add($"{empty} fără înregistrări");
                if (failed > 0) bits.Add($"{failed} eșuate");
                if (unparsed > 0) bits.Add($"{unparsed} neparsate");
                Result = R(CoverageState.Partial, string.Join(", ", bits));
            }
        }

        if (Result.State == CoverageState.Collected)
        {
            if (famGaps.Count > 0)
                return R(CoverageState.Partial, "sursa este colectată, dar auditul a fost incomplet: " + string.Join("; ", famGaps.Take(3).Select(g => g.Artifact)));
            var badRun = runs.FirstOrDefault(c => c.Status is EvidenceStatus.Partial or EvidenceStatus.Failed or EvidenceStatus.NotAvailable);
            if (badRun is not null)
                return R(CoverageState.Partial, $"colectorul {badRun.Collector}: {badRun.Status.ToSpec()}" + (badRun.Errors.Length > 0 ? $" ({Trim(badRun.Errors)})" : ""));
        }
        return Result;
    }

    private static string Trim(string s) => s.Length <= 140 ? s : s[..140] + "…";

    private static string ParserStatusOf(Family f, IReadOnlyList<ParserDescriptor> parsers, List<string> registered)
    {
        if (registered.Count == 0) return f.ExtraSourceTypes.Length > 0 ? "analizor propriu" : "fără parser";
        var worst = parsers.Where(d => registered.Contains(d.ParserId)).Select(d => d.Status).DefaultIfEmpty(ParserMaturity.Validated).Max();
        return worst.ToString().ToUpperInvariant();
    }
}
