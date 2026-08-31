# MASTER EXECUTION PLAN — Cognitive Brain ("Creier Vorbitor")

## Objective
Implement an autonomous, production-grade local Cognitive Brain in `projects/jarvis_cognitive_brain` meeting requirements R1–R5 under Python 3.12+, with full E2E test suites and rigorous multi-agent verification.

## Phase Breakdown

### Phase 0: Survey & Technical Spec Mining
- Dispatch 3 Explorers / Spec Miners:
  - Explorer 1 (`survey_cognitive_memory`): Investigate Cognitive OODA loop requirements, Google Antigravity SDK, Ollama integration, memory schema & SQLite WAL storage.
  - Explorer 2 (`survey_audio_bargein`): Investigate Silero VAD (500ms threshold), Faster-Whisper STT, Kokoro-82M ONNX TTS, and real-time Barge-in/AEC interruption architecture.
  - Explorer 3 (`survey_iot_fastmcp_hud`): Investigate FastMCP `JarvisControls`, Home Assistant `/api/states` REST API simulator, Multi-agent workers (Router, Retrieval, Verifier, Consolidator, Critic), and 3D Web HUD.
- Synthesize findings into `PROJECT.md` (Feature Inventory, Architecture, Interface Contracts, Code Layout).

### Phase 1: Dual-Track Execution Setup
- Track A: E2E Testing Track Orchestrator / Test Writer — setup test infra, fixtures, and 4-tier test suites (Feature coverage, Corner cases, Pairwise combinations, Real-world workloads).
- Track B: Implementation Track Sub-orchestrators / Workers for Milestones M1 through M5.

### Phase 2: Milestone Implementation & Verification Loop
- **Milestone 1 (R1)**: Cognitive Loop Core (Observe, Retrieve, Reason, Plan, Act, Reflect, Consolidate) + Obsidian Markdown/SQLite WAL persistent memory + Google Antigravity SDK & Ollama client.
- **Milestone 2 (R2)**: Cascaded Audio Pipeline (Silero VAD + Faster-Whisper + Kokoro-82M ONNX + Barge-in interruption manager with thread-safe cancellation tokens).
- **Milestone 3 (R3)**: Multi-Agent Worker Orchestration (Supervisor + Router, Retrieval, Verifier, Consolidator, Critic with least-privilege scoping).
- **Milestone 4 (R4)**: FastMCP IoT Server & Home Assistant Simulator (FastMCP `JarvisControls`, REST `/api/states` query/mutation, mock HA daemon).
- **Milestone 5 (R5)**: Ultra-Modern 3D Web HUD & Dashboard (Three.js WebGL reactive visualizer, OODA thought stream, memory graph, health telemetry, FastAPI/WebSocket backend).

### Phase 3: Independent Review, Stress Testing & Forensic Integrity Audit
- 2 Independent Reviewers (`teamwork_preview_reviewer`) verify code quality, contract compliance, and edge case safety.
- 2 Challengers (`teamwork_preview_challenger`) run empirical stress testing, concurrency validation, and latency benchmarks (<300ms TTFB audio, barge-in sub-100ms response).
- Forensic Auditor (`teamwork_preview_auditor`) performs non-bypassable integrity verification.

### Phase 4: Final Acceptance & Victory Handoff
- 100% pass on E2E test suite (Tiers 1–4).
- Tier 5 Adversarial Coverage Hardening.
- Final human summary report and handoff.
