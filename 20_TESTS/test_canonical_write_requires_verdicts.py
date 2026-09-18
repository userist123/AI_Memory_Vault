"""No verdict manifest, no write into the ontology.

The manifest arrived as an opt-in so existing callers would not break, which
meant it held only for callers who chose it. These tests pin the other half:
a merge aimed at the canonical slot files without a manifest is refused before
anything is written, and every spelling of that path is the same path.

None of them writes the real ontology to find out. The canonical directory is
pointed at a temporary copy, and a separate test checks that the constant the
gate compares against is the real one. If the gate were broken, the refusal
tests would fail by writing into a temporary directory, not into the vault.
"""
from __future__ import annotations

import json
import os
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

import merge_candidate_concepts as M  # noqa: E402
from promotion_verdicts import VERDICTS_SCHEMA_VERSION  # noqa: E402

HEADER = (
    "## Candidate concepts\n"
    "| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |\n"
    "|---|---|---|---|---|---|---|---|\n"
)


@pytest.fixture
def ontology(tmp_path, monkeypatch):
    """A stand-in for the canonical slot files."""
    slots = tmp_path / "ontology" / "slots"
    slots.mkdir(parents=True)
    (slots / "03_ontology.md").write_text(HEADER, encoding="utf-8")
    monkeypatch.setattr(M, "CANONICAL_SLOT_DIRECTORY", str(slots))
    return slots


@pytest.fixture
def staging(tmp_path):
    path = tmp_path / "staging.json"
    path.write_text(json.dumps([
        {"concept": "x", "maps_to_slot": "ontology", "occurrences": 3,
         "evidence_quote": "q", "definition": "d", "source_book": "b"}
    ]), encoding="utf-8")
    return path


def _slot_bytes(slots: pathlib.Path) -> dict:
    return {p.name: p.read_bytes() for p in sorted(slots.iterdir())}


def test_an_ungated_write_into_the_ontology_is_refused_and_writes_nothing(ontology, staging):
    before = _slot_bytes(ontology)
    with pytest.raises(M.UngatedCanonicalWrite, match="without a verdict manifest"):
        M.merge_candidate_concepts(str(staging), slots_dir=str(ontology))
    assert _slot_bytes(ontology) == before


@pytest.mark.parametrize("spelling", [
    pytest.param(lambda p: str(p) + os.sep, id="trailing separator"),
    pytest.param(lambda p: os.path.join(str(p.parent), ".", p.name), id="dot segment"),
    pytest.param(lambda p: os.path.join(str(p), "..", p.name), id="up and back"),
])
def test_another_spelling_of_the_ontology_is_still_the_ontology(ontology, staging, spelling):
    with pytest.raises(M.UngatedCanonicalWrite):
        M.merge_candidate_concepts(str(staging), slots_dir=spelling(ontology))


def test_a_manifest_opens_the_gate(ontology, staging, tmp_path):
    verdicts = tmp_path / "v.json"
    verdicts.write_text(json.dumps({
        "schema_version": VERDICTS_SCHEMA_VERSION,
        "verdicts": [{"concept": "x", "verdict": "PROMOTE", "reason": "distinct"}],
    }), encoding="utf-8")
    result = M.merge_candidate_concepts(str(staging), slots_dir=str(ontology),
                                        verdicts_file=str(verdicts))
    assert result["gated"] is True
    assert result["net_new_concepts_merged"] == 1


def test_a_working_copy_needs_no_manifest(ontology, staging, tmp_path):
    """The dry run: the same merge against a copy of the slots."""
    before = _slot_bytes(ontology)
    copy = tmp_path / "copy"
    copy.mkdir()
    (copy / "03_ontology.md").write_text(HEADER, encoding="utf-8")
    result = M.merge_candidate_concepts(str(staging), slots_dir=str(copy))
    assert result["gated"] is False
    assert result["net_new_concepts_merged"] == 1
    assert _slot_bytes(ontology) == before


def test_a_malformed_staging_file_still_reports_its_own_error(ontology, tmp_path):
    """The gate sits after validation, so a bad row is named as a bad row."""
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps([{"concept": "x", "maps_to_slot": "nowhere"}]), encoding="utf-8")
    with pytest.raises(ValueError, match="Invalid canonical slot 'nowhere'"):
        M.merge_candidate_concepts(str(bad), slots_dir=str(ontology))


def test_the_gate_guards_the_real_ontology():
    """Every test above swaps the constant; this one checks what it is swapped from."""
    real = _REPO / "01_ARCHITECTURE" / "ontology" / "slots"
    assert os.path.samefile(M.CANONICAL_SLOT_DIRECTORY, real)
    assert any(real.glob("*_ontology.md"))
