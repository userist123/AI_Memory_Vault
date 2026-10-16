---
id: 0c9f1a5e-6f4a-4a32-9a21-7c1f2b8d4e30
type: procedure
lifecycle: REVIEW
category: evaluation.ranking
tags: ['preregistration', 'ranking', 'benchmark-v3', 'measured']
created: "2026-10-07"
updated: "2026-10-07"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-10-07: every prior benchmark figure was measured on RANKING_ARM_BASELINE, not the production default'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Ranking arms on benchmark v3 — preregistration

**Committed before any arm was run.** The decision rule below is fixed in the
same commit as the hypotheses and does not change after results exist.

## Why this exists: two corrections

**Every benchmark figure this project quotes was measured on the wrong arm.**

`MemoryController.search()` takes a `ranking_arm`. Since r025 the production
default is `RANKING_ARM_FUSED_SCORE` — ranking by the fused BM25 + entity RRF
score (`controller.py:942`). `RANKING_ARM_BASELINE` is the pre-r024 key, which
ranks by `RelevanceScorer`'s blend, `(overlap_ratio + confidence) / 2`.

Both of the measurements the project has been reasoning from passed
`RANKING_ARM_BASELINE` explicitly:

- the v3 arm runner (39/130 at `HUMAN`, `page_size=10`);
- the loss funnel (21/130 at `AI_AGENT`, `page_size=5`, floor on), and with it
  the 71.56% `PAGINATION_CUT` finding and the verdict "adopt a reranker".

So the conclusion that retrieval is rank-limited rests on a ranking no caller
uses by default. That has to be remeasured before anything is built on it.

**The second correction is mine.** Earlier today I said half of production's
ranking weight is `confidence`, a field unrelated to the query. That is true of
the BASELINE arm, not of the production default. I read `RelevanceScorer` and
the r024 comment and stopped before reading `_ranking_key_fn`.

**What stands:** `retrieval/hybrid_retrieval.py::tokenize` — the ASCII-only
tokenizer that turns `învățare` into `['nv', 'are']` — is called only from
`interfaces/benchmarks/retrieval_ab.py`, an offline A/B script. It is not in the
`search()` path. Replacing a tokenizer that production never calls is why the
tokenizer experiment found zero changed cases across 130 queries, in all three
arms. The defect is real; its blast radius is a benchmark script.

## Hypotheses

- **H-RANK-1.** The production default (`fused_score`) and `baseline` differ on
  `context_recall` at the production operating point by at least 5 discordant
  cases out of 130. *Direction unstated on purpose: either arm may win, and the
  interesting outcome is that the project has been measuring the weaker one.*
- **H-RANK-2.** `no_confidence` (overlap only) beats `baseline`, which carries
  the same overlap signal diluted 50% by `confidence`.
- **H-RANK-3.** The `PAGINATION_CUT` share of misses — 71.56% on `baseline` —
  drops by at least 10 percentage points on the best-performing arm. If it does
  not, the rank-limited diagnosis holds independently of the arm.

## Arms

All five arms the controller already supports, nothing new implemented:
`baseline`, `fused_score`, `no_confidence`, `confidence_tiebreak`,
`fused_plus_tiebreak`.

Operating point, stated because the project has confused two of them before:
`Principal.AI_AGENT`, `page_size=5`, agent lifecycle floor **on**, graph
expansion off, cognitive modules off. The same arms are also run at
`Principal.HUMAN`, `page_size=10`, floor off, for comparability with the 39/130
figure.

## Mandatory control: every arm must be shown to be an arm

Three experiments in this project reported null results from arms that were not
arms: `spreading_activation` at a budget that adds nothing, `reasoning` and
`executive` writing only to a trace, and the tokenizer patched outside the code
path. A null result is only evidence if the mechanism was live.

So, before any arm comparison is reported:

1. **Sabotage.** The ranking key of each arm is replaced by a constant. If the
   returned order does not change, that arm does not drive ranking and its
   result is void.
2. **Distinctness.** At least one case must differ between any two arms whose
   keys differ. Two arms that agree on all 130 cases are reported as
   *indistinguishable at this sample size*, never as "no difference".
3. **Determinism.** Three runs per arm, identical results required. Ties break
   on note id in every arm by construction, so variation would mean something
   else moved.

## Decision rule

Let *d* be discordant cases on `context_recall` against the current production
default (`fused_score`), at the production operating point.

- **Change the default** to arm X only if X wins at least 8 cases, loses at most
  2, and exact two-sided McNemar gives p < 0.05.
- **Keep the default** otherwise, and say so plainly, including when an arm is
  nominally ahead but inside noise.
- Latency is not a criterion here: all arms sort an already-computed list.

## Reranker question, settled by this experiment

The reranker was recommended on the strength of `PAGINATION_CUT` = 71.56% and a
median miss rank of 21, measured on `baseline`. After remeasuring:

- if the best arm leaves `PAGINATION_CUT` above 50% and the oracle ceiling at
  k=200 stays far above achieved recall, the reranker case holds and the next
  step is its cost envelope;
- if `PAGINATION_CUT` collapses, the reranker was a solution to a
  mismeasurement, and the cheap fix was a sort key.

Both outcomes are reported. The one that makes the previous recommendation
wrong is not softened.

## What this cannot conclude

Nothing about dense retrieval: `NEVER_CANDIDATE` was 11.93% on `baseline` and
candidate generation does not change between arms. Nothing about real traffic:
130 labelled cases are not a usage distribution.
