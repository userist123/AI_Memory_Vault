---
name: zonos3-mini-ro
version: ZONOS3-Mini repo a50e14117d99181ba7f28047b451ba03ba1a09a9 / weights Zyphra/ZONOS3-Mini @ 2921b882 (gguf)
sha256: zonos3-server.exe 0aaf92eadc645975707db233505f920a1d41a8f3a4dcfc7a50e3cfe4355dab47; backbone-ptq.gguf 66fceb6d73a8c30bde80249e4112bb308380ab7f3384fa11c8e4048115e4ceec
path: E:\ai-video-studio\engines\ZONOS3-Mini\servers\ggml\build\zonos3-server.exe
install_method: source build (MSVC 14.44 + cmake/ninja from venv-tts + cargo), weights via hfget
licence: code MIT, weights Apache-2.0 (class A)
last_verified_at: 2026-11-24
---
# ZONOS3-Mini Romanian TTS (CPU ggml server)

- Start: phase0\start_zonos3.ps1 (127.0.0.1:19190, --cpu; add build\bin to PATH or it fails with "ggml-base.dll not found"). Stop by PID.
- Request: POST /v2/generate {"text","language":"ro","stream":false,"format":"wav","speaker_audio_base64": <clean reference wav>,"speaker_mode":"embedding"}.
  A speaker is mandatory (unconditioned output is noise); "reference" mode needs zac-encoder.gguf + ssl-teacher.gguf (not downloaded).
- Verified: 3 Romanian sentences with diacritics, Whisper round-trip WER 0 / 7.7 / 0 %, RTF ~0.45 on the CPU. Script phase0\s4_zonos.py.
- Rebuild: phase0\build_zonos3.cmd (needs VS 2022 BuildTools, cargo, cmake+ninja on PATH, CARGO_HOME on E:).
- Uninstall: delete the engine dir, E:\ai-video-studio\cache\cargo*, E:\ai-video-studio\models\tts\zonos3-mini.
