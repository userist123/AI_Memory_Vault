---
title: Jarvis Cognitive Brain Workspace Specification
type: specification
status: active
category: product
---

# Jarvis Cognitive Brain

Runtime-ul implementează arhitectura din `arhitectura_asistent_ai_jarvis.html` și `proiect.pdf` fără să mute fișierele sursă:

- orchestrare `Observe -> Retrieve -> Reason/Plan -> Act -> Reflect -> Consolidate`;
- supervisor multi-agent cu worker pool prioritar, dispatch background și council review paralel pentru Retrieval/Verifier/Critic;
- gateway FastAPI single-process care servește interfața web, chat-ul cognitiv, memoria, TTS-ul, FastMCP-ul logic și WebSocket-ul pe un singur port;
- Ollama local ca provider implicit, cu provider `mock` pentru testare offline;
- `JARVIS_MEMORY.md` compact și recap zilnic atomic în `.jarvis/`;
- STT local Faster-Whisper `large-v3-turbo` cu cuantizare `int8`, VAD Silero când modelul este configurat și fallback Energy VAD;
- TTS Kokoro ONNX la 24 kHz când modelul/vocile sunt configurate;
- barge-in prin anularea redării și tokenului de anulare;
- Home Assistant REST sync/async și server FastMCP `JarvisControls`;
- HUD FastAPI/WebSocket și fațadele documentate în `app/`.

## Instalare

Din acest director:

```powershell
python -m pip install -e .
python -m pip install -e ".[voice]"
```

Setările sunt citite cu prefixul `JARVIS_`. Pentru un setup local minim, configurează în `.env`:

```dotenv
JARVIS_LLM_PROVIDER=ollama
JARVIS_OLLAMA_URL=http://localhost:11434
JARVIS_OLLAMA_MODEL=qwen2.5-coder:7b
JARVIS_HOME_ASSISTANT_URL=http://localhost:8123
JARVIS_HOME_ASSISTANT_TOKEN=replace-with-a-local-token
JARVIS_TTS_MODEL_PATH=.\models\kokoro-v1.0.onnx
JARVIS_TTS_VOICES_DIR=.\models\voices-v1.0.bin
```

Nu comite tokenul Home Assistant. Modelele Faster-Whisper și Kokoro se încarcă la nevoie; lipsa lor este raportată prin health și folosește fallback-urile locale disponibile.

## Pornire

Pornirea recomandată este python unified_server.py: toate funcțiile web și cognitive sunt expuse pe http://127.0.0.1:3000; JARVIS_BACKEND_AUDIO=1 activează captura audio backend.

Runtime complet cu audio și HUD:

```powershell
python run.py
```

Server MCP separat:

```powershell
python run_mcp.py --transport stdio
```

Command Center-ul web pornește toate serviciile, inclusiv runtime-ul cognitiv, prin `..\jarvis_web\start.bat`.

## Verificare

```powershell
python -m pytest -q
npm --prefix ..\jarvis_web test
```

`JARVIS_SYNC_VAULT=1` poate fi folosit explicit pentru indexarea Markdown-ului canonic; implicit runtime-ul nu scanează întregul vault.
