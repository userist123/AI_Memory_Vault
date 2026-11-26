---
id: core-motion
title: Motion basics (fluid kit)
tier_min: T1
page_types: ["any"]
topics: ["micro-interactions", "hover-press", "easing", "performance", "durations", "mask-reveal", "animation-timing", "breakpoint-scoped-motion", "copy-button-feedback", "doherty-threshold", "doherty-threshold-feedback", "loader-choice", "passive-input-autoplay", "perceived-speed", "pinned-scroll-story"]
rules: 33
---

# Motion basics (fluid kit)

CSS-only motion every page may use: tokens, reveal, hover, view transitions, reduced motion.
Support = number of independent educators who teach the rule.

## micro-interactions
- Keep frequent feedback brief and remove animation entirely (0ms) from high-frequency keyboard actions so motion never becomes a speed tax. _(support 1, basic, R0950)_
  - values: 0ms for keyboard selection changes and repeated command-palette toggles; subsequent tooltips in a warm group 0ms
  - do: Use 0ms for command palettes, autocomplete highlighting, arrow-key navigation and warm tooltip groups; let users continue or cancel without waiting for an animation.
  - don't: Do not add motion to frequent standard controls that already have system feedback; do not strip animation from first-time contextual disclosures or low-frequency spatial transitions where orientation matters.
- Use motion to communicate state, feedback, instruction, hierarchy or direct manipulation, not as decoration. _(support 1, basic, R0951)_
  - do: Give every animation a functional purpose.
  - don't: Do not add motion solely as decoration when it distracts or delays the task.
- Animate popovers, dropdowns, modals and toasts from an initial scale of 0.90-0.97 with opacity 0 rather than from scale(0). _(support 1, basic, R0952)_
  - values: scale range 0.90-0.97 (default 0.95); duration 150ms-200ms; easing cubic-bezier(0.16, 1, 0.3, 1)
  - do: Initial opacity: 0; scale(0.95) -> opacity: 1; scale(1).
  - don't: Never animate overlays from scale(0).
- Anchor every popover, tooltip and context menu's transform-origin to the trigger edge it emerges from. _(support 1, basic, R0953)_
  - values: scale 0.95 -> 1.0, translation 4px -> 0px, duration 150ms
  - do: Use top left, bottom center or a dynamic anchor-position origin.
  - don't: Do not leave the default transform-origin: center on menus attached to a top or bottom trigger.
- Morph stateful action icons (Copy -> Checkmark, Play -> Pause) with simultaneous opacity and scale. _(support 1, basic, R0955)_
  - values: scale(0.5) -> scale(1), 150ms-200ms; hold 1500ms-2000ms
  - do: Hold the feedback state before reverting.
  - don't: Do not run the morph longer than 200ms or leave the feedback state stuck.

## hover-press
- Gate hover-only effects behind a hover-capable input query and give touch and keyboard users equivalent active and focus feedback. _(support 1, basic, R0957)_
  - values: @media (hover: hover) and (pointer: fine); :active 100ms
  - do: Use @media (hover: hover) and (pointer: fine); touch feedback relies on :active (100ms).
  - don't: Never make hover the only way to reveal critical controls, labels or information.
- Shrink buttons slightly on :active press to confirm the press registered. _(support 1, basic, R0958)_
  - values: scale(0.97), transition: transform 100ms ease-out; 100ms-150ms
  - do: Use 0.98-0.99 for large cards/rows and 0.95-0.97 for buttons/pills.
  - don't: Do not apply :active shrink to giant cards (>400px) or to disabled buttons.
- Use asymmetric hover timing on cards and decorative elements: fast on pointer enter, gentler settle on pointer leave. _(support 1, basic, R0959)_
  - values: hover enter: transition: transform 125ms ease-out; translateY(-4px); base (exit): 400ms ease-out
  - do: Enter 100ms-150ms; leave 250ms-450ms.
  - don't: Do not use slow hover exits (400ms) on dense list rows, menu items or table rows.
- Prevent hover jitter on lifted cards by keeping the hit-test wrapper stationary and translating only the inner visual surface. _(support 1, basic, R0960)_
  - values: translateY(-4px) to -8px; example -6px
  - do: Outer hit area fixed; inner .card-surface translateY(-6px).
  - don't: Never translate the element bearing :hover directly without a stationary wrapper.

## easing
- Use an aggressive decelerating ease-out curve for elements entering the viewport so the animation starts instantly and settles gently. _(support 1, basic, R0940)_
  - values: M3 Emphasized Decelerate cubic-bezier(0.05, 0.7, 0.1, 1); Carbon Entrance Productive cubic-bezier(0, 0, 0.38, 0.9); Fluent 2 curveDecelerateMax cubic-bezier(0.1, 0.9, 0.2, 1)
  - do: Apply to entering overlays, toasts and revealed content.
  - don't: Never use for exiting elements or continuous looping spinners.
- Use an accelerating ease-in curve only for elements permanently exiting the screen so they clear the workspace rapidly. _(support 1, basic, R0941)_
  - values: M3 Emphasized Accelerate cubic-bezier(0.3, 0, 0.8, 0.15); M3 Standard Accelerate cubic-bezier(0.3, 0, 1, 1); Carbon Exit Productive cubic-bezier(0.2, 0, 1, 0.9); Fluent 2 curveAccelerateMax cubic-bezier(0.9, 0.1, 1, 0.2)
  - do: Apply to dismissals and exits.
  - don't: Never use ease-in or accelerate curves for entering UI elements or user-triggered hover/press feedback, as slow starts feel laggy.
- Reserve linear easing strictly for constant-rate time indicators, marquee loops, and color/opacity transitions without spatial translation. _(support 1, basic, R0945)_
  - values: linear or cubic-bezier(0, 0, 1, 1)
  - do: Use linear for progress and time indicators.
  - don't: Never apply linear easing to spatial transform: translate() or scale() movements, which look robotic.

## performance
- Animate transform and opacity for continuous or interactive motion so the compositor can handle it without layout or paint. _(support 1, basic, R0989)_
  - values: frame budget 16.67ms at 60Hz, 8.33ms at 120Hz
  - do: Use translate, scale, rotate and opacity. Exception: one-off disclosure (grid-template-rows 0fr->1fr) on a small subtree.
  - don't: Never animate top, left, width, height, margin, padding or border-width in keyframes or per-frame JS loops.
- Paint press/toggle feedback on the next frame before heavy synchronous work to keep INP within the good threshold. _(support 1, basic, R0995)_
  - values: INP good <= 200ms (75th percentile); needs improvement 200ms-500ms; poor > 500ms
  - do: Use :active, the Web Animations API, requestAnimationFrame or startTransition.
  - don't: Never block the click handler for >50ms before the press animation starts.
- Never insert unsolicited animated banners, toasts or accordions above existing content without reserved space or fixed positioning, to keep CLS low. _(support 1, basic, R0996)_
  - values: CLS good <= 0.1 (75th percentile); poor > 0.25; transform animations contribute 0.00
  - do: Use position: fixed or pre-allocated space; transform-based motion does not add CLS. User-initiated expansion within 500ms of input is excluded from CLS, so this applies to unsolicited insertions only.
  - don't: Never expand a top banner from height: 0 -> 48px after initial paint.

## durations
- Scale transition duration with travel distance and surface area instead of using one fixed duration everywhere. _(support 1, basic, R0934)_
  - values: Carbon: 70ms button/toggle micro-interactions, 110ms fades, 150ms small expansions or short movements, 240ms expansions/toasts, 400ms large expansions, 700ms background dimming; gemini ranges: small (<32px) 50ms-150ms, medium (cards, dropdowns) 150ms-250ms, large (dialogs, drawers) 250ms-400ms
  - do: Small state shifts short, medium components mid, large layout expansions longest.
  - don't: Do not use the same fixed duration (e.g., 300ms everywhere) across both 16px checkboxes and 800px drawers.
- Keep standard interactive UI transitions (dropdowns, popovers, tooltips, menus, modals) under 300ms so the interface never feels sluggish. _(support 1, basic, R0946)_
  - values: Button press: 100ms-160ms; Tooltips/micro-interactions: 100ms-200ms; Dropdowns/popovers: 150ms-250ms; Modals/drawers: 200ms-300ms (mobile bottom sheets up to 400ms-500ms)
  - do: Pick the duration per component type from the values.
  - don't: Do not apply the <300ms cap to full-screen mobile bottom sheets mimicking native iOS spring physics or ambient background illustrations.

## mask-reveal
- Use strong ease-out deceleration curves for text reveals so text arrives immediately upon trigger and settles gently. _(support 1, basic, R1072)_
  - values: Recommended curves: cubic-bezier(0.16, 1, 0.3, 1) (Expo Out), cubic-bezier(0.215, 0.61, 0.355, 1) (Cubic Out), or cubic-bezier(0.32, 0.72, 0, 1) (iOS sheet/drawer curve). Prohibited: ease-in, cubic-bezier(0.42, 0, 1, 1).
  - don't: Never use ease-in or linear timing for text reveals. | except: use linear timing when the reveal is tied 1:1 to the user's scroll position (animation-timeline: view()) or for continuous marquee loops.
- Render the hero H1 text in the initial HTML so it is visible without JavaScript; never hide the H1 behind JS. _(support 1, basic, R1075)_
  - values: H1 text present in the initial HTML; no JS-gated opacity: 0 or zero-area clip-path on the H1.
  - don't: Never hide the hero H1 with .js-loading { opacity: 0 } waiting for React hydration or an external animation library, which delays LCP. | codex (web.dev): keep the H1 text in the initial HTML and do not hide it until an animation runs; do not let a JS-dependent reveal delay or suppress the visible headline.

## animation-timing
- Keep UI animations under 300ms and respond to input within 400ms (Doherty threshold). _(support 1, basic, R0362)_
  - values: <300ms animations, 400ms response

## breakpoint-scoped-motion
- A transform or hidden state that belongs to a responsive variant is declared inside that variant's media query (DIRECT, v7 L588). _(support 1, basic, R0670)_
  - values: Computed transform is `none` outside the variant.
  - do: Declare variant-specific transforms and hidden states inside that variant's media query.
  - don't: Do not set a variant's transform or hidden state in the base styles.

## copy-button-feedback
- Morph a copy icon into a checkmark (with accent colour) after copying so users know it worked. _(support 1, basic, R0167)_

## doherty-threshold
- Respond to every action instantly: show feedback on tap, a loading state while working, and a success confirmation; never leave users guessing. _(support 1, basic, R0144)_
  - do: Save button -> 'Saving...' spinner -> 'Saved successfully!' check

## doherty-threshold-feedback
- Respond instantly to every action: show a loading state (e.g. 'Saving...') immediately, then confirm success ('Saved successfully!'); never leave users guessing. _(support 1, basic, R0033)_

## loader-choice
- Pick the loader by wait type: spinner for short waits, progress bar or ring when progress is known, skeleton screen for content, shimmer for AI generation, pull-to-refresh for feeds. _(support 1, basic, R0078)_
  - values: 8 styles: Spinner, Typing dots, Progress bar, Skeleton, AI shimmer, Progress ring, Pull to refresh, Shape morph

## passive-input-autoplay
- Passive input never cancels autoplay (DIRECT, v8 L1254-1258): hover arms after autoplay ends; click, drag and keys cancel. _(support 1, basic, R0753)_
  - values: Cancel on click, drag, keys only.
  - do: Arm hover only after autoplay ends and cancel autoplay on click, drag or keys.
  - don't: Do not cancel autoplay on a passive pointer move or hover.

## perceived-speed
- Give instant visual feedback on press for slow actions (e.g. payment); identical load times feel faster when the UI responds immediately. _(support 1, basic, R0180)_
  - values: button takes 2.0 s; 'one second too long'

## pinned-scroll-story
- Unpin when the stage content no longer fits: gate the pinned layout on min-width AND min-height (plus `prefers-reduced-motion: no-preference`) and fall back to a static vertical stack with every state visible. _(support 1, basic, R0520)_
  - values: candidate gate: min-width 1024px; min-height = stage content + header + 96px; stack gap 24-32px.
  - don't: Do not disable pinning on mobile if the entire section is a single full-bleed visual reveal with `<= 200vh` total track height and minimal overlay copy (`<= 2` lines).
  - group: pinned-scene-always-on

## pull-to-refresh-feedback
- Show a visible indicator as soon as the user pulls to refresh so they know something is happening. _(support 1, basic, R0181)_

## scroll-driven
- Gate CSS scroll-driven animations behind @supports and keep content fully visible by default for unsupported browsers. _(support 1, basic, R0962)_
  - values: fallback 0 KB JS, or IntersectionObserver (threshold: 0.15) if an entrance trigger is required
  - do: Use scroll() for scroller progress and view() for element visibility progress inside @supports (animation-timeline: view()) and (prefers-reduced-motion: no-preference).
  - don't: Never set opacity: 0 in base CSS outside the @supports block.

## scroll-timeline
- Gate all CSS scroll-driven story styles and 300vh track heights behind @supports (animation-timeline: view()) and (prefers-reduced-motion: no-preference) so unsupported browsers render an accessible static stacked layout. _(support 1, basic, R1064)_
  - values: Feature query: @supports (animation-timeline: view()) { @media (prefers-reduced-motion: no-preference) { .track { height: 320vh; } .panel { opacity: 0; animation: reveal linear both; animation-timeline: --track; } } }; Fallback state: height: auto; position: relative; opacity: 1; transform: none. Browser status (belief, unverified): scroll-driven animations ship in Chromium 115+ and Safari 26; Firefox default-on status is UNKNOWN, so @supports gating is mandatory.
  - don't: Never apply opacity: 0 or height: 300vh outside the @supports block, which leaves older browsers staring at 300vh of blank space or unrevealed content. | codex (MDN/Chrome): declare animation-timeline AFTER the animation shorthand because the shorthand resets it to auto; if the animation is decorative and support is missing, leave content in a fully visible static state; never hide content by default or rely on animation to reveal necessary information. | codex (Chrome): detect with @supports ((animation-timeline: view()) and (animation-range: entry)) since checking animation-range filters out partial implementations; for functional effects build an IntersectionObserver or scroll-listener fallback; purely decorative effects need no JS fallback; do not use the scroll-timeline polyfill automatically (Chrome says it is incomplete with known issues). | codex (web.dev): respect prefers-reduced-motion; remove or reduce decorative motion and provide the final static state; parallax and independent layer motion can cause dizziness, so never make it the only way to understand the product story.

## scrub-panel-text
- No masked or rolling text in a scrubbed panel (DIRECT, v8 L724, L786): whole rows only. _(support 1, basic, R0754)_
  - values: Whole rows only.
  - do: Animate whole rows in a scrubbed panel.
  - don't: Do not mask or roll text inside a scrubbed panel.

## view-transitions
- Use cross-document view transitions between opted-in same-origin pages as progressive enhancement, declared inside a reduced-motion no-preference query. _(support 1, basic, R0968)_
  - values: Chrome/Edge 126+ and Safari 18.2+ (no Firefox support as of late 2026 - feature-detect)
  - do: @media (prefers-reduced-motion: no-preference) { @view-transition { navigation: auto; } } in both pages.
  - don't: Do not enable on slow-TTFB pages without prerendering/prefetching; keep standard navigation when unsupported.

## Checklist

- [ ] Keep UI animations under 300ms and respond to input within 400ms (Doherty threshold).
- [ ] A transform or hidden state that belongs to a responsive variant is declared inside that variant's media query (DIRECT, v7 L588).
- [ ] Morph a copy icon into a checkmark (with accent colour) after copying so users know it worked.
- [ ] Respond to every action instantly: show feedback on tap, a loading state while working, and a success confirmation; never leave users guessing.
- [ ] Respond instantly to every action: show a loading state (e.g. 'Saving...') immediately, then confirm success ('Saved successfully!'); never leave users guessing.
- [ ] Scale transition duration with travel distance and surface area instead of using one fixed duration everywhere.
- [ ] Keep standard interactive UI transitions (dropdowns, popovers, tooltips, menus, modals) under 300ms so the interface never feels sluggish.
- [ ] Use an aggressive decelerating ease-out curve for elements entering the viewport so the animation starts instantly and settles gently.
