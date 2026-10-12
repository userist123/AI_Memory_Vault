# Milestone 3 Independent Review & Adversarial Critic: Handoff Report

## 1. Observation

### 1.1 Direct Observations & Code Inspection
- `jarvis/agents/base.py`:
  - `ScopedStorageProxy` (Lines 28–211) enforces `ROLE_PERMISSIONS` capabilities: `ROUTER` (READ, SEARCH), `RETRIEVAL` (READ, SEARCH), `VERIFIER` (READ), `CONSOLIDATOR` (SEARCH, READ, PROPOSE, ARCHIVE), `CRITIC` (READ, PROPOSE). Unauthorized operations raise `PermissionError(f"... (RBAC Violation).")`.
  - Proposals call `validate_propose_invariants` (P0-001, P0-002, P0-004, P0-005, P16-P18).
  - Updates call `validate_update_invariants` (P0-003, P0-006, P0-007, P0-011, P16-P18).
  - Attest calls `validate_attest_invariants` (P0-005).
  - Promotes call `validate_promote_invariants` (P0-004, P0-008).
- `jarvis/agents/verifier.py`:
  - `VerifierAgent.verify_note()` (Lines 41–189) validates mandatory fields, RFC-4122 UUID syntax, `NoteType` and `Lifecycle` enums, blocks AI self-verification (`ERR_P0_001_AI_VERIFIED_GATE`), blocks proposal into `ACTIVE` (`ERR_P0_004_AI_CREATION_LIFECYCLE`), blocks privileged provenance (`ERR_P0_002_FORBIDDEN_PROVENANCE`), and detects cyclic supersession (`ERR_P0_012_CYCLIC_SUPERSESSION`).
- `jarvis/agents/supervisor.py`:
  - `_dispatch()` (Lines 230–324) handles errors via `except Exception as exc:`. Python's `asyncio.CancelledError` (which subclasses `BaseException`) is not caught in `_dispatch()`, escaping to `_worker_loop()` (Line 191 `except asyncio.CancelledError: break`), permanently killing the background worker coroutine without resolving `self._task_futures[task.task_id]`.
  - Retry logic in `_dispatch()` (Lines 297–308) re-enqueues into `self._async_queue` AND calls `return await self._dispatch(task)` on the current worker, causing duplicate concurrent execution.
  - `cancel_tasks_matching()` (Lines 352–376) cancels futures in `self.queue` but does not purge `self._async_queue`, and `_dispatch()` does not check `fut.cancelled()` before executing worker workload.

### 1.2 Verbatim Test Outputs
1. `pytest tests/unit/test_challenger_m3_bug_retry.py -v`:
```
FAILED tests/unit/test_challenger_m3_bug_retry.py::test_retry_duplicate_execution_race
AssertionError: Expected 2 executions (1 fail + 1 retry), but observed 3 due to duplicate queue put!
assert 3 == 2
```

2. `pytest tests/unit/test_challenger_m3_bug_cancellation.py -v`:
```
FAILED tests/unit/test_challenger_m3_bug_cancellation.py::test_worker_survives_asyncio_cancelled_error
TimeoutError: Timeout [expired] / asyncio.exceptions.CancelledError
```

3. `pytest tests/unit/test_challenger_m3_bug_pending_cancel.py -v`:
```
FAILED tests/unit/test_challenger_m3_bug_pending_cancel.py::test_cancelled_pending_task_must_not_execute
AssertionError: Expected only ['blocker'], but observed ['blocker', 'pending-0', 'pending-1', 'pending-2'] (pending tasks executed despite cancellation!)
```

4. `pytest tests/unit/test_challenger_m3_adversarial_deep.py -v`:
```
tests/unit/test_challenger_m3_adversarial_deep.py::test_deep_concurrency_saturation_and_semaphore_invariance PASSED
tests/unit/test_challenger_m3_adversarial_deep.py::test_priority_preemption_under_heavy_queue_backlog PASSED
tests/unit/test_challenger_m3_adversarial_deep.py::test_mid_execution_cancellation_with_in_flight_tasks [HANGS INDEFINITELY]
```

5. Standard test suite (`tests/unit/test_agent_least_privilege.py tests/unit/test_multi_agent.py tests/unit/test_challenger_m3_stress.py tests/e2e/tier1_features/test_t1_multi_agent.py`):
```
50 passed in 1.56s
```

---

## 2. Logic Chain

1. **Security & Invariants P0–P18**:
   - `ScopedStorageProxy` and `VerifierAgent` strictly enforce trust boundaries and RBAC permissions.
   - Self-verification (P0-001), privileged provenance (P0-002), unauthorized `ACTIVE` proposals (P0-004), cyclic supersession (P0-012/P0-013), and hardware telemetry immutability (P16–P18) all pass 100% of unit and invariant tests.

2. **Supervisor Concurrency & Cancellation Vulnerabilities**:
   - From Observation 1.1 and 1.2: `_dispatch()` in `MultiAgentSupervisor` fails to catch `asyncio.CancelledError`. When cancellation is triggered (e.g. during barge-in speech interruption), the exception escapes to `_worker_loop()`, breaking out of the loop and killing the worker. The associated task future is never resolved, resulting in hanging futures and deadlocked workers.
   - Retries in `_dispatch()` place the task back into `_async_queue` AND execute it locally, creating duplicate concurrent tasks.
   - Pending task cancellation does not prevent dequeued tasks from running in `_dispatch()`.

3. **Conclusion Rationale**:
   - Although the core domain logic and invariant checks are well implemented, the supervisor coordinator fails under stress, cancellation, and retry scenarios. Therefore, the verdict must be `REQUEST_CHANGES`.

---

## 3. Caveats

- All tests use local in-memory SQLite and mock LLMs; no cloud latency or live network was evaluated.
- No other subsystems (M1 cognitive loop, M2 audio pipeline) were modified during this review.

---

## 4. Conclusion

**Verdict**: **REQUEST_CHANGES**

The multi-agent RBAC security boundaries, invariant checks, and specialized agent worker logic are verified and compliant. However, 3 concurrency and lifecycle defects in `MultiAgentSupervisor` (`jarvis/agents/supervisor.py`) must be resolved before Milestone 3 can be fully approved.

---

## 5. Verification Method

To reproduce and verify:

```powershell
cd C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain

# 1. Run least-privilege & invariant tests (Passes)
python -m pytest tests/unit/test_agent_least_privilege.py -v

# 2. Run supervisor bug reproductions (Fails / Reveals findings)
python -m pytest tests/unit/test_challenger_m3_bug_retry.py -v
python -m pytest tests/unit/test_challenger_m3_bug_cancellation.py -v
python -m pytest tests/unit/test_challenger_m3_bug_pending_cancel.py -v
```
