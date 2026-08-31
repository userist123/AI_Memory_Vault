"""
Milestone 3 Challenger Tests: Adversarial Concurrency, Queue Flooding, Crash Isolation, and Cancellation.
"""

import pytest
import asyncio
import time
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
async def test_supervisor_cancellation_token_halts_worker_instantly(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits background task with CancellationToken; trips cancellation before execution."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)
    token = CancellationToken()
    token.cancel(reason="user_bargein")

    task = AgentTask(
        task_id="t-cancel-1",
        priority=3,
        role=AgentRole.CRITIC,
        payload={"draft": "long background draft"},
        cancellation_token=token,
    )

    res = await supervisor.execute_task_direct(task)
    assert res.status == TaskStatus.CANCELLED
    assert "cancelled" in res.error.lower()


@pytest.mark.asyncio
async def test_supervisor_task_timeout_handling(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits task with ultra-short timeout (0.02s) on slow mock agent; asserts TIMED_OUT status."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    # Monkeypatch critic to sleep
    original_critique = supervisor.critic.critique_draft
    async def slow_execute(payload, cancellation_token=None):
        await asyncio.sleep(0.15)
        return {"approved": True}

    supervisor.critic.execute = slow_execute

    task = AgentTask(
        task_id="t-timeout-1",
        priority=2,
        role=AgentRole.CRITIC,
        payload={"draft": "sample"},
        timeout_seconds=0.02,
    )

    res = await supervisor.execute_task_direct(task)
    assert res.status == TaskStatus.TIMED_OUT
    assert "timeout" in res.error.lower()


@pytest.mark.asyncio
async def test_supervisor_worker_crash_isolation_and_resilience(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Worker raises unhandled ZeroDivisionError; asserts supervisor catches failure and remains operational."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    async def crashing_execute(payload, cancellation_token=None):
        raise ZeroDivisionError("Simulated mathematical singularity crash")

    supervisor.router.execute = crashing_execute

    task_crash = AgentTask(
        task_id="t-crash",
        priority=1,
        role=AgentRole.ROUTER,
        payload={"query": "test query"},
    )

    res_crash = await supervisor.execute_task_direct(task_crash)
    assert res_crash.status == TaskStatus.FAILED
    assert "ZeroDivisionError" in res_crash.error or "singularity" in res_crash.error

    # Verify supervisor is still healthy and handles next task
    task_healthy = AgentTask(
        task_id="t-healthy",
        priority=2,
        role=AgentRole.CRITIC,
        payload={"draft": "clear response"},
    )
    res_healthy = await supervisor.execute_task_direct(task_healthy)
    assert res_healthy.status == TaskStatus.COMPLETED


@pytest.mark.asyncio
async def test_supervisor_retry_mechanism_with_transient_failures(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Simulates flaky worker failing on 1st attempt and succeeding on 2nd attempt with max_retries=2."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)
    attempts = 0

    async def flaky_execute(payload, cancellation_token=None):
        nonlocal attempts
        attempts += 1
        if attempts < 2:
            raise ConnectionError("Transient network drop")
        return {"status": "ok", "attempts": attempts}

    supervisor.retrieval.execute = flaky_execute

    task = AgentTask(
        task_id="t-flaky",
        priority=2,
        role=AgentRole.RETRIEVAL,
        payload={"query": "test"},
        max_retries=2,
    )

    res = await supervisor._dispatch(task)
    assert res.status == TaskStatus.COMPLETED
    assert attempts == 2


@pytest.mark.asyncio
async def test_supervisor_dead_letter_queue_on_exhausted_retries(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Task continuously fails exceeding max_retries; asserts recording in failed_tasks dead letter queue."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    async def always_failing(payload, cancellation_token=None):
        raise RuntimeError("Permanent upstream error")

    supervisor.critic.execute = always_failing

    task = AgentTask(
        task_id="t-dead-letter",
        priority=2,
        role=AgentRole.CRITIC,
        payload={"draft": "test"},
        max_retries=1,
    )

    res = await supervisor._dispatch(task)
    assert res.status == TaskStatus.FAILED
    assert any(f["task_id"] == "t-dead-letter" for f in supervisor.failed_tasks)


@pytest.mark.asyncio
async def test_supervisor_high_contention_queue_flooding_stress(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits 60 tasks concurrently with randomized priorities across 4 worker coroutines."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=4)
    await supervisor.start()

    futures = []
    for i in range(60):
        p = (i % 5) + 1
        t = AgentTask(
            task_id=f"flood-{i}",
            priority=p,
            role=AgentRole.CRITIC,
            payload={"draft": f"draft content {i}"},
        )
        fut = supervisor.submit_task(t)
        futures.append(fut)

    results = await asyncio.gather(*futures)
    assert len(results) == 60
    assert all(r.status == TaskStatus.COMPLETED for r in results)

    await supervisor.shutdown(wait=True, timeout=2.0)
    assert len(supervisor.completed_tasks) == 60


@pytest.mark.asyncio
async def test_supervisor_rapid_bargein_burst_cancellations(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits 30 tasks and immediately cancels all matching tasks; asserts clean recovery."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    for i in range(30):
        token = CancellationToken()
        t = AgentTask(
            task_id=f"burst-{i}",
            priority=4,
            role=AgentRole.CRITIC,
            payload={"draft": f"burst draft {i}"},
            cancellation_token=token,
        )
        supervisor.submit_task(t)

    cancelled_count = supervisor.cancel_tasks_matching(lambda t: "burst" in t.task_id, reason="bargein_burst")
    assert cancelled_count == 30
    assert len(supervisor.queue) == 0
