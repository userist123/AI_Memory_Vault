"""S8: live emulator mirror relay (DESIGN 2.3 video chunks -> backend hub -> owner WS /api/v1/device/video).

The device session lives in the run's MCP child; it sends h264 Annex-B chunks over an authenticated local pipe
(multiprocessing.connection, HMAC challenge with a per-run key) to the backend hub, which re-cuts them at NAL
boundaries, caps size and rate, keeps SPS/PPS + the current GOP for late viewers and fans them out, per project, to
at most MAX_SUBSCRIBERS owner sockets. Video is dropped first: a slow viewer loses whole NAL units and resyncs at the
next IDR, never a partial NAL.
"""
from __future__ import annotations

import json
import queue
import time
from collections.abc import Iterator
from multiprocessing.connection import Client
from multiprocessing import AuthenticationError
from pathlib import Path
from typing import Any

import pytest
from aas_device import video_relay as vr
from apps.backend import main as backend
from starlette.testclient import TestClient
from starlette.websockets import WebSocketDisconnect
from studio_core import project_kind
from studio_core.config import Settings

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}
SC = b"\x00\x00\x00\x01"
SPS = SC + bytes([0x67, 0x42, 0x00, 0x1F, 0xAB, 0xCD])
PPS = SC + bytes([0x68, 0xCE, 0x3C, 0x80])


def idr(tag: int, n: int = 40) -> bytes:
    return SC + bytes([0x65, 0x88]) + bytes([tag]) * n


def pfr(tag: int, n: int = 20) -> bytes:
    return SC + bytes([0x41, 0x9A]) + bytes([tag]) * n


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    app = backend.create_app(Settings(data_dir=tmp_path / "data"), TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c
    project_kind.unregister("app")


def project(c: TestClient, name: str = "Mirror") -> str:
    r = c.post("/api/v1/projects", json={"name": name, "template": "blank"}, headers=AUTH)
    assert r.status_code == 201, r.text
    return str(r.json()["id"])


def video_ws(c: TestClient, pid: str, token: str | None = TOKEN, protos: list[str] | None = None,
             headers: dict[str, str] | None = None):  # type: ignore[no-untyped-def]
    if protos is None:
        protos = ["aas.v1", f"aas.token.{token}"] if token else ["aas.v1"]
    return c.websocket_connect(f"ws://127.0.0.1:17875/api/v1/device/video?projectId={pid}", subprotocols=protos,
                               headers=headers or {})


def sender(env: dict[str, str]):  # type: ignore[no-untyped-def]
    return Client(env["AAS_VIDEO_PIPE"], authkey=bytes.fromhex(env["AAS_VIDEO_KEY"]))


def recv_until(ws: Any, needle: bytes, limit: int = 50) -> bytes:
    got = b""
    for _ in range(limit):
        got += ws.receive_bytes()
        if needle in got:
            return got
    raise AssertionError(f"{needle!r} not received; got {len(got)} bytes")


# ---------------------------------------------------------------- auth (owner only, same boundary + subprotocols)
def test_video_ws_auth_and_fixed_subprotocol(client: TestClient) -> None:
    pid = project(client)
    for kwargs, code in (({"token": None}, 4401), ({"token": "w" * 40}, 4401),
                         ({"headers": {"Origin": "http://evil.example"}}, 4403),
                         ({"headers": {"Host": "evil.com"}}, 4421),
                         ({"protos": [f"aas.token.{TOKEN}"]}, 4400)):
        with pytest.raises(WebSocketDisconnect) as ei:
            with video_ws(client, pid, **kwargs) as ws:  # type: ignore[arg-type]
                ws.receive_bytes()
        assert ei.value.code == code, kwargs
    with video_ws(client, pid) as ws:  # positive control
        assert ws.accepted_subprotocol == "aas.v1"
        assert TOKEN not in repr(ws.extra_headers)


@pytest.mark.parametrize("pid", ["", "prj_xyz", "../etc", "prj_ffffffffffffffff"])
def test_video_ws_needs_an_existing_project(client: TestClient, pid: str) -> None:
    with pytest.raises(WebSocketDisconnect) as ei:
        with video_ws(client, pid) as ws:
            ws.receive_bytes()
    assert ei.value.code in (4404, 4422)


# ---------------------------------------------------------------- relay, per project
def test_chunks_from_the_run_pipe_reach_the_owner_socket_of_that_project_only(client: TestClient) -> None:
    hub: vr.VideoHub = client.app.state.video_hub  # type: ignore[attr-defined]
    pa, pb = project(client, "A"), project(client, "B")
    env = hub.open_run("run_aaaaaaaaaaaaaaaa", pa)
    assert set(env) == {"AAS_VIDEO_PIPE", "AAS_VIDEO_KEY"} and len(env["AAS_VIDEO_KEY"]) == 64
    try:
        with video_ws(client, pa) as wa, video_ws(client, pb) as wb:
            conn = sender(env)
            stream = SPS + PPS + idr(1) + pfr(2) + pfr(3) + idr(4)
            for i in range(0, len(stream), 7):  # arbitrary cuts: NALs straddle chunks
                conn.send_bytes(stream[i:i + 7])
            got = recv_until(wa, pfr(3))
            assert got.startswith(SPS + PPS + idr(1)) and pfr(2) in got
            assert hub.stats("run_aaaaaaaaaaaaaaaa")["received"] > 0
            _assert_nothing(wb)  # project B sees nothing of project A
            conn.close()
    finally:
        hub.close_run("run_aaaaaaaaaaaaaaaa")


def _assert_nothing(ws: Any) -> None:
    """starlette's TestClient has no timeout on receive; peek its app->client queue instead."""
    time.sleep(0.4)
    import anyio
    with pytest.raises(anyio.WouldBlock):
        ws._send_rx.receive_nowait()  # noqa: SLF001


def test_wrong_key_is_refused_and_relays_nothing(client: TestClient) -> None:
    hub: vr.VideoHub = client.app.state.video_hub  # type: ignore[attr-defined]
    pid = project(client)
    env = hub.open_run("run_bbbbbbbbbbbbbbbb", pid)
    try:
        with pytest.raises((AuthenticationError, EOFError, OSError)):
            c = Client(env["AAS_VIDEO_PIPE"], authkey=b"x" * 32)
            c.send_bytes(SPS + PPS + idr(9))
            c.recv_bytes()
        time.sleep(0.3)
        st = hub.stats("run_bbbbbbbbbbbbbbbb")
        assert st["received"] == 0 and st["authFailures"] >= 1
        conn = sender(env)  # positive control: the right key still works afterwards
        conn.send_bytes(SPS + PPS + idr(1) + pfr(2))
        deadline = time.monotonic() + 5
        while hub.stats("run_bbbbbbbbbbbbbbbb")["received"] == 0 and time.monotonic() < deadline:
            time.sleep(0.05)
        assert hub.stats("run_bbbbbbbbbbbbbbbb")["received"] > 0
        conn.close()
    finally:
        hub.close_run("run_bbbbbbbbbbbbbbbb")


def test_closed_run_pipe_refuses_new_senders(client: TestClient) -> None:
    hub: vr.VideoHub = client.app.state.video_hub  # type: ignore[attr-defined]
    env = hub.open_run("run_cccccccccccccccc", project(client))
    hub.close_run("run_cccccccccccccccc")
    with pytest.raises((OSError, EOFError, AuthenticationError)):
        c = sender(env)
        for _ in range(5):
            c.send_bytes(SPS)
            time.sleep(0.1)
        c.recv_bytes()


# ---------------------------------------------------------------- caps (unit level, no pipe)
def _sub(hub: vr.VideoHub, pid: str) -> vr.Subscription:
    return hub.subscribe(pid, wake=lambda: None)


def test_oversize_chunk_drops_the_sender() -> None:
    hub = vr.VideoHub()
    env = hub.open_run("run_dddddddddddddddd", "prj_0000000000000001")
    try:
        conn = sender(env)
        with pytest.raises((EOFError, OSError)):  # the hub ends the pipe (the oversize write itself may fail)
            conn.send_bytes(b"\x00" * (vr.MAX_CHUNK + 1))
            for _ in range(20):
                conn.send_bytes(SPS)
                time.sleep(0.1)
            conn.recv_bytes()
        assert hub.stats("run_dddddddddddddddd")["oversize"] == 1
        assert hub.stats("run_dddddddddddddddd")["received"] == 0
    finally:
        hub.close()


def test_chunk_above_max_chunk_is_dropped_by_feed() -> None:
    hub = vr.VideoHub()
    pid = "prj_0000000000000009"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1, vr.MAX_CHUNK) + SC)
    assert sub.take() == [] and hub.stats_project(pid)["rateDropped"] == 1


def test_rate_cap_drops_video_and_resyncs_at_the_next_idr() -> None:
    hub = vr.VideoHub(max_bps=1000, burst=1000)
    pid = "prj_0000000000000002"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1) + pfr(2))
    hub.feed(pid, pfr(3, 5000))  # over the bucket: dropped, the stream is discontinuous
    time.sleep(1.2)
    hub.feed(pid, pfr(4) + idr(5) + pfr(6) + SC)
    out = b"".join(sub.take())
    assert hub.stats_project(pid)["rateDropped"] == 1
    assert pfr(3, 5000) not in out and pfr(4) not in out  # nothing between the drop and the next IDR
    assert out.endswith(SPS + PPS + idr(5) + pfr(6)) or (SPS + PPS + idr(5) + pfr(6)) in out


def test_subscriber_limit_per_project() -> None:
    hub = vr.VideoHub(max_subscribers=2)
    a, b = _sub(hub, "prj_0000000000000003"), _sub(hub, "prj_0000000000000003")
    with pytest.raises(vr.Busy):
        _sub(hub, "prj_0000000000000003")
    _sub(hub, "prj_0000000000000004")  # other project: own limit
    a.close()
    _sub(hub, "prj_0000000000000003")  # a slot freed
    b.close()


def test_late_viewer_gets_sps_pps_and_the_current_gop() -> None:
    hub = vr.VideoHub()
    pid = "prj_0000000000000005"
    hub.feed(pid, SPS + PPS + idr(1) + pfr(2) + idr(3) + pfr(4) + SC)
    late = _sub(hub, pid)
    out = b"".join(late.take())
    assert out == SPS + PPS + idr(3) + pfr(4)


def test_slow_viewer_loses_whole_nals_and_resyncs_at_idr() -> None:
    hub = vr.VideoHub(sub_queue_bytes=200)
    pid = "prj_0000000000000006"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1) + b"".join(pfr(10 + i) for i in range(20)))  # never taken: overflow
    hub.feed(pid, idr(7) + pfr(8) + SC)
    out = b"".join(sub.take())
    assert sub.dropped > 0
    assert out == SPS + PPS + idr(7) + pfr(8)


def test_frames_are_at_most_max_frame_bytes() -> None:
    hub = vr.VideoHub()
    pid = "prj_0000000000000007"
    sub = _sub(hub, pid)
    big = idr(1, 3 * vr.MAX_FRAME)
    stream = SPS + PPS + big + pfr(2) + SC
    for i in range(0, len(stream), vr.MAX_CHUNK):  # the pipe never carries more than MAX_CHUNK per chunk
        hub.feed(pid, stream[i:i + vr.MAX_CHUNK])
    frames = sub.take()
    assert frames and all(len(f) <= vr.MAX_FRAME for f in frames)
    assert b"".join(frames) == SPS + PPS + big + pfr(2)


def test_oversized_nal_resets_the_splitter() -> None:
    hub = vr.VideoHub()
    pid = "prj_0000000000000008"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1))
    for _ in range(vr.MAX_NAL // 60000 + 2):
        hub.feed(pid, b"\x01" * 60000)  # no start code for more than MAX_NAL bytes
    hub.feed(pid, SPS + PPS + idr(2) + pfr(3) + SC)
    out = b"".join(sub.take())
    assert b"\x01" * 1000 not in out and out.endswith(SPS + PPS + idr(2) + pfr(3))


# ---------------------------------------------------------------- MCP side: the device service sends the run's video
class FakeVideoSession:
    def __init__(self, form: str) -> None:
        self.video_q: queue.Queue[dict[str, Any]] = queue.Queue()
        self.video_on = False

    def start(self) -> dict[str, Any]:
        return {"ch": "e", "kind": "booted"}

    def video_start(self, size: str = "540x1170") -> dict[str, Any]:
        import base64
        self.video_on = True
        for part in (SPS + PPS, idr(1), pfr(2) + SC):
            self.video_q.put({"ch": "v", "seq": 0, "b64": base64.b64encode(part).decode()})
        return {"ok": True}

    def video_stop(self) -> dict[str, Any]:
        self.video_on = False
        return {"ok": True}

    def shutdown(self) -> int:
        return 0


def test_device_service_streams_the_session_video_to_the_run_pipe(tmp_path: Path, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    from aas_device import service as dsvc
    from studio_core.app_kind import register_app_kind
    from studio_core.services import Studio
    from studio_core.policy import Actor
    register_app_kind()
    try:
        studio = Studio(Settings(data_dir=tmp_path / "data"), kind="app")
        owner = Actor("human", "owner", "rest")
        dsvc.set_enabled(studio, owner, True)
        pid = studio.create_project(owner, "p", "blank", "")["id"]
        hub = vr.VideoHub()
        env = hub.open_run("run_eeeeeeeeeeeeeeee", pid)
        for k, v in env.items():
            monkeypatch.setenv(k, v)
        sub = _sub(hub, pid)
        d = dsvc.DeviceService(studio, preflight=lambda: {}, free_bytes=lambda: 20 * 1024 ** 3,
                               session_factory=FakeVideoSession)
        d.start("phone")
        deadline = time.monotonic() + 5
        out = b""
        while pfr(2) not in out and time.monotonic() < deadline:
            out += b"".join(sub.take())
            time.sleep(0.05)
        assert out == SPS + PPS + idr(1) + pfr(2)
        d.stop()
        hub.close()
    finally:
        project_kind.unregister("app")


def test_device_service_without_relay_env_starts_no_video(tmp_path: Path, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    from aas_device import service as dsvc
    from studio_core.app_kind import register_app_kind
    from studio_core.services import Studio
    from studio_core.policy import Actor
    monkeypatch.delenv("AAS_VIDEO_PIPE", raising=False)
    monkeypatch.delenv("AAS_VIDEO_KEY", raising=False)
    register_app_kind()
    try:
        studio = Studio(Settings(data_dir=tmp_path / "data"), kind="app")
        dsvc.set_enabled(studio, Actor("human", "owner", "rest"), True)
        d = dsvc.DeviceService(studio, preflight=lambda: {}, free_bytes=lambda: 20 * 1024 ** 3,
                               session_factory=FakeVideoSession)
        d.start("phone")
        assert d.session.video_on is False
        d.stop()
    finally:
        project_kind.unregister("app")


# ---------------------------------------------------------------- runner: per-run pipe, key only in the MCP child env
class StubHub:
    def __init__(self) -> None:
        self.opened: list[tuple[str, str]] = []
        self.closed: list[str] = []
        self.cfg_env: dict[str, str] = {}
        self.root: Path | None = None

    def open_run(self, run_id: str, project_id: str) -> dict[str, str]:
        self.opened.append((run_id, project_id))
        return {"AAS_VIDEO_PIPE": r"\\.\pipe\aas-video-test", "AAS_VIDEO_KEY": "ab" * 32}

    def close_run(self, run_id: str) -> None:
        assert self.root is not None
        cfg = json.loads((self.root / f"{run_id}.mcp.json").read_text(encoding="utf-8"))
        self.cfg_env = next(iter(cfg["mcpServers"].values()))["env"]
        self.closed.append(run_id)


def test_runner_opens_a_video_pipe_per_run_and_closes_it(tmp_path: Path) -> None:
    from aas_runner import claude as rc
    from studio_core.app_kind import register_app_kind
    from studio_core.services import Studio
    from studio_core.policy import Actor
    import sys
    register_app_kind()
    try:
        studio = Studio(Settings(data_dir=tmp_path / "data"), kind="app")
        pid = studio.create_project(Actor("human", "owner", "rest"), "p", "blank", "")["id"]
        hub = StubHub()
        r = rc.Runner(studio, cli=[sys.executable, str(Path(__file__).resolve().parents[1] / "fixtures" /
                                                        "fake_claude.py")], video_hub=hub)
        hub.root = r.root
        rec = tmp_path / "rec.json"
        run = r.start(pid, json.dumps({"record": str(rec)}))
        r.wait(run, 30)
        assert hub.opened == [(run, pid)] and hub.closed == [run]
        assert hub.cfg_env["AAS_VIDEO_KEY"] == "ab" * 32  # in the MCP child's env ...
        assert "AAS_VIDEO_KEY" not in json.loads(rec.read_text(encoding="utf-8"))["env"]  # ... never the CLI's
    finally:
        project_kind.unregister("app")
