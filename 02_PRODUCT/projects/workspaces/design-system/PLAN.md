# Design system (A2) — PLAN

Source: `docs/research/UI_UX_MOTION_APLICATII_COMPLEXE_2026.md` §A2. Owner chose A2 on 2026-10-22.
Checkpoint: `00_GOVERNANCE/coordination/tasks/todo-claude-design-system-a2.md`.

## Decisions (and why)
- **Workspace here**, not inside Casa3D: the tokens serve every project; Casa3D is consumer #1.
- **Production consumer from day 1** (CLAUDE.md rule): phase 1 replaces Casa3D's hand-written `:root` token
  block in `app/app/globals.css` with a generated file. No layer without a consumer.
- **Zero visual change in phase 1.** Values are migrated exactly (hex output); the brand is not redesigned here.
  Found while verifying: the `:root` values (cream `#F6F3EE` + brown `#7A5A36`) are only the base used by the
  secondary pages (login, share, print…). The live product look is "Visual System 02" (aubergine ink, mineral
  paper, oxidised vermilion `#F06A53`), redefined on `.home` (globals.css ~L945) and as a dark editor chrome on
  `.ws` (~L1048). Phase 2 models these as brand scopes/themes in DTCG.
- **DTCG 2025.10 format**: colours as `{colorSpace:"oklch", components:[L,C,H], hex}`, dimensions
  `{value, unit}`, durations `{value, unit:"ms"}`, `cubicBezier` arrays, `shadow` composites.
  OKLCH is the design source; `hex` is the exact sRGB output. A test keeps them consistent.
- **Style Dictionary pinned to 5.6.0** (exact, with lockfile, `npm ci --ignore-scripts`): 5.7.0 was 6 days old.
- **Old CSS names kept as aliases** via `$extensions["vault.cssAlias"]` (e.g. `--bg: var(--color-bg-canvas)`),
  so the 1 246-line stylesheet keeps working untouched.
- Out of scope for phase 1: TS/Motion constants and WPF output (no consumer yet), registry, Storybook.

## Layers
- `tokens/core/*.json` — shared: space (4 px grid), motion durations + easings (Carbon productive/expressive),
  control sizes.
- `tokens/brands/<brand>/*.json` — brand primitives + semantic tokens (`color.bg.surface`, `color.fg.muted`, …).
- Build per brand → `dist/<brand>/tokens.css`; brand config may name extra copy targets (Casa3D:
  `../casa3d/app/app/tokens.css`).

## Increments (test first, then code)
1. Package skeleton + pinned deps + `npm test` (node:test). Done when: `npm ci --ignore-scripts && npm test` green.
2. Token files for core + casa3d, migrated from `globals.css` `:root`. Test: every old custom property from that
   block maps to exactly one token whose output equals the old value (string-normalised).
3. Build + custom formats/transforms. Test: generated CSS parses, contains every new + alias name, no raw value
   differs from the baseline; oklch↔hex ΔE2000 < 0.5.
4. Casa3D consumes it: `import './tokens.css'` before `globals.css` in `app/app/layout.tsx`; the `:root` value
   block removed from `globals.css` (media query, `color-scheme`, legacy `--paper…` aliases stay).
   Done when: vitest + tsc + `next build` green; in Chromium every old custom property computes the same value
   before/after on `/` and the editor (script), screenshots identical by eye.
5. Drift gate: test fails if `casa3d/app/app/tokens.css` ≠ fresh build. CI workflow `design-system-build.yml`.

## Metrics (from §A2)
- Raw colour literals (hex, `rgb(`/`rgba(`, `oklch(`) in Casa3D `globals.css` outside the token file:
  **477** before and after phase 1 (phase 1 does not touch them); target 0 in later phases.

## Phase 1 evidence (2026-10-22)
- design-system `npm test`: 11/11; each test fails on its own mutation. Casa3D: tsc 0, `next build` 0, Vitest 108/108
  files (8 rerun with one worker after Windows worker crashes, 86/86).
- Chromium: all 50 original custom properties compute to their original values; home page full-page screenshot
  pixel-identical (0 of 3 394 350 px) to the same page with the original `:root` rule put back via CSSOM.

## Phase 2 — scopes (2026-10-22)
- The three Visual System 02 re-themes are DTCG scopes of the casa3d brand: `tokens/brands/casa3d/scopes/{home,ws,ws-side}`
  (19 + 21 + 20 colours, OKLCH computed from the original hex), configured in `brand.json` `scopes` (`.ws .side`
  nested under `.ws`). The legacy names (`--paper`, `--paper-2`, `--graphite`, `--lead`, `--rule`) became aliases;
  `color.paper.*` and `color.border.rule` are references (`{color.bg.canvas}` …) because the editor keeps a light
  plan on a dark chrome and the inspector draws `--rule` darker than its border.
- Build rule: a scope emits what its files declare plus every token whose final value differs from the parent's, and
  repeats the aliases of the tokens it emits (`var()` is substituted where declared, so `--bg` on `:root` would not
  follow a scope's canvas). `globals.css` keeps only the non-token declarations of those rules and `color-scheme`.
- Evidence: design-system `npm test` 22/22; mutation probes (no scope aliases, only declared tokens emitted, scope
  hex changed with and without rebuild, property put back in `globals.css`, token property in another rule, scope
  path not in the brand, dangling reference) each caught; dropping `parent` of `.ws .side` is equivalent with the
  current data (identical output). Casa3D: tsc 0, `next build` 0, Vitest 108/108 files (651 tests).
  Chromium (dev server, CDP, light scheme, reduced motion, animations frozen): 100–150 custom properties on
  `:root`/`.home`/`.ws`/`.ws .side` compute equal to the original rules put back via CSSOM, on home and editor at
  1440×900 and 390×844. Pixels: editor 0 px (desktop and mobile); home ≤ 2/255 on ~55 px at the pill buttons' top
  edge, the same as a null control that re-inserts the generated rules unchanged (raster artefact of the CSSOM
  edit); positive control (one channel +1 on one colour) changes 79 k – 2.2 M px.
- Metric: raw colour literals in `globals.css` 477 → 415 (same regex as phase 1).

## Phase 3 (next session)
- Replace the 415 raw literals with semantic tokens, page by page (home first: `.home …` rules, then the editor
  chrome `.ws …`), each with the same Chromium parity check; new semantic tokens only where a literal has a role
  the current set does not name. TS/Motion output once a component consumes motion tokens.
