import json
import zlib
import copy
import pytest
from memory_controller.context.budget import (
    ContextBudget,
    BudgetExceededError,
    ContextBudgetError,
    load_agent_budget,
)


def generate_note(id_suffix: int, size: int, relevance: int = 0, content_prefix: str = "a"):
    content = content_prefix * size
    return {"id": f"note_{id_suffix}", "content": content, "relevance": relevance}


def test_context_budget_initialization_defaults():
    cb = ContextBudget({})
    assert cb.max_notes == 50
    assert cb.max_full_documents == 3
    assert cb.soft_limit_bytes == 16 * 1024
    assert cb.hard_limit_bytes == 32 * 1024
    assert cb.soft_context_budget == 16 * 1024
    assert cb.hard_context_budget == 32 * 1024


def test_context_budget_legacy_aliases():
    cfg = {"soft_context_budget": 1000, "hard_context_budget": 2000, "max_full_documents": 2}
    cb = ContextBudget(cfg)
    assert cb.soft_limit_bytes == 1000
    assert cb.hard_limit_bytes == 2000
    assert cb.soft_context_budget == 1000
    assert cb.hard_context_budget == 2000

    # check_budget alias
    cb.check_budget(1500)
    with pytest.raises(ContextBudgetError):
        cb.check_budget(2500)

    # check_hard_limit
    cb.check_hard_limit(1500)
    with pytest.raises(BudgetExceededError):
        cb.check_hard_limit(2500)


def test_tier1_no_degradation():
    cfg = {"soft_limit_bytes": 5000, "hard_limit_bytes": 10000, "max_full_documents": 5}
    cb = ContextBudget(cfg)
    notes = [
        generate_note(1, 100, relevance=10),
        generate_note(2, 100, relevance=5),
    ]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert len(res) == 2
    assert res[0]["content"] == "a" * 100
    assert res[1]["content"] == "a" * 100


def test_tier2_max_full_documents_enforcement():
    cfg = {"soft_limit_bytes": 10000, "hard_limit_bytes": 20000, "max_full_documents": 2}
    cb = ContextBudget(cfg)
    notes = [
        generate_note(1, 50, relevance=10),
        generate_note(2, 50, relevance=30),
        generate_note(3, 50, relevance=20),
        generate_note(4, 50, relevance=5),
    ]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert len(res) == 4
    # Highest relevance: note_2 (30), note_3 (20) stay full
    assert res[0]["id"] == "note_2" and res[0]["content"] == "a" * 50
    assert res[1]["id"] == "note_3" and res[1]["content"] == "a" * 50
    # Lower relevance: note_1 (10), note_4 (5) downgraded to metadata only
    assert res[2]["id"] == "note_1" and res[2]["content"] == ""
    assert res[3]["id"] == "note_4" and res[3]["content"] == ""


def test_tier3_soft_budget_truncation_to_partial():
    # Soft limit accommodates truncated 50 chars + 12 chars marker = 62 bytes
    cfg = {"soft_limit_bytes": 80, "hard_limit_bytes": 500, "max_full_documents": 1}
    cb = ContextBudget(cfg)
    notes = [generate_note(1, 200, relevance=10)]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert len(res) == 1
    assert res[0]["content"] == ("a" * 50) + "...[PARTIAL]"
    assert cb._size_of(res[0]) == 62


def test_tier3_soft_budget_downgrade_to_metadata_only():
    # Soft limit too tight for 62 bytes marker
    cfg = {"soft_limit_bytes": 30, "hard_limit_bytes": 500, "max_full_documents": 1}
    cb = ContextBudget(cfg)
    notes = [generate_note(1, 200, relevance=10)]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert len(res) == 1
    assert res[0]["content"] == ""
    assert cb._size_of(res[0]) == 0


def test_tier4_zlib_compression_for_large_notes():
    cfg = {"soft_limit_bytes": 50000, "hard_limit_bytes": 100000, "max_full_documents": 3}
    cb = ContextBudget(cfg)
    large_payload = "Important knowledge context payload " * 100  # > 1024 bytes
    notes = [{"id": "large_1", "content": large_payload, "relevance": 10}]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert isinstance(res[0]["content"], bytes)
    decompressed = zlib.decompress(res[0]["content"]).decode("utf-8")
    assert decompressed == large_payload


def test_tier5_hard_limit_breach():
    cfg = {"soft_limit_bytes": 5000, "hard_limit_bytes": 50, "max_full_documents": 2}
    cb = ContextBudget(cfg)
    notes = [generate_note(1, 200, relevance=10)]
    with pytest.raises(BudgetExceededError):
        cb.apply_degradation(notes)


def test_edge_cases_empty_list():
    cb = ContextBudget({})
    assert cb.apply_degradation([]) == []


def test_edge_cases_utf8_multibyte():
    cjk_text = "核心架构记忆索引" * 10  # 80 chars, 240 bytes
    cb = ContextBudget({"soft_limit_bytes": 100, "hard_limit_bytes": 500, "max_full_documents": 1})
    notes = [{"id": "cjk", "content": cjk_text, "relevance": 10}]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert len(res) == 1
    # 50 CJK chars (150 bytes) + 12 byte marker = 162 bytes > soft_limit_bytes (100) -> downgraded to ""
    assert res[0]["content"] == ""


def test_edge_cases_precompressed_content():
    payload = zlib.compress(b"binary data payload " * 20)
    cb = ContextBudget({"soft_limit_bytes": 5000, "hard_limit_bytes": 10000, "max_full_documents": 2})
    notes = [{"id": "bin", "content": payload, "relevance": 10}]
    res = cb.apply_degradation(copy.deepcopy(notes))
    assert res[0]["content"] == payload
    assert cb._size_of(res[0]) == len(payload)


def test_enforce_max_full_standalone():
    cb = ContextBudget({"max_full_documents": 1})
    notes = [
        {"id": "1", "content": "alpha", "relevance": 1},
        {"id": "2", "content": "beta", "relevance": 10},
    ]
    res = cb.enforce_max_full(copy.deepcopy(notes))
    assert res[0]["id"] == "2" and res[0]["content"] == "beta"
    assert res[1]["id"] == "1" and res[1]["content"] == ""


def test_load_agent_budget_fallback(tmp_path):
    # Non-existent file falls back to defaults
    cb = load_agent_budget("router", str(tmp_path / "non_existent.json"))
    assert cb.max_notes == 50

    # Valid config
    cfg_file = tmp_path / "budgets.json"
    cfg_file.write_text(json.dumps({"router": {"soft_limit_bytes": 2048, "max_full_documents": 2}}), encoding="utf-8")
    cb2 = load_agent_budget("router", str(cfg_file))
    assert cb2.soft_limit_bytes == 2048
    assert cb2.max_full_documents == 2
