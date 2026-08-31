# Comprehensive Technical Survey & Specification Mining Report: IoT FastMCP, Multi-Agent & 3D HUD Specialist (Explorer 3)

**Author**: Explorer 3 (IoT FastMCP, Multi-Agent & 3D HUD Specialist)  
**Target Project**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`  
**Working Directory**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\survey_iot_fastmcp_hud`  
**Timestamp**: 2026-08-27T19:25:00Z  
**Requirements Covered**: R3 (Multi-Agent Worker Orchestration), R4 (FastMCP & IoT Home Assistant Integration), R5 (Ultra-Modern GUI Dashboard & 3D Web HUD)

---

## 1. Observation

1. **Authoritative Specification & Core Request**:
   - In `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md` (lines 124–170, timestamp `2026-08-27T19:19:42Z`), the user specifies building a local, fully autonomous, self-improving Cognitive Brain ("Creier Vorbitor") in `projects/jarvis_cognitive_brain`.
   - **Requirement R3** specifies: "Multi-Agent Worker Orchestration: Coordinate execution using a supervisor and specialized, least-privilege agent workers (Router, Retrieval, Verifier, Consolidator, Critic) to process background tasks (e.g., gathering data, verifying memory compliance) without blocking the primary real-time voice loop."
   - **Requirement R4** specifies: "FastMCP & IoT Home Assistant Integration: Implement a FastMCP tool server (`JarvisControls`) that provides validated tools to query and manipulate IoT device states over a local REST API (`/api/states`). Deliver a lightweight local simulator script to mock Home Assistant REST endpoints for reliable offline testing."
   - **Requirement R5** specifies: "Ultra-Modern GUI Dashboard & Web HUD: Build a highly polished, responsive Web UI dashboard and 3D visualizer showing: Active vocal states (Idle, Listening, Thinking, Speaking) with dynamic sound reactivity; Visual representation of the active 'thoughts' (OODA execution stages) and memory graphs/citations; System health meters and configuration settings."

2. **Codebase & Runtime Environment Analysis**:
   - Current Python environment: Python `3.14.2` (`C:\Python314\python.exe`).
   - Installed packages verified via runtime inspection:
     - `fastapi` == `0.128.0`
     - `uvicorn` == `0.40.0`
     - `websockets` == `17.0.1`
     - `pydantic` == `2.12.5`
     - `httpx` == `0.28.1`
     - `aiohttp` == `3.13.3`
     - `pytest` == `9.0.2`
     - `jinja2` == `3.1.6`
     - `starlette` == `0.50.0`
     - `mcp` / `fastmcp` == Not installed in global Python path.
   - Pre-existing Agent Implementations in `cognitive_core/agents/`:
     - `base_agent.py`: `BaseWorkerAgent` abstract class with `permitted_actions`, `execute_action()`, `process_task(principal, task)`.
     - `router_agent.py`: `RouterAgent` (role: `"router"`, permitted: `["search", "read"]`, max_steps: 2).
     - `retrieval_agent.py`: `RetrievalAgent` (role: `"retrieval"`, permitted: `["search", "read"]`, max_steps: 3).
     - `verifier_agent.py`: `VerifierAgent` (role: `"verifier"`, permitted: `["read"]`, max_steps: 2). Validates provenance claims against source-of-truth invariants.
     - `consolidator_agent.py`: `ConsolidatorAgent` (role: `"consolidator"`, permitted: `["search", "read", "propose", "archive"]`, max_steps: 4).
     - `critic_agent.py`: `CriticAgent` (role: `"critic"`, permitted: `["read", "propose"]`, max_steps: 3).
   - Pre-existing Multi-Agent Orchestrator in `cognitive_core/orchestrator.py`:
     - Contains `MultiAgentOrchestrator`, `AgentRole`, `SubagentSpec`, `GlobalWorkspace` (GWT competition), and `route_and_dispatch()`.
     - Note: Currently synchronous in cognitive dispatch; does not yet have a dedicated non-blocking asyncio PriorityQueue/WorkerPool to decouple long-running background tasks from the voice loop.
   - Pre-existing Web UI in `projects/jarvis_web/`:
     - `index.html`: Responsive HUD shell with reactor visualization, tabs (Overview, Memory, Agents, Skills, Council, Execution, Analytics, Settings), Chat panel, Proposal dialog.
     - `js/hologram.js`: Raw WebGL point cloud rendering an avatar mesh.
     - `js/sound_engine.js`: Pure Web Audio API `TacticalAudio` synthesizer with 9 tactical procedural sound effects (`playWakeChime`, `playListeningBeep`, `startThinkingDrone`, `stopThinkingDrone`, `playSuccessChime`, `playErrorAlert`, `playStandbyChirp`, `playCitationPulse`, `playClickFeedback`).
     - `js/state_machine.js`: `StateMachine` managing `INIT`, `IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `MUTED`, `ERROR` with transitions and timeouts.

---

## 2. Logic Chain

1. **Decoupling Real-Time Voice Loop and Background Multi-Agent Workers (R3)**:
   - *Observation*: Real-time conversational AI voice interactions require Time-To-First-Byte (TTFB) < 300ms for audio synthesis and immediate responsiveness. Heavy cognitive operations (e.g. memory deduplication, sleep consolidation, 6-stage Reflexion critique, deep graph exploration) take between 200ms and 5000ms.
   - *Deduction*: Executing background workers synchronously in the primary voice path would induce vocal stutter, dropped audio frames, and latency spikes.
   - *Design*: The `CognitiveSupervisor` must maintain a dedicated non-blocking `AsyncWorkerQueue` (`asyncio.PriorityQueue` / `WorkerPool`) with three priority tiers:
     - `PRIORITY_HIGH` (P0): Immediate operational validation (e.g., VerifierAgent validating a real-time memory query context).
     - `PRIORITY_NORMAL` (P1): Standard subagent task delegation.
     - `PRIORITY_LOW` (P2): Background consolidation, memory deduplication scans, and asynchronous Reflexion learning.
   - *Security Scoping*: Every worker must strictly adhere to the Least Privilege Invariants (P0-P18 from `vault_cognitive_rules.md` and `AGENTS.md`):
     - `RouterAgent` (`search`, `read` only)
     - `RetrievalAgent` (`search`, `read` only)
     - `VerifierAgent` (`read` only; cannot escalate verification to `verified`)
     - `ConsolidatorAgent` (`search`, `read`, `propose`, `archive`; gated against archiving human-verified notes per BRAIN-13)
     - `CriticAgent` (`read`, `propose` candidate notes into `REVIEW` only)

2. **FastMCP & Home Assistant IoT Integration Architecture (R4)**:
   - *Observation*: `fastmcp` is not installed in the global Python environment. External dependencies should be avoided when possible or wrapped with standard pure-Python JSON-RPC 2.0 / Pydantic schema engines.
   - *Deduction*: The FastMCP `JarvisControls` tool server should provide a dual interface:
     - A standalone protocol engine implementing Model Context Protocol (MCP) JSON-RPC 2.0 specification (`tools/list`, `tools/call`) with Pydantic v2 JSON-Schema generation.
     - Direct asynchronous Python tool execution methods callable by the Cognitive Brain's `ToolRouter` and `Act` stage.
   - *Home Assistant Integration*:
     - REST client communicating over standard HA endpoints: `GET /api/states`, `GET /api/states/<entity_id>`, `POST /api/services/<domain>/<service>`, `GET /api/services`, `GET /api/config`.
     - Validated tool signatures: `ha_get_states`, `ha_call_service`, `ha_toggle_device`, `ha_query_entities`, `ha_set_state_value`.
   - *Lightweight Local HA REST Simulator*:
     - To ensure 100% offline testability, CI reproducibility, and demo execution without an actual Home Assistant server, implement `HomeAssistantSimulator`.
     - Seeded with comprehensive smart home fixtures (lights, thermostats, switches, contact sensors, temperature/humidity sensors, media players).
     - Emulates token authentication (`Authorization: Bearer <token>`), state persistence, service execution side-effects (e.g. `light.turn_on` updating brightness/state, `climate.set_temperature` updating target temperature).

3. **Ultra-Modern GUI Dashboard & 3D Web HUD (R5)**:
   - *Observation*: The user requires a Three.js 3D WebGL holographic Arc-Reactor / Sphere visualization reacting in real-time to vocal states and sound audio, displaying live OODA thought streams, memory graph visualizer, and system health.
   - *Deduction*:
     - The front-end must combine a Three.js WebGL canvas (procedural particle Arc-Reactor + energy torus rings) with Web Audio `AnalyserNode` FFT analysis to achieve real-time sound reactivity.
     - A FastAPI WebSocket server (`/ws/hud` or `/ws/ooda`) should stream real-time telemetry (vocal states, active OODA execution stages, memory activations, FastMCP tool triggers).
     - Headless fallback support: The UI must gracefully fall back to 2D CSS pulse animations if WebGL or audio hardware is missing (critical for headless test environments).

---

## 3. Features Discovered & Specification Mining

### Features Discovered Table
| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|---|---|---|---|---|---|---|
| 1 | R3: Multi-Agent | `CognitiveSupervisor` | Master coordination daemon dispatching voice vs background tasks | User vocal/text prompt, System state | Routed execution response, dispatched task IDs | Graceful degradation if worker fails | R3 Spec / `orchestrator.py` |
| 2 | R3: Multi-Agent | `RouterAgent` | Intent triage, complexity decomposition, and subagent selection | `{"query": str, "context": List}` | `{"complexity": str, "target_agents": List[str]}` | Returns default `["retrieval"]` on parse ambiguity | `router_agent.py` |
| 3 | R3: Multi-Agent | `RetrievalAgent` | Associative activation, semantic scoring, supersession lineage resolution | `{"query": str, "working_memory": WM}` | `{"results": List[Dict], "scored_results": List}` | Returns empty list on zero match | `retrieval_agent.py` |
| 4 | R3: Multi-Agent | `VerifierAgent` | Provenance audit, source-of-truth validation, invariant P0-P15 checks | `{"nodes": List[Dict]}` | `{"verified_count": int, "violations": List, "is_clean": bool}` | Flags violations without crashing | `verifier_agent.py` |
| 5 | R3: Multi-Agent | `ConsolidatorAgent` | Duplicate detection & synthesis of REVIEW lessons into canonical memory | `{"type": "dedup"\|"consolidate"\|"all"}` | `{"duplicates_flagged": int, "consolidated_id": str}` | ApprovalRequiredError on human-verified targets | `consolidator_agent.py` |
| 6 | R3: Multi-Agent | `CriticAgent` | 6-stage Reflexion failure analysis & SelfRefine memory critique | `{"type": "reflect"\|"self_refine", ...}` | `{"reflection_note_id": str, "passed_filter": bool}` | Logs rejection if critique fails | `critic_agent.py` |
| 7 | R3: Multi-Agent | `AsyncWorkerQueue` | Non-blocking priority queue for background tasks | `WorkerTask(priority, agent_role, payload)` | `task_id`, `Future[TaskResult]` | Full queue backpressure / timeout handling | R3 Spec |
| 8 | R4: FastMCP | `JarvisControls` Server | MCP JSON-RPC 2.0 tool registry with Pydantic v2 schemas | JSON-RPC 2.0 requests (`tools/list`, `tools/call`) | JSON-RPC 2.0 responses with tool schema/result | Standard JSON-RPC error codes (-32600, -32602) | R4 Spec / `mcp-server-integrations` |
| 9 | R4: FastMCP | `ha_get_states` Tool | Query current states and attributes of all or specific HA entities | `entity_id: Optional[str]`, `domain: Optional[str]` | List of entity state objects | Returns empty list or 404 error dict | R4 Spec |
| 10 | R4: FastMCP | `ha_call_service` Tool | Actuate IoT devices via HA services (`turn_on`, `set_temp`, etc.) | `domain: str`, `service: str`, `service_data: Dict`, `target_entity_ids: List[str]` | List of modified entity state objects | Returns descriptive service execution error | R4 Spec |
| 11 | R4: FastMCP | `ha_toggle_device` Tool | Quick toggle utility for lights, switches, and relays | `entity_id: str` | Updated entity state object | Raises error on non-toggleable domain | R4 Spec |
| 12 | R4: FastMCP | `ha_query_entities` Tool | Semantic and attribute filtering for smart home devices | `domain: Optional[str]`, `state_filter: Optional[str]`, `attribute_filters: Dict` | Filtered list of matching devices | Returns `[]` if no devices match | R4 Spec |
| 13 | R4: FastMCP | `HomeAssistantSimulator` | Lightweight local HTTP REST simulator for offline testing | Standard HA REST API calls (`/api/states`, `/api/services/*`) | Authentic HA JSON responses and state mutations | 401 on bad token, 404 on unknown entity/service | R4 Spec |
| 14 | R5: Web HUD | Three.js Arc-Reactor | 3D WebGL particle sphere & holographic energy reactor visualizer | Three.js Canvas, Vocal state, Audio FFT data | 60 FPS animated 3D WebGL scene | Fallback to 2D CSS pulse on WebGL failure | R5 Spec / `threejs` |
| 15 | R5: Web HUD | Vocal State Sync | Synchronizes visualizer geometry & color across 5 states | `IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `ERROR` | Dynamic shader color, particle velocity, rotation | Auto-recovers to IDLE on timeout | `state_machine.js` |
| 16 | R5: Web HUD | Sound Reactivity Engine | Web Audio FFT frequency analysis modulating 3D uniforms | Microphone stream or TTS playback audio | Uniforms (`uAudioVolume`, `uAudioBass`, `uAudioTreble`) | No-op when audio is muted or silent | `sound_engine.js` |
| 17 | R5: Web HUD | OODA Thought Stream | Live visualizer of OODA loop stages (Observe, Retrieve, Plan, Act, Reflect) | WebSocket event stream from Cognitive Brain | Interactive visual execution cards with timestamps | Displays offline status indicator on WS disconnect | R5 Spec |
| 18 | R5: Web HUD | Memory Graph Visualizer | Interactive 2D/3D force-directed graph of active memory nodes & synapses | Activated memory nodes & wikilink relations | Interactive node network with confidence badges | Empty state when no memories active | R5 Spec / `00_CORE` |
| 19 | R5: Web HUD | System Health Telemetry | Dashboard meters for TTFB, queue depth, LLM latency, HA status | WebSocket telemetry packets | Gauge cards, sparklines, and status badges | Shows red warning badge on metric violation | R5 Spec / `dashboard-admin-ui` |
| 20 | R5: Web HUD | WebSocket Streaming Hub | High-throughput bi-directional WebSocket server (`/ws/hud`) | Client subscriptions, user action triggers | Real-time JSON telemetry broadcast | Auto-reconnect with exponential backoff | R5 Spec |

---

## 4. Edge Cases & Resilience Matrix

| # | Feature | Input / Condition | Observed / Required Behavior |
|---|---|---|---|
| 1 | `RouterAgent` | Empty query or gibberish input (`""`, `"??"`, `"!@#$"`) | Returns `{"status": "success", "complexity": "low", "target_agents": ["retrieval"]}` without throwing exceptions. |
| 2 | `VerifierAgent` | Node claiming `source_type="user"` with `verification="unverified"` | Successfully flags violation: `"claims 'user' without attested verification"`, sets `is_clean=False`. |
| 3 | `ConsolidatorAgent` | Target memory is human-verified (`verification="verified"`) | Raises `ApprovalRequiredError` per BRAIN-13; prevents automatic mutation or archiving. |
| 4 | `CriticAgent` | Candidate note with empty or low-quality content | `SelfRefine.refine_memory()` flags as rejected, preventing memory pollution. |
| 5 | `AsyncWorkerQueue` | Worker task throws unhandled exception | Exception is trapped in worker envelope, recorded with `executed=False` and `error` string; queue continues processing remaining tasks. |
| 6 | `AsyncWorkerQueue` | Voice barge-in interrupt occurs during background processing | Real-time audio cancellation proceeds immediately without waiting for background workers to finish. |
| 7 | `JarvisControls` | Client invokes tool with invalid JSON arguments | Returns standard JSON-RPC 2.0 error code `-32602` (Invalid params) with Pydantic validation error details. |
| 8 | `HomeAssistantClient` | Home Assistant server is offline or unreachable (`ConnectionRefused`) | Returns clean structured error dictionary `{"error": "HA_UNREACHABLE", "message": "..."}`, no unhandled crash. |
| 9 | `HomeAssistantSimulator` | Service call to non-existent domain/service (e.g. `vacuum.fly_to_moon`) | Returns HTTP 404 with standard HA error payload `{"message": "Service not found"}`. |
| 10 | `HomeAssistantSimulator` | Entity toggled without explicit initial state in fixture | Defaults to `"on"`, creates state dynamically, and records state change. |
| 11 | Three.js Arc-Reactor | Browser environment does not support WebGL (e.g. headless CI) | Canvas context creation failure triggers `no-webgl` CSS class and renders 2D fallback pulse animation. |
| 12 | Three.js Arc-Reactor | Window resized rapidly or resized to zero width/height | `resize()` clamps dimensions to `Math.max(1, width)`, updates camera aspect ratio and projection matrix without NaN errors. |
| 13 | Sound Reactivity | Microphone permission denied or audio context suspended | Web Audio auto-unlock listener hooks click/touch gestures; visualizer defaults to smooth procedural idle wave. |
| 14 | WebSocket HUD | WebSocket connection drops unexpectedly | Client initiates auto-reconnect timer (exponential backoff 1s -> 2s -> 5s) and displays `"RECONNECTING"` pill. |

---

## 5. Technical Specifications & Architecture Designs

### Architecture R3: Multi-Agent Worker Orchestration

```
                      +------------------------------------------+
                      |         CognitiveSupervisor             |
                      |   (Real-time Voice & Action Dispatcher)  |
                      +--------------------+---------------------+
                                           |
                +--------------------------+--------------------------+
                |                                                     |
       [Foreground Vocal Loop]                                [Background Worker Queue]
       - Speech Input (VAD)                                   - asyncio.PriorityQueue
       - Fast LLM Stream (TTFB < 300ms)                       - Worker ThreadPool
       - Direct Tool Execution                                - Priority P0 / P1 / P2
                |                                                     |
                v                                                     v
+-------------------------------+                     +-------------------------------+
|         ToolRouter            |                     |       Specialized Workers     |
| - Pre-tool risk checks        |                     | - RouterAgent (search, read)  |
| - BRAIN-13 reconciliation     |                     | - RetrievalAgent (search,read)|
| - FastMCP actuation           |                     | - VerifierAgent (read only)   |
+-------------------------------+                     | - ConsolidatorAgent           |
                                                      | - CriticAgent (Reflexion)     |
                                                      +---------------+---------------+
                                                                      |
                                                                      v
                                                      +-------------------------------+
                                                      |   Global Workspace (GWT)      |
                                                      | - Proposal Competition        |
                                                      | - Coalition Broadcasting      |
                                                      +-------------------------------+
```

#### Detailed Class Specifications for R3:
1. `SubagentSpec(role: AgentRole, allowed_actions: List[str], max_steps: int = 3)`
2. `WorkerTask(task_id: str, priority: int, role: AgentRole, payload: Dict[str, Any], created_at: float)`
3. `AsyncWorkerQueue`:
   - Methods: `enqueue(task: WorkerTask) -> str`, `get_next_task() -> WorkerTask`, `mark_done(task_id: str, result: Dict[str, Any])`, `get_status() -> Dict[str, Any]`.
   - Priority bounds: P0 (0 = High/Immediate), P1 (10 = Normal), P2 (20 = Background).

---

### Architecture R4: FastMCP & IoT Home Assistant Integration

```
+------------------------------------------------------------------------------------+
|                         FastMCP Server: JarvisControls                             |
|                                                                                    |
|  [Tool Registry with Pydantic v2 Schemas]                                          |
|  1. ha_get_states(entity_id?, domain?)                                             |
|  2. ha_call_service(domain, service, service_data?, target_entity_ids?)            |
|  3. ha_toggle_device(entity_id)                                                    |
|  4. ha_query_entities(domain?, state_filter?, attribute_filters?)                  |
|  5. ha_get_config()                                                                |
|                                                                                    |
|  [Transports Supported]                                                            |
|  - JSON-RPC 2.0 Stdio Transport                                                    |
|  - SSE / HTTP Transport                                                            |
+------------------------------------------+-----------------------------------------+
                                           |
                                           v
+------------------------------------------------------------------------------------+
|                         HomeAssistantClient (REST Client)                          |
|  - Auth: Bearer <HASS_TOKEN>                                                       |
|  - Endpoints: GET /api/states, POST /api/services/<domain>/<service>               |
+------------------------------------------+-----------------------------------------+
                                           |
                        +------------------+------------------+
                        |                                     |
                        v                                     v
         [Live Home Assistant Instance]        [HomeAssistantSimulator (Mock)]
         - http://homeassistant.local:8123      - Pure Python in-memory server
                                                - Pre-seeded smart home entities
                                                - Thread-safe state transitions
```

#### Standard IoT Entities Seeded in `HomeAssistantSimulator`:
- `light.living_room_ceiling`: `{"state": "on", "attributes": {"brightness": 200, "friendly_name": "Living Room Ceiling Light", "rgb_color": [255, 220, 180]}}`
- `light.kitchen_strip`: `{"state": "off", "attributes": {"brightness": 0, "friendly_name": "Kitchen LED Strip"}}`
- `climate.living_room_thermostat`: `{"state": "heat", "attributes": {"current_temperature": 21.5, "temperature": 22.0, "hvac_modes": ["heat", "cool", "off"], "friendly_name": "Main Thermostat"}}`
- `switch.coffee_maker`: `{"state": "off", "attributes": {"friendly_name": "Smart Coffee Plug", "power_consumption_w": 0.0}}`
- `sensor.outdoor_temperature`: `{"state": "18.5", "attributes": {"unit_of_measurement": "°C", "friendly_name": "Outdoor Temperature"}}`
- `sensor.indoor_humidity`: `{"state": "45", "attributes": {"unit_of_measurement": "%", "friendly_name": "Indoor Humidity"}}`
- `binary_sensor.front_door_contact`: `{"state": "off", "attributes": {"device_class": "door", "friendly_name": "Front Door Sensor"}}`
- `media_player.living_room_tv`: `{"state": "idle", "attributes": {"volume_level": 0.35, "friendly_name": "Living Room Smart TV"}}`

---

### Architecture R5: Ultra-Modern GUI Dashboard & 3D Web HUD

```
+------------------------------------------------------------------------------------+
|                           Web HUD Client (Browser)                                 |
|                                                                                    |
|  +------------------------------------------------------------------------------+  |
|  | [Three.js 3D WebGL Holographic Arc-Reactor]                                   |  |
|  | - Procedural particle sphere & rotating energy rings (~3000 points)          |  |
|  | - State color transitions: Cyan (Idle), Emerald (Listening),                |  |
|  |   Amber (Thinking), Cobalt/White (Speaking), Crimson (Error)                 |  |
|  | - Web Audio AnalyserNode FFT Real-Time Sound Reactivity                       |  |
|  +------------------------------------------------------------------------------+  |
|                                                                                    |
|  +---------------------------+  +--------------------------+  +------------------+  |
|  | [OODA Thought Stream]     |  | [Memory Graph Viz]       |  | [System Health]  |  |
|  | - Observe / VAD / Audio   |  | - Force-directed 2D node |  | - Audio TTFB     |  |
|  | - Retrieve / Citations    |  |   network                |  | - Queue Depth    |  |
|  | - Plan / Subagent Tree    |  | - Confidence badges      |  | - LLM Latency    |  |
|  | - Act / FastMCP IoT Logs  |  | - Synapse relations      |  | - HA Link State  |  |
|  | - Reflect / Reflexion     |  | - Node inspection dialog |  | - Memory Notes   |  |
|  +---------------------------+  +--------------------------+  +------------------+  |
+------------------------------------------^-----------------------------------------+
                                           | WebSocket (/ws/hud)
                                           v
+------------------------------------------------------------------------------------+
|                         FastAPI Backend & WebSocket Hub                            |
|  - Real-time event broadcasting (vocal_state, ooda_step, audio_spectrum, telemetry)|
|  - REST endpoints: /api/hud/state, /api/hud/command, /api/hud/devices, /api/health |
+------------------------------------------------------------------------------------+
```

---

## 6. Caveats

1. `fastmcp` is not installed in the default Python environment (`C:\Python314\python.exe`). The implementation should build a clean, self-contained MCP-compliant JSON-RPC 2.0 tool registry utilizing standard `pydantic` v2 and `asyncio`, eliminating third-party packaging incompatibilities.
2. In headless environments (CI runners without GPU / display), Three.js WebGL canvas initialization returns `null` or throws a WebGL context creation error. The front-end must incorporate an automatic 2D CSS canvas fallback so automated testing and UI renders never crash.
3. No other caveats.

---

## 7. Conclusion

The specification mining for R3 (Multi-Agent Worker Orchestration), R4 (FastMCP & IoT Home Assistant Integration), and R5 (Ultra-Modern GUI Dashboard & 3D Web HUD) is fully complete. The architectural contracts, tool schemas, protocol frames, and error boundaries are documented with precision. 

The resulting design ensures:
- Strict non-blocking execution preserving <300ms vocal latency.
- Full compliance with Trust Boundary Invariants (P0-P18).
- 100% offline testable Home Assistant IoT simulation.
- High-performance, sound-reactive 3D holographic Three.js HUD with headless fallback resilience.

---

## 8. Verification Method

To verify the findings and test the upcoming implementations:
1. **Multi-Agent Orchestrator & Workers**:
   - `pytest -q projects/jarvis_cognitive_brain/tests/test_multiagent_orchestration.py`
   - `pytest -q projects/jarvis_cognitive_brain/tests/test_specialized_agents.py`
   - Assert: All worker agents (`RouterAgent`, `RetrievalAgent`, `VerifierAgent`, `ConsolidatorAgent`, `CriticAgent`) execute under least-privilege scoping without raising unhandled exceptions or violating P0-P18 trust boundaries.
2. **FastMCP & Home Assistant IoT Simulator**:
   - `pytest -q projects/jarvis_cognitive_brain/tests/test_fastmcp_ha_integration.py`
   - Assert: `ha_get_states`, `ha_call_service`, `ha_toggle_device`, and `ha_query_entities` correctly query and manipulate mock device states via `HomeAssistantSimulator`.
3. **Web HUD & Telemetry Backend**:
   - `pytest -q projects/jarvis_cognitive_brain/tests/test_hud_backend.py`
   - Assert: WebSocket `/ws/hud` establishes connections, broadcasts OODA steps and telemetry packets, and REST endpoints respond with valid JSON schemas.
