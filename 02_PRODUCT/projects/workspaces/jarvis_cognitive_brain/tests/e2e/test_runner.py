"""Jarvis Cognitive Brain Dedicated E2E Test Suite Runner."""

import sys
import os
import time
import argparse
from pathlib import Path
import pytest

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


class StructuredE2ERunner:
    """Orchestrates and reports E2E test execution across all tiers."""

    TIERS = {
        "tier1": {
            "name": "Tier 1: Feature Coverage (R1-R5)",
            "path": "tests/e2e/tier1_features",
            "min_tests": 50,
        },
        "tier2": {
            "name": "Tier 2: Boundaries & Invariants (P0-P18)",
            "path": "tests/e2e/tier2_boundaries",
            "min_tests": 25,
        },
        "tier3": {
            "name": "Tier 3: Pairwise Cross-Feature Interactions",
            "path": "tests/e2e/tier3_combinations",
            "min_tests": 20,
        },
        "tier4": {
            "name": "Tier 4: Real-World Workload Scenarios",
            "path": "tests/e2e/tier4_workloads",
            "min_tests": 10,
        },
    }

    def __init__(self, selected_tier: str = "all", verbose: bool = True):
        self.selected_tier = selected_tier
        self.verbose = verbose
        self.results = {}

    def run(self) -> int:
        print("=" * 80)
        print(" JARVIS COGNITIVE BRAIN — DUAL-TRACK E2E TEST RUNNER")
        print("=" * 80)
        start_time = time.time()

        tiers_to_run = (
            [self.selected_tier]
            if self.selected_tier != "all" and self.selected_tier in self.TIERS
            else list(self.TIERS.keys())
        )

        overall_passed = True

        for tier_key in tiers_to_run:
            tier_info = self.TIERS[tier_key]
            tier_path = PROJECT_ROOT / tier_info["path"]
            if not tier_path.exists():
                continue

            print(f"\n>>> Running {tier_info['name']} [{tier_info['path']}]...")
            t0 = time.time()

            pytest_args = [
                str(tier_path),
                "-v" if self.verbose else "-q",
                "--tb=short",
                "-p", "pytest_asyncio",
                "-o", f"pythonpath={PROJECT_ROOT}",
            ]
            exit_code = pytest.main(pytest_args)
            t_elapsed = time.time() - t0

            passed = (exit_code == pytest.ExitCode.OK or exit_code == 0)
            self.results[tier_key] = {
                "name": tier_info["name"],
                "passed": passed,
                "exit_code": exit_code,
                "elapsed_s": t_elapsed,
            }

            if not passed:
                overall_passed = False

        total_elapsed = time.time() - start_time

        print("\n" + "=" * 80)
        print(" E2E TEST SUITE EXECUTION SUMMARY")
        print("=" * 80)
        for tier_key, res in self.results.items():
            status_str = "[PASS] SUCCESS" if res["passed"] else "[FAIL] FAILED"
            print(f" * {res['name']:<50} {status_str:<15} ({res['elapsed_s']:.2f}s)")

        print("-" * 80)
        print(f" Total Duration: {total_elapsed:.2f}s")
        print(f" Overall Status: {'PASSED (100% Pass Rate)' if overall_passed else 'FAILED'}")
        print("=" * 80)

        return 0 if overall_passed else 1


def main():
    parser = argparse.ArgumentParser(description="Jarvis Cognitive Brain E2E Test Suite Runner")
    parser.add_argument(
        "--tier",
        choices=["all", "tier1", "tier2", "tier3", "tier4"],
        default="all",
        help="Specify which tier to execute (default: all)",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Run pytest in quiet mode",
    )
    args = parser.parse_args()

    runner = StructuredE2ERunner(selected_tier=args.tier, verbose=not args.quiet)
    sys.exit(runner.run())


if __name__ == "__main__":
    main()
