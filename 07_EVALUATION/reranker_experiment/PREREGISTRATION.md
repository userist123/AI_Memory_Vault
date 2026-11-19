# Reranker experiment — preregistration (frozen before any labelled reranker score)

Date: 2026-11-19. Base: `main` `f28d1eb93`. Branch `claude/retrieval-reranker-experiment`.
Status: **FROZEN**. Any change after the first labelled score is a deviation and is reported as such in the report.

## 1. Question

Does a local cross-encoder that reorders the production fused candidate list put the gold note into the
agent's top 5 more often than production `search()` does today?

Motivation: `07_EVALUATION/loss_funnel/LOSS_FUNNEL_REPORT.md` attributes most misses to ranking and
preregistered a cross-encoder as the next step. **Caveat found in design review:** the funnel ran the legacy
`RANKING_ARM_BASELINE`, not production `fused_score`, so its numbers are not the baseline here. The baseline
below was re-measured on the production arm before this document was frozen.

## 2. Production baseline (measured, labels used only for baseline and ceiling)

Harness `30_SCRIPTS/evaluation/reranker_snapshot.py`: `controller_factory.get_memory_controller()`,
`Principal.AI_AGENT`, `page_size=5`, exactly as `interfaces/memory_access.search`. Baseline top 5 is
`pack["results"]`, not `fused_ranking[:5]`.

| Measure | Value |
|---|---:|
| Cases / measurable (non-abstain) | 160 / 130 (en 69, ro 61; direct 60, multi_hop 40, conceptual 30) |
| Trace validity | `source=generated` 160/160, `ranking_arm=fused_score` 160/160, `classifier_filter_arm=hard` 160/160 |
| **Baseline hit@5** | **24 / 130** (en 19, ro 5) |
| Oracle@K (gold in fused top K) | 5: 24 · 10: 31 · 20: 55 · 30: 62 · **50: 77** · 100: 88 · 200: 93 |
| Gold never in fused list | 37 |
| Convertible cases (gold at fused rank 6–50) | 53 (conceptual 21, multi_hop 20, direct 12; en 29, ro 24) |
| `search()` latency, 160 queries | p50 202 ms, p95 332 ms |
| Snapshot | `snapshot.json` sha256 `5996422a…8f93d1`; `texts.json` (not committed, regenerable) sha256 `931038a2…301a78` |
| Benchmark v3 | sha256 `eeb53822…57eaa` (frozen file, verified by the harness) |

The fused list is computed over the already gated pool (`RetrievalEngine.retrieve`: RAW exclusion and hard
lifecycle/type filters inside `storage.query()` before candidate generation), so reordering it cannot
re-admit a filtered note. Every note an arm newly places in a top 5 is additionally checked through the
agent read path (`memory_access.get`) and reported.

## 3. Latency pre-check (no labels read)

`30_SCRIPTS/evaluation/reranker_rerank.py --latency-only`, all 160 queries, warm, rerank stage only:

| Model | Device | K | p50 | p95 | Peak VRAM |
|---|---|---:|---:|---:|---:|
| bge-reranker-v2-m3 fp16 | RTX 5060 8 GB | 30 | 431 ms | 439 ms | 1452 MB |
| bge-reranker-v2-m3 fp16 | RTX 5060 8 GB | 50 | 684 ms | 722 ms | 1452 MB |
| bge-reranker-v2-m3 fp32 | CPU (5-query sample) | 50 | 32.2 s | 34.6 s | — |
| mmarco-mMiniLMv2-L12 fp32 | RTX 5060 8 GB | 50 | 267 ms | 281 ms | 730 MB |

Deviation from design: the measurement could not run with an Ollama model resident; the machine hit its
Windows commit limit (os error 1455). Peak VRAM 1.45 GB + a 3.4 GB Ollama model fits the 8 GB card;
co-residency latency is UNVERIFIED. CPU is unusable for bge (> 30 s per query), so the declared device is GPU.
Model load: 4.0 s (cold, once per process).

## 4. Arms

**Confirmatory (the only one tested for adoption):**
`BAAI/bge-reranker-v2-m3` revision `953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e`, fp16, CUDA, K = 50,
input = (query, title + first 1200 characters of the note `content`), `max_length` 512 (70 of 7791 pairs
truncated), batch 32, ties broken by fused rank.

**Descriptive only (no adoption claim):** same model K = 30; `cross-encoder/mmarco-mMiniLMv2-L12-H384-v1`
revision `1427fd652930e4ba29e8149678df786c240d8825`, K = 50.

## 5. Decision rule (primary metric: paired hit@5 on the 130 measurable cases, strict gold)

- **ADOPT** only if all hold: wins − losses ≥ 8; exact two-sided McNemar p < 0.05; losses ≤ wins / 3;
  Romanian stratum wins − losses ≥ 0; end-to-end warm p95 (search + rerank) ≤ 2 s on the declared device.
- **REJECT** if wins − losses ≤ 2, or losses > wins / 2.
- Anything else: **INCONCLUSIVE**, nothing changes.

Adoption here means "worth wiring behind a flag, OFF by default"; it does not change production by itself.

Secondary, descriptive: MRR@10, hit@1, hit@3, conversion of the 53 convertible cases, every metric per class
and per language; AUROC of the top reranker score for abstain vs measurable (no threshold fitted).

## 6. Controls (must pass before any conclusion)

1. Identity reranker (fused order) reproduces the baseline: 0 wins, 0 losses.
2. Oracle reranker reaches oracle@50 = 77.
3. Shuffled gold labels: net near 0.
4. Three repeated confirmatory runs: report top-5 flips (fp16 may flip near-ties).

## 7. Known limits, stated in advance

- v3 queries were written from title + a 700-character excerpt, and required facts appear in that excerpt;
  v3 therefore validates head-text reranking only, and may favour it. It says nothing about chunking.
- Labels cover the corpus at `b3ada1b54`; storage now sees more notes. A reranker that promotes an unjudged
  but correct note is scored a miss (pooling bias, against adoption). Sensitivity check: the top 5 of both
  arms for every discordant case is listed in the report for blind adjudication; the strict result stands.
- The 20 work questions in `07_EVALUATION/memory_usage/` are a harm check only, not evidence of gain.
- Design reviewed by a read-only second-opinion agent; harness reviewed by an independent reviewer before
  the labelled run.
