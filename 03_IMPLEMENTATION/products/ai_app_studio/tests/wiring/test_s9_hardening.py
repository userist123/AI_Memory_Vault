"""S9a hardening: B4 (disk gate on builds, device screenshot quota, activity retention, owner-only project delete),
B11 (no dev origins in production), B12 (builds and device actions go through Studio.operation: policy + audit)."""
from __future__ import annotations

import datetime as dt
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from aas_build import service
from aas_device import service as dsvc
from sqlalchemy import select, update
from starlette.testclient import TestClient
from studio_core import activity, project_kind
from studio_core.aas_models import ActivityEvent
from studio_core.config import Settings
from studio_core.db import ToolExecution
from studio_core.errors import PolicyDenied, Unavailable
from studio_core.policy import POLICY, Actor, PolicyEngine

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, make_fake
from .test_device_service import GIB, FakeSession

AGENT = Actor("agent", "runner:run_0123456789abcdef", "mcp")


def rows(studio, tool: str) -> list[ToolExecution]:  # type: ignore[no-untyped-def]
    with studio.session() as s:
        out = list(s.execute(select(ToolExecution).where(ToolExecution.tool == tool)).scalars())
        s.expunge_all()
        return out


# ---------------------------------------------------------------- B11
def test_production_origins_have_no_vite_dev_server() -> None:
    o = Settings(data_dir=Path("x")).allowed_origins
    assert not any(":1420" in x or ":1421" in x for x in o)
    assert {"http://tauri.localhost", "tauri://localhost"} <= set(o)  # exact members, not substrings


def test_dev_origin_is_only_added_explicitly(tmp_path: Path) -> None:
    from apps.backend.main import create_app
    tok = "t" * 40
    app = create_app(Settings(data_dir=tmp_path / "d"), tok, dev_origins=["http://127.0.0.1:1421"])
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        h = {"Authorization": f"Bearer {tok}"}
        assert c.get("/api/v1/projects", headers={**h, "Origin": "http://127.0.0.1:1421"}).status_code == 200
    project_kind.unregister("app")
    app = create_app(Settings(data_dir=tmp_path / "e"), tok)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        assert c.get("/api/v1/projects", headers={**h, "Origin": "http://localhost:1420"}).status_code == 403
        assert c.get("/api/v1/projects", headers={**h, "Origin": "http://127.0.0.1:1420"}).status_code == 403
    project_kind.unregister("app")


# ---------------------------------------------------------------- B4 disk gate
@pytest.mark.parametrize("target", ["web", "android"])
def test_builds_refuse_below_5_gib_free(studio, target: str) -> None:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "p", "blank", "")["id"]
    called: list[int] = []

    def never(**_kw: Any) -> Any:
        called.append(1)
        raise AssertionError("the build must not start")
    fn = service.build_web if target == "web" else service.build_android
    with pytest.raises(Unavailable, match="5 GiB"):
        fn(studio, pid, run_build=never, docker=DOCKER, image=IMAGE, free_bytes=lambda: 5 * GIB - 1)
    assert called == []


def test_build_runs_with_enough_free_disk(studio) -> None:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "p", "blank", "")["id"]
    res = service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE, free_bytes=lambda: 6 * GIB)
    assert res["ok"] is True


# ---------------------------------------------------------------- B4 device screenshot quota
class ShotSession(FakeSession):
    n = 0

    def screenshot(self) -> bytes:
        ShotSession.n += 1
        return b"\x89PNG\r\n\x1a\n" + ShotSession.n.to_bytes(4, "big") + b"x" * 1000


def test_device_screenshots_are_capped_by_count_and_bytes(studio, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    monkeypatch.setattr(dsvc, "MAX_SHOTS", 5)
    monkeypatch.setattr(dsvc, "MAX_SHOT_BYTES_TOTAL", 4 * 1012)
    d = dsvc.DeviceService(studio, preflight=lambda: {}, free_bytes=lambda: 20 * GIB, session_factory=ShotSession)
    d.start("phone")
    shas = [d.screenshot()["sha256"] for _ in range(8)]
    left = sorted(p.stem for p in (studio.settings.data_dir / "device" / "shots").glob("*.png"))
    assert len(left) == 4  # the byte cap is tighter than the count cap here
    assert set(left) == set(shas[-4:])  # newest kept, oldest pruned
    d.stop()


def test_oversized_device_screenshot_is_refused(studio, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    monkeypatch.setattr(dsvc, "MAX_SHOT_BYTES", 100)
    d = dsvc.DeviceService(studio, preflight=lambda: {}, free_bytes=lambda: 20 * GIB, session_factory=ShotSession)
    d.start("phone")
    with pytest.raises(Unavailable):
        d.screenshot()
    assert not list((studio.settings.data_dir / "device" / "shots").glob("*.png"))
    d.stop()


# ---------------------------------------------------------------- B4 activity retention
def _age(studio, days: int) -> None:  # type: ignore[no-untyped-def]
    old = dt.datetime.now(dt.UTC) - dt.timedelta(days=days)
    with studio.session() as s:
        s.execute(update(ActivityEvent).values(ts=old))


def test_retention_prunes_old_events_but_keeps_the_newest_row(studio) -> None:  # type: ignore[no-untyped-def]
    for i in range(5):
        activity.record(studio, "device.log", payload={"text": f"e{i}"})
    _age(studio, 40)
    activity.record(studio, "device.log", payload={"text": "fresh"})
    assert activity.retention_days(studio) == 30
    n = activity.prune(studio)
    assert n == 5
    left = activity.list_events(studio, limit=100)
    assert [e["payload"]["text"] for e in left] == ["fresh"]
    # every row old: the newest is kept so seq numbers are never reused
    _age(studio, 400)
    assert activity.prune(studio) == 0
    assert len(activity.list_events(studio, limit=100)) == 1
    nxt = activity.record(studio, "device.log", payload={"text": "next"})
    assert nxt["seq"] > left[0]["seq"]


def test_retention_is_configurable_by_the_owner_only(studio) -> None:  # type: ignore[no-untyped-def]
    with pytest.raises(PolicyDenied):
        activity.set_retention_days(studio, AGENT, 7)
    with pytest.raises(ValueError):
        activity.set_retention_days(studio, OWNER, 0)
    activity.set_retention_days(studio, OWNER, 7)
    assert activity.retention_days(studio) == 7
    activity.record(studio, "device.log", payload={"text": "a"})
    _age(studio, 8)
    activity.record(studio, "device.log", payload={"text": "b"})
    assert activity.prune(studio) == 1


# ---------------------------------------------------------------- B4 owner-only project delete (REST, two-step)
@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    from apps.backend.main import create_app
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        yield c
    project_kind.unregister("app")


AUTH = {"Authorization": "Bearer " + "t" * 40}


def test_project_delete_needs_the_token_and_an_owner_approval(client: TestClient) -> None:
    pid = client.post("/api/v1/projects", json={"name": "p"}, headers=AUTH).json()["id"]
    st = client.app.state.studio
    prev = service.preview_base(st.settings, pid)
    prev.mkdir(parents=True)
    (prev / "current").write_text("site-0000000000000", encoding="utf-8")
    assert client.request("DELETE", f"/api/v1/projects/{pid}").status_code == 401
    r = client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH)
    assert r.status_code == 428
    apr = r.json()["details"]["approvalId"]
    assert any(x["id"] == apr and x["action"] == "project.delete" for x in client.get("/api/v1/approvals", headers=AUTH).json())
    # not decided yet: the approval id alone does not delete
    assert client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH, json={"approvalId": apr}).status_code == 428
    assert client.post(f"/api/v1/approvals/{apr}/decide", json={"decision": "approve"}, headers=AUTH).status_code == 200
    r = client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH, json={"approvalId": apr})
    assert r.status_code == 200, r.text
    assert r.json()["deleted"] == pid
    assert not prev.exists()
    assert all(p["id"] != pid for p in client.get("/api/v1/projects", headers=AUTH).json())
    # the approval is single use
    assert client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH, json={"approvalId": apr}).status_code in (403, 404, 428)


def test_retention_route_is_owner_only_and_bounded(client: TestClient) -> None:
    assert client.put("/api/v1/settings/retention", json={"days": 5}).status_code == 401
    assert client.put("/api/v1/settings/retention", json={"days": 0}, headers=AUTH).status_code == 422
    assert client.put("/api/v1/settings/retention", json={"days": "5"}, headers=AUTH).status_code == 422
    assert client.put("/api/v1/settings/retention", json={"days": 5}, headers=AUTH).json() == {"days": 5}
    assert client.get("/api/v1/settings/retention", headers=AUTH).json() == {"days": 5}


def test_delete_is_refused_while_a_run_of_the_project_is_live(client: TestClient, monkeypatch: pytest.MonkeyPatch) -> None:
    pid = client.post("/api/v1/projects", json={"name": "p"}, headers=AUTH).json()["id"]
    monkeypatch.setattr(client.app.state.runner, "live_project", lambda: pid)
    assert client.request("DELETE", f"/api/v1/projects/{pid}", headers=AUTH).status_code == 409


# ---------------------------------------------------------------- B12 audit + policy
def test_policy_table_knows_build_and_device_operations() -> None:
    pe = PolicyEngine()
    for op in ("app.build", "device.control"):
        assert op in POLICY
        pe.enforce(AGENT, op)  # allowed for agents (gated further by plan gate / owner device setting)


def test_web_build_writes_an_audit_row(studio) -> None:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "p", "blank", "")["id"]
    service.build_web(studio, pid, actor=AGENT, run_build=make_fake(), docker=DOCKER, image=IMAGE,
                      free_bytes=lambda: 9 * GIB)
    r = rows(studio, "app.build")
    assert len(r) == 1 and r[0].status == "ok" and r[0].project_id == pid and r[0].actor == AGENT.label
    assert r[0].params == {"projectId": pid, "target": "web"}


def test_failed_and_refused_builds_are_audited(studio) -> None:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "p", "blank", "")["id"]
    with pytest.raises(service.BuildFailed):
        service.build_web(studio, pid, actor=AGENT, run_build=make_fake(fail="boom"), docker=DOCKER, image=IMAGE,
                          free_bytes=lambda: 9 * GIB)
    with pytest.raises(Unavailable):
        service.build_web(studio, pid, actor=AGENT, run_build=make_fake(), docker=DOCKER, image=IMAGE,
                          free_bytes=lambda: 1)
    assert [x.status for x in rows(studio, "app.build")] == ["error", "error"]


def test_device_actions_write_audit_rows(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    d = dsvc.DeviceService(studio, preflight=lambda: {}, free_bytes=lambda: 20 * GIB, session_factory=FakeSession,
                           actor=lambda: AGENT)
    d.start("phone")
    d.tap(10, 20)
    d.screenshot()
    d.stop()
    tools = [x.tool for x in rows(studio, "device.start") + rows(studio, "device.tap") + rows(studio, "device.screenshot")]
    assert tools == ["device.start", "device.tap", "device.screenshot"]
    tap = rows(studio, "device.tap")[0]
    assert tap.params == {"x": 10, "y": 20} and tap.status == "ok" and tap.actor == AGENT.label
    # a refused call is audited too (setting switched off)
    dsvc.set_enabled(studio, OWNER, False)
    with pytest.raises(Unavailable):
        d.start("phone")
    assert [x.status for x in rows(studio, "device.start")] == ["ok", "error"]
