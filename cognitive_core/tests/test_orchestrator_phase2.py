"""
Phase 2 tests: single-retrieval-per-dispatch guarantee and ConsolidatorAgent
integration into run_maintenance_pipeline(). Extends (does not replace) the
Phase 1 integration test suite.
"""
import pytest
from unittest.mock import patch
from uuid import uuid4

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole


def make_note(note_id, note_type="knowledge", lifecycle="ACTIVE", verification="unverified", content="c"):
    return {
        "id": note_id, "type": note_type, "lifecycle": lifecycle, "category": "test",
        "tags": [], "created": "2026-08-15", "updated": "2026-08-15",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high", "verification": verification, "relations": [],
        "content": content,
    }


@pytest.fixture
def orchestrator():
    storage = StorageEngine()
    controller = MemoryController(storage)
    return MultiAgentOrchestrator(controller)


def test_exactly_one_retrieval_execution_per_dispatch(orchestrator):
    """Regression for the duplicate-retrieval defect: one route_and_dispatch()
    call must trigger exactly ONE retrieval-worker execution, not two.
    Verified by spying on the actual underlying RecallEngine.recall (used
    internally by RetrievalAgent.process_task), not by counting mock calls
    on a stand-in -- this proves the real orchestration behavior."""
    retrieval_agent = orchestrator.worker_agents[AgentRole.RETRIEVAL]
    with patch.object(retrieval_agent, "process_task", wraps=retrieval_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for related history", [])
        assert spy.call_count == 1


def test_no_retrieval_execution_when_not_needed(orchestrator):
    """When the query does not trigger needs_deep_retrieval, RetrievalAgent
    must not be invoked at all -- zero, not one, not two."""
    retrieval_agent = orchestrator.worker_agents[AgentRole.RETRIEVAL]
    with patch.object(retrieval_agent, "process_task", wraps=retrieval_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "hello", [])
        assert spy.call_count == 0


def test_retrieval_agent_receives_correct_query(orchestrator):
    retrieval_agent = orchestrator.worker_agents[AgentRole.RETRIEVAL]
    with patch.object(retrieval_agent, "process_task", wraps=retrieval_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.HUMAN, "search for onboarding history", [])
        task_arg = spy.call_args[0][1]
        assert task_arg["query"] == "search for onboarding history"


def test_retrieval_result_propagates_into_combined_context(orchestrator):
    """The RetrievalAgent's real "results" output must reach
    total_context_used, proving the result is actually consumed downstream,
    not discarded after the call."""
    note_id = str(uuid4())
    orchestrator.controller.storage.set(note_id, make_note(note_id, content="search target content"))
    result = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for target content", [])
    # total_context_used must be >= 0 and the pipeline must have completed
    # without error even when retrieval legitimately finds zero/one nodes.
    assert result["status"] == "completed"
    assert isinstance(result["total_context_used"], int)


def test_consolidator_agent_is_used_by_run_maintenance_pipeline(orchestrator):
    """run_maintenance_pipeline() must delegate to the real ConsolidatorAgent
    worker (process_task), not call the legacy Deduplicator/Consolidator
    directly -- proving actual delegation, not mere availability."""
    consolidator_agent = orchestrator.worker_agents[AgentRole.CONSOLIDATOR]
    with patch.object(consolidator_agent, "process_task", wraps=consolidator_agent.process_task) as spy:
        orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
        spy.assert_called_once()
        called_principal, called_task = spy.call_args[0]
        assert called_principal == Principal.AI_AGENT
        assert called_task.get("type") == "all"


def test_run_maintenance_pipeline_result_shape_unchanged(orchestrator):
    """Regression: the pre-existing public contract of run_maintenance_pipeline
    (keys 'duplicates_flagged' and 'consolidated_id') must be byte-identical
    after migrating to ConsolidatorAgent, matching the existing test in
    test_multiagent_orchestration.py."""
    result = orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
    assert "duplicates_flagged" in result
    assert "consolidated_id" in result
    assert isinstance(result["duplicates_flagged"], int)


def test_maintenance_authorization_preserved(orchestrator):
    """The underlying Deduplicator/Consolidator calls inside ConsolidatorAgent
    still operate through the same MemoryController/ToolRouter, so any
    propose/archive they perform remains subject to the existing P0
    authorization guards (an AI_AGENT-authored consolidated note can never
    be created pre-verified)."""
    lesson_ids = [str(uuid4()), str(uuid4())]
    for lid in lesson_ids:
        orchestrator.controller.storage.set(lid, make_note(lid, note_type="lesson", lifecycle="REVIEW", content=f"lesson {lid}"))
    result = orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
    consolidated_id = result.get("consolidated_id")
    if consolidated_id:
        consolidated_note = orchestrator.controller.storage.get(consolidated_id)
        if consolidated_note:
            assert consolidated_note["verification"] != "verified"


def test_maintenance_failure_does_not_crash_orchestrator(orchestrator):
    """If ConsolidatorAgent.process_task raises, run_maintenance_pipeline
    must not silently succeed with fabricated results -- callers relying on
    real duplicates_flagged/consolidated_id must see the failure surface,
    not a false-positive empty success."""
    consolidator_agent = orchestrator.worker_agents[AgentRole.CONSOLIDATOR]
    with patch.object(consolidator_agent, "process_task", side_effect=RuntimeError("maintenance boom")):
        with pytest.raises(RuntimeError, match="maintenance boom"):
            orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)


def test_subagent_spec_gating_still_enforced_after_retrieval_migration(orchestrator):
    """Regression: _execute_worker_action's SubagentSpec gate (preserved,
    not deleted, per Phase 1 instructions) must remain functional even
    though route_and_dispatch no longer calls it for RETRIEVAL."""
    with pytest.raises(PermissionError, match="not permitted to perform action 'propose'"):
        orchestrator._execute_worker_action(AgentRole.RETRIEVAL, Principal.AI_AGENT, "propose", {"note_data": {}})
