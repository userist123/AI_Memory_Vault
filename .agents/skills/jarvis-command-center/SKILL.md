---
name: jarvis-command-center
description: Design and engineer a production-grade JARVIS AI Command Center over AI Memory Vault, combining an immersive Tony-Stark-inspired cinematic cockpit with real Memory V6, Agent Council, operational skills, local LLM chat, voice interaction, procedural WebGL avatar, routing and controlled memory workflows.
---

# JARVIS Command Center

## Core identity

This is an operational AI cockpit, not a landing page. The visual hierarchy must feel like a private Stark-lab command surface: one dominant holographic AI presence, restrained cyan/blue energy, technical overlays and information arranged around the avatar.

Do not imitate a copied movie screenshot. Build an original futuristic assistant experience with:
- holographic humanoid avatar / AI presence;
- reactive status state (STANDBY, LISTENING, THINKING, RESPONDING, ERROR);
- conversational command interface;
- memory-aware responses;
- Agent Council routing;
- live operational skill registry;
- Memory V6 proposal and review lifecycle.

## Visual system

- Dark Obsidian / near-black base with layered depth.
- Cyan primary signal, blue secondary signal, green healthy state, amber review, red fault.
- Thin HUD borders, restrained bloom, scanlines, radial grids, orbital paths and technical micro-labels.
- One visual focal point: the JARVIS holographic avatar.
- Information density should be high but never become a generic card dashboard.
- Use 4/8/16/24/32px rhythm and a three-level typography hierarchy.
- Prefer meaningful motion: avatar breathing, orbit rotation, signal pulses, listening waveform, response activity.

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
- LISTENING = cyan pulse / microphone state;
- THINKING = faster orbital activity;
- RESPONDING = stronger scan and signal emission;
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

Recommended environment variables:
- `OLLAMA_HOST` — default `http://127.0.0.1:11434`;
- `JARVIS_MODEL` — preferred local model name;
- `AI_MEMORY_VAULT_ROOT` — optional Vault root override.

## Voice interaction

Provide browser-native speech input/output where supported:
- Romanian recognition by default;
- one-click LISTENING state;
- speech synthesis for assistant responses;
- no external speech service dependency;
- graceful unsupported-browser fallback.

## One-screen information architecture

Desktop should feel like a single cockpit:
1. global header and live system status;
2. left operational rail;
3. center holographic avatar + live metrics + conversation surface;
4. memory search and Agent Router below the avatar but within the same control plane;
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
