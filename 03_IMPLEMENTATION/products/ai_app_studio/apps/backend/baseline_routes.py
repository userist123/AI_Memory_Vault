"""Owner routes for visual baselines and the acceptance checklist (WOW W6; logic in aas_build/baselines.py).

Every route sits behind BoundaryMiddleware (owner bearer token, host and origin checks): only the owner reaches them;
the agent's only way in is the read-only MCP tool ``baseline_status`` (it can compare, never approve).

GET    /api/v1/projects/{pid}/baselines                 -> state: routes, threshold, baselines, last check
POST   /api/v1/projects/{pid}/baselines/approve         {frames?, routes?} -> the current screens become the baselines
POST   /api/v1/projects/{pid}/baselines/check           {intendedRoutes?}  -> capture again + perceptual diff
PUT    /api/v1/projects/{pid}/baselines/threshold       {thresholdPct}
DELETE /api/v1/projects/{pid}/baselines                 -> forget the approved screens
GET    /api/v1/projects/{pid}/baselines/images/{sha256} -> a baseline / current / diff PNG of this project
GET    /api/v1/projects/{pid}/checklist                 -> flows + statements + W3 flow-test result per flow
PUT    /api/v1/projects/{pid}/checklist                 {flows: [{id, title, statements}]}
"""
from __future__ import annotations

import hashlib
import re
from typing import Any, Literal

from fastapi import APIRouter, Response
from pydantic import BaseModel, ConfigDict, Field

from aas_build import baselines
from studio_core.errors import NotFound, ValidationFailed
from studio_core.policy import Actor

API = "/api/v1"
_PID = re.compile(r"prj_[0-9a-f]{16}")
_SHA = re.compile(r"[0-9a-f]{64}")


class ApproveReq(BaseModel):
    model_config = ConfigDict(extra="forbid")
    frames: list[Literal["phone", "tablet", "desktop"]] | None = Field(default=None, max_length=3)
    routes: list[str] | None = Field(default=None, max_length=baselines.MAX_ROUTES)


class CheckReq(BaseModel):
    model_config = ConfigDict(extra="forbid")
    intendedRoutes: list[str] | None = Field(default=None, max_length=baselines.MAX_ROUTES)  # noqa: N815


class ThresholdReq(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    thresholdPct: float = Field(ge=baselines.MIN_THRESHOLD_PCT, le=baselines.MAX_THRESHOLD_PCT)  # noqa: N815


class Flow(BaseModel):
    model_config = ConfigDict(extra="forbid")
    id: str = Field(max_length=40)
    title: str = Field(max_length=baselines.MAX_TITLE_CHARS)
    statements: list[str] = Field(max_length=baselines.MAX_STATEMENTS)


class ChecklistReq(BaseModel):
    model_config = ConfigDict(extra="forbid")
    flows: list[Flow] = Field(max_length=baselines.MAX_FLOWS)


def router(studio: Any, owner: Actor) -> APIRouter:
    r = APIRouter()

    def pid_ok(pid: str) -> str:
        if not _PID.fullmatch(pid):
            raise ValidationFailed("invalid project id")
        baselines.require_project(studio, owner, pid)  # NotFound
        return pid

    @r.get(f"{API}/projects/{{pid}}/baselines")
    def baseline_state(pid: str) -> dict[str, Any]:
        return baselines.status(studio, pid_ok(pid))

    @r.post(f"{API}/projects/{{pid}}/baselines/approve")
    def baseline_approve(pid: str, b: ApproveReq | None = None) -> dict[str, Any]:
        b = b or ApproveReq()
        return baselines.approve(studio, owner, pid_ok(pid), frames=b.frames, routes=b.routes)

    @r.post(f"{API}/projects/{{pid}}/baselines/check")
    def baseline_check(pid: str, b: CheckReq | None = None) -> dict[str, Any]:
        b = b or CheckReq()
        return baselines.check(studio, pid_ok(pid), intended_routes=b.intendedRoutes)

    @r.put(f"{API}/projects/{{pid}}/baselines/threshold")
    def baseline_threshold(pid: str, b: ThresholdReq) -> dict[str, Any]:
        return baselines.set_threshold(studio, owner, pid_ok(pid), b.thresholdPct)

    @r.delete(f"{API}/projects/{{pid}}/baselines")
    def baseline_reset(pid: str) -> dict[str, Any]:
        return baselines.reset(studio, owner, pid_ok(pid))

    @r.get(f"{API}/projects/{{pid}}/baselines/images/{{sha256}}")
    def baseline_image(pid: str, sha256: str) -> Response:
        if not _SHA.fullmatch(sha256):
            raise ValidationFailed("invalid image id")
        p = baselines.baseline_file(studio.settings, pid_ok(pid), sha256)
        if p is None:
            raise NotFound("image not found")
        data = p.read_bytes()
        if len(data) > baselines.capture.MAX_PNG_BYTES or hashlib.sha256(data).hexdigest() != sha256:
            raise NotFound("image not found")  # content-addressed: a changed file is never served
        return Response(content=data, media_type="image/png",
                        headers={"X-Content-Type-Options": "nosniff", "Cache-Control": "no-store",
                                 "Content-Security-Policy": "default-src 'none'"})

    @r.get(f"{API}/projects/{{pid}}/checklist")
    def checklist_get(pid: str) -> dict[str, Any]:
        return baselines.get_checklist(studio.settings, pid_ok(pid))

    @r.put(f"{API}/projects/{{pid}}/checklist")
    def checklist_put(pid: str, b: ChecklistReq) -> dict[str, Any]:
        return baselines.set_checklist(studio, owner, pid_ok(pid), b.model_dump())

    return r
