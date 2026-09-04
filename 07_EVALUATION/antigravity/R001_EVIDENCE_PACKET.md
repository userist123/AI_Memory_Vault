# AI MEMORY VAULT — ANTIGRAVITY R001 EVIDENCE PACKET

```text
ROUND_ID: R001
BASELINE_SHA: 2c3126876cbe579afb227615b4b6c4d4048b6d42
AGENT: ANTIGRAVITY
LANE: OBSERVE / EXPLAIN
WORKING_BRANCH: antigravity/observability-v1
LATEST_COMMIT_SHA: 44b4fea2b6510a7cb54df7b4c910fa8c8230538f
STATUS: READY
```

---

## 1. Task Inventory & Completed Milestones

| Task ID | Description | Primary Output Artifact | Evidence Level | Status |
|---|---|---|---|---|
| **A1** | 14-Step Retrieval Trace & Dual-Pipeline Walkthrough | `07_EVALUATION/antigravity/RETRIEVAL_TRACE_V1.md` | `RUNTIME_VERIFIED` | `COMPLETED` |
| **A2** | Score Distributions Across 7 Query Archetypes | `07_EVALUATION/antigravity/SCORE_DISTRIBUTION_V1.md` | `RUNTIME_VERIFIED` | `COMPLETED` |
| **A3** | Graph / Activation Controlled Visualization | `07_EVALUATION/antigravity/RETRIEVAL_DASHBOARD_R001.md` | `RUNTIME_VERIFIED` | `COMPLETED` |
| **A4** | Lifecycle Census (905 Markdown, 49 SQLite) | `07_EVALUATION/antigravity/LIFECYCLE_VISUALIZATION_V1.md` | `RUNTIME_VERIFIED` | `COMPLETED` |
| **A5** | Memory-Use to Execution Outcome Tracer (4 Tiers) | `cognitive_core/observability/memory_outcome_tracer.py` | `RUNTIME_VERIFIED` | `COMPLETED` |
| **A6** | Living Architecture Gap Register (`GAP-001`..`GAP-012`) | `07_EVALUATION/antigravity/ARCHITECTURE_GAP_REGISTER.md` | `CODE_VERIFIED` | `COMPLETED` |
| **A7** | Associative Differential Trace & Dual-Reality Reconciliation | `07_EVALUATION/antigravity/A7_ASSOCIATIVE_DIFFERENTIAL_TRACE.md` | `RUNTIME_VERIFIED` | `COMPLETED` |
| **BL** | Held-Out Retrieval Baseline (14 Cases, 7 Classes) | `07_EVALUATION/antigravity/RETRIEVAL_METRICS_BASELINE.md` | `RUNTIME_VERIFIED` | `COMPLETED` |

---

## 2. Timing & Execution Evidence

- **Started At**: `2026-09-04T17:00:00+03:00`
- **Completed At**: `2026-09-04T17:40:00+03:00`
- **Commands / Execution Methods**:
  1. `python -m cognitive_core.observability.trace_cli --query "..." --ab-activation --outcomes`
  2. `python scratch/run_benchmark_baseline.py`
  3. `python scratch/run_a7_differential_trace.py`
  4. `python -m pytest cognitive_core/tests/test_retrieval_observability.py -v` (5/5 passed in 1.43s)
  5. Master regression: 809 passed, 2 skipped in 21.35s across `cognitive_core` and `memory_controller`

---

## 3. Changed & Delivered Files

### Production Observability Engine (`cognitive_core/observability/`):
- `retrieval_tracer.py`: 14-step telemetry recorder with recursive secret redaction and JSON/Markdown export.
- `ab_comparison_engine.py`: Controlled A/B measurement engine with rank shift calculation, Kendall $\tau$, Spearman $\rho$, and lifecycle degradation multipliers.
- `memory_outcome_tracer.py`: 4-tier execution trace scanner (`UNUSED`, `REFERENCED`, `FUNCTIONAL`, `CAUSAL`).
- `benchmark_evaluator.py`: Quantitative benchmark evaluator (P@K, R@K, MRR, FPR, Abstention Accuracy).
- `trace_cli.py`: Interactive developer CLI.
- `__init__.py`: Public exports.

### Unit Tests:
- `cognitive_core/tests/test_retrieval_observability.py`: 5 deterministic unit tests covering tracer, A/B engine, lifecycle degradation, outcome tracer, and benchmark evaluator.

### Evaluation Artifacts (`07_EVALUATION/antigravity/`):
1. `RETRIEVAL_TRACE_V1.md`
2. `SCORE_DISTRIBUTION_V1.md`
3. `LIFECYCLE_VISUALIZATION_V1.md`
4. `BOOK_KNOWLEDGE_MAP_V1.md`
5. `ASSOCIATIVE_MEMORY_AUDIT_V1.md`
6. `OBSERVABILITY_GAPS_V1.md`
7. `RETRIEVAL_DASHBOARD_R001.md`
8. `RETRIEVAL_METRICS_BASELINE.md`
9. `A7_ASSOCIATIVE_DIFFERENTIAL_TRACE.md`
10. `ARCHITECTURE_GAP_REGISTER.md`

### Raw Telemetry Records (`telemetry/retrieval_traces/`):
- `benchmark_baseline_summary.json`
- `a7_differential_trace.json`

---

## 4. Key Empirical Findings by Epistemic Level

### 4.1 RUNTIME_VERIFIED
1. **The Dual-Reality Gap**:
   - In a controlled in-memory corpus, spreading activation is **behaviorally operational**: alters ranking ($\tau = 0.80 - 1.00$, $\rho = 0.50 - 0.90$, mean delta = $0.40 - 0.80$), pulls unretrieved multi-hop neighbors into the top-10, and preserving edge weights introduces secondary rank divergence ($\tau = 0.7778 - 0.9111$).
   - In the production runtime (`SQLiteStorageEngine` and `FileStorageEngine`), spreading activation is **behaviorally dead/disconnected**: `ranked_search.py` crashes on `build_multi_graph()` because neither production engine exposes `.store`. An unhandled `AttributeError` is silently caught by `except Exception: return results[:top_k]`, returning unranked base results for 100% of queries (`GAP-012`).
2. **Relevance Score Stripping**: `ContextPackBuilder.build()` strips the float `relevance_score` calculated by `RelevanceScorer`. Downstream consumers receive zero score visibility and are forced to invent synthetic weights like `1.0 / (idx + 1)` (`GAP-002`).
3. **Lexical Baseline Clifford**:
   - `exact_relevant` and `paraphrase`: P@1 = 100%, MRR = 1.0000.
   - `synonym`: P@1 = 0.0%, MRR = 0.0000 (0% token overlap $\to$ complete false negative).
   - `lexical_trap`: FPR = 28.6% - 50.0% (accidental keyword match defeats threshold).
4. **Memory-to-Outcome Reality ($N=120$ retrieved memories across 44 real LLM traces)**:
   - `RETRIEVED_AND_UNUSED`: **90 (75.0%)** — Dead-weight prompt context.
   - `RETRIEVED_AND_FUNCTIONAL`: **30 (25.0%)** — Verifiable code constraints.

### 4.2 CODE_VERIFIED
1. **Edge-Weight Variable Overwrite (`GAP-010`)**: In `spreading_activation.py` line 37, `propagated` is calculated with weight normalization, but line 38 immediately overwrites it with `score * (decay ** (hop + 1))`. Edge weight magnitude is completely ignored in baseline.
2. **Classifier Substring Trap (`GAP-011`)**: `QueryClassifier.classify()` checks `if stage in lowered`. Searching for `"unverified"` matches `"verified"`, forcing `lifecycle_filters=['VERIFIED']` and dropping 100% of candidate unverified/review notes.
3. **Template Cloning in Derived Notes**: All 10 book synthesis atoms in `06_INBOX/DERIVED/BOOKS/` share identical metadata metrics (`confidence: 0.85`, `validity: 0.90`, `misleading_risk: 0.15`).

---

## 5. Failures & Blockers Encountered

1. **Storage Engine `.store` Disconnect**: Prevented running `ranked_search()` directly against the live `vault_memory.sqlite3` or disk files without an adapter.
2. **Missing Top-Level `evaluation` Package**: Explains Codex's blocker in `R001_C2_HELDOUT_FORENSICS.md` (legacy scripts in `07_EVALUATION/retrieval_fusion/` imported `evaluation.*`). Antigravity bypassed this by implementing a self-contained evaluator in `cognitive_core/observability/benchmark_evaluator.py`.
3. **Pagination HMAC Secret**: `MemoryController.search()` raises `MissingHMACSecretError` if `MEMORY_CONTROLLER_HMAC_SECRET` is not set in the environment ($< 32$ chars).

---

## 6. Open Questions for Barrier Reconciliation

1. **Graph Construction API**: Should `build_multi_graph(controller)` iterate over all notes in SQLite/File storage via a public query method, or should graph construction be bounded strictly to the retrieved candidate set?
2. **Context Pack Score Exposure**: Should `ContextPackBuilder` be updated to preserve `relevance_score: float` so downstream re-ranking can use true lexical similarity instead of reciprocal rank?
3. **Prompt Boundary Tagging**: How should `RealAgentExecutionHarness` and `ContextPackBuilder` demarcate `REVIEW` / untrusted memory to prevent prompt injection from affecting agent action execution?

---

## 7. Next Recommendation for R002

1. **For CODEX**:
   - Fix `build_multi_graph()` in `cognitive_core/ranked_search.py` to use a public storage query interface instead of private `.store`.
   - Preserve `relevance_score` in `ContextPackBuilder`.
   - Fix `QueryClassifier` keyword matching using word boundaries `\b` (`GAP-011`).
2. **For LUNA**:
   - Formally verify `A7_ASSOCIATIVE_DIFFERENTIAL_TRACE.md` and `a7_differential_trace.json`.
   - Attack `QueryClassifier` with adversarial sub-tokens (`unverified`, `non-archived`).
   - Reconcile lane findings at `09_COORDINATION/rounds/R001/BARRIER.md`.
3. **For ANTIGRAVITY**:
   - Provide prompt-boundary observability instruments measuring whether untrusted `REVIEW` notes can alter model-produced tool actions.
