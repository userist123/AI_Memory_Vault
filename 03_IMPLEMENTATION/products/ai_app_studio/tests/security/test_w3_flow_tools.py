"""W3 flow tools over real stdio: flow_test_write is plan-gated, refuses a spec outside the Playwright subset before
anything is stored, and writes only tests/flows/<name>.spec.ts; flow_test_run refuses without a web build."""
from __future__ import annotations

from pathlib import Path

from studio_core import plans, project_kind
from studio_core.app_kind import register_app_kind
from studio_core.config import Settings
from studio_core.services import Studio

from .test_mcp_app import OWNER, body, mcp_session

SPEC = "test('home', async ({ page }) => { await expect(page.getByRole('heading')).toBeVisible(); });"


async def test_flow_tools_are_gated_validated_and_narrow(tmp_path: Path) -> None:
    data = tmp_path / "data"
    register_app_kind()
    try:
        studio = Studio(Settings(data_dir=data), kind="app")
        pid = studio.create_project(OWNER, "Flows", "blank")["id"]
        rev = studio.get_revision(pid)
        studio.set_external_mcp(OWNER, True)
        async with mcp_session(data) as s:
            args = {"projectId": pid, "baseRevision": rev, "name": "home", "content": SPEC}
            assert body(await s.call_tool("flow_test_write", args))["error"] == "plan_required"
            sub = body(await s.call_tool("plan_submit", {"projectId": pid, "title": "t", "steps": ["s"]}))
            plans.decide(studio, OWNER, sub["result"]["planId"], "approve", content_hash=sub["result"]["contentSha256"])
            bad = body(await s.call_tool("flow_test_write", {**args, "content": SPEC.replace(
                "await expect(page.getByRole('heading')).toBeVisible();", "await page.evaluate('fetch(1)');")}))
            assert bad["ok"] is False and "evaluate" in bad["message"]
            assert studio.get_revision(pid) == rev  # refused before any write
            for name in ("../src/App", "a.b", "x/y", "", "a" * 42):
                r = await s.call_tool("flow_test_write", {**args, "name": name})
                assert r.is_error, name
            ok = body(await s.call_tool("flow_test_write", args))
            assert ok["ok"] is True and ok["result"]["path"] == "tests/flows/home.spec.ts"
            assert studio.read_file(OWNER, pid, "tests/flows/home.spec.ts")["content"] == SPEC
            run = body(await s.call_tool("flow_test_run", {"projectId": pid}))
            assert run["ok"] is False and "build" in run["message"]
            forged = await s.call_tool("flow_test_run", {"projectId": pid, "timeout": 999999})
            assert forged.is_error  # closed schema: no agent-chosen timeout / config / url
            forged = await s.call_tool("flow_test_run", {"projectId": pid, "url": "http://example.com/"})
            assert forged.is_error
    finally:
        project_kind.unregister("app")
