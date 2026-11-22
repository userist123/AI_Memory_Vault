"""On-demand reranker launcher: starts the model process on the first /rerank,
answers 503 warming meanwhile, forwards once ready, stops it when idle.

The model process is replaced by a stdlib fake that speaks the same protocol
(prints the ``listening`` line for ``--port 0`` and scores by document length),
so no torch is needed.
"""
from __future__ import annotations

import http.client
import importlib.util
import json
import sys
import textwrap
import threading
import time
from pathlib import Path

import pytest

from retrieval.reranker import HttpReranker, RerankerError

ROOT = Path(__file__).resolve().parents[1]


def _load_launcher():
    path = ROOT / "30_SCRIPTS" / "retrieval" / "reranker_launcher.py"
    spec = importlib.util.spec_from_file_location("reranker_launcher_under_test", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


launcher = _load_launcher()

FAKE_SERVER = textwrap.dedent('''
    import json, sys, time
    from http.server import BaseHTTPRequestHandler, HTTPServer
    delay = float(sys.argv[sys.argv.index("--delay") + 1]) if "--delay" in sys.argv else 0.0
    time.sleep(delay)
    class H(BaseHTTPRequestHandler):
        def log_message(self, *a): pass
        def do_POST(self):
            req = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            body = json.dumps({"scores": [float(len(d)) for d in req["documents"]], "model": "fake"}).encode()
            self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers()
            self.wfile.write(body)
    srv = HTTPServer(("127.0.0.1", int(sys.argv[sys.argv.index("--port") + 1])), H)
    print(json.dumps({"listening": "http://127.0.0.1:%d" % srv.server_address[1], "model": "fake"}), flush=True)
    srv.serve_forever()
''')


@pytest.fixture
def running(tmp_path):
    script = tmp_path / "fake_server.py"
    script.write_text(FAKE_SERVER, encoding="utf-8")
    worker = launcher.Worker([sys.executable, str(script), "--port", "0"], idle_seconds=3600)
    server = launcher.ExclusiveServer(("127.0.0.1", 0), launcher.build_handler(worker))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    yield worker, server.server_address[1]
    server.shutdown()
    server.server_close()
    worker.stop()


def _request(port, method, path, body=None, host="127.0.0.1", ctype="application/json"):
    conn = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
    headers = {"Host": f"{host}:{port}"}
    if ctype:
        headers["Content-Type"] = ctype
    data = json.dumps(body).encode() if body is not None else None
    conn.request(method, path, body=data, headers=headers)
    resp = conn.getresponse()
    payload = json.loads(resp.read() or b"{}")
    conn.close()
    return resp.status, payload


def _wait_ready(port, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if _request(port, "GET", "/health")[1].get("state") == "ready":
            return
        time.sleep(0.05)
    raise AssertionError("worker never became ready")


RERANK = {"query": "q", "documents": ["a", "bbb"]}


def test_idle_until_first_rerank_then_warming_then_forwarded(running):
    worker, port = running
    assert _request(port, "GET", "/health") == (200, {"ok": True, "state": "idle", "model": None})
    assert worker.state() == "idle"  # health never starts the model process
    status, payload = _request(port, "POST", "/rerank", RERANK)
    assert (status, payload) == (503, {"error": "warming"})
    _wait_ready(port)
    assert _request(port, "POST", "/rerank", RERANK) == (200, {"scores": [1.0, 3.0], "model": "fake"})


def test_idle_child_is_stopped_and_restarted_on_demand(running):
    worker, port = running
    _request(port, "POST", "/warm", {})
    _wait_ready(port)
    worker.idle_seconds = 0.0
    assert worker.reap() is True
    assert worker.state() == "idle"
    assert _request(port, "POST", "/rerank", RERANK)[0] == 503
    _wait_ready(port)
    worker.idle_seconds = 3600
    assert worker.reap() is False
    assert _request(port, "POST", "/rerank", RERANK)[0] == 200


def test_a_child_that_never_reports_a_port_is_stopped(tmp_path, monkeypatch):
    script = tmp_path / "silent.py"
    script.write_text("import time\ntime.sleep(60)\n", encoding="utf-8")
    clock = [0.0]
    worker = launcher.Worker([sys.executable, str(script)], idle_seconds=3600, clock=lambda: clock[0])
    assert worker.ensure_started() == "warming"
    clock[0] = launcher.START_TIMEOUT_SECONDS + 1
    assert worker.reap() is True
    assert worker.state() == "idle"


def test_a_crashed_child_reads_as_idle(tmp_path):
    script = tmp_path / "crash.py"
    script.write_text("raise SystemExit(3)\n", encoding="utf-8")
    worker = launcher.Worker([sys.executable, str(script)], idle_seconds=3600)
    worker.ensure_started()
    worker._proc.wait(timeout=10)
    assert worker.state() == "idle"


@pytest.mark.parametrize("host", ["evil.example", "192.168.1.5", "localhost.evil.example"])
def test_non_loopback_host_header_is_refused(running, host):
    worker, port = running
    assert _request(port, "POST", "/rerank", RERANK, host=host)[0] == 403
    assert _request(port, "GET", "/health", host=host)[0] == 403
    assert worker.state() == "idle"


@pytest.mark.parametrize("ctype", [None, "text/plain", "application/x-www-form-urlencoded"])
def test_post_without_json_content_type_is_refused(running, ctype):
    """A web page can only send these without a CORS preflight; they must not start the model."""
    worker, port = running
    assert _request(port, "POST", "/rerank", RERANK, ctype=ctype)[0] == 415
    assert _request(port, "POST", "/warm", {}, ctype=ctype)[0] == 415
    assert worker.state() == "idle"


@pytest.mark.parametrize("line,expected", [
    ('{"listening": "http://127.0.0.1:5123", "model": "m"}', 5123),
    ('{"listening": "http://10.0.0.2:5123"}', None),
    ('{"listening": "http://127.0.0.1.evil:5123"}', None),
    ('{"listening": "http://127.0.0.1:0"}', None),
    ("Loading weights: 100%", None),
    ("[1, 2]", None),
])
def test_only_a_loopback_listening_line_sets_the_forward_port(line, expected):
    assert launcher.parse_listening(line) == expected


def test_client_treats_warming_as_a_short_pause_not_a_down_server(running):
    worker, port = running
    clock = [100.0]
    client = HttpReranker(f"http://127.0.0.1:{port}", timeout=5, clock=lambda: clock[0])
    with pytest.raises(RerankerError, match="warming"):
        client.score("q", ["a", "bbb"])
    assert client._down_until == pytest.approx(100.0 + HttpReranker.WARMING_RETRY_SECONDS)
    _wait_ready(port)
    with pytest.raises(RerankerError, match="unavailable"):
        client.score("q", ["a", "bbb"])  # still inside the short pause
    clock[0] += HttpReranker.WARMING_RETRY_SECONDS
    assert client.score("q", ["a", "bbb"]) == [1.0, 3.0]
    assert client.model == "fake"


def test_client_other_http_errors_still_open_the_long_pause(running):
    worker, port = running
    clock = [0.0]
    client = HttpReranker(f"http://127.0.0.1:{port}", clock=lambda: clock[0])
    client.url = client.url + "/nope"  # 404 from the launcher
    with pytest.raises(RerankerError, match="unreachable"):
        client.score("q", ["a"])
    assert client._down_until == pytest.approx(HttpReranker.RETRY_AFTER_SECONDS)
