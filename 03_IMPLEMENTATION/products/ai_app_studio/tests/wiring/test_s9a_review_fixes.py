"""S9a review fixes: (1) trash purge by the deletion time in the folder name, not the folder's mtime; (2) project delete
removes the project's capture PNGs that no other project references; (3) named pipe: handle ownership moves to the
connection, a full pipe (ERROR_PIPE_BUSY) does not kill the listener; (5) delete is refused while a build of the
project runs (also an external-MCP build)."""
from __future__ import annotations

import datetime as dt
import json
import os
import secrets
import sys
import threading
import time
from collections.abc import Iterator
from pathlib import Path

import pytest
from aas_build import capture, service
from starlette.testclient import TestClient
from studio_core import project_kind
from studio_core.config import Settings

from .test_build_service import DOCKER, IMAGE, make_fake

AUTH = {"Authorization": "Bearer " + "t" * 40}


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    from apps.backend.main import create_app
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        yield c
    project_kind.unregister("app")


# ---------------------------------------------------------------- 1 trash purge uses the deletion time
def test_purge_uses_the_deletion_stamp_not_the_folder_mtime(tmp_path: Path) -> None:
    from apps.backend.main import purge_trash
    s = Settings(data_dir=tmp_path / "d")
    trash = s.data_dir / "trash"
    now = dt.datetime.now(dt.UTC)
    fresh = trash / f"prj_{'a' * 16}-{now.strftime('%Y%m%dT%H%M%S')}"  # deleted today ...
    fresh.mkdir(parents=True)
    old_t = time.time() - 60 * 86400
    os.utime(fresh, (old_t, old_t))  # ... but shutil.move kept the project's 60-day-old mtime
    gone = trash / f"prj_{'b' * 16}-{(now - dt.timedelta(days=40)).strftime('%Y%m%dT%H%M%S')}"
    gone.mkdir()
    bad = trash / f"prj_{'c' * 16}-99999999T999999"  # unparsable stamp: kept (never guess)
    bad.mkdir()
    assert purge_trash(s, 30) == 1
    assert fresh.is_dir() and bad.is_dir() and not gone.exists()


# ---------------------------------------------------------------- 2 delete removes unreferenced capture PNGs
def test_delete_removes_the_projects_capture_pngs_kept_when_shared(client: TestClient) -> None:
    st = client.app.state.studio
    a = client.post("/api/v1/projects", json={"name": "a"}, headers=AUTH).json()["id"]
    b = client.post("/api/v1/projects", json={"name": "b"}, headers=AUTH).json()["id"]
    d = capture.captures_dir(st.settings)
    (d / ".index").mkdir(parents=True)
    own, shared = "1" * 64, "2" * 64
    for sha in (own, shared):
        (d / f"{sha}.png").write_bytes(b"\x89PNG\r\n\x1a\n")
    (d / ".index" / f"{a}.json").write_text(json.dumps([{"sha": own, "bytes": 8}, {"sha": shared, "bytes": 8}]))
    (d / ".index" / f"{b}.json").write_text(json.dumps([{"sha": shared, "bytes": 8}]))
    apr = client.request("DELETE", f"/api/v1/projects/{a}", headers=AUTH).json()["details"]["approvalId"]
    client.post(f"/api/v1/approvals/{apr}/decide", json={"decision": "approve"}, headers=AUTH)
    assert client.request("DELETE", f"/api/v1/projects/{a}", headers=AUTH, json={"approvalId": apr}).status_code == 200
    assert not (d / f"{own}.png").exists()
    assert (d / f"{shared}.png").exists()  # still referenced by project b
    assert not (d / ".index" / f"{a}.json").exists()


# ---------------------------------------------------------------- 5 delete refused during a build of the project
def test_delete_refused_while_a_build_of_the_project_runs(client: TestClient) -> None:
    st = client.app.state.studio
    pid = client.post("/api/v1/projects", json={"name": "p"}, headers=AUTH).json()["id"]
    hold = threading.Event()
    t = threading.Thread(target=lambda: service.build_web(st, pid, run_build=make_fake(hold=hold), docker=DOCKER,
                                                          image=IMAGE))
    t.start()
    try:
        for _ in range(100):
            if service.building_project() == pid:
                break
            time.sleep(0.02)
        assert service.building_project() == pid
        r = client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH)
        assert r.status_code == 409, r.text
    finally:
        hold.set()
        t.join()
    assert service.building_project() is None
    assert client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH).status_code == 428


# ---------------------------------------------------------------- 3 named pipe ownership + busy
@pytest.mark.skipif(sys.platform != "win32", reason="Windows named pipes")
def test_full_pipe_keeps_the_listener_alive_and_never_double_closes() -> None:
    from aas_device import video_relay as vr

    from .test_s9_named_pipe import _k32
    addr = r"\\.\pipe\aas-test-" + secrets.token_hex(8)
    lst = vr.NativePipeListener(addr)
    clients: list[int] = []
    conns = []

    def connect() -> int:
        h = _k32.CreateFileW(addr, 0xC0000000, 0, None, 3, 0, None)
        assert h not in (None, -1, 0xFFFFFFFFFFFFFFFF), "client could not open the pipe"
        clients.append(h)
        return h
    try:
        for _ in range(vr.PIPE_INSTANCES):  # stalled clients that never handshake: every instance is taken
            connect()
            c = None
            for _ in range(20):
                c = lst.accept(0.1)
                if c is not None:
                    break
            assert c is not None
            conns.append(c)
        assert lst.accept(0.1) is None  # pipe full: no exception, the listener survives
        conns.pop(0).close()  # one instance is free again
        got = None
        for _ in range(30):
            got = lst.accept(0.1)
            if got is not None:
                break
            if lst.handle is not None and len(clients) == vr.PIPE_INSTANCES:
                connect()
        assert got is not None
        conns.append(got)
        for c in conns:  # the connections still own valid handles (no double close by the listener)
            c.close()
    finally:
        lst.close()
        for h in clients:
            _k32.CloseHandle(h)
