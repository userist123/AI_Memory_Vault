using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>
/// What the presentation models read besides the finding itself: the case's evidence items, timeline rows, parse results and evidence gaps.
/// All of it is already produced by the pipeline (or read back by the case loader); the models only look things up. A missing piece is shown
/// as unknown, never filled in.
/// </summary>
public sealed class EvidenceContext
{
    public static readonly EvidenceContext Empty = new();

    public IReadOnlyList<EvidenceItem> Items { get; init; } = [];
    public IReadOnlyList<TimelineEvent> Timeline { get; init; } = [];
    public IReadOnlyList<ParseResult> Parsing { get; init; } = [];
    public IReadOnlyList<EvidenceGap> Gaps { get; init; } = [];

    public EvidenceItem? ItemOf(string evidenceId) => Items.FirstOrDefault(i => i.EvidenceId == evidenceId);

    /// <summary>The timeline row an evidence reference points at (same evidence item, same locator), or null.</summary>
    public TimelineEvent? EventOf(EvidenceRef r) => Timeline.FirstOrDefault(e => e.EvidenceId == r.EvidenceId && e.Locator == r.Locator);

    public ParseResult? ParseOf(string evidenceId) => Parsing.FirstOrDefault(p => p.EvidenceId == evidenceId);
}
