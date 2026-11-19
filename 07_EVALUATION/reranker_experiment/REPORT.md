# Reranker experiment — report

Date: 2026-11-19. Code and data at `4ce3d982f`, base `main` `f28d1eb93`. Preregistration: [[PREREGISTRATION]]
(frozen at `77a2f8593`, amendment A1 at `ab3406e35`, before any labelled reranker score).

## 1. Result

| Arm | Served hit@5 (of 130) | Wins / losses vs baseline | Exact McNemar p | Rerank p95 | Search + rerank p95 (upper bound) | Verdict |
|---|---:|---:|---:|---:|---:|---|
| Production baseline (`search()`, `fused_score`) | 24 | — | — | — | 332 ms | — |
| **bge-reranker-v2-m3 fp16, K = 50 (confirmatory)** | **71** | **47 / 0** | 1.4e-14 | 765 ms | 1097 ms | **ADOPT** |
| bge-reranker-v2-m3 fp16, K = 30 (descriptive) | 57 | 33 / 0 | < 1e-9 | 493 ms | 825 ms | descriptive |
| mmarco-mMiniLMv2-L12 fp32, K = 50 (descriptive) | 70 | 46 / 0 | < 1e-13 | 287 ms | 619 ms | descriptive |
| Oracle ceiling through the pack stage, K = 50 | 77 | — | — | — | — | ceiling |

The confirmatory arm meets every condition of section 5: net +47 ≥ 8, p < 0.05, 0 losses ≤ 47/3, Romanian net +19 ≥ 0,
end-to-end p95 ≤ 2 s on the RTX 5060. It converts 47 of the 53 cases whose gold sat at fused rank 6–50.
**Adoption means: worth wiring into `search()` behind a flag, OFF by default (section 5). Production is unchanged.**

## 2. Confirmatory arm in detail

| Stratum | n | Baseline | Arm | Wins | Losses |
|---|---:|---:|---:|---:|---:|
| direct | 60 | 15 | 27 | 12 | 0 |
| multi_hop | 40 | 2 | 16 | 14 | 0 |
| conceptual | 30 | 7 | 28 | 21 | 0 |
| en | 69 | 19 | 47 | 28 | 0 |
| ro | 61 | 5 | 24 | 19 | 0 |

Secondary (served lists): MRR@5 0.135 → 0.491; hit@1 14 → 59; hit@3 19 → 67. Cases served fewer than 5 notes: 39 → 24.
Abstain: AUROC of the top reranker score, measurable vs abstain queries = 0.83 (descriptive; no threshold fitted).

## 3. Controls (section 6, enforced by the analyzer)

| Control | Expected | Measured |
|---|---|---|
| Identity order replayed through `search()` | equals baseline in 160/160 | 0 of 160 differ (K = 50 and K = 30) |
| Oracle order replayed through `search()` | oracle@K | 77 (K = 50), 62 (K = 30) |
| Shuffled gold labels | net ≈ 0 | +1 (bge K = 50), +3 (MiniLM) |
| Three repeated fp16 runs | 0 flips | 0 and 0 changed served top 5 |
| Same `git_head` for every served file | one | `4ce3d982f` |

## 4. Process record

1. A read-only design review rejected the first design: the motivating numbers came from the legacy ranking arm, the
   adoption rule contradicted itself (8 wins / 2 losses is p = 0.109), and 74 of 160 queries are Romanian, not English.
   The baseline was re-measured on the production arm (24/130, not the funnel's figure) before freezing.
2. An independent harness review returned NO-GO: the baseline went through pack building/egress while the arm was scored
   on raw candidates (39 baseline cases are served fewer than 5 notes). Amendment A1 moved the arm onto served lists;
   a second review returned GO; its two recommended guards (controls enforced, injected ids fully matched) were added.
3. The latency check ran without an Ollama model resident: the machine hit its Windows commit limit (os error 1455).
   Peak VRAM is 1.45 GB, so bge fits next to a 3.4 GB Ollama model on the 8 GB card; co-residency latency is UNVERIFIED.
   bge on CPU takes > 30 s per query: GPU only.

## 5. What this does not show

- **Generality.** v3 queries were written from a note's title plus a 700-character excerpt, and the reranker reads title +
  1200 characters. The benchmark therefore favours head-text reranking by construction; the size of the gain on real work
  questions, and on answers deep inside long notes, is UNVERIFIED.
- **Harm on real questions.** The harm check on the 20 work questions (`07_EVALUATION/memory_usage/`) was NOT RUN: it needs
  blind judging of new entrants.
- **Pooling bias** would only add wins (unjudged notes promoted by the reranker count as misses); with 0 losses the
  sensitivity adjudication was not needed for the verdict and was not done.
- **Decisions.** Better retrieval is not better agent decisions; the two earlier memory ablations were null
  (`07_EVALUATION/memory_ablation_2026-09.md`, `07_EVALUATION/golden_memory_effectiveness_v1/`). A causal run with the
  reranked retrieval is the next test of "retrieval ≠ influence".

## 6. Side finding (not fixed here)

`search()` serves notes that `memory_get` refuses: 16 notes newly placed in a top 5 raise "not eligible for cognitive
retrieval" in `memory_access.get`, 14 of them gold answers and 9 also served by the baseline (pre-existing). Example: a
`REVIEW` note with `verification: inferred`. An agent sees the snippet but cannot open the note. Follow-up.

## 7. Reproduce

```text
python 30_SCRIPTS/evaluation/reranker_snapshot.py                      # production baseline + candidates
<venv with sentence-transformers> 30_SCRIPTS/evaluation/reranker_rerank.py --snapshot-dir 07_EVALUATION/reranker_experiment \
    --model BAAI/bge-reranker-v2-m3 --revision 953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e --k 30 50 --device cuda --fp16 \
    --out 07_EVALUATION/reranker_experiment/runs/bge_fp16_r1.json
python 30_SCRIPTS/evaluation/reranker_replay.py --ordering identity --k 50 --out .../served/identity_k50.json   # and oracle
python 30_SCRIPTS/evaluation/reranker_replay.py --ordering run --run .../runs/bge_fp16_r1.json --k 50 --out .../served/bge_fp16_r1_k50.json
python 30_SCRIPTS/evaluation/reranker_analyze.py --served .../served/bge_fp16_r1_k50.json \
    --repeat-served .../served/bge_fp16_r2_k50.json .../served/bge_fp16_r3_k50.json \
    --raw-run .../runs/bge_fp16_r1.json --k 50 --confirmatory --out .../results/confirmatory_bge_k50.json
```

## 8. Next step

Wire the reranker into `search()` behind a flag, OFF by default, with the MiniLM model as the cheaper candidate (70 vs 71,
2.7× faster, half the VRAM), keep it out of the CPU path, then measure harm on the real work questions and run a causal
agent ablation with the flag ON.
