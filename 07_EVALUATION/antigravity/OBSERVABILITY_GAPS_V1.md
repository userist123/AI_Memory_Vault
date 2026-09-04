# Observability Gaps & Instrumentation Recommendations V1 (Antigravity Observability)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Date**: 2026-09-04  
**Status**: `CODE_VERIFIED` / `DOCUMENT_VERIFIED`  

---

## 1. Executive Summary

This report documents architectural and operational opacity gaps in the AI Memory Vault runtime where execution is functional but developer visibility is impaired. Per our operating contract, we recommend concrete instrumentation patterns rather than modifying runtime behavior.

---

## 2. Identified Observability Gaps

### Gap 1: Opaque Candidate Rejection in `MemoryController.search()` [`CODE_VERIFIED`]
- **Current Reality**: When `controller.search()` executes, it applies query classification, filters by lifecycle, scores relevance, sorts, and slices by `page_size`. The resulting `ContextPack` only contains admitted notes.
- **Opacity**: Developers cannot see how many candidates were considered, which notes were rejected, or why a specific note was excluded (e.g. filtered by lifecycle vs low relevance score vs budget exhaustion).
- **Recommended Instrumentation**:
  Add an optional diagnostic metadata block to `ContextPack`:
  ```json
  "diagnostics": {
    "candidates_considered": 48,
    "filtered_by_lifecycle": 12,
    "filtered_by_budget": 3,
    "rejection_summary": [
      {"id": "note_123", "reason": "LIFECYCLE_RAW_EXCLUDED"},
      {"id": "note_456", "reason": "SCORE_BELOW_THRESHOLD"}
    ]
  }
  ```

---

### Gap 2: Unavailable Multi-Signal Score Breakdown [`CODE_VERIFIED`]
- **Current Reality**: In `RelevanceScorer.score()`, only the single composite float `score` is returned:
  $$\text{score} = \frac{\text{overlap\_ratio} + \text{confidence}}{2}$$
  In `RecallEngine.recall()`, only `(node, final_score)` tuples are returned.
- **Opacity**: A developer or evaluator cannot inspect how much of the score came from semantic similarity vs working-memory relevance vs recency vs authority.
- **Recommended Instrumentation**:
  Store a structured breakdown dict on the returned node or trace:
  ```python
  node["_score_breakdown"] = {
      "semantic": 0.2275,
      "wm_relevance": 0.0,
      "confidence": 0.0600,
      "activation": 0.0,
      "authority": 0.1000,
      "lifecycle_factor": 1.0,
      "final": 0.3875
  }
  ```

---

### Gap 3: Invisible Supersession Lineage Resolution in API [`CODE_VERIFIED`]
- **Current Reality**: `resolve_active_lineage()` traverses `superseded_by` chains and promotes active successor notes.
- **Opacity**: The consuming agent receives the active note but has no indication that it was surfaced because an older, superseded note matched the query keywords.
- **Recommended Instrumentation**:
  Add a `lineage_provenance` field to the admitted note:
  ```json
  "lineage_provenance": {
    "surfaced_via_superseded_id": "SUPERSEDED-NET-001",
    "lineage_hops": 1,
    "original_query_match": "synchronous socket server"
  }
  ```

---

### Gap 4: Disconnection of Multi-Graph Spreading Activation [`CODE_VERIFIED`]
- **Current Reality**: `cognitive_core/spreading_activation.py` and `cognitive_core/multi_graph.py` exist and pass unit tests, but operate as an isolated island disconnected from `MemoryController.search()` and `RecallEngine`.
- **Opacity**: Looking at the codebase gives the false impression that spreading activation is active during agent execution.
- **Recommended Instrumentation**:
  Expose an explicit capability registry flag:
  ```yaml
  retrieval_pipeline:
    semantic_search: "active"
    progressive_disclosure: "active"
    spreading_activation: "experimental_offline_only"
    multi_graph_indexing: "derived_offline"
  ```

---

### Gap 5: Component Timing Telemetry Missing in Production Search [`CODE_VERIFIED`]
- **Current Reality**: `RealAgentExecutionHarness` captures total execution time and model inference latency, but `MemoryController.search()` does not record internal phase timings.
- **Opacity**: Developers cannot profile whether classification, database fetching, scoring, or serialization is the primary latency bottleneck.
- **Recommended Instrumentation**:
  Record microsecond phase timings in audit events:
  ```python
  audit_details = {
      "t_sanitize_us": 45,
      "t_classify_us": 120,
      "t_storage_fetch_us": 1250,
      "t_scoring_us": 340,
      "t_disclosure_us": 80
  }
  ```

---

### Gap 6: Coarse-Grained Abstention Diagnostics [`CODE_VERIFIED`]
- **Current Reality**: When `best_pre_lifecycle_score < self.abstention_threshold`, `RecallEngine` returns `[]`.
- **Opacity**: The caller cannot distinguish between "no records matched the search terms" versus "matching records were found but scored 0.198 (just below 0.200 threshold)".
- **Recommended Instrumentation**:
  Return an abstention audit record in the trace:
  ```json
  "abstention": {
    "triggered": true,
    "best_score": 0.1975,
    "threshold": 0.2000,
    "nearest_candidate": "M-RELIABILITY-001"
  }
  ```
