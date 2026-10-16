---
id: 5b7e2c41-9d3a-4f0e-8c6b-2a1f7e9d3c58
type: procedure
lifecycle: REVIEW
category: evaluation.reranking
tags: ['preregistration', 'reranker', 'benchmark-v3', 'heldout-v2', 'measured']
created: "2026-10-10"
updated: "2026-10-10"
provenance:
  source_type: 'execution'
  source_ref: 'follows 07_EVALUATION/ranking_formula: 70 of 130 cases hold the gold note in the pool below rank 5'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Reranker cost envelope — preregistration

**Committed before any arm was run.** Hypotheses, arms, controls, decision rule
and cost threshold are fixed here and do not change after results exist.

## What is known going in

On the production arm at the agent operating point (`AI_AGENT`, `page_size=5`,
lifecycle floor on, graph expansion off), benchmark v3 gives 24/130. The gold
note is somewhere in the candidate pool in 94/130 cases; in **70** of them it is
below rank 5. Those 70 are the only cases any reranker can reach. The other 36
(20 candidate-generation failures, 16 policy exclusions) are out of its reach
whatever it costs. The pool is the fusion order, unchanged: the production sort
step is a no-op (`07_EVALUATION/ranking_formula/REPORT.md`).

No reranker exists in the pipeline. `retrieval/interference_gate.py` takes a
`reranker_score` as input but has no production consumer and no producer.

## What is being measured

Whether reordering the fusion pool with a dense signal moves gold notes onto
the page, and what that costs per query. The only dense model available
locally without new dependencies is `nomic-embed-text` (768-d) through the
running Ollama instance: ~20 ms per call warm, batch endpoint available. No
cross-encoder is available on this machine, so **nothing here is evidence
about cross-encoders**; see "What this cannot conclude".

## Hypotheses

- **H-RR-1.** Cosine reranking of the fusion top-50 gains at least 8 cases and
  loses at most 2 against production on `context_recall@5` (the decision rule
  below). *Direction stated: dense similarity should surface paraphrases that
  BM25 ranks low.*
- **H-RR-2.** The gain comes from the 70 reachable cases and from nowhere
  else: no case outside that set changes from miss to hit under any arm. This
  is a consistency check, not a result; a violation means the harness is wrong.
- **H-RR-3.** Widening K from 20 to 200 adds fewer than 3 net cases: most
  recoverable gold notes sit in the first 50 of the fusion order (ceiling@50 is
  76/130 against 94/130 at the full pool).
- **H-RR-4 (cost).** Added latency per query at K=50, with the corpus embedded
  once and cached, is under 200 ms at p95. The production retrieval stage is
  ~350 ms; an addition above 200 ms changes what the product is.

## Arms

All arms start from the same fusion pool (mean 190 notes) and the same page
size 5. Only the order of the first K changes; positions after K are untouched.

| arm | what it does |
|---|---|
| `production` | fusion order, as today |
| `embed_top20` | cosine(query, note) reorders the top 20 of the fusion order |
| `embed_top50` | same, top 50 — **the primary arm** |
| `embed_top200` | same, top 200 (effectively the whole pool) |
| `rrf_top50` | reciprocal-rank fusion of the fusion rank and the cosine rank over the top 50 (keeps the lexical signal) |

Note text given to the embedder: title plus content, truncated to 2,000
characters. The query is embedded as typed. Embeddings are cached on disk,
keyed by SHA-256 of model name plus text, so the corpus is embedded once.

## Mandatory controls

1. **Sabotage.** Each embedding arm is re-run with the cosine scores replaced
   by random numbers from a fixed seed. If the returned pages are identical to
   the real arm on every case, the arm does not drive the order and its result
   is void.
2. **Random-score arm cannot win.** The same random-score run is scored as an
   arm. If it meets the decision rule, the rule is too loose for this sample
   and no embedding arm may be adopted on it.
3. **Determinism.** The embedding service is called three times for 20 fixed
   texts before anything else; byte-identical vectors are required. With the
   cache in place every later run is deterministic by construction, which is
   why the service itself is what gets tested.
4. **Reachability.** H-RR-2 is asserted on every arm's rows, not read off a
   summary.

## Decision rule

Let *d* be discordant cases on `context_recall@5` against `production`, at the
agent operating point, benchmark v3, 130 measurable cases.

- An arm **qualifies** if it gains ≥ 8, loses ≤ 2, exact two-sided McNemar
  p < 0.05, and its Holm-adjusted p across the four arms is < 0.05.
- Among qualifying arms, the one with the smallest K wins; `rrf_top50` beats
  `embed_top50` only if it gains strictly more cases net.
- The winning arm is **recommended for wiring** only if it also meets H-RR-4
  (p95 added latency < 200 ms). An arm that qualifies on recall and fails on
  cost is reported as "effective, too slow", with the number.
- If no arm qualifies: **no reranker is recommended on dense similarity from
  this model**, and the next question is a cross-encoder or better candidate
  generation — stated as open, not answered.

## Held-out confirmation

If an arm qualifies, it is run **once** on `heldout_retrieval_benchmark_v2`
(29 measurable cases, SHA
`0222d5e858dfca5bbae6db4559eafe0d43b133380a912664aaa7e4ea6c0d8418`) against
`production`, and the paired result is reported as is. No arm is tuned after
that run, and no second arm is tried on the held-out set. If no arm qualifies
on v3, the held-out set is not touched.

## Cost reporting

Per query, warm cache: p50 and p95 of added wall time (query embedding plus K
cosines), embedding calls (1), and bytes of cache on disk. One-time: corpus
embedding wall time and call count. Measured on this machine with Ollama
already resident; stated as such.

## What this cannot conclude

Nothing about cross-encoder rerankers. Nothing about the 36 cases outside the
pool. Nothing about queries that are not in a 130-case benchmark; a qualifying
arm is a candidate for wiring, not a shipped change — wiring is a separate PR
with its own tests on `MemoryController.search()`.
