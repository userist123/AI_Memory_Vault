"""Updating a note must never move it out of the canonical tree.

`resolve_path()` maps a note TYPE to the legacy tree (knowledge ->
01_KNOWLEDGE, procedure -> 03_PROCEDURES). `set()` writes to that path and
then deletes the note's previous file as a stale duplicate. Applied to a note
living in 01_ARCHITECTURE, that is silent data relocation: a copy appears in
the legacy tree and the canonical original is removed.

The path was inert only while the engine loaded zero notes — `id_to_path`
never held a canonical note, so the delete never fired. Fixing the engine's
blindness armed it against 738 real notes, which is what these tests pin.
"""
import os
import uuid

import pytest

from memory_controller.storage.file_engine import (
    CONTENT_ROOTS,
    LEGACY_WRITE_ROOTS,
    FileStorageEngine,
)


def _note(**over):
    n = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "sec",
        "tags": ["t"],
        "created": "2026-09-06",
        "updated": "2026-09-06",
        "confidence": "medium",
        "verification": "unverified",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "relations": [],
        "content": "body",
    }
    n.update(over)
    return n


@pytest.fixture
def vault(tmp_path):
    for folder in tuple(CONTENT_ROOTS) + tuple(LEGACY_WRITE_ROOTS):
        (tmp_path / folder).mkdir(parents=True, exist_ok=True)
    return str(tmp_path)


def _seed_canonical(vault, note, root="01_ARCHITECTURE"):
    """Place a note where the real corpus actually lives."""
    from memory_controller.storage.file_engine import serialize
    path = os.path.join(vault, root, "knowledge", f"{note['category']}_seed.md")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(serialize(note))
    return path


def test_updating_a_canonical_note_keeps_it_in_the_canonical_tree(vault):
    """THE REGRESSION: this is the data-loss case."""
    note = _note()
    seeded = _seed_canonical(vault, note)
    engine = FileStorageEngine(vault)
    assert note["id"] in engine.id_to_path

    note["content"] = "updated body"
    engine.set(note["id"], note)

    final = engine.id_to_path[note["id"]]
    assert "01_ARCHITECTURE" in final, f"note escaped the canonical tree: {final}"
    assert "01_KNOWLEDGE" not in final
    assert os.path.exists(final)
    assert engine.get(note["id"])["content"] == "updated body"


def test_the_canonical_original_is_not_deleted(vault):
    note = _note()
    seeded = _seed_canonical(vault, note)
    engine = FileStorageEngine(vault)

    note["content"] = "updated"
    engine.set(note["id"], note)

    assert os.path.exists(seeded), "the canonical file was deleted by an update"


def test_a_canonical_note_keeps_its_file_name_when_its_category_changes(vault):
    """In the canonical tree the file name is the note's graph identity:
    Obsidian and VaultIndex.by_slug both resolve `[[links]]` by file name, so
    renaming on a category change would silently break every inbound link.
    Legacy notes keep the old rename behaviour; canonical ones do not."""
    note = _note(category="old_title")
    _seed_canonical(vault, note)
    engine = FileStorageEngine(vault)
    before = engine.id_to_path[note["id"]]

    note["category"] = "new_title"
    engine.set(note["id"], note)
    after = engine.id_to_path[note["id"]]

    assert before == after
    assert os.path.exists(after)
    assert engine.get(note["id"])["category"] == "new_title"


def test_a_new_note_still_lands_in_the_legacy_tree(vault):
    """The write taxonomy is a pending migration decision, not something this
    fix quietly changes."""
    engine = FileStorageEngine(vault)
    note = _note()
    engine.set(note["id"], note)
    assert "01_KNOWLEDGE" in engine.id_to_path[note["id"]]


def test_a_legacy_note_keeps_legacy_relocation_behaviour(vault):
    """Notes already in the legacy tree are unaffected by the pin."""
    engine = FileStorageEngine(vault)
    note = _note(category="first")
    engine.set(note["id"], note)
    first = engine.id_to_path[note["id"]]

    note["category"] = "second"
    engine.set(note["id"], note)
    second = engine.id_to_path[note["id"]]

    assert first != second
    assert not os.path.exists(first)
    assert "01_KNOWLEDGE" in second


def test_type_change_does_not_drag_a_canonical_note_into_the_legacy_tree(vault):
    """Even a change that would re-map the type must not relocate the file."""
    note = _note(type="knowledge")
    _seed_canonical(vault, note)
    engine = FileStorageEngine(vault)

    note["type"] = "procedure"          # resolve_path would say 03_PROCEDURES
    engine.set(note["id"], note)

    final = engine.id_to_path[note["id"]]
    assert "01_ARCHITECTURE" in final
    assert "03_PROCEDURES" not in final
