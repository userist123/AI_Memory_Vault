"""Owner-initiated builds (owner report 2026-11-23 "Nu merge sa o export APK": the studio had no way for the owner to
build the APK, so "Export APK" ended in "no APK has been built"; one click must build it when needed).

The owner's REST actor may build directly (policy ``app.build`` ALLOW + audited inside ``Studio.operation``); the plan
gate is an AGENT control and stays on the MCP ``app_build`` path. A build runs in a background thread (an Android
build takes ~40 s to minutes); the UI polls ``status``. Refused while a run is alive (the AI may be building or
writing: one thing at a time) or while any build holds the cross-process build lock. Disk gate, Docker sandbox
(``--network none``), image resolution and the APK checks are the unchanged ``service.build_*`` paths.
"""
from __future__ import annotations

import threading
import time
from collections.abc import Callable
from typing import Any

from sqlalchemy import select

from studio_core.aas_models import ActivityEvent
from studio_core.errors import StudioError, ValidationFailed
from studio_core.policy import Actor

from . import service

TARGETS = ("web", "android")


def web_revision(studio: Any, project_id: str) -> int | None:
    """Revision of the newest successful web build whose site is still the preview's current site."""
    if service.site_root_for(studio.settings)(project_id[4:]) is None:
        return None
    with studio.session() as db:
        q = (select(ActivityEvent).where(ActivityEvent.project_id == project_id, ActivityEvent.kind == "build.finished")
             .order_by(ActivityEvent.seq.desc()).limit(50))
        for e in db.execute(q).scalars():
            pl = e.payload or {}
            if pl.get("target", "web") == "web" and pl.get("status") == "ok" and isinstance(pl.get("revision"), int):
                return int(pl["revision"])
    return None


class OwnerBuilds:
    def __init__(self, studio: Any, actor: Actor, *, busy: Callable[[], str | None],
                 build_web: Callable[..., Any] | None = None, build_android: Callable[..., Any] | None = None) -> None:
        self.studio = studio
        self.actor = actor
        self.busy = busy  # -> project id of a live run, else None
        self._web = build_web or service.build_web
        self._android = build_android or service.build_android
        self._lock = threading.Lock()
        self._state: dict[str, dict[str, Any]] = {}

    def status(self, project_id: str) -> dict[str, Any]:
        with self._lock:
            return dict(self._state.get(project_id) or {"projectId": project_id, "state": "idle"})

    def start(self, project_id: str, target: str) -> dict[str, Any]:
        if target not in TARGETS:
            raise ValidationFailed("target must be web or android")
        from aas_runner.claude import RunInProgress  # local: aas_runner imports the studio core, not the reverse
        self.studio.get_project(self.actor, project_id, with_components=False)  # NotFound / invalid id
        with self._lock:
            cur = self._state.get(project_id)
            if cur and cur.get("state") == "building":
                return dict(cur)
            live = self.busy()
            if live:
                raise RunInProgress("the AI is working on a project: wait for the run to finish, then build",
                                    projectId=live)
            other = service.building_project(self.studio.settings)
            if other:
                raise RunInProgress("a build is already running: wait for it", projectId=other)
            st = {"projectId": project_id, "target": target, "state": "building", "step": "", "message": "",
                  "startedAt": time.time()}
            self._state[project_id] = st
        threading.Thread(target=self._run, args=(project_id, target), name=f"aas-owner-build-{project_id}",
                         daemon=True).start()
        return dict(st)

    def _set(self, project_id: str, **kv: Any) -> None:
        with self._lock:
            self._state.setdefault(project_id, {"projectId": project_id}).update(kv)

    def _run(self, project_id: str, target: str) -> None:
        try:
            rev = self.studio.get_revision(project_id)
            # the APK packages the preview site: a site older than the app's revision is rebuilt first (no stale APK)
            if target == "web" or web_revision(self.studio, project_id) != rev:
                self._set(project_id, step="web")
                self._web(self.studio, project_id, actor=self.actor)
            if target == "android":
                self._set(project_id, step="android")
                res = self._android(self.studio, project_id, actor=self.actor)
                self._set(project_id, sizeBytes=int((res or {}).get("sizeBytes", 0) or 0))
            self._set(project_id, state="ok", step="", finishedAt=time.time())
        except StudioError as e:
            self._set(project_id, state="failed", message=str(e.message)[:500], finishedAt=time.time())
        except Exception as e:  # noqa: BLE001 - reported to the owner, never raised into the thread runner
            self._set(project_id, state="failed", message=f"{type(e).__name__}: {str(e)[:300]}", finishedAt=time.time())
