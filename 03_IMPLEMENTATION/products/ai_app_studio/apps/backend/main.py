"""AI App Studio REST API (/api/v1) + activity WebSocket. Windows loopback 127.0.0.1 only.

Run:  python -m apps.backend --token-stdin      (production: the desktop app writes the token to stdin)
      python -m apps.backend --dev-token-file   (development: token in <data>/config/api_token)

Boundary (security.py, vendored from AI Website Studio 25b3eab): exact Host, Origin allow-list, bearer token >= 32
chars on every route except /health, body limit. The server binds 127.0.0.1 and nothing else (T4, DESIGN.md 7).
"""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import dataclasses
import hashlib
import json
import os
import re
import shutil
import sys
from pathlib import Path
from collections.abc import AsyncIterator
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "core"))

from fastapi import FastAPI, Query, Request, WebSocket  # noqa: E402
from fastapi.middleware.cors import CORSMiddleware  # noqa: E402
from fastapi.responses import JSONResponse, Response  # noqa: E402
from pydantic import BaseModel, ConfigDict, Field, StrictBool  # noqa: E402
from aas_build import capture  # noqa: E402
from aas_device import service as dsvc  # noqa: E402
from aas_runner.claude import Runner  # noqa: E402
from studio_core import __version__, activity, plans  # noqa: E402
from studio_core.app_kind import BLANK_FILES, register_app_kind  # noqa: E402,F401
from studio_core.config import Settings, load_settings  # noqa: E402
from studio_core.errors import NotFound, StudioError, ValidationFailed  # noqa: E402
from studio_core.policy import Actor  # noqa: E402
from studio_core.services import Studio  # noqa: E402

from .security import MIN_TOKEN_CHARS, BoundaryMiddleware  # noqa: E402

OWNER = Actor("human", "owner", "rest")
HOST = "127.0.0.1"  # fixed: there is no setting, flag or env var that changes the bind address
API = "/api/v1"
POLL_SECONDS = 0.5
REPLAY_BATCH = 200
WS_SUBPROTOCOL = "aas.v1"  # the only protocol the server ever selects; the token entry is never echoed back


class Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


class ProjectCreate(Strict):
    name: str = Field(max_length=64)
    template: str = "blank"
    description: str = Field(default="", max_length=2000)


class FileWrite(Strict):
    baseRevision: int = Field(ge=0)
    path: str = Field(max_length=200)
    content: str = Field(max_length=200_000)


class RunStart(Strict):
    projectId: str = Field(pattern=r"^prj_[0-9a-f]{16}$")  # noqa: N815
    prompt: str = Field(min_length=1, max_length=20_000)


class Decision(Strict):
    decision: str = Field(max_length=16)


class PlanDecision(Strict):
    decision: str = Field(max_length=16)
    note: str = Field(default="", max_length=4000)
    contentSha256: str = Field(pattern=r"^[0-9a-f]{64}$")  # S7: the plan content the owner was shown  # noqa: N815


class ExternalMcp(Strict):
    enabled: StrictBool  # JSON true/false only: "yes", 1, "on" are 422


_DEV_ORIGIN = re.compile(r"^http://(?:127\.0\.0\.1|localhost):[0-9]{4,5}$")


def parse_dev_origins(value: str) -> list[str]:
    return [o.strip() for o in value.split(",") if o.strip()]


def create_app(settings: Settings, token: str, *, extra_hosts: tuple[str, ...] = (),
               runner_cli: list[str] | None = None, runner_extra_env: dict[str, str] | None = None,
               dev_origins: list[str] | None = None) -> FastAPI:
    """``runner_cli`` is the argv prefix that starts the agent CLI (default: the installed ``claude``).
    ``dev_origins`` adds exact loopback UI origins (``http://127.0.0.1:<port>``) for the vite dev/preview servers of
    scripts/run_studio.ps1 and the tests. Both come from the process command line / environment of the backend
    only - no route or setting can change them, and nothing but a loopback http origin with a port is accepted."""
    for o in dev_origins or []:
        if not _DEV_ORIGIN.match(o):
            raise ValueError(f"dev origin {o!r} is not http://127.0.0.1:<port> or http://localhost:<port>")
    if dev_origins:
        settings = dataclasses.replace(settings, allowed_origins=(*settings.allowed_origins, *dev_origins))
    if len(token) < MIN_TOKEN_CHARS:
        raise ValueError(f"owner token must be at least {MIN_TOKEN_CHARS} characters")
    activity.register_secret(token)
    register_app_kind()
    studio = Studio(settings, kind="app")
    runner = Runner(studio, cli=runner_cli or [shutil.which("claude") or "claude"], extra_env=runner_extra_env)

    @contextlib.asynccontextmanager
    async def lifespan(a: FastAPI) -> AsyncIterator[None]:
        yield
        # backend shutdown: every run is stopped (grant revoked -> its device stops -> process tree killed)
        await asyncio.to_thread(a.state.runner.stop_all)

    app = FastAPI(title="AI App Studio API", version=__version__, docs_url=None, redoc_url=None,
                  openapi_url=f"{API}/openapi.json", lifespan=lifespan)
    app.state.studio = studio
    app.state.runner = runner

    @app.exception_handler(StudioError)
    async def _studio_error(_r: Request, e: StudioError) -> JSONResponse:
        return JSONResponse(e.to_dict(), status_code=e.http_status)

    @app.get(f"{API}/health")
    def health() -> dict[str, Any]:
        return {"status": "ok", "version": __version__, "app": "ai-app-studio"}

    # ---------------------------------------------------------------- projects and files
    @app.post(f"{API}/projects", status_code=201)
    def create_project(b: ProjectCreate) -> dict[str, Any]:
        return studio.create_project(OWNER, b.name, b.template, b.description)

    @app.get(f"{API}/projects")
    def list_projects(includeArchived: bool = False) -> list[dict[str, Any]]:  # noqa: N803
        return studio.list_projects(OWNER, includeArchived)

    @app.get(f"{API}/projects/{{pid}}/files")
    def list_files(pid: str) -> list[dict[str, Any]]:
        return studio.list_files(OWNER, pid)

    @app.get(f"{API}/projects/{{pid}}/file")
    def read_file(pid: str, path: str = Query(max_length=200)) -> dict[str, Any]:
        return studio.read_file(OWNER, pid, path)

    @app.put(f"{API}/projects/{{pid}}/file")
    def write_file(pid: str, b: FileWrite) -> dict[str, Any]:
        return studio.write_file(OWNER, pid, b.baseRevision, b.path, b.content)

    # ---------------------------------------------------------------- approvals and plans (owner only)
    @app.get(f"{API}/approvals")
    def approvals() -> list[dict[str, Any]]:
        return plans.list_records(studio)  # server records: the UI renders cards only from these (P6b)

    @app.post(f"{API}/approvals/{{approval_id}}/decide")
    def decide_approval(approval_id: str, b: Decision) -> dict[str, Any]:
        return plans.decide_approval(studio, OWNER, approval_id, b.decision)

    @app.post(f"{API}/plans/{{plan_id}}/decide")
    def decide_plan(plan_id: str, b: PlanDecision) -> dict[str, Any]:
        return plans.decide(studio, OWNER, plan_id, b.decision, b.note, content_hash=b.contentSha256)

    # ---------------------------------------------------------------- preview captures (S7), by sha256 id
    @app.get(f"{API}/captures/{{sha256}}")
    def get_capture(sha256: str) -> Response:
        if not re.fullmatch(r"[0-9a-f]{64}", sha256):
            raise ValidationFailed("invalid capture id")
        p = capture.capture_file(settings, sha256)
        if p is None:
            raise NotFound("capture not found")
        data = p.read_bytes()
        if hashlib.sha256(data).hexdigest() != sha256:  # content-addressed: a changed file is never served
            raise NotFound("capture not found")
        return Response(data, media_type="image/png",
                        headers={"X-Content-Type-Options": "nosniff", "Cache-Control": "no-store",
                                 "Content-Security-Policy": "default-src 'none'"})

    # ---------------------------------------------------------------- activity + owner-only settings
    @app.get(f"{API}/activity")
    def activity_events(afterSeq: int = Query(0, ge=0), limit: int = Query(200, ge=1, le=1000),  # noqa: N803
                        projectId: str | None = None) -> list[dict[str, Any]]:  # noqa: N803
        return activity.list_events(studio, after_seq=afterSeq, limit=limit, project_id=projectId)

    @app.get(f"{API}/settings/device")
    def get_device() -> dict[str, Any]:
        return {"enabled": dsvc.is_enabled(studio)}

    @app.put(f"{API}/settings/device")
    def set_device(b: ExternalMcp) -> dict[str, Any]:  # same shape: {"enabled": <strict bool>}
        return dsvc.set_enabled(studio, OWNER, b.enabled)

    @app.get(f"{API}/settings/plan-gate")
    def get_plan_gate() -> dict[str, Any]:
        return {"enabled": plans.gate_enabled(studio)}

    @app.put(f"{API}/settings/plan-gate")
    def set_plan_gate(b: ExternalMcp) -> dict[str, Any]:  # {"enabled": <strict bool>}; P5f, default ON
        return plans.set_gate(studio, OWNER, b.enabled)

    # ---------------------------------------------------------------- built-in runner (owner only)
    @app.post(f"{API}/runs", status_code=202)
    def start_run(b: RunStart) -> dict[str, Any]:
        return {"runId": runner.start(b.projectId, b.prompt), "status": "running"}

    @app.post(f"{API}/runs/{{run_id}}/stop")
    def stop_run(run_id: str) -> dict[str, Any]:
        if not re.fullmatch(r"run_[0-9a-f]{16}", run_id):
            raise ValidationFailed("invalid run id")
        runner.stop(run_id)
        return {"runId": run_id, "status": "stopping"}

    @app.get(f"{API}/settings/external-mcp")
    def get_external_mcp() -> dict[str, Any]:
        return {"enabled": studio.get_external_mcp()}

    @app.put(f"{API}/settings/external-mcp")
    def set_external_mcp(b: ExternalMcp) -> dict[str, Any]:
        return studio.set_external_mcp(OWNER, b.enabled)  # the REST actor is the authenticated owner

    # ---------------------------------------------------------------- WebSocket: replay from the DB, then tail
    @app.websocket(f"{API}/events")
    async def events(ws: WebSocket, afterSeq: int = 0) -> None:  # noqa: N803
        # the boundary middleware already checked the token entry ("aas.token.<t>"); the client must also offer the
        # fixed protocol, which is the one selected - echoing the token entry would leak it into response headers
        offered = [p.strip() for p in ws.headers.get("sec-websocket-protocol", "").split(",")]
        if WS_SUBPROTOCOL not in offered:
            await ws.close(code=4400)
            return
        await ws.accept(subprotocol=WS_SUBPROTOCOL)
        loop = asyncio.get_running_loop()
        wake = asyncio.Event()
        unsub = studio.events.subscribe(lambda _e: loop.call_soon_threadsafe(wake.set))

        async def watch_disconnect() -> None:
            while (await ws.receive())["type"] != "websocket.disconnect":
                pass

        watcher = asyncio.create_task(watch_disconnect())
        sent = max(0, afterSeq)  # last seq sent on this socket (the frame's `prev`)
        db_last = sent  # last seq seen in the table; a row whose stored prev differs marks a missing row
        try:
            while not watcher.done():
                rows = await asyncio.to_thread(activity.list_events, studio, after_seq=sent, limit=REPLAY_BATCH)
                for ev in rows:
                    if ev["prev"] != db_last and ev["prev"] > db_last:
                        await ws.send_json({"type": "gap", "after": db_last, "before": ev["seq"]})
                    await ws.send_json({"seq": ev["seq"], "prev": sent, "kind": ev["kind"], "ts": ev["ts"],
                                        "runId": ev["runId"], "projectId": ev["projectId"], "payload": ev["payload"]})
                    sent = db_last = ev["seq"]
                if len(rows) == REPLAY_BATCH:
                    continue
                wake.clear()
                try:
                    await asyncio.wait_for(wake.wait(), POLL_SECONDS)  # wakes early for in-process events;
                except TimeoutError:  # the poll picks up rows written by the MCP process
                    pass
        except RuntimeError:  # socket closed under us
            pass
        finally:
            unsub()
            watcher.cancel()

    # middleware order: CORS inside the boundary, boundary outermost
    app.add_middleware(CORSMiddleware, allow_origins=list(settings.allowed_origins), allow_credentials=False,
                       allow_methods=["GET", "POST", "PUT"],
                       allow_headers=["Authorization", "X-AAS-Token", "Content-Type"])
    hosts = (f"{HOST}:{settings.api_port}", f"localhost:{settings.api_port}", *extra_hosts)
    app.add_middleware(BoundaryMiddleware, token=token, allowed_hosts=hosts,
                       allowed_origins=settings.allowed_origins, max_body=settings.max_body_bytes)
    return app


def main(argv: list[str] | None = None) -> None:
    ap = argparse.ArgumentParser(prog="python -m apps.backend")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--token-stdin", action="store_true", help="read the owner token from the first stdin line")
    g.add_argument("--dev-token-file", action="store_true", help="DEV ONLY: token from <data>/config/api_token")
    ap.add_argument("--runner-cli-json", help="DEV/TEST: JSON list used as argv prefix instead of the installed `claude` "
                                              "(e.g. the fake CLI fixture); command line only")
    args = ap.parse_args(argv)
    runner_cli: list[str] | None = None
    if args.runner_cli_json:
        runner_cli = json.loads(args.runner_cli_json)
        if not isinstance(runner_cli, list) or not runner_cli or not all(isinstance(x, str) for x in runner_cli):
            sys.exit("--runner-cli-json must be a non-empty JSON list of strings")
    settings = load_settings()
    if args.token_stdin:
        token = sys.stdin.readline().strip()
        if len(token) < MIN_TOKEN_CHARS:
            sys.exit(f"token from stdin must be at least {MIN_TOKEN_CHARS} characters")
    else:
        token = settings.api_token()
        print("WARNING: dev token file mode - any local process of this user can read the token", file=sys.stderr)
    import uvicorn
    dev_origins = parse_dev_origins(os.environ.get("AAS_DEV_ORIGINS", ""))
    uvicorn.run(create_app(settings, token, runner_cli=runner_cli, dev_origins=dev_origins), host=HOST, port=settings.api_port, log_level="warning",
                server_header=False, proxy_headers=False)


if __name__ == "__main__":
    main()
