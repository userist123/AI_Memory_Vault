"""T4 (P4b, P4c bind), T6 (P6d, P6f), body limit, token strength, REST surface of apps/backend."""

from __future__ import annotations

import sys
from collections.abc import Iterator
from pathlib import Path

import pytest
from apps.backend import main as backend
from apps.backend.security import BoundaryMiddleware
from starlette.testclient import TestClient
from starlette.websockets import WebSocketDisconnect
from studio_core import activity, project_kind
from studio_core.config import Settings
from studio_core.policy import Actor

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}
OWNER = Actor("human", "owner", "rest")


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    settings = Settings(data_dir=tmp_path / "data")
    app = backend.create_app(settings, TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c
    project_kind.unregister("app")


def test_health_is_public_and_exact_host_ok(client: TestClient) -> None:
    r = client.get("/api/v1/health")
    assert r.status_code == 200 and r.json()["status"] == "ok"


@pytest.mark.parametrize("host", ["evil.com", "127.0.0.1.nip.io", "p-1.localhost.evil", "127.0.0.1:9", "localhost", ""])
def test_p4b_foreign_host_is_421(client: TestClient, host: str) -> None:
    r = client.get("/api/v1/health", headers={"Host": host})
    assert r.status_code == 421


def test_p4b_duplicate_host_header(client: TestClient) -> None:
    r = client.get("/api/v1/health", headers=[("Host", "127.0.0.1:17875"), ("Host", "127.0.0.1:17875")])
    assert r.status_code == 400


def test_origin_allow_list(client: TestClient) -> None:
    assert client.get("/api/v1/projects", headers={**AUTH, "Origin": "http://evil.example"}).status_code == 403
    assert client.get("/api/v1/projects", headers={**AUTH, "Origin": "null"}).status_code == 403
    assert client.get("/api/v1/projects", headers={**AUTH, "Origin": "http://tauri.localhost"}).status_code == 200


def test_token_required_and_checked(client: TestClient) -> None:
    assert client.get("/api/v1/projects").status_code == 401
    assert client.get("/api/v1/projects", headers={"Authorization": "Bearer " + "x" * 40}).status_code == 401
    assert client.get("/api/v1/projects", headers={"X-AWS-Token": TOKEN}).status_code == 401  # AWS header not accepted
    assert client.get("/api/v1/projects", headers={"X-AAS-Token": TOKEN}).status_code == 200
    assert client.get("/api/v1/projects", headers=AUTH).status_code == 200


def test_short_token_refused() -> None:
    with pytest.raises(ValueError):
        BoundaryMiddleware(lambda s, r, se: None, token="short", allowed_hosts=["h"], allowed_origins=[], max_body=1)  # type: ignore[arg-type]
    with pytest.raises(ValueError):
        backend.create_app(Settings(data_dir=Path("x")), "a" * 31)


def test_body_limit(client: TestClient) -> None:
    big = {"name": "p", "template": "blank", "description": "d" * (3 * 1024 * 1024)}
    assert client.post("/api/v1/projects", json=big, headers=AUTH).status_code == 413


def test_rest_projects_files_activity(client: TestClient) -> None:
    r = client.post("/api/v1/projects", json={"name": "Rest One", "template": "blank"}, headers=AUTH)
    assert r.status_code == 201, r.text
    p = r.json()
    assert p["kind"] == "app"
    assert [x["id"] for x in client.get("/api/v1/projects", headers=AUTH).json()] == [p["id"]]
    files = client.get(f"/api/v1/projects/{p['id']}/files", headers=AUTH).json()
    assert {f["path"] for f in files} == {"src/App.tsx", "src/main.tsx", "public/icon.svg"}
    f = client.get(f"/api/v1/projects/{p['id']}/file", params={"path": "src/App.tsx"}, headers=AUTH).json()
    assert "App" in f["content"]
    bad = client.put(f"/api/v1/projects/{p['id']}/file", headers=AUTH,
                     json={"baseRevision": p["revision"], "path": "package.json", "content": "{}"})
    assert bad.status_code == 422 or bad.status_code == 400
    ok = client.put(f"/api/v1/projects/{p['id']}/file", headers=AUTH,
                    json={"baseRevision": p["revision"], "path": "src/New.tsx", "content": "export {}\n"})
    assert ok.status_code == 200 and ok.json()["revision"] == p["revision"] + 1
    unknown = client.put(f"/api/v1/projects/{p['id']}/file", headers=AUTH,
                         json={"baseRevision": 9, "path": "src/x.ts", "content": "", "extra": 1})
    assert unknown.status_code == 422  # extra fields forbidden
    ev = client.get("/api/v1/activity", params={"afterSeq": 0}, headers=AUTH).json()
    assert any(e["kind"] == "tool.call" and e["payload"].get("tool") == "code.write_file" for e in ev)
    after = client.get("/api/v1/activity", params={"afterSeq": ev[-1]["seq"]}, headers=AUTH).json()
    assert after == []


def test_external_mcp_setting_is_owner_only_route(client: TestClient) -> None:
    assert client.get("/api/v1/settings/external-mcp", headers=AUTH).json() == {"enabled": False}
    assert client.put("/api/v1/settings/external-mcp", json={"enabled": True}).status_code == 401
    assert client.put("/api/v1/settings/external-mcp", json={"enabled": True}, headers=AUTH).json() == {"enabled": True}
    assert client.get("/api/v1/settings/external-mcp", headers=AUTH).json() == {"enabled": True}


def test_main_binds_loopback_only_and_reads_token_from_stdin(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    import io

    import uvicorn
    seen: dict[str, object] = {}
    monkeypatch.setattr(uvicorn.Server, "run", lambda self, sockets=None: seen.update(  # S7 L2: serve on a pre-bound socket
        host=self.config.host, port=self.config.port, bound=sockets[0].getsockname()))  # type: ignore[index]
    monkeypatch.setenv("AAS_DATA_DIR", str(tmp_path / "d"))
    monkeypatch.setattr(sys, "stdin", io.StringIO(TOKEN + "\n"))
    backend.main(["--token-stdin"])
    assert seen["host"] == "127.0.0.1" and seen["port"] == 17875 and seen["bound"] == ("127.0.0.1", 17875)
    monkeypatch.setattr(sys, "stdin", io.StringIO("short\n"))
    with pytest.raises(SystemExit):
        backend.main(["--token-stdin"])


# ---------------------------------------------------------------- WebSocket (P6d, P6f)
def _ws(client: TestClient, qs: str = "", token: str | None = TOKEN, headers: dict[str, str] | None = None):  # type: ignore[no-untyped-def]
    protos = ["aas.v1", f"aas.token.{token}"] if token else ["aas.v1"]
    return client.websocket_connect(f"ws://127.0.0.1:17875/api/v1/events{qs}", subprotocols=protos, headers=headers or {})


def test_p6d_ws_auth(client: TestClient) -> None:
    for kwargs, code in (({"token": None}, 4401), ({"token": "w" * 40}, 4401),
                         ({"headers": {"Origin": "http://evil.example"}}, 4403),
                         ({"headers": {"Host": "evil.com"}}, 4421)):
        with pytest.raises(WebSocketDisconnect) as ei:
            with _ws(client, **kwargs) as ws:  # type: ignore[arg-type]
                ws.receive_json()
        assert ei.value.code == code, kwargs
    with _ws(client, "?afterSeq=0") as ws:  # positive control
        activity.record(client.app.state.studio, "step.started", payload={"index": 1})  # type: ignore[attr-defined]
        assert ws.receive_json()["kind"] == "step.started"


def test_p6f_replay_in_order_without_loss(client: TestClient) -> None:
    studio = client.app.state.studio  # type: ignore[attr-defined]
    first = activity.record(studio, "step.started", payload={"index": 0})["seq"]
    for i in range(1, 51):
        activity.record(studio, "step.started", payload={"index": i})
    with _ws(client, f"?afterSeq={first}") as ws:
        frames = [ws.receive_json() for _ in range(50)]
    assert [f["seq"] for f in frames] == list(range(first + 1, first + 51))
    assert frames[0]["prev"] == first and all(frames[i]["prev"] == frames[i - 1]["seq"] for i in range(1, 50))
    assert set(frames[0]) >= {"seq", "prev", "kind", "ts", "runId", "payload"}


def test_ws_gap_marker_when_rows_missing(client: TestClient) -> None:
    from sqlalchemy import text
    studio = client.app.state.studio  # type: ignore[attr-defined]
    seqs = [activity.record(studio, "step.started", payload={"index": i})["seq"] for i in range(4)]
    with studio.session() as s:
        s.execute(text("DELETE FROM activity_events WHERE seq = :q"), {"q": seqs[1]})
    with _ws(client, "?afterSeq=0") as ws:
        got = [ws.receive_json() for _ in range(4)]
    assert [g.get("seq") for g in got if g.get("type") != "gap"] == [seqs[0], seqs[2], seqs[3]]
    gap = next(g for g in got if g.get("type") == "gap")
    assert gap["after"] == seqs[0] and gap["before"] == seqs[2]


def test_ws_fixed_subprotocol_token_never_echoed(client: TestClient) -> None:
    with _ws(client, "?afterSeq=0") as ws:
        assert ws.accepted_subprotocol == "aas.v1"
        assert TOKEN not in repr(ws.extra_headers)
    url = "ws://127.0.0.1:17875/api/v1/events"
    with pytest.raises(WebSocketDisconnect) as ei:  # authenticated but without the fixed protocol: refused
        with client.websocket_connect(url, subprotocols=[f"aas.token.{TOKEN}"]) as ws:
            ws.receive_json()
    assert ei.value.code == 4400
    with client.websocket_connect(url, subprotocols=[f"aas.token.{TOKEN}", "aas.v1"]) as ws:  # order-independent
        assert ws.accepted_subprotocol == "aas.v1"


@pytest.mark.parametrize("value", ["yes", "true", 1, 0, "on", None])
def test_external_mcp_enabled_is_strict_bool(client: TestClient, value: object) -> None:
    r = client.put("/api/v1/settings/external-mcp", json={"enabled": value}, headers=AUTH)
    assert r.status_code == 422, r.text
    assert client.get("/api/v1/settings/external-mcp", headers=AUTH).json() == {"enabled": False}
    assert client.put("/api/v1/settings/external-mcp", json={"enabled": True}, headers=AUTH).status_code == 200


def test_runner_profile_is_an_owner_only_route_defaulting_to_full(client: TestClient) -> None:
    """Owner decision 2026-11-23: default "full"; only the authenticated owner switches it."""
    assert client.get("/api/v1/settings/runner-profile").status_code == 401
    assert client.get("/api/v1/settings/runner-profile", headers=AUTH).json() == {"profile": "full"}
    assert client.put("/api/v1/settings/runner-profile", json={"profile": "isolated"}).status_code == 401
    r = client.put("/api/v1/settings/runner-profile", json={"profile": "isolated"}, headers=AUTH)
    assert r.status_code == 200 and r.json() == {"profile": "isolated"}
    assert client.get("/api/v1/settings/runner-profile", headers=AUTH).json() == {"profile": "isolated"}


@pytest.mark.parametrize("body", [{"profile": "FULL"}, {"profile": "open"}, {"profile": 1}, {"profile": None},
                                  {"profile": True}, {}, {"profile": "full", "extra": 1}])
def test_runner_profile_value_is_validated(client: TestClient, body: object) -> None:
    r = client.put("/api/v1/settings/runner-profile", json=body, headers=AUTH)
    assert r.status_code == 422, r.text
    assert client.get("/api/v1/settings/runner-profile", headers=AUTH).json() == {"profile": "full"}
