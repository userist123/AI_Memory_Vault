# Comprehensive Exploration & Architecture Report for Milestone 3 (Multi-Agent Worker Orchestration)

**Project:** Jarvis Cognitive Brain ("Creier Vorbitor")  
**Target Subsystem:** Multi-Agent Worker Orchestration (Milestone 3)  
**Agent:** `explorer_m3_1` (`teamwork_preview_explorer`)  
**Date:** 2026-08-28  

---

## 1. Executive Summary

A comprehensive, read-only architectural investigation was conducted across the `projects/jarvis_cognitive_brain` repository. The codebase has successfully completed **Milestone 1** (Cognitive OODA Loop & Dual Persistence Memory Storage Engine) and **Milestone 2** (Cascaded Audio Pipeline with Silero VAD, Faster-Whisper STT, Kokoro-82M ONNX TTS, and Sub-50ms Barge-In Interruption). Currently, **235 out of 235 unit, boundary, combination, and stress tests pass cleanly** (`python -m pytest -q` reports 0 failures in 6.22s).

This report analyzes the existing data models, engine contracts, audio/memory interfaces, and specifies the exact design, class hierarchy, least-privilege scoping, and directory layout for **Milestone 3: Multi-Agent Worker Orchestration**.

---

## 2. Codebase Inventory & Current State

### 2.1 File & Module Layout
```
projects/jarvis_cognitive_brain/
├── jarvis/
│   ├── config.py                  # Pydantic Settings (LLM, Storage, Audio, VAD, IoT)
│   ├── core/
│   │   ├── models.py              # Core Data Models (PerceptionEvent, ActivePlan, WorkingMemory, OODACycleResult)
│   │   ├── context.py             # AudioSessionContext & DialogueTurn tracking
│   │   ├── ooda.py                # OODACognitiveEngine (Observe, Retrieve, Plan, Act, Reflect, Consolidate)
│   │   └── executive.py           # CognitiveExecutive Daemon with atomic checkpointing & co-activation synapses
│   ├── llm/
│   │   ├── base.py                # BaseLLMProvider & CancellationToken (<50ms Barge-in abort)
│   │   ├── ollama_provider.py     # Ollama provider (qwen2.5-coder)
│   │   ├── cloud_providers.py     # Gemini / Antigravity & Claude providers
│   │   └── mock_provider.py       # Deterministic mock LLM provider for tests
│   ├── memory/
│   │   ├── invariants.py          # Trust boundary rules P0-P18 (Principal, Lifecycle, NoteType, NoteFrontmatter)
│   │   ├── sqlite_engine.py       # Thread-safe SQLite WAL engine with CTE lineage traversal
│   │   ├── markdown_sync.py       # Bidirectional Markdown <-> SQLite sync
│   │   ├── recall.py              # Multi-Signal Associative Recall (BM25 + Semantic + ACT-R + Lineage)
│   │   ├── activation.py          # ACT-R base-level decay & Spreading Activation across wikilinks
│   │   ├── reflection.py          # 6-Stage Formal Reflexion & SelfRefine critique filter
│   │   └── consolidation.py       # Plastic memory reconsolidation & lesson distillation
│   └── audio/
│       ├── pipeline.py            # Cascaded AudioPipeline & VoiceState state machine
│       ├── vad.py                 # Silero ONNX & Energy VAD segmentation (500ms trailing silence)
│       ├── stt.py                 # Faster-Whisper CTranslate2 STT engine
│       ├── tts.py                 # Kokoro-82M ONNX 24kHz streaming TTS
│       ├── bargein.py             # Sub-50ms BargeInController (DAC abort + LLM token cancellation)
│       ├── drivers.py             # Audio I/O driver abstraction, CircularAudioBuffer, RobustAudioSanitizer
│       └── chunker.py             # Sentence/clause streaming chunker & text normalizer
├── tests/
│   ├── conftest.py                # Fixtures (VirtualAudioDriver, HomeAssistantSimulator, MockWebSocketHub)
│   ├── unit/                      # Unit tests & Adversarial stress suites (M1 & M2)
│   └── e2e/                       # 4-Tier Opaque-box E2E test suites (Tier 1-4)
```

---

## 3. Detailed Inspection of Existing Core Components

### 3.1 `jarvis.config.Settings`
- Located at `jarvis/config.py:11`.
- Configures environment-variable backed parameters (`JARVIS_` prefix):
  - LLM: `llm_provider` ("ollama", "gemini", "claude", "mock"), `ollama_url`, `ollama_model`, `ollama_timeout`.
  - Storage: `vault_path` (`Path("vault_notes")`), `sqlite_db_path` (`Path("vault_memory.sqlite3")`), `sqlite_busy_timeout_ms` (5000), `checkpoint_dir` (`Path(".checkpoints")`), `audit_log_path` (`Path("audit_log.jsonl")`).
  - Audio: `audio_sample_rate` (16000), `tts_sample_rate` (24000), `vad_silence_threshold_ms` (500), `vad_threshold` (0.5), `vad_frame_size` (512 samples = 32ms).
  - IoT: `home_assistant_url` (`"http://localhost:8123"`), `home_assistant_token`.

### 3.2 Core Data Models (`jarvis/core/models.py`)
1. **`IntentType` (Enum, lines 15-23)**:
   `QUERY`, `IOT_CONTROL`, `TASK`, `MEMORY_STORE`, `SYSTEM_STATUS`, `CONVERSATION`.
2. **`UserIntent` (BaseModel, lines 25-33)**:
   `raw_text: str`, `intent_type: IntentType`, `entities: List[Dict[str, Any]]`, `requires_tool: bool`, `extracted_query: Optional[str]`, `confidence: float`.
3. **`PerceptionEvent` (BaseModel, lines 35-41)**:
   `timestamp: float`, `channel: str` (voice, text, sensor, api), `raw_data: str`, `metadata: Dict[str, Any]`.
4. **`PlanStep` (BaseModel, lines 52-61)**:
   `step_id: int`, `action: str`, `kwargs: Dict[str, Any]`, `description: str`, `status: StepStatus`, `result: Optional[Any]`, `error: Optional[str]`.
5. **`ActivePlan` (BaseModel, lines 63-116)**:
   - State machine holding `goal: str`, `steps: List[PlanStep]`, `current_step_index: int`.
   - Methods: `get_next_step()`, `complete_current_step()`, `fail_current_step()`, `is_complete()`.
   - Persistence: `save_state(file_path)` (atomic via temp file + `os.replace`), `load_state(file_path)`.
6. **`StepExecutionResult` (BaseModel, lines 118-126)**:
   `step_id: int`, `action: str`, `status: str` (success/blocked/error), `result: Optional[Any]`, `error: Optional[str]`, `execution_time_ms: float`.
7. **`WorkingMemory` (lines 128-207)**:
   - Bounded capacity (default 10) with ACT-R decayed working memory chunks.
   - Methods: `admit(nodes_with_scores)`, `add(item)`, `get_active_context()`, `clear()`, `save_state()`, `load_state()`.
8. **`OODACycleResult` (BaseModel, lines 209-234)**:
   `perception`, `intent`, `active_plan`, `step_results`, `context_used`, `reflections`, `consolidated_ids`, `execution_time_ms`.
   Properties: `success`, `plan`, `response_text`.

### 3.3 OODA Loop & Executive Daemon (`jarvis/core/ooda.py` & `executive.py`)
- **`OODACognitiveEngine`**:
  - `observe(perception: PerceptionEvent) -> UserIntent`
  - `retrieve(intent: UserIntent) -> List[Dict[str, Any]]`
  - `reason_and_plan(intent: UserIntent, context: List[Dict[str, Any]]) -> ActivePlan`
  - `act_step(step: PlanStep, principal: Principal) -> StepExecutionResult`
  - `act(plan: ActivePlan, principal: Principal) -> List[StepExecutionResult]`
  - `reflect(target: Union[ActivePlan, PlanStep], error: Optional[str], principal: Principal) -> Optional[str]`
  - `consolidate(lesson_note: Optional[Dict[str, Any]], principal: Principal) -> Optional[str]`
  - `execute_cycle(perception: PerceptionEvent, principal: Principal, auto_checkpoint_callback: Optional[Callable]) -> OODACycleResult`
- **`CognitiveExecutive`**:
  - Coordinates `OODACognitiveEngine`, `WorkingMemory`, `ActivePlan`, atomic checkpoints (`wm.json`, `plan.json`), and co-activation synapse firing across recalled notes (`_fire_synapses`).
  - Entry point: `process_utterance(text: str, source: str, principal: Principal) -> OODACycleResult`.

### 3.4 Memory Invariants & Trust Boundaries (`jarvis/memory/invariants.py`)
- **`Principal`**: `HUMAN`, `AI_AGENT`, `ADMIN`.
- **`Lifecycle`**: `RAW` -> `CLASSIFIED` -> `NORMALIZED` -> `REVIEW` -> `VERIFIED` -> `ACTIVE` -> `RECONSOLIDATING` -> `SUPERSEDED` -> `ARCHIVED`.
- **`NoteType`**: `KNOWLEDGE`, `PROJECT`, `PROCEDURE`, `DECISION`, `EXPERIENCE`, `ERROR`, `LESSON`, `PREFERENCE`, `RESOURCE`, `HYPOTHESIS`, `SYSTEM`, `CORE`.
- **Invariant Validation Functions**:
  - `validate_propose_invariants`: P0-001/P0-005 (AI cannot self-verify), P0-002 (AI cannot claim user/official/experience/import provenance), P0-004 (AI can only propose into RAW, CLASSIFIED, NORMALIZED, REVIEW), P16-P18 (hardware telemetry immutability).
  - `validate_update_invariants`: P0-003 (provenance source_type is immutable), P0-007 (lifecycle immutable via update), P0-011 (verification status cannot be escalated via update).
  - `validate_attest_invariants`: P0-005 (Only HUMAN or ADMIN can attest).
  - `validate_promote_invariants`: P0-004/P0-008 (Only HUMAN/ADMIN can directly promote to ACTIVE).
  - `validate_supersession_invariants`: P0-012/P0-013 (Acyclic DAG supersession validation, no self-supersession).

### 3.5 Storage, Recall & Consolidation Engines (`jarvis/memory/`)
- **`SQLiteStorageEngine`**: Thread-safe with WAL mode (`PRAGMA busy_timeout=5000`, `PRAGMA synchronous=NORMAL`), explicit `BEGIN IMMEDIATE` atomic transactions, and recursive CTE lineage traversal (`get_lineage`, `resolve_active_lineage`).
- **`MultiSignalRecallEngine`**: Composite scoring combining BM25 lexical match, Spreading Activation across wikilinks, Working Memory Cosine relevance, Confidence/Authority, Version Matching, and CTE Lineage resolution.
- **`ReflexionEngine` & `FormalReflexion`**: 6-stage reflection structure (`Error` -> `Root Cause` -> `Fix` -> `Verification` -> `Prevention Rule` -> `Core Lesson`).
- **`ConsolidationEngine`**: Plastic memory reconsolidation (`challenge` -> `RECONSOLIDATING` -> `resolve_challenge` -> `ACTIVE`), recurring lesson distillation into consolidated knowledge notes.

### 3.6 Audio Pipeline & Barge-In (`jarvis/audio/`)
- Continuous capture via `VirtualAudioInputDriver` or hardware driver.
- VAD segmentation with Silero ONNX / Energy VAD (500ms trailing silence threshold).
- STT transcription via Faster-Whisper.
- Sub-50ms Barge-In interruption via `BargeInController` (DAC abort, token cancellation, TTS queue flush).
- Streaming synthesis via Kokoro-82M ONNX 24kHz with sentence/clause chunker (<300ms TTFB).

---

## 4. Architectural Design for Milestone 3 (Multi-Agent Worker Orchestration)

### 4.1 Purpose & Role Scoping
Milestone 3 establishes a multi-agent worker coordination subsystem that isolates heavy computations, query decomposition, memory compliance audits, and background memory consolidation from the real-time voice and OODA loops.

Each agent operates under strict **Least Privilege Scoping**:

| Worker Agent | Principal | Permitted Operations | Primary Responsibilities |
|---|---|---|---|
| **Supervisor** | `Principal.AI_AGENT` / System | Coordinate Queue, Dispatch | Manages prioritized task queue, worker task lifecycle, retry policies, and telemetry. |
| **Router Agent** | `Principal.AI_AGENT` | `Operation.READ`, `Operation.SEARCH` | Decomposes composite/multi-intent user queries into atomic executable sub-tasks. |
| **Retrieval Agent** | `Principal.AI_AGENT` | `Operation.READ`, `Operation.SEARCH` | Multi-signal associative recall, scoped search against SQLite storage, supersession lineage traversal, and filtering. |
| **Verifier Agent** | `Principal.AI_AGENT` | `Operation.READ` | Audits frontmatter schema, checks invariant rules P0-P18 (UUID format, mandatory fields, forbidden provenance claims). |
| **Consolidator Agent** | `Principal.AI_AGENT` | `Operation.SEARCH`, `Operation.READ`, `Operation.PROPOSE`, `Operation.ARCHIVE` | Distills ephemeral `REVIEW` lessons into unified canonical knowledge notes; handles plastic memory challenges. |
| **Critic Agent** | `Principal.AI_AGENT` | `Operation.READ`, `Operation.PROPOSE` | Executes formal 6-stage Reflexion critique and SelfRefine validation on drafts, plans, and execution outputs. |

---

### 4.2 Priority Queue Model (`AgentTask`)
Tasks are prioritized using an integer priority field where lower numbers represent higher urgency:

- **Priority 1 (Urgent / Interactive)**: Router query decomposition, real-time command routing, immediate user query analysis.
- **Priority 2 (Interactive Verification / Critique)**: Critic agent validation of immediate plans / responses before output.
- **Priority 3 (Deep Retrieval)**: Associative memory recall, multi-hop wikilink graph traversal, CTE lineage resolution.
- **Priority 4 (Integrity Audit)**: Verifier agent frontmatter audits on raw/newly proposed notes.
- **Priority 5 (Background Consolidation)**: Consolidator agent lesson distillation, ACT-R dormancy cleanup, archive operations.

```python
class AgentTask(BaseModel):
    task_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    priority: int = 3  # 1 = Urgent, 5 = Background
    role: AgentRole
    payload: Dict[str, Any] = Field(default_factory=dict)
    created_at: float = Field(default_factory=time.time)
    timeout_s: float = 30.0

    def __lt__(self, other: "AgentTask") -> bool:
        return self.priority < other.priority
```

---

### 4.3 Target Module Structure

Milestone 3 should be organized under `jarvis/agents/` with full backwards-compatibility exposure in `jarvis/core/multi_agent.py`:

```
jarvis/
├── agents/
│   ├── __init__.py            # Exports MultiAgentSupervisor, AgentTask, AgentRole, and all 5 worker classes
│   ├── models.py              # AgentRole enum, AgentTask, TaskResult, TaskStatus
│   ├── supervisor.py          # MultiAgentSupervisor implementation with priority queue and background worker loop
│   ├── router.py              # RouterAgent implementation (query decomposition)
│   ├── retrieval.py           # RetrievalAgent implementation (scoped recall & lineage)
│   ├── verifier.py            # VerifierAgent implementation (frontmatter & invariant audits)
│   ├── consolidator.py        # ConsolidatorAgent implementation (lesson consolidation)
│   └── critic.py              # CriticAgent implementation (Reflexion & SelfRefine)
└── core/
    └── multi_agent.py         # Re-exports MultiAgentSupervisor & AgentTask for backward compatibility
```

---

### 4.4 Class & Interface Specifications

#### 1. `BaseWorkerAgent` (Abstract Base Class)
```python
class BaseWorkerAgent(ABC):
    role: AgentRole
    principal: Principal = Principal.AI_AGENT

    @abstractmethod
    async def execute(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        """Execute assigned role-specific workload."""
        pass
```

#### 2. `RouterAgent` (`jarvis/agents/router.py`)
- Analyzes incoming queries (e.g. "Turn on kitchen light and set thermostat to 22 degrees").
- Decomposes composite sentences by conjunctions (`and`, `then`, `after that`) or LLM structured output.
- Identifies entity targets and action types.
- Returns `{"subtasks": List[str], "count": int, "plan_hints": List[Dict[str, Any]]}`.

#### 3. `RetrievalAgent` (`jarvis/agents/retrieval.py`)
- Receives search query, filters, and optional working memory context.
- Invokes `MultiSignalRecallEngine` and `SQLiteStorageEngine.get_lineage()`.
- Strips superseded notes unless historical queries are requested.
- Returns `{"matches": List[Dict[str, Any]], "count": int, "top_id": Optional[str]}`.

#### 4. `VerifierAgent` (`jarvis/agents/verifier.py`)
- Audits note dictionaries against `NoteFrontmatter` and Invariants P0-P18.
- Validates UUID formatting, required fields (`id`, `type`, `lifecycle`, `category`, `provenance`), checks `source_type` permissions for `Principal.AI_AGENT`, and ensures verification is not set to `verified` without attestation.
- Returns `{"valid": bool, "missing": List[str], "violations": List[str]}`.

#### 5. `ConsolidatorAgent` (`jarvis/agents/consolidator.py`)
- Interacts with `ConsolidationEngine` to scan `REVIEW` lessons and errors.
- Synthesizes 2+ related lesson notes into a unified knowledge note.
- Handles `challenge()` and `resolve_challenge()` for plastic memory reconsolidation.
- Returns `{"consolidated_id": Optional[str], "archived_ids": List[str], "status": str}`.

#### 6. `CriticAgent` (`jarvis/agents/critic.py`)
- Performs structured critique on candidate answers, active plans, or error states.
- Implements `FormalReflexion` 6-stage error decomposition and `SelfRefine` coherence filtering.
- Returns `{"approved": bool, "critique": str, "refined_draft": Optional[str]}`.

#### 7. `MultiAgentSupervisor` (`jarvis/agents/supervisor.py`)
- Coordinates the worker instances:
  - `router: RouterAgent`
  - `retrieval: RetrievalAgent`
  - `verifier: VerifierAgent`
  - `consolidator: ConsolidatorAgent`
  - `critic: CriticAgent`
- Maintains `queue: List[AgentTask]` backed by `heapq` and `asyncio.Event` for non-blocking task notification.
- Provides:
  - `submit_task(task: AgentTask) -> str`
  - `run_next_task() -> Optional[Dict[str, Any]]`
  - `run_task_immediate(task: AgentTask) -> Dict[str, Any]`
  - `start_background_loop() -> None`
  - `stop_background_loop() -> None`
  - `get_completed_tasks() -> List[Dict[str, Any]]`

---

### 4.5 Integration with `CognitiveExecutive` and Audio Pipeline

```
+───────────────────────────────────────────────────────────────────────────+
|                           Audio Pipeline (Real-Time)                      |
|  [Mic Input] -> [VAD Segmenter] -> [Faster-Whisper STT] -> "User Query"    |
+─────────────────────────────────────┬─────────────────────────────────────+
                                      │
                                      ▼
+───────────────────────────────────────────────────────────────────────────+
|                         CognitiveExecutive (OODA)                         |
|  1. Observe                                                               |
|  2. Retrieve (Optional delegate to RetrievalAgent / MultiSignalRecall)     |
|  3. Reason & Plan (Optional delegate complex query to RouterAgent)        |
|  4. Act (Execute tools, FastMCP)                                          |
|  5. Reflect (CriticAgent / 6-stage Reflexion on error)                     |
|  6. Consolidate (ConsolidatorAgent background consolidation)              |
+─────────────────────────────────────┬─────────────────────────────────────+
                                      │
                 ┌────────────────────┴────────────────────┐
                 ▼                                         ▼
+─────────────────────────────────+       +─────────────────────────────────+
|      MultiAgentSupervisor       |       |       SQLite Storage Engine     |
|  [PriorityQueue: P1 -> P5]      |       |  - WAL Mode, BEGIN IMMEDIATE    |
|  - Router (P1)                  | <---> |  - CTE Lineage Traversal        |
|  - Critic (P2)                  |       |  - Invariants P0-P18            |
|  - Retrieval (P3)               |       |  - Atomic Markdown Sync         |
|  - Verifier (P4)                |       +─────────────────────────────────+
|  - Consolidator (P5)            |
+─────────────────────────────────+
```

---

## 5. Verification & Test Plan for Milestone 3

1. **Unit Tests**:
   - `test_agent_task_priority_ordering`: Verify `heapq` / `PriorityQueue` correctly serves Priority 1 before Priority 5.
   - `test_router_agent_complex_decomposition`: Verify multi-clause sentences are decomposed into discrete subtasks.
   - `test_retrieval_agent_scoping_and_lineage`: Verify superseded notes are excluded and lineage is traversed.
   - `test_verifier_agent_frontmatter_and_invariants`: Verify valid notes pass and invalid/privilege-violating notes fail.
   - `test_consolidator_agent_lesson_distillation`: Verify 2+ review lessons merge into a knowledge note and originals are archived.
   - `test_critic_agent_reflexion_and_refinement`: Verify 6-stage reflexion formatting and critique validation.
   - `test_supervisor_background_loop_lifecycle`: Verify start, task processing, and graceful shutdown of worker loop.
2. **Integration & E2E Tests**:
   - Run existing test suite (`python -m pytest -q tests/e2e/tier1_features/test_t1_multi_agent.py`).
   - Run full regression suite (`python -m pytest -q`).

---

## 6. Recommendations for Implementer

1. Implement `jarvis/agents/models.py` with `AgentRole`, `AgentTask`, and `TaskResult`.
2. Implement worker classes in `jarvis/agents/router.py`, `retrieval.py`, `verifier.py`, `consolidator.py`, and `critic.py`.
3. Implement `MultiAgentSupervisor` in `jarvis/agents/supervisor.py` and re-export in `jarvis/core/multi_agent.py` and `jarvis/agents/__init__.py`.
4. Update `CognitiveExecutive` in `jarvis/core/executive.py` to optionally accept `MultiAgentSupervisor` and route background tasks to it.
5. Create comprehensive unit tests in `tests/unit/test_multi_agent.py`.
6. Run `python -m pytest -q` to ensure 100% test pass rate with zero regressions.
