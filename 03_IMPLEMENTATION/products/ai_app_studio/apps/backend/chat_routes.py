"""Owner chat with the AI per project (owner request 2026-11-23). Behind the same BoundaryMiddleware as every other
route (bearer token, host and origin checks): only the owner reaches it.

GET  /api/v1/projects/{pid}/chat  -> the turns of the project (prompt, sanitised + redacted reply, status, plan ids)
POST /api/v1/projects/{pid}/chat  -> {"message": "..."}: one new turn = one runner run with chat continuity
                                     (aas_runner/chat.py); 409 run_in_progress while any run is live (B6)
"""
from __future__ import annotations

import re
from typing import Any

from aas_runner import chat as chat_mod
from aas_runner import profile as runner_profile
from fastapi import APIRouter
from pydantic import BaseModel, ConfigDict, Field
from studio_core.errors import ValidationFailed

API = "/api/v1"
_PID = re.compile(r"prj_[0-9a-f]{16}")
MAX_MESSAGE = 4000  # = the chat.prompt text cap in activity.PAYLOAD_FIELDS: the turn is recorded whole


class ChatMessage(BaseModel):
    model_config = ConfigDict(extra="forbid")
    message: str = Field(min_length=1, max_length=MAX_MESSAGE)


def _check(pid: str) -> None:
    if not _PID.fullmatch(pid):
        raise ValidationFailed("invalid project id")


def router(studio: Any, runner: Any) -> APIRouter:
    r = APIRouter()

    @r.get(f"{API}/projects/{{pid}}/chat")
    def chat_history(pid: str) -> dict[str, Any]:
        _check(pid)
        studio.peek_project(chat_mod.CHAT_ACTOR, pid)  # NotFound
        live = runner.live_project()
        resumable = runner_profile.get_profile(studio) == "full" and chat_mod.get_session(studio, pid) is not None
        return {"projectId": pid, "turns": chat_mod.history(studio, pid), "running": live == pid,
                "busy": live is not None, "continuity": "resume" if resumable else "summary"}

    @r.post(f"{API}/projects/{{pid}}/chat", status_code=202)
    def chat_send(pid: str, b: ChatMessage) -> dict[str, Any]:
        _check(pid)
        if not b.message.strip():
            raise ValidationFailed("empty message")
        return {"runId": runner.start(pid, b.message, chat=True), "status": "running"}

    return r
