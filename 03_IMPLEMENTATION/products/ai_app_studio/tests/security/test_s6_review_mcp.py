"""S6 review fixes on the real MCP server over stdio: H1 (a run grant pins EVERY tool to its project, activity_list
included) and P5f enforcement (code_write_file / code_delete_file / app_build need an approved plan of the current run;
the plan_submit call waits for the owner; owner-only toggle, default ON)."""
from __future__ import annotations

import json
import os
import sys
import threading
import time
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

import pytest
from mcp.client.session import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client
from studio_core import activity, plans, project_kind, run_grants
from studio_core.app_kind import register_app_kind
from studio_core.config import Settings
from studio_core.errors import PolicyDenied
from studio_core.policy import Actor
from studio_core.services import Studio

ROOT = Path(__file__).resolve().parents[2]
SERVER = ROOT / "apps" / "mcp-app" / "server.py"
OWNER = Actor("human", "owner", "rest")
AGENT = Actor("agent", "claude-code", "mcp")
RUN_A, RUN_B = "run_aaaaaaaaaaaaaaaa", "run_bbbbbbbbbbbbbbbb"


@pytest.fixture()
def studio(tmp_path: Path):  # type: ignore[no-untyped-def]
    register_app_kind()
    yield Studio(Settings(data_dir=tmp_path / "data"), kind="app")
    project_kind.unregister("app")


@asynccontextmanager
async def session(studio: Studio, **extra: str) -> AsyncIterator[ClientSession]:
    env = {k: v for k, v in os.environ.items() if not k.startswith(("AWS_", "AAS_"))}
    env.update({"AAS_DATA_DIR": str(studio.settings.data_dir), "AAS_AGENT_ID": "test-agent", **extra})
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)], env=env, cwd=str(ROOT))
    async with stdio_client(params) as (r, w), ClientSession(r, w) as s:
        await s.initialize()
        yield s


def body(res: Any) -> dict[str, Any]:
    return json.loads(next(c for c in res.content if c.type == "text").text)


def grant_env(studio: Studio, run_id: str, pid: str, **extra: str) -> dict[str, str]:
    return {"AAS_RUN_ID": run_id, "AAS_RUN_GRANT": run_grants.issue(studio, run_id, pid), **extra}


def two_projects(studio: Studio) -> tuple[str, str]:
    a = studio.create_project(OWNER, "alpha", "blank", "")["id"]
    b = studio.create_project(OWNER, "beta", "blank", "")["id"]
    activity.record(studio, "agent.text", project_id=a, payload={"text": "event of A"})
    activity.record(studio, "agent.text", project_id=b, payload={"text": "event of B (must not leak)"})
    return a, b


# ---------------------------------------------------------------- H1
async def test_h1_grant_pins_activity_list_to_the_run_project(studio: Studio) -> None:
    a, b = two_projects(studio)
    async with session(studio, **grant_env(studio, RUN_A, a)) as s:
        res = body(await s.call_tool("activity_list", {}))
        assert res["ok"] is True, res
        pids = {e["projectId"] for e in res["result"]}
        assert pids == {a}, pids
        assert "must not leak" not in json.dumps(res)
        other = body(await s.call_tool("activity_list", {"projectId": b}))
        assert other["ok"] is False and other["error"] == "policy_denied", other
        own = body(await s.call_tool("activity_list", {"projectId": a}))
        assert own["ok"] is True and {e["projectId"] for e in own["result"]} == {a}


async def test_h1_positive_control_external_mcp_sees_every_project(studio: Studio) -> None:
    a, b = two_projects(studio)
    studio.set_external_mcp(OWNER, True)
    async with session(studio) as s:  # no grant: the owner's external switch, not bound to a project
        res = body(await s.call_tool("activity_list", {}))
        assert {a, b} <= {e["projectId"] for e in res["result"]}


async def test_h1_grant_refuses_other_project_on_every_project_tool(studio: Studio) -> None:
    a, b = two_projects(studio)
    async with session(studio, **grant_env(studio, RUN_A, a)) as s:
        for name, args in (("project_inspect", {"projectId": b}), ("code_list_files", {"projectId": b}),
                           ("code_read_file", {"projectId": b, "path": "src/App.tsx"}),
                           ("plan_submit", {"projectId": b, "title": "t", "steps": []}),
                           ("device_install", {"projectId": b})):
            res = body(await s.call_tool(name, args))
            assert res["ok"] is False and res["error"] == "policy_denied", (name, res)


# ---------------------------------------------------------------- P5f
def _approve_when_pending(studio: Studio, decision: str = "approve", run_id: str = RUN_A) -> threading.Thread:
    def go() -> None:
        deadline = time.time() + 30
        while time.time() < deadline:
            recs = [r for r in plans.list_records(studio) if r.get("kind") == "plan" and r["runId"] == run_id]
            if recs:
                plans.decide(studio, OWNER, recs[0]["id"], decision, "change step 2")
                return
            time.sleep(0.1)
    t = threading.Thread(target=go, daemon=True)
    t.start()
    return t


async def test_p5f_write_refused_without_plan_allowed_after_owner_approval(studio: Studio) -> None:
    a, _ = two_projects(studio)
    rev = studio.get_revision(a)
    assert plans.gate_enabled(studio) is True  # safe default ON
    async with session(studio, **grant_env(studio, RUN_A, a)) as s:
        for name, args in (("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx", "content": "x"}),
                           ("code_delete_file", {"projectId": a, "baseRevision": rev, "path": "src/App.tsx"}),
                           ("app_build", {"projectId": a, "target": "web"})):
            res = body(await s.call_tool(name, args))
            assert res["ok"] is False and res["error"] == "plan_required", (name, res)
        assert studio.get_revision(a) == rev
        t = _approve_when_pending(studio)
        sub = body(await s.call_tool("plan_submit", {"projectId": a, "title": "Build", "steps": ["one", "two"]}))
        t.join(5)
        assert sub["ok"] is True and sub["result"]["status"] == "approved", sub  # the call waited for the owner
        w = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx",
                                                       "content": "export {}\n"}))
        assert w["ok"] is True, w
        d = body(await s.call_tool("code_delete_file", {"projectId": a, "baseRevision": rev + 1, "path": "src/X.tsx"}))
        assert d["ok"] is True, d
    assert run_grants.plan_of(studio, RUN_A)


async def test_p5f_rejected_plan_keeps_writes_refused_and_returns_the_note(studio: Studio) -> None:
    a, _ = two_projects(studio)
    rev = studio.get_revision(a)
    async with session(studio, **grant_env(studio, RUN_A, a)) as s:
        t = _approve_when_pending(studio, "edit")
        sub = body(await s.call_tool("plan_submit", {"projectId": a, "title": "Build", "steps": ["one"]}))
        t.join(5)
        assert sub["result"]["status"] == "rejected" and "change step 2" in sub["result"]["note"], sub
        res = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx",
                                                         "content": "x"}))
        assert res["error"] == "plan_required"


async def test_p5f_plan_approved_for_another_run_does_not_unlock(studio: Studio) -> None:
    a, _ = two_projects(studio)
    rev = studio.get_revision(a)
    other = plans.submit(studio, Actor("agent", f"runner:{RUN_B}", "mcp"), a, "other", ["x"], run_id=RUN_B)
    plans.decide(studio, OWNER, other["planId"], "approve")
    async with session(studio, **grant_env(studio, RUN_A, a)) as s:
        res = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx",
                                                         "content": "x"}))
        assert res["error"] == "plan_required", res


async def test_p5f_plan_wait_times_out(studio: Studio) -> None:
    a, _ = two_projects(studio)
    async with session(studio, **grant_env(studio, RUN_A, a, AAS_PLAN_WAIT_SECONDS="1")) as s:
        t0 = time.time()
        sub = body(await s.call_tool("plan_submit", {"projectId": a, "title": "Build", "steps": ["one"]}))
        assert sub["result"]["status"] == "timeout" and time.time() - t0 < 15, sub


async def test_p5f_positive_control_toggle_off_allows_writes_without_plan(studio: Studio) -> None:
    a, _ = two_projects(studio)
    rev = studio.get_revision(a)
    with pytest.raises(PolicyDenied):
        plans.set_gate(studio, AGENT, False)
    assert plans.gate_enabled(studio) is True
    plans.set_gate(studio, OWNER, False)
    async with session(studio, **grant_env(studio, RUN_A, a)) as s:
        w = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx",
                                                       "content": "export {}\n"}))
        assert w["ok"] is True, w
        plans.set_gate(studio, OWNER, True)  # re-read on every call: back ON takes effect immediately
        res = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev + 1, "path": "src/Y.tsx",
                                                         "content": "x"}))
        assert res["error"] == "plan_required"


async def test_p5f_external_mcp_needs_an_approved_plan_of_its_own_session(studio: Studio) -> None:
    a, _ = two_projects(studio)
    rev = studio.get_revision(a)
    studio.set_external_mcp(OWNER, True)
    stale = plans.submit(studio, AGENT, a, "someone else's", ["x"])
    plans.decide(studio, OWNER, stale["planId"], "approve")
    async with session(studio) as s:
        res = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx",
                                                         "content": "x"}))
        assert res["error"] == "plan_required", res
        sub = body(await s.call_tool("plan_submit", {"projectId": a, "title": "Build", "steps": ["one"]}))
        assert sub["result"]["status"] == "pending"  # external sessions are not paused; the gate still holds
        plans.decide(studio, OWNER, sub["result"]["planId"], "approve")
        w = body(await s.call_tool("code_write_file", {"projectId": a, "baseRevision": rev, "path": "src/X.tsx",
                                                       "content": "export {}\n"}))
        assert w["ok"] is True, w

