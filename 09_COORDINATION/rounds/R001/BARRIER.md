# R001 — Official Evidence Barrier

## Round Overview

- **Round ID**: `R001`
- **Actual Main Baseline SHA**: `2c3126876cbe579afb227615b4b6c4d4048b6d42`
- **Barrier Timestamp**: `2026-09-04T17:48:00+03:00`
- **Working Branches Reconciled**:
  - `codex/r001-retrieval-v2` (`adba7135`, `6a00c6c5`) & `codex/r001-heldout-v1` (`b2f2e981`)
  - `antigravity/observability-v1` (`176cf8164`)
  - `luna/r001-independent-audit-20260904` (`821cb964`)

---

## Lane Status Summary

| Lane | Status | Evidence Artifact | Latest Commit | Test / Execution State |
|---|---|---|---|---|
| **CODEX** | `PARTIAL` | [`07_EVALUATION/codex/R001_C2_HELDOUT_FORENSICS.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/codex/R001_C2_HELDOUT_FORENSICS.md) | `b2f2e981` | Edge weight fix passes 11 unit tests; held-out runner blocked on entrypoint |
| **ANTIGRAVITY** | `READY` | [`07_EVALUATION/antigravity/R001_EVIDENCE_PACKET.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/antigravity/R001_EVIDENCE_PACKET.md) | `176cf8164` | 5/5 unit tests pass in 1.43s; 809 passed master regression; full trace + A/B + gap register |
| **PERPLEXITY** | `READY` | [`09_COORDINATION/prompts/PERPLEXITY_CONTINUATION_V1.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/09_COORDINATION/prompts/PERPLEXITY_CONTINUATION_V1.md) | n/a | Research criteria for hybrid retrieval, instruction/data isolation, CombMNZ |
| **LUNA** | `READY` | [`07_EVALUATION/luna/R001_INDEPENDENT_AUDIT.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/luna/R001_INDEPENDENT_AUDIT.md) | `821cb964` | Static code falsifications complete; local execution container blocked on network name resolution |

---

## Evidence Classification & Reconciled Findings

### 1. Confirmed Across Multiple Lanes

1. **Dual-Reality of Spreading Activation (`GAP-012`)**:
   - *In Controlled Laboratory (In-Memory)*: Spreading activation is **behaviorally operational**. It produces significant rank shifts ($\tau = 0.80 - 1.00$, $\rho = 0.50 - 0.90$, mean delta = $0.40 - 0.80$), successfully expands candidate sets by pulling unretrieved multi-hop neighbors into the top-10, and preserving edge weights introduces a secondary rank divergence ($\tau = 0.7778 - 0.9111$). (`RUNTIME_VERIFIED` by Antigravity).
   - *In Production Runtime (SQLite / Filesystem)*: Spreading activation is **behaviorally dead/disconnected**. `build_multi_graph()` in `ranked_search.py` hardcodes `controller.storage.store.values()`. Neither `SQLiteStorageEngine` nor `FileStorageEngine` exposes `.store`. An unhandled `AttributeError` is silently caught by `except Exception: return results[:top_k]`, returning unranked base results for 100% of real queries. (`RUNTIME_VERIFIED` by Antigravity; `CODE_VERIFIED` by Luna).
2. **Edge-Weight Variable Overwrite (`GAP-010`)**:
   - In `spreading_activation.py` line 37, `propagated` calculates weight normalization, but line 38 immediately overwrites it with `score * (decay ** (hop + 1))`. Edge weight magnitude is completely ignored in baseline. (`CODE_VERIFIED` by Luna Finding R001-L3-05; `RUNTIME_VERIFIED` by Codex commit `adba7135` and Antigravity A7 trace).
3. **Relevance Score Stripping in Context Packs (`GAP-002`)**:
   - `ContextPackBuilder.build()` strips the float `relevance_score` calculated by `RelevanceScorer`. Downstream consumers (including `ranked_search.py`) receive zero score visibility and must invent synthetic reciprocal rank seeds (`1.0 / (idx + 1)`). (`RUNTIME_VERIFIED` by Antigravity).
4. **Lexical Retrieval Cliff & Synonym Failure (`GAP-008`)**:
   - Candidate generation and scoring are purely lexical token overlap / Jaccard. Exact and paraphrase queries achieve P@1 = 100%, MRR = 1.0, but synonym queries achieve P@1 = 0%, MRR = 0.0 (total false negative), and lexical traps exhibit high false positive rates (28.6% - 50.0%). (`RUNTIME_VERIFIED` by Antigravity; `CODE_VERIFIED` by Luna Finding R001-L3-01..03).
5. **Outcome Loop Is Open Telemetry Only (`GAP-007`)**:
   - `scripts/label_council_outcome.py` and `telemetry/` write JSONL records, but no automatic mechanism consumes outcome events to promote memories or close the learning loop. (`CODE_VERIFIED` by Luna Finding R001-L7; `RUNTIME_VERIFIED` by Antigravity).

### 2. Blocked / Partially Confirmed Items

1. **Held-Out Benchmark Entrypoint Blocker**:
   - Codex reported `ModuleNotFoundError: No module named 'evaluation'` in `07_EVALUATION/codex/R001_C2_HELDOUT_FORENSICS.md` due to the directory rename from `evaluation/` to `07_EVALUATION/`.
   - *Status*: Antigravity resolved this in the observability lane by providing `RetrievalBenchmarkEvaluator` (`cognitive_core/observability/benchmark_evaluator.py`), which imports `memory_controller` cleanly.
2. **Luna Runtime Execution Container**:
   - Luna's local test execution was blocked because GitHub DNS name resolution failed inside the execution container. Luna therefore relied on static code inspection (`CODE_VERIFIED`) without claiming unverified local test runs.

### 3. New Defects & Gaps Discovered During R001

1. **`GAP-011` (QueryClassifier Substring Trap)**:
   - `QueryClassifier.classify()` searches `if stage in lowered`. Searching for `"unverified"` matches `"verified"`, setting `lifecycle_filters = ['VERIFIED']` and dropping 100% of candidate unverified / review notes.
2. **`GAP-012` (Production `.store` Disconnect)**:
   - `build_multi_graph(controller)` hardcodes `.storage.store`, crashing on production SQLite and File storage engines and silently falling back to unranked base results.

---

## Security Gate Audit

- [x] **No security invariant weakened**: `I-001` through `I-012` and `I-RETRIEVAL` remain 100% intact.
- [x] **No REVIEW -> ACTIVE promotion for benchmark purposes**: All unverified notes remain strictly in `REVIEW` or `RAW`.
- [x] **Provenance preserved**: All test notes and evaluation traces retain explicit provenance and source tracking.
- [x] **Lifecycle semantics preserved**: `_cognitive_unverified` and `REVIEW` boundaries are enforced.
- [x] **No fabricated test/runtime/CI evidence**: All empirical metrics are backed by committed JSON telemetry and passing pytest executions.

---

## Integration Decisions for R002

| Proposed Change | Author / Branch | Reproduced? | Security-Safe? | Tests Passing | Integration Decision |
|---|---|---|---|---|---|
| **Preserve Edge Weights in Spreading Activation** | Codex (`adba7135`) | **YES** ($\tau = 0.7778 - 0.9111$) | **YES** (no storage mutation) | 11 unit tests pass | **ACCEPT FOR MAIN MERGE** |
| **Fix Production Storage Query in `build_multi_graph`** | Codex / Antigravity (`GAP-012`) | **YES** (AttributeError verified) | **YES** (read-only query) | New tests required | **DISPATCH FOR R002 BUILD** |
| **Fix Word-Boundary Matching in `QueryClassifier`** | Codex / Antigravity (`GAP-011`) | **YES** (Substring trap verified) | **YES** (classification only) | New tests required | **DISPATCH FOR R002 BUILD** |
| **Expose `relevance_score` in `ContextPackBuilder`** | Codex / Antigravity (`GAP-002`) | **YES** (Score stripping verified) | **YES** (diagnostics exposure) | Regression suite | **DISPATCH FOR R002 BUILD** |

---

## R002 Candidate Backlog

1. **C1/A1 — Production Multi-Graph Storage Connector**: Enable `build_multi_graph(controller)` to query `SQLiteStorageEngine` and `FileStorageEngine` safely without private `.store` access.
2. **C2/L2 — Unified Held-Out Retrieval Suite**: Replace broken `evaluation/` imports with `RetrievalBenchmarkEvaluator` and run formal held-out benchmarks.
3. **C3/L3 — Prompt Demarcation for Untrusted REVIEW Memories**: Wrap `CONTEXT MEMORIES` in strict XML tags (`<untrusted_memory>`) and evaluate resilience against prompt injection payloads.
4. **C4/P2 — Hybrid Lexical + Local Dense Embedding Search**: Implement deterministic hybrid candidate scoring to eliminate the synonym lexical cliff (synonym P@1: 0% $\to$ > 50%).
5. **C5/A5 — Closed-Loop Learning Bridge**: Connect verified execution outcome records to `ActivationTracker` and review candidate generation with human attestation gating.

---

## Sign-Off & Epistemic Verdict

Round **R001 is officially CLOSED at the Evidence Barrier**.
The parallel execution successfully converted structural module claims into precise empirical evidence, exposed the Dual-Reality gap, fixed the edge-weight overwrite, and formulated clear, non-overlapping tasks for R002.
