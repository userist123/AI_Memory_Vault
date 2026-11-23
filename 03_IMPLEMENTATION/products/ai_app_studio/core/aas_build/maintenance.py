"""Owner-run maintenance (never called by the app): ``python -m aas_build.maintenance purge-inspect <studio.db>``.

Removes ONLY the run-less ``tool.call`` rows whose tool is ``project.inspect`` (written by read GETs before the
non-emitting read path existed; they made the desktop selection ping-pong). A backup copy of the database is made
first (SQLite online backup, next to the file), and ``prev_seq`` of the surviving rows is re-linked so the
tamper-evidence chain stays gap-free.
"""

from __future__ import annotations

import sqlite3
import sys
import time
from pathlib import Path
from typing import Any

_WHERE = "kind = 'tool.call' AND (run_id IS NULL OR run_id = '') AND json_extract(payload, '$.tool') = 'project.inspect'"


def purge_runless_inspect(db: Path) -> dict[str, Any]:
    db = Path(db)
    if not db.is_file():
        raise FileNotFoundError(str(db))
    backup = db.with_name(f"{db.stem}.pre-purge-{time.strftime('%Y%m%d-%H%M%S')}{db.suffix}")
    src = sqlite3.connect(db)
    try:
        dst = sqlite3.connect(backup)
        try:
            src.backup(dst)
        finally:
            dst.close()
        total = src.execute("SELECT COUNT(*) FROM activity_events").fetchone()[0]
        n = src.execute(f"SELECT COUNT(*) FROM activity_events WHERE {_WHERE}").fetchone()[0]
        src.execute(f"DELETE FROM activity_events WHERE {_WHERE}")
        # re-link the chain: each row points at the surviving row before it
        src.execute(
            "UPDATE activity_events SET prev_seq = COALESCE("
            "(SELECT MAX(b.seq) FROM activity_events b WHERE b.seq < activity_events.seq), 0)"
        )
        src.commit()
        remaining = src.execute("SELECT COUNT(*) FROM activity_events").fetchone()[0]
    finally:
        src.close()
    return {"before": total, "removed": n, "remaining": remaining, "backup": str(backup)}


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "purge-inspect":
        sys.exit("usage: python -m aas_build.maintenance purge-inspect <path to studio.db>")
    print(purge_runless_inspect(Path(sys.argv[2])))
