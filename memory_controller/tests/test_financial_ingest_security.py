import pytest

import memory_controller.financial_query as financial_query
from memory_controller.authorizer import Principal
from memory_controller.financial_query import FinancialQueryEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


class _DenyAuthorizer:
    def is_allowed(self, principal, operation):
        return False


class _IndexStub:
    def index_note(self, note):
        self.note = note


def test_direct_financial_ingest_requires_propose_authorization():
    engine = FinancialQueryEngine(SQLiteStorageEngine(":memory:"), authorizer=_DenyAuthorizer())
    with pytest.raises(PermissionError):
        engine.ingest_financial_note({}, Principal.AI_AGENT)


def test_direct_financial_ingest_rejects_verified_injection():
    engine = FinancialQueryEngine(SQLiteStorageEngine(":memory:"))
    with pytest.raises(ValueError, match="cannot be set via direct financial ingest"):
        engine.ingest_financial_note({"verification": "verified"}, Principal.AI_AGENT)


def test_direct_financial_ingest_forces_review_and_unverified(monkeypatch):
    monkeypatch.setattr(financial_query.jsonschema, "validate", lambda **kwargs: None)
    engine = FinancialQueryEngine(SQLiteStorageEngine(":memory:"))
    engine.search_engine = _IndexStub()

    note_id = engine.ingest_financial_note(
        {
            "lifecycle": "ACTIVE",
            "tags": ["finance"],
            "confidence": "high",
            "verification": "partially_verified",
        },
        Principal.AI_AGENT,
    )

    stored = engine.storage.get(note_id)
    assert stored["lifecycle"] == "REVIEW"
    assert stored["verification"] == "unverified"
