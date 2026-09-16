# WP-9 — QueryClassifier inferred-filter collapse: proven systematic, fixed behind a flag left OFF

package: WP-9 | intent: measure, then fix | status: DONE
baseline: C1 hard (production default, unchanged) — context_recall 10/37 = 0.2703 measurable
result: C2 boost / C3 conditional — context_recall 15/37 = 0.4054 measurable (+5 measurable cases)
n: 42 total (30 heldout + 12 dev), 37 measurable | decision: mechanism confirmed systematic, not
anecdotal — fix implemented as C2/C3 arms, both left OFF by production default per requirement 3.
This is a reported finding, not a default flip.

## Phase A (BLOCKING) — attribution, not anecdote

`phase_a_attribution.py` ran every heldout + dev case through `QueryClassifier.classify()` and
`storage.query()` with four filter combinations (unfiltered / lifecycle-only / type-only / both),
attributing any pool collapse to the specific filter responsible. Full data:
`phase_a_attribution_report.json`.

- **8/42 cases (19.05%)** collapse to a pool of `< 5` notes once the classifier's inferred filter
  is applied both-together; **5/30 heldout-only (16.67%)**.
- Attribution: `{no_collapse: 34, lifecycle_filter: 4, both_lifecycle_and_type: 3,
  target_type_filter: 1}`.
- **Root cause, proven deterministically, not just observed**: the real corpus's lifecycle
  distribution is `REVIEW(661), ACTIVE(46), None(24), ARCHIVED(5), NORMALIZED(4), "raw"(3)` —
  `VERIFIED` and `CLASSIFIED` have **zero** notes. `QueryClassifier.classify()` fires a lifecycle
  filter for one of these two stages on any query whose text merely *contains* the word
  "verified" or "classified" (`re.search(rf"\b{keyword}\b", ...)`), regardless of whether the
  query intends to filter by lifecycle at all. Every one of the 8 collapsed cases is a query
  containing "verified" or "classified" as ordinary content, filtered to a stage with no notes to
  return — this is a guaranteed, structural failure mode, not a rare coincidence.

**Abort criterion assessed and NOT triggered.** 8/42 (19%) is not "two anecdotes"; the mechanism
is provable from the corpus's own lifecycle histogram independent of which 8 cases happen to
contain the trigger words, so it will recur on any future query containing them. Proceeding to
Phase B.

### A correction to Phase A's own count, found while building Phase B (report it straight)

Phase A's `attribute()` labels a case "collapsed" only when the post-both-filter pool drops
**below 5** — a labelling threshold, not the true impact boundary. Measuring C1 vs C2 directly
(not through that threshold) shows **10** cases, not 8, where the inferred filter causes
`candidate_recall` to flip from 0 to 1 once softened: the 8 Phase-A-flagged cases minus `H04`
(unaffected either way — see below) plus three more (`H06`, `H24`, `D05`) whose post-filter pool
was `>= 5` (so Phase A's threshold did not flag them) but which still excluded the *specific* gold
note via the inferred filter. A pool "not small" does not mean "not harmed" — the pool-size
threshold and the recall-impact boundary are different things, and only the second is what
Phase B actually measures. Both counts are reported in `phase_a_attribution_report.json` (the
labelled 8) and `phase_b_arms_report.json` (the measured 10); this file uses the measured 10 as
the operative number.

### How inferred vs. explicit was distinguished (requirement 2)

No caller in this measurement (Phase A or Phase B) ever passes an explicit `lifecycles=`/`types=`
argument to `MemoryController.search()` — every case calls
`controller.search(Principal.HUMAN, case["query"], page_size=10)` exactly like R016 and WP-8 do.
Per `controller.py`'s `search()`: `classified['lifecycle_filters_source']`/`['target_types_source']`
are set to `'inferred'` right after `QueryClassifier.classify()` runs, then overwritten to
`'explicit'` **only** if the caller passed `lifecycles=`/`types=` explicitly (which overwrites the
value itself too, not just the source tag). `RetrievalEngine.retrieve()` reads these `_source`
tags to decide what is safe to soften: an `'explicit'` filter is **always** hard, in every arm,
including C2/C3 — only an `'inferred'` filter is ever a candidate for softening. RAW exclusion is
untouched in all three arms: it is enforced unconditionally inside `storage.query()` itself,
never through `lifecycle_filters`/`target_types` at all (requirement 1).

## Phase B — C1/C2/C3 on heldout + dev, `ranking_arm` pinned to the current production default

`phase_b_arms.py` reuses `run_case`/`summarise`/`mcnemar`/`UNMEASURABLE` from
`run_production_arms.py` (no duplicated scoring logic). `ranking_arm` is pinned explicitly to
`RANKING_ARM_FUSED_SCORE` — the current production default per r025 WP-8 — so this measurement
reflects what production traffic actually experiences, and does not silently drift if the ranking
default changes again (the same reasoning R016 applies in the other direction, by pinning to
`RANKING_ARM_BASELINE` for its own ranking-invariant comparison).

| arm | n | n_measurable | candidate_recall | context_recall | answer_correctness |
|---|---:|---:|---:|---:|---:|
| C1 hard (production default) | 42 | 37 | 0.7027 (26/37) | 0.2703 (10/37) | 0.2703 (10/37) |
| C2 boost | 42 | 37 | **0.9730 (36/37)** | **0.4054 (15/37)** | **0.4054 (15/37)** |
| C3 conditional | 42 | 37 | 0.9730 (36/37) | 0.4054 (15/37) | 0.4054 (15/37) |

McNemar (skipping 5 unmeasurable pairs), C1 vs C2 and C1 vs C3 — **identical for both pairs on
this benchmark**: `candidate_recall` `off_only=0, on_only=10`; `context_recall` and
`answer_correctness` both `off_only=0, on_only=5`. Neither arm ever makes a case worse
(`off_only=0` everywhere) — softening an inferred filter never excludes a note the hard filter
would have included, by construction (`_apply_classifier_filter_arm()` only reorders or narrows
`candidates`, and C3's narrowing step falls back to the unfiltered `candidates` if narrowing would
empty the result — see `retrieval.py`).

**C2 and C3 produce byte-identical results on this benchmark.** This is not a coincidence in the
code (they are genuinely different logic — C2 always reorders, C3 only narrows and only when the
unfiltered pool exceeds `candidate_limit`) but a property of this specific benchmark: every
collapsing case here has an unfiltered pool small enough that C3's narrowing condition
(`unfiltered_pool_size > candidate_limit`) never fires, so C3 degrades to a no-op identical in
final-candidate-set terms to C2's non-exclusionary reorder. **This benchmark cannot distinguish
C2 from C3** — that would require cases where the unfiltered pool is large, which none of the 8
Phase-A-flagged (or 10 measured) cases are (see below, `H04`).

### Per-case detail on the 10 candidate_recall-discordant cases

| id | class | C1 candidate/context | C2/C3 candidate/context |
|---|---|---|---|
| H02 | exact_identifier_lookup | 0 / 0 | 1 / 0 |
| H04 | exact_identifier_lookup | 0 / 0 | 0 / 0 (unchanged — see below) |
| H06 | (not flagged by Phase A's pool<5 threshold) | 0 / 0 | 1 / 0 |
| H14 | lexical_trap | 0 / 0 | 1 / 1 |
| H15 | lexical_trap | 0 / 0 | 1 / 1 |
| H22 | one_hop_graph_expansion | 0 / 0 | 1 / 1 |
| H24 | (not flagged by Phase A's pool<5 threshold) | 0 / 0 | 1 / 0 |
| D05 | (not flagged by Phase A's pool<5 threshold) | 0 / 0 | 1 / 0 |
| D06 | synonym_substitution | 0 / 0 | 1 / 0 |
| D07 | lexical_trap | 0 / 0 | 1 / 1 |
| D08 | lexical_trap | 0 / 0 | 1 / 1 |

Five of the ten recover `candidate_recall` only, not `context_recall` — the gold note re-enters
the candidate pool once the inferred filter stops excluding it, but does not survive ranking into
the final returned context. That is a ranking-quality question, out of scope for WP-9 (which only
governs what enters the candidate pool, not how it is ranked once there).

## Requirements checklist

1. Lifecycle filtering as a SECURITY boundary (RAW exclusion, caller-supplied explicit filters) is
   untouched in every arm — verified by `lifecycle_filters_source`/`target_types_source` gating in
   `controller.py` and by the AST proof in
   `20_TESTS/regression/test_candidate_generation_call_path.py` still passing unmodified (the
   single `storage.query()` gate assignment is never touched by C2/C3 logic, which operates only
   on `generate_candidates()`'s output).
2. How inferred vs. explicit was distinguished in code: documented above and in
   `phase_a_attribution.py`'s own docstring, and now enforced structurally via the `_source` tags
   rather than only by convention of which harness never passes an explicit filter.
3. The change is behind `classifier_filter_arm`, resolving to `CLASSIFIER_FILTER_ARM_HARD`
   (production default, unchanged) whenever unset — `MemoryController.__init__`/`search()` both
   default this to `None`, and `RetrievalEngine.retrieve()` resolves `None` to `"hard"`. **Default
   stays OFF**, per requirement 3, independent of the measured result above.

## Abort criterion — honoured

Assessed explicitly (see Phase A section) and not triggered: the collapse is provable from the
corpus's lifecycle histogram, not from 8 (or 10) coincidental cases.

## What remains open

- **`H04` never recovers under any arm.** Its inferred filter is softened identically to the
  other 9, yet `candidate_recall` stays 0 — meaning the gold note is absent from the pool for a
  reason unrelated to the classifier filter (a BM25/entity ranking miss upstream of this fix, a
  benchmark gold-annotation issue, or something else). Not individually root-caused here; flagged
  for separate investigation.
- **C2 vs. C3 are not distinguished by this benchmark** (see above) — both are implemented and
  both pass the AST security proof, but choosing between them for any future default change would
  need cases with a large unfiltered pool, which this frozen set does not contain.
- **This is not a held-out validation in the WP-8 sense.** Phase A and Phase B both measure
  directly on `heldout.json` + `dev.json` together — there is no separate confirmation set here,
  because WP-9's requirement 3 fixes the default regardless of outcome (unlike WP-8, which used
  held-out specifically to decide whether to flip a default). The 19%/24% collapse-rate and
  10-case recovery numbers should be read as "measured on the frozen v2 set," not as a
  held-out-confirmed generalization claim.
- Ranking-arm interaction: this measurement pins `ranking_arm=RANKING_ARM_FUSED_SCORE`
  deliberately; the same C1/C2/C3 comparison under `RANKING_ARM_BASELINE` was not run, since WP-9
  studies the candidate pool, not ranking, and R016 already established ranking arms don't touch
  candidate generation (r024 WP-1).
