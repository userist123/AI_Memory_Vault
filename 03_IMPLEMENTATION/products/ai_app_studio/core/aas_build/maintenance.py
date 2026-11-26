"""Owner-run maintenance (never called by the app): ``python -m aas_build.maintenance purge-inspect <studio.db>``.

STOP THE STUDIO APP FIRST (backend and MCP server). The script takes ``activity.lock`` next to the database and
an immediate SQLite write transaction; if either is held (the studio is writing) it refuses with
``MaintenanceRefused`` and changes nothing.

Removes ONLY the run-less ``tool.call`` rows whose tool is ``project.inspect`` (written by read GETs before the
non-emitting read path existed; they made the desktop selection ping-pong). The newest row is never removed (the
table has no AUTOINCREMENT, so deleting it would let SQLite hand out a seq a client has already seen - same rule
as ``activity.prune``). A backup copy of the database is made first (SQLite online backup, next to the file).

The tamper-evidence chain is repaired narrowly: only a surviving row whose ``prev_seq`` points at a deleted row is
re-linked, to the nearest surviving predecessor along the chain (the deleted row's own ``prev_seq``, followed
through further deleted rows). Every other row is left untouched, so a pre-existing gap stays visible to
``find_gaps`` and the oldest row's ``prev_seq`` (the ``pruned_through`` retention marker) is preserved. One
``tool.call`` row (tool ``maintenance.purge-inspect``) is appended with the counts and the backup file name.
"""

from __future__ import annotations

import datetime as _dt
import json
import sqlite3
import sys
import time
from pathlib import Path
from typing import Any

_WHERE = "kind = 'tool.call' AND (run_id IS NULL OR run_id = '') AND json_extract(payload, '$.tool') = 'project.inspect'"
MAINTENANCE_TOOL = "maintenance.purge-inspect"


class MaintenanceRefused(RuntimeError):
    """The studio holds the activity lock or the database write lock: stop the app first."""


def _relink_target(prev: int, prev_of: dict[int, int], deleted: set[int]) -> int:
    seen: set[int] = set()
    while prev in deleted and prev not in seen:
        seen.add(prev)
        prev = prev_of[prev]
    return prev


def purge_runless_inspect(db: Path, *, lock_timeout: float = 5.0) -> dict[str, Any]:
    from studio_core.activity import _build_payload
    from studio_core.migrations import interprocess_lock

    db = Path(db)
    if not db.is_file():
        raise FileNotFoundError(str(db))
    lock = interprocess_lock(db.parent / "activity.lock", timeout=lock_timeout)
    try:
        lock.__enter__()
    except TimeoutError as exc:
        raise MaintenanceRefused("activity.lock is held: stop the studio app first") from exc
    try:
        src = sqlite3.connect(db, timeout=lock_timeout, isolation_level=None)
        try:
            try:  # probe the write lock first: the online backup would wait forever on a busy writer
                src.execute("BEGIN IMMEDIATE")
                src.execute("ROLLBACK")
            except sqlite3.OperationalError as exc:
                raise MaintenanceRefused(f"database is locked ({exc}): stop the studio app first") from exc
            backup = db.with_name(f"{db.stem}.pre-purge-{time.strftime('%Y%m%d-%H%M%S')}{db.suffix}")
            dst = sqlite3.connect(backup)
            try:
                src.backup(dst)  # outside a transaction (a backup from inside one never finishes)
            finally:
                dst.close()
            try:
                src.execute("BEGIN IMMEDIATE")
            except sqlite3.OperationalError as exc:
                raise MaintenanceRefused(f"database is locked ({exc}): stop the studio app first") from exc
            try:
                rows = src.execute("SELECT seq, prev_seq FROM activity_events ORDER BY seq").fetchall()
                total = len(rows)
                newest = rows[-1][0] if rows else 0
                deleted = {r[0] for r in src.execute(f"SELECT seq FROM activity_events WHERE {_WHERE} AND seq < ?",
                                                     (newest,))}
                prev_of = {s: p for s, p in rows}
                relinks = [(_relink_target(p, prev_of, deleted), s) for s, p in rows if s not in deleted and p in deleted]
                src.executemany("UPDATE activity_events SET prev_seq = ? WHERE seq = ?", relinks)
                src.executemany("DELETE FROM activity_events WHERE seq = ?", [(s,) for s in sorted(deleted)])
                remaining = src.execute("SELECT COUNT(*) FROM activity_events").fetchone()[0]
                note = (f"removed={len(deleted)}; relinked={len(relinks)}; before={total}; remaining={remaining}; "
                        f"backup={backup.name}")
                payload, redactions, dropped = _build_payload("tool.call", {
                    "tool": MAINTENANCE_TOOL, "status": "ok", "actor": "owner (maintenance script)", "input": note})
                ts = _dt.datetime.now(_dt.UTC).replace(tzinfo=None).isoformat(sep=" ", timespec="microseconds")
                cur = src.execute(
                    "INSERT INTO activity_events (prev_seq, ts, run_id, project_id, kind, source, audit_id, payload, "
                    "artifact_sha256, redactions, dropped) VALUES (?, ?, NULL, NULL, 'tool.call', 'server', NULL, ?, "
                    "NULL, ?, ?)", (newest, ts, json.dumps(payload), redactions, dropped))
                audit_seq = cur.lastrowid
                src.execute("COMMIT")
            except BaseException:
                src.execute("ROLLBACK")
                raise
        finally:
            src.close()
    finally:
        lock.__exit__(None, None, None)
    return {"before": total, "removed": len(deleted), "relinked": len(relinks), "remaining": remaining,
            "auditSeq": audit_seq, "backup": str(backup)}


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "purge-inspect":
        sys.exit("usage: python -m aas_build.maintenance purge-inspect <path to studio.db>  (stop the studio first)")
    try:
        print(purge_runless_inspect(Path(sys.argv[2])))
    except MaintenanceRefused as exc:
        sys.exit(f"refused: {exc}")
