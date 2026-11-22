"""The REST gateway serves notes under the same agent read contract as memory_search/memory_get.

Threat model (fable-consultant, 2026-11-22, path 1; verified in code before the fix):
- /api/v1/note/<id> returned storage.get(id) as is: RAW, ARCHIVED and quarantined notes included;
- /api/v1/search ignored the query (FileStorageEngine.query never reads `intent`) and returned the
  20 most recently updated non-RAW notes, ARCHIVED and quarantined included;
- /api/v1/chat put 8 such notes into the system prompt as "CANONICAL VAULT CONTEXT" and posted to
  OLLAMA_HOST without a loopback check;
- the unauthenticated /api/v1/status returned the absolute vault path.
After the fix the gateway reads through interfaces/memory_access (search/get), the chat context
comes from the same search, OLLAMA_HOST must be loopback (vault_access.ollama_assistant
.check_local_url), and /status does not reveal the vault path.
"""
from __future__ import annotations

import importlib.util
import json
import os
import sys
import threading
import urllib.error
import urllib.request
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

_spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fx)

from cognitive_core.proposal_queue import MemoryProposalQueue  # noqa: E402
from memory_controller.api_server import BrowserMemoryAPIHandler, HTTPServer  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402

TOKEN = "rest-read-contract-token"
MARKER = "zebrafinch"


def _call(base, method, path, body=None, token=TOKEN):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode("utf-8") if body is not None else None
    request = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as err:
        return err.code, json.loads(err.read())


@pytest.fixture
def gw(tmp_path, monkeypatch):
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    monkeypatch.setenv("AI_MEMORY_VAULT_API_TOKEN", TOKEN)
    vault = fx.make_vault(tmp_path)
    k = vault / "01_ARCHITECTURE" / "knowledge"
    ids = {}
    for name, lifecycle, extra in (("active", "ACTIVE", ""), ("raw", "RAW", ""), ("archived", "ARCHIVED", ""),
                                   ("quarantined", "ACTIVE", "quarantined: true\n")):
        nid = str(uuid.uuid4())
        ids[name] = nid
        (k / f"{name}.md").write_text(
            fx.note_text(f"{MARKER} {name}", f"The {MARKER} {name} note body.", nid, lifecycle=lifecycle,
                         extra=extra), encoding="utf-8")
    storage = FileStorageEngine(str(vault))
    controller = MemoryController(storage)
    queue = MemoryProposalQueue(tmp_path / "queue.jsonl")
    monkeypatch.setattr(BrowserMemoryAPIHandler, "storage", storage)
    monkeypatch.setattr(BrowserMemoryAPIHandler, "controller", controller)
    monkeypatch.setattr(BrowserMemoryAPIHandler, "queue", queue)
    monkeypatch.setattr(BrowserMemoryAPIHandler, "vault_root", vault)
    httpd = HTTPServer(("127.0.0.1", 0), BrowserMemoryAPIHandler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{httpd.server_address[1]}", ids, vault
    finally:
        httpd.shutdown()
        httpd.server_close()


@pytest.mark.parametrize("name", ["raw", "archived", "quarantined"])
def test_note_route_withholds_what_memory_get_withholds(gw, name):
    base, ids, _ = gw
    status, body = _call(base, "GET", f"/api/v1/note/{ids[name]}")
    assert status == 404, body
    assert f"{MARKER} {name} note body" not in json.dumps(body)


def test_note_route_serves_an_active_note(gw):
    base, ids, _ = gw
    status, body = _call(base, "GET", f"/api/v1/note/{ids['active']}")
    assert status == 200 and f"{MARKER} active note body" in json.dumps(body)


def test_search_is_query_driven_and_never_returns_withheld_notes(gw):
    base, ids, _ = gw
    status, body = _call(base, "GET", f"/api/v1/search?q={MARKER}")
    assert status == 200
    returned = {r.get("id") for r in body["results"]}
    assert ids["active"] in returned
    assert not returned & {ids["raw"], ids["archived"], ids["quarantined"]}
    assert len(body["results"]) <= 20


def test_status_does_not_reveal_the_vault_path(gw):
    base, ids, vault = gw
    status, body = _call(base, "GET", "/api/v1/status", token=None)
    assert status == 200
    assert str(vault) not in json.dumps(body) and "vault_root" not in body


def test_chat_refuses_a_non_loopback_model_host(gw, monkeypatch):
    base, ids, _ = gw
    monkeypatch.setenv("OLLAMA_HOST", "http://evil.example.com:11434")
    status, body = _call(base, "POST", "/api/v1/chat", {"message": f"tell me about {MARKER}", "model": "x"})
    assert status == 400, body
    assert "loopback" in json.dumps(body).lower()


def test_chat_context_comes_from_the_read_contract(gw, monkeypatch):
    """The system prompt sent to the local model never carries withheld notes."""
    import memory_controller.api_server as api_mod

    base, ids, _ = gw
    sent = {}

    def fake_post(path, payload, timeout=120):
        sent["payload"] = payload
        return {"message": {"content": "ok"}}

    monkeypatch.setattr(api_mod, "_ollama_post", fake_post)
    monkeypatch.setattr(api_mod, "_ollama_models", lambda: ["local-model"])
    status, body = _call(base, "POST", "/api/v1/chat", {"message": f"tell me about {MARKER}", "model": "local-model"})
    assert status == 200, body
    system = sent["payload"]["messages"][0]["content"]
    assert f"{MARKER} active note body" in system
    for name in ("raw", "archived", "quarantined"):
        assert f"{MARKER} {name} note body" not in system


def test_local_model_calls_do_not_follow_redirects(monkeypatch):
    """Security review finding 6: a redirect from the local model host is not followed."""
    import http.server
    import memory_controller.api_server as api_mod

    class Redirect(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            self.send_response(302)
            self.send_header("Location", "http://evil.example.com/steal")
            self.end_headers()

        def log_message(self, *args):
            return

    srv = http.server.HTTPServer(("127.0.0.1", 0), Redirect)
    t = threading.Thread(target=srv.serve_forever, daemon=True)
    t.start()
    try:
        monkeypatch.setenv("OLLAMA_HOST", f"http://127.0.0.1:{srv.server_address[1]}")
        with pytest.raises(urllib.error.HTTPError) as exc:
            api_mod._ollama_get("/api/tags", timeout=5)
        assert exc.value.code == 302
    finally:
        srv.shutdown()
        srv.server_close()
