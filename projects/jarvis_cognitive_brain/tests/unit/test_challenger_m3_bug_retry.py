"""
Targeted tests for Challenger findings:
1. Cancellation in worker loop kills worker and leaves future unresolved.
2. Retry logic duplicate dispatch bug.
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
async def test_retry_duplicate_execution_race(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits a task with max_retries=1 to a 2-worker supervisor.
    The task fails on attempt 1 and succeeds on attempt 2.
    Asserts total executions across all workers is exactly 2, not duplicated.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)
    await supervisor.start()

    execution_count = 0

    async def flaky_execute(payload, cancellation_token=None):
        nonlocal execution_count
        execution_count += 1
        if execution_count == 1:
            raise ConnectionError("Transient network drop")
        await asyncio.sleep(0.02)
        return {"status": "ok", "count": execution_count}

    supervisor.retrieval.execute = flaky_execute

    task = AgentTask(
        task_id="t-retry-dup-check",
        priority=2,
        role=AgentRole.RETRIEVAL,
        payload={"query": "test"},
        max_retries=1,
    )

    fut = supervisor.submit_task(task)
    res = await fut
    assert res.status == TaskStatus.COMPLETED

    # Give other worker time to pull from queue if duplicate was queued
    await asyncio.sleep(0.1)
    await supervisor.shutdown(wait=True, timeout=2.0)

    # In the buggy code, execution_count will be 3 because attempt 2 ran directly and attempt 2 was also queued and run by worker 2!
    print(f"Total executions observed: {execution_count}")
    assert execution_count == 2, f"Expected 2 executions (1 fail + 1 retry), but observed {execution_count} due to duplicate queue put!"
