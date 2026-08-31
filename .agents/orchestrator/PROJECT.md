# Project: JARVIS Web Ecosystem

## Architecture
Modular ES6 / Web Standards Client Architecture targeting zero-cost, 100% free operation with browser-native APIs and local Python Memory Vault REST backend:
- `projects/jarvis_web/index.html` — Cyberpunk HUD, 3D Canvas, Glassmorphism panels, Conversation log, Agent telemetry, Input dispatcher.
- `projects/jarvis_web/style.css` — High-contrast Dark Obsidian theme, frosted glass cards, reactive pulse glow, responsive mobile/desktop grid.
- `projects/jarvis_web/js/state_machine.js` — Finite State Machine (`IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `MUTED`, `ERROR`) synchronizing voice, visuals, audio, and network operations.
- `projects/jarvis_web/js/voice_engine.js` — Continuous Web Speech STT, Romanian/English token classifier, wake-word detection ("Jarvis"), SpeechSynthesis neural voice manager with queue & barge-in support.
- `projects/jarvis_web/js/hologram.js` — Three.js 60 FPS 3D Arc-Reactor / Sphere visualization with 6 dynamic reactive states, shaders, particle system, and 2D Canvas/CSS fallback.
- `projects/jarvis_web/js/sound_engine.js` — Procedural Web Audio API sound synthesizer with 6 tactical sci-fi audio effects (wake chime, listening beep, thinking drone, success chime, error alert, standby chirp).
- `projects/jarvis_web/js/vault_client.js` — Local AI Memory Vault REST client (`http://127.0.0.1:8000/api/v1/search`), sub-50ms timeout handling, citation cards, and built-in offline memory cache.
- `projects/jarvis_web/js/app.js` — Main bootstrap coordinator wiring UI components, voice engine, 3D hologram, sound synthesizer, and REST client.
- `projects/jarvis_web/test/test_jarvis.js` — Comprehensive automated Node.js test harness verifying state machine transitions, wake-word detection, language classifier, REST API offline fallback, and WebGL degradation.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Continuous Speech Recognition | Native Web Speech API STT capturing live mic audio stream | M1 | ORIGINAL_REQUEST R1 |
| 2 | Wake-Word Detection ("Jarvis") | Scans interim speech stream for trigger tokens ("Jarvis", "Hey Jarvis") | M1 | ORIGINAL_REQUEST R1 |
| 3 | Bilingual Autodetection (RO/EN) | Linguistic token classifier detecting Romanian vs English queries | M1 | ORIGINAL_REQUEST R1 |
| 4 | Neural Speech Synthesis | Native browser SpeechSynthesis speaking responses with pitch/rate modulation | M1 | ORIGINAL_REQUEST R1 |
| 5 | Dynamic Voice Selector | Scans browser voices to select top natural RO (Andrei/Emil) and EN (Christopher/Guy) | M1 | ORIGINAL_REQUEST R1 |
| 6 | Mic Mute / Audio Toggle | Instant software mute/unmute of mic listener and audio FX | M1 | ORIGINAL_REQUEST R1 |
| 7 | Holographic Arc-Reactor Three.js | 60 FPS 3D rendering of central core, 3 orbital gimbal rings, and 1000 quantum particles | M2 | ORIGINAL_REQUEST R2 |
| 8 | Dynamic State Animations (6 states)| Visual transitions for IDLE, LISTENING, THINKING, SPEAKING, MUTED, ERROR | M2 | ORIGINAL_REQUEST R2 |
| 9 | Audio Reactive Pulsation | Modulates core geometry vertices and ring radius based on mic/speech volume | M2 | ORIGINAL_REQUEST R2 |
| 10 | Procedural Tactical Audio Engine | Web Audio API synthesizer creating 6 sci-fi sound effects without external audio files | M2 | ORIGINAL_REQUEST R2 |
| 11 | Thinking Ambient Drone | Continuous dual sub-bass oscillator loop during computational/search phases | M2 | ORIGINAL_REQUEST R2 |
| 12 | Live Knowledge Search REST | Async client querying `http://127.0.0.1:8000/api/v1/search?q=...` | M3 | ORIGINAL_REQUEST R3 |
| 13 | Note Inspector & Citations | Formats retrieved note metadata into interactive cards with wikilinks | M3 | ORIGINAL_REQUEST R3 |
| 14 | Memory Proposal API | Proposes new memory notes to `POST /api/v1/propose` in REVIEW lifecycle | M3 | ORIGINAL_REQUEST R3 |
| 15 | Offline Fallback Cache | Built-in offline knowledge cache with essential system documents | M3 | ORIGINAL_REQUEST R3 |
| 16 | Cyberpunk Glassmorphism HUD | Dark Obsidian high-contrast UI with frosted glass cards and glow borders | M4 | ORIGINAL_REQUEST R4 |
| 17 | Real-time Conversation Stream | Dual-speaker chat stream showing user transcripts and vocal responses | M4 | ORIGINAL_REQUEST R4 |
| 18 | Subagent Council Telemetry | Visual status meters for local agents (Router, Retrieval, Verifier, Consolidator, Critic) | M4 | ORIGINAL_REQUEST R4 |
| 19 | Direct Command / Prompt Input | Text input box with send button and keyboard shortcut (Enter) | M4 | ORIGINAL_REQUEST R4 |
| 20 | Central Finite State Controller | FSM governing state transitions across UI, 3D Canvas, Voice, and Audio | M4 | ORIGINAL_REQUEST Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Voice & Speech Engine | `voice_engine.js` (STT continuous, RO/EN detector, wake-word, TTS queue, mute) | none | PLANNED |
| M2 | 3D WebGL Hologram & Tactical Sound | `hologram.js` (Three.js Arc-Reactor, 6 states, 2D fallback), `sound_engine.js` (Web Audio procedural SFX) | none | PLANNED |
| M3 | AI Memory Vault REST Client & Cache | `vault_client.js` (HTTP search, citations, offline cache, propose endpoint) | none | PLANNED |
| M4 | Standalone Dashboard HUD & Dispatcher | `index.html`, `style.css`, `state_machine.js`, `app.js` (wiring UI, logs, telemetry, controls) | M1, M2, M3 | PLANNED |
| M5 | Automated E2E Verification & Hardening | `test/test_jarvis.js` (Tiers 1-4 comprehensive suite + Tier 5 adversarial stress tests) | M1, M2, M3, M4 | PLANNED |

## Interface Contracts

### `StateMachine` ↔ All Components
- `setState(newState: 'IDLE' | 'LISTENING' | 'THINKING' | 'SPEAKING' | 'MUTED' | 'ERROR')`
- `getState(): string`
- `subscribe(listener: (state, prevState, payload) => void): unsubscribeFunction`

### `VoiceEngine` ↔ `App` / `StateMachine`
- `startListening(): void`
- `stopListening(): void`
- `toggleMute(): boolean`
- `speak(text: string, lang?: string, onEnd?: () => void): void`
- `stopSpeaking(): void`
- `onWakeWordDetected: (payload: { rawText: string, commandText: string, lang: string }) => void`
- `onTranscript: (payload: { text: string, isFinal: boolean, lang: string }) => void`
- `getAudioLevel(): number` (0.0 - 1.0)

### `HologramController` ↔ `App`
- `init(containerElement: HTMLElement): void`
- `setVisualState(state: string): void`
- `setAudioReactivity(level: number, frequencyData?: Uint8Array): void`
- `destroy(): void`

### `TacticalAudio` ↔ `App`
- `unlockAudioContext(): Promise<void>`
- `playWakeChime(): void`
- `playListeningBeep(): void`
- `startThinkingDrone(): void`
- `stopThinkingDrone(): void`
- `playSuccessChime(): void`
- `playErrorAlert(): void`
- `playStandbyChirp(): void`

### `VaultClient` ↔ `App`
- `search(query: string): Promise<{ source: 'live' | 'offline_cache', results: Array<Note>, latencyMs: number }>`
- `getStatus(): Promise<{ online: boolean, indexedNotes: number }>`
- `proposeNote(notePayload: object): Promise<{ success: boolean, noteId?: string, error?: string }>`

## Code Layout
```
projects/jarvis_web/
├── index.html                  # Cyberpunk Standalone HUD Dashboard
├── style.css                   # Glassmorphism, animations, UI tokens
├── js/
│   ├── app.js                  # Main entry point & component orchestrator
│   ├── state_machine.js        # Central Finite State Controller
│   ├── voice_engine.js         # Web Speech STT/TTS, wake-word, RO/EN detector
│   ├── hologram.js             # Three.js 3D Arc-Reactor & 2D fallback
│   ├── sound_engine.js         # Procedural Web Audio API SFX synthesizer
│   └── vault_client.js         # AI Memory Vault REST Client & Offline cache
├── test/
│   ├── test_jarvis.js          # Automated Unit & Integration test runner (Node.js)
│   └── mocks/
│       ├── mock_web_speech.js  # Web Speech API test doubles
│       ├── mock_web_audio.js   # Web Audio API test doubles
│       └── mock_webgl.js       # WebGL / Canvas test doubles
└── assets/
    └── favicon.ico             # App icon
```
