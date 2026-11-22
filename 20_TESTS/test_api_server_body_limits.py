"""The REST gateway bounds request bodies before reading them (security program, path 8).

`_body()` used to trust `Content-Length` and call `rfile.read(n)` for any n: one authenticated
request claiming a multi-gigabyte body made the handler thread allocate and wait for it, and a
non-numeric or negative header raised ValueError (a dropped connection instead of an answer).
The real server runs as a subprocess on an empty vault, as in
test_api_server_survives_idle_connections.py.
"""
from __future__ import annotations

import os
import socket
import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SERVER = REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "api_server.py"
TOKEN = "t" * 32


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.fixture
def gateway(tmp_path):
    port = _free_port()
    env = dict(os.environ, AI_MEMORY_VAULT_ROOT=str(tmp_path), AI_MEMORY_VAULT_API_TOKEN=TOKEN,
               MEMORY_CONTROLLER_HMAC_SECRET="s" * 48,  # the gateway refuses to start without one;
               # set here so the test does not depend on another test leaking it into os.environ
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


def _post(port: int, length_header: str, body: bytes = b"", auth: bool = True) -> int:
    lines = [f"POST /api/v1/propose HTTP/1.1", "Host: 127.0.0.1", "Content-Type: application/json",
             f"Content-Length: {length_header}"]
    if auth:
        lines.append(f"Authorization: Bearer {TOKEN}")
    raw = ("\r\n".join(lines) + "\r\n\r\n").encode("ascii") + body
    with socket.create_connection(("127.0.0.1", port), timeout=10) as s:
        s.sendall(raw)
        data = b""
        while b"\r\n" not in data:
            chunk = s.recv(4096)
            if not chunk:
                break
            data += chunk
    status_line = data.split(b"\r\n", 1)[0].decode("ascii", "replace")
    assert status_line.startswith("HTTP/"), f"no HTTP answer: {data[:200]!r}"
    return int(status_line.split()[1])


def test_a_huge_declared_body_is_refused_without_being_read(gateway):
    # 10 GB declared, nothing sent: the old code blocked in rfile.read until the idle timeout
    assert _post(gateway, str(10 * 1024 ** 3)) == 413


def test_a_body_just_over_the_cap_is_refused(gateway):
    sys.path.insert(0, str(SERVER.parent.parent))
    try:
        from interfaces.api_server import MAX_BODY_BYTES
    finally:
        sys.path.pop(0)
    assert _post(gateway, str(MAX_BODY_BYTES + 1)) == 413


@pytest.mark.parametrize("bad", ["abc", "-5", "1e3", " ", "99999999999999999999999999"])
def test_a_malformed_content_length_gets_an_answer(gateway, bad):
    assert _post(gateway, bad) in {400, 413}


def test_a_normal_body_still_works(gateway):
    body = b'{"content": "a small fact", "type": "fact"}'
    assert _post(gateway, str(len(body)), body) == 201


def test_auth_is_still_checked_before_any_body_handling(gateway):
    assert _post(gateway, str(10 * 1024 ** 3), auth=False) == 401
