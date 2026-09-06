# A8 — Production Graph Differential & Falsification Report

**Milestone**: Round R001 / Antigravity Lane Task A8  
**Agent**: ANTIGRAVITY  
**Historical working branch**: `antigravity/observability-v1`  

## Executive Summary

A8 compared base retrieval with graph-reranked search across in-memory, SQLite, and File storage. The historical run found that production graph construction was dead because `build_multi_graph()` accessed `controller.storage.store`, which only existed on the in-memory test storage. It also showed that graph ranking could not add candidates outside the base result map and that true `relevance_score` was unavailable downstream after the historical context-pack boundary.

## Historical Falsification Targets

| Target | Historical result | Evidence |
|---|---|---|
| Graph activation on SQLite/File | Failed on missing `.store` | `RUNTIME_VERIFIED` failure |
| Add previously omitted graph neighbor | Blocked by downstream `id_to_result` filtering | `CODE_VERIFIED` / `RUNTIME_VERIFIED` |
| Edge-weight repair in production | Unreachable until production graph contract fixed | `RUNTIME_VERIFIED` limitation |
| Relevance-score preservation | Historical context boundary discarded scalar score | `RUNTIME_VERIFIED` in lane |
| Silent fallback | Historical broad exception fallback confirmed | `RUNTIME_VERIFIED` |

## Current-main Boundary

This report is retained as historical evidence only. The production storage contract and weighted activation were subsequently repaired selectively on `main`; therefore the historical failure state must not be treated as the present state of the repository.

## Regression Guidance

Keep production and in-memory backends in the same graph diagnostics suite. Assert explicit graph status, preserve real retrieval scores through ranking, and test candidate reachability independently from base-search result truncation.

No canonical-memory promotion was performed from this report.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
