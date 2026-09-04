# A7 — Associative Differential Trace & Score Causality Report

**Evaluation Lane**: `ANTIGRAVITY` (Developer Observability & Architecture Inspection)  
**Round**: `R001`  
**Historical working branch**: `antigravity/observability-v1`  
**Epistemic Standard**: strict separation of `RUNTIME_VERIFIED`, `CODE_VERIFIED`, and `UNAVAILABLE`.

## Executive Summary

A7 used a controlled representative corpus and query archetypes to compare base controller retrieval with the historical associative re-ranking layer (`ranked_search.py` + `spreading_activation.py`). The lane documented a dual-reality gap: graph behavior was observable under the in-memory test storage, while the historical production path failed because `ranked_search.py` depended directly on a `.store` attribute absent from SQLite/File storage.

The report also documented two important historical retrieval defects:

1. base relevance scores were lost at the context boundary and replaced downstream by synthetic reciprocal-rank seeds;
2. edge-weighted propagation was calculated and then overwritten by an unweighted decay expression.

It additionally discovered a lifecycle-classification substring trap where `unverified` could match `verified`.

## Historical Findings

| Finding | Historical classification |
|---|---|
| In-memory spreading activation can change ranking | `RUNTIME_VERIFIED` |
| Historical SQLite/File graph construction failed on `.store` | `RUNTIME_VERIFIED` failure |
| Historical downstream ranking used synthetic reciprocal-rank seeds when score was unavailable | `CODE_VERIFIED` / `RUNTIME_VERIFIED` in lane |
| Historical edge-weight propagation was overwritten | `CODE_VERIFIED` |
| Historical classifier substring trap existed | `CODE_VERIFIED` |
| Associative activation could provide reachability expansion in the controlled harness | `RUNTIME_VERIFIED` in the historical harness |

## Important Provenance Boundary

This is historical forensic evidence preserved on canonical `main`. It must not be read as proof that every historical defect remains present on current `main`. Subsequent targeted fixes were selectively restored on `main`, including the storage abstraction, weighted propagation, and context-boundary corrections.

The report therefore serves as a record of the defects, empirical observations, and regression targets that motivated those changes.

## Reusable Regression Targets

Future associative-memory evaluation should retain:

- fixed candidate-set comparisons;
- explicit score survival from retrieval through ranking;
- measurable edge-weight sensitivity;
- reachability tests where relevant neighbors begin outside the base candidate set;
- lifecycle tests containing `unverified`, `verified`, `review`, and supersession terminology;
- separate production-storage and in-memory execution paths;
- explicit diagnostics instead of silent fallback.

No canonical-memory promotion was performed from this report.
