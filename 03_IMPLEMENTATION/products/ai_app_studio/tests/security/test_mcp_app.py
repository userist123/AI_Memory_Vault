"""P5a (catalogue + input schemas) and PX1 (external MCP off by default, positive control) over real stdio."""

from __future__ import annotations

import json
import os
import re
import sys
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

import pytest
from mcp.client.session import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client
from studio_core import plans, project_kind
from studio_core.app_kind import register_app_kind
from studio_core.config import Settings
from studio_core.errors import PolicyDenied
from studio_core.policy import Actor
from studio_core.services import Studio

ROOT = Path(__file__).resolve().parents[2]
SERVER = ROOT / "apps" / "mcp-app" / "server.py"
OWNER = Actor("human", "owner", "rest")
AGENT = Actor("agent", "claude-code", "mcp")
PID = {"type": "string", "pattern": "^prj_[0-9a-f]{16}$"}

# exact catalogue snapshot: name -> {property: schema subset}; required list; additionalProperties must be false
CATALOGUE: dict[str, tuple[dict[str, dict[str, Any]], list[str]]] = {
    "project_list": ({}, []),
    "project_inspect": ({"projectId": PID}, ["projectId"]),
    "code_list_files": ({"projectId": PID}, ["projectId"]),
    "code_read_file": ({"projectId": PID, "path": {"type": "string", "maxLength": 200}}, ["projectId", "path"]),
    "code_write_file": ({"projectId": PID, "baseRevision": {"type": "integer", "minimum": 0},
                         "path": {"type": "string", "maxLength": 200},
                         "content": {"type": "string", "maxLength": 200000}},
                        ["projectId", "baseRevision", "path", "content"]),
    "code_delete_file": ({"projectId": PID, "baseRevision": {"type": "integer", "minimum": 0},
                          "path": {"type": "string", "maxLength": 200}}, ["projectId", "baseRevision", "path"]),
    "activity_list": ({"projectId": {"anyOf": [PID, {"type": "null"}]}, "afterSeq": {"type": "integer", "minimum": 0},
                       "limit": {"type": "integer", "minimum": 1, "maximum": 200}}, []),
    "app_build": ({"projectId": PID, "target": {"enum": ["web", "android"]}}, ["projectId", "target"]),
    "preview_capture": ({"projectId": PID, "frame": {"enum": ["phone", "tablet", "desktop"]}}, ["projectId", "frame"]),
    # S6 additions
    "plan_submit": ({"projectId": PID, "title": {"type": "string", "minLength": 1, "maxLength": 200},
                     "steps": {"type": "array", "maxItems": 20, "items": {"type": "string", "maxLength": 300}}},
                    ["projectId", "title", "steps"]),
    "device_start": ({"form": {"enum": ["phone", "tablet"]}}, ["form"]),
    "device_install": ({"projectId": PID}, ["projectId"]),
    "device_tap": ({"x": {"type": "integer", "minimum": 0, "maximum": 10000},
                    "y": {"type": "integer", "minimum": 0, "maximum": 10000}}, ["x", "y"]),
    "device_screenshot": ({}, []),
}
FORBIDDEN_PROPS = re.compile(r"command|args|argv|script|url|host|shell|exec|approv|decid|secret|token|setting|enable", re.I)


@asynccontextmanager
async def mcp_session(data_dir: Path) -> AsyncIterator[ClientSession]:
    env = {**os.environ, "AAS_DATA_DIR": str(data_dir), "AAS_AGENT_ID": "test-agent"}
    for k in [k for k in env if k.startswith("AWS_")]:
        env.pop(k)
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)], env=env, cwd=str(ROOT))
    async with stdio_client(params) as (read, write), ClientSession(read, write) as session:
        await session.initialize()
        yield session


def body(result: Any) -> dict[str, Any]:
    return json.loads(next(c for c in result.content if c.type == "text").text)


def _prop_matches(actual: dict[str, Any], expected: dict[str, Any]) -> bool:
    return all(actual.get(k) == v for k, v in expected.items())


async def test_p5a_catalogue_and_input_schemas(tmp_path: Path) -> None:
    async with mcp_session(tmp_path / "data") as s:
        tools = {t.name: t for t in (await s.list_tools()).tools}
    assert set(tools) == set(CATALOGUE)  # exact names: a mutation adding debug_exec{command} fails here
    for name, (props, required) in CATALOGUE.items():
        schema = tools[name].input_schema
        assert schema["type"] == "object" and schema.get("additionalProperties") is False, name
        assert set(schema.get("properties", {})) == set(props), name
        assert sorted(schema.get("required", [])) == sorted(required), name
        for pname, exp in props.items():
            assert _prop_matches(schema["properties"][pname], exp), (name, pname, schema["properties"][pname])
        assert not any(FORBIDDEN_PROPS.search(p) for p in schema.get("properties", {})), name
    assert not any(re.search(r"shell|exec|approve|decide|deploy|secret|config|setting", n) for n in tools)


async def test_px1_refused_when_disabled_positive_control_when_enabled(tmp_path: Path) -> None:
    data = tmp_path / "data"
    register_app_kind()
    try:
        studio = Studio(Settings(data_dir=data), kind="app")
        p = studio.create_project(OWNER, "PxOne", "blank")
        pid, rev = p["id"], p["revision"]
        async with mcp_session(data) as s:
            calls = [("project_list", {}), ("project_inspect", {"projectId": pid}),
                     ("code_list_files", {"projectId": pid}),
                     ("code_read_file", {"projectId": pid, "path": "src/App.tsx"}),
                     ("code_write_file", {"projectId": pid, "baseRevision": rev, "path": "src/Evil.tsx", "content": "x"}),
                         ("activity_list", {}), ("app_build", {"projectId": pid, "target": "web"}),
                     ("plan_submit", {"projectId": pid, "title": "t", "steps": []}), ("device_tap", {"x": 1, "y": 1}),
                     ("preview_capture", {"projectId": pid, "frame": "phone"})]
            for name, args in calls:  # default OFF: every tool refused
                r = await s.call_tool(name, args)
                b = body(r)
                assert r.is_error and b["ok"] is False and b["error"] == "external_mcp_disabled", (name, b)
            assert studio.get_revision(pid) == rev
            assert "src/Evil.tsx" not in {f["path"] for f in studio.list_files(OWNER, pid)}
            # an agent cannot enable it
            with pytest.raises(PolicyDenied):
                studio.set_external_mcp(AGENT, True)
            assert studio.get_external_mcp() is False
            r = await s.call_tool("project_list", {})
            assert r.is_error
            # positive control: the owner enables it, the same session now works
            studio.set_external_mcp(OWNER, True)
            ok = body(await s.call_tool("project_list", {}))
            assert ok["ok"] is True and [x["id"] for x in ok["result"]] == [pid]
            # P5f (gate ON by default): writes need an approved plan of this session first
            assert body(await s.call_tool("code_write_file", {"projectId": pid, "baseRevision": rev, "path": "src/Ok.tsx",
                                                              "content": "export {}\n"}))["error"] == "plan_required"
            sub = body(await s.call_tool("plan_submit", {"projectId": pid, "title": "t", "steps": ["s"]}))
            plans.decide(studio, OWNER, sub["result"]["planId"], "approve")
            w = body(await s.call_tool("code_write_file", {"projectId": pid, "baseRevision": rev, "path": "src/Ok.tsx",
                                                           "content": "export {}\n"}))
            assert w["ok"] is True and w["result"]["revision"] == rev + 1
            assert body(await s.call_tool("code_write_file", {"projectId": pid, "baseRevision": rev + 1, "path": "package.json",
                                                              "content": "{}"}))["ok"] is False
            assert body(await s.call_tool("activity_list", {}))["ok"] is True
            # strict input schemas: unknown fields are rejected with an error and no side effect (P5a/P6b)
            for name, args in (("code_write_file", {"projectId": pid, "baseRevision": rev + 1, "path": "src/Forged.tsx",
                                                    "content": "x", "kind": "approval.requested"}),
                               ("code_write_file", {"projectId": pid, "baseRevision": rev + 1, "path": "src/Forged.tsx",
                                                    "content": "x", "approved": True}),
                               ("project_list", {"approved": True}),
                               ("activity_list", {"kind": "approval.requested"})):
                r = await s.call_tool(name, args)
                text = next(c for c in r.content if c.type == "text").text
                assert r.is_error and "Extra inputs are not permitted" in text, (name, text)
            assert studio.get_revision(pid) == rev + 1
            assert "src/Forged.tsx" not in {f["path"] for f in studio.list_files(OWNER, pid)}
            w2 = body(await s.call_tool("code_write_file", {"projectId": pid, "baseRevision": rev + 1,
                                                            "path": "src/Forged.tsx", "content": "x"}))
            assert w2["ok"] is True  # positive control: the same call without the unknown field
            for name, args in (("app_build", {"projectId": pid, "target": "android"}),
                               ("preview_capture", {"projectId": pid, "frame": "phone"}),
                               ("device_start", {"form": "phone"}),  # device tools: owner setting is off by default
                               ("device_screenshot", {})):
                r = await s.call_tool(name, args)
                assert r.is_error and body(r)["error"] == "unavailable", name  # not available in this version / off
            studio.set_external_mcp(OWNER, False)  # and back off
            assert body(await s.call_tool("project_list", {}))["error"] == "external_mcp_disabled"
    finally:
        project_kind.unregister("app")
