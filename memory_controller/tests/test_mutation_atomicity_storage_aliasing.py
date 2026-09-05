"""Mutation atomicity / storage aliasing regression tests (runtime security
front, owner: claude-code).

Root cause fixed: StorageEngine.get()/set() (in-memory) and
FileStorageEngine.get()/set() previously returned/stored notes via shallow
copy (`dict(x)` / `x.copy()`), so nested fields (`provenance`, `relations`)
were shared by reference between the caller, the storage engine's internal
cache, and -- in MemoryController.supersede() -- between a note and its own
"rollback" backup. A caller mutating a nested field in place before an
operation failed could corrupt storage with zero write ever occurring, and
supersede()'s rollback path was provably a no-op for `relations` whenever the
note already had a non-empty relations list, because `old_note.copy()` (a
shallow copy) still shared the very list object that
`old_note.setdefault("relations", []).append(...)` mutated in place.

Scenario required by the runtime-security brief:
    get note -> mutate local -> validation fails -> storage state MUST
    remain byte-for-byte equivalent
"""
from __future__ import annotations

import copy
import uuid

import pytest

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from memory_controller.storage.file_engine import FileStorageEngine


def _note(note_id=None, **overrides):
    note_id = note_id or str(uuid.uuid4())
    base = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "category": "test",
        "tags": [],
        "created": "2026-01-01",
        "updated": "2026-01-01",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())}],
        "content": "body",
    }
    base.update(overrides)
    return base


class TestInMemoryStorageEngineDeepCopy:
    def test_get_returns_independent_copy_not_internal_reference(self):
        storage = StorageEngine()
        storage.set("a", _note("a"))
        note = storage.get("a")
        note["relations"].append({"relation": "hacked", "target": "x", "target_id": "y"})
        note["provenance"]["source_type"] = "tampered"

        fresh = storage.get("a")
        assert fresh["relations"] == [
            {"relation": "related_to", "target": "knowledge", "target_id": fresh["relations"][0]["target_id"]}
        ]
        assert fresh["provenance"]["source_type"] == "user"

    def test_set_stores_independent_copy_not_caller_reference(self):
        storage = StorageEngine()
        data = _note("a")
        storage.set("a", data)
        data["relations"].append({"relation": "hacked", "target": "x", "target_id": "y"})
        data["provenance"]["source_type"] = "tampered"

        stored = storage.get("a")
        assert len(stored["relations"]) == 1
        assert stored["provenance"]["source_type"] == "user"


class TestFileStorageEngineDeepCopy:
    def test_get_returns_independent_copy(self, tmp_path):
        for folder in ["00_CORE", "01_KNOWLEDGE"]:
            (tmp_path / folder).mkdir(parents=True, exist_ok=True)
        storage = FileStorageEngine(str(tmp_path))
        storage.set("a", _note("a"))
        note = storage.get("a")
        note["relations"].append({"relation": "hacked", "target": "x", "target_id": "y"})

        fresh = storage.get("a")
        assert len(fresh["relations"]) == 1

    def test_set_stores_independent_copy(self, tmp_path):
        for folder in ["00_CORE", "01_KNOWLEDGE"]:
            (tmp_path / folder).mkdir(parents=True, exist_ok=True)
        storage = FileStorageEngine(str(tmp_path))
        data = _note("a")
        storage.set("a", data)
        data["relations"].append({"relation": "hacked", "target": "x", "target_id": "y"})

        stored = storage.get("a")
        assert len(stored["relations"]) == 1


class TestGetMutateValidateAbortNeverCorruptsStorage:
    """The exact scenario required by the brief: get -> mutate local ->
    validation fails -> storage state MUST remain byte-for-byte equivalent."""

    def test_update_with_invalid_lifecycle_transition_leaves_storage_untouched(self):
        storage = StorageEngine()
        controller = MemoryController(storage)
        note_id = str(uuid.uuid4())
        controller.propose(Principal.HUMAN, _note(note_id, lifecycle=Lifecycle.RAW.value,
                                                    provenance={"source_type": "user", "source_ref": "u"}))
        before = copy.deepcopy(storage.get(note_id))

        # Attempt an update whose payload fails schema validation (invalid enum value).
        with pytest.raises(Exception):
            controller.update(Principal.AI_AGENT, note_id, {"confidence": "not-a-real-confidence-level"})

        after = storage.get(note_id)
        assert after == before, "storage must be byte-for-byte equivalent after a failed validation"

    def test_attest_with_empty_reason_leaves_storage_untouched(self):
        storage = StorageEngine()
        controller = MemoryController(storage)
        note_id = str(uuid.uuid4())
        controller.propose(Principal.HUMAN, _note(note_id, provenance={"source_type": "user", "source_ref": "u"}))
        before = copy.deepcopy(storage.get(note_id))

        with pytest.raises(ValueError):
            controller.attest(Principal.HUMAN, note_id, "", "evidence-ref")

        after = storage.get(note_id)
        assert after == before


class TestSupersedeRollbackIsGenuinelyAtomic:
    """Proves the exact bug: before the deep-copy fix, a note with a
    pre-existing non-empty `relations` list would survive supersede()'s
    rollback with a phantom 'replaced_by'/'replaces' entry still present,
    because the rollback snapshot shared the same list object."""

    def test_rollback_restores_relations_list_exactly_when_second_write_fails(self, monkeypatch):
        storage = StorageEngine()
        controller = MemoryController(storage)

        old_id, new_id = str(uuid.uuid4()), str(uuid.uuid4())
        controller.propose(Principal.HUMAN, _note(old_id, provenance={"source_type": "user", "source_ref": "u"},
                                                    relations=[{"relation": "related_to", "target": "knowledge",
                                                                "target_id": str(uuid.uuid4())}]))
        controller.propose(Principal.HUMAN, _note(new_id, provenance={"source_type": "user", "source_ref": "u"}))

        before_old = copy.deepcopy(storage.get(old_id))

        original_set = storage.set
        calls = {"n": 0}

        def flaky_set(note_id, data):
            calls["n"] += 1
            # Let the FIRST set() (old_id, mutated) succeed, force the SECOND
            # set() (new_id) to fail, triggering the rollback branch.
            if calls["n"] == 2:
                raise RuntimeError("simulated storage failure on second write")
            return original_set(note_id, data)

        monkeypatch.setattr(storage, "set", flaky_set)

        with pytest.raises(ValueError, match="Atomic supersession write failed"):
            controller.supersede(Principal.HUMAN, old_id, new_id)

        monkeypatch.setattr(storage, "set", original_set)
        after_old = storage.get(old_id)
        assert after_old == before_old, (
            "supersede() rollback must restore the exact pre-mutation state, "
            "including the relations list -- not a version with a leftover "
            "'replaced_by' entry appended in place"
        )
        assert after_old["lifecycle"] == before_old["lifecycle"]
        assert len(after_old["relations"]) == len(before_old["relations"])
