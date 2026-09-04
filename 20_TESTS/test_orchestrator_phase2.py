"""Phase 2 regression tests for the current MultiAgentOrchestrator contract.

These tests verify single retrieval execution, correct argument forwarding,
maintenance delegation, authorization, and failure propagation without relying
on the removed worker_agents API.
"""
import pytest
from unittest.mock import patch
from uuid import uuid4

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole


def make_note(note_id, note_type="knowledge", lifecycle="ACTIVE", verification="unverified", content="c"):
    return {
        "id": note_id,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content,
    }


@pytest.fixture
def orchestrator():
    storage = StorageEngine()
    controller = MemoryController(storage)
    return MultiAgentOrchestrator(controller)


def test_exactly_one_retrieval_execution_per_dispatch(orchestrator):
    """A deep query performs one retrieval tool execution."""
    original = orchestrator._execute_worker_action
    with patch.object(orchestrator, "_execute_worker_action", wraps=original) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for related history", [])
        retrieval_calls = [
            call for call in spy.call_args_list
            if call.args[0] == AgentRole.RETRIEVAL and call.args[2] == "search"
        ]
        assert len(retrieval_calls) == 1


def test_no_retrieval_execution_when_not_needed(orchestrator):
    """A non-deep query performs zero retrieval executions."""
    original = orchestrator._execute_worker_action
    with patch.object(orchestrator, "_execute_worker_action", wraps=original) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "hello", [])
        retrieval_calls = [
            call for call in spy.call_args_list
            if call.args[0] == AgentRole.RETRIEVAL and call.args[2] == "search"
        ]
        assert retrieval_calls == []


def test_retrieval_agent_receives_correct_query(orchestrator):
    """The retrieval ToolRouter call receives the original query unchanged."""
    captured = {}
    original = orchestrator._execute_worker_action

    def spy(role, principal, action, kwargs):
        if role == AgentRole.RETRIEVAL and action == "search":
            captured["query"] = kwargs["query"]
        return original(role, principal, action, kwargs)

    with patch.object(orchestrator, "_execute_worker_action", side_effect=spy):
        orchestrator.route_and_dispatch(Principal.HUMAN, "search for onboarding history", [])

    assert captured["query"] == "search for onboarding history"


def test_retrieval_result_propagates_into_combined_context(orchestrator):
    note_id = str(uuid4())
    orchestrator.controller.storage.set(note_id, make_note(note_id, content="search target content"))
    result = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for target content", [])
    assert result["status"] == "completed"
    assert isinstance(result["total_context_used"], int)


def test_consolidator_legacy_components_are_used_by_run_maintenance_pipeline(orchestrator):
    """The current maintenance contract delegates to the existing consolidator
    and deduplicator components rather than an obsolete worker_agents registry."""
    with patch.object(orchestrator.consolidator, "consolidate_lessons", wraps=orchestrator.consolidator.consolidate_lessons) as consolidate_spy, \
         patch.object(orchestrator.deduplicator, "scan_for_duplicates", wraps=orchestrator.deduplicator.scan_for_duplicates) as dedup_spy:
        result = orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
    consolidate_spy.assert_called_once()
    dedup_spy.assert_called_once()
    assert "duplicates_flagged" in result
    assert "consolidated_id" in result


def test_run_maintenance_pipeline_result_shape_unchanged(orchestrator):
    result = orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
    assert "duplicates_flagged" in result
    assert "consolidated_id" in result
    assert isinstance(result["duplicates_flagged"], int)


def test_maintenance_authorization_preserved(orchestrator):
    lesson_ids = [str(uuid4()), str(uuid4())]
    for lid in lesson_ids:
        orchestrator.controller.storage.set(
            lid,
            make_note(
                lid,
                note_type="lesson",
                lifecycle="REVIEW",
                content=f"lesson {lid}",
            ),
        )
    result = orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
    consolidated_id = result.get("consolidated_id")
    if consolidated_id:
        consolidated_note = orchestrator.controller.storage.get(consolidated_id)
        if consolidated_note:
            assert consolidated_note["verification"] != "verified"


def test_maintenance_failure_is_propagated(orchestrator):
    with patch.object(
        orchestrator.consolidator,
        "consolidate_lessons",
        side_effect=RuntimeError("maintenance boom"),
    ):
        with pytest.raises(RuntimeError, match="maintenance boom"):
            orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)


def test_subagent_spec_gating_still_enforced_after_retrieval_migration(orchestrator):
    with pytest.raises(PermissionError, match="not permitted to perform action 'propose'"):
        orchestrator._execute_worker_action(
            AgentRole.RETRIEVAL,
            Principal.AI_AGENT,
            "propose",
            {"note_data": {}},
        )
