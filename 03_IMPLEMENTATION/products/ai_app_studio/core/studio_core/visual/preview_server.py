"""Preview server: a separate loopback origin that serves rendered sites read-only.

It is deliberately NOT the API server: no token, no cookies, no API routes, so JavaScript in a
generated (possibly untrusted) site cannot reach the application API through same-origin.
URL layout: http://127.0.0.1:<port>/p/<projectId>/<path>
"""

from __future__ import annotations

import mimetypes
import re
import threading
from collections.abc import Callable
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlparse

from ..errors import StudioError
from ..workspace import Workspace

_PATH_RE = re.compile(r"^/p/(prj_[0-9a-f]{16})(/.*)?$")
MAX_SERVE = 50 * 1024 * 1024


class PreviewServer:
    def __init__(self, workspace_for: Callable[[str], Workspace], host: str = "127.0.0.1", port: int = 0) -> None:
        self._workspace_for = workspace_for
        handler = self._make_handler()
        self.httpd = ThreadingHTTPServer((host, port), handler)
        self.httpd.daemon_threads = True
        self.host, self.port = str(self.httpd.server_address[0]), int(self.httpd.server_address[1])
        self._thread: threading.Thread | None = None

    @property
    def base_url(self) -> str:
        return f"http://{self.host}:{self.port}"

    def url_for(self, project_id: str, path: str = "index.html") -> str:
        return f"{self.base_url}/p/{project_id}/{path}"

    def start(self) -> PreviewServer:
        if self._thread is None:
            self._thread = threading.Thread(target=self.httpd.serve_forever, name="aws-preview", daemon=True)
            self._thread.start()
        return self

    def stop(self) -> None:
        self.httpd.shutdown()
        self.httpd.server_close()
        self._thread = None

    def _make_handler(self) -> type[BaseHTTPRequestHandler]:
        server = self

        class Handler(BaseHTTPRequestHandler):
            server_version = "AWSPreview/1"
            sys_version = ""

            def log_message(self, *_a: object) -> None:  # quiet
                return

            def _send(self, code: int, body: bytes, ctype: str = "text/plain; charset=utf-8") -> None:
                self.send_response(code)
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Length", str(len(body)))
                self.send_header("X-Content-Type-Options", "nosniff")
                self.send_header("Cache-Control", "no-store")
                self.send_header("Referrer-Policy", "no-referrer")
                self.send_header("Cross-Origin-Resource-Policy", "same-origin")
                if ctype.startswith("image/svg"):
                    self.send_header("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; script-src 'none'")
                self.end_headers()
                if self.command != "HEAD":
                    self.wfile.write(body)

            def do_HEAD(self) -> None:  # noqa: N802
                self.do_GET()

            def do_GET(self) -> None:  # noqa: N802
                host = self.headers.get("Host", "")
                if host not in (f"127.0.0.1:{server.port}", f"localhost:{server.port}"):
                    return self._send(421, b"misdirected request")  # DNS-rebinding guard
                path = unquote(urlparse(self.path).path)
                m = _PATH_RE.match(path)
                if not m:
                    return self._send(404, b"not found")
                rel = (m.group(2) or "/").lstrip("/") or "index.html"
                if rel.endswith("/"):
                    rel += "index.html"
                if rel.startswith(".") or "/." in rel:
                    return self._send(404, b"not found")
                try:
                    ws = server._workspace_for(m.group(1))
                    f: Path = ws.resolve("site/" + rel)
                    if not f.is_file():
                        return self._send(404, b"not found")
                    data = ws.read_bytes("site/" + rel, max_bytes=MAX_SERVE)
                except StudioError as e:
                    return self._send(404 if e.code == "workspace_violation" and "does not exist" in e.message else 403, b"forbidden")
                ctype = mimetypes.guess_type(f.name)[0] or "application/octet-stream"
                if ctype.startswith("text/") or ctype in ("application/javascript", "text/javascript"):
                    ctype += "; charset=utf-8"
                self._send(200, data, ctype)

            def do_POST(self) -> None:  # noqa: N802
                self._send(405, b"read-only")

            do_PUT = do_DELETE = do_PATCH = do_POST  # noqa: N815

        return Handler
