"""The MemoryController can retire the ontology slot notes; nothing in it says it may not.

The 16 `slot-*` notes live under 01_ARCHITECTURE, a root the storage engine scans, and
carry ids, so the controller treats them as ordinary ACTIVE notes. The verdict-manifest
gates on merge/promote/purge and the disposition-manifest gate on apply protect the
*rows* of the slot tables; none of them sits on this path. See
07_EVALUATION/ontology_write_paths/CONTROLLER_SLOT_PATH.md.

Everything runs on a temporary copy of the slots; the real ones are never touched.
"""
from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402

SLOTS = REPO / "01_ARCHITECTURE" / "ontology" / "slots"
SLOT_ID, OTHER_ID = "slot-06-procedures", "slot-07-judgement"


@pytest.fixture
def vault(tmp_path, monkeypatch):
    """A temporary vault holding a copy of the real slots, with the audit log kept inside it."""
    root = tmp_path / "vault"
    shutil.copytree(SLOTS, root / "01_ARCHITECTURE" / "ontology" / "slots")
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.chdir(tmp_path)
    engine = FileStorageEngine(str(root))
    slot_file = root / "01_ARCHITECTURE" / "ontology" / "slots" / "06_procedures.md"
    return MemoryController(engine), engine, slot_file


def _lifecycle_line(path: Path) -> str:
    return next(l for l in path.read_text(encoding="utf-8").splitlines() if l.startswith("lifecycle:"))


# --------------------------------------------------------------------------------------
# The desired state. Fails today, so it is xfail(strict=True): the day someone closes the
# path it XPASSes, the suite goes red, and the marker (and the demonstration below) must go.
# --------------------------------------------------------------------------------------

@pytest.mark.xfail(strict=True, reason="controlerul permite rescrierea sloturilor pe lângă poarta de verdicte")
@pytest.mark.parametrize("principal,operation", [
    (Principal.AI_AGENT, "supersede"),
    (Principal.HUMAN, "supersede"),
    (Principal.HUMAN, "archive"),
])
def test_controller_refuses_to_retire_an_ontology_slot(vault, principal, operation):
    controller, _, slot_file = vault
    before = slot_file.read_bytes()
    with pytest.raises(Exception):
        if operation == "supersede":
            controller.supersede(principal, SLOT_ID, OTHER_ID, "test")
        else:
            controller.archive(principal, SLOT_ID, "test")
    assert slot_file.read_bytes() == before


# --------------------------------------------------------------------------------------
# What is true today. These pass now and are meant to fail when the path is closed.
# --------------------------------------------------------------------------------------

def test_today_an_ai_agent_can_supersede_a_slot_note(vault):
    controller, _, slot_file = vault
    assert _lifecycle_line(slot_file) == "lifecycle: ACTIVE"
    controller.supersede(Principal.AI_AGENT, SLOT_ID, OTHER_ID, "no manifest is asked for")
    assert _lifecycle_line(slot_file) == "lifecycle: SUPERSEDED"
    assert f"superseded_by: {OTHER_ID}" in slot_file.read_text(encoding="utf-8")


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.ADMIN])
def test_today_a_human_or_admin_can_archive_a_slot_note(vault, principal):
    controller, _, slot_file = vault
    controller.archive(principal, SLOT_ID, "no manifest is asked for")
    assert _lifecycle_line(slot_file) == "lifecycle: ARCHIVED"


def test_today_archive_is_refused_to_an_ai_agent_by_the_authorizer(vault):
    controller, _, slot_file = vault
    before = slot_file.read_bytes()
    with pytest.raises(PermissionError):
        controller.archive(Principal.AI_AGENT, SLOT_ID, "test")
    assert slot_file.read_bytes() == before


@pytest.mark.parametrize("principal", [Principal.AI_AGENT, Principal.HUMAN, Principal.ADMIN])
def test_today_update_and_propose_are_refused_only_because_the_slot_id_is_not_a_uuid(vault, principal):
    """The refusal is a side effect of the frontmatter schema (`id` must be a uuid; slot ids are
    `slot-NN-name`), not a policy. If the schema relaxes `id`, this fails and the update path
    has to be looked at before anything else."""
    controller, _, slot_file = vault
    before = slot_file.read_bytes()
    body = controller.storage.get(SLOT_ID)["content"]
    with pytest.raises(Exception) as err:
        controller.update(principal, SLOT_ID, {"category": "ontology", "verification": "unverified",
                                               "content": body + "\n| forged | x | 0.9 | promoted | 2026-09-19 | z | | 1 |\n"})
    assert "is not a 'uuid'" in str(err.value)
    with pytest.raises(Exception) as err2:
        controller.propose(principal, {"id": SLOT_ID, "content": "overwritten"})
    assert "is not a 'uuid'" in str(err2.value)
    assert slot_file.read_bytes() == before


def test_negative_control_the_temporary_vault_is_not_the_real_one(vault):
    """The demonstrations must not have touched the repository's own slots."""
    controller, _, slot_file = vault
    controller.archive(Principal.HUMAN, SLOT_ID, "test")
    assert "lifecycle: ACTIVE" in (SLOTS / "06_procedures.md").read_text(encoding="utf-8")
    assert slot_file != SLOTS / "06_procedures.md"
