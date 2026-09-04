# Living Architecture & Observability Gap Register (R001)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Date**: 2026-09-04  
**Baseline SHA**: `2c3126876cbe579afb227615b4b6c4d4048b6d42`  
**Epistemic Baseline**: `RUNTIME_VERIFIED` / `CODE_VERIFIED` / `TEST_VERIFIED`  

---

## Gap Inventory Summary

| Gap ID | Component | Severity | Description | Codex Build Item | Luna Verify Item |
|---|---|---|---|---|---|
| `GAP-001` | `MemoryController` | **HIGH** | Opaque candidate rejection without diagnostics | C1 / C7 | L1 / L6 |
| `GAP-002` | `RecallEngine` / `PackBuilder` | **HIGH** | Scalar composite score without component attribution; score stripped from context pack | C1 / C7 | L6 |
| `GAP-003` | `multi_graph.py` | **CRITICAL** | Associative multi-graph is an isolated test island | C4 | L4 |
| `GAP-004` | `spreading_activation.py` | **MEDIUM** | Decay formula never invoked in live search | C4 | L4 |
| `GAP-005` | `supersession.py` | **MEDIUM** | Invisible lineage promotion in context packs | C1 / C5 | L5 |
| `GAP-006` | `RecallEngine` | **MEDIUM** | Coarse-grained abstention (empty list return) | C1 / C7 | L6 |
| `GAP-007` | `learning_loop` | **CRITICAL** | Execution outcomes are telemetry-only | C6 | L7 |
| `GAP-008` | `RelevanceScorer` | **HIGH** | Token-overlap failure on synonyms & lexical traps | C2 | L2 / L3 |
| `GAP-009` | `FileStorageEngine` | **HIGH** | Synthesis atoms in `06_INBOX/` invisible to `search()` | C1 / C3 | L1 / L5 |
| `GAP-010` | `spreading_activation.py` | **HIGH** | Edge weight calculation neutralized by immediate overwrite on L38 | C4 | L4 |
| `GAP-011` | `QueryClassifier` | **CRITICAL** | Substring matching in `QueryClassifier` induces inverted lifecycle filtering | C1 | L1 / L3 |
| `GAP-012` | `ranked_search.py` | **CRITICAL** | Hardcoded `.store` dictionary access crashes on production SQLite and File engines | C1 / C4 | L1 / L4 |

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

### GAP-002: Scalar Composite Score Stripping in Context Packs
* **Affected Component**: [`memory_controller/context/pack_builder.py:build()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/memory_controller/context/pack_builder.py)
* **Current Behavior**: `RelevanceScorer` calculates a float score, but `ContextPackBuilder.build()` strips the score from the returned note dictionaries.
* **Opacity Hazard**: Downstream consumers (including `ranked_search.py` and downstream LLMs) receive zero score visibility and are forced to invent synthetic weights like `1.0 / (idx + 1)`.
* **Proposed Interface**: Preserve `relevance_score: float` in each result dictionary in `pack["results"]`.
* **Codex Acceptance Criterion**: `assert "relevance_score" in pack["results"][0]`.
* **Luna Attack Verification**: Compare `relevance_score` returned in pack with internal `RelevanceScorer.score()`; verify exact match.

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
* **Current Behavior**: `SpreadingActivationEngine.rank()` exists only in unit tests and `ranked_search.py`.
* **Opacity Hazard**: Activation dynamics do not influence standard agent execution pipelines (`recall_cli` or `RealAgentExecutionHarness`).
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

---

### GAP-009: Inaccessible Ingestion Buffer in Storage Engine
* **Affected Component**: [`memory_controller/storage/file_engine.py:FileStorageEngine`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/memory_controller/storage/file_engine.py#L20)
* **Current Behavior**: `FileStorageEngine` explicitly excludes `06_INBOX` from indexing. Unpromoted synthesis atoms in `06_INBOX/DERIVED/BOOKS/` are completely invisible to `MemoryController.search()`.
* **Opacity Hazard**: Unpromoted candidate knowledge cannot be retrieved, tested, or benchmarked via the standard API without custom candidate injection bypasses.
* **Proposed Interface**: Support a read-only staging search scope (`include_review_staging: bool = False`) for authorized evaluation principals (`Principal.ADMIN`).
* **Codex Acceptance Criterion**: Authorized evaluation queries can inspect `REVIEW` candidate atoms without violating trust boundary `I-003`.
* **Luna Attack Verification**: Verify `Principal.AI_AGENT` is strictly rejected from searching `06_INBOX` without explicit staging flag.

---

### GAP-010: Edge Weight Calculation Neutralized by Immediate Overwrite
* **Affected Component**: [`cognitive_core/spreading_activation.py:SpreadingActivationEngine._propagate_on_graph()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/spreading_activation.py#L36-L38)
* **Current Behavior**: Line 37 calculates `propagated` incorporating edge weight normalization, but Line 38 immediately overwrites it with `score * (decay ** (hop + 1))`.
* **Opacity Hazard**: Edge weights (encoding tag overlap count and shared entity counts) are completely ignored. Spreading activation behaves identically whether 1 or 50 tags/entities are shared.
* **Empirical Evidence**: In A7 differential test, preserving edge weights changed rankings with Kendall $\tau$ down to $0.7778$ on lexical trap queries.
* **Proposed Interface**: Remove line 38 overwrite and preserve the weighted propagation formula.
* **Codex Acceptance Criterion**: `assert engine.activate(seeds)[neighbor] == pytest.approx(expected_weighted_score)`.
* **Luna Attack Verification**: Compare multi-edge graph with high weight ($w=3.0$) vs low weight ($w=1.0$); verify high weight node receives strictly higher activation.

---

### GAP-011: Substring Matching in QueryClassifier Induces Inverse Lifecycle Filtering
* **Affected Component**: [`memory_controller/context/query_classifier.py:QueryClassifier.classify()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/memory_controller/context/query_classifier.py#L53-L56)
* **Current Behavior**: Iterates through lifecycle stage strings using substring search (`if stage in lowered`). Because `"unverified"` contains the substring `"verified"`, queries searching for unverified notes set `lifecycle_filters = ['VERIFIED']`.
* **Opacity Hazard**: Searching for unverified review notes causes `StorageEngine.query()` to filter strictly for `VERIFIED` notes, dropping 100% of candidate unverified notes.
* **Proposed Interface**: Use regex whole-word boundaries: `r"\b" + stage + r"\b"`, or explicit token matching.
* **Codex Acceptance Criterion**: `assert "VERIFIED" not in classifier.classify("search unverified drafts")["lifecycle_filters"]`.
* **Luna Attack Verification**: Query `"unverified draft"`; verify returned candidates include `REVIEW` notes.

---

### GAP-012: Hardcoded `.store` Dictionary Access in `ranked_search.py`
* **Affected Component**: [`cognitive_core/ranked_search.py:build_multi_graph()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/ranked_search.py#L16)
* **Current Behavior**: `build_multi_graph` assumes `controller.storage.store.values()`.
* **Opacity Hazard**: In production runs using `SQLiteStorageEngine` or `FileStorageEngine`, `build_multi_graph` raises `AttributeError: 'SQLiteStorageEngine' object has no attribute 'store'`, which is silently swallowed by `except Exception: return results[:top_k]`. Spreading activation never runs in production.
* **Proposed Interface**: Query notes via public storage method `controller.storage.query(intent="all")` or pass candidate notes directly into graph builder.
* **Codex Acceptance Criterion**: `build_multi_graph(controller)` succeeds without error on `SQLiteStorageEngine` and `FileStorageEngine`.
* **Luna Attack Verification**: Call `ranked_search(sqlite_controller, Principal.AI_AGENT, "wal")`; verify graph activation ranking executes without falling back to unranked slice.

---

### GAP-013: Candidate Reachability Expansion Blocked by `id_to_result` Filter in `ranked_search.py`
* **Affected Component**: [`cognitive_core/ranked_search.py:ranked_search()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/ranked_search.py#L47-L52)
* **Current Behavior**: Line 48 explicitly filters `[id_to_result[note_id] for note_id, _ in ranked_ids if note_id in id_to_result]`.
* **Opacity Hazard**: The foundational promise of spreading activation—associative recall of unretrieved multi-hop neighbors—is completely negated. Even when a 1-hop or 2-hop neighbor without lexical match receives high activation, it is discarded before returning to the caller. Spreading activation acts exclusively as a candidate re-sorter, never an expander.
* **Proposed Interface**: For top-ranked IDs not present in `id_to_result`, retrieve the note object via `controller.get(principal, note_id)` and append to results subject to authorization and lifecycle checks.
* **Codex Acceptance Criterion**: An unretrieved 1-hop neighbor with high activation is fetched and included in `ranked_search()` output.
* **Luna Attack Verification**: Query a note connected to a zero-lexical-overlap neighbor; verify the neighbor appears in `ranked_search()` output.

---

### GAP-014: Silent Exception Swallowing in `ranked_search.py` Masquerades Complete Crash as Successful Retrieval
* **Affected Component**: [`cognitive_core/ranked_search.py:ranked_search()`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/cognitive_core/ranked_search.py#L39-L45)
* **Current Behavior**: A broad `except Exception: return results[:top_k]` surrounds graph construction and ranking.
* **Opacity Hazard**: Fatal integration crashes (e.g. `AttributeError` on `.store`, missing graph dependencies, or activation divide-by-zero) are silently suppressed without audit logging, console warnings, or error metadata. Callers and test suites receive a standard 200 OK list response and cannot distinguish between a successful graph reranking and a total internal crash.
* **Proposed Interface**: Log graph failures via `audit_event()` and attach diagnostic metadata (`graph_status: "FAILED"`, `fallback_reason: str(e)`) to the returned container.
* **Codex Acceptance Criterion**: `ranked_search()` annotates response or logs an audit event when fallback occurs.
* **Luna Attack Verification**: Inject a mock crashing graph engine; verify the failure is observable and distinguishable from successful base retrieval.

