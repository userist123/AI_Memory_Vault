# Milestone 3 Challenger: Handoff Report

## 1. Observation

### 1.1 Direct File and Code Observations
1. In `jarvis/agents/supervisor.py` lines 306–308:
   ```python
   # Re-queue task
   heapq.heappush(self.queue, task)
   self._async_queue.put_nowait((task.priority, self._seq + 1, task))
   return await self._dispatch(task)
   ```
2. In `jarvis/agents/supervisor.py` lines 230–320:
   `_dispatch()` wraps role execution in `asyncio.timeout(timeout)` and catches `except asyncio.TimeoutError` and `except Exception as exc`. It does **not** catch `asyncio.CancelledError`.
3. In `jarvis/agents/supervisor.py` lines 191–193:
   `_worker_loop()` catches `except asyncio.CancelledError: break`.
4. In `jarvis/agents/supervisor.py` lines 352–376:
   `cancel_tasks_matching()` cancels futures and removes items from `self.queue`, but cannot remove elements already queued in `self._async_queue`. If `task.cancellation_token` is `None`, `_dispatch()` executes the task upon dequeueing.

### 1.2 Verbatim Test Failures Observed During Execution
1. **Retry Duplicate Execution Test** (`tests/unit/test_challenger_m3_bug_retry.py`):
   ```
   FAILED tests/unit/test_challenger_m3_bug_retry.py::test_retry_duplicate_execution_race
   AssertionError: Expected 2 executions (1 fail + 1 retry), but observed 3 due to duplicate queue put!
   assert 3 == 2
   ```
2. **Worker Cancellation Deadlock Test** (`tests/unit/test_challenger_m3_bug_cancellation.py`):
   ```
   FAILED tests/unit/test_challenger_m3_bug_cancellation.py::test_worker_survives_asyncio_cancelled_error
   TimeoutError: Future was never resolved because worker loop terminated on unhandled CancelledError.
   ```
3. **Pending Task Cancellation Test** (`tests/unit/test_challenger_m3_bug_pending_cancel.py`):
   ```
   FAILED tests/unit/test_challenger_m3_bug_pending_cancel.py::test_cancelled_pending_task_must_not_execute
   AssertionError: Expected only ['blocker'], but observed ['blocker', 'pending-0', 'pending-1', 'pending-2'] (pending tasks executed despite cancellation!)
   ```

---

## 2. Logic Chain

1. **Duplicate Execution via Redundant Dispatch (Finding 1)**:
   - *Observation 1.1.1*: `_dispatch()` puts `task` back into `self._async_queue` AND recurses into `_dispatch(task)`.
   - *Logic*: In a concurrent worker pool, Worker 1 immediately runs the retry attempt via recursion, while Worker 2 pulls the re-queued task from `self._async_queue` and executes the retry a second time. This causes duplicate side-effects and breaks task count invariants (Observation 1.2.1).

2. **Worker Pool Death on Task Cancellation (Finding 2)**:
   - *Observation 1.1.2 & 1.1.3*: In Python 3.8+, `asyncio.CancelledError` inherits from `BaseException` rather than `Exception`.
   - *Logic*: When a worker coroutine raises `CancelledError` during cancellation/barge-in, `_dispatch()` fails to catch it. The exception escapes into `_worker_loop()` where `except asyncio.CancelledError: break` catches it and immediately terminates the worker task permanently. The future in `self._task_futures[task.task_id]` is never resolved, causing awaiting callers to deadlock (Observation 1.2.2).

3. **Phantom Cancellation for Pending Tasks (Finding 3)**:
   - *Observation 1.1.4*: `cancel_tasks_matching()` modifies `self.queue` but cannot modify `asyncio.PriorityQueue` (`self._async_queue`).
   - *Logic*: Tasks submitted without explicit `CancellationToken` remain in `_async_queue`. When a worker pulls them, `_dispatch()` sees no active cancellation token and executes the full workload, wasting system resources (Observation 1.2.3).

---

## 3. Caveats

- **Scope boundary**: This challenger review evaluated the concurrency, scheduling, retry, and cancellation properties of the supervisor and worker agents in Milestone 3.
- **RBAC Boundaries**: `ScopedStorageProxy` and invariant rules P0–P18 were verified under 50-task adversarial bombardment (`test_concurrent_proxy_rbac_invariant_bombardment`) and performed with 100% compliance.
- No caveats regarding reproducibility of the 3 identified bugs.

---

## 4. Conclusion

**Verdict: REQUEST_CHANGES**

Milestone 3 cannot be approved in its current state due to the 2 CRITICAL and 1 HIGH concurrency flaws in `jarvis/agents/supervisor.py`. The worker (`worker_m3_1`) must remediate:
1. Prevent duplicate retry dispatches (either recurse without `_async_queue.put_nowait` or re-queue and return).
2. Catch `asyncio.CancelledError` inside `_dispatch()`, mark `TaskStatus.CANCELLED`, and resolve the awaiting future cleanly without crashing `_worker_loop()`.
3. Track cancelled task IDs in `cancel_tasks_matching()` (e.g. via `self._cancelled_task_ids` or default `CancellationToken`) so `_dispatch()` skips execution of cancelled pending tasks.

---

## 5. Verification Method

To independently verify the bugs and subsequent fixes:

1. Change directory to project root:
   ```powershell
   cd C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain
   ```

2. Run the reproducer test suite:
   ```powershell
   python -m pytest tests/unit/test_challenger_m3_bug_retry.py tests/unit/test_challenger_m3_bug_cancellation.py tests/unit/test_challenger_m3_bug_pending_cancel.py -v
   ```

3. Run the full adversarial deep suite:
   ```powershell
   python -m pytest tests/unit/test_challenger_m3_adversarial_deep.py -v
   ```

4. Run the full regression test suite:
   ```powershell
   python -m pytest
   ```
