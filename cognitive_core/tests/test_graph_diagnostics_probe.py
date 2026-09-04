"""Tests for Graph Diagnostics Probe (Antigravity A10).

Validates non-invasive detection of:
- Silent graph exceptions (GAP-014, GAP-012)
- Storage engine .store attribute presence
- Candidate rejection attribution (GAP-001)
"""
import os
import tempfile
import pytest

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine
from cognitive_core.observability.graph_diagnostics_probe import (
    GraphDiagnosticsProbe,
    GraphExecutionStatus,
    RejectionReason,
)


@pytest.fixture
def sample_notes():
    return [
        {
            "id": "NOTE-001",
            "type": "procedure",
            "category": "backend",
            "tags": ["python", "async"],
            "created": "2026-09-01T00:00:00Z",
            "updated": "2026-09-01T00:00:00Z",
            "source_type": "execution",
            "source_ref": "test_ref",
            "confidence": "high",
            "verification": "verified",
            "relations": [],
            "provenance": {"source_type": "execution"},
            "content": "Python async await patterns with asyncio event loop",
            "raw_json": "{}",
            "metadata": {"title": "Async Python"},
            "entities": ["asyncio", "loop"],
            "lifecycle": "ACTIVE",
        },
        {
            "id": "NOTE-002",
            "type": "knowledge",
            "category": "backend",
            "tags": ["python", "async", "concurrency"],
            "created": "2026-09-01T00:00:00Z",
            "updated": "2026-09-01T00:00:00Z",
            "source_type": "execution",
            "source_ref": "test_ref",
            "confidence": "high",
            "verification": "verified",
            "relations": [],
            "provenance": {"source_type": "execution"},
            "content": "Asyncio concurrency and task pools in Python backend services",
            "raw_json": "{}",
            "metadata": {"title": "Asyncio Pools"},
            "entities": ["asyncio", "tasks"],
            "lifecycle": "ACTIVE",
        },
        {
            "id": "NOTE-RAW-001",
            "type": "resource",
            "category": "inbox",
            "tags": ["python"],
            "created": "2026-09-01T00:00:00Z",
            "updated": "2026-09-01T00:00:00Z",
            "source_type": "unknown",
            "source_ref": "test_ref",
            "confidence": "low",
            "verification": "unverified",
            "relations": [],
            "provenance": {"source_type": "unknown"},
            "content": "Raw unparsed snippet about python asyncio",
            "raw_json": "{}",
            "metadata": {"title": "Raw Snippet"},
            "entities": ["raw"],
            "lifecycle": "RAW",
        },
        {
            "id": "NOTE-SUP-001",
            "type": "procedure",
            "category": "backend",
            "tags": ["python", "sockets"],
            "created": "2026-09-01T00:00:00Z",
            "updated": "2026-09-01T00:00:00Z",
            "source_type": "execution",
            "source_ref": "test_ref",
            "confidence": "medium",
            "verification": "verified",
            "relations": [],
            "provenance": {"source_type": "execution"},
            "content": "Deprecated synchronous socket server in python",
            "raw_json": "{}",
            "metadata": {"title": "Old Socket Server"},
            "entities": ["socket"],
            "lifecycle": "SUPERSEDED",
        },
    ]


def test_probe_in_memory_engine(sample_notes):
    storage = StorageEngine()
    for note in sample_notes:
        storage.set(note["id"], note)

    controller = MemoryController(storage=storage)
    report = GraphDiagnosticsProbe.probe_ranked_search(
        controller, Principal.AI_AGENT, "python asyncio concurrency"
    )

    assert "StorageEngine" in report.storage_engine
    assert report.has_store_attribute is True
    assert report.exception_type is None
    assert report.base_count > 0
    assert report.ranked_count > 0
    assert report.status in [GraphExecutionStatus.APPLIED, GraphExecutionStatus.FALLBACK_NO_GRAPH_CHANGES]


def test_probe_sqlite_engine_silent_exception(sample_notes):
    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as tmp:
        db_path = tmp.name

    storage = None
    try:
        storage = SQLiteStorageEngine(db_path=db_path)
        for note in sample_notes:
            storage.set(note["id"], note)

        controller = MemoryController(storage=storage)
        report = GraphDiagnosticsProbe.probe_ranked_search(
            controller, Principal.AI_AGENT, "python asyncio concurrency"
        )

        assert report.storage_engine == "SQLiteStorageEngine"
        assert report.has_store_attribute is False
        assert report.status == GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION
        assert report.exception_type == "AttributeError"
        assert "has no attribute 'store'" in (report.exception_message or "")
        assert report.exception_traceback is not None
        # Proves that ranked_search returned base results despite exception
        assert report.ranked_count == report.base_count
    finally:
        if storage is not None:
            storage.close()
        if os.path.exists(db_path):
            try:
                os.remove(db_path)
            except OSError:
                pass


def test_probe_file_engine_silent_exception(sample_notes):
    with tempfile.TemporaryDirectory() as tmp_dir:
        storage = FileStorageEngine(vault_root=tmp_dir)
        for note in sample_notes:
            storage.set(note["id"], note)

        controller = MemoryController(storage=storage)
        report = GraphDiagnosticsProbe.probe_ranked_search(
            controller, Principal.AI_AGENT, "python asyncio concurrency"
        )

        assert report.storage_engine == "FileStorageEngine"
        assert report.has_store_attribute is False
        assert report.status == GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION
        assert report.exception_type == "AttributeError"
        assert "has no attribute 'store'" in (report.exception_message or "")


def test_probe_candidate_rejections(sample_notes, monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test-secret-for-pagination-token-signing-12345")
    storage = StorageEngine()
    notes = list(sample_notes)
    notes.append({
        "id": "NOTE-IRRELEVANT-001",
        "type": "recipe" if hasattr(Principal, "HUMAN") else "knowledge",
        "category": "culinary",
        "tags": ["cooking", "pasta"],
        "created": "2026-09-01T00:00:00Z",
        "updated": "2026-09-01T00:00:00Z",
        "source_type": "execution",
        "source_ref": "test_ref",
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "provenance": {"source_type": "execution"},
        "content": "Cooking authentic Italian pasta carbonara with guanciale",
        "raw_json": "{}",
        "metadata": {"title": "Pasta Carbonara"},
        "entities": ["pasta"],
        "lifecycle": "ACTIVE",
    })
    for note in notes:
        storage.set(note["id"], note)

    controller = MemoryController(storage=storage)
    # page_size=2 ensures only top 2 notes admitted; lower scored notes cut off
    rejection_report = GraphDiagnosticsProbe.probe_candidate_rejection(
        controller, Principal.AI_AGENT, "python asyncio concurrency", target_notes=notes, page_size=2
    )

    assert rejection_report.total_scanned == 5
    assert rejection_report.admitted_count == 2
    assert rejection_report.rejected_count == 3

    rejection_reasons = {r.rejection_reason for r in rejection_report.rejections}
    assert RejectionReason.LIFECYCLE_RAW_EXCLUDED.value in rejection_reasons
    assert RejectionReason.SUPERSEDED_INACTIVE.value in rejection_reasons
    assert RejectionReason.SCORE_BELOW_THRESHOLD.value in rejection_reasons
