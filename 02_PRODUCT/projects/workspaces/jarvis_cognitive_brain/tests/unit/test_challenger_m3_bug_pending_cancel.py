"""
Targeted test for Challenger finding:
Cancelled pending task without CancellationToken still executes in worker loop.
"""

import pytest
import asyncio
import time
from typing import Dict, Any

from jarvis.memory.sqlite_engine import SQLiteStorageEngine
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
async def test_cancelled_pending_task_must_not_execute(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits 5 tasks without cancellation tokens while workers are busy with a 50ms task.
    Calls cancel_tasks_matching to cancel the 5 pending tasks.
    Asserts the 5 cancelled tasks are NOT executed by the worker pool.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=1)
    await supervisor.start()

    executed_tasks = []

    async def tracking_execute(payload, cancellation_token=None):
        name = payload.get("name")
        executed_tasks.append(name)
        await asyncio.sleep(0.05)
        return {"status": "ok", "name": name}

    supervisor.critic.execute = tracking_execute

    # 1. Submit blocker task that keeps worker busy
    t_block = AgentTask(task_id="blocker", priority=1, role=AgentRole.CRITIC, payload={"name": "blocker"})
    supervisor.submit_task(t_block)

    # 2. Submit 3 pending tasks without explicit CancellationToken
    for i in range(3):
        t = AgentTask(task_id=f"pending-{i}", priority=3, role=AgentRole.CRITIC, payload={"name": f"pending-{i}"})
        supervisor.submit_task(t)

    # 3. Cancel matching pending tasks immediately before blocker finishes
    cancelled = supervisor.cancel_tasks_matching(lambda t: "pending" in t.task_id)
    assert cancelled == 3

    # Wait for blocker to finish and let worker loop process queue
    await asyncio.sleep(0.15)
    await supervisor.shutdown(wait=True, timeout=2.0)

    print(f"Executed tasks: {executed_tasks}")
    # Only "blocker" should have executed! "pending-0", "pending-1", "pending-2" must NOT have executed!
    assert executed_tasks == ["blocker"], f"Expected only ['blocker'], but observed {executed_tasks} (pending tasks executed despite cancellation!)"
