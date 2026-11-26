---
id: manual-motion-tokens
title: Motion tokens and motion budget per tier (skill default)
tier_min: T1
page_types: ["any"]
topics: ["motion-tokens", "durations", "easing", "stagger", "distance", "reduced-motion", "motion-budget"]
provenance: Reconciled 2026-11-23 by the fable-consultant advisor from codex + gemini-argon web research
  (Carbon, Material 3, Apple, practitioners). Values are skill CONVENTIONS, not sourced facts.
  Core 150/250/400 matches existing rule R0424.
---

# Motion tokens (use these, never literal ms or curves in components)

| Token | Value | Use | Tier |
|---|---|---|---|
| dur-instant | 0ms | keyboard navigation, warm tooltips, command palette | all |
| dur-micro | 100ms | press, hover colour, toggle | all |
| dur-fast | 150ms | tooltip, small fade, exits of small overlays | all |
| dur-base | 250ms | popover/modal enter, reveal, route transition | all |
| dur-slow | 400ms | drawer, sheet, large surface, hover-leave on decorative cards | all |
| dur-expressive | 600ms (ambient up to 1000ms) | hero and text reveals, backdrop | T2 hero only, T4 |
| ease-standard | cubic-bezier(0.2, 0, 0, 1) | on-screen move or morph (default) | all |
| ease-enter | cubic-bezier(0, 0, 0.3, 1) | entering elements | all |
| ease-exit | cubic-bezier(0.3, 0, 1, 1) | permanent exits only | all |
| ease-expressive | cubic-bezier(0.16, 1, 0.3, 1) | reveals of 400ms or more | T2 hero, T4 |
| linear | linear | progress, loops, opacity-only | all |
| spring | 0.35s, bounce 0 (physical 400/30/1); bounce up to 0.2 only at T4 | gestures, drag release, layout | T3+ |
| stagger | 40ms per item (cards 60-80ms), first 6-8 items only, 400ms total cap | lists and grids | T2+ |
| text stagger | 20ms per character (<=30 chars), 60ms per line | headlines | T4 |
| distance | tooltip 4px, reveal 12px, section/route 16-24px, shared axis 30px, hover lift -2 to -4px | | all |
| scale | press 0.97 (large surfaces 0.98-0.99), overlay enter 0.95 | | all |
| travel cap | 64px; parallax +/-40px | | T4 |
| reduced motion | spatial motion becomes a 150ms opacity fade; stagger 0; parallax and scrubbing off | | all |

Exit = one duration step down from the matching enter (250 enter -> 150 exit).

## Motion budget per tier (advisor recommendation)
- T1/T2: one orchestrated moment per viewport (usually the hero, finished within 600-800ms, starting within 100ms
  of first paint, never replayed on back/forward). Everything else <= 250ms and <= 16px travel. <= ~3 KB of motion JS.
  CSS + IntersectionObserver only; no GSAP/Motion/Lottie/smooth-scroll libraries.
- T3: tokens above + FLIP/layout transitions, swipe/drag springs, same- and cross-document View Transitions.
- T4: pinning (max two pinned sections per page, each <= 200vh, keyboard and scrollbar keep working), scrubbed
  scroll-driven sequences, parallax, text effects, libraries when justified by a measured need.

## Rules (advisor recommendation, not sourced)
- Apply hidden initial states only after script or @supports confirmed the animation can run; add the class from JS
  and keep a 2-3s failsafe that reveals everything. Content must be readable with JS off.
- Reveal = opacity + 8-16px translate (24px max). Never >= 40px or scale < 0.9 below T4. Animate only content that
  is below the fold at load; never run entrance animations on hydration of stateful components.
- Never alter native scroll speed or direction below T4.
- Pause every loop, video, canvas and Lottie when off-screen and on visibilitychange. No infinite loops in content
  areas except loaders; decorative loops >= 6s with low amplitude.
- Never animate box-shadow, filter: blur or backdrop-filter values directly; cross-fade a pre-rendered pseudo-element.
  Verify at 4x CPU throttle and cut these first if frames drop.
- Same element type always enters the same way. Disable transitions during theme switch and window resize.
- T2 default reveal mechanism: IntersectionObserver-triggered, time-based, once-only (works in every browser).
  Keep CSS scroll()/view() timelines for progress bars, header shrink and T4, always behind @supports.
- Browser support (advisor belief 2026-11; verify before relying): scroll-driven animations in Chromium 115+ and
  Safari 26, treat Firefox as unsupported; same-document View Transitions Chromium 111+, Safari 18, Firefox 144;
  cross-document View Transitions Chromium 126+, Safari 18.2, not Firefox. Always feature-detect.

## Checklist
- [ ] Only tokens from the table above; no literal ms/curves in components.
- [ ] Tier budget respected (T1/T2: one orchestrated moment, no motion libraries).
- [ ] prefers-reduced-motion: spatial motion -> 150ms fade, stagger 0, no parallax/scrub.
- [ ] Hidden states only after JS/@supports confirmation, with a failsafe.
- [ ] Only transform and opacity animated (exception: one-off disclosure height on a small subtree).
- [ ] Loops pause off-screen and on hidden tab.

## Scrub and pinned-scene tokens (iter2 advisor lessons; HEURISTIC, verify in a real-input test)
| Token | Value | Use |
|---|---|---|
| ease-scrub-in | cubic-bezier(.4, 0, 1, 1) | exit of a state in a scrubbed timeline |
| ease-scrub-out | cubic-bezier(0, 0, .2, 1) | entrance of a state |
| ease-scrub-move | cubic-bezier(.33, 1, .68, 1) | element moving across the stage |
| ease-scrub-fade | cubic-bezier(.65, 0, .35, 1) | symmetric fade (reversible on scroll-up) |

- A keyframe's timing function governs the interval to the next stop and is ignored on the last one; the element-level
  function applies to each interval separately.
- Pinned distance = track height minus stage height, both in `svh`. On the track, `contain 0%` to `contain 100%`
  equals the pinned span only when the stage has `top: 0`; with a header offset add a matching `view-timeline-inset`.
- A snap sentinel's offset includes the root `scroll-padding-top`. Snap only with `proximity`, only after a real-input test.
- Rules AR12 (scrub timeline), AR13 (transform registration) and AR21 (testable fallback, e.g. `?sda=off`) apply.
- Browser status (belief, verify): `linear()` Chrome 113 / Firefox 112 / Safari 17.2; scroll-driven animations
  Chromium 115+ and Safari 26, Firefox UNKNOWN.

## Iteration 3 lessons (CANDIDATE; UNPROVEN until v8 is scored)
- AR24 crossfade budget, px by viewport height (fade / overlap / handoff): 800 -> 108 / 42 / 174; 900 -> 122 / 47 / 196;
  1080 -> 146 / 57 / 235. Fade >= 8% of pinned distance and >= 100px; incoming starts at 55-65% of outgoing; opacity >= .35.
- `--dur-tape: 2800ms` linear for the tape/playhead mapping; (the scroll-tied drift token 5px is superseded in iteration 4: no drift in holds, AR38).
- FLIP recipe (AR25): measure first/last with running animations neutralised (`getBoundingClientRect` includes current
  transforms); re-measure on load, `fonts.ready` and ResizeObserver; never store a distance measured at one viewport.
- No root-level proximity snap together with a sticky stage.
- Chrome ignores `var()` inside keyframe timing functions (builder-reported); write the curve literally.
- AR23 scrubbable demo contract (one state variable, final frame in HTML, autoplay once < 4 s, native range) and AR28
  (variant transforms live inside the variant's media query) apply.

## Iteration 4 lessons (CANDIDATE; UNPROVEN until v9 is scored)
- AR38 rest states: each state = build, hold, handoff; hold >= 200px and >= its handoff; nothing changes in a hold; no drift.
- v9 px table (viewport height: holds state 1/2/3, fade, handoff): 800: 211/211/264, 114, 176; 900: 238/238/297, 129, 198;
  1080: 285/285/356, 154, 238.
- Pin 220svh (73svh per state); no snap; step buttons scroll to the hold centre.
- AR44: passive input never cancels autoplay; hover arms after autoplay ends; click, drag and keys cancel.

## Iteration 5 lessons (CANDIDATE; UNPROVEN until v10 is scored)
- AR47 v10 px table (viewport height: holds state 1/2/3, fade, gap, handoff), pin 250svh: 800: 240/240/310, 110, 20, 240; 900: 270/270/349, 124, 22, 270; 1080: 324/324/418, 148, 27, 324. Supersedes the v9 table above where they conflict.
- The turn: 380px range; flip 0-55% in-out sine, twist 55-100% ease-out; px ranges anchored at scrollY 0.
