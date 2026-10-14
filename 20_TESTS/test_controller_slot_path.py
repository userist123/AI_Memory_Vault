"""The MemoryController must not retire or rewrite the ontology slot notes.

The 16 `slot-*` notes live under 01_ARCHITECTURE, a root the storage engine scans, and
carry ids, so the controller treats them as ordinary ACTIVE notes. The verdict-manifest
gates on merge/promote/purge and the disposition-manifest gate on apply protect the
*rows* of the slot tables; none of them sits on this path. Until 2026-10-14 any principal
could retire a slot through it (an AI agent via supersede). Closed by Option A of
07_EVALUATION/ontology_write_paths/CONTROLLER_SLOT_PATH.md: the controller refuses
archive/supersede/update/propose on a slot for every principal.

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
# Closed 2026-10-14 (Option A of CONTROLLER_SLOT_PATH.md): the controller refuses archive,
# supersede, update and propose on an ontology slot for every principal. Was a strict xfail.
# --------------------------------------------------------------------------------------

ALL_PRINCIPALS = [Principal.AI_AGENT, Principal.HUMAN, Principal.ADMIN]


@pytest.mark.parametrize("principal,operation", [
    (Principal.AI_AGENT, "supersede"),
    (Principal.HUMAN, "supersede"),
    (Principal.HUMAN, "archive"),
])
def test_controller_refuses_to_retire_an_ontology_slot(vault, principal, operation):
    controller, _, slot_file = vault
    before = slot_file.read_bytes()
    with pytest.raises(PermissionError, match="ontology slot|not allowed"):
        if operation == "supersede":
            controller.supersede(principal, SLOT_ID, OTHER_ID, "test")
        else:
            controller.archive(principal, SLOT_ID, "test")
    assert slot_file.read_bytes() == before


@pytest.mark.parametrize("principal", ALL_PRINCIPALS)
def test_supersede_of_a_slot_is_refused_by_the_slot_rule_for_every_principal(vault, principal):
    controller, _, slot_file = vault
    other_file = slot_file.parent / "07_judgement.md"
    before, other_before = slot_file.read_bytes(), other_file.read_bytes()
    with pytest.raises(PermissionError, match="ontology slot") as err:
        controller.supersede(principal, SLOT_ID, OTHER_ID, "no manifest is asked for")
    assert "30_SCRIPTS/ingestion/" in str(err.value)
    assert slot_file.read_bytes() == before
    assert other_file.read_bytes() == other_before


@pytest.mark.parametrize("principal", ALL_PRINCIPALS)
def test_archive_of_a_slot_is_refused_by_the_slot_rule_for_every_principal(vault, principal):
    controller, _, slot_file = vault
    before = slot_file.read_bytes()
    # An AI agent is already stopped earlier by the authorizer (ARCHIVE is HUMAN/ADMIN only).
    expected = "not allowed" if principal == Principal.AI_AGENT else "ontology slot"
    with pytest.raises(PermissionError, match=expected):
        controller.archive(principal, SLOT_ID, "no manifest is asked for")
    assert slot_file.read_bytes() == before


@pytest.mark.parametrize("principal", ALL_PRINCIPALS)
def test_update_and_propose_of_a_slot_are_refused_by_policy_not_by_the_id_schema(vault, principal):
    """The refusal used to be a side effect of the frontmatter schema (`id` must be a uuid).
    It is now the slot rule, raised before any schema validation runs."""
    controller, _, slot_file = vault
    before = slot_file.read_bytes()
    body = controller.storage.get(SLOT_ID)["content"]
    with pytest.raises(PermissionError, match="ontology slot"):
        controller.update(principal, SLOT_ID, {"category": "ontology", "verification": "unverified",
                                               "content": body + "\n| forged | x | 0.9 | promoted | 2026-09-19 | z | | 1 |\n"})
    with pytest.raises(PermissionError, match="ontology slot"):
        controller.propose(principal, {"id": SLOT_ID, "content": "overwritten"})
    assert slot_file.read_bytes() == before


@pytest.mark.parametrize("principal", ALL_PRINCIPALS)
def test_a_note_typed_ontology_definition_is_refused_whatever_its_id(vault, principal):
    """The rule keys on the type as well as the `slot-` prefix: a uuid id does not get round it."""
    import uuid as _uuid
    controller, _, _ = vault
    with pytest.raises(PermissionError, match="ontology slot"):
        controller.propose(principal, {"id": str(_uuid.uuid4()), "type": "ontology_definition",
                                       "content": "a new slot"})


def test_reads_of_a_slot_are_untouched(vault):
    controller, _, _ = vault
    assert controller.storage.get(SLOT_ID)["type"] == "ontology_definition"
    controller.query(Principal.HUMAN)


def test_negative_control_the_temporary_vault_is_not_the_real_one(vault):
    """The refusals above run on a copy and never touch the repository's own slots."""
    controller, _, slot_file = vault
    with pytest.raises(PermissionError):
        controller.archive(Principal.HUMAN, SLOT_ID, "test")
    assert "lifecycle: ACTIVE" in (SLOTS / "06_procedures.md").read_text(encoding="utf-8")
    assert slot_file != SLOTS / "06_procedures.md"


@pytest.mark.parametrize("op", ["review", "promote", "attest", "challenge", "resolve_challenge"])
def test_every_other_controller_side_write_is_refused_by_the_slot_rule(vault, op):
    """R-01: attest/review/promote and the Consolidator's challenge/resolve_challenge write through
    the controller too; the slot rule refuses them for every principal, before any schema or
    lifecycle check gets a chance to refuse for an accidental reason."""
    from memory_controller.authorizer import Principal
    ctrl, _engine, slot_path = vault
    slot_id = "slot-06-procedures"
    before = slot_path.read_bytes()
    for principal in (Principal.HUMAN, Principal.ADMIN):
        with pytest.raises(PermissionError, match="ontology slot"):
            if op == "review":
                ctrl.review(principal, slot_id, "approve")
            elif op == "promote":
                ctrl.promote(principal, slot_id)
            elif op == "attest":
                ctrl.attest(principal, slot_id, "reason", "evidence-ref")
            else:
                from cognitive_core.consolidation import Consolidator
                cons = Consolidator(ctrl, tool_router=None)  # the router is not reached: the slot rule fires first
                if op == "challenge":
                    cons.challenge(slot_id, {"claim": "x"}, principal=principal)
                else:
                    cons.resolve_challenge(slot_id, {"content": "x"}, principal=principal)
    assert slot_path.read_bytes() == before
