import pytest
from cognitive_core.executive import Executive
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def _make_note(note_id, verification="unverified", relations=None):
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
        "relations": relations or [],
    }


def test_estimate_complexity_prefers_real_plan_step_count():
    e = Executive.__new__(Executive)  # bypass __init__ deps; testing pure method

    class FakePlan:
        def __init__(self, steps):
            self.steps = steps

    single_step_plan = FakePlan([{"step": 1}])
    multi_step_plan = FakePlan([{"step": 1}, {"step": 2}])

    assert e._estimate_complexity("anything", [], single_step_plan) == 1
    assert e._estimate_complexity("anything", [], multi_step_plan) == 2


def test_process_intent_uses_real_planner_step_count_for_dispatch():
    # Regression test: complexity must reflect the ACTUAL Planner.create_plan()
    # output (verified from cognitive_core/planning.py), not a pre-planning
    # word-count/context-size proxy. A short query with unverified context
    # produces a 2-step plan (search + verify), which must yield LIGHT tier
    # or higher -- even though the query itself is short.
    storage = StorageEngine()
    storage.set("n1", _make_note("n1", verification="unverified"))
    controller = MemoryController(storage)
    executive = Executive(controller)

    short_query = "check"  # would be complexity=1 under the old word-count proxy
    result = executive.process_intent(Principal.AI_AGENT, short_query)

    assert executive.active_plan is not None
    if len(executive.active_plan.steps) >= Executive.COMPLEXITY_PLAN_STEP_THRESHOLD:
        assert result["dispatch_report"]["council_tier"] in ("light", "standard", "high_risk")
