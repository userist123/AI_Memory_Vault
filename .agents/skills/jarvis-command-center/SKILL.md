---
name: jarvis-command-center
description: Design and engineer a production-grade JARVIS AI Command Center over AI Memory Vault, combining an immersive cinematic cockpit with real Memory V6, Agent Council, operational skills, local LLM chat, bidirectional Romanian voice, procedural WebGL avatar, routing and controlled memory workflows.
---

# JARVIS Command Center

## Core identity

This is an operational AI cockpit, not a landing page. The visual hierarchy should feel like a private advanced-lab command surface: one dominant holographic AI presence, restrained cyan/blue energy, technical overlays and information arranged around the avatar.

Build an original futuristic assistant experience with:
- holographic humanoid avatar / AI presence;
- reactive status state (STANDBY, LISTENING, THINKING, SPEAKING, ERROR);
- conversational command interface;
- memory-aware responses;
- Agent Council routing;
- live operational skill registry;
- Memory V6 proposal and review lifecycle;
- bidirectional local Romanian voice interaction.

## Visual system

- Dark Obsidian / near-black base with layered depth.
- Cyan primary signal, blue secondary signal, green healthy state, amber review, red fault.
- Thin HUD borders, restrained bloom, scanlines, radial grids, orbital paths and technical micro-labels.
- One visual focal point: the JARVIS holographic avatar.
- Information density should be high but never become a generic card dashboard.
- Use 4/8/16/24/32px rhythm and a three-level typography hierarchy.
- Prefer meaningful motion: avatar breathing, orbit rotation, signal pulses, listening waveform, thinking activity and speaking state.

## 3D avatar contract

The center stage MUST support a real 3D surface:
- WebGL canvas preferred;
- procedural or imported 3D model allowed;
- holographic point/line/mesh rendering;
- rotating orbital geometry around the avatar;
- central reactor / cognitive core signal;
- responsive resize and device-pixel-ratio handling;
- reduced-motion fallback;
- graceful fallback when WebGL is unavailable.

The avatar is part of the application state, not decoration. State mapping:
- STANDBY = low-energy idle;
- LISTENING = microphone pulse / waveform;
- THINKING = faster orbital activity;
- SPEAKING = stronger pulse / mouth or signal animation;
- ERROR = red fault pulse.

## Conversational AI contract

JARVIS Chat must:
- use the configured local model through Ollama when available;
- expose available local models to the UI;
- allow model selection or AUTO mode;
- keep bounded conversation history;
- retrieve relevant Vault memory before generating the response;
- identify the best Agent Council specialist for the task;
- explicitly distinguish canonical memory from inference;
- never claim a backend action happened unless an API confirms it;
- gracefully report when Ollama is offline.

Romanian conversational style is canonical:
- When the user speaks Romanian, respond in native Romanian with correct grammar and diacritics.
- Sound like a competent human assistant, not a status terminal or screen reader.
- Avoid canned robotic phrases such as "Afirmativ", "Procesare solicitare", "Comanda primita" or similar wording.
- Preserve technical names, code, URLs and file paths when necessary.

Recommended environment variables:
- `OLLAMA_HOST` — default `http://127.0.0.1:11434`;
- `JARVIS_MODEL` — preferred local model name;
- `AI_MEMORY_VAULT_ROOT` — optional Vault root override.

## Bidirectional voice contract

JARVIS voice is a local neural voice layer, not browser text-to-speech.

Input:
- browser microphone speech recognition with `ro-RO` as the default language;
- one-click LISTENING state;
- recognized speech is sent to the same `/api/v1/chat` path as typed messages;
- graceful unsupported-browser fallback.

Output:
- use a local neural TTS service, preferably Piper;
- default Romanian voice: `ro_RO-mihai-medium`;
- TTS runs locally on `127.0.0.1:8002`;
- JARVIS returns generated WAV audio to the browser for playback;
- do not use browser `speechSynthesis` as the primary voice path;
- strip markdown, code blocks, URLs and UI telemetry before synthesis;
- expose SPEAKING state while audio is playing;
- stop the current utterance when a new response begins.

Voice setup is provided by `setup_jarvis_voice.ps1`; dependency definition lives in `requirements-voice.txt` and the server in `voice_server.py`.

## One-screen information architecture

Desktop should feel like a single cockpit:
1. global header and live system status;
2. left operational rail;
3. center holographic avatar + live metrics + conversation surface;
4. memory search and Agent Router around/below the avatar in the same control plane;
5. right intelligence rail with memory, queue and health signals;
6. persistent bottom status strip.

Secondary modules can use internal scrolling, modal surfaces or focus transitions. Avoid turning the product into a generic long-form dashboard.

## Functional contract

- Memory search calls canonical Memory API.
- Chat calls canonical JARVIS `/api/v1/chat`.
- Model discovery calls `/api/v1/models`.
- Agent routing calls `/api/v1/route`.
- Agent Council loads from registry data.
- Skill Registry loads recursively from `.agents/skills/**/SKILL.md` through API.
- Proposals enter Memory V6 `PENDING_REVIEW` state; they do not bypass review.
- Proposal approval/rejection is visible in the UI and recorded by the queue.
- Promotion must go through the canonical promoter/controller path.
- Metrics come from `/api/v1/metrics`.
- Diagnostics test actual API availability.
- Execution timeline records actual UI/API events and never fabricates successful backend work.
- Voice health can be verified through the JARVIS TTS `/health` endpoint.

## Motion and quality

- Use requestAnimationFrame for WebGL and pointer effects.
- Avoid expensive continuous blur/filter animations.
- Support `prefers-reduced-motion`.
- Keep WebGL scene lightweight enough for normal laptops.
- Preserve keyboard navigation and visible focus.
- Target WCAG 2.2 AA contrast.
- Avoid large external dependencies unless justified.

## Anti-patterns

- Generic SaaS card-grid appearance.
- Static fake numbers when APIs provide real state.
- A single flat landscape illustration replacing actual interaction.
- Browser `speechSynthesis` pretending to be a neural assistant voice.
- A giant hero area that hides operational functions.
- Neon decoration with no semantic purpose.
- Separate, competing memory systems.

## Primary routing agents

- `ui_sensei_architect`
- `web_design_engineer_agent`
- `web_creative_developer`
- `web_quality_engineer`
- `frontend_saas_engineer`
- `local_ai_engineer`
- `agentic_workflow_orchestrator`
- `memory_controller_architect`

The JARVIS layer remains a control plane over the canonical Memory Vault; it must never fork memory authority or invent a parallel source of truth.
