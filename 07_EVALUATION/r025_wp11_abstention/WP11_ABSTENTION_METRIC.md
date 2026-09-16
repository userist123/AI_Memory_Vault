# WP-11 — abstention does not exist, and the benchmark pretended it did

package: WP-11 | intent: measure, then fix | status: DONE
baseline (pre-fix): heldout ALL n=30, candidate 0.7667, context 0.2333 (R016 as reported in r024)
result: no separable signal on any of D1/D2/D3 (Phase A); metric fixed, corrected heldout ALL n=27 measurable, candidate 0.7407, context 0.1481 (Phase C)
n: 27 answerable + 3 unanswerable heldout, 20 generated nonsense | decision: no abstention mechanism built; metric no longer tautological

## The defect, confirmed

`run_production_arms.py`'s abstain branch was `correct = not (gold & context)`.
Every abstain case carries `gold_relevant_notes: []`; an empty set
intersected with anything is empty, so `not set()` is `True` always. Checked
further than the brief's own framing: **all three metrics were tautological
for these cases, not only `answer_correctness`** —
`candidate_recall`/`context_recall` use the identical `if gold else 1`
pattern, and `gold` is empty for every abstain case by construction. All
three scored 1.0 in every arm regardless of system behaviour.

## Phase A — calibration (BLOCKING), all three candidate signals measured

Measured through the real `MemoryController.search()` (graph off, matching
production default), page_size=10, on three populations: 27 answerable
heldout queries, 3 unanswerable heldout queries, and 20 fixed generated
nonsense queries (`NONSENSE_QUERIES` in `phase_a_calibration.py` — gibberish,
word salad, foreign nonsense, syntactically-query-shaped-but-empty
questions; not regenerated at runtime).

**D1 (top fused_score): not separable.**
min(answerable) = 0.0221 < max(non-answerable) = 0.0295. Nonsense queries'
mean top score (0.02873, n=20) is *higher* than answerable queries' mean
(0.02810, n=22 — see note below on why n=22 not 27); the ranges overlap
48% of their combined span against nonsense, 36% against the actual
unanswerable cases.

**A finding this measurement surfaced in passing, relevant to WP-9, not
fixed here:** the "answerable" population's score list has n=22, not 27 —
**5 of the 27 answerable heldout cases (H02, H04, H14, H15, H22) return
zero candidates at all** (`fused_ranking` empty), so no top score exists to
measure. This is the same classifier-collapse pattern r024 WP-5 found on
3 of 8 dev cases (D06-D08), now confirmed on 5 of 27 heldout cases (18.5%)
— a materially larger share on the larger set. Recorded here for WP-9,
which measures and attributes this directly; not investigated further in
this package.

**D2 (margin, top minus median candidate score): not separable, and
inverted.** min(answerable margin) = 0.0011, max(non-answerable margin) =
0.0183 — the ranges do not merely overlap, the non-answerable population's
*mean* margin (0.0169) is higher than the answerable population's mean
(0.0149). A query with no real answer looks *more* confident on this signal
than one with a real answer, on average.

**D3 (bm25/entity top-1 agreement): not separable, and inverted harder.**
Agreement rate: answerable 4.6%, unanswerable 0%, nonsense **50%**. Nonsense
queries produce agreeing bm25/entity top picks roughly 11x more often than
real answerable queries do.

**Root cause of the inversion, not just the overlap:** RRF fuses *ranks*,
not raw scores (`score(d) = Σ w_r / (k + rank_r(d))`, established in
`candidate_generation.py`, r004). Checked directly: for the nonsense query
`"xyzzy plugh frobnicate"`, the top-ranked candidate's raw bm25 score AND
raw entity score are both **0.0** — no real signal exists — yet its
`fused_score` is 0.0295, among the highest observed anywhere in this
measurement. A fully-tied, all-zero ranking still produces a complete,
deterministic rank order (tie-broken by note id, per r004's determinism
requirement), and RRF assigns that arbitrary #1 position the same reward it
would give a genuinely well-matched #1. Worse, when bm25 and entity both
degrate to identical zero-score, id-broken tie orders, they *agree* on rank
#1 by construction — explaining D3's inversion directly, not just
correlating with it.

**This is the "no threshold exists" case the brief anticipated, confirmed
for all three candidate signals, not assumed from D1 alone.** Phase B
(tuning D1/D2/D3 as thresholds) was not run: there is nothing to tune when
two of three signals are actively anti-correlated with the property they'd
need to detect.

## Phase B — not run, and why that is not a shortcut

The brief gates Phase B on "only if a separable signal exists." None of the
three candidate signals in scope exists as one — D1 fails on overlap alone;
D2 and D3 fail on overlap AND point the wrong way. Building any threshold on
top of them would not be "underpowered," it would actively reward the
behaviour it's meant to catch (declining more often would mean declining
more of the correctly-answerable queries first, since they score *lower* on
D2/D3 than the nonsense the mechanism is supposed to catch). Tuning here
would make the system worse, not weakly better; not attempted.

## Phase C — fix the metric regardless

`run_production_arms.py`: an abstain case now returns
`candidate_recall`/`context_recall`/`answer_correctness` = `"UNMEASURABLE"`
(a string sentinel, not a number) instead of a fabricated `1`.
`summarise()` excludes `UNMEASURABLE` rows from every mean and reports
`n`, `n_measurable`, `n_unmeasurable` per class and overall. `mcnemar()`
skips pairs where either arm is `UNMEASURABLE` and reports
`skipped_unmeasurable`. Per requirement 1: this is a strictly honest
downgrade, not a favourable redefinition — `UNMEASURABLE` cannot inflate
any mean the way the old `1.0` did, and the `unanswerable` class's own row
now correctly shows `candidate_recall: null` etc. rather than a number.

**Re-freeze: NOT done, and none was needed.** `heldout.json`/`dev.json`
were not touched — same 30/12 cases, same gold ids, same bytes, same
hashes. Only `run_production_arms.py` (the runner) changed. Per requirement
2, a runner change does not require a re-freeze; verified by re-running
`freeze.py` (verify mode) after this change, which still passes against
the existing recorded hashes.

## Corrected R016 baseline (supersedes r024's 0.77/0.23/0.23)

| arm | n | n_measurable | candidate_recall | context_recall | answer_correctness |
|---|---:|---:|---:|---:|---:|
| graph_off | 30 | 27 | 0.7407 | 0.1481 | 0.1481 |
| graph_on | 23\* | 20 | 1.0 | 0.1000 | 0.1000 |

\* `graph_on`'s `n=23` (not 30): the strict graph arm raises
`GraphExpansionDegraded` for 7 cases rather than silently degrading —
unrelated to this package, pre-existing behaviour, reported as-is.

McNemar (skipping the 3 unmeasurable pairs in both arms): context_recall
and answer_correctness both show `off_only: 2, on_only: 0` — graph-on made
2 cases worse, 0 better — **unchanged from r024's graph conclusion**, only
the recall figures moved (0.77→0.7407 candidate, 0.23→0.1481 context/
answer, on the corrected 27-case denominator). The graph NO-GO stands; only
the number it was measured against was wrong.

**This corrected baseline (0.7407 / 0.1481 / 0.1481, n_measurable=27) is
the one WP-8, WP-9 and WP-12 must use, not r024's 0.77/0.23/0.23.**

## Requirements checklist

1. Did not raise abstain scores by redefinition — lowered them from a
   fabricated 1.0 to an honest "not measured yet."
2. No re-freeze; runner-only change, stated above and verified by
   `freeze.py`'s own verify mode still passing.
3. No abstention behaviour was built (Phase B did not run), so there is no
   flag to gate — recorded as N/A rather than silently skipped.

## What remains open

- No abstention mechanism exists in production, and this package does not
  propose fabricating one from a non-existent signal. If the vault owner
  wants declination, it needs a genuinely different signal (e.g. an
  absolute BM25/entity raw-score floor before fusion, not a post-fusion
  rank-based score) — not designed or measured here.
- `n=20` nonsense queries is small; the inversion on D2/D3 is stark enough
  (agreement 4.6% vs 50%) that a larger sample is unlikely to reverse the
  conclusion, but the exact rate has not been re-measured at higher n.
- The RRF root-cause mechanism (ties-broken-by-id producing spuriously high
  fused scores) likely affects more than abstention — any downstream
  consumer of `fused_score` as if it meant "confidence" would have the same
  problem. Recorded here as a finding, not investigated further or fixed
  beyond this package's own scope (the abstention metric).
