"""D-01: FileStorageEngine must account for every .md file it skips while indexing.

Before the fix `_initialize_index` dropped unreadable, malformed and id-less files with a
bare `continue`: only one class (Malformed YAML) left an audit line, the others left no
trace at all. The engine now keeps a `load_report` and emits one `storage_skipped` audit
event per skipped file (path, reason, exception type; never the content).
"""
from __future__ import annotations

import uuid
from collections import Counter
from pathlib import Path

import pytest

import memory_controller.storage.file_engine as file_engine_mod
from memory_controller.storage.file_engine import FileStorageEngine
from memory_vault_fixture import make_vault, note_text

PLANTED = {
    "corrupt_yaml.md": ("---\nid: aaa\ntitle: [unclosed\n---\nbody\n", "malformed_yaml"),
    "no_delims.md": ("no frontmatter at all\n", "malformed_yaml"),
    "bad_utf8.md": (None, "decode_error"),
    "list_id.md": ("---\nid: [a, b]\ntype: knowledge\nlifecycle: ACTIVE\n---\nx\n", "invalid_id"),
    "scalar_fm.md": ("---\njust a string\n---\nx\n", "malformed_yaml"),
    "no_id.md": ("---\ntype: knowledge\nlifecycle: ACTIVE\n---\nx\n", "missing_id"),
}
SECRET_MARKER = "SECRET-BODY-MARKER"


@pytest.fixture
def audit_calls(monkeypatch):
    calls = []

    def _record(operation, principal, target_id, success=True, details=None):
        calls.append({"operation": operation, "principal": principal, "target_id": target_id,
                      "success": success, "details": details})

    monkeypatch.setattr(file_engine_mod, "audit_event", _record)
    return calls


def _plant(vault: Path) -> None:
    k = vault / "01_ARCHITECTURE" / "knowledge"
    for name, (text, _reason) in PLANTED.items():
        if text is None:
            (k / name).write_bytes(b"---\nid: bbb\n---\n\xff\xfe\xfa " + SECRET_MARKER.encode() + b"\n")
        else:
            (k / name).write_text(text.replace("x\n", SECRET_MARKER + "\n"), encoding="utf-8")


def test_every_skipped_file_is_reported_and_audited(tmp_path, audit_calls):
    vault = make_vault(tmp_path)
    _plant(vault)
    eng = FileStorageEngine(str(vault))
    report = eng.load_report

    skipped = report["skipped"]
    assert len(skipped) == 6
    assert Counter(s["reason"] for s in skipped) == Counter(r for _t, r in PLANTED.values())
    by_name = {Path(s["path"]).name: s for s in skipped}
    assert set(by_name) == set(PLANTED)
    for name, (_t, reason) in PLANTED.items():
        entry = by_name[name]
        assert entry["reason"] == reason
        assert not Path(entry["path"]).is_absolute()
        assert isinstance(entry["exception"], (str, type(None)))
    assert by_name["bad_utf8.md"]["exception"] == "UnicodeDecodeError"
    assert by_name["list_id.md"]["exception"] == "TypeError"

    assert report["indexed"] == len(eng.id_to_path)
    assert report["scanned"] == report["indexed"] + len(skipped)

    skip_events = [c for c in audit_calls if c["operation"] == "storage_skipped"]
    assert len(skip_events) == 6
    assert {Path(c["details"]["path"]).name for c in skip_events} == set(PLANTED)
    for c in skip_events:
        assert c["success"] is False
        assert SECRET_MARKER not in repr(c)


def test_clean_fixture_has_no_skips(tmp_path, audit_calls):
    eng = FileStorageEngine(str(make_vault(tmp_path)))
    assert eng.load_report["skipped"] == []
    assert eng.load_report["indexed"] == eng.load_report["scanned"] == len(eng.id_to_path) > 0
    assert not [c for c in audit_calls if c["operation"] == "storage_skipped"]


def test_duplicate_uuid_still_hard_fails(tmp_path, audit_calls):
    vault = make_vault(tmp_path)
    dup = str(uuid.uuid4())
    k = vault / "01_ARCHITECTURE" / "knowledge"
    (k / "d1.md").write_text(note_text("d one", "b", dup), encoding="utf-8")
    (k / "d2.md").write_text(note_text("d two", "b", dup), encoding="utf-8")
    with pytest.raises(ValueError, match="Duplicate UUID"):
        FileStorageEngine(str(vault))


def test_controller_storage_diagnostics_is_read_only_copy(tmp_path, audit_calls):
    from memory_controller.controller import MemoryController

    vault = make_vault(tmp_path)
    _plant(vault)
    eng = FileStorageEngine(str(vault))
    ctl = MemoryController(storage=eng)
    diag = ctl.storage_diagnostics()
    assert len(diag["skipped"]) == 6
    diag["skipped"].clear()
    assert len(eng.load_report["skipped"]) == 6
