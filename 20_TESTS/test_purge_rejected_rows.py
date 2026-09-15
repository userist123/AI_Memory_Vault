import os
import sys
import tempfile
import pathlib
import pytest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.append(str(REPO_ROOT / "30_SCRIPTS/ingestion"))

from purge_rejected_rows import (
    purge_rows, normalize_concept_name, load_catalog, format_report
)

SAMPLE_SLOT_MARKDOWN = """---
id: slot-03-ontology
ontology_slot: ontology
---

# Ontology Slot: Ontology

## Question
What does each memory type mean?

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| semantic memory | why_we_forget | 0.90 | promoted | 2026-09-12 | note-12345 | general knowledge store | 25 |
| qualia | quantum_framework | 0.88 | proposed | 2026-09-12 | | speculative qualia field | 4 |
| topological | quantum_framework | 0.88 | proposed | 2026-09-12 | | topological quantum theory | 3 |
| artificial | quantum_framework | 0.88 | proposed | 2026-09-12 | | artificial consciousness | 5 |
| chunk | wcs_1488 | 0.95 | proposed | 2026-09-12 | | symbolic chunk package | 7 |
"""

SAMPLE_SLOT_WITH_NOTES = """---
id: slot-01-identity
---

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| self-model | cognitive_arch | 0.95 | proposed | 2026-09-12 | note-9999 | internal self representation | 12 |
| agentic memory | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | agent memory umbrella term | 4 |
"""

SAMPLE_SLOT_WITH_UNVERIFIED = """---
id: slot-16-consolidation
---

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| Synaptic Consolidation | Sarfraz et al. (2022) | 0.95 | unverified_source | 2026-09-07 | | | |
| Stochastic Weight Consolidation | Sarfraz et al. (2022) | 0.85 | unverified_source | 2026-09-07 | | | |
| promoted consolidation | neuro_text | 0.95 | promoted | 2026-09-12 | note-1111 | verified consolidation | 15 |
| unverified with note | neuro_text | 0.90 | unverified_source | 2026-09-07 | note-2222 | linked note guard test | 2 |
"""


@pytest.fixture
def temp_slots_dir(tmp_path):
    slot_file = tmp_path / "03_ontology.md"
    slot_file.write_text(SAMPLE_SLOT_MARKDOWN, encoding="utf-8")
    
    slot_file2 = tmp_path / "01_identity.md"
    slot_file2.write_text(SAMPLE_SLOT_WITH_NOTES, encoding="utf-8")

    slot_file3 = tmp_path / "16_consolidation.md"
    slot_file3.write_text(SAMPLE_SLOT_WITH_UNVERIFIED, encoding="utf-8")
    return tmp_path


def test_refuse_promoted_status(temp_slots_dir):
    """
    Guard 1: The purger must refuse to delete a row if status == 'promoted'.
    """
    targets = [{"slot_file": "03_ontology.md", "concept": "semantic memory"}]
    res = purge_rows(targets, slots_dir=str(temp_slots_dir), apply=True)

    assert res["deleted_count"] == 0
    assert res["refused_count"] == 1
    assert "promoted" in res["refused"][0]["reason"]

    # Verify file content is unchanged for semantic memory
    content = (temp_slots_dir / "03_ontology.md").read_text(encoding="utf-8")
    assert "semantic memory" in content


def test_refuse_non_empty_promoted_note_id(temp_slots_dir):
    """
    Guard 2: The purger must refuse to delete a row if promoted_note_id is non-empty,
    even if status is 'proposed'.
    """
    targets = [{"slot_file": "01_identity.md", "concept": "self-model"}]
    res = purge_rows(targets, slots_dir=str(temp_slots_dir), apply=True)

    assert res["deleted_count"] == 0
    assert res["refused_count"] == 1
    assert "promoted_note_id is non-empty" in res["refused"][0]["reason"]

    # Verify file content still has self-model
    content = (temp_slots_dir / "01_identity.md").read_text(encoding="utf-8")
    assert "self-model" in content


def test_delete_by_concept_name_when_line_numbers_shift(temp_slots_dir):
    """
    Deletions must match by concept name, not line number.
    Deleting multiple rows in sequence must succeed even as lines shift.
    """
    # Delete 'qualia' and 'artificial' (skipping 'topological' between them)
    targets = [
        {"slot_file": "03_ontology.md", "concept": "qualia"},
        {"slot_file": "03_ontology.md", "concept": "artificial"}
    ]
    res = purge_rows(targets, slots_dir=str(temp_slots_dir), apply=True)

    assert res["deleted_count"] == 2
    assert res["refused_count"] == 0

    content = (temp_slots_dir / "03_ontology.md").read_text(encoding="utf-8")
    assert "qualia" not in content
    assert "artificial" not in content
    # 'topological' and 'chunk' must remain intact
    assert "topological" in content
    assert "chunk" in content
    assert "semantic memory" in content


def test_dry_run_leaves_files_untouched(temp_slots_dir):
    """
    By default (apply=False), dry-run must not write anything to disk.
    """
    orig_content = (temp_slots_dir / "03_ontology.md").read_text(encoding="utf-8")
    targets = [{"slot_file": "03_ontology.md", "concept": "qualia"}]
    
    res = purge_rows(targets, slots_dir=str(temp_slots_dir), apply=False)
    assert res["dry_run"] is True
    assert res["deleted_count"] == 1

    post_content = (temp_slots_dir / "03_ontology.md").read_text(encoding="utf-8")
    assert orig_content == post_content


def test_catalog_batches_structure():
    """
    Verify predefined batches A (36), B (62), C (23), D1 (5), D2 (3) in catalog.
    """
    catalog = load_catalog()
    assert "A" in catalog
    assert "B" in catalog
    assert "C" in catalog
    assert "D1" in catalog
    assert "D2" in catalog

    assert len(catalog["A"]) == 36
    assert len(catalog["B"]) == 62
    assert len(catalog["C"]) == 23
    assert len(catalog["D1"]) == 5
    assert len(catalog["D2"]) == 3

    # All items must have slot_file and concept
    for batch_name, items in catalog.items():
        for item in items:
            assert "slot_file" in item
            assert "concept" in item
            assert len(item["concept"]) > 0


def test_unverified_source_refused_without_flag(temp_slots_dir):
    """
    Without --allow-status, unverified_source rows must be refused by the default proposed guard.
    """
    targets = [{"slot_file": "16_consolidation.md", "concept": "Stochastic Weight Consolidation"}]
    res = purge_rows(targets, slots_dir=str(temp_slots_dir), apply=True)

    assert res["deleted_count"] == 0
    assert res["refused_count"] == 1
    assert "unverified_source" in res["refused"][0]["status"]
    assert "Status 'unverified_source' != 'proposed'" in res["refused"][0]["reason"]

    # File remains untouched
    content = (temp_slots_dir / "16_consolidation.md").read_text(encoding="utf-8")
    assert "Stochastic Weight Consolidation" in content


def test_unverified_source_deleted_with_flag(temp_slots_dir):
    """
    With --allow-status unverified_source, unverified_source rows are safely deleted.
    """
    targets = [{"slot_file": "16_consolidation.md", "concept": "Stochastic Weight Consolidation"}]
    res = purge_rows(
        targets,
        slots_dir=str(temp_slots_dir),
        apply=True,
        allow_status="unverified_source"
    )

    assert res["deleted_count"] == 1
    assert res["refused_count"] == 0
    assert res["allow_status"] == "unverified_source"

    # Row deleted from file
    content = (temp_slots_dir / "16_consolidation.md").read_text(encoding="utf-8")
    assert "Stochastic Weight Consolidation" not in content
    # Other rows remain
    assert "Synaptic Consolidation" in content


def test_promoted_refused_even_with_allow_status_flag(temp_slots_dir):
    """
    Unconditional Guard: even if --allow-status is passed, a promoted row is ALWAYS refused.
    """
    targets = [{"slot_file": "16_consolidation.md", "concept": "promoted consolidation"}]
    res = purge_rows(
        targets,
        slots_dir=str(temp_slots_dir),
        apply=True,
        allow_status="unverified_source"
    )

    assert res["deleted_count"] == 0
    assert res["refused_count"] == 1
    assert "Status 'promoted' is unconditionally protected" in res["refused"][0]["reason"]

    content = (temp_slots_dir / "16_consolidation.md").read_text(encoding="utf-8")
    assert "promoted consolidation" in content


def test_note_id_refused_even_with_allow_status_flag(temp_slots_dir):
    """
    Unconditional Guard: even if status matches allow_status, non-empty note_id is ALWAYS refused.
    """
    targets = [{"slot_file": "16_consolidation.md", "concept": "unverified with note"}]
    res = purge_rows(
        targets,
        slots_dir=str(temp_slots_dir),
        apply=True,
        allow_status="unverified_source"
    )

    assert res["deleted_count"] == 0
    assert res["refused_count"] == 1
    assert "promoted_note_id is non-empty ('note-2222')" in res["refused"][0]["reason"]

    content = (temp_slots_dir / "16_consolidation.md").read_text(encoding="utf-8")
    assert "unverified with note" in content


def test_allow_status_appears_in_report(temp_slots_dir):
    """
    Report Formatter: when allow_status is invoked, it must be reported in the output text.
    """
    targets = [{"slot_file": "16_consolidation.md", "concept": "Stochastic Weight Consolidation"}]
    res = purge_rows(
        targets,
        slots_dir=str(temp_slots_dir),
        apply=False,
        allow_status="unverified_source"
    )

    rep = format_report(res)
    assert "Explicit Allow Status: unverified_source [INVOKED]" in rep


def test_merge_into_refused(temp_slots_dir):
    """
    Fix (a): MERGE_INTO entries must be refused as a simple deletion,
    explaining that merge transfers occurrences and is not implemented.
    """
    targets = [
        {
            "slot_file": "03_ontology.md",
            "concept": "qualia",
            "disposition": "MERGE_INTO",
            "merge_into": "semantic memory"
        }
    ]
    res = purge_rows(targets, slots_dir=str(temp_slots_dir), apply=True)

    assert res["deleted_count"] == 0
    assert res["refused_count"] == 1
    assert "Disposition is 'MERGE_INTO'" in res["refused"][0]["reason"]
    assert "merge transfers occurrences and is not implemented" in res["refused"][0]["reason"]

    content = (temp_slots_dir / "03_ontology.md").read_text(encoding="utf-8")
    assert "qualia" in content


def test_allow_status_extends_not_replaces(temp_slots_dir):
    """
    Fix (b): --allow-status must extend permitted statuses, not replace them.
    A single run with allow_status='unverified_source' must delete both proposed and unverified_source.
    """
    targets = [
        {"slot_file": "03_ontology.md", "concept": "qualia"},  # proposed
        {"slot_file": "16_consolidation.md", "concept": "Stochastic Weight Consolidation"},  # unverified_source
    ]
    res = purge_rows(
        targets,
        slots_dir=str(temp_slots_dir),
        apply=True,
        allow_status="unverified_source"
    )

    assert res["deleted_count"] == 2
    assert res["refused_count"] == 0
    assert "proposed" in res["allowed_statuses"]
    assert "unverified_source" in res["allowed_statuses"]

    content_03 = (temp_slots_dir / "03_ontology.md").read_text(encoding="utf-8")
    assert "qualia" not in content_03

    content_16 = (temp_slots_dir / "16_consolidation.md").read_text(encoding="utf-8")
    assert "Stochastic Weight Consolidation" not in content_16


def test_total_targets_reports_distinct_and_warns_duplicates():
    """
    Fix (c): total_targets reports distinct physical rows (122 for Batch ALL),
    and generates explicit warnings for the 7 duplicates.
    """
    catalog = load_catalog()
    all_targets = []
    for b in ["A", "B", "C", "D1", "D2"]:
        all_targets.extend(catalog[b])

    assert len(all_targets) == 129

    res = purge_rows(all_targets, slots_dir="01_ARCHITECTURE/ontology/slots", apply=False)

    assert res["input_targets_count"] == 129
    assert res["total_targets"] == 122
    assert res["duplicate_targets_count"] == 7
    assert len(res["duplicate_warnings"]) == 7

    rep = format_report(res)
    assert "Duplicate Targets: 7 (deduplicated)" in rep
    assert "WARNING: Duplicate target rows detected" in rep


def test_disposition_file_mode():
    """
    --disposition-file mode: loads disposition_manifest.json, acts only on DELETE,
    refuses MERGE_INTO, KEEP_PROMOTED, KEEP_PROPOSED, SPLIT, UNDECIDED, and has 0 not_found.
    """
    manifest_path = REPO_ROOT / "07_EVALUATION/book_corpus_conversion/disposition_manifest.json"
    assert manifest_path.exists(), "disposition_manifest.json must exist"

    from disposition_manifest import load_manifest
    mf = load_manifest(manifest_path)
    assert len(mf) == 213

    target_items = [r.to_dict() for r in mf.rows]
    res = purge_rows(
        target_items,
        slots_dir="01_ARCHITECTURE/ontology/slots",
        apply=False,
        from_disposition_manifest=True
    )

    assert res["total_targets"] == 213
    assert res["deleted_count"] == 112
    assert res["refused_count"] == 101
    assert res["not_found_count"] == 0
    assert res["files_modified_count"] == 16




# --- a selection must not outrank a decision ---------------------------------

def test_batch_mode_cannot_delete_what_the_manifest_protects(tmp_path):
    """Extending --allow-status made a single command reach every catalogued
    row, and the catalogue predates the disposition manifest: it cannot express
    that eight of those rows are merges whose occurrences would be lost, nor
    that the unverified_source triage recovered two of them.

    Measured before this guard: `--batch ALL --allow-status unverified_source`
    deleted 122 rows, ten of which the manifest protects.
    """
    import shutil, subprocess, sys, json, pathlib

    repo = pathlib.Path(__file__).resolve().parents[1]
    manifest = repo / "07_EVALUATION" / "book_corpus_conversion" / "disposition_manifest.json"
    if not manifest.exists():
        pytest.skip("disposition manifest not present")

    slots = tmp_path / "slots"
    shutil.copytree(repo / "01_ARCHITECTURE" / "ontology" / "slots", slots)

    def run(*extra):
        out = subprocess.run(
            [sys.executable, str(repo / "30_SCRIPTS" / "ingestion" / "purge_rejected_rows.py"),
             "--batch", "ALL", "--allow-status", "unverified_source",
             "--slots-dir", str(slots), "--json", *extra],
            capture_output=True, text=True, encoding="utf-8",
            env={**os.environ, "PYTHONIOENCODING": "utf-8"},
        ).stdout
        return json.loads(out[out.index("{"):])

    guarded = run()
    protected = {
        r["reason"].split("'")[1]
        for r in guarded["refused"]
    }
    assert guarded["deleted_count"] == 112, "batch mode must stop at the approved set"
    assert guarded["refused_count"] == 10
    assert protected == {"MERGE_INTO", "KEEP_PROPOSED"}

    # The old behaviour stays reachable, but only by asking for it.
    unguarded = run("--disposition-manifest", "")
    assert unguarded["deleted_count"] == 122


def test_the_manifest_and_batch_paths_agree_on_what_may_go(tmp_path):
    """Two selections, one decision. If these ever diverge, one of them is
    deleting a row nobody approved."""
    import shutil, subprocess, sys, json, pathlib

    repo = pathlib.Path(__file__).resolve().parents[1]
    manifest = repo / "07_EVALUATION" / "book_corpus_conversion" / "disposition_manifest.json"
    if not manifest.exists():
        pytest.skip("disposition manifest not present")

    def deleted_via(*args):
        slots = tmp_path / f"slots{abs(hash(args))}"
        shutil.copytree(repo / "01_ARCHITECTURE" / "ontology" / "slots", slots)
        out = subprocess.run(
            [sys.executable, str(repo / "30_SCRIPTS" / "ingestion" / "purge_rejected_rows.py"),
             *args, "--slots-dir", str(slots), "--json"],
            capture_output=True, text=True, encoding="utf-8",
            env={**os.environ, "PYTHONIOENCODING": "utf-8"},
        ).stdout
        payload = json.loads(out[out.index("{"):])
        return {(os.path.basename(r["file"]), r["concept"].strip().lower())
                for r in payload["deleted"]}

    assert deleted_via("--batch", "ALL", "--allow-status", "unverified_source") == \
           deleted_via("--disposition-file", str(manifest))
