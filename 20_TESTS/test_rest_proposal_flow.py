"""The documented REST flow works end to end: propose -> approve -> promote-approved.

It did not: the decision endpoint approved as the reviewer 'jarvis-human', which the U05 gate does
not count, so `promote-approved` always failed; and behind that, a promoted candidate was proposed
as `candidate-<uuid>` with the extractor's own provenance keys and a task/fact type, none of which
the canonical frontmatter schema accepts, so no real controller ever took one. This test runs the
whole flow against a real MemoryController on a temporary vault, over HTTP.
"""
from __future__ import annotations

import importlib.util
import json
import os
import sys
import threading
import urllib.error
import urllib.request
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

TOKEN = "rest-flow-test-token"


class Api:
    def __init__(self, base, controller, queue):
        self.base, self.controller, self.queue = base, controller, queue

    def call(self, method, path, body=None, token=TOKEN):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = f"Bearer {token}"
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as err:
            return err.code, json.loads(err.read())

    def propose(self, content, kind="decision"):
        status, body = self.call("POST", "/api/v1/propose", {"content": content, "type": kind})
        assert status == 201, body
        return body["candidate_id"]

    def record(self, candidate_id):
        return next(r for r in self.queue._load() if r["candidate_id"] == candidate_id)


@pytest.fixture
def api(tmp_path, monkeypatch):
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    monkeypatch.setenv("AI_MEMORY_VAULT_API_TOKEN", TOKEN)
    vault = fx.make_vault(tmp_path)
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
        yield Api(f"http://127.0.0.1:{httpd.server_address[1]}", controller, queue)
    finally:
        httpd.shutdown()
        httpd.server_close()


def test_propose_approve_promote_works_end_to_end(api):
    candidate_id = api.propose("Am decis: folosim SQLite WAL pentru indexul local.")

    status, body = api.call("POST", f"/api/v1/proposals/{candidate_id}/decision", {"decision": "APPROVED"})
    assert (status, body["status"]) == (200, "APPROVED"), body
    record = api.record(candidate_id)
    # the approval is an owner attestation made by the authenticated request, not a typed word
    assert record["queue_status"] == "APPROVED" and record["verification"] == "verified"
    assert record["verification_source"] == "human" and record["reviewed_by"] == "jarvis-web"
    assert candidate_id in record["evidence_reference"] and "bearer-authenticated" in record["evidence_reference"]

    status, body = api.call("POST", "/api/v1/proposals/promote-approved", {})
    assert status == 200, body
    assert body["status"] == "promoted" and body["ids"] == [candidate_id] and body["skipped"] == []
    assert api.record(candidate_id)["queue_status"] == "PROMOTED"

    # the note is really in the vault: a proposal, never trusted by being promoted
    note = api.controller.storage.get(candidate_id)
    assert note["type"] == "decision" and "queue-candidate" in note["tags"]
    assert note["verification"] == "unverified" and note["lifecycle"] != "ACTIVE"
    assert set(note["provenance"]) == {"source_type", "source_ref"}
    assert "SQLite WAL" in note["content"]

    # running it again promotes nothing twice
    status, body = api.call("POST", "/api/v1/proposals/promote-approved", {})
    assert (status, body["ids"]) == (200, [])


@pytest.mark.parametrize("kind,note_type", [("fact", "knowledge"), ("task", "project"), ("lesson", "lesson"),
                                            ("preference", "preference"), ("procedure", "procedure")])
def test_every_candidate_kind_the_ui_can_propose_is_promotable(api, kind, note_type):
    candidate_id = api.propose(f"Continut pentru tipul {kind}, destul de lung.", kind)
    assert api.call("POST", f"/api/v1/proposals/{candidate_id}/decision", {"decision": "APPROVED"})[0] == 200
    status, body = api.call("POST", "/api/v1/proposals/promote-approved", {})
    assert status == 200 and body["ids"] == [candidate_id], body
    assert api.controller.storage.get(candidate_id)["type"] == note_type


def test_a_rejected_candidate_is_never_promoted(api):
    candidate_id = api.propose("Am decis: renuntam la indexul vechi.")
    assert api.call("POST", f"/api/v1/proposals/{candidate_id}/decision", {"decision": "REJECTED"})[0] == 200
    status, body = api.call("POST", "/api/v1/proposals/promote-approved", {})
    assert (status, body["ids"]) == (200, [])
    assert api.controller.storage.get(candidate_id) is None


def test_the_decision_and_promote_endpoints_need_the_owner_token(api):
    candidate_id = api.propose("Am decis: toate mutatiile cer token.")
    for token in (None, "wrong-token"):
        status, _ = api.call("POST", f"/api/v1/proposals/{candidate_id}/decision", {"decision": "APPROVED"}, token=token)
        assert status == 401
        assert api.call("POST", "/api/v1/proposals/promote-approved", {}, token=token)[0] == 401
    assert api.record(candidate_id)["queue_status"] == "PENDING_REVIEW"


def test_a_legacy_unattested_approval_is_reported_and_does_not_block_the_flow(api):
    legacy = api.propose("Am decis: aprobare veche fara atestare.")
    fresh = api.propose("Am decis: aprobare noua cu atestare.")
    records = api.queue._load()
    for record in records:
        if record["candidate_id"] == legacy:
            record.update(queue_status="APPROVED", reviewed_by="jarvis-human")  # the pre-fix REST shape
    api.queue._write(records)
    assert api.call("POST", f"/api/v1/proposals/{fresh}/decision", {"decision": "APPROVED"})[0] == 200

    status, body = api.call("POST", "/api/v1/proposals/promote-approved", {})
    assert status == 200 and body["ids"] == [fresh]
    assert [item["candidate_id"] for item in body["skipped"]] == [legacy]
    assert api.record(legacy)["queue_status"] == "APPROVED"

    # alone, the same legacy record is a hard error rather than a silent no-op
    status, body = api.call("POST", "/api/v1/proposals/promote-approved", {})
    assert status == 400 and "verified evidence" in body["error"]


def test_the_jarvis_supervisor_client_authenticates_against_the_gateway(api, monkeypatch):
    """jarvis_v2/supervisor.py is a Python consumer of the same gateway: it sent no token, so every
    route but /status answered 401. It reads the token from the same environment variable the gateway does."""
    sys.path.insert(0, str(REPO / "02_PRODUCT" / "projects" / "workspaces" / "jarvis_web"))
    from jarvis_v2 import supervisor

    monkeypatch.setattr(supervisor, "MEMORY_API", api.base + "/api/v1")
    assert supervisor._request_json("/metrics")["engine"] == "V6"

    monkeypatch.delenv("AI_MEMORY_VAULT_API_TOKEN")
    with pytest.raises(urllib.error.HTTPError) as refused:
        supervisor._request_json("/metrics")
    assert refused.value.code == 401
    assert supervisor._request_json("/status")["status"] == "online"  # the one public route
