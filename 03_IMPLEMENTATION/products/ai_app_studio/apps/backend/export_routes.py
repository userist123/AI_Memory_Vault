"""Owner export/build routes (owner report 2026-11-23: "Nu merge sa o export APK").

``POST /projects/<id>/build`` {target web|android} starts an owner-initiated build in the background (the owner's
own click is the decision: the plan gate is the AGENT's control and stays on the MCP ``app_build`` path; policy
``app.build``, disk gate, build lock, Docker sandbox and audit are the unchanged ``service.build_*`` paths).
``GET /projects/<id>/build`` is its status. ``POST /projects/<id>/export/open-folder`` opens the project's export
folder in Explorer: the path is computed here (export root + project folder), never taken from the request, must
exist, and no link in its chain is followed. Every route sits behind the owner bearer token (BoundaryMiddleware).
"""
from __future__ import annotations

import os
import re
from collections.abc import Callable
from typing import Any, Literal

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict

from aas_build import export as export_mod
from aas_build import owner_build
from studio_core.errors import NotFound, ValidationFailed
from studio_core.policy import Actor

API = "/api/v1"
_PID = re.compile(r"^prj_[0-9a-f]{16}$")


class OwnerBuildReq(BaseModel):
    model_config = ConfigDict(extra="forbid")
    target: Literal["web", "android"]


def _open_in_explorer(path: str) -> None:
    if os.name == "nt":
        os.startfile(path)  # type: ignore[attr-defined]  # noqa: S606 - an existing folder: Explorer opens it


def _pid(pid: str) -> str:
    if not _PID.match(pid):
        raise ValidationFailed("invalid project id")
    return pid


def register(app: FastAPI, studio: Any, owner: Actor, runner: Any,
             *, open_folder: Callable[[str], None] | None = None) -> owner_build.OwnerBuilds:
    builds = owner_build.OwnerBuilds(studio, owner, busy=runner.live_project)
    app.state.owner_builds = builds  # tests swap the build functions here
    app.state.open_folder = open_folder or _open_in_explorer

    @app.post(f"{API}/projects/{{pid}}/build", status_code=202)
    def owner_build_start(pid: str, b: OwnerBuildReq) -> dict[str, Any]:
        """Build the web preview / the debug APK of the CURRENT revision in the background; poll GET .../build."""
        return builds.start(_pid(pid), b.target)

    @app.get(f"{API}/projects/{{pid}}/build")
    def owner_build_status(pid: str) -> dict[str, Any]:
        return builds.status(_pid(pid))

    @app.post(f"{API}/projects/{{pid}}/export/open-folder")
    def export_open_folder(pid: str) -> dict[str, Any]:
        folder = export_mod.project_folder(studio, _pid(pid))
        export_mod._safe_dir(folder)  # noqa: SLF001 - no link anywhere in the chain
        if not folder.is_dir():
            raise NotFound("nothing has been exported for this project yet")
        app.state.open_folder(str(folder))
        return {"path": str(folder)}

    return builds
