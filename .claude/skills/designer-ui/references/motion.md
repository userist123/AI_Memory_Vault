---
id: motion
title: Motion & micro-interactions
tier_min: T3
page_types: ["any"]
topics: ["performance", "scrub-easing", "easing", "page-transitions", "pinned-scroll-story", "micro-interactions", "scroll-snap", "pinned-scroll", "scroll-timeline", "transform-registration", "view-transitions", "mask-reveal", "optimistic-ui", "scroll-driven", "scroll-reveal-range"]
rules: 90
---

# Motion & micro-interactions

Durations, easing, feedback.
Support = number of independent educators who teach the rule.

## performance
- Treat the hero as a server-rendered static HTML/CSS product shell that paints immediately; never start the likely LCP element at `opacity: 0` or `visibility: hidden`, and measure LCP in field data against the 2.5 s (75th percentile) target. _(support 1, intermediate, R0600)_
  - values: LCP <= 2.5 s at p75 (web.dev threshold); Chrome ignores an opacity:0 or hidden element for LCP until it becomes visible.
  - do: Ship the headline and a static product frame in the initial HTML, reveal secondary layers after first paint, and read LCP from field data after every hero change.
  - don't: Do not hide the LCP element and fade it in with JS; do not treat the 2.5 s target as proof that an animated hero technique is good.
  - group: hero-no-video-performance
- Make the likely LCP image discoverable in the initial HTML (`img src`/`srcset`) or preload it when it is a CSS background, and give only that one or two images `fetchpriority="high"`. _(support 1, intermediate, R0601)_
  - values: `fetchpriority="high"` on <= 2 images; preload for late-discovered assets.
  - do: Use a plain `<img>` with `fetchpriority="high"` for the hero visual; add `<link rel="preload" as="image">` only when it is a CSS background or injected late.
  - don't: Do not lazy-load or JS-inject the LCP image; do not mark more than one or two images high priority; do not expect `fetchpriority` to guarantee an LCP gain, it is a browser hint.
- Animate hero UI transitions with `transform` and `opacity` only, because they are the compositor-friendly properties; reserve layout-changing movement for FLIP. _(support 1, intermediate, R0603)_
  - values: target: no layout work per frame; 60 FPS is the aim, verify in a trace.
  - do: Express hero motion as translate/scale plus opacity; when a block must change size, measure first/last boxes and play a FLIP transform instead.
  - don't: Do not animate width, height, top, left or margin; do not call `filter` or `clip-path` compositor-only, they repaint and need a perf check before use in a scrubbed scene.
  - caveat: source_url is the CLS article; the compositor-only claim is standard platform guidance, verify per browser with a performance trace.
- Pause canvas/WebGL `requestAnimationFrame` loops when the hero leaves the viewport, and do not start them at all when `prefers-reduced-motion: reduce` is set. _(support 1, intermediate, R0605)_
  - values: zero rAF ticks while the hero is fully off screen.
  - do: Observe the hero with IntersectionObserver, cancel the rAF loop at `intersectionRatio === 0`, and render one static frame for reduced-motion users.
  - don't: Do not leave an infinite rAF loop running under a scrolled-away hero; skip this for pure CSS animations the browser already pauses off screen.
- Hydrate interactive hero demos progressively: paint a static shell, attach handlers on idle or on first interaction, and keep input feedback within about 100 ms. _(support 1, intermediate, R0614)_
  - values: feedback <= 100 ms after input (RAIL-style budget).
  - do: Render the demo markup server-side, bind keystroke and tab handlers on first pointer/keyboard event or idle, and defer heavy demo bundles.
  - don't: Do not ship heavy WebGL or demo bundles on the critical rendering path; skip hydration for CSS-only hover and focus effects.
- In ResizeObserver and FLIP handlers batch all geometry reads before any DOM or SVG writes, and keep per-callback work well inside one frame. _(support 1, intermediate, R0630)_
  - values: 16.6 ms frame budget at 60 FPS; total interaction setup within about 100 ms.
  - do: Collect every `getBoundingClientRect` first, then write attributes and transforms in a second pass.
  - don't: Do not interleave reads and writes; skip this for isolated transforms driven natively by CSS timelines or anchor positioning.
- Apply will-change dynamically on anticipated interaction and remove it after transitionend rather than declaring it permanently. _(support 1, intermediate, R0992)_
  - values: each 1080p RGBA layer ~8.3 MB VRAM (1920 * 1080 * 4 bytes)
  - do: Add it on :hover or pointerenter.
  - don't: Never declare * { will-change: transform; } or permanent will-change on dozens of cards. disagrees: codex requires a measured performance issue before using will-change.
- Use FLIP (First, Last, Invert, Play) when transitioning layout dimensions or grid positions so the change runs as a compositor transform. _(support 1, intermediate, R0994)_
  - values: animate to translate(0,0) scale(1,1) over 200ms-300ms
  - do: Automate via the Motion layout prop or GSAP Flip.from().
  - don't: Do not run FLIP measurements across 100+ nodes on every resize without throttling.

## scrub-easing
- Apply non-linear `animation-timing-function` declarations locally inside individual `@keyframes` stops (or individual child tweens in a scrubbed GSAP timeline) to shape transitions between plateaus while keeping the overall scroll timeline proportional. The element-level `animation-timing-function` is applied to each keyframe interval separately, not across the whole animation. _(support 1, intermediate, R0523)_
  - values: At keyframe `0%`: `animation-timing-function: cubic-bezier(0.22, 1, 0.36, 1)` (enter); at `20%` (start of plateau): `animation-timing-function: linear`; at `80%` (end of plateau / start of exit): `animation-timing-function: cubic-bezier(0.4, 0, 1, 1)`. codex: animation-timing-function can be set on individual keyframes; validate the position-to-progress mapping instead of assuming the element-level function governs every interval.
  - don't: Do not declare `animation-timing-function` on the `100%` keyframe selector, as CSS ignores timing functions on the final keyframe stop (a keyframe's timing function governs interpolation from that keyframe to the next).
  - group: pinned-scene-always-on
- Always use strict `linear` easing (`ease: "none"`, `animation-timing-function: linear`) for direct spatial/geometric mappings: progress bars, scroll thumb indicators, 1:1 parallax translations, continuous 360-degree rotations, and SVG path drawing (`stroke-dashoffset`) that tracks a scroll line down the page. _(support 1, intermediate, R0524)_
  - values: `animation-timing-function: linear`; `ease: "none"`; 1:1 ratio between scroll delta `dy` and progress delta `dp`. Root `animation-timing-function: linear` (CSS) or `ease: "none"` (GSAP). Root timeline linear first, then local per-segment easings. codex: GSAP scroll-linked example uses ease: "none"; progress meters must not get keyframe easing.
  - don't: Do not use `linear` easing for UI card entrances, modal reveals, or feature state transitions inside a pinned stage, where linear motion feels robotic and weightless.
  - group: pinned-scene-always-on
- Use asymmetric ease-out (cubic-bezier(0.23, 1, 0.32, 1)) for time-triggered UI entrances and symmetric ease-in-out (cubic-bezier(0.65, 0, 0.35, 1) or power2.inOut) for scrubbed state transitions, because a scrubbed animation is reversed whenever the user scrolls up; keep the timeline root linear. For an exit followed by an entrance, use ease-in on the exit and ease-out on the entrance: the pair is symmetric as a whole. _(support 1, intermediate, R0525)_
  - values: Time-based entrance: `cubic-bezier(0.23, 1, 0.32, 1)` (asymmetric `ease-out`). Scroll-scrubbed transition between states: `cubic-bezier(0.65, 0, 0.35, 1)` (`ease-in-out`, symmetric under time-reversal `f(1-t) = 1 - f(t)`).
  - don't: Do not apply linear or ease-in to time-based UI transitions that need immediate responsiveness.
  - disagrees: practitioner UI animation guides (Emil Kowalski, Rauno Freiberg) advise avoiding linear and ease-in in UI motion, while GSAP and Chrome scroll-driven docs use linear at the timeline root; reconcile by linear root plus per-segment easing.
  - group: pinned-scene-always-on
- Use `scrub: true` (0ms synthetic lag) on mobile touch devices or when a global smooth-scroll wrapper (such as Lenis or GSAP ScrollSmoother with `smooth: 1`) is already active on the page. _(support 1, intermediate, R0527)_
  - values: `scrub: true` (0s additional tween lag) when paired with native touch inertia or global smooth scroll (`lerp: 0.1` / `smooth: 1`).
  - don't: Never stack a global smooth scroller (`ScrollSmoother` `smooth: 1.2`) on top of a per-trigger `scrub: 1` lag; double-smoothing compounds latency and causes rubber-banding lag.
  - stack: gsap
- Use the native CSS `linear()` multi-point easing function (`animation-timing-function: linear(0, 0.45 20%, 0.88 45%, 1.03 70%, 1)`) for spring physics on time-triggered scroll milestones, and never use an overshooting curve (output above 1) on a scroll-scrubbed timeline. _(support 1, intermediate, R0530)_
  - values: Multi-stop `linear(...)` with 12-20 sampled spring points and subtle overshoot (`1.02` to `1.04`).
  - don't: Do not use overshooting spring curves (`> 1.0`) on scroll-scrubbed animations or opacity fades; in a scrubbed timeline, an overshoot curve means scrolling forward continuously causes the element to move forward, then backward, then forward again while the user's finger moves strictly in one direction, violating direct manipulation physics.
  - group: pinned-scene-always-on
- When scrubbing through a sequence of discrete UI cards or code snippets inside a pinned stage, overlap the outgoing card's exit window and the incoming card's entrance window by at most `5%-10%` of the transition range (or separate them by a `2%-4%` gap where outgoing reaches `opacity: 0` at `48%` and incoming starts at `50%`) while using `ease-in` on the exit and `ease-out` on the entrance. _(support 1, intermediate, R0571)_
  - values: Step 1 exit: `35% { opacity: 1; transform: translateY(0); animation-timing-function: cubic-bezier(0.4, 0, 1, 1); } 48% { opacity: 0; transform: translateY(-16px); }`. Step 2 enter: `50% { opacity: 0; transform: translateY(16px); animation-timing-function: cubic-bezier(0, 0, 0.2, 1); } 65% { opacity: 1; transform: translateY(0); }`.
  - don't: Never crossfade two dense text blocks or code editors simultaneously at `50%` opacity on the same coordinates (`35%-65%` linear overlap), because at the midpoint (`50%` scroll) both text layers render at `0.5` opacity on top of each other, creating an illegible muddled frame if the user stops scrolling mid-transition.
  - group: pinned-scene-always-on
- Use linear progress for tweens inside a scrubbed timeline (`ease: "none"`), use `scrub: true` for direct linkage, and add smoothing only through the numeric `scrub` catch-up value, never as an extra easing curve. _(support 1, intermediate, R0624)_
  - values: GSAP docs: `scrub: true` = direct link, `scrub: 1` = 1 s catch-up; snap duration 0.2-3 s and delay 0.2 s are doc examples, not defaults.
  - do: Set `ease: "none"` on scrubbed tweens, start with `scrub: true`, and raise it to a small numeric value (0.5-1) only when motion feels brittle.
  - don't: Do not stack easing curves on top of scrub smoothing; use ease-out curves only for time-triggered onEnter/onLeave animations.
  - disagrees: applies to spatial mappings (position follows scroll). Short opacity handoffs in a CSS scroll timeline may carry per-keyframe easing (AR24).
  - stack: gsap

## easing
- For time-triggered UI animation use the project's motion tokens instead of the browser default `ease`: an ease-out for entrances, an ease-in-out for elements that move across the screen; do not add a new curve when a token exists. _(support 1, intermediate, R0528)_
  - values: candidate curves: `--ease-out-quint: cubic-bezier(0.23, 1, 0.32, 1)`; `--ease-in-out-quart: cubic-bezier(0.77, 0, 0.175, 1)`; `--ease-drawer: cubic-bezier(0.32, 0.72, 0, 1)`; durations `150ms-250ms` for micro-UI, `250ms-400ms` for panels/drawers.
  - don't: Do not apply `150ms-250ms` durations to large full-viewport marketing hero reveals (>800px travel), which require `500ms-700ms` with `cubic-bezier(0.16, 1, 0.3, 1)` (ease-out-expo) to avoid looking jarring.
- Never animate elements from `scale(0)` in scroll reveals or UI popovers; start entrances from `scale(0.95)` or `scale(0.97)` paired with `opacity: 0` and `translateY(8px-16px)`. _(support 1, intermediate, R0529)_
  - values: Initial state: `opacity: 0; transform: translateY(12px) scale(0.96);` -> Final state: `opacity: 1; transform: translateY(0) scale(1);`.
  - do: Start entrances from `scale(0.95-0.97)` with `opacity: 0` and `translateY(8-16px)`.
  - don't: Never animate from `scale(0)`. except: no scale on hairline-bordered or tabular elements if 1px borders shimmer.
- Keep separate productive (task efficiency) and expressive (brand/emphasis) easing curves and do not treat them as interchangeable. _(support 1, intermediate, R0933)_
  - values: Productive: standard cubic-bezier(0.2, 0, 0.38, 0.9), entrance cubic-bezier(0, 0, 0.38, 0.9), exit cubic-bezier(0.2, 0, 1, 0.9); Expressive: standard cubic-bezier(0.4, 0.14, 0.3, 1), entrance cubic-bezier(0, 0, 0.3, 1), exit cubic-bezier(0.4, 0.14, 1, 1)
  - do: Use expressive curves for marketing/brand moments and productive curves for dense app UI.
  - don't: Do not mix productive and expressive curves arbitrarily within one component.
- Use an asymmetric ease-in-out curve for elements moving or morphing while remaining visible on screen. _(support 1, intermediate, R0942)_
  - values: M3 standard curve cubic-bezier(0.2, 0, 0, 1) (fallback for emphasized); Carbon Standard Expressive cubic-bezier(0.4, 0.14, 0.3, 1); Fluent 2 curveEasyEase cubic-bezier(0.33, 0, 0.67, 1) or curveEasyEaseMax cubic-bezier(0.8, 0, 0.2, 1)
  - do: Fast initial acceleration followed by prolonged deceleration.
  - don't: Do not use for simple opacity-only cross-fades or elements entering from outside the viewport.
- Use springs for spatial, gesture-driven or interruptible motion and time-based easing curves for short non-spatial effects such as opacity. _(support 1, intermediate, R0943)_
  - values: SwiftUI spring API Spring(duration: 0.4, bounce: 0.0-0.2); Motion spring stiffness: 300-500, damping: 25-35, mass: 1 (or bounce: 0.15, duration: 0.35); CSS linear(0, 0.006, ..., 1.045 30.5%, ..., 1)
  - do: Use springs (or CSS linear() approximations) where gesture velocity must be preserved or motion can be interrupted mid-flight.
  - don't: Do not force springs onto every property; avoid bouncy springs (bounce > 0.2 or under-damped) in enterprise productivity interfaces, financial dashboards, or when prefers-reduced-motion: reduce is active.
- Configure physics springs through physical parameters and let settling time follow the spring instead of specifying a fixed duration as its defining parameter. _(support 1, intermediate, R0944)_
  - do: Use a duration only for a CSS linear() approximation that necessarily requires one.
  - don't: disagrees: gemini-argon gives duration-based spring presets (Spring(duration: 0.4, ...), bounce: 0.15, duration: 0.35).

## page-transitions
- Match screen transitions to navigation meaning: slide for drilling into content, shared element for continuity between a card and its detail, modal sheet for temporary tasks; other options are fade, zoom, flip, circle reveal and card stack. _(support 1, intermediate, R0216)_
- Keep full-route page transitions between 150ms and 300ms using subtle cross-fades or short directional shifts rather than opaque curtain wipes. _(support 1, intermediate, R0972)_
  - values: 150ms-300ms (M3 medium1=250ms, medium2=300ms); offset 8px-24px
  - do: translateY(8px) to translateY(16px) with opacity 0 -> 1.
  - don't: Never use 800ms-1200ms full-screen curtain wipes on content, documentation, e-commerce or SaaS apps.
- Overlap route exit and entrance with a fast fade-through: outgoing fades in the first 30% while incoming fades and scales up in the remaining 70%. _(support 1, intermediate, R0973)_
  - values: total 250ms-300ms; exit 0ms-90ms cubic-bezier(0.3, 0, 1, 1); enter 90ms-300ms scale(0.96 -> 1) cubic-bezier(0, 0, 0, 1); initial scale 92%-96%
  - do: Run exit and entrance overlapping, not sequentially.
  - don't: Do not wait for a 300ms exit before a 300ms entrance (600ms blocked time).
- Encode hierarchy in direction: going deeper pushes forward, returning pops back, peer tabs slide by index order. _(support 1, intermediate, R0974)_
  - values: shared axis 30px, 250ms-300ms, cubic-bezier(0.2, 0, 0, 1)
  - do: Use shared-axis transitions for related routes.
  - don't: Do not apply directional slides between unrelated top-level routes; use fade-through.
- Reset scroll and move focus to the new route heading after the outgoing view hides, and skip custom transitions on native back/forward swipe gestures. _(support 1, intermediate, R0975)_
  - values: focus move <=300ms
  - do: Synchronize the focus move with transition completion.
  - don't: Never play a custom slide on top of iOS Safari's native edge-swipe back gesture.

## pinned-scroll-story
- Encode explicit plateau keyframes in CSS `@keyframes` attached to `animation-timeline: view()` or `scroll()` by repeating identical property values across the middle keyframe range (`20%, 80% { opacity: 1; transform: translateY(0); }`). _(support 1, intermediate, R0514)_
  - values: `0% { opacity: 0; transform: translateY(24px); } 20%, 80% { opacity: 1; transform: translateY(0); } 100% { opacity: 0; transform: translateY(-24px); }`.
  - don't: Do not use symmetric 20%/80% plateaus for the first and last steps of a pinned scene; the first step must start visible at `0%` (`0%, 80% { opacity: 1 }`) and the final step must remain visible through `100%` (`20%, 100% { opacity: 1 }`).
  - group: pinned-scene-always-on
- Option: Keep section headings, step counters (`01 / 04`), and progress indicators anchored inside the pinned stage (`position: sticky; top: var(--header-height, 64px); height: calc(100vh - var(--header-height, 64px))`) using an internal CSS Grid (`grid-template-rows: auto 1fr auto`), rather than letting the heading scroll out before the stage locks. _(support 1, intermediate, R0517)_
  - values: Option values: Stage height `calc(100dvh - 64px)`, top offset `64px`, header row `padding-top: 32px-48px`, step counter tabular monospace `12px-14px`. codex: keep headings, counters and controls as persistent siblings inside the pinned stage; animate nested content, never the pinned element itself (GSAP warns it breaks precomputed measurements).
  - don't: Do not lock a multi-line hero H1 (>120px tall) inside the pinned stage on viewports under `700px` tall, as it consumes >25% of the active stage canvas.
  - caveat: the "observed on <sites>" claim was not independently verified (codex: no live computed-style inspection of the named sites was performed).
- Size the sticky stage in `svh` (with a `vh` fallback) and the track in `svh` or `vh`, so the stage never resizes while pinned and scrub progress never jumps; use `dvh` on the stage only when it must fill the screen edge to edge and its content tolerates a resize. _(support 1, intermediate, R0518)_
  - values: `height: calc(100vh - var(--header-height)); height: calc(100svh - var(--header-height));` (stage); track in `svh` or `vh`.
  - don't: Do not use `dvh` on the track: its length changes as the mobile URL bar moves and the progress percentage jumps.
- When crossfading overlapping layers inside one shared runway, disable the injected spacer (`pinSpacing: false`) and give the runway an explicit overlap distance so no blank gap appears between panels. _(support 1, intermediate, R0617)_
  - values: explicit overlap distance in px or vh.
  - do: Set `pinSpacing: false`, size the runway yourself in px or vh, and verify the next section starts exactly after the last state.
  - don't: Do not use it for sequential non-overlapping pinned sections that need the spacer; do not forget to add the runway height back, or the following content slides under the pin.
  - stack: gsap
- Normalize wheel input instead of hardcoding it: read `deltaMode` (pixel, line, page), never assume `deltaY` is pixels or equals 120, and tune scroll-scene lengths on a notched mouse and a trackpad. _(support 1, intermediate, R0618)_
  - values: WheelEvent.deltaMode: DOM_DELTA_PIXEL = 0, LINE = 1, PAGE = 2; a notch is roughly 100px in some Chromium/Windows setups (not a standard).
  - do: Convert deltaY by `deltaMode` before using it in custom wheel logic, or delegate to native scrolling and CSS timelines that need no wheel listener.
  - don't: Do not encode a minimum transition length from an assumed wheel-notch distance; a notched wheel emits discrete steps and a trackpad a continuous stream of small deltas.
  - disagrees: gemini-hydrogen reports a roughly 100px Chromium notch and 3 lines in Firefox; codex found no source establishing a standard notch distance or trackpad minimum; the MDN page documents deltaMode only, so treat the pixel values as device-dependent.

## micro-interactions
- On cursor-reactive hero demos map pointer movement 1:1 immediately (feedback first), then settle with an interruptible spring or a CSS `linear()` spring. _(support 1, intermediate, R0607)_
  - values: candidate spring: stiffness ~400, damping ~30, mass 1, or duration 0.5 s with bounce 0.2; 0 ms drag-start lag.
  - do: Bind the element to pointer deltas without lag, and on release hand over to a spring that can be interrupted by the next input.
  - don't: Do not add an entrance spring to high-frequency utility triggers such as a repeatedly opened command bar; do not delay the drag start.
- Read `path.getTotalLength()` whenever nodes resize to set `stroke-dasharray` and `stroke-dashoffset` for traveling beams, and under reduced motion stop the pulse but keep the static line. _(support 1, intermediate, R0643)_
  - values: candidate: beam loop about 3 s linear.
  - do: Recompute the dash lengths in the same observer that redraws the path, and gate the beam animation on `prefers-reduced-motion: no-preference`.
  - don't: Do not hardcode dash pixel lengths in responsive diagrams; avoid infinite beams on dense diagrams of more than about 15 connectors; dash animation repaints and is not compositor-only.
- Delay the first tooltip in a group by 300ms-500ms, then open adjacent tooltips instantly while the group remains warm. _(support 1, intermediate, R0954)_
  - values: Initial delay: 300ms-500ms; warm handoff window: 300ms-400ms; subsequent tooltip delay 0ms, duration 0ms (or 100ms layout morph)
  - do: Use a warm handoff window between tooltips.
  - don't: Never make users wait 500ms and watch a fresh entrance on every icon when scanning a toolbar.
- Let swipe-to-dismiss drawers and toasts inherit release velocity and apply rubber-band resistance past their limit. _(support 1, intermediate, R0956)_
  - values: flick velocity >500 px/s OR drag >40%-50% of element height; damping factor 0.2-0.35 (distance * 0.25)
  - do: Dismiss on a velocity flick or a drag-distance threshold.
  - don't: Do not trigger destructive swipe actions on a minor flick without an undo toast or clear threshold confirmation.

## scroll-snap
- Use `scroll-snap-type: y proximity` for document-level snapping on pages with sections taller than the viewport; reserve `mandatory` for containers where every snap interval fits the scrollport. _(support 1, intermediate, R0515)_
  - values: GSAP option (stack gsap): `scroll-snap-type: y proximity`; GSAP `snap: { snapTo: 1 / (steps - 1), duration: { min: 0.2, max: 0.5 }, delay: 0.1, ease: "power1.inOut" }`. codex: proximity when free scrolling must remain; mandatory only with navigable intervals.
  - don't: Do not ship root-level snapping without a real-input test (wheel, keyboard, touch) showing the user can scroll through and out of every snap area. Do not use `proximity` on full-screen horizontal carousels (`scroll-snap-type: x mandatory`) where stopping mid-card leaves two unreadable half-slides.
  - disagrees: codex: mandatory snapping is acceptable only when every snap interval stays navigable and content cannot overflow between snap points; use proximity when users keep free scrolling; avoid mandatory only for long variable-height reading sections (MDN, Chrome, web.dev) rather than a blanket ban.
  - stack: gsap
- Option: put `scroll-snap-align` on empty sentinel elements positioned inside the track (one per rest point), not on the sticky stage: a pinned stage does not move, so it cannot offer one snap position per state. Offset each sentinel by the root `scroll-padding-top`. _(support 1, intermediate, R0516)_
  - values: `top: calc(p * (trackH - stageH) + scroll-padding-top)`; sentinel 1px high.
  - don't: Do not combine CSS snapping with a JS scrub that adds lag: two systems then settle the scroll position.
- If GSAP snapping is used, snap to explicit timeline labels or progress stops, consider directional snapping, and never prevent users from keeping their natural scroll position. _(support 1, intermediate, R0627)_
  - values: GSAP doc examples: delay 0.2 s, duration 0.2-3 s.
  - do: Define snap stops from labels in the timeline and let user interaction cancel the snap.
  - don't: Do not treat the documented delay and duration examples as universal values.
  - stack: gsap
- If a stepped pinned track needs snapping, prefer `proximity` over `mandatory`, and do not add snap to a root scroller with a sticky stage unless it was tested with a real wheel. _(support 1, intermediate, R0785)_
  - values: `y proximity`; project evidence that a root snap with a sticky stage trapped the wheel.
  - do: Use `scroll-snap-type: y proximity` on a dedicated container, test mouse wheel and trackpad separately, and keep each step no taller than the viewport.
  - don't: Do not use `mandatory` on tracks with steps taller than 100vh or for freeform drag and scrollbar users.

## pinned-scroll
- Tie scene progress to the native scroll position, keep the DOM text linear and readable, and animate only opacity and transform, so scrolling back reverses the scene and no content depends on finishing an animation. _(support 1, intermediate, R0681)_
  - values: 0 custom wheel overrides; compositor-only properties are `transform` and `opacity`.
  - do: Drive the scene with scroll or view timelines, keep headings and paragraphs as normal semantic DOM, and make every state readable even if the animation never runs.
  - don't: Do not call `preventDefault()` on wheel events, change scroll speed, or hide essential content until an animation completes; do not use this pattern for data tables or docs meant for fast skimming.
  - disagrees: the Nielsen Norman Group warns that scrolljacking removes user control, while scrollytelling practice pins scenes; reconcile by keeping native scroll physics and varying only opacity and transform.
- Do not put root-level scroll-snap on a page that also has a sticky pinned stage unless it was tested with real wheel and trackpad input, and never combine CSS snap with a smooth-scroll library or ScrollTrigger snap. _(support 1, intermediate, R0682)_
  - values: `proximity` for optional snapping; `mandatory` only when every target is reachable; project evidence: a root snap with a sticky stage trapped the wheel. v9 decision: no snap; rests are reached by step buttons that scroll to the hold centre.
  - do: Prefer a pinned scene with no snap; if snap is needed, use `proximity` on a dedicated container, keep every snap child no taller than the viewport, and use one snap controller.
  - don't: Do not use `mandatory` where a child can overflow the container, do not use `scroll-snap-stop: always` on long vertical wheel flows, and do not rely on a fixed snap duration, it is left to the browser.
  - caveat: the wheel-trap finding is project evidence, not from the MDN page; MDN documents the mandatory/proximity semantics and that snap physics is user-agent defined.
- Size pinned stages with `svh` or `dvh` units, or a JS-measured inner height, so the mobile address bar does not make the stage jump. _(support 1, intermediate, R0687)_
  - values: 100svh stage height.
  - do: Use `height: 100svh` (fallback `100vh`) on the sticky stage and re-test on a real phone browser.
  - don't: Do not size a pinned stage in plain `vh` only and assume desktop behaviour on mobile.

## scroll-timeline
- Declare `animation-timeline` and `animation-range` after the `animation` shorthand (the shorthand resets them), gate scroll-driven CSS with `@supports`, and set `animation-timeline: none` under reduced motion. _(support 1, intermediate, R0626)_
  - values: order: shorthand first, then `animation-timeline`, then `animation-range`.
  - do: Write `animation: name linear both; animation-timeline: view(); animation-range: entry 0% exit 100%;` inside an `@supports` block and a reduced-motion override.
  - don't: Do not rely on CSS timelines where you need snapping, runtime height recalculation, or coverage of browsers without scroll-driven animations and no fallback.
- Native `view()`/`scroll()` timelines also carry one sticky stage with 3 states (v7 L629-697 runs 17 nodes on one named timeline, DIRECT). Choose GSAP ScrollTrigger for snapping, several pins or older-browser coverage. _(support 1, intermediate, R0636)_
  - values: only transform and opacity animations stay off the main thread.
  - do: Build simple reveals with `animation-timeline: view()` and keep GSAP for multi-state pinned stories.
  - don't: Do not assume cross-browser support without a tested fallback switch (AR21).
  - caveat: the baseline dates reported for Safari and Firefox (late 2026) were not verified in this research.
- Use scroll(nearest block) or scroll(root block) only for continuous page-level progress indicators, and always prefer view() timelines for component-level product stories. _(support 1, intermediate, R1068)_
  - values: Page progress bar: animation-timeline: scroll(root block); Component story: view-timeline: --feature-card block; animation-timeline: --feature-card.
  - don't: Never tie a mid-page feature story to scroll(root), because dynamic content height changes (e.g. expanding an FAQ or banner above) shift all percentage keyframes out of alignment. | codex (MDN): use scroll() for scroll-position progress, view() or a named view-timeline for progress while an element crosses the scrollport; use a named timeline when the animated element is not a descendant of the controlling element; do not use view() if progress must follow a scroller other than the nearest scrollable ancestor.

## transform-registration
- Never scale or translate a container whose children must stay aligned with elements outside it; animate the items around their own centres. _(support 1, intermediate, R0591)_
  - values: transform-origin: centre of each item; no transform on the shared container.
  - do: Animate the items around their own centres so they stay registered with elements outside the container.
  - don't: Do not scale or translate a container whose children must stay aligned with outside elements.
- Use FLIP (First, Last, Invert, Play) to animate between measured layouts: measure both boxes, apply the inverse transform, then play it to identity so only transform runs. _(support 1, intermediate, R0631)_
  - values: measurement within about 100 ms of the input; playback on transform and opacity.
  - do: Capture the first box, apply the layout change, capture the last box, set the inverse transform, then transition to none; with GSAP Flip pass `absolute: true` for cross-parent moves. Measure with the element's running or scroll-driven animation neutralised in the same task; `getBoundingClientRect` includes current transforms.
  - don't: Do not run FLIP on very large DOM trees where synchronous measurement exceeds the input budget; do not use it when the box never changes.
  - caveat: codex could not verify an authoritative FLIP source; the aerotwist article and the GSAP Flip page were cited by gemini-hydrogen and were not reopened here.
  - stack: gsap
- Do not apply a non-uniform FLIP `scale(x, y)` directly to containers holding text or icons without counter-scaling an inner wrapper, or the glyphs squash and blur mid-animation. _(support 1, intermediate, R0632)_
  - values: inner scale = 1 / outer scale per axis.
  - do: Counter-scale the inner wrapper by 1/scale per axis, or animate width and height only on text-free media cards.
  - don't: Do not scale text containers; do not skip the counter-scale because the effect is short.

## view-transitions
- Wrap same-document state changes in document.startViewTransition() behind a feature check so unsupported browsers update immediately. _(support 1, intermediate, R0967)_
  - values: Chrome/Edge 111+, Safari 18.0+, Firefox 144+ (gemini)
  - do: if (!document.startViewTransition) { updateDOM(); return; }
  - don't: Do not wrap high-frequency keystrokes, drag coordinates or 60fps canvas updates in startViewTransition().
- Assign unique view-transition-name values only to the few shared hero elements that morph and share styling via view-transition-class. _(support 1, intermediate, R0969)_
  - values: ::view-transition-group(*.card-morph) { animation-duration: 250ms; animation-timing-function: cubic-bezier(0.2, 0, 0, 1); }
  - do: Typically 1-5 elements per transition.
  - don't: Never assign unique view-transition-name values to 50+ list items simultaneously.
- Constrain ::view-transition-old/new with object-fit: cover and height: 100% when morphing between containers of different aspect ratios. _(support 1, intermediate, R0970)_
  - values: height: 100%; object-fit: cover; overflow: clip
  - do: Apply overflow: clip on the snapshot.
  - don't: Do not rely on the default cross-fade scaling when morphing text blocks of different line-wrap widths.

## mask-reveal
- Add bottom padding (0.15em to 0.22em) and matching negative bottom margin to line-mask overflow wrappers so descenders on g, j, p, q, y are not sliced off by overflow: hidden. _(support 1, intermediate, R1074)_
  - values: Mask wrapper: overflow: hidden; padding-bottom: 0.18em; margin-bottom: -0.18em; display: block; Inner line element: transform: translate3d(0, 105%, 0) -> translate3d(0, 0%, 0); will-change: transform.
  - don't: Do not apply tight overflow: hidden directly to an element with line-height: 1.00 without descender padding compensation, or lowercase descenders will be visibly clipped even after the animation finishes.
- When using clip-path: inset() for single-element text or card reveals without extra wrapper divs, keep at least a non-zero initial paint or trigger synchronously via CSS @starting-style. _(support 1, intermediate, R1076)_
  - values: Starting state: clip-path: inset(0 0 100% 0); End state: clip-path: inset(0 0 -15% 0) (negative bottom inset preserves descenders); Transition: clip-path 500ms cubic-bezier(0.16, 1, 0.3, 1).
  - don't: Do not animate complex SVG polygon clip-paths with >12 vertices on large text blocks on low-end mobile devices, as path interpolation is more expensive than compositor translate3d inside an overflow: hidden mask.

## optimistic-ui
- Update the UI immediately on tap (optimistic update) and save in the background instead of waiting for the server. _(support 1, intermediate, R0007)_
  - values: waits for server 730 ms vs optimistic 16 ms
  - do: setLiked(); save()
  - don't: await save(); setLiked() with a pending spinner
- Use optimistic UI: update the interface instantly on tap and save in the background instead of waiting for the server. _(support 1, intermediate, R0195)_
  - values: waits for server 730 ms vs optimistic 16 ms
  - do: setLiked(); save()
  - don't: await save(); setLiked()

## scroll-driven
- Compute scroll-scene offsets as functions of live layout (function-based `start`/`end`/tween values with `invalidateOnRefresh`) and call `ScrollTrigger.refresh()` after fonts or lazy images change the document height. _(support 1, intermediate, R0628)_
  - values: ScrollTrigger calculates on creation and resize; function-based values and `refresh()` recompute.
  - do: Write `end: () => "+=" + container.clientWidth * 0.5` with `invalidateOnRefresh: true`, and refresh on `document.fonts.ready` and image load.
  - don't: Do not assume positions update continuously after arbitrary DOM changes; do not mix a one-time mount measurement with static `calc()` offsets.
  - stack: gsap
- Wrap breakpoint-specific scroll scenes and reduced-motion variants in `gsap.matchMedia()` so unmatched queries revert pins and inline styles automatically. _(support 1, intermediate, R0629)_
  - values: GSAP 3.11+ matchMedia reverts on mismatch.
  - do: Create one `matchMedia` context per breakpoint and put the pin and its tweens inside it.
  - don't: Do not use it for animations a plain CSS `@media` query can control.
  - stack: gsap

## scroll-reveal-range
- Start scroll-driven entrance animations after the element is 10-20% inside the viewport and finish them no later than `entry 100%` (tall elements: `entry 40-50%`), so nothing stays half-faded while fully in view. _(support 1, intermediate, R0531)_
  - values: `animation-range: entry 15% entry 100%`.
  - don't: Do not apply positive bottom `view-timeline-inset` to elements located at the very bottom of the page (such as the footer CTA), because the scroll container may reach maximum scroll before the element can travel 35% up the viewport, leaving the element permanently stuck at partial opacity.
- Overlap the outgoing and incoming states of a pinned scroll scene across adjacent windows instead of switching all content at one threshold, so combined presence never drops to a blank frame. _(support 1, intermediate, R0619)_
  - values: text panels on a shared surface: the incoming fade starts at 55-65% of the outgoing window (v8: out 24-33%, in 29.5-38.5%). Near-total overlap (0.30-0.50 / 0.28-0.48) only for image layers. No source gives a best overlap.
  - do: Start the incoming fade (or place it with GSAP position `"<"` / overlapping `animation-range` phases) slightly before the outgoing fade ends, and give each transition its own range.
  - don't: Do not run fade-out and then fade-in sequentially; do not use a single-threshold class toggle for a scrubbed scene; keep deliberate dip-to-black only between unrelated acts.

## try-it-performance
- Keep the try-it hero LCP at or under 2.5 s by making an eligible element (text, image or video poster) discoverable in the initial HTML, and never rely on an empty canvas for LCP. _(support 1, intermediate, R0697)_
  - values: LCP good <= 2.5 s, poor > 4.0 s (p75); canvas is not an eligible LCP element type.
  - do: Render a server-side shell with the H1 and a static image or poster, preload it with `fetchpriority="high"` only when late-discovered, and keep the demo widget hydrating after first paint.
  - don't: Do not lazy-load the hero media, do not give `fetchpriority="high"` to more than one or two images, and do not block first paint on file parsers or model weights.
- Load heavy interactive engines (WASM, Web Audio, Rive) only on pointer enter, focus or click on the demo. _(support 1, intermediate, R0698)_
  - values: zero engine bytes before the first interaction.
  - do: Dynamic-import the engine in the first interaction handler and show a ready static state until then.
  - don't: Do not bundle the engine into the initial script path.

## compositor-only
- Animate hero and stand-out elements with transform and opacity, wrap a complex inline SVG in a div and animate the wrapper, and do not assume even these are free. _(support 1, intermediate, R0767)_
  - values: frame budget about 16.6 ms at 60 fps; no element-count or duration limit in the sources.
  - do: Animate `transform` and `opacity` on the wrapper, apply `will-change` only to the one element that animates now, and measure layer memory.
  - don't: Do not animate internal SVG geometry attributes every frame, do not leave `will-change` on dozens of idle cards, and do not claim a transform alone protects the LCP.

## durations
- Keep interactive hero transitions (toggles, popovers, state switches) under about 300 ms, with an ease-out for entrances and exits and ease-in-out only for on-screen repositioning. _(support 1, intermediate, R0606)_
  - values: candidate curves: ease-out `cubic-bezier(0.23, 1, 0.32, 1)`, ease-in-out `cubic-bezier(0.77, 0, 0.175, 1)`, drawer `cubic-bezier(0.32, 0.72, 0, 1)`; source is a practitioner guide, not a standard.
  - do: Start from tokens: press feedback 100-160 ms, tooltips and popovers 125-200 ms, dropdowns 150-250 ms, standard transitions 200-300 ms (drawers up to 500 ms) and tune by feel.
  - don't: Do not use ease-in for UI feedback; do not apply this cap to ambient backgrounds or long narrative transitions that do not block input.
  - disagrees: practitioners disagree on hero flair: one camp favours whimsical, memorable CSS flourishes, another (Rauno Freiberg, Paco Coursey) strips motion from high-frequency triggers to 0 ms; do not mix bouncy hero physics with tools whose brand promise is speed.

## edge-fade
- Mask incoming and outgoing scrolling text columns inside a pinned stage using alpha gradient masks (`mask-image: linear-gradient(to bottom, transparent 0%, #000 12%, #000 88%, transparent 100%)` or explicit `40px-64px` pixel stops) on the overflow wrapper so lines dissolve instead of being cut mid-glyph; make the fade at least one line-height tall. On a flat background a gradient overlay in the background colour is equivalent and cheaper; use `mask-image` when the background is textured. _(support 1, intermediate, R0519)_
  - values: Top fade stop `0px` to `48px` (or `0%` to `12%`), full opacity zone `48px` to `calc(100% - 48px)` (`12%` to `88%`), bottom fade stop `calc(100% - 48px)` to `100%` (`88%` to `100%`). Label 48px / 12% are starting points.
  - don't: Do not apply `mask-image` to a parent container that wraps a `position: sticky` child or a visible native scrollbar, as `mask-image` creates a stacking context and clips/fades the scrollbar thumb in WebKit/Blink; apply the mask strictly to the inner scrolling text track.
  - disagrees: codex: no source establishes a standard gradient-mask value or guarantees a mask prevents half-cut text; the 48px mask boundary is unvalidated.

## fab-expand
- Have a FAB expand its secondary actions out of the button itself and turn its + into an X to close. _(support 1, intermediate, R0169)_
  - don't: Actions stacked statically above a grey + button

## firefox-fallback
- Guard scroll-driven rules with `@supports (animation-timeline: view())`, check current Firefox support at release rather than relying on an old claim, and make the unsupported branch a readable static layout. _(support 1, intermediate, R0783)_
  - values: MDN compat data retrieved on 2026-11-23 lists Firefox support from version 159; gemini says October 2026, not verified; support changes over time. Version comes from compat data; release status not verified. Test the installed build with `CSS.supports`; gate by feature only.
  - do: Put every `opacity: 0` or pinned base style inside the `@supports` block, and let the fallback show `opacity: 1; transform: none`.
  - don't: Do not ship `opacity: 0` base styles outside the `@supports` block, and do not assert that Firefox categorically lacks support.

## frequency-of-use
- Animate frequently used interactions less; reserve exaggerated feedback for rare, meaningful moments. _(support 1, intermediate, R0366)_

## hero-media
- Replace large animated GIFs with muted looping video with a poster, or a state-machine vector canvas, plus a static reduced-motion fallback. _(support 1, intermediate, R0707)_
  - values: no byte budget verified.
  - do: Use `<video autoplay muted loop playsinline poster>` or a vector runtime, pause it off screen, and show the poster when motion is reduced.
  - don't: Do not ship a multi-megabyte GIF, and do not run WebGL or vector loops off screen or on low-spec devices unpaused.

## interruptible-motion
- Make animations interruptible so users can change their mind mid-animation, and add rubber-banding when pulling past the end of a scroll. _(support 1, intermediate, R0365)_

## like-feedback
- Animate a like (fill, scale, ripple) instead of swapping the heart state in a single frame. _(support 1, intermediate, R0182)_
  - don't: Heart that 'swaps in 1 frame'

## loading-states
- Loading buttons show a spinner while keeping the original label; delay spinners/skeletons ~150-300ms and keep them visible ~300-500ms minimum to avoid flicker. _(support 1, intermediate, R0451)_
  - values: show-delay ~150-300ms; min visible ~300-500ms

## media-loading
- Defer demo media bytes until the visitor asks: use a poster for video, `preload="none"` or `"metadata"`, and create audio contexts only in the click handler. _(support 1, intermediate, R0703)_
  - values: `preload="none"` or `"metadata"`.
  - do: Show a poster, set `preload="none"` on hero audio, keep controls on a lazy audio element, and preload the poster with `fetchpriority="high"` only when it is the LCP element.
  - don't: Do not preload a non-LCP poster that competes with critical resources.

## milestone-celebration
- Celebrate onboarding milestones with a micro-interaction (e.g. confetti) and a subtle badge so completion feels rewarding. _(support 1, intermediate, R0469)_
  - do: 'Congratulations, your profile is ready!' screen with confetti and an 'Approved' badge

## motif
- Keep section transitions legible and motion optional; scrolling backward must not leave content hidden or half-finished. _(support 1, intermediate, R0730)_
  - values: scroll-driven progress follows scroll position.
  - do: Make each section readable without its transition and respect the reduced-motion query.
  - don't: Do not make animation completion a prerequisite for reading.

## motion-cues
- Use motion cues (arrows, lines, indicators and micro-animations) to guide the visitor's reading path through the page. _(support 1, intermediate, R0495)_

## motion-durations
- Define motion tokens for duration, easing, delays and enter/exit animations; use motion to explain change, not to decorate. _(support 1, intermediate, R0424)_
  - values: Fast 150ms, Normal 250ms, Slow 400ms

## motion-tokens
- Pair directional easing tokens with duration tiers in token aliases: decelerate for entering, accelerate for exiting, standard/emphasized for on-screen movement. _(support 1, intermediate, R0938)_
  - values: M3: easing-emphasized (use M3 standard curve cubic-bezier(0.2, 0, 0, 1) as fallback for emphasized), easing-emphasized-decelerate=cubic-bezier(0.05, 0.7, 0.1, 1), easing-emphasized-accelerate=cubic-bezier(0.3, 0, 0.8, 0.15)
  - do: Bundle easing and duration into one semantic alias per motion role.
  - don't: Do not pair an exit acceleration token with a long duration (>300ms), which makes dismissed UI feel unresponsive.

## origin-awareness
- Make popovers and expanding elements grow out of the point where the user clicked or tapped, not from an arbitrary center. _(support 1, intermediate, R0363)_

## rating-microinteraction
- Animate the selected rating star with a scale pop and sparkle, filling stars in accent colour up to the selection. _(support 1, intermediate, R0168)_

## scroll-depth
- Drive scroll-linked depth effects with transform and opacity through CSS scroll timelines, keep them modest, and do not animate every decorative property. _(support 1, intermediate, R0728)_
  - values: no verified frame-budget threshold in the cited page.
  - do: Use `animation-timeline` on opacity and translate for a few layers and measure it.
  - don't: Do not assume CSS alone guarantees smooth performance, and do not scroll-animate every layer.

## scroll-reveal
- Don't fade-in every section on scroll; reserve one orchestrated motion moment and trigger other motion on user action. _(support 1, intermediate, R0299)_
  - don't: scroll fade-ins on everything

## scroll-synced-index
- Highlight the item in a project list or index that matches the content currently in view so users always know where they are. _(support 1, intermediate, R0374)_

## scrub-timeline
- When ScrollTrigger runs on top of a smooth-scroll library such as Lenis, use `scrub: true` (or a very small value), sync the library to `ScrollTrigger.update`, drive it from `gsap.ticker`, and set `lagSmoothing(0)`. _(support 1, intermediate, R0625)_
  - values: native scroll: scrub 0.5-1.0; with Lenis: `scrub: true` or 0.1-0.2, lerp about 0.1.
  - do: Wire `lenis.on("scroll", ScrollTrigger.update)` and `gsap.ticker.add` for `lenis.raf`, then use `scrub: true`.
  - don't: Do not combine a numeric `scrub` of about 1 with smooth-scroll lag; the two smoothings stack into a rubber-band feel.
  - disagrees: numeric scrub smooths wheel notches on native scroll (documented), while with a smooth-scroll library it double-lags; the Lenis-specific values come from practitioner usage, not the GSAP page.
  - stack: gsap

## shared-element-transition
- When a card opens a detail page, morph the card into the page (shared-element transition) and reverse it on back, so nothing just appears without an origin. _(support 1, intermediate, R0179)_
  - don't: Hard cut that makes users find their place again

## skeleton-loaders
- Use skeleton loaders instead of spinners and optimistic updates (UI changes before the server answers) in app UIs. _(support 1, intermediate, R0055)_

## spring-motion
- use a CSS `linear()` spring for follow-through on the hero focal object and switch it off under reduced motion. _(support 1, intermediate, R0768)_
  - values: reduced-motion override of about 0.01 ms is a gemini pattern; MDN notes scaling and panning large objects can trigger vestibular symptoms but gives no safe threshold.
  - do: Store the curve in a custom property, gate continuous sweeps, 3D folds and springs in `@media (prefers-reduced-motion: no-preference)`, and show the static end state under `reduce`.
  - don't: Do not rely on cascade order alone to fall back a custom property, an unsupported `linear()` still parses as a valid custom property.
  - scope: time-based transitions only; not for scrubbed scenes.

## staggering
- Stagger list and group entrances one after another instead of animating all items in unison, and use slow-in/slow-out easing so motion is less robotic. _(support 1, intermediate, R0364)_

## success-feedback
- Confirm a completed action by turning the button into a coloured success state with a checkmark and a small celebratory burst. _(support 1, intermediate, R0166)_
  - don't: Grey button with no feedback after submit

## testable-fallback
- Feature-gated motion ships with a switch (for example `?sda=off`) that forces the fallback path in the same browser. _(support 1, intermediate, R0599)_
  - values: Query switch such as `?sda=off`.
  - do: Ship a switch (for example `?sda=off`) that forces the fallback path of feature-gated motion in the same browser.
  - don't: Do not ship a fallback that can only be tested in another browser.

## will-change
- Use `will-change` only on the one or two elements that are animating right now, and never on whole card lists. _(support 1, intermediate, R0725)_
  - values: no numeric limit verified.
  - do: Add it just before the animation and remove it after, on the specific layer.
  - don't: Do not set `will-change: transform` or `backdrop-filter` globally, it holds layers in GPU memory.

## Checklist

- [ ] Animate hero and stand-out elements with transform and opacity, wrap a complex inline SVG in a div and animate the wrapper, and do not assume even these are free.
- [ ] Keep interactive hero transitions (toggles, popovers, state switches) under about 300 ms, with an ease-out for entrances and exits and ease-in-out only for on-screen repositioning.
- [ ] For time-triggered UI animation use the project's motion tokens instead of the browser default `ease`: an ease-out for entrances, an ease-in-out for elements that move across the screen; do not add a new curve when a token exists.
- [ ] Never animate elements from `scale(0)` in scroll reveals or UI popovers; start entrances from `scale(0.95)` or `scale(0.97)` paired with `opacity: 0` and `translateY(8px-16px)`.
- [ ] Keep separate productive (task efficiency) and expressive (brand/emphasis) easing curves and do not treat them as interchangeable.
- [ ] Use an asymmetric ease-in-out curve for elements moving or morphing while remaining visible on screen.
- [ ] Use springs for spatial, gesture-driven or interruptible motion and time-based easing curves for short non-spatial effects such as opacity.
- [ ] Configure physics springs through physical parameters and let settling time follow the spring instead of specifying a fixed duration as its defining parameter.
