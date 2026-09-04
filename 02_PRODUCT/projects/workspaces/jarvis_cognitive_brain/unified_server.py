"""Single-process Jarvis gateway combining the web command center and cognitive brain."""

from __future__ import annotations

import argparse
import asyncio
import io
import json
import os
import time
import wave
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

import httpx
import numpy as np
from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse, JSONResponse, Response
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from jarvis.agents.models import AgentRole
from jarvis.config import get_settings
from jarvis.hud.ws_manager import manager
from jarvis.memory.invariants import Principal
from jarvis.runtime import JarvisRuntime, create_runtime

PROJECT_ROOT = Path(__file__).resolve().parents[1]
WEB_ROOT = PROJECT_ROOT / "jarvis_web"
DATA_ROOT = WEB_ROOT / "data"
AGENTS_FILE = DATA_ROOT / "agents.json"


class ChatRequest(BaseModel):
    message: str = Field(min_length=1, max_length=12000)
    history: List[Dict[str, str]] = Field(default_factory=list)
    source: str = "web"


class CouncilRequest(BaseModel):
    query: str = Field(min_length=1, max_length=12000)
    draft: str = Field(default="", max_length=20000)


class ForgeRequest(BaseModel):
    spec: str = Field(min_length=20, max_length=30000)
    language: str = Field(default="csharp", max_length=80)
    mode: str = Field(default="blueprint", max_length=40)


class ForgeDecomposeRequest(BaseModel):
    spec: str = Field(min_length=20, max_length=30000)
    language: str = Field(default="csharp", max_length=80)


class ProposalRequest(BaseModel):
    type: str = "fact"
    category: Optional[str] = None
    content: str = Field(min_length=1, max_length=20000)
    tags: List[str] = Field(default_factory=list)
    relations: List[Dict[str, Any]] = Field(default_factory=list)


class ProposalDecision(BaseModel):
    decision: str


class TTSRequest(BaseModel):
    text: str = Field(min_length=1, max_length=12000)
    voice: str = "default"
    speed: float = Field(default=1.0, ge=0.5, le=2.0)


runtime: Optional[JarvisRuntime] = None
audio_task: Optional[asyncio.Task] = None


def _load_agents() -> List[Dict[str, Any]]:
    try:
        payload = json.loads(AGENTS_FILE.read_text(encoding="utf-8-sig"))
        return list(payload.get("agents", []))
    except Exception:
        return [
            {"id": role.value, "name": role.value.title(), "domain": "Cognitive Brain", "status": "ONLINE", "skills": []}
            for role in AgentRole
            if role is not AgentRole.SUPERVISOR
        ]


AGENTS = _load_agents()


def _require_runtime() -> JarvisRuntime:
    if runtime is None:
        raise HTTPException(status_code=503, detail="Jarvis runtime is starting.")
    return runtime


def _skills() -> List[Dict[str, str]]:
    unique: Dict[str, Dict[str, str]] = {}
    for agent in AGENTS:
        for skill in agent.get("skills", []):
            skill_id = str(skill).strip().lower().replace(" ", "-")
            if skill_id and skill_id not in unique:
                unique[skill_id] = {"id": skill_id, "name": str(skill)}
    return list(unique.values())


def _pending_proposals(jarvis: JarvisRuntime) -> List[Dict[str, Any]]:
    return [
        {
            "candidate_id": note["id"],
            "id": note["id"],
            "type": note.get("category", note.get("type", "knowledge")),
            "content": note.get("content", ""),
            "lifecycle": note.get("lifecycle", "REVIEW"),
            "verification": note.get("verification", "unverified"),
            "created": note.get("created", ""),
        }
        for note in jarvis.storage.query(lifecycle="REVIEW", limit=100)
    ]


def _note_title(note: Dict[str, Any]) -> str:
    return str(note.get("title") or note.get("name") or note.get("category") or note.get("id") or "Memory")


def _wav_bytes(samples: np.ndarray, sample_rate: int) -> bytes:
    audio = np.asarray(samples, dtype=np.float32).reshape(-1)
    audio = np.clip(audio, -1.0, 1.0)
    pcm = (audio * 32767.0).astype(np.int16)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(sample_rate)
        output.writeframes(pcm.tobytes())
    return buffer.getvalue()


def _extract_json_object(text: str) -> Optional[Dict[str, Any]]:
    clean = str(text or "").strip()
    if not clean:
        return None
    try:
        parsed = json.loads(clean)
        return parsed if isinstance(parsed, dict) else None
    except json.JSONDecodeError:
        start = clean.find("{")
        end = clean.rfind("}")
        if start < 0 or end <= start:
            return None
        try:
            parsed = json.loads(clean[start : end + 1])
            return parsed if isinstance(parsed, dict) else None
        except json.JSONDecodeError:
            return None


def _fallback_forge_plan(spec: str, language: str) -> Dict[str, Any]:
    target = ".NET / C#" if language.casefold() == "csharp" else "C++ / native core"
    return {
        "mission": spec.strip(),
        "architecture": [
            f"Modular monolith first, with clear seams for {target} services.",
            "Ports and adapters around domain logic, storage and external tools.",
            "Human approval gates before filesystem, network or deployment actions.",
        ],
        "phases": [
            {"id": "P0", "name": "Discovery and constraints", "owner": "Architect", "status": "READY", "deliverables": ["scope.md", "decision log"], "acceptance": ["users, invariants and non-goals are explicit"]},
            {"id": "P1", "name": "Domain foundation", "owner": "Domain specialist", "status": "READY", "deliverables": ["domain model", "invariants", "contract tests"], "acceptance": ["core use case is deterministic and testable"]},
            {"id": "P2", "name": "Application vertical slice", "owner": "Principal engineer", "status": "READY", "deliverables": ["API/CLI surface", "orchestration", "persistence adapter"], "acceptance": ["one end-to-end scenario runs locally"]},
            {"id": "P3", "name": "Quality and hardening", "owner": "Verifier", "status": "QUEUED", "deliverables": ["unit/integration tests", "observability", "threat model"], "acceptance": ["failure modes and recovery paths are exercised"]},
            {"id": "P4", "name": "Delivery slices", "owner": "Release engineer", "status": "QUEUED", "deliverables": ["CI pipeline", "deployment artifacts", "operator runbook"], "acceptance": ["build, test and rollback are repeatable"]},
        ],
        "risks": [
            {"risk": "scope expansion", "mitigation": "freeze a vertical slice and keep follow-up work explicit"},
            {"risk": "unsafe automation", "mitigation": "approval gates, least privilege and dry-run defaults"},
            {"risk": "generated code drift", "mitigation": "compile/test gates and architecture decision records"},
        ],
        "next_slice": "Define the smallest end-to-end scenario, its contracts and its acceptance tests before generating code.",
    }


async def _ollama_models(jarvis: JarvisRuntime) -> Dict[str, Any]:
    if jarvis.settings.llm_provider == "mock":
        return {"available": True, "default": "mock", "models": ["mock"]}
    try:
        async with httpx.AsyncClient(timeout=1.5) as client:
            response = await client.get(f"{jarvis.settings.ollama_url.rstrip('/')}/api/tags")
            response.raise_for_status()
            models = [str(item.get("name")) for item in response.json().get("models", []) if item.get("name")]
            return {
                "available": bool(models),
                "default": jarvis.settings.ollama_model,
                "models": models,
            }
    except Exception:
        return {"available": False, "default": jarvis.settings.ollama_model, "models": []}


@asynccontextmanager
async def lifespan(_: FastAPI):
    global runtime, audio_task
    runtime = create_runtime()
    from jarvis.hud.state_publisher import publish_state

    runtime.executive.register_state_callback(publish_state)
    await runtime.start_agent_workers()
    if os.getenv("JARVIS_BACKEND_AUDIO", "0").lower() in {"1", "true", "yes"}:
        audio_task = asyncio.create_task(runtime.run_forever())
    try:
        yield
    finally:
        if audio_task is not None:
            audio_task.cancel()
            await asyncio.gather(audio_task, return_exceptions=True)
            audio_task = None
        else:
            await runtime.agent_supervisor.shutdown(wait=True)
            runtime.storage.close()
        runtime = None


app = FastAPI(title="JARVIS Unified AI Command Center", version="1.0.0", lifespan=lifespan)


@app.get("/health")
async def health() -> Dict[str, Any]:
    jarvis = _require_runtime()
    return {
        "status": "ok",
        "service": "JARVIS Unified",
        "port": int(os.getenv("JARVIS_UNIFIED_PORT", "3000")),
        "agents": len(AGENTS),
        "memory_items": jarvis.storage.count(),
    }


@app.get("/api/v1/status")
async def status() -> Dict[str, Any]:
    jarvis = _require_runtime()
    return {
        "status": "online",
        "service": "JARVIS Unified Cognitive Brain",
        "indexed_notes": jarvis.storage.count(),
        "agents": len(AGENTS),
        "llm_provider": jarvis.settings.llm_provider,
        "voice_state": jarvis.audio.state.value,
    }


@app.get("/api/v1/metrics")
async def metrics() -> Dict[str, Any]:
    jarvis = _require_runtime()
    pending = len(_pending_proposals(jarvis))
    return {
        "engine": "SQLite WAL + OODA + Multi-Agent Supervisor",
        "memory_items": jarvis.storage.count(),
        "agents_online": len(AGENTS),
        "agents_total": len(AGENTS),
        "skills_operational": len(_skills()),
        "proposals_pending": pending,
        "agent_workers": jarvis.agent_supervisor.max_workers,
        "agent_active_workers": jarvis.agent_supervisor.active_worker_count,
        "agent_queue_depth": len(jarvis.agent_supervisor.queue),
    }


@app.get("/api/v1/models")
async def models() -> Dict[str, Any]:
    return await _ollama_models(_require_runtime())


@app.get("/api/v1/agents")
async def agents() -> Dict[str, Any]:
    jarvis = _require_runtime()
    rows = [dict(agent) for agent in AGENTS]
    active = jarvis.agent_supervisor.active_worker_count
    for index, row in enumerate(rows):
        row["status"] = "ACTIVE" if index < active else row.get("status", "ONLINE")
    return {"agents": rows, "total": len(rows)}


@app.get("/api/v1/skills")
async def skills(q: str = "") -> Dict[str, Any]:
    rows = _skills()
    if q.strip():
        needle = q.casefold()
        rows = [skill for skill in rows if needle in skill["name"].casefold() or needle in skill["id"]]
    return {"skills": rows, "total": len(rows)}


@app.get("/api/v1/search")
async def search(q: str = "", query: str = "", limit: int = 20) -> Dict[str, Any]:
    jarvis = _require_runtime()
    search_query = (q or query).strip()
    rows = jarvis.storage.search_bm25(search_query, limit=max(1, min(limit, 100)))
    results = []
    for note in rows:
        item = dict(note)
        item["title"] = _note_title(note)
        item["summary"] = str(note.get("content", ""))[:380]
        results.append(item)
    return {"status": "success", "results": results, "total_results": len(results)}


@app.post("/api/v1/chat")
async def chat(request: ChatRequest) -> Dict[str, Any]:
    jarvis = _require_runtime()
    started = time.perf_counter()
    try:
        result = await jarvis.process_text(request.message, source=request.source, principal=Principal.HUMAN)
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"Jarvis cognitive runtime unavailable: {exc}") from exc

    intent = getattr(result.intent.intent_type, "value", str(result.intent.intent_type))
    reply = result.response_text
    if not reply:
        try:
            reply = await jarvis.llm.chat([
                {"role": "system", "content": "You are Jarvis, a precise local AI assistant. Answer in the language of the user."},
                {"role": "user", "content": request.message},
            ])
        except Exception:
            reply = "Am procesat cererea, dar providerul LLM nu a furnizat un răspuns textual."
    reply = reply or "Am procesat cererea, dar nu am primit un răspuns textual."
    duration_ms = round((time.perf_counter() - started) * 1000.0, 2)
    events = [
        {"name": "OBSERVE", "state": "COMPLETED", "detail": {"source": request.source}},
        {"name": "RETRIEVE", "state": "COMPLETED", "detail": {"memory_hits": len(result.context_used)}},
        {"name": "REASON_PLAN_ACT", "state": "COMPLETED", "detail": {"intent": intent}},
        {"name": "REFLECT_CONSOLIDATE", "state": "COMPLETED", "detail": {"reflections": len(result.reflections)}},
    ]
    return {
        "status": "success",
        "reply": reply,
        "response": reply,
        "events": events,
        "intent": intent,
        "context_used": result.context_used,
        "memory_hits": len(result.context_used),
        "duration_ms": duration_ms,
        "selected_agent": {"name": "Cognitive Executive", "role": "supervisor"},
        "model": getattr(jarvis.llm, "model", jarvis.settings.llm_provider),
    }



@app.post("/api/v1/council/review")
async def council_review(request: CouncilRequest) -> Dict[str, Any]:
    jarvis = _require_runtime()
    try:
        review = await jarvis.run_council_review(
            query=request.query,
            draft=request.draft,
        )
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"Council review unavailable: {exc}") from exc
    return {
        "status": "success",
        "query": request.query,
        "retrieval": review.get("retrieval"),
        "verification": review.get("verification"),
        "critique": review.get("critique"),
    }


@app.post("/api/v1/forge")
async def forge(request: ForgeRequest) -> Dict[str, Any]:
    jarvis = _require_runtime()
    started = time.perf_counter()
    mode = request.mode.strip().lower()
    if mode not in {"blueprint", "implementation"}:
        raise HTTPException(status_code=400, detail="Forge mode must be blueprint or implementation.")

    if mode == "blueprint":
        instruction = """
Create a production-grade software blueprint for the requested system. Include:
1. executive goal and non-functional requirements;
2. bounded contexts and architecture decisions;
3. complete repository tree with responsibilities;
4. domain models, API contracts and event flows;
5. security, observability, testing and deployment;
6. staged implementation plan with vertical slices.
"""
    else:
        instruction = """
Create the first compile-ready vertical slice for the requested system. Include:
1. exact solution and project structure;
2. concrete interfaces and data contracts;
3. implementation-ready code blocks for the core path;
4. tests, configuration and run instructions;
5. explicit follow-up slices for the rest of the large program.
6. format every generated source file as `### FILE: relative/path` followed by a fenced code block.
Do not claim files were created; return the implementation package as Markdown.
"""

    prompt = (
        "You are JARVIS Principal Software Architect and Staff Engineer. "
        "Design large maintainable systems, prefer C#/.NET when the language is C#, "
        "and be precise about trade-offs, boundaries and failure modes. "
        f"Target language: {request.language}. Mode: {mode}.\\n\\n"
        + instruction
        + "\\nUser specification:\\n"
        + request.spec
    )
    try:
        result = await jarvis.process_text(prompt, source="program-forge", principal=Principal.HUMAN)
        response_text = result.response_text
        context_used = result.context_used
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"Program Forge unavailable: {exc}") from exc

    if not response_text:
        try:
            response_text = await jarvis.llm.chat([
                {"role": "system", "content": "You are JARVIS Principal Software Architect. Return a precise implementation package in Markdown."},
                {"role": "user", "content": prompt},
            ])
        except Exception as exc:
            raise HTTPException(status_code=503, detail=f"Program Forge model unavailable: {exc}") from exc

    return {
        "status": "success",
        "mode": mode,
        "language": request.language,
        "response": response_text or "Forge did not produce a textual blueprint.",
        "context_used": context_used,
        "duration_ms": round((time.perf_counter() - started) * 1000.0, 2),
        "model": getattr(jarvis.llm, "model", jarvis.settings.llm_provider),
    }


@app.post("/api/v1/forge/decompose")
async def forge_decompose(request: ForgeDecomposeRequest) -> Dict[str, Any]:
    jarvis = _require_runtime()
    started = time.perf_counter()
    prompt = (
        "You are JARVIS program decomposition council. Return only valid JSON, with no Markdown. "
        "The JSON must contain mission (string), architecture (array of strings), phases (array of objects), "
        "risks (array of objects) and next_slice (string). Each phase object must contain id, name, owner, "
        "status, deliverables (array) and acceptance (array). Create 5-7 implementation phases for a large "
        "maintainable software program, not a home automation system. Include security, tests, observability and delivery. "
        f"Target language: {request.language}. User specification: {request.spec.strip()}"
    )
    raw = ""
    plan = None
    try:
        raw = await jarvis.llm.chat([
            {"role": "system", "content": "You are a principal software architect producing machine-readable delivery plans."},
            {"role": "user", "content": prompt},
        ]) or ""
        plan = _extract_json_object(raw)
    except Exception:
        plan = None

    if not plan or not isinstance(plan.get("phases"), list):
        plan = _fallback_forge_plan(request.spec, request.language)

    return {
        "status": "success",
        "language": request.language,
        "plan": plan,
        "model": getattr(jarvis.llm, "model", jarvis.settings.llm_provider),
        "duration_ms": round((time.perf_counter() - started) * 1000.0, 2),
        "generated_by": "cognitive-decomposition-council",
    }


@app.get("/api/v1/proposals")
async def proposals() -> Dict[str, Any]:
    return {"pending": _pending_proposals(_require_runtime())}


@app.post("/api/v1/propose")
async def propose(request: ProposalRequest) -> Dict[str, Any]:
    jarvis = _require_runtime()
    now = datetime.now(timezone.utc).date().isoformat()
    note_type = request.type.strip().lower()
    if note_type == "fact":
        note_type = "knowledge"
    allowed_types = {"knowledge", "project", "procedure", "decision", "experience", "error", "lesson", "preference", "resource", "hypothesis", "system", "core"}
    if note_type not in allowed_types:
        note_type = "knowledge"
    note = {
        "id": f"proposal-{int(time.time() * 1000)}",
        "type": note_type,
        "lifecycle": "REVIEW",
        "category": request.category or request.type,
        "tags": request.tags,
        "created": now,
        "updated": now,
        "provenance": {"source_type": "inference", "source_ref": "jarvis-unified-web"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": request.relations,
        "content": request.content,
    }
    try:
        jarvis.storage.propose(Principal.AI_AGENT, note)
    except Exception as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    return {"status": "success", "candidate_id": note["id"], "note_id": note["id"], "message": "Propunere salvată pentru review."}


@app.post("/api/v1/proposals/{candidate_id}/decision")
async def decide_proposal(candidate_id: str, request: ProposalDecision) -> Dict[str, Any]:
    jarvis = _require_runtime()
    decision = request.decision.strip().upper()
    if decision not in {"APPROVED", "REJECTED"}:
        raise HTTPException(status_code=400, detail="Decision must be APPROVED or REJECTED.")
    note = jarvis.storage.get(candidate_id)
    if note is None:
        raise HTTPException(status_code=404, detail="Proposal not found.")
    try:
        if decision == "APPROVED":
            updated = jarvis.storage.promote(Principal.HUMAN, candidate_id)
        else:
            updated = jarvis.storage.archive(Principal.HUMAN, candidate_id, reason="Rejected from unified command center.")
    except Exception as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    return {"status": "success", "decision": decision, "note": updated}


@app.post("/tts")
async def tts(request: TTSRequest) -> Response:
    jarvis = _require_runtime()
    try:
        samples = await jarvis.audio.tts_engine.synthesize_async(
            request.text,
            voice=request.voice,
            speed=request.speed,
        )
        audio = _wav_bytes(samples, jarvis.audio.tts_engine.sample_rate)
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"TTS unavailable: {exc}") from exc
    return Response(content=audio, media_type="audio/wav", headers={"Cache-Control": "no-store"})


@app.get("/tts/health")
async def tts_health() -> Dict[str, Any]:
    jarvis = _require_runtime()
    return {"status": "online", "engine": type(jarvis.audio.tts_engine).__name__}


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket) -> None:
    await manager.connect(websocket)
    try:
        while True:
            await websocket.receive_text()
    except WebSocketDisconnect:
        await manager.disconnect(websocket)
    except Exception:
        await manager.disconnect(websocket)


@app.get("/renderer.html")
async def renderer() -> FileResponse:
    return FileResponse(Path(__file__).parent / "jarvis" / "hud" / "static" / "renderer.html")


app.mount("/cognitive", StaticFiles(directory=str(Path(__file__).parent / "jarvis" / "hud" / "static")), name="cognitive-hud")
app.mount("/", StaticFiles(directory=str(WEB_ROOT), html=True), name="web")


if __name__ == "__main__":
    import uvicorn

    parser = argparse.ArgumentParser(description="Run the single-process JARVIS command center.")
    parser.add_argument("--host", default=os.getenv("JARVIS_UNIFIED_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.getenv("JARVIS_UNIFIED_PORT", "3000")))
    args = parser.parse_args()
    os.environ["JARVIS_UNIFIED_PORT"] = str(args.port)
    uvicorn.run(app, host=args.host, port=args.port, log_level="info")
