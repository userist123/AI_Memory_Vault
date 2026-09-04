# Living Architecture & Observability Gap Register (R001)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Date**: 2026-09-04  
**Epistemic Baseline**: `CODE_VERIFIED` / `TEST_VERIFIED`  

---

## Gap Inventory Summary

| Gap ID | Component | Severity | Description | Codex Build Item | Luna Verify Item |
|---|---|---|---|---|---|
| `GAP-001` | `MemoryController` | **HIGH** | Opaque candidate rejection without diagnostics | C1 / C7 | L1 / L6 |
| `GAP-002` | `RecallEngine` | **HIGH** | Scalar composite score without component attribution | C1 / C7 | L6 |
| `GAP-003` | `multi_graph.py` | **CRITICAL** | Associative multi-graph is an isolated test island | C4 | L4 |
| `GAP-004` | `spreading_activation.py` | **MEDIUM** | Decay formula never invoked in live search | C4 | L4 |
| `GAP-005` | `supersession.py` | **MEDIUM** | Invisible lineage promotion in context packs | C1 / C5 | L5 |
| `GAP-006` | `RecallEngine` | **MEDIUM** | Coarse-grained abstention (empty list return) | C1 / C7 | L6 |
| `GAP-007` | `learning_loop` | **CRITICAL** | Execution outcomes are telemetry-only | C6 | L7 |
| `GAP-008` | `RelevanceScorer` | **HIGH** | Token-overlap failure on synonyms & lexical traps | C2 | L2 / L3 |

---

## Detailed Gap Specifications

### GAP-001: Opaque Candidate Rejection
* **Affected Component**: [`memory_controller/controller.py:search()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/memory_controller/controller.py#L234)
* **Current Behavior**: Excluded candidates are silently dropped during lifecycle filtering, scoring cutoffs, and page size slicing.
* **Opacity Hazard**: Evaluators cannot distinguish between notes rejected due to security policy (`RAW`), low similarity, or budget exhaustion.
* **Proposed Interface**: Return optional `diagnostics: Dict[str, Any]` inside `ContextPack` detailing candidate rejection causes.
* **Codex Acceptance Criterion**: `assert "rejection_summary" in result["diagnostics"]`.
* **Luna Attack Verification**: Submit queries expected to match `RAW` notes; verify diagnostic reports `LIFECYCLE_RAW_EXCLUDED` while note content is completely absent from payload.

---

### GAP-002: Scalar Composite Score Without Component Attribution
* **Affected Component**: [`cognitive_core/recall.py:recall()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/recall.py#L90) & [`memory_controller/relevance.py`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/memory_controller/relevance.py)
* **Current Behavior**: Return single float score ($0.3875$).
* **Opacity Hazard**: Cannot inspect whether a note won Rank 1 due to high semantic relevance, working memory alignment, or stale authority.
* **Proposed Interface**: Attach `_score_breakdown: Dict[str, float]` containing `semantic`, `wm_relevance`, `confidence`, `activation`, `authority`, `temporal`, `lifecycle_factor`.
* **Codex Acceptance Criterion**: `assert sum(breakdown.values()) == pytest.approx(final_score)`.
* **Luna Attack Verification**: Inject high-confidence distractor with zero semantic similarity; verify score breakdown isolates confidence boost from semantic score.

---

### GAP-003: Associative Multi-Graph Is an Isolated Test Island
* **Affected Component**: [`cognitive_core/multi_graph.py`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/multi_graph.py)
* **Current Behavior**: `MultiGraphMemory` supports 4 orthogonal graphs (`semantic`, `temporal`, `causal`, `entity`), but is never called by `MemoryController.search()`.
* **Opacity Hazard**: Claims of "GraphRAG" or "multi-graph memory" in documentation are not backed by runtime execution.
* **Proposed Interface**: Provide a `MultiGraphRetriever` adapter that expands candidate sets by 1 hop along `causal` and `semantic` edges.
* **Codex Acceptance Criterion**: `assert len(retriever.expand_1hop(candidates)) > len(candidates)`.
* **Luna Attack Verification**: Run A/B test with and without graph expansion on multi-hop query; verify rank changes and budget enforcement.

---

### GAP-004: Spreading Activation Decay Never Invoked in Live Search
* **Affected Component**: [`cognitive_core/spreading_activation.py`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/spreading_activation.py)
* **Current Behavior**: `SpreadingActivationEngine.rank()` exists only in unit tests.
* **Opacity Hazard**: Activation dynamics do not influence real agent execution.
* **Proposed Interface**: Connect `ActivationTracker` access events to `SpreadingActivationEngine` when `activation_mode="act_r"`.
* **Codex Acceptance Criterion**: Repeated access to Note A increases retrieval rank of connected Note B.
* **Luna Attack Verification**: Verify decay over simulated time to ensure runaway positive feedback does not permanently pin primed notes.

---

### GAP-005: Invisible Lineage Promotion in Context Packs
* **Affected Component**: [`cognitive_core/recall.py:resolve_active_lineage()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/recall.py#L182)
* **Current Behavior**: Superseded note is matched, but active successor is returned without provenance annotation.
* **Opacity Hazard**: Consuming LLM cannot understand why an active note without matching query keywords was included in context.
* **Proposed Interface**: Add `lineage_origin: {"matched_superseded_id": str, "hop_distance": int}` to the returned note dictionary.
* **Codex Acceptance Criterion**: Promoted active notes carry `lineage_origin` metadata.
* **Luna Attack Verification**: Query for superseded keyword; verify active note is returned AND lineage origin metadata matches superseded ID.

---

### GAP-006: Coarse-Grained Abstention
* **Affected Component**: [`cognitive_core/recall.py:recall()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/recall.py#L210)
* **Current Behavior**: When `best_pre_score < threshold`, returns empty list `[]`.
* **Opacity Hazard**: Cannot diagnose whether retrieval failed due to vocabulary mismatch, empty candidate pool, or temporal expiration.
* **Proposed Interface**: Return structured `RecallResult(notes=..., abstained=True, abstention_reason="...")`.
* **Codex Acceptance Criterion**: `result.abstained is True` and `result.abstention_reason == "BEST_SCORE_BELOW_THRESHOLD"`.
* **Luna Attack Verification**: Test edge cases with scores $0.199$ vs $0.201$; verify exact threshold boundary behavior.

---

### GAP-007: Execution Outcomes Are Telemetry-Only
* **Affected Component**: [`cognitive_core/real_execution_harness.py`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/real_execution_harness.py)
* **Current Behavior**: Execution traces write `trace_*.json` to disk, but no feedback modifies future memory retrieval or note confidence.
* **Opacity Hazard**: Learning loop is open; the system cannot improve from repeated experience.
* **Proposed Interface**: Implement `OutcomeFeedbackBridge` connecting verified execution successes to `ActivationTracker` and proposing `REVIEW` notes.
* **Codex Acceptance Criterion**: Passed execution generates a verified outcome event ready for consolidation.
* **Luna Attack Verification**: Test failed execution; verify failed outcome does NOT boost note confidence or promote unverified lessons.

---

### GAP-008: Lexical Scorer Failure on Synonyms & Lexical Traps
* **Affected Component**: [`memory_controller/relevance.py:RelevanceScorer`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/memory_controller/relevance.py)
* **Current Behavior**: Uses Jaccard token overlap between query and note text.
* **Opacity Hazard**: 0% score on conceptual synonyms (false negative); high scores on lexical traps (false positive).
* **Proposed Interface**: Integrate dense local embedding / hybrid scoring (`sim = 0.5 * bm25 + 0.5 * dense`).
* **Codex Acceptance Criterion**: Synonym queries achieve score $> 0.30$ and avoid abstention.
* **Luna Attack Verification**: Run Hard-Negative lexical trap suite; verify hybrid scorer discriminates semantic intent from surface keywords.
