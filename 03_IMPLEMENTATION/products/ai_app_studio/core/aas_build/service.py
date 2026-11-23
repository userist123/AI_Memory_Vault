"""Build service: project files + studio kit -> offline Docker build (``runner.run_web_build``) -> preview site.

Used by the MCP ``app_build`` tool and nothing else. Flow: build context in ``<data>/build/<id>/ctx`` (studio kit
files + the project's ``src/`` and ``public/`` only, never mounted from the project workspace) -> fixed-argv
``docker run --network none`` -> validated output copied to ``<data>/preview/<project>/site-<ms>`` -> pointer file
``current`` switched -> the preview server (``p-<id16>.localhost``) serves the new site on the next request. A failed
build leaves the previous site in place. Events: build.started, build.log (tail lines), build.error, build.finished,
preview.captured. One build at a time (the machine has 2 cpus / 2 GB reserved for it).
"""
from __future__ import annotations

import os
import re
import shutil
import threading
import time
import uuid
from collections.abc import Callable
from pathlib import Path
from typing import Any

from studio_core import activity
from studio_core.config import Settings
from studio_core.errors import StudioError, Unavailable

from . import runner

PRODUCT = Path(__file__).resolve().parents[2]
KIT = PRODUCT / "kit"
KIT_FILES = ("package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc", "index.html", "tsconfig.json",
             "capacitor.config.ts", "vite.config.ts")
KEEP_SITES = 2
LOG_LINES = 40
_ID16 = re.compile(r"^[0-9a-f]{16}$")
_SITE = re.compile(r"^site-[0-9]{10,16}$")
_LOCK = threading.Lock()


class BuildFailed(StudioError):
    code = "build_failed"
    http_status = 422


def preview_base(settings: Settings, project_id: str) -> Path:
    return settings.data_dir / "preview" / project_id


def site_root_for(settings: Settings) -> Callable[[str], Path | None]:
    """The preview server's resolver: ``<id16>`` (host label) -> the current built site, or None."""
    def resolve(id16: str) -> Path | None:
        if not isinstance(id16, str) or not _ID16.match(id16):
            return None
        base = preview_base(settings, "prj_" + id16)
        try:
            name = (base / "current").read_text(encoding="utf-8")[:40].strip()
        except OSError:
            return None
        if not _SITE.match(name):
            return None
        site = base / name
        return site if site.is_dir() and not site.is_symlink() else None
    return resolve


def _materialise(studio: Any, project_id: str, ctx: Path) -> int:
    ctx.mkdir(parents=True)
    for name in KIT_FILES:
        shutil.copy2(KIT / name, ctx / name)
    ws = studio.workspace(project_id, create=False)
    n = 0
    for top in ("src", "public"):
        for rel in ws.iter_files(top):
            target = ctx / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(ws.read_bytes(rel))
            n += 1
    return n


def _rec(studio: Any, kind: str, project_id: str, run_id: str | None, **payload: Any) -> None:
    try:
        activity.record(studio, kind, project_id=project_id, run_id=run_id, source="build", payload=payload)
    except Exception:  # noqa: BLE001 - the activity stream never breaks a build
        activity._note_dropped(studio)  # noqa: SLF001


def _prune(base: Path, keep: str) -> None:
    sites = sorted((p for p in base.iterdir() if p.is_dir() and _SITE.match(p.name)), key=lambda p: p.name)
    for p in sites[:-KEEP_SITES]:
        if p.name != keep:
            shutil.rmtree(p, ignore_errors=True)


def build_web(studio: Any, project_id: str, *, run_id: str | None = None,
              run_build: Callable[..., runner.BuildResult] = runner.run_web_build,
              docker: str | None = None, image: str | None = None) -> dict[str, Any]:
    if not re.match(r"^prj_[0-9a-f]{16}$", project_id):
        raise BuildFailed("invalid project id")
    if not _LOCK.acquire(blocking=False):
        raise Unavailable("a build is already running")
    t0 = time.monotonic()
    build_root = studio.settings.data_dir / "build" / ("b" + uuid.uuid4().hex[:16])
    try:
        revision = studio.get_revision(project_id)
        docker = docker or shutil.which("docker")
        if not docker or not os.path.isabs(docker):
            raise Unavailable("docker is not installed or not on PATH")
        try:
            image = image or runner.resolve_image(docker)
        except runner.BuildError as e:
            raise Unavailable(str(e)) from e
        _rec(studio, "build.started", project_id, run_id, target="web", revision=revision)
        base = preview_base(studio.settings, project_id)
        base.mkdir(parents=True, exist_ok=True)
        name = f"site-{int(time.time() * 1000):013d}"
        ctx, out = build_root / "ctx", build_root / "out"
        try:
            _materialise(studio, project_id, ctx)
            out.mkdir(parents=True)
            res = run_build(docker=docker, build_id=build_root.name, ctx=ctx, out=out, image=image, dest=base / name)
        except (runner.BuildError, OSError, StudioError) as e:
            msg = str(e)[:1000]
            _rec(studio, "build.error", project_id, run_id, code="build_failed", message=msg)
            _rec(studio, "build.finished", project_id, run_id, status="failed", revision=revision,
                 durationMs=int((time.monotonic() - t0) * 1000))
            raise BuildFailed(msg) from e
        for line in [ln for ln in res.log_tail.splitlines() if ln.strip()][-LOG_LINES:]:
            _rec(studio, "build.log", project_id, run_id, stream="stdout", line=line)
        tmp_ptr = base / "current.tmp"
        tmp_ptr.write_text(name, encoding="utf-8")
        os.replace(tmp_ptr, base / "current")  # atomic pointer switch: the preview reloads from the new site
        _prune(base, name)
        dur = int((time.monotonic() - t0) * 1000)
        _rec(studio, "build.finished", project_id, run_id, status="ok", revision=revision, durationMs=dur)
        _rec(studio, "preview.captured", project_id, run_id, revision=revision, frame="phone",
             sha256=res.dist_sha256)
        return {"ok": True, "revision": revision, "files": len(res.files), "distSha256": res.dist_sha256,
                "durationMs": dur, "url": f"http://p-{project_id[4:]}.localhost:{studio.settings.preview_port}/"}
    finally:
        shutil.rmtree(build_root, ignore_errors=True)
        _LOCK.release()
