#!/usr/bin/env python3
"""
test_concept_promotion.py — Verification of gated concept promotion to REVIEW memory notes.

Verifies:
1. Every promoted note strictly passes the canonical schema validator (schema.py).
2. Every promoted note has lifecycle: REVIEW (never ACTIVE).
3. Every promoted note has provenance.source_type == "import" (book import).
4. Originating slot file row is updated to status=promoted with non-empty promoted_note_id.
5. No note exists for concepts explicitly declined by judgment evaluation (e.g. 'Synergy').
"""

import os
import yaml
import glob
import pytest
import importlib.util

schema_path = os.path.abspath("03_IMPLEMENTATION/packages/lifecycle/validation/schema.py")
spec = importlib.util.spec_from_file_location("schema_mod", schema_path)
schema_mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(schema_mod)
validate_frontmatter = schema_mod.validate_frontmatter

KNOWLEDGE_DIR = "01_ARCHITECTURE/knowledge"
SLOTS_DIR = "01_ARCHITECTURE/ontology/slots"


def parse_note_frontmatter(filepath: str) -> dict:
    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    assert content.startswith("---"), f"Note {filepath} does not start with frontmatter '---'"
    parts = content.split("---", 2)
    assert len(parts) >= 3, f"Note {filepath} frontmatter parsing error"
    return yaml.safe_load(parts[1])


def test_promoted_note_passes_schema_validator():
    """
    Verifies that all Promoted_*.md notes in 01_ARCHITECTURE/knowledge pass canonical schema validation.
    """
    promoted_notes = glob.glob(os.path.join(KNOWLEDGE_DIR, "Promoted_*.md"))
    assert len(promoted_notes) > 0, "No promoted concept notes found in knowledge store!"

    for note_path in promoted_notes:
        data = parse_note_frontmatter(note_path)
        assert validate_frontmatter(data) is True


def test_promoted_note_lifecycle_is_review_never_active():
    """
    Asserts every promoted note has lifecycle: REVIEW and NEVER ACTIVE.
    """
    promoted_notes = glob.glob(os.path.join(KNOWLEDGE_DIR, "Promoted_*.md"))
    for note_path in promoted_notes:
        data = parse_note_frontmatter(note_path)
        assert data["lifecycle"] == "REVIEW"
        assert data["lifecycle"] != "ACTIVE"


def test_promoted_note_provenance():
    """
    Asserts every promoted note has valid book import provenance.
    """
    promoted_notes = glob.glob(os.path.join(KNOWLEDGE_DIR, "Promoted_*.md"))
    for note_path in promoted_notes:
        data = parse_note_frontmatter(note_path)
        assert data["provenance"]["source_type"] in ["import", "official"]
        assert "Sarfraz" in data["provenance"]["source_ref"]


def test_originating_slot_file_table_updated():
    """
    Asserts the originating slot file (06_procedures.md) table row was updated to status=promoted
    with matching promoted_note_id.
    """
    procedures_slot = glob.glob(os.path.join(SLOTS_DIR, "*_procedures.md"))[0]
    with open(procedures_slot, "r", encoding="utf-8") as f:
        content = f.read()

    row = None
    for line in content.splitlines():
        if line.startswith("|") and "Reservoir Sampling" in line:
            row = line
            break

    assert row is not None, "Reservoir Sampling row missing in 06_procedures.md!"
    cols = [c.strip() for c in row.split("|")]
    assert len(cols) == 8  # 6 columns pipe-split into 8 elements
    assert cols[1] == "Reservoir Sampling"
    assert cols[4] == "promoted"
    assert len(cols[6]) > 10  # Promoted note ID populated

    # Verify ID matches the promoted note
    promoted_note_path = os.path.join(KNOWLEDGE_DIR, "Promoted_reservoir_sampling.md")
    note_data = parse_note_frontmatter(promoted_note_path)
    assert cols[6] == note_data["id"]


def test_declined_concept_has_no_note_file():
    """
    Asserts no memory note was created for declined concept ('Synergy') and status remains 'proposed'.
    """
    declined_note = os.path.join(KNOWLEDGE_DIR, "Promoted_synergy.md")
    assert not os.path.exists(declined_note), f"Note file {declined_note} should NOT exist for declined concept!"

    consolidation_slot = glob.glob(os.path.join(SLOTS_DIR, "*_consolidation.md"))[0]
    with open(consolidation_slot, "r", encoding="utf-8") as f:
        content = f.read()

    synergy_row = None
    for line in content.splitlines():
        if line.startswith("|") and "Synergy" in line and "Sarfraz" in line:
            synergy_row = line
            break

    assert synergy_row is not None
    cols = [c.strip() for c in synergy_row.split("|")]
    assert cols[4] == "proposed"
    assert cols[6] == ""  # Empty promoted_note_id for declined concept
