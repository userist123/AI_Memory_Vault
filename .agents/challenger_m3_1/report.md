# Milestone 3 Empirical Challenger Stress & Adversarial Report

**Author**: Teamwork Challenger (`challenger_m3_1`)  
**Target Milestone**: Milestone 3 (Multi-Agent Subsystem & Worker Pool Orchestration)  
**Project Path**: `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`  
**Date**: 2026-08-28T17:06:10+03:00  
**Final Verdict**: **REQUEST_CHANGES**

---

## 1. Executive Summary

As an empirical challenger, we designed and executed a battery of stress harnesses, concurrency floods, race condition detectors, and failure scenario generators against the `MultiAgentSupervisor`, `ScopedStorageProxy`, and the specialized agent worker pool (`RouterAgent`, `RetrievalAgent`, `VerifierAgent`, `ConsolidatorAgent`, `CriticAgent`).

While the basic baseline functionality and RBAC invariant boundaries (P0–P18) performed strongly under standard conditions (280 baseline tests passing), **deep stress testing revealed 3 concrete, reproducible concurrency and lifecycle bugs** in `jarvis/agents/supervisor.py`:
1. **CRITICAL Bug 1 (Duplicate Task Execution on Retries)**: Re-queuing logic creates duplicate task instances across workers during retries under concurrency.
2. **CRITICAL Bug 2 (Worker Pool Thread Death on `CancelledError`)**: Unhandled `asyncio.CancelledError` in `_dispatch()` propagates to `_worker_loop()`, permanently killing worker tasks and leaving awaiting callers deadlocked on orphaned futures.
3. **HIGH Bug 3 (Phantom Cancellation of Pending Tasks)**: `cancel_tasks_matching()` fails to invalidate pending tasks in `_async_queue` if submitted without explicit `CancellationToken`, resulting in unwanted full task execution by workers.

---

## 2. Test Execution & Empirical Stress Results

### 2.1 Baseline Suite Execution
```
pytest
============================= 280 passed in 7.62s =============================
```

### 2.2 Challenger Stress & Invariant Suites

| Test Suite / Scenario | Target Subsystem | Result | Key Finding |
|---|---|---|---|
| `test_deep_concurrency_saturation_and_semaphore_invariance` | `MultiAgentSupervisor` | **PASS** | 100 concurrent tasks properly capped at `max_workers=4` |
| `test_priority_preemption_under_heavy_queue_backlog` | `PriorityQueue` & Heap | **PASS** | 5 Urgent P1 tasks successfully preempted 40 P5 tasks |
| `test_cascading_worker_exceptions_and_recovery` | Error Isolation | **PASS** | Worker exceptions (KeyError, RuntimeError, MemoryError) isolated |
| `test_concurrent_proxy_rbac_invariant_bombardment` | `ScopedStorageProxy` | **PASS** | 50 concurrent attacks against P0-P18 rejected with RBAC errors |
| `test_retry_duplicate_execution_race` | Retry Mechanism | **FAIL** | **Bug 1**: 3 executions instead of 2 on 1 retry attempt |
| `test_worker_survives_asyncio_cancelled_error` | Cancellation & Worker Pool | **FAIL** | **Bug 2**: Worker coroutine dies; future deadlocks |
| `test_cancelled_pending_task_must_not_execute` | `cancel_tasks_matching` | **FAIL** | **Bug 3**: Cancelled pending tasks still executed by worker loop |

---

## 3. Detailed Bug Findings & Reproduction Chains

### 🔴 Finding 1 (CRITICAL): Duplicate Task Execution in Concurrency Pool on Retries
- **Location**: `jarvis/agents/supervisor.py`, Lines 306–308
- **Code**:
  ```python
  # Re-queue task
  heapq.heappush(self.queue, task)
  self._async_queue.put_nowait((task.priority, self._seq + 1, task))
  return await self._dispatch(task)
  ```
- **Vulnerability Analysis**:
  When a task encounters a transient failure and `task.retry_count < task.max_retries`, `_dispatch()` puts a duplicate entry onto `self._async_queue` AND synchronously invokes `await self._dispatch(task)` within the current worker.
  In any pool with `max_workers > 1`, a second idle worker immediately dequeues the duplicate entry from `self._async_queue` and executes the retry simultaneously with Worker 1.
- **Empirical Proof (`tests/unit/test_challenger_m3_bug_retry.py`)**:
  ```
  FAILED tests/unit/test_challenger_m3_bug_retry.py::test_retry_duplicate_execution_race
  AssertionError: Expected 2 executions (1 fail + 1 retry), but observed 3 due to duplicate queue put!
  assert 3 == 2
  ```
- **Remediation**:
  Choose either synchronous retry inside the current worker (without re-putting onto `self._async_queue`), OR re-queue to `self._async_queue` and return without recursing into `_dispatch()`.

---

### 🔴 Finding 2 (CRITICAL): Worker Pool Termination & Deadlocked Futures on `asyncio.CancelledError`
- **Location**: `jarvis/agents/supervisor.py`, Lines 230–320 and 191–193
- **Code**:
  ```python
  # In _dispatch():
  except asyncio.TimeoutError:
      ...
  except Exception as exc:
      ...
  # (asyncio.CancelledError is NOT caught because it inherits from BaseException)

  # In _worker_loop():
  except asyncio.CancelledError:
      break # <--- Kills the background worker!
  ```
- **Vulnerability Analysis**:
  When a running agent workload raises `asyncio.CancelledError` (e.g. cancelled sub-task or mid-flight barge-in), `_dispatch()` does not catch it. The exception propagates out of `_dispatch()` to `_worker_loop()`, where the `except asyncio.CancelledError: break` handler terminates the worker loop permanently.
  Furthermore, `self._task_futures[task.task_id]` is never resolved or cancelled, leaving external callers waiting on `await fut` permanently deadlocked.
- **Empirical Proof (`tests/unit/test_challenger_m3_bug_cancellation.py`)**:
  ```
  FAILED tests/unit/test_challenger_m3_bug_cancellation.py::test_worker_survives_asyncio_cancelled_error
  asyncio.exceptions.CancelledError / TimeoutError: Future never resolved and worker crashed
  ```
- **Remediation**:
  Add an explicit `except asyncio.CancelledError:` block inside `_dispatch()`:
  ```python
  except asyncio.CancelledError:
      exec_ms = (time.time() - t0) * 1000.0
      task_res = TaskResult(
          task_id=task.task_id,
          role=task.role,
          action=task.action,
          status=TaskStatus.CANCELLED,
          error="Task cancelled during execution.",
          execution_time_ms=exec_ms,
      )
      self._record_completion(task, task_res)
      return task_res
  ```

---

### 🟡 Finding 3 (HIGH): Phantom Cancellation of Pending Tasks in Asynchronous Priority Queue
- **Location**: `jarvis/agents/supervisor.py`, Lines 352–376
- **Code**:
  ```python
  def cancel_tasks_matching(self, predicate: Callable[[AgentTask], bool], reason: str = "cancelled") -> int:
      ...
      for task in list(self.queue):
          if predicate(task):
              fut = self._task_futures.get(task.task_id)
              if fut and not fut.done():
                  fut.cancel()
              try:
                  self.queue.remove(task)
                  heapq.heapify(self.queue)
                  cancelled_count += 1
              except ValueError:
                  pass
  ```
- **Vulnerability Analysis**:
  `cancel_tasks_matching()` cancels the pending future and removes the task from `self.queue`, but cannot remove items from `asyncio.PriorityQueue` (`self._async_queue`).
  If a task was submitted without an explicit `CancellationToken` object, `_dispatch()` does not know the task was cancelled and executes the full agent workload when popped by a worker.
- **Empirical Proof (`tests/unit/test_challenger_m3_bug_pending_cancel.py`)**:
  ```
  FAILED tests/unit/test_challenger_m3_bug_pending_cancel.py::test_cancelled_pending_task_must_not_execute
  AssertionError: Expected only ['blocker'], but observed ['blocker', 'pending-0', 'pending-1', 'pending-2']
  ```
- **Remediation**:
  Track cancelled task IDs in a set `self._cancelled_task_ids = set()`, ensure all `AgentTask` instances instantiate an internal `CancellationToken` by default, or check `if task.task_id in self._cancelled_task_ids` at the entry of `_dispatch()` to skip execution.

---

## 4. Verdict and Next Steps

**Verdict: REQUEST_CHANGES**

The Worker (`worker_m3_1`) must apply the fixes for Findings 1, 2, and 3 in `jarvis/agents/supervisor.py` so that all 3 newly added challenger tests (`test_challenger_m3_bug_retry.py`, `test_challenger_m3_bug_cancellation.py`, `test_challenger_m3_bug_pending_cancel.py`, and `test_challenger_m3_adversarial_deep.py`) pass with 100% success alongside the existing 280 tests.
