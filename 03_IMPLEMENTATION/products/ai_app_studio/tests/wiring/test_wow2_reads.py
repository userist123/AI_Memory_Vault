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


def test_maintenance_removes_only_runless_inspect_rows_with_backup(tmp_path: Path) -> None:
    from aas_build import maintenance
    db = tmp_path / "activity.db"
    con = sqlite3.connect(db)
    con.execute("create table activity_events (seq integer primary key, prev_seq integer, run_id text, project_id text, kind text, payload text)")
    rows = [
        (1, 0, None, "p", "tool.call", '{"tool": "project.inspect"}'),
        (2, 1, None, "p", "tool.call", '{"tool": "project.inspect"}'),
        (3, 2, "run_1", "p", "tool.call", '{"tool": "project.inspect"}'),  # live run: keep
        (4, 3, None, "p", "tool.call", '{"tool": "code.write"}'),  # other tool: keep
        (5, 4, None, "p", "run.started", '{}'),  # other kind: keep
    ]
    con.executemany("insert into activity_events values (?,?,?,?,?,?)", rows)
    con.commit()
    con.close()
    res = maintenance.purge_runless_inspect(db)
    assert res["removed"] == 2 and res["remaining"] == 3
    assert Path(res["backup"]).is_file() and Path(res["backup"]) != db
    left = [r[0] for r in sqlite3.connect(db).execute("select seq from activity_events order by seq")]
    assert left == [3, 4, 5]
    bk = [r[0] for r in sqlite3.connect(res["backup"]).execute("select seq from activity_events order by seq")]
    assert bk == [1, 2, 3, 4, 5]
    chain = [tuple(r) for r in sqlite3.connect(db).execute("select seq, prev_seq from activity_events order by seq")]
    assert chain == [(3, 0), (4, 3), (5, 4)]  # tamper-evidence chain re-linked, no gap


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
