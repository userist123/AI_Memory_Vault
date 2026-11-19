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
- **Harm on real questions: none found (section 9).** On the 20 work questions the reranker gave 3 wins and 0 losses, but
  20 questions is an observation, not a benchmark.
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

## 9. Harm check on the 20 real work questions (run after the verdict)

`30_SCRIPTS/evaluation/reranker_worklist_harm.py`: each question from `07_EVALUATION/memory_usage/work_queries.json` through
the production controller with the reranker OFF and ON; the first three results (what an agent reads) of both arms pooled
into 88 distinct (question, note) pairs, shuffled, arm hidden; two independent blind judges (Claude Sonnet, Claude Opus),
rule "relevant only if it would help do the task the question was taken from".

| | Reranker OFF | Reranker ON |
|---|---:|---:|
| Relevant results in the first three, judge A / judge B | 8 / 8 | 13 / 14 |
| Questions with at least one relevant result (of 20), A / B | 6 / 7 | 9 / 10 |
| Questions won / lost by ON (both judges agree) | — | 3 / 0 |

Inter-judge agreement: 86 of 88 pairs, Cohen's kappa 0.92. No human judgement. Files: `worklist/`.

## 10. Wiring (after the verdict)

`MemoryController(reranker=...)`, OFF by default; `controller_factory` builds `retrieval.reranker.HttpReranker` only when
`AI_MEMORY_VAULT_RERANKER_URL` is set (loopback `http://` only). The model runs in `30_SCRIPTS/retrieval/reranker_server.py`
(own environment, binds 127.0.0.1, checks the Host header). The wired path reproduces the confirmatory arm on all 160
queries (`results/wired_check.json`: identical served top 5 160/160, hit@5 71). Independent review found that urllib's
default opener honours `HTTP_PROXY`/system proxies, which would have sent note text to a proxy despite the loopback check;
the client now uses an opener with no proxy and no redirects (regression tests in `20_TESTS/test_controller_reranker.py`).

Enable on the owner's PC:

```text
<reranker venv>/python 30_SCRIPTS/retrieval/reranker_server.py --port 8765 --fp16
etrieval
eranker_server.py --port 8765 --fp16
set AI_MEMORY_VAULT_RERANKER_URL=http://127.0.0.1:8765      (then start the MCP server / CLI)
```

**Co-residency, measured 2026-11-19 (`results/wired_check_coresident.json`):** with `qwen2.5:7b-instruct` loaded in Ollama
(5.0 GB, 100% GPU) and the reranker server up, VRAM is 7.4 of 8.1 GB; the wired path still serves the identical top 5 on
160/160 queries (hit@5 71), search p95 1.07 s, rerank p95 0.74 s, and the 7B model still generates at 54–70 tokens/s
(~2 s per answer with a 2k-token memory context). The server commits about 6.4 GB of virtual memory (CUDA context); this
only works once the page file has room to grow (it grew from 32 to 38 GB after space was freed on its drive).

The server refuses to start on a port another process already holds (`ExclusiveServer`, `SO_EXCLUSIVEADDRUSE` on Windows):
on 2026-11-19 another session's `python -m http.server` held 8765 and the stdlib server bound the same port silently, so
requests went to the other process (the client failed closed). Regression test in `20_TESTS/test_controller_reranker.py`.

## 11. Where the remaining misses are (diagnosis after the verdict, descriptive)

With K = 50 the reranker misses only 6 cases whose gold is in the top 50 (all multi_hop). The other 53 misses are gold notes
outside the top 50: 37 never among the candidates, 16 at fused rank 51–200 (Romanian: 32 of 61 cases are beyond the top 50).

**25 of the 37 "never a candidate" cases are note metadata, not retrieval** (`results/ineligible_gold_notes.json`, 21 notes):

| Cause | Notes | Cases |
|---|---:|---:|
| No frontmatter `id`: the storage engine does not index the file | 9 | — |
| `lifecycle` missing (type `core`): below the agent floor ACTIVE+REVIEW | 8 | — |
| `lifecycle` `NORMALIZED` or lower-case `raw` | 4 | — |
| **Total** | **21** | **25** |

No ranking change can serve these notes to an agent. Giving them an id or a lifecycle means choosing values, which the
vault reserves for the owner (not done here). The remaining ~12 are genuine lexical candidate misses (a dense retriever is
the candidate fix; not measured).

**Larger K, descriptive only (chosen after seeing v3, so not adopted; K = 50 stays the default):** identity replays reproduce
the baseline 0/160 at K = 100 and 200; oracle 88 and 93.

| Arm | Served hit@5 (of 130) | Wins / losses vs baseline | Rerank p95 |
|---|---:|---:|---:|
| bge fp16, K = 50 (confirmatory) | 71 | 47 / 0 | 0.74 s |
| bge fp16, K = 100 | 79 | 55 / 0 | 1.58 s |
| bge fp16, K = 200 | 82 | 58 / 0 | 3.13 s (over the 2 s limit) |
| mMiniLM, K = 100 | 74 | 51 / 1 | 0.59 s |
| mMiniLM, K = 200 | 75 | 52 / 1 | 1.20 s |

`AI_MEMORY_VAULT_RERANKER_K` (1–200, default 50) sets K in `controller_factory`; a K other than 50 needs its own
preregistered test before it becomes a default.
