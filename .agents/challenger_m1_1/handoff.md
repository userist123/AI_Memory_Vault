# Milestone 1 Adversarial Review Report: OODA Stress & Correctness

**Agent**: Challenger 1 (Adversarial Correctness & OODA Stress Specialist)  
**Assigned Directory**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\challenger_m1_1`  
**Target Codebase**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`  
**Date**: 2026-08-27  
**Verdict**: `REQUEST_CHANGES` (2 Fixes Required)

---

## 1. Observation

Direct empirical observations from executing adversarial tests across the OODA cognitive loop, LLM streaming, cancellation tokens, reflection engine, and checkpoint restoration:

### A. Test Execution Results
- **Test Suite**: `tests/unit/test_adversarial_m1.py` (15 test scenarios) and `tests/unit/test_adversarial_storage_concurrency.py` (13 test scenarios) + existing unit tests (26 test scenarios) = **54 total tests passing**.
- **Execution Command**: `python -m pytest tests/unit -v`
- **Output**:
  ```text
  platform win32 -- Python 3.14.2, pytest-9.0.2, pluggy-1.6.0
  collected 54 items
  ============================= 54 passed in 1.76s ==============================
  ```

### B. Confirmed Empirical Vulnerabilities

1. **Vulnerability 1 — Denial of Service via Expression Tree Depth Overflow in `search_bm25` (HIGH)**
   - **Location**: `jarvis/memory/sqlite_engine.py`, lines 416-433
   - **Verbatim Error**: `sqlite3.OperationalError: Expression tree is too large (maximum depth 1000)`
   - **Observed Behavior**:
     ```python
     tokens = [t.strip().lower() for t in query.split() if t.strip()]
     # When query contains >= 250 words or large sensory payload:
     clauses.append("(LOWER(content) LIKE ? OR LOWER(category) LIKE ? OR LOWER(tags) LIKE ? OR LOWER(source_ref) LIKE ?)")
     # Generates >1000 OR branches, causing SQLite to reject the query with an OperationalError and crash the OODA retrieve() phase.
     ```
   - **Test Case**: `test_ooda_massive_payload_perception_exposes_sqlite_depth_limit`

2. **Vulnerability 2 — WorkingMemory Poisoning on Non-List JSON Checkpoints (MEDIUM)**
   - **Location**: `jarvis/core/models.py`, lines 185-189 (`WorkingMemory.load_state`)
   - **Verbatim Error**: `AttributeError: 'str' object has no attribute 'get'` during `WorkingMemory.admit()`
   - **Observed Behavior**:
     ```python
     def load_state(self, file_path: Union[str, Path]) -> None:
         with open(file_path, "r", encoding="utf-8") as f:
             self.active_chunks = json.load(f)
     # If wm.json contains a JSON object {"key": "value"} or primitives, self.active_chunks is assigned a non-list or list of strings.
     # When WorkingMemory.admit() executes `for old in self.active_chunks: if old.get("id") ...`, it crashes with AttributeError.
     ```
   - **Test Case**: `test_checkpoint_recovery_corrupted_wm_schema_handling`

### C. Confirmed Robust Components
- **Rapid Cancellation**:
  - `CancellationToken` triggered prior to streaming immediately aborts generator with `CancellationError` (0 tokens emitted).
  - Mid-stream cancellation interrupts iteration within a 1-token window.
  - Multi-threaded concurrent cancellation calls and throwing callbacks are handled idempotently without uncaught exceptions.
- **Perception Handling**:
  - Empty, whitespace-only, null-byte (`\x00`), and Unicode BOM inputs are classified safely into fallback conversational/query intents without crashing.
  - Adversarial prompt injection payloads attempting to forge `source_type="official"` or `verification="verified"` are strictly blocked by P0-P18 invariant validation rules.
- **Error Recovery & 6-Stage Reflexion**:
  - Step execution failures halt subsequent plan steps and propose structured 6-stage formal reflection notes (`Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson`) into `04_MEMORY/Errors/` under the `REVIEW` lifecycle.
  - Cascading failures trigger automated consolidation distillation via `ConsolidationEngine`.
- **Checkpoint Resilience**:
  - Unparseable JSON syntax, truncated files, and 0-byte checkpoint files are safely detected by `CognitiveExecutive.load_checkpoint()`, returning `False` and maintaining clean working memory defaults without unhandled exceptions.
  - `save_state()` uses atomic tempfile replacement (`.tmp_...` + `os.fsync` + `os.replace`), eliminating partial-write file corruption risks.

---

## 2. Logic Chain

1. **Adversarial Input Boundary**:
   - The OODA loop's `Observe` and `Retrieve` phases receive unfiltered sensory and user query strings.
   - When a long prompt or sensory event is processed, `MultiSignalRecallEngine` delegates token querying to `SQLiteStorageEngine.search_bm25()`.
   - Because `search_bm25()` constructs 4 parameterized SQL conditions per token without deduplication or capping, a query with 250+ tokens produces >1000 SQL expression nodes. SQLite enforces a hard compile-time recursion limit (`SQLITE_MAX_EXPR_DEPTH = 1000`), immediately raising `OperationalError` and crashing the daemon.
2. **Persistence Deserialization Boundary**:
   - Checkpoint files can be corrupted during unexpected system termination or manual tampering.
   - While `ActivePlan.load_state()` leverages Pydantic schema validation (`cls.model_validate(data)`), `WorkingMemory.load_state()` directly assigned raw `json.load()` output to `self.active_chunks`.
   - Loading a non-list JSON payload silently poisons `WorkingMemory`, causing catastrophic failure on the next OODA cycle during `WorkingMemory.admit()`.
3. **Verdict Deduction**:
   - Because both vulnerabilities can be triggered by external sensory input or untrusted file states, they must be patched by Worker 1 before advancing to Milestone 2 (where live streaming audio will feed continuous transcriptions into the OODA loop).

---

## 3. Caveats

1. **Audio Driver Invariants**:
   - Audio barge-in cancellation was validated at the LLM stream and cancellation token level using deterministic mock streams; live microphone and Silero VAD timing benchmarks will be audited in Milestone 2.
2. **Home Assistant Network Socketry**:
   - Tool execution failure tests used mock dispatchers raising synthetic network errors; live FastMCP IoT client interaction will be validated in Milestone 4.

---

## 4. Conclusion & Required Changes

**Verdict**: `REQUEST_CHANGES`

### Required Fix 1: Cap and Deduplicate Tokens in `SQLiteStorageEngine.search_bm25`
**File**: `jarvis/memory/sqlite_engine.py`  
**Patch**:
```python
def search_bm25(self, query: str, limit: int = 20) -> List[Dict[str, Any]]:
    conn = self._get_conn()
    raw_tokens = [t.strip().lower() for t in query.split() if len(t.strip()) > 1]
    # Deduplicate while preserving order and limit to top 20 unique tokens
    tokens = list(dict.fromkeys(raw_tokens))[:20]
    if not tokens:
        return self.query(limit=limit)
    ...
```

### Required Fix 2: Add List and Type Guards in `WorkingMemory.load_state`
**File**: `jarvis/core/models.py`  
**Patch**:
```python
def load_state(self, file_path: Union[str, Path]) -> None:
    with open(file_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, list):
        self.active_chunks = [item for item in data if isinstance(item, dict)][: self.capacity]
    else:
        self.active_chunks = []
```

### Required Fix 3: Align `tests/e2e/tier1_features/test_t1_memory_storage.py`
Update legacy method invocations (`propose_note` -> `propose`, `update_note` -> `update`, `vault_path` -> `vault_root`) to match canonical API definitions.

---

## 5. Verification Method

To independently verify these adversarial findings and run the full suite:

```powershell
cd C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain

# Run complete unit and adversarial test suite
python -m pytest tests/unit -v
```

Expected result: 54 passed in < 2.0s.
