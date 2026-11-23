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
import datetime
import hashlib
import json
import os
import re
import shutil
import socket
import sys
from pathlib import Path
from collections.abc import AsyncIterator
from typing import Any, Literal

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "core"))

from fastapi import FastAPI, Query, Request, WebSocket  # noqa: E402
from fastapi.middleware.cors import CORSMiddleware  # noqa: E402
from fastapi.responses import JSONResponse, Response  # noqa: E402
from pydantic import BaseModel, ConfigDict, Field, StrictBool, StrictInt  # noqa: E402
from aas_build import capture  # noqa: E402
from aas_build import export as export_mod  # noqa: E402
from aas_build import service as build_service  # noqa: E402
from aas_build.preview_server import PreviewServer  # noqa: E402
from aas_device import service as dsvc  # noqa: E402
from aas_device import video_relay  # noqa: E402
from aas_runner import claude as runner_mod  # noqa: E402
from aas_runner import profile as runner_profile  # noqa: E402
from aas_runner.claude import Runner  # noqa: E402
from studio_core import __version__, activity, plans  # noqa: E402
from studio_core.app_kind import BLANK_FILES, register_app_kind  # noqa: E402,F401
from studio_core.config import Settings, load_settings  # noqa: E402
from studio_core.errors import ApprovalRequired, NotFound, StudioError, ValidationFailed  # noqa: E402
from studio_core.policy import Actor  # noqa: E402
from studio_core.services import Studio  # noqa: E402

from .security import MIN_TOKEN_CHARS, BoundaryMiddleware  # noqa: E402
from .headers import install as install_api_hygiene  # noqa: E402
from . import chat_routes  # noqa: E402
from . import export_routes  # noqa: E402
from . import qa_routes  # noqa: E402

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


class RunnerProfile(Strict):
    profile: Literal["full", "isolated"]  # exact strings only; anything else is 422


class ProjectDelete(Strict):
    approvalId: str | None = Field(default=None, pattern=r"^apr_[0-9a-f]{16}$")  # noqa: N815


class Retention(Strict):
    days: StrictInt = Field(ge=1, le=activity.RETENTION_MAX_DAYS)


class ExportReq(Strict):
    target: Literal["web", "android", "desktop"]
    approvalId: str | None = Field(default=None, pattern=r"^apr_[0-9a-f]{16}$")  # noqa: N815


class ExportFolder(Strict):
    path: str = Field(min_length=3, max_length=260)


RETENTION_SWEEP_SECONDS = 6 * 3600


_DEV_ORIGIN = re.compile(r"^http://(?:127\.0\.0\.1|localhost):[0-9]{4,5}$")


def parse_dev_origins(value: str) -> list[str]:
    return [o.strip() for o in value.split(",") if o.strip()]


def create_app(settings: Settings, token: str, *, extra_hosts: tuple[str, ...] = (),
               runner_cli: list[str] | None = None, runner_extra_env: dict[str, str] | None = None,
               dev_origins: list[str] | None = None, preview: bool = False) -> FastAPI:
    """``runner_cli`` is the argv prefix that starts the agent CLI (default: the installed ``claude``).
    ``dev_origins`` adds exact loopback UI origins (``http://127.0.0.1:<port>``) for the vite dev/preview servers of
    scripts/run_studio.ps1 and the tests. Both come from the process command line / environment of the backend
    only - no route or setting can change them, and nothing but a loopback http origin with a port is accepted.
    ``preview`` (S9, B3): the per-project preview server (127.0.0.1:<preview port>) runs inside this process and
    starts/stops with the API (lifespan), so the packaged shell needs no second process; main() passes True,
    tests opt in."""
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
    video_hub = video_relay.VideoHub()  # S8: live emulator mirror, fed by the run's MCP child over a keyed pipe
    # B7: resolved once, here, to an absolute path; not installed -> [] and every run start is refused (503)
    runner = Runner(studio, cli=runner_cli or runner_mod.resolve_cli() or [], extra_env=runner_extra_env,
                    purge_stale=False, video_hub=video_hub)  # main() purges once the API port is bound (S7 review L2)

    @contextlib.asynccontextmanager
    async def lifespan(a: FastAPI) -> AsyncIterator[None]:
        srv: PreviewServer | None = None
        if preview:  # B3: bind failure (port taken) fails the startup loudly instead of serving without previews
            srv = PreviewServer(build_service.site_root_for(settings), port=settings.preview_port,
                                extra_frame_ancestors=dev_origins or ()).start()
        a.state.preview_server = srv
        stop_sweep = asyncio.Event()

        async def sweep() -> None:  # B4: activity retention, at startup and every RETENTION_SWEEP_SECONDS
            while not stop_sweep.is_set():
                with contextlib.suppress(Exception):
                    await asyncio.to_thread(activity.prune, studio)
                with contextlib.suppress(Exception):
                    await asyncio.to_thread(purge_trash, settings, activity.retention_days(studio))
                with contextlib.suppress(TimeoutError):
                    await asyncio.wait_for(stop_sweep.wait(), RETENTION_SWEEP_SECONDS)

        sweeper = asyncio.create_task(sweep())
        try:
            yield
        finally:
            stop_sweep.set()
            sweeper.cancel()
            # backend shutdown: every run is stopped (grant revoked -> its device stops -> process tree killed)
            await asyncio.to_thread(a.state.runner.stop_all)
            await asyncio.to_thread(video_hub.close)
            if srv is not None:
                await asyncio.to_thread(srv.stop)

    app = FastAPI(title="AI App Studio API", version=__version__, docs_url=None, redoc_url=None,
                  openapi_url=f"{API}/openapi.json", lifespan=lifespan)
    app.state.studio = studio
    app.state.runner = runner
    app.state.video_hub = video_hub
    export_routes.register(app, studio, OWNER, runner)  # owner 2026-11-23: one-click APK build + export folder
    qa_routes.register(app, studio, runner)  # W2: quality gates + Auto-polish (qa_routes.py)

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
        rows = studio.list_projects(OWNER, includeArchived)
        for p in rows:  # project picker: when the current preview site was switched in (None before the first build)
            p["lastBuildAt"] = _last_build_at(str(p.get("id", "")))
        return rows

    def _last_build_at(pid: str) -> str | None:
        if not re.fullmatch(r"prj_[0-9a-f]{16}", pid):
            return None
        try:
            mtime = (build_service.preview_base(settings, pid) / "current").stat().st_mtime
        except OSError:
            return None
        return datetime.datetime.fromtimestamp(mtime, tz=datetime.UTC).isoformat()

    @app.get(f"{API}/projects/{{pid}}/files")
    def list_files(pid: str) -> list[dict[str, Any]]:
        return studio.list_files(OWNER, pid)

    @app.get(f"{API}/projects/{{pid}}/file")
    def read_file(pid: str, path: str = Query(max_length=200)) -> dict[str, Any]:
        return studio.read_file(OWNER, pid, path)

    @contextlib.contextmanager
    def _delete_build_lock(pid: str) -> Any:
        lock = build_service.build_lock(settings, pid)
        try:
            lock.__enter__()
        except build_service.Unavailable as e:  # a build (any project, any process) started after the probe
            raise runner_mod.RunInProgress("a build is in progress: wait for it", projectId=pid) from e
        try:
            yield
        finally:
            lock.__exit__(None, None, None)

    @app.delete(f"{API}/projects/{{pid}}")
    def delete_project(pid: str, b: ProjectDelete | None = None) -> dict[str, Any]:
        """B4, owner only, two steps: without ``approvalId`` an approval request is created and 428 returned; the owner
        approves it (POST /approvals/<id>/decide) and repeats the call with the id. Files go to <data>/trash (purged by
        the retention sweep); the preview site, APK and capture index of the project are removed."""
        if not re.fullmatch(r"prj_[0-9a-f]{16}", pid):
            raise ValidationFailed("invalid project id")
        if runner.live_project() == pid:
            raise runner_mod.RunInProgress("a run of this project is in progress: stop it first", projectId=pid)
        if build_service.building_project(settings) in (pid, "unknown"):  # any process: also an external MCP server (M1)
            raise runner_mod.RunInProgress("a build of this project is in progress: wait for it", projectId=pid)
        with _delete_build_lock(pid):  # S9c review 2 F3: no build may start while the files move
            studio.get_project(OWNER, pid, with_components=False)  # NotFound
            if b is None or not b.approvalId:
                apr = studio.request_approval(OWNER, "project.delete", {"projectId": pid}, project_id=pid)
                raise ApprovalRequired("deleting a project needs the owner's approval", approvalId=apr["approvalId"])
            res = studio.delete_project(OWNER, pid, approval_id=b.approvalId)
            shutil.rmtree(build_service.preview_base(settings, pid), ignore_errors=True)
            capture.forget_project(settings, pid)  # index + the PNGs no other project references
            return res

    @app.post(f"{API}/projects/{{pid}}/export")
    def export_project(pid: str, b: ExportReq) -> dict[str, Any]:
        """S9b, owner: two steps like delete. Without ``approvalId`` -> 428 {approvalId, sha256}; the owner approves
        (POST /approvals/<id>/decide) and repeats with the id. A secret found in the artifact -> 422 export_blocked."""
        if not re.fullmatch(r"prj_[0-9a-f]{16}", pid):
            raise ValidationFailed("invalid project id")
        return export_mod.export(studio, OWNER, pid, b.target, approval_id=b.approvalId)

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

    @app.get(f"{API}/settings/export-folder")
    def get_export_folder() -> dict[str, Any]:
        return {"path": str(export_mod.export_root(studio))}

    @app.put(f"{API}/settings/export-folder")
    def set_export_folder(b: ExportFolder) -> dict[str, Any]:  # S9b: owner only, audited, not inside the data root
        return export_mod.set_export_root(studio, OWNER, b.path)

    @app.get(f"{API}/settings/retention")
    def get_retention() -> dict[str, Any]:
        return {"days": activity.retention_days(studio)}

    @app.put(f"{API}/settings/retention")
    def set_retention(b: Retention) -> dict[str, Any]:  # B4: activity retention in days (default 30), owner only
        return activity.set_retention_days(studio, OWNER, b.days)

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

    @app.get(f"{API}/runs/active")
    def active_run() -> dict[str, Any]:  # the desktop's source of truth for "a run is in progress" (picker lock)
        run_id, project_id = runner.live_run()
        return {"runId": run_id, "projectId": project_id, "running": run_id is not None}

    app.include_router(chat_routes.router(studio, runner))  # owner chat per project (chat_routes.py)

    @app.post(f"{API}/runs/{{run_id}}/stop")
    def stop_run(run_id: str) -> dict[str, Any]:
        if not re.fullmatch(r"run_[0-9a-f]{16}", run_id):
            raise ValidationFailed("invalid run id")
        runner.stop(run_id)
        return {"runId": run_id, "status": "stopping"}

    @app.get(f"{API}/settings/runner-profile")
    def get_runner_profile() -> dict[str, Any]:
        return {"profile": runner_profile.get_profile(studio)}

    @app.put(f"{API}/settings/runner-profile")
    def set_runner_profile(b: RunnerProfile) -> dict[str, Any]:  # owner decision 2026-11-23: default "full"
        return runner_profile.set_profile(studio, OWNER, b.profile)  # owner only, audited; next run uses it

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
        # last seq seen in the table; a row whose stored prev differs marks a missing row. Rows removed by the
        # retention sweep (B4) are not a gap: start at the oldest retained row's prev.
        db_last = max(sent, await asyncio.to_thread(activity.pruned_through, studio))
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

    # ---------------------------------------------------------------- WebSocket: live emulator mirror (S8)
    @app.websocket(f"{API}/device/video")
    async def device_video(ws: WebSocket, projectId: str = "") -> None:  # noqa: N803
        # same boundary (token, Host, Origin) and subprotocol rules as /events; binary frames: 4-byte seq + whole NALs
        offered = [p.strip() for p in ws.headers.get("sec-websocket-protocol", "").split(",")]
        if WS_SUBPROTOCOL not in offered:
            await ws.close(code=4400)
            return
        if not re.fullmatch(r"prj_[0-9a-f]{16}", projectId):
            await ws.close(code=4422)
            return
        try:
            await asyncio.to_thread(studio.get_project, OWNER, projectId, with_components=False)
        except StudioError:
            await ws.close(code=4404)
            return
        loop = asyncio.get_running_loop()
        wake = asyncio.Event()
        try:
            sub = video_hub.subscribe(projectId, wake=lambda: loop.call_soon_threadsafe(wake.set))
        except video_relay.Busy:
            await ws.close(code=4429)
            return
        try:
            await ws.accept(subprotocol=WS_SUBPROTOCOL)

            async def watch_disconnect() -> None:
                while (await ws.receive())["type"] != "websocket.disconnect":
                    pass

            watcher = asyncio.create_task(watch_disconnect())
            try:
                while not watcher.done():
                    for frame in sub.take_frames():
                        await ws.send_bytes(frame)
                    wake.clear()
                    with contextlib.suppress(TimeoutError):
                        await asyncio.wait_for(wake.wait(), 1.0)
            finally:
                watcher.cancel()
        except RuntimeError:  # socket closed under us
            pass
        finally:
            sub.close()

    # middleware order: CORS inside the boundary, boundary outermost
    app.add_middleware(CORSMiddleware, allow_origins=list(settings.allowed_origins), allow_credentials=False,
                       allow_methods=["GET", "POST", "PUT", "DELETE"],
                       allow_headers=["Authorization", "X-AAS-Token", "Content-Type"])
    hosts = (f"{HOST}:{settings.api_port}", f"localhost:{settings.api_port}", *extra_hosts)
    app.add_middleware(BoundaryMiddleware, token=token, allowed_hosts=hosts,
                       allowed_origins=settings.allowed_origins, max_body=settings.max_body_bytes)
    install_api_hygiene(app)  # O6/O7: security headers on every response (outermost), 422 without the input
    return app


class CappedLog:
    """Text stream for the backend's stdout/stderr (B3): ``<data>/logs/backend.log``, rotated to ``backend.log.1`` when
    it passes ``cap`` bytes, so the pair never exceeds about 2 x cap. Thread safe; never raises into the caller."""

    def __init__(self, path: Path, cap: int = 2 * 1024 * 1024) -> None:
        import threading
        self.path, self.cap = Path(path), int(cap)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()
        self._f = open(self.path, "a", encoding="utf-8", errors="replace")  # noqa: SIM115
        self.encoding = "utf-8"

    def write(self, text: str) -> int:
        with self._lock:
            try:
                if self._f.tell() + len(text) > self.cap:
                    self._f.close()
                    os.replace(self.path, self.path.with_name(self.path.name + ".1"))
                    self._f = open(self.path, "a", encoding="utf-8", errors="replace")  # noqa: SIM115
                self._f.write(text[: self.cap])
            except (OSError, ValueError):
                pass
        return len(text)

    def flush(self) -> None:
        with self._lock:
            try:
                self._f.flush()
            except (OSError, ValueError):
                pass

    def isatty(self) -> bool:
        return False

    def close(self) -> None:
        with self._lock:
            try:
                self._f.close()
            except OSError:
                pass


def purge_trash(settings: Settings, days: int, *, now: float | None = None) -> int:
    """B4: deleted projects wait in <data>/trash for the retention period, then go. Only ``prj_*`` entries that are
    real directories (no link) directly under trash are removed. Returns the number removed.
    The age is the deletion time encoded in the folder name (``prj_<id>-<UTC %Y%m%dT%H%M%S>``, set by
    Studio.delete_project): shutil.move keeps the project's old mtime, so the mtime says nothing about the deletion.
    A name whose stamp does not parse is kept (never guessed)."""
    import datetime as _dt
    import time
    trash = settings.data_dir / "trash"
    if not trash.is_dir() or trash.is_symlink():
        return 0
    limit = (now if now is not None else time.time()) - days * 86400
    n = 0
    for d in trash.iterdir():
        m = re.fullmatch(r"prj_[0-9a-f]{16}-([0-9]{8}T[0-9]{6})", d.name)
        if not m:
            continue
        try:
            deleted = _dt.datetime.strptime(m.group(1), "%Y%m%dT%H%M%S").replace(tzinfo=_dt.UTC).timestamp()
        except ValueError:
            continue
        if d.is_dir() and not d.is_symlink() and deleted < limit:
            shutil.rmtree(d, ignore_errors=True)
            n += 1
    return n


def bind_api_socket(port: int) -> socket.socket:
    """Bind the loopback API port exclusively (raises OSError when another process owns it)."""
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        if os.name == "nt":
            s.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)  # SO_REUSEADDR on Windows shares the port
        else:
            s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((HOST, port))
        s.listen(2048)
    except OSError:
        s.close()
        raise
    return s


def serve(app: FastAPI, port: int) -> None:
    """Bind first, purge the stale runner grants second (only a process that owns the port is the single instance),
    then serve on the bound socket."""
    import uvicorn
    try:
        sock = bind_api_socket(port)
    except OSError as e:
        sys.exit(f"API port {port} is not available (is another studio backend running?): {e}")
    try:
        app.state.runner.purge_stale()
        cfg = uvicorn.Config(app, host=HOST, port=port, log_level="warning", server_header=False, proxy_headers=False)
        uvicorn.Server(cfg).run(sockets=[sock])
    finally:
        sock.close()


def main(argv: list[str] | None = None) -> None:
    ap = argparse.ArgumentParser(prog="python -m apps.backend")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--token-stdin", action="store_true", help="read the owner token from the first stdin line")
    g.add_argument("--dev-token-file", action="store_true", help="DEV ONLY: token from <data>/config/api_token")
    ap.add_argument("--log-to-data-root", action="store_true",
                    help="write stdout/stderr and logging to <data>/logs/backend.log (capped, rotated); the desktop "
                         "shell passes this")
    ap.add_argument("--runner-cli-json", help="DEV/TEST: JSON list used as argv prefix instead of the installed `claude` "
                                              "(e.g. the fake CLI fixture); command line only")
    args = ap.parse_args(argv)
    runner_cli: list[str] | None = None
    if args.runner_cli_json:
        runner_cli = json.loads(args.runner_cli_json)
        if not isinstance(runner_cli, list) or not runner_cli or not all(isinstance(x, str) for x in runner_cli):
            sys.exit("--runner-cli-json must be a non-empty JSON list of strings")
    settings = load_settings()
    if args.log_to_data_root:
        import logging
        log = CappedLog(settings.data_dir / "logs" / "backend.log")
        sys.stdout = sys.stderr = log  # type: ignore[assignment]
        logging.basicConfig(stream=log, level=logging.WARNING, force=True,
                            format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    if args.token_stdin:
        token = sys.stdin.readline().strip()
        if len(token) < MIN_TOKEN_CHARS:
            sys.exit(f"token from stdin must be at least {MIN_TOKEN_CHARS} characters")
    else:
        token = settings.api_token()
        print("WARNING: dev token file mode - any local process of this user can read the token", file=sys.stderr)
    dev_origins = parse_dev_origins(os.environ.get("AAS_DEV_ORIGINS", ""))
    serve(create_app(settings, token, runner_cli=runner_cli, dev_origins=dev_origins, preview=True), settings.api_port)


if __name__ == "__main__":
    main()
