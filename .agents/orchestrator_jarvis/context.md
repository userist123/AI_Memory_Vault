# CONTEXT & ARCHITECTURAL SPECIFICATION

## Project Scope
Build the autonomous, self-improving local Cognitive Brain ("Creier Vorbitor") in `projects/jarvis_cognitive_brain`.

## Key Technical Components
1. **R1: Cognitive Loop Core & Persistent Memory**
   - Stateful OODA Cycle: Observe (speech/text classification), Retrieve (associative/semantic recall from Obsidian Markdown & SQLite WAL storage), Reason/Plan (structured multi-step plans), Act (tool calls via FastMCP), Reflect (Reflexion self-critique), Consolidate (store refined lessons).
   - Google Antigravity SDK daemon & modular LLM Provider interface (Ollama with `qwen2.5-coder` / fallback to cloud providers).
2. **R2: Cascaded Audio Pipeline & Barge-In Engine**
   - STT: Silero VAD (500ms silence threshold) + Faster-Whisper local engine.
   - TTS: Kokoro-82M ONNX local synthesis (<300ms TTFB).
   - Barge-in/AEC: Immediate audio interruption upon VAD trigger, canceling active TTS playback and halting active LLM inference streaming.
3. **R3: Multi-Agent Worker Coordination**
   - Supervisor + least-privilege workers: Router (query classification), Retrieval (memory search), Verifier (invariant & schema check), Consolidator (knowledge distillation), Critic (reflexion & critique).
   - Non-blocking background worker queue preserving real-time voice latency.
4. **R4: FastMCP IoT & Home Assistant Integration**
   - FastMCP tool server `JarvisControls`.
   - Tool endpoints to query and mutate IoT device states via `/api/states`.
   - Lightweight local Home Assistant REST API simulator for offline verification.
5. **R5: Ultra-Modern 3D Web HUD & Dashboard**
   - WebGL 3D holographic visualizer with real-time sound reactivity.
   - States: Idle (breathing ambient glow), Listening (reactive wave pulse), Thinking (orbiting OODA rings), Speaking (dynamic frequency displacement).
   - Real-time OODA thought visualizer, interactive memory graph, system health meters, and telemetry dashboard.

## Target Constraints & Environment
- Python 3.12+ compatibility.
- Zero external paid API hard-dependencies (100% offline runnable with local models/simulators).
- Thread-safe concurrency, atomic state checkpoints, SQLite WAL mode with `PRAGMA busy_timeout=5000`.
