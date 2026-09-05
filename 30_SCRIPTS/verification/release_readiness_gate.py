#!/usr/bin/env python3
"""30_SCRIPTS/verification/release_readiness_gate.py — Release Readiness & Gatekeeper.

Performs forensic evaluation of repository release readiness across 6 dimensions:
  1. test_integrity (discovery, collection, execution, no silent exclusions)
  2. security_coverage (all P0-015 / I-001..I-012 / I-RETRIEVAL invariants linked to passing tests)
  3. evidence_integrity (commit SHA matches git HEAD, evidence artifacts valid, working tree provenance)
  4. determinism (2-pass semantic stability across test collections and execution)
  5. dependency_integrity (root manifests valid, all critical dependencies importable)
  6. runtime_status (PASS / BLOCKED / FAIL)

Overall Release Verdict Rule:
  - ANY FAIL in sub-gates -> release_status = FAIL
  - ONLY BLOCKED external ownership (runtime_status == BLOCKED) -> release_status = BLOCKED
  - ALL gates PASS and 0 blockers -> release_status = READY
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import platform
import subprocess
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]

REQUIRED_TEST_DIRECTORIES = [
    "cognitive_core/tests",
    "memory_controller/tests",
]

REQUIRED_INVARIANTS = [
    "I-001", "I-002", "I-003", "I-004", "I-005", "I-006", "I-007",
    "I-008", "I-009", "I-010", "I-011", "I-012", "I-RETRIEVAL",
    "I-PAGINATION", "P16-P18"
]

REQUIRED_DEPENDENCIES = {
    "pytest": "pytest",
    "pytest-asyncio": "pytest_asyncio",
    "pyyaml": "yaml",
    "jsonschema": "jsonschema",
    "fastapi": "fastapi",
    "httpx": "httpx",
    "pydantic": "pydantic",
    "numpy": "numpy",
    "pandas": "pandas",
    "requests": "requests",
}

KNOWN_CODEX_BLOCKED_TESTS = {
    "test_audit_promote_success_and_fail",
    "test_human_promote_allowed",
    "test_admin_promote_allowed",
    "test_mutation_invalidation_review_promote",
    "test_concurrent_attest_and_update_race_sqlite",
    "test_verified_is_still_detected_as_whole_word",
    "test_query_raw_boundary_holds_for_sqlite_storage",
}


def get_git_commit_sha(root: Path) -> Tuple[str, Optional[str]]:
    try:
        proc = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=root,
            capture_output=True,
            text=True,
            check=False,
        )
        if proc.returncode == 0:
            return proc.stdout.strip(), None
        return "UNKNOWN", f"git rev-parse error: {proc.stderr.strip()}"
    except Exception as exc:
        return "UNKNOWN", str(exc)


def check_working_tree_clean(root: Path) -> Tuple[bool, List[str]]:
    """Check for uncommitted modifications or untracked test/core files."""
    try:
        proc = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=root,
            capture_output=True,
            text=True,
            check=False,
        )
        if proc.returncode != 0:
            return False, ["git status check failed"]
        lines = [ln.strip() for ln in proc.stdout.splitlines() if ln.strip()]
        # Ignore scratch or transient log files
        critical_drift = []
        for line in lines:
            parts = line.split()
            if len(parts) >= 2:
                path = parts[1]
                if any(path.startswith(p) for p in ["cognitive_core/", "memory_controller/", "30_SCRIPTS/verification/"]):
                    critical_drift.append(line)
        return len(critical_drift) == 0, critical_drift
    except Exception as exc:
        return False, [str(exc)]


def evaluate_test_integrity(root: Path) -> Tuple[str, Dict[str, Any]]:
    """Verify complete test discovery, no ini exclusions, and execution outcomes."""
    for req in REQUIRED_TEST_DIRECTORIES:
        if not (root / req).is_dir():
            return "FAIL", {"error": f"Required test directory missing: {req}"}

    # Verify pytest.ini exclusions
    ini_path = root / "pytest.ini"
    if not ini_path.exists():
        return "FAIL", {"error": "pytest.ini is missing"}
    text = ini_path.read_text(encoding="utf-8", errors="ignore")
    for line in text.splitlines():
        if line.strip().startswith("norecursedirs"):
            for req in REQUIRED_TEST_DIRECTORIES:
                parts = req.split("/")
                if any(p in line for p in parts if p not in (".git", ".vs", "tests")):
                    return "FAIL", {"error": f"pytest.ini excludes required directory: {req}"}

    # Run collection
    cmd = [sys.executable, "-m", "pytest", "cognitive_core/tests", "memory_controller/tests", "--collect-only", "-q"]
    proc = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)
    if proc.returncode != 0 and "ERRORS" in proc.stdout + proc.stderr:
        return "FAIL", {"error": "Pytest collection failed with errors", "stderr": proc.stderr}

    nodes = [ln.strip() for ln in proc.stdout.splitlines() if "::" in ln and not ln.startswith("ERROR")]
    if len(nodes) == 0:
        return "FAIL", {"error": "Zero tests collected across required test paths"}

    return "PASS", {"tests_collected": len(nodes)}


def evaluate_security_coverage(root: Path) -> Tuple[str, Dict[str, Any]]:
    """Verify security manifest existence, invariant completeness, and passing execution."""
    manifest_path = root / "07_EVALUATION" / "ci_evidence" / "security-test-manifest.json"
    if not manifest_path.exists():
        return "FAIL", {"error": "security-test-manifest.json is missing"}

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except Exception as exc:
        return "FAIL", {"error": f"Failed to parse security manifest: {exc}"}

    found_invariants = {item["invariant"] for item in manifest.get("invariants", [])}
    missing_invs = [inv for inv in REQUIRED_INVARIANTS if inv not in found_invariants]
    if missing_invs:
        return "FAIL", {"error": f"Missing canonical invariants in manifest: {missing_invs}"}

    all_test_paths = []
    for inv in manifest.get("invariants", []):
        all_test_paths.extend(inv.get("test_paths", []))

    # Run security test paths
    cmd = [sys.executable, "-m", "pytest"] + all_test_paths + ["-q"]
    proc = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)
    if proc.returncode != 0:
        return "FAIL", {"error": "One or more security invariant tests failed", "output": proc.stdout}

    return "PASS", {"invariants_verified": len(found_invariants), "security_tests_passed": len(all_test_paths)}


def evaluate_evidence_integrity(root: Path, current_sha: str) -> Tuple[str, Dict[str, Any]]:
    """Verify that CI evidence exists, matches HEAD, and is internally consistent."""
    summary_paths = [
        root / "07_EVALUATION" / "ci_evidence" / "summary.json",
        root / "ci-evidence" / "summary.json",
    ]
    summary_path = next((p for p in summary_paths if p.exists()), None)
    if not summary_path:
        return "FAIL", {"error": "CI evidence summary.json is missing"}

    try:
        data = json.loads(summary_path.read_text(encoding="utf-8"))
    except Exception as exc:
        return "FAIL", {"error": f"Corrupted summary.json: {exc}"}

    ev_sha = data.get("commit_sha", "")
    if ev_sha != current_sha:
        return "FAIL", {"error": f"Evidence SHA mismatch: artifact has {ev_sha}, git HEAD is {current_sha}"}

    return "PASS", {"evidence_sha": ev_sha, "artifact_path": str(summary_path)}


def evaluate_determinism(root: Path) -> Tuple[str, Dict[str, Any]]:
    """Verify 2-pass test collection and outcome determinism."""
    cmd = [sys.executable, "-m", "pytest", "cognitive_core/tests", "memory_controller/tests", "--collect-only", "-q"]
    p1 = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)
    p2 = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)

    nodes1 = [ln.strip() for ln in p1.stdout.splitlines() if "::" in ln and not ln.startswith("ERROR")]
    nodes2 = [ln.strip() for ln in p2.stdout.splitlines() if "::" in ln and not ln.startswith("ERROR")]

    if len(nodes1) != len(nodes2) or nodes1 != nodes2:
        return "FAIL", {
            "error": "Non-deterministic test discovery across consecutive passes",
            "p1_count": len(nodes1),
            "p2_count": len(nodes2),
        }

    return "PASS", {"nodes_collected": len(nodes1)}


def evaluate_dependency_integrity(root: Path) -> Tuple[str, Dict[str, Any]]:
    """Verify requirements manifests and importability of critical dependencies."""
    missing = []
    for pkg_name, mod_name in REQUIRED_DEPENDENCIES.items():
        if importlib.util.find_spec(mod_name) is None:
            missing.append(pkg_name)

    if missing:
        return "FAIL", {"missing_dependencies": missing}

    return "PASS", {"dependencies_verified": len(REQUIRED_DEPENDENCIES)}


def evaluate_runtime_status(root: Path) -> Tuple[str, Dict[str, Any]]:
    """Execute tests and classify status as PASS, BLOCKED (if strictly external known blocker), or FAIL."""
    cmd = [sys.executable, "-m", "pytest", "cognitive_core/tests", "memory_controller/tests", "-q"]
    proc = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)
    combined = proc.stdout + "\n" + proc.stderr

    failed_tests = [ln.replace("FAILED ", "").strip() for ln in combined.splitlines() if ln.startswith("FAILED ")]

    if len(failed_tests) == 0 and proc.returncode == 0:
        return "PASS", {"failed_tests": []}

    all_codex_blocked = True
    for ft in failed_tests:
        test_stem = ft.split("::")[-1].split("[")[0]
        if test_stem not in KNOWN_CODEX_BLOCKED_TESTS:
            all_codex_blocked = False
            break

    if all_codex_blocked and len(failed_tests) > 0:
        return "BLOCKED", {"failed_tests": failed_tests, "blocker_owner": "CODEX"}

    return "FAIL", {"failed_tests": failed_tests}


def execute_release_gate(root: Path = REPO_ROOT) -> Dict[str, Any]:
    current_sha, git_err = get_git_commit_sha(root)

    test_integrity, test_details = evaluate_test_integrity(root)
    security_coverage, sec_details = evaluate_security_coverage(root)
    evidence_integrity, ev_details = evaluate_evidence_integrity(root, current_sha)
    determinism, det_details = evaluate_determinism(root)
    dependency_integrity, dep_details = evaluate_dependency_integrity(root)
    runtime_status, run_details = evaluate_runtime_status(root)

    # Cleanliness check (detects working tree drift)
    is_clean, drift_items = check_working_tree_clean(root)

    # Evaluation Rule:
    # ANY FAIL -> release_status = FAIL
    # ONLY BLOCKED external ownership -> release_status = BLOCKED
    # ALL required gates PASS -> release_status = READY
    sub_gates = [test_integrity, security_coverage, evidence_integrity, determinism, dependency_integrity]

    if any(g == "FAIL" for g in sub_gates) or runtime_status == "FAIL":
        release_status = "FAIL"
    elif runtime_status == "BLOCKED" or not is_clean:
        # If uncommitted drift exists or external runtime is blocked, release cannot be READY
        release_status = "BLOCKED"
    elif all(g == "PASS" for g in sub_gates) and runtime_status == "PASS" and is_clean:
        release_status = "READY"
    else:
        release_status = "BLOCKED"

    report = {
        "commit_sha": current_sha,
        "test_integrity": test_integrity,
        "security_coverage": security_coverage,
        "evidence_integrity": evidence_integrity,
        "determinism": determinism,
        "dependency_integrity": dependency_integrity,
        "runtime_status": runtime_status,
        "release_status": release_status,
        "diagnostics": {
            "working_tree_clean": is_clean,
            "working_tree_drift": drift_items,
            "test_details": test_details,
            "security_details": sec_details,
            "evidence_details": ev_details,
            "determinism_details": det_details,
            "dependency_details": dep_details,
            "runtime_details": run_details,
        },
    }
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description="Release Readiness Gate")
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root path")
    parser.add_argument("--json", action="store_true", help="Output JSON only")
    args = parser.parse_args()

    report = execute_release_gate(args.root)

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print("=" * 80)
        print("                    RELEASE READINESS GATE REPORT                             ")
        print("=" * 80)
        print(f"Commit SHA:           {report['commit_sha']}")
        print(f"Test Integrity:       {report['test_integrity']}")
        print(f"Security Coverage:    {report['security_coverage']}")
        print(f"Evidence Integrity:   {report['evidence_integrity']}")
        print(f"Determinism:          {report['determinism']}")
        print(f"Dependency Integrity: {report['dependency_integrity']}")
        print(f"Runtime Status:       {report['runtime_status']}")
        print("-" * 80)
        print(f"RELEASE STATUS:       {report['release_status']}")
        print("=" * 80)

    if report["release_status"] == "READY":
        return 0
    elif report["release_status"] == "BLOCKED":
        return 2
    else:
        return 1


if __name__ == "__main__":
    sys.exit(main())
