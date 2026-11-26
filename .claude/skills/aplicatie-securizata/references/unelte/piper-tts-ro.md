---
name: piper-tts-ro
version: piper-tts 1.8.0 + voice ro_RO-mihai-medium (rhasspy/piper-voices @ e534943)
sha256: voice onnx ac9e1be65c059279c53500f11e8c276657c7e48a2d33b733add35dbe75748e29 (venv python.exe 5ee333922d9cb0b11b0309f1ab6551a1d96998af8a65921e96419a754e620b30)
path: E:\ai-video-studio\engines\venv-tts\Scripts\python.exe
install_method: uv (PyPI, pins in phase0\venv_tts_freeze.txt)
licence: voice CC0 data / repo card MIT; RUNTIME GPL-3.0-or-later (piper1-gpl + espeak-ng) -> conditional class A
last_verified_at: 2026-11-24
---
# Piper TTS, Romanian (CPU, onnxruntime)

- Use: `venv-tts\Scripts\python.exe -I -X utf8 -m piper -m <voice.onnx> -f out.wav` with the text on stdin. **-X utf8 is required** under -I,
  otherwise diacritics arrive as cp1252 mojibake and are spelled out ("a grav", "tilde"). Script phase0\s4_tts.py.
- Verified: 3 Romanian sentences with diacritics, Whisper round-trip WER 0 % on all three; 22.05 kHz, single voice (male), medium quality.
- Commercial caution: the voice is class A, the runtime is GPL. Call it only as a separate process; no linking or bundling without review.
- Uninstall: `cmd /c rmdir /s /q E:\ai-video-studio\engines\venv-tts` and delete E:\ai-video-studio\models\tts\piper-ro_RO-mihai-medium.
