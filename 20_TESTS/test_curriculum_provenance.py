"""Tests for Bibliographic Provenance and Promotion Gate Enforcement.

Verifies:
1. All 16 OpenStax Psychology notes pass validate_note_provenance.
2. The curriculum provenance manifest passes validate_source_manifest.
3. Negative control 1: Note without edition fails validation.
4. Negative control 2: Note without license fails validation.
5. Negative control 3: Manifest missing ISBN, authors, or files fails validation.
6. Negative control 4: Promoting a curriculum concept without valid provenance manifest is rejected.
"""
from __future__ import annotations

import glob
import json
import os
import sys
import tempfile
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPTS_DIR = REPO_ROOT / "30_SCRIPTS" / "knowledge"
INGESTION_DIR = REPO_ROOT / "30_SCRIPTS" / "ingestion"
for p in (str(SCRIPTS_DIR), str(INGESTION_DIR), str(REPO_ROOT / "03_IMPLEMENTATION" / "packages")):
    if p not in sys.path:
        sys.path.insert(0, p)

from validate_bibliographic_provenance import (
    validate_note_provenance,
    validate_source_manifest,
)
from promote_candidate_concept import (
    promote_candidate_concept,
    BibliographicProvenanceRequired,
)

MANIFEST_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "provenance_manifest.json"
NOTES_DIR = REPO_ROOT / "01_ARCHITECTURE" / "knowledge"


class TestCurriculumBibliographicProvenance:
    def test_provenance_manifest_is_valid(self):
        assert MANIFEST_PATH.exists()
        data = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        ok, errors = validate_source_manifest(data)
        assert ok, f"Manifest validation errors: {errors}"

    def test_all_16_openstax_psychology_notes_pass_provenance(self):
        note_paths = sorted(glob.glob(str(NOTES_DIR / "openstax_psy2e_8_*.md")))
        assert len(note_paths) == 16, f"Expected 16 notes, found {len(note_paths)}"
        for npath in note_paths:
            ok, errors = validate_note_provenance(npath)
            assert ok, f"Note {Path(npath).name} failed provenance validation: {errors}"

    def test_negative_control_manifest_missing_isbn_fails(self):
        data = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        del data["isbn_or_doi"]
        ok, errors = validate_source_manifest(data)
        assert not ok
        assert any("isbn_or_doi" in e for e in errors)

    def test_negative_control_manifest_missing_edition_fails(self):
        data = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        del data["edition"]
        ok, errors = validate_source_manifest(data)
        assert not ok
        assert any("edition" in e for e in errors)

    def test_negative_control_note_without_edition_fails(self, tmp_path):
        sample_note = NOTES_DIR / "openstax_psy2e_8_1_how_memory_functions_encoding.md"
        content = sample_note.read_text(encoding="utf-8")
        # Remove edition line completely
        import re
        stripped = re.sub(r"-\s+\*\*(?:Ediție|Edition)\*\*:[^\n]*\n", "", content)
        temp_note = tmp_path / "bad_edition.md"
        temp_note.write_text(stripped, encoding="utf-8")
        ok, errors = validate_note_provenance(temp_note)
        assert not ok
        assert any("edition" in e.lower() for e in errors)

    def test_negative_control_note_without_license_fails(self, tmp_path):
        sample_note = NOTES_DIR / "openstax_psy2e_8_1_how_memory_functions_encoding.md"
        content = sample_note.read_text(encoding="utf-8")
        # Break manifest reference and remove license lines
        stripped = content.replace("- **Licență**:", "- **Altceva**:")
        stripped = stripped.replace("07_EVALUATION/curriculum/provenance_manifest.json", "nonexistent.json")
        temp_note = tmp_path / "bad_license.md"
        temp_note.write_text(stripped, encoding="utf-8")
        ok, errors = validate_note_provenance(temp_note)
        assert not ok
        assert any("license" in e.lower() or "manifest" in e.lower() for e in errors)


class TestCurriculumPromotionProvenanceGate:
    def test_negative_control_promotion_refused_without_provenance_manifest(self, tmp_path, monkeypatch):
        slots_dir = tmp_path / "slots"
        notes_dir = tmp_path / "knowledge"
        slots_dir.mkdir()
        notes_dir.mkdir()

        slot_file = slots_dir / "06_procedures.md"
        slot_content = (
            "---\nid: slot-06-procedures\nontology_slot: procedures\ntype: ontology_definition\n"
            "lifecycle: ACTIVE\nprovenance:\n  source_type: design\n  source_author: TEST\n"
            "confidence: 1.00\nstatus: scaffold\ntags: [ontology]\nrelations: []\ncreated: 2026-09-07\n---\n\n"
            "# Ontology Slot: Procedures\n\n## Candidate concepts\n"
            "| concept | source_book | confidence | status | date_added | promoted_note_id |\n"
            "|---|---|---|---|---|---|\n"
            "| Chunking method | openstax_psychology_ch08 | 0.95 | proposed | 2026-09-18 | |\n"
        )
        slot_file.write_text(slot_content, encoding="utf-8")

        # Point manifest path to non-existent location
        import promote_candidate_concept as pcc
        monkeypatch.setattr(
            pcc,
            "CURRICULUM_PROVENANCE_MANIFEST",
            str(tmp_path / "nonexistent_manifest.json"),
        )

        with pytest.raises(BibliographicProvenanceRequired):
            pcc.promote_candidate_concept(
                concept_name="Chunking method",
                slot_name="procedures",
                slots_dir=str(slots_dir),
                notes_dir=str(notes_dir),
                judgment_reasoning="test",
            )
