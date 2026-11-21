"""FileStorageEngine writes notes with LF on every platform (.gitattributes: `* text=auto eol=lf`).

Before: the file was opened in text mode, so on Windows every `\n` became `\r\n` and a body that
already held `\r\n` was stored as `\r\r\n`, read back as a blank line. An owner-attested body then
no longer matched its `attested_content_sha256` after a reload, and promote refused it (security
review of PR #398, follow-up 1).
"""
from __future__ import annotations

import os
import sys
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(REPO / "20_TESTS"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_vault_fixture import make_vault  # noqa: E402


def _note(body: str) -> dict:
    return {
        "id": str(uuid.uuid4()), "type": "knowledge", "category": "test", "tags": ["t"],
        "created": "2026-11-21", "updated": "2026-11-21",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high", "verification": "unverified", "relations": [], "content": body,
    }


def test_note_files_are_written_with_lf_only(tmp_path):
    vault = make_vault(tmp_path)
    c = MemoryController(storage=FileStorageEngine(str(vault)))
    note = _note("line one\nline two\r\nline three\rline four\n")
    c.propose(Principal.HUMAN, note)
    raw = Path(c.storage.id_to_path[note["id"]]).read_bytes()
    assert b"\r" not in raw
    reloaded = FileStorageEngine(str(vault)).get(note["id"])
    assert reloaded["content"].splitlines() == ["line one", "line two", "line three", "line four"]


def test_attested_crlf_body_still_promotes_after_a_reload(tmp_path):
    vault = make_vault(tmp_path)
    c = MemoryController(storage=FileStorageEngine(str(vault)))
    note = _note("crlf\r\nbody\r\n")
    c.propose(Principal.HUMAN, note)
    c.attest(Principal.HUMAN, note["id"], verification_reason="read", evidence_reference="test")
    fresh = MemoryController(storage=FileStorageEngine(str(vault)))
    fresh.review(Principal.HUMAN, note["id"], "approve")
    fresh.promote(Principal.HUMAN, note["id"])
    assert str(fresh.storage.get(note["id"])["lifecycle"]).endswith("ACTIVE")


def test_cache_after_set_matches_the_file(tmp_path):
    """Review follow-up: get() right after set() must return what a reload returns."""
    vault = make_vault(tmp_path)
    engine = FileStorageEngine(str(vault))
    c = MemoryController(storage=engine)
    note = _note("a\r\nb\rc\n")
    c.propose(Principal.HUMAN, note)
    assert engine.get(note["id"])["content"] == FileStorageEngine(str(vault)).get(note["id"])["content"]
