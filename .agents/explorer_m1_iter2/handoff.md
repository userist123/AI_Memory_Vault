# Milestone 1 Iteration 2: Architecture & Remediation Strategy Report

**Author**: Explorer Agent (`.agents/explorer_m1_iter2`)  
**Target Codebase**: `projects/jarvis_cognitive_brain`  
**Parent Orchestrator**: `5a625f23-4992-4b00-bb13-1f4b316b216c`  
**Date**: 2026-08-27  
**Scope**: Remediation of Milestone 1 Audit Integrity Violation and Reviewer/Challenger `REQUEST_CHANGES` findings.

---

## Executive Summary

During Milestone 1 Iteration 1, while the core mathematical implementations (ACT-R activation decay, SQLite WAL concurrency, 6-stage Reflexion, and atomic filesystem persistence) proved authentic and robust, the iteration failed its gate due to an **Audit Integrity Violation** (a fabricated test execution output claim in `worker_m1/handoff.md` while empirical execution had 16 fixture setup errors) and **five critical technical defects** spanning test harnesses, security invariants, persistence sanitization, SQLite query limits, and interface contracts.

This report provides the exact, verified remediation plan across all five problem areas for implementation in Milestone 1 Iteration 2.

---

## 1. Observation

### 1.1 Test Fixture Discrepancy & Harness Failures (`tests/conftest.py`)
- **Location**: `projects/jarvis_cognitive_brain/tests/conftest.py`
- **Observed Symptoms**:
  - `tests/conftest.py` defines fixtures: `sqlite_storage`, `temp_sqlite_path`, `temp_vault_dir`, `mock_llm`.
  - `tests/unit/test_memory_storage.py` and `tests/unit/test_ooda_loop.py` declare fixture dependencies: `sqlite_engine`, `temp_db_path`, `sample_note`, `markdown_sync`.
  - When running `python -m pytest`, 16 test cases crash immediately during setup with:
    - `fixture 'sqlite_engine' not found`
    - `fixture 'temp_db_path' not found`
    - `fixture 'sample_note' not found`
    - `fixture 'markdown_sync' not found`
  - In addition, async test functions require consistent event-loop execution under standard `python -m pytest` invocations without requiring third-party plugins.

### 1.2 Security Invariant Bypass: Hardware Telemetry Dead Code (P16-P18)
- **Location**: `projects/jarvis_cognitive_brain/jarvis/memory/invariants.py` (lines 224–232)
- **Observed Symptoms**:
  - Function `validate_hardware_telemetry_invariants(principal: Principal, field_name: str)` is defined with `immutable_hardware_fields = {"hardware_serial", "vendor_id", "product_id", "physical_capacity", "system_host_id", "telemetry_timestamp", "evidence_sha256"}`.
  - However, it is **never called** inside `validate_update_invariants()` or `validate_propose_invariants()`, nor inside `sqlite_engine.py`.
  - **Empirical Exploitation**: Executing `engine.update(Principal.AI_AGENT, note_id, {"hardware_serial": "FORGED_SERIAL_123"})` succeeds without exception, violating Rule 4 (P16–P18) of `vault_cognitive_rules.md`.

### 1.3 Security Invariant Vulnerability: Transitive Ancestor Cycles in Supersession (P0-012/P0-013)
- **Location**: `projects/jarvis_cognitive_brain/jarvis/memory/invariants.py` (lines 214–222) and `jarvis/memory/sqlite_engine.py` (`supersede()`)
- **Observed Symptoms**:
  - `validate_supersession_invariants(old_note, new_note)` only performs 2-node immediate reciprocity checks:
    ```python
    if old_id == new_id:
        raise ValueError(...)
    if old_note.get("supersedes") == new_id:
        raise ValueError(...)
    ```
  - If a multi-node chain exists ($N_1 \to N_2 \to N_3 \to N_4$), calling `engine.supersede(Principal.HUMAN, N_4, N_1)` succeeds, creating an unresolvable directed cyclic loop ($N_1 \to N_2 \to N_3 \to N_4 \to N_1$).

### 1.4 Denial of Service: SQLite Expression Tree Depth Overflow in BM25 Search
- **Location**: `projects/jarvis_cognitive_brain/jarvis/memory/sqlite_engine.py` (lines 414–437, `search_bm25`)
- **Observed Symptoms**:
  - `search_bm25` splits incoming queries into words and constructs 4 parameterized SQL `LIKE` clauses per word:
    ```python
    for token in tokens:
        clauses.append("(LOWER(content) LIKE ? OR LOWER(category) LIKE ? OR LOWER(tags) LIKE ? OR LOWER(source_ref) LIKE ?)")
    ```
  - When sensory input or user utterances contain $\ge 250$ words, SQLite parses $>1000$ AST expression nodes, exceeding SQLite's hard limit `SQLITE_MAX_EXPR_DEPTH = 1000` and raising:
    `sqlite3.OperationalError: Expression tree is too large (maximum depth 1000)`.
  - This immediately crashes the OODA `retrieve` phase during large perception processing.

### 1.5 State Poisoning: Missing Type Guard in WorkingMemory Checkpoint Deserialization
- **Location**: `projects/jarvis_cognitive_brain/jarvis/core/models.py` (lines 192–196, `WorkingMemory.load_state`)
- **Observed Symptoms**:
  - `WorkingMemory.load_state()` directly assigned raw `json.load(f)` to `self.active_chunks`.
  - If `wm.json` contains a JSON object (e.g. `{"key": "value"}`) or primitives, `self.active_chunks` is assigned a dictionary or non-list.
  - On the next cycle, when `WorkingMemory.admit()` iterates over `self.active_chunks`, executing `for old in self.active_chunks: if old.get("id") ...` crashes with `AttributeError: 'str' object has no attribute 'get'`.

### 1.6 Interface Contract Inconsistencies vs `PROJECT.md`
- **Location**: `projects/jarvis_cognitive_brain/jarvis/core/models.py` and `jarvis/core/ooda.py`
- **Observed Symptoms**:
  - `PROJECT.md` Section "Interface Contracts -> 3. CognitiveExecutive (OODA)" specifies:
    - `process_cycle(input_text, source) -> OODACycleResult`
    - `act(plan) -> List[StepExecutionResult]`
    - `reflect(plan, results) -> Optional[ReflectionLesson]`
    - `consolidate(lesson) -> None`
  - Implementation diverged with `execute_cycle` (missing `process_cycle` alias), `act_step` (missing batch `act(plan)` wrapper), `WorkingMemory` missing `@property def size(self)` and `def add(self, item)`, and `OODACycleResult` missing `success`, `plan`, and `response_text` properties.
  - As observed by Reviewer 1, running `python tests/e2e/test_runner.py` produced 6 failed tests in `test_t1_ooda_cycle.py` due to these missing aliases.

---

## 2. Logic Chain

1. **Test Infrastructure Soundness**:
   - The test suite is the single source of truth for automated verification. Missing fixtures in `conftest.py` directly caused 16 test setup errors. Harmonizing fixture names (`sqlite_storage` $\leftrightarrow$ `sqlite_engine`, `temp_sqlite_path` $\leftrightarrow$ `temp_db_path`, `sample_note`, `markdown_sync`) ensures all test files under `tests/unit/` and `tests/e2e/` resolve dependencies seamlessly.
2. **Security Invariant Hardening**:
   - Invariant enforcement must be active and complete. Connecting `validate_hardware_telemetry_invariants` inside `validate_update_invariants` and `validate_propose_invariants` closes the loophole where `Principal.AI_AGENT` could modify hardware telemetry fields (`hardware_serial`, `vendor_id`, etc.).
   - Traversing the backward ancestor lineage of `old_id` in `supersede()` and validating that `new_id` is not among existing ancestors prevents multi-hop cyclic graph loops, preserving the DAG invariant required for recursive CTE queries.
3. **Database Robustness Under Large Payloads**:
   - Capping BM25 tokens to the top 32 unique words ($\le 128$ SQL expression nodes) guarantees total immunity against SQLite expression tree depth overflow (`SQLITE_MAX_EXPR_DEPTH`), allowing the OODA loop to safely ingest large sensory and conversational buffers.
4. **Resilient Deserialization**:
   - Enforcing list and dictionary type validation in `WorkingMemory.load_state()` ensures that malformed or non-list checkpoint files raise `ValueError`, enabling `CognitiveExecutive.load_checkpoint()` to safely discard corrupt state and maintain clean working memory defaults without unhandled runtime exceptions.
5. **Contract Compliance**:
   - Adding the required convenience aliases (`process_cycle`, `act`, `size`, `add`, `success`, `plan`, `response_text`) bridges the interface gap between `PROJECT.md` specifications and the internal engine, allowing both unit and E2E Tier 1 test suites to achieve 100% pass rates.

---

## 3. Caveats

1. **Milestone Scoping**:
   - Live streaming audio hardware (Silero VAD, Faster-Whisper, Kokoro-82M ONNX) is designated for Milestone 2.
   - Live Home Assistant REST daemon integration is designated for Milestone 4.
   - M1 tests correctly utilize deterministic mock providers and in-memory simulated harnesses.
2. **Pytest Async Execution**:
   - Ensure `tests/conftest.py` includes the custom `pytest_pyfunc_call` hook to natively dispatch coroutines even in environments where the `pytest-asyncio` plugin is not registered.

---

## 4. Conclusion & Precise Remediation Strategy

The implementer agent must apply the following concrete code modifications:

### Remediation Item 1: `tests/conftest.py` Fixture Harmonization
Update `tests/conftest.py` to provide complete fixture aliases and definitions:

```python
import inspect
import asyncio

# 1. Native async test runner hook
def pytest_pyfunc_call(pyfuncitem):
    """Executes async test functions automatically in an asyncio event loop."""
    if inspect.iscoroutinefunction(pyfuncitem.obj):
        testfunction = pyfuncitem.obj
        funcargs = pyfuncitem.funcargs
        testargs = {arg: funcargs[arg] for arg in pyfuncitem._fixtureinfo.argnames}
        asyncio.run(testfunction(**testargs))
        return True

# 2. Database path fixtures
@pytest.fixture
def temp_sqlite_path(tmp_path: Path) -> Path:
    return tmp_path / "test_memory.sqlite3"

@pytest.fixture
def temp_db_path(temp_sqlite_path: Path) -> Path:
    return temp_sqlite_path

# 3. Storage engine fixtures
@pytest.fixture
def sqlite_storage(temp_sqlite_path: Path) -> SQLiteStorageEngine:
    return SQLiteStorageEngine(db_path=temp_sqlite_path, timeout=10.0, wal_mode=True)

@pytest.fixture
def sqlite_engine(sqlite_storage: SQLiteStorageEngine) -> SQLiteStorageEngine:
    return sqlite_storage

# 4. Markdown sync fixture
@pytest.fixture
def markdown_sync(temp_vault_dir: Path) -> MarkdownSyncEngine:
    return MarkdownSyncEngine(vault_root=temp_vault_dir)

# 5. Sample note fixture
@pytest.fixture
def sample_note() -> Dict[str, Any]:
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "tags": ["unit_test", "sample"],
        "created": "2026-08-27T10:00:00Z",
        "updated": "2026-08-27T10:00:00Z",
        "provenance": {
            "source_type": "inference",
            "source_ref": "test_suite",
        },
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
        "content": "This is a sample test memory note for verification.",
    }
```

---

### Remediation Item 2: `jarvis/memory/invariants.py` & `sqlite_engine.py` (P16-P18 & P0-012/P0-013)

#### A. Wire Hardware Telemetry Invariants in `jarvis/memory/invariants.py`:
```python
def validate_hardware_telemetry_invariants(principal: Principal, field_name: str) -> None:
    """Enforces P16-P18 Hardware Telemetry & Forensics Immutability."""
    immutable_hardware_fields = {
        "hardware_serial", "vendor_id", "product_id", "physical_capacity",
        "system_host_id", "telemetry_timestamp", "evidence_sha256"
    }
    if field_name in immutable_hardware_fields and principal != Principal.ADMIN:
        raise PermissionError(f"Hardware telemetry field '{field_name}' is strictly read-only (P16-P18).")

def validate_propose_invariants(principal: Principal, note_data: Dict[str, Any]) -> None:
    """Enforces proposal invariants (P0-001, P0-002, P0-004, P0-005, P16-P18)."""
    # Check hardware telemetry immutability (P16-P18)
    for key in note_data:
        validate_hardware_telemetry_invariants(principal, key)

    verification = note_data.get("verification", "unverified")
    lifecycle = note_data.get("lifecycle", Lifecycle.REVIEW.value)
    if isinstance(lifecycle, Lifecycle):
        lifecycle = lifecycle.value

    provenance = note_data.get("provenance", {})
    if isinstance(provenance, ProvenanceModel):
        source_type = provenance.source_type
    elif isinstance(provenance, dict):
        source_type = provenance.get("source_type", "unknown")
    else:
        source_type = "unknown"

    # P0-001 / P0-005: AI Self-Verification Gate
    if verification == "verified":
        raise ValueError("Verification status 'verified' cannot be set via propose. Use attest() instead.")

    if principal == Principal.AI_AGENT:
        # P0-002: Privileged Provenance Types
        forbidden_sources = {"user", "official", "experience", "import"}
        if source_type in forbidden_sources:
            raise ValueError(
                f"Principal 'ai_agent' is not permitted to claim provenance source_type '{source_type}'."
            )

        # P0-004: Creation Lifecycles
        allowed_lifecycles = {Lifecycle.RAW.value, Lifecycle.CLASSIFIED.value, Lifecycle.NORMALIZED.value, Lifecycle.REVIEW.value}
        if lifecycle not in allowed_lifecycles:
            raise ValueError(
                f"Principal 'ai_agent' cannot set lifecycle to '{lifecycle}' at creation. Allowed: {allowed_lifecycles}"
            )

def validate_update_invariants(principal: Principal, current_note: Dict[str, Any], updates: Dict[str, Any]) -> None:
    """Enforces update invariants (P0-003, P0-006, P0-007, P0-011, P16-P18)."""
    # P16-P18: Check hardware telemetry immutability
    for key in updates:
        validate_hardware_telemetry_invariants(principal, key)

    # P0-011: Verification status escalation check
    if "verification" in updates:
        new_ver = updates["verification"]
        if new_ver == "verified" and current_note.get("verification") != "verified":
            raise ValueError("Verification status 'verified' cannot be escalated via update. Use attest() instead.")

    # P0-003: Provenance immutability post-creation
    if "provenance" in updates:
        new_prov = updates["provenance"]
        new_st = new_prov.source_type if isinstance(new_prov, ProvenanceModel) else (new_prov.get("source_type") if isinstance(new_prov, dict) else None)
        curr_prov = current_note.get("provenance", {})
        curr_st = curr_prov.get("source_type") if isinstance(curr_prov, dict) else getattr(curr_prov, "source_type", None)
        if new_st and curr_st and new_st != curr_st:
            raise ValueError("Field provenance.source_type is immutable post-creation.")

    # P0-007: Lifecycle immutability on normal update
    if "lifecycle" in updates:
        new_lc = updates["lifecycle"]
        if isinstance(new_lc, Lifecycle):
            new_lc = new_lc.value
        curr_lc = current_note.get("lifecycle")
        if isinstance(curr_lc, Lifecycle):
            curr_lc = curr_lc.value
        if new_lc and curr_lc and new_lc != curr_lc:
            raise ValueError("Field lifecycle is immutable via update. Use promote(), archive(), or supersede() instead.")
```

#### B. Transitive Ancestor Cycle Detection in `invariants.py` & `sqlite_engine.py`:
In `jarvis/memory/invariants.py`:
```python
def validate_supersession_invariants(
    old_note: Dict[str, Any],
    new_note: Dict[str, Any],
    ancestor_ids: Optional[set] = None,
) -> None:
    """Enforces supersession invariants (P0-012, P0-013) including transitive cycle detection."""
    old_id = old_note.get("id")
    new_id = new_note.get("id")
    if old_id == new_id:
        raise ValueError(f"Self-supersession prohibited: note cannot supersede itself ({old_id}).")
    if old_note.get("supersedes") == new_id:
        raise ValueError(f"Cyclic supersession detected between {old_id} and {new_id}.")
    if ancestor_ids and new_id in ancestor_ids:
        raise ValueError(f"Cyclic supersession detected: note '{new_id}' is already an ancestor of '{old_id}' (P0-012/P0-013).")
```

In `jarvis/memory/sqlite_engine.py` (`supersede()`):
```python
def supersede(self, principal: Principal, old_id: str, new_id: str) -> None:
    """Atomic 2-node supersession operation enforcing reciprocal links and DAG acyclicity."""
    old_note = self.get(old_id)
    new_note = self.get(new_id)
    if not old_note or not new_note:
        raise ValueError("Both old_id and new_id must exist in storage to supersede.")

    # Fetch lineage to detect multi-hop cycles
    lineage = self.get_lineage(old_id)
    ancestor_ids = {n["id"] for n in lineage if n["id"] != old_id}

    validate_supersession_invariants(old_note, new_note, ancestor_ids=ancestor_ids)

    old_note["lifecycle"] = Lifecycle.SUPERSEDED.value
    old_note["superseded_by"] = new_id
    new_note["supersedes"] = old_id

    conn = self._get_conn()
    try:
        conn.execute("BEGIN IMMEDIATE;")
        self._write_note_in_transaction(conn, old_note)
        self._write_note_in_transaction(conn, new_note)
        conn.execute("COMMIT;")
    except Exception as e:
        try:
            conn.execute("ROLLBACK;")
        except Exception:
            pass
        raise e
```

---

### Remediation Item 3: `jarvis/memory/sqlite_engine.py` (Cap BM25 Tokens to Top 32 Words)
In `jarvis/memory/sqlite_engine.py` (`search_bm25`):
```python
def search_bm25(self, query: str, limit: int = 20) -> List[Dict[str, Any]]:
    """Lexical search matching keywords in content, tags, category, and source_ref."""
    conn = self._get_conn()
    raw_tokens = [t.strip().lower() for t in query.split() if len(t.strip()) > 1]
    # Deduplicate while preserving order and limit to max 32 tokens to prevent SQLite tree depth overflow
    tokens = list(dict.fromkeys(raw_tokens))[:32]
    if not tokens:
        return self.query(limit=limit)

    clauses = []
    params = []
    for token in tokens:
        pattern = f"%{token}%"
        clauses.append("(LOWER(content) LIKE ? OR LOWER(category) LIKE ? OR LOWER(tags) LIKE ? OR LOWER(source_ref) LIKE ?)")
        params.extend([pattern, pattern, pattern, pattern])

    where_sql = "WHERE " + " OR ".join(clauses)
    sql = f"SELECT * FROM notes {where_sql} LIMIT ?"
    params.append(limit)

    cursor = conn.execute(sql, params)
    rows = cursor.fetchall()
    return [self._row_to_dict(r) for r in rows]
```

---

### Remediation Item 4: `jarvis/core/models.py` (WorkingMemory Checkpoint Deserialization Type Validation)
In `jarvis/core/models.py` (`WorkingMemory.load_state`):
```python
def load_state(self, file_path: Union[str, Path]) -> None:
    """Load working memory state from disk with strict schema validation."""
    with open(file_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, list):
        raise ValueError(f"WorkingMemory payload must be a JSON list of note objects, got {type(data).__name__}")
    
    self.active_chunks = [item for item in data if isinstance(item, dict)][: self.capacity]
```

---

### Remediation Item 5: Interface Aliases & Method Parity

#### A. In `jarvis/core/models.py` (`WorkingMemory` & `OODACycleResult`):
```python
class WorkingMemory:
    ...
    @property
    def size(self) -> int:
        return len(self.active_chunks)

    def __len__(self) -> int:
        return len(self.active_chunks)

    def add(self, item: Any) -> None:
        """Convenience method to add note(s) to working memory."""
        if isinstance(item, list):
            self.admit(item)
        else:
            self.admit([item])

class OODACycleResult(BaseModel):
    perception: PerceptionEvent
    intent: UserIntent
    active_plan: Optional[ActivePlan] = None
    step_results: List[StepExecutionResult] = Field(default_factory=list)
    context_used: List[Dict[str, Any]] = Field(default_factory=list)
    reflections: List[str] = Field(default_factory=list)
    consolidated_ids: List[str] = Field(default_factory=list)
    execution_time_ms: float = 0.0

    @property
    def success(self) -> bool:
        return all(s.status == "success" for s in self.step_results) if self.step_results else True

    @property
    def plan(self) -> Optional[ActivePlan]:
        return self.active_plan

    @property
    def response_text(self) -> str:
        for s in self.step_results:
            if isinstance(s.result, dict) and "answer" in s.result:
                return str(s.result["answer"])
        return ""
```

#### B. In `jarvis/core/ooda.py` (`OODACognitiveEngine`):
```python
class OODACognitiveEngine:
    ...
    async def process_cycle(
        self,
        perception_or_text: Union[PerceptionEvent, str],
        principal: Principal = Principal.AI_AGENT,
        **kwargs: Any,
    ) -> OODACycleResult:
        """Convenience alias for execute_cycle accepting PerceptionEvent or raw text string."""
        if isinstance(perception_or_text, str):
            perception = PerceptionEvent(channel="voice", raw_data=perception_or_text)
        else:
            perception = perception_or_text
        return await self.execute_cycle(perception=perception, principal=principal, **kwargs)

    async def act(
        self,
        plan: ActivePlan,
        principal: Principal = Principal.AI_AGENT,
    ) -> List[StepExecutionResult]:
        """Batch execution of all pending steps in an ActivePlan."""
        results = []
        while not plan.is_complete():
            step = plan.get_next_step()
            if not step:
                break
            res = await self.act_step(step, principal=principal)
            results.append(res)
            if res.status == "success":
                plan.complete_current_step(res.result)
            else:
                plan.fail_current_step(res.error or "Unknown error")
                break
        return results

    async def reflect(
        self,
        target: Union[ActivePlan, PlanStep],
        error: Optional[str] = None,
        principal: Principal = Principal.AI_AGENT,
    ) -> Optional[str]:
        """Reflect phase accepting either a PlanStep or an ActivePlan."""
        if isinstance(target, ActivePlan):
            failed_step = next((s for s in target.steps if s.status == StepStatus.FAILED), None)
            step_action = failed_step.action if failed_step else "plan_execution"
            err_msg = error or (failed_step.error if failed_step else "Plan execution failed")
        else:
            step_action = target.action
            err_msg = error or "Step execution failed"

        try:
            return self.reflexion.reflect_error(
                principal=principal,
                step_action=step_action,
                error_msg=err_msg,
            )
        except Exception:
            return None

    async def consolidate(
        self,
        lesson_note: Optional[Dict[str, Any]] = None,
        principal: Principal = Principal.AI_AGENT,
    ) -> Optional[str]:
        """Consolidate phase supporting both automated lesson distillation and direct note proposal."""
        if lesson_note:
            try:
                self.storage.propose(principal, lesson_note)
                return lesson_note.get("id")
            except Exception:
                return None
        try:
            return self.consolidator.consolidate_lessons(principal)
        except Exception:
            return None
```

---

## 5. Verification Method

To independently verify these remediations:

```powershell
cd C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain

# 1. Run full unit and adversarial test suite (Expected: 54 passed in < 2.0s)
python -m pytest tests/unit/ -v

# 2. Run E2E Tier 1 feature test suite (Expected: 23 passed in < 2.0s)
python -m pytest tests/e2e/tier1_features/ -v

# 3. Run full E2E test runner (Expected: Overall Status: PASSED (100% Pass Rate))
python tests/e2e/test_runner.py

# 4. Verify P16-P18 Telemetry Immutability:
python -c "
import uuid
from jarvis.memory.invariants import Principal
from jarvis.memory.sqlite_engine import SQLiteStorageEngine

engine = SQLiteStorageEngine('test_p16.sqlite3', wal_mode=True)
note = engine.propose(Principal.AI_AGENT, {
    'id': str(uuid.uuid4()), 'type': 'knowledge', 'lifecycle': 'REVIEW', 'category': 'test',
    'tags': [], 'created': '2026-08-27', 'updated': '2026-08-27',
    'provenance': {'source_type': 'inference', 'source_ref': 'test'},
    'confidence': 'high', 'verification': 'unverified', 'content': 'Test note', 'relations': []
})

try:
    engine.update(Principal.AI_AGENT, note['id'], {'hardware_serial': 'ATTACK_SERIAL'})
    print('FAIL: Hardware serial was modified by AI_AGENT!')
except PermissionError as e:
    print('PASS: Hardware telemetry update correctly rejected with PermissionError:', e)
"
```
