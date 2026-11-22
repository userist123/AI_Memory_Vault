"""On-demand front for the local reranker server (stdlib only, loopback only).

The cross-encoder process (``reranker_server.py``: torch + CUDA) holds ~6 GB of
commit memory while it runs. This launcher is what listens on the reranker port
instead. It costs a few MB, starts the model process only when a ``/rerank``
request arrives, forwards requests to it, and stops it after ``--idle-minutes``
without a request. MemoryController's ``HttpReranker`` needs no change of URL.

While the model process is starting (cold start: 5-70 s, measured 70 s on
2026-11-23 under memory pressure) ``/rerank`` answers ``503 {"error": "warming"}``
at once; the client keeps the production order for that search and retries a few
seconds later, so no search waits for the model to load.

API (same as the model server, plus a state field):
    POST /rerank {"query": str, "documents": [str, ...]} -> forwarded
    GET  /health -> {"ok": true, "state": "idle|warming|ready", "model": str|null}
    POST /warm   -> starts the model process now (state as above)

Safety, same rules as the model server: binds 127.0.0.1 only; answers only a
loopback Host header (DNS rebinding); POST needs ``Content-Type:
application/json`` (a web page cannot send that without a CORS preflight, which
is not answered); body <= 4 MB. The model process is started with ``--port 0``
and the launcher forwards only to the port that its own child reports on its
stdout, so another program squatting a fixed port can never receive note text.
"""
from __future__ import annotations

import argparse
import json
import os
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import List, Optional

MAX_BODY = 4 * 1024 * 1024
DEFAULT_IDLE_MINUTES = 15.0
#: Give up on a child that has not reported its port after this many seconds.
START_TIMEOUT_SECONDS = 240.0
FORWARD_TIMEOUT_SECONDS = 30.0

#: Never send note text through a proxy, never follow a redirect.
class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: D401
        raise urllib.error.HTTPError(req.full_url, code, "redirects are refused", headers, fp)


_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}), _NoRedirect())


def parse_listening(line: str) -> Optional[int]:
    """Port from the model server's ``{"listening": "http://127.0.0.1:<port>"}`` line, loopback only."""
    try:
        obj = json.loads(line)
    except (ValueError, TypeError):
        return None
    url = obj.get("listening") if isinstance(obj, dict) else None
    if not isinstance(url, str) or not url.startswith("http://127.0.0.1:"):
        return None
    try:
        port = int(url.rsplit(":", 1)[1].rstrip("/"))
    except ValueError:
        return None
    return port if 0 < port < 65536 else None


class Worker:
    """Owns at most one model process; thread-safe."""

    def __init__(self, command: List[str], idle_seconds: float, log_path: Optional[Path] = None,
                 env: Optional[dict] = None, clock=time.monotonic):
        self.command = list(command)
        self.idle_seconds = float(idle_seconds)
        self.log_path = log_path
        self.env = env
        self._clock = clock
        self._lock = threading.Lock()
        self._proc: Optional[subprocess.Popen] = None
        self._port: Optional[int] = None
        self._model: Optional[str] = None
        self._last_use = clock()
        self._started_at = 0.0

    # -- state -----------------------------------------------------------
    def state(self) -> str:
        with self._lock:
            return self._state_locked()

    def _state_locked(self) -> str:
        if self._proc is None:
            return "idle"
        if self._proc.poll() is not None:  # child died
            self._proc, self._port, self._model = None, None, None
            return "idle"
        return "ready" if self._port else "warming"

    def snapshot(self) -> dict:
        with self._lock:
            return {"state": self._state_locked(), "model": self._model}

    # -- lifecycle -------------------------------------------------------
    def ensure_started(self) -> str:
        with self._lock:
            self._last_use = self._clock()
            st = self._state_locked()
            if st != "idle":
                return st
            log = open(self.log_path, "ab") if self.log_path else subprocess.DEVNULL
            flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            self._proc = subprocess.Popen(self.command, stdout=subprocess.PIPE, stderr=log,
                                          stdin=subprocess.DEVNULL, env=self.env, creationflags=flags)
            self._started_at = self._clock()
            threading.Thread(target=self._read_stdout, args=(self._proc, log), daemon=True).start()
            return "warming"

    def _read_stdout(self, proc: subprocess.Popen, log) -> None:
        for raw in iter(proc.stdout.readline, b""):
            line = raw.decode("utf-8", "replace").strip()
            if hasattr(log, "write"):
                try:
                    log.write(raw)
                    log.flush()
                except (OSError, ValueError):
                    pass
            port = parse_listening(line)
            if port is not None:
                with self._lock:
                    if self._proc is proc and self._port is None:
                        self._port = port
                        try:
                            self._model = str(json.loads(line).get("model") or "") or None
                        except ValueError:
                            self._model = None

    def stop(self) -> None:
        with self._lock:
            proc, self._proc, self._port, self._model = self._proc, None, None, None
        if proc is not None and proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()

    def reap(self) -> bool:
        """Stop the child if idle too long or stuck starting; True if it was stopped."""
        with self._lock:
            st = self._state_locked()
            now = self._clock()
            idle = st == "ready" and now - self._last_use >= self.idle_seconds
            stuck = st == "warming" and now - self._started_at >= START_TIMEOUT_SECONDS
        if idle or stuck:
            self.stop()
            return True
        return False

    def port(self) -> Optional[int]:
        with self._lock:
            return self._port if self._state_locked() == "ready" else None

    def touch(self) -> None:
        with self._lock:
            self._last_use = self._clock()


def build_handler(worker: Worker):
    class Handler(BaseHTTPRequestHandler):
        def _send(self, code: int, payload: dict) -> None:
            self._send_raw(code, json.dumps(payload).encode("utf-8"))

        def _send_raw(self, code: int, body: bytes) -> None:
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, fmt, *args):  # no query text in logs
            return

        def _host_ok(self) -> bool:
            host = (self.headers.get("Host") or "").rsplit(":", 1)[0].strip("[]").lower()
            return host in ("127.0.0.1", "localhost", "::1")

        def _json_ok(self) -> bool:
            ctype = (self.headers.get("Content-Type") or "").split(";", 1)[0].strip().lower()
            return ctype == "application/json"

        def do_GET(self):
            if not self._host_ok():
                self._send(403, {"error": "host"})
                return
            if self.path == "/health":
                self._send(200, {"ok": True, **worker.snapshot()})
            else:
                self._send(404, {"error": "not_found"})

        def do_POST(self):
            try:
                length = int(self.headers.get("Content-Length") or 0)
            except ValueError:
                length = -1
            if length < 0 or length > MAX_BODY:
                self.close_connection = True
                self._send(413, {"error": "body_size"})
                return
            # Read the (bounded) body before any refusal: answering with an unread
            # body makes Windows reset the connection before the client sees the code.
            body = self.rfile.read(length) if length else b""
            if not self._host_ok():
                self._send(403, {"error": "host"})
                return
            if not self._json_ok():
                self._send(415, {"error": "content_type"})
                return
            if self.path == "/warm":
                self._send(200, {"ok": True, "state": worker.ensure_started()})
                return
            if self.path != "/rerank":
                self._send(404, {"error": "not_found"})
                return
            if length == 0:
                self._send(413, {"error": "body_size"})
                return
            if worker.ensure_started() != "ready":
                self._send(503, {"error": "warming"})
                return
            port = worker.port()
            if port is None:
                self._send(503, {"error": "warming"})
                return
            req = urllib.request.Request(f"http://127.0.0.1:{port}/rerank", data=body, method="POST",
                                         headers={"Content-Type": "application/json"})
            try:
                with _OPENER.open(req, timeout=FORWARD_TIMEOUT_SECONDS) as resp:
                    payload = resp.read()
                    code = resp.status
            except urllib.error.HTTPError as exc:
                payload, code = exc.read() or b'{"error": "upstream"}', exc.code
            except Exception:  # noqa: BLE001 - the client fails closed on any non-200
                self._send(502, {"error": "upstream_unreachable"})
                return
            finally:
                worker.touch()
            self._send_raw(code, payload)

    return Handler


class ExclusiveServer(ThreadingHTTPServer):
    """Refuses a port another process already holds (see reranker_server.py)."""
    allow_reuse_address = False
    daemon_threads = True

    def server_bind(self):
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()


def reaper_loop(worker: Worker, stop: threading.Event, every: float = 15.0) -> None:
    while not stop.wait(every):
        worker.reap()


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--port", type=int, default=18765)
    ap.add_argument("--idle-minutes", type=float,
                    default=float(os.getenv("AI_MEMORY_VAULT_RERANKER_IDLE_MINUTES", DEFAULT_IDLE_MINUTES)))
    ap.add_argument("--python", default=sys.executable, help="interpreter of the torch environment")
    ap.add_argument("--server", default=str(Path(__file__).with_name("reranker_server.py")))
    ap.add_argument("--log", default=None, help="append the model process output here")
    ap.add_argument("server_args", nargs=argparse.REMAINDER,
                    help="after --, extra arguments for reranker_server.py (e.g. -- --fp16)")
    args = ap.parse_args(argv)
    if args.idle_minutes <= 0:
        ap.error("--idle-minutes must be > 0")
    extra = [a for a in args.server_args if a != "--"]
    command = [args.python, args.server, "--port", "0", *extra]
    worker = Worker(command, idle_seconds=args.idle_minutes * 60.0,
                    log_path=Path(args.log) if args.log else None)
    server = ExclusiveServer(("127.0.0.1", args.port), build_handler(worker))
    stop = threading.Event()
    threading.Thread(target=reaper_loop, args=(worker, stop), daemon=True).start()
    print(json.dumps({"launcher": f"http://127.0.0.1:{args.port}", "idle_minutes": args.idle_minutes}), flush=True)
    try:
        server.serve_forever()
    finally:
        stop.set()
        worker.stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
