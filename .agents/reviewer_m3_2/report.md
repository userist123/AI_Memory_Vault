# Milestone 3 Multi-Agent Subsystem: Quality & Adversarial Security Review Report

## 1. Review Summary

- **Verdict**: **REQUEST_CHANGES**
- **Reviewer Identity**: `reviewer_m3_2` (Teamwork Preview Reviewer / Critic)
- **Target Project**: `projects/jarvis_cognitive_brain`
- **Scope**: Milestone 3 Multi-Agent Subsystem (`jarvis/agents/`, `ScopedStorageProxy`, `VerifierAgent`, `MultiAgentSupervisor`, `tests/unit/test_agent_least_privilege.py`, and full test suite).

---

## 2. Executive Assessment

The architectural implementation of Role-Based Access Control (RBAC) via `ScopedStorageProxy` and invariant enforcement (P0–P18) in `VerifierAgent` and `jarvis/memory/invariants.py` is sound, robust, and correctly blocks all tested privilege escalation vectors:
- `AI_AGENT` cannot self-verify (P0-001 / P0-005).
- `AI_AGENT` cannot promote directly into `ACTIVE` lifecycle (P0-004).
- `AI_AGENT` cannot claim privileged source types (`user`, `official`, `experience`, `import`) (P0-002).
- Cyclic supersession chains (self-referencing and transitive graph cycles) are strictly detected and rejected (P0-012 / P0-013).
- Hardware telemetry fields remain immutable across all agent workers (P16–P18).

However, deep adversarial stress-testing revealed **three critical/major concurrency and lifecycle defects** in `jarvis/agents/supervisor.py` (`MultiAgentSupervisor`) that cause worker pool permanent thread death, unresolved future deadlocks during barge-in cancellations, duplicate task executions during retries, and leaked execution of cancelled pending tasks.

---

## 3. Findings & Required Changes

### [Critical] Finding 1: Worker Coroutine Termination and Unresolved Future Hang on `asyncio.CancelledError`
- **Location**: `jarvis/agents/supervisor.py`, lines 230–324 (`_dispatch` and `_worker_loop`)
- **Vulnerability / Problem**: In Python 3.8+, `asyncio.CancelledError` inherits from `BaseException` rather than `Exception`. In `_dispatch()`, error handling is structured as `except Exception as exc:`. When an agent workload raises `asyncio.CancelledError` (or a sub-task is cancelled via `CancellationToken`), `_dispatch` fails to catch it. The exception escapes to `_worker_loop()`, where `except asyncio.CancelledError: break` catches it and immediately terminates the worker coroutine permanently. Because `_record_completion()` is bypassed, the task's Future (`self._task_futures[task.task_id]`) is never resolved, causing any awaiting caller to hang indefinitely and leaking memory. Under barge-in bursts, all worker coroutines in the pool terminate, deadlocking the supervisor with 0 active workers.
- **Evidence**: `test_challenger_m3_bug_cancellation.py::test_worker_survives_asyncio_cancelled_error` FAILED (TimeoutError / CancelledError). `test_challenger_m3_adversarial_deep.py::test_mid_execution_cancellation_with_in_flight_tasks` hangs indefinitely.
- **Suggested Fix**:
  In `jarvis/agents/supervisor.py` inside `_dispatch()`:
  ```python
  except asyncio.CancelledError:
      exec_ms = (time.time() - t0) * 1000.0
      task_res = TaskResult(
          task_id=task.task_id,
          role=task.role,
          action=task.action,
          status=TaskStatus.CANCELLED,
          error="Task execution was cancelled.",
          execution_time_ms=exec_ms,
      )
      self._record_completion(task, task_res)
      return task_res
  ```

---

### [Major] Finding 2: Race Condition and Duplicate Task Execution in Retry Mechanism
- **Location**: `jarvis/agents/supervisor.py`, lines 297–308
- **Vulnerability / Problem**: When a task encounters a retryable exception (`task.retry_count < task.max_retries`), `_dispatch()` attempts to re-queue the task via `self._async_queue.put_nowait(...)` AND immediately executes `return await self._dispatch(task)` on the current worker. This causes dual execution: the current worker re-runs the task immediately, while another idle worker in the pool pulls the same task from `_async_queue` and executes it simultaneously.
- **Evidence**: `test_challenger_m3_bug_retry.py::test_retry_duplicate_execution_race` FAILED with `assert 3 == 2` (3 total executions for 1 retry).
- **Suggested Fix**:
  In `_dispatch()`, perform local retry dispatch without placing a duplicate into `_async_queue`:
  ```python
  if task.retry_count < task.max_retries:
      task.retry_count += 1
      logger.warning(f"Retrying task {task.task_id} (Attempt {task.retry_count}/{task.max_retries}): {exc}")
      self._emit_telemetry("task_retry", {
          "task_id": task.task_id,
          "attempt": task.retry_count,
          "error": str(exc),
      })
      return await self._dispatch(task)
  ```

---

### [Major] Finding 3: Leaked Execution of Cancelled Pending Tasks
- **Location**: `jarvis/agents/supervisor.py`, lines 352–376 (`cancel_tasks_matching`)
- **Vulnerability / Problem**: When `cancel_tasks_matching()` is called, it removes matching tasks from `self.queue` (the synchronous heap), but `self._async_queue` (`asyncio.PriorityQueue`) cannot have items removed directly. When worker coroutines call `await self._async_queue.get()`, they dequeue the task. Because `_dispatch()` does not verify whether the task's future was cancelled before executing the worker payload, the cancelled task runs to completion anyway.
- **Evidence**: `test_challenger_m3_bug_pending_cancel.py::test_cancelled_pending_task_must_not_execute` FAILED with `assert ['blocker', 'pending-0', 'pending-1', 'pending-2'] == ['blocker']`.
- **Suggested Fix**:
  In `_dispatch()`, check if the task or its future has already been cancelled prior to invoking the worker:
  ```python
  fut = self._task_futures.get(task.task_id)
  if fut and fut.cancelled():
      exec_ms = (time.time() - t0) * 1000.0
      task_res = TaskResult(
          task_id=task.task_id,
          role=task.role,
          action=task.action,
          status=TaskStatus.CANCELLED,
          error="Task was cancelled in queue.",
          execution_time_ms=exec_ms,
      )
      self._record_completion(task, task_res)
      return task_res
  ```

---

## 4. Verified Invariants & Claims

| Invariant / Requirement | Description | Status | Evidence / Verification Method |
|---|---|---|---|
| **P0-001 / P0-005** | AI Agent cannot self-verify | **PASS** | `test_invariant_p0_verifier_cannot_attest_or_promote`, `test_verifier_flags_ai_agent_self_verification_attempt` |
| **P0-002** | Privileged source types forbidden for AI Agent | **PASS** | `test_invariant_p0_consolidator_cannot_claim_privileged_provenance` |
| **P0-004** | Direct proposal to `ACTIVE` lifecycle blocked | **PASS** | `test_invariant_p0_consolidator_cannot_propose_active_lifecycle`, `test_verifier_flags_unauthorized_active_lifecycle_at_creation` |
| **P0-012 / P0-013** | Self & cyclic supersession rejected | **PASS** | `test_verifier_flags_cyclic_and_self_supersession`, `test_retrieval_agent_supersession_lineage_exclusion` |
| **P16–P18** | Hardware telemetry fields strictly immutable | **PASS** | `test_invariant_p16_p18_hardware_telemetry_immutability_across_all_workers` |
| **RBAC Scoping** | `ScopedStorageProxy` gates role operations | **PASS** | `test_invariant_p0_router_cannot_mutate_memory`, `test_invariant_p0_retrieval_cannot_mutate_memory`, `test_invariant_p0_critic_cannot_archive_or_attest` |
| **Consolidator** | Lesson distillation into REVIEW + archival | **PASS** | `test_consolidator_synthesizes_multiple_review_lessons`, `test_consolidator_archives_consumed_review_lessons` |
| **Critic** | 6-stage Reflexion & API key leak audit | **PASS** | `test_critic_detects_secret_or_credential_leak`, `test_critic_flags_hallucinated_facts_or_contradictions` |

---

## 5. Adversarial Stress-Test Results Summary

- `tests/unit/test_agent_least_privilege.py`: 7/7 PASSED
- `tests/unit/test_multi_agent.py`: 31/31 PASSED
- `tests/unit/test_challenger_m3_stress.py`: 7/7 PASSED
- `tests/e2e/tier1_features/test_t1_multi_agent.py`: 5/5 PASSED
- `tests/unit/test_challenger_m3_bug_cancellation.py`: **1 FAILED** (`CancelledError` worker kill)
- `tests/unit/test_challenger_m3_bug_retry.py`: **1 FAILED** (Duplicate execution race)
- `tests/unit/test_challenger_m3_bug_pending_cancel.py`: **1 FAILED** (Pending task executed despite cancel)
- `tests/unit/test_challenger_m3_adversarial_deep.py`: **HANGS on test 3** due to unhandled `CancelledError`

---

## 6. Verdict & Next Steps

**Verdict**: **REQUEST_CHANGES**

**Action Required**:
The worker agent must resolve the 3 supervisor lifecycle defects in `jarvis/agents/supervisor.py`. Once fixed, running the full pytest suite (`python -m pytest`) including all challenger suites will yield 100% pass rate without hangs or race conditions.
