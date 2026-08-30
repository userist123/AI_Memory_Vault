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


def test_process_intent_uses_estimated_complexity_not_hardcoded_one():
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller)

    long_query = " ".join(["summarize"] * 12)
    result = executive.process_intent(Principal.AI_AGENT, long_query)

    # A 12-word query crosses COMPLEXITY_QUERY_WORD_THRESHOLD, so the
    # dispatch should reflect LIGHT tier (complexity-driven), not be skipped
    # as it would be under the old hardcoded complexity=1 default.
    assert result["dispatch_report"]["council_tier"] in ("light", "standard", "high_risk")
