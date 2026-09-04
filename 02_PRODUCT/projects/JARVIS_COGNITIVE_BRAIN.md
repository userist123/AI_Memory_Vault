# Project: Jarvis Cognitive Brain ("Creier Vorbitor")

## Architecture
Autonomous, local, self-improving Cognitive Brain system integrating real-time streaming audio I/O with barge-in interruption, a stateful OODA cognitive loop, persistent SQLite WAL + Markdown memory, multi-agent least-privilege background workers, FastMCP IoT Home Assistant integration, and an ultra-modern 3D WebGL HUD dashboard.

```
+---------------------------------------------------------------------------------------------------------+
|                                    JARVIS COGNITIVE BRAIN ARCHITECTURE                                  |
+---------------------------------------------------------------------------------------------------------+
|                                                                                                         |
|  [Microphone Input] <---> [Cascaded Audio Pipeline (STT/TTS/Barge-in)] <---> [Speaker Output]           |
|                                     │                     ▲                                             |
|                                     │ Transcribed Text    │ Streaming Audio Chunks (TTFB < 300ms)       |
|                                     ▼                     │                                             |
|                     +───────────────────────────────────────────────────+                               |
|                     |            Cognitive Brain Daemon (OODA)          |                               |
|                     |  - Observe: SensorBuffer, Intent Classifier       |                               |
|                     |  - Retrieve: Multi-layer Associative Memory       |                               |
|                     |  - Reason/Plan: ActivePlan State Machine          |                               |
|                     |  - Act: Tool Router (FastMCP)                     |                               |
|                     |  - Reflect: 6-Stage Formal Reflexion              |                               |
|                     |  - Consolidate: Lesson Synthesis & Reconsolidation|                               |
|                     +───────────────────────────────────────────────────+                               |
|                                     │                     │                                             |
|            ┌────────────────────────┴────────┐   ┌────────┴────────────────────────┐                    |
|            ▼                                 ▼   ▼                                 ▼                    |
|  [Multi-Agent Supervisor]         [Dual Storage Engine]                 [FastMCP & IoT Server]          |
|  - Router Agent (Search/Read)     - SQLite WAL Engine                   - JarvisControls FastMCP Server |
|  - Retrieval Agent (Read/Search)  - Markdown Sync Engine                - Home Assistant REST Client    |
|  - Verifier Agent (Read)          - Chained SHA-256 Audit Log           - HA In-Memory REST Simulator   |
|  - Consolidator Agent (Propose)                                                                         |
|  - Critic Agent (Reflexion)                                                                             |
|            │                                                                       │                    |
|            └─────────────────────────────────┬─────────────────────────────────────┘                    |
|                                              ▼                                                          |
|                             [3D Web HUD & Dashboard Telemetry]                                         |
|                             - FastAPI WebSocket Server (`/ws/hud`)                                      |
|                             - Three.js 3D WebGL Holographic Arc-Reactor                                 |
|                             - Real-Time OODA Thought Stream Visualizer                                  |
|                             - Dynamic Interactive Memory Graph & Health Meters                          |
+---------------------------------------------------------------------------------------------------------+
```

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Modular LLM Provider Layer | Abstract `BaseLLMProvider` with Ollama (`qwen2.5-coder`), Google Antigravity SDK, Claude, and Mock implementations | M1 | Survey R1 |
| 2 | Complete Stateful OODA Loop | Stateful cycle: Observe, Retrieve, Reason, Plan, Act, Reflect, Consolidate | M1 | Survey R1 |
| 3 | Multi-Layer Associative Recall | BM25 search, semantic cosine similarity, ACT-R base-level decay, wikilinks graph spreading activation, and recursive CTE supersession lineage traversal | M1 | Survey R1 |
| 4 | Dual Persistence Storage Engine | Thread-safe SQLite engine in WAL mode (`PRAGMA busy_timeout=5000`, `BEGIN IMMEDIATE`) paired with atomic Markdown file synchronization | M1 | Survey R1 |
| 5 | Trust Boundary Invariants (P0-P18) | Enforces least-privilege permission validation, attestation locks, frontmatter constraints, and tamper-evident SHA-256 audit logs | M1 | Survey R1 |
| 6 | Continuous STT with Silero VAD | 16kHz audio capture with Silero VAD (500ms trailing silence threshold) feeding local `faster-whisper` CTranslate2 engine with Romanian/English auto-detection | M2 | Survey R2 |
| 7 | Streaming TTS Engine (Kokoro-82M) | Streaming sentence/clause chunking with `Kokoro-82M` ONNX 24kHz synthesis achieving <300ms TTFB | M2 | Survey R2 |
| 8 | Sub-50ms Barge-In Interruption | Immediate audio interruption halting DAC playback, flushing audio queues, and triggering LLM streaming task cancellation | M2 | Survey R2 |
| 9 | Headless Audio Drivers & Mock Engine | Driver abstraction (`AudioInputDriver`, `AudioOutputDriver`, `STTEngine`, `TTSEngine`, `VADEngine`) with virtual/mock implementations for headless testing | M2 | Survey R2 |
| 10 | Multi-Agent Supervisor | Non-blocking background worker pool (PriorityQueue) isolating heavy tasks from real-time audio loop | M3 | Survey R3 |
| 11 | Least-Privilege Specialized Agents | Role-scoped worker implementations: Router, Retrieval, Verifier, Consolidator, and Critic (formal 6-stage Reflexion) | M3 | Survey R3 |
| 12 | FastMCP `JarvisControls` Server | Standard FastMCP JSON-RPC 2.0 tool definitions for IoT device querying and manipulation | M4 | Survey R4 |
| 13 | Home Assistant REST Client | High-reliability async client for querying and controlling Home Assistant entities via `/api/states` and `/api/services` | M4 | Survey R4 |
| 14 | Local Home Assistant Simulator | In-memory mock HA REST API daemon with realistic state transitions and service dispatch for offline testing | M4 | Survey R4 |
| 15 | 3D WebGL Holographic Visualizer | Three.js particle Arc-Reactor / sphere with FFT sound reactivity for Idle, Listening, Thinking, Speaking, Error states | M5 | Survey R5 |
| 16 | Real-Time OODA Thought Stream | Live telemetry feed visualizing current cognitive phase, active plan step, and decision logic | M5 | Survey R5 |
| 17 | Interactive Memory Graph Visualizer | 2D/3D force-directed graph rendering active memory nodes, clusters, and wikilink synapses | M5 | Survey R5 |
| 18 | System Health & Audio Controls | Web dashboard displaying CPU/memory, VAD meter, latency gauges, and audio mute/unmute controls | M5 | Survey R5 |
| 19 | Dual-Track 4-Tier E2E Test Suite | Comprehensive opaque-box test suite covering Tiers 1-4 (Features, Boundaries, Pairwise, Real-World Workloads) | M6 | Survey R1-R5 |
| 20 | Tier 5 Adversarial Coverage Hardening | White-box adversarial testing targeting edge cases, race conditions, memory leaks, and failure recovery | M6 | Survey R1-R5 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Cognitive OODA Engine & Memory Storage | R1: LLM Providers, OODA cycle, Associative Recall, SQLite WAL engine, Markdown sync, Invariants P0-P18 | none | DONE |
| M2 | Cascaded Audio Pipeline & Barge-In | R2: Silero VAD, faster-whisper STT, Kokoro-82M ONNX TTS, sub-50ms Barge-in cancellation, Audio drivers | M1 | DONE |
| M3 | Multi-Agent Worker Orchestration | R3: Supervisor coordinator, Background queue, Router, Retrieval, Verifier, Consolidator, Critic | M1 | DONE |
| M4 | FastMCP IoT & Home Assistant Integration | R4: FastMCP JarvisControls, HA REST client, HA In-Memory Simulator, integration with Act phase | M1, M3 | DONE |
| M5 | Ultra-Modern 3D Web HUD & Dashboard | R5: Three.js WebGL visualizer, Audio reactivity, OODA stream, Memory graph, FastAPI WebSocket hub | M1, M2, M3, M4 | IN_PROGRESS |
| M6 | E2E Testing Suite & Adversarial Hardening | E2E Test Runner, 4-Tier Opaque-box Test Suite (Tiers 1-4), Tier 5 Adversarial Hardening | M1, M2, M3, M4, M5 | PLANNED |

## Code Layout
Target root: `projects/jarvis_cognitive_brain`

```
projects/jarvis_cognitive_brain/
├── pyproject.toml
├── README.md
├── jarvis/
│   ├── __init__.py
│   ├── config.py
│   ├── core/
│   │   ├── __init__.py
│   │   ├── executive.py
│   │   ├── ooda.py
│   │   ├── context.py
│   │   └── models.py
│   ├── llm/
│   │   ├── __init__.py
│   │   ├── base.py
│   │   ├── ollama_provider.py
│   │   ├── cloud_providers.py
│   │   └── mock_provider.py
│   ├── memory/
│   │   ├── __init__.py
│   │   ├── sqlite_engine.py
│   │   ├── markdown_sync.py
│   │   ├── recall.py
│   │   ├── activation.py
│   │   ├── consolidation.py
│   │   ├── reflection.py
│   │   └── invariants.py
│   ├── audio/
│   │   ├── __init__.py
│   │   ├── pipeline.py
│   │   ├── vad.py
│   │   ├── stt.py
│   │   ├── tts.py
│   │   ├── bargein.py
│   │   ├── drivers.py
│   │   └── chunker.py
│   ├── agents/
│   │   ├── __init__.py
│   │   ├── supervisor.py
│   │   ├── router.py
│   │   ├── retrieval.py
│   │   ├── verifier.py
│   │   ├── consolidator.py
│   │   └── critic.py
│   ├── iot/
│   │   ├── __init__.py
│   │   ├── fastmcp_server.py
│   │   ├── ha_client.py
│   │   └── ha_simulator.py
│   └── hud/
│       ├── __init__.py
│       ├── server.py
│       ├── static/
│       │   ├── index.html
│       │   ├── css/style.css
│       │   ├── js/app.js
│       │   ├── js/visualizer3d.js
│       │   └── js/memory_graph.js
│       └── templates/
├── tests/
│   ├── conftest.py
│   ├── unit/
│   │   ├── test_llm_providers.py
│   │   ├── test_ooda_loop.py
│   │   ├── test_memory_storage.py
│   │   ├── test_audio_pipeline.py
│   │   ├── test_bargein.py
│   │   ├── test_multi_agent.py
│   │   ├── test_fastmcp_iot.py
│   │   └── test_hud_server.py
│   └── e2e/
│       ├── test_runner.py
│       ├── tier1_features/
│       ├── tier2_boundaries/
│       ├── tier3_combinations/
│       ├── tier4_workloads/
│       └── tier5_adversarial/
```

## Interface Contracts
### 1. `BaseLLMProvider`
```python
class BaseLLMProvider(ABC):
    @abstractmethod
    async def generate(self, prompt: str, **kwargs) -> str: ...
    @abstractmethod
    async def stream(self, prompt: str, cancellation_token: Optional[CancellationToken] = None, **kwargs) -> AsyncIterator[str]: ...
    @abstractmethod
    async def generate_structured(self, prompt: str, schema: Type[BaseModel], **kwargs) -> BaseModel: ...
```

### 2. `AudioEngine` & `BargeInController`
```python
class BargeInController:
    def trigger_bargein(self, reason: str = "speech_detected") -> None: ...
    def register_cancellation_callback(self, cb: Callable[[], None]) -> None: ...
    @property
    def is_interrupted(self) -> bool: ...

class AudioPipeline:
    async def start(self) -> None: ...
    async def stop(self) -> None: ...
    async def process_utterance(self, audio_data: np.ndarray) -> str: ...
    async def speak_stream(self, text_stream: AsyncIterator[str], cancellation_token: CancellationToken) -> None: ...
```

### 3. `CognitiveExecutive` (OODA)
```python
class CognitiveExecutive:
    async def process_cycle(self, input_text: str, source: str = "voice") -> OODACycleResult: ...
    async def observe(self, input_text: str) -> PerceptionEvent: ...
    async def retrieve(self, perception: PerceptionEvent) -> List[MemoryNote]: ...
    async def reason_and_plan(self, perception: PerceptionEvent, context: List[MemoryNote]) -> ActivePlan: ...
    async def act(self, plan: ActivePlan) -> List[StepExecutionResult]: ...
    async def reflect(self, plan: ActivePlan, results: List[StepExecutionResult]) -> Optional[ReflectionLesson]: ...
    async def consolidate(self, lesson: ReflectionLesson) -> None: ...
```

### 4. `FastMCP IoT & Home Assistant Client`
```python
class HomeAssistantClient:
    async def get_states(self) -> List[EntityState]: ...
    async def get_state(self, entity_id: str) -> Optional[EntityState]: ...
    async def call_service(self, domain: str, service: str, service_data: dict) -> ServiceResponse: ...
```
