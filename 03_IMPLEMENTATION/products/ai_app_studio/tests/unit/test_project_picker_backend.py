"""Slice C (project picker): GET /projects carries ``lastBuildAt`` (the time the current preview site was switched in,
None before the first build); create with a template, and delete stays the two-step owner approval flow."""

from __future__ import annotations

import datetime as dt
import os
from collections.abc import Iterator
from pathlib import Path

import pytest
from aas_build import service as build_service
from apps.backend import main as backend
from starlette.testclient import TestClient
from studio_core import project_kind
from studio_core.config import Settings

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}


@pytest.fixture()
def env(tmp_path: Path) -> Iterator[tuple[TestClient, Settings]]:
    settings = Settings(data_dir=tmp_path / "data")
    app = backend.create_app(settings, TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c, settings
    project_kind.unregister("app")


def test_list_has_last_build_at_none_before_a_build(env: tuple[TestClient, Settings]) -> None:
    client, _ = env
    r = client.post("/api/v1/projects", json={"name": "Fresh", "template": "tabs"}, headers=AUTH)
    assert r.status_code == 201, r.text
    rows = client.get("/api/v1/projects", headers=AUTH).json()
    assert [(x["name"], x["template"], x["lastBuildAt"]) for x in rows] == [("Fresh", "tabs", None)]


def test_list_last_build_at_is_the_current_pointer_time(env: tuple[TestClient, Settings]) -> None:
    client, settings = env
    pid = client.post("/api/v1/projects", json={"name": "Built", "template": "blank"}, headers=AUTH).json()["id"]
    base = build_service.preview_base(settings, pid)
    base.mkdir(parents=True)
    (base / "current").write_text("b_0001", encoding="utf-8")
    when = dt.datetime(2026, 11, 20, 9, 30, tzinfo=dt.UTC).timestamp()
    os.utime(base / "current", (when, when))
    row = client.get("/api/v1/projects", headers=AUTH).json()[0]
    assert dt.datetime.fromisoformat(row["lastBuildAt"]) == dt.datetime(2026, 11, 20, 9, 30, tzinfo=dt.UTC)


def test_delete_is_still_two_step_with_owner_approval(env: tuple[TestClient, Settings]) -> None:
    client, _ = env
    pid = client.post("/api/v1/projects", json={"name": "Gone", "template": "list-detail"}, headers=AUTH).json()["id"]
    first = client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH)
    assert first.status_code == 428
    apr = first.json()["details"]["approvalId"]
    # without the owner's decision the repeat is refused and the project stays
    assert client.request("DELETE", f"/api/v1/projects/{pid}", json={"approvalId": apr}, headers=AUTH).status_code != 200
    assert [x["id"] for x in client.get("/api/v1/projects", headers=AUTH).json()] == [pid]
    assert client.post(f"/api/v1/approvals/{apr}/decide", json={"decision": "approve"}, headers=AUTH).status_code == 200
    done = client.request("DELETE", f"/api/v1/projects/{pid}", json={"approvalId": apr}, headers=AUTH)
    assert done.status_code == 200, done.text
    assert client.get("/api/v1/projects", headers=AUTH).json() == []
