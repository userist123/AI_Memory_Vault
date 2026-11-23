"""Windows side of the WSL device host: the ONLY place in the studio that starts wsl.exe (docs/DESIGN.md 2.1).

- argv comes only from argv_for(verb); validate_argv() accepts nothing but the three fixed argv lists.
- Before each spawn the installed /opt/aas/bin files are read through \\\\wsl.localhost, refused if they are
  reparse points, and compared with wsl/opt-aas-bin/MANIFEST.sha256 (drift detection, R-EXT residual).
- wsl.exe runs with a minimal environment (no WSLENV/BASH_ENV, N4) and a fixed cwd.
- Every stdout line is validated by protocol.parse_device_line; stdout is always drained by a reader thread;
  video goes into a bounded queue that drops the oldest chunk (N7), so control replies never wait on video.

CLI (stdio bridge for the backend):
  python -m apps.device_host.launcher preflight
  python -m apps.device_host.launcher bridge phone|tablet
"""
from __future__ import annotations

import base64
import hashlib
import json
import os
import queue
import stat
import subprocess
import sys
import threading
import time
from collections import deque
from pathlib import Path
from typing import Any, Callable, Iterator

from apps.device_host import protocol

SYSTEM_ROOT = os.environ.get("SystemRoot") or os.environ.get("SYSTEMROOT") or r"C:\Windows"
SYSTEM32 = os.path.join(SYSTEM_ROOT, "System32")
WSL_EXE = os.path.join(SYSTEM32, "wsl.exe")
DISTRO = "Ubuntu-S9AI-D"
BIN_UNC = "\\\\wsl.localhost\\" + DISTRO + "\\opt\\aas\\bin"
MANIFEST = Path(__file__).resolve().parents[2] / "wsl" / "opt-aas-bin" / "MANIFEST.sha256"

_TAILS: dict[str, tuple[str, ...]] = {
    "preflight": ("/opt/aas/bin/aas-preflight",),
    "serve-phone": ("/opt/aas/bin/aas-serve", "phone"),
    "serve-tablet": ("/opt/aas/bin/aas-serve", "tablet"),
}
FORMS = {"phone": "aas-phone", "tablet": "aas-tablet"}
_TOKEN_OK = frozenset("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789/._-")
CREATE_NO_WINDOW = 0x08000000


class LauncherRefused(RuntimeError):
    pass


class Tampered(LauncherRefused):
    pass


class DeviceError(RuntimeError):
    pass


class DeviceTimeout(DeviceError):
    pass


def argv_for(verb: str) -> list[str]:
    tail = _TAILS.get(verb) if isinstance(verb, str) else None
    if tail is None:
        raise LauncherRefused("unknown verb")
    return [WSL_EXE, "-d", DISTRO, "--", *tail]


def validate_argv(argv: list[str]) -> None:
    if not isinstance(argv, list) or not all(isinstance(t, str) for t in argv):
        raise LauncherRefused("argv must be a list of str")
    if any(not t or not set(t) <= _TOKEN_OK for t in argv[1:]):
        raise LauncherRefused("unsafe token")
    allowed = [[WSL_EXE, "-d", DISTRO, "--", *tail] for tail in _TAILS.values()]
    if argv not in allowed:
        raise LauncherRefused("argv not in allow-list")


def pinned_hashes() -> dict[str, str]:
    out: dict[str, str] = {}
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        h, name = line.split("  ")
        if len(h) != 64 or not set(name) <= _TOKEN_OK or "/" in name:
            raise Tampered("bad manifest")
        out[name] = h
    return out


def _read_installed(name: str) -> bytes:
    p = BIN_UNC + "\\" + name
    st = os.lstat(p)
    if stat.S_ISLNK(st.st_mode) or getattr(st, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
        raise Tampered("reparse point: " + name)
    if not stat.S_ISREG(st.st_mode):
        raise Tampered("not a regular file: " + name)
    with open(p, "rb") as f:
        return f.read()


def verify_scripts() -> None:
    for name, h in pinned_hashes().items():
        try:
            data = _read_installed(name)
        except OSError as e:
            raise Tampered("missing: " + name) from e
        if hashlib.sha256(data).hexdigest() != h:
            raise Tampered("hash mismatch: " + name)


def minimal_env() -> dict[str, str]:
    return {"SystemRoot": SYSTEM_ROOT, "windir": SYSTEM_ROOT,
            "SystemDrive": os.environ.get("SystemDrive", SYSTEM_ROOT[:2]), "PATH": SYSTEM32}


def _popen(argv: list[str], **kw: Any) -> subprocess.Popen[bytes]:
    return subprocess.Popen(argv, **kw)  # the single wsl.exe call site (PM3a)


def spawn_argv(argv: list[str]) -> Any:
    validate_argv(argv)
    verify_scripts()
    return _popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0,
                  env=minimal_env(), cwd=SYSTEM32, creationflags=CREATE_NO_WINDOW, close_fds=True)


def spawn(verb: str) -> Any:
    return spawn_argv(argv_for(verb))


PREFLIGHT_MAX = 64 * 1024


def _read_capped(stream: Any, cap: int, box: dict[str, Any]) -> None:
    buf = bytearray()
    try:
        while True:
            chunk = stream.read(65536)
            if not chunk:
                break
            buf += chunk
            if len(buf) > cap:
                box["over"] = True
                return
    except (OSError, ValueError):
        pass
    finally:
        box["data"] = bytes(buf[:cap])
        box["done"] = True


def _kill_proc(p: Any) -> None:
    try:
        p.kill()
        p.wait(timeout=15)
    except (OSError, subprocess.TimeoutExpired):
        pass


def _bounded_output(p: Any, cap: int, timeout: float) -> bytes:
    """Read at most cap bytes of p.stdout (stderr is drained, capped and discarded) within timeout, then reap p.
    More output than cap, or no EOF in time, kills the process and refuses."""
    out: dict[str, Any] = {}
    err: dict[str, Any] = {}
    t_out = threading.Thread(target=_read_capped, args=(p.stdout, cap, out), daemon=True)
    t_err = threading.Thread(target=_read_capped, args=(p.stderr, cap, err), daemon=True)
    t_out.start()
    t_err.start()
    t_out.join(timeout)
    if out.get("over"):
        _kill_proc(p)
        raise LauncherRefused("preflight: too much output")
    if not out.get("done"):
        _kill_proc(p)
        raise LauncherRefused("preflight: timeout")
    try:
        p.wait(timeout=max(1.0, timeout / 4))
    except subprocess.TimeoutExpired:
        _kill_proc(p)
        raise LauncherRefused("preflight: no exit") from None
    return out.get("data", b"")


def preflight(timeout: float = 120) -> dict[str, Any]:
    p = spawn("preflight")
    out = _bounded_output(p, PREFLIGHT_MAX, timeout)
    lines = [ln for ln in out.splitlines() if ln.strip()]
    if p.returncode != 0 or len(lines) != 1:
        raise LauncherRefused("preflight failed (rc=%s)" % p.returncode)
    info = protocol.parse_device_line(lines[0] + b"\n")
    if info.get("kind") != "preflight":
        raise LauncherRefused("preflight: unexpected line")
    if info.get("networkingMode") != "nat":
        raise LauncherRefused("WSL networking mode must be nat")
    avds = info.get("avds")
    if not isinstance(avds, list) or any(not isinstance(a, str) or not a.startswith("aas-") for a in avds):
        raise LauncherRefused("studio AVD directory holds a non aas-* AVD")
    if info.get("installed") != pinned_hashes():
        raise Tampered("installed scripts differ from manifest")
    return info


MIN_FREE_BYTES = 5 * 1024 ** 3


def check_disk_space() -> None:
    r"""Refuse a launch when the drive receiving WSL crash dumps (%LOCALAPPDATA%\Temp\wsl-crashes, about 2.6 GB
    per qemu core) or the system drive has less than 5 GiB free."""
    import shutil
    roots = {SYSTEM_ROOT[:3]}
    lad = os.environ.get("LOCALAPPDATA")
    if lad and len(lad) > 2 and lad[1] == ":":
        roots.add(lad[:3])
    for r in sorted(roots):
        if shutil.disk_usage(r).free < MIN_FREE_BYTES:
            raise LauncherRefused("less than 5 GiB free on " + r)


SHOT_MAX_BYTES = 4 * 1024 * 1024  # decoded screenshot bytes buffered for one request id
SHOT_TOTAL_MAX = 8 * 1024 * 1024  # decoded screenshot bytes buffered over all ids
MAX_PENDING = 32  # outstanding request ids (timed-out ones count until their late reply arrives)
BRIDGE_TIMEOUT_MAX = 240.0  # largest per-request timeout the backend may ask for


class DeviceSession:
    """One studio device session (visible WSLg emulator) driven over the JSON-lines protocol.

    The device side is untrusted: a reply ("c") or screenshot part ("s") is accepted only for an outstanding
    request id (screenshot parts only for a screenshot request, in sequence, at most SHOT_MAX_BYTES per id and
    SHOT_TOTAL_MAX overall). Anything else kills the session, so device output cannot grow launcher memory.
    """

    def __init__(self, form: str, video_queue_max: int = 64) -> None:
        if form not in FORMS:
            raise LauncherRefused("unknown form factor")
        self.form = form
        self.verb = "serve-" + form
        self.proc: Any = None
        self.video_q: queue.Queue[dict[str, Any]] = queue.Queue(maxsize=video_queue_max)
        self.video_dropped = 0
        self.video_received = 0
        self._video_gap_next = False  # the last chunk could not be queued: the next one follows a hole
        self.events: deque[dict[str, Any]] = deque(maxlen=200)
        self.stderr_tail: deque[str] = deque(maxlen=200)
        self.exit_event: dict[str, Any] | None = None
        self.violation: str | None = None
        self.booted: dict[str, Any] | None = None  # kept outside the bounded event deque
        self._replies: dict[int, dict[str, Any]] = {}
        self._pending: dict[int, str] = {}  # outstanding id -> op
        self._abandoned: set[int] = set()  # timed-out ids: a late reply is dropped once
        self._shots: dict[int, list[bytes]] = {}
        self._shot_done: set[int] = set()
        self._shot_bytes = 0
        self._cond = threading.Condition()
        self._wlock = threading.Lock()
        self._next = 0
        self._eof = False
        self._on_video: Callable[[dict[str, Any]], None] = self._default_on_video

    # ---------- lifecycle ----------
    def start(self, boot_timeout: float = 300) -> dict[str, Any]:
        check_disk_space()
        self.proc = spawn(self.verb)
        threading.Thread(target=self._reader, daemon=True, name="devhost-stdout").start()
        threading.Thread(target=self._err_reader, daemon=True, name="devhost-stderr").start()
        end = time.monotonic() + boot_timeout
        with self._cond:
            while True:
                if self.booted:
                    return self.booted
                if self._eof or self.exit_event:
                    raise DeviceError("device host exited: %s" % (self.exit_event or self.violation,))
                left = end - time.monotonic()
                if left <= 0:
                    self._kill()
                    raise DeviceTimeout("boot timeout")
                self._cond.wait(min(left, 1.0))

    def shutdown(self, timeout: float = 60) -> int:
        if self.proc is None:
            return 0
        if self.proc.poll() is None and not self._eof:
            try:
                self.request("shutdown", timeout=10)
            except (DeviceError, OSError, protocol.ProtocolError):
                pass
        try:
            self.proc.stdin.close()
        except OSError:
            pass
        try:
            self.proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            self._kill()
        code = self.exit_event.get("code") if self.exit_event else None
        return int(code) if isinstance(code, int) else int(self.proc.returncode or 0)

    def _kill(self) -> None:
        try:
            self.proc.kill()
            self.proc.wait(timeout=15)
        except (OSError, subprocess.TimeoutExpired):
            pass

    # ---------- reader side (always drains stdout) ----------
    def _reader(self) -> None:
        f = self.proc.stdout
        try:
            while True:
                line = f.readline(protocol.MAX_LINE + 1)
                if not line:
                    break
                try:
                    o = protocol.parse_device_line(line)
                except protocol.ProtocolError as e:
                    self.violation = str(e)
                    self._kill()
                    break
                ch = o["ch"]
                if ch == "v":
                    self.video_received += 1
                    self._on_video(o)
                    continue
                with self._cond:
                    bad = self._admit(ch, o)
                    self._cond.notify_all()
                if bad:
                    self.violation = bad
                    self._kill()
                    break
        finally:
            with self._cond:
                self._eof = True
                self._cond.notify_all()

    def _admit(self, ch: str, o: dict[str, Any]) -> str | None:
        """Store one reply / screenshot part / event (caller holds self._cond). Returns a violation or None."""
        if ch == "c":
            rid = o["id"]
            if rid in self._abandoned:
                self._abandoned.discard(rid)
                self._pending.pop(rid, None)
                self._drop_shots(rid)
                return None
            if rid not in self._pending or rid in self._replies:
                return "unsolicited reply"
            self._replies[rid] = o
            return None
        if ch == "s":
            rid = o["id"]
            if rid in self._abandoned and self._pending.get(rid) == "screenshot":
                return None  # late part of a timed-out screenshot: dropped, never stored
            if self._pending.get(rid) != "screenshot" or rid in self._replies:
                return "unsolicited screenshot part"
            parts = self._shots.setdefault(rid, [])
            if o["seq"] != len(parts) or rid in self._shot_done:
                return "screenshot part out of order"
            data = base64.b64decode(o["b64"])
            if sum(len(x) for x in parts) + len(data) > SHOT_MAX_BYTES:
                return "screenshot cap exceeded"
            if self._shot_bytes + len(data) > SHOT_TOTAL_MAX:
                return "screenshot total buffer exceeded"
            parts.append(data)
            self._shot_bytes += len(data)
            if o["last"]:
                self._shot_done.add(rid)
            return None
        self.events.append(o)
        kind = o.get("kind")
        if kind == "booted" and self.booted is None:
            self.booted = o
        elif kind == "exit":
            self.exit_event = o
        return None

    def _drop_shots(self, rid: int) -> list[bytes]:
        parts = self._shots.pop(rid, [])
        self._shot_done.discard(rid)
        self._shot_bytes -= sum(len(x) for x in parts)
        return parts

    def _err_reader(self) -> None:
        for raw in iter(lambda: self.proc.stderr.readline(4096), b""):
            self.stderr_tail.append(raw.decode("utf-8", "replace").rstrip()[:400])

    def _default_on_video(self, o: dict[str, Any]) -> None:
        """Whole chunks only: when full, the oldest chunk is dropped and the chunk now after the hole is marked
        ``gap`` (S8 review M1), so the relay resyncs at the next IDR instead of decoding across the hole."""
        if self._video_gap_next:
            o["gap"] = True
        try:
            self.video_q.put_nowait(o)
            self._video_gap_next = False
        except queue.Full:
            try:
                self.video_q.get_nowait()  # drop the oldest chunk, never block the reader
                self.video_dropped += 1
                with self.video_q.mutex:  # the consumer's next chunk follows the dropped one
                    if self.video_q.queue:
                        self.video_q.queue[0]["gap"] = True
            except queue.Empty:
                pass
            try:
                self.video_q.put_nowait(o)
                self._video_gap_next = False
            except queue.Full:
                self.video_dropped += 1
                self._video_gap_next = True

    def drain_video(self) -> int:
        n = 0
        while True:
            try:
                self.video_q.get_nowait()
                n += 1
            except queue.Empty:
                return n

    def video_chunks(self, timeout: float = 1.0) -> Iterator[bytes]:
        """Decoded h264 Annex-B chunks; stops when no chunk arrives within timeout."""
        while True:
            try:
                o = self.video_q.get(timeout=timeout)
            except queue.Empty:
                return
            yield base64.b64decode(o["b64"])

    # ---------- requests ----------
    def request(self, op: str, timeout: float = 15, **args: Any) -> dict[str, Any]:
        return self._request(op, timeout, **args)[1]

    def _request(self, op: str, timeout: float, **args: Any) -> tuple[int, dict[str, Any]]:
        with self._wlock:
            if self.proc is None or self._eof or self.violation:
                raise DeviceError("session not running")
            line = protocol.encode_request(self._next + 1, op, **args)
            with self._cond:
                if len(self._pending) >= MAX_PENDING:
                    raise DeviceError("too many outstanding requests")
                self._next += 1
                rid = self._next
                self._pending[rid] = op
            self.proc.stdin.write(line)
            self.proc.stdin.flush()
        end = time.monotonic() + timeout
        with self._cond:
            while rid not in self._replies:
                if self._eof:
                    self._pending.pop(rid, None)
                    self._drop_shots(rid)
                    raise DeviceError("device host exited")
                left = end - time.monotonic()
                if left <= 0:
                    self._abandoned.add(rid)  # stays in _pending (counted) until its late reply arrives
                    self._drop_shots(rid)
                    raise DeviceTimeout(op)
                self._cond.wait(left)
            self._pending.pop(rid, None)
            return rid, self._replies.pop(rid)

    def _take_shot(self, rid: int) -> bytes:
        with self._cond:
            return b"".join(self._drop_shots(rid))

    def tap(self, x: int, y: int, timeout: float = 10) -> dict[str, Any]:
        return self.request("tap", timeout=timeout, x=x, y=y)

    def swipe(self, x1: int, y1: int, x2: int, y2: int, ms: int = 300, timeout: float = 10) -> dict[str, Any]:
        return self.request("swipe", timeout=timeout, x1=x1, y1=y1, x2=x2, y2=y2, ms=ms)

    def text(self, t: str) -> dict[str, Any]:
        return self.request("text", t=t)

    def launch(self, pkg: str) -> dict[str, Any]:
        return self.request("launch", pkg=pkg)

    def uninstall(self, pkg: str) -> dict[str, Any]:
        return self.request("uninstall", timeout=60, pkg=pkg)

    def video_start(self, size: str = "540x1170") -> dict[str, Any]:
        return self.request("video_start", size=size)

    def video_stop(self) -> dict[str, Any]:
        return self.request("video_stop", timeout=30)

    def video_keyframe(self) -> dict[str, Any]:
        """S9b: ask the device host for a fresh key frame (screenrecord restart); used by the hub on discontinuity."""
        return self.request("video_keyframe", timeout=10)

    def screenshot(self) -> bytes:
        rid, rep = self._request("screenshot", 40)
        png = self._take_shot(rid)
        if not rep.get("ok") or hashlib.sha256(png).hexdigest() != rep.get("sha256"):
            raise DeviceError("screenshot failed")
        return png

    def install(self, apk: Path) -> dict[str, Any]:
        data = Path(apk).read_bytes()
        rep = self.request("install", sha256=hashlib.sha256(data).hexdigest(), size=len(data))
        if not rep.get("ok"):
            return rep
        for i in range(0, len(data), 48 * 1024):
            rep = self.request("chunk", timeout=240, b64=base64.b64encode(data[i:i + 48 * 1024]).decode())
            if not rep.get("ok"):
                return rep
        return rep


def _bridge(form: str) -> int:
    """stdin: {"op":..., args} per line (no id); stdout: validated device lines + replies, one per line."""
    out_lock = threading.Lock()

    def emit(obj: dict[str, Any]) -> None:
        with out_lock:
            sys.stdout.write(json.dumps(obj, separators=(",", ":")) + "\n")
            sys.stdout.flush()

    s = DeviceSession(form)
    try:
        emit(s.start())
    except (DeviceError, LauncherRefused) as e:
        emit({"ch": "e", "kind": "exit", "code": 1, "reason": type(e).__name__})
        return 1

    def pump_video() -> None:
        while s.proc.poll() is None:
            try:
                emit(s.video_q.get(timeout=1.0))
            except queue.Empty:
                continue

    threading.Thread(target=pump_video, daemon=True).start()
    for raw in sys.stdin:
        out, stop = _bridge_line(s, raw)
        emit(out)
        if stop:
            break
    return s.shutdown()


def _bridge_timeout(req: dict[str, Any]) -> float:
    if "timeout" not in req:
        return 15.0
    t = req.pop("timeout")
    if type(t) not in (int, float) or not 0 < t <= BRIDGE_TIMEOUT_MAX:  # NaN fails both comparisons
        raise _BadTimeout()
    return float(t)


class _BadTimeout(ValueError):
    pass


def _bridge_line(s: DeviceSession, raw: str) -> tuple[dict[str, Any], bool]:
    """One backend line -> (reply to emit, stop?). Only JSON objects with a protocol op are forwarded; a
    backend-supplied timeout must be a number in (0, BRIDGE_TIMEOUT_MAX]."""
    try:
        if len(raw) > protocol.MAX_LINE:
            raise ValueError("line too long")
        req = json.loads(raw)
        if not isinstance(req, dict):
            raise TypeError("not an object")
        op = req.pop("op")
        if not isinstance(op, str):
            raise TypeError("bad op")
        timeout = _bridge_timeout(req)
        if op == "screenshot":
            if req:
                raise protocol.ProtocolError("bad fields for screenshot")
            png = s.screenshot()
            return {"ch": "c", "op": op, "ok": True, "png_b64": base64.b64encode(png).decode()}, False
        rep = s.request(op, timeout=timeout, **req)
        return dict(rep, op=op), op == "shutdown"
    except _BadTimeout:
        return {"ch": "c", "ok": False, "err": "bad timeout"}, False
    except (ValueError, KeyError, TypeError, DeviceError, protocol.ProtocolError) as e:
        return {"ch": "c", "ok": False, "err": type(e).__name__}, False


def main(argv: list[str]) -> int:
    if argv == ["preflight"]:
        print(json.dumps(preflight(), separators=(",", ":")))
        return 0
    if len(argv) == 2 and argv[0] == "bridge" and argv[1] in FORMS:
        return _bridge(argv[1])
    print("usage: launcher.py preflight | bridge phone|tablet", file=sys.stderr)
    return 64


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
