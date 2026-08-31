## 2026-08-27T19:42:50Z

<USER_REQUEST>
You are Explorer 3 for Milestone 2 of the Jarvis Cognitive Brain project ("Creier Vorbitor").
Your working directory for metadata and reports is:
`C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\explorer_m2_3`

Scope & Context:
- Read the authoritative user request at `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md`.
- Read the master project plan at `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`.
- Target project codebase: `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`.

Task:
1. Deep-dive into the TTS, Streaming Audio Chunker, and Barge-In/AEC Interruption requirements:
   - Local text-to-speech synthesis using `Kokoro-82M` ONNX model (24kHz synthesis, lightweight acoustic & voice embedding).
   - Streaming sentence & clause chunker (`jarvis/audio/chunker.py`) yielding immediate synth blocks to achieve <300ms Time-To-First-Byte (TTFB).
   - Sub-50ms Barge-in interruption controller (`jarvis/audio/bargein.py` and `jarvis/audio/pipeline.py`): immediately halting audio output DAC, clearing buffers, canceling in-flight LLM token streaming tasks, and transitioning state machine from Speaking to Listening.
2. Outline exact classes and contracts for `jarvis/audio/tts.py`, `jarvis/audio/chunker.py`, `jarvis/audio/bargein.py`, and `jarvis/audio/pipeline.py`.
3. Document comprehensive unit test strategy for TTS streaming, chunking, and microsecond-level barge-in cancellation in `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\explorer_m2_3\handoff.md`.
4. Send a summary message back to parent when complete.
</USER_REQUEST>
