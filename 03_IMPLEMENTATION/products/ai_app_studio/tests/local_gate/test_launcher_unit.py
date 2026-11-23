"""Non-live launcher hardening: reply/screenshot admission, bounded buffers, preflight cap, boot event, bridge input.

A fake process stands in for wsl.exe; nothing here starts WSL or an emulator.
"""
from __future__ import annotations

import base64
import hashlib
import io
import json
import queue
import threading
import time
import tracemalloc
from typing import Any, Callable, Iterator

import pytest

from apps.device_host import launcher, protocol


class _Stdout:
    """Line source that counts how many lines the reader consumed (lazy: an attacker stream never ends)."""

    def __init__(self, lines: Iterator[bytes] | None = None) -> None:
        self._lines = lines
        self.q: queue.Queue[bytes] = queue.Queue()
        self.consumed = 0

    def readline(self, _n: int = -1) -> bytes:
        if self._lines is not None:
            line = next(self._lines, b"")
        else:
            try:
                line = self.q.get(timeout=10)
            except queue.Empty:
                line = b""
        if line:
            self.consumed += 1
        return line


class _Stdin:
    def __init__(self, on_line: Callable[[dict[str, Any]], None]) -> None:
        self.on_line = on_line
        self.closed = False

    def write(self, data: bytes) -> int:
        self.on_line(json.loads(data))
        return len(data)

    def flush(self) -> None:
        pass

    def close(self) -> None:
        self.closed = True


class FakeProc:
    def __init__(self, lines: Iterator[bytes] | None = None,
                 responder: Callable[["FakeProc", dict[str, Any]], None] | None = None) -> None:
        self.stdout = _Stdout(lines)
        self.stderr = io.BytesIO(b"")
        self.stdin = _Stdin(lambda req: responder(self, req) if responder else None)
        self.killed = 0
        self.returncode: int | None = None

    def send(self, obj: dict[str, Any]) -> None:
        self.stdout.q.put((json.dumps(obj, separators=(",", ":")) + "\n").encode())

    def eof(self) -> None:
        self.stdout.q.put(b"")

    def kill(self) -> None:
        self.killed += 1
        self.returncode = -9

    def wait(self, timeout: float | None = None) -> int:
        return self.returncode or 0

    def poll(self) -> int | None:
        return self.returncode


def _line(obj: dict[str, Any]) -> bytes:
    return (json.dumps(obj, separators=(",", ":")) + "\n").encode()


def _session(proc: FakeProc) -> launcher.DeviceSession:
    s = launcher.DeviceSession("phone")
    s.proc = proc
    return s


# ---------- 1: only replies / screenshot parts for outstanding ids are accepted ----------

def test_unsolicited_replies_kill_session_memory_bounded() -> None:
    lines = (_line({"ch": "c", "id": i, "ok": True}) for i in range(1, 200_001))
    proc = FakeProc(lines)
    s = _session(proc)
    tracemalloc.start()
    s._reader()
    _cur, peak = tracemalloc.get_traced_memory()
    tracemalloc.stop()
    assert proc.killed >= 1
    assert s.violation and "unsolicited" in s.violation
    assert s._replies == {} and proc.stdout.consumed == 1
    assert peak < 2 * 1024 * 1024


def test_unsolicited_screenshot_ids_kill_session() -> None:
    b64 = base64.b64encode(b"x" * 1024).decode()
    lines = (_line({"ch": "s", "id": i, "seq": 0, "last": False, "b64": b64}) for i in range(1, 3001))
    proc = FakeProc(lines)
    s = _session(proc)
    s._reader()
    assert proc.killed >= 1 and s.violation and "screenshot" in s.violation
    assert s._shots == {} and proc.stdout.consumed == 1


def _pending(s: launcher.DeviceSession, rid: int, op: str) -> None:
    with s._cond:
        s._pending[rid] = op


def test_screenshot_parts_capped_per_id() -> None:
    raw = b"x" * (48 * 1024)
    b64 = base64.b64encode(raw).decode()
    n = launcher.SHOT_MAX_BYTES // len(raw) + 5
    lines = (_line({"ch": "s", "id": 7, "seq": i, "last": False, "b64": b64}) for i in range(n))
    proc = FakeProc(lines)
    s = _session(proc)
    _pending(s, 7, "screenshot")
    s._reader()
    assert proc.killed >= 1 and s.violation and "cap" in s.violation
    assert proc.stdout.consumed <= launcher.SHOT_MAX_BYTES // len(raw) + 1
    assert s._shot_bytes <= launcher.SHOT_MAX_BYTES


def test_screenshot_total_buffer_capped() -> None:
    raw = b"y" * (48 * 1024)
    b64 = base64.b64encode(raw).decode()
    per_id = launcher.SHOT_MAX_BYTES // len(raw) - 1
    ids = list(range(1, launcher.MAX_PENDING + 1))
    lines = (_line({"ch": "s", "id": rid, "seq": i, "last": False, "b64": b64}) for rid in ids for i in range(per_id))
    proc = FakeProc(lines)
    s = _session(proc)
    for rid in ids:
        _pending(s, rid, "screenshot")
    s._reader()
    assert proc.killed >= 1 and s.violation and "total" in s.violation
    assert s._shot_bytes <= launcher.SHOT_TOTAL_MAX


def test_screenshot_part_for_non_screenshot_request_kills() -> None:
    b64 = base64.b64encode(b"z").decode()
    proc = FakeProc(iter([_line({"ch": "s", "id": 3, "seq": 0, "last": True, "b64": b64})]))
    s = _session(proc)
    _pending(s, 3, "tap")
    s._reader()
    assert proc.killed and s.violation


def test_screenshot_out_of_order_seq_kills() -> None:
    b64 = base64.b64encode(b"z").decode()
    proc = FakeProc(iter([_line({"ch": "s", "id": 3, "seq": 1, "last": True, "b64": b64})]))
    s = _session(proc)
    _pending(s, 3, "screenshot")
    s._reader()
    assert proc.killed and s.violation


def test_duplicate_reply_kills_session() -> None:
    def responder(p: FakeProc, req: dict[str, Any]) -> None:
        p.send({"ch": "c", "id": req["id"], "ok": True})
        p.send({"ch": "c", "id": req["id"], "ok": True})  # replay of an answered id

    proc = FakeProc(responder=responder)
    s = _session(proc)
    t = threading.Thread(target=s._reader, daemon=True)
    t.start()
    assert s.tap(1, 2, timeout=5)["ok"] is True
    t.join(5)
    assert proc.killed and s.violation and "unsolicited" in s.violation


def test_late_reply_after_timeout_is_dropped_not_fatal() -> None:
    held: list[int] = []

    def responder(p: FakeProc, req: dict[str, Any]) -> None:
        if req["op"] == "tap" and not held:
            held.append(req["id"])  # answer later
            return
        if held and req["op"] == "swipe":
            p.send({"ch": "c", "id": held.pop(), "ok": True})
        p.send({"ch": "c", "id": req["id"], "ok": True})

    proc = FakeProc(responder=responder)
    s = _session(proc)
    threading.Thread(target=s._reader, daemon=True).start()
    with pytest.raises(launcher.DeviceTimeout):
        s.tap(1, 2, timeout=0.2)
    assert s.swipe(1, 2, 3, 4, 100, timeout=5)["ok"] is True
    assert s.violation is None and proc.killed == 0
    assert s._replies == {} and s._pending == {}


def test_screenshot_roundtrip_ok() -> None:
    png = b"\x89PNG" + b"p" * (100 * 1024)

    def responder(p: FakeProc, req: dict[str, Any]) -> None:
        parts = [png[i:i + 48 * 1024] for i in range(0, len(png), 48 * 1024)]
        for i, part in enumerate(parts):
            p.send({"ch": "s", "id": req["id"], "seq": i, "last": i == len(parts) - 1,
                    "b64": base64.b64encode(part).decode()})
        p.send({"ch": "c", "id": req["id"], "ok": True, "size": len(png),
                "sha256": hashlib.sha256(png).hexdigest(), "chunks": len(parts)})

    proc = FakeProc(responder=responder)
    s = _session(proc)
    threading.Thread(target=s._reader, daemon=True).start()
    assert s.screenshot() == png
    assert s._shots == {} and s._shot_bytes == 0 and s.violation is None


def test_pending_requests_capped() -> None:
    proc = FakeProc(responder=lambda p, r: None)  # never answers
    s = _session(proc)
    for _ in range(launcher.MAX_PENDING):
        with pytest.raises(launcher.DeviceTimeout):
            s.tap(1, 1, timeout=0.001)
    with pytest.raises(launcher.DeviceError, match="too many outstanding"):
        s.tap(1, 1, timeout=0.001)


# ---------- 2: preflight output is read with a cap ----------

class _Flood(io.RawIOBase):
    def __init__(self, total: int) -> None:
        self.left = total
        self.read_bytes = 0

    def readable(self) -> bool:
        return True

    def read(self, n: int = -1) -> bytes:
        if self.left <= 0:
            return b""
        k = min(self.left, 65536 if n is None or n < 0 else n)
        self.left -= k
        self.read_bytes += k
        return b"a" * k


def test_preflight_bounded_read(monkeypatch: pytest.MonkeyPatch) -> None:
    proc = FakeProc()
    flood = _Flood(50 * 1024 * 1024)
    proc.stdout = flood  # type: ignore[assignment]
    monkeypatch.setattr(launcher, "spawn", lambda verb: proc)
    with pytest.raises(launcher.LauncherRefused, match="too much output"):
        launcher.preflight(timeout=10)
    assert proc.killed >= 1
    assert flood.read_bytes <= launcher.PREFLIGHT_MAX + 65536


def test_preflight_small_output_parsed(monkeypatch: pytest.MonkeyPatch) -> None:
    info = {"ch": "e", "kind": "preflight", "networkingMode": "nat", "avds": ["aas-phone"],
            "installed": launcher.pinned_hashes()}
    proc = FakeProc()
    proc.stdout = io.BytesIO(_line(info))  # type: ignore[assignment]
    proc.returncode = 0
    monkeypatch.setattr(launcher, "spawn", lambda verb: proc)
    assert launcher.preflight(timeout=10)["networkingMode"] == "nat"


# ---------- 3: the boot event cannot be pushed out of the bounded event deque ----------

def test_boot_event_survives_event_burst(monkeypatch: pytest.MonkeyPatch) -> None:
    booted = {"ch": "e", "kind": "booted", "avd": "aas-phone", "ms": 1000, "size": [1080, 2340]}
    burst = [_line(booted)] + [_line({"ch": "e", "kind": "log", "msg": "m%d" % i}) for i in range(500)]
    gate, done = threading.Event(), threading.Event()

    def lines() -> Iterator[bytes]:
        gate.wait(5)
        yield from burst
        while not done.is_set():
            time.sleep(0.05)
            yield _line({"ch": "e", "kind": "log", "msg": "tail"})

    proc = FakeProc(lines())
    monkeypatch.setattr(launcher, "check_disk_space", lambda: None)
    monkeypatch.setattr(launcher, "spawn", lambda verb: proc)
    s = launcher.DeviceSession("phone")
    real_wait = s._cond.wait

    def slow_wait(t: float | None = None) -> bool:  # start() looks again only after the whole burst
        gate.set()
        end = time.monotonic() + 5
        while proc.stdout.consumed < len(burst) and time.monotonic() < end:
            real_wait(0.05)
        return True

    s._cond.wait = slow_wait  # type: ignore[method-assign]
    try:
        ev = s.start(boot_timeout=10)
    finally:
        done.set()
    assert ev["kind"] == "booted"
    assert all(e.get("kind") != "booted" for e in s.events)  # it was pushed out of the deque


# ---------- 8: bridge input ----------

def _bridge_session(responder: Callable[[FakeProc, dict[str, Any]], None]) -> launcher.DeviceSession:
    proc = FakeProc(responder=responder)
    s = _session(proc)
    threading.Thread(target=s._reader, daemon=True).start()
    return s


def _ok(p: FakeProc, req: dict[str, Any]) -> None:
    p.send({"ch": "c", "id": req["id"], "ok": True})


@pytest.mark.parametrize("raw", ["[1,2]", "\"tap\"", "3", "null", "{bad json"])
def test_bridge_rejects_non_object_lines(raw: str) -> None:
    s = _bridge_session(_ok)
    out, stop = launcher._bridge_line(s, raw)
    assert out["ok"] is False and stop is False


@pytest.mark.parametrize("t", [launcher.BRIDGE_TIMEOUT_MAX + 1, 1e9, -1, 0, True, "5", float("nan"), float("inf")])
def test_bridge_refuses_bad_timeout(t: Any) -> None:
    seen: list[dict[str, Any]] = []
    s = _bridge_session(lambda p, r: seen.append(r) or _ok(p, r))
    out, _ = launcher._bridge_line(s, json.dumps({"op": "tap", "x": 1, "y": 2, "timeout": t}))
    assert out["ok"] is False and out["err"] == "bad timeout"
    assert seen == []


def test_bridge_accepts_valid_request_and_timeout() -> None:
    s = _bridge_session(_ok)
    out, stop = launcher._bridge_line(s, json.dumps({"op": "tap", "x": 1, "y": 2, "timeout": 5}))
    assert out["ok"] is True and out["op"] == "tap" and stop is False


def test_bridge_unknown_op_refused() -> None:
    s = _bridge_session(_ok)
    out, _ = launcher._bridge_line(s, json.dumps({"op": "shell", "cmd": "id"}))
    assert out["ok"] is False


def test_check_disk_space_docstring_has_no_invalid_escape() -> None:
    import warnings
    src = (launcher.MANIFEST.parents[2] / "apps" / "device_host" / "launcher.py").read_text(encoding="utf-8")
    with warnings.catch_warnings():
        warnings.simplefilter("error")
        compile(src, "launcher.py", "exec")


def test_protocol_uninstall_op() -> None:
    line = protocol.encode_request(1, "uninstall", pkg="dev.aiappstudio.p0123456789abcdef")
    assert json.loads(line)["op"] == "uninstall"
    with pytest.raises(protocol.ProtocolError):
        protocol.encode_request(1, "uninstall", pkg="com.android.settings")
