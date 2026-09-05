"""Integration regression tests for the direct financial-ingestion write path."""

import json

import pytest

from memory_controller.financial_ingestion import FinancialSourceIngestionManager, SecretScrubber


class _Deduplicator:
    def register_note(self, note):
        return True, None


class _Storage:
    def __init__(self):
        self.records = {}

    def set(self, note_id, record):
        self.records[note_id] = record


def _manager(tmp_path, storage=None):
    manager = object.__new__(FinancialSourceIngestionManager)
    manager.vault_root = tmp_path
    manager.storage = storage
    manager.deduplicator = _Deduplicator()
    manager.scrubber = SecretScrubber()
    return manager


def _frontmatter(lifecycle="REVIEW", verification="unverified"):
    return {
        "id": "12345678-1234-5678-1234-567812345678",
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "financial",
        "tags": ["test"],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
    }


@pytest.mark.parametrize("lifecycle", [
    "ACTIVE", "VERIFIED", "ARCHIVED", "SUPERSEDED", "RECONSOLIDATING",
])
def test_persist_rejects_privileged_lifecycle_before_any_write(tmp_path, lifecycle):
    storage = _Storage()
    manager = _manager(tmp_path, storage)
    note = {
        "frontmatter": _frontmatter(lifecycle=lifecycle),
        "markdown": "CALLER-CONTROLLED",
        "content": "content",
    }

    with pytest.raises(ValueError):
        manager._persist_note(note, "notes/test.md")

    assert storage.records == {}
    assert not (tmp_path / "notes" / "test.md").exists()


def test_persist_rejects_verified_injection_before_any_write(tmp_path):
    storage = _Storage()
    manager = _manager(tmp_path, storage)
    note = {
        "frontmatter": _frontmatter(verification="verified"),
        "markdown": "CALLER-CONTROLLED",
        "content": "content",
    }

    with pytest.raises(ValueError):
        manager._persist_note(note, "notes/test.md")

    assert storage.records == {}
    assert not (tmp_path / "notes" / "test.md").exists()


def test_persist_normalizes_benign_input_for_storage_and_filesystem(tmp_path):
    storage = _Storage()
    manager = _manager(tmp_path, storage)
    original = _frontmatter(lifecycle="CLASSIFIED", verification="partially_verified")
    note = {
        "frontmatter": original,
        "markdown": "caller markdown must not be reused",
        "content": "canonical content",
    }

    manager._persist_note(note, "notes/test.md")

    stored = storage.records[original["id"]]
    assert stored["lifecycle"] == "REVIEW"
    assert stored["verification"] == "unverified"
    assert stored["content"] == "canonical content"

    persisted = (tmp_path / "notes" / "test.md").read_text(encoding="utf-8")
    assert '"lifecycle": "REVIEW"' in persisted
    assert '"verification": "unverified"' in persisted
    assert "CALLER-CONTROLLED" not in persisted
    json.loads(persisted.split("---", 2)[1])


def test_persist_does_not_mutate_caller_frontmatter(tmp_path):
    manager = _manager(tmp_path)
    original = _frontmatter(lifecycle="CLASSIFIED", verification="partially_verified")
    note = {"frontmatter": original, "content": "content"}

    manager._persist_note(note, "notes/test.md")

    assert original["lifecycle"] == "CLASSIFIED"
    assert original["verification"] == "partially_verified"
