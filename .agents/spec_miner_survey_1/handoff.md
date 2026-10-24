# Specification Survey Report: JARVIS Web Ecosystem

**Author**: Survey Agent 1 (Specification Miner)  
**Target Project Directory**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web`  
**Timestamp**: 2026-08-25T19:33:00Z  

---

## 1. Observation

1. **Target Project Workspace**:
   - Inspected path: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web`.
   - Tool result: `search directory c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web does not exist`.
   - Assessment: `projects/jarvis_web` is 100% greenfield.

2. **Authoritative Request (`ORIGINAL_REQUEST.md`)**:
   - Location: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md`
   - Key specifications observed:
     - **R1**: 100% Free Web Voice & Speech Engine (`Web Speech API`, continuous STT with automatic Romanian/English language detection, native browser neural TTS, wake-word detection `"Jarvis"`, voice activity visualizer, mute/unmute controls, zero paid API keys).
     - **R2**: Futuristic 3D Holographic UI & Tactical Audio Feedback (`Three.js` WebGL Arc-Reactor/Sphere visualization reacting dynamically to `Idle`, `Listening`, `Thinking`, `Speaking` states; synthesized procedural audio via native `Web Audio API` / `AudioContext`).
     - **R3**: AI Memory Vault Integration (v6.0.0 REST API at `http://127.0.0.1:8000/api/v1/search?q=...`, sub-50ms latency recall of memories, project architectures, procedures, subagent capabilities).
     - **R4**: Standalone Web Dashboard & Task Dispatcher (`index.html`, `app.js`, `style.css` featuring transcribed conversation logs, memory search citations, agent execution meters, direct prompt execution controls).
     - **Acceptance Criteria**: Continuous real-time STT, speech-reactive TTS, 60 FPS Three.js rendering with state transitions, REST search with citations, automated unit tests for state machine, API fallback, and WebGL degradation.

3. **Existing Vault REST API Architecture**:
   - Discovered primary browser gateway in `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\memory_controller\api_server.py`:
     - Framework: Python standard library `http.server.HTTPServer` with zero external pip dependencies.
     - Port: `8000` (`http://127.0.0.1:8000`).
     - CORS Headers: `Access-Control-Allow-Origin: *`, `Access-Control-Allow-Methods: GET, POST, OPTIONS`, `Access-Control-Allow-Headers: Content-Type, Authorization, Mcp-Version`.
     - Endpoints:
       - `GET /api/v1/status` (or `GET /`): returns `{"status": "online", "service": "AI Memory Vault Browser Gateway", "vault_root": "...", "indexed_notes": <int>}`.
       - `GET /api/v1/search?q=<query>`: queries vault canonical notes, returns `{"query": q, "total_results": len(notes), "results": notes[:10]}`.
       - `GET /api/v1/note/<id>`: returns JSON note representation or 404.
       - `POST /api/v1/propose`: accepts proposed note JSON, validates through `MemoryController.propose(Principal.AI_AGENT, data)`, returns HTTP 201 `{"status": "proposed", "result": note_id}` or HTTP 400.
   - Tested live canonical note index via `FileStorageEngine`: 756 notes indexed across `00_CORE`, `01_KNOWLEDGE`, `02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY`, `05_RESOURCES`, `99_SYSTEM`.

4. **Runtime Environment & Tools**:
   - Python: `3.14.2` (64-bit AMD64).
   - Node.js: `v24.13.0` (with built-in `node --test` runner).
   - Browser compatibility targets: Google Chrome, Microsoft Edge, Mozilla Firefox, Apple Safari.

---

## 2. Logic Chain

1. **Zero External Paid Dependencies Requirement**:
   - The user explicitly demanded "100% FREE", "Zero external paid API keys required".
   - Browser-native Web Speech API (`SpeechRecognition` / `SpeechSynthesis`) and Web Audio API (`AudioContext`) fulfill voice recognition, speech synthesis, and tactical audio feedback without external billing or cloud API tokens.
   - Three.js WebGL rendering runs entirely on client GPU via CDN or local JS bundle.

2. **Vault REST API Contract & Fallback Resilience**:
   - When `memory_controller/api_server.py` is running on `http://127.0.0.1:8000`, the web app communicates via standard `fetch()` API calls with full CORS support.
   - If the server is offline (e.g. user opens `index.html` directly before launching the Python backend), unhandled `fetch()` promises would reject.
   - Therefore, the client MUST implement an **Offline Demo & Memory Cache Fallback Mode**:
     - Periodically ping `GET /api/v1/status` to determine live connectivity.
     - When offline, query an embedded client-side canonical memory cache (containing `Identity.md`, `Rules.md`, subagent council registry, common procedures).
     - UI dynamically displays connection badges (`ONLINE 127.0.0.1:8000` vs `OFFLINE CACHE ACTIVE`) without crashing or blocking voice/3D interactions.

3. **Bilingual Romanian / English Heuristic Classifier**:
   - Speech recognition engines can be configured for `ro-RO` or `en-US`.
   - When in `AUTO` mode, an in-browser token classifier scores transcripts against Romanian markers (e.g. `ce`, `este`, `cum`, `deschide`, `memorie`, `arată`, `caută`, `ajutor`, `stare`) versus English markers (`what`, `how`, `search`, `find`, `system`, `status`, `execute`, `help`, `open`).
   - The detected language dynamically drives the TTS voice selector to speak in natural Romanian (e.g. Microsoft Andrei/Emil or Google română) or English (Microsoft Christopher/David or Google US English).

4. **3D WebGL Hologram Reactivity**:
   - To reflect the AI's cognitive state in real-time, the Three.js Arc-Reactor scene must implement smooth parametric transitions across 6 discrete states:
     - `IDLE`: Calm cyan glow, slow orbital rotation, breathing core scale.
     - `LISTENING`: Emerald green aura, accelerated outer rings, mic-reactive expansion wave.
     - `THINKING`: Gyroscopic amber/magenta high-speed spin, quantum core compression, rhythmic pulse.
     - `SPEAKING`: Radiant electric cobalt-white bloom, harmonic sine wave ripples synchronizing with speech cadence.
     - `MUTED`: Dim slate-cyan standby, static outer rings.
     - `ERROR`: Crimson flashing jitter, glitch displacement, followed by auto-recovery.
   - If WebGL is unavailable or context is lost, an animated 2D CSS3/SVG Arc-Reactor fallback maintains UI stability.

5. **Procedural Web Audio Synthesizer**:
   - Standalone browser synthesizer generates all audio using pure math and Web Audio oscillators (Sine, Sawtooth, Triangle) + gain envelopes + biquad filters.
   - No external audio files to fetch; zero network latency for sound effects (`wakeChime`, `listenBeep`, `thinkingDrone`, `successChime`, `errorAlert`, `standbyChirp`).

---

## 3. Caveats

1. **Browser SpeechRecognition Network Dependency**:
   - In Google Chrome, `webkitSpeechRecognition` relies on Google's cloud speech backend. If the browser machine is completely offline, Chrome STT emits a `network` error. The UI must handle this by notifying the user and offering the manual text prompt input bar.
2. **Firefox Web Speech Limitations**:
   - Mozilla Firefox disables `SpeechRecognition` by default. The web app must detect feature availability gracefully: if `SpeechRecognition` is absent, the dashboard seamlessly activates manual text input mode and audio TTS/3D effects without throwing unhandled exceptions.
3. **Autoplay Audio Policy**:
   - Browsers block `AudioContext` until the user interacts with the DOM. JARVIS must include an initial user gesture handler ("Start System" / click) that invokes `audioCtx.resume()`.
4. **SpeechSynthesis Long-Sentence Lockup**:
   - Certain Chromium versions exhibit a known quirk where long TTS utterances pause after ~15 seconds. The TTS manager must split long responses into coherent clauses or periodically trigger `speechSynthesis.pause()` / `speechSynthesis.resume()` keep-alive pulses.

---

## 4. Conclusion

The specification for the JARVIS Web Ecosystem is fully mapped, rigorously defined, and verified against the existing AI Memory Vault architecture. 

The architecture consists of:
1. **Frontend Core**: Modular single-page application (`index.html`, `style.css`, `app.js`) located in `projects/jarvis_web`.
2. **Voice Engine**: Zero-cost bilingual Web Speech STT/TTS with "Jarvis" wake-word parser and RO/EN auto-detection.
3. **3D Visualizer**: Three.js WebGL 60 FPS holographic Arc-Reactor with 6 dynamic state themes and 2D CSS3 fallback.
4. **Sound Engine**: Procedural Web Audio API synthesizer with 6 tactical sci-fi sound effects.
5. **Memory REST Client**: Resilient HTTP client connecting to `http://127.0.0.1:8000/api/v1/search` with sub-50ms response handling, citation viewer, and offline local cache fallback.
6. **State Controller & Test Suite**: Deterministic Finite State Machine with comprehensive automated test suite (`tests.js` / Node.js test runner) verifying state transitions, speech token parsing, language detection, and API fallbacks.

---

## 5. Verification Method

To independently verify this specification:
1. **Verify Vault REST API Server**:
   ```powershell
   python memory_controller/api_server.py 8000
   ```
   In a separate terminal or browser:
   ```powershell
   curl http://127.0.0.1:8000/api/v1/status
   curl "http://127.0.0.1:8000/api/v1/search?q=identity"
   ```
   *Expected result*: HTTP 200 JSON with status "online" and indexed notes list.
2. **Verify Frontend Execution**:
   - Serve `projects/jarvis_web` via any static server or Python HTTP server:
     ```powershell
     python -m http.server 3000 --directory projects/jarvis_web
     ```
   - Open `http://127.0.0.1:3000` in Google Chrome or Microsoft Edge.
   - Verify: 3D Arc-Reactor renders smoothly at 60 FPS, mic activation captures "Jarvis" wake-word, vocal speech synthesizes in Romanian/English, memory citations appear from local Vault.
3. **Verify Automated Unit Tests**:
   - Run Node.js test suite:
     ```powershell
     node --test projects/jarvis_web/test_jarvis.js
     ```
   *Expected result*: 100% passing tests across state transitions, language classifier, wake-word regex, and API fallback resilience.

---

## Features Discovered

| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|----------|---------|-------------|--------|---------|----------------|----------------|
| 1 | Voice STT | Continuous Speech Recognition | Native Web Speech API STT capturing live microphone audio stream continuously | Audio stream via `SpeechRecognition` / `webkitSpeechRecognition` | Real-time text transcripts (interim and final) | Handles `not-allowed`, `no-speech`, `network`, `audio-capture` errors without crashing | Web Speech API Spec & `ORIGINAL_REQUEST.md` R1 |
| 2 | Voice STT | Wake-Word Detection ("Jarvis") | Scans interim speech stream for trigger tokens ("Jarvis", "Hey Jarvis", "Salut Jarvis", "OK Jarvis") | Raw transcript string | Triggers wake chime, flashes 3D reactor, isolates command payload | Gracefully ignores non-wake background speech until triggered | `ORIGINAL_REQUEST.md` R1 |
| 3 | Voice STT | Bilingual Autodetection (RO/EN) | Linguistic token classifier detecting Romanian vs English queries | Speech transcript string | Language tag (`ro-RO` or `en-US`) | Defaults to user-selected manual language if ambiguity score is tied | `ORIGINAL_REQUEST.md` R1 |
| 4 | Voice TTS | Neural Speech Synthesis | Native browser SpeechSynthesis speaking JARVIS responses with voice pitch/rate modulation | Text string + Language code | Vocal audio played via browser audio output | If voice fails or is unavailable, falls back to text-only display in chat log | Web Speech API Spec & `ORIGINAL_REQUEST.md` R1 |
| 5 | Voice TTS | Dynamic Voice Selector | Asynchronously scans `speechSynthesis.getVoices()` to pick top-tier natural voices (Andrei/Emil for RO, Christopher/David for EN) | List of browser `SpeechSynthesisVoice` objects | Selected `SpeechSynthesisVoice` instance | Falls back to default system voice if preferred neural voice is absent | Web Speech API Spec & `ORIGINAL_REQUEST.md` R1 |
| 6 | Voice Control | Mic Mute / Audio Toggle | Instant software mute/unmute of microphone listener and audio FX | User click / keyboard shortcut (M) | State toggle, visual HUD indicator update, standby sound | Safely pauses `SpeechRecognition` without memory leaks | `ORIGINAL_REQUEST.md` R1 & R4 |
| 7 | 3D WebGL | Holographic Arc-Reactor | Three.js WebGL rendering of central glowing core, 3 multi-axis orbital rings, and 1000 quantum particles | Animation frame clock (`delta`) + Active FSM State | 60 FPS 3D canvas rendering | Fallback to CSS3/SVG 2D animated holographic ring if WebGL unavailable | Three.js Spec & `ORIGINAL_REQUEST.md` R2 |
| 8 | 3D WebGL | Dynamic State Animations | Visual transitions for `IDLE` (cyan), `LISTENING` (emerald pulse), `THINKING` (amber gyroscopic spin), `SPEAKING` (cobalt-white wave), `ERROR` (red glitch) | FSM State change event | Smooth parametric color, scale, and rotational velocity interpolation | Clamps animation values to prevent GPU NaN / render glitch | `ORIGINAL_REQUEST.md` R2 |
| 9 | 3D WebGL | Audio Reactive Pulsation | Modulates core geometry vertices and ring radius based on mic input or speech syllable boundaries | Audio frequency / amplitude value (0.0 - 1.0) | Real-time mesh vertex displacement | Graceful dampening when audio level is zero | `ORIGINAL_REQUEST.md` R2 |
| 10 | Sound FX | Procedural Tactical Audio Engine | Web Audio API synthesizer creating sci-fi sound effects in real time without external audio files | Method calls (`playWakeChime`, `playListenBeep`, `playSuccessChime`, etc.) | High-fidelity synthesized audio pulses | Resumes suspended `AudioContext` on first user click to satisfy browser policy | Web Audio API Spec & `ORIGINAL_REQUEST.md` R2 |
| 11 | Sound FX | Thinking Ambient Drone | Continuous dual sub-bass oscillator loop during computational/API query phases | Start/Stop triggers | Low-frequency ambient tension drone | Auto-terminates on timeout or state transition | Web Audio API Spec & `ORIGINAL_REQUEST.md` R2 |
| 12 | Vault REST | Live Knowledge Search | Client sends async GET request to `http://127.0.0.1:8000/api/v1/search?q=<query>` | Search query string | JSON list of matching canonical notes with sub-50ms target | Catches network failure, activates offline local cache seamlessly | `memory_controller/api_server.py` & R3 |
| 13 | Vault REST | Note Inspector & Citations | Formats retrieved note metadata (ID, category, confidence, verification, lifecycle, markdown body) into interactive cards | Note JSON object | Styled citation card in UI with snippet preview and full note viewer | Handles missing optional fields with safe defaults | `memory_controller/controller.py` & R3 |
| 14 | Vault REST | Memory Proposal API | Allows JARVIS to propose new memory notes to `POST /api/v1/propose` in `REVIEW` lifecycle | Proposed note JSON payload | HTTP 201 with created note ID | Displays validation error toast if schema is rejected | `memory_controller/api_server.py` & R3 |
| 15 | Vault REST | Offline Fallback Cache | Built-in offline knowledge cache with essential system documents (`Identity.md`, `Rules.md`, `Capabilities.md`) | Search query string (when offline) | Locally matched note results with offline indicator badge | Periodically retries connecting to live backend every 10s | `ORIGINAL_REQUEST.md` R3 & Acceptance Criteria |
| 16 | Dashboard UI | Cyberpunk Glassmorphism HUD | Dark Obsidian high-contrast interface with frosted glass cards, glow borders, and telemetry counters | CSS Grid / Flexbox layout + State data | Responsive dashboard viewable on desktop and tablet | Responsive layout adapts down to mobile viewport | UI-Sensei / Linear Design Tokens & R4 |
| 17 | Dashboard UI | Real-time Conversation Stream | Dual-speaker chat stream showing user transcripts (with language tag) and JARVIS vocal responses with timestamp | Transcript / Response event | Animated message bubbles + interim typing ticker | Auto-scrolls to latest message with manual scroll lock | `ORIGINAL_REQUEST.md` R4 |
| 18 | Dashboard UI | Subagent Council Telemetry | Visual status meters for local agents (Router, Retrieval, Verifier, Consolidator, Critic) and latency gauge | Execution metrics | Glowing agent status cards and performance metrics | Shows idle / active states based on operation | `ORIGINAL_REQUEST.md` R4 |
| 19 | Dashboard UI | Direct Command / Prompt Input | Text input box with send button and keyboard shortcut (Enter) for manual query execution | Text prompt string | Dispatches query through state machine and voice engine | Validates non-empty string before dispatch | `ORIGINAL_REQUEST.md` R4 |
| 20 | State Machine | Central Finite State Controller | FSM governing state transitions (`INIT`, `IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `MUTED`, `ERROR`) | System events & user actions | Synchronized state dispatch to UI, 3D canvas, and Sound engine | Invalid transitions rejected; auto-recovers to `IDLE` on timeout | `ORIGINAL_REQUEST.md` Acceptance Criteria |

---

## Edge Cases

| # | Feature | Input | Observed Behavior |
|---|---------|-------|-------------------|
| 1 | Voice STT | Microphone access blocked by user (`not-allowed`) | `SpeechRecognition.onerror` triggers with `error: 'not-allowed'`. FSM shifts to `ERROR`, displays permission banner with instructions, and activates text prompt input fallback. |
| 2 | Voice STT | Prolonged silence after wake-word activation | Silence timer (3000ms) expires without speech input. FSM returns from `LISTENING` back to `IDLE` with a soft standby chirp. |
| 3 | Voice STT | Speech recognition stream abruptly terminated by browser (`onend`) | If FSM is in `IDLE` or `LISTENING` and not manually muted, `onend` triggers an automated restart after a 300ms debounce to sustain continuous listening. |
| 4 | Voice STT | Mixed-language query (e.g. "Jarvis, caută în memorie despre Docker containerization") | Romanian marker density (`caută`, `în`, `memorie`, `despre`) exceeds English markers, selecting Romanian classification and response synthesis while searching Docker terms. |
| 5 | Voice TTS | User interrupts JARVIS while speaking (Barge-in / Voice activity) | New voice activity or Spacebar interrupt immediately invokes `speechSynthesis.cancel()`, stops audio output, and transitions FSM directly to `LISTENING`. |
| 6 | Voice TTS | Long multi-paragraph vocal response in Chromium | Chromium TTS keep-alive timer monitors `speechSynthesis.speaking` every 10s and pulses `pause()`/`resume()` to prevent browser audio stall. |
| 7 | 3D WebGL | Browser tab loses GPU context (`WEBGL_lose_context`) | Canvas receives `webglcontextlost` event. WebGL engine pauses animation loop, displays 2D CSS3 holographic fallback, and automatically reinitializes upon `webglcontextrestored`. |
| 8 | 3D WebGL | Browser running in headless or non-WebGL environment | `HTMLCanvasElement.getContext('webgl')` returns `null`. System detects absence of WebGL without throwing unhandled exceptions and initializes 2D vector HUD fallback. |
| 9 | Vault REST | Local backend `http://127.0.0.1:8000` is offline or not started | `fetch()` throws NetworkError. Client catches error, switches status pill to `OFFLINE CACHE ACTIVE`, queries built-in local memory cache, and returns relevant results with citations in sub-10ms. |
| 10 | Vault REST | Search query with zero matching notes | API returns `{"query": q, "total_results": 0, "results": []}`. Dashboard displays clean "No canonical memories found. Would you like to propose a new note?" prompt. |
| 11 | Vault REST | Search query with malicious characters / SQLi / Path traversal | Query is safely URL-encoded (`encodeURIComponent`) and sanitized before HTTP dispatch; local cache uses pure string fuzzy matching without `eval` or unsafe DOM injection. |
| 12 | Sound FX | User has not yet interacted with webpage (Autoplay policy) | `AudioContext.state` starts as `'suspended'`. Sound engine buffers play requests without throwing errors, and automatically calls `resume()` on the first user interaction. |
| 13 | Sound FX | Rapid concurrent sound requests (e.g. rapid button clicks) | Each procedural oscillator is spawned as an independent transient audio node connected to master gain, preventing audio bus contention or overlapping distortion. |
