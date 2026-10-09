using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>One statement in a column, with the basis it rests on (a record reference, a reason, or why it cannot be shown).</summary>
public sealed record KtdStatement(string Text, string Basis);

/// <summary>
/// „Ce știm / Ce suspectăm / Ce nu putem demonstra” (UX contract §8, U8). The rule that puts a statement in a column is this one, and the tests are written on it:
/// <list type="bullet">
/// <item><b>CE ȘTIM</b>: (1) every cited record of the finding (<see cref="EvidenceRef"/>: what the record shows, with EvidenceId + locator); (2) the finding's own claim,
/// only when it has evidence AND is observed (classification Direct or BenignKnown) or the verifier marked it VERIFIED; (3) the observed steps of a combined sequence;
/// (4) contradicting evidence that was found (a known fact that weakens the claim). A claim without evidence is never listed here.</item>
/// <item><b>CE SUSPECTĂM</b>: the finding's own claim when it has evidence but is not observed (classification Correlated, Candidate or Unproven) and was not contradicted or rejected,
/// with its classification reason; and every alternative explanation that the evidence does not exclude.</item>
/// <item><b>CE NU PUTEM DEMONSTRA</b>: the missing evidence, the limits of the finding (what its kind of evidence cannot prove: who started it, intent, attribution), the sequence steps that were
/// not observed (WP14b: „pas neobservat”, never „absent”), the evidence gaps of the sources the finding rests on, a claim without evidence, and a claim the verifier CONTRADICTED or REJECTED.</item>
/// </list>
/// Nothing here is a score; every statement says what it rests on.
/// </summary>
public sealed record KnowThinkDontKnow(IReadOnlyList<KtdStatement> Know, IReadOnlyList<KtdStatement> Think, IReadOnlyList<KtdStatement> DontKnow)
{
    public const string KnowHeading = "CE ȘTIM";
    public const string ThinkHeading = "CE SUSPECTĂM";
    public const string DontKnowHeading = "CE NU PUTEM DEMONSTRA";

    public static KnowThinkDontKnow Build(Finding f, EvidenceContext ctx)
    {
        var know = new List<KtdStatement>();
        var think = new List<KtdStatement>();
        var dont = new List<KtdStatement>();
        var verdict = f.Verification.State;
        bool refuted = verdict is StandardState.Contradicted or StandardState.Rejected;
        bool hasEvidence = f.SupportingEvidence.Count > 0;
        string claim = f.Title;
        string reason = f.ClassificationReason.Length > 0 ? f.ClassificationReason : WhyExplainer.NoReasoning;

        if (refuted)
            dont.Add(new($"{claim} ({StateLabels.Label(verdict).ToLowerInvariant()} de verificare)", $"{StateLabels.Label(verdict)}: {f.Verification.Reason}"));
        else
        {
            foreach (var r in f.SupportingEvidence.Where(r => r.Description.Length > 0))
                know.Add(new(r.Description, $"{r.EvidenceId}{(r.Locator.Length > 0 ? " · " + r.Locator : "")}"));
            if (!hasEvidence)
                dont.Add(new($"Constatarea „{claim}” nu are probe atașate", "fără probă nu se poate afirma"));
            else if (f.Classification is Classification.Direct or Classification.BenignKnown || verdict == StandardState.Verified)
                know.Insert(0, new(claim, EvidenceLevels.SummaryLine(f.SupportingEvidence.Count, f.SupportingEvidence.Select(r => r.EvidenceId).Distinct().Count())));
            else
                think.Add(new(claim, $"{StateLabels.Label(f.Status)}: {reason}"));
        }

        foreach (var c in f.ContradictingEvidence) know.Add(new("Contrazis de: " + c, "dovadă contrară găsită"));
        foreach (var a in f.AlternativeExplanations) think.Add(new(a, "explicație alternativă care nu poate fi exclusă cu probele din caz"));

        foreach (var m in f.MissingEvidence) dont.Add(new(m, "probă lipsă"));
        foreach (var l in f.Limitations.Where(l => l.Length > 0)) dont.Add(new(l, "limită a acestui tip de probă sau de afirmație"));

        if (f.Sequence is { } seq)
            foreach (var st in seq.Steps)
            {
                if (st.Observed) know.Add(new($"Pas {st.Order}: {st.Name}", $"{st.Note} (sursa: {(st.Source.Length > 0 ? st.Source : EvidenceLevels.Unknown)})"));
                else dont.Add(new($"Pas {st.Order}: {st.Name}", st.Note.Length > 0 ? st.Note : "pas neobservat"));
            }

        var sources = f.SupportingEvidence.Select(r => ctx.ItemOf(r.EvidenceId)).Where(i => i is not null)
            .SelectMany(i => new[] { i!.Source, i.SourceType }).Where(s => s.Length > 0).ToList();
        foreach (var g in ctx.Gaps)
        {
            var art = g.Artifact.Trim();
            if (art.Length == 0 || !sources.Any(s => s.Contains(art, StringComparison.OrdinalIgnoreCase) || art.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            dont.Add(new($"{g.Artifact}: {g.Reason}" + (g.Impact.Length > 0 ? $" ({g.Impact})" : ""), $"gol de probă ({g.Status.ToSpec()})"));
        }

        return new KnowThinkDontKnow(Distinct(know), Distinct(think), Distinct(dont));
    }

    private static IReadOnlyList<KtdStatement> Distinct(List<KtdStatement> l) => l.DistinctBy(s => (s.Text, s.Basis)).ToList();
}
