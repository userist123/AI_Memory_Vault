"""Per-project preview origin server (DESIGN 4; probes P3d, P3e, P7b, PM5b).

One listener on 127.0.0.1:17880. Project `<id16>` is served at `http://p-<id16>.localhost:17880/`; the project comes
from the Host header only (never the path), and Host must equal exactly that string. Every response, including errors,
carries the full security header set (CSP as an HTTP header, COOP, CORP, X-Frame-Options, Permissions-Policy ...).
GET/HEAD only, no directory listing, no dotfiles, no symlink / junction / hardlink following.
"""
from __future__ import annotations

import os
import re
import socket
import stat
import sys
import threading
from collections.abc import Callable, Iterable
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote

BIND_HOST = "127.0.0.1"
DEFAULT_PORT = 17880
MAX_SERVE = 50 * 1024 * 1024
IDLE_TIMEOUT_S = 10.0
MAX_CONNECTIONS = 32

CSP = (
    "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
    "font-src 'self'; media-src 'self' blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; "
    "frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; "
    "frame-ancestors http://tauri.localhost tauri://localhost"
)
PERMISSIONS_POLICY = "camera=(), microphone=(), geolocation=(), display-capture=(), usb=(), serial=(), hid=(), bluetooth=(), payment=()"
SECURITY_HEADERS: tuple[tuple[str, str], ...] = (
    ("Content-Security-Policy", CSP),
    ("Permissions-Policy", PERMISSIONS_POLICY),
    ("Cross-Origin-Opener-Policy", "same-origin"),
    ("Cross-Origin-Resource-Policy", "same-origin"),
    # Chromium ignores X-Frame-Options when CSP frame-ancestors is present; kept for engines that only know XFO.
    ("X-Frame-Options", "SAMEORIGIN"),
    ("X-Content-Type-Options", "nosniff"),
    ("Referrer-Policy", "no-referrer"),
    ("Cache-Control", "no-store"),
)

_HOST_RE = re.compile(r"^p-([0-9a-f]{16})\.localhost:([0-9]{1,5})$")
_DEV_ANCESTOR_RE = re.compile(r"^http://(?:127\.0\.0\.1|localhost):[0-9]{4,5}$")
_SEGMENT_RE = re.compile(r"^[A-Za-z0-9_][A-Za-z0-9._@+-]{0,100}$")
_REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
_MIME = {
    "html": "text/html; charset=utf-8", "js": "text/javascript; charset=utf-8", "css": "text/css; charset=utf-8",
    "json": "application/json; charset=utf-8", "map": "application/json; charset=utf-8", "txt": "text/plain; charset=utf-8",
    "svg": "image/svg+xml", "png": "image/png", "jpg": "image/jpeg", "jpeg": "image/jpeg", "webp": "image/webp",
    "ico": "image/x-icon", "woff2": "font/woff2",
}


def _link_like(st: os.stat_result) -> bool:
    return stat.S_ISLNK(st.st_mode) or bool(getattr(st, "st_file_attributes", 0) & _REPARSE)


def _safe_file(root: Path, rel_segments: list[str]) -> Path | None:
    """Walk component by component; None when anything is a link/reparse point, missing, or not a lone regular file."""
    root_s = os.path.normpath(os.path.abspath(root))
    cur = Path(root_s)
    try:
        if _link_like(os.lstat(root_s)):
            return None
        for seg in rel_segments:
            nxt = os.path.normpath(os.path.join(str(cur), seg))
            if not nxt.startswith(root_s + os.sep):  # defence in depth over _SEGMENT_RE: never leave the site root
                return None
            cur = Path(nxt)
            st = os.lstat(nxt)
            if _link_like(st):
                return None
        if not stat.S_ISREG(st.st_mode) or st.st_nlink > 1 or st.st_size > MAX_SERVE:
            return None
    except OSError:
        return None
    return cur


class _BoundedServer(ThreadingHTTPServer):
    """ThreadingHTTPServer with a cap on concurrent connections; extra connections are closed immediately."""

    daemon_threads = True
    allow_reuse_address = False  # S9: SO_REUSEADDR on Windows lets a second socket share (hijack) the port

    def __init__(self, addr, handler, max_connections: int) -> None:  # type: ignore[no-untyped-def]
        super().__init__(addr, handler)
        self._slots = threading.BoundedSemaphore(max_connections)

    def handle_error(self, request, client_address) -> None:  # type: ignore[no-untyped-def]
        # a client that hangs up mid-response (the WebView reloading a frame) is normal, not an error worth a traceback
        if isinstance(sys.exc_info()[1], (ConnectionAbortedError, ConnectionResetError, BrokenPipeError)):
            return
        super().handle_error(request, client_address)

    def server_bind(self) -> None:
        if os.name == "nt":
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()

    def process_request(self, request, client_address) -> None:  # type: ignore[no-untyped-def]
        if not self._slots.acquire(blocking=False):
            self.shutdown_request(request)
            return
        super().process_request(request, client_address)

    def process_request_thread(self, request, client_address) -> None:  # type: ignore[no-untyped-def]
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._slots.release()


class PreviewServer:
    def __init__(self, site_root_for: Callable[[str], Path | None], host: str = BIND_HOST, port: int = DEFAULT_PORT,
                 idle_timeout: float = IDLE_TIMEOUT_S, max_connections: int = MAX_CONNECTIONS,
                 extra_frame_ancestors: Iterable[str] = ()) -> None:
        if host != BIND_HOST:
            raise ValueError("preview server binds to 127.0.0.1 only")
        extra = list(extra_frame_ancestors)
        for o in extra:  # S6: exact loopback UI origins of the vite dev/preview servers only (dev + tests)
            if not _DEV_ANCESTOR_RE.fullmatch(o):
                raise ValueError(f"frame ancestor {o!r} is not http://127.0.0.1:<port> or http://localhost:<port>")
        self._headers = tuple(
            (k, v + "".join(f" {o}" for o in extra) if k == "Content-Security-Policy" else v) for k, v in SECURITY_HEADERS)
        self._site_root_for = site_root_for
        self._idle_timeout = idle_timeout
        self.httpd = _BoundedServer((host, port), self._make_handler(), max_connections)
        self.host, self.port = str(self.httpd.server_address[0]), int(self.httpd.server_address[1])
        self._thread: threading.Thread | None = None

    def origin_for(self, project_id16: str) -> str:
        return f"http://p-{project_id16}.localhost:{self.port}"

    def start(self) -> PreviewServer:
        if self._thread is None:
            self._thread = threading.Thread(target=self.httpd.serve_forever, name="aas-preview", daemon=True)
            self._thread.start()
        return self

    def stop(self) -> None:
        self.httpd.shutdown()
        self.httpd.server_close()
        self._thread = None

    def _make_handler(self) -> type[BaseHTTPRequestHandler]:
        server = self

        class Handler(BaseHTTPRequestHandler):
            server_version = "AASPreview/1"
            sys_version = ""
            protocol_version = "HTTP/1.1"
            # http.server answers a malformed request line in HTTP/0.9 style (no status line, no headers); answering as
            # HTTP/1.1 keeps the security headers on every error. HTTP/0.9 requests are refused in parse_request.
            default_request_version = "HTTP/1.1"
            timeout = server._idle_timeout  # per-connection socket timeout: slow/idle clients are dropped

            def log_message(self, *_a: object) -> None:
                return

            def _send(self, code: int, body: bytes = b"", ctype: str = "text/plain; charset=utf-8") -> None:
                self.send_response(code)
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Length", str(len(body)))
                for k, v in server._headers:
                    self.send_header(k, v)
                self.send_header("Connection", "close")
                self.end_headers()
                self.close_connection = True
                if self.command != "HEAD" and code not in (204, 304):
                    self.wfile.write(body)

            def send_error(self, code: int, message: str | None = None, explain: str | None = None) -> None:  # type: ignore[override]
                # generic body: never reflect the (attacker-controlled) request line back
                self._send(code, b"bad request" if code == 400 else b"error")

            def parse_request(self) -> bool:
                if not super().parse_request():
                    return False
                if len(self.requestline.split()) != 3:  # HTTP/0.9 simple request ("GET /")
                    self.send_error(400)
                    return False
                return True

            def do_HEAD(self) -> None:  # noqa: N802
                self.do_GET()

            def do_GET(self) -> None:  # noqa: N802
                if len(self.headers.get_all("Host") or []) > 1:
                    return self._send(400, b"bad request")  # duplicate Host header
                m = _HOST_RE.fullmatch(self.headers.get("Host", ""))
                if not m or int(m.group(2)) != server.port:
                    return self._send(421, b"misdirected request")  # DNS-rebinding / wrong-origin guard
                root = server._site_root_for(m.group(1))
                if root is None:
                    return self._send(404, b"not found")
                target = self.path.split("?", 1)[0].split("#", 1)[0]
                if not target.startswith("/") or "%00" in target.lower() or not target.isascii():
                    return self._send(400, b"bad request")
                try:
                    path = unquote(target, errors="strict")
                except UnicodeDecodeError:
                    return self._send(400, b"bad request")  # invalid UTF-8 percent-encoding (/%ff, /%c0%af)
                if path == "/":
                    segments = ["index.html"]
                else:
                    segments = path[1:].split("/")
                    if not path or "\\" in path or ":" in path or "\0" in path or any(not _SEGMENT_RE.fullmatch(s) or s.endswith(".") for s in segments):
                        return self._send(404, b"not found")  # empty segment, dotfile, '..', ADS, backslash, drive letter
                f = _safe_file(Path(root), segments)
                if f is None:
                    return self._send(404, b"not found")
                ext = f.name.rsplit(".", 1)[-1].casefold() if "." in f.name else ""
                ctype = _MIME.get(ext)
                if ctype is None:
                    return self._send(404, b"not found")
                try:
                    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
                    fd = os.open(f, flags)
                    try:
                        st = os.fstat(fd)
                        if not stat.S_ISREG(st.st_mode) or st.st_nlink > 1:
                            return self._send(404, b"not found")
                        with os.fdopen(fd, "rb", closefd=False) as fh:
                            data = fh.read(MAX_SERVE + 1)
                    finally:
                        os.close(fd)
                except OSError:
                    return self._send(404, b"not found")
                self._send(200, data, ctype)

            def _deny(self) -> None:
                self._send(405, b"read-only")

            do_POST = do_PUT = do_DELETE = do_PATCH = do_OPTIONS = _deny  # noqa: N815

        return Handler
