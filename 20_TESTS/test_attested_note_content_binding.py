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


# --- security review round 1: bind the attestation to the attested content ----------------------

import hashlib  # noqa: E402


def _sha(text: str) -> str:
    return hashlib.sha256(text.replace("\r\n", "\n").encode("utf-8")).hexdigest()


def test_attest_records_the_content_hash():
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test")
    assert c.storage.get(nid)["attested_content_sha256"] == _sha(ORIGINAL)


def test_attest_refuses_when_the_content_changed_since_the_owner_read_it():
    """Pre-existing race: the agent rewrites an unattested RAW note while the owner reads it."""
    c = _controller()
    nid = _raw_note(c)
    read_hash = _sha(ORIGINAL)
    c.update(Principal.AI_AGENT, nid, {"content": "SWAPPED WHILE THE OWNER READ"})
    with pytest.raises(ValueError, match="changed"):
        c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test",
                 expected_content_sha256=read_hash)
    assert c.storage.get(nid)["verification"] == "unverified"


def test_attest_accepts_the_hash_the_owner_read():
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test",
             expected_content_sha256=_sha(ORIGINAL))
    assert c.storage.get(nid)["verification"] == "verified"


def test_partially_verified_cannot_be_laundered_into_active():
    """Downgrade, rewrite, re-stamp: the owner's stamp is cleared and promote refuses the text."""
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test",
             verification_state="partially_verified")
    c.update(Principal.AI_AGENT, nid, {"verification": "unverified"})
    stored = c.storage.get(nid)
    assert "verification_source" not in stored and "last_verified" not in stored
    assert "attested_content_sha256" not in stored
    c.update(Principal.AI_AGENT, nid, {"content": "AGENT TEXT"})
    c.update(Principal.AI_AGENT, nid, {"verification": "partially_verified"})
    c.review(Principal.HUMAN, nid, "approve")
    c.promote(Principal.HUMAN, nid)  # no owner stamp left: promoted as an agent-labelled note
    stored = c.storage.get(nid)
    assert stored.get("verification_source") is None
    assert stored.get("attested_content_sha256") is None


def test_promote_refuses_an_owner_attested_note_whose_content_changed():
    """Defence in depth: whatever path changes the body, promote checks the attested hash."""
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test")
    c.review(Principal.HUMAN, nid, "approve")
    note = c.storage.get(nid)
    note["content"] = "CHANGED BEHIND THE CONTROLLER"
    c.storage.set(nid, note)
    with pytest.raises(ValueError, match="re-attest"):
        c.promote(Principal.HUMAN, nid)


def test_promote_refuses_an_owner_attested_note_without_a_hash():
    """Attestations made before the hash existed are re-attested before promotion (fail closed)."""
    c = _controller()
    nid = _raw_note(c)
    c.attest(Principal.HUMAN, nid, verification_reason="read it", evidence_reference="test")
    c.review(Principal.HUMAN, nid, "approve")
    note = c.storage.get(nid)
    note.pop("attested_content_sha256")
    c.storage.set(nid, note)
    with pytest.raises(ValueError, match="re-attest"):
        c.promote(Principal.HUMAN, nid)
