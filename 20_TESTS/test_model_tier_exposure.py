import pytest
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def test_retrieval_history_entry_has_real_model_tier():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orch = MultiAgentOrchestrator(controller)

    result = orch.route_and_dispatch(Principal.AI_AGENT, "search for x", [])

    retrieval_entries = [h for h in result["orchestration_history"] if h.get("agent") == AgentRole.RETRIEVAL.value]
    assert len(retrieval_entries) == 1
    assert retrieval_entries[0]["model_tier"] == "light"


def test_verifier_history_entry_has_real_model_tier():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orch = MultiAgentOrchestrator(controller)

    result = orch.route_and_dispatch(Principal.AI_AGENT, "check this", [{"verification": "verified"}])

    verifier_entries = [h for h in result["orchestration_history"] if h.get("agent") == AgentRole.VERIFIER.value]
    assert len(verifier_entries) == 1
    assert verifier_entries[0]["model_tier"] == "light"


def test_model_tier_matches_subagentspec_source_of_truth():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orch = MultiAgentOrchestrator(controller)

    result = orch.route_and_dispatch(Principal.AI_AGENT, "search and check", [{"verification": "unverified"}])

    for entry in result["orchestration_history"]:
        role = AgentRole(entry["agent"])
        assert entry["model_tier"] == orch.workers[role].model_tier
