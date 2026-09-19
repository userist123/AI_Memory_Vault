"""
run_all_v3.py — Complete Verification & Reproducibility Suite for Planning Influence V3

1. Runs isolation unit tests (20_TESTS/test_planning_influence_isolation.py) — exits non-zero on failure.
2. Runs full v3 experiment with frozen seeds into a temporary directory.
3. Compares byte-for-byte regenerated tables against canonical tables (07_EVALUATION/luna/tables/table_luna_1..5.csv).
4. Verifies report generator reproducibility (07_EVALUATION/luna/PLANNING_INFLUENCE_RESULTS_V3.md).
5. Reports SHA-256 hashes of all tables and match status.
6. Exits with non-zero status if any table or report differs.
7. Prints exact success message: "TOATE CELE 5 TABELE REPRODUSE IDENTIC LA OCTET (N=200/celulă)".
"""
import hashlib
import os
import shutil
import subprocess
import sys
import tempfile

EXPECTED_TABLES = [
    "table_luna_1_main_experiment.csv",
    "table_luna_2_accuracy_thresholds.csv",
    "table_luna_3_stale_arm.csv",
    "table_luna_4_verification_ablation.csv",
    "table_luna_5_robustness_grid.csv",
]

CANONICAL_TABLES_DIR = os.path.join("07_EVALUATION", "luna", "tables")
REPORT_PATH = os.path.join("07_EVALUATION", "luna", "PLANNING_INFLUENCE_RESULTS_V3.md")


def lf_sha256_bytes(b: bytes) -> str:
    norm = b.replace(b"\r\n", b"\n")
    return hashlib.sha256(norm).hexdigest()


def main():
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(encoding="utf-8")
        except Exception:
            pass

    print("-" * 70)
    print("PLANNING INFLUENCE V3 -- REPRODUCIBILITY & ISOLATION VERIFICATION")
    print("-" * 70)

    # 1. Run isolation tests via pytest
    test_script = os.path.join("20_TESTS", "test_planning_influence_isolation.py")
    print(f"\n[Step 1/4] Running isolation tests: {test_script}")
    res = subprocess.run([sys.executable, "-m", "pytest", "-q", test_script], capture_output=True, text=True)
    print(res.stdout)
    if res.stderr:
        print(res.stderr, file=sys.stderr)
    if res.returncode != 0:
        print("ERROR: Isolation tests failed!", file=sys.stderr)
        sys.exit(res.returncode)
    print("[OK] All isolation tests passed.")

    # 2. Run full experiment in a temporary directory
    print(f"\n[Step 2/4] Running full v3 experiments into temporary directory...")
    temp_dir = tempfile.mkdtemp(prefix="luna_v3_repro_")
    try:
        runner_script = os.path.join("07_EVALUATION", "luna", "run_experiments_v3.py")
        exp_res = subprocess.run(
            [sys.executable, runner_script, "--out-dir", temp_dir, "--n-scenarios", "200"],
            capture_output=True,
            text=True,
        )
        if exp_res.returncode != 0:
            print("ERROR running experiments:", file=sys.stderr)
            print(exp_res.stdout)
            print(exp_res.stderr, file=sys.stderr)
            sys.exit(exp_res.returncode)
        print("[OK] Experiments completed successfully.")

        # 3. Compare tables byte-for-byte (LF-normalized) and check SHA-256
        print(f"\n[Step 3/4] Byte-for-byte table verification against {CANONICAL_TABLES_DIR}...")
        all_matched = True
        print(f"{'Table Name':<38} | {'Status':<10} | {'SHA-256 Hash'}")
        print("-" * 85)

        for tbl in EXPECTED_TABLES:
            canonical_path = os.path.join(CANONICAL_TABLES_DIR, tbl)
            regenerated_path = os.path.join(temp_dir, tbl)

            if not os.path.exists(canonical_path):
                print(f"{tbl:<38} | MISSING_CANONICAL | -")
                all_matched = False
                continue

            if not os.path.exists(regenerated_path):
                print(f"{tbl:<38} | MISSING_REGEN     | -")
                all_matched = False
                continue

            with open(canonical_path, "rb") as f:
                c_raw = f.read()
            with open(regenerated_path, "rb") as f:
                r_raw = f.read()

            c_norm = c_raw.replace(b"\r\n", b"\n")
            r_norm = r_raw.replace(b"\r\n", b"\n")

            c_hash = hashlib.sha256(c_norm).hexdigest()
            r_hash = hashlib.sha256(r_norm).hexdigest()

            if c_norm == r_norm:
                print(f"{tbl:<38} | MATCH      | {c_hash[:16]}...{c_hash[-8:]}")
            else:
                print(f"{tbl:<38} | MISMATCH   | C:{c_hash[:8]} vs R:{r_hash[:8]}")
                all_matched = False

        print("-" * 85)
        if not all_matched:
            print("FAILED: One or more tables do not match canonical repository tables!", file=sys.stderr)
            sys.exit(1)

        print("\nTOATE CELE 5 TABELE REPRODUSE IDENTIC LA OCTET (N=200/celulă)")

        # 4. Verify report generation reproducibility
        print(f"\n[Step 4/4] Verifying report generation reproducibility: {REPORT_PATH}...")
        with open(REPORT_PATH, "rb") as f:
            existing_report = f.read().replace(b"\r\n", b"\n")

        rep_gen_script = os.path.join("07_EVALUATION", "luna", "generate_planning_influence_report.py")
        gen_res = subprocess.run([sys.executable, rep_gen_script], capture_output=True, text=True)
        if gen_res.returncode != 0:
            print("ERROR running report generator:", file=sys.stderr)
            print(gen_res.stderr, file=sys.stderr)
            sys.exit(gen_res.returncode)

        with open(REPORT_PATH, "rb") as f:
            regen_report = f.read().replace(b"\r\n", b"\n")

        if existing_report == regen_report:
            print(f"[OK] Report {REPORT_PATH} matches freshly generated output byte-for-byte.")
        else:
            print("ERROR: Report changed upon regeneration!", file=sys.stderr)
            sys.exit(1)

        print("=" * 70)
        print("=== TOATE VERIFICĂRILE AU TRECUT CU SUCCES ===")

    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)


if __name__ == "__main__":
    main()
