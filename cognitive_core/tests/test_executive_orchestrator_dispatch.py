import pytest
from cognitive_core.executive import Executive
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def _make_verified_note(note_id, verification="verified"):
    return {
        "id": note_id,
        "type": "lesson",
        "lifecycle": "ACTIVE",
        "content": f"Content for {note_id}",
        "category": "test",
        "tags": [],
        "created": "2026-08-30",
        "updated": "2026-08-30",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
    }


def test_process_intent_attaches_dispatch_report():
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    result = executive.process_intent(Principal.AI_AGENT, "search for related procedures")

    assert "dispatch_report" in result
    report = result["dispatch_report"]
    assert "orchestration_history" in report
    assert "total_context_used" in report
    assert report["status"] == "completed"


def test_dispatch_report_reflects_verification_counts():
    storage = StorageEngine()
    storage.set("v1", _make_verified_note("v1", verification="verified"))
    storage.set("v2", _make_verified_note("v2", verification="unverified"))
    controller = MemoryController(storage)
    executive = Executive(controller)

    result = executive.process_intent(Principal.AI_AGENT, "search for related procedures")

    verifier_entries = [
        h for h in result["dispatch_report"]["orchestration_history"]
        if h.get("agent") == AgentRole.VERIFIER.value
    ]
    assert len(verifier_entries) == 1


def test_orchestrator_failure_does_not_break_process_intent():
    # WIRE-MAO fail-soft contract: if the orchestrator dispatch raises, the
    # primary cognitive loop must still complete and return a status, just
    # without a dispatch_report key.
    class ExplodingOrchestrator(MultiAgentOrchestrator):
        def route_and_dispatch(self, principal, query, context):
            raise RuntimeError("boom")

    storage = StorageEngine()
    controller = MemoryController(storage)
    exploding = ExplodingOrchestrator(controller)
    executive = Executive(controller, orchestrator=exploding)

    result = executive.process_intent(Principal.AI_AGENT, "search for related procedures")

    assert "dispatch_report" not in result
    assert "status" in result


def test_orchestrator_can_be_injected_and_is_reused():
    storage = StorageEngine()
    controller = MemoryController(storage)
    custom_orchestrator = MultiAgentOrchestrator(controller)
    executive = Executive(controller, orchestrator=custom_orchestrator)

    assert executive.orchestrator is custom_orchestrator
    # Confirms the orchestrator was NOT rebuilt with a second, independent
    # ToolRouter -- it is exactly the instance the caller passed in.


def test_default_orchestrator_shares_executive_tool_router():
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    # WIRE-MAO: the default orchestrator must reuse this Executive's own
    # ToolRouter so worker actions go through the same authorization/audit
    # path as every other action in the loop, not a second independent one.
    assert executive.orchestrator.router is executive.router
