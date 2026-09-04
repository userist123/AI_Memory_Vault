"""
Exhaustive empirical challenge harness for MultiAgentSupervisor:
1. High-concurrency randomized retry chaos (exact invocation counting across 50 tasks, 8 workers).
2. Interleaved cancellation during active retry recursion.
3. Future cancellation (fut.cancel()) handling for pending tasks.
4. Retry limit boundary compliance (exact 1 + max_retries executions).
5. Rapid burst submit and shutdown drain invariant under load.
6. Worker pool cleanup, active_tasks dictionary cleanliness, and active_workers==0 guarantee.
"""

import pytest
import asyncio
import time
import random
from typing import Dict, Any, List

from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.base import CancellationToken
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.agents import (
    AgentRole,
    TaskPriority,
    TaskStatus,
    AgentTask,
    TaskResult,
    MultiAgentSupervisor,
)


@pytest.mark.asyncio
async def test_high_concurrency_randomized_retry_chaos(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits 50 tasks with varying max_retries (1 to 3) across 8 concurrent workers.
    Each task has a predefined failure schedule (e.g. fails 0, 1, 2, or 3 times before succeeding or exhausting retries).
    Verifies:
    1. Every task executes EXACTLY its required number of attempts (no duplicate queue dispatch).
    2. All futures resolve correctly without deadlocks or timeouts.
    3. Active workers return to 0 and active_tasks is empty.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=8)
    await supervisor.start()

    task_attempts: Dict[str, int] = {}
    task_failure_schedules: Dict[str, int] = {} # task_id -> number of times to fail

    num_tasks = 50
    for i in range(num_tasks):
        tid = f"chaos-task-{i}"
        # 0: always succeeds, 1: fails once, 2: fails twice, 3: fails 3 times
        task_failure_schedules[tid] = i % 4
        task_attempts[tid] = 0

    async def chaos_execute(payload, cancellation_token=None):
        tid = payload["task_id"]
        task_attempts[tid] += 1
        curr_attempt = task_attempts[tid] - 1 # 0-indexed failure check
        
        # Small variable delay to induce interleaving
        await asyncio.sleep(random.uniform(0.005, 0.015))

        if curr_attempt < task_failure_schedules[tid]:
            raise ConnectionResetError(f"Simulated failure on attempt {curr_attempt + 1}")
        return {"status": "success", "tid": tid, "attempts": task_attempts[tid]}

    supervisor.critic.execute = chaos_execute

    futures = []
    tasks = []
    for i in range(num_tasks):
        tid = f"chaos-task-{i}"
        max_retries = 2 if (i % 2 == 0) else 3
        t = AgentTask(
            task_id=tid,
            priority=(i % 5) + 1,
            role=AgentRole.CRITIC,
            payload={"task_id": tid},
            max_retries=max_retries,
        )
        tasks.append(t)
        futures.append(supervisor.submit_task(t))

    results = await asyncio.gather(*futures)
    assert len(results) == num_tasks

    for i, res in enumerate(results):
        tid = f"chaos-task-{i}"
        failures_needed = task_failure_schedules[tid]
        max_ret = tasks[i].max_retries

        if failures_needed <= max_ret:
            # Expected to succeed after (failures_needed + 1) attempts
            expected_attempts = failures_needed + 1
            assert res.status == TaskStatus.COMPLETED, f"Task {tid} should have succeeded but got {res.status}"
            assert task_attempts[tid] == expected_attempts, f"Task {tid} had {task_attempts[tid]} attempts, expected {expected_attempts}"
        else:
            # Expected to fail after (max_ret + 1) attempts
            expected_attempts = max_ret + 1
            assert res.status == TaskStatus.FAILED, f"Task {tid} should have failed but got {res.status}"
            assert task_attempts[tid] == expected_attempts, f"Task {tid} had {task_attempts[tid]} attempts, expected {expected_attempts}"

    await supervisor.shutdown(wait=True, timeout=3.0)

    assert supervisor.active_worker_count == 0
    assert len(supervisor._active_tasks) == 0


@pytest.mark.asyncio
async def test_cancellation_during_active_retry_recursion(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits a task with max_retries=3.
    On attempt 1: fails.
    On attempt 2: cancellation token is cancelled while executing attempt 2.
    Verifies:
    1. Attempt 2 terminates with TaskStatus.CANCELLED.
    2. No further retries (attempts 3 and 4) are executed.
    3. Future resolves cleanly with TaskStatus.CANCELLED.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)
    await supervisor.start()

    token = CancellationToken()
    attempts = 0

    async def retry_then_cancel_execute(payload, cancellation_token=None):
        nonlocal attempts
        attempts += 1
        if attempts == 1:
            raise ValueError("First attempt transient failure")
        elif attempts == 2:
            # Cancel token during attempt 2 execution
            token.cancel("Barge-in cancellation during retry")
            await asyncio.sleep(0.01)
            if cancellation_token and cancellation_token.is_cancelled:
                raise asyncio.CancelledError("Token cancelled in retry attempt 2")
            return {"status": "should_not_reach"}
        else:
            attempts += 10 # Sentinel to catch illegal extra retries
            return {"status": "illegal_attempt"}

    supervisor.critic.execute = retry_then_cancel_execute

    task = AgentTask(
        task_id="t-retry-cancel-test",
        priority=1,
        role=AgentRole.CRITIC,
        payload={"query": "test"},
        max_retries=3,
        cancellation_token=token,
    )

    fut = supervisor.submit_task(task)
    res = await asyncio.wait_for(fut, timeout=2.0)

    assert res.status == TaskStatus.CANCELLED
    assert attempts == 2, f"Expected exactly 2 attempts before cancellation stopped retries, but observed {attempts}"

    await supervisor.shutdown(wait=True, timeout=2.0)
    assert supervisor.active_worker_count == 0


@pytest.mark.asyncio
async def test_future_cancel_on_queued_task(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits a blocking task to occupy the single worker, then submits a second task and calls fut2.cancel().
    Verifies that when worker reaches task2, it observes future.cancelled() and does NOT execute task2 logic.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=1)
    await supervisor.start()

    executed = []

    async def mock_execute(payload, cancellation_token=None):
        name = payload["name"]
        executed.append(name)
        await asyncio.sleep(0.05)
        return {"done": name}

    supervisor.critic.execute = mock_execute

    t1 = AgentTask(task_id="t1-blocker", priority=1, role=AgentRole.CRITIC, payload={"name": "t1"})
    t2 = AgentTask(task_id="t2-to-cancel", priority=2, role=AgentRole.CRITIC, payload={"name": "t2"})

    fut1 = supervisor.submit_task(t1)
    fut2 = supervisor.submit_task(t2)

    # Cancel fut2 before t1 finishes
    fut2.cancel()

    await asyncio.gather(fut1, return_exceptions=True)
    await asyncio.sleep(0.1) # Let worker process queue

    await supervisor.shutdown(wait=True, timeout=2.0)

    assert executed == ["t1"], f"Expected only ['t1'], but observed {executed}"


@pytest.mark.asyncio
async def test_exact_retry_limit_boundary(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits a task with max_retries=5 that always fails.
    Verifies total executions is exactly 6 (1 initial + 5 retries).
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)
    await supervisor.start()

    attempts = 0

    async def always_fail_execute(payload, cancellation_token=None):
        nonlocal attempts
        attempts += 1
        raise RuntimeError(f"Fail attempt {attempts}")

    supervisor.critic.execute = always_fail_execute

    task = AgentTask(
        task_id="t-retry-exhaust-5",
        priority=2,
        role=AgentRole.CRITIC,
        payload={"test": True},
        max_retries=5,
    )

    fut = supervisor.submit_task(task)
    res = await asyncio.wait_for(fut, timeout=2.0)

    assert res.status == TaskStatus.FAILED
    assert attempts == 6, f"Expected 6 attempts (1 + 5 retries), got {attempts}"
    assert len(supervisor.failed_tasks) == 1

    await supervisor.shutdown(wait=True, timeout=2.0)


@pytest.mark.asyncio
async def test_rapid_burst_drain_and_shutdown(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits 40 tasks and immediately calls shutdown(wait=True).
    Verifies that all tasks drain cleanly and no worker coroutine leaks.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=4)
    await supervisor.start()

    async def quick_execute(payload, cancellation_token=None):
        await asyncio.sleep(0.005)
        return {"ok": True}

    supervisor.critic.execute = quick_execute

    futures = []
    for i in range(40):
        t = AgentTask(task_id=f"burst-{i}", priority=2, role=AgentRole.CRITIC, payload={"idx": i})
        futures.append(supervisor.submit_task(t))

    # Trigger shutdown with wait=True
    await supervisor.shutdown(wait=True, timeout=5.0)

    results = await asyncio.gather(*futures)
    assert len(results) == 40
    assert all(r.status == TaskStatus.COMPLETED for r in results)
    assert len(supervisor.completed_tasks) == 40
    assert supervisor.active_worker_count == 0
