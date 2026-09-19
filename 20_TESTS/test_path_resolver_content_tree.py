"""New notes go to the content tree; old notes stay where they are.

VAULT_STATE.md §5 recorded the defect: `storage/path_resolver.py` sent a `knowledge` note to
01_KNOWLEDGE while the corpus lives in 01_ARCHITECTURE/knowledge. A note proposed through the MCP
server would have landed in a second, legacy taxonomy. The destinations used now are where the
existing notes of each type already are; a vault without that tree keeps the legacy folders.
"""
from __future__ import annotations

import os
import sys
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_controller.storage.path_resolver import CONTENT_TREE_FOR_TYPE, resolve_path  # noqa: E402


def _note(note_type: str, category: str = "cat") -> dict:
    return {"id": str(uuid.uuid4()), "type": note_type, "category": category, "tags": [],
            "created": "2026-09-19", "updated": "2026-09-19",
            "provenance": {"source_type": "user", "source_ref": "test"}, "confidence": "high",
            "verification": "unverified", "relations": [], "lifecycle": "ACTIVE", "content": "corp\n"}


def _rel(vault: Path, path: str) -> str:
    return Path(path).relative_to(vault).as_posix().rsplit("/", 1)[0]


@pytest.fixture
def content_vault(tmp_path):
    vault = tmp_path / "vault"
    for folder in CONTENT_TREE_FOR_TYPE.values():
        (vault / folder).mkdir(parents=True, exist_ok=True)
    return vault


def test_the_destinations_are_where_the_real_corpus_already_is():
    for note_type, folder in CONTENT_TREE_FOR_TYPE.items():
        assert (REPO / folder).is_dir(), (note_type, folder)


@pytest.mark.parametrize("note_type,expected", sorted(CONTENT_TREE_FOR_TYPE.items()))
def test_a_vault_with_the_content_tree_writes_new_notes_there(content_vault, note_type, expected):
    assert _rel(content_vault, resolve_path(str(content_vault), _note(note_type))) == expected


def test_negative_control_a_vault_without_the_content_tree_keeps_the_legacy_folders(tmp_path):
    legacy = {"knowledge": "01_KNOWLEDGE", "procedure": "03_PROCEDURES", "project": "02_PROJECTS", "lesson": "04_MEMORY"}
    for note_type, folder in legacy.items():
        assert _rel(tmp_path, resolve_path(str(tmp_path), _note(note_type))) == folder


def test_prefer_content_tree_false_gives_the_legacy_destination(content_vault):
    assert _rel(content_vault, resolve_path(str(content_vault), _note("knowledge"), prefer_content_tree=False)) == "01_KNOWLEDGE"


def test_a_type_without_evidence_keeps_its_legacy_folder(content_vault):
    assert _rel(content_vault, resolve_path(str(content_vault), _note("decision"))) == "04_MEMORY"


def test_engine_writes_a_new_knowledge_note_into_the_content_tree(content_vault):
    engine = FileStorageEngine(str(content_vault))
    note = _note("knowledge", "nota-noua")
    engine.set(note["id"], note)
    assert _rel(content_vault, engine.id_to_path[note["id"]]) == "01_ARCHITECTURE/knowledge"
    assert not (content_vault / "01_KNOWLEDGE").exists()


def test_an_existing_content_tree_note_stays_exactly_where_it_is(content_vault):
    from memory_controller.storage.serializer import serialize
    note = _note("knowledge", "veche")
    path = content_vault / "01_ARCHITECTURE" / "knowledge" / "un_nume_care_nu_e_categoria.md"
    path.write_text(serialize(note), encoding="utf-8")
    engine = FileStorageEngine(str(content_vault))
    engine.set(note["id"], dict(engine.get(note["id"]), tags=["x"]))
    assert Path(engine.id_to_path[note["id"]]) == path
    assert len(list((content_vault / "01_ARCHITECTURE" / "knowledge").glob("*.md"))) == 1


def test_an_existing_legacy_note_is_not_moved_by_an_update(content_vault):
    """Existing notes stay where they are: an update must not relocate a legacy-tree note."""
    legacy_dir = content_vault / "01_KNOWLEDGE"
    legacy_dir.mkdir()
    note = _note("knowledge", "legacy")
    legacy_path = legacy_dir / f"legacy_{note['id'][:8]}.md"
    from memory_controller.storage.serializer import serialize
    legacy_path.write_text(serialize(note), encoding="utf-8")

    engine = FileStorageEngine(str(content_vault))
    assert Path(engine.id_to_path[note["id"]]) == legacy_path
    engine.set(note["id"], dict(engine.get(note["id"]), tags=["updated"]))
    assert Path(engine.id_to_path[note["id"]]).parent == legacy_dir
    assert not list((content_vault / "01_ARCHITECTURE" / "knowledge").glob("*.md"))
