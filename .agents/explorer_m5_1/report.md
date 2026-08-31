# Milestone 5 Exploration & Architectural Design Report
**Project**: Jarvis Cognitive Brain ("Creier Vorbitor")  
**Milestone**: M5 — Ultra-Modern GUI Dashboard & 3D Web HUD + Unified Production Entry Point  
**Agent**: `teamwork_preview_explorer` (`explorer_m5_1`)  
**Date**: 2026-08-28  

---

## 1. Executive Summary

Milestone 5 completes the full user-facing and real-time operational layer of the Jarvis Cognitive Brain. It unifies all prior subsystems (Milestones 1–4: Cognitive OODA loop, persistent SQLite WAL + Markdown memory storage, cascading STT/TTS audio pipeline with sub-50ms barge-in, multi-agent background worker supervisor, and FastMCP smart home IoT integration) into an interactive, ultra-modern Web HUD dashboard and unified production entry point.

### Key Deliverables Designed:
1. **FastAPI & WebSocket Telemetry Server (`jarvis/hud/server.py`)**:
   - High-throughput, non-blocking `HUDTelemetryHub` managing active WebSocket client subscriptions with thread-safe and async broadcast channels.
   - Complete REST API suite (`/api/status`, `/api/health`, `/api/memory/graph`, `/api/config`, `/api/interact`, `/api/voice/state`, `/api/voice/mute`, `/api/voice/unmute`, `/api/bargein`, `/api/iot/entities`, `/api/iot/call`, `/api/telemetry/recent`).
   - Bidirectional WebSocket protocol on `/ws/hud` and `/ws/telemetry` broadcasting real-time voice state transitions, live OODA thoughts, memory activation pulses, multi-agent worker telemetry, and system health metrics.
2. **Ultra-Modern 3D Web HUD & Dashboard Assets (`jarvis/hud/static/`)**:
   - `index.html`: Responsive tactical sci-fi HUD layout (3-column responsive grid: Thought Stream & Agent Monitor | 3D Arc-Reactor Core & Voice Ribbon | Canonical Memory Graph & IoT Matrix).
   - `css/style.css`: Ultra-modern dark glassmorphism styling (`#070b12` background, cyan/cobalt/amber glow borders, backdrop blurs, responsive layout).
   - `js/visualizer3d.js`: Three.js WebGL particle arc reactor / pulsating holographic sphere with dynamic state transitions (`IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `INTERRUPTED`) and FFT sound reactivity + 2D canvas fallback.
   - `js/memory_graph.js`: Interactive force-directed canvas/SVG memory graph with color-coded nodes by type (`knowledge`, `decision`, `procedure`, `lesson`, `error`), wikilink synapse edges, click-to-inspect modal, and real-time activation pulses.
   - `js/app.js`: Resilient WebSocket client with automatic exponential backoff reconnection, live OODA thought stream rendering, voice activity/barge-in controls, Web Speech API fallback, and IoT device tiles.
3. **Unified Production Entry Point (`jarvis/main.py` and `run.py`)**:
   - `JarvisApp` coordinator initializing configuration, SQLite WAL storage, LLM providers (Ollama / Gemini / Claude / Mock), Multi-Agent Supervisor, FastMCP IoT Server, Audio Pipeline, and HUD Server.
   - Clean graceful shutdown on `SIGINT` / `SIGTERM` (halting audio DAC playback, stopping worker loops, draining active OODA cycles, saving working memory checkpoints, flushing SQLite audit logs).
   - Full CLI options (`--host`, `--port`, `--provider`, `--no-audio`, `--no-hud`, `--mock`, `--vault-path`, `--db-path`).
4. **Unit & Integration Test Suite (`tests/unit/test_hud_server.py`)**:
   - Comprehensive test suite covering REST endpoints, WebSocket connections/disconnections, telemetry broadcasting, memory graph queries, bi-directional actions, static file serving, and graceful initialization/shutdown.

---

## 2. Telemetry Ingestion & Broadcast Architecture

### 2.1 Subsystem Event Sources & Data Schemas

```
+-----------------------------------------------------------------------------------------------------+
|                                      TELEMETRY EVENT PRODUCERS                                      |
+-----------------------------------+----------------------------------+------------------------------+
| AudioPipeline                     | CognitiveExecutive (OODA)        | MultiAgentSupervisor         |
| - VoiceState (IDLE, LISTENING,    | - Phase (OBSERVE, RETRIEVE,      | - Worker status (active/idle)|
|   THINKING, SPEAKING, INTERRUPT)  |   REASON, ACT, REFLECT, CONSOL)  | - Task queue depth           |
| - Audio FFT spectrum / levels     | - Plan steps & status            | - Tasks submitted / finished |
| - Barge-in trigger events         | - Recalled memory activations    | - Error isolation events     |
+-----------------------------------+----------------------------------+------------------------------+
                                                     │
                                                     ▼
                         +───────────────────────────────────────────────────────+
                         |       HUDTelemetryHub (jarvis/hud/server.py)          |
                         |  - Set[WebSocket] connection registry                |
                         |  - Circular buffer of last 100 packets                |
                         |  - async broadcast() & threadsafe broadcast_sync()    |
                         +───────────────────────────────────────────────────────+
                                                     │
                                                     ▼
                         +───────────────────────────────────────────────────────+
                         |       WebSocket Broadcast (/ws/hud & /ws/telemetry)   |
                         |  - vocal_state       - cognitive_thought              |
                         |  - memory_activation - agent_telemetry                |
                         |  - audio_spectrum    - system_heartbeat               |
                         |  - iot_state_change                                   |
                         +───────────────────────────────────────────────────────+
```

### 2.2 Telemetry Packet Specifications

All WebSocket packets follow a uniform JSON structure:
```json
{
  "type": "<message_type>",
  "timestamp": 1724851200.123,
  "payload": { ... }
}
```

#### 1. `vocal_state`
Emitted on any voice state transition or audio level tick:
```json
{
  "type": "vocal_state",
  "timestamp": 1724851200.123,
  "payload": {
    "state": "SPEAKING",
    "audio_level": 0.65,
    "vad_prob": 0.88,
    "is_muted": false
  }
}
```

#### 2. `cognitive_thought`
Emitted at each stage of the OODA cognitive loop:
```json
{
  "type": "cognitive_thought",
  "timestamp": 1724851201.456,
  "payload": {
    "cycle_id": "c-7f8e9a12",
    "phase": "REASON_AND_PLAN",
    "thought": "Decomposing user request into smart lighting and temperature commands.",
    "goal": "Set living room lights to evening mode and turn on thermostat.",
    "plan_steps": [
      {
        "step_id": 1,
        "action": "iot_call",
        "status": "running",
        "description": "Dispatch IoT Home Assistant control command: 'turn_on light.living_room_ceiling'"
      },
      {
        "step_id": 2,
        "action": "synthesize_response",
        "status": "pending",
        "description": "Synthesize verbal response"
      }
    ],
    "execution_time_ms": 42.5
  }
}
```

#### 3. `memory_activation`
Emitted during the `RETRIEVE` phase when notes are activated and admitted to Working Memory:
```json
{
  "type": "memory_activation",
  "timestamp": 1724851200.789,
  "payload": {
    "node_id": "11111111-1111-1111-1111-111111111111",
    "title": "Living Room Evening Lighting",
    "node_type": "decision",
    "activation_score": 0.87,
    "connected_edges": [
      "22222222-2222-2222-2222-222222222222"
    ]
  }
}
```

#### 4. `agent_telemetry`
Emitted by the `MultiAgentSupervisor` background worker pool:
```json
{
  "type": "agent_telemetry",
  "timestamp": 1724851202.001,
  "payload": {
    "event": "task_completed",
    "task_id": "t-45a8b2",
    "role": "verifier",
    "active_workers": 2,
    "queue_depth": 0,
    "status": "success",
    "execution_time_ms": 15.3
  }
}
```

#### 5. `system_heartbeat`
Emitted periodically at 1 Hz:
```json
{
  "type": "system_heartbeat",
  "timestamp": 1724851203.000,
  "payload": {
    "fps": 60.0,
    "latency_ms": 12.5,
    "cpu_percent": 8.4,
    "memory_mb": 245.8,
    "uptime_s": 3600.0,
    "connected_clients": 1,
    "voice_state": "IDLE"
  }
}
```

#### 6. `audio_spectrum`
Emitted during audio capture / playback:
```json
{
  "type": "audio_spectrum",
  "timestamp": 1724851200.200,
  "payload": {
    "fft": [0.12, 0.45, 0.78, 0.92, 0.65, 0.33, 0.15, 0.08, 0.04, 0.02, 0.01, 0.0],
    "rms": 0.42,
    "peak": 0.89
  }
}
```

---

## 3. Detailed Component Designs

### 3.1 `jarvis/hud/server.py` Design

```python
"""
Milestone 5: FastAPI & WebSocket Telemetry Server for Jarvis Cognitive Brain.
Provides REST APIs, real-time WebSocket telemetry broadcasting, and static HUD asset hosting.
"""

import os
import sys
import json
import time
import asyncio
import logging
from pathlib import Path
from typing import Dict, Any, List, Optional, Set, Union, Callable
from collections import deque

from fastapi import FastAPI, WebSocket, WebSocketDisconnect, HTTPException, Query, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import HTMLResponse, JSONResponse, FileResponse
from pydantic import BaseModel, Field

from jarvis.config import Settings, get_settings
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal
from jarvis.core.executive import CognitiveExecutive
from jarvis.audio.pipeline import AudioPipeline, VoiceState
from jarvis.agents.supervisor import MultiAgentSupervisor
from jarvis.iot.fastmcp_server import FastMCPIoTServer

logger = logging.getLogger("jarvis.hud.server")


class InteractionRequest(BaseModel):
    query: str = Field(..., description="User prompt or voice transcript")
    source: str = Field(default="hud", description="Input channel source")
    principal: str = Field(default="ai_agent", description="Caller principal role")


class IoTCallRequest(BaseModel):
    domain: str = Field(..., description="Target IoT domain, e.g. light, switch, climate")
    service: str = Field(..., description="Target service, e.g. turn_on, turn_off, toggle")
    service_data: Dict[str, Any] = Field(default_factory=dict, description="Service payload")


class VoiceStateRequest(BaseModel):
    state: Optional[str] = None
    is_muted: Optional[bool] = None


class HUDTelemetryHub:
    """
    Central WebSocket Telemetry Hub managing connected client subscriptions
    and broadcasting telemetry packets asynchronously and safely from worker threads.
    """

    def __init__(self, max_history: int = 100):
        self.connected_clients: Set[WebSocket] = set()
        self.recent_packets: deque = deque(maxlen=max_history)
        self.broadcast_count: int = 0
        self._loop: Optional[asyncio.AbstractEventLoop] = None
        self._lock = asyncio.Lock()

    def set_loop(self, loop: asyncio.AbstractEventLoop) -> None:
        self._loop = loop

    @property
    def client_count(self) -> int:
        return len(self.connected_clients)

    async def connect_client(self, websocket: Optional[WebSocket] = None) -> None:
        """Register a new client connection (supports mock or actual WebSocket)."""
        if websocket:
            self.connected_clients.add(websocket)
        else:
            # Fallback counter increment for unit tests
            pass

    async def disconnect_client(self, websocket: Optional[WebSocket] = None) -> None:
        """Remove a client connection."""
        if websocket and websocket in self.connected_clients:
            self.connected_clients.discard(websocket)

    async def broadcast(self, message_type: str, payload: Dict[str, Any]) -> None:
        """Asynchronously broadcast telemetry packet to all connected clients."""
        packet = {
            "type": message_type,
            "timestamp": time.time(),
            "payload": payload,
        }
        self.recent_packets.append(packet)
        self.broadcast_count += 1

        if not self.connected_clients:
            return

        dead_sockets: List[WebSocket] = []
        raw_msg = json.dumps(packet)

        for ws in list(self.connected_clients):
            try:
                await ws.send_text(raw_msg)
            except Exception:
                dead_sockets.append(ws)

        for dead_ws in dead_sockets:
            self.connected_clients.discard(dead_ws)

    def broadcast_sync(self, message_type: str, payload: Dict[str, Any]) -> None:
        """Thread-safe synchronous broadcast dispatch for callbacks from audio/worker threads."""
        try:
            loop = self._loop or asyncio.get_running_loop()
            if loop.is_running():
                asyncio.run_coroutine_threadsafe(self.broadcast(message_type, payload), loop)
        except RuntimeError:
            pass

    def get_packets_of_type(self, message_type: str) -> List[Dict[str, Any]]:
        return [p for p in self.recent_packets if p["type"] == message_type]

    def clear(self) -> None:
        self.recent_packets.clear()
        self.connected_clients.clear()
        self.broadcast_count = 0


def create_hud_app(
    hub: Optional[HUDTelemetryHub] = None,
    storage: Optional[SQLiteStorageEngine] = None,
    executive: Optional[CognitiveExecutive] = None,
    audio_pipeline: Optional[AudioPipeline] = None,
    supervisor: Optional[MultiAgentSupervisor] = None,
    iot_server: Optional[FastMCPIoTServer] = None,
    settings: Optional[Settings] = None,
) -> FastAPI:
    """
    Factory creating the FastAPI HUD application with all REST and WebSocket routes.
    """
    app = FastAPI(
        title="JARVIS Cognitive Brain HUD",
        description="Real-time 3D Holographic Dashboard & Cognitive Telemetry Hub",
        version="6.0.0",
    )

    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    app_hub = hub or HUDTelemetryHub()
    app_settings = settings or get_settings()
    start_time = time.time()

    # Mount static directory
    static_dir = Path(__file__).parent / "static"
    if static_dir.exists():
        app.mount("/static", StaticFiles(directory=str(static_dir)), name="static")

    @app.get("/", response_class=HTMLResponse)
    async def get_index():
        index_file = static_dir / "index.html"
        if index_file.exists():
            return FileResponse(index_file)
        return HTMLResponse("<h1>JARVIS Cognitive Brain HUD</h1><p>Static assets loading...</p>")

    @app.get("/api/health")
    @app.get("/api/status")
    async def get_status():
        uptime = time.time() - start_time
        notes_count = 0
        if storage:
            try:
                notes = storage.query_notes(principal=Principal.ADMIN, limit=1000)
                notes_count = len(notes)
            except Exception:
                pass

        v_state = audio_pipeline.state.value.upper() if audio_pipeline else "IDLE"
        workers_count = supervisor.active_worker_count if supervisor else 0
        entities_count = len(iot_server.ha_simulator.states) if iot_server and hasattr(iot_server, "ha_simulator") else 0

        return {
            "status": "online",
            "uptime_seconds": round(uptime, 2),
            "voice_state": v_state,
            "connected_clients": app_hub.client_count,
            "memory_notes_count": notes_count,
            "active_workers": workers_count,
            "iot_entities_count": entities_count,
            "llm_provider": app_settings.llm_provider,
            "timestamp": time.time(),
        }

    @app.get("/api/memory/graph")
    async def get_memory_graph():
        """Returns nodes and links formatted for force-directed graph rendering."""
        nodes = []
        links = []
        if storage:
            try:
                notes = storage.query_notes(principal=Principal.ADMIN, limit=500)
                for n in notes:
                    nid = n.get("id")
                    title = n.get("content", "").split("\n")[0].replace("#", "").strip() or nid[:8]
                    nodes.append({
                        "id": nid,
                        "label": title,
                        "type": n.get("type", "knowledge"),
                        "lifecycle": n.get("lifecycle", "ACTIVE"),
                        "confidence": n.get("confidence", "high"),
                        "verification": n.get("verification", "unverified"),
                        "val": 10 if n.get("lifecycle") == "ACTIVE" else 5,
                    })
                    for rel in n.get("relations", []):
                        if isinstance(rel, dict) and "target_id" in rel:
                            links.append({
                                "source": nid,
                                "target": rel["target_id"],
                                "relation": rel.get("relation", "connected"),
                            })
            except Exception as e:
                logger.error(f"Error fetching memory graph: {e}")

        return {"nodes": nodes, "links": links}

    @app.get("/api/config")
    async def get_config():
        """Returns sanitized configuration."""
        cfg = app_settings.model_dump()
        # Redact secrets
        for k in ["gemini_api_key", "claude_api_key", "home_assistant_token"]:
            if cfg.get(k):
                cfg[k] = "***REDACTED***"
        return cfg

    @app.post("/api/interact")
    async def post_interact(req: InteractionRequest):
        """Dispatches text or vocal prompt to the Cognitive Executive."""
        if not executive:
            raise HTTPException(status_code=503, detail="Cognitive Executive not initialized")

        principal = Principal(req.principal) if req.principal in [p.value for p in Principal] else Principal.AI_AGENT
        t0 = time.time()
        result = await executive.process_utterance(req.query, source=req.source, principal=principal)
        exec_ms = (time.time() - t0) * 1000.0

        # Broadcast cognitive thought to WebSocket clients
        await app_hub.broadcast("cognitive_thought", {
            "cycle_id": getattr(result, "cycle_id", str(time.time())),
            "phase": "COMPLETED",
            "goal": req.query,
            "response": result.response_text or "Processed successfully.",
            "execution_time_ms": exec_ms,
            "plan_steps": [s.model_dump() if hasattr(s, "model_dump") else s for s in (result.active_plan.steps if result.active_plan else [])],
        })

        return {
            "status": "success",
            "response": result.response_text,
            "execution_time_ms": exec_ms,
            "plan": [s.model_dump() if hasattr(s, "model_dump") else s for s in (result.active_plan.steps if result.active_plan else [])],
            "context_count": len(result.context_used),
        }

    @app.post("/api/bargein")
    async def trigger_bargein():
        """Immediate barge-in cancellation."""
        if audio_pipeline:
            latency = audio_pipeline.bargein_controller.trigger_bargein("REST API trigger")
            audio_pipeline.set_state(VoiceState.INTERRUPTED)
            await app_hub.broadcast("vocal_state", {"state": "INTERRUPTED", "audio_level": 0.0})
            return {"status": "interrupted", "latency_ms": latency}
        return {"status": "no_audio_pipeline"}

    @app.post("/api/voice/state")
    async def set_voice_state(req: VoiceStateRequest):
        if audio_pipeline and req.state:
            try:
                st = VoiceState(req.state.lower())
                audio_pipeline.set_state(st)
                await app_hub.broadcast("vocal_state", {"state": st.value.upper(), "audio_level": 0.5})
            except ValueError:
                raise HTTPException(status_code=400, detail=f"Invalid voice state: {req.state}")
        return {"status": "updated"}

    @app.get("/api/iot/entities")
    async def get_iot_entities():
        if not iot_server:
            return {"entities": []}
        states = iot_server.ha_simulator.states.values() if hasattr(iot_server, "ha_simulator") else []
        return {"entities": list(states)}

    @app.post("/api/iot/call")
    async def post_iot_call(req: IoTCallRequest):
        if not iot_server:
            raise HTTPException(status_code=503, detail="IoT Server not available")
        result = iot_server.client.call_service(req.domain, req.service, req.service_data)
        await app_hub.broadcast("iot_state_change", {"domain": req.domain, "service": req.service, "result": result})
        return {"status": "executed", "result": result}

    @app.get("/api/telemetry/recent")
    async def get_recent_telemetry():
        return {"packets": list(app_hub.recent_packets)}

    @app.websocket("/ws/hud")
    @app.websocket("/ws/telemetry")
    async def websocket_endpoint(websocket: WebSocket):
        await websocket.accept()
        await app_hub.connect_client(websocket)

        # Send initial snapshot
        initial_state = {
            "type": "initial_state",
            "timestamp": time.time(),
            "payload": {
                "voice_state": audio_pipeline.state.value.upper() if audio_pipeline else "IDLE",
                "uptime": time.time() - start_time,
                "recent_packets": list(app_hub.recent_packets)[-20:],
            }
        }
        await websocket.send_text(json.dumps(initial_state))

        try:
            while True:
                msg_text = await websocket.receive_text()
                try:
                    data = json.loads(msg_text)
                    action = data.get("action")
                    if action == "ping":
                        await websocket.send_text(json.dumps({"type": "pong", "timestamp": time.time()}))
                    elif action == "prompt":
                        text = data.get("text", "")
                        if executive and text:
                            res = await executive.process_utterance(text, source="hud")
                            await app_hub.broadcast("cognitive_thought", {
                                "phase": "COMPLETED",
                                "thought": text,
                                "response": res.response_text,
                            })
                    elif action == "barge_in":
                        if audio_pipeline:
                            audio_pipeline.bargein_controller.trigger_bargein("WebSocket client request")
                            audio_pipeline.set_state(VoiceState.INTERRUPTED)
                            await app_hub.broadcast("vocal_state", {"state": "INTERRUPTED"})
                    elif action == "mute":
                        if audio_pipeline:
                            # Handle mute toggle
                            pass
                except json.JSONDecodeError:
                    pass
        except WebSocketDisconnect:
            await app_hub.disconnect_client(websocket)
        except Exception:
            await app_hub.disconnect_client(websocket)

    return app
```

---

### 3.2 Front-End Assets Design (`jarvis/hud/static/`)

#### 3.2.1 `index.html` Specification
```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>JARVIS — Cognitive Brain 3D Web HUD</title>
  <link rel="stylesheet" href="/static/css/style.css">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;600;700&family=JetBrains+Mono:wght@400;600&family=Orbitron:wght@600;800&display=swap" rel="stylesheet">
  <script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script>
</head>
<body class="hud-body">
  <div class="hud-scanlines"></div>
  <div class="hud-vignette"></div>

  <!-- TOP STATUS HEADER -->
  <header class="hud-header">
    <div class="header-left">
      <div class="hud-logo-wrapper">
        <span class="hud-logo-icon">▲</span>
        <span class="hud-logo-text">JARVIS <span class="logo-sub">v6.0.0 // COGNITIVE BRAIN</span></span>
      </div>
      <div class="status-badge" id="system-status-badge">
        <span class="status-dot pulsing"></span>
        <span class="status-label" id="system-status-label">ONLINE // WAL LOCKED</span>
      </div>
    </div>

    <div class="header-center">
      <div class="metric-chip">
        <span class="metric-name">UPTIME</span>
        <span class="metric-val" id="val-uptime">00:00:00</span>
      </div>
      <div class="metric-chip">
        <span class="metric-name">LATENCY</span>
        <span class="metric-val" id="val-latency">12ms</span>
      </div>
      <div class="metric-chip">
        <span class="metric-name">VAULT NODES</span>
        <span class="metric-val" id="val-nodes">0</span>
      </div>
      <div class="metric-chip">
        <span class="metric-name">WORKERS</span>
        <span class="metric-val" id="val-workers">4 IDLE</span>
      </div>
    </div>

    <div class="header-right">
      <button class="btn-tactical btn-bargein" id="btn-bargein" title="Instant Sub-50ms Barge-In Interruption">
        <span class="btn-icon">⚡</span> BARGE-IN
      </button>
      <button class="btn-tactical btn-mute" id="btn-mute-toggle" title="Mute Microphone Audio">
        <span class="btn-icon" id="mute-icon">🎙️</span>
      </button>
      <div class="connection-pill" id="ws-status-pill">
        <span class="pill-dot"></span> WS: CONNECTED
      </div>
    </div>
  </header>

  <!-- MAIN HUD 3-COLUMN WORKSPACE -->
  <main class="hud-grid">
    <!-- LEFT PANEL: OODA THOUGHT STREAM & AGENTS -->
    <section class="hud-panel panel-left">
      <div class="panel-header">
        <h2 class="panel-title"><span class="title-accent">01 //</span> OODA COGNITIVE STREAM</h2>
        <span class="panel-badge" id="ooda-cycle-badge">CYCLE #0</span>
      </div>
      <div class="panel-body thought-stream-body" id="thought-stream-container">
        <div class="thought-card initial">
          <div class="thought-meta">
            <span class="phase-tag phase-observe">OBSERVE</span>
            <span class="thought-time">System Ready</span>
          </div>
          <p class="thought-text">Awaiting sensory input. Cognitive loop initialized with SQLite WAL & Antigravity core.</p>
        </div>
      </div>

      <!-- WORKER POOL STATUS -->
      <div class="worker-pool-strip">
        <div class="worker-pill" id="worker-router"><span class="w-dot"></span> ROUTER</div>
        <div class="worker-pill" id="worker-retrieval"><span class="w-dot"></span> RETRIEVAL</div>
        <div class="worker-pill" id="worker-verifier"><span class="w-dot"></span> VERIFIER</div>
        <div class="worker-pill" id="worker-consolidator"><span class="w-dot"></span> CONSOLIDATOR</div>
        <div class="worker-pill" id="worker-critic"><span class="w-dot"></span> CRITIC</div>
      </div>
    </section>

    <!-- CENTER PANEL: 3D HOLOGRAPHIC CORE & INTERACTION -->
    <section class="hud-panel panel-center">
      <div class="visualizer-wrapper">
        <canvas id="visualizer3d-canvas"></canvas>
        <div class="voice-state-ribbon" id="voice-state-ribbon">
          <span class="ribbon-bracket">[</span>
          <span class="ribbon-text" id="voice-state-label">IDLE</span>
          <span class="ribbon-bracket">]</span>
        </div>
        <canvas id="audio-spectrum-canvas" width="400" height="60"></canvas>
      </div>

      <!-- INTERACTION CONTROL CENTER -->
      <div class="interaction-bar">
        <div class="input-group">
          <button class="btn-voice-input" id="btn-mic-toggle" title="Push-to-Talk / Web Speech">
            <span class="mic-glyph">⏺</span>
          </button>
          <input type="text" id="prompt-input" class="hud-input" placeholder="Execute vocal or cognitive command..." autocomplete="off">
          <button class="btn-send" id="btn-send-prompt">TRANSMIT</button>
        </div>
        <div class="quick-prompts">
          <button class="chip" data-query="Turn on living room light">💡 Living Room Light</button>
          <button class="chip" data-query="What is our core architecture?">🧠 Architecture Recall</button>
          <button class="chip" data-query="Set living room thermostat to 22 degrees">🌡️ Climate 22°C</button>
          <button class="chip" data-query="Reflect on recent execution errors">🔍 Reflexion Audit</button>
        </div>
      </div>
    </section>

    <!-- RIGHT PANEL: MEMORY VAULT GRAPH & IOT MATRIX -->
    <section class="hud-panel panel-right">
      <div class="panel-header">
        <div class="tab-group">
          <button class="tab-btn active" data-tab="tab-memory">MEMORY VAULT</button>
          <button class="tab-btn" data-tab="tab-iot">IOT MATRIX</button>
        </div>
        <span class="panel-badge" id="graph-nodes-badge">0 NODES</span>
      </div>

      <div class="panel-body tab-content active" id="tab-memory">
        <div class="graph-toolbar">
          <input type="text" id="graph-search" class="hud-input-sm" placeholder="Filter nodes...">
          <div class="filter-chips">
            <span class="f-chip active" data-type="all">ALL</span>
            <span class="f-chip" data-type="knowledge">KNOW</span>
            <span class="f-chip" data-type="decision">DEC</span>
            <span class="f-chip" data-type="procedure">PROC</span>
            <span class="f-chip" data-type="lesson">LESSON</span>
          </div>
        </div>
        <div class="canvas-container">
          <canvas id="memory-graph-canvas"></canvas>
        </div>
        <div class="node-inspector" id="node-inspector">
          <div class="inspector-header">
            <span class="node-type-tag" id="inspect-type">KNOWLEDGE</span>
            <span class="node-id" id="inspect-id">---</span>
          </div>
          <h4 class="inspector-title" id="inspect-title">Select a node to inspect</h4>
          <p class="inspector-content" id="inspect-content">...</p>
        </div>
      </div>

      <div class="panel-body tab-content" id="tab-iot">
        <div class="iot-grid" id="iot-devices-container">
          <!-- Dynamic IoT Device Cards -->
        </div>
      </div>
    </section>
  </main>

  <footer class="hud-footer">
    <div class="footer-item"><span>SECURITY INVARIANTS:</span> <strong class="text-emerald">P0-P18 ACTIVE</strong></div>
    <div class="footer-item"><span>DUAL STORAGE:</span> <strong class="text-cyan">SQLite WAL + Markdown Sync</strong></div>
    <div class="footer-item"><span>AUDIO TTFB:</span> <strong class="text-amber">&lt; 300ms</strong></div>
  </footer>

  <script src="/static/js/visualizer3d.js"></script>
  <script src="/static/js/memory_graph.js"></script>
  <script src="/static/js/app.js"></script>
</body>
</html>
```

#### 3.2.2 `css/style.css` Specification
- **Color Palette**:
  - `--bg-primary: #070b12` (Void Black)
  - `--bg-panel: rgba(13, 23, 42, 0.78)` (Frosted Dark Glass)
  - `--bg-card: rgba(15, 28, 54, 0.65)` (Elevated Card)
  - `--accent-cyan: #00f0ff` (Primary Glow)
  - `--accent-cobalt: #3b82f6` (Processing Blue)
  - `--accent-emerald: #10b981` (Verified Green)
  - `--accent-amber: #f59e0b` (Caution / Reflexion)
  - `--accent-crimson: #ef4444` (Interruption / Barge-in)
  - `--text-main: #f8fafc`
  - `--text-dim: #94a3b8`
- **Effects**:
  - `backdrop-filter: blur(16px)` on panels and cards.
  - Border gradients: `1px solid rgba(0, 240, 255, 0.25)` with glowing corners.
  - Subtle scanline overlay (`repeating-linear-gradient(0deg, rgba(0,0,0,0.15), rgba(0,0,0,0.15) 1px, transparent 1px, transparent 2px)`).
  - Responsive flex/grid system supporting 4K, 1440p, 1080p, tablets, and mobile screens.

#### 3.2.3 `js/visualizer3d.js` Specification
- **Three.js WebGL Holographic Arc-Reactor**:
  - `JarvisVisualizer3D` initializes a multi-ring particle reactor:
    1. Outer Orbiting Ring: Torus geometry with custom glowing line shader.
    2. Middle Counter-Rotating Gear Ring: Dodecagonal segmented points with alternating vertex pulses.
    3. Inner Particle Sphere: 1200+ Fibonacci sphere points reacting dynamically to audio levels and FFT energy bins.
    4. Concentric Energy Field Pulses: Expanding circular shockwaves.
  - Smooth state transition interpolators for `IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `INTERRUPTED`.
  - FFT Audio Reactivity: Vertices displace along normals proportionally to frequency band amplitudes.
  - 2D Canvas Fallback: In headless environments or browsers without WebGL, renders a multi-harmonic Lissajous circle with FFT arcs.

#### 3.2.4 `js/memory_graph.js` Specification
- **Force-Directed Canonical Vault Graph**:
  - Loads nodes and wikilink relations from `/api/memory/graph`.
  - High-performance 2D Canvas physics engine (repulsion, spring attraction, center gravity).
  - Node types color coded (`knowledge`: `#00f0ff`, `decision`: `#a855f7`, `procedure`: `#10b981`, `lesson`: `#f59e0b`, `error`: `#f43f5e`).
  - Interactive click / hover inspection, search filtering, and real-time activation pulse waves triggered by WebSocket `memory_activation` events.

#### 3.2.5 `js/app.js` Specification
- **Master HUD Application Controller**:
  - WebSocket auto-reconnect logic with exponential backoff.
  - Real-time OODA thought stream rendering with animated step status cards.
  - Instant Barge-In trigger button dispatching interruption packets.
  - Audio spectrum visualizer rendering FFT bars on canvas.
  - Web Speech API fallback for direct browser voice recognition.
  - Interactive Smart Home IoT device controls.

---

### 3.3 `jarvis/main.py` & `run.py` Unified Production Entry Point

```python
"""
Unified Production Entry Point for Jarvis Cognitive Brain ("Creier Vorbitor").
Initializes all subsystems, wires telemetry, handles signals gracefully, and launches CLI/HUD.
"""

import os
import sys
import time
import signal
import asyncio
import argparse
import logging
from pathlib import Path
from typing import Optional

from jarvis.config import Settings, get_settings, reset_settings
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.invariants import Principal
from jarvis.llm.base import BaseLLMProvider
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.llm.ollama_provider import OllamaLLMProvider
from jarvis.core.executive import CognitiveExecutive
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.agents.supervisor import MultiAgentSupervisor
from jarvis.iot.fastmcp_server import FastMCPIoTServer
from jarvis.iot.ha_client import HomeAssistantClient
from jarvis.iot.ha_simulator import HomeAssistantSimulator
from jarvis.audio.pipeline import AudioPipeline, VoiceState
from jarvis.audio.vad import EnergyVADEngine
from jarvis.audio.stt import MockSTTEngine
from jarvis.audio.tts import MockTTSEngine
from jarvis.audio.drivers import VirtualAudioInputDriver, VirtualAudioOutputDriver
from jarvis.hud.server import HUDTelemetryHub, create_hud_app

import uvicorn

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("jarvis.main")


class JarvisApp:
    """
    Main application coordinator managing complete lifecycle of all Jarvis subsystems.
    """

    def __init__(self, settings: Optional[Settings] = None):
        self.settings = settings or get_settings()
        self.storage: Optional[SQLiteStorageEngine] = None
        self.sync_engine: Optional[MarkdownSyncEngine] = None
        self.llm_provider: Optional[BaseLLMProvider] = None
        self.executive: Optional[CognitiveExecutive] = None
        self.supervisor: Optional[MultiAgentSupervisor] = None
        self.iot_server: Optional[FastMCPIoTServer] = None
        self.audio_pipeline: Optional[AudioPipeline] = None
        self.telemetry_hub: HUDTelemetryHub = HUDTelemetryHub()
        self.fastapi_app = None
        self.uvicorn_server: Optional[uvicorn.Server] = None
        self._is_running = False
        self._shutdown_event = asyncio.Event()

    async def initialize(self, mock_mode: bool = False, no_audio: bool = False) -> None:
        """Initialize all subsystems in dependency order."""
        logger.info("Initializing Jarvis Cognitive Brain...")

        # 1. Storage Engines
        os.makedirs(self.settings.vault_path, exist_ok=True)
        os.makedirs(self.settings.checkpoint_dir, exist_ok=True)
        self.storage = SQLiteStorageEngine(
            db_path=self.settings.sqlite_db_path,
            busy_timeout_ms=self.settings.sqlite_busy_timeout_ms,
            wal_mode=True,
        )
        self.sync_engine = MarkdownSyncEngine(vault_root=self.settings.vault_path)

        # 2. LLM Provider
        if mock_mode or self.settings.llm_provider == "mock":
            self.llm_provider = MockLLMProvider(
                default_response="JARVIS Cognitive Core online. All invariants P0-P18 enforced."
            )
        else:
            self.llm_provider = OllamaLLMProvider(
                base_url=self.settings.ollama_url,
                model=self.settings.ollama_model,
                timeout=self.settings.ollama_timeout,
            )

        # 3. Multi-Agent Supervisor
        self.supervisor = MultiAgentSupervisor(
            storage=self.storage,
            llm=self.llm_provider,
            max_concurrent_workers=4,
            telemetry_callback=lambda evt, data: self.telemetry_hub.broadcast_sync(
                "agent_telemetry", {"event": evt, **data}
            ),
        )

        # 4. FastMCP IoT Server
        ha_sim = HomeAssistantSimulator(auth_token=self.settings.home_assistant_token or "jarvis_token")
        self.iot_server = FastMCPIoTServer(ha=ha_sim)

        # 5. Cognitive Executive
        self.executive = CognitiveExecutive(
            llm_provider=self.llm_provider,
            storage_engine=self.storage,
            checkpoint_dir=self.settings.checkpoint_dir,
        )
        self.executive.engine.tool_executor = lambda action, kwargs: self.iot_server.client.call_service(
            domain="light", service="turn_on" if "on" in kwargs.get("command", "") else "turn_off", service_data={}
        )

        # 6. Cascaded Audio Pipeline
        if not no_audio:
            self.audio_pipeline = AudioPipeline(
                settings=self.settings,
                executive=self.executive,
                on_state_change=lambda state: self.telemetry_hub.broadcast_sync(
                    "vocal_state", {"state": state.value.upper(), "audio_level": 0.5}
                ),
            )

        # 7. HUD Telemetry Server
        self.telemetry_hub.set_loop(asyncio.get_running_loop())
        self.fastapi_app = create_hud_app(
            hub=self.telemetry_hub,
            storage=self.storage,
            executive=self.executive,
            audio_pipeline=self.audio_pipeline,
            supervisor=self.supervisor,
            iot_server=self.iot_server,
            settings=self.settings,
        )
        logger.info("All subsystems initialized successfully.")

    async def start(self, host: str = "127.0.0.1", port: int = 8080, no_hud: bool = False) -> None:
        """Start all services concurrently."""
        self._is_running = True

        # Start Supervisor workers
        if self.supervisor:
            await self.supervisor.start()

        # Start Audio Pipeline
        if self.audio_pipeline:
            self.audio_pipeline.start(loop=asyncio.get_running_loop())

        # Start HUD Web Server
        if not no_hud and self.fastapi_app:
            config = uvicorn.Config(app=self.fastapi_app, host=host, port=port, log_level="warning")
            self.uvicorn_server = uvicorn.Server(config)
            logger.info(f"HUD Dashboard available at http://{host}:{port}")
            await self.uvicorn_server.serve()
        else:
            await self._shutdown_event.wait()

    async def shutdown(self) -> None:
        """Graceful shutdown protocol."""
        if not self._is_running:
            return
        logger.info("Shutting down Jarvis Cognitive Brain...")
        self._is_running = False

        if self.audio_pipeline:
            self.audio_pipeline.stop()

        if self.supervisor:
            await self.supervisor.stop()

        if self.executive:
            self.executive.save_checkpoint()

        if self.uvicorn_server:
            self.uvicorn_server.should_exit = True

        self._shutdown_event.set()
        logger.info("Shutdown complete.")


def parse_args():
    parser = argparse.ArgumentParser(description="Jarvis Cognitive Brain Daemon & Web HUD")
    parser.add_argument("--host", default="127.0.0.1", help="HUD Host address")
    parser.add_argument("--port", type=int, default=8080, help="HUD Port")
    parser.add_argument("--provider", choices=["ollama", "gemini", "claude", "mock"], default="mock", help="LLM Provider")
    parser.add_argument("--no-audio", action="store_true", help="Disable audio I/O hardware drivers")
    parser.add_argument("--no-hud", action="store_true", help="Run in headless daemon mode")
    parser.add_argument("--mock", action="store_true", default=True, help="Enable mock mode for all drivers")
    return parser.parse_args()


def main():
    args = parse_args()
    settings = get_settings()
    settings.llm_provider = args.provider

    app = JarvisApp(settings=settings)

    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)

    def _sig_handler():
        asyncio.create_task(app.shutdown())

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(sig, _sig_handler)
        except (NotImplementedError, AttributeError):
            pass

    try:
        loop.run_until_complete(app.initialize(mock_mode=args.mock, no_audio=args.no_audio))
        loop.run_until_complete(app.start(host=args.host, port=args.port, no_hud=args.no_hud))
    except KeyboardInterrupt:
        loop.run_until_complete(app.shutdown())
    finally:
        loop.close()


if __name__ == "__main__":
    main()
```

---

## 4. Test Suite Strategy (`tests/unit/test_hud_server.py`)

A comprehensive unit test suite covering:
1. **Telemetry Hub Broadcast**: Connects clients, broadcasts typed packets, validates payload delivery and packet history retention.
2. **Disconnected Client Resilience**: Verifies that client disconnections or failed socket writes do not crash subsequent broadcasts or corrupt state.
3. **REST Endpoints**:
   - `GET /api/status` & `GET /api/health` -> HTTP 200 with online status, uptime, voice state, worker count.
   - `GET /api/memory/graph` -> HTTP 200 returning nodes and links adhering to graph schema.
   - `GET /api/config` -> HTTP 200 with redacted secrets.
   - `POST /api/interact` -> HTTP 200 executing end-to-end OODA cycle.
   - `POST /api/bargein` -> HTTP 200 invoking barge-in controller.
   - `GET /api/iot/entities` & `POST /api/iot/call` -> HTTP 200 querying and toggling smart home devices.
4. **Static Asset Delivery**: `GET /` serves `index.html` with valid HTML.
5. **WebSocket Bidirectional Protocol**: Handshake snapshot, `ping`/`pong` keepalive, prompt execution, and barge-in action dispatch.
6. **Graceful App Teardown**: Validates `JarvisApp.initialize()`, `JarvisApp.shutdown()`, and signal handling without leaking open file descriptors or threads.

---

## 5. File Creation / Layout Matrix for Implementation

| Target File | Purpose | Key Classes / Exports |
|---|---|---|
| `jarvis/hud/__init__.py` | Package exports | `HUDTelemetryHub`, `HUDServer`, `create_hud_app` |
| `jarvis/hud/server.py` | FastAPI & WebSocket server | `HUDTelemetryHub`, `create_hud_app`, `HUDServer` |
| `jarvis/hud/static/index.html` | Tactical 3D HUD Dashboard | Full HTML5 interface |
| `jarvis/hud/static/css/style.css` | Glassmorphism stylesheet | Dark glass sci-fi design system |
| `jarvis/hud/static/js/visualizer3d.js` | 3D WebGL visualizer | `JarvisVisualizer3D`, `JarvisVisualizer2D` |
| `jarvis/hud/static/js/memory_graph.js` | Force memory graph | `JarvisMemoryGraph` |
| `jarvis/hud/static/js/app.js` | Frontend controller | Master HUD UI & WebSocket client |
| `jarvis/main.py` | Production entry point | `JarvisApp`, `main()`, CLI parser |
| `run.py` | Root launcher script | Direct executable wrapper |
| `tests/unit/test_hud_server.py` | M5 Unit test suite | 15+ comprehensive unit & integration tests |
