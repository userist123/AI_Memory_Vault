"""ContextPackBuilder must execute the requested disclosure stage explicitly."""

import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.insert(0, str(PACKAGES))

from retrieval.context.pack_builder import ContextPackBuilder


ITEM = {
    "id": "n1",
    "content": "alpha beta gamma " * 30,
    "relevance": 1,
    "verification": {"status": "TRUSTED", "scanner_verdict": "ALLOW"},
    "security": {"trust_state": "TRUSTED"},
    "provenance": {"verified": True},
    "requirements": ["keep boundary"],
    "forbidden": ["bypass"],
    "acceptance": ["preserve evidence"],
}


def build(level, **kwargs):
    return ContextPackBuilder().build(
        request_id="disclosure-test",
        agent_id="default",
        budget={"max_notes": 1, "max_full_documents": 1, "soft": 4096, "hard": 8192, "soft_tokens": 1000, "hard_tokens": 2000},
        results=[dict(ITEM)],
        disclosure_level=level,
        **kwargs,
    )


def test_snippet_disclosure_is_applied_and_retains_security():
    result = build("snippet")
    item = result["results"][0]
    assert "snippet" in item
    assert "content" not in item
    assert item["verification"]["status"] == "TRUSTED"


def test_sections_require_explicit_query():
    try:
        build("sections")
    except ValueError as exc:
        assert str(exc) == "sections disclosure requires disclosure_query"
    else:
        raise AssertionError("sections must not guess a query")


def test_unknown_disclosure_level_fails_closed():
    try:
        build("invented")
    except ValueError as exc:
        assert str(exc) == "unknown disclosure level: invented"
    else:
        raise AssertionError("unknown disclosure levels must fail closed")


def test_sections_disclosure_matches_lines_and_retains_security():
    item = dict(ITEM)
    item["content"] = "architecture\nsecurity boundary\ntoken economy\n"
    result = ContextPackBuilder().build(
        request_id="sections-test",
        agent_id="default",
        budget={"max_notes": 1, "max_full_documents": 1, "soft": 4096, "hard": 8192, "soft_tokens": 1000, "hard_tokens": 2000},
        results=[item],
        disclosure_level="sections",
        disclosure_query="token economy",
    )
    entry = result["results"][0]
    assert entry["sections"] == ["token economy"]
    assert entry["verification"]["status"] == "TRUSTED"

def test_context_budget_accepts_injected_tokenizer():
    from retrieval.context.budget import ContextBudget

    budget = ContextBudget({"hard_limit_tokens": 5, "tokenizer": lambda text: len(text.split())})
    assert budget.estimate_tokens({"content": "one two three"}) == 4


def test_context_pack_records_adaptive_compression():
    item = dict(ITEM)
    item["content"] = ("irrelevant history " * 120) + "\nMUST NOT bypass verification.\n" + ("token economy " * 120)
    result = ContextPackBuilder().build(
        request_id="compression-test",
        agent_id="default",
        budget={"max_notes": 1, "max_full_documents": 1, "soft": 65536, "hard": 131072,
                "soft_tokens": 120, "hard_tokens": 240},
        results=[item],
        disclosure_level="full_document",
        query="token economy",
    )
    entry = result["results"][0]
    assert entry["compression"]["action"] in {"COMPRESS", "FALLBACK"}
    assert entry["verification"]["status"] == "TRUSTED"
    assert "MUST NOT bypass verification." in entry["content"]


def test_context_pack_allows_safe_no_op():
    item = dict(ITEM)
    item["content"] = "short context"
    result = ContextPackBuilder().build(
        request_id="noop-test",
        agent_id="default",
        budget={"max_notes": 1, "max_full_documents": 1, "soft": 4096, "hard": 8192,
                "soft_tokens": 1000, "hard_tokens": 2000},
        results=[item],
        disclosure_level="full_document",
    )
    assert result["results"][0]["compression"]["action"] == "NO_OP"
    assert result["results"][0]["content"] == "short context"
