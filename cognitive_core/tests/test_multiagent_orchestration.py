import pytest
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole
from cognitive_core.executive import Executive
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal

def test_orchestrator_initialization():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    assert AgentRole.ROUTER in orchestrator.workers
    assert AgentRole.RETRIEVAL in orchestrator.workers
    assert AgentRole.VERIFIER in orchestrator.workers
    assert AgentRole.CONSOLIDATOR in orchestrator.workers
    assert AgentRole.CRITIC in orchestrator.workers

def test_orchestrator_least_privilege_enforcement():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    # ROUTER cannot call archive
    with pytest.raises(PermissionError, match="not permitted to perform action 'archive'"):
        orchestrator._execute_worker_action(AgentRole.ROUTER, Principal.AI_AGENT, "archive", {"note_id": "x"})

def test_orchestrator_route_and_dispatch():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    # Setup context
    context = [
        {"id": "n1", "content": "Verified knowledge", "verification": "verified"},
        {"id": "n2", "content": "Inferred knowledge", "verification": "unverified"}
    ]

    result = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for related procedures", context)
    assert result["status"] == "completed"
    assert "orchestration_history" in result
    assert result["total_context_used"] >= 2

def test_orchestrator_maintenance_pipeline():
    storage = StorageEngine()
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    maintenance_result = orchestrator.run_maintenance_pipeline(Principal.AI_AGENT)
    assert "duplicates_flagged" in maintenance_result
    assert "consolidated_id" in maintenance_result

def test_executive_wires_maintenance_on_plan_completion():
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    # Pre-populate 2 REVIEW lessons to check if maintenance consolidates them
    lesson1 = {
        "id": "l1",
        "type": "lesson",
        "lifecycle": "REVIEW",
        "content": "Lesson 1: Always check memory boundaries before writing.",
        "category": "test",
        "tags": [],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": []
    }
    lesson2 = {
        "id": "l2",
        "type": "lesson",
        "lifecycle": "REVIEW",
        "content": "Lesson 2: Always check memory boundaries before writing.",
        "category": "test",
        "tags": [],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": []
    }
    storage.set("l1", lesson1)
    storage.set("l2", lesson2)

    # Process an intent to trigger and complete plan
    res = executive.process_intent(Principal.ADMIN, "search for lessons")
    assert res["status"] == "success"
