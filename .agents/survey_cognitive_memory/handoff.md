# Technical Survey & Specification Mining Report: Requirement R1 (Cognitive Core & Persistent Memory)

**Project**: Jarvis Cognitive Brain ('Creier Vorbitor')  
**Working Directory**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`  
**Investigator**: Explorer 1 (Cognitive Core & Persistent Memory Specialist)  
**Date**: 2026-08-27  
**Status**: COMPLETE (Hard Handoff)

---

## 1. Observation

Direct observations extracted from the authoritative requirements, vault architecture, core governance policies, and existing prototype modules:

### 1.1 Authoritative Requirement (R1)
From `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md` (lines 134–141):
```text
### R1. Cognitive Loop Self-Execution & Memory Persistent Storage
Establish a local daemon utilizing the Google Antigravity SDK. Configured out-of-the-box to prioritize local LLMs (e.g., via Ollama running local models like `qwen2.5-coder`), but structured modularly to allow simple configuration-level swapping to external API models (Gemini API, Claude API, etc.) in the future. Implement a full stateful OODA cycle:
- Observe: Classify incoming vocal/text requests.
- Retrieve: Use associative and semantic recall to fetch context from an Obsidian-style markdown database and a persistent database.
- Reason/Plan: Formulate structured multi-step plans.
- Act: Route tool calls via FastMCP.
- Reflect/Consolidate: Run self-reflection (Reflexion) and store consolidated lessons back to the long-term memory.
```

### 1.2 Vault Governance & Operating Contract
- **Source of Truth Hierarchy** (`AGENTS.md:10-23`):
  1. Explicitly confirmed by the user
  2. Directly verified by execution/test
  3. Official primary documentation
  4. Project documentation in Vault
  5. Repeated successful experience
  6. Other external sources
  7. AI-generated or inferred information
- **Canonical Lifecycle Path** (`00_CORE/Memory_Protocol.md:57-60`, `AGENTS.md:144-150`):
  `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED`
  `RAW` remains permanently in `06_INBOX/RAW_IMPORTS/` and is never indexed as canonical knowledge.
- **Trust Boundary Invariants (P0–P15)** (`.agents/rules/vault_cognitive_rules.md:3-9`, `99_SYSTEM/Canonical_Frontmatter.md:1-68`):
  - `Principal.AI_AGENT` cannot set `verification = "verified"`.
  - Only `Principal.HUMAN` and `Principal.ADMIN` can invoke `controller.attest()` via `Operation.ATTEST` or promote to `ACTIVE`.
  - Permitted `source_type` for `Principal.AI_AGENT`: `{"execution", "ai", "inference", "unknown"}`. Restricted: `{"user", "official", "experience", "import"}`.
  - Creation lifecycles for AI agent: `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`.
  - Supersession invariants (`00_CORE/Memory_Protocol.md:85-98`): Reciprocal links (`supersedes` / `superseded_by`), non-self-referential, non-cyclic, atomic 2-node updates.

### 1.3 Existing Storage Engine & SQLite WAL Concurrency
- `memory_controller/storage/sqlite_engine.py:14-45`:
  - Table `notes` schema enforcing SQL CHECK constraints on `type`, `lifecycle`, `source_type`, `confidence`, and `verification`.
  - PRAGMAs configured on connection (`sqlite_engine.py:74-78`):
    `PRAGMA journal_mode=WAL;`
    `PRAGMA synchronous=NORMAL;`
    `PRAGMA busy_timeout=5000;`
    `PRAGMA foreign_keys=ON;`
  - Transaction isolation (`sqlite_engine.py:70`, `180-189`): `isolation_level=None` with explicit `BEGIN IMMEDIATE;` ... `COMMIT;` / `ROLLBACK;`.
  - Recursive CTE lineage traversal (`sqlite_engine.py:224-240`) for superseded note chains up to depth 50.
  - Thread safety (`sqlite_engine.py:51`, `64-82`): `threading.local()` connection handling.
- `memory_controller/storage/file_engine.py:89-102`:
  - Atomic Markdown write: `tempfile.mkstemp(dir=dir_name, prefix=".tmp_")` -> `os.fsync(f.fileno())` -> `os.replace(temp_path, target_path)`.
  - Strict exclusion of `06_INBOX` and `90_TEMPLATES` from canonical memory index.

### 1.4 Cognitive Loop Modules in Workspace
- **Executive Orchestrator** (`cognitive_core/executive.py:79-153`, `183-226`):
  - Stateful loop: `process_intent` (Observe -> Retrieve -> Attend -> Reason -> Plan -> Execute) followed by `step_loop` (Act -> Checkpoint -> Synapse -> Reflect -> Maintenance).
  - WIRE-5 Checkpointing (`executive.py:50-59`): Saves working memory (`wm.json`) and active plan (`plan.json`) atomically.
  - WIRE-6 Error Recovery (`executive.py:133-142`): Replanning up to `max_retries=2` on step failure.
- **Formal Reflexion & SelfRefine** (`cognitive_core/reflection.py:8-30`, `31-56`, `72-104`):
  - 6-stage structured reflection template: `Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson`.
  - Submits proposed notes in `REVIEW` lifecycle with `provenance.source_type = "inference"`.
  - `SelfRefine` filter validates candidate length >= 15 chars, structure, and coherence.
- **Consolidation & Reconsolidation** (`cognitive_core/consolidation.py:21-96`):
  - Memory Reconsolidation: `challenge()` transitions canonical/active note to `RECONSOLIDATING` when challenged by conflicting evidence, preserving previous version and history.
  - `consolidate_lessons()` scans `REVIEW` lesson notes, aggregates recurring lessons into `knowledge` notes, proposes the synthesis, and archives the originals.
- **Associative Recall & ACT-R Activation** (`cognitive_core/recall.py:26-32`, `cognitive_core/activation.py:25-46`):
  - Multi-signal scoring weights: `semantic=0.35`, `activation=0.25`, `wm_relevance=0.15`, `confidence=0.15`, `authority=0.10`.
  - ACT-R base-level decay: $B_i = \ln(\sum_{j=1}^n (t - t_j)^{-d})$ with $d=0.5$ and dormant threshold $-2.0$.
  - Version-aware recall: $+0.3$ confidence boost for exact technology version matches; $-0.3$ penalty for version mismatches.
- **Ollama Local LLM Extractor** (`cognitive_core/ollama_extractor.py:22-65`):
  - HTTP client invoking `http://localhost:11434/api/generate` with JSON-mode prompt formatting and graceful parsing fallback to deterministic path.

---

## 2. Logic Chain

```
[R1 Requirement: Self-Executing Cognitive Brain & Persistent Memory]
   │
   ├──> 1. LLM Provider Abstraction Layer
   │       └── Multi-Provider Strategy (Ollama 'qwen2.5-coder' primary -> Gemini/Claude fallback -> Mock test provider)
   │       └── Streaming Interface for Real-Time Cascaded Audio Loop (TTFB < 300ms)
   │       └── Structured Output (Pydantic v2 JSON-Schema constrained generation)
   │
   ├──> 2. Complete Stateful OODA Cognitive Loop
   │       ├── Observe: Perception & Intent Classification (UserIntent schema, SensorBuffer)
   │       ├── Retrieve: Multi-layer Associative Recall (BM25 + Semantic + ACT-R Decay + Wikilinks + Lineage)
   │       ├── Reason/Plan: Structured ActivePlan State Machine & Precondition Validator
   │       ├── Act: Least-Privilege FastMCP Tool Router (Home Assistant IoT + Vault Actions)
   │       ├── Reflect: 6-Stage Formal Reflexion on Failures/Blocks -> Lesson Generation
   │       └── Consolidate: Lesson Synthesis + Plastic Memory Reconsolidation + Dynamic Synapses
   │
   ├──> 3. Persistent Storage Engine & Markdown Sync
   │       ├── SQLite WAL Engine: PRAGMA journal_mode=WAL, PRAGMA busy_timeout=5000, BEGIN IMMEDIATE
   │       ├── Thread-safe connection pool via threading.local()
   │       ├── Bidirectional Markdown Sync: Atomic tempfile writes + Frontmatter JSON-Schema validator
   │       └── Lineage & Reciprocal Supersession Graph
   │
   └──> 4. Trust Boundaries & Security Governance (P0–P18)
           ├── AI Agent cannot self-verify (verification != "verified")
           ├── Proposal lifecycle strictly restricted to REVIEW/RAW
           └── Tamper-Evident SHA-256 Chained Audit Trail
```

### 2.1 LLM Provider Layer Logic
The system must run autonomously without requiring cloud internet access (100% free local execution via Ollama), while supporting enterprise cloud scaling (Gemini, Claude) and fully isolated deterministic testing in CI:
- **Base Interface**: `BaseLLMProvider` defining `generate()`, `chat()`, `generate_structured()`, and `stream()`.
- **Primary Local Provider**: `OllamaProvider` connecting to `http://localhost:11434` with model `qwen2.5-coder` (or `qwen2.5:7b`/`llama3.1`). Utilizes stream generation to feed the audio TTS pipeline immediately.
- **Cloud Fallback Adapters**: `GeminiProvider` (Google Antigravity / Generative AI SDK) and `ClaudeProvider` (Anthropic API), active only when configured.
- **Deterministic Mock Provider**: `MockLLMProvider` returning pre-programmed structured responses for zero-dependency unit tests.
- **Circuit Breaker / Fallback Chain**: If local Ollama is offline or times out (>5s), the provider orchestrator falls back to the configured secondary provider or raises a structured `ProviderUnavailableError` triggering Reflexion.

### 2.2 Stateful OODA Cycle Logic
Each conversational turn or autonomous goal executes a discrete stateful cycle:
1. **Observe (Perception)**:
   - Voice audio transcription (from STT engine) or text prompt from API/HUD arrives.
   - SensorBuffer records the ephemeral perception event with timestamp and source channel.
   - Intent Classifier parses the utterance into a validated `UserIntent` (intent type: `QUERY`, `IOT_CONTROL`, `TASK`, `MEMORY_STORE`, `SYSTEM_STATUS`).
2. **Retrieve (Recall & Working Memory Admission)**:
   - Recall query formulated from intent and active context.
   - Dual search: BM25 lexical keyword scan + dense cosine vector similarity over active canonical notes (`00_CORE` through `05_RESOURCES`).
   - ACT-R base-level activation calculation calculates memory recency/frequency strength.
   - Graph spreading activation traverses `[[wikilinks]]` across semantic, temporal, causal, and entity relations.
   - Supersession resolution: CTE lineage traversal resolves any superseded note to its latest active successor.
   - Working Memory admits top-ranked nodes (capacity: 7–10 chunks).
3. **Reason & Plan (Structured Planner)**:
   - Reasoner synthesizes working memory context and tags unverified items (`_cognitive_unverified = True` for `REVIEW` notes).
   - Planner creates an `ActivePlan` containing discrete `PlanStep` objects.
   - Each step specifies: `step_id`, `action`, `kwargs`, `preconditions`, `expected_outcome`, `risk_level`.
   - Atomic Checkpointing: `wm.json` and `plan.json` are written atomically via tempfiles before and after each step execution.
4. **Act (Tool Execution & Least Privilege)**:
   - Executes steps via FastMCP tool server (`JarvisControls`) or internal ToolRouter.
   - Authorizer validates permissions: `Principal.AI_AGENT` is allowed `READ`, `SEARCH`, `PROPOSE`, `UPDATE` (drafts), but blocked from direct `DELETE`, `ATTEST`, or `PROMOTE` to `ACTIVE`.
   - High-risk operations (e.g. destructive IoT state, bulk file modification) return `status="blocked"`, prompting user confirmation.
5. **Reflect (Formal Reflexion & Learning)**:
   - On step error or block, `ReflexionEngine` executes the 6-stage reflection:
     `Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson`.
   - Generates a structured Markdown reflection note proposed to `04_MEMORY/Errors/` or `04_MEMORY/Lessons/` in `REVIEW` lifecycle with `provenance.source_type = "inference"`.
6. **Consolidate (Reconsolidation & Synapse Formation)**:
   - Maintenance routine scans `REVIEW` lesson notes; when 2+ related lessons accumulate, `Consolidator` synthesizes a unified `knowledge` note.
   - Applies `SelfRefine` critique filter before proposal.
   - Co-activated nodes in Working Memory dynamically form `relations` and `[[wikilinks]]`.
   - If conflicting evidence is encountered against an `ACTIVE` note, the note is transitioned to `RECONSOLIDATING`, preserving historical versioning.

### 2.3 Persistent Database & Markdown Sync Logic
Dual-persistence architecture ensures high-speed relational queries alongside human-readable Obsidian vault files:
- **SQLite Engine**:
  - WAL journal mode (`PRAGMA journal_mode=WAL;`) allows simultaneous read queries without blocking writes.
  - `PRAGMA busy_timeout=5000;` ensures threads wait up to 5s during lock contention.
  - `PRAGMA synchronous=NORMAL;` guarantees durability without excessive disk flushes.
  - `PRAGMA mmap_size=268435456;` enables 256MB memory-mapped I/O.
  - `BEGIN IMMEDIATE;` acquires write locks up front, preventing deadlock during concurrent transactions.
  - `threading.local()` provides isolated connections per worker thread.
- **Markdown File Sync Engine**:
  - All notes are persisted as `.md` files with YAML frontmatter adhering to `Canonical Frontmatter`.
  - Atomic File Persistence: writes to `.tmp_...` in destination folder, executes `os.fsync()`, and atomically replaces target via `os.replace()`.
  - Two-way sync: SQLite serves as high-speed query/index layer; filesystem modifications trigger re-indexing and validation.

---

## 3. Caveats

1. **Audio & FastMCP Boundary**: This survey strictly specifies Requirement R1 (Cognitive Core & Persistent Memory). Audio STT/TTS pipeline details (Silero VAD, Faster-Whisper, Kokoro-82M ONNX, Barge-in AEC) and IoT Home Assistant REST server implementation are covered by companion specialists.
2. **Local LLM Model Availability**: While Ollama `qwen2.5-coder` is the recommended default, the host machine may run other local models (e.g. `llama3.1`, `qwen2.5:7b`, `mistral`). The provider layer is decoupled from specific model names via environment configuration (`OLLAMA_MODEL`, `OLLAMA_HOST`).
3. **Database Write Concurrency**: SQLite WAL supports multiple concurrent readers and exactly one active writer. High concurrency from multiple agent workers is safely handled by `PRAGMA busy_timeout=5000` and thread-local connections, but intensive batch operations should be sequenced through the `MemoryController` write queue.
4. **No Destructive Direct File Writes**: Explorer 1 is in read-only investigation mode; no implementation code has been placed in `projects/jarvis_cognitive_brain`.

---

## 4. Conclusion & Technical Specifications

The following detailed architecture, data models, class contracts, and file layout are specified for Requirement R1:

### 4.1 Target Codebase Layout (`projects/jarvis_cognitive_brain`)

```
projects/jarvis_cognitive_brain/
├── __init__.py
├── config.py                     # Environment variables, model settings, paths
├── core/
│   ├── __init__.py
│   ├── ooda_engine.py            # Central OODA Cognitive Loop Controller
│   ├── llm_provider.py           # Modular BaseLLMProvider, Ollama, Gemini, Claude, Mock
│   ├── perception.py             # Observe phase & UserIntent classifier
│   ├── working_memory.py         # WorkingMemory with ACT-R activation decay
│   ├── recall_engine.py          # BM25 + Semantic + Graph spreading activation
│   ├── planner.py                # Multi-step ActivePlan generator & replanner
│   ├── executor.py               # Act phase & FastMCP tool router bridge
│   ├── reflexion.py              # 6-stage Formal Reflexion & SelfRefine
│   └── consolidator.py           # Lesson consolidation & Memory Reconsolidation
├── storage/
│   ├── __init__.py
│   ├── sqlite_wal.py             # SQLite WAL Storage Engine with atomic transactions
│   ├── markdown_sync.py          # Bidirectional Markdown note sync & atomic file writer
│   ├── schema_validator.py       # Pydantic v2 Canonical Frontmatter validator
│   └── audit_logger.py           # Tamper-evident SHA-256 chained audit logger
├── models/
│   ├── __init__.py
│   ├── frontmatter.py            # Pydantic models for NoteFrontmatter, Provenance, Relation
│   ├── intent.py                 # UserIntent, IntentType, Entity models
│   ├── plan.py                   # ActivePlan, PlanStep, StepStatus models
│   ├── tool.py                   # ToolCall, ToolResult, RiskLevel models
│   └── reflection.py             # ReflexionReport, ReconsolidationChallenge models
└── tests/
    ├── __init__.py
    ├── conftest.py               # Pytest fixtures: in-memory DB, mock LLM, temp vault
    ├── test_ooda_loop.py         # End-to-end OODA cycle tests
    ├── test_llm_providers.py     # Provider fallback & streaming tests
    ├── test_sqlite_wal.py        # WAL concurrency & transaction tests
    ├── test_markdown_sync.py     # SQLite <-> Markdown sync & atomic replace tests
    └── test_reflexion.py         # 6-stage Reflexion & reconsolidation tests
```

---

### 4.2 Data Models & Schemas (Pydantic v2)

```python
# models/frontmatter.py
from pydantic import BaseModel, Field, field_validator
from typing import List, Optional, Literal, Dict, Any
from enum import Enum
import uuid

class LifecycleEnum(str, Enum):
    RAW = "RAW"
    CLASSIFIED = "CLASSIFIED"
    NORMALIZED = "NORMALIZED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"
    ACTIVE = "ACTIVE"
    RECONSOLIDATING = "RECONSOLIDATING"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"

class NoteTypeEnum(str, Enum):
    KNOWLEDGE = "knowledge"
    PROJECT = "project"
    PROCEDURE = "procedure"
    DECISION = "decision"
    EXPERIENCE = "experience"
    ERROR = "error"
    LESSON = "lesson"
    PREFERENCE = "preference"
    RESOURCE = "resource"
    HYPOTHESIS = "hypothesis"

class ProvenanceModel(BaseModel):
    source_type: Literal["user", "official", "execution", "experience", "ai", "inference", "import", "unknown"]
    source_ref: str
    source_date: Optional[str] = None
    original_path: Optional[str] = None
    extraction_date: Optional[str] = None
    redaction: Optional[Literal["none", "applied", "not_applicable"]] = "none"
    provenance_status: Optional[Literal["complete", "incomplete"]] = "complete"

class RelationModel(BaseModel):
    relation: str  # e.g., "related_to", "supports", "contradicts", "derived_from", "replaces"
    target: str    # e.g., "[[Target Note]]" or note type
    target_id: Optional[str] = None

class NoteFrontmatter(BaseModel):
    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    type: NoteTypeEnum = NoteTypeEnum.KNOWLEDGE
    lifecycle: LifecycleEnum = LifecycleEnum.REVIEW
    category: str = "general"
    tags: List[str] = Field(default_factory=list)
    created: str
    updated: str
    provenance: ProvenanceModel
    confidence: Literal["very_high", "high", "medium", "low", "unknown"] = "medium"
    verification: Literal["verified", "partially_verified", "unverified", "inferred"] = "unverified"
    valid_from: Optional[str] = None
    valid_until: Optional[str] = None
    version_range: Optional[str] = None
    applies_to: Optional[str] = None
    supersedes: Optional[str] = None
    superseded_by: Optional[str] = None
    conflicts_with: Optional[str] = None
    relations: List[RelationModel] = Field(default_factory=list)

    @field_validator("id")
    @classmethod
    def validate_uuid(cls, v: str) -> str:
        uuid.UUID(str(v))
        return str(v)
```

```python
# models/intent.py
from pydantic import BaseModel, Field
from typing import List, Dict, Any, Optional
from enum import Enum

class IntentType(str, Enum):
    QUERY = "query"               # Knowledge / memory retrieval
    IOT_CONTROL = "iot_control"   # Home Assistant smart device command
    TASK = "task"                 # Multi-step complex problem solving
    MEMORY_STORE = "memory_store" # Explicit instruction to remember something
    SYSTEM_STATUS = "system_status" # Status / health / diagnostic check
    CONVERSATION = "conversation" # General chit-chat / conversational response

class UserIntent(BaseModel):
    raw_text: str
    intent_type: IntentType
    entities: List[Dict[str, Any]] = Field(default_factory=list)
    requires_tool: bool = False
    confidence: float = 1.0
    extracted_query: Optional[str] = None
```

```python
# models/plan.py
from pydantic import BaseModel, Field
from typing import List, Dict, Any, Optional
from enum import Enum

class StepStatus(str, Enum):
    PENDING = "pending"
    RUNNING = "running"
    SUCCESS = "success"
    BLOCKED = "blocked"
    FAILED = "failed"

class PlanStep(BaseModel):
    step_id: int
    action: str               # e.g., "search", "read", "iot_call", "propose"
    kwargs: Dict[str, Any] = Field(default_factory=dict)
    description: str
    status: StepStatus = StepStatus.PENDING
    result: Optional[Any] = None
    error: Optional[str] = None

class ActivePlan(BaseModel):
    goal: str
    steps: List[PlanStep]
    current_step_index: int = 0

    def get_next_step(self) -> Optional[PlanStep]:
        if self.current_step_index < len(self.steps):
            return self.steps[self.current_step_index]
        return None

    def complete_current_step(self, result: Any) -> None:
        if self.current_step_index < len(self.steps):
            self.steps[self.current_step_index].status = StepStatus.SUCCESS
            self.steps[self.current_step_index].result = result
            self.current_step_index += 1

    def is_complete(self) -> bool:
        return self.current_step_index >= len(self.steps)
```

---

### 4.3 Interface Contracts & Class Signatures

#### 4.3.1 Modular LLM Provider Interface
```python
# core/llm_provider.py
from abc import ABC, abstractmethod
from typing import AsyncIterator, Type, TypeVar, Optional, Dict, Any
from pydantic import BaseModel
import httpx
import json

T = TypeVar("T", bound=BaseModel)

class BaseLLMProvider(ABC):
    @abstractmethod
    async def generate(self, prompt: str, system_prompt: Optional[str] = None, **kwargs) -> str:
        """Generate plain text completion."""
        pass

    @abstractmethod
    async def stream(self, prompt: str, system_prompt: Optional[str] = None, **kwargs) -> AsyncIterator[str]:
        """Stream completion tokens asynchronously for sub-300ms TTFB audio synthesis."""
        pass

    @abstractmethod
    async def generate_structured(self, prompt: str, schema: Type[T], system_prompt: Optional[str] = None, **kwargs) -> T:
        """Generate JSON structured output validated against Pydantic schema."""
        pass

class OllamaProvider(BaseLLMProvider):
    def __init__(self, host: str = "http://127.0.0.1:11434", model: str = "qwen2.5-coder", timeout: float = 30.0):
        self.host = host.rstrip("/")
        self.model = model
        self.timeout = timeout

    async def generate(self, prompt: str, system_prompt: Optional[str] = None, **kwargs) -> str:
        url = f"{self.host}/api/generate"
        payload = {"model": self.model, "prompt": prompt, "system": system_prompt or "", "stream": False}
        async with httpx.AsyncClient(timeout=self.timeout) as client:
            resp = await client.post(url, json=payload)
            resp.raise_for_status()
            return resp.json().get("response", "")

    async def stream(self, prompt: str, system_prompt: Optional[str] = None, **kwargs) -> AsyncIterator[str]:
        url = f"{self.host}/api/generate"
        payload = {"model": self.model, "prompt": prompt, "system": system_prompt or "", "stream": True}
        async with httpx.AsyncClient(timeout=self.timeout) as client:
            async with client.stream("POST", url, json=payload) as response:
                async for line in response.aiter_lines():
                    if line:
                        chunk = json.loads(line)
                        yield chunk.get("response", "")

    async def generate_structured(self, prompt: str, schema: Type[T], system_prompt: Optional[str] = None, **kwargs) -> T:
        schema_json = json.dumps(schema.model_json_schema())
        full_prompt = f"{prompt}\n\nSTRICT REQUIREMENT: Respond ONLY with valid JSON conforming to this schema:\n{schema_json}"
        raw_text = await self.generate(full_prompt, system_prompt=system_prompt, **kwargs)
        start = raw_text.find("{")
        end = raw_text.rfind("}") + 1
        if start == -1 or end <= start:
            raise ValueError(f"No JSON object found in response: {raw_text}")
        parsed = json.loads(raw_text[start:end])
        return schema.model_validate(parsed)
```

#### 4.3.2 Stateful OODA Cognitive Loop Controller
```python
# core/ooda_engine.py
class OODACognitiveEngine:
    def __init__(
        self,
        llm_provider: BaseLLMProvider,
        storage_engine: SQLiteStorageEngine,
        markdown_sync: MarkdownSyncEngine,
        checkpoint_dir: str = ".checkpoints"
    ):
        self.llm = llm_provider
        self.storage = storage_engine
        self.sync = markdown_sync
        self.checkpoint_dir = checkpoint_dir
        self.working_memory = WorkingMemory(capacity=10)
        self.recall_engine = RecallEngine(self.storage)
        self.planner = Planner(self.llm)
        self.executor = Executor(self.storage)
        self.reflexion = ReflexionEngine(self.storage)
        self.consolidator = Consolidator(self.storage)
        self.active_plan: Optional[ActivePlan] = None

    async def execute_turn(self, input_text: str, principal: Principal = Principal.AI_AGENT) -> Dict[str, Any]:
        """Executes one full OODA cycle asynchronously."""
        # 1. OBSERVE
        intent = await self._observe(input_text)
        
        # 2. RETRIEVE
        activated_nodes = self.recall_engine.retrieve(intent.extracted_query or input_text)
        self.working_memory.admit(activated_nodes)
        context = self.working_memory.get_context()
        
        # 3. REASON & PLAN
        self.active_plan = await self.planner.create_plan(intent, context)
        self._checkpoint()

        # 4. ACT & REFLECT LOOP
        results = []
        while self.active_plan and not self.active_plan.is_complete():
            step = self.active_plan.get_next_step()
            try:
                step_result = await self.executor.execute_step(step, principal)
                self.active_plan.complete_current_step(step_result)
                self._checkpoint()
                results.append(step_result)
            except Exception as e:
                # 5. REFLECT on Failure
                reflection_id = self.reflexion.reflect_error(step, str(e), principal)
                new_plan = await self.planner.replan(self.active_plan, str(e), context)
                self.active_plan = new_plan
                self._checkpoint()
                break

        # 6. CONSOLIDATE
        consolidated_id = self.consolidator.run_post_task_maintenance(principal)

        return {
            "intent": intent.model_dump(),
            "plan_completed": self.active_plan.is_complete() if self.active_plan else True,
            "results": results,
            "consolidated_id": consolidated_id
        }
```

#### 4.3.3 SQLite WAL Storage & Atomic Markdown Sync
```python
# storage/sqlite_wal.py
import sqlite3
import threading
import json
import os
from typing import Optional, Dict, Any, List

class SQLiteStorageEngine:
    SCHEMA = """
    CREATE TABLE IF NOT EXISTS notes (
        id TEXT PRIMARY KEY,
        type TEXT NOT NULL CHECK(type IN ('knowledge', 'project', 'procedure', 'decision', 'experience', 'error', 'lesson', 'preference', 'resource', 'hypothesis', 'system', 'core', 'index')),
        lifecycle TEXT NOT NULL CHECK(lifecycle IN ('RAW', 'CLASSIFIED', 'NORMALIZED', 'REVIEW', 'VERIFIED', 'ACTIVE', 'RECONSOLIDATING', 'SUPERSEDED', 'ARCHIVED')),
        category TEXT NOT NULL,
        tags TEXT NOT NULL,
        created TEXT NOT NULL,
        updated TEXT NOT NULL,
        source_type TEXT NOT NULL,
        source_ref TEXT NOT NULL,
        confidence TEXT NOT NULL,
        verification TEXT NOT NULL,
        valid_from TEXT,
        valid_until TEXT,
        version_range TEXT,
        applies_to TEXT,
        supersedes TEXT,
        superseded_by TEXT,
        conflicts_with TEXT,
        relations TEXT NOT NULL,
        provenance TEXT NOT NULL,
        content TEXT NOT NULL,
        raw_json TEXT NOT NULL
    );
    CREATE INDEX IF NOT EXISTS idx_notes_lifecycle ON notes(lifecycle);
    CREATE INDEX IF NOT EXISTS idx_notes_type ON notes(type);
    """

    def __init__(self, db_path: str = "vault_memory.sqlite3", timeout: float = 5.0):
        self.db_path = db_path
        self.timeout = timeout
        self._local = threading.local()
        os.makedirs(os.path.dirname(os.path.abspath(db_path)) if os.path.dirname(db_path) else ".", exist_ok=True)
        conn = self._get_conn()
        with conn:
            conn.executescript(self.SCHEMA)

    def _get_conn(self) -> sqlite3.Connection:
        if not hasattr(self._local, "conn") or self._local.conn is None:
            conn = sqlite3.connect(self.db_path, timeout=self.timeout, isolation_level=None)
            conn.row_factory = sqlite3.Row
            conn.execute("PRAGMA journal_mode=WAL;")
            conn.execute("PRAGMA busy_timeout=5000;")
            conn.execute("PRAGMA synchronous=NORMAL;")
            conn.execute("PRAGMA foreign_keys=ON;")
            conn.execute("PRAGMA mmap_size=268435456;")
            self._local.conn = conn
        return self._local.conn

    def set_note_atomic(self, note: Dict[str, Any]) -> None:
        """Atomic write using BEGIN IMMEDIATE transaction."""
        conn = self._get_conn()
        insert_sql = """
        INSERT INTO notes (
            id, type, lifecycle, category, tags, created, updated,
            source_type, source_ref, confidence, verification,
            valid_from, valid_until, version_range, applies_to,
            supersedes, superseded_by, conflicts_with,
            relations, provenance, content, raw_json
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(id) DO UPDATE SET
            type=excluded.type, lifecycle=excluded.lifecycle, category=excluded.category,
            tags=excluded.tags, updated=excluded.updated, source_type=excluded.source_type,
            source_ref=excluded.source_ref, confidence=excluded.confidence,
            verification=excluded.verification, supersedes=excluded.supersedes,
            superseded_by=excluded.superseded_by, relations=excluded.relations,
            content=excluded.content, raw_json=excluded.raw_json;
        """
        params = (
            note["id"], note.get("type", "knowledge"), note.get("lifecycle", "REVIEW"),
            note.get("category", "general"), json.dumps(note.get("tags", [])),
            note.get("created", ""), note.get("updated", ""),
            note.get("provenance", {}).get("source_type", "unknown"),
            note.get("provenance", {}).get("source_ref", "unknown"),
            note.get("confidence", "medium"), note.get("verification", "unverified"),
            note.get("valid_from"), note.get("valid_until"), note.get("version_range"),
            note.get("applies_to"), note.get("supersedes"), note.get("superseded_by"),
            note.get("conflicts_with"), json.dumps(note.get("relations", [])),
            json.dumps(note.get("provenance", {})), note.get("content", ""), json.dumps(note)
        )
        try:
            conn.execute("BEGIN IMMEDIATE;")
            conn.execute(insert_sql, params)
            conn.execute("COMMIT;")
        except Exception as e:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise e
```

---

## 5. Verification Method

To independently verify the architecture and specifications upon implementation:

### 5.1 Automated Test Execution Commands
```powershell
# 1. Run core cognitive loop & executive tests
python -m pytest -v cognitive_core/tests/test_cognitive_loop.py cognitive_core/tests/test_executive.py

# 2. Run SQLite WAL storage concurrency & constraint tests
python -m pytest -v memory_controller/tests/test_sqlite_storage.py

# 3. Run formal reflexion, ToT & reconsolidation tests
python -m pytest -v cognitive_core/tests/test_tot_and_formal_reflexion.py cognitive_core/tests/test_reconsolidation.py

# 4. Verify Trust Boundary Invariants (P0-P15)
python -m pytest -v memory_controller/tests/test_security_hardening.py cognitive_core/tests/test_tool_router_security.py
```

### 5.2 Specific File Inspection & Invariant Checks
1. **Schema Check**: Frontmatter of all generated notes must pass Draft-07 validation via `validate_frontmatter()` with valid UUID `id`, `created`/`updated` ISO dates, and complete `provenance`.
2. **Atomic Write Check**: Markdown files written to disk must use temporary file creation (`mkstemp`) followed by `os.replace` to prevent zero-byte corruptions.
3. **WAL Mode Check**: Running `PRAGMA journal_mode;` on `vault_memory.sqlite3` must return `wal`.
4. **Adversarial Gate Check**: Any attempt by `Principal.AI_AGENT` to directly propose `lifecycle="ACTIVE"` or `verification="verified"` must raise `PermissionError` or `ValidationError`.

### 5.3 Invalidation Criteria
The specification is invalidated if:
- Concurrent reads block write operations (violating WAL concurrency).
- An AI Agent successfully bypasses human attestation to verify a note.
- An LLM streaming failure crashes the cognitive loop instead of triggering Reflexion and replanning.
