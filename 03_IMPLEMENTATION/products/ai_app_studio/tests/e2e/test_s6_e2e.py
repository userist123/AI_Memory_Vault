"""S6 end to end on this PC: backend + preview server + UI preview server + browser window, the built-in runner driven
by the FAKE CLI fixture, real MCP stdio server, real offline Docker build, real preview server.

create project -> POST /runs -> fake CLI calls MCP tools (plan_submit, code_write_file, app_build) -> files written ->
docker build (--network none) -> preview p-<id>.localhost serves the new bundle -> events visible in the desktop UI
(headless Chromium screenshot; set AAS_E2E_SHOT to keep it). Skipped when Docker, the web-build image, node/pnpm
or the desktop node_modules are missing.
"""
from __future__ import annotations

import http.client
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.request
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from tests.winpaths import system_root

PRODUCT = Path(__file__).resolve().parents[2]
DESKTOP = PRODUCT / "apps" / "desktop"
FAKE = PRODUCT / "tests" / "fixtures" / "fake_claude.py"
API, PREVIEW, UI = 27875, 27880, 24174
TOKEN = "e2e" + "0123456789abcdef" * 3
DOCKER = shutil.which("docker")
PNPM = shutil.which("pnpm")
NODE = shutil.which("node")


def _image() -> bool:
    if not DOCKER:
        return False
    r = subprocess.run([DOCKER, "image", "inspect", "aas-web-build:dev", "--format", "{{.Id}}"],  # noqa: S603
                       capture_output=True, text=True, timeout=60)
    return r.returncode == 0


def _real_free_ok() -> bool:
    """The backend runs in a subprocess, so the hermetic disk_free fake (tests/hermetic_disk.py) cannot reach its build gate:
    this end-to-end test needs the real 5 GiB on the system and temp drives (evaluated at import, before any fake)."""
    roots = {os.environ.get("SystemDrive", "C:") + "\\", str(Path(os.environ.get("TEMP", PRODUCT)).anchor or PRODUCT)}
    return all(shutil.disk_usage(r).free >= 5 * 1024 ** 3 for r in roots)


pytestmark = [
    pytest.mark.skipif(
        not (_image() and PNPM and NODE and (DESKTOP / "node_modules").exists()),
        reason="docker + aas-web-build:dev image + node + pnpm + apps/desktop/node_modules required"),
    pytest.mark.skipif(not _real_free_ok(), reason="needs >= 5 GiB really free on the system / temp drive (build gate "
                       "runs in the backend subprocess)"),
    pytest.mark.real_disk,
]


def call(method: str, path: str, body: Any = None, *, token: bool = True) -> Any:
    req = urllib.request.Request(f"http://127.0.0.1:{API}{path}", method=method,
                                 data=None if body is None else json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", **({"Authorization": f"Bearer {TOKEN}"} if token else {})})
    with urllib.request.urlopen(req, timeout=30) as r:  # noqa: S310 - fixed loopback URL
        return json.loads(r.read() or b"null")


def wait_for(fn, what: str, seconds: float = 60):  # type: ignore[no-untyped-def]
    end = time.time() + seconds
    last: Exception | None = None
    while time.time() < end:
        try:
            v = fn()
            if v:
                return v
        except Exception as e:  # noqa: BLE001
            last = e
        time.sleep(0.4)
    raise AssertionError(f"timeout waiting for {what}: {last}")


def preview_get(pid: str, path: str = "/") -> tuple[int, bytes]:
    c = http.client.HTTPConnection("127.0.0.1", PREVIEW, timeout=10)
    c.request("GET", path, headers={"Host": f"p-{pid[4:]}.localhost:{PREVIEW}"})
    r = c.getresponse()
    return r.status, r.read()


@pytest.fixture(scope="module")
def studio_stack(tmp_path_factory: pytest.TempPathFactory) -> Iterator[dict[str, Any]]:
    data = tmp_path_factory.mktemp("e2e") / "data"
    env = {k: os.environ[k] for k in ("SYSTEMROOT", "PATH", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP",
                                       "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG", "PLAYWRIGHT_BROWSERS_PATH") if k in os.environ}
    env.update(AAS_DATA_DIR=str(data), AAS_API_PORT=str(API), AAS_PREVIEW_PORT=str(PREVIEW), PYTHONUTF8="1",
               AAS_DEV_ORIGINS=f"http://127.0.0.1:{UI}")
    py = sys.executable
    procs: list[subprocess.Popen[bytes]] = []
    try:
        be = subprocess.Popen([py, "-X", "utf8", "-m", "apps.backend", "--token-stdin", "--runner-cli-json",  # noqa: S603
                               json.dumps([py, str(FAKE)])], cwd=PRODUCT, env=env, stdin=subprocess.PIPE)
        procs.append(be)
        assert be.stdin is not None
        be.stdin.write(TOKEN.encode() + b"\n")
        be.stdin.flush()
        # S9 B3: the backend hosts the preview server (one process, as in the packaged app)
        wait_for(lambda: call("GET", "/api/v1/health", token=False)["status"] == "ok", "backend health")
        ui_env = {**env, "AAS_TEST_API_PORT": str(API), "AAS_TEST_PREVIEW_PORT": str(PREVIEW), "AAS_TEST_UI_PORT": str(UI)}
        subprocess.run([PNPM, "exec", "vite", "build"], cwd=DESKTOP, env=ui_env, check=True, capture_output=True, timeout=300)  # noqa: S603
        ui = subprocess.Popen([PNPM, "exec", "vite", "preview"], cwd=DESKTOP, env=ui_env, stdout=subprocess.DEVNULL,  # noqa: S603
                              stderr=subprocess.DEVNULL)
        procs.append(ui)
        wait_for(lambda: urllib.request.urlopen(f"http://127.0.0.1:{UI}/", timeout=3).status == 200, "ui server")  # noqa: S310
        yield {"data": data, "env": env}
    finally:
        for p in reversed(procs):
            if p.poll() is None:
                subprocess.run([os.path.join(system_root(), "System32", "taskkill.exe"),  # noqa: S603
                                "/PID", str(p.pid), "/T", "/F"], capture_output=True, timeout=30)


def scenario(marker: str, pid: str) -> str:
    app = f"export default function App() {{\n  return <main><h1>{marker}</h1></main>;\n}}\n"
    return json.dumps({
        "project_id": pid,
        "events": [
            {"type": "assistant", "message": {"content": [
                {"type": "thinking", "thinking": "hidden reasoning"},
                {"type": "text", "text": f"I will build the {marker} screen."}]}},
        ],
        "mcp_calls": [
            {"tool": "plan_submit", "args": {"projectId": "$project", "title": f"Build {marker}",
                                             "steps": ["write App.tsx", "build", "preview"]}},
            {"tool": "project_inspect", "args": {"projectId": "$project"}},  # gives the fake the current revision
            {"tool": "code_write_file", "args": {"projectId": "$project", "baseRevision": "$rev", "path": "src/App.tsx",
                                                 "content": app}},
            {"tool": "app_build", "args": {"projectId": "$project", "target": "web"}},
        ],
        "final_events": [{"type": "result", "subtype": "success", "num_turns": 3, "total_cost_usd": 0.0421,
                          "duration_ms": 1234, "usage": {"input_tokens": 1500, "output_tokens": 420}}],
    })


SEEN_PLANS: list[dict[str, Any]] = []


def run_to_end(pid: str, marker: str) -> str:
    r = call("POST", "/api/v1/runs", {"projectId": pid, "prompt": scenario(marker, pid)})
    run = r["runId"]

    def finished() -> bool:
        # P5f (gate ON by default): the run pauses on plan_submit until the owner decides; the test is the owner
        for rec in call("GET", "/api/v1/approvals"):
            if rec.get("kind") == "plan" and rec.get("runId") == run:
                SEEN_PLANS.append(rec)
                call("POST", f"/api/v1/plans/{rec['id']}/decide", {"decision": "approve", "note": "",
                                                                     "contentSha256": rec["contentSha256"]})
        return any(e["kind"] == "run.finished" and e["runId"] == run
                   for e in call("GET", f"/api/v1/activity?limit=1000&projectId={pid}"))
    wait_for(finished, f"run {run} to finish", 300)
    return run


def test_end_to_end_fake_cli_to_preview_and_ui(studio_stack: dict[str, Any], tmp_path: Path) -> None:
    shot = Path(os.environ.get("AAS_E2E_SHOT") or tmp_path / "e2e.png")
    proj = call("POST", "/api/v1/projects", {"name": "E2E habit tracker"})
    pid = proj["id"]
    # the UI window is already open and live while the runs happen
    win = subprocess.Popen([NODE, "scripts/studio_window.mjs", "--url", f"http://127.0.0.1:{UI}", "--api",  # noqa: S603
                            f"http://127.0.0.1:{API}", "--preview-port", str(PREVIEW), "--headless", "--shot", str(shot),
                            "--wait-selector", "[data-testid=step]", "--min", "4",
                            "--wait-text", "[data-testid=revision]=rev 3", "--settle-ms", "6000"],
                           cwd=DESKTOP, stdin=subprocess.PIPE, env={k: v for k, v in studio_stack["env"].items()
                                                                     if not k.startswith("AAS_")})
    assert win.stdin is not None
    win.stdin.write(TOKEN.encode() + b"\n")
    win.stdin.flush()
    try:
        # first run: the stream the UI shows
        run1 = run_to_end(pid, "FIRST-MARKER")
        events = call("GET", f"/api/v1/activity?limit=1000&projectId={pid}")
        kinds = [e["kind"] for e in events]
        for k in ("run.started", "agent.text", "step.started", "tool.call", "tool.result", "plan.proposed", "file.diff",
                  "build.started", "build.log", "build.finished", "preview.revision", "cost.update", "run.finished"):
            assert k in kinds, (k, sorted(set(kinds)))
        assert "hidden reasoning" not in json.dumps(events)
        fin = next(e for e in events if e["kind"] == "run.finished" and e["runId"] == run1)["payload"]
        assert fin["status"] == "success" and fin["costUsd"] == pytest.approx(0.0421)
        assert next(e for e in events if e["kind"] == "build.finished")["payload"]["status"] == "ok"
        # file written through MCP, preview serves the built bundle
        f = call("GET", f"/api/v1/projects/{pid}/file?path=src/App.tsx")
        assert "FIRST-MARKER" in f["content"]
        st, html = preview_get(pid)
        assert st == 200 and b"<script" in html
        js = re.search(rb'src="(/assets/[^"]+\.js)"', html) or re.search(rb'src="(\./assets/[^"]+\.js)"', html)
        assert js, html[:300]
        st, bundle = preview_get(pid, js.group(1).decode().lstrip("."))
        assert st == 200 and b"FIRST-MARKER" in bundle
        # the plan was a server record the owner saw and approved (rendered only from this route); the run waited
        plan = next(r for r in SEEN_PLANS if r["runId"] == run1)
        assert plan["title"] == "Build FIRST-MARKER" and plan["steps"] == ["write App.tsx", "build", "preview"]
        assert not [r for r in call("GET", "/api/v1/approvals") if r.get("kind") == "plan"]  # decided, none pending
        decided = [e for e in events if e["kind"] == "plan.decided" and e["runId"] == run1]
        assert decided and decided[0]["payload"]["decision"] == "approve"
        # second run: the preview reloads to the new bundle
        run_to_end(pid, "SECOND-MARKER")
        st, html2 = preview_get(pid)
        js2 = re.search(rb'src="\.?(/assets/[^"]+\.js)"', html2)
        assert js2 and st == 200
        st, bundle2 = preview_get(pid, js2.group(1).decode())
        assert b"SECOND-MARKER" in bundle2 and b"FIRST-MARKER" not in bundle2
        # the UI window saw the run
        assert win.wait(timeout=180) == 0
        assert shot.exists() and shot.stat().st_size > 20_000
    finally:
        if win.poll() is None:
            win.kill()
