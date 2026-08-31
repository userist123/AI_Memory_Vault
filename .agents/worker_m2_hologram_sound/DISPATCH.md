## 2026-08-25T19:33:03Z

<USER_REQUEST>
You are Worker 2 (3D Hologram & Tactical Sound Specialist).
Working Directory: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m2_hologram_sound
Original Request Path: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md
Project Master Plan: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\orchestrator\PROJECT.md
Survey Spec: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\explorer_survey_2\handoff.md
Target Files:
- c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web\js\hologram.js
- c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web\js\sound_engine.js

Exclusive Write Ownership: `projects/jarvis_web/js/hologram.js` and `projects/jarvis_web/js/sound_engine.js`

Tasks:
1. Read ORIGINAL_REQUEST.md, PROJECT.md, and explorer_survey_2/handoff.md.
2. Implement `projects/jarvis_web/js/hologram.js`:
   - `HologramController` class conforming to PROJECT.md interface.
   - Procedural Three.js 3D WebGL Holographic Arc-Reactor scene: central glowing sphere core, 3 multi-axis gyroscopic gimbal rings (inner, middle, outer), dynamic energy arcs, and particle system (1000+ particles) running at 60 FPS.
   - Dynamic 6-state reactivity (`IDLE`, `LISTENING`, `THINKING`, `SPEAKING`, `MUTED`, `ERROR`) with smooth parametric lerping of rotation speeds, colors (cyan, emerald, amber/magenta, cobalt-white, red), and scaling.
   - Audio reactivity: modulations of core geometry and outer ring expansion based on real-time audio level (0.0 to 1.0) and frequency data.
   - Automatic WebGL context detection and robust 2D Canvas / CSS holographic radar fallback if WebGL is unavailable or context is lost (`WEBGL_lose_context`).
3. Implement `projects/jarvis_web/js/sound_engine.js`:
   - `TacticalAudio` class conforming to PROJECT.md interface.
   - 100% Free, zero-external-audio-file procedural sound synthesizer using native browser Web Audio API (`AudioContext`).
   - Pure mathematical oscillators and gain/filter envelopes for:
     - `playWakeChime()`: Triad harmonic crystal chime.
     - `playListeningBeep()`: Dual ascending tech blip.
     - `startThinkingDrone()` / `stopThinkingDrone()`: Sub-bass AM drone loop.
     - `playSuccessChime()`: Upbeat confirmation chime.
     - `playErrorAlert()`: Dissonant alert buzz.
     - `playStandbyChirp()`: Soft descending standby tone.
   - Autoplay policy unlocking on first user interaction with seamless state management.
4. Document all implementation details in `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m2_hologram_sound\handoff.md`.
5. Send a completion message to the orchestrator.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.
</USER_REQUEST>
