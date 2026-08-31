# Milestone 3 Multi-Agent Subsystem: Comprehensive Test Suite Architecture & Design Report

**Author**: `explorer_m3_3` (teamwork_preview_explorer)  
**Date**: 2026-08-28  
**Project**: Jarvis Cognitive Brain (`projects/jarvis_cognitive_brain`)  
**Target Milestone**: Milestone 3 — Multi-Agent Worker Orchestration (`jarvis/agents/`)  
**Test Suite Target**: `tests/unit/test_multi_agent.py`, `tests/unit/test_challenger_m3_stress.py`, and supporting test infrastructure  

---

## 1. Executive Summary & Test Infrastructure Audit

### 1.1 Baseline Test Suite Status
The existing test suite was audited and executed on Python 3.14 / pytest 9.0.2 / pytest-asyncio 1.4.0.
- **Total Tests Collected**: 235 items
- **Execution Result**: **235 passed in 6.19s (100% pass rate)**
- **Breakdown by Subsystem**:
  - `tests/e2e/tier1_features/`: 53 tests (Features R1-R5: audio barge-in, STT/VAD, TTS Kokoro, FastMCP IoT, Home Assistant client, HUD WebSocket, LLM providers, Memory storage, Multi-agent initial mock, OODA cycle)
  - `tests/e2e/tier2_boundaries/`: 25 tests (Boundaries & Invariants P0-P18, audio buffer overflow/underrun, rapid barge-in interruption, IoT timeouts/malformed inputs, memory invariants, corrupted OODA inputs)
  - `tests/e2e/tier3_combinations/`: 20 tests (Pairwise cross-feature interactions: Voice+IoT, Voice+Memory, OODA+Reflexion, BargeIn+LLM stream cancellation, etc.)
  - `tests/e2e/tier4_workloads/`: 10 tests (Real-world multi-turn conversational workloads, simulated household automation, stress workloads)
  - `tests/unit/`: 127 tests (Adversarial stress tests M1/M2, storage concurrency hammer, audio pipeline, barge-in timing <50ms, chunker edge cases, LLM providers, memory storage engine, OODA loop state machine)

### 1.2 Pytest Environment & Configuration Audit
The environment configuration in `pyproject.toml` and `conftest.py` is configured with:
1. **Async Runner Hook**: `pytest-asyncio` configured in `auto` mode (`asyncio_default_fixture_loop_scope = "function"`, `asyncio_mode = "auto"`), supported by an automatic `pytest_pyfunc_call` runner hook in `conftest.py` for direct async coroutine test functions.
2. **Path Configuration**: Python path automatically prepends `projects/jarvis_cognitive_brain` to `sys.path`.
3. **Shared Test Fixtures in `conftest.py`**:
   - `temp_vault_dir`: Initialized Obsidian vault directory structure (`00_CORE`, `01_KNOWLEDGE`, `02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY/Errors`, `04_MEMORY/Lessons`, `04_MEMORY/Decisions`, `05_RESOURCES`, `06_INBOX/RAW_IMPORTS`, `99_SYSTEM`) seeded with canonical test notes.
   - `temp_sqlite_path` / `sqlite_storage` / `sqlite_engine`: Initialized SQLite engine in WAL mode (`PRAGMA busy_timeout=5000`, `PRAGMA foreign_keys=ON`).
   - `markdown_sync`: Two-way Markdown-to-SQLite file synchronization fixture.
   - `sample_note`: Frontmatter-compliant dictionary fixture for validation tests.
   - `mock_llm`: Configurable `MockLLMProvider` with programmatic response queues, token delay simulation, and failure flags.
   - `virtual_audio`: Headless `VirtualAudioDriver` with sine wave/silence generation and barge-in trigger callbacks.
   - `ha_simulator`: In-memory Home Assistant REST simulator with entity state transitions and service dispatch tracking.
   - `websocket_hub`: In-memory `MockWebSocketHub` for HUD broadcast packet capture.

---

## 2. Milestone 3 Architecture & Contract Requirements

Milestone 3 implements the **Multi-Agent Worker Orchestration Subsystem** under `jarvis/agents/` to decouple heavy background cognitive tasks (data retrieval, frontmatter compliance verification, lesson consolidation, and self-reflection critique) from the primary real-time voice loop (STT/TTS TTFB < 300ms).

### 2.1 Core Subsystem Components & Contracts
```
+---------------------------------------------------------------------------------------------------+
|                                  MULTI-AGENT SUPERVISOR (P1 - P5)                                 |
|  - Priority Queue (asyncio.PriorityQueue)                                                         |
|  - Non-Blocking Background Worker Pool (TaskGroup / Concurrency Governor)                         |
|  - CancellationToken & Timeout Propagation                                                        |
|  - Worker Crash Isolation & Retry Engine                                                           |
+---------------------------------+---------------------------------+-------------------------------+
                                  │                                 │
             ┌────────────────────┼────────────────────┐            │
             ▼                    ▼                    ▼            ▼
    [Router Agent]       [Retrieval Agent]    [Verifier Agent]  [Consolidator Agent]  [Critic Agent]
    - Priority: 1-2      - Priority: 2-3      - Priority: 2     - Priority: 5         - Priority: 4
    - Role: Query Decomp - Role: Associative  - Role: Schema &  - Role: Lesson Synth  - Role: 6-Stage
    - Perms: Read/Search - Perms: Read/Search - Perms: Read     - Perms: Propose/Arch - Perms: Propose
    - Invariants: Pure   - Invariants: Pure   - Invariants:     - Invariants: P0-004  - Invariants:
      analysis, no state   lineage traversal,   P0-005 Human      propose to REVIEW     SelfRefine,
      mutations            read-only            attest gate       only, no ACTIVE       no attest
```

### 2.2 Worker Role Permissions & Invariant Enforcement Matrix (P0-P15)

| Worker Agent | Priority Tier | Permitted Operations | Forbidden Operations | Invariants Enforced |
|---|---|---|---|---|
| **RouterAgent** | P1 (Urgent) / P2 | `READ`, `SEARCH` | `PROPOSE`, `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `DELETE` | Pure query decomposition; immutable input state |
| **RetrievalAgent** | P2 (Real-time) / P3 | `READ`, `SEARCH` | `PROPOSE`, `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `DELETE` | Recursive CTE lineage traversal without state mutation |
| **VerifierAgent** | P2 (Verification) | `READ` | `PROPOSE`, `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `DELETE` | P0-001 (AI verified gate), P0-002 (source type gate), P0-005 (human attest only) |
| **ConsolidatorAgent**| P5 (Background) | `SEARCH`, `READ`, `PROPOSE`, `ARCHIVE` | `ATTEST`, `PROMOTE` to `ACTIVE`, direct propose to `ACTIVE`/`VERIFIED` | P0-004 (creation in REVIEW only), P0-002 (`source_type=inference`), P0-012/13 (no cycles) |
| **CriticAgent** | P4 (Post-turn) | `READ`, `PROPOSE` | `ATTEST`, `PROMOTE` to `ACTIVE`, `DELETE` | 6-stage Reflexion critique, SelfRefine memory verification |

---

## 3. Dedicated Milestone 3 Test Fixtures (in `conftest.py` / `test_multi_agent.py`)

To achieve complete test isolation and deterministic concurrency verification, the following specialized fixtures are designed:

```python
# Fixture Definitions for Milestone 3

@pytest.fixture
def agent_supervisor(sqlite_engine: SQLiteStorageEngine, mock_llm: MockLLMProvider) -> MultiAgentSupervisor:
    """Provides a fresh MultiAgentSupervisor with worker concurrency limit = 4."""
    supervisor = MultiAgentSupervisor(
        storage=sqlite_engine,
        llm=mock_llm,
        max_concurrent_workers=4,
        default_timeout_s=5.0,
    )
    return supervisor

@pytest.fixture
def router_agent(mock_llm: MockLLMProvider) -> RouterAgent:
    """Provides a standalone RouterAgent instance for intent decomposition testing."""
    return RouterAgent(llm=mock_llm)

@pytest.fixture
def retrieval_agent(sqlite_engine: SQLiteStorageEngine) -> RetrievalAgent:
    """Provides a standalone RetrievalAgent instance with read/search only access."""
    return RetrievalAgent(storage=sqlite_engine)

@pytest.fixture
def verifier_agent() -> VerifierAgent:
    """Provides a standalone VerifierAgent instance for frontmatter & invariant auditing."""
    return VerifierAgent()

@pytest.fixture
def consolidator_agent(sqlite_engine: SQLiteStorageEngine) -> ConsolidatorAgent:
    """Provides a ConsolidatorAgent instance with memory synthesis and reconsolidation capabilities."""
    return ConsolidatorAgent(storage=sqlite_engine)

@pytest.fixture
def critic_agent(mock_llm: MockLLMProvider) -> CriticAgent:
    """Provides a CriticAgent instance with 6-stage Reflexion and SelfRefine rules."""
    return CriticAgent(llm=mock_llm)
```

---

## 4. Test Suite Architecture & Detailed Test Specifications

The Milestone 3 test suite is structured into 4 dedicated test suites:
1. `tests/unit/test_multi_agent.py`: Complete functional unit test suite covering Supervisor priority queue, 5 specialized agents, and end-to-end multi-agent orchestration.
2. `tests/unit/test_agent_least_privilege.py`: Invariant security boundary test suite (P0-P15) enforcing least-privilege role boundaries and attack prevention.
3. `tests/unit/test_challenger_m3_stress.py`: Adversarial concurrency, high-throughput queue flooding, non-blocking voice loop validation, timeouts, and cancellation stress.
4. `tests/e2e/tier1_features/test_t1_multi_agent.py`: Tier 1 feature integration verified with production agent classes.

---

### Module 1: `tests/unit/test_multi_agent.py` Specification

#### Group 1: MultiAgentSupervisor Priority Queue & Worker Pool (6 Tests)
1. `test_supervisor_priority_queue_strict_ordering`:
   - Submits 5 tasks with priorities [P5, P1, P4, P2, P3] in random order.
   - Verifies that tasks execute and complete strictly in ascending numerical priority order: P1 -> P2 -> P3 -> P4 -> P5.
2. `test_supervisor_fifo_ordering_within_same_priority`:
   - Submits 10 tasks all at Priority 2 with monotonically increasing sequential IDs.
   - Verifies execution order maintains strict FIFO order.
3. `test_supervisor_async_worker_pool_concurrency_limit`:
   - Configures supervisor with `max_concurrent_workers = 3`.
   - Submits 10 simulated long-running tasks (sleeping 50ms each).
   - Monitors active running task count during execution; asserts active worker count never exceeds 3.
4. `test_supervisor_non_blocking_voice_loop_execution`:
   - Simulates high-frequency voice loop (100 iterations of audio frame processing @ 10ms intervals).
   - Concurrently submits 20 heavy background consolidation and retrieval tasks to the supervisor.
   - Verifies voice loop execution time remains completely unimpeded (maximum loop jitter < 5ms).
5. `test_supervisor_task_status_lifecycle_tracking`:
   - Submits a task; asserts status transitions from `PENDING` -> `RUNNING` -> `COMPLETED`.
   - Checks that completion callbacks (`on_task_complete`) fire with the expected task result payload.
6. `test_supervisor_graceful_shutdown_drains_active_tasks`:
   - Submits 5 in-flight tasks.
   - Calls `await supervisor.shutdown(wait=True, timeout=2.0)`.
   - Asserts all running tasks complete cleanly without hanging coroutines or unhandled exceptions.

#### Group 2: Router Agent Intent Decomposition & Slot Parsing (5 Tests)
7. `test_router_single_atomic_query`:
   - Input: `"What is the memory retrieval architecture?"`
   - Verifies Router generates 1 atomic task for `retrieval` agent with extracted intent `QUERY`.
8. `test_router_composite_intent_decomposition`:
   - Input: `"Turn off the kitchen lights and set the living room thermostat to 21 degrees and check system status"`
   - Verifies Router decomposes request into 3 distinct atomic sub-tasks:
     - Task 1: `iot_control` (domain: `light`, service: `turn_off`, entity: `light.kitchen`)
     - Task 2: `iot_control` (domain: `climate`, service: `set_temperature`, target: `21`)
     - Task 3: `system_status` (diagnostic query)
9. `test_router_composite_query_and_memory_store`:
   - Input: `"Remember that our SQLite database uses WAL mode and check if the living room lights are on"`
   - Verifies decomposition into 1 `memory_store` task + 1 `iot_query` task with appropriate priority tags.
10. `test_router_empty_and_whitespace_input_handling`:
    - Inputs: `""`, `"   "`, `"\n\t"`
    - Verifies Router returns empty task list or fallback conversational task without throwing exceptions.
11. `test_router_ambiguous_and_malformed_syntax`:
    - Inputs: `"and and turn on light and"`, `"please and maybe do nothing"`
    - Verifies Router safely sanitizes input and extracts valid operable clauses.

#### Group 3: Retrieval Agent Lineage & Multi-Signal Recall (5 Tests)
12. `test_retrieval_agent_bm25_and_tag_filtering`:
    - Seeds SQLite with 5 notes across categories `security`, `iot`, `architecture`.
    - Queries for `"cryptographic encryption standards"`.
    - Asserts retrieved notes match lexical and category filters with relevance ranking.
13. `test_retrieval_agent_supersession_lineage_exclusion`:
    - Creates Note A (ACTIVE), supersedes Note A with Note B (ACTIVE), and supersedes Note B with Note C (ACTIVE).
    - Queries for content present across all three notes.
    - Asserts retrieval agent returns canonical Note C and marks Notes A & B as superseded/archived in lineage.
14. `test_retrieval_agent_wikilink_synapse_graph_traversal`:
    - Creates linked cluster: Note A `[[relates_to]]` Note B `[[depends_on]]` Note C.
    - Executes associative recall with `max_depth = 2`.
    - Asserts co-activated context graph returns the complete semantic cluster.
15. `test_retrieval_agent_confidence_and_recency_scoring`:
    - Seeds high-confidence old note vs medium-confidence recent note.
    - Verifies combined ranking calculation reflects ACT-R decay and confidence weighting.
16. `test_retrieval_agent_read_only_isolation`:
    - Verifies Retrieval agent cannot invoke `.propose()`, `.update()`, or `.archive()` on the storage engine.

#### Group 4: Verifier Agent Frontmatter & Invariant Auditing (6 Tests)
17. `test_verifier_validates_canonical_frontmatter_schema`:
    - Supplies complete, compliant NoteFrontmatter dictionary.
    - Verifies audit passes with `is_valid == True` and zero violation codes.
18. `test_verifier_detects_missing_mandatory_fields`:
    - Supplies notes missing `id`, `type`, `lifecycle`, `provenance`, or `created`.
    - Verifies audit fails, identifying exact missing field names and remediation advice.
19. `test_verifier_detects_invalid_uuid_and_enum_violations`:
    - Supplies invalid UUID string `"not-a-uuid"`, invalid type `"custom_note_type"`, invalid lifecycle `"DONE"`.
    - Verifies audit flags schema validation failures cleanly.
20. `test_verifier_flags_ai_agent_self_verification_attempt`:
    - Supplies note with `verification: "verified"` where provenance source is `ai` or `inference`.
    - Verifies audit flags violation code `ERR_P0_001_AI_VERIFIED_GATE`.
21. `test_verifier_flags_unauthorized_active_lifecycle_at_creation`:
    - Supplies proposed note with `lifecycle: "ACTIVE"` from an AI agent without human attestation.
    - Verifies audit flags violation code `ERR_P0_004_AI_CREATION_LIFECYCLE`.
22. `test_verifier_flags_cyclic_and_self_supersession`:
    - Supplies note where `id == supersedes` or where recursive ancestor cycle is detected.
    - Verifies audit flags violation code `ERR_P0_012_CYCLIC_SUPERSESSION`.

#### Group 5: Consolidator Agent Lesson Synthesis & Reconsolidation (5 Tests)
23. `test_consolidator_synthesizes_multiple_review_lessons`:
    - Seeds 3 related lessons in `REVIEW` lifecycle under category `audio-pipeline`.
    - Executes Consolidator synthesis.
    - Asserts new unified knowledge note is proposed into `REVIEW` with `derived_from` relations linking back to all 3 source lesson IDs.
24. `test_consolidator_archives_consumed_review_lessons`:
    - Verifies that after successful consolidation, the 3 original source lesson notes are transitioned to `ARCHIVED` lifecycle with proper archive reasons.
25. `test_consolidator_handles_insufficient_candidates_gracefully`:
    - Seeds only 1 lesson in `REVIEW`.
    - Asserts consolidator returns `None` without creating spurious single-item consolidations.
26. `test_consolidator_plastic_memory_reconsolidation_challenge`:
    - Triggers `challenge()` on an `ACTIVE` note with conflicting evidence.
    - Asserts note transitions to `RECONSOLIDATING` and snapshots `previous_version` content and timestamp.
27. `test_consolidator_plastic_memory_reconsolidation_resolution`:
    - Resolves active challenge with updated consolidated findings.
    - Asserts note returns to `ACTIVE` lifecycle with updated content and cleared conflicting evidence.

#### Group 6: Critic Agent 6-Stage Formal Reflexion & SelfRefine (4 Tests)
28. `test_critic_evaluates_valid_draft_and_approves`:
    - Submits compliant response draft and memory note.
    - Executes 6-stage Reflexion critique.
    - Asserts critique score >= 0.85 and `approved == True`.
29. `test_critic_detects_secret_or_credential_leak`:
    - Submits draft containing simulated API key / password (`"sk-proj-12345abcdef"`).
    - Asserts Critic rejects draft (`approved == False`), flags security policy violation, and redacts secret.
30. `test_critic_flags_hallucinated_facts_or_contradictions`:
    - Submits draft contradicting stored canonical facts in working memory.
    - Asserts Critic scores draft low (< 0.50), returns actionable critique, and requests plan revision.
31. `test_critic_self_refine_enforces_atomicity_and_style`:
    - Submits overly verbose, multi-concept draft ("Everything about our server").
    - Verifies Critic decomposes or refines into atomic single-concept representations.

---

### Module 2: `tests/unit/test_agent_least_privilege.py` (Security & Invariants P0-P15)

1. `test_invariant_p0_router_cannot_mutate_memory`:
   - Router attempts to call `storage.propose()`, `storage.update()`, `storage.delete()`.
   - Asserts role permission policy raises `PermissionError`.
2. `test_invariant_p0_retrieval_cannot_mutate_memory`:
   - Retrieval agent attempts write operations; asserts `PermissionError`.
3. `test_invariant_p0_verifier_cannot_attest_or_promote`:
   - Verifier agent attempts to invoke `storage.attest()` or `storage.promote()`.
   - Asserts `PermissionError` (only `Principal.HUMAN` / `Principal.ADMIN` permitted).
4. `test_invariant_p0_consolidator_cannot_propose_active_lifecycle`:
   - Consolidator agent attempts to propose a note with `lifecycle = "ACTIVE"`.
   - Asserts `ValueError` raised under `Principal.AI_AGENT`.
5. `test_invariant_p0_consolidator_cannot_claim_privileged_provenance`:
   - Consolidator agent attempts to propose note claiming `source_type = "user"` or `"official"`.
   - Asserts `ValueError` raised under `Principal.AI_AGENT`.
6. `test_invariant_p16_p18_hardware_telemetry_immutability_across_all_workers`:
   - Any agent worker attempts to alter `hardware_serial`, `vendor_id`, or `evidence_sha256`.
   - Asserts strict `PermissionError` (P16-P18).

---

### Module 3: `tests/unit/test_challenger_m3_stress.py` (Adversarial Concurrency & Fault Tolerance)

1. `test_supervisor_cancellation_token_halts_worker_instantly`:
   - Submits long-running background task with `CancellationToken`.
   - Calls `token.cancel(reason="user_bargein")` mid-execution.
   - Asserts worker halts within 1 execution step, sets status to `CANCELLED`, and releases resources.
2. `test_supervisor_task_timeout_handling`:
   - Submits task configured with `timeout_seconds = 0.1` that sleeps for 1.0s.
   - Asserts supervisor times out task, marks status `TIMED_OUT`, and continues serving subsequent tasks.
3. `test_supervisor_worker_crash_isolation_and_resilience`:
   - Submits task with payload engineered to raise `ZeroDivisionError` / `RuntimeError`.
   - Asserts supervisor captures exception, records traceback, marks task `FAILED`, and worker pool does not crash.
4. `test_supervisor_retry_mechanism_with_transient_failures`:
   - Creates task with `max_retries = 3` and failing worker that succeeds on 3rd attempt.
   - Asserts supervisor retries task 2 times and ultimately marks task `COMPLETED`.
5. `test_supervisor_dead_letter_queue_on_exhausted_retries`:
   - Task fails continuously exceeding `max_retries = 2`.
   - Asserts task moves to dead-letter / failed tasks registry with complete failure metadata.
6. `test_supervisor_high_contention_queue_flooding_stress`:
   - Spawns 8 threads simultaneously submitting 200 tasks with randomized priorities [P1..P5].
   - Asserts all 200 tasks process deterministically with zero deadlocks, zero lost tasks, and zero race conditions.
7. `test_supervisor_rapid_bargein_burst_cancellations`:
   - Rapidly submits and cancels 50 tasks in quick succession (<1ms intervals).
   - Asserts clean state recovery, zero dangling futures, and zero unhandled async task warnings.

---

## 5. Mocking Strategy & Subsystem Integration

### 5.1 Standalone Unit vs. Integrated Modes
- **Unit Test Level**:
  - `MockLLMProvider` is used for all agent tests, guaranteeing fast, deterministic, offline execution with zero network dependencies.
  - In-memory SQLite (`:memory:` or temporary disk `.sqlite3`) with WAL mode is used for memory operations.
  - No external services or real audio hardware required.
- **Executive Integration Level**:
  - In `CognitiveExecutive` and `OODACognitiveEngine`, the supervisor is wired to run background consolidation and reflexion without blocking the foreground `process_utterance()` method.
- **HUD Telemetry Level**:
  - Supervisor task state changes broadcast events (`agent_task_started`, `agent_task_completed`, `agent_task_failed`) to the WebSocket hub for visualization on the 3D HUD.

---

## 6. Verification and Execution Commands

To execute and verify the entire test suite including the new Milestone 3 tests:

```bash
# 1. Run all unit tests including Milestone 3 Multi-Agent
python -m pytest tests/unit/test_multi_agent.py -v

# 2. Run adversarial stress & challenger tests
python -m pytest tests/unit/test_challenger_m3_stress.py -v

# 3. Run least privilege security tests
python -m pytest tests/unit/test_agent_least_privilege.py -v

# 4. Run entire E2E and Unit test suite
python -m pytest -q
```

---

## 7. Deliverable Summary

| Test File | Focus Area | Planned Test Count |
|---|---|---|
| `tests/unit/test_multi_agent.py` | Supervisor priority queue, Router, Retrieval, Verifier, Consolidator, Critic | 31 tests |
| `tests/unit/test_agent_least_privilege.py` | P0-P15 Invariant boundary attacks and permission audits | 6 tests |
| `tests/unit/test_challenger_m3_stress.py` | Adversarial stress, queue flooding, timeout, cancellation, worker crash isolation | 7 tests |
| `tests/e2e/tier1_features/test_t1_multi_agent.py`| E2E Tier 1 feature verification with production agent classes | 5 tests |
| **Total M3 Test Suite** | **Comprehensive Multi-Agent Verification** | **49 tests** |
