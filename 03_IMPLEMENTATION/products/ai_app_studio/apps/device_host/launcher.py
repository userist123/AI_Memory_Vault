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


def preflight(timeout: float = 120) -> dict[str, Any]:
    p = spawn("preflight")
    out, _err = p.communicate(timeout=timeout)
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
    """Refuse a launch when the drive receiving WSL crash dumps (%LOCALAPPDATA%\Temp\wsl-crashes, about 2.6 GB
    per qemu core) or the system drive has less than 5 GiB free."""
    import shutil
    roots = {SYSTEM_ROOT[:3]}
    lad = os.environ.get("LOCALAPPDATA")
    if lad and len(lad) > 2 and lad[1] == ":":
        roots.add(lad[:3])
    for r in sorted(roots):
        if shutil.disk_usage(r).free < MIN_FREE_BYTES:
            raise LauncherRefused("less than 5 GiB free on " + r)


class DeviceSession:
    """One studio device session (visible WSLg emulator) driven over the JSON-lines protocol."""

    def __init__(self, form: str, video_queue_max: int = 64) -> None:
        if form not in FORMS:
            raise LauncherRefused("unknown form factor")
        self.form = form
        self.verb = "serve-" + form
        self.proc: Any = None
        self.video_q: queue.Queue[dict[str, Any]] = queue.Queue(maxsize=video_queue_max)
        self.video_dropped = 0
        self.video_received = 0
        self.events: deque[dict[str, Any]] = deque(maxlen=200)
        self.stderr_tail: deque[str] = deque(maxlen=200)
        self.exit_event: dict[str, Any] | None = None
        self.violation: str | None = None
        self._replies: dict[int, dict[str, Any]] = {}
        self._shots: dict[int, list[bytes]] = {}
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
                booted = next((e for e in self.events if e.get("kind") == "booted"), None)
                if booted:
                    return booted
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
                elif ch == "c":
                    with self._cond:
                        self._replies[o["id"]] = o
                        self._cond.notify_all()
                elif ch == "s":
                    with self._cond:
                        self._shots.setdefault(o["id"], []).append(base64.b64decode(o["b64"]))
                else:
                    with self._cond:
                        self.events.append(o)
                        if o.get("kind") == "exit":
                            self.exit_event = o
                        self._cond.notify_all()
        finally:
            with self._cond:
                self._eof = True
                self._cond.notify_all()

    def _err_reader(self) -> None:
        for raw in iter(lambda: self.proc.stderr.readline(4096), b""):
            self.stderr_tail.append(raw.decode("utf-8", "replace").rstrip()[:400])

    def _default_on_video(self, o: dict[str, Any]) -> None:
        try:
            self.video_q.put_nowait(o)
        except queue.Full:
            try:
                self.video_q.get_nowait()  # drop the oldest chunk, never block the reader
                self.video_dropped += 1
            except queue.Empty:
                pass
            try:
                self.video_q.put_nowait(o)
            except queue.Full:
                self.video_dropped += 1

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
            self._next += 1
            rid = self._next
            line = protocol.encode_request(rid, op, **args)
            if self.proc is None or self._eof:
                raise DeviceError("session not running")
            self.proc.stdin.write(line)
            self.proc.stdin.flush()
        end = time.monotonic() + timeout
        with self._cond:
            while rid not in self._replies:
                if self._eof:
                    raise DeviceError("device host exited")
                left = end - time.monotonic()
                if left <= 0:
                    raise DeviceTimeout(op)
                self._cond.wait(left)
            return rid, self._replies.pop(rid)

    def tap(self, x: int, y: int, timeout: float = 10) -> dict[str, Any]:
        return self.request("tap", timeout=timeout, x=x, y=y)

    def swipe(self, x1: int, y1: int, x2: int, y2: int, ms: int = 300, timeout: float = 10) -> dict[str, Any]:
        return self.request("swipe", timeout=timeout, x1=x1, y1=y1, x2=x2, y2=y2, ms=ms)

    def text(self, t: str) -> dict[str, Any]:
        return self.request("text", t=t)

    def launch(self, pkg: str) -> dict[str, Any]:
        return self.request("launch", pkg=pkg)

    def video_start(self, size: str = "540x1170") -> dict[str, Any]:
        return self.request("video_start", size=size)

    def video_stop(self) -> dict[str, Any]:
        return self.request("video_stop", timeout=30)

    def screenshot(self) -> bytes:
        rid, rep = self._request("screenshot", 40)
        with self._cond:
            parts = self._shots.pop(rid, [])
        png = b"".join(parts)
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
        try:
            req = json.loads(raw)
            op = req.pop("op")
            if op == "screenshot":
                png = s.screenshot()
                emit({"ch": "c", "op": op, "ok": True, "png_b64": base64.b64encode(png).decode()})
                continue
            rep = s.request(op, **req)
            emit(dict(rep, op=op))
            if op == "shutdown":
                break
        except (ValueError, KeyError, TypeError, DeviceError, protocol.ProtocolError) as e:
            emit({"ch": "c", "ok": False, "err": type(e).__name__})
    return s.shutdown()


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
