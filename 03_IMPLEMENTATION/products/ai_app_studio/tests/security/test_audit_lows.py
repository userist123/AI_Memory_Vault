"""Security audit lows (docs/SECURITY_AUDIT.md): O6 response headers on every API response, O7 422 bodies without
the rejected input; F2 backend side: a prompt-derived title with ':' / ',' is refused with a readable message, the
sanitised title is accepted."""
from __future__ import annotations

from collections.abc import Iterator
from pathlib import Path

import pytest
from apps.backend import main as backend
from starlette.testclient import TestClient
from studio_core import project_kind
from studio_core.config import Settings

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}
CANARY = "CANARY-INPUT-9f2e<script>"


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    app = backend.create_app(Settings(data_dir=tmp_path / "data"), TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c
    project_kind.unregister("app")


def _hardened(r) -> None:  # type: ignore[no-untyped-def]
    h = r.headers
    assert h.get("x-content-type-options") == "nosniff", (r.status_code, dict(h))
    assert "no-store" in h.get("cache-control", ""), (r.status_code, dict(h))
    assert h.get("referrer-policy") == "no-referrer", (r.status_code, dict(h))
    assert "frame-ancestors 'none'" in h.get("content-security-policy", ""), (r.status_code, dict(h))


@pytest.mark.parametrize("call", [
    lambda c: c.get("/api/v1/health"),
    lambda c: c.get("/api/v1/projects", headers=AUTH),
    lambda c: c.get("/api/v1/projects"),  # 401 from the boundary
    lambda c: c.get("/api/v1/projects/prj_0000000000000000/files", headers=AUTH),  # studio error
    lambda c: c.get("/api/v1/nope", headers=AUTH),  # 404 route
    lambda c: c.post("/api/v1/projects", headers=AUTH, json={"name": 5}),  # 422 validation
])
def test_o6_every_response_carries_the_security_headers(client: TestClient, call) -> None:  # type: ignore[no-untyped-def]
    _hardened(call(client))


def test_o7_422_body_is_generic_and_never_echoes_input(client: TestClient) -> None:
    r = client.post("/api/v1/projects", headers=AUTH, json={"name": CANARY * 3, "bogus": CANARY})
    assert r.status_code == 422
    assert CANARY not in r.text and "script" not in r.text
    body = r.json()
    assert body["error"] == "validation_failed" and body["message"] == "request validation failed"
    assert set(body["details"]["fields"]) == {"name", "bogus"}
    assert all(isinstance(f, str) and len(f) <= 64 for f in body["details"]["fields"])


def test_o7_malformed_json_body_is_generic(client: TestClient) -> None:
    r = client.post("/api/v1/projects", headers={**AUTH, "Content-Type": "application/json"},
                    content=b'{"name": "' + CANARY.encode() + b'"')
    assert r.status_code == 422 and CANARY not in r.text


def test_f2_prompt_title_with_colon_is_refused_with_a_readable_message(client: TestClient) -> None:
    r = client.post("/api/v1/projects", headers=AUTH, json={"name": "Build: a todo app, with tags"})
    assert r.status_code == 422
    assert "project name" in r.json()["message"]


def test_f2_sanitised_title_is_accepted(client: TestClient) -> None:
    r = client.post("/api/v1/projects", headers=AUTH, json={"name": "Build a todo app with tags"})
    assert r.status_code == 201 and r.json()["id"].startswith("prj_")
    r = client.post("/api/v1/projects", headers=AUTH, json={"name": "App"})
    assert r.status_code == 201
