# Architectural Specification & Design Report: Milestone 3 Specialized Least-Privilege Agent Workers & Supervisor Coordinator

**Author**: Explorer Agent (`explorer_m3_2`)  
**Target Subsystem**: `jarvis.agents`  
**Target Codebase**: `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`  
**Date**: 2026-08-28  
**Status**: SPECIFICATION COMPLETE / READY FOR IMPLEMENTATION  

---

## 1. Architectural Overview & Context

In the Jarvis Cognitive Brain architecture, real-time interactivity is paramount: the cascaded audio pipeline demands sub-300ms Time-To-First-Byte (TTFB) and sub-50ms barge-in cancellation. However, cognitive operations such as multi-source memory consolidation, exhaustive schema compliance verification, multi-intent query decomposition, and formal post-mortem reflexion are computationally and temporally demanding.

Milestone 3 establishes a decentralized, multi-agent worker orchestration layer running behind a central **Supervisor Coordinator**. By separating responsibilities across five specialized, least-privilege agent workers, the architecture isolates background tasks from the real-time voice loop while enforcing strict cognitive trust boundaries (Invariants P0–P18).

```
+----------------------------------------------------------------------------------------------------+
|                                    COGNITIVE EXECUTIVE (OODA)                                      |
|                                                                                                    |
|   Observe ──► Retrieve ──► Reason/Plan ──► Act (FastMCP Tools) ──► Reflect ──► Consolidate         |
+──────────────┬──────────────────┬─────────────────┬───────────────────┬───────────────┬────────────+
               │                  │                 │                   │               │
               ▼                  ▼                 ▼                   ▼               ▼
+────────────────────────────────────────────────────────────────────────────────────────────────────+
|                                 SUPERVISOR COORDINATOR                                             |
|  - PriorityQueue (Priority 1 = Interactive Voice -> Priority 5 = Background Maintenance)           |
|  - Non-Blocking Async Worker Pool (Worker Tasks, Timeout Controls, Cancellation Propagation)       |
|  - Error Isolation & Telemetry Stream (Broadcasts agent lifecycle events to 3D HUD)                |
+───────┬─────────────────┬──────────────────┬───────────────────┬──────────────────────┬────────────+
        │                 │                  │                   │                      │
        ▼                 ▼                  ▼                   ▼                      ▼
  +───────────+     +───────────+      +───────────+       +──────────────+       +───────────+
  |  Router   |     | Retrieval |      | Verifier  |       | Consolidator |       |  Critic   |
  |   Agent   |     |   Agent   |      |   Agent   |       |    Agent     |       |   Agent   |
  +───────────+     +───────────+      +───────────+       +──────────────+       +───────────+
  | READ      |     | READ      |      | READ      |       | SEARCH       |       | READ      |
  | SEARCH    |     | SEARCH    |      | (Audit)   |       | READ         |       | PROPOSE   |
  |           |     |           |      |           |       | PROPOSE      |       |           |
  |           |     |           |      |           |       | ARCHIVE      |       |           |
  +─────┬─────+     +─────┬─────+      +─────┬─────+       +──────┬───────+       +─────┬─────+
        │                 │                  │                    │                     │
        └─────────────────┴──────────────────┼────────────────────┴─────────────────────┘
                                             ▼
                             +───────────────────────────────+
                             |     SCOPED STORAGE PROXY      |
                             |  Runtime Least-Privilege &    |
                             |  P0-P18 Invariant Enforcement |
                             +───────────────┬───────────────+
                                             ▼
                             +───────────────────────────────+
                             |  SQLite WAL & Markdown Sync   |
                             +───────────────────────────────+
```

---

## 2. Least-Privilege Security Governance & Trust Boundaries (P0–P18)

### 2.1 Role-Based Access Control (RBAC) Capability Matrix

Each agent operates under an immutable role with an explicitly bounded set of permitted operations. Any operation outside this capability matrix is trapped at the `ScopedStorageProxy` boundary and raises a `PermissionError`, logged to the chained SHA-256 audit log.

| Agent Role | Primary Cognitive Function | Permitted Operations | Forbidden Operations |
|---|---|---|---|
| **ROUTER** | Query analysis, intent decomposition, tool routing | `READ`, `SEARCH` | `PROPOSE`, `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `SUPERSEDE`, `DELETE` |
| **RETRIEVAL** | Associative BM25 + semantic recall, CTE lineage resolution | `READ`, `SEARCH` | `PROPOSE`, `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `SUPERSEDE`, `DELETE` |
| **VERIFIER** | Frontmatter schema audit, provenance verification, invariant check | `READ` | `PROPOSE`, `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `SUPERSEDE`, `DELETE` |
| **CONSOLIDATOR**| Synthesize REVIEW lessons, distill canonical knowledge, archive source notes | `SEARCH`, `READ`, `PROPOSE`, `ARCHIVE` | `UPDATE`, `ATTEST`, `PROMOTE`, `SUPERSEDE`, `DELETE` |
| **CRITIC** | Formal 6-stage Reflexion, SelfRefine pre-emission filtering | `READ`, `PROPOSE` | `UPDATE`, `ATTEST`, `PROMOTE`, `ARCHIVE`, `SUPERSEDE`, `DELETE` |
| **SUPERVISOR** | Coordination, scheduling, monitoring | (Delegates to workers) | Direct mutation of storage is disallowed |

### 2.2 Core Invariant Enforcement (P0–P18)

All agent actions in Milestone 3 must strictly adhere to the established trust boundaries in `jarvis.memory.invariants`:

1. **P0-001 & P0-005 (AI Self-Verification Gate)**:
   - No AI agent (including Verifier and Critic) may propose, update, or set `verification = "verified"`.
   - The status `verified` can only be set via `Operation.ATTEST` invoked by `Principal.HUMAN` or `Principal.ADMIN`.
2. **P0-002 (Privileged Provenance Types)**:
   - AI agents are restricted to `source_type in {"execution", "ai", "inference", "unknown"}`.
   - AI agents cannot claim `source_type` of `user`, `official`, `experience`, or `import`.
3. **P0-003 (Provenance Immutability)**:
   - Once a memory note is proposed and persisted, its `provenance.source_type` and `provenance.source_ref` are immutable.
4. **P0-004 (Creation Lifecycles)**:
   - AI agents may only propose notes into `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`. Direct creation in `ACTIVE` or `VERIFIED` is rejected.
5. **P0-007 & P0-008 (Promotion Gate)**:
   - Direct promotion to `ACTIVE` requires human attestation or governed pipeline elevation.
6. **P0-012 & P0-013 (Acyclic Supersession & DAG Integrity)**:
   - Supersession chains must be acyclic. A note cannot supersede itself or any of its transitive ancestors.
7. **P16–P18 (Hardware Telemetry & Forensics Immutability)**:
   - Physical hardware descriptors (`vendor_id`, `product_id`, `hardware_serial`, `evidence_sha256`) are strictly read-only and immutable.

### 2.3 Scoped Storage Access Enforcement (`ScopedStorageProxy`)

```python
class ScopedStorageProxy:
    """
    Security boundary wrapping SQLiteStorageEngine.
    Enforces agent role-based capability boundaries and P0-P18 invariants at runtime.
    """
    def __init__(self, storage: SQLiteStorageEngine, role: AgentRole, principal: Principal = Principal.AI_AGENT):
        self._storage = storage
        self._role = role
        self._principal = principal

    def _assert_op(self, op: Operation) -> None:
        allowed = ROLE_PERMISSIONS.get(self._role, set())
        if op not in allowed:
            raise PermissionError(
                f"Agent with role '{self._role.value}' is not permitted to perform operation '{op.value}'."
            )

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        self._assert_op(Operation.READ)
        return self._storage.get(note_id)

    def query(self, **kwargs) -> List[Dict[str, Any]]:
        self._assert_op(Operation.READ)
        return self._storage.query(**kwargs)

    def search_bm25(self, query: str, limit: int = 20) -> List[Dict[str, Any]]:
        self._assert_op(Operation.SEARCH)
        return self._storage.search_bm25(query, limit=limit)

    def propose(self, note: Dict[str, Any]) -> Dict[str, Any]:
        self._assert_op(Operation.PROPOSE)
        return self._storage.propose(self._principal, note)

    def archive(self, note_id: str, reason: str = "") -> Dict[str, Any]:
        self._assert_op(Operation.ARCHIVE)
        return self._storage.archive(self._principal, note_id, reason=reason)
```

---

## 3. Specialized Agent Specifications

### 3.1 Router Agent (`jarvis/agents/router.py`)

#### Role & Scope
- **Role**: `AgentRole.ROUTER`
- **Permissions**: `READ`, `SEARCH`
- **Execution Model**: Ultra-fast heuristic routing with fallback to structured LLM generation.

#### Responsibilities
1. **Query Intent Decomposition**: Parses natural language requests and separates compound multi-intent commands (e.g., `"Turn on the kitchen lights and check the memory for the wifi password"`) into independent sub-tasks organized in a directed execution graph.
2. **Scope & Tool Requirement Checking**: Determines whether each sub-task requires FastMCP IoT execution, vault memory retrieval, cognitive reasoning, or conversational synthesis.
3. **Priority Assignment**: Assigns execution priorities (Interactive Voice = Priority 1, High-priority Query = Priority 2, Background Maintenance = Priority 5).
4. **Parameter Extraction**: Extracts named entities, numerical targets, units, and domain filters.

#### Data Models & Interfaces
```python
class SubTaskScope(str, Enum):
    IOT_CONTROL = "iot_control"
    MEMORY_RETRIEVAL = "memory_retrieval"
    MEMORY_STORE = "memory_store"
    SYSTEM_STATUS = "system_status"
    CONVERSATION = "conversation"
    REASONING = "reasoning"

class DecomposedSubTask(BaseModel):
    subtask_id: int
    raw_query: str
    scope: SubTaskScope
    priority: int = 2
    dependencies: List[int] = Field(default_factory=list)
    action: str
    kwargs: Dict[str, Any] = Field(default_factory=dict)
    description: str = ""

class RouterOutput(BaseModel):
    original_query: str
    subtasks: List[DecomposedSubTask] = Field(default_factory=list)
    is_composite: bool = False
    confidence: float = 1.0
    routing_latency_ms: float = 0.0

class RouterAgent(BaseAgent):
    async def decompose(self, query: str, context: Optional[Dict[str, Any]] = None) -> RouterOutput: ...
```

---

### 3.2 Retrieval Agent (`jarvis/agents/retrieval.py`)

#### Role & Scope
- **Role**: `AgentRole.RETRIEVAL`
- **Permissions**: `READ`, `SEARCH`
- **Execution Model**: Deterministic multi-signal associative recall and lineage traversal.

#### Responsibilities
1. **Multi-Signal Lexical & Semantic Recall**: Combines parameterized BM25 keyword matching with token cosine fallback.
2. **Spreading Activation & ACT-R Access History**: Propagates cognitive energy across relations (`related_to`, `depends_on`, `derived_from`) and applies ACT-R decay based on historical frequency and recency.
3. **Recursive CTE Supersession Lineage Traversal**: Identifies superseded notes and traverses lineage graphs to replace obsolete information with active successor notes.
4. **Temporal Validity & Version Filtering**: Validates `valid_until` timestamps and software/hardware version tags to eliminate stale facts.

#### Data Models & Interfaces
```python
class RetrievalQuery(BaseModel):
    query: str
    working_memory_context: Optional[List[Dict[str, Any]]] = None
    limit: int = 10
    include_superseded: bool = False
    temporal_override: Optional[str] = None

class ScoredMemoryNote(BaseModel):
    note: Dict[str, Any]
    composite_score: float
    lexical_score: float = 0.0
    activation_score: float = 0.0
    confidence_score: float = 0.0
    lineage_active_successor_id: Optional[str] = None

class RetrievalResult(BaseModel):
    query: str
    notes: List[ScoredMemoryNote] = Field(default_factory=list)
    total_candidates: int = 0
    retrieval_time_ms: float = 0.0

class RetrievalAgent(BaseAgent):
    async def retrieve(self, request: RetrievalQuery) -> RetrievalResult: ...
    async def resolve_lineage(self, note_id: str) -> Optional[Dict[str, Any]]: ...
```

---

### 3.3 Verifier Agent (`jarvis/agents/verifier.py`)

#### Role & Scope
- **Role**: `AgentRole.VERIFIER`
- **Permissions**: `READ` (Strict Read-Only Audit)
- **Execution Model**: Fast, deterministic schema and constraint evaluation.

#### Responsibilities
1. **Frontmatter Schema Audit**: Validates YAML metadata conform to `NoteFrontmatter` specifications (UUID validation, ISO dates, permitted NoteTypes and Lifecycles).
2. **Provenance & Least-Privilege Verification**: Enforces that AI-generated proposals do not claim privileged sources (`user`, `official`, `experience`, `import`) and that source references are non-empty.
3. **Invariant P0–P18 Compliance Audit**: Verifies that unverified notes are not marked `verified`, lifecycle states comply with creation rules, and hardware telemetry is unmodified.
4. **Wikilink Syntax & Referential Integrity**: Checks that all `[[wikilinks]]` and relation target IDs are syntactically valid and point to existing or proposed entities.

#### Data Models & Interfaces
```python
class ViolationSeverity(str, Enum):
    ERROR = "error"          # Blocks persistence/action
    WARNING = "warning"      # Non-blocking advisory
    INFO = "info"

class SchemaViolation(BaseModel):
    field: str
    rule: str
    message: str
    severity: ViolationSeverity = ViolationSeverity.ERROR

class VerificationReport(BaseModel):
    note_id: Optional[str] = None
    is_valid: bool
    violations: List[SchemaViolation] = Field(default_factory=list)
    audit_timestamp: str
    auditor_role: str = "verifier"

class VerifierAgent(BaseAgent):
    def verify_note(self, note_data: Dict[str, Any]) -> VerificationReport: ...
    def verify_provenance(self, provenance: Dict[str, Any], principal: Principal) -> VerificationReport: ...
```

---

### 3.4 Consolidator Agent (`jarvis/agents/consolidator.py`)

#### Role & Scope
- **Role**: `AgentRole.CONSOLIDATOR`
- **Permissions**: `SEARCH`, `READ`, `PROPOSE`, `ARCHIVE`
- **Execution Model**: Background asynchronous synthesis and atomic memory reconsolidation.

#### Responsibilities
1. **Ephemeral Lesson Clustering**: Scans `REVIEW` lifecycle notes for recurring `ERROR` or `LESSON` notes (minimum cluster threshold: 2 related items).
2. **Knowledge Distillation**: Synthesizes clusters into cohesive, generalizable canonical knowledge notes with `provenance.source_type = "inference"` and bidirectional `derived_from` wikilinks.
3. **Atomic Source Archival**: Automatically transitions processed source lessons to `ARCHIVED` lifecycle with an explicit `archive_reason` citing the new knowledge note ID.
4. **Plastic Memory Reconsolidation**: Manages `RECONSOLIDATING` state transitions when new empirical observations contradict existing active knowledge notes, preserving previous versions in rollback metadata.

#### Data Models & Interfaces
```python
class ConsolidationCluster(BaseModel):
    cluster_id: str
    topic: str
    source_note_ids: List[str]
    proposed_content: str
    tags: List[str] = Field(default_factory=list)

class ConsolidationSummary(BaseModel):
    consolidated_note_id: Optional[str] = None
    archived_source_ids: List[str] = Field(default_factory=list)
    cluster_count: int = 0
    status: str = "success"

class ConsolidatorAgent(BaseAgent):
    async def scan_and_consolidate(self, limit: int = 50) -> ConsolidationSummary: ...
    async def challenge_note(self, note_id: str, conflicting_evidence: Dict[str, Any]) -> Dict[str, Any]: ...
    async def resolve_challenge(self, note_id: str, resolved_node: Optional[Dict[str, Any]]) -> Dict[str, Any]: ...
```

---

### 3.5 Critic Agent (`jarvis/agents/critic.py`)

#### Role & Scope
- **Role**: `AgentRole.CRITIC`
- **Permissions**: `READ`, `PROPOSE`
- **Execution Model**: Hybrid fast deterministic heuristics + structured LLM critique.

#### Responsibilities
1. **Formal 6-Stage Reflexion**: Generates structured post-mortem analysis on plan failures adhering to the 6-stage protocol:
   - `Error` -> `Root Cause` -> `Fix Applied` -> `Verification` -> `Prevention Rule` -> `Core Lesson`.
2. **SelfRefine Quality Gate**: Audits generated response drafts and candidate memory notes for:
   - Factual grounding against retrieved memory context.
   - Absence of hallucinated facts, API keys, or forbidden secrets.
   - Brevity, conciseness, and tone appropriate for voice output (<50 words for voice synthesis).
3. **Proposes Reflexion Lessons**: Submits validated reflection notes into `REVIEW` storage for subsequent consolidation.

#### Data Models & Interfaces
```python
class ReflexionAnalysis(BaseModel):
    error: str
    root_cause: str
    fix_applied: str
    verification: str
    prevention_rule: str
    core_lesson: str

    def to_markdown(self) -> str:
        return (
            f"## Formal Reflexion Analysis\n\n"
            f"- **Error**: {self.error}\n"
            f"- **Root Cause**: {self.root_cause}\n"
            f"- **Fix Applied**: {self.fix_applied}\n"
            f"- **Verification**: {self.verification}\n"
            f"- **Prevention Rule**: {self.prevention_rule}\n"
            f"- **Core Lesson**: {self.core_lesson}\n"
        )

class CritiqueResult(BaseModel):
    approved: bool
    score: float  # 0.0 to 1.0
    critique: str
    suggested_refinement: Optional[str] = None
    flags: List[str] = Field(default_factory=list)

class CriticAgent(BaseAgent):
    async def reflect_on_error(self, step_action: str, error_msg: str, context: Optional[Dict[str, Any]] = None) -> str: ...
    async def critique_draft(self, draft: str, context: Optional[List[Dict[str, Any]]] = None, is_voice: bool = True) -> CritiqueResult: ...
```

---

## 4. Supervisor Coordinator Architecture & Implementation

### 4.1 Priority Scheduling & Non-Blocking Execution Model

The `SupervisorCoordinator` utilizes an asynchronous priority queue (`asyncio.PriorityQueue` / min-heap with a monotonic sequence counter) to ensure that interactive voice tasks are never blocked by background maintenance or long-running memory consolidation jobs.

```
Priority 1: Urgent Interactive Voice (STT parsing, real-time IoT commands, voice responses)
Priority 2: Interactive Queries (Knowledge lookup, structured planning)
Priority 3: Standard Multi-Step Subtasks (Batch IoT actions, sensor polling)
Priority 4: Verifier Audits & Pre-consolidation checks
Priority 5: Background Maintenance (Lesson clustering, database reindexing, plastic memory reconsolidation)
```

```python
class AgentTask(BaseModel):
    task_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    priority: int = 3  # 1 (highest) to 5 (lowest)
    role: AgentRole
    action: str
    payload: Dict[str, Any] = Field(default_factory=dict)
    created_at: float = Field(default_factory=time.time)
    timeout_seconds: float = 30.0
    cancellation_token: Optional[CancellationToken] = None

    def __lt__(self, other: "AgentTask") -> bool:
        # Lower priority number executes first; tie-break on creation time
        if self.priority == other.priority:
            return self.created_at < other.created_at
        return self.priority < other.priority

class AgentTaskResult(BaseModel):
    task_id: str
    role: AgentRole
    action: str
    status: Literal["success", "failed", "cancelled", "timeout"]
    result: Optional[Any] = None
    error: Optional[str] = None
    execution_time_ms: float = 0.0
```

### 4.2 Non-Blocking Async Worker Pool & Lifecycle Management

The coordinator maintains a pool of `asyncio.Task` workers executing concurrently up to `max_workers` (default: 4):

```python
class SupervisorCoordinator:
    """
    Coordinates specialized agent workers using a prioritized asynchronous task queue.
    Provides complete error isolation, worker pool lifecycle management, and non-blocking operation.
    """
    def __init__(
        self,
        storage: SQLiteStorageEngine,
        llm: BaseLLMProvider,
        max_workers: int = 4,
        telemetry_callback: Optional[Callable[[str, Dict[str, Any]], Any]] = None,
    ):
        self.storage = storage
        self.llm = llm
        self.max_workers = max_workers
        self.telemetry_callback = telemetry_callback

        self._queue: asyncio.PriorityQueue[Tuple[int, int, AgentTask]] = asyncio.PriorityQueue()
        self._seq = 0  # Monotonic sequence counter to break priority ties
        self._running = False
        self._workers: List[asyncio.Task] = []
        self._active_tasks: Dict[str, AgentTask] = {}
        self._task_futures: Dict[str, asyncio.Future] = {}

        # Instantiate specialized agents with least-privilege scoped proxies
        self.router = RouterAgent(ScopedStorageProxy(storage, AgentRole.ROUTER), llm)
        self.retrieval = RetrievalAgent(ScopedStorageProxy(storage, AgentRole.RETRIEVAL), llm)
        self.verifier = VerifierAgent(ScopedStorageProxy(storage, AgentRole.VERIFIER), llm)
        self.consolidator = ConsolidatorAgent(ScopedStorageProxy(storage, AgentRole.CONSOLIDATOR), llm)
        self.critic = CriticAgent(ScopedStorageProxy(storage, AgentRole.CRITIC), llm)

    async def start(self) -> None:
        """Start the background worker pool."""
        self._running = True
        self._workers = [
            asyncio.create_task(self._worker_loop(f"worker-{i}"))
            for i in range(self.max_workers)
        ]

    async def stop(self) -> None:
        """Gracefully stop workers and flush queues."""
        self._running = False
        for w in self._workers:
            w.cancel()
        await asyncio.gather(*self._workers, return_exceptions=True)
        self._workers.clear()

    async def submit_task(self, task: AgentTask) -> asyncio.Future:
        """Submit a task to the priority queue and return an awaitable Future."""
        self._seq += 1
        loop = asyncio.get_running_loop()
        fut = loop.create_future()
        self._task_futures[task.task_id] = fut
        await self._queue.put((task.priority, self._seq, task))
        self._emit_telemetry("task_submitted", {"task_id": task.task_id, "role": task.role, "priority": task.priority})
        return fut

    async def execute_task_direct(self, task: AgentTask) -> AgentTaskResult:
        """Direct, high-priority synchronous dispatch for real-time interactive paths."""
        return await self._dispatch(task)
```

### 4.3 Error Isolation & Circuit Breaking

Worker tasks wrap agent executions in boundary handlers. If an agent raises an unhandled exception or times out:
1. The error is captured in `AgentTaskResult(status="failed", error=str(exc))`.
2. The failure is recorded in the audit trail.
3. The supervisor remains fully operational; no exception bubbles up to crash the event loop or block the audio engine.

```python
    async def _dispatch(self, task: AgentTask) -> AgentTaskResult:
        t0 = time.time()
        self._active_tasks[task.task_id] = task
        self._emit_telemetry("task_started", {"task_id": task.task_id, "role": task.role})

        try:
            # Check cancellation
            if task.cancellation_token and task.cancellation_token.is_cancelled:
                return AgentTaskResult(
                    task_id=task.task_id, role=task.role, action=task.action,
                    status="cancelled", error="Task cancelled before execution",
                    execution_time_ms=(time.time() - t0) * 1000.0,
                )

            # Route to specialized worker with timeout guard
            async with asyncio.timeout(task.timeout_seconds):
                if task.role == AgentRole.ROUTER:
                    res = await self.router.execute(task)
                elif task.role == AgentRole.RETRIEVAL:
                    res = await self.retrieval.execute(task)
                elif task.role == AgentRole.VERIFIER:
                    res = await self.verifier.execute(task)
                elif task.role == AgentRole.CONSOLIDATOR:
                    res = await self.consolidator.execute(task)
                elif task.role == AgentRole.CRITIC:
                    res = await self.critic.execute(task)
                else:
                    raise ValueError(f"Unknown agent role '{task.role}'")

            exec_ms = (time.time() - t0) * 1000.0
            result = AgentTaskResult(
                task_id=task.task_id, role=task.role, action=task.action,
                status="success", result=res, execution_time_ms=exec_ms,
            )
            self._emit_telemetry("task_completed", {"task_id": task.task_id, "exec_ms": exec_ms})
            return result

        except asyncio.TimeoutError:
            exec_ms = (time.time() - t0) * 1000.0
            self._emit_telemetry("task_timeout", {"task_id": task.task_id, "exec_ms": exec_ms})
            return AgentTaskResult(
                task_id=task.task_id, role=task.role, action=task.action,
                status="timeout", error=f"Task exceeded timeout of {task.timeout_seconds}s",
                execution_time_ms=exec_ms,
            )
        except Exception as exc:
            exec_ms = (time.time() - t0) * 1000.0
            self._emit_telemetry("task_failed", {"task_id": task.task_id, "error": str(exc)})
            return AgentTaskResult(
                task_id=task.task_id, role=task.role, action=task.action,
                status="failed", error=str(exc), execution_time_ms=exec_ms,
            )
        finally:
            self._active_tasks.pop(task.task_id, None)
```

### 4.4 Barge-In & Cooperative Cancellation

When a user interrupts ongoing TTS speech (VAD speech trigger in `BargeInController`):
1. `BargeInController` cancels active LLM text generation tokens.
2. `SupervisorCoordinator.cancel_tasks_matching(predicate)` can immediately signal cancellation to any in-flight background worker tasks that consume GPU/CPU resources, ensuring 100% of computational throughput returns to the interactive audio STT/TTS pipeline.

---

## 5. Interface Contracts & CognitiveExecutive Integration Blueprint

### 5.1 Directory & Module Layout

The codebase layout adheres to `PROJECT.md` specifications under `jarvis/agents/`:

```
projects/jarvis_cognitive_brain/
├── jarvis/
│   ├── agents/
│   │   ├── __init__.py           # Package exports
│   │   ├── base.py               # BaseAgent, AgentRole, Operation, ScopedStorageProxy, AgentTask
│   │   ├── supervisor.py         # SupervisorCoordinator, PriorityQueue, WorkerPool
│   │   ├── router.py             # RouterAgent, SubTask decomposition, scope analyzer
│   │   ├── retrieval.py          # RetrievalAgent, multi-signal recall, CTE lineage resolver
│   │   ├── verifier.py           # VerifierAgent, frontmatter audit, P0-P18 validator
│   │   ├── consolidator.py       # ConsolidatorAgent, lesson clustering, memory reconsolidation
│   │   └── critic.py             # CriticAgent, 6-stage Reflexion, SelfRefine critique
```

### 5.2 Seamless Integration with OODA Cognitive Loop

The `CognitiveExecutive` and `OODACognitiveEngine` delegate each phase to the corresponding specialized worker:

```python
class OODACognitiveEngine:
    """Enhanced OODA Cognitive Engine utilizing Specialized Least-Privilege Agents."""
    
    def __init__(self, supervisor: SupervisorCoordinator, ...):
        self.supervisor = supervisor

    async def observe(self, perception: PerceptionEvent) -> UserIntent:
        # Direct high-priority dispatch to RouterAgent
        task = AgentTask(priority=1, role=AgentRole.ROUTER, action="decompose", payload={"query": perception.raw_data})
        res = await self.supervisor.execute_task_direct(task)
        router_out: RouterOutput = res.result
        return UserIntent(
            raw_text=perception.raw_data,
            intent_type=map_scope_to_intent(router_out.subtasks[0].scope),
            requires_tool=any(s.scope == SubTaskScope.IOT_CONTROL for s in router_out.subtasks),
        )

    async def retrieve(self, intent: UserIntent) -> List[Dict[str, Any]]:
        # High-priority dispatch to RetrievalAgent
        task = AgentTask(priority=1, role=AgentRole.RETRIEVAL, action="retrieve", payload={"query": intent.raw_text})
        res = await self.supervisor.execute_task_direct(task)
        retrieval_res: RetrievalResult = res.result
        return [sn.note for sn in retrieval_res.notes]

    async def reflect(self, target: Union[ActivePlan, PlanStep], error: str) -> Optional[str]:
        # Dispatch to CriticAgent for formal 6-stage Reflexion
        task = AgentTask(priority=2, role=AgentRole.CRITIC, action="reflect_error", payload={"step_action": target.action, "error": error})
        res = await self.supervisor.execute_task_direct(task)
        return res.result  # Returns reflection note ID

    async def consolidate(self) -> None:
        # Non-blocking background dispatch to ConsolidatorAgent (Priority 5)
        task = AgentTask(priority=5, role=AgentRole.CONSOLIDATOR, action="consolidate_lessons")
        await self.supervisor.submit_task(task)  # Fire and forget; runs in worker pool
```

---

## 6. Testability & Verification Strategy

### 6.1 Test Coverage Matrix for Milestone 3

| Test Category | Target Component | Invariants / Scenarios Covered |
|---|---|---|
| **Priority Queue Scheduling** | `SupervisorCoordinator` | Verifies priority ordering (P1 preempts P5), FIFO tie-breaking with monotonic seq. |
| **Worker Concurrency & Non-Blocking** | `SupervisorCoordinator` | Proves background tasks run concurrently without stalling caller; timeout guards fire properly. |
| **RBAC Security Boundaries** | `ScopedStorageProxy` | Verifies `PermissionError` when `ROUTER` attempts `propose`, `VERIFIER` attempts `update`, or `CRITIC` attempts `archive`. |
| **P0-P18 Trust Invariants** | `VerifierAgent` & Proxy | Asserts AI agent self-verification prohibition (`verification='verified'`), provenance source protection. |
| **Intent Decomposition** | `RouterAgent` | Tests compound query splitting into DAG subtasks (IoT + Retrieval + Response). |
| **CTE Lineage & Recall** | `RetrievalAgent` | Asserts superseded notes resolve to active head notes without write side effects. |
| **Lesson Synthesis & Archival** | `ConsolidatorAgent` | Confirms clustering of 2+ REVIEW lessons into canonical knowledge and source note archival. |
| **6-Stage Reflexion & SelfRefine** | `CriticAgent` | Validates structured post-mortem formatting and pre-voice response length/quality gating. |
| **Fault Isolation & Recovery** | `SupervisorCoordinator` | Injects intentional exceptions and timeouts in worker agents, verifying daemon stability. |

---

## 7. Conclusion

This design provides a production-grade, least-privilege multi-agent architecture specifically tailored to the real-time constraints and cognitive trust boundaries of the Jarvis Cognitive Brain. Implementation can proceed cleanly in `jarvis/agents/` with 100% adherence to `PROJECT.md` and `vault_cognitive_rules.md`.
