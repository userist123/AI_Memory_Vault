"""S6 review: the plan-gate toggle is an owner-only, strict-bool route (default ON); backend shutdown stops every run."""
from __future__ import annotations

from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from apps.backend import main as backend
from starlette.testclient import TestClient
from studio_core import plans, project_kind
from studio_core.config import Settings

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    app = backend.create_app(Settings(data_dir=tmp_path / "data"), TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c
    project_kind.unregister("app")


def test_plan_gate_route_owner_only_default_on_strict_bool(client: TestClient) -> None:
    route = "/api/v1/settings/plan-gate"
    assert client.get(route, headers=AUTH).json() == {"enabled": True}
    assert client.get(route).status_code == 401
    assert client.put(route, json={"enabled": False}).status_code == 401
    for bad in ({"enabled": "no"}, {"enabled": 0}, {"enabled": None}, {"enabled": False, "x": 1}, {}):
        assert client.put(route, headers=AUTH, json=bad).status_code == 422, bad
    assert client.get(route, headers=AUTH).json() == {"enabled": True}
    assert client.put(route, headers=AUTH, json={"enabled": False}).json() == {"enabled": False}
    assert plans.gate_enabled(client.app.state.studio) is False
    assert client.put(route, headers=AUTH, json={"enabled": True}).json() == {"enabled": True}


def test_backend_shutdown_stops_every_run(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    calls: list[Any] = []
    app = backend.create_app(Settings(data_dir=tmp_path / "data"), TOKEN)
    monkeypatch.setattr(app.state.runner, "stop_all", lambda *a, **k: calls.append("stop_all"))
    with TestClient(app, base_url=BASE) as c:
        assert c.get("/api/v1/health").status_code == 200
        assert calls == []
    assert calls == ["stop_all"]
    project_kind.unregister("app")
