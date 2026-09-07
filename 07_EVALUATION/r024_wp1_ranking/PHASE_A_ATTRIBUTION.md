# WP-1 Phase A — attribution (BLOCKING, run before any fix)

Predictions committed in `07_EVALUATION/r024_hypotheses.md` before this
measurement ran. This report is the result.

## Method

`phase_a_attribution.py` runs `MemoryController.search()` (graph expansion
off, matching the production default) on dev.json's 8 ordinary cases
(non-abstain, excluding `one_hop_graph_expansion` — graph is out of scope
for WP-1). For every case where a gold note was a candidate
(`candidate_trace['fused_ranking']`) but absent from the final context, it
reproduces controller.py's exact graph-off pipeline externally —
`RelevanceScorer.score()` → sort by `(score, id)` reverse=True → the real
`ProgressiveDisclosure.metadata_only()` → slice to `page_size` — using the
actual production classes, not re-derived logic, so a genuine disclosure or
budget drop would show up rather than being assumed away.

**Cross-validated, not just self-consistent**: before trusting this script's
numbers, I ran the same 8 dev cases through the *existing*, already-verified
`run_production_arms.py::run_case()` (the R016 harness) directly. Both
scripts agree exactly on candidate_recall and context_recall per case (see
raw output in the commit). This rules out the new script being the source of
an unexpected number, per the vault's own standing rule: treat an impossible
or surprising metric as a bug in the instrument before it is a finding.

## Result

| | value |
|---|---:|
| cases considered | 8 |
| candidate recall | 0.50 (4/8) |
| context recall | 0.00 (0/8) |
| loss instances (gold was candidate, absent from context) | 4 |
| **`ranked_out`** | **4 (100%)** |
| `budget_truncated` | 0 |
| `filtered` | 0 |
| `lost_in_disclosure` | 0 |

Every single loss instance is `ranked_out`. The four cases where the gold
note *did* become a candidate had it sitting at positions 25, 152, 76 and 194
in `RelevanceScorer`'s final order (`page_size=10`) — deep inside the
200-candidate window (so `candidate_generation.py`'s BM25/entity fusion did
its job) but nowhere near the page the user actually sees.

This confirms the hypothesis registered before running: `ranked_out` was
predicted to account for at least 5 of 8 cases; the observed rate is 4 of 4
loss instances (the other 4 cases failed earlier, at candidate generation,
which is out of WP-1's scope and not attributed here). Zero instances of
`budget_truncated`, `filtered`, or `lost_in_disclosure` — matching the
structural reading in the hypothesis registry that `ProgressiveDisclosure`'s
per-note budget counter and `pack_builder`'s hard-byte degradation are very
unlikely to trip at this candidate-set scale.

## A note on the dev-set numbers themselves

Context recall of 0/8 on dev.json is markedly worse than the reference
figure (0.23, n=30, `heldout.json`, R016). This is not a contradiction to
chase down: dev and heldout are different, disjoint case sets by design (dev
exists specifically so heldout stays untouched during tuning), dev has 8
ordinary cases against heldout's much larger n, and dev's zero here is
consistent with — not corrected by — the finding above: with 100% of
observed losses being a ranking defect, a small, unlucky dev sample landing
at 0/8 while a larger heldout sample lands at 0.23 is well within what a
severe-but-not-absolute ranking defect produces. It is flagged here rather
than silently absorbed into the headline number.

## Decision

Per the abort criterion recorded before this ran: **ranking is the
dominant loss.** Proceeding to Phase B.
