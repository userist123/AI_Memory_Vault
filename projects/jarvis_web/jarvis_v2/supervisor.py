from __future__ import annotations

import concurrent.futures
import json
import os
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import asdict, dataclass, field
from typing import Any

MEMORY_API = os.getenv("JARVIS_MEMORY_API", "http://127.0.0.1:8000/api/v1").rstrip("/")


@dataclass
class ExecutionEvent:
    name: str
    state: str
    ts: float = field(default_factory=time.time)
    detail: dict[str, Any] = field(default_factory=dict)


@dataclass
class SessionState:
    session_id: str
    request: str
    phase: str = "RECEIVED"
    language: str = "auto"
    selected_agent: dict[str, Any] | None = None
    memory_hits: int = 0
    reply: str = ""
    events: list[ExecutionEvent] = field(default_factory=list)
    started_at: float = field(default_factory=time.time)

    def emit(self, name: str, state: str, **detail: Any) -> None:
        self.phase = state
        self.events.append(ExecutionEvent(name=name, state=state, detail=detail))

    def public(self) -> dict[str, Any]:
        payload = asdict(self)
        payload["events"] = [asdict(event) for event in self.events]
        payload["duration_ms"] = round((time.time() - self.started_at) * 1000, 2)
        return payload


def _request_json(path: str, method: str = "GET", payload: dict[str, Any] | None = None, timeout: int = 60) -> dict[str, Any]:
    url = f"{MEMORY_API}/{path.lstrip('/')}"
    body = None
    headers = {"Accept": "application/json"}
    if payload is not None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=body, headers=headers, method=method)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


class JarvisSupervisor:
    """Small dependency-free Supervisor/Worker engine for JARVIS V2.

    It deliberately keeps the orchestration contract explicit so a LangGraph backend
    can replace this implementation later without changing the UI/API contract.
    """

    def run(self, message: str, history: list[dict[str, Any]] | None = None, model: str = "") -> SessionState:
        clean = message.strip()
        session = SessionState(session_id=str(uuid.uuid4()), request=clean)
        session.emit("TASK_CREATED", "ROUTING")

        if not clean:
            session.emit("TASK_REJECTED", "ERROR", reason="empty_message")
            session.reply = "Spune-mi cu ce vrei să te ajut."
            return session

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            route_future = pool.submit(_request_json, "/route", "POST", {"task": clean}, 30)
            memory_future = pool.submit(_request_json, "/search?q=" + urllib.parse.quote(clean), "GET", None, 30)
            try:
                route = route_future.result()
            except Exception as exc:
                route = {"selected": [], "error": str(exc)}
            try:
                memory = memory_future.result()
            except Exception as exc:
                memory = {"results": [], "error": str(exc)}

        selected = (route.get("selected") or [None])[0]
        session.selected_agent = selected
        session.memory_hits = len(memory.get("results") or [])
        session.language = "ro" if any(ch in clean.lower() for ch in "ăâîșț") else "auto"
        session.emit("MEMORY_RETRIEVED", "SUPERVISING", hits=session.memory_hits)
        session.emit("AGENT_SELECTED", "WORKING", agent=(selected or {}).get("name", "Agent Council"))

        try:
            result = _request_json(
                "/chat",
                "POST",
                {
                    "message": clean,
                    "model": model,
                    "history": history or [],
                },
                180,
            )
            session.reply = str(result.get("reply") or "Nu am primit un răspuns de la modelul local.")
            session.emit("SYNTHESIS_COMPLETED", "RESPONDING", model=result.get("model", ""))
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            session.reply = f"Nu pot ajunge momentan la modelul local. {exc}"
            session.emit("WORKER_FAILED", "ERROR", error=str(exc))
        except Exception as exc:
            session.reply = f"A apărut o problemă la procesarea cererii: {exc}"
            session.emit("WORKER_FAILED", "ERROR", error=str(exc))

        return session
