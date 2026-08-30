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


# Regression note (WIRE-CBC audit): the three tests below originally used
# "search for related procedures" as the query. That query contains no
# CouncilBudgetController risky keyword, so once CBC gating was added, these
# tests started depending on an UNVERIFIED assumption -- whether
# ActivationEngine/RecallEngine actually populate `context` with enough
# items (or unverified/related items) to cross COMPLEXITY_PLAN_STEP_THRESHOLD
# and get a non-NONE tier. That source was never available to confirm, so
# this was a latent, unverified dependency, not a deterministic test.
#
# Fixed by using a query containing a real risky keyword ("verify"), which
# CouncilBudgetController.decide() unconditionally escalates to at least
# STANDARD tier regardless of complexity/context -- restoring a
# deterministic guarantee that dispatch_report is present.
RISKY_QUERY = "verify related procedures"


def test_process_intent_attaches_dispatch_report():
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    result = executive.process_intent(Principal.AI_AGENT, RISKY_QUERY)

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

    result = executive.process_intent(Principal.AI_AGENT, RISKY_QUERY)

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
        def route_and_dispatch(self, principal, query, context, **kwargs):
            raise RuntimeError("boom")

    storage = StorageEngine()
    controller = MemoryController(storage)
    exploding = ExplodingOrchestrator(controller)
    executive = Executive(controller, orchestrator=exploding)

    result = executive.process_intent(Principal.AI_AGENT, RISKY_QUERY)

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


def test_dispatch_via_orchestrator_skips_redundant_retrieval():
    # Regression test: before skip_retrieval, any query containing a
    # deep-retrieval keyword ("search", "find", ...) made route_and_dispatch
    # fire a second live "search" through the ToolRouter, duplicating the
    # ActivationEngine + RecallEngine retrieval Executive already did for the
    # same query. This confirms the RETRIEVAL worker step is now skipped when
    # called from Executive. Uses RISKY_QUERY (guarantees dispatch) rather
    # than a query containing "search" itself, so the retrieval-skip check
    # is isolated from the deep-retrieval-keyword trigger.
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    result = executive.process_intent(Principal.AI_AGENT, RISKY_QUERY)

    retrieval_entries = [
        h for h in result["dispatch_report"]["orchestration_history"]
        if h.get("agent") == AgentRole.RETRIEVAL.value
    ]
    assert retrieval_entries == []


def test_direct_orchestrator_callers_keep_original_retrieval_behavior():
    # Backward-compatibility check: a caller that does NOT pass
    # skip_retrieval (the direct/standalone contract exercised by
    # cognitive_core/tests/test_multiagent_orchestration.py) must keep the
    # original behavior -- deep-retrieval keywords still trigger the
    # RETRIEVAL worker exactly as before this change.
    storage = StorageEngine()
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    context = [
        {"id": "n1", "content": "Verified knowledge", "verification": "verified"},
        {"id": "n2", "content": "Inferred knowledge", "verification": "unverified"},
    ]
    result = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for related procedures", context)

    retrieval_entries = [
        h for h in result["orchestration_history"]
        if h.get("agent") == AgentRole.RETRIEVAL.value
    ]
    assert len(retrieval_entries) == 1
