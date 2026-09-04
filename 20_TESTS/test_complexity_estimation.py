import pytest
from cognitive_core.executive import Executive
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def test_short_query_small_context_is_complexity_1():
    e = Executive.__new__(Executive)  # bypass __init__ deps; testing pure method
    assert e._estimate_complexity("what time is it", []) == 1


def test_long_query_is_complexity_2():
    e = Executive.__new__(Executive)
    long_query = " ".join(["word"] * 12)
    assert e._estimate_complexity(long_query, []) == 2


def test_large_context_is_complexity_2_even_with_short_query():
    e = Executive.__new__(Executive)
    context = [{"id": f"n{i}"} for i in range(3)]
    assert e._estimate_complexity("ok", context) == 2


def test_process_intent_uses_real_planner_shape_not_query_length_proxy():
    """The production decision is derived from the real ActivePlan.

    A repeated 12-word query can still produce a single-step read-only plan.
    In that case Council must remain skipped despite the old word-count
    heuristic returning complexity=2.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    long_query = " ".join(["summarize"] * 12)
    result = executive.process_intent(Principal.AI_AGENT, long_query)

    assert executive.active_plan is not None
    assert len(executive.active_plan.steps) == 1
    assert result["status"] == "success"
    assert "dispatch_report" not in result
