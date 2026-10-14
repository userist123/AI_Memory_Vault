"""purge_rejected_rows.py may not delete from the canonical slots without a disposition manifest.

`--apply` was the only thing between a row selection and deleting from the
ontology. These tests point CANONICAL_SLOT_DIRECTORY at a temporary directory
(as the merge and promotion gates' tests do), so the real slots are never touched.
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


def test_targets_carrying_dispositions_from_a_manifest_are_let_through(world, tmp_path):
    module, slots, slot_file = world
    manifest = _manifest(tmp_path, [QUALIA, CHUNK])
    result = module.purge_rows(DECIDED, slots_dir=str(slots), apply=True, from_disposition_manifest=True,
                               manifest_path=str(manifest))
    assert result["deleted_count"] == 1
    assert "qualia" not in slot_file.read_text(encoding="utf-8")
    assert "chunk" in slot_file.read_text(encoding="utf-8")


def test_command_line_with_the_manifest_switched_off_is_refused(world, monkeypatch):
    """`--disposition-manifest ''` used to switch the only protection off."""
    module, slots, slot_file = world
    before = _digest(slot_file)
    monkeypatch.setattr(sys, "argv", ["purge_rejected_rows.py", "--batch", "A", "--apply",
                                       "--disposition-manifest", "", "--slots-dir", str(slots)])
    with pytest.raises(SystemExit) as exc:  # main() reports the refusal on stderr and exits 2
        module.main()
    assert exc.value.code == 2
    assert _digest(slot_file) == before


def test_negative_control_the_gate_is_what_stops_the_write(world, monkeypatch):
    """With the gate's directory test switched off the same call deletes the row,
    so the refusals above come from the gate and not from some other failure."""
    module, slots, slot_file = world
    monkeypatch.setattr(module, "_same_directory", lambda a, b: False)
    result = module.purge_rows(PLAIN, slots_dir=str(slots), apply=True)
    assert result["deleted_count"] == 1
    assert "qualia" not in slot_file.read_text(encoding="utf-8")


# --- --apply on the canonical slots needs a manifest that matches them -----------------
# The gate in purge_rows() only checks that each target carries a disposition, so a
# self-authored one-row manifest passed through --disposition-file used to delete from
# the canonical slots. apply_row_disposition.py validates its manifest against the slots
# on disk; main() now does too, before anything is written.

QUALIA = {"slot_file": "03_ontology.md", "line": 9, "concept": "qualia", "status": "proposed",
          "disposition": "DELETE", "reason": "rejected in review"}
CHUNK = {"slot_file": "03_ontology.md", "line": 10, "concept": "chunk", "status": "proposed",
         "disposition": "KEEP_PROPOSED", "reason": "kept"}


def _manifest(tmp_path: pathlib.Path, rows) -> pathlib.Path:
    path = tmp_path / "manifest.json"
    path.write_text(json.dumps({"schema_version": "ontology-row-disposition.v1", "rows": rows}), encoding="utf-8")
    return path


def _run(module, monkeypatch, *argv):
    monkeypatch.setattr(sys, "argv", ["purge_rejected_rows.py", *argv])
    return module.main()


def test_a_partial_manifest_is_refused_on_canonical_apply_and_nothing_is_written(world, tmp_path, monkeypatch, capsys):
    module, slots, slot_file = world
    before = _digest(slot_file)
    manifest = _manifest(tmp_path, [QUALIA])            # one row; the slots hold two
    with pytest.raises(SystemExit) as exc:
        _run(module, monkeypatch, "--disposition-file", str(manifest), "--slots-dir", str(slots), "--apply")
    assert exc.value.code not in (0, None)
    assert "does not match the slots" in capsys.readouterr().err
    assert _digest(slot_file) == before


def test_a_manifest_that_disagrees_with_disk_is_refused(world, tmp_path, monkeypatch):
    module, slots, slot_file = world
    before = _digest(slot_file)
    wrong = dict(CHUNK, concept="not-on-disk")
    manifest = _manifest(tmp_path, [QUALIA, wrong])
    with pytest.raises(SystemExit) as exc:
        _run(module, monkeypatch, "--disposition-file", str(manifest), "--slots-dir", str(slots), "--apply")
    assert exc.value.code not in (0, None)
    assert _digest(slot_file) == before


def test_a_partial_manifest_still_dry_runs(world, tmp_path, monkeypatch, capsys):
    module, slots, slot_file = world
    before = _digest(slot_file)
    manifest = _manifest(tmp_path, [QUALIA])
    _run(module, monkeypatch, "--disposition-file", str(manifest), "--slots-dir", str(slots), "--json")
    out = json.loads(capsys.readouterr().out)
    assert out["dry_run"] is True and out["deleted_count"] == 1
    assert _digest(slot_file) == before


def test_a_full_matching_manifest_still_applies(world, tmp_path, monkeypatch, capsys):
    module, slots, slot_file = world
    manifest = _manifest(tmp_path, [QUALIA, CHUNK])
    _run(module, monkeypatch, "--disposition-file", str(manifest), "--slots-dir", str(slots), "--apply", "--json")
    out = json.loads(capsys.readouterr().out)
    assert out["deleted_count"] == 1
    text = slot_file.read_text(encoding="utf-8")
    assert "qualia" not in text and "chunk" in text


def test_batch_mode_on_canonical_validates_the_consulted_manifest(world, tmp_path, monkeypatch):
    """--batch/--concept-file consult --disposition-manifest; on a canonical apply it must match too."""
    module, slots, slot_file = world
    before = _digest(slot_file)
    concepts = tmp_path / "concepts.json"
    concepts.write_text(json.dumps([{"slot_file": "03_ontology.md", "concept": "qualia"}]), encoding="utf-8")
    manifest = _manifest(tmp_path, [QUALIA])
    with pytest.raises(SystemExit) as exc:
        _run(module, monkeypatch, "--concept-file", str(concepts), "--disposition-manifest", str(manifest),
             "--slots-dir", str(slots), "--apply")
    assert exc.value.code not in (0, None)
    assert _digest(slot_file) == before


def test_apply_on_a_copy_is_not_held_to_the_canonical_check(world, tmp_path, monkeypatch, capsys):
    module, _, _ = world
    copy = tmp_path / "copy"
    copy.mkdir()
    (copy / "03_ontology.md").write_text(SLOT, encoding="utf-8", newline="")
    manifest = _manifest(tmp_path, [QUALIA])
    _run(module, monkeypatch, "--disposition-file", str(manifest), "--slots-dir", str(copy), "--apply", "--json")
    assert json.loads(capsys.readouterr().out)["deleted_count"] == 1


def test_a_python_caller_of_purge_rows_is_gated_like_the_cli(world, tmp_path):
    """R-08: the library entry point validates the manifest against the slots too; dispositions
    on the targets alone (or no manifest path at all) are not a decision on the canonical slots."""
    module, slots, slot_file = world
    before = _digest(slot_file)
    with pytest.raises(module.UngatedCanonicalWrite):
        module.purge_rows(DECIDED, slots_dir=str(slots), apply=True, from_disposition_manifest=True)
    partial = _manifest(tmp_path, [QUALIA])
    with pytest.raises(module.UngatedCanonicalWrite):
        module.purge_rows(DECIDED, slots_dir=str(slots), apply=True, from_disposition_manifest=True,
                          manifest_path=str(partial))
    assert _digest(slot_file) == before
