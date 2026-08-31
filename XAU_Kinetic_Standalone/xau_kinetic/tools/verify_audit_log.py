"""
SHA-256 Audit Log Integrity Verification CLI Tool.
Verifies tamper-evident cryptographic hash chain of SQLite audit databases.
"""

import argparse
import sys
from pathlib import Path

from xau_kinetic.infrastructure.persistence import SQLitePersistence


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Verify XAU_Kinetic SHA-256 Chained Audit Log Database")
    parser.add_argument("--db", type=str, default="xau_kinetic_audit.db", help="Path to SQLite audit database file")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    db_file = Path(args.db)

    if not db_file.exists():
        print(f"[ERROR] Audit database file '{db_file.resolve()}' does not exist.")
        sys.exit(1)

    print(f"=== XAU_Kinetic SHA-256 Audit Chain Verification ===")
    print(f"Target DB File: {db_file.resolve()}")

    persistence = SQLitePersistence(db_path=db_file)
    valid, message = persistence.verify_chain_integrity()

    last_hash = persistence.get_last_audit_hash()
    print(f"Head Hash: {last_hash}")

    if valid:
        print(f"\n[PASS] Cryptographic Audit Chain Status: VERIFIED VALID")
        print(f"Details: {message}")
        sys.exit(0)
    else:
        print(f"\n[FAIL] Cryptographic Audit Chain Status: TAMPER DETECTED / INVALID")
        print(f"Details: {message}")
        sys.exit(2)


if __name__ == "__main__":
    main()
