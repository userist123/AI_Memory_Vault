"""promote_candidate_concept.py may not rewrite the canonical slot files without a manifest.

It flipped any `proposed` row to `promoted` on the caller's say-so, the same
one-flag-short path merge_candidate_concepts.py closed with UngatedCanonicalWrite.
These tests never touch the real slots: CANONICAL_SLOT_DIRECTORY is pointed at a
temporary directory, which is what the merge gate's own tests do.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))

SLOT = (
    "---\nid: slot-06-procedures\nontology_slot: procedures\ntype: ontology_definition\n"
    "lifecycle: ACTIVE\nprovenance:\n  source_type: design\n  source_author: TEST\n"
    "confidence: 1.00\nstatus: scaffold\ntags: [ontology]\nrelations: []\ncreated: 2026-09-07\n---\n\n"
    "# Ontology Slot: Procedures\n\n## Candidate concepts\n"
    "| concept | source_book | confidence | status | date_added | promoted_note_id |\n"
    "|---|---|---|---|---|---|\n"
    "| Reservoir sampling | test-book | 0.90 | proposed | 2026-09-09 | |\n"
)


def _load_promote():
    # Fresh copy per test module import; the gate constant is patched per test.
    spec = importlib.util.spec_from_file_location(
        "promote_gate_under_test", _REPO / "30_SCRIPTS" / "ingestion" / "promote_candidate_concept.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _manifest(path: pathlib.Path, verdict: str) -> str:
    path.write_text(json.dumps({
        "schema_version": "polymarket-promotion-verdicts.v1",
        "verdicts": [{"concept": "Reservoir sampling", "verdict": verdict, "reason": "test decision"}],
    }), encoding="utf-8")
    return str(path)


@pytest.fixture
def world(tmp_path, monkeypatch):
    slots = tmp_path / "slots"
    notes = tmp_path / "knowledge"
    slots.mkdir()
    notes.mkdir()
    slot_file = slots / "06_procedures.md"
    slot_file.write_text(SLOT, encoding="utf-8", newline="")
    module = _load_promote()
    monkeypatch.setattr(module, "CANONICAL_SLOT_DIRECTORY", str(slots), raising=False)
    return module, slots, notes, slot_file


def test_canonical_write_without_manifest_is_refused_and_writes_nothing(world):
    module, slots, notes, slot_file = world
    before = _digest(slot_file)
    with pytest.raises(module.UngatedCanonicalWrite):
        module.promote_candidate_concept("Reservoir sampling", "procedures",
                                         slots_dir=str(slots), notes_dir=str(notes))
    assert _digest(slot_file) == before
    assert list(notes.iterdir()) == []


def test_refused_under_every_spelling_of_the_canonical_directory(world):
    module, slots, notes, slot_file = world
    before = _digest(slot_file)
    spellings = [str(slots) + os.sep, os.path.join(str(slots), "."), os.path.join(str(slots), "..", "slots")]
    for spelling in spellings:
        with pytest.raises(module.UngatedCanonicalWrite):
            module.promote_candidate_concept("Reservoir sampling", "procedures",
                                             slots_dir=spelling, notes_dir=str(notes))
    assert _digest(slot_file) == before


def test_manifest_that_does_not_admit_is_refused(world, tmp_path):
    module, slots, notes, slot_file = world
    before = _digest(slot_file)
    for verdict in ("REJECT", "UNSURE"):
        with pytest.raises(Exception) as err:
            module.promote_candidate_concept(
                "Reservoir sampling", "procedures", slots_dir=str(slots), notes_dir=str(notes),
                verdicts_file=_manifest(tmp_path / f"{verdict}.json", verdict))
        assert "does not admit" in str(err.value)
    assert _digest(slot_file) == before
    assert list(notes.iterdir()) == []


def test_manifest_that_admits_lets_the_promotion_through(world, tmp_path):
    module, slots, notes, slot_file = world
    result = module.promote_candidate_concept(
        "Reservoir sampling", "procedures", slots_dir=str(slots), notes_dir=str(notes),
        judgment_reasoning="test", verdicts_file=_manifest(tmp_path / "ok.json", "PROMOTE"))
    assert result["status"] == "promoted"
    assert "| promoted |" in slot_file.read_text(encoding="utf-8")


def test_non_canonical_directory_needs_no_manifest(world, tmp_path):
    """The gate is not over-broad: a dry run on a copy still works."""
    module, slots, notes, _ = world
    copy = tmp_path / "copy"
    copy.mkdir()
    (copy / "06_procedures.md").write_text(SLOT, encoding="utf-8", newline="")
    result = module.promote_candidate_concept(
        "Reservoir sampling", "procedures", slots_dir=str(copy), notes_dir=str(notes),
        judgment_reasoning="test")
    assert result["status"] == "promoted"


def test_negative_control_the_gate_is_what_stops_the_write(world, monkeypatch):
    """With the canonical directory pointed elsewhere the same call succeeds, so
    the refusal above comes from the gate and not from some other failure."""
    module, slots, notes, slot_file = world
    monkeypatch.setattr(module, "CANONICAL_SLOT_DIRECTORY", str(slots / "elsewhere"))
    result = module.promote_candidate_concept(
        "Reservoir sampling", "procedures", slots_dir=str(slots), notes_dir=str(notes),
        judgment_reasoning="test")
    assert result["status"] == "promoted"
