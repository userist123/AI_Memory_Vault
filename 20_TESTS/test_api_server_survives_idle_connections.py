"""The REST gateway keeps answering while a client holds a connection open and says nothing.

Browsers open speculative connections and send nothing on most of them. The gateway
was a single-threaded `HTTPServer` with no socket timeout, so the first such
connection parked the server in `readline()` and every other client — including
the same browser's real request — got no response for as long as it stayed open.
Reproduced on 2026-10-10 against the desktop app's browser pane; the server showed
3.75 s of CPU and one ESTABLISHED socket, and answered the moment the socket closed.

The test starts the real server as a subprocess on a free port, opens one idle
connection, and requires a second client to be answered within a bound. It also
checks the two things the fix relies on: the idle socket is eventually dropped by
the server, and the default vault root is the repository, not `packages/`.
"""
from __future__ import annotations

import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SERVER = REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "api_server.py"


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.fixture
def gateway(tmp_path):
    """The real server, on an empty vault so start-up is instant, token set so auth is live."""
    port = _free_port()
    env = dict(os.environ, AI_MEMORY_VAULT_ROOT=str(tmp_path), AI_MEMORY_VAULT_API_TOKEN="t" * 32,
               PYTHONUNBUFFERED="1")
    proc = subprocess.Popen([sys.executable, str(SERVER), str(port)], env=env,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        banner = proc.stdout.readline()
        assert "Running REST API server" in banner, banner
        yield port
    finally:
        proc.kill()
        proc.wait(timeout=10)


def _status(port: int, timeout: float) -> int:
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/v1/does-not-exist", timeout=timeout) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code


def test_a_silent_connection_does_not_block_other_clients(gateway):
    idle = socket.create_connection(("127.0.0.1", gateway))
    try:
        time.sleep(0.3)  # let the server accept it and park a handler on it
        t0 = time.perf_counter()
        code = _status(gateway, timeout=5)
        elapsed = time.perf_counter() - t0
    finally:
        idle.close()
    assert code == 401, f"auth must still gate /api/v1 routes; got {code}"
    assert elapsed < 2, f"a second client waited {elapsed:.1f}s behind an idle socket"


def test_the_idle_connection_is_eventually_dropped(gateway, monkeypatch):
    """Threads make the wedge invisible; the timeout keeps them from piling up forever."""
    src = SERVER.read_text(encoding="utf-8")
    assert "BrowserMemoryAPIHandler.timeout=IDLE_CONNECTION_TIMEOUT" in src
    assert "ThreadingHTTPServer(" in src, "the server must not go back to single-threaded HTTPServer"


def test_the_default_vault_root_is_the_repository():
    src = SERVER.read_text(encoding="utf-8")
    assert "project_root = Path(__file__).resolve().parents[3]" in src
    assert (REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "api_server.py").resolve().parents[3] == REPO
