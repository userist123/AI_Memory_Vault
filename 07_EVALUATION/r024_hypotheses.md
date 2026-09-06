# r024 hypothesis registry

Written and committed BEFORE any WP-1 or WP-5 measurement is run, per WP-3's
requirement: a prediction recorded after the fact is indistinguishable from a
rationalisation. Both packages tune on `dev.json` only
(`07_EVALUATION/heldout_retrieval_benchmark_v2/dev.json`, 12 cases: 2
`unanswerable`/abstain, 2 `one_hop_graph_expansion`, 8 ordinary non-abstain
non-graph cases). `heldout.json` stays untouched by both packages.

Measured state this registry starts from (given, not re-derived): candidate
recall 0.77, context recall 0.23, n=30 (R016, on `heldout.json`, graph off).
Graph arms: ON made 2 cases worse, 0 better — expansion stays OFF and is out
of scope for both WP-1 and WP-5.

---

## WP-1 — the ranking bottleneck

### Hypothesis (Phase A: attribution)

**Claim:** among held-out cases where the gold note was a candidate
(`fused_ranking`) but absent from the final context, the dominant loss
category is `ranked out` — the gold note's position in the final,
RelevanceScorer-sorted order falls at or beyond `page_size` — not
`budget-truncated`, `filtered`, or `lost in progressive disclosure`.

**Mechanism:** `RelevanceScorer.score()`
(`03_IMPLEMENTATION/packages/retrieval/context/relevance_scoring.py:32`)
computes `final = (overlap_ratio + confidence) / 2` with no tokeniser, no
stopword removal, no IDF weighting, and `confidence` is epistemic metadata
carrying no relation to the query — it contributes exactly half the score
regardless of match quality. A relevant note with `confidence: unknown`
cannot score above 0.5 no matter how well it matches; an irrelevant note
with `confidence: high` can reach 0.45 on zero overlap. This overwrites
`candidate_generation.py`'s `fused_score` (BM25 + entity RRF), which nothing
downstream reads (`grep -rn "fused_score" --include='*.py' .` outside
`candidate_generation.py` itself returns nothing). Structurally, I expect
this to push a material fraction of genuinely relevant candidates past
`page_size` (10 in the R016 configuration), independent of their true
relevance, because their position is decided by a formula that is at most
weakly correlated with relevance.

I looked at the surrounding pipeline before predicting this (not just the
brief's framing): the default disclosure level is `metadata`, and
`ProgressiveDisclosure.metadata_only()` counts *one unit per note* against
`budget.check_budget()`, whose default hard byte limit is 32768 — so
truncation inside disclosure would need on the order of 32,000+ candidates
to fire, far beyond the 200-candidate ceiling. `pack_builder.build()`'s
`apply_degradation()` runs on `page_results` only (already ≤ `page_size`
items), sorts by a `relevance` key that metadata-only entries never carry
(defaults to 0 for all, so the sort is a no-op over an already-short list),
and only drops entries once serialized size exceeds the hard budget, which
metadata-sized entries are very unlikely to approach at this scale. Neither
mechanism looks capable of accounting for a 54-point gap between candidate
and context recall at the numbers in section 0. I could be wrong about this
reading — that is exactly what Phase A checks empirically rather than by
argument.

**Confirming metric:** the attribution distribution itself — count of
losses per category (`ranked out` / `budget-truncated` / `filtered` /
`lost in progressive disclosure`), on dev.json's 8 ordinary non-abstain
non-graph cases, each case classified by exactly one category.

**Expected value:** `ranked out` accounts for at least 5 of the 8 cases'
losses (a clear majority), because the mechanism above is structural, not
incidental, and nothing else in the pipeline was found to have a comparably
severe defect.

**Falsifying result:** if `budget-truncated` + `filtered` +
`lost in progressive disclosure` together account for a majority of the
losses, that falsifies "ranking is the dominant loss" — the mechanism above
would be real but not the main driver, and Phase B's arms (which only touch
ranking) would be the wrong intervention.

**What would make me abandon Phase B:** exactly the brief's abort criterion,
restated as a decision I commit to now rather than after seeing results: if
Phase A does not show `ranked out` as the dominant category, I stop, report
the actual distribution, and do not run Phase B "to justify having planned
it." A registry that lets me run Phase B anyway after an unfavourable Phase A
is not a registry.

### Hypothesis (Phase B: arms — conditional on Phase A confirming ranking is dominant)

**Claim:** A1 (rank by `fused_score` instead of re-scoring) improves context
recall on dev.json's ordinary cases relative to baseline, because it removes
the confidence-dominated re-score entirely in favour of a signal that is at
least partially query-derived.

**Mechanism:** same as above, inverted — replacing a ~50%-confidence,
no-IDF score with the already-computed BM25+entity fusion should reduce the
number of relevant-but-low-confidence notes pushed past `page_size`.

**Confirming metric:** context recall per arm on dev.json, plus the same
metric reported for A2 (confidence removed from RelevanceScorer), A3
(confidence as tie-break only) and A4 (A1 + whichever of A2/A3 the
measurement favours) — each reported independently, per the brief's
requirement that two arms differ in exactly one variable and that
arms making things worse are reported too, not filtered out.

**Expected value:** I do not commit to a specific winner among A1-A4 before
Phase A's result is known — the brief itself withholds that decision until
Phase A's distribution is in hand, and doing otherwise would be predicting
past evidence I do not yet have. I do commit to: at least one arm moves
context recall on dev.json by more than 1 case out of 8 (the smallest
possible non-noise movement at this n), given the mechanism is structural
rather than incidental.

**Falsifying result:** all four arms within ±1 case of baseline. That would
mean the located mechanism, though real, is not the dominant lever on this
dev set, and no arm should be recommended for flipping the default on.

**What would make me abandon the package:** no arm clears the ±1-case bar on
dev.json. I would report all four results (including the ones that made
things worse), recommend leaving every flag at its current default, and stop
— not retune, not add a fifth arm to search for a win.

---

## WP-5 — corpus dilution

### Hypothesis

**Claim:** excluding the 394 policy-lesson notes from the index (B2) will
**not** move context recall beyond noise, relative to baseline (B1).

**Mechanism:** the given measured state already shows these notes are
*under*-represented at top-10 (17%) relative to their candidate-level share
(42% of a 200-candidate set) and their share of the corpus (46%). That
gap — from 46% of the corpus, to 42% of candidates, to 17% of top-10 — says
the existing ranking mechanism (BM25's IDF term, which penalises tokens
common across near-duplicate documents) is already correctly deprioritising
them where they do not help a specific query. If the mechanism already
suppresses them at the ranking stage, removing them from the index earlier
should not change which notes end up in the final context for a typical
query, because they were already losing to more distinctive content by the
time it mattered.

The genuine countervailing mechanism, which is why this is not asserted
without measuring: `candidate_limit` is a hard cap (200). If a specific
query's true signal is weak enough that many near-duplicate policy-lesson
notes individually out-score a genuinely relevant document on raw lexical
overlap (plausible for a corpus where 394 documents are 644-648 characters
of similar template text), the candidate SLOTS they occupy could be votes
that would otherwise have gone to a different, relevant note, even while
each individual policy-lesson note is himself outranked at the final
disclosure stage. That failure mode would show up as a candidate-recall
loss specifically, which is why requirement 1 (report both candidate AND
context recall per arm) is load-bearing, not a formality.

**Confirming metric:** candidate recall and context recall for B1
(baseline), B2 (excluded from index) and B3 (retained, per-type candidate
cap), on the same dev.json cases.

**Expected value:** B2 and B3 within noise of B1 on both metrics. At n=8
ordinary dev cases, "noise" is defined as ≤1 case moved either direction —
anything larger is a real signal at this sample size, not a rounding
artefact.

**Falsifying result:** B2 or B3 changes candidate recall or context recall
by more than 1 case, in either direction, without an equal and opposite
change in the other metric (a real trade-off, not noise cancelling itself
out). Requirement 1 exists precisely so a recall gain on one axis paired
with a loss on the other cannot be reported as an unqualified improvement.

**What would make me abandon the package (already given, restated as a
commitment):** if no arm moves context recall beyond the ±1-case band,
I report that the policy-lesson notes are harmless to retrieval at their
current 46% share, stop, and do not propose any change to the corpus or the
index. This is the brief's own abort criterion; I record it here so it is
not decided only after seeing which way the number leans. A recommendation
to keep them out of the index and a recommendation to delete them from the
vault are treated as two different findings regardless of which arm wins —
requirement 2 is honoured explicitly in whichever report follows.

---

## What would make either package look wrong in hindsight, and why I am
## recording the prediction anyway

Both hypotheses could be wrong for reasons the mechanism section did not
anticipate: dev.json's 8 ordinary non-abstain cases is small enough that a
single unusual case could dominate the distribution in either package, and
neither hypothesis has been checked against the actual RelevanceScorer
output on this corpus yet — only against reading the source. That is the
point of writing this down now: if Phase A shows losses split evenly across
categories, or B2 clearly improves recall, this document is the record that
the alternative was considered and rejected on stated reasoning, not
discovered and then presented as if it had been the plan all along.
