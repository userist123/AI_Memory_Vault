"""20_TESTS/regression/test_release_readiness_gate.py — Regression tests for Release Gatekeeper."""
from __future__ import annotations

import json
import importlib.util
from pathlib import Path
from unittest.mock import patch, MagicMock

import pytest

_GATE_PATH = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification" / "release_readiness_gate.py"
_spec = importlib.util.spec_from_file_location("release_readiness_gate", _GATE_PATH)
rrg = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(rrg)


def test_1_missing_test_directory_causes_test_integrity_fail(tmp_path):
    # Only create cognitive_core/tests, omit memory_controller/tests
    (tmp_path / "cognitive_core" / "tests").mkdir(parents=True)
    status, details = rrg.evaluate_test_integrity(tmp_path)
    assert status == "FAIL"
    assert "Required test directory missing" in details["error"]


def test_2_missing_invariant_in_security_manifest_causes_coverage_fail(tmp_path):
    ci_ev = tmp_path / "07_EVALUATION" / "ci_evidence"
    ci_ev.mkdir(parents=True)
    manifest = {
        "invariants": [{"invariant": "I-001", "test_paths": []}]
    }
    (ci_ev / "security-test-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    status, details = rrg.evaluate_security_coverage(tmp_path)
    assert status == "FAIL"
    assert "Missing canonical invariants in manifest" in details["error"]


def test_3_sha_mismatch_causes_evidence_integrity_fail(tmp_path):
    ci_ev = tmp_path / "07_EVALUATION" / "ci_evidence"
    ci_ev.mkdir(parents=True)
    summary = {"commit_sha": "abc12345"}
    (ci_ev / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
    status, details = rrg.evaluate_evidence_integrity(tmp_path, current_sha="def67890")
    assert status == "FAIL"
    assert "Evidence SHA mismatch" in details["error"]


def test_4_missing_dependency_causes_dependency_integrity_fail(monkeypatch):
    with patch("importlib.util.find_spec", return_value=None):
        status, details = rrg.evaluate_dependency_integrity(Path("."))
        assert status == "FAIL"
        assert "missing_dependencies" in details


def test_5_determinism_failure_detected(monkeypatch):
    proc1 = MagicMock(returncode=0, stdout="test_a::1\ntest_b::2\n")
    proc2 = MagicMock(returncode=0, stdout="test_a::1\n")
    with patch("subprocess.run", side_effect=[proc1, proc2]):
        status, details = rrg.evaluate_determinism(Path("."))
        assert status == "FAIL"
        assert "Non-deterministic" in details["error"]


def test_6_codex_blocked_runtime_causes_blocked_status(monkeypatch):
    proc = MagicMock(
        returncode=1,
        stdout="FAILED memory_controller/tests/test_audit.py::test_audit_promote_success_and_fail\n",
        stderr=""
    )
    with patch("subprocess.run", return_value=proc):
        status, details = rrg.evaluate_runtime_status(Path("."))
        assert status == "BLOCKED"
        assert details["blocker_owner"] == "CODEX"


def test_7_any_fail_forces_release_status_fail():
    with patch.object(rrg, "evaluate_test_integrity", return_value=("FAIL", {"err": "broken"})), \
         patch.object(rrg, "evaluate_security_coverage", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_evidence_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_determinism", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_dependency_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_runtime_status", return_value=("PASS", {})), \
         patch.object(rrg, "check_working_tree_clean", return_value=(True, [])):
        report = rrg.execute_release_gate(Path("."))
        assert report["release_status"] == "FAIL"


def test_8_working_tree_dirty_forces_blocked_status():
    with patch.object(rrg, "evaluate_test_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_security_coverage", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_evidence_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_determinism", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_dependency_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_runtime_status", return_value=("PASS", {})), \
         patch.object(rrg, "check_working_tree_clean", return_value=(False, ["M file.py"])):
        report = rrg.execute_release_gate(Path("."))
        assert report["release_status"] == "BLOCKED"


def test_9_clean_state_with_all_pass_gives_ready():
    with patch.object(rrg, "evaluate_test_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_security_coverage", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_evidence_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_determinism", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_dependency_integrity", return_value=("PASS", {})), \
         patch.object(rrg, "evaluate_runtime_status", return_value=("PASS", {})), \
         patch.object(rrg, "check_working_tree_clean", return_value=(True, [])):
        report = rrg.execute_release_gate(Path("."))
        assert report["release_status"] == "READY"
