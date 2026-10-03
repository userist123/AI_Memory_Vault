import json
import zlib
import pytest

from memory_controller.context.budget import ContextBudget, BudgetExceededError


def test_zlib_roundtrip():
    text = "The quick brown fox jumps over the lazy dog" * 10
    compressed = zlib.compress(text.encode('utf-8'))
    decompressed = zlib.decompress(compressed).decode('utf-8')
    assert decompressed == text

def generate_note(id_suffix: int, size: int, relevance: int):
    content = "a" * size
    return {"id": f"note{id_suffix}", "content": content, "relevance": relevance}

def usage(notes):
    return sum(len(json.dumps(n).encode('utf-8')) for n in notes)

def test_soft_budget_degradation():
    cfg = {"soft_limit_bytes": 150, "hard_limit_bytes": 1000, "max_full_documents": 1}
    budget = ContextBudget(cfg)
    notes = [
        generate_note(1, 200, relevance=10),
        generate_note(2, 200, relevance=5),
        generate_note(3, 200, relevance=1),
    ]
    degraded = budget.apply_degradation(notes)
    # Verify that the resulting pack respects the soft budget
    assert usage(degraded) <= cfg["soft_limit_bytes"]
    # At most max_full_documents notes may retain non-empty content
    non_empty = [n for n in degraded if n["content"]]
    assert len(non_empty) <= cfg["max_full_documents"]
    # If a note is kept, it may be FULL or PARTIAL (contains marker)
    for n in non_empty:
        content = n["content"]
        assert content == "" or "[PARTIAL]" in content or len(content) == 200

def test_hard_limit_enforcement():
    cfg = {"soft_limit_bytes": 5000, "hard_limit_bytes": 300, "max_full_documents": 5}
    budget = ContextBudget(cfg)
    notes = [generate_note(1, 400, relevance=10), generate_note(2, 400, relevance=5)]
    with pytest.raises(BudgetExceededError):
        budget.apply_degradation(notes)


def test_injected_token_counter_is_used_for_budget_estimates():
    budget = ContextBudget({
        "hard_limit_tokens": 10,
        "tokenizer": lambda text: len(text.split()),
    })
    assert budget.estimate_tokens({"content": "one two three four"}) == 4


def test_compression_is_not_reported_as_llm_token_savings():
    text = "repeated " * 1000
    budget = ContextBudget({"soft_limit_bytes": 20000, "hard_limit_bytes": 100000})
    notes = [{"id": "n1", "content": text, "relevance": 1}]
    degraded = budget.apply_degradation(notes)
    assert isinstance(degraded[0]["content"], bytes)
    # The public token estimator sees the decompressed value after transport
    # conversion; compression itself is not a token-reduction mechanism.
    restored = zlib.decompress(degraded[0]["content"]).decode("utf-8")
    assert budget.estimate_tokens({"content": restored}) > 100
