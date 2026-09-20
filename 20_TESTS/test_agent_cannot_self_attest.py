"""Through the API that exists, an agent cannot move its own note into a trusted state.

The policy table allows `structural_rewrite: REVIEW -> VERIFIED` for an agent.
The controller does not expose it: `update()` treats lifecycle as immutable and
`attest()` refuses an agent. That is the property worth protecting, so it is
tested against the real controller rather than inferred from the table.
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


@pytest.fixture
def controller_with_an_agent_note():
    controller = MemoryController(storage=SQLiteStorageEngine(":memory:"), index=VaultIndex([]))
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, {
        "id": note_id, "type": "knowledge", "category": "test", "tags": ["t"],
        "created": "2026-09-20", "updated": "2026-09-20",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "confidence": "high", "verification": "unverified", "relations": [],
        "content": "a proposed note",
    })
    return controller, note_id


def test_an_agents_proposal_starts_untrusted(controller_with_an_agent_note):
    controller, note_id = controller_with_an_agent_note
    assert controller.storage.get(note_id)["lifecycle"] == "RAW"


@pytest.mark.parametrize("target", ["VERIFIED", "ACTIVE"])
def test_an_agent_cannot_rewrite_its_way_into_a_trusted_state(controller_with_an_agent_note, target):
    controller, note_id = controller_with_an_agent_note
    with pytest.raises(ValueError, match="immutable"):
        controller.update(Principal.AI_AGENT, note_id, {"lifecycle": target})
    assert controller.storage.get(note_id)["lifecycle"] == "RAW", "the note did not move"


def test_an_agent_cannot_attest(controller_with_an_agent_note):
    controller, note_id = controller_with_an_agent_note
    with pytest.raises(PermissionError):
        controller.attest(Principal.AI_AGENT, note_id, "reason", "evidence")
    assert controller.storage.get(note_id)["verification"] == "unverified"
