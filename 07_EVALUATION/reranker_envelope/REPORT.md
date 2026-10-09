# Reranker cost envelope — results

Preregistration: `07_EVALUATION/reranker_envelope/PREREGISTRATION.md`, committed before this run; deviations in `DEVIATIONS.md`. Benchmark v3 SHA-256 `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`, 130 measurable cases, model `nomic-embed-text` via local Ollama.

### [Operating point: Principal.AI_AGENT, page_size=5, Floor: ACTIV, graph expansion off]

The arms run inside `search()`: only the sort key changes, and pagination and the context-pack budget apply as in production.

| premise | value |
|---|---|
| identity-order patch reproduces production's page | 130/130 (must be 130) |
| production's page equals the fusion top-5 | 93/130 — the rest lost notes to the pack budget |
| production's page shorter than 5 / empty | 41 / 22 |
| gold note anywhere in the fusion pool (mean size 189.88) | 92/130 |
| gold in the pool below rank 5 — reachable by reordering | **68** |

| arm | K | context_recall@5 | 95% CI | gains | losses | McNemar p | Holm p | live (sabotage Δ) | added p50 ms | added p95 ms | search p50 ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `production` | — | 24/130 | 18.46% [12.73, 26.00] | — | — | — | — | — | — | — | 510.1 |
| `embed_top20` | 20 | 45/130 | 34.62% [26.99, 43.13] | 21 | 0 | 1e-06 | 1e-06 | 127 | 40.9 | 51.5 | 638.1 |
| `embed_top50` | 50 | 59/130 | 45.38% [37.08, 53.95] | 35 | 0 | 0.0 | 0.0 | 128 | 39.8 | 53.2 | 524.3 |
| `embed_top200` | 200 | 64/130 | 49.23% [40.78, 57.72] | 41 | 1 | 0.0 | 0.0 | 128 | 50.4 | 63.9 | 518.8 |
| `rrf_top50` | 50 | 46/130 | 35.38% [27.69, 43.92] | 22 | 0 | 0.0 | 0.0 | 127 | 44.9 | 54.8 | 547.8 |

### Controls

- Embedding service determinism: 20 texts × 3 calls, byte-identical: **True**.
- `embed_top20` with random scores: gains 4, losses 19, p = 0.002599 — does not qualify.
- `embed_top50` with random scores: gains 5, losses 23, p = 0.000912 — does not qualify.
- `embed_top200` with random scores: gains 1, losses 24, p = 2e-06 — does not qualify.
- `rrf_top50` with random scores: gains 6, losses 12, p = 0.237885 — does not qualify.

### Cost

- One-time: 457 notes embedded in 0.0 s over 0 batched calls (0 calls means the cache already held them); cache 4,722,507 bytes on disk.
- Per query: added = live query embedding + K cosines, 1 embedding call. Budget: p95 < 200.0 ms. `search p50` is the whole call, for scale.

### Decision

| arm | gains ≥ 8 | losses ≤ 2 | p < 0.05 | Holm < 0.05 | live | random does not qualify | gains reachable | premise | qualifies | within budget |
|---|---|---|---|---|---|---|---|---|---|---|
| `embed_top20` | True | True | True | True | True | True | True | True | **True** | True |
| `embed_top50` | True | True | True | True | True | True | True | True | **True** | True |
| `embed_top200` | True | True | True | True | True | True | True | True | **True** | True |
| `rrf_top50` | True | True | True | True | True | True | True | True | **True** | True |

**Verdict: embed_top20: effective and within budget — candidate for wiring.**

### Held-out confirmation (v2, one run)

Arm `embed_top20` vs production on 29 cases (SHA `0222d5e858dfca5bbae6db4559eafe0d43b133380a912664aaa7e4ea6c0d8418`): production 2/29, arm 6/29; gains 5, losses 1, McNemar p = 0.21875.

*Every figure above is read from `results.json`.*
