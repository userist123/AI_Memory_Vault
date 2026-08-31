## 2026-08-25T19:33:03Z

<USER_REQUEST>
You are the E2E Test Writer for the JARVIS Web Ecosystem.
Working Directory: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\test_writer_track
Original Request Path: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md
Project Master Plan: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\orchestrator\PROJECT.md
Test Infra Plan: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\orchestrator\TEST_INFRA.md
Target Directory: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web\test

Tasks:
1. Read ORIGINAL_REQUEST.md, PROJECT.md, and TEST_INFRA.md.
2. Implement high-fidelity standalone browser mocks in `projects/jarvis_web/test/mocks/`:
   - `mock_web_speech.js`: Mock `SpeechRecognition` (continuous listening, onresult, onerror, onend, event emitters) and Mock `SpeechSynthesis` / `SpeechSynthesisUtterance` / `getVoices()`.
   - `mock_web_audio.js`: Mock `AudioContext`, `OscillatorNode`, `GainNode`, `BiquadFilterNode`, `AnalyserNode`, state management ('suspended'/'running'), and `getByteFrequencyData()`.
   - `mock_webgl.js`: Mock `HTMLCanvasElement`, WebGL context detection (`getContext('webgl')`), and WebGL loss/restored events.
   - `mock_fetch.js`: Deterministic HTTP mock for `http://127.0.0.1:8000/api/v1/*` responses and network error simulations.
3. Implement the master test suite in `projects/jarvis_web/test/test_jarvis.js` using Node.js built-in `node:test` and `node:assert/strict`:
   - **Tier 1 (Feature Coverage ≥5 per feature)**: Individual happy-path unit tests for all 20 features (F1 to F20) per TEST_INFRA.md.
   - **Tier 2 (Boundary & Corner Cases ≥5 per feature)**: Extreme values, silence timeouts, missing DOM elements, empty inputs, network dropouts, unhandled voice errors.
   - **Tier 3 (Cross-Feature Combinations)**: Multi-module interactions (STT triggering FSM -> FSM triggering Audio Chime + 3D State -> REST Search -> Speech TTS).
   - **Tier 4 (Real-World Application Scenarios)**: 5 end-to-end user journeys per TEST_INFRA.md.
4. Execute the test suite using `node --test projects/jarvis_web/test/test_jarvis.js` via your test execution capabilities and ensure tests execute properly against the modules as they are created.
5. Create `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\orchestrator\TEST_READY.md` summarizing the test suite runner and feature coverage checklist.
6. Write your final report to `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\test_writer_track\handoff.md` and send a completion message to the orchestrator.
</USER_REQUEST>
