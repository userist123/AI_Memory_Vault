"""S8 review fixes: mirror recovery (M1), pipe handshake timeout (L1), late-viewer budget (L2), stream end (L3).

M1 (a) every SPS/PPS reaches synced viewers; (b) pipe chunks carry a seq: a gap is a discontinuity (drop until the next
IDR, ask the sender for a fresh key frame), the launcher marks the chunk after a queue drop; WS frames carry a seq and
a server-side drop shows as a seq gap to the client. L1: a client that connects and never answers the HMAC challenge
cannot block the accept loop; no thread outlives close_run. L2: the late-viewer prime is capped and not counted against
the live queue budget. L3: run end clears the project's cached stream (the WS stays open, idle until the next run).
"""
from __future__ import annotations

import base64
import queue
import struct
import threading
import time
from multiprocessing.connection import Client
from typing import Any

import pytest
from aas_device import video_relay as vr

SC = b"\x00\x00\x00\x01"
SPS = SC + bytes([0x67, 0x42, 0x00, 0x1F, 0xAB, 0xCD])
PPS = SC + bytes([0x68, 0xCE, 0x3C, 0x80])
SPS2 = SC + bytes([0x67, 0x42, 0x00, 0x28, 0x11, 0x22])
PPS2 = SC + bytes([0x68, 0xCE, 0x3C, 0x81])


def idr(tag: int, n: int = 40) -> bytes:
    return SC + bytes([0x65, 0x88]) + bytes([tag]) * n


def pfr(tag: int, n: int = 20) -> bytes:
    return SC + bytes([0x41, 0x9A]) + bytes([tag]) * n


def _sub(hub: vr.VideoHub, pid: str) -> vr.Subscription:
    return hub.subscribe(pid, wake=lambda: None)


def _wait(cond: Any, timeout: float = 5.0) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if cond():
            return True
        time.sleep(0.05)
    return bool(cond())


# ---------------------------------------------------------------- M1 (a): every SPS/PPS reaches synced viewers
def test_a_second_sps_pps_reaches_a_synced_viewer() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000a1"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1) + pfr(2))
    hub.feed(pid, SPS2 + PPS2 + idr(3) + SC)  # screenrecord restarted with new parameters
    out = b"".join(sub.take())
    assert out == SPS + PPS + idr(1) + pfr(2) + SPS2 + PPS2 + idr(3)


# ---------------------------------------------------------------- M1 (b): seq gaps
def test_seq_gap_at_the_hub_drops_until_the_next_idr_and_requests_a_key_frame() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000a2"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1) + SC, seq=1)
    hub.feed(pid, pfr(2)[4:] + SC, seq=2)
    assert hub.take_key_request(pid) is False
    hub.feed(pid, pfr(3)[4:] + pfr(4) + SC, seq=4)  # seq 3 lost: the rest of this GOP is undecodable
    assert hub.stats_project(pid)["seqGaps"] == 1
    assert hub.take_key_request(pid) is True
    assert hub.take_key_request(pid) is False  # throttled
    hub.feed(pid, idr(5)[4:] + pfr(6) + SC, seq=5)
    out = b"".join(sub.take())
    assert pfr(3) not in out and pfr(4) not in out
    assert out.startswith(SPS + PPS + idr(1)) and out.endswith(SPS + PPS + idr(5) + pfr(6))


def test_contiguous_seq_is_not_a_gap() -> None:  # positive control
    hub = vr.VideoHub()
    pid = "prj_00000000000000a3"
    sub = _sub(hub, pid)
    for i, part in enumerate((SPS + PPS, idr(1), pfr(2), pfr(3) + SC), start=7):
        hub.feed(pid, part, seq=i)
    assert hub.stats_project(pid)["seqGaps"] == 0
    assert b"".join(sub.take()) == SPS + PPS + idr(1) + pfr(2) + pfr(3)


def _frame(seq: int, data: bytes) -> bytes:
    return struct.pack(">Q", seq) + data


def test_pipe_seq_gap_sends_a_key_frame_request_back_to_the_sender() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000a4"
    env = hub.open_run("run_a4a4a4a4a4a4a4a4", pid)
    try:
        conn = Client(env[vr.ENV_PIPE], authkey=bytes.fromhex(env[vr.ENV_KEY]))
        conn.send_bytes(_frame(1, SPS + PPS + idr(1)))
        conn.send_bytes(_frame(3, pfr(2)))
        assert conn.poll(5), "no key frame request"
        assert conn.recv_bytes(16) == vr.KEY_REQUEST
        assert hub.stats_project(pid)["seqGaps"] == 1
        conn.close()
    finally:
        hub.close()


def test_pipe_message_shorter_than_the_seq_header_drops_the_sender() -> None:
    hub = vr.VideoHub()
    env = hub.open_run("run_a5a5a5a5a5a5a5a5", "prj_00000000000000a5")
    try:
        conn = Client(env[vr.ENV_PIPE], authkey=bytes.fromhex(env[vr.ENV_KEY]))
        with pytest.raises((EOFError, OSError)):
            conn.send_bytes(b"\x01\x02")
            for _ in range(20):
                conn.send_bytes(_frame(1, SPS))
                time.sleep(0.1)
            conn.recv_bytes()
        assert hub.stats("run_a5a5a5a5a5a5a5a5")["malformed"] == 1
    finally:
        hub.close()


class GapSession:
    """Host chunks with a lost one (seq 3) and a launcher drop mark; answers key frame requests."""

    def __init__(self) -> None:
        self.video_q: queue.Queue[dict[str, Any]] = queue.Queue()
        self.keyframes = 0

    def video_start(self, size: str = "540x1170") -> dict[str, Any]:
        parts = [(1, SPS + PPS + idr(1)), (2, pfr(2)), (4, pfr(3)), (5, idr(5) + pfr(6) + SC)]
        for seq, part in parts:
            self.video_q.put({"ch": "v", "seq": seq, "b64": base64.b64encode(part).decode()})
        return {"ok": True}

    def video_keyframe(self) -> dict[str, Any]:
        self.keyframes += 1
        return {"ok": True}


def test_sender_turns_a_host_seq_gap_into_a_pipe_gap_and_forwards_the_key_frame_request() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000a6"
    env = hub.open_run("run_a6a6a6a6a6a6a6a6", pid)
    sub = _sub(hub, pid)
    sess = GapSession()
    s = vr.VideoSender(sess, env[vr.ENV_PIPE], env[vr.ENV_KEY]).start()
    try:
        out = b""

        def got() -> bool:
            nonlocal out
            out += b"".join(sub.take())
            return pfr(6) in out
        assert _wait(got)
        assert hub.stats_project(pid)["seqGaps"] == 1
        assert pfr(3) not in out and out.endswith(SPS + PPS + idr(5) + pfr(6))
        assert _wait(lambda: sess.keyframes == 1)
    finally:
        s.stop()
        hub.close()


def test_sender_marks_a_launcher_drop_as_a_gap() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000a7"
    env = hub.open_run("run_a7a7a7a7a7a7a7a7", pid)

    class S(GapSession):
        def video_start(self, size: str = "540x1170") -> dict[str, Any]:  # no host seq (older host), gap flag only
            for part, gap in ((SPS + PPS + idr(1), False), (pfr(2), False), (pfr(3), True), (idr(5) + SC, False)):
                o: dict[str, Any] = {"ch": "v", "b64": base64.b64encode(part).decode()}
                if gap:
                    o["gap"] = True
                self.video_q.put(o)
            return {"ok": True}
    s = vr.VideoSender(S(), env[vr.ENV_PIPE], env[vr.ENV_KEY]).start()
    try:
        assert _wait(lambda: hub.stats_project(pid).get("nals", 0) >= 4)
        assert hub.stats_project(pid)["seqGaps"] == 1
    finally:
        s.stop()
        hub.close()


def test_launcher_drop_of_queued_video_drops_whole_chunks_and_marks_the_gap() -> None:
    from apps.device_host.launcher import DeviceSession
    s = DeviceSession("phone", video_queue_max=2)
    for i in (1, 2, 3):
        s._default_on_video({"ch": "v", "seq": i, "b64": "AA=="})  # noqa: SLF001
    first, second = s.video_q.get_nowait(), s.video_q.get_nowait()
    assert (first["seq"], second["seq"]) == (2, 3) and s.video_dropped == 1
    assert first.get("gap") is True and not second.get("gap")


# ---------------------------------------------------------------- M1: WS frames carry a seq, a drop shows as a gap
def test_ws_frames_carry_a_seq_and_a_server_drop_is_a_seq_gap() -> None:
    hub = vr.VideoHub(sub_queue_bytes=200)
    pid = "prj_00000000000000a8"
    sub = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1) + pfr(2) + SC)
    f1 = sub.take_frames()
    seqs = [struct.unpack(">I", f[:4])[0] for f in f1]
    assert seqs == list(range(1, len(f1) + 1))
    assert b"".join(f[4:] for f in f1) == SPS + PPS + idr(1) + pfr(2)
    assert all(len(f) <= vr.MAX_FRAME for f in f1)
    hub.feed(pid, b"".join(pfr(10 + i) for i in range(20)))  # never taken: overflow, whole NALs dropped
    hub.feed(pid, idr(7) + SC)
    f2 = sub.take_frames()
    assert struct.unpack(">I", f2[0][:4])[0] > seqs[-1] + 1  # the client sees the gap
    assert b"".join(f[4:] for f in f2) == SPS + PPS + idr(7)


# ---------------------------------------------------------------- L1: handshake timeout, accept loop never blocked
def _video_threads() -> list[threading.Thread]:
    return [t for t in threading.enumerate() if t.name.startswith("aas-video") and t.is_alive()]


def test_a_staller_cannot_block_the_accept_loop_and_is_dropped_after_the_timeout() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000b1"
    before = len(_video_threads())
    env = hub.open_run("run_b1b1b1b1b1b1b1b1", pid)
    sub = _sub(hub, pid)
    staller = Client(env[vr.ENV_PIPE])  # connects, never answers the challenge
    ok: list[bool] = []

    def good() -> None:
        c = Client(env[vr.ENV_PIPE], authkey=bytes.fromhex(env[vr.ENV_KEY]))
        c.send_bytes(_frame(1, SPS + PPS + idr(1) + pfr(2) + SC))
        ok.append(True)
        time.sleep(0.5)
        c.close()
    t = threading.Thread(target=good, daemon=True)
    t0 = time.monotonic()
    t.start()
    t.join(timeout=vr.AUTH_TIMEOUT + 1)
    try:
        assert ok == [True], "the honest sender was blocked by the staller"
        assert _wait(lambda: pfr(2) in b"".join(sub.take()) or hub.stats_project(pid).get("nals", 0) >= 4)
        assert time.monotonic() - t0 < vr.AUTH_TIMEOUT + 1
        with pytest.raises((EOFError, OSError)):  # the staller is cut after AUTH_TIMEOUT
            deadline = time.monotonic() + vr.AUTH_TIMEOUT + 3
            while time.monotonic() < deadline:
                staller.recv_bytes(256)  # the challenge, then EOF
            raise AssertionError("staller still connected")
        assert _wait(lambda: hub.stats("run_b1b1b1b1b1b1b1b1")["authTimeouts"] == 1)
    finally:
        hub.close_run("run_b1b1b1b1b1b1b1b1")
        staller.close()
    assert _wait(lambda: len(_video_threads()) <= before, vr.AUTH_TIMEOUT + 2), [t.name for t in _video_threads()]


def test_close_run_with_a_pending_staller_leaks_no_thread() -> None:
    hub = vr.VideoHub()
    before = len(_video_threads())
    env = hub.open_run("run_b2b2b2b2b2b2b2b2", "prj_00000000000000b2")
    staller = Client(env[vr.ENV_PIPE])
    time.sleep(0.2)
    hub.close_run("run_b2b2b2b2b2b2b2b2")
    try:
        assert _wait(lambda: len(_video_threads()) <= before, vr.AUTH_TIMEOUT + 2), [t.name for t in _video_threads()]
    finally:
        staller.close()


# ---------------------------------------------------------------- L2: late viewer prime vs live budget
def test_late_viewer_prime_is_not_counted_against_the_live_budget() -> None:
    hub = vr.VideoHub(sub_queue_bytes=300)
    pid = "prj_00000000000000c1"
    hub.feed(pid, SPS + PPS + idr(1, 1000) + pfr(2) + SC)  # GOP ~1.1 kB > the 300 B live queue
    late = _sub(hub, pid)
    hub.feed(pid, pfr(3) + SC)  # live NAL right after the prime, before the socket drained the queue
    out = b"".join(late.take())
    assert late.dropped == 0
    assert out == SPS + PPS + idr(1, 1000) + pfr(2) + pfr(3)


def test_late_viewer_prime_is_capped_and_a_bigger_gop_waits_for_the_next_idr() -> None:
    hub = vr.VideoHub(prime_max=1000)
    pid = "prj_00000000000000c2"
    hub.feed(pid, SPS + PPS + idr(1, 2000) + pfr(2) + SC)
    late = _sub(hub, pid)
    assert late.take() == []
    hub.feed(pid, idr(3) + pfr(4) + SC)
    assert b"".join(late.take()) == SPS + PPS + idr(3) + pfr(4)
    assert vr.PRIME_MAX <= 2 * 1024 * 1024


# ---------------------------------------------------------------- L3: run end clears the cached stream
def test_run_end_clears_the_projects_cached_stream() -> None:
    hub = vr.VideoHub()
    pid = "prj_00000000000000d1"
    env = hub.open_run("run_d1d1d1d1d1d1d1d1", pid)
    assert env
    viewer = _sub(hub, pid)
    hub.feed(pid, SPS + PPS + idr(1) + pfr(2) + SC)
    assert viewer.take()
    hub.close_run("run_d1d1d1d1d1d1d1d1")
    assert _sub(hub, pid).take() == []  # a viewer after the run sees no stale GOP
    hub.feed(pid, pfr(3) + SC)  # stray bytes after the end: the old viewer is no longer synced
    assert viewer.take() == []
    hub.feed(pid, SPS + PPS + idr(4) + SC)  # the next run's stream resumes on the same socket
    assert b"".join(viewer.take()) == SPS + PPS + idr(4)
    hub.close()
