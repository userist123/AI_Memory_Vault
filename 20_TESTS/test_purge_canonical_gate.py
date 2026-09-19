"""purge_rejected_rows.py may not delete from the canonical slots without a disposition manifest.

`--apply` was the only thing between a row selection and deleting from the
ontology. These tests point CANONICAL_SLOT_DIRECTORY at a temporary directory
(as the merge and promotion gates' tests do), so the real slots are never touched.
"""
from __future__ import annotations

import hashlib
import importlib.util
import os
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

SLOT = """---
id: slot-03-ontology
ontology_slot: ontology
---

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| qualia | quantum_framework | 0.88 | proposed | 2026-09-12 | | speculative | 4 |
| chunk | wcs_1488 | 0.95 | proposed | 2026-09-12 | | symbolic chunk | 7 |
"""


def _load():
    spec = importlib.util.spec_from_file_location(
        "purge_gate_under_test", _REPO / "30_SCRIPTS" / "ingestion" / "purge_rejected_rows.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


@pytest.fixture
def world(tmp_path, monkeypatch):
    slots = tmp_path / "slots"
    slots.mkdir()
    slot_file = slots / "03_ontology.md"
    slot_file.write_text(SLOT, encoding="utf-8", newline="")
    module = _load()
    monkeypatch.setattr(module, "CANONICAL_SLOT_DIRECTORY", str(slots))
    return module, slots, slot_file


PLAIN = [{"slot_file": "03_ontology.md", "concept": "qualia"}]
DECIDED = [{"slot_file": "03_ontology.md", "concept": "qualia", "disposition": "DELETE"}]


def test_apply_on_canonical_without_manifest_is_refused_and_deletes_nothing(world):
    module, slots, slot_file = world
    before = _digest(slot_file)
    with pytest.raises(module.UngatedCanonicalWrite):
        module.purge_rows(PLAIN, slots_dir=str(slots), apply=True)
    assert _digest(slot_file) == before


def test_flag_alone_is_not_enough_without_dispositions_on_the_targets(world):
    module, slots, slot_file = world
    before = _digest(slot_file)
    with pytest.raises(module.UngatedCanonicalWrite):
        module.purge_rows(PLAIN, slots_dir=str(slots), apply=True, from_disposition_manifest=True)
    assert _digest(slot_file) == before


def test_refused_under_every_spelling_of_the_canonical_directory(world):
    module, slots, slot_file = world
    before = _digest(slot_file)
    for spelling in (str(slots) + os.sep, os.path.join(str(slots), "."), os.path.join(str(slots), "..", "slots")):
        with pytest.raises(module.UngatedCanonicalWrite):
            module.purge_rows(PLAIN, slots_dir=spelling, apply=True)
    assert _digest(slot_file) == before


def test_dry_run_on_canonical_needs_no_manifest_and_writes_nothing(world):
    module, slots, slot_file = world
    before = _digest(slot_file)
    result = module.purge_rows(PLAIN, slots_dir=str(slots), apply=False)
    assert result["dry_run"] is True and result["deleted_count"] == 1
    assert _digest(slot_file) == before


def test_apply_on_a_copy_needs_no_manifest(world, tmp_path):
    module, slots, _ = world
    copy = tmp_path / "copy"
    copy.mkdir()
    (copy / "03_ontology.md").write_text(SLOT, encoding="utf-8", newline="")
    result = module.purge_rows(PLAIN, slots_dir=str(copy), apply=True)
    assert result["deleted_count"] == 1
    assert "qualia" not in (copy / "03_ontology.md").read_text(encoding="utf-8")


def test_targets_carrying_dispositions_from_a_manifest_are_let_through(world):
    module, slots, slot_file = world
    result = module.purge_rows(DECIDED, slots_dir=str(slots), apply=True, from_disposition_manifest=True)
    assert result["deleted_count"] == 1
    assert "qualia" not in slot_file.read_text(encoding="utf-8")
    assert "chunk" in slot_file.read_text(encoding="utf-8")


def test_command_line_with_the_manifest_switched_off_is_refused(world, monkeypatch):
    """`--disposition-manifest ''` used to switch the only protection off."""
    module, slots, slot_file = world
    before = _digest(slot_file)
    monkeypatch.setattr(sys, "argv", ["purge_rejected_rows.py", "--batch", "A", "--apply",
                                       "--disposition-manifest", "", "--slots-dir", str(slots)])
    with pytest.raises(module.UngatedCanonicalWrite):
        module.main()
    assert _digest(slot_file) == before


def test_negative_control_the_gate_is_what_stops_the_write(world, monkeypatch):
    """With the gate's directory test switched off the same call deletes the row,
    so the refusals above come from the gate and not from some other failure."""
    module, slots, slot_file = world
    monkeypatch.setattr(module, "_same_directory", lambda a, b: False)
    result = module.purge_rows(PLAIN, slots_dir=str(slots), apply=True)
    assert result["deleted_count"] == 1
    assert "qualia" not in slot_file.read_text(encoding="utf-8")
