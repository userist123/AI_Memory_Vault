import importlib.util
import json
import pytest
from pathlib import Path
from unittest.mock import MagicMock, patch

import sys
_VERIF_DIR = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification"
if str(_VERIF_DIR) not in sys.path:
    sys.path.insert(0, str(_VERIF_DIR))

from reproducibility_gate import (
    REPO_ROOT,
    REQUIRED_SECURITY_TESTS,
    REQUIRED_TEST_DIRECTORIES,
    check_dependencies,
    check_pytest_ini_exclusions,
    compute_manifest_hash,
    execute_gate,
    get_git_commit_sha,
    parse_pytest_summary,
    run_pytest_collection,
)


def test_1_missing_required_test_suite_returns_not_found(tmp_path: Path):
    # tmp_path has no cognitive_core/tests or memory_controller/tests
    res = run_pytest_collection(tmp_path)
    assert res["status"] == "NOT_FOUND"
    assert res["collection_errors"] == 1
    assert "Required test directory not found" in res["error_details"][0]


def test_2_collection_error_returns_fail():
    with patch("subprocess.run") as mock_run:
        mock_run.return_value = MagicMock(
            returncode=2,
            stdout="ERROR collecting tests/test_bad.py\nModuleNotFoundError: No module named 'foo'",
            stderr="",
        )
        res = run_pytest_collection(REPO_ROOT)
        assert res["status"] == "FAIL"
        assert res["collection_errors"] >= 1


def test_3_test_failure_returns_fail():
    with patch("reproducibility_gate.run_test_execution") as mock_exec, \
         patch("reproducibility_gate.run_pytest_collection") as mock_coll:
        mock_coll.return_value = {
            "tests_collected": 100,
            "collection_errors": 0,
            "discovered_directories": REQUIRED_TEST_DIRECTORIES,
            "missing_discovery": [],
            "missing_security": [],
            "status": "PASS",
            "collected_nodes": ["dummy::test_1"],
        }
        mock_exec.return_value = {
            "counts": {"passed": 90, "failed": 10, "errors": 0, "skipped": 0, "duration": 5.0},
            "failed_tests": ["test_unrelated_failure"],
            "is_strictly_codex_blocked": False,
            "duration_seconds": 5.0,
        }
        report, _ = execute_gate(root=REPO_ROOT, collect_only=False, allow_codex_block=False)
        assert report["status"] == "FAIL"
        assert report["tests"]["failed"] == 10


def test_4_zero_tests_collected_returns_fail():
    with patch("reproducibility_gate.run_pytest_collection") as mock_coll:
        mock_coll.return_value = {
            "tests_collected": 0,
            "collection_errors": 0,
            "discovered_directories": [],
            "missing_discovery": [],
            "missing_security": [],
            "status": "FAIL",
            "collected_nodes": [],
        }
        report, _ = execute_gate(root=REPO_ROOT, collect_only=True)
        assert report["status"] == "FAIL"
        assert report["collection"]["tests_collected"] == 0


def test_5_required_test_directory_excluded_in_ini_returns_fail(tmp_path: Path):
    bad_ini = tmp_path / "pytest.ini"
    bad_ini.write_text("[pytest]\nnorecursedirs = memory_controller\n", encoding="utf-8")
    violations = check_pytest_ini_exclusions(tmp_path)
    assert len(violations) > 0
    assert "memory_controller/tests" in violations[0]


def test_6_sha_mismatch_returns_fail():
    real_sha, _ = get_git_commit_sha(REPO_ROOT)
    fake_sha = "0000000000000000000000000000000000000000"
    report, _ = execute_gate(root=REPO_ROOT, expected_sha=fake_sha, collect_only=True)
    assert report["status"] == "FAIL"
    assert "FAIL" in report["details"]["sha_check"]


def test_7_missing_dependency_detected():
    with patch("importlib.util.find_spec", return_value=None):
        res = check_dependencies(REPO_ROOT)
        assert res["all_installed"] is False
        assert len(res["missing_dependencies"]) > 0


def test_8_dependency_installed():
    res = check_dependencies(REPO_ROOT)
    assert res["all_installed"] is True
    assert len(res["missing_dependencies"]) == 0
    assert res["manifests"]["requirements.txt"] != "NOT_FOUND"


def test_9_pass_status_when_clean():
    with patch("reproducibility_gate.run_test_execution") as mock_exec, \
         patch("reproducibility_gate.run_pytest_collection") as mock_coll:
        mock_coll.return_value = {
            "tests_collected": 500,
            "collection_errors": 0,
            "discovered_directories": REQUIRED_TEST_DIRECTORIES,
            "missing_discovery": [],
            "missing_security": [],
            "status": "PASS",
            "collected_nodes": ["dummy::test_1"],
        }
        mock_exec.return_value = {
            "counts": {"passed": 500, "failed": 0, "errors": 0, "skipped": 0, "duration": 8.0},
            "failed_tests": [],
            "is_strictly_codex_blocked": False,
            "duration_seconds": 8.0,
        }
        report, _ = execute_gate(root=REPO_ROOT, collect_only=False)
        assert report["status"] == "PASS"
        assert report["tests"]["failed"] == 0


def test_10_blocked_status_when_codex_blocked():
    with patch("reproducibility_gate.run_test_execution") as mock_exec, \
         patch("reproducibility_gate.run_pytest_collection") as mock_coll:
        mock_coll.return_value = {
            "tests_collected": 819,
            "collection_errors": 0,
            "discovered_directories": REQUIRED_TEST_DIRECTORIES,
            "missing_discovery": [],
            "missing_security": [],
            "status": "PASS",
            "collected_nodes": ["dummy::test_1"],
        }
        mock_exec.return_value = {
            "counts": {"passed": 813, "failed": 6, "errors": 0, "skipped": 0, "duration": 18.0},
            "failed_tests": [
                "memory_controller/tests/test_authorization.py::test_human_promote_allowed",
                "memory_controller/tests/test_authorization.py::test_admin_promote_allowed",
                "memory_controller/tests/test_audit.py::test_audit_promote_success_and_fail",
                "memory_controller/tests/test_cache.py::test_mutation_invalidation_review_promote",
                "memory_controller/tests/test_milestone3_empirical_challenge.py::test_concurrent_attest_and_update_race_sqlite",
                "memory_controller/tests/test_query_classifier.py::test_verified_is_still_detected_as_whole_word",
            ],
            "is_strictly_codex_blocked": True,
            "duration_seconds": 18.0,
        }
        report, _ = execute_gate(root=REPO_ROOT, collect_only=False, allow_codex_block=True)
        assert report["status"] == "BLOCKED"
        assert report["details"]["codex_blocked_status"] == "BLOCKED — CODEX"


def test_11_not_found_status_on_missing_security_test():
    with patch("subprocess.run") as mock_run:
        # Mock pytest collection returning nodes but omitting test_adversarial_p0_p15_invariants.py
        mock_run.return_value = MagicMock(
            returncode=0,
            stdout="cognitive_core/tests/test_activation.py::test_something\nmemory_controller/tests/test_storage.py::test_crud\n",
            stderr="",
        )
        res = run_pytest_collection(REPO_ROOT)
        assert res["status"] == "NOT_FOUND"
        assert len(res["missing_security"]) > 0


def test_12_deterministic_repeated_result():
    # Calling parse_pytest_summary and dependency checks twice gives identical results
    summary1 = parse_pytest_summary("813 passed, 6 failed, 2 skipped in 18.79s")
    summary2 = parse_pytest_summary("813 passed, 6 failed, 2 skipped in 18.79s")
    assert summary1 == summary2

    hash1 = compute_manifest_hash(REPO_ROOT / "requirements.txt")
    hash2 = compute_manifest_hash(REPO_ROOT / "requirements.txt")
    assert hash1 == hash2
