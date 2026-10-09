using LogAnalyzer.Dfir.Language;
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
    public string HumanFamily => LogAnalyzer.Dfir.Language.Glossary.Find(FamilyId) is { } t ? t.Human() : Family;
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
        CoverageState.Collected => Loc.T("cov.state.collected"), CoverageState.Partial => Loc.T("cov.state.partial"), CoverageState.Unavailable => Loc.T("cov.state.unavailable"),
        CoverageState.NotCollected => Loc.T("cov.state.not_collected"), _ => Loc.T("cov.state.not_supported"),
    };

    public static string Spec(OverallCoverage o) => o.ToString().ToUpperInvariant();

    public static string Label(OverallCoverage o) => o switch
    {
        OverallCoverage.Full => Loc.T("cov.overall.full"), OverallCoverage.Partial => Loc.T("cov.overall.partial"),
        OverallCoverage.Minimal => Loc.T("cov.overall.minimal"), _ => Loc.T("cov.overall.unknown"),
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

    private sealed record Family(string Id, string LabelKey, string[] ParserIds, string[] Collectors, string[] ExtraSourceTypes, string[] GapPrefixes, string NoteKey = "")
    {
        public string Label => Loc.T(LabelKey);
        public string Note => NoteKey.Length > 0 ? Loc.T(NoteKey) : "";
    }

    private static readonly Family[] Families =
    [
        new("evtx", "cov.family.evtx", ["EvtxParser"], ["EventLogCollector"], [], ["Security "]),
        new("prefetch", "cov.family.prefetch", ["PrefetchParser"], ["PrefetchCollector"], [], []),
        new("amcache", "cov.family.amcache", ["AmcacheParser"], ["ExecutionArtifactsCollector"], [], [], "cov.note.amcache"),
        new("shimcache", "cov.family.shimcache", ["SystemHiveExecutionParser"], ["ExecutionArtifactsCollector"], [], [], "cov.note.shimcache"),
        new("bam", "cov.family.bam", ["SystemHiveExecutionParser"], ["ExecutionArtifactsCollector"], [], [], "cov.note.bam"),
        new("srum", "cov.family.srum", ["SrumNetworkParser"], ["SrumCollector"], [], [], "cov.note.srum"),
        new("registry", "cov.family.registry", ["UserHiveParser", "SoftwareHiveParser"], ["ExecutionArtifactsCollector"], [], []),
        new("services", "cov.family.services", ["ServicesParser"], [], [], []),
        new("usn", "cov.family.usn", ["UsnJournalParser"], [], [], []),
        new("lnk_jumplist", "cov.family.lnk_jumplist", ["LnkParser", "JumpListParser"], [], [], []),
        new("usb", "cov.family.usb", ["UsbDevicesParser"], [], [], []),
        new("network_profiles", "cov.family.network_profiles", [], [], [], [], "cov.note.network_profiles"),
        new("network_capture", "cov.family.network_capture", ["PcapngParser"], [], [], []),
        new("browser", "cov.family.browser", ["BrowserHistoryParser", "FirefoxHistoryParser"], [], [], []),
        new("tasks", "cov.family.tasks", ["ScheduledTaskParser"], [], [], []),
        new("live_state", "cov.family.live_state", [], ["LiveStateCollector"], ["live_snapshot"], [], "cov.note.live_state"),
    ];

    public static CoverageMatrix Build(CoverageInputs inp)
    {
        var rows = Families.Select(f => Row(f, inp)).ToList();

        bool anything = inp.Evidence.Count > 0 || inp.Collection.Count > 0 || inp.Parsing.Count > 0;
        var supported = rows.Where(r => r.State != CoverageState.NotSupported).ToList();
        if (!anything || supported.Count == 0)
            return new CoverageMatrix(rows, OverallCoverage.Unknown, Loc.T("cov.reason.unknown"));

        int collected = supported.Count(r => r.State == CoverageState.Collected);
        int partial = supported.Count(r => r.State == CoverageState.Partial);
        double score = (collected + 0.5 * partial) / supported.Count;
        string counts = Loc.Format("cov.reason.counts", collected, partial, supported.Count(r => r.State == CoverageState.Unavailable), supported.Count(r => r.State == CoverageState.NotCollected), supported.Count);
        int unsupported = rows.Count - supported.Count;
        string tail = unsupported > 0 ? Loc.Format("cov.reason.tail", unsupported) : "";
        if (collected == supported.Count) return new CoverageMatrix(rows, OverallCoverage.Full, Loc.Format("cov.reason.all", counts) + tail);
        if (score < MinimalBelow) return new CoverageMatrix(rows, OverallCoverage.Minimal, counts + tail);
        return new CoverageMatrix(rows, OverallCoverage.Partial, counts + tail);
    }

    private static CoverageRow Row(Family f, CoverageInputs inp)
    {
        var registered = f.ParserIds.Where(id => inp.Parsers.Any(d => d.ParserId == id)).ToList();
        string status = ParserStatusOf(f, inp.Parsers, registered);
        CoverageRow R(CoverageState s, string reason) => new(f.Id, f.Label, s, reason, status, f.ParserIds);

        bool ownAnalyzer = f.ExtraSourceTypes.Length > 0;
        if (registered.Count == 0 && !ownAnalyzer) return R(CoverageState.NotSupported, f.Note.Length > 0 ? f.Note : Loc.T("cov.reason.no_parser"));

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
                return R(CoverageState.Unavailable, Loc.Format("cov.reason.ran_no_source", runs[0].Collector));
            return R(CoverageState.NotCollected, Loc.T("cov.reason.not_collected"));
        }

        CoverageRow Result;
        if (ownAnalyzer && registered.Count == 0)
            Result = R(CoverageState.Collected, Loc.Format("cov.reason.live", ev.Count));
        else if (prs.Count == 0)
            Result = R(CoverageState.Partial, Loc.Format("cov.reason.unparsed", ev.Count));
        else
        {
            int ok = prs.Count(p => p.Status == EvidenceStatus.Success);
            int part = prs.Count(p => p.Status == EvidenceStatus.Partial);
            int empty = prs.Count(p => p.Status == EvidenceStatus.Empty);
            int failed = prs.Count(p => p.Status is EvidenceStatus.Failed or EvidenceStatus.NotAvailable);
            int parsedEvidence = prs.Select(p => p.EvidenceId).Distinct(StringComparer.Ordinal).Count(id => ev.Any(e => e.EvidenceId == id));
            int unparsed = Math.Max(0, ev.Count - parsedEvidence);
            if (failed == prs.Count)
                Result = R(CoverageState.Unavailable, Loc.Format("cov.reason.all_failed", failed, Trim(prs.Select(p => p.Error).FirstOrDefault(e => e.Length > 0) ?? Loc.T("cov.reason.no_detail"))));
            else if (empty == prs.Count)
                Result = R(CoverageState.Partial, Loc.T("cov.reason.empty"));
            else if (part == 0 && empty == 0 && failed == 0 && unparsed == 0)
                Result = R(CoverageState.Collected, Loc.Format("cov.reason.parsed", ok));
            else
            {
                var bits = new List<string>();
                if (ok > 0) bits.Add(Loc.Format("cov.bit.parsed", ok));
                if (part > 0) bits.Add(Loc.Format("cov.bit.partial", part));
                if (empty > 0) bits.Add(Loc.Format("cov.bit.empty", empty));
                if (failed > 0) bits.Add(Loc.Format("cov.bit.failed", failed));
                if (unparsed > 0) bits.Add(Loc.Format("cov.bit.unparsed", unparsed));
                Result = R(CoverageState.Partial, string.Join(", ", bits));
            }
        }

        if (Result.State == CoverageState.Collected)
        {
            if (famGaps.Count > 0)
                return R(CoverageState.Partial, Loc.T("cov.reason.audit_incomplete") + string.Join("; ", famGaps.Take(3).Select(g => g.Artifact)));
            var badRun = runs.FirstOrDefault(c => c.Status is EvidenceStatus.Partial or EvidenceStatus.Failed or EvidenceStatus.NotAvailable);
            if (badRun is not null)
                return R(CoverageState.Partial, Loc.Format("cov.reason.collector", badRun.Collector, badRun.Status.ToSpec()) + (badRun.Errors.Length > 0 ? $" ({Trim(badRun.Errors)})" : ""));
        }
        return Result;
    }

    private static string Trim(string s) => s.Length <= 140 ? s : s[..140] + "…";

    private static string ParserStatusOf(Family f, IReadOnlyList<ParserDescriptor> parsers, List<string> registered)
    {
        if (registered.Count == 0) return f.ExtraSourceTypes.Length > 0 ? Loc.T("cov.parser.own") : Loc.T("cov.parser.none");
        var worst = parsers.Where(d => registered.Contains(d.ParserId)).Select(d => d.Status).DefaultIfEmpty(ParserMaturity.Validated).Max();
        return worst.ToString().ToUpperInvariant();
    }
}
