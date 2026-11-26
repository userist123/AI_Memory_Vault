# Kit design system

Every app the studio builds starts from this design system. The rules below are also given to the builder agent
(the runner system prompt reads the same list from `core/studio_core/app_templates.py`, `DESIGN_RULES`; a test keeps
both copies identical). They are style rules only: they never change what the agent may do.

## The 7 rules

1. Typography: at most 2 font families (var(--font-body) and var(--font-display)) and only the type scale --text-xs to --text-2xl; no ad-hoc font sizes.
2. Spacing: a 4/8 px grid only (var(--space-1) to var(--space-8)); no arbitrary margins or paddings.
3. Colour: only the semantic tokens var(--color-*) from src/ui/globals.css, never raw hex values in components or screens; text keeps WCAG AA contrast (4.5:1, 3:1 for large text and UI parts).
4. Dark mode: tonal, not inverted; it comes from the same tokens (data-theme="dark" on html), so never hard-code light-only colours.
5. Touch: every interactive element is at least 44x44 px (var(--touch)) and keeps a visible :focus-visible ring.
6. Safe areas: the studio kit page already sets viewport-fit=cover; pad fixed headers, tab bars, sheets and full-screen pages with var(--safe-top), var(--safe-bottom), var(--safe-left) and var(--safe-right).
7. States: every screen that loads or lists data has a loading state (Skeleton), an empty state (EmptyState) and an error state (EmptyState tone="error" with a retry action); actions give feedback within 200 ms (pressed and hover micro-interactions, Toast), and motion respects prefers-reduced-motion. Wire the states to the studio preview switch: import { useAasState } from "./aas-states" and let its value (loading, empty, error or populated; null in normal use) override the screen's real state.

## Tokens (`src/ui/globals.css`)

- Shared on `:root`: spacing `--space-1..8` (4, 8, 12, 16, 24, 32, 48, 64 px), type scale `--text-xs..2xl`
  (12-32 px), `--touch` (44 px), motion `--dur-fast`, `--dur`, `--ease`, safe areas `--safe-*`.
- Per preset and theme: `--font-body`, `--font-display`, `--radius-*`, `--shadow-1/2` and the colours
  `--color-bg`, `--color-surface`, `--color-surface-2`, `--color-fg`, `--color-muted`, `--color-border`,
  `--color-primary` / `--color-on-primary`, `--color-primary-soft` / `--color-on-primary-soft` (tonal container),
  `--color-danger` / `--color-on-danger`, `--color-success`, `--color-focus`, `--color-scrim`.
- Dark mode is tonal: the canvas is a dark tinted grey (never #000), elevated surfaces get lighter, text is
  off-white. Every text/background pair of every preset is contrast-checked by `tests/unit/test_w1_design_system.py`.
- Token utilities (`src/ui/utilities.css`, generated from the tokens above): `text-<colour>`, `bg-<colour>`,
  `border-<colour>` for each colour token (e.g. `bg-surface-2`, `text-on-primary`) and
  `p|px|py|pt|pb|pl|pr|m|mx|my|mt|mb|gap-<1..8>` for the spacing scale. Prefer them in `className` for colour and
  spacing: the studio's click-to-edit inspector can swap exactly these classes.

## Style presets

Chosen in the studio when a project is created (template id `<template>:<preset>`, e.g. `tabs:warm`) and written to
`src/ui/preset.ts`; all presets stay in `globals.css` so the studio theme switcher can preview any of them
(`?aas-preset=<id>&aas-theme=light|dark`, read by `src/ui/theme.ts` from a whitelist).

| id | name | character |
|----|------|-----------|
| `clean` | Clean | iOS-like neutrals, blue accent, 12 px radius (default) |
| `material` | Material tonal | Material 3 tonal purple, large radii |
| `dark-pro` | Dark pro | developer-tool dark first, teal accent |
| `warm` | Warm editorial | cream paper, serif headings, terracotta |
| `playful` | Playful | rounded, vivid magenta, friendly |
| `minimal` | Minimal mono | black and white, monospace headings, sharp corners |

## Component catalogue (`src/ui`, import from `./ui`)

Hand-written, no dependencies beyond React, keyboard and screen-reader tested:
`Button` (primary, secondary, ghost, danger; loading), `Input` (label, hint, error), `Card`, `ListItem` (with `List`),
`TabBar` (WAI-ARIA tabs, bottom bar), `Sheet` (bottom sheet), `Dialog`, `Toast` (live region), `Skeleton`,
`EmptyState` (empty and error). `Dialog` and `Sheet` trap focus, close on Escape and return focus to the opener.

Build screens from these components. When something is missing, add a new component in `src/ui` that follows the same
rules (tokens only, 44 px targets, visible focus) instead of styling one-off elements.
