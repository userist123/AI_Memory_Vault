# WP-8 — A1 validated on held-out; default flipped

package: WP-8 | intent: measure, then implement | status: DONE
baseline: heldout graph_off (WP-11-corrected), n=30, n_measurable=27, context_recall 4/27 = 0.1481
result: A1 context_recall 6/27 = 0.2222 (+2 measurable cases) | n: 27 measurable | decision: CONFIRMED — default flipped to fused_score

Threshold pre-registered in `WP8_PREREGISTRATION.md`, committed before this
measurement ran: confirm and flip at >=+2 measurable cases, leave alone at
<=0, report as inconclusive at exactly +1.

## Result

| arm | n | n_measurable | candidate_recall | context_recall | answer_correctness |
|---|---:|---:|---:|---:|---:|
| baseline (RelevanceScorer) | 30 | 27 | 0.7407 | 0.1481 (4/27) | 0.1481 |
| A1 (fused_score) | 30 | 27 | 0.7407 | **0.2222 (6/27)** | 0.2222 |

McNemar (skipping 3 unmeasurable pairs): `context_recall` and
`answer_correctness` both `off_only=1, on_only=3` — 1 case A1 makes worse,
3 cases A1 makes better, net +2. `candidate_recall` unchanged (0/0
discordant) — confirms ranking arms never touch candidate generation, as
r024 WP-1 established.

**+2 measurable cases meets the pre-registered CONFIRMED threshold exactly.**
Held-out confirms dev (dev: 0/8 → 3/8; held-out: 4/27 → 6/27). Per
requirement 4: **the production default is flipped.**
`MemoryController`/`search()`'s `ranking_arm` default is now
`RANKING_ARM_FUSED_SCORE`; `RANKING_ARM_BASELINE` remains fully available
via an explicit `ranking_arm=` argument (constructor or per-call) for
comparison or rollback. `RelevanceScorer.score()` still runs unchanged and
confidence still reaches the pack/trace (requirement 3) — only the sort key
changed.

## A trap this flip could have walked into, and how it was closed

Flipping a default that many tests and evaluation scripts rely on
implicitly is exactly "after changing a limit, re-run whatever DERIVES a
value from it." Checked directly, not assumed clear:

- **Full suite**: 2 of 1419 tests failed — both, and only, the tests
  asserting the *old* default's behaviour on an *unspecified* `ranking_arm`
  (`test_baseline_ranking_matches_pre_r024_relevance_scorer_order`,
  `test_ranking_arm_defaults_to_none_end_to_end_when_unspecified`). Updated:
  the first now passes `ranking_arm=RANKING_ARM_BASELINE` explicitly and
  keeps verifying baseline's own ordering is unchanged; the second now
  asserts the new default resolves to `RANKING_ARM_FUSED_SCORE`. No other
  test in the suite depended on the default, so no other test needed
  touching.
- **`run_production_arms.py` (R016, the graph-arm harness): this was the
  live risk.** Its `build()` never specified `ranking_arm`, so it would
  have silently started comparing "graph on/off + fused_score" instead of
  "graph on/off + baseline" the moment this default flipped — changing what
  R016 measures without changing a single line in R016 itself, the exact
  two-independently-correct-changes-cancelling shape r024 already hit once
  with the graph expansion budget. **Fixed by pinning
  `ranking_arm=RANKING_ARM_BASELINE` explicitly in `build()`**, with the
  reasoning recorded in the code: R016 studies graph expansion holding
  ranking fixed, deliberately, so its comparison stays valid regardless of
  what the production ranking default becomes later. Verified the pin
  works: re-ran R016 after the flip and it reproduced WP-11's committed
  numbers exactly (0.7407 / 0.1481 / 0.1481 graph_off; 1.0 / 0.10 / 0.10
  graph_on) — unchanged, as it should be.

## Supplementary check: does the graph NO-GO hold under the NEW default?

R016 itself deliberately no longer answers this (see above), so a separate
script (`graph_arms_under_new_default.py`) asks it directly: graph on/off
with `ranking_arm` left unspecified (i.e. resolving to the actual current
production default, fused_score).

| arm | context_recall | vs. R016 (pinned baseline) |
|---|---:|---|
| graph_off | 0.2222 (6/27) | matches A1's own result, as expected |
| graph_on | 0.10 (2/20 measurable) | unchanged from R016 |

McNemar: `off_only=4, on_only=0` (was `off_only=2, on_only=0` under the
pinned baseline ranking). **The graph NO-GO does not just hold under the
new default — it strengthens**: with the ranking default improved,
graph-on now costs 4 measurable cases instead of 2, for the same 0 gained.
Graph expansion stays off; nothing here argues otherwise.

## Requirements checklist

1. Threshold and disconfirming result pre-registered before running
   (`WP8_PREREGISTRATION.md`, committed in its own prior commit).
2. Both arms through `MemoryController.search()`, per class with n — see
   `wp8_heldout_report.json`'s per-class breakdown.
3. Confidence stays in the pack/trace under every arm — unchanged from
   r024 (not re-verified here beyond the existing `test_confidence_survives_
   in_pack_and_trace_under_every_arm`, which still passes with the new
   default).
4. Held-out confirmed dev at the pre-registered threshold; default flipped
   and said so, here and in code comments.
5. Re-run after WP-11 landed: this measurement used WP-11's corrected
   scoring throughout (branched from `r025/abstention-metric`, not from
   r024's stale tautology-inflated numbers).

## Forbidden — honoured

A1 was run exactly once against `heldout.json`, using the exact
`RANKING_ARM_FUSED_SCORE` arm implemented in r024, unmodified. No tuning,
no second variant tried after seeing the number.

## What remains open

- The 1-case-worse outcome under A1 (McNemar `off_only=1`) was not
  individually attributed — which specific case regressed and why is not
  investigated here, only counted.
- `graph_arms_under_new_default_report.json` is a supplementary, informal
  check, not a frozen/registered comparison the way R016 itself is; treat
  its exact numbers as directional, not a new canonical baseline.
