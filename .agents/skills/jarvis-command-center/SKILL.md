---
name: jarvis-command-center
description: Design and engineer a production-grade JARVIS AI Command Center over AI Memory Vault. Combines UI Sensei clarity with immersive sci-fi HUD, dense operational information architecture, Agent Council, Memory V6, skill registry, execution telemetry, controlled proposals, and responsive full-screen layouts.
---

# JARVIS Command Center

## Purpose

Build a single-screen operational control plane for the AI Memory Vault. The UI must be functional, not a decorative mockup.

## Visual direction

- Dark Obsidian base with layered elevation; cyan/blue operational accents.
- HUD-inspired geometry: thin technical borders, orbital rings, grids, compact labels, live status lights.
- One strong visual focal point: the JARVIS cognitive core.
- High information density without visual clutter.
- Use 4/8/16/24/32px rhythm.
- Three-level typography hierarchy: primary focal, section guide, data stream.
- Prefer functional decoration: every line, glow, grid, animation or badge must communicate state or hierarchy.
- Use asymmetry deliberately; keep the central core visually dominant.

## One-screen information architecture

Desktop viewport should present these modules together without requiring page scrolling for core operations:

1. Global header/status/navigation.
2. Left operational rail: system state, Memory V6 pipeline, quick actions.
3. Central stage: JARVIS core, live metrics, memory retrieval, Agent Council, skill registry, execution timeline.
4. Right intelligence rail: memory search, recent memory, system metrics, pending proposals.
5. Bottom status bar with Vault version, agent count, skill count and live refresh time.

Secondary views may scroll inside panels or focus the relevant module; never turn the product into a generic long dashboard.

## Functional contract

- Memory search must call the canonical Memory API.
- Proposals must call `/api/v1/propose` and show lifecycle feedback.
- Agent Council must load from registry data, not hardcoded UI cards.
- Agent selection must expose domain and skills and create a visible routing event.
- Routing should call `/api/v1/route` when available and return ranked agents/capabilities.
- Metrics should call `/api/v1/metrics` when available, with graceful fallback.
- Diagnostics must verify Memory API, agent registry and skill registry.
- Execution timeline must record real UI/API actions; do not fabricate completed backend executions.

## Motion rules

- Use short transitions and requestAnimationFrame for pointer-driven effects.
- No perpetual heavy WebGL scene is required; CSS/SVG motion is preferred for the HUD.
- Respect reduced-motion preferences.
- Avoid expensive blur/filter effects on animated elements.

## Accessibility and quality

- WCAG 2.2 AA contrast target.
- Full keyboard navigation and visible focus.
- Semantic landmarks and aria labels.
- No keyboard traps.
- Keep initial UI lightweight and avoid unnecessary dependencies.
- Preserve responsive operation down to tablet/mobile; use internal panel scrolling rather than horizontal overflow.

## Design anti-patterns

- Generic SaaS card grids.
- Fake telemetry presented as real telemetry.
- Decorative neon everywhere.
- Giant hero sections that push operational modules below the fold.
- Unbounded blur/glow effects.
- Hardcoded agent/skill counts when registry/API data is available.

## Agent routing

Primary agents for this skill:

- `ui_sensei_architect`
- `web_design_engineer_agent`
- `web_creative_developer`
- `web_quality_engineer`
- `frontend_saas_engineer`
- `agentic_workflow_orchestrator`

The design must stay subordinate to the canonical Memory Vault and must not create a second memory system.
