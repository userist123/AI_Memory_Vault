"""
run_all.py — Verificator de Reproductibilitate Octet-cu-Octet (V1 + V2 Research Suite)

Funcționalitate:
1. Regenerează toate tabelele de date offline (V1 și V2) într-un director temporar izolat.
2. Compară binar (octet cu octet) fiecare fișier generat cu versiunea comisă în depozit.
3. Verifică integritatea lanțului criptografic SHA-256 din prospective_log.jsonl.
4. Iese cu cod de eroare nenul dacă există chiar și un singur octet diferit.
5. Opțional: flag-ul --include-power re-rulează simularea Monte Carlo pentru Table 8.
"""

import argparse
import hashlib
import os
import shutil
import subprocess
import sys

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
TEMP_DIR = os.path.join(BASE_DIR, "temp_reproducibility_tables")


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def run_cmd(cmd_list: list):
    print(f"Running: {' '.join(cmd_list)}...", flush=True)
    res = subprocess.run([sys.executable] + cmd_list, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"Command failed with code {res.returncode}:\n{res.stderr}\n{res.stdout}", file=sys.stderr)
        sys.exit(res.returncode)


def main():
    parser = argparse.ArgumentParser(description="V1 + V2 Reproducibility Runner")
    parser.add_argument("--include-power", action="store_true", help="Re-run full Monte Carlo simulation for Table 8 (~2 min)")
    args = parser.parse_args()

    print("=== START REPRODUCIBILITY RUNNER (MT5 V1 + V2 RESEARCH SUITE) ===", flush=True)

    if os.path.exists(TEMP_DIR):
        shutil.rmtree(TEMP_DIR)
    os.makedirs(TEMP_DIR, exist_ok=True)

    # 1. V1: Regenerare tabele calitative (table_2)
    run_cmd([os.path.join(BASE_DIR, "fetch_quality.py"), "--out-dir", TEMP_DIR])

    # 2. V1: Regenerare tabele costuri (table_3)
    run_cmd([os.path.join(BASE_DIR, "fetch_costs.py"), "--out-dir", TEMP_DIR])

    # 3. V1: Regenerare tabele analiză și teste SPA (table_4, table_5, table_6, table_7)
    run_cmd([os.path.join(BASE_DIR, "analyze_strategies.py"), "--out-dir", TEMP_DIR])

    # 4. V2: Regenerare test robustețe zero-spread (table_6b)
    run_cmd([os.path.join(BASE_DIR, "analyze_zero_spread_robustness.py"), "--out-dir", TEMP_DIR])

    # 5. V2: Regenerare test holdout bot Owner XAUUSD (table_10)
    run_cmd([os.path.join(BASE_DIR, "analyze_owner_bot_holdout.py"), "--out-dir", TEMP_DIR])

    # 6. V2: Regenerare ipoteze exploratorii (table_11)
    run_cmd([os.path.join(BASE_DIR, "analyze_exploratory_hypotheses.py"), "--out-dir", TEMP_DIR])

    # 7. V2: Putere statistică (table_8) - dacă e solicitat
    if args.include_power:
        run_cmd([os.path.join(BASE_DIR, "analyze_statistical_power.py"), "--out-dir", TEMP_DIR])

    # 8. Verificare comparativă octet-cu-octet a tuturor tabelelor regenerate offline
    verified_files = [
        "table_2_quality.csv",
        "table_3_costs_hourly.csv",
        "table_3_costs_summary.csv",
        "table_4_development_results.csv",
        "table_5_validation_results.csv",
        "table_6_validation_spa_tests.csv",
        "table_6b_zero_spread_robustness.csv",
        "table_7_commission_sensitivity.csv",
        "table_10_owner_bot_holdout.csv",
        "table_11_exploratory_hypotheses.csv",
    ]

    if args.include_power:
        verified_files.append("table_8_power_analysis.csv")

    mismatches = []
    print("\nComparing regenerated tables against committed tables byte-for-byte...", flush=True)

    for fname in verified_files:
        committed_file = os.path.join(TABLES_DIR, fname)
        temp_file = os.path.join(TEMP_DIR, fname)

        if not os.path.exists(committed_file):
            mismatches.append((fname, "COMMITTED_MISSING", ""))
            continue
        if not os.path.exists(temp_file):
            mismatches.append((fname, "TEMP_MISSING", ""))
            continue

        with open(committed_file, "rb") as f:
            comm_bytes = f.read()
        with open(temp_file, "rb") as f:
            temp_bytes = f.read()

        h_comm = hashlib.sha256(comm_bytes).hexdigest()
        h_temp = hashlib.sha256(temp_bytes).hexdigest()

        if comm_bytes == temp_bytes:
            print(f"  [MATCH] {fname:36s} | Bytes: {len(comm_bytes):6d} | SHA: {h_comm[:12]}...", flush=True)
        else:
            print(f"  [DIFF!] {fname:36s} | Comm SHA: {h_comm[:12]} != Temp SHA: {h_temp[:12]}", file=sys.stderr, flush=True)
            mismatches.append((fname, h_comm, h_temp))

    # Verificare hash-uri pentru tabelele extrase live (table_1 census, table_9 ticks, table_8 dacă nu e re-rulat)
    fixed_artifacts = [
        ("table_1_census.csv", "5c6b289d309331a2ddd8ffa9b95527b9344b85afd40c0cc65615abf6bbc87629"),
        ("table_9_tick_cost_comparison.csv", "b43401bf12290c869f3993173b608ab43436fd4f275b7d1893741e3d8b77183c"),
    ]
    if not args.include_power:
        fixed_artifacts.append(("table_8_power_analysis.csv", "acfa945e1da4e5df1bdac17516f43b4d3cc9d50e0eba6e1917cf4190d980ca95"))

    print("\nVerifying fixed empirical artifacts cryptographic SHA-256 hashes...", flush=True)
    for fname, expected_hash in fixed_artifacts:
        path = os.path.join(TABLES_DIR, fname)
        if not os.path.exists(path):
            mismatches.append((fname, "MISSING_STATIC_ARTIFACT", ""))
            continue
        actual_hash = sha256_file(path)
        if actual_hash == expected_hash:
            print(f"  [VERIFIED] {fname:36s} | SHA: {actual_hash[:12]}...", flush=True)
        else:
            print(f"  [CORRUPT!] {fname:36s} | Expected: {expected_hash[:12]} != Actual: {actual_hash[:12]}", file=sys.stderr, flush=True)
            mismatches.append((fname, expected_hash, actual_hash))

    # 9. Verificare integritate lanț prospectiv
    print("\nVerifying prospective logger tamper-evident SHA-256 chain...", flush=True)
    res_chain = subprocess.run([sys.executable, os.path.join(BASE_DIR, "prospective_logger.py"), "--verify"], capture_output=True, text=True)
    if res_chain.returncode == 0:
        print("  [VERIFIED] prospective_log.jsonl SHA-256 hash chain intact.", flush=True)
    else:
        print(f"  [CORRUPT!] prospective_log.jsonl verification failed:\n{res_chain.stderr}", file=sys.stderr, flush=True)
        mismatches.append(("prospective_log.jsonl", "CHAIN_ERROR", ""))

    # Curățare director temporar
    shutil.rmtree(TEMP_DIR, ignore_errors=True)

    if mismatches:
        print(f"\nFAILED: {len(mismatches)} files have byte differences or integrity failures!", file=sys.stderr, flush=True)
        sys.exit(1)

    print(f"\nREPRODUCIBILITY_VERIFIED: All tables and artifacts regenerated and verified byte-for-byte identically.", flush=True)
    print("=== MT5 RESEARCH SUITE V1 + V2 VALIDATION COMPLETE ===", flush=True)


if __name__ == "__main__":
    main()
