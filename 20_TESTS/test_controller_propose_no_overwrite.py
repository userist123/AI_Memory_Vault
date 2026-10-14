"""A-08: propose() must not overwrite an existing note.

It used to call storage.set() for any id, so a second proposal with the id of an
existing (non-ACTIVE) note silently replaced it, provenance included: an AI agent
could rewrite a HUMAN's REVIEW note and stamp its own provenance on it. Re-saves go
through update(), which has its own rules.
"""
from __future__ import annotations

import uuid
from pathlib import Path

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from memory_controller.storage.file_engine import FileStorageEngine
from memory_vault_fixture import make_vault


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_a08_propose_32bytes_long_")


def _note(note_id, content, source_type):
    return {"id": note_id, "type": "knowledge", "category": "a08", "lifecycle": "REVIEW",
            "content": content, "provenance": {"source_type": source_type, "source_ref": "a08"}}


def test_ai_agent_cannot_overwrite_a_human_review_note(tmp_path):
    engine = FileStorageEngine(str(make_vault(tmp_path)))
    ctl = MemoryController(engine)
    nid = str(uuid.uuid4())
    ctl.propose(Principal.HUMAN, _note(nid, "the owner's own words", "user"))
    path = Path(engine.id_to_path[nid])
    before = path.read_bytes()

    with pytest.raises((ValueError, PermissionError), match="already exists"):
        ctl.propose(Principal.AI_AGENT, _note(nid, "rewritten by an agent", "ai"))
    assert path.read_bytes() == before
    assert ctl.storage.get(nid)["provenance"]["source_type"] == "user"


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.ADMIN])
def test_no_principal_can_repropose_an_existing_id(tmp_path, principal):
    engine = FileStorageEngine(str(make_vault(tmp_path)))
    ctl = MemoryController(engine)
    nid = str(uuid.uuid4())
    ctl.propose(Principal.HUMAN, _note(nid, "first", "user"))
    before = Path(engine.id_to_path[nid]).read_bytes()
    with pytest.raises((ValueError, PermissionError), match="already exists"):
        ctl.propose(principal, _note(nid, "second", "user"))
    assert Path(engine.id_to_path[nid]).read_bytes() == before


def test_a_fresh_id_is_still_accepted(tmp_path):
    ctl = MemoryController(FileStorageEngine(str(make_vault(tmp_path))))
    nid = str(uuid.uuid4())
    assert ctl.propose(Principal.AI_AGENT, _note(nid, "new", "ai")) == nid


def test_an_indexed_but_unreadable_note_is_still_not_a_free_id(tmp_path):
    """R-09: `storage.get()` returns None for an indexed file that became unreadable; propose()
    must still refuse that id rather than overwrite the file."""
    import sys
    from pathlib import Path
    REPO = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
    from memory_controller.authorizer import Principal
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine
    import importlib.util
    spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
    fx = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fx)
    vault = fx.make_vault(tmp_path)
    engine = FileStorageEngine(str(vault))
    note_id, path = next(iter(engine.id_to_path.items()))
    Path(path).write_bytes(b"---\nid: [broken\n---\n")  # now unreadable, still indexed
    assert engine.get(note_id) is None
    ctrl = MemoryController(engine)
    before = Path(path).read_bytes()
    with pytest.raises(ValueError, match="already exists"):
        ctrl.propose(Principal.HUMAN, {"id": note_id, "type": "knowledge", "lifecycle": "REVIEW",
                                       "title": "x", "content": "x",
                                       "provenance": {"source_type": "user", "source_ref": "t"}})
    assert Path(path).read_bytes() == before
