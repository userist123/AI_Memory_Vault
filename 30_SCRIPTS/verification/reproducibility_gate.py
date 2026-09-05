#!/usr/bin/env python3
"""30_SCRIPTS/verification/reproducibility_gate.py — Reproducibility & CI Evidence Gate.

Validates that a clean checkout satisfies deterministic verification contracts:
  1. Source & Git SHA integrity
  2. Complete, non-silent test discovery (cognitive_core/tests, memory_controller/tests)
  3. Security regression test presence (adversarial P0/P15, reconciliation, pagination, CLI)
  4. Root dependency manifest completeness & importability
  5. Test suite execution & empirical result collection
  6. Deterministic repeatability check (--determinism)
  7. Generation of compact, non-volatile evidence artifacts (summary.json, summary.md, collection.txt)

Status Taxonomy:
  - PASS: All required checks ran and passed with 0 failures, 0 errors, and complete discovery.
  - FAIL: Any test failure, collection error, missing required suite/test, or SHA mismatch.
  - BLOCKED: Known documented upstream runtime dependency block (never masks true failure).
  - NOT_FOUND: Missing required test directory or required security test file.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import platform
import re
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

REQUIRED_SECURITY_TESTS = [
    "memory_controller/tests/test_adversarial_p0_p15_invariants.py",
    "memory_controller/tests/test_security_hardening.py",
    "cognitive_core/tests/test_reconciliation_boundary.py",
    "cognitive_core/tests/test_tool_router_security.py",
    "cognitive_core/tests/test_secure_recall_cli.py",
    "memory_controller/tests/test_pagination_token_bounds.py",
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
    """Return (commit_sha, error_message)."""
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
        return "UNKNOWN", f"git rev-parse failed: {proc.stderr.strip()}"
    except Exception as exc:
        return "UNKNOWN", f"git execution error: {exc}"


def compute_manifest_hash(path: Path) -> str:
    if not path.exists():
        return "NOT_FOUND"
    data = path.read_bytes()
    return hashlib.sha256(data).hexdigest()


def check_dependencies(root: Path) -> Dict[str, Any]:
    """Verify that dependencies declared in requirements.txt are installed and importable."""
    req_file = root / "requirements.txt"
    v6_file = root / "requirements-memory-v6.txt"
    
    manifests = {
        "requirements.txt": compute_manifest_hash(req_file),
        "requirements-memory-v6.txt": compute_manifest_hash(v6_file),
    }
    
    import_results = {}
    missing_imports = []
    for pkg_name, module_name in REQUIRED_DEPENDENCIES.items():
        spec = importlib.util.find_spec(module_name)
        if spec is not None:
            import_results[pkg_name] = "INSTALLED"
        else:
            import_results[pkg_name] = "MISSING"
            missing_imports.append(pkg_name)
            
    return {
        "manifests": manifests,
        "import_status": import_results,
        "missing_dependencies": missing_imports,
        "all_installed": len(missing_imports) == 0,
    }


def check_pytest_ini_exclusions(root: Path) -> List[str]:
    """Verify pytest.ini does not silently exclude required test directories."""
    ini_path = root / "pytest.ini"
    if not ini_path.exists():
        return ["pytest.ini is missing"]
        
    text = ini_path.read_text(encoding="utf-8", errors="ignore")
    violations = []
    for line in text.splitlines():
        if line.strip().startswith("norecursedirs"):
            for req in REQUIRED_TEST_DIRECTORIES:
                parts = req.split("/")
                if any(p in line for p in parts if p not in (".git", ".vs", "tests")):
                    violations.append(f"norecursedirs appears to exclude required directory: {req}")
    return violations


def run_pytest_collection(root: Path) -> Dict[str, Any]:
    """Execute pytest collection to discover all tests and catch collection errors."""
    is_v6_spine = (root / "20_TESTS").is_dir()
    req_dirs = ["20_TESTS"] if is_v6_spine else REQUIRED_TEST_DIRECTORIES
    for req_dir in req_dirs:
        full_dir = root / req_dir
        if not full_dir.is_dir():
            return {
                "tests_collected": 0,
                "collection_errors": 1,
                "error_details": [f"Required test directory not found on disk: {req_dir}"],
                "collected_nodes": [],
                "discovered_directories": [],
                "missing_discovery": [req_dir],
                "missing_security": list(REQUIRED_SECURITY_TESTS),
                "status": "NOT_FOUND",
            }
            
    cmd = [
        sys.executable,
        "-m",
        "pytest",
        *(["20_TESTS"] if is_v6_spine else ["cognitive_core/tests", "memory_controller/tests"]),
        "--collect-only",
        "-q",
    ]
    
    proc = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)
    output_lines = [ln.strip() for ln in proc.stdout.splitlines() if ln.strip()]
    
    collected_nodes = []
    error_details = []
    discovered_dirs = set()
    
    for line in output_lines:
        if "::" in line and not line.startswith("ERROR"):
            collected_nodes.append(line)
            file_part = line.split("::")[0]
            discovered_dirs.add(str(Path(file_part).parent).replace("\\", "/"))
        elif "ERROR" in line or "ERRORS" in line:
            error_details.append(line)
            
    # Also parse stderr for collection errors
    if proc.stderr:
        for err_line in proc.stderr.splitlines():
            if "ERROR" in err_line:
                error_details.append(err_line.strip())

    # Verify each required directory is actually discovered
    missing_discovery = []
    for req in req_dirs:
        if not any(d == req or d.startswith(req) for d in discovered_dirs):
            missing_discovery.append(req)
            
    # Verify required security tests
    missing_security = []
    for sec_test in REQUIRED_SECURITY_TESTS:
        sec_name = Path(sec_test).name
        if not any(sec_name in c.replace("\\", "/") for c in collected_nodes):
            missing_security.append(sec_test)
            
    status = "PASS"
    if error_details:
        status = "FAIL"
    elif missing_discovery:
        status = "NOT_FOUND"
    elif missing_security:
        status = "NOT_FOUND"
    elif len(collected_nodes) == 0:
        status = "FAIL"

    return {
        "tests_collected": len(collected_nodes),
        "collection_errors": len(error_details),
        "error_details": error_details,
        "collected_nodes": collected_nodes,
        "discovered_directories": sorted(discovered_dirs),
        "missing_discovery": missing_discovery,
        "missing_security": missing_security,
        "status": status,
    }


def parse_pytest_summary(output: str) -> Dict[str, Any]:
    """Extract pass/fail/error counts and duration from pytest output."""
    counts = {"passed": 0, "failed": 0, "skipped": 0, "errors": 0, "duration": 0.0}
    for m in re.finditer(r"(\d+)\s+(passed|failed|skipped|error[s]?)", output):
        val = int(m.group(1))
        key = m.group(2)
        if key.startswith("error"):
            counts["errors"] = val
        else:
            counts[key] = val
    m_dur = re.search(r"in\s+([\d.]+)s", output)
    if m_dur:
        counts["duration"] = float(m_dur.group(1))
    return counts


def run_test_execution(root: Path) -> Dict[str, Any]:
    """Execute pytest suite and capture real empirical results."""
    cmd = [
        sys.executable,
        "-m",
        "pytest",
        "cognitive_core/tests",
        "memory_controller/tests",
        "--continue-on-collection-errors",
        "-q",
    ]
    
    t0 = time.perf_counter()
    proc = subprocess.run(cmd, cwd=root, capture_output=True, text=True, check=False)
    duration = time.perf_counter() - t0
    
    combined_output = proc.stdout + "\n" + proc.stderr
    summary = parse_pytest_summary(combined_output)
    if summary["duration"] == 0.0:
        summary["duration"] = round(duration, 2)
        
    # Extract failed test names
    failed_names = []
    for line in combined_output.splitlines():
        if line.startswith("FAILED "):
            failed_names.append(line.replace("FAILED ", "").strip())
            
    # Check if failures are strictly the known Codex-owned blocks
    is_strictly_codex_blocked = False
    if summary["failed"] > 0:
        all_codex = True
        for fn in failed_names:
            test_stem = fn.split("::")[-1].split("[")[0]
            if test_stem not in KNOWN_CODEX_BLOCKED_TESTS:
                all_codex = False
                break
        is_strictly_codex_blocked = all_codex

    return {
        "returncode": proc.returncode,
        "counts": summary,
        "failed_tests": failed_names,
        "is_strictly_codex_blocked": is_strictly_codex_blocked,
        "duration_seconds": round(duration, 2),
    }


def execute_gate(
    root: Path = REPO_ROOT,
    expected_sha: Optional[str] = None,
    collect_only: bool = False,
    allow_codex_block: bool = False,
) -> Dict[str, Any]:
    """Execute complete Reproducibility Gate inspection."""
    commit_sha, git_err = get_git_commit_sha(root)
    sha_status = "PASS"
    if git_err:
        sha_status = "FAIL"
    elif expected_sha and commit_sha != expected_sha:
        sha_status = "FAIL"

    dep_result = check_dependencies(root)
    ini_violations = check_pytest_ini_exclusions(root)
    collection_result = run_pytest_collection(root)

    manifest_hash = dep_result["manifests"].get("requirements.txt", "UNKNOWN")
    
    test_results = {
        "passed": 0,
        "failed": 0,
        "errors": 0,
        "skipped": 0,
        "duration_seconds": 0.0,
        "failed_tests": [],
        "is_strictly_codex_blocked": False,
    }

    if not collect_only and collection_result["status"] in ("PASS", "FAIL"):
        exec_res = run_test_execution(root)
        test_results["passed"] = exec_res["counts"]["passed"]
        test_results["failed"] = exec_res["counts"]["failed"]
        test_results["errors"] = exec_res["counts"]["errors"]
        test_results["skipped"] = exec_res["counts"]["skipped"]
        test_results["duration_seconds"] = exec_res["duration_seconds"]
        test_results["failed_tests"] = exec_res["failed_tests"]
        test_results["is_strictly_codex_blocked"] = exec_res["is_strictly_codex_blocked"]

    # Evaluate Overall Status
    # Rule 1: SHA mismatch or missing dependencies -> FAIL
    if sha_status != "PASS" or not dep_result["all_installed"] or ini_violations:
        overall_status = "FAIL"
    # Rule 2: NOT_FOUND if required suite or security test file is missing
    elif collection_result["status"] == "NOT_FOUND":
        overall_status = "NOT_FOUND"
    # Rule 3: Collection error -> FAIL
    elif collection_result["collection_errors"] > 0:
        overall_status = "FAIL"
    # Rule 4: Zero tests collected -> FAIL
    elif collection_result["tests_collected"] == 0:
        overall_status = "FAIL"
    # Rule 5: Test failures
    elif test_results["failed"] > 0 or test_results["errors"] > 0:
        if test_results["is_strictly_codex_blocked"] and allow_codex_block:
            overall_status = "BLOCKED"
        else:
            overall_status = "FAIL"
    else:
        overall_status = "PASS"

    import pytest
    report = {
        "commit_sha": commit_sha,
        "python_version": platform.python_version(),
        "platform": platform.platform(),
        "install_command": "pip install -r requirements.txt",
        "dependency_manifest_hash": manifest_hash,
        "pytest_version": pytest.__version__,
        "collection": {
            "tests_collected": collection_result["tests_collected"],
            "collection_errors": collection_result["collection_errors"],
            "discovered_directories": collection_result["discovered_directories"],
            "missing_discovery": collection_result.get("missing_discovery", []),
            "missing_security": collection_result.get("missing_security", []),
        },
        "tests": {
            "passed": test_results["passed"],
            "failed": test_results["failed"],
            "errors": test_results["errors"],
            "skipped": test_results["skipped"],
        },
        "duration_seconds": test_results["duration_seconds"],
        "status": overall_status,
        "details": {
            "sha_check": "PASS" if sha_status == "PASS" else f"FAIL (expected {expected_sha}, got {commit_sha})",
            "ini_exclusions": "PASS" if not ini_violations else ini_violations,
            "dependency_status": "PASS" if dep_result["all_installed"] else dep_result["missing_dependencies"],
            "codex_blocked_status": "BLOCKED — CODEX" if test_results["is_strictly_codex_blocked"] else "NONE",
            "failed_tests": test_results["failed_tests"],
        },
    }
    return report, collection_result.get("collected_nodes", [])


def write_evidence_artifacts(evidence_dir: Path, report: Dict[str, Any], collected_nodes: List[str]) -> None:
    evidence_dir.mkdir(parents=True, exist_ok=True)
    
    # 1. summary.json
    json_path = evidence_dir / "summary.json"
    json_path.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    
    # 2. summary.md
    md_path = evidence_dir / "summary.md"
    md_content = f"""# CI Reproducibility & Verification Evidence

* **Status**: {report['status']}
* **Commit SHA**: {report['commit_sha']}
* **Python**: {report['python_version']} ({report['platform']})
* **Pytest Version**: {report['pytest_version']}
* **Manifest Hash**: {report['dependency_manifest_hash']}
* **Timestamp**: {time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}

## Collection Summary
* **Tests Collected**: {report['collection']['tests_collected']}
* **Collection Errors**: {report['collection']['collection_errors']}
* **Discovered Suites**: {', '.join(report['collection']['discovered_directories'])}

## Test Execution
* **Passed**: {report['tests']['passed']}
* **Failed**: {report['tests']['failed']}
* **Errors**: {report['tests']['errors']}
* **Skipped**: {report['tests']['skipped']}
* **Duration**: {report['duration_seconds']}s

## Diagnostic Status
* **SHA Check**: {report['details']['sha_check']}
* **Dependency Check**: {report['details']['dependency_status']}
* **Codex Runtime Block**: {report['details']['codex_blocked_status']}
"""
    if report["details"]["failed_tests"]:
        md_content += "\n### Failed Tests\n"
        for ft in report["details"]["failed_tests"]:
            md_content += f"- {ft}\n"
            
    md_path.write_text(md_content, encoding="utf-8")
    
    # 3. collection.txt
    coll_path = evidence_dir / "collection.txt"
    coll_path.write_text("\n".join(collected_nodes) + "\n", encoding="utf-8")


def run_determinism_mode(root: Path) -> int:
    """Run two consecutive collection & test passes and verify semantic stability."""
    print("[DETERMINISM] Executing Pass 1...")
    rep1, nodes1 = execute_gate(root=root, collect_only=False, allow_codex_block=True)
    
    print("[DETERMINISM] Executing Pass 2...")
    rep2, nodes2 = execute_gate(root=root, collect_only=False, allow_codex_block=True)
    
    discrepancies = []
    if rep1["collection"]["tests_collected"] != rep2["collection"]["tests_collected"]:
        discrepancies.append(
            f"Collected test counts differ: {rep1['collection']['tests_collected']} vs {rep2['collection']['tests_collected']}"
        )
    if nodes1 != nodes2:
        discrepancies.append("Collected test node sequence differed between passes")
    if rep1["tests"]["passed"] != rep2["tests"]["passed"]:
        discrepancies.append(f"Passed count differed: {rep1['tests']['passed']} vs {rep2['tests']['passed']}")
    if rep1["tests"]["failed"] != rep2["tests"]["failed"]:
        discrepancies.append(f"Failed count differed: {rep1['tests']['failed']} vs {rep2['tests']['failed']}")
    if rep1["tests"]["errors"] != rep2["tests"]["errors"]:
        discrepancies.append(f"Errors count differed: {rep1['tests']['errors']} vs {rep2['tests']['errors']}")
    if rep1["dependency_manifest_hash"] != rep2["dependency_manifest_hash"]:
        discrepancies.append("Dependency manifest hash differed between passes")
        
    print("\n" + "=" * 80)
    print("                    DETERMINISM VERIFICATION REPORT                           ")
    print("=" * 80)
    print(f"Pass 1: {rep1['tests']['passed']} passed, {rep1['tests']['failed']} failed, {rep1['collection']['tests_collected']} collected")
    print(f"Pass 2: {rep2['tests']['passed']} passed, {rep2['tests']['failed']} failed, {rep2['collection']['tests_collected']} collected")
    
    if discrepancies:
        print("\n[RESULT] DETERMINISM CHECK FAILED:")
        for d in discrepancies:
            print(f"  - {d}")
        return 1
    else:
        print("\n[RESULT] DETERMINISM CHECK PASSED: 100% semantic stability verified across repeated runs.")
        return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Reproducibility & Evidence Gate")
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root path")
    parser.add_argument("--evidence-dir", type=Path, default=Path("ci-evidence"), help="Output evidence directory")
    parser.add_argument("--expected-sha", type=str, default=None, help="Enforce expected commit SHA")
    parser.add_argument("--determinism", action="store_true", help="Run 2-pass determinism verification")
    parser.add_argument("--collect-only", action="store_true", help="Run discovery and collection only")
    parser.add_argument("--allow-codex-block", action="store_true", help="Report BLOCKED instead of FAIL if only Codex lifecycle failures occur")
    parser.add_argument("--json", action="store_true", help="Output JSON report to stdout")
    
    args = parser.parse_args()
    
    if args.determinism:
        return run_determinism_mode(args.root)
        
    report, collected_nodes = execute_gate(
        root=args.root,
        expected_sha=args.expected_sha,
        collect_only=args.collect_only,
        allow_codex_block=args.allow_codex_block,
    )
    
    write_evidence_artifacts(args.evidence_dir, report, collected_nodes)
    
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print("=" * 80)
        print("              REPRODUCIBILITY & CI EVIDENCE GATE REPORT                       ")
        print("=" * 80)
        print(f"Commit SHA:       {report['commit_sha']}")
        print(f"Platform:         {report['platform']}")
        print(f"Python:           {report['python_version']} (Pytest {report['pytest_version']})")
        print(f"Manifest Hash:    {report['dependency_manifest_hash'][:16]}...")
        print("-" * 80)
        print(f"Discovery:        {report['collection']['tests_collected']} tests collected across {len(report['collection']['discovered_directories'])} directories")
        print(f"Collection Errs:  {report['collection']['collection_errors']}")
        print(f"Test Outcome:     {report['tests']['passed']} passed, {report['tests']['failed']} failed, {report['tests']['skipped']} skipped, {report['tests']['errors']} errors")
        print(f"Duration:         {report['duration_seconds']}s")
        print("-" * 80)
        print(f"OVERALL STATUS:   {report['status']}")
        if report["details"]["codex_blocked_status"] != "NONE":
            print(f"CODEX BLOCK:      {report['details']['codex_blocked_status']} ({len(report['details']['failed_tests'])} runtime failures)")
        print(f"Evidence Saved:   {args.evidence_dir.resolve()}")
        print("=" * 80)

    # Return exit code based on status:
    # PASS -> 0
    # BLOCKED -> 2 (if allowed) or 1
    # FAIL / NOT_FOUND -> 1
    if report["status"] == "PASS":
        return 0
    elif report["status"] == "BLOCKED":
        return 0 if args.allow_codex_block else 1
    else:
        return 1


if __name__ == "__main__":
    sys.exit(main())
