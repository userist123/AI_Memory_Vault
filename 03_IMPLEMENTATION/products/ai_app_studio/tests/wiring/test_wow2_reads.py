"""wow2 B2a: owner read GETs are silent (no run-less project.inspect activity event); the maintenance script removes
only those rows and backs the DB up first."""
from __future__ import annotations

import sqlite3
from pathlib import Path

from sqlalchemy import select
from studio_core import project_kind
from studio_core.aas_models import ActivityEvent
from studio_core.config import Settings


def inspect_rows(studio) -> int:  # type: ignore[no-untyped-def]
    with studio.session() as s:
        rows = s.execute(select(ActivityEvent).where(ActivityEvent.kind == "tool.call")).scalars().all()
        return sum(1 for r in rows if (r.payload or {}).get("tool") == "project.inspect")


def test_read_gets_do_not_emit_inspect_events(tmp_path: Path) -> None:
    from apps.backend.main import create_app
    from starlette.testclient import TestClient
    tok = "t" * 40
    h = {"Authorization": f"Bearer {tok}"}
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), tok)
    try:
        with TestClient(app, base_url="http://127.0.0.1:17875") as c:
            pid = c.post("/api/v1/projects", json={"name": "Notes"}, headers=h).json()["id"]
            studio = app.state.studio
            base = f"/api/v1/projects/{pid}"
            before = inspect_rows(studio)
            studio.get_project(studio_owner(), pid, with_components=False)  # positive control: get_project still emits
            assert inspect_rows(studio) == before + 1
            before = inspect_rows(studio)
            for path in ("/brand", "/baselines", "/chat", "/inspector"):
                assert c.get(base + path, headers=h).status_code == 200, path
                assert inspect_rows(studio) == before, path
            assert c.get("/api/v1/projects/prj_0000000000000000/brand", headers=h).status_code == 404  # NotFound kept
    finally:
        project_kind.unregister("app")


def studio_owner():  # type: ignore[no-untyped-def]
    from studio_core.policy import Actor
    return Actor("human", "owner", "rest")


_DDL = ("create table activity_events (seq integer primary key, prev_seq integer not null default 0, ts datetime not null "
        "default '2026-11-01 00:00:00', run_id text, project_id text, kind text, source text not null default 'server', "
        "audit_id integer, payload text, artifact_sha256 text, redactions integer not null default 0, "
        "dropped integer not null default 0)")
_INS = "insert into activity_events (seq, prev_seq, run_id, project_id, kind, payload) values (?,?,?,?,?,?)"
_INSPECT = '{"tool": "project.inspect"}'


def _mkdb(path: Path, rows: list[tuple]) -> Path:  # type: ignore[type-arg]
    con = sqlite3.connect(path)
    con.execute(_DDL)
    con.executemany(_INS, rows)
    con.commit()
    con.close()
    return path


def _chain(db: Path) -> list[tuple[int, int]]:
    con = sqlite3.connect(db)
    try:
        return [tuple(r) for r in con.execute("select seq, prev_seq from activity_events order by seq")]  # type: ignore[misc]
    finally:
        con.close()


def test_maintenance_removes_only_runless_inspect_rows_with_backup(tmp_path: Path) -> None:
    from aas_build import maintenance
    db = _mkdb(tmp_path / "activity.db", [
        (1, 0, None, "p", "tool.call", _INSPECT),
        (2, 1, None, "p", "tool.call", _INSPECT),
        (3, 2, "run_1", "p", "tool.call", _INSPECT),  # live run: keep
        (4, 3, None, "p", "tool.call", '{"tool": "code.write"}'),  # other tool: keep
        (5, 4, None, "p", "run.started", '{}'),  # other kind: keep
    ])
    res = maintenance.purge_runless_inspect(db)
    assert res["removed"] == 2 and res["remaining"] == 3
    assert Path(res["backup"]).is_file() and Path(res["backup"]) != db
    bk = [r[0] for r in sqlite3.connect(res["backup"]).execute("select seq from activity_events order by seq")]
    assert bk == [1, 2, 3, 4, 5]
    assert _chain(db)[:3] == [(3, 0), (4, 3), (5, 4)]  # tamper-evidence chain re-linked, no gap


def test_maintenance_keeps_preexisting_gaps_marker_and_newest_row_and_writes_an_audit_row(tmp_path: Path) -> None:
    from aas_build import maintenance
    from studio_core.activity import find_gaps
    db = _mkdb(tmp_path / "studio.db", [
        (10, 7, None, "p", "run.started", '{}'),       # oldest: prev 7 = pruned_through retention marker
        (11, 10, None, "p", "tool.call", _INSPECT),   # purge
        (12, 11, None, "p", "tool.call", _INSPECT),   # purge
        (13, 12, None, "p", "run.started", '{}'),     # re-linked to 10
        (15, 14, None, "p", "run.started", '{}'),     # pre-existing gap (14 missing): must survive
        (16, 15, None, "p", "tool.call", _INSPECT),   # purge
        (17, 16, None, "p", "tool.call", _INSPECT),   # newest row: never deleted (seq reuse)
    ])
    gaps_before = find_gaps({"seq": s, "prev": p} for s, p in _chain(db))
    assert gaps_before == [{"after": 13, "before": 15}]
    res = maintenance.purge_runless_inspect(db)
    assert res["removed"] == 3 and res["relinked"] == 2 and res["remaining"] == 4
    chain = _chain(db)
    assert chain[:4] == [(10, 7), (13, 10), (15, 14), (17, 15)]
    assert chain[0][1] == 7  # pruned_through marker unchanged
    assert find_gaps({"seq": s, "prev": p} for s, p in chain) == gaps_before  # gap still reported
    # one audit row appended after the newest row, with the counts and the backup name
    assert chain[4] == (18, 17) and res["auditSeq"] == 18
    con = sqlite3.connect(db)
    kind, run_id, source, payload = con.execute(
        "select kind, run_id, source, payload from activity_events where seq = 18").fetchone()
    con.close()
    import json
    p = json.loads(payload)
    assert (kind, run_id, source, p["tool"], p["status"]) == ("tool.call", None, "server", "maintenance.purge-inspect", "ok")
    assert "removed=3" in p["input"] and "relinked=2" in p["input"] and Path(res["backup"]).name in p["input"]


def test_maintenance_refuses_while_the_activity_lock_is_held(tmp_path: Path) -> None:
    import pytest
    from aas_build import maintenance
    from studio_core.migrations import interprocess_lock
    db = _mkdb(tmp_path / "studio.db", [(1, 0, None, "p", "tool.call", _INSPECT), (2, 1, None, "p", "run.started", '{}')])
    with interprocess_lock(tmp_path / "activity.lock"), pytest.raises(maintenance.MaintenanceRefused, match="stop the studio"):
        maintenance.purge_runless_inspect(db, lock_timeout=0.2)
    assert _chain(db) == [(1, 0), (2, 1)]
    assert not list(tmp_path.glob("studio.pre-purge-*"))  # nothing touched, not even a backup


def test_maintenance_refuses_while_the_database_is_write_locked(tmp_path: Path) -> None:
    import pytest
    from aas_build import maintenance
    db = _mkdb(tmp_path / "studio.db", [(1, 0, None, "p", "tool.call", _INSPECT), (2, 1, None, "p", "run.started", '{}')])
    other = sqlite3.connect(db, isolation_level=None)
    other.execute("BEGIN IMMEDIATE")
    try:
        with pytest.raises(maintenance.MaintenanceRefused, match="locked"):
            maintenance.purge_runless_inspect(db, lock_timeout=0.2)
    finally:
        other.execute("ROLLBACK")
        other.close()
    assert _chain(db) == [(1, 0), (2, 1)]


# ---------------------------------------------------------------- #3 capture lock waits
def test_capture_waits_for_the_lock_then_answers_409_in_romanian(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    import threading
    import time

    from aas_build import capture
    from studio_core.errors import ConflictError

    from .test_s7_review_fixes import _built, _shot
    pid = _built(studio)
    capture._CAP_LOCK.acquire()  # noqa: SLF001
    try:
        t0 = time.monotonic()
        with __import__("pytest").raises(ConflictError, match="Captura e ocupată, reîncearcă") as ei:
            _shot(studio, pid, "a", lock_wait=0.3)
        assert time.monotonic() - t0 >= 0.25 and ei.value.http_status == 409
    finally:
        capture._CAP_LOCK.release()  # noqa: SLF001
    capture._CAP_LOCK.acquire()  # noqa: SLF001
    threading.Timer(0.3, capture._CAP_LOCK.release).start()  # noqa: SLF001
    assert _shot(studio, pid, "b", lock_wait=5)  # the lock freed within the wait: the capture simply ran
