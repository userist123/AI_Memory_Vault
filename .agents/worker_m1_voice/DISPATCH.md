## 2026-08-25T19:33:03Z

You are Worker 1 (Voice & Speech Engine Specialist).
Working Directory: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m1_voice
Original Request Path: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md
Project Master Plan: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\orchestrator\PROJECT.md
Survey Spec: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\explorer_survey_3\handoff.md
Target File: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web\js\voice_engine.js

Exclusive Write Ownership: `projects/jarvis_web/js/voice_engine.js`

Tasks:
1. Read ORIGINAL_REQUEST.md, PROJECT.md, and explorer_survey_3/handoff.md.
2. Implement `projects/jarvis_web/js/voice_engine.js` (ES6 Module / Vanilla JS compatible with both browser and test harness) providing:
   - `VoiceEngine` class conforming to the interface contracts in PROJECT.md.
   - Continuous `SpeechRecognition` STT with auto-restart on silence/end debounce (300ms).
   - Wake-word detection engine recognizing "Jarvis", "Hey Jarvis", "Salut Jarvis", "OK Jarvis", and "Hei Jarvis".
   - Token-based bilingual Romanian / English autodetection classifier.
   - Speech synthesis vocal response engine with queue management, chunking long phrases to avoid Chromium 15s freeze, natural voice priority selector (Andrei/Emil for RO, Christopher/Guy for EN), speech rate/pitch modulation, and immediate cancellation on interruption / barge-in.
   - Microphone mute/unmute software toggle.
   - Audio level / visualizer hook emitting simulated or real RMS amplitude (0.0 to 1.0) for 3D Arc-Reactor modulation.
   - Graceful fallback when Web Speech API is absent or permissions are denied (`not-allowed`).
3. Verify syntax and logic. Document all exported classes and methods in `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m1_voice\handoff.md`.
4. Send a completion message to the orchestrator.
