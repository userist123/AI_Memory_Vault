"""S6 backend follow-ups: approvals/plan routes (owner only), activity field names the UI reads, sanitiser fixes
(DCS/OSC termination, invisible fillers), audit.redact F1/F2."""
from __future__ import annotations

import json
from collections.abc import Iterator
from pathlib import Path

import pytest
from apps.backend import main as backend
from starlette.testclient import TestClient
from studio_core import activity, audit, plans, project_kind
from studio_core.config import Settings
from studio_core.errors import PolicyDenied
from studio_core.policy import Actor

TOKEN = "t" * 40
BASE = "http://127.0.0.1:17875"
AUTH = {"Authorization": f"Bearer {TOKEN}"}
AGENT = Actor("agent", "runner:run_0123456789abcdef", "mcp")
RUN = "run_0123456789abcdef"
SHA = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"


@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    app = backend.create_app(Settings(data_dir=tmp_path / "data"), TOKEN)
    with TestClient(app, base_url=BASE) as c:
        yield c
    project_kind.unregister("app")


def pid_of(client: TestClient) -> str:
    return client.post("/api/v1/projects", headers=AUTH, json={"name": "p1"}).json()["id"]


# ---------------------------------------------------------------- approvals + plans
def test_approvals_list_has_generic_and_plan_records_in_the_shape_the_ui_parses(client: TestClient) -> None:
    studio = client.app.state.studio
    pid = pid_of(client)
    plan = plans.submit(studio, AGENT, pid, "Build a habit tracker", ["scaffold", "build"], run_id=RUN)
    apr = studio.request_approval(AGENT, "deps.install", {"argv": ["pnpm", "add", "date-fns"], "target": "package.json",
                                                          "reason": "dates"}, pid)
    r = client.get("/api/v1/approvals", headers=AUTH)
    assert r.status_code == 200
    items = {i["id"]: i for i in r.json()}
    p = items[plan["planId"]]
    assert p["kind"] == "plan" and p["status"] == "pending" and p["title"] == "Build a habit tracker"
    assert p["steps"] == ["scaffold", "build"] and p["runId"] == RUN
    a = items[apr["approvalId"]]
    assert a["action"] == "deps.install" and a["argv"] == ["pnpm", "add", "date-fns"] and a["target"] == "package.json"
    assert a["reason"] == "dates" and a["status"] == "pending" and a.get("kind") != "plan"
    assert client.get("/api/v1/approvals").status_code == 401


def test_plan_submit_writes_a_plan_proposed_event_with_title(client: TestClient) -> None:
    studio = client.app.state.studio
    pid = pid_of(client)
    plans.submit(studio, AGENT, pid, "Title here", ["a"], run_id=RUN)
    ev = [e for e in activity.list_events(studio) if e["kind"] == "plan.proposed"]
    assert ev and ev[-1]["payload"]["title"] == "Title here" and ev[-1]["runId"] == RUN


def test_owner_decides_approval_once_and_event_is_written(client: TestClient) -> None:
    studio = client.app.state.studio
    pid = pid_of(client)
    apr = studio.request_approval(AGENT, "deps.install", {"argv": ["pnpm", "add", "x"]}, pid)["approvalId"]
    assert client.post(f"/api/v1/approvals/{apr}/decide", json={"decision": "approve"}).status_code == 401
    assert client.post(f"/api/v1/approvals/{apr}/decide", headers=AUTH, json={"decision": "maybe"}).status_code == 422
    assert client.post(f"/api/v1/approvals/{apr}/decide", headers=AUTH,
                       json={"decision": "approve", "approved": True}).status_code == 422  # extra field refused
    r = client.post(f"/api/v1/approvals/{apr}/decide", headers=AUTH, json={"decision": "approve"})
    assert r.status_code == 200 and r.json()["status"] == "approved"
    assert client.post(f"/api/v1/approvals/{apr}/decide", headers=AUTH, json={"decision": "deny"}).status_code == 422
    assert client.post("/api/v1/approvals/apr_ffffffffffffffff/decide", headers=AUTH,
                       json={"decision": "deny"}).status_code == 404
    assert any(e["kind"] == "approval.decided" and e["payload"]["decision"] == "approve"
               for e in activity.list_events(studio))


def test_plan_decide_route_and_cross_route_refusal(client: TestClient) -> None:
    studio = client.app.state.studio
    pid = pid_of(client)
    plan = plans.submit(studio, AGENT, pid, "T", ["s"], run_id=RUN)["planId"]
    apr = studio.request_approval(AGENT, "deps.install", {"argv": ["x"]}, pid)["approvalId"]
    # a plan cannot be decided as a generic approval and vice versa
    assert client.post(f"/api/v1/approvals/{plan}/decide", headers=AUTH, json={"decision": "approve"}).status_code == 422
    assert client.post(f"/api/v1/plans/{apr}/decide", headers=AUTH, json={"decision": "approve"}).status_code == 422
    assert client.post(f"/api/v1/plans/{plan}/decide", headers=AUTH, json={"decision": "nope"}).status_code == 422
    r = client.post(f"/api/v1/plans/{plan}/decide", headers=AUTH, json={"decision": "approve", "note": ""})
    assert r.status_code == 200 and r.json()["status"] == "approved"
    assert plans.is_approved(studio, plan, pid) is True
    assert any(e["kind"] == "plan.decided" and e["payload"]["decision"] == "approve" for e in activity.list_events(studio))
    left = [i["id"] for i in client.get("/api/v1/approvals", headers=AUTH).json()]
    assert plan not in left and apr in left


def test_plan_edit_decision_rejects_the_plan(client: TestClient) -> None:
    studio = client.app.state.studio
    pid = pid_of(client)
    plan = plans.submit(studio, AGENT, pid, "T", ["s"], run_id=RUN)["planId"]
    r = client.post(f"/api/v1/plans/{plan}/decide", headers=AUTH, json={"decision": "edit", "note": "use dark mode"})
    assert r.status_code == 200 and r.json()["status"] == "rejected"
    assert plans.is_approved(studio, plan, pid) is False


def test_agent_actor_cannot_decide_anything(client: TestClient) -> None:
    studio = client.app.state.studio
    pid = pid_of(client)
    plan = plans.submit(studio, AGENT, pid, "T", ["s"], run_id=RUN)["planId"]
    apr = studio.request_approval(AGENT, "deps.install", {"argv": ["x"]}, pid)["approvalId"]
    for actor in (AGENT, Actor("human", "owner", "mcp"), Actor("human", "owner", "computer")):
        with pytest.raises(PolicyDenied):
            plans.decide(studio, actor, plan, "approve")
        with pytest.raises(PolicyDenied):
            plans.decide_approval(studio, actor, apr, "approve")
    assert plans.is_approved(studio, plan, pid) is False


# ---------------------------------------------------------------- field names the UI reads
def test_activity_payload_field_names_match_the_ui(studio) -> None:  # type: ignore[no-untyped-def]
    rec = lambda kind, **p: activity.record(studio, kind, project_id="prj_0123456789abcdef", run_id=RUN,  # noqa: E731
                                            payload=p)["payload"]
    assert rec("run.started", title="T", budgetUsd=2.5) == {"title": "T", "budgetUsd": 2.5}
    assert rec("step.started", stepId="s1", title="Scaffold") == {"stepId": "s1", "title": "Scaffold"}
    assert rec("tool.call", callId="c1", target="src/App.tsx", tool="code_write_file") == {
        "callId": "c1", "target": "src/App.tsx", "tool": "code_write_file"}
    assert rec("tool.result", callId="c1", isError=False, size=3, sha256=SHA)["callId"] == "c1"
    assert rec("plan.proposed", planId="apr_1", title="Plan T", steps=["a"])["title"] == "Plan T"
    assert rec("build.log", line="vite build ok")["line"] == "vite build ok"
    assert rec("cost.update", inputTokens=1, outputTokens=2, costUsd=0.1) == {"inputTokens": 1, "outputTokens": 2,
                                                                             "costUsd": 0.1}
    assert rec("preview.captured", revision=3, frame="phone")["frame"] == "phone"
    assert rec("agent.typing", path="src/App.tsx", text="x")["path"] == "src/App.tsx"


# ---------------------------------------------------------------- sanitiser fixes
def test_dcs_stops_at_newline_and_end_and_osc_stops_at_cr() -> None:
    s = activity.sanitize_text
    assert s("a\x1bPsecret-body\nvisible") == "a\nvisible"  # DCS body never spans a newline
    assert s("a\x1bPunterminated to the end") == "a"
    assert s("a\x1bPbody\x1b\\b") == "ab"  # terminated by ST
    assert s("x\x1b]0;title\rvisible") == "xvisible"  # OSC ends at \r (the \r itself is a dropped control)
    assert s("x\x1b]0;title\x07y") == "xy"


@pytest.mark.parametrize("ch", ["ㅤ", "ᅟ", "᠎", "͏"])
def test_invisible_filler_characters_are_dropped(ch: str) -> None:
    assert activity.sanitize_text(f"ab{ch}cd") == "abcd"


# ---------------------------------------------------------------- audit.redact F1/F2
def test_f1_hash_under_hash_named_key_is_kept_verbatim_other_values_redacted() -> None:
    out = audit.redact({"sha256": SHA, "distSha256": SHA, "note": "plain", "blob": "sk-ant-api03-" + "Zz9" * 10,
                        "sha256_bad": "not-a-hash-but-" + "k" * 5})
    assert out["sha256"] == SHA and out["distSha256"] == SHA and out["note"] == "plain"
    assert "REDACTED" in out["blob"]
    assert out["sha256_bad"].startswith("not-a-hash")


@pytest.mark.parametrize("key", ["pass​word", "api\x1b[0m_key", "to‮ken", "se᠎cret"])
def test_f2_keys_are_sanitised_before_the_secret_key_check(key: str) -> None:
    out = audit.redact({key: "hunter2", "ok": 1})
    assert "hunter2" not in json.dumps(out) and out["ok"] == 1


# ---------------------------------------------------------------- owner setting for the device tools
def test_device_setting_is_owner_route_default_off_strict_bool(client: TestClient) -> None:
    assert client.get("/api/v1/settings/device", headers=AUTH).json() == {"enabled": False}
    assert client.put("/api/v1/settings/device", json={"enabled": True}).status_code == 401
    assert client.put("/api/v1/settings/device", headers=AUTH, json={"enabled": "yes"}).status_code == 422
    assert client.put("/api/v1/settings/device", headers=AUTH, json={"enabled": True, "x": 1}).status_code == 422
    assert client.put("/api/v1/settings/device", headers=AUTH, json={"enabled": True}).json() == {"enabled": True}
    assert client.get("/api/v1/settings/device", headers=AUTH).json() == {"enabled": True}


# ---------------------------------------------------------------- runs (owner token only; the CLI comes from argv only)
def test_runs_routes_need_the_owner_token_and_validate_input(client: TestClient) -> None:
    body = {"projectId": "prj_0123456789abcdef", "prompt": "x"}
    assert client.post("/api/v1/runs", json=body).status_code == 401
    assert client.post("/api/v1/runs", headers=AUTH, json={**body, "projectId": "../x"}).status_code == 422
    assert client.post("/api/v1/runs", headers=AUTH, json={**body, "cli": ["calc.exe"]}).status_code == 422
    assert client.post("/api/v1/runs", headers=AUTH, json=body).status_code == 404  # unknown project, nothing started
    assert client.post("/api/v1/runs/run_0123456789abcdef/stop").status_code == 401
    assert client.post("/api/v1/runs/not-a-run/stop", headers=AUTH).status_code == 422


# ---------------------------------------------------------------- dev UI origins (process environment only, strict)
def test_dev_origins_are_off_by_default_and_strictly_validated(tmp_path: Path) -> None:
    dev = "http://127.0.0.1:1421"
    off = TestClient(backend.create_app(Settings(data_dir=tmp_path / "a"), TOKEN), base_url=BASE)
    assert off.get("/api/v1/projects", headers={**AUTH, "Origin": dev}).status_code == 403  # control: not allowed by default
    on = TestClient(backend.create_app(Settings(data_dir=tmp_path / "b"), TOKEN, dev_origins=[dev]), base_url=BASE)
    assert on.get("/api/v1/projects", headers={**AUTH, "Origin": dev}).status_code == 200
    assert on.get("/api/v1/projects", headers={**AUTH, "Origin": "http://127.0.0.1:1422"}).status_code == 403
    for bad in ("http://evil.example:1421", "http://127.0.0.1", "https://127.0.0.1:1421", "null", "*", "http://127.0.0.1:1421/x",
                "http://localhost:1421.evil.com"):
        with pytest.raises(ValueError):
            backend.create_app(Settings(data_dir=tmp_path / "c"), TOKEN, dev_origins=[bad])
    assert backend.parse_dev_origins(" http://127.0.0.1:1421 , http://localhost:4174 ") == [
        "http://127.0.0.1:1421", "http://localhost:4174"]
    assert backend.parse_dev_origins("") == []
    project_kind.unregister("app")
