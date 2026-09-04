"""
Performance & Audit Ledger Export CLI Utility.
Queries SQLite audit log database and exports audit events and trade history to JSON/CSV files.
"""

import argparse
import json
import sqlite3
import sys
from pathlib import Path

from xau_kinetic.infrastructure.persistence import SQLitePersistence


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export XAU_Kinetic Performance & Audit Ledger Reports")
    parser.add_argument("--db", type=str, default="xau_kinetic_audit.db", help="Path to SQLite audit database")
    parser.add_argument("--out-json", type=str, default="audit_report.json", help="Output JSON path")
    parser.add_argument("--out-csv", type=str, default="audit_events.csv", help="Output CSV path")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    db_file = Path(args.db)

    if not db_file.exists():
        print(f"[ERROR] Database file '{db_file.resolve()}' not found.")
        sys.exit(1)

    print(f"=== XAU_Kinetic Audit Ledger Export Utility ===")
    print(f"Reading database: {db_file.resolve()}")

    persistence = SQLitePersistence(db_path=db_file)
    valid, msg = persistence.verify_chain_integrity()

    # Query audit events
    conn = sqlite3.connect(str(db_file))
    conn.row_factory = sqlite3.Row
    cursor = conn.execute("SELECT * FROM audit_log ORDER BY id ASC;")
    rows = cursor.fetchall()
    conn.close()

    events = []
    csv_lines = ["id,event_id,timestamp,event_type,prev_hash,current_hash,payload\n"]

    for r in rows:
        payload_data = json.loads(r["payload"]) if r["payload"] else {}
        item = {
            "id": r["id"],
            "event_id": r["event_id"],
            "timestamp": r["timestamp"],
            "event_type": r["event_type"],
            "prev_hash": r["prev_hash"],
            "current_hash": r["current_hash"],
            "payload": payload_data,
        }
        events.append(item)
        payload_str_escaped = r["payload"].replace('"', '""')
        csv_lines.append(
            f'{r["id"]},"{r["event_id"]}","{r["timestamp"]}","{r["event_type"]}","{r["prev_hash"]}","{r["current_hash"]}","{payload_str_escaped}"\n'
        )

    # Write JSON report
    report = {
        "db_path": str(db_file.resolve()),
        "total_records": len(events),
        "chain_integrity_valid": valid,
        "integrity_message": msg,
        "head_hash": persistence.get_last_audit_hash(),
        "events": events,
    }

    json_path = Path(args.out_json)
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    print(f"[SUCCESS] Exported JSON audit report to '{json_path.resolve()}' ({len(events)} records).")

    csv_path = Path(args.out_csv)
    with open(csv_path, "w", encoding="utf-8") as f:
        f.writelines(csv_lines)
    print(f"[SUCCESS] Exported CSV audit events to '{csv_path.resolve()}'.")


if __name__ == "__main__":
    main()
