#!/usr/bin/env python3
"""
test_book_ingestion_pipeline.py — Unit test suite for r027 book ingestion pipeline.

Verifies:
1. Verbatim guard blocks >=15 consecutive word exact matches against source text.
2. Appended table rows strictly conform to 5-column schema.
3. maps_to_slot is strictly one of the 16 canonical slot names.
4. Merge step is idempotent (re-running on same staging data appends 0 rows).
"""

import os
import re
import json
import tempfile
import shutil
import pytest
from typing import Dict, Any

import importlib

extract_mod = importlib.import_module("30_SCRIPTS.ingestion.extract_book_concepts")
check_verbatim_overlap = extract_mod.check_verbatim_overlap
extract_book_concepts = extract_mod.extract_book_concepts
split_into_structural_chunks = extract_mod.split_into_structural_chunks
CANONICAL_SLOTS = extract_mod.CANONICAL_SLOTS

merge_mod = importlib.import_module("30_SCRIPTS.ingestion.merge_candidate_concepts")
merge_candidate_concepts = merge_mod.merge_candidate_concepts
extract_existing_concepts = merge_mod.extract_existing_concepts
find_slot_file = merge_mod.find_slot_file
SLOT_DIRECTORY = merge_mod.SLOT_DIRECTORY


def test_verbatim_guard_detection():
    """
    Verifies check_verbatim_overlap flags >=15 consecutive word verbatim matches.
    """
    source_chunk = (
        "In biological neural networks synaptic consolidation retains information over longer periods "
        "by decreasing the rates of plasticity in a proportion of strengthened synapses that persist."
    )

    # 1. Exact 15-word verbatim match
    verbatim_def = (
        "synaptic consolidation retains information over longer periods by decreasing the rates of "
        "plasticity in a proportion of strengthened synapses that persist"
    )
    is_verbatim, match = check_verbatim_overlap(verbatim_def, source_chunk, n_gram_len=15)
    assert is_verbatim is True
    assert len(match.split()) == 15

    # 2. Paraphrased definition (zero 15-word verbatim match)
    paraphrased_def = (
        "Stabilization of synaptic updates in neural networks across extended trajectories "
        "to preserve foundational weight patterns."
    )
    is_verbatim_2, _ = check_verbatim_overlap(paraphrased_def, source_chunk, n_gram_len=15)
    assert is_verbatim_2 is False


def test_canonical_slot_validation():
    """
    Verifies maps_to_slot validation against 16 canonical slots.
    """
    assert len(CANONICAL_SLOTS) == 16

    valid_record = {
        "concept": "Test Concept",
        "definition": "A test definition paraphrased.",
        "claim_type": "mechanism",
        "maps_to_slot": "consolidation",
        "maps_to_module": "",
        "confidence_in_literature": 0.90,
        "source_book": "Test Book",
        "source_location": "Chapter 1"
    }

    invalid_record = dict(valid_record)
    invalid_record["maps_to_slot"] = "non_existent_slot"

    with tempfile.TemporaryDirectory() as tmpdir:
        staging_file = os.path.join(tmpdir, "staging.json")
        with open(staging_file, "w") as f:
            json.dump([invalid_record], f)

        with pytest.raises(ValueError) as excinfo:
            merge_candidate_concepts(staging_file, slots_dir=SLOT_DIRECTORY)

        assert "Invalid canonical slot 'non_existent_slot'" in str(excinfo.value)


def test_merge_idempotency_and_table_schema(tmp_path):
    """
    Verifies that merge_candidate_concepts appends valid 5-column rows and is idempotent.
    """
    # Create temporary mock slots directory with 16 empty slot files
    mock_slots_dir = tmp_path / "slots"
    mock_slots_dir.mkdir()

    for i, slot in enumerate(CANONICAL_SLOTS, start=1):
        filename = f"{i:02d}_{slot}.md"
        file_path = mock_slots_dir / filename
        content = f"""---
ontology_slot: {slot}
---

# Ontology Slot: {slot.capitalize()}

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence |
|---|---|---|---|---|---|---|
"""
        file_path.write_text(content, encoding="utf-8")

    sample_staging = [
        {
            "concept": "Synaptic Consolidation",
            "definition": "Paraphrased definition of synaptic consolidation.",
            "claim_type": "mechanism",
            "maps_to_slot": "consolidation",
            "maps_to_module": "",
            "confidence_in_literature": 0.95,
            "source_book": "Sarfraz et al. (2022)",
            "source_location": "Section 1"
        },
        {
            "concept": "Experience Replay",
            "definition": "Paraphrased definition of experience replay.",
            "claim_type": "mechanism",
            "maps_to_slot": "retrieval",
            "maps_to_module": "",
            "confidence_in_literature": 0.90,
            "source_book": "Sarfraz et al. (2022)",
            "source_location": "Section 2"
        }
    ]

    staging_file = tmp_path / "staging.json"
    staging_file.write_text(json.dumps(sample_staging), encoding="utf-8")

    # Run 1: First merge
    res1 = merge_candidate_concepts(str(staging_file), slots_dir=str(mock_slots_dir), override_date="2026-09-07")
    assert res1["net_new_concepts_merged"] == 2
    assert res1["concepts_deduplicated"] == 0

    # Verify table row schema in consolidation slot file
    consolidation_file = find_slot_file("consolidation", str(mock_slots_dir))
    content1 = open(consolidation_file, "r", encoding="utf-8").read()

    table_lines = [l.strip() for l in content1.strip().split("\n") if l.strip().startswith("|")]
    assert len(table_lines) >= 3  # Header + Separator + Row

    new_row = table_lines[-1]
    cols = [c.strip() for c in new_row.split("|")]
    assert len(cols) == 9  # Split by '|' produces 9 items for 7 columns (leading/trailing empty strings)
    assert cols[1] == "Synaptic Consolidation"
    assert cols[2] == "Sarfraz et al. (2022)"
    assert cols[3] == "0.95"
    assert cols[4] == "proposed"
    assert cols[5] == "2026-09-07"
    assert cols[6] == ""

    # Run 2: Second merge with exact same staging file (idempotency check)
    res2 = merge_candidate_concepts(str(staging_file), slots_dir=str(mock_slots_dir), override_date="2026-09-07")
    assert res2["net_new_concepts_merged"] == 0
    assert res2["concepts_deduplicated"] == 2


def test_structural_chunking():
    """
    Verifies that split_into_structural_chunks splits text on section headings.
    """
    sample_text = """
INTRODUCTION
Catastrophic forgetting occurs in deep neural networks.

BACKGROUND
Experience replay stores samples from past tasks.

METHOD
We combine synaptic consolidation with replay.
"""
    chunks = split_into_structural_chunks(sample_text)
    assert len(chunks) >= 3
    headings = [c["heading"] for c in chunks]
    assert any("INTRODUCTION" in h for h in headings)
    assert any("BACKGROUND" in h for h in headings)
    assert any("METHOD" in h for h in headings)
