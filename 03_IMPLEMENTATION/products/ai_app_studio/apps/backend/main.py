"""AI App Studio REST API (/api/v1) + activity WebSocket. Windows loopback 127.0.0.1 only.

Run:  python -m apps.backend --token-stdin      (production: the desktop app writes the token to stdin)
      python -m apps.backend --dev-token-file   (development: token in <data>/config/api_token)

Boundary (security.py, vendored from AI Website Studio 25b3eab): exact Host, Origin allow-list, bearer token >= 32
chars on every route except /health, body limit. The server binds 127.0.0.1 and nothing else (T4, DESIGN.md 7).
"""

from __future__ import annotations

import argparse
import asyncio
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "core"))

from fastapi import FastAPI, Query, Request, WebSocket  # noqa: E402
from fastapi.middleware.cors import CORSMiddleware  # noqa: E402
from fastapi.responses import JSONResponse  # noqa: E402
from pydantic import BaseModel, ConfigDict, Field  # noqa: E402
from studio_core import __version__, activity  # noqa: E402
from studio_core.app_kind import BLANK_FILES, register_app_kind  # noqa: E402,F401
from studio_core.config import Settings, load_settings  # noqa: E402
from studio_core.errors import StudioError  # noqa: E402
from studio_core.policy import Actor  # noqa: E402
from studio_core.services import Studio  # noqa: E402

from .security import MIN_TOKEN_CHARS, BoundaryMiddleware  # noqa: E402

OWNER = Actor("human", "owner", "rest")
HOST = "127.0.0.1"  # fixed: there is no setting, flag or env var that changes the bind address
API = "/api/v1"
POLL_SECONDS = 0.5
REPLAY_BATCH = 200


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


class ExternalMcp(Strict):
    enabled: bool


def create_app(settings: Settings, token: str, *, extra_hosts: tuple[str, ...] = ()) -> FastAPI:
    if len(token) < MIN_TOKEN_CHARS:
        raise ValueError(f"owner token must be at least {MIN_TOKEN_CHARS} characters")
    activity.register_secret(token)
    register_app_kind()
    studio = Studio(settings, kind="app")
    app = FastAPI(title="AI App Studio API", version=__version__, docs_url=None, redoc_url=None,
                  openapi_url=f"{API}/openapi.json")
    app.state.studio = studio

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

    # ---------------------------------------------------------------- activity + owner-only settings
    @app.get(f"{API}/activity")
    def activity_events(afterSeq: int = Query(0, ge=0), limit: int = Query(200, ge=1, le=1000),  # noqa: N803
                        projectId: str | None = None) -> list[dict[str, Any]]:  # noqa: N803
        return activity.list_events(studio, after_seq=afterSeq, limit=limit, project_id=projectId)

    @app.get(f"{API}/settings/external-mcp")
    def get_external_mcp() -> dict[str, Any]:
        return {"enabled": studio.get_external_mcp()}

    @app.put(f"{API}/settings/external-mcp")
    def set_external_mcp(b: ExternalMcp) -> dict[str, Any]:
        return studio.set_external_mcp(OWNER, b.enabled)  # the REST actor is the authenticated owner

    # ---------------------------------------------------------------- WebSocket: replay from the DB, then tail
    @app.websocket(f"{API}/events")
    async def events(ws: WebSocket, afterSeq: int = 0) -> None:  # noqa: N803
        proto = next((p.strip() for p in ws.headers.get("sec-websocket-protocol", "").split(",")
                      if p.strip().startswith("aas.token.")), None)
        await ws.accept(subprotocol=proto)
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
    args = ap.parse_args(argv)
    settings = load_settings()
    if args.token_stdin:
        token = sys.stdin.readline().strip()
        if len(token) < MIN_TOKEN_CHARS:
            sys.exit(f"token from stdin must be at least {MIN_TOKEN_CHARS} characters")
    else:
        token = settings.api_token()
        print("WARNING: dev token file mode - any local process of this user can read the token", file=sys.stderr)
    import uvicorn
    uvicorn.run(create_app(settings, token), host=HOST, port=settings.api_port, log_level="warning",
                server_header=False, proxy_headers=False)


if __name__ == "__main__":
    main()
