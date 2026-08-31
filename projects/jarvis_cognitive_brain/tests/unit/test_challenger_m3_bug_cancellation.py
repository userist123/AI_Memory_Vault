"""
Targeted test for Challenger finding:
Worker death and orphaned future on asyncio.CancelledError.
"""

import pytest
import asyncio
import time
from typing import Dict, Any

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
async def test_worker_survives_asyncio_cancelled_error(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits a task whose execution raises asyncio.CancelledError (e.g. cancelled sub-coroutine).
    1. Asserts the task's future resolves to TaskStatus.CANCELLED (not hanging).
    2. Asserts the supervisor worker loop does NOT terminate and can process subsequent tasks.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=1)
    await supervisor.start()

    async def cancelling_execute(payload, cancellation_token=None):
        raise asyncio.CancelledError("Simulated internal cancellation")

    supervisor.critic.execute = cancelling_execute

    task1 = AgentTask(
        task_id="t-cancel-worker-test",
        priority=2,
        role=AgentRole.CRITIC,
        payload={"draft": "test"},
    )

    fut1 = supervisor.submit_task(task1)
    # This must not hang and must resolve within 1 second
    res1 = await asyncio.wait_for(fut1, timeout=1.0)
    assert res1.status == TaskStatus.CANCELLED, f"Expected CANCELLED status, got {res1.status}"

    # Now verify the single worker in the pool is STILL ALIVE and can execute task2
    async def healthy_execute(payload, cancellation_token=None):
        return {"status": "ok"}

    supervisor.critic.execute = healthy_execute

    task2 = AgentTask(
        task_id="t-healthy-after-cancel",
        priority=2,
        role=AgentRole.CRITIC,
        payload={"draft": "healthy"},
    )

    fut2 = supervisor.submit_task(task2)
    res2 = await asyncio.wait_for(fut2, timeout=1.0)
    assert res2.status == TaskStatus.COMPLETED
    assert res2.result == {"status": "ok"}

    await supervisor.shutdown(wait=True, timeout=2.0)
