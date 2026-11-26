---
id: manual-visual-tokens
title: Visual tokens (radius, depth, surfaces, type, glow, noise, glass) - skill default
tier_min: T1
page_types: ["any"]
topics: ["shape-radius", "nested-radius", "depth-shadow", "dark-mode", "hairline", "display-type", "fluid-type", "glow", "noise", "glass", "not-boxy"]
provenance: Reconciled 2026-11-23 by the fable-consultant advisor from codex + gemini-argon web research
  (Material 3, Fluent 2, Carbon, Apple HIG, Polaris, Geist/Linear, practitioners). Values are skill HEURISTIC
  DEFAULTS, not sourced facts. WCAG 2.x contrast is the only hard gate (APCA advisory).
---

# Visual tokens

| Token | Default |
|---|---|
| Radius scale | 4 / 8 / 12 / 16 / 24 / 32 / full. Dense (T3 apps): controls 6, containers 8-12. Standard (T1/T2): controls 8-10, cards 12-16, overlays 16. Expressive / "not boxy" (T4 or when the owner asks for modern-rounded): controls pill or 12-16, cards 20-28, hero containers 32-48. |
| Nested radius | inner = outer - padding, floor 4px, never inner > outer; if padding >= outer, use the next scale step down. |
| Shadow, light theme | Rest: `0 0 0 1px rgb(0 0 0/.06), 0 1px 2px rgb(0 0 0/.06)`. Raised: add `0 8px 16px -4px rgb(0 0 0/.08)`. Overlay: `0 0 0 1px rgb(0 0 0/.08), 0 8px 16px rgb(0 0 0/.10), 0 24px 48px -12px rgb(0 0 0/.18)`. Tint shadows with the surface hue; one light direction. |
| Elevation, dark theme | Rest/raised: lighter surface step + hairline + `inset 0 1px 0 rgb(255 255 255/.06)`, no drop shadow. Overlay only: `0 0 0 1px rgb(255 255 255/.10), 0 16px 48px -12px rgb(0 0 0/.6)`. |
| Hairline | Light `rgb(0 0 0/.08)` (strong .14). Dark `rgb(255 255 255/.08)` (subtle .06, hover .16, selected .28). Inputs also need a 3:1 focus/active cue. |
| Dark surfaces | OKLCH, chroma .005-.015 at brand hue: sunken = canvas -.03 L; canvas L .15-.17; raised +.04; overlay +.08; hover +.02. Text L .95 / .75 / .55 (.55 only for disabled or large text). Off-black, never #000 (except art direction / sunken wells); off-white text, never #FFF on #000. |
| Display tracking | <=16px 0; 18-22px -.01em; 24-40px -.02em; 48-72px -.03em; cap -.04em; halve for optical display cuts (SF Pro Display, Segoe UI Variable Display, Inter opsz). Line height: display 1.05-1.15, headings 1.2-1.3, body 1.5-1.6. Caps labels 11-12px, +.06em, weight 500-600. |
| Fluid type | display `clamp(2.25rem, 1.5rem + 3vw, 4.5rem)` (T4 up to 5.5rem); h1 `clamp(1.875rem, 1.35rem + 2vw, 3.25rem)`; h2 `clamp(1.5rem, 1.2rem + 1.2vw, 2.25rem)`; h3 `clamp(1.25rem, 1.1rem + .6vw, 1.625rem)`; body 1rem (.875rem dense); floor .75rem. Numbers that change: `font-variant-numeric: tabular-nums`. Headings `text-wrap: balance`, paragraphs `text-wrap: pretty` (enhancement). |
| Glow | Max 1 ambient glow per viewport, peak alpha <= .12, radius >= 400px, never behind body text or on cards, static under reduced motion. |
| Noise / grain | Tiled 128-200px SVG on `::after`, opacity .03-.05 (or <= .20 with overlay blend), hero or dark banners only (T4). |
| Glass | Nav and overlays only, <= 2 per viewport, blur 12-20px. Tint >= .85 if opposite-theme sections scroll under it, else >= .70; verify composited text contrast against the worst backdrop. Solid fallback via `@supports not (backdrop-filter: blur(1px))` and `@media (prefers-reduced-transparency: reduce)`. |
| Sticky header | `html { scroll-padding-top: <header height + 16-24px> }` so anchor jumps are not hidden. |
| Targets | 24px legal floor (WCAG 2.5.8); 44px default for anything touch-reachable on the web; 48dp Android native. |

## "Not boxy / modern" recipe (T2 when the owner asks for it, otherwise T4)
Pill buttons and toggles; cards 20-28px with correct nested radii; one asymmetric bento grid (keep DOM order on
collapse); one organic divider (SVG wave/curve) between contrasting sections; overlapping element (chip or card
breaking a container edge); one ambient glow; large fluid display type with tracking from the table; hairline-first
depth in dark themes. Avoid the AI-template set: purple-to-blue gradient everywhere, glass on every card, emoji
icons, identical cards with identical soft shadow, gradient text on more than one short headline.

## Checklist
- [ ] Radii from the scale; nested radius rule holds everywhere.
- [ ] Dark theme: off-black canvas, surface steps + hairlines, drop shadows only on overlays.
- [ ] WCAG 2.x contrast (4.5:1 body, 3:1 large/UI) incl. text over glass and glow.
- [ ] Glass has a solid fallback; glow <= 1 per viewport, alpha <= .12.
- [ ] scroll-padding-top for sticky headers; targets >= 44px for touch.
- [ ] Display tracking capped at -.04em; tabular numbers where values change.

## Landing-page tiers and surfaces (iter2 advisor lessons; HEURISTIC)
- Button tiers: Tier 1 = solid fill, Tier 2 = translucent surface fill + hairline, Tier 3 = ghost. Define the fill per
  surface (page, card, mock-up). At most one Tier 1 fill per viewport (AR14); mock-up buttons never use Tier 1.
- Hover depth = ring plus 2px lift at 150ms.
- Text floor 13px (mono 12.5px); never below 12px anywhere.
- Product panels carry a status-bar caption (mono, >= 12px).
- Signature devices (crosshairs, figure indices, daggers) must map to the product's own mechanic (AR20, AR1).
- Research hygiene: a `source_url` must contain the claim; brand homepages are not sources; research seeded with our
  own brief is not independent evidence; every contrast figure in a rule is "advisor arithmetic" until measured.
- Browser status (belief, verify): subgrid Chrome 117 / Firefox 71 / Safari 16; `text-wrap: balance` Chrome 114.

## Iteration 3 lessons
- AR27: tier and utility class names never double as content indices; grep every short class for a second meaning.
- AR32: a stat is numeral + what it counts + SAMPLE / TARGET / ILLUSTRATIVE tag; never a bare "5 h".
- AR35: static cards do not lift (ring change only); the 2px lift is for link cards.
- Connector = 1px `--hair-3`, radius 4, built from CSS boxes (no SVG).
- Paper artifacts (tape, sheet, mail) may sit inside dark sections.

## Iteration 4 lessons (CANDIDATE; UNPROVEN until v9 is scored)
- AR39 quote marker: one accent band over the lower .38em, alpha .32 dark / .6 paper; no fill box, no left bar.
- AR40 bars to scale: shared start x, one line each, end-capped, both >= 3:1, inside the viewport.
- AR41 depth budget: grain as a background tile, rim and shadow on objects, <= 1 light pool per viewport and 2 per page; no glass beyond the nav, no 3D, no animated shadow.
- Connector ends: a T stub versus a radius-4 elbow; a blank rotated paper under-sheet is allowed (text-bearing sheet stays unrotated).

## Iteration 5 lessons (CANDIDATE; UNPROVEN until v10 is scored)
- Fold = cut band + flap + twist path (one 2D flap mirrored across a 45 degree crease); flap shadow as a static gradient; `--paper-back` for the flap's reverse side.
- AR41 amended: one 2D flip per page is allowed; still no perspective, preserve-3d or animated shadow (supersedes "no 3D" above).
- Custody ticks reuse the tick motif.
