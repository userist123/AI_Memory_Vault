---
name: faster-whisper-venv-audio
version: faster-whisper 1.2.1 / ctranslate2 4.9.1 / model Systran/faster-whisper-large-v3 @ edaa852
sha256: model.bin 69f74147e3334731bc3a76048724833325d2ec74642fb52620eda87352e3d4f1 (venv python.exe 5ee333922d9cb0b11b0309f1ab6551a1d96998af8a65921e96419a754e620b30)
path: E:\ai-video-studio\engines\venv-audio\Scripts\python.exe
install_method: uv (PyPI wheels, pinned in E:\claude-work\ai-video-studio\phase0\venv_audio_freeze.txt)
licence: MIT (faster-whisper, CTranslate2, model card mit)
last_verified_at: 2026-11-24
---
# faster-whisper STT (separate venv, E:)

- Env first: `. E:\claude-work\ai-video-studio\phase0\env.ps1`. Run with `python -I`; before importing faster_whisper add
  `os.add_dll_directory` for every `venv-audio\Lib\site-packages\nvidia\*\bin` (cublas 12 + cudnn 9 wheels, no CUDA toolkit needed).
- Model dir: E:\ai-video-studio\models\stt\faster-whisper-large-v3 (CTranslate2 binary model.bin, not a pickle; decision D6).
- Verified: RTX 5060 sm_120, compute_type int8_float16, word_timestamps; FLEURS ro dev 30 utts: WER 9.6 %, 9.7x realtime, ~2.3 GB VRAM
  (docs\MEASUREMENTS.md S4a). Script phase0\s4_stt.py.
- Pitfall: av >= 17 breaks faster-whisper 1.2.1 (pin av<17). Under `python -I` PYTHONUTF8 is ignored: open files with encoding="utf8".
- Uninstall: `cmd /c rmdir /s /q E:\ai-video-studio\engines\venv-audio`; models are under E:\ai-video-studio\models\stt.
