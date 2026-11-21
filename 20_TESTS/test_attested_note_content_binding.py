"""An owner attestation must cover the content the owner attested, not whatever an agent writes later.

Found 2026-11-21 (AKE Loop A review, probe reproduced end to end):
1. the owner attests a RAW note (`attest()` has no lifecycle check);
2. an AI_AGENT rewrites its content: `update()` lets an agent change any field of a
   RAW/CLASSIFIED/NORMALIZED note, and `verification: verified` stays;
3. the owner reviews and promotes it: the note is ACTIVE and verified with the agent's text,
   and `memory_get` serves that text to every agent as verified.

Fix (fail closed): on an attested note an AI_AGENT may change only what it may change on an
ACTIVE note (relations, confidence, valid_until, and verification never to "verified"). An
unattested RAW note stays editable by its agent, as before.
"""
from __future__ import annotations

import os
import sys
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

ORIGINAL = "the text the owner read and attested"


def _controller():
    return MemoryController(storage=SQLiteStorageEngine(":memory:"), index=VaultIndex([]))


def _raw_note(controller, principal=Principal.HUMAN, source_type="user"):
    note_id = str(uuid.uuid4())
    controller.propose(principal, {
        "id": note_id, "type": "knowledge", "category": "test", "tags": ["t"],
        "created": "2026-11-21", "updated": "2026-11-21",
        "provenance": {"source_type": source_type, "source_ref": "test"},
        "confidence": "high", "verification": "unverified", "relations": [],
        "content": ORIGINAL,
    })
    return note_id


@pytest.mark.parametrize("state", ["verified", "partially_verified"])
def test_agent_cannot_rewrite_an_attested_raw_note(state):
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test", verification_state=state)
    with pytest.raises(PermissionError):
        c.update(Principal.AI_AGENT, nid, {"content": "REWRITTEN BY AGENT"})
    stored = c.storage.get(nid)
    assert stored["content"] == ORIGINAL and stored["verification"] == state


def test_attested_content_is_what_reaches_active():
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test")
    with pytest.raises(PermissionError):
        c.update(Principal.AI_AGENT, nid, {"content": "REWRITTEN BY AGENT"})
    c.review(Principal.HUMAN, nid, "approve")
    c.promote(Principal.HUMAN, nid)
    stored = c.storage.get(nid)
    assert str(stored["lifecycle"]).endswith("ACTIVE") and stored["content"] == ORIGINAL


def test_agent_may_still_adjust_metadata_of_an_attested_raw_note():
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test")
    c.update(Principal.AI_AGENT, nid, {"confidence": "medium"})
    assert c.storage.get(nid)["confidence"] == "medium"
    assert c.storage.get(nid)["content"] == ORIGINAL


def test_unattested_raw_note_stays_editable_by_the_agent():
    c = _controller()
    nid = _raw_note(c, principal=Principal.AI_AGENT, source_type="execution")
    c.update(Principal.AI_AGENT, nid, {"content": "a better draft"})
    assert c.storage.get(nid)["content"] == "a better draft"


def test_downgrade_then_rewrite_leaves_no_verified_stamp_on_agent_text():
    """An agent may still withdraw the attestation (verification -> unverified) and then edit a RAW
    note, as before. What it can no longer do is keep `verified` on text the owner never read:
    the owner sees the note as unverified at review."""
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test")
    c.update(Principal.AI_AGENT, nid, {"verification": "unverified"})
    c.update(Principal.AI_AGENT, nid, {"content": "REWRITTEN BY AGENT"})
    stored = c.storage.get(nid)
    assert stored["content"] == "REWRITTEN BY AGENT" and stored["verification"] == "unverified"
