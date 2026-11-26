---
id: advanced-motion
title: Advanced motion
tier_min: T4
page_types: ["any"]
topics: ["pinned-scroll-story", "scroll-driven", "scroll-timeline", "crossfade-budget", "decode-in-text", "decode-text", "drag-to-reveal", "kinetic-headlines", "layout-derived-offsets", "loading-animations", "mask-reveal", "motion-story", "one-standout-moment", "one-state-at-a-time", "pinned-handoff"]
rules: 31
---

# Advanced motion

Scroll storytelling, complex animation.
Support = number of independent educators who teach the rule.

## pinned-scroll-story
- When using CSS `view-timeline` on a tall sticky track (`height: 400vh`), bind the timeline to the outer track wrapper (`view-timeline-name: --pin-track; view-timeline-axis: block;`) and constrain the active animation window to `animation-range: contain 0% contain 100%`, because the inner `position: sticky` stage is pinned precisely while the outer track is *contained* within the viewport (`entry 100%` equals `contain 0%`, and `exit 0%` equals `contain 100%`). This holds for a stage with `top: 0`; with `top: var(--header-height)` add a matching `view-timeline-inset` or the pin starts before `contain 0%`. _(support 1, advanced, R0569)_
  - values: `view-timeline: --pin-track block; animation-timeline: --pin-track; animation-range: contain 0% contain 100%;` (maps `0%` keyframe to the exact pixel the stage locks at `top: var(--header-height)` and `100%` keyframe to the exact pixel the stage unpins).
  - don't: Do not use the default `animation-range: cover 0% cover 100%` for a pinned stage's internal step transitions, because `cover 0%` starts animating as soon as the top border of the track enters the *bottom* of the viewport— wasting the first `100vh` of keyframes before the stage has even reached the sticky lock point at the top of the screen!
  - group: pinned-scene-always-on
- Pre-allocate GPU composite layers on pinned stage elements that scrub continuously (`will-change: transform, opacity`) only while the parent track intersects the viewport (or via CSS `@keyframes` that hold `transform: translate3d(0,0,0)`), and restrict scrubbed properties strictly to compositor-only properties (`transform`, `opacity`). _(support 1, advanced, R0570)_
  - values: `0` main-thread layout/paint properties (`top`, `left`, `width`, `height`, `margin`, `border-width`) animated during scroll scrub; `100%` compositor-thread properties (`transform: translate3d/scale`, `opacity`).
  - don't: Never leave `will-change: transform, opacity` permanently declared on many large offscreen cards across an entire landing page, which exhausts GPU texture memory.
- Build pinned scroll-driven product stories by pairing an outer tall track container (250vh-400vh) with an inner position: sticky stage (height: 100dvh; top: 0) driven by view-timeline-name on the outer track. _(support 1, advanced, R1060)_
  - values: Outer track: height: 300vh; view-timeline-name: --story-track; view-timeline-axis: block; Inner stage: position: sticky; top: 0; height: 100dvh; overflow: hidden; Child animation: animation-timeline: --story-track; animation-range: contain 0% contain 100%; animation-fill-mode: both; animation-timing-function: linear.
  - don't: Never apply overflow: hidden, overflow: auto, or overflow: scroll to any ancestor between the sticky stage and the scroll container, which silently breaks position: sticky.

## scroll-driven
- Treat scroll-driven animation support as browser-version-dependent and do not claim support for older versions. _(support 1, advanced, R0961)_
  - values: animation-timeline, scroll(), view(): Chrome 115, Firefox 159, Safari 26
  - do: Check compatibility data at build time.
  - don't: Do not assume support in older Safari/Firefox.
- Use CSS animation-timeline: scroll() for reading progress bars and scroll-linked headers instead of main-thread JavaScript scroll listeners. _(support 1, advanced, R0963)_
  - values: Chrome/Edge 115+, Safari 26+, Firefox 159+
  - do: animation-timeline: scroll(nearest block) or scroll(root).
  - don't: Do not ship without @supports progressive enhancement when legacy parity is mandatory.
- Pair animation-timeline: view() with a constrained animation-range so reveals complete before the element reaches 30%-50% viewport height. _(support 1, advanced, R0964)_
  - values: animation-range: entry 0% cover 30% (or entry 10% entry 90%); animation-fill-mode: both
  - do: animation-range: entry 0% cover 30%.
  - don't: Never tie essential reading copy opacity to cover 0% cover 100%.

## scroll-timeline
- Use timeline-scope on a shared ancestor container to hoist named view-timelines so sibling sticky product mockups can react to scrolling text steps in an adjacent column. _(support 1, advanced, R1061)_
  - values: Shared parent: timeline-scope: --step-1, --step-2, --step-3; Scrolling step items: view-timeline-name: --step-1 (etc.); Sticky UI visual layers: animation-timeline: --step-2; animation-range: entry 20% cover 50%.
  - don't: Do not declare duplicate view-timeline-name identifiers within the same timeline-scope subtree without unique naming, as name collisions invalidate timeline resolution. | codex (MDN): timeline-scope hoists a NAMED timeline to a common ancestor; it does not work with anonymous timelines created by view() or scroll().
- Use animation-range: contain 0% contain 100% for animations that must run strictly while the sticky container is pinned, and exit-crossing 0% exit-crossing 100% for stacking cards that scale down as the next card overlaps. _(support 1, advanced, R1062)_
  - values: Pinned range: animation-range: contain 0% contain 100%; Stacking card scale-down: position: sticky; top: 32px; transform: scale(0.92); opacity: 0.65; animation-range: exit-crossing 0% exit-crossing 100%.
  - don't: Do not use default animation-range: cover 0% cover 100% on pinned sticky tracks, because the first 100vh of entry motion executes before the element even sticks to top: 0.

## crossfade-budget
- Crossfade budget, floors in px only: each fade at least 100px; the incoming fade starts at 55-65% of the outgoing one; the handoff at least 170px. A handoff is out, gap, in. Replaces the AR12 4% / 40% numbers. _(support 1, advanced, R0666)_
  - values: px by viewport height: 800 -> fade 108 / overlap 42 / handoff 174; 900 -> 122 / 47 / 196; 1080 -> 146 / 57 / 235 (UNPROVEN until scored; superseded in part by the AR47 v10 table); floors fade >= 100, handoff >= 170.
  - do: Size each fade to >= 100px, start the incoming fade at 55-65% of the outgoing one and keep handoffs >= 170px (out, gap, in).
  - don't: Do not use fades of about 31px or fully still plateaus: v7 scored them as cuts and dead stretches.
  - caveat: amended 2026-11-23 (iter4): the '8% of the pin' and scroll-tied drift clauses were removed; amended (iter5): the 'max opacity >= .35' floor was removed because a handoff is out, gap, in.

## decode-in-text
- For premium portfolio/agency sites, reveal headlines by scrambling through random characters before they settle so text feels revealed, not loaded. _(support 1, advanced, R0456)_
  - do: locomotive.ca

## decode-text
- Reveal headlines with a decode effect that scrambles through random characters before settling. _(support 1, advanced, R0378)_

## drag-to-reveal
- Use an interactive 'drag to reveal' control on a hero image to invite exploration of the product. _(support 1, advanced, R0002)_
  - do: Circular drag handle with label 'DRAG TO REVEAL' over a covered car

## kinetic-headlines
- Split headlines into letters and words that animate on load and hover instead of static headings. _(support 1, advanced, R0360)_

## layout-derived-offsets
- No token holds a distance measured at one viewport (DIRECT failure v7 L50 vs L675). Derive travel from CSS percentages of a shared container, or from JS measured on load, `fonts.ready` and ResizeObserver with animations neutralised. _(support 1, advanced, R0667)_
  - values: Re-measure on load, fonts.ready and ResizeObserver.
  - do: Compute travel from percentages of a shared container, or measure it in JS on load, `fonts.ready` and ResizeObserver with running animations neutralised.
  - don't: Do not store a pixel distance measured at one viewport in a token.

## loading-animations
- Use a branded loader animation (dot ring, triple arc, Newton's cradle) for page or content loading on expressive sites. _(support 1, advanced, R0049)_

## mask-reveal
- Revert split-text DOM wrappers back to a single plain text node on animationend (or ResizeObserver width change) so native user text selection, copy-paste, and browser Find-in-Page (Ctrl+F) work seamlessly. _(support 1, advanced, R1078)_
  - values: Event hook: element.addEventListener('animationend', () => revertToPlainText()); Resize debounce: 150ms.
  - don't: Do not leave hundreds of nested line/word/char <span> wrappers permanently in the DOM after a one-time entrance reveal if the animation will not replay.

## motion-story
- Tell the product story twice: once as a one-shot hero sequence (<= 2.5 s, then rest, with a Replay control) and once scrubbed on scroll, with reveals complete by entry 100%. _(support 1, advanced, R1126)_
  - values: Hero sequence <= 2.5 s then rest, Replay control present; scroll-scrubbed story; reveals complete by entry 100%; infinite loops: 0.
  - don't: Do not use infinite decorative loops.

## one-standout-moment
- One stand-out moment: one physical change of the product's own data unit; reserved zone, no text in its sweep; transform and opacity only; frame 0 at scrollY 0 and the last frame are clean rests; the last frame is the static, no-SDA and reduced-motion frame. _(support 1, advanced, R0802)_
  - values: Last frame = static, no-SDA and reduced-motion frame.
  - do: Give the page one physical change of its own data unit in a reserved zone, animate transform and opacity only, and make frame 0 and the last frame clean rests.
  - don't: Do not put text inside the sweep or add a second stand-out moment.

## one-state-at-a-time
- One state at a time (DIRECT, L854-859): the outgoing state reaches 0 before the incoming leaves 0; gap >= 16px; each fade >= 100px; hold >= handoff. _(support 1, advanced, R0798)_
  - values: Gap >= 16px; fade >= 100px; hold >= handoff. See manual-motion-tokens for the v10 table.
  - do: Bring the outgoing state to opacity 0 before the incoming one leaves 0, with a gap of at least 16px, fades of at least 100px and a hold at least as long as the handoff.
  - don't: Do not let two states have positive opacity at the same scroll position.

## pinned-handoff
- Drive child states from a view timeline on the tall outer track, with a sticky inner stage, instead of a scroll timeline on the sticky child, and always ship a readable static state. _(support 1, advanced, R0759)_
  - values: outer track about 300vh-400vh is a gemini example (test per content); range `contain 0%` to `contain 100%`; codex: no recommended duration, declaration order matters.
  - do: Put `view-timeline` on the outer track and `position: sticky; top: 0; height: 100svh` on the stage, drive children with `animation-timeline: --name` and `animation-range: contain 0% contain 100%`, and keep the `animation-timeline` declaration after the `animation` shorthand.
  - don't: Do not put `animation-timeline` before the `animation` shorthand, it gets reset; do not make the animated version the only presentation of the content.
  - caveat: gemini sources were not opened here, so the numbers are unverified; the MDN property page documents the shorthand order and timeline syntax; the sticky-parent view timeline pattern and the 300-400vh track are from gemini.

## pinned-scroll
- If a pinned stage flashes unpinned content during fast scrolling, tune `anticipatePin` against real scroll velocity instead of copying a value. _(support 1, advanced, R0684)_
  - values: GSAP documents the flash as about one frame (about 1/60 s); it gives no universal hold in vh or px.
  - do: Test fast wheel flicks and touch flings, enable `anticipatePin` only if the flash appears, and tune it on the target devices.
  - don't: Do not treat one `anticipatePin` value as a universal design constant.
  - caveat: not applicable to native CSS scroll timelines.
  - stack: gsap

## pinned-stage-composition
- The pinned stage owns its label, h2 and step counter. Panel height derives from the viewport (clamp), never a fixed px. Every state fills the panel with one elastic region; a persistent shell stays while contents swap. _(support 1, advanced, R0589)_
  - values: Panel height: clamp(min, viewport-derived, max); one shell, swapped contents.
  - do: Put label, h2 and step counter inside the pinned stage, derive panel height from the viewport with clamp(), fill every state with one elastic region and keep one persistent shell.
  - don't: Do not give the panel a fixed px height or let the heading scroll away before the stage locks.

## rest-states
- Rest states (DIRECT failures v8 L774-779, L785-786): each state = build, hold, handoff; hold >= 200px and >= its handoff; nothing inside the frame changes in a hold; chrome never translates inside a static shell; no drift. _(support 1, advanced, R0747)_
  - values: Hold >= 200px and >= handoff; see manual-motion-tokens for the v9 px table.
  - do: Give each state a build, a hold of at least 200px (and at least its handoff) and a handoff, and keep everything in the frame still during the hold.
  - don't: Do not translate chrome inside a static shell and do not add scroll-tied drift to a hold.

## scroll-effects
- Use scroll effects such as layered parallax, pinned heading, 3D card zoom and line-by-line text reveal for immersive pages. _(support 1, advanced, R0050)_
  - do: Reference: lusion.co

## scroll-storytelling
- Use scroll-driven animation and dynamic content to tell a story section by section. _(support 1, advanced, R0325)_

## scroll-transitions
- Use scroll-driven section transitions (e.g. the page tearing open, headline changing color across the edge) so the story moves with the reader; every interaction must have a job, not decoration. _(support 1, advanced, R0375)_

## scrub-easing
- Apply distance-normalised mapping on GSAP horizontal scroll pins by setting the vertical `end` distance equal to the exact horizontal overflow pixel delta (`end: () => "+=" + (track.scrollWidth - window.innerWidth)`) with `invalidateOnRefresh: true` and `ease: "none"`, ensuring 1px of vertical scroll equals exactly 1px of horizontal travel at all viewport widths. _(support 1, advanced, R0572)_
  - values: Ratio `1:1` (`1px` vertical scroll = `1px` horizontal translation); `ease: "none"`; `invalidateOnRefresh: true`.
  - don't: Do not hardcode `end: "+=3000px"` on a horizontal scroll pin when the horizontal track width varies between `1440px` and `768px` viewports, as doing so changes the effective scroll speed and creates dead scroll padding after the last card arrives.
  - stack: gsap

## scrub-timeline
- Linear root with per-keyframe easing. Per state: own animation, then a rest plateau where nothing changes; transition and plateau sizes follow AR24. Spatial mappings (rail, playhead) stay linear; no overshoot. Snap targets, if any, sit at plateau centres with proximity only, and ship only after a real-input test. _(support 1, advanced, R0590)_
  - values: Fade, overlap, handoff and hold budgets per AR24; snap points at plateau centres (22 / 55.5 / 93% in the v7 brief); proximity only.
  - do: Build the scrub timeline with a linear root, per-keyframe easing and a rest plateau per state sized by AR24; place any snap targets at plateau centres with proximity only.
  - don't: Do not use overshoot or non-linear easing on spatial mappings (rail, playhead), and do not ship snapping without a real-input test.
  - caveat: amended 2026-11-23: the earlier 'transition of 4% or less' and 'plateau of at least 40%' produced v7 defect 8 (DIRECT) and were removed.

## scrubbable-demo-contract
- Scrubbable demo contract: one state variable and one render function; the HTML is the final frame; a head-script class sets the pre-state so the final frame never flashes; autoplay once in under 4 s; input cancels it; leaving returns to the final frame; keyboard through a native range; nothing focusable is ever invisible. _(support 1, advanced, R0665)_
  - values: Autoplay once, < 4 s; native range input; one state variable.
  - do: Drive the demo from one state variable and one render function, ship the final frame as HTML, set the pre-state with a head-script class, autoplay once under 4 s, cancel on input, return to the final frame on leave, and expose a native range for the keyboard.
  - don't: Do not let the final frame flash before the pre-state applies, loop the autoplay, or leave a focusable element invisible.

## smooth-scroll
- Use inertial smooth scroll on immersive pages to keep WebGL and typography animation in sync. _(support 1, advanced, R0359)_

## text-animations
- Pick a named text-reveal animation for headlines instead of a generic fade. _(support 1, advanced, R0048)_
  - do: Typewriter, mask rise, scramble decode, gradient flow, word stagger, blur in, letter bounce

## Checklist

- [ ] Crossfade budget, floors in px only: each fade at least 100px; the incoming fade starts at 55-65% of the outgoing one; the handoff at least 170px. A handoff is out, gap, in. Replaces the AR12 4% / 40% numbers.
- [ ] For premium portfolio/agency sites, reveal headlines by scrambling through random characters before they settle so text feels revealed, not loaded.
- [ ] Reveal headlines with a decode effect that scrambles through random characters before settling.
- [ ] Use an interactive 'drag to reveal' control on a hero image to invite exploration of the product.
- [ ] Split headlines into letters and words that animate on load and hover instead of static headings.
- [ ] No token holds a distance measured at one viewport (DIRECT failure v7 L50 vs L675). Derive travel from CSS percentages of a shared container, or from JS measured on load, `fonts.ready` and ResizeObserver with animations neutralised.
- [ ] Use a branded loader animation (dot ring, triple arc, Newton's cradle) for page or content loading on expressive sites.
- [ ] Revert split-text DOM wrappers back to a single plain text node on animationend (or ResizeObserver width change) so native user text selection, copy-paste, and browser Find-in-Page (Ctrl+F) work seamlessly.
