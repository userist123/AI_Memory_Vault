# WP-1 Phase B — ranking arms, measured independently

Ran only because Phase A confirmed ranking as the dominant (100%, 4/4) loss
category. Predictions committed in `07_EVALUATION/r024_hypotheses.md` before
this ran: at least one arm was expected to move context recall by more than
1 of 8 cases.

## Method

Five arms, otherwise identical: same 8 dev.json ordinary cases (non-abstain,
excluding `one_hop_graph_expansion`), same corpus, same principal, same
`page_size=10`, graph expansion off and untouched throughout. The only
variable is `MemoryController(ranking_arm=...)`, which changes exactly the
sort key used in the graph-off branch — nothing about candidate generation,
filtering, or disclosure differs between arms (confirmed by candidate_recall
being identical, 0.500, across every arm below: only ordering changed).

- **baseline** — unchanged production default (`RelevanceScorer.score()`,
  50% confidence).
- **A1 `fused_score`** — rank by `candidate_trace['fused_ranking']`'s
  already-computed BM25+entity fusion; `RelevanceScorer` still runs (its
  output stays in the pack/trace, per requirement 3) but does not decide
  order.
- **A2 `no_confidence`** — rank by `overlap_ratio` alone
  (`RelevanceScorer.score_components()`, confidence weight 0).
- **A3 `confidence_tiebreak`** — rank by `(overlap_ratio, confidence, id)`:
  confidence only breaks ties between equal overlap, never contributes to
  the primary score.
- **A4 `fused_plus_best_confidence_strategy`** — A1's primary signal plus
  whichever of A2/A3 scored higher; resolved empirically below.

## Result

| arm | candidate recall | context recall | Δ context recall vs baseline |
|---|---:|---:|---:|
| baseline | 0.500 | **0.000** (0/8) | — |
| A1 fused_score | 0.500 | **0.375** (3/8) | **+3 cases** |
| A2 no_confidence | 0.500 | 0.125 (1/8) | +1 case |
| A3 confidence_tiebreak | 0.500 | 0.125 (1/8) | +1 case |
| A4 (resolved) | 0.500 | 0.375 (3/8) | +3 cases |

Full per-case rows: `phase_b_arms_report.json`.

**A2 and A3 tied exactly** (both 1/8, same case). Per the registered
resolution rule (A3 wins only if it *strictly* beats A2), a tie resolves to
A2's side: confidence contributes nothing even as a tie-break on this dev
set, so **A4 collapses to A1 itself** — reported as `resolved_as:
"fused_score alone (== A1)"` in the JSON, not silently re-run as if it were
a distinct result.

**A1 is the clear winner.** It recovers 3 of the 4 cases Phase A found
`ranked_out` at baseline (D01, D03 and one of D02/D04 by the case list;
see the JSON for the exact ids). It clears the pre-registered falsification
bar (">1 case out of 8") at 3x the minimum.

## Cases that got worse

None. Every arm's context-recall hit set is a superset of baseline's empty
set — no arm made a previously-correct case wrong. This is reported because
the brief asked for it explicitly, not because it was a close call: at
baseline context_recall=0, there was no case for an arm to break.

## Recommendation (reported separately from the change, per requirement 4)

**Flip the default ranking arm to `fused_score` (A1)** for the graph-off
path. `RelevanceScorer.score()` keeps running unchanged — its output stays
in the returned note and the trace, so confidence remains visible to any
reader — but stops deciding order.

This is a recommendation, not an action taken in this commit:
`MemoryController`'s and `search()`'s default remains `RANKING_ARM_BASELINE`
(unchanged code path) until the vault owner acts on this report, per
requirement 4 ("every change behind a flag, default OFF, until the
measurement justifies flipping it").

## What remains open

- n=8 is small; a 3-case swing is real at this n but the confidence interval
  around 0.375 is wide. `heldout.json` was not touched by this package
  (WP-1 requirement 5) and should be the confirmatory check before any
  default is actually flipped, not dev.json again.
- A4's resolution rule (tie -> A2's side) was decided before seeing that A2
  and A3 would tie exactly; it produced a degenerate case (A4 == A1) that a
  different tie-break convention could have avoided. Recorded rather than
  re-run with a different rule after the fact.
- 4 of 8 dev cases failed at candidate_recall, before ranking is even
  reached; those are unaffected by any ranking arm and are out of this
  package's scope (see Phase A).
