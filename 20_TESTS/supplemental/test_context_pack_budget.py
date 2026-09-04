import pytest

from memory_controller.context.budget import BudgetExceededError
from memory_controller.context.pack_builder import ContextPackBuilder


def test_pack_builder_uses_agent_budget_when_legacy_budget_is_empty():
    builder = ContextPackBuilder()
    results = [{"id": str(i), "content": "x" * 1000} for i in range(10)]
    pack = builder.build("t", "backend_systems_engineer", {}, results, "full")

    assert len(pack["results"]) <= 5
    assert pack["budget"]["hard"] == 24576
    assert pack["budget"]["hard_tokens"] == 3000


def test_pack_builder_never_emits_more_than_hard_budget():
    builder = ContextPackBuilder()
    results = [{"id": "x", "metadata": "x" * 1000}]
    with pytest.raises(BudgetExceededError):
        builder.build("t", "backend_systems_engineer", {"soft": 128, "hard": 256}, results, "full")


def test_pack_builder_enforces_token_budget_even_when_bytes_fit():
    builder = ContextPackBuilder()
    results = [{"id": "x", "content": "x" * 10000}]
    pack = builder.build(
        "t", "backend_systems_engineer",
        {"soft": 24576, "hard": 24576, "soft_tokens": 1000, "hard_tokens": 1200},
        results, "full"
    )

    assert pack["budget"]["hard_tokens"] == 1200
    assert builder._resolve_budget("backend_systems_engineer", pack["budget"]).estimate_tokens(pack) <= 1200


def test_pack_builder_fails_when_even_empty_envelope_exceeds_token_budget():
    builder = ContextPackBuilder()
    with pytest.raises(BudgetExceededError):
        builder.build(
            "t", "backend_systems_engineer",
            {"soft": 24576, "hard": 24576, "soft_tokens": 1, "hard_tokens": 1},
            [], "metadata"
        )
