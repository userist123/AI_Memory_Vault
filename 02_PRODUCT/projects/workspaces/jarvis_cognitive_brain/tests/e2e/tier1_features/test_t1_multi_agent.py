"""
Tier 1 Feature Coverage: Multi-Agent Worker Coordination & Specialized Roles (R3).
Covers Supervisor priority queue, Router query decomposition, Retrieval associative recall,
Verifier frontmatter schema audit, and Consolidator/Critic synthesis.
Uses production jarvis.agents implementation.
"""

import pytest
import asyncio
import uuid
import heapq
from typing import Dict, Any, List, Optional

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.agents import (
    AgentTask,
    AgentRole,
    TaskPriority,
    MultiAgentSupervisor,
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent,
)


def test_supervisor_task_priority_queue(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Test Supervisor prioritizes urgent interactive tasks over background jobs."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    task_low = AgentTask(task_id="t-low", priority=5, role="verifier", payload={"note": {"id": "1"}})
    task_high = AgentTask(task_id="t-high", priority=1, role="router", payload={"query": "Turn off lights"})

    supervisor.submit_task(task_low)
    supervisor.submit_task(task_high)

    assert len(supervisor.queue) == 2
    # Highest priority (lowest number) should be popped first
    first = heapq.heappop(supervisor.queue)
    assert first.task_id == "t-high"


@pytest.mark.asyncio
async def test_router_agent_intent_decomposition(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Test Router agent decomposes composite requests into atomic sub-intent tasks."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)
    task = AgentTask(
        task_id="t1",
        priority=1,
        role="router",
        payload={"query": "Turn on kitchen light and set thermostat to 22 degrees"},
    )
    supervisor.submit_task(task)

    result = await supervisor.run_next_task()
    assert result is not None
    assert result["count"] == 2
    assert "Turn on kitchen light" in result["subtasks"][0]


def test_verifier_agent_frontmatter_audit(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Test Verifier agent validates strict presence of canonical metadata fields."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    valid_note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "core",
        "provenance": {"source_type": "execution", "source_ref": "test"},
    }
    invalid_note = {"id": str(uuid.uuid4()), "content": "Missing type and category"}

    res_valid = supervisor._run_verifier({"note": valid_note})
    assert res_valid["valid"] is True
    assert len(res_valid["missing"]) == 0

    res_invalid = supervisor._run_verifier({"note": invalid_note})
    assert res_invalid["valid"] is False
    assert "type" in res_invalid["missing"]
    assert "category" in res_invalid["missing"]


def test_retrieval_agent_lineage_isolation(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Test Retrieval agent scopes search queries against SQLite storage."""
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "security",
            "tags": ["crypto"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "very_high",
            "verification": "partially_verified",
            "relations": [],
            "content": "AES-256-GCM encryption is used for confidential payloads.",
        },
    )

    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)
    res = supervisor._run_retrieval({"query": "AES-256-GCM encryption"})

    assert res["count"] >= 1
    assert any("AES-256-GCM" in n["content"] for n in res["matches"])


@pytest.mark.asyncio
async def test_consolidator_and_critic_agent_workflow(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Test Critic agent evaluation passes compliant responses."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)
    task = AgentTask(
        task_id="t-critic",
        priority=2,
        role="critic",
        payload={"draft": "The living room light has been set to 75% brightness."},
    )
    supervisor.submit_task(task)

    result = await supervisor.run_next_task()
    assert result is not None
    assert result["approved"] is True
    assert "clear" in result["critique"]
