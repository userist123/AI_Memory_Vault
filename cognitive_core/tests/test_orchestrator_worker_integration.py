"""Integration tests for the current SubagentSpec + ToolRouter orchestrator contract.

The runtime no longer exposes the older worker_agents/process_task API. These
regressions therefore verify the implementation that actually exists:
least-privilege SubagentSpec gating, worker tier metadata, retrieval/verifier
history, and preservation of the public route_and_dispatch result shape.
"""
from uuid import uuid4

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, StorageEngine
from cognitive_core.orchestrator import AgentRole, MultiAgentOrchestrator


def make_note(note_id, verification="unverified", lifecycle="ACTIVE"):
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": "test content",
    }


@pytest.fixture
def orchestrator():
    storage = StorageEngine()
    controller = MemoryController(storage)
    return MultiAgentOrchestrator(controller)


def test_subagent_specs_exist_with_expected_model_tiers(orchestrator):
    assert set(orchestrator.workers) == {
        AgentRole.ROUTER,
        AgentRole.RETRIEVAL,
        AgentRole.VERIFIER,
        AgentRole.CONSOLIDATOR,
        AgentRole.CRITIC,
        AgentRole.SYNTHESIZER,
    }

    assert orchestrator.workers[AgentRole.ROUTER].model_tier == "light"
    assert orchestrator.workers[AgentRole.RETRIEVAL].model_tier == "light"
    assert orchestrator.workers[AgentRole.VERIFIER].model_tier == "light"
    assert orchestrator.workers[AgentRole.CONSOLIDATOR].model_tier == "standard"
    assert orchestrator.workers[AgentRole.CRITIC].model_tier == "standard"
    assert orchestrator.workers[AgentRole.SYNTHESIZER].model_tier == "heavy"


def test_least_privilege_enforcement(orchestrator):
    with pytest.raises(
        PermissionError,
        match="not permitted to perform action 'archive'",
    ):
        orchestrator._execute_worker_action(
            AgentRole.ROUTER,
            Principal.AI_AGENT,
            "archive",
            {"note_id": "x"},
        )

    # RETRIEVAL is read/search only; archive remains unavailable.
    with pytest.raises(
        PermissionError,
        match="not permitted to perform action 'archive'",
    ):
        orchestrator._execute_worker_action(
            AgentRole.RETRIEVAL,
            Principal.AI_AGENT,
            "archive",
            {"note_id": "x"},
        )


def test_route_and_dispatch_executes_retrieval_and_verifier_for_deep_query(orchestrator):
    result = orchestrator.route_and_dispatch(
        Principal.AI_AGENT,
        "search for related procedures",
        [],
    )

    assert result["status"] == "completed"
    assert result["query"] == "search for related procedures"
    assert "orchestration_history" in result
    assert "total_context_used" in result

    retrieval_entries = [
        item for item in result["orchestration_history"]
        if item.get("agent") == AgentRole.RETRIEVAL.value
    ]
    verifier_entries = [
        item for item in result["orchestration_history"]
        if item.get("agent") == AgentRole.VERIFIER.value
    ]

    assert len(retrieval_entries) == 1
    assert retrieval_entries[0]["model_tier"] == "light"
    assert len(verifier_entries) == 1
    assert verifier_entries[0]["model_tier"] == "light"


def test_skip_retrieval_prevents_duplicate_search(orchestrator):
    result = orchestrator.route_and_dispatch(
        Principal.AI_AGENT,
        "search history",
        [{"id": "already-retrieved", "verification": "verified"}],
        skip_retrieval=True,
    )

    assert result["status"] == "completed"
    assert not any(
        item.get("agent") == AgentRole.RETRIEVAL.value
        for item in result["orchestration_history"]
    )
    assert result["total_context_used"] == 1


def test_max_context_items_is_enforced(orchestrator):
    context = [
        {"id": "n1", "verification": "verified"},
        {"id": "n2", "verification": "verified"},
        {"id": "n3", "verification": "unverified"},
    ]

    result = orchestrator.route_and_dispatch(
        Principal.AI_AGENT,
        "simple query",
        context,
        skip_retrieval=True,
        max_context_items=2,
    )

    assert result["total_context_used"] == 2
    verifier = next(
        item for item in result["orchestration_history"]
        if item.get("agent") == AgentRole.VERIFIER.value
    )
    assert verifier["verified_nodes"] == 2
    assert verifier["unverified_nodes"] == 0


def test_principal_is_forwarded_to_tool_router(orchestrator):
    calls = []
    original_execute = orchestrator.router.execute

    def spy_execute(principal, action, kwargs):
        calls.append((principal, action, kwargs))
        return original_execute(principal, action, kwargs)

    orchestrator.router.execute = spy_execute
    orchestrator.route_and_dispatch(
        Principal.HUMAN,
        "search for x",
        [],
    )

    assert calls
    assert all(call[0] == Principal.HUMAN for call in calls)
    assert any(call[1] == "search" for call in calls)


def test_verification_data_is_not_mutated_by_read_only_dispatch(orchestrator):
    note_id = str(uuid4())
    orchestrator.controller.storage.set(note_id, make_note(note_id))
    before = orchestrator.controller.storage.get(note_id)

    orchestrator.route_and_dispatch(
        Principal.AI_AGENT,
        "verify this",
        [before],
        skip_retrieval=True,
    )

    after = orchestrator.controller.storage.get(note_id)
    assert after["verification"] == "unverified"
    assert after["provenance"]["source_type"] == "user"


def test_existing_route_and_dispatch_contract_still_compatible(orchestrator):
    result = orchestrator.route_and_dispatch(
        Principal.AI_AGENT,
        "search for related procedures",
        [],
    )

    assert result["status"] == "completed"
    assert "orchestration_history" in result
    assert "total_context_used" in result
    assert "query" in result
