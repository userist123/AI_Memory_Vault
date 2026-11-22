"""Audit integrity hardening: threat model T1-T5.

T1 chain fork across processes, T2 exception text leaking into audit details,
T3 storage_error leaking absolute path / parser message, T4 lock timeout is a hard
failure (no unchained entry), T5 logs written by the previous algorithm still verify.
"""
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import textwrap
from pathlib import Path

import pytest

import memory_controller.audit.logger as logger_module
from memory_controller.audit.logger import AuditLogger

PACKAGES = Path(__file__).resolve().parents[1] / "03_IMPLEMENTATION" / "packages"
MARKER = "LEAKMARKER-7f3a9c"

_WORKER = textwrap.dedent(
    """
    import sys
    sys.path.insert(0, sys.argv[1])
    from memory_controller.audit.logger import AuditLogger
    lg = AuditLogger(sys.argv[2])
    for i in range(int(sys.argv[3])):
        lg.log(actor=sys.argv[4], operation="op", target_id=str(i))
    """
)


def _entries(path):
    return [json.loads(l) for l in Path(path).read_text(encoding="utf-8").splitlines() if l.strip()]


# ---------------------------------------------------------------- T1
def test_t1_two_processes_do_not_fork_the_chain(tmp_path):
    log = tmp_path / "audit_log.jsonl"
    n = 40
    procs = [
        subprocess.Popen([sys.executable, "-c", _WORKER, str(PACKAGES), str(log), str(n), name])
        for name in ("p1", "p2")
    ]
    for p in procs:
        assert p.wait(timeout=120) == 0
    ok, violations = AuditLogger(str(log)).verify_integrity()
    assert ok, violations[:3]
    assert len(_entries(log)) == 2 * n


def test_t1_cache_is_not_trusted_across_instances(tmp_path):
    log = str(tmp_path / "a.jsonl")
    a, b = AuditLogger(log), AuditLogger(log)
    for i in range(5):
        a.log("x", "op", str(i))
        b.log("y", "op", str(i))
    ok, violations = a.verify_integrity()
    assert ok, violations
    assert len(_entries(log)) == 10


# ---------------------------------------------------------------- T4
def test_t4_lock_timeout_raises_and_writes_nothing(tmp_path):
    log = str(tmp_path / "a.jsonl")
    holder = AuditLogger(log)
    waiter = AuditLogger(log, lock_timeout=0.3)
    with holder._file_lock():
        with pytest.raises(logger_module.AuditLockTimeout):
            waiter.log("x", "op", "t")
    assert _entries(log) == []
    waiter.log("x", "op", "t")  # lock released: works again
    assert len(_entries(log)) == 1


# ---------------------------------------------------------------- T5
def _legacy_write(path, entries):
    prev = "GENESIS"
    with open(path, "a", encoding="utf-8") as f:
        for e in entries:
            e = dict(e)
            e["prev_hash"] = prev
            e["entry_hash"] = hashlib.sha256(
                json.dumps(e, sort_keys=True, ensure_ascii=False).encode("utf-8")).hexdigest()
            prev = e["entry_hash"]
            f.write(json.dumps(e, ensure_ascii=False) + "\n")


def test_t5_legacy_log_verifies_and_can_be_extended(tmp_path):
    log = str(tmp_path / "legacy.jsonl")
    base = {"actor": "a", "operation": "read", "target_id": "n", "timestamp": "2026-01-01T00:00:00Z",
            "outcome": "success"}
    _legacy_write(log, [base, dict(base, operation="search", metadata={"k": "é"})])
    lg = AuditLogger(log)
    assert lg.verify_integrity() == (True, [])
    lg.log("a", "propose", "n2", metadata={"z": 1})
    assert lg.verify_integrity() == (True, [])
    last = _entries(log)[-1]
    assert set(last) == {"actor", "operation", "target_id", "timestamp", "outcome", "metadata",
                         "prev_hash", "entry_hash"}


def test_t5_tail_read_handles_long_last_line_and_no_trailing_newline(tmp_path):
    log = tmp_path / "t.jsonl"
    lg = AuditLogger(str(log))
    lg.log("a", "op", "1", metadata={"big": "x" * 50000})
    with open(log, "a", encoding="utf-8") as f:
        pass
    lg2 = AuditLogger(str(log))
    lg2.log("a", "op", "2")
    assert lg2.verify_integrity() == (True, [])


# ---------------------------------------------------------------- T2
def _audit_text(path) -> str:
    return Path(path).read_text(encoding="utf-8")


def test_t2_controller_errors_leave_no_message_in_audit(tmp_path, monkeypatch):
    from memory_controller.controller import controller, Principal
    log = str(tmp_path / "audit.jsonl")
    monkeypatch.setattr(logger_module, "_logger_instance", AuditLogger(log))

    def boom(*a, **k):
        raise RuntimeError(f"cannot parse note body {MARKER} at C:/secret/vault")

    monkeypatch.setattr(controller.storage, "get", boom)
    with pytest.raises(Exception):
        controller.read(Principal.ADMIN, "11111111-1111-1111-1111-111111111111")
    text = _audit_text(log)
    assert '"outcome": "error"' in text
    assert MARKER not in text and "secret" not in text
    meta = _entries(log)[-1]["metadata"]
    assert meta["error_type"] == "RuntimeError"
    assert len(meta["error_sha256"]) == 16
    assert meta["error_sha256"] == hashlib.sha256(
        f"cannot parse note body {MARKER} at C:/secret/vault".encode()).hexdigest()[:16]
    assert "error" not in meta


def test_t2_no_str_e_left_in_controller_audit_details():
    src = (PACKAGES / "memory" / "controller.py").read_text(encoding="utf-8")
    import re
    assert not re.search(r"'error':\s*str\(e\)", src)


# ---------------------------------------------------------------- T3
def test_t3_storage_error_has_no_marker_and_no_absolute_path(tmp_path, monkeypatch):
    from memory_controller.storage.file_engine import FileStorageEngine
    from memory_vault_fixture import make_vault
    log = str(tmp_path / "audit.jsonl")
    monkeypatch.setattr(logger_module, "_logger_instance", AuditLogger(log))
    vault = make_vault(tmp_path / "v")
    bad = vault / "01_ARCHITECTURE" / "knowledge" / "bad.md"
    bad.write_text(f"---\nid: aaa\ntitle: [unclosed {MARKER}\n---\nbody\n", encoding="utf-8")
    FileStorageEngine(str(vault))
    text = _audit_text(log)
    assert "storage_error" in text
    assert MARKER not in text
    assert str(vault) not in text and str(vault).replace("\\", "\\\\") not in text
    err = [e for e in _entries(log) if e["operation"] == "storage_error"][0]["metadata"]
    assert err["path"].replace("\\", "/") == "01_ARCHITECTURE/knowledge/bad.md"
    assert "message" not in err
    assert err["error_type"]
