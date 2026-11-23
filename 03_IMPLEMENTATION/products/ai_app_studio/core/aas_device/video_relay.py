"""Live emulator mirror relay (S8; DESIGN 2.3 video chunks, 5.4 binary WS /api/v1/device/video). Stdlib only.

Path: device host ``{"ch":"v","seq":n,"b64":...}`` -> DeviceSession.video_q in the run's MCP child (bounded, the oldest
chunk is dropped first and the chunk after the hole is marked ``gap``, N7) -> ``VideoSender`` -> per-run local pipe ->
``VideoHub`` in the backend -> owner WebSocket.

- The pipe (S9, B8) is a native Windows named pipe on a random name (``\\\\.\\pipe\\aas-video-<128-bit hex>``) made by
  ``NativePipeListener``: protected DACL with one ACE for the current logon SID, PIPE_REJECT_REMOTE_CLIENTS,
  FILE_FLAG_FIRST_PIPE_INSTANCE (no second server on the name), accept with a timeout; each connection is a
  ``multiprocessing.connection.PipeConnection``. Elsewhere a ``multiprocessing.connection`` socket in a fresh private
  temp dir. Per run there is a 256-bit key: both sides prove the key
  (HMAC challenge) before one byte of video is accepted. The accept loop only accepts; each connection's handshake runs
  in its own short-lived thread and must finish within AUTH_TIMEOUT (at most MAX_PENDING_AUTH at once), so a client
  that connects and never answers cannot block the real sender (review L1). The key travels only in the MCP child's
  env (the run's mcp.json, like the run grant) and never reaches the agent CLI's env. Only ``recv_bytes`` is used
  (never ``recv``: nothing is unpickled). One sender at a time per run; the pipe is closed when the run ends.
- Pipe messages are an 8-byte big-endian chunk seq + h264 bytes. The sender skips a seq when the host's seq jumped or
  the launcher marked a drop; any seq other than last + 1 is a discontinuity (review M1): the hub drops until the next
  IDR and sends ``KEY_REQUEST`` back on the pipe (throttled), the sender asks the session for a fresh key frame when it
  can (``video_keyframe``: since S9b the device host restarts screenrecord, throttled to one per 2 s).
- Caps: a chunk is at most MAX_CHUNK bytes (a bigger one drops the sender), a token bucket of ``max_bps`` /
  ``burst`` bytes per project (excess chunks are dropped and the stream is marked discontinuous), a NAL unit is at
  most MAX_NAL bytes (else the splitter resets), at most ``max_subscribers`` sockets per project, each with a live queue
  of at most ``sub_queue_bytes`` plus a late-viewer prime of at most ``prime_max`` counted apart (review L2); WS frames
  are at most MAX_FRAME bytes: a 4-byte big-endian frame seq + NAL bytes; a viewer-side drop skips one seq.
- The hub re-cuts the byte stream at NAL boundaries. A viewer only ever receives whole NAL units in order, starting
  with SPS + PPS + an IDR; once synced it receives every NAL, including a new SPS/PPS (review M1). A late viewer gets
  the cached SPS/PPS + the current GOP; a viewer whose queue overflowed (or any viewer after a discontinuity) loses
  whole NALs and resumes at the next IDR. Video is never stored.
- The per-project WS outlives runs (review L3): at run end the hub drops the project's cached SPS/PPS/GOP and desyncs
  its viewers; the socket stays open and idle and the next run's stream resumes on it with SPS + PPS + IDR.
"""
from __future__ import annotations

import base64
import os
import secrets
import shutil
import sys
import tempfile
import threading
import time
from collections import deque
from collections.abc import Callable
from multiprocessing import AuthenticationError
from multiprocessing.connection import Client, Listener, answer_challenge, deliver_challenge
from typing import Any

MAX_CHUNK = 64 * 1024  # DESIGN 2.3: a device video line carries <= 64 KiB of h264
MAX_FRAME = 64 * 1024  # one binary WS frame to the UI (seq header included)
MAX_NAL = 1024 * 1024  # an IDR of 720x1560 at 4 Mbit/s is well below this
GOP_MAX_BYTES = 2 * 1024 * 1024  # cached for late viewers; above it the late viewer waits for the next IDR
PRIME_MAX = 2 * 1024 * 1024  # SPS + PPS + GOP sent to a late viewer at once (L2); kept apart from the live budget
MAX_BPS = 2 * 1024 * 1024  # screenrecord runs at 4 Mbit/s (0.5 MiB/s); 4x headroom
BURST = 2 * 1024 * 1024
MAX_SUBSCRIBERS = 2
SUB_QUEUE_BYTES = 4 * 1024 * 1024
SC = b"\x00\x00\x00\x01"
ENV_PIPE, ENV_KEY = "AAS_VIDEO_PIPE", "AAS_VIDEO_KEY"
SEQ_HEADER = 8  # pipe message = 8-byte big-endian chunk seq + h264 bytes (M1)
WS_SEQ_HEADER = 4  # WS frame = 4-byte big-endian frame seq + h264 bytes
KEY_REQUEST = b"K"  # hub -> sender on the same pipe: the stream is discontinuous, a fresh key frame would help
KEY_REQUEST_MIN_S = 2.0
AUTH_TIMEOUT = 2.0  # seconds for a connection to complete the HMAC handshake (L1)
MAX_PENDING_AUTH = 4  # concurrent unauthenticated connections per run; more are closed at once
PIPE_INSTANCES = MAX_PENDING_AUTH + 2  # one sender + pending handshakes + the instance waiting for the next client
ACCEPT_POLL_S = 0.5


# ---------------------------------------------------------------- S9 (B8): native Windows named pipe
if sys.platform == "win32":
    import ctypes
    from ctypes import wintypes
    from multiprocessing.connection import PipeConnection

    import _winapi

    _ADV = ctypes.WinDLL("advapi32", use_last_error=True)
    _K32 = ctypes.WinDLL("kernel32", use_last_error=True)

    class _SA(ctypes.Structure):
        _fields_ = [("nLength", wintypes.DWORD), ("lpSecurityDescriptor", ctypes.c_void_p),
                    ("bInheritHandle", wintypes.BOOL)]

    _ADV.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    _ADV.GetTokenInformation.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
                                         ctypes.POINTER(wintypes.DWORD)]
    _ADV.ConvertSidToStringSidW.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.LPWSTR)]
    _ADV.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [
        wintypes.LPCWSTR, wintypes.DWORD, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.ULONG)]
    _K32.GetCurrentProcess.restype = wintypes.HANDLE
    _K32.LocalFree.argtypes = [ctypes.c_void_p]
    _K32.CloseHandle.argtypes = [wintypes.HANDLE]
    _K32.CreateNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
                                      wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(_SA)]
    _K32.CreateNamedPipeW.restype = wintypes.HANDLE

    _TOKEN_QUERY = 0x0008
    _TOKEN_LOGON_SID = 28  # TokenLogonSid: TOKEN_GROUPS holding the logon SID
    _PIPE_ACCESS_DUPLEX = 0x3
    _FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000
    _FILE_FLAG_OVERLAPPED = 0x40000000
    _PIPE_TYPE_MESSAGE, _PIPE_READMODE_MESSAGE, _PIPE_REJECT_REMOTE_CLIENTS = 0x4, 0x2, 0x8
    _INVALID_HANDLE = wintypes.HANDLE(-1).value
    _ERROR_NO_DATA = 232
    _ERROR_PIPE_BUSY = 231
    _WAIT_TIMEOUT = 0x102

    def logon_sid() -> str:
        """String SID of this process's logon session (S-1-5-5-X-Y): shared by the backend and its MCP child, not by
        another account, a network logon or a service."""
        tok = wintypes.HANDLE()
        if not _ADV.OpenProcessToken(_K32.GetCurrentProcess(), _TOKEN_QUERY, ctypes.byref(tok)):
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            need = wintypes.DWORD(0)
            _ADV.GetTokenInformation(tok, _TOKEN_LOGON_SID, None, 0, ctypes.byref(need))
            if need.value == 0:
                raise ctypes.WinError(ctypes.get_last_error())
            buf = ctypes.create_string_buffer(need.value)
            if not _ADV.GetTokenInformation(tok, _TOKEN_LOGON_SID, buf, need, ctypes.byref(need)):
                raise ctypes.WinError(ctypes.get_last_error())
            if ctypes.c_uint32.from_buffer(buf).value < 1:
                raise OSError("the process token has no logon SID")
            # TOKEN_GROUPS { DWORD GroupCount; SID_AND_ATTRIBUTES Groups[]; }: Groups[0].Sid sits at pointer alignment
            sid_ptr = ctypes.c_void_p.from_buffer(buf, ctypes.sizeof(ctypes.c_void_p)).value
            out = wintypes.LPWSTR()
            if not _ADV.ConvertSidToStringSidW(sid_ptr, ctypes.byref(out)):
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                sid = str(out.value or "")
            finally:
                _K32.LocalFree(out)
        finally:
            _K32.CloseHandle(tok)
        if not sid.startswith("S-1-5-5-"):
            raise OSError(f"unexpected logon SID {sid!r}")
        return sid

    class NativePipeListener:
        """Server end of the run's pipe. ``accept(timeout)`` returns a PipeConnection, or None on timeout / for a
        client that left before it was served. ``sddl`` and ``reject_remote`` exist for the tests' positive controls."""

        def __init__(self, address: str, *, reject_remote: bool = True, sddl: str | None = None) -> None:
            self.address = address
            self._mode = _PIPE_TYPE_MESSAGE | _PIPE_READMODE_MESSAGE | (
                _PIPE_REJECT_REMOTE_CLIENTS if reject_remote else 0)
            sd = ctypes.c_void_p()
            text = sddl or f"D:P(A;;GA;;;{logon_sid()})"
            if not _ADV.ConvertStringSecurityDescriptorToSecurityDescriptorW(text, 1, ctypes.byref(sd), None):
                raise ctypes.WinError(ctypes.get_last_error())
            self._sd = sd
            self._sa = _SA(ctypes.sizeof(_SA), sd, False)
            self._lock = threading.Lock()
            self._ov: Any = None
            self._closed = False
            try:
                self.handle = self._new_handle(first=True)
            except OSError:
                _K32.LocalFree(sd)
                raise

        def _new_handle(self, first: bool = False) -> int:
            flags = _PIPE_ACCESS_DUPLEX | _FILE_FLAG_OVERLAPPED | (_FILE_FLAG_FIRST_PIPE_INSTANCE if first else 0)
            h = _K32.CreateNamedPipeW(self.address, flags, self._mode, PIPE_INSTANCES, 65536, 65536, 0,
                                      ctypes.byref(self._sa))
            if h in (None, _INVALID_HANDLE):
                raise ctypes.WinError(ctypes.get_last_error())
            return int(h)

        def accept(self, timeout: float = ACCEPT_POLL_S) -> Any:
            with self._lock:
                if self._closed:
                    raise OSError("listener closed")
                busy = False
                if self.handle is None:  # all instances were busy after the last accept: try again now
                    try:
                        self.handle = self._new_handle()
                    except OSError as e:
                        if getattr(e, "winerror", None) != _ERROR_PIPE_BUSY:
                            raise
                        busy = True
                if not busy and self._ov is None:
                    try:
                        self._ov = _winapi.ConnectNamedPipe(self.handle, overlapped=True)
                    except OSError as e:
                        if e.winerror != _ERROR_NO_DATA:
                            raise
                        self._recycle()  # the client connected and left already
                        return None
                ov = self._ov
            if busy:
                time.sleep(min(timeout, ACCEPT_POLL_S))
                return None
            res = _winapi.WaitForMultipleObjects([ov.event], False, max(1, int(timeout * 1000)))
            if res == _WAIT_TIMEOUT:
                return None
            with self._lock:
                if self._closed or self._ov is not ov:
                    raise OSError("listener closed")
                self._ov = None
                _n, err = ov.GetOverlappedResult(True)
                if err != 0:
                    self._recycle()
                    return None
                conn = PipeConnection(self.handle)
                self.handle = None  # owned by conn from here: the listener never closes it
                try:
                    self.handle = self._new_handle()
                except OSError as e:  # ERROR_PIPE_BUSY: every instance is taken; the next accept() retries
                    if getattr(e, "winerror", None) != _ERROR_PIPE_BUSY:
                        conn.close()
                        raise
                return conn

        def _recycle(self) -> None:
            h, self.handle = self.handle, None
            try:
                if h is not None:
                    _winapi.CloseHandle(h)
            finally:
                try:
                    self.handle = self._new_handle()
                except OSError as e:
                    if getattr(e, "winerror", None) != _ERROR_PIPE_BUSY:
                        raise

        def close(self) -> None:
            with self._lock:
                if self._closed:
                    return
                self._closed = True
                ov, self._ov = self._ov, None
                if ov is not None:
                    try:
                        ov.cancel()
                        ov.GetOverlappedResult(True)
                    except OSError:
                        pass
                try:
                    if self.handle is not None:
                        _winapi.CloseHandle(self.handle)
                except OSError:
                    pass
                self.handle = None
                if self._sd:
                    _K32.LocalFree(self._sd)
                    self._sd = ctypes.c_void_p()


class Busy(Exception):
    """Too many viewers for this project."""


def _nal_type(nal: bytes) -> int:
    return nal[0] & 0x1F if nal else 0


def _first_slice(nal: bytes) -> bool:
    return len(nal) > 1 and (nal[1] & 0x80) != 0  # first_mb_in_slice == 0 (ue(v) "1")


class _Splitter:
    """Annex-B byte stream -> complete NAL units (without start code). A NAL is complete when the next start code
    arrives, so the last one waits in the buffer. More than MAX_NAL bytes without a start code: reset."""

    def __init__(self) -> None:
        self.buf = bytearray()
        self.scan = 0
        self.synced = False  # False until the first start code: bytes before it are dropped

    def reset(self) -> None:
        self.buf.clear()
        self.scan = 0
        self.synced = False

    def feed(self, data: bytes) -> tuple[list[bytes], bool]:
        """(complete NALs, overflowed). After sync the buffer always starts with the current NAL's start code."""
        self.buf += data
        if not self.synced:
            j = self.buf.find(b"\x00\x00\x01")
            if j < 0:
                del self.buf[:-2]  # a start code may straddle the next chunk
                return [], False
            del self.buf[:j]
            self.synced, self.scan = True, 3
        out: list[bytes] = []
        begin = 3
        i = max(3, self.scan - 2)
        while True:
            j = self.buf.find(b"\x00\x00\x01", i)
            if j < 0:
                break
            nal = bytes(self.buf[begin:j]).rstrip(b"\x00")  # trailing zero_byte of a 4-byte start code
            if nal:
                out.append(nal)
            begin = i = j + 3
        if begin > 3:
            del self.buf[: begin - 3]
        self.scan = len(self.buf)
        if len(self.buf) > MAX_NAL + 3:
            self.reset()
            return out, True
        return out, False


class Subscription:
    """One owner socket's queue. ``offer`` is called by the hub (any thread), ``take`` by the socket's coroutine."""

    def __init__(self, hub: VideoHub, project_id: str, wake: Callable[[], None], max_bytes: int) -> None:
        self.hub, self.project_id, self._wake, self.max_bytes = hub, project_id, wake, max_bytes
        self._q: deque[bytes] = deque()
        self._bytes = 0
        self._prime_bytes = 0  # queued prime bytes: not counted against the live budget (L2)
        self._lock = threading.Lock()
        self.synced = False
        self.dropped = 0
        self.closed = False
        self._seq = 0  # last WS frame seq handed out
        self._gap = False  # whole NALs were lost since the last frame: the next frame seq skips one

    def _append(self, nal: bytes) -> None:
        self._q.append(nal)
        self._bytes += len(nal) + 4

    def _lost(self) -> None:
        self.synced = False
        if self._seq > 0:
            self._gap = True

    def prime(self, sps: bytes | None, pps: bytes | None, gop: list[bytes], prime_max: int = PRIME_MAX) -> None:
        with self._lock:
            if sps and pps and gop:
                nals = (sps, pps, *gop)
                if sum(len(n) + 4 for n in nals) <= prime_max:  # bigger: this viewer waits for the next IDR
                    for n in nals:
                        self._append(n)
                    self._prime_bytes = self._bytes
                    self.synced = True
        self._wake()

    def desync(self) -> None:
        with self._lock:
            if self.synced:
                self._lost()

    def offer(self, nal: bytes, sps: bytes | None, pps: bytes | None) -> None:
        t = _nal_type(nal)
        idr_start = t == 5 and _first_slice(nal)
        with self._lock:
            if self.closed:
                return
            if self.synced and self._bytes - self._prime_bytes + len(nal) + 4 > self.max_bytes:
                self._q.clear()  # video is dropped first: whole NALs, then wait for the next IDR
                self._bytes = self._prime_bytes = 0
                self._lost()
                self.dropped += 1
            if not self.synced:
                if t in (7, 8) or not (idr_start and sps and pps):
                    return  # an unsynced viewer gets SPS + PPS in front of its next IDR
                self._append(sps)
                self._append(pps)
                self.synced = True
            self._append(nal)
        self._wake()

    def _drain(self) -> list[bytes]:
        nals = list(self._q)
        self._q.clear()
        self._bytes = self._prime_bytes = 0
        return nals

    def take(self) -> list[bytes]:
        """Queued NALs (with start codes) packed into frames of at most MAX_FRAME bytes (no seq header)."""
        with self._lock:
            nals = self._drain()
        return _pack(nals, MAX_FRAME)

    def take_frames(self) -> list[bytes]:
        """WS frames: 4-byte big-endian seq (1, 2, ...) + NAL bytes, at most MAX_FRAME bytes in all. Whole NALs lost
        since the last frame skip one seq, so the client sees the discontinuity (M1)."""
        with self._lock:
            out: list[bytes] = []
            for body in _pack(self._drain(), MAX_FRAME - WS_SEQ_HEADER):
                self._seq = (self._seq + (2 if self._gap else 1)) & 0xFFFFFFFF
                self._gap = False
                out.append(self._seq.to_bytes(WS_SEQ_HEADER, "big") + body)
        return out

    def close(self) -> None:
        with self._lock:
            self.closed = True
            self._q.clear()
        self.hub._unsubscribe(self)  # noqa: SLF001


def _pack(nals: list[bytes], size: int) -> list[bytes]:
    frames: list[bytes] = []
    cur = bytearray()
    for n in nals:
        piece = SC + n
        while piece:
            room = size - len(cur)
            cur += piece[:room]
            piece = piece[room:]
            if len(cur) >= size:
                frames.append(bytes(cur))
                cur = bytearray()
    if cur:
        frames.append(bytes(cur))
    return frames


class _Project:
    def __init__(self, max_bps: float, burst: float) -> None:
        self.splitter = _Splitter()
        self.sps: bytes | None = None
        self.pps: bytes | None = None
        self.gop: list[bytes] = []
        self.gop_bytes = 0
        self.rate, self.burst = float(max_bps), float(burst)
        self.tokens = float(burst)
        self.t = time.monotonic()
        self.last_seq: int | None = None  # None: the next chunk starts a new stream (new sender)
        self.want_key = False
        self.key_t = -KEY_REQUEST_MIN_S
        self.stats = {"received": 0, "rateDropped": 0, "nals": 0, "resets": 0, "seqGaps": 0}

    def admit(self, n: int) -> bool:
        now = time.monotonic()
        self.tokens = min(self.burst, self.tokens + (now - self.t) * self.rate)
        self.t = now
        if n > self.tokens:
            return False
        self.tokens -= n
        return True


class _RunPipe:
    def __init__(self, run_id: str, project_id: str, address: str, key: bytes, listener: Listener,
                 tmpdir: str | None) -> None:
        self.run_id, self.project_id, self.address, self.key = run_id, project_id, address, key
        self.listener, self.tmpdir = listener, tmpdir
        self.closed = False
        self.conn: Any = None
        self.pending = 0  # connections still in the handshake
        self.lock = threading.Lock()
        self.stats = {"received": 0, "authFailures": 0, "authTimeouts": 0, "oversize": 0, "malformed": 0,
                      "connections": 0, "busy": 0}


class _Deadline:
    """Connection proxy for the HMAC handshake: every recv waits at most until the deadline (L1).
    ``deliver_challenge`` / ``answer_challenge`` only call send_bytes and recv_bytes."""

    def __init__(self, conn: Any, timeout: float) -> None:
        self.conn, self.end = conn, time.monotonic() + timeout

    def send_bytes(self, data: bytes) -> None:
        self.conn.send_bytes(data)

    def recv_bytes(self, maxlength: int | None = None) -> bytes:
        if not self.conn.poll(max(0.0, self.end - time.monotonic())):
            raise TimeoutError("handshake timeout")
        return bytes(self.conn.recv_bytes(maxlength))


class VideoHub:
    def __init__(self, *, max_bps: float = MAX_BPS, burst: float = BURST, max_subscribers: int = MAX_SUBSCRIBERS,
                 sub_queue_bytes: int = SUB_QUEUE_BYTES, prime_max: int = PRIME_MAX) -> None:
        self.max_bps, self.burst, self.prime_max = max_bps, burst, prime_max
        self.max_subscribers, self.sub_queue_bytes = max_subscribers, sub_queue_bytes
        self._lock = threading.RLock()
        self._runs: dict[str, _RunPipe] = {}
        self._projects: dict[str, _Project] = {}
        self._subs: dict[str, list[Subscription]] = {}

    # ---------------------------------------------------------------- run pipes (backend side)
    def open_run(self, run_id: str, project_id: str) -> dict[str, str]:
        key = secrets.token_bytes(32)
        tmpdir = None
        if sys.platform == "win32":
            address, family = r"\\.\pipe\aas-video-" + secrets.token_hex(16), "AF_PIPE"
        else:
            tmpdir = tempfile.mkdtemp(prefix="aas-video-")  # 0700
            address, family = os.path.join(tmpdir, "s"), "AF_UNIX"
        # no authkey on the listener: each connection's handshake runs in its own thread with a timeout
        listener: Any = NativePipeListener(address) if sys.platform == "win32" else Listener(address, family=family)
        rp = _RunPipe(run_id, project_id, address, key, listener, tmpdir)
        with self._lock:
            old = self._runs.pop(run_id, None)
            self._runs[run_id] = rp
        if old:
            self._close_pipe(old)
        threading.Thread(target=self._accept_loop, args=(rp,), name=f"aas-video-{run_id}", daemon=True).start()
        return {ENV_PIPE: address, ENV_KEY: key.hex()}

    def _accept_loop(self, rp: _RunPipe) -> None:
        """Accepts only. Each connection's HMAC handshake (AUTH_TIMEOUT) and read loop run in a short-lived thread of
        its own, so a client that connects and never answers cannot block the next sender (L1)."""
        while not rp.closed:
            try:
                conn = rp.listener.accept()
            except (OSError, EOFError):
                if rp.closed:
                    return
                time.sleep(0.2)
                continue
            if conn is None:  # native pipe: accept timed out (ACCEPT_POLL_S); look at rp.closed again
                continue
            if rp.closed:
                conn.close()
                return
            with rp.lock:
                full = rp.pending >= MAX_PENDING_AUTH
                if not full:
                    rp.pending += 1
            if full:
                rp.stats["busy"] += 1
                conn.close()
                time.sleep(0.05)
                continue
            threading.Thread(target=self._serve, args=(rp, conn), name=f"aas-video-conn-{rp.run_id}",
                             daemon=True).start()

    def _serve(self, rp: _RunPipe, conn: Any) -> None:
        authed = False
        try:
            try:
                guarded = _Deadline(conn, AUTH_TIMEOUT)
                deliver_challenge(guarded, rp.key)  # type: ignore[arg-type]
                answer_challenge(guarded, rp.key)  # type: ignore[arg-type]
                authed = True
            except TimeoutError:
                rp.stats["authTimeouts"] += 1
            except AuthenticationError:
                rp.stats["authFailures"] += 1
                time.sleep(0.2)  # keeps the pending slot: wrong-key retries are throttled
            except (OSError, EOFError):
                pass
            finally:
                with rp.lock:
                    rp.pending -= 1
                    if authed and (rp.closed or rp.conn is not None):
                        authed = False  # one sender at a time
                        rp.stats["busy"] += 1
                    if authed:
                        rp.conn = conn
            if not authed:
                return
            rp.stats["connections"] += 1
            try:
                self._read_loop(rp, conn)
            finally:
                with rp.lock:
                    rp.conn = None
        finally:
            try:
                conn.close()
            except OSError:
                pass

    def _read_loop(self, rp: _RunPipe, conn: Any) -> None:
        self._new_stream(rp.project_id)
        while not rp.closed:
            try:
                if not conn.poll(0.5):
                    continue
                msg = conn.recv_bytes(MAX_CHUNK + SEQ_HEADER)
            except OSError as e:
                if "length" in str(e).lower() or "too long" in str(e).lower():
                    rp.stats["oversize"] += 1
                return
            except EOFError:
                return
            if len(msg) < SEQ_HEADER:
                rp.stats["malformed"] += 1
                return
            rp.stats["received"] += 1
            self.feed(rp.project_id, msg[SEQ_HEADER:], seq=int.from_bytes(msg[:SEQ_HEADER], "big"))
            if self.take_key_request(rp.project_id):
                try:
                    conn.send_bytes(KEY_REQUEST)
                except OSError:
                    return

    def close_run(self, run_id: str) -> None:
        with self._lock:
            rp = self._runs.pop(run_id, None)
        if rp:
            self._close_pipe(rp)

    def _close_pipe(self, rp: _RunPipe) -> None:
        rp.closed = True  # a sender's read loop sees this within 0.5 s, a pending handshake ends within AUTH_TIMEOUT
        # (the native pipe's accept returns within ACCEPT_POLL_S: the old wake-up client is not needed any more)
        try:
            rp.listener.close()
        except OSError:
            pass
        if rp.tmpdir:
            shutil.rmtree(rp.tmpdir, ignore_errors=True)
        self._end_stream(rp.project_id)

    def stats(self, run_id: str) -> dict[str, int]:
        with self._lock:
            rp = self._runs.get(run_id)
        return dict(rp.stats) if rp else {}

    def stats_project(self, project_id: str) -> dict[str, int]:
        with self._lock:
            p = self._projects.get(project_id)
            return dict(p.stats) if p else {}

    def close(self) -> None:
        with self._lock:
            runs = list(self._runs)
        for r in runs:
            self.close_run(r)

    # ---------------------------------------------------------------- stream (any thread)
    def _project(self, project_id: str) -> _Project:
        p = self._projects.get(project_id)
        if p is None:
            p = self._projects[project_id] = _Project(self.max_bps, self.burst)
        return p

    def _new_stream(self, project_id: str) -> None:
        """A new sender: its bytes are never stitched to the previous sender's."""
        with self._lock:
            p = self._project(project_id)
            p.last_seq = None
            p.splitter.reset()
            p.gop, p.gop_bytes = [], 0

    def _end_stream(self, project_id: str) -> None:
        """Run end (L3): the project's cached SPS/PPS/GOP is dropped and its viewers desync; their sockets stay open
        and idle, so a viewer after the run sees nothing stale and the next run resumes with SPS + PPS + IDR."""
        with self._lock:
            p = self._projects.pop(project_id, None)
            subs = list(self._subs.get(project_id, ()))
        if p is not None:
            for s in subs:
                s.desync()

    def take_key_request(self, project_id: str) -> bool:
        """True at most once per KEY_REQUEST_MIN_S after a discontinuity (seq gap, rate drop, splitter reset)."""
        with self._lock:
            p = self._projects.get(project_id)
            now = time.monotonic()
            if p is None or not p.want_key or now - p.key_t < KEY_REQUEST_MIN_S:
                return False
            p.want_key, p.key_t = False, now
            return True

    def feed(self, project_id: str, data: bytes, seq: int | None = None) -> None:
        """One chunk of the Annex-B byte stream. ``seq`` (pipe chunks): anything but last + 1 is a discontinuity."""
        with self._lock:
            p = self._project(project_id)
            p.stats["received"] += 1
            subs = list(self._subs.get(project_id, ()))
            if seq is not None:
                gap = p.last_seq is not None and seq != p.last_seq + 1
                p.last_seq = seq
                if gap:
                    p.stats["seqGaps"] += 1
                    self._discontinuity(p, subs)
            if len(data) > MAX_CHUNK or not p.admit(len(data)):
                p.stats["rateDropped"] += 1
                self._discontinuity(p, subs)
                return
            nals, overflow = p.splitter.feed(data)
            if overflow:
                p.stats["resets"] += 1
            for nal in nals:
                self._nal(p, nal, subs)
            if overflow:
                self._discontinuity(p, subs)

    @staticmethod
    def _discontinuity(p: _Project, subs: list[Subscription]) -> None:
        p.splitter.reset()
        p.gop, p.gop_bytes = [], 0
        p.want_key = True
        for s in subs:
            s.desync()

    def _nal(self, p: _Project, nal: bytes, subs: list[Subscription]) -> None:
        p.stats["nals"] += 1
        t = _nal_type(nal)
        if t == 7:
            p.sps, p.gop, p.gop_bytes = nal, [], 0
        elif t == 8:
            p.pps = nal
        elif t == 5 and _first_slice(nal):
            p.gop, p.gop_bytes = [nal], len(nal)
        elif p.gop:
            if p.gop_bytes + len(nal) > GOP_MAX_BYTES:
                p.gop, p.gop_bytes = [], 0
            else:
                p.gop.append(nal)
                p.gop_bytes += len(nal)
        for s in subs:  # synced viewers get every NAL incl. a new SPS/PPS; a resync adds SPS + PPS before the IDR
            s.offer(nal, p.sps, p.pps)

    # ---------------------------------------------------------------- viewers (backend WS)
    def subscribe(self, project_id: str, wake: Callable[[], None]) -> Subscription:
        with self._lock:
            subs = self._subs.setdefault(project_id, [])
            if len(subs) >= self.max_subscribers:
                raise Busy("too many mirror viewers for this project")
            s = Subscription(self, project_id, wake, self.sub_queue_bytes)
            subs.append(s)
            p = self._projects.get(project_id)
            if p is not None:
                s.prime(p.sps, p.pps, list(p.gop), self.prime_max)
        return s

    def _unsubscribe(self, s: Subscription) -> None:
        with self._lock:
            subs = self._subs.get(s.project_id, [])
            if s in subs:
                subs.remove(s)


# -------------------------------------------------------------------- MCP child side
class VideoSender:
    """Starts the device's screenrecord stream and forwards its chunks, each with a pipe seq, to the run's pipe. A
    hole (host seq jumped, launcher drop mark) skips one pipe seq so the hub resyncs; the hub's KEY_REQUEST is passed
    to ``session.video_keyframe`` when the session has it. Any error stops the sender (the mirror is optional; the
    visible emulator window stays the primary view)."""

    def __init__(self, session: Any, address: str, key_hex: str, size: str = "540x1170") -> None:
        self.session, self.address, self.size = session, address, size
        self.key = bytes.fromhex(key_hex)
        self._stop = threading.Event()
        self.sent = 0
        self.gaps = 0
        self.key_requests = 0
        self.error = ""
        self._key_t = -KEY_REQUEST_MIN_S
        self._thread = threading.Thread(target=self._run, name="aas-video-send", daemon=True)

    @classmethod
    def from_env(cls, session: Any) -> VideoSender | None:
        address, key = os.environ.get(ENV_PIPE, ""), os.environ.get(ENV_KEY, "")
        if not address or len(key) != 64:
            return None
        try:
            bytes.fromhex(key)
        except ValueError:
            return None
        return cls(session, address, key)

    def start(self) -> VideoSender:
        self._thread.start()
        return self

    def _key_requests(self, conn: Any) -> None:
        while conn.poll(0):
            if conn.recv_bytes(16) != KEY_REQUEST:
                continue
            self.key_requests += 1
            fn = getattr(self.session, "video_keyframe", None)
            now = time.monotonic()
            if callable(fn) and now - self._key_t >= KEY_REQUEST_MIN_S:
                self._key_t = now
                try:
                    fn()
                except Exception:  # noqa: BLE001 - best effort: the next IDR resyncs anyway
                    pass

    def _run(self) -> None:
        conn = None
        try:
            family = "AF_PIPE" if sys.platform == "win32" else "AF_UNIX"
            conn = Client(self.address, family=family, authkey=self.key)
            rep = self.session.video_start(self.size)
            if isinstance(rep, dict) and rep.get("ok") is False:
                self.error = "video_start refused"
                return
            q = self.session.video_q
            seq = 0
            host_last: int | None = None
            while not self._stop.is_set():
                self._key_requests(conn)
                try:
                    o = q.get(timeout=0.5)
                except Exception:  # noqa: BLE001 - queue.Empty
                    continue
                hs = o.get("seq")
                hs = hs if type(hs) is int else None
                gap = o.get("gap") is True or (hs is not None and host_last is not None and hs != host_last + 1)
                if hs is not None:
                    host_last = hs
                data = base64.b64decode(o.get("b64", ""), validate=True)
                if not 0 < len(data) <= MAX_CHUNK:
                    seq += 1  # nothing sent for this chunk: the next one follows a hole
                    continue
                if gap:
                    self.gaps += 1
                    seq += 1
                seq += 1
                conn.send_bytes(seq.to_bytes(SEQ_HEADER, "big") + data)
                self.sent += 1
        except Exception as e:  # noqa: BLE001 - pipe gone, auth failed, device gone: the mirror just stops
            self.error = type(e).__name__
        finally:
            if conn is not None:
                try:
                    conn.close()
                except OSError:
                    pass

    def stop(self) -> None:
        self._stop.set()
        self._thread.join(timeout=3)


__all__ = ["NativePipeListener", "logon_sid", "VideoHub", "VideoSender", "Subscription", "Busy", "MAX_CHUNK", "MAX_FRAME", "MAX_NAL", "PRIME_MAX",
           "ENV_PIPE", "ENV_KEY", "KEY_REQUEST", "AUTH_TIMEOUT"]
