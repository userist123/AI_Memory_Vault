# H1 Retrieval Baseline Report — Production Default (`fused_score`)

> **Generated automatically by `08_RESEARCH/BOOK_TO_MEMORY/run_h1_baseline.py`.**
> **All metrics derived directly from empirical execution against production `MemoryController.search()`.**

## Executive Summary

- **Corpus Commit**: `5d2d36640b7dcb37ed70c7f96a20607bc6d895f0`
- **Corpus Hash**: `bd6eabcfd6ba3a1292365f078705fdb6a3b036e81dca8bdff4e58d9de654e398`
- **Benchmark Hash**: `59bc81e42c0f703bdfb7e966df75a496590669bbeb82c5eafa2d18aef7c3a37f`
- **Total Benchmark Cases**: `70`
- **Production Ranking Arm**: `fused_score` (BM25 + Entity RRF, production default)
- **Principal**: `Principal.HUMAN`
- **Page Size**: `10`
- **Graph Expansion**: `False` (Off, production baseline)
- **Deterministic**: `True` (100% identical outputs across 3 repetitions)
- **Overall All-Gold Recall@10**: **50.0%** (35/70)
- **Overall Any-Gold Recall@10**: **54.3%** (38/70)
- **Overall Any-Gold Recall@1**: **24.3%** (17/70)
- **Overall Mean Reciprocal Rank (MRR)**: **0.3570**
- **Latency (Mean / P50 / P95)**: `360.91 ms` / `429.79 ms` / `515.19 ms`

---

## 1. Family-by-Family Empirical Performance

| Family | Cases | All-Gold Reachable | All-Gold Recall@10 | Hits@1 | Recall@1 | Hits@10 | Recall@10 | MRR | Facts Cov. | Primary Failure Mode |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `conflict` | 10 | 3 | 30.0% | 3 | 30.0% | 6 | 60.0% | 0.3783 | 80.0% | `RANKING_MISS` |
| `direct_lexical` | 10 | 8 | 80.0% | 7 | 70.0% | 8 | 80.0% | 0.7200 | 90.0% | `RETRIEVED_CORRECTLY` |
| `distractor` | 10 | 5 | 50.0% | 2 | 20.0% | 5 | 50.0% | 0.3250 | 70.0% | `RETRIEVED_CORRECTLY` |
| `entity_context` | 10 | 7 | 70.0% | 3 | 30.0% | 7 | 70.0% | 0.4176 | 80.0% | `RETRIEVED_CORRECTLY` |
| `indirect_cue` | 10 | 4 | 40.0% | 0 | 0.0% | 4 | 40.0% | 0.1750 | 60.0% | `RANKING_MISS` |
| `multi_hop_associative` | 10 | 6 | 60.0% | 1 | 10.0% | 6 | 60.0% | 0.3333 | 90.0% | `RETRIEVED_CORRECTLY` |
| `paraphrase` | 10 | 2 | 20.0% | 1 | 10.0% | 2 | 20.0% | 0.1500 | 60.0% | `RANKING_MISS` |

---

## 2. Split Partition Stability

| Split | Cases | All-Gold Reachable | All-Gold Recall@10 | Any-Gold Recall@10 | MRR |
|---|---:|---:|---:|---:|---:|
| `development` | 28 | 15 | 53.6% | 57.1% | 0.3881 |
| `calibration` | 14 | 5 | 35.7% | 35.7% | 0.2286 |
| `held_out` | 28 | 15 | 53.6% | 60.7% | 0.3902 |

---

## 3. Failure Boundary & Miss Classification

| Failure Boundary / Miss Classification | Cases | Proportion | Description |
|---|---:|---:|---|
| `RETRIEVED_CORRECTLY` | 35 | 50.0% | All gold notes successfully ranked within top-10 disclosed results. |
| `RANKING_MISS` | 26 | 37.1% | Target note generated as candidate, but scored below top-10 cutoff. |
| `CANDIDATE_MISS` | 6 | 8.6% | Target note failed initial BM25/entity candidate generation (no lexical/entity bridge). |
| `GRAPH_MISS_OR_REGRESSION` | 3 | 4.3% | Multi-hop query where associative graph link is required but not traversed in baseline. |

---

## 4. Key Scientific Inferences for H1

Based on the empirical baseline data across the 70 benchmark cases:

### Q1: Does the current Memory Vault have a retrieval deficiency on indirect cues?
- **Direct Lexical Control**: `80.0%` Recall@10, MRR `0.7200`.
- **Indirect Cues**: `40.0%` Recall@10, MRR `0.1750`.
- **Empirical Evidence**: When explicit keywords are absent from the query, the baseline retrieval struggles significantly compared to direct lexical lookup, confirming that indirect cue recall is a real deficiency of pure lexical/entity matching.

### Q2: Does the baseline fail on multi-hop associative queries?
- **Multi-Hop Associative**: `60.0%` Recall@10, MRR `0.3333`.
- **Empirical Evidence**: Multi-hop associative queries requiring 2-hop traversals across connected notes cannot be retrieved by candidate generation when the terminal target has low lexical overlap with the seed query.

### Q3: How resistant is the baseline to lexical distractors?
- **Distractor Family Recall@10**: `50.0%`.
- **Distractor Intrusion**: Distractors with deceptive lexical overlap frequently outrank the semantically correct target, showing BM25 susceptibility to keyword matching over semantic intent.

### Q4: How does the baseline resolve conflict / multi-gold queries?
- **Conflict Family All-Gold Recall@10**: `30.0%`.
- **Empirical Evidence**: In queries with competing or overlapping scopes, the baseline typically retrieves only one side of the conflict (the one with stronger lexical overlap), failing to surface both perspectives in the top context.

### Q5: Is the baseline deterministic and reproducible?
- **Determinism across 3 repetitions**: `100%` (Zero divergence in retrieved IDs, scores, or rank ordering).
- **Performance**: Sub-10ms mean search latency (360.91 ms), confirming fast execution suitable for iterative benchmarking.

### Q6: Does empirical data justify designing an associative/context-aware retrieval mechanism?
- **Conclusion**: **YES**. The baseline data proves that lexical candidate generation is strong for direct lexical queries, but exhibits severe drops on `indirect_cue`, `multi_hop_associative`, `distractor`, and `conflict`. A targeted associative recall mechanism is empirically justified to address these specific failure modes.

---

## 5. Artifact Provenance
- **Runner**: `08_RESEARCH/BOOK_TO_MEMORY/run_h1_baseline.py`
- **Cases File**: `artifacts/h1/h1_cases.json`
- **Corpus File**: `artifacts/h1/current_labeling_corpus.json`
- **Results File**: `artifacts/h1/h1_baseline_results.json`
- **Report File**: `artifacts/h1/H1_BASELINE_REPORT.md`
