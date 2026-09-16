import json
import pathlib
import sys
import pytest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.append(str(REPO_ROOT / "30_SCRIPTS/ingestion"))

from disposition_manifest import (
    DISPOSITION_SCHEMA_VERSION,
    DISPOSITION_KEEP_PROMOTED,
    DISPOSITION_KEEP_PROPOSED,
    DISPOSITION_DELETE,
    DISPOSITION_MERGE_INTO,
    DISPOSITION_SPLIT,
    DISPOSITION_UNDECIDED,
    KNOWN_DISPOSITIONS,
    DispositionError,
    RowDisposition,
    DispositionManifest,
    load_manifest,
)
import slot_rows

MANIFEST_PATH = REPO_ROOT / "07_EVALUATION/book_corpus_conversion/disposition_manifest.json"
SLOTS_DIR = REPO_ROOT / "01_ARCHITECTURE/ontology/slots"


def test_manifest_schema_and_constants():
    assert DISPOSITION_SCHEMA_VERSION == "ontology-row-disposition.v1"
    assert len(KNOWN_DISPOSITIONS) == 6
    assert {
        DISPOSITION_KEEP_PROMOTED,
        DISPOSITION_KEEP_PROPOSED,
        DISPOSITION_DELETE,
        DISPOSITION_MERGE_INTO,
        DISPOSITION_SPLIT,
        DISPOSITION_UNDECIDED,
    } == KNOWN_DISPOSITIONS


def test_manifest_loads_and_row_count():
    assert MANIFEST_PATH.exists(), f"Missing {MANIFEST_PATH}"
    mf = load_manifest(MANIFEST_PATH)
    assert len(mf) == 213
    assert len(mf.rows) == 213


def test_manifest_disposition_counts():
    mf = load_manifest(MANIFEST_PATH)
    counts = mf.counts()
    assert counts == {
        DISPOSITION_KEEP_PROMOTED: 84,
        DISPOSITION_DELETE: 112,
        DISPOSITION_MERGE_INTO: 8,
        DISPOSITION_UNDECIDED: 6,
        DISPOSITION_KEEP_PROPOSED: 2,
        DISPOSITION_SPLIT: 1,
    }
    assert sum(counts.values()) == 213


def test_manifest_validates_against_slots():
    mf = load_manifest(MANIFEST_PATH)
    # Must validate bit-for-bit against disk without raising
    mf.validate_against_slots(SLOTS_DIR)


def test_manifest_merge_into_targets_promoted():
    mf = load_manifest(MANIFEST_PATH)
    disk_rows = slot_rows.read_all(SLOTS_DIR)
    promoted_concepts = {
        slot_rows._clean(r.concept).lower()
        for r in disk_rows
        if r.status == slot_rows.STATUS_PROMOTED
    }

    assert len(mf.merge_rows()) == 8
    for mr in mf.merge_rows():
        assert mr.merge_into is not None
        target_norm = slot_rows._clean(mr.merge_into).lower()
        assert target_norm in promoted_concepts, (
            f"MERGE_INTO target '{mr.merge_into}' for '{mr.concept}' not found as promoted concept on disk"
        )


def test_manifest_no_promoted_has_delete():
    mf = load_manifest(MANIFEST_PATH)
    for r in mf.rows:
        if r.status == slot_rows.STATUS_PROMOTED:
            assert r.disposition != DISPOSITION_DELETE, (
                f"Promoted concept '{r.concept}' at {r.slot_file}:{r.line} assigned DELETE disposition"
            )


def test_manifest_mandatory_reasons():
    mf = load_manifest(MANIFEST_PATH)
    for r in mf.rows:
        assert r.reason and r.reason.strip(), (
            f"Row {r.slot_file}:{r.line} '{r.concept}' has empty reason"
        )


def test_manifest_familiarity_single_split():
    mf = load_manifest(MANIFEST_PATH)
    fam_rows = [r for r in mf.rows if "familiarity" in r.concept.lower()]
    assert len(fam_rows) == 1
    fam = fam_rows[0]
    assert fam.slot_file == "10_confidence.md"
    assert fam.line == 30
    assert fam.disposition == DISPOSITION_SPLIT
    assert "feeling of knowing" in fam.reason
    assert "familiarity buffer" in fam.reason


def test_row_disposition_validation_errors():
    # Empty slot_file
    with pytest.raises(DispositionError, match="must specify slot_file"):
        RowDisposition("", 10, "foo", "proposed", DISPOSITION_DELETE, "reason").validate()

    # Invalid line
    with pytest.raises(DispositionError, match="line must be positive integer"):
        RowDisposition("01_identity.md", 0, "foo", "proposed", DISPOSITION_DELETE, "reason").validate()

    # Empty concept
    with pytest.raises(DispositionError, match="concept cannot be empty"):
        RowDisposition("01_identity.md", 10, "", "proposed", DISPOSITION_DELETE, "reason").validate()

    # Unknown disposition
    with pytest.raises(DispositionError, match="unknown disposition"):
        RowDisposition("01_identity.md", 10, "foo", "proposed", "INVALID_DISP", "reason").validate()

    # Empty reason
    with pytest.raises(DispositionError, match="reason is mandatory"):
        RowDisposition("01_identity.md", 10, "foo", "proposed", DISPOSITION_DELETE, "  ").validate()

    # Promoted with DELETE
    with pytest.raises(DispositionError, match="promoted rows cannot receive DELETE"):
        RowDisposition("01_identity.md", 10, "foo", "promoted", DISPOSITION_DELETE, "reason").validate()

    # MERGE_INTO without merge_into target
    with pytest.raises(DispositionError, match="MERGE_INTO must specify merge_into target"):
        RowDisposition("01_identity.md", 10, "foo", "proposed", DISPOSITION_MERGE_INTO, "reason").validate()


def test_manifest_duplicate_prevention():
    r1 = RowDisposition("01_identity.md", 10, "concept_a", "proposed", DISPOSITION_DELETE, "reason")
    r2 = RowDisposition("01_identity.md", 10, "concept_b", "proposed", DISPOSITION_DELETE, "reason")
    with pytest.raises(DispositionError, match="Duplicate entry for location"):
        DispositionManifest([r1, r2])

    r3 = RowDisposition("01_identity.md", 11, "concept_a", "proposed", DISPOSITION_DELETE, "reason")
    with pytest.raises(DispositionError, match="Duplicate entry for concept"):
        DispositionManifest([r1, r3])


def test_round_trip_serialization(tmp_path):
    mf = load_manifest(MANIFEST_PATH)
    out_file = tmp_path / "test_manifest.json"
    mf.save(out_file)

    mf_reloaded = load_manifest(out_file)
    assert len(mf_reloaded) == len(mf)
    assert mf_reloaded.counts() == mf.counts()
    assert mf_reloaded.source_disposition == mf.source_disposition
    assert mf_reloaded.source_verdicts == mf.source_verdicts
