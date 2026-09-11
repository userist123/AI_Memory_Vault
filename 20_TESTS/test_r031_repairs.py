from __future__ import annotations

import importlib.util
import json
import pathlib
import shutil
import sys


_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))


def _load(name: str, path: pathlib.Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def test_occurrences_count_distinct_source_sections_only():
    module = _load(
        "model_extract_concepts_r031",
        _REPO / "30_SCRIPTS" / "ingestion" / "model_extract_concepts.py",
    )
    base = {
        "concept": "Synaptic consolidation",
        "definition": "A process that stabilizes newly acquired information so it can persist as durable knowledge.",
        "confidence_in_literature": 0.8,
        "maps_to_slot": "consolidation",
    }
    rows = [
        dict(base, source_location="Section 1"),
        dict(base, source_location="Section 1"),
        dict(base, source_location="Section 2"),
        dict(base, source_location="Section 2"),
    ]

    merged, collapsed = module.deduplicate(rows)

    assert collapsed == 3
    assert len(merged) == 1
    assert merged[0]["occurrences"] == 2
    assert merged[0]["also_found_in"] == ["Section 2"]


def test_promotion_emits_synapse_store_compatible_relation(tmp_path):
    module = _load(
        "promote_candidate_concept_r031",
        _REPO / "30_SCRIPTS" / "ingestion" / "promote_candidate_concept.py",
    )

    slots = tmp_path / "slots"
    notes = tmp_path / "knowledge"
    slots.mkdir()
    notes.mkdir()

    slot = slots / "06_procedures.md"
    slot.write_text(
        "---\n"
        "id: slot-06-procedures\n"
        "ontology_slot: procedures\n"
        "type: ontology_definition\n"
        "lifecycle: ACTIVE\n"
        "provenance:\n"
        "  source_type: design\n"
        "  source_author: TEST\n"
        "confidence: 1.00\n"
        "status: scaffold\n"
        "tags: [ontology]\n"
        "relations: []\n"
        "created: 2026-09-07\n"
        "---\n\n"
        "# Ontology Slot: Procedures\n\n"
        "## Candidate concepts\n"
        "| concept | source_book | confidence | status | date_added | promoted_note_id |\n"
        "|---|---|---|---|---|---|\n"
        "| Reservoir sampling | test-book | 0.90 | proposed | 2026-09-09 | |\n",
        encoding="utf-8",
    )

    result = module.promote_candidate_concept(
        "Reservoir sampling",
        "procedures",
        slots_dir=str(slots),
        notes_dir=str(notes),
        judgment_reasoning="test",
    )

    note = pathlib.Path(result["note_filepath"])
    text = note.read_text(encoding="utf-8")
    assert "type: part_of" in text
    assert 'target_id: "slot-06-procedures"' in text
    assert "relation: derived_from" not in text
    assert 'target: "' not in text
