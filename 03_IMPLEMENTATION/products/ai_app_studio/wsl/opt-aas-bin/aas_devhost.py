"""AI App Studio device host control loop (python3 -I -S, stdlib only).

Runs as pid 1 of a private user+net+mount+pid namespace started by aas-serve/aas-ns. stdin/stdout carry JSON
lines (docs/DESIGN.md 2.3); stderr is a capped log. stdin EOF or {"op":"shutdown"} stops the emulator and exits;
when this process exits every process of the namespace is killed by the kernel.
Output is written by one thread from two queues: control lines first, video chunks bounded (oldest dropped).
"""
import base64
import hashlib
import json
import os
import queue
import re
import signal
import socket
import stat
import struct
import subprocess
import sys
import threading
import time
import zipfile
from collections import deque

AVDS = {"aas-phone": 5600, "aas-tablet": 5602}
MAX_LINE = 192 * 1024
RAW_CHUNK = 48 * 1024
VIDEO_Q_MAX = 32
BOOT_TIMEOUT = 240
APK_MAX = 200 * 1024 * 1024
VIDEO_SIZES = {"540x1170", "720x1560", "800x1280", "540x864"}
PKG_RE = re.compile(r"^dev\.aiappstudio\.p[0-9a-f]{16}$")
TEXT_RE = re.compile(r"^[ -~]{0,200}$")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")
IDLE_S = 15 * 60  # no request for this long -> stop the session (DESIGN 2.2)
# S9: "-gpu host" hands guest GL streams to the host renderer (gfxstream / virglrenderer -> WSL /dev/dxg -> the Windows
# GPU driver); the software renderer keeps them in the emulator process. Chosen after the live check in docs/slices/S9.md.
GPU_MODE = "swiftshader_indirect"
USER_NET_OPTS = "restrict=on,ipv6=off"  # both NICs: no host (10.0.2.2), no DNS (10.0.2.3), no egress, no IPv6
ADB_REFUSED = frozenset({"reverse", "forward"})  # adb reverse bypasses slirp through a QEMU pipe; forward is not needed


class ConsoleTokenError(Exception):
    """The emulator console auth token is missing, empty or malformed: an empty file DISABLES console auth."""


def ensure_console_token(path):
    """Create the studio console token (random, 0600, never through a link) when it is missing; refuse to go on
    when the file exists but is empty, not a regular file or not token-shaped (fail closed)."""
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0)
    try:
        fd = os.open(path, flags, 0o600)
    except FileExistsError:
        fd = None
    except OSError as e:
        raise ConsoleTokenError("cannot create the console token: %s" % type(e).__name__) from None
    if fd is not None:
        try:
            os.write(fd, base64.urlsafe_b64encode(os.urandom(18)))
        finally:
            os.close(fd)
    try:
        st = os.lstat(path)
    except OSError:
        raise ConsoleTokenError("console token missing") from None
    if not stat.S_ISREG(st.st_mode) or st.st_size == 0 or st.st_size > 256:
        raise ConsoleTokenError("console token file is empty or not a regular file")
    with open(path, "rb") as f:
        tok = f.read(257).decode("ascii", "replace").strip()
    if len(tok) < 16 or not Console.TOKEN_RE.fullmatch(tok):
        raise ConsoleTokenError("console token is malformed")
    return True
MANIFEST_MAX = 4 * 1024 * 1024


SCREEN_MIN = 200
WM_SIZE_RE = re.compile(r"(Physical|Override) size: ([1-9][0-9]{0,4})x([1-9][0-9]{0,4})")
VIEWPORT_ROT_RE = re.compile(r"Viewport INTERNAL:.*?\borientation=([0-9]+)\b")
SURFACE_ROT_RE = re.compile(r"SurfaceOrientation: ([0-9]+)")
ROTATION_CMD = "dumpsys input | grep -E 'Viewport INTERNAL|SurfaceOrientation'"


def parse_wm_size(rc, text):
    """`wm size` -> panel [w, h] or None. Anchored lines only; an override that differs from the physical size
    (scaled display) is None: console pixels would not match `input` pixels."""
    if rc != 0:
        return None
    found = {}
    for ln in text.splitlines():
        m = WM_SIZE_RE.fullmatch(ln.strip())
        if m and m.group(1) not in found:
            found[m.group(1)] = [int(m.group(2)), int(m.group(3))]
    phys = found.get("Physical")
    if phys is None or found.get("Override", phys) != phys:
        return None
    if not all(SCREEN_MIN <= v <= 8192 for v in phys):
        return None
    return phys


def parse_rotation(rc, text):
    """`dumpsys input` (filtered) -> display rotation 0..3, or None when absent, out of range or ambiguous."""
    if rc != 0:
        return None
    vals = set()
    for ln in text.splitlines():
        m = VIEWPORT_ROT_RE.search(ln)
        if m is None or "Viewport INTERNAL" not in ln:
            m = SURFACE_ROT_RE.search(ln)
        if m:
            vals.add(int(m.group(1)))
    if len(vals) != 1:
        return None
    r = vals.pop()
    return r if 0 <= r <= 3 else None


def _int(v, lo, hi):
    if type(v) is not int or not lo <= v <= hi:
        raise ValueError("bad int")
    return v


class Out:
    def __init__(self, stream):
        self.stream = stream
        self.cond = threading.Condition()
        self.ctl = deque()
        self.vid = deque()
        self.dropped = 0
        self.closed = False
        threading.Thread(target=self._run, daemon=True).start()

    def control(self, obj):
        line = (json.dumps(obj, separators=(",", ":")) + "\n").encode()
        if len(line) > MAX_LINE:
            raise ValueError("line too long")
        with self.cond:
            self.ctl.append(line)
            self.cond.notify()

    def video(self, obj):
        line = (json.dumps(obj, separators=(",", ":")) + "\n").encode()
        with self.cond:
            if len(self.vid) >= VIDEO_Q_MAX:
                self.vid.popleft()
                self.dropped += 1
            self.vid.append(line)
            self.cond.notify()

    def flush_wait(self, timeout=5.0):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            with self.cond:
                if not self.ctl:
                    return
            time.sleep(0.02)

    def _run(self):
        while True:
            with self.cond:
                while not self.ctl and not self.vid:
                    self.cond.wait()
                line = self.ctl.popleft() if self.ctl else self.vid.popleft()
            try:
                self.stream.write(line)
                self.stream.flush()
            except (BrokenPipeError, OSError):
                os._exit(3)


class InputShell:
    """One `adb shell` kept open; each command is a line on its stdin followed by a unique completion marker.

    Words are shell-quoted (_q) and every caller validates them first (ints, TEXT_RE). This removes the per-tap
    adb connection and shell start (N7). A timeout kills the shell; the next command starts a new one."""

    def __init__(self, argv):
        self.argv = list(argv)
        self.p = None
        self.q = None
        self.n = 0
        self.spawned = 0
        self.lock = threading.Lock()

    def _ensure(self):
        if self.p is not None and self.p.poll() is None:
            return
        self.p = subprocess.Popen(self.argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL)
        self.spawned += 1
        self.q = queue.Queue(maxsize=256)
        threading.Thread(target=self._pump, args=(self.p, self.q), daemon=True).start()

    @staticmethod
    def _pump(p, q):
        try:
            for line in iter(lambda: p.stdout.readline(4096), b""):
                try:
                    q.put(line, timeout=5)
                except queue.Full:
                    pass
        except (OSError, ValueError):
            pass
        try:
            q.put(None, timeout=5)
        except queue.Full:
            pass

    def _kill(self):
        p, self.p = self.p, None
        if p is not None:
            try:
                p.kill()
                p.wait(timeout=5)
            except (OSError, subprocess.TimeoutExpired):
                pass

    def close(self):
        with self.lock:
            self._kill()

    def run(self, words, timeout=30):
        with self.lock:
            self._ensure()
            self.n += 1
            mark = "__aas_%d__" % self.n
            cmd = " ".join(_q(w) for w in words) + " >/dev/null 2>&1; echo " + mark + " $?\n"
            try:
                self.p.stdin.write(cmd.encode("ascii"))
                self.p.stdin.flush()
            except (OSError, ValueError):
                self._kill()
                return 1, "input shell gone"
            end = time.monotonic() + timeout
            want = mark.encode() + b" "
            while True:
                left = end - time.monotonic()
                try:
                    if left <= 0:
                        raise queue.Empty
                    line = self.q.get(timeout=left)
                except queue.Empty:
                    self._kill()
                    raise subprocess.TimeoutExpired(self.argv, timeout) from None
                if line is None:
                    self._kill()
                    return 1, "input shell exited"
                if line.startswith(want):
                    tail = line[len(want):].strip()
                    return (int(tail), "") if tail.isdigit() else (1, "bad marker")


class ConsoleUnavailable(Exception):
    """Console not usable (connect, banner or auth failed); no touch event was sent on this attempt."""


class ConsoleError(Exception):
    """The console answered KO or dropped the connection after a touch command was sent."""


class Console:
    """Touch injection through the emulator console (N7): `input tap` starts app_process in the guest and took up
    to 3.6 s under screenrecord load; `event mouse` is handled by the emulator itself, outside the guest.

    One persistent connection to 127.0.0.1:<console port> inside the private net namespace, authenticated with
    the token the emulator wrote into the studio fake HOME. Only `event mouse <x> <y> 0 <0|1>` lines with
    integers validated against the screen size are ever sent; the token is sent only when the banner asks for
    it and names exactly that token file.

    Coordinates come in the logical (rotated) display space, like `input tap`; `size` is the panel (rotation 0)
    size and `rotation` (Surface.ROTATION_*) maps logical to panel pixels. Once a touch-down was written, every
    error path writes the matching up (best effort) and is reported as ConsoleError: never retried on adb."""

    MAX_LINE = 1024
    MAX_LINES = 32
    RELEASE_S = 1.0
    LINE_RE = re.compile(r"event mouse (0|[1-9][0-9]{0,4}) (0|[1-9][0-9]{0,4}) 0 [01]")
    TOKEN_RE = re.compile(r"[A-Za-z0-9+/=_-]{1,128}")

    def __init__(self, port, token_path, size, connect=socket.create_connection, rotation=0):
        if not isinstance(size, list) or len(size) != 2:
            raise ValueError("bad size")
        self.w, self.h = _int(size[0], 1, 8192), _int(size[1], 1, 8192)
        self.rotation = _int(rotation, 0, 3)
        self.port = _int(port, 1, 65535)
        self.token_path = token_path
        self.connect = connect
        self.sock = None
        self.buf = b""
        self.connects = 0
        self.disabled = False
        self.lock = threading.Lock()

    # ---------- public ----------
    def set_rotation(self, rotation):
        rotation = _int(rotation, 0, 3)
        with self.lock:
            self.rotation = rotation

    def tap(self, x, y, timeout=5.0):
        with self.lock:
            lw, lh = self._logical()
            x, y = _int(x, 0, lw - 1), _int(y, 0, lh - 1)
            self._gesture([(x, y, 1, 0.0), (x, y, 0, 0.0)], timeout)

    def swipe(self, x1, y1, x2, y2, ms, timeout=10.0):
        with self.lock:
            lw, lh = self._logical()
            x1, x2 = _int(x1, 0, lw - 1), _int(x2, 0, lw - 1)
            y1, y2 = _int(y1, 0, lh - 1), _int(y2, 0, lh - 1)
            ms = _int(ms, 1, 5000)
            steps = max(1, min(100, ms // 16))
            pts = [(x1, y1, 1, 0.0)]
            for k in range(1, steps + 1):
                pts.append((x1 + (x2 - x1) * k // steps, y1 + (y2 - y1) * k // steps, 1,
                            ms / 1000.0 * k / steps))
            pts.append((x2, y2, 0, ms / 1000.0))
            self._gesture(pts, timeout + ms / 1000.0)

    def close(self):
        with self.lock:
            self._close()

    # ---------- internals ----------
    def _logical(self):
        return (self.h, self.w) if self.rotation in (1, 3) else (self.w, self.h)

    def _panel(self, x, y):
        """Logical (rotated) pixel -> panel pixel; inverse of the TouchInputMapper rotation transform."""
        r, w1, h1 = self.rotation, self.w - 1, self.h - 1
        if r == 1:
            return w1 - y, x
        if r == 2:
            return w1 - x, h1 - y
        if r == 3:
            return y, h1 - x
        return x, y

    def _gesture(self, pts, timeout):  # caller holds self.lock
        if self.disabled:
            raise ConsoleUnavailable("disabled")
        t0 = time.monotonic()
        deadline = t0 + timeout
        fresh = self.sock is None
        if fresh:
            self._open(deadline)  # ConsoleUnavailable here: nothing was sent, the adb path may be used
        down = None  # panel position of a touch-down that may have reached the emulator without its up
        try:
            for i, (x, y, b, at) in enumerate(pts):
                wait = t0 + at - time.monotonic()
                if wait > 0:
                    time.sleep(wait)
                px, py = self._panel(x, y)
                line = "event mouse %d %d 0 %d" % (px, py, b)
                if b:
                    down = (px, py)  # set before the write: a partial or unanswered write may have landed
                try:
                    self._cmd(line, deadline)
                except (ConsoleError, OSError) as e:
                    if isinstance(e, ConsoleError) and str(e).startswith("KO"):
                        raise
                    if i != 0 or fresh:
                        self._close()
                        raise ConsoleError("console connection lost") from None
                    # stale persistent connection (the console went away between requests): reconnect once
                    self._close()
                    fresh = True
                    self._open(deadline)
                    self._cmd(line, deadline)
                if not b:
                    down = None
        except BaseException as e:
            if down is not None:
                self._release(down)
                if isinstance(e, ConsoleUnavailable):
                    raise ConsoleError("console lost after touch down") from None
            raise

    def _release(self, pos):
        """Best effort: write the touch-up for a down that was sent, on the open or a fresh connection."""
        deadline = time.monotonic() + self.RELEASE_S
        try:
            if self.sock is None:
                self._open(deadline)
            self._send("event mouse %d %d 0 0" % pos)
            self._until_ok(deadline)
        except Exception:  # noqa: BLE001 - best effort, the original error is reported
            self._close()

    def _cmd(self, line, deadline):
        try:
            self._send(line)
            self._until_ok(deadline)
        except socket.timeout:
            self._close()
            raise subprocess.TimeoutExpired(["console", line], 0) from None
        except ConsoleError as e:
            if not str(e).startswith("KO"):
                self._close()
            raise

    def _send(self, line, secret=False):
        if not secret and not self.LINE_RE.fullmatch(line):
            raise ValueError("bad console line")
        if self.sock is None:
            raise ConsoleError("not connected")
        self.sock.sendall(line.encode("ascii") + b"\n")

    def _readline(self, deadline):
        while b"\n" not in self.buf:
            if len(self.buf) > self.MAX_LINE:
                raise ConsoleError("console line too long")
            left = deadline - time.monotonic()
            if left <= 0:
                raise socket.timeout("console")
            self.sock.settimeout(left)
            data = self.sock.recv(4096)
            if not data:
                raise ConsoleError("console closed")
            self.buf += data
        line, self.buf = self.buf.split(b"\n", 1)
        if len(line) > self.MAX_LINE:
            raise ConsoleError("console line too long")
        return line.rstrip(b"\r")

    def _until_ok(self, deadline):
        lines = []
        while True:
            line = self._readline(deadline)
            if line == b"OK":
                return lines
            if line.startswith(b"KO"):
                raise ConsoleError(line[:120].decode("ascii", "replace"))
            lines.append(line)
            if len(lines) > self.MAX_LINES:
                raise ConsoleError("console reply too long")

    def _read_token(self):
        flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_BINARY", 0)
        try:
            fd = os.open(self.token_path, flags)
        except OSError:
            raise ConsoleUnavailable("no console token") from None
        try:
            st = os.fstat(fd)
            if not stat.S_ISREG(st.st_mode) or st.st_size > 256:
                raise ConsoleUnavailable("bad console token file")
            data = os.read(fd, 257)
        finally:
            os.close(fd)
        try:
            tok = data.decode("ascii").strip()
        except UnicodeDecodeError:
            raise ConsoleUnavailable("bad console token") from None
        if not self.TOKEN_RE.fullmatch(tok):
            raise ConsoleUnavailable("bad console token")
        return tok

    def _open(self, deadline):
        left = max(0.1, deadline - time.monotonic())
        try:
            self.sock = self.connect(("127.0.0.1", self.port), timeout=min(left, 5.0))
        except OSError:
            self.sock = None
            raise ConsoleUnavailable("console connect failed") from None
        self.buf = b""
        self.connects += 1
        try:
            banner = self._until_ok(deadline)
            if any(b"Authentication required" in ln for ln in banner):
                named = [ln[1:-1] for ln in banner if len(ln) > 2 and ln[:1] == b"'" and ln[-1:] == b"'"]
                ok = {self.token_path.encode(), os.path.realpath(self.token_path).encode()}
                if len(named) != 1 or named[0] not in ok:
                    # no path, several paths or another token file: this may not be our console, never send ours
                    self.disabled = True
                    raise ConsoleUnavailable("console does not name the studio token file")
                tok = self._read_token()
                self._send("auth " + tok, secret=True)
                try:
                    self._until_ok(deadline)
                except ConsoleError:
                    self.disabled = True
                    raise ConsoleUnavailable("console auth refused") from None
        except ConsoleUnavailable:
            self._close()
            raise
        except (ConsoleError, OSError) as e:
            self._close()
            raise ConsoleUnavailable("console handshake failed: %s" % (type(e).__name__,)) from None

    def _close(self):
        s, self.sock = self.sock, None
        self.buf = b""
        if s is not None:
            try:
                s.close()
            except OSError:
                pass


def apk_package(path):
    """The `package` attribute of <manifest> in the APK's binary AndroidManifest.xml, or None.

    Stdlib only (no aapt2 in the SDK). Refuses duplicate manifest entries, a non-manifest root element, more than
    one package attribute and anything malformed."""
    try:
        with zipfile.ZipFile(path) as z:
            names = [i.filename for i in z.infolist()]
            if names.count("AndroidManifest.xml") != 1:
                return None
            info = z.getinfo("AndroidManifest.xml")
            if info.file_size > MANIFEST_MAX:
                return None
            with z.open(info) as f:
                data = f.read(MANIFEST_MAX + 1)
        return _axml_package(data)
    except (zipfile.BadZipFile, OSError, ValueError, KeyError, IndexError, UnicodeDecodeError,
            NotImplementedError, EOFError, RuntimeError):
        return None


def _u16(b, o):
    return b[o] | (b[o + 1] << 8)


def _u32(b, o):
    if o + 4 > len(b):
        raise ValueError("short")
    return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)



POOL_MAX_BYTES = 256 * 1024  # total decoded string bytes per manifest
POOL_MAX_STRINGS = 20000
_KNOWN_CHUNKS = {0x0001, 0x0100, 0x0101, 0x0102, 0x0103, 0x0104, 0x0180}  # pool, ns, elements, cdata, resmap


def _pool_strings(b, o):
    hsize, size = _u16(b, o + 2), _u32(b, o + 4)
    if hsize < 28 or hsize > size or o + size > len(b):
        raise ValueError("pool header")
    count, flags, start = _u32(b, o + 8), _u32(b, o + 16), _u32(b, o + 20)
    if count > POOL_MAX_STRINGS or hsize + 4 * count > size or start > size:
        raise ValueError("pool")
    utf8 = bool(flags & 0x100)
    end = o + size
    total = 0
    out = []
    for i in range(count):
        so = o + start + _u32(b, o + hsize + 4 * i)
        if so >= end:
            raise ValueError("string offset")
        if utf8:
            n = b[so]
            so += 2 if n & 0x80 else 1
            blen = b[so]
            if blen & 0x80:
                blen = ((blen & 0x7F) << 8) | b[so + 1]
                so += 1
            so += 1
            nbytes = blen
        else:
            n = _u16(b, so)
            so += 2
            if n & 0x8000:
                n = ((n & 0x7FFF) << 16) | _u16(b, so)
                so += 2
            nbytes = 2 * n
        total += nbytes
        if total > POOL_MAX_BYTES or so + nbytes > end:
            raise ValueError("pool too large")
        out.append(b[so:so + nbytes].decode("utf-8" if utf8 else "utf-16-le"))
    return out


def _axml_package(b):
    try:
        return _axml_package1(b)
    except (ValueError, MemoryError, struct.error, IndexError, UnicodeDecodeError, OverflowError):
        return None


def _axml_package1(b):
    if len(b) < 8 or _u16(b, 0) != 0x0003 or _u32(b, 4) > len(b):
        return None
    top = _u16(b, 2)
    if top < 8 or top > len(b):
        return None
    # pass 1: every chunk header must be sane and known; exactly one string pool (Android uses the last one, a
    # parser that uses another one can be fooled, so more than one is refused).
    chunks = []
    o = top
    while o + 8 <= len(b):
        ctype, hsize, csize = _u16(b, o), _u16(b, o + 2), _u32(b, o + 4)
        if ctype not in _KNOWN_CHUNKS or hsize < 8 or csize < hsize or o + csize > len(b):
            return None
        chunks.append((ctype, hsize, o))
        o += csize
    if o != len(b) and _u32(b, 4) != o:
        return None
    pools = [c for c in chunks if c[0] == 0x0001]
    if len(pools) != 1:
        return None
    strings = _pool_strings(b, pools[0][2])
    for ctype, hsize, o in chunks:
        if ctype != 0x0102:  # first start element must be <manifest>
            continue
        if hsize < 16:
            return None
        body = o + hsize  # ResXMLTree_attrExt: ns, name, attributeStart, attributeSize, attributeCount, ...
        name = _u32(b, body + 4)
        if name >= len(strings) or strings[name] != "manifest":
            return None
        a_start, a_size, a_count = _u16(b, body + 8), _u16(b, body + 10), _u16(b, body + 12)
        if a_size < 20:
            return None
        found = []
        for i in range(a_count):
            ao = body + a_start + i * a_size
            an, raw = _u32(b, ao + 4), _u32(b, ao + 8)
            dtype, data = b[ao + 15], _u32(b, ao + 16)
            if an < len(strings) and strings[an] == "package":
                idx = raw if raw != 0xFFFFFFFF else (data if dtype == 0x03 else None)
                if idx is None or idx >= len(strings):
                    return None
                found.append(strings[idx])
        return found[0] if len(found) == 1 else None
    return None


def log(msg):
    try:
        sys.stderr.write("aas_devhost: " + str(msg)[:400] + "\n")
        sys.stderr.flush()
    except OSError:
        pass


def emulator_argv(emu_bin, avd, port):
    """-no-nested-warnings (emulator 31.3.9 -help: "Disable the warning dialog when emulator is running in nested
    virtualization") replaces the "Emulator Running in Nested Virtualization" modal; no settings file is written."""
    return [emu_bin, "-avd", avd, "-port", str(port), "-gpu", GPU_MODE, "-no-audio",
            "-no-boot-anim", "-accel", "on", "-wipe-data", "-no-snapshot", "-no-snapshot-save", "-no-snapshot-load",
            "-no-nested-warnings", "-grpc-use-jwt",
            "-network-user-mode-options", USER_NET_OPTS, "-wifi-user-mode-options", USER_NET_OPTS]


class Host:
    def __init__(self, avd):
        self.avd = avd
        self.port = AVDS[avd]
        self.serial = "emulator-%d" % self.port
        sdk = os.environ["ANDROID_SDK_ROOT"]
        self.state = os.environ["AAS_STATE"]
        self.adb_bin = os.path.join(sdk, "platform-tools", "adb")
        self.emu_bin = os.path.join(sdk, "emulator", "emulator")
        self.out = Out(sys.stdout.buffer)
        self.emu = None
        self.rec = None
        self.rec_lock = threading.Lock()
        self.video_on = False
        self.key_t = 0.0  # last video_keyframe restart (monotonic)
        self.video_seq = 0
        self.install = None
        self.stopping = False
        self.input = InputShell([self.adb_bin, "-P", "5641", "-s", self.serial, "shell"])
        self.console = None
        self.busy = 0
        self.last_activity = time.monotonic()
        self._exit = os._exit

    # ---------- adb helpers (argv lists, never a host shell) ----------
    def adb(self, *args, timeout=30, data=False):
        if any(a in ADB_REFUSED for a in args[:3]):
            raise ValueError("adb reverse/forward is never used by the device host")
        argv = [self.adb_bin, "-P", "5641", "-s", self.serial, *args]
        r = subprocess.run(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                           timeout=timeout)
        return r if data else (r.returncode, r.stdout.decode("utf-8", "replace").strip())

    def shell(self, *words, timeout=30):
        # adb joins words into one device shell command line: every word is quoted here.
        return self.adb("shell", " ".join(_q(w) for w in words), timeout=timeout)

    # ---------- lifecycle ----------
    def _clear_locks(self):
        """SIGKILL shutdown leaves the emulator's AVD lock files behind; the flock in aas-serve already
        guarantees one session, so stale locks in the studio AVD dir are removed before each start."""
        d = os.path.join(os.environ["ANDROID_AVD_HOME"], self.avd + ".avd")
        for n in os.listdir(d):
            p = os.path.join(d, n)
            if n.endswith(".lock") and os.path.isfile(p) and not os.path.islink(p):
                os.unlink(p)

    def start(self):
        try:  # S9: before anything runs; the emulator reads $HOME/.emulator_console_auth_token (studio fake HOME)
            ensure_console_token(os.path.join(os.environ.get("HOME", ""), ".emulator_console_auth_token"))
        except ConsoleTokenError as e:
            log(str(e))
            self.out.control({"ch": "e", "kind": "exit", "code": 32, "reason": "console-token"})
            return False
        self._clear_locks()
        subprocess.run([self.adb_bin, "-P", "5641", "start-server"], stdin=subprocess.DEVNULL,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30, check=True)
        argv = emulator_argv(self.emu_bin, self.avd, self.port)
        logf = open(os.path.join(self.state, "run", "emulator-%s.log" % self.avd), "wb")
        self.emu = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=logf, stderr=subprocess.STDOUT)
        logf.close()
        t0 = time.monotonic()
        while True:
            if self.emu.poll() is not None:
                self.out.control({"ch": "e", "kind": "exit", "code": 30, "reason": "emulator-exited"})
                return False
            if time.monotonic() - t0 > BOOT_TIMEOUT:
                self.out.control({"ch": "e", "kind": "exit", "code": 31, "reason": "boot-timeout"})
                return False
            try:
                rc, val = self.adb("shell", "getprop sys.boot_completed", timeout=10)
            except subprocess.TimeoutExpired:
                rc, val = 1, ""
            if rc == 0 and val == "1":
                break
            time.sleep(2)
        boot_ms = int((time.monotonic() - t0) * 1000)
        for words in (("settings", "put", "system", "show_touches", "1"),
                      ("settings", "put", "system", "pointer_location", "1"),
                      ("input", "keyevent", "KEYCODE_WAKEUP"), ("wm", "dismiss-keyguard")):
            try:
                self.shell(*words, timeout=20)
            except subprocess.TimeoutExpired:
                pass
        size, self.console = self._setup_console()
        self.out.control({"ch": "e", "kind": "booted", "avd": self.avd, "ms": boot_ms, "size": size})
        if self.console is not None:
            threading.Thread(target=self._rotation_watch, daemon=True).start()
        threading.Thread(target=self._modal_watch, daemon=True).start()
        threading.Thread(target=self._emu_watch, daemon=True).start()
        self.last_activity = time.monotonic()
        threading.Thread(target=self._idle_watch, daemon=True).start()
        return True

    def _setup_console(self):
        """(panel size or [0, 0], Console or None). Unknown or bogus size or rotation: no console (adb path)."""
        try:
            rc, w = self.adb("shell", "wm size", timeout=10)
        except subprocess.TimeoutExpired:
            rc, w = 1, ""
        size = parse_wm_size(rc, w)
        if size is None:
            log("screen size unknown or bogus: console input off")
            return [0, 0], None
        rot = self._read_rotation()
        if rot is None:
            log("display rotation unknown: console input off")
            return size, None
        return size, self._make_console(size, rot)

    def _read_rotation(self):
        try:
            rc, txt = self.adb("shell", ROTATION_CMD, timeout=10)
        except subprocess.TimeoutExpired:
            return None
        return parse_rotation(rc, txt)

    def _rotation_tick(self):
        """Refresh the console rotation; False (console dropped, adb path) when the rotation cannot be read."""
        c = self.console
        if c is None:
            return False
        rot = self._read_rotation()
        if rot is None:
            log("display rotation unreadable: console input off")
            self.console = None
            c.close()
            return False
        c.set_rotation(rot)
        return True

    def _rotation_watch(self, tick=2.0):
        while not self.stopping and self.console is not None:
            time.sleep(tick)
            if self.stopping or not self._rotation_tick():
                return

    def _make_console(self, size, rotation=0):
        """Console client for taps/swipes, or None (adb `input` path). The token is read only from the studio fake
        HOME that aas-serve sets ($AAS_STATE/home), never from another HOME."""
        home = os.environ.get("HOME", "")
        if home != os.path.join(self.state, "home"):
            log("HOME is not the studio home: console input off")
            return None
        try:
            return Console(self.port, os.path.join(home, ".emulator_console_auth_token"), size, rotation=rotation)
        except ValueError:
            log("screen size unknown: console input off")
            return None

    def _idle_watch(self, limit=IDLE_S, tick=5.0):
        while not self.stopping:
            time.sleep(tick)
            if self.stopping:
                return
            if self.busy == 0 and time.monotonic() - self.last_activity > limit:
                log("idle for %ds, stopping" % limit)
                self.stop()
                self.out.control({"ch": "e", "kind": "exit", "code": 35, "reason": "idle"})
                self.out.flush_wait()
                self._exit(35)  # pid 1 exits -> the kernel kills the rest of the namespace
                return

    def _emu_watch(self):
        self.emu.wait()
        if not self.stopping:
            self.stopping = True
            self.out.control({"ch": "e", "kind": "exit", "code": 33, "reason": "emulator-exited"})
            self.out.flush_wait()
            os._exit(33)  # pid 1 exits -> the kernel kills the rest of the namespace

    def _modal_watch(self):
        for _ in range(10):
            try:
                r = subprocess.run(["xwininfo", "-root", "-tree"], stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=10)
                if b"Nested Virtualization" in r.stdout:
                    self.out.control({"ch": "e", "kind": "modal", "title": "Nested Virtualization"})
                    return
            except (OSError, subprocess.TimeoutExpired):
                return
            time.sleep(3)

    def stop(self):
        if self.stopping:
            return
        self.stopping = True
        self._video_stop()
        # No `adb emu kill`: qemu segfaults (core dump) on its graceful-exit path. The guest state is discarded
        # anyway (-wipe-data, -no-snapshot-save), so every process of this pid namespace gets SIGKILL
        # (SIGKILL never dumps core). We are pid 1 here, so kill(-1) reaches all of them but not us.
        if self.install:
            self._install_abort()
        if self.input is not None:
            self.input.close()
        if self.console is not None:
            self.console.close()
        if os.getpid() == 1:
            try:
                os.kill(-1, signal.SIGKILL)
            except ProcessLookupError:
                pass
        elif self.emu is not None and self.emu.poll() is None:
            self.emu.kill()
        if self.emu is not None:
            try:
                self.emu.wait(timeout=15)
            except subprocess.TimeoutExpired:
                pass
        _reap()

    # ---------- video ----------
    def _video_start(self, size):
        with self.rec_lock:
            if self.video_on:
                return
            self.video_on = True
        threading.Thread(target=self._video_loop, args=(size,), daemon=True).start()

    def _video_loop(self, size):
        while self.video_on and not self.stopping:
            argv = [self.adb_bin, "-P", "5641", "-s", self.serial, "exec-out", "screenrecord",
                    "--output-format=h264", "--size", size, "--bit-rate", "4000000", "--time-limit", "180", "-"]
            p = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
            with self.rec_lock:
                self.rec = p
            fd = p.stdout.fileno()
            while True:
                buf = os.read(fd, RAW_CHUNK)
                if not buf:
                    break
                self.video_seq += 1
                self.out.video({"ch": "v", "seq": self.video_seq, "b64": base64.b64encode(buf).decode()})
            p.wait()
            with self.rec_lock:
                self.rec = None

    def _video_keyframe(self):
        """S9b: a fresh key frame for the hub after a discontinuity. screenrecord has no IDR request and the emulator
        console has none either, so the running screenrecord is ended; _video_loop starts the next one at once and a
        new screenrecord stream begins with SPS/PPS + IDR. At most one restart per KEYFRAME_MIN_S."""
        with self.rec_lock:
            if not self.video_on:
                return {"ok": True, "msg": "video-off"}
            now = time.monotonic()
            if now - self.key_t < KEYFRAME_MIN_S:
                return {"ok": True, "msg": "throttled"}
            self.key_t = now
            p = self.rec
        if p is not None and p.poll() is None:
            p.terminate()
            try:
                p.wait(timeout=5)
            except subprocess.TimeoutExpired:
                p.kill()
        return {"ok": True, "msg": "restarted"}

    def _video_stop(self):
        with self.rec_lock:
            self.video_on = False
            p = self.rec
        if p is not None and p.poll() is None:
            p.terminate()
            try:
                p.wait(timeout=5)
            except subprocess.TimeoutExpired:
                p.kill()
        try:
            self.shell("pkill", "-INT", "screenrecord", timeout=10)
        except subprocess.TimeoutExpired:
            pass

    # ---------- install ----------
    def _install_abort(self):
        inst, self.install = self.install, None
        if inst:
            inst["f"].close()
            try:
                os.unlink(inst["path"])
            except OSError:
                pass

    # ---------- dispatch ----------
    def handle(self, req):
        op = req["op"]
        if op == "tap":
            x, y = _int(req["x"], 0, 4096), _int(req["y"], 0, 4096)
            res = self._console_input("tap", (x, y), 5.0)
            if res is not None:
                return res
            return self._ok_rc(self.input.run(("input", "tap", str(x), str(y)), timeout=10))
        if op == "swipe":
            a = [str(_int(req[k], 0, 4096)) for k in ("x1", "y1", "x2", "y2")]
            ms = _int(req["ms"], 1, 5000)
            res = self._console_input("swipe", tuple(int(v) for v in a) + (ms,), 10.0)
            if res is not None:
                return res
            return self._ok_rc(self.input.run(("input", "swipe", *a, str(ms)), timeout=20))
        if op == "text":
            t = req["t"]
            if not isinstance(t, str) or not TEXT_RE.match(t):
                raise ValueError("bad text")
            return self._ok_rc(self.input.run(("input", "text", t.replace(" ", "%s")), timeout=20))
        if op == "launch":
            pkg = req["pkg"]
            if not isinstance(pkg, str) or not PKG_RE.match(pkg):
                raise ValueError("bad pkg")
            return self._ok_rc(self.shell("monkey", "-p", pkg, "-c", "android.intent.category.LAUNCHER", "1"))
        if op == "uninstall":
            pkg = req["pkg"]
            if not isinstance(pkg, str) or not PKG_RE.match(pkg):
                raise ValueError("bad pkg")
            rc, txt = self.shell("pm", "uninstall", pkg, timeout=60)
            return {"ok": rc == 0 and "Success" in txt, "msg": txt[-200:]}
        if op == "screenshot":
            r = self.adb("exec-out", "screencap", "-p", timeout=30, data=True)
            if r.returncode != 0 or not r.stdout.startswith(b"\x89PNG"):
                return {"ok": False, "err": "screencap"}
            png = r.stdout
            n = (len(png) + RAW_CHUNK - 1) // RAW_CHUNK
            for i in range(n):
                part = png[i * RAW_CHUNK:(i + 1) * RAW_CHUNK]
                self.out.control({"ch": "s", "id": req["id"], "seq": i, "last": i == n - 1,
                                  "b64": base64.b64encode(part).decode()})
            return {"ok": True, "size": len(png), "sha256": hashlib.sha256(png).hexdigest(), "chunks": n}
        if op == "video_start":
            if req["size"] not in VIDEO_SIZES:
                raise ValueError("bad size")
            self._video_start(req["size"])
            return {"ok": True}
        if op == "video_keyframe":
            return self._video_keyframe()
        if op == "video_stop":
            self._video_stop()
            return {"ok": True, "dropped": self.out.dropped}
        if op == "install":
            sha, size = req["sha256"], _int(req["size"], 1, APK_MAX)
            if not isinstance(sha, str) or not SHA_RE.match(sha):
                raise ValueError("bad sha")
            self._install_abort()
            path = os.path.join(self.state, "run", sha + ".apk")
            fdn = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            self.install = {"sha": sha, "size": size, "got": 0, "h": hashlib.sha256(), "f": os.fdopen(fdn, "wb"),
                            "path": path}
            return {"ok": True, "phase": "begin"}
        if op == "chunk":
            inst = self.install
            if not inst:
                raise ValueError("no install")
            buf = base64.b64decode(req["b64"], validate=True)
            if inst["got"] + len(buf) > inst["size"]:
                self._install_abort()
                raise ValueError("oversize")
            inst["f"].write(buf)
            inst["h"].update(buf)
            inst["got"] += len(buf)
            if inst["got"] < inst["size"]:
                return {"ok": True, "phase": "chunk", "got": inst["got"]}
            inst["f"].close()
            if inst["h"].hexdigest() != inst["sha"]:
                self._install_abort()
                return {"ok": False, "err": "sha256"}
            self.install = None
            try:
                pkg = apk_package(inst["path"])
                if pkg is None or not PKG_RE.match(pkg):
                    return {"ok": False, "phase": "done", "err": "package not allowed"}
                # between builds: drop the previous build and its data, then install (DESIGN 2.2)
                self.shell("pm", "uninstall", pkg, timeout=60)
                # authoritative check: whatever the manifest parser believed, only studio packages may appear
                before = self._packages()
                if before is None:
                    return {"ok": False, "phase": "done", "err": "package list failed"}
                rc, txt = self.adb("install", "-r", "-d", inst["path"], timeout=180)
                after = self._packages()
                if after is None:
                    return {"ok": False, "phase": "done", "err": "package list failed"}
                bad = sorted(p for p in after - before if not PKG_RE.match(p))
                if bad:
                    for p in bad:
                        if PKG_RE.match(p) is None and re.match(r"^[A-Za-z0-9_.]{1,200}$", p):
                            self.shell("pm", "uninstall", p, timeout=60)
                    return {"ok": False, "phase": "done", "err": "unexpected package installed"}
            finally:
                os.unlink(inst["path"])
            return {"ok": rc == 0 and "Success" in txt, "phase": "done", "pkg": pkg, "msg": txt[-200:]}
        raise ValueError("bad op")

    def _console_input(self, kind, args, timeout):
        """Touch through the emulator console; None means "use the adb input path" (console unavailable, nothing
        was sent). A console error after a command was sent is returned as is: no adb retry, no double tap."""
        c = self.console
        if c is None:
            return None
        try:
            getattr(c, kind)(*args, timeout=timeout)
            return {"ok": True}
        except ConsoleUnavailable as e:
            log("console unavailable (%s), adb input" % (e,))
            if c.disabled:
                self.console = None
            return None
        except ConsoleError as e:
            return {"ok": False, "err": "console: %s" % (str(e)[:100],)}

    def _packages(self):
        rc, txt = self.shell("pm", "list", "packages", timeout=60)
        if rc != 0:
            return None
        return {ln[8:].strip() for ln in txt.splitlines() if ln.startswith("package:")}

    @staticmethod
    def _ok_rc(res):
        rc, txt = res
        return {"ok": rc == 0} if rc == 0 else {"ok": False, "err": txt[-200:]}


KEYFRAME_MIN_S = 2.0  # video_keyframe: at most one screenrecord restart per 2 s (the hub asks at most 1/s)

FIELDS = {
    "tap": {"x", "y"}, "swipe": {"x1", "y1", "x2", "y2", "ms"}, "text": {"t"}, "launch": {"pkg"},
    "uninstall": {"pkg"},
    "screenshot": set(), "video_start": {"size"}, "video_stop": set(), "video_keyframe": set(), "shutdown": set(),
    "install": {"sha256", "size"}, "chunk": {"b64"},
}


def _reap():
    """pid 1 duty: collect every exited child of the namespace."""
    while True:
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            return
        if pid == 0:
            time.sleep(0.2)
            try:
                if os.waitpid(-1, os.WNOHANG)[0] == 0:
                    return
            except ChildProcessError:
                return


def _q(word):
    return "'" + word.replace("'", "'\\''") + "'"


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in AVDS:
        sys.exit(64)
    host = Host(sys.argv[1])
    try:
        if not host.start():
            host.stop()
            host.out.flush_wait()
            sys.exit(30)
    except Exception as e:  # noqa: BLE001
        log("start failed: %r" % (e,))
        host.stop()
        host.out.control({"ch": "e", "kind": "exit", "code": 32, "reason": "start-failed"})
        host.out.flush_wait()
        sys.exit(32)
    stdin = sys.stdin.buffer
    code = 0
    while True:
        if host.emu.poll() is not None:
            host.out.control({"ch": "e", "kind": "exit", "code": 33, "reason": "emulator-exited"})
            code = 33
            break
        line = stdin.readline(MAX_LINE + 1)
        if not line:
            break  # EOF: the Windows side went away
        if len(line) > MAX_LINE or not line.endswith(b"\n"):
            log("oversize request, stopping")
            code = 34
            break
        rid = None
        try:
            req = json.loads(line)
            if not isinstance(req, dict):
                raise ValueError("not an object")
            rid = req.get("id")
            if type(rid) is not int or not 0 < rid < 2 ** 31:
                raise ValueError("bad id")
            op = req.get("op")
            if op not in FIELDS or set(req) != FIELDS[op] | {"id", "op"}:
                raise ValueError("bad request")
            if op == "shutdown":
                host.out.control({"ch": "c", "id": rid, "ok": True})
                break
            t_req = time.monotonic()
            host.busy += 1
            host.last_activity = t_req
            try:
                res = host.handle(req)
            finally:
                host.busy -= 1
                host.last_activity = time.monotonic()
            res = dict(res, ms=int((time.monotonic() - t_req) * 1000))
        except subprocess.TimeoutExpired:
            res = {"ok": False, "err": "timeout"}
        except (ValueError, KeyError, TypeError) as e:
            res = {"ok": False, "err": "invalid: %s" % (str(e)[:80],)}
        if type(rid) is int:
            res = dict(res, ch="c", id=rid)
            host.out.control(res)
    host.stop()
    host.out.control({"ch": "e", "kind": "exit", "code": code, "reason": "stopped"})
    host.out.flush_wait()
    sys.exit(code)


if __name__ == "__main__":
    main()
