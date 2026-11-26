---
id: core-visual
title: Visual basics
tier_min: T1
page_types: ["any"]
topics: ["warm-serif-editorial", "depth-shadow", "shape-radius", "glass-blur", "radius-discipline", "signature-motif", "subtle-shadows", "swap-test", "text-animation"]
rules: 18
---

# Visual basics

Shadows, radius, imagery, icons at basic level.
Support = number of independent educators who teach the rule.

## warm-serif-editorial
- Option: Replace generic 2025 SaaS bento-box tropes (purple/blue neon radial blobs, floating 3D glassmorphic spline shapes, and generic Lucide icon badges) with archival print-and-manual signature motifs: visible `1px solid #2C2621` structural ledger grids, corner registration crosshairs (`+` or `7px` L-ticks in `#6E655D`), monospace figure indices (`[FIG. 02 — LATENCY DISTRIBUTION]`), and footnote daggers (`†`, `‡`) linking claims to marginalia. Use only devices that map to the product's own data (AR1); daggers only for real footnotes. _(support 1, basic, R0565)_
  - values: Ledger grid border `1px solid #2C2621`; corner registration tick `7px x 7px`, `1px` stroke `#6E655D`; index label `12px` monospace, `color: #9E958C`, `letter-spacing: 0.06em`, accent prefix dot `6px x 6px` in `#D97706`.
  - don't: Do not place registration crosshairs and `[FIG. XX]` labels on every minor UI sub-card or button; reserve index taxonomy for primary visual exhibits (`1` per major section or bento cell, max `4-6` across the page) so it reads as deliberate editorial structure rather than visual noise.
  - scope: aesthetic:warm-editorial
- Option: Apply tactile paper-and-film surface texture via a section-scoped SVG (one section only) `<feTurbulence type="fractalNoise" baseFrequency="0.8" numOctaves="3" stitchTiles="stitch"/>` overlay at `opacity: 0.025` to `0.04` (`mix-blend-mode: overlay` or `soft-light`, `pointer-events: none`), paired with warm candlelight top borders on cards (`background: linear-gradient(180deg, rgba(245, 158, 11, 0.05) 0%, rgba(26, 23, 20, 0) 100%)` and `box-shadow: inset 0 1px 0 0 rgba(237, 232, 226, 0.09)`). _(support 1, basic, R0566)_
  - values: SVG noise `baseFrequency: 0.75-0.85`, `opacity: 0.025-0.04`; card top inner specular highlight `inset 0 1px 0 0 rgba(237, 232, 226, 0.09)`; card border-radius `4px-6px` (sharp editorial corners).
  - don't: Do not animate the SVG `feTurbulence` seed or position on scroll, as full-viewport SVG filter repaints cause severe GPU composite bottlenecks on Retina/4K displays; bake the noise into a static `128x128px` or `200x200px` tiled PNG/WebP data URI.
  - scope: aesthetic:warm-editorial
- Option: Style technical diagrams and product schematics after patent drawings, Horological blueprints, or 1970s technical manuals: use `1px` single-weight vector line art in warm stone (`#9E958C`) and parchment (`#EDE8E2`), dashed leader lines (`stroke-dasharray: 3 3`), filled `4px` amber terminal nodes (`#F59E0B`), and hatched SVG pattern fills (`45deg` lines at `6px` pitch, `1px` stroke `rgba(217, 119, 6, 0.25)`) instead of smooth multi-stop gradients. _(support 1, basic, R0567)_
  - values: Primary vector stroke `1px` (`#EDE8E2`), secondary construction stroke `1px` (`#463D36`), dashed leader `stroke-dasharray: 3 3` (`#6E655D`), active signal path `1.5px` (`#F59E0B`), hatch pitch `6px` at `45deg`, node radius `3px-4px`.
  - don't: Do not use `1px` unscaled hairline SVG strokes without `vector-effect: non-scaling-stroke` when diagrams resize responsively from `1200px` down to `350px`, or strokes will shrink to `0.3px` and vanish on mobile screens.
  - caveat: evidence DIRECT only for `vector-effect: non-scaling-stroke`; the drawing style is an option
  - scope: aesthetic:warm-editorial
- Option: Frame live product screenshots or UI mocks inside the warm-serif dark aesthetic by removing browser chrome and instead nesting the UI panel inside an editorial 'Exhibit Mat'—a recessed `#161310` outer frame with `24px-32px` padding, a `1px solid #2C2621` outer border, a `1px solid #3A322B` inner bezel around the UI, and a bottom caption bar (`height: 36px`, `border-top: 1px solid #2C2621`) displaying the exhibit number on the left and an interactive state toggle or live metric on the right. _(support 1, basic, R0568)_
  - values: Exhibit mat padding `24px-32px` (desktop), `12px-16px` (mobile); mat background `#161310`; caption bar `36px` height, `12px` monospace `#9E958C`.
  - don't: Do not tilt product screenshots in 3D perspective (`rotateX(20deg) rotateY(-15deg)`) inside an editorial dark aesthetic; they clash with flat, orthographic editorial print authority.
  - scope: aesthetic:warm-editorial
  - group: product-frame (pick one)
- Option: Use editorial horizontal rules and section colophons inspired by book typography: pair a `1px solid #2C2621` full-width rule with a centered or left-anchored `24px-48px` amber accent tick (`1px solid #D97706`) or a double-rule header (`border-top: 2px solid #EDE8E2; border-bottom: 1px solid #2C2621; padding: 12px 0`) for major manifesto or benchmark tables. _(support 1, basic, R0583)_
  - values: Double rule: `2px` top stroke + `3px` gap + `1px` bottom stroke (classic newspaper/editorial ledger rule); or `1px solid #2C2621` with `32px` `#D97706` active indicator bar.
  - don't: Do not use blurred glowing neon divider lines (`box-shadow: 0 0 20px #D97706`), which shift the aesthetic from tactile print/editorial to sci-fi cyberpunk.
  - scope: aesthetic:warm-editorial

## depth-shadow
- Tint shadow layers using the underlying surface's HSL/OKLCH hue at reduced saturation and low lightness rather than neutral black so shadows blend naturally with colored backgrounds. _(support 1, basic, R0813)_
  - values: Shadow color formula: hsl(var(--surface-hue) 40% 13% / 0.04 to 0.10) or oklch(0.20 0.04 var(--surface-hue) / 0.06) instead of rgba(0, 0, 0, 0.25), keeping light source direction fixed across the entire UI (typically top-lit: X-offset = 0px, Y-offset > 0px).
  - do: Tint shadow layers using the underlying surface's HSL/OKLCH hue at reduced saturation and low lightness rather than neutral black so shadows blend naturally with colored
  - don't: Do not use heavily saturated shadow tints (>50% saturation) or mix inconsistent X/Y offset angles across components on the same screen.
- Structure application Z-space into four semantic elevation tiers—Sunken, Default, Raised, and Overlay—so every component maps to a predictable depth role. _(support 1, basic, R0815)_
  - values: Atlassian Design System elevation tokens: 1. elevation.surface.sunken (recessed wells, Kanban columns, code blocks); 2. elevation.surface (default page canvas, 0dp); 3. elevation.surface.raised + elevation.shadow.raised (draggable cards, elevated tiles); 4. elevation.surface.overlay + elevation.shadow.overlay (modals, dropdown menus, popovers, floating toolbars).
  - don't: Do not nest more than 3 distinct elevation tiers within a single component tree (e.g., an overlay inside a raised card inside another raised card).
- In a shadow scale use blur as a distance cue: larger elevation gets larger blur. _(support 1, basic, R0906)_
  - values: Fluent shadow 2 = blur 2px; shadow 64 = blur 64px
  - do: Scale blur with elevation (Fluent: shadow 2 blur 2px, shadow 64 blur 64px).
  - don't: Do not treat these values as a complete recipe independent of system tokens and opacities.

## shape-radius
- Establish a proportional border-radius token scale anchored to a single root variable or 4px geometric progression so small controls, medium cards, and large modals scale cohesively. _(support 1, basic, R0806)_
  - values: Fluent 2 enterprise scale: None=0px (nav/tab bars), Small=2px (badges <32px), Medium=4px (buttons/inputs), Large=8px (cards), X-Large=12px (flyouts/sheets), Circle=50% (avatars); Shopify Polaris scale: --p-border-radius-100=4px, -200=8px, -300=12px, -400=16px; shadcn/ui radius math: --radius=0.625rem (10px), --radius-sm=calc(var(--radius) - 4px) (6px), --radius-md=calc(var(--radius) - 2px) (8px), --radius-lg=var(--radius) (10px), --radius-xl=calc(var(--radius) + 4px) (14px).
  - do: Establish a proportional border-radius token scale anchored to a single root variable or 4px geometric progression so small controls, medium cards, and large modals
  - don't: Do not apply rounded radii (>0px) to full-bleed viewport containers, edge-to-edge sticky top bars, or flush table cells where corner gaps create visual leaks. | Geist (codex): 6px for base/small materials and 12px for medium/large/menu/modal; codex notes the source gives no full radius scale and platform shapes may override.
- Derive a nested child radius from its parent as inner = outer - padding with a floor of 4px; if padding >= outer, use the next scale step down. _(support 1, basic, R0808)_
  - values: inner = max(4px, outer - padding); if padding >= outer pick the next token one step down the radius scale. Example: outer 20px, padding 8px -> inner 12px; outer 8px, padding 12px -> next step down (4px).
  - don't: Do not apply the subtraction when sibling elements separate the child from the parent corner. context: derived geometry, not a vendor-documented formula; the sources consulted only document 'child radius <= parent radius'. Merged from research2-codex-1.
- Shape floating navigation bars, primary touch pills, and status badges as full capsules by setting the corner radius to at least half of the element's height. _(support 1, basic, R0810)_
  - values: Capsule radius: border-radius: 9999px (CSS) or CornerRadius = Height / 2 (e.g., a 44pt high floating tab bar or button uses a 22pt corner radius; a 24px badge uses a 12px radius).
  - don't: Do not use capsule (9999px) radii on multi-line text containers, tall cards, or expandable accordions, where a 9999px radius distorts vertical edges into an oval stadium shape.

## glass-blur
- Never put glass/backdrop-blur behind body content; use it only on nav and overlays. _(support 1, basic, R0929)_
  - values: Glass belongs to the floating control layer (nav bars, toolbars, popovers, command palettes); the content layer stays opaque.
  - don't: Do not apply backdrop-filter to cards, tables or reading containers, and do not stack glass on glass.
- Give every glass surface a solid fallback via @supports not (backdrop-filter) and @media (prefers-reduced-transparency: reduce). _(support 1, basic, R0930)_
  - values: Default: opaque background-color; @supports (backdrop-filter: blur(12px)) { translucent + blur }; @media (prefers-reduced-transparency: reduce) { backdrop-filter: none; background: var(--solid-surface); }.
  - don't: Never ship a translucent rgba() background without an opaque fallback for unsupported browsers and reduced-transparency users.

## radius-discipline
- For editorial/product-led pages use 3 radii (12 frame, 8 inner, 6 control), pills only for status tags, hairlines carrying structure and shadows only on overlays. _(support 1, basic, R1124)_
  - values: Radii: 12 (frame) / 8 (inner) / 6 (control); pills: status tags only; shadows: overlays only.
  - don't: Do not use pills for non-status elements, shadows on non-overlay surfaces, or more than 3 radii. | except: this is an alternative to the expressive 20-28px 'not boxy' recipe; pick one per page.

## signature-motif
- Derive one signature motif from the product's own data transformation, fix its geometry as tokens, and reuse it in at least 5 places (logo, headline accent, product frame, section labels, chart, rail, final CTA). _(support 1, basic, R1119)_
  - values: Motif geometry fixed once as tokens (angle, stroke width, radius, spacing); reuse count >= 5; decorative non-motif shapes: 0.
  - don't: Do not use decorative shapes that are not the motif (blobs, waves, orbs, glows).

## subtle-shadows
- Show elevation with a very subtle drop shadow instead of adding borders or stronger fills. _(support 1, basic, R0386)_
  - values: X 0, Y 28, Blur 28, Spread 0, 000000 3%
  - do: Drop shadow X 0, Y 28, blur 28, spread 0, #000000 at 3%

## swap-test
- At 25% zoom with the logo hidden the page must still be identifiable; if another brand name could be dropped in unnoticed, the art direction fails. _(support 1, basic, R1127)_
  - values: Test: view at 25% zoom, logo hidden, then swap the brand name; the page must still read as this product.
  - don't: Do not ship art direction that stays identical when the brand name is swapped.

## text-animation
- Never hide the LCP element (hero heading/image) behind an entrance animation; animate secondary elements instead. _(support 1, basic, R0985)_
  - values: LCP <= 2.5s (good threshold)
  - do: Render the hero heading/image visible in the first paint; animate secondary elements (eyebrow, subcopy, decorative layers).
  - don't: Do not hide the LCP element with opacity: 0, visibility or a delayed entrance waiting on JS.

## Checklist

- [ ] Tint shadow layers using the underlying surface's HSL/OKLCH hue at reduced saturation and low lightness rather than neutral black so shadows blend naturally with colored backgrounds.
- [ ] Structure application Z-space into four semantic elevation tiers—Sunken, Default, Raised, and Overlay—so every component maps to a predictable depth role.
- [ ] In a shadow scale use blur as a distance cue: larger elevation gets larger blur.
- [ ] Never put glass/backdrop-blur behind body content; use it only on nav and overlays.
- [ ] Give every glass surface a solid fallback via @supports not (backdrop-filter) and @media (prefers-reduced-transparency: reduce).
- [ ] For editorial/product-led pages use 3 radii (12 frame, 8 inner, 6 control), pills only for status tags, hairlines carrying structure and shadows only on overlays.
- [ ] Establish a proportional border-radius token scale anchored to a single root variable or 4px geometric progression so small controls, medium cards, and large modals scale cohesively.
- [ ] Derive a nested child radius from its parent as inner = outer - padding with a floor of 4px; if padding >= outer, use the next scale step down.
