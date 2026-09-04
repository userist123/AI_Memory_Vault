# A10 — Production Graph Observability Probe & Candidate Diagnostics Report

**Milestone**: Round R001 / Antigravity Lane Task A10  
**Agent**: ANTIGRAVITY (Developer-Observability & Architecture Inspection)  
**Baseline SHA**: `e43cc81e09789e284ef35a7e326297194f429a9e`  
**Working Branch at capture**: `antigravity/observability-v1`  

## Executive Summary

Task A10 delivered a non-invasive diagnostic probe and candidate attribution suite (`GraphDiagnosticsProbe`). The captured runtime evidence showed production graph execution falling back after `AttributeError` because the historical implementation accessed `controller.storage.store` on SQLite and File storage, while candidate attribution exposed lifecycle and scoring rejection causes.

## Key Empirical Findings

| Diagnostic Target | Result | Evidence class |
|---|---|---|
| Production graph execution on SQLite/File | `FALLBACK_SILENT_EXCEPTION` caused by missing `.store` | `RUNTIME_VERIFIED` in A10 capture |
| In-memory graph execution | Graph path available on the test storage contract | `RUNTIME_VERIFIED` in A10 capture |
| Silent fallback masquerade | Historical broad exception handling returned base results without surfacing graph failure | `RUNTIME_VERIFIED` in A10 capture |
| Candidate rejection attribution | Segmented into lifecycle, supersession, and score/page causes | `RUNTIME_VERIFIED` in A10 capture |
| Relevance score survival into graph phase | Historical capture reported `0.0%` survival because the context boundary stripped `relevance_score` | `RUNTIME_VERIFIED` in A10 capture |

## Important Provenance Boundary

This report is historical evidence from the Antigravity observability branch. It is preserved on canonical `main` for traceability. It is **not** a claim that the historical defect still exists after the targeted production graph/storage and context-boundary fixes were selectively restored on `main`.

The exact historical report remains useful because it documents why those repairs were required and what failure mode they addressed.

## Historical Verification Protocol

The original A10 lane specified:

```text
python scratch/run_a10_graph_diagnostics.py
```

and required the diagnostic trace to show zero silent fallbacks after the corresponding repair, with `APPLIED` status for SQLite and File storage. That historical acceptance protocol remains a reusable verification target for future regression testing.

## Preservation Decision

Kept as forensic evidence only. No automatic promotion from this historical report into canonical memory was performed.
