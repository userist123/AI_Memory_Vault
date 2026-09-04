# ANTIGRAVITY R001 — NEXT TASK A10

**Round**: R001  
**Lane**: ANTIGRAVITY (Developer-Observability & Architecture Inspection)  
**Current Main SHA**: `e43cc81e09789e284ef35a7e326297194f429a9e`  
**Working Branch**: `antigravity/observability-v1`  
**Completed Task**: `A9 (Review Memory Authority & Prompt Demarcation Observability)`  
**Completion Evidence**: `07_EVALUATION/antigravity/A9_PROMPT_DEMARCATION_OBSERVABILITY.md`, `telemetry/retrieval_traces/a9_prompt_demarcation_trace.json` (Commit `e10481420`)  
**Result Classification**: `RUNTIME_VERIFIED`  
**Next Task ID**: `A10`  

---

## 1. Mission & Objective

Build the **Production Graph Observability Probe & Candidate Diagnostics Suite** (`A10`) to provide end-to-end trace visibility into:
1. **Production Graph Execution Status & Fallback Diagnostics (`GAP-012`, `GAP-014`)**:
   - Provide non-invasive observability into `ranked_search.py` execution across all 3 storage engines (`InMemoryStorageEngine`, `SQLiteStorageEngine`, `FileStorageEngine`).
   - Distinguish and record the 3 execution states: `GRAPH_AVAILABLE_AND_APPLIED`, `GRAPH_UNAVAILABLE_DETERMINISTIC_FALLBACK`, and `GRAPH_FAILED_EXCEPTION` with full exception details and stack traces.
   - Eliminate the silent masquerade (`GAP-014`) from the observability layer without modifying core scoring logic.
2. **Candidate Rejection & Diagnostic Attribution (`GAP-001`, `GAP-002`)**:
   - Instrument the candidate filtering pipeline to report exact rejection reasons (`LIFECYCLE_RAW_EXCLUDED`, `SCORE_BELOW_THRESHOLD`, `BUDGET_EXHAUSTED`, `PRINCIPAL_UNAUTHORIZED`).
   - Track score component preservation (`relevance_score`, `activation_score`, `authority_score`).
3. **Observability Verification for Codex C9 & Luna L10**:
   - Produce automated probe harness and test suite validating that any future graph repair (e.g. from Codex C9) can be verified instantaneously across engines with zero silent failures.

---

## 2. Scope & Target Areas

- Component: `cognitive_core/observability/graph_diagnostics_probe.py`
- Test Suite: `cognitive_core/tests/test_graph_diagnostics_probe.py`
- Telemetry: `telemetry/retrieval_traces/a10_graph_diagnostics_trace.json`
- Evaluation Report: `07_EVALUATION/antigravity/A10_GRAPH_DIAGNOSTICS_OBSERVABILITY.md`

---

## 3. Do Not Change (Hard Invariants)

- **DO NOT** modify core memory trust boundaries (`I-001..I-012`, `I-RETRIEVAL`).
- **DO NOT** unilaterally modify production `ranked_search.py` or `MemoryController.search()` core retrieval scoring; keep the probe non-invasive or observational.
- **DO NOT** write to `07_EVALUATION/codex/`, `07_EVALUATION/luna/`, or `07_EVALUATION/perplexity/`.
- **DO NOT** silently promote `REVIEW` memories to `ACTIVE`.

---

## 4. Required Evidence

Create and publish:
1. `07_EVALUATION/antigravity/A10_GRAPH_DIAGNOSTICS_OBSERVABILITY.md` containing:
   - BASELINE_SHA
   - Diagnostics Schema Specification
   - 3-Engine Empirical Probe Results
   - Fallback Detection & Masquerade Neutralization Proof
   - Rejection Reason Attribution Audit
   - Coordination Handoff to Codex C9 & Luna L10
2. `telemetry/retrieval_traces/a10_graph_diagnostics_trace.json` (Machine-readable empirical trace).
3. `cognitive_core/tests/test_graph_diagnostics_probe.py` (Passing test suite).

---

## 5. Exit Condition

- Probe cleanly detects and records graph execution states (`APPLIED`, `FALLBACK`, `FAILED`) across `InMemoryStorageEngine`, `SQLiteStorageEngine`, and `FileStorageEngine`.
- Candidate rejection attribution correctly identifies reasons for excluded notes.
- Test suite passes with 100% success rate.
- Artifacts committed and pushed to `origin/antigravity/observability-v1`.
