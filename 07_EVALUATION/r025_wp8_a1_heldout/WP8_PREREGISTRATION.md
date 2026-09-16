# WP-8 pre-registration — committed before running the held-out measurement

Per requirement 1: the threshold and the result that would leave the
default alone, written down before the measurement runs, on the same
principle as r024 WP-3 (a prediction recorded after the fact is
indistinguishable from a rationalisation).

## Baseline this measures against (r025 WP-11's corrected numbers, not r024's)

`heldout.json`, graph off, corrected metric (WP-11 Phase C):
**n=30, n_measurable=27, context_recall = 4/27 = 0.1481.**
r024's own 0.77/0.23/0.23 (n=30, no `n_measurable` distinction) is
superseded and must not be used as the comparison point — that number
included 3 tautological 1.0s.

## Dev result this is validating

r024 WP-1 Phase B: A1 (`ranking_arm='fused_score'`) moved dev context_recall
0.000 → 0.375 (0/8 → 3/8), the clear winner among A1-A4.

## Threshold, fixed now

- **Confirms dev, flip the default:** A1's held-out context_recall
  (measurable) rises to **6/27 (0.222) or higher** — an increase of at
  least 2 measurable cases over the 4/27 baseline.
- **Disconfirms dev, leave the flag off:** A1's held-out context_recall is
  **4/27 or lower** — zero or negative movement.
- **Inconclusive, reported as such, flag stays off pending more data:** a
  move of exactly +1 measurable case (5/27, 0.185). At this n, a single
  case is inside the noise band this session has used elsewhere (r024 WP-5
  used ±1 case as the noise threshold at n=8; the same ±1 convention is
  applied here at n=27, not scaled, and that choice is stated rather than
  hidden).

candidate_recall is expected to stay unchanged (r024 WP-1 confirmed
ranking arms never touch candidate generation) and is reported as a sanity
check, not a decision criterion.

## What would make this dev result look like it was tuned on held-out instead

Adjusting the threshold above after seeing the held-out number, or running
A1 more than once with different parameters until one variant clears the
bar. Neither happens in this package: A1 is exactly `RANKING_ARM_FUSED_SCORE`
as implemented in r024, unmodified, run once against `heldout.json`.
