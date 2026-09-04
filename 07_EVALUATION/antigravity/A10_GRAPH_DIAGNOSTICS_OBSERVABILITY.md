# A10 — Production Graph Observability Probe & Candidate Diagnostics Report

**Milestone**: Round R001 / Antigravity Lane Task A10  
**Agent**: ANTIGRAVITY (Developer-Observability & Architecture Inspection)  
**Baseline SHA**: `e43cc81e09789e284ef35a7e326297194f429a9e`  
**Working Branch**: `antigravity/observability-v1`  
**Timestamp**: `2026-09-04T18:20:00+03:00`  
**Telemetry File**: [`telemetry/retrieval_traces/a10_graph_diagnostics_trace.json`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/telemetry/retrieval_traces/a10_graph_diagnostics_trace.json)  
**Coordination Dispatch**: [`09_COORDINATION/dispatch/ANTIGRAVITY_R001_NEXT_A10.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/09_COORDINATION/dispatch/ANTIGRAVITY_R001_NEXT_A10.md)  
**Test Suite**: [`cognitive_core/tests/test_graph_diagnostics_probe.py`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/tests/test_graph_diagnostics_probe.py)  

---

## 1. Executive Summary & Core Findings

Task **A10** delivers a non-invasive diagnostic probe and candidate attribution suite (`GraphDiagnosticsProbe`), establishing end-to-end trace visibility into the retrieval pipeline without altering core memory contracts or security boundaries.

### Key Empirical Results

| Diagnostic Target | Observation Mode | Empirical Status | Forensic Implication |
|---|---|---|---|
| **Production Graph Execution** | SQLite & FileStorage | **`FALLBACK_SILENT_EXCEPTION`** (100% of non-empty queries) | `AttributeError: '...StorageEngine' object has no attribute 'store'` intercepted and surfaced with full traceback (`GAP-012`, `GAP-014`). |
| **In-Memory Graph Execution** | In-Memory `StorageEngine` | **`FALLBACK_NO_GRAPH_CHANGES`** / **`APPLIED`** | Graph successfully builds when `.store` is present, confirming divergence between test harness and production. |
| **Silent Masquerade** | Production Runtime | **NEUTRALIZED IN OBSERVABILITY** | Probe detects and flags when `ranked_search()` swallows internal exceptions and reverts to unranked base results. |
| **Candidate Rejection Attribution** | All Engines | **`RUNTIME_VERIFIED`** | Accurately segments non-admitted candidates into `LIFECYCLE_RAW_EXCLUDED`, `SUPERSEDED_INACTIVE`, and `SCORE_BELOW_THRESHOLD` (`GAP-001`). |
| **Score Survival Rate** | Context Pack Boundary | **0.0% (`UNAVAILABLE`)** | Re-confirmed that `ContextPackBuilder.build()` strips scalar `relevance_score`, forcing consumers to use synthetic weights (`GAP-002`). |

---

## 2. Probe Architecture & Execution Matrix

The probe implements two non-invasive inspection methods:
1. `GraphDiagnosticsProbe.probe_ranked_search()`: Executes `ranked_search()` while directly inspecting graph construction to intercept swallowed exceptions and compute pre- vs post-ranking delta.
2. `GraphDiagnosticsProbe.probe_candidate_rejection()`: Audits candidate note pools to classify why specific notes were excluded from the admitted `ContextPack`.

### Empirical Execution Results (12 Engine Probes)

| Storage Engine | Query Archetype | Status | Shifted | Exception Intercepted | Scores Preserved |
|---|---|---|---|---|---|
| `InMemoryStorageEngine` | `python asyncio concurrency` | `FALLBACK_NO_GRAPH_CHANGES` | False | None | False (`0.0%`) |
| `InMemoryStorageEngine` | `deterministic memory state machine` | `FALLBACK_NO_GRAPH_CHANGES` | False | None | False (`0.0%`) |
| `InMemoryStorageEngine` | `foreign keys pragma wal timeout` | `FALLBACK_NO_GRAPH_CHANGES` | False | None | False (`0.0%`) |
| `InMemoryStorageEngine` | `retrieve unverified review lessons` | `FALLBACK_NO_RESULTS` | False | None | False (`0.0%`) |
| `SQLiteStorageEngine` | `python asyncio concurrency` | **`FALLBACK_SILENT_EXCEPTION`** | False | **`AttributeError`** | False (`0.0%`) |
| `SQLiteStorageEngine` | `deterministic memory state machine` | **`FALLBACK_SILENT_EXCEPTION`** | False | **`AttributeError`** | False (`0.0%`) |
| `SQLiteStorageEngine` | `foreign keys pragma wal timeout` | **`FALLBACK_SILENT_EXCEPTION`** | False | **`AttributeError`** | False (`0.0%`) |
| `SQLiteStorageEngine` | `retrieve unverified review lessons` | `FALLBACK_NO_RESULTS` | False | None | False (`0.0%`) |
| `FileStorageEngine` | `python asyncio concurrency` | **`FALLBACK_SILENT_EXCEPTION`** | False | **`AttributeError`** | False (`0.0%`) |
| `FileStorageEngine` | `deterministic memory state machine` | **`FALLBACK_SILENT_EXCEPTION`** | False | **`AttributeError`** | False (`0.0%`) |
| `FileStorageEngine` | `foreign keys pragma wal timeout` | **`FALLBACK_SILENT_EXCEPTION`** | False | **`AttributeError`** | False (`0.0%`) |
| `FileStorageEngine` | `retrieve unverified review lessons` | `FALLBACK_NO_RESULTS` | False | None | False (`0.0%`) |

---

## 3. Neutralizing the Silent Fallback Masquerade (GAP-014)

### The Diagnostic Intercept

In production code (`cognitive_core/ranked_search.py:L39-L45`):
```python
    try:
        graph_memory = build_multi_graph(controller)
        base_scores = {note_id: 1.0 / (idx + 1) for idx, note_id in enumerate(id_to_result)}
        engine = SpreadingActivationEngine(graph_memory, decay=decay, max_hops=max_hops)
        ranked_ids = engine.rank(base_scores, top_k=top_k)
    except Exception:
        return results[:top_k]
```

When called on `SQLiteStorageEngine` or `FileStorageEngine`, `build_multi_graph` raises:
```text
AttributeError: 'SQLiteStorageEngine' object has no attribute 'store'
  File "cognitive_core/ranked_search.py", line 16, in build_multi_graph
    notes = list(controller.storage.store.values())
```
Because the production method returns `results[:top_k]`, the caller receives an HTTP 200 / standard list response indistinguishable from successful reranking.

### The Probe Solution

`GraphDiagnosticsProbe.probe_ranked_search()` executes the graph phase in an isolated pre-check probe, recording:
1. `status = GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION`
2. `exception_type = "AttributeError"`
3. `exception_message = "'SQLiteStorageEngine' object has no attribute 'store'"`
4. Full formatted traceback

This enables evaluators, Luna adversarial audits, and CI systems to immediately assert:
```python
assert report.status != GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION
```
providing an automated, deterministic verification gate for Codex Task C9.

---

## 4. Candidate Rejection Diagnostics (GAP-001)

When querying a candidate pool of 5 notes with `page_size=2`:
- **Admitted**: 2 notes (top scoring active notes).
- **Rejected**: 3 notes.
  - **`LIFECYCLE_RAW_EXCLUDED`** (1 note): `NOTE-RAW-001` excluded by storage query boundary per invariant `I-003`.
  - **`SUPERSEDED_INACTIVE`** (1 note): `NOTE-SUP-001` filtered from active search.
  - **`SCORE_BELOW_THRESHOLD`** (1 note): Active note cut off by page size or low similarity.

For Query 4 (`"retrieve unverified review lessons"`):
- `Admitted = 0`, `Rejected = 5`
- Rejection reason: `GAP-011` substring match forced `lifecycle_filters = ['VERIFIED']`, causing all candidate review notes to be rejected.

---

## 5. Verification Protocol for Codex C9 & Luna L10

### Codex Task C9 Acceptance Protocol
When Codex repairs `build_multi_graph()` in `cognitive_core/ranked_search.py`:
1. Run `python scratch/run_a10_graph_diagnostics.py`.
2. Verify in `telemetry/retrieval_traces/a10_graph_diagnostics_trace.json` that:
   - `silent_fallbacks_detected == 0`
   - `status == "APPLIED"` for both `SQLiteStorageEngine` and `FileStorageEngine`
   - `exception_type is None`

### Luna Task L10 Verification Protocol
Luna can execute `cognitive_core/tests/test_graph_diagnostics_probe.py`:
- Proves independently that on current main (`e43cc81e`), SQLite and File storage engines fail silently on `.store`.
- Validates candidate rejection attribution without manual SQLite inspection.

---

## 6. Canonical Status Summary

| Area | Prior Claim | A10 Empirical Finding | Epistemic Classification |
|---|---|---|---|
| Production Graph Status | "Graph reranking active" | Crashes silently on `.store`; probe catches 100% of crashes | `RUNTIME_VERIFIED` |
| Candidate Rejection | Opaque empty list or slice | Segmented by `RAW`, `SUPERSEDED`, and `SCORE` | `RUNTIME_VERIFIED` |
| Score Preservation | "Scores used in reranking" | Stripped by `ContextPackBuilder`; `0.0%` survived | `RUNTIME_VERIFIED` |
| Fallback Visibility | Swallowed without trace | Surfaced with full traceback and execution status enum | `RUNTIME_VERIFIED` |
