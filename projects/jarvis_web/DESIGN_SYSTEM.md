# JARVIS Command Center Design System

## Intent

JARVIS is a single-screen cognitive command center for AI Memory Vault V6. The UI is a control plane over MemoryController, Cognitive Core, Agent Council and operational skills; it is not a second memory system.

## Applied Vault Skills

- `ui-sensei`: lasting clarity, de-cluttering, 4/8/16 spacing rhythm, three-level typography, dark elevation layers and explicit interaction states.
- `web-quality-skills`: performance-first CSS, restrained motion, accessible controls, predictable layout and no unnecessary heavy effects.
- `web-design-engineer`: structured information density, strong grid, deliberate visual variance and system-level consistency.
- `web-quality` / best-practices: local-first assets, zero external runtime dependencies, resilient responsive behavior.

## Layout Contract

```text
┌─────────────────────────────────────────────────────────────┐
│ Brand │ Navigation │ Status / Clock                        │
├──────────────┬──────────────────────────┬───────────────────┤
│ System rail  │ Cognitive core           │ Memory / metrics  │
│ V6 pipeline  │ Memory + agents          │ Recent / queue    │
│ Quick action │ Skills + execution       │ System telemetry  │
├──────────────┴──────────────────────────┴───────────────────┤
│ Footer / canonical identity                                  │
└─────────────────────────────────────────────────────────────┘
```

The desktop shell is viewport-bound. Rails and the center stage scroll internally when content exceeds the viewport; the application does not create unnecessary page-level scrolling.

## Visual Language

- Deep Obsidian background layers.
- Cyan is the primary system/link state.
- Green is successful/healthy/verified.
- Amber is review/pending attention.
- Red is unavailable/error.
- Borders are thin and functional; no decorative frame around every element.
- Holographic effects are reserved for the cognitive core and state feedback.
- Cards expose information hierarchy rather than acting as decoration.

## Interaction Rules

- Focus states remain visible.
- Buttons provide immediate state feedback.
- Search is selective and operates against the canonical Memory API.
- Agent selection updates the active routing context.
- Diagnostics refresh system state without page navigation.
- Navigation focuses the relevant in-cockpit section rather than opening a second application.

## Performance Rules

- No external font or UI framework dependency.
- Static Node server only.
- CSS-first visual system.
- No continuous canvas/WebGL dependency for the core dashboard.
- Animations are limited to low-cost transforms/opacity.
- Respect reduced-motion preferences when extending motion.

## Accessibility Rules

- Semantic buttons and navigation.
- Text labels remain available for icon affordances.
- Keyboard focus must remain visible.
- Contrast must remain readable against the dark background.
- Responsive layout must preserve access to Memory, Agents, Skills and Execution sections.
