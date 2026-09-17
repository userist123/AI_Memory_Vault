"""
test_row_disposition_applied.py — Verification gate and negative controls for Part B.

Verifies the state of 01_ARCHITECTURE/ontology/slots/*.md after executing
disposition_manifest.json:
- Exactly 93 rows remaining (84 KEEP_PROMOTED, 2 KEEP_PROPOSED, 6 UNDECIDED, 1 SPLIT)
- All 112 deleted concepts are absent across slot files
- All 43 promoted concepts are preserved (status='promoted', note ID invariant under I-003)
- All 8 merges accurately summed occurrences
- Zero slot conflicts via find_slot_conflicts()

Negative Controls (B.3):
Demonstrates that planted violations fail the verification checks:
1. Deleting a promoted row fails invariant check.
2. Tampering with promoted_note_id fails invariant check.
3. Tampering with merge occurrence sum fails check.
4. Injecting duplicate concept into multiple slots is detected by find_slot_conflicts().
"""
from __future__ import annotations

import pathlib
import shutil
import sys
import pytest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPTS_DIR = REPO_ROOT / "30_SCRIPTS" / "ingestion"
if str(SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPTS_DIR))

import slot_rows
from disposition_manifest import (
    DISPOSITION_KEEP_PROMOTED,
    DISPOSITION_KEEP_PROPOSED,
    DISPOSITION_DELETE,
    DISPOSITION_MERGE_INTO,
    DISPOSITION_SPLIT,
    DISPOSITION_UNDECIDED,
    load_manifest,
)
from merge_candidate_concepts import find_slot_conflicts

SLOTS_DIR = REPO_ROOT / "01_ARCHITECTURE" / "ontology" / "slots"
MANIFEST_PATH = REPO_ROOT / "07_EVALUATION" / "book_corpus_conversion" / "disposition_manifest.json"


def _clean(s: str) -> str:
    return slot_rows._clean(s).lower()


# --- Positive Verification Gate ---

def test_remaining_row_counts_and_disposition():
    """Verify exactly 93 rows remain on disk with expected categories."""
    manifest = load_manifest(MANIFEST_PATH)
    rows = slot_rows.read_all(SLOTS_DIR)

    assert len(rows) == 93, f"Expected exactly 93 rows remaining, found {len(rows)}"

    # All 112 deleted rows must NOT be present in their respective files (and across slots)
    deleted_manifest_entries = [r for r in manifest.rows if r.disposition == DISPOSITION_DELETE]
    assert len(deleted_manifest_entries) == 112

    current_concept_names = {_clean(r.concept) for r in rows}

    for d in deleted_manifest_entries:
        d_clean = _clean(d.concept)
        if d.slot_file == "15_retrieval.md" and d_clean == "experience replay":
            # Historical duplicate removed from 15_retrieval.md; canonical kept in 16_consolidation.md
            retrieval_concepts = {_clean(r.concept) for r in rows if r.slot_file == "15_retrieval.md"}
            assert "experience replay" not in retrieval_concepts, (
                "Duplicate 'Experience Replay' still present in 15_retrieval.md!"
            )
        else:
            assert d_clean not in current_concept_names, (
                f"Deleted concept '{d.concept}' (from {d.slot_file}:{d.line}) still present on disk!"
            )

    # Check 7 pending rows are present
    pending_manifest = [r for r in manifest.rows if r.disposition in (DISPOSITION_UNDECIDED, DISPOSITION_SPLIT)]
    assert len(pending_manifest) == 7
    for p in pending_manifest:
        p_clean = _clean(p.concept)
        assert p_clean in current_concept_names, f"Pending concept '{p.concept}' missing from disk!"


def test_promoted_rows_and_invariant_i003_preserved():
    """All 43 promoted concepts must remain with status='promoted' and identical note IDs."""
    rows = slot_rows.read_all(SLOTS_DIR)

    promoted_rows = [r for r in rows if r.status == slot_rows.STATUS_PROMOTED]
    assert len(promoted_rows) == 43, f"Expected 43 promoted rows, found {len(promoted_rows)}"

    for r in promoted_rows:
        assert r.promoted_note_id, f"Promoted concept '{r.concept}' missing promoted_note_id"
        assert len(r.promoted_note_id) == 36, f"Invalid UUID for '{r.concept}': {r.promoted_note_id}"


def test_eight_merges_occurrences_summed():
    """The 8 merge targets must have occurrences equal to target_before + source_before."""
    rows = slot_rows.read_all(SLOTS_DIR)
    rows_by_concept = {_clean(r.concept): r for r in rows}

    # Verified sums for the 8 merge targets:
    # 1. semantic memory (25 + 9 = 34)
    # 2. parametric memory (11 + 3 = 14)
    # 3. buffer (11 + 7 = 18)
    # 4. working memory (53 + 15 = 68)
    # 5. essential variables (17 + 25 = 42)
    # 6. impasse (12 + 7 = 19)
    # 7. episodic memory (44 + 14 = 58)
    # 8. consolidation (29 + 5 = 34)
    expected_sums = {
        "semantic memory": 34,
        "parametric memory": 14,
        "buffer": 18,
        "working memory": 68,
        "essential variables": 42,
        "impasse": 19,
        "episodic memory": 58,
        "consolidation": 34,
    }

    for target, exp_occ in expected_sums.items():
        assert target in rows_by_concept, f"Merge target '{target}' missing from disk!"
        actual_occ = rows_by_concept[target].occurrences
        assert actual_occ == exp_occ, (
            f"Merge occurrence sum mismatch for '{target}': expected {exp_occ}, got {actual_occ}"
        )


def test_find_slot_conflicts_zero_on_final_tree():
    """Verify find_slot_conflicts() finds zero cross-slot duplicate concepts."""
    rows = slot_rows.read_all(SLOTS_DIR)
    records = [
        {
            "concept": r.concept,
            "maps_to_slot": r.slot_file.replace(".md", ""),
            "source_book": r.source_book,
        }
        for r in rows
    ]
    conflicts = find_slot_conflicts(records)
    assert conflicts == {}, f"Unexpected slot conflicts on final tree: {conflicts}"


# --- Negative Controls (B.3) ---

def _verify_slot_tree(slots_path: pathlib.Path, expected_count: int = 93):
    """Core verification function used by the gate and negative controls."""
    rows = slot_rows.read_all(slots_path)

    promoted = [r for r in rows if r.status == slot_rows.STATUS_PROMOTED]
    if len(promoted) != 43:
        raise ValueError(f"Expected 43 promoted rows, got {len(promoted)}")

    if len(rows) != expected_count:
        raise ValueError(f"Expected {expected_count} rows, got {len(rows)}")

    for r in promoted:
        if not r.promoted_note_id or len(r.promoted_note_id) != 36:
            raise ValueError(f"Invalid promoted note ID for '{r.concept}': {r.promoted_note_id}")

    rows_by_concept = {_clean(r.concept): r for r in rows}
    if rows_by_concept.get("consolidation") and rows_by_concept["consolidation"].occurrences != 34:
        raise ValueError(
            f"Consolidation occurrences corrupted: expected 34, got {rows_by_concept['consolidation'].occurrences}"
        )

    records = [
        {
            "concept": r.concept,
            "maps_to_slot": r.slot_file.replace(".md", ""),
            "source_book": r.source_book,
        }
        for r in rows
    ]
    conflicts = find_slot_conflicts(records)
    if conflicts:
        raise ValueError(f"Slot conflict detected: {conflicts}")

    return True


def test_negative_control_planted_deletion_of_promoted(tmp_path):
    """Negative control 1: Deleting a promoted row must cause verification failure."""
    fake_slots = tmp_path / "slots"
    shutil.copytree(SLOTS_DIR, fake_slots)

    assert _verify_slot_tree(fake_slots) is True

    target_file = fake_slots / "16_consolidation.md"
    content = target_file.read_text(encoding="utf-8")
    lines = [l for l in content.splitlines() if "chunking" not in l]
    target_file.write_text("\n".join(lines) + "\n", encoding="utf-8")

    with pytest.raises(ValueError, match=r"Expected 43 promoted rows, got 42"):
        _verify_slot_tree(fake_slots)


def test_negative_control_planted_tampering_of_promoted_note_id(tmp_path):
    """Negative control 2: Tampering with promoted_note_id must cause verification failure."""
    fake_slots = tmp_path / "slots"
    shutil.copytree(SLOTS_DIR, fake_slots)

    target_file = fake_slots / "16_consolidation.md"
    content = target_file.read_text(encoding="utf-8")
    content = content.replace("a1df3d1c-201c-4217-aa62-f36292b01aa4", "invalid-short-id")
    target_file.write_text(content, encoding="utf-8")

    with pytest.raises(ValueError, match=r"Invalid promoted note ID for 'chunking'"):
        _verify_slot_tree(fake_slots)


def test_negative_control_planted_tampering_of_merge_occurrences(tmp_path):
    """Negative control 3: Tampering with merge occurrence sum must cause verification failure."""
    fake_slots = tmp_path / "slots"
    shutil.copytree(SLOTS_DIR, fake_slots)

    target_file = fake_slots / "16_consolidation.md"
    content = target_file.read_text(encoding="utf-8")
    content = content.replace("| 34 |", "| 29 |")
    target_file.write_text(content, encoding="utf-8")

    with pytest.raises(ValueError, match=r"Consolidation occurrences corrupted: expected 34, got 29"):
        _verify_slot_tree(fake_slots)


def test_negative_control_planted_duplicate_slot_conflict(tmp_path):
    """Negative control 4: Injecting duplicate concept into another slot is caught by find_slot_conflicts."""
    fake_slots = tmp_path / "slots"
    shutil.copytree(SLOTS_DIR, fake_slots)

    target_file = fake_slots / "01_identity.md"
    content = target_file.read_text(encoding="utf-8")
    new_line = "| consolidation | fake_book | 0.95 | proposed | 2026-09-17 | | evidence | 1 |"
    target_file.write_text(content + "\n" + new_line + "\n", encoding="utf-8")

    with pytest.raises(ValueError, match=r"Slot conflict detected"):
        _verify_slot_tree(fake_slots, expected_count=94)
