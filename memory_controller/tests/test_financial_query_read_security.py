"""Regression tests for the financial-query read trust boundary."""

import pytest

from memory_controller.authorizer import Operation, Principal
from memory_controller.financial_query import FinancialQueryEngine


class _Storage:
    def __init__(self, records):
        self.records = records

    def get(self, note_id):
        return self.records.get(note_id)


class _SearchEngine:
    def __init__(self, results):
        self.results = results
        self.calls = 0

    def search(self, query, top_k=10, **kwargs):
        self.calls += 1
        return list(self.results)


class _Authorizer:
    def __init__(self, allowed=True):
        self.allowed = allowed
        self.calls = []

    def is_allowed(self, principal, operation):
        self.calls.append((principal, operation))
        return self.allowed


def _engine(records, results, *, allowed=True):
    engine = object.__new__(FinancialQueryEngine)
    engine.storage = _Storage(records)
    engine.search_engine = _SearchEngine(results)
    engine.authorizer = _Authorizer(allowed=allowed)
    return engine


def _record(note_id, lifecycle, verification):
    return {
        "id": note_id,
        "lifecycle": lifecycle,
        "verification": verification,
        "frontmatter": {
            "lifecycle": lifecycle,
            "verification": verification,
        },
        "content": {"symbol": "TEST"},
    }


def test_financial_search_exposes_only_active_verified_records():
    active = _record("active", "ACTIVE", "verified")
    candidates = [
        active,
        _record("review", "REVIEW", "unverified"),
        _record("raw", "RAW", "unverified"),
        _record("archived", "ARCHIVED", "verified"),
        _record("unverified", "ACTIVE", "unverified"),
    ]
    engine = _engine({r["id"]: r for r in candidates}, candidates)

    results = engine.search("earnings", principal=Principal.AI_AGENT)

    assert [r["id"] for r in results] == ["active"]
    assert engine.search_engine.calls == 1
    assert engine.authorizer.calls == [(Principal.AI_AGENT, Operation.SEARCH)]


def test_financial_search_rejects_unknown_principal_before_retrieval():
    engine = _engine({}, [])

    with pytest.raises(PermissionError, match="Invalid financial search principal"):
        engine.search("earnings", principal="unknown")

    assert engine.search_engine.calls == 0


def test_financial_search_rejects_unauthorized_principal_before_retrieval():
    engine = _engine({}, [], allowed=False)

    with pytest.raises(PermissionError, match="not allowed to search financial notes"):
        engine.search("earnings", principal=Principal.AI_AGENT)

    assert engine.search_engine.calls == 0


def test_financial_search_defaults_to_ai_agent_for_backward_compatibility():
    active = _record("active", "ACTIVE", "verified")
    engine = _engine({"active": active}, [active])

    results = engine.search("earnings")

    assert [r["id"] for r in results] == ["active"]
    assert engine.authorizer.calls == [(Principal.AI_AGENT, Operation.SEARCH)]
