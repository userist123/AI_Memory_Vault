import pytest

from memory_controller.context.pack_builder import ContextPackBuilder


def test_pack_builder_uses_agent_budget_when_legacy_budget_is_empty():
    builder = ContextPackBuilder()
    results = [{"id": str(i), "content": "x" * 1000} for i in range(10)]
    pack = builder.build("t", "backend_systems_engineer", {}, results, "full")

    assert len(pack["results"]) <= 5
    assert pack["budget"]["hard"] == 24576


def test_pack_builder_never_emits_more_than_hard_budget():
    builder = ContextPackBuilder()
    results = [{"id": "x", "metadata": "x" * 1000}]
    with pytest.raises(RuntimeError):
        builder.build("t", "backend_systems_engineer", {"soft": 128, "hard": 256}, results, "full")
