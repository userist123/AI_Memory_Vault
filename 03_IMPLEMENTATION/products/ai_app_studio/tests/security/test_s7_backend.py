"""S7 backend: preview captures served by sha256 over the authenticated REST API only; plan gate route owner-only."""
from __future__ import annotations

import hashlib
from collections.abc import Iterator
from pathlib import Path

import pytest
from apps.backend import main as backend
from starlette.testclient import TestClient
from studio_core import plans, project_kind
from studio_core.config import Settings

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}
PNG = b"\x89PNG\r\n\x1a\n" + b"capture"


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    app = backend.create_app(Settings(data_dir=tmp_path / "data"), TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c
    project_kind.unregister("app")


def _store(client: TestClient, data: bytes = PNG) -> str:
    sha = hashlib.sha256(data).hexdigest()
    d = client.app.state.studio.settings.data_dir / "captures"
    d.mkdir(parents=True, exist_ok=True)
    (d / f"{sha}.png").write_bytes(data)
    return sha


def test_capture_is_served_by_sha_with_the_owner_token_only(client: TestClient) -> None:
    sha = _store(client)
    assert client.get(f"/api/v1/captures/{sha}").status_code == 401
    assert client.get(f"/api/v1/captures/{sha}", headers={"Authorization": "Bearer " + "x" * 40}).status_code == 401
    r = client.get(f"/api/v1/captures/{sha}", headers=AUTH)  # [+]
    assert r.status_code == 200 and r.content == PNG
    assert r.headers["content-type"] == "image/png" and r.headers["x-content-type-options"] == "nosniff"
    assert "default-src 'none'" in r.headers["content-security-policy"]


@pytest.mark.parametrize("bad", ["../../config/api_token", "A" * 64, "0" * 63, "0" * 64])
def test_capture_route_refuses_bad_or_unknown_ids(client: TestClient, bad: str) -> None:
    r = client.get(f"/api/v1/captures/{bad}", headers=AUTH)
    assert r.status_code in (404, 422)


def test_a_changed_capture_file_is_not_served(client: TestClient) -> None:
    sha = _store(client)
    (client.app.state.studio.settings.data_dir / "captures" / f"{sha}.png").write_bytes(b"\x89PNG\r\n\x1a\nother")
    assert client.get(f"/api/v1/captures/{sha}", headers=AUTH).status_code == 404


def test_plan_gate_toggle_route_needs_the_owner_token(client: TestClient) -> None:
    studio = client.app.state.studio
    assert client.put("/api/v1/settings/plan-gate", json={"enabled": False}).status_code == 401
    assert plans.gate_enabled(studio) is True
    r = client.put("/api/v1/settings/plan-gate", headers=AUTH, json={"enabled": False})  # [+]
    assert r.status_code == 200 and r.json() == {"enabled": False} and plans.gate_enabled(studio) is False
    assert client.get("/api/v1/settings/plan-gate", headers=AUTH).json() == {"enabled": False}
    assert client.put("/api/v1/settings/plan-gate", headers=AUTH, json={"enabled": "no"}).status_code == 422
