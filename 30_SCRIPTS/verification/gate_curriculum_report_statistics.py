#!/usr/bin/env python3
"""Automated Quality Gate for Curriculum Report Statistics.

Enforces:
1. Sample size N or n must be explicitly declared for reported proportions.
2. Confidence intervals (e.g. Wilson score 95% CI) must accompany proportion estimates.
3. Comparative dual-arm benchmark reports must include paired test statistics (McNemar test).

Provides:
- Strict gate verification for curriculum benchmark reports (exits 1 on violation).
- Informational audit scanning legacy vault reports to identify historical gaps without blocking CI.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]
CURRICULUM_REPORTS_MD = REPO_ROOT / "07_EVALUATION" / "curriculum" / "reports"
CURRICULUM_REPORTS_JSON = REPO_ROOT / "08_OBSERVABILITY" / "reports"


def check_report_statistics(content: str, is_json: bool = False) -> Tuple[bool, List[str]]:
    """Validates that a benchmark report satisfies the small sample proportion reporting standard."""
    errors: List[str] = []

    if is_json:
        try:
            data = json.loads(content)
        except Exception as e:
            return False, [f"Invalid JSON content: {e}"]

        # 1. Sample size check
        sample_n = (
            data.get("paired_statistics", {}).get("sample_size_n")
            or data.get("control_arm", {}).get("review_questions", {}).get("total")
            or data.get("sample_size")
        )
        if not sample_n:
            errors.append("JSON report missing explicit sample size N ('paired_statistics.sample_size_n' or 'total')")

        # 2. Confidence interval check
        c_ci = data.get("control_arm", {}).get("review_questions", {}).get("wilson_95_ci")
        t_ci = data.get("treatment_arm", {}).get("review_questions", {}).get("wilson_95_ci")
        if not c_ci or not t_ci:
            errors.append("JSON report missing 'wilson_95_ci' confidence interval objects in control and treatment arms")
        else:
            if "lower" not in c_ci or "upper" not in c_ci:
                errors.append("Control arm Wilson CI missing 'lower' or 'upper' boundary")
            if "lower" not in t_ci or "upper" not in t_ci:
                errors.append("Treatment arm Wilson CI missing 'lower' or 'upper' boundary")

        # 3. Paired statistical test check (for dual-arm reports)
        if "control_arm" in data and "treatment_arm" in data:
            mcn = data.get("paired_statistics", {}).get("mcnemar_exact")
            if not mcn or "two_sided_p_value" not in mcn:
                errors.append("Dual-arm JSON report missing exact McNemar paired test ('paired_statistics.mcnemar_exact')")

    else:
        # Markdown report check
        lower = content.lower()

        # 1. Sample size check: must state n= or N =
        has_sample_n = bool(re.search(r"\b[nN]\s*=\s*\d+", content)) or bool(re.search(r"\(n=\d+\)", content))
        if not has_sample_n:
            errors.append("Markdown report missing explicit sample size (e.g. 'N = 12' or '(n=12)')")

        # 2. Confidence interval check: must mention Wilson or CI with bracket range
        has_ci = bool(re.search(r"\[\s*0\.\d{2,4}\s*,\s*0\.\d{2,4}\s*\]", content)) or (
            "wilson" in lower and ("ci" in lower or "interval" in lower)
        )
        if not has_ci:
            errors.append("Markdown report missing confidence interval brackets (e.g. '[0.320, 0.807]') or Wilson CI specification")

        # 3. Paired test check (if comparison table between control and treatment exists)
        if "control" in lower and "treatment" in lower:
            has_paired = "mcnemar" in lower or "paired" in lower
            if not has_paired:
                errors.append("Dual-arm markdown report missing paired statistical analysis (McNemar test)")

    return len(errors) == 0, errors


def audit_curriculum_reports() -> Tuple[int, int]:
    """Audits all curriculum reports under canonical locations."""
    md_files = list(CURRICULUM_REPORTS_MD.glob("*.md")) if CURRICULUM_REPORTS_MD.exists() else []
    json_files = list(CURRICULUM_REPORTS_JSON.glob("curriculum_heldout_eval_*.json")) if CURRICULUM_REPORTS_JSON.exists() else []

    total = len(md_files) + len(json_files)
    if total == 0:
        print("Warning: No curriculum reports found to audit.", file=sys.stderr)
        return 0, 0

    failed = 0
    for f in md_files:
        ok, errors = check_report_statistics(f.read_text(encoding="utf-8"), is_json=False)
        if not ok:
            failed += 1
            print(f"FAILED: {f.name}", file=sys.stderr)
            for e in errors:
                print(f"  - {e}", file=sys.stderr)
        else:
            print(f"PASS: {f.name}")

    for f in json_files:
        ok, errors = check_report_statistics(f.read_text(encoding="utf-8"), is_json=True)
        if not ok:
            failed += 1
            print(f"FAILED: {f.name}", file=sys.stderr)
            for e in errors:
                print(f"  - {e}", file=sys.stderr)
        else:
            print(f"PASS: {f.name}")

    return total, failed


def audit_legacy_vault_reports() -> None:
    """Scans other historical markdown reports across 08_OBSERVABILITY/reports for informational purposes."""
    obs_dir = REPO_ROOT / "08_OBSERVABILITY" / "reports"
    if not obs_dir.exists():
        return

    md_reports = [f for f in obs_dir.glob("*.md") if "curriculum" not in f.name.lower()]
    print("\n--- Informational Legacy Report Audit ---")
    failing_legacy = []
    passing_legacy = []

    for f in md_reports:
        ok, _ = check_report_statistics(f.read_text(encoding="utf-8"), is_json=False)
        if ok:
            passing_legacy.append(f.name)
        else:
            failing_legacy.append(f.name)

    print(f"Legacy reports meeting strict standard: {len(passing_legacy)}")
    print(f"Legacy reports lacking explicit sample sizes/CIs: {len(failing_legacy)}")
    if failing_legacy:
        print("Legacy candidates for future statistical back-porting:")
        for name in failing_legacy[:5]:
            print(f"  * {name}")
        if len(failing_legacy) > 5:
            print(f"  ... and {len(failing_legacy) - 5} more")


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit and gate curriculum report statistics.")
    parser.add_argument("--check-file", type=Path, help="Check a specific report file (MD or JSON)")
    parser.add_argument("--audit-legacy", action="store_true", help="Run informational audit on legacy reports")
    args = parser.parse_args()

    if args.check_file:
        if not args.check_file.exists():
            print(f"Error: File not found: {args.check_file}", file=sys.stderr)
            return 1
        is_json = args.check_file.suffix.lower() == ".json"
        ok, errors = check_report_statistics(args.check_file.read_text(encoding="utf-8"), is_json=is_json)
        if not ok:
            print(f"GATE FAILED for {args.check_file}:", file=sys.stderr)
            for e in errors:
                print(f"  - {e}", file=sys.stderr)
            return 1
        print(f"GATE PASSED: {args.check_file} satisfies statistical reporting standard.")
        return 0

    total, failed = audit_curriculum_reports()
    if args.audit_legacy:
        audit_legacy_vault_reports()

    if failed > 0:
        print(f"\nQuality Gate FAILED: {failed}/{total} reports violated statistical standards.", file=sys.stderr)
        return 1

    print(f"\nQuality Gate PASSED: All {total} curriculum benchmark reports verified.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
