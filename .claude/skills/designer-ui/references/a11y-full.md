---
id: a11y-full
title: Accessibility (full)
tier_min: T3
page_types: ["any"]
topics: ["reduced-motion", "drag-alternative", "auto-advance", "dropzone", "live-regions", "mask-reveal", "media-controls", "pinned-fallback", "pinned-scroll-story", "redundant-entry", "screen-reader-specs", "text-animation", "touch-targets"]
rules: 17
---

# Accessibility (full)

Detailed accessibility for products.
Support = number of independent educators who teach the rule.

## reduced-motion
- Honor `prefers-reduced-motion: reduce` in hero and scroll scenes by removing or substantially reducing nonessential motion in both CSS and JS, and never let motion be the only carrier of product information. _(support 1, intermediate, R0610)_
  - values: MDN: reduce means minimize movement, preferably remove nonessential movement; `animation-timeline: none` is the documented opt-out for scroll timelines.
  - do: Wrap hero motion in `@media (prefers-reduced-motion: no-preference)` and set `animation-timeline: none` plus a stable end state for reduce.
  - don't: Do not rely on a scrubbed or hover-only effect as the sole way to reach required content; do not assume a duration hack like 0.01 ms is enough in JS-driven motion.
- Under `prefers-reduced-motion: reduce`, replace the pinned scroll-linked scene with a plain stacked flow that shows every state at full opacity with no transform. _(support 1, intermediate, R0686)_
  - values: `@media (prefers-reduced-motion: reduce)`; no universal replacement duration is stated.
  - do: Wrap the pin and `animation-timeline` rules in a no-preference query, and in the reduce branch reset `opacity: 1; transform: none` and drop the pin spacer.
  - don't: Do not keep large panning or scaling just because it is scroll-driven.
- Under `prefers-reduced-motion: reduce`, drop the pinned runway and render the steps as a calm stacked list, with no empty scroll track left behind. _(support 1, intermediate, R0782)_
  - values: `@media (prefers-reduced-motion: no-preference)` gate; no duration cap in the sources.
  - do: Gate sticky scrubbing in a no-preference query, and in the reduce branch set `animation: none; animation-timeline: auto`, remove the tall track and show all steps at full opacity.
  - don't: Do not leave a 300vh-400vh empty track active when animations are off, and do not apply WCAG 2.3.3 as a blanket AA requirement, it is AAA with an essential-animation exception.
- Provide an in-app motion toggle or pause control that supplements the OS reduced-motion setting via a root data attribute. _(support 1, intermediate, R1000)_
  - values: [data-motion="reduced"]
  - do: Check both the media query and :root[data-motion="reduced"]; auto-pause background video when either is active.
  - don't: Do not force users to change OS settings just to stop an autoplaying background video.

## drag-alternative
- Provide a single-pointer click or tap alternative for every drag-and-drop interaction, slider, Kanban card move, or sortable list so users with motor tremors or eye-gaze pointers never have to press-and-drag (WCAG 2.2 SC 2.5.7 Level AA). _(support 1, intermediate, R0867)_
  - values: WCAG 2.2 SC 2.5.7 Dragging Movements (Level AA): Pair every draggable Kanban card/list row with a single-click context menu or action popover ("Move to column...", "Move up / Move down" buttons) and pair range sliders with clickable step buttons or direct numeric text inputs. Note: Keyboard-only arrow support does NOT satisfy SC 2.5.7; it must be operable by a single pointer without dragging.
  - do: Provide a single-pointer click or tap alternative for every drag-and-drop interaction, slider, Kanban card move, or sortable list so users with motor tremors or eye-gaze
  - don't: Only exempt when dragging path itself is essential to the activity (such as freehand drawing or signature capture canvases). | codex: drag-only operation is forbidden except where dragging is essential or user-agent controlled.
- Provide alternatives to complex path-based or multipoint gestures and do not require a specific screen orientation unless essential. _(support 1, intermediate, R0914)_
  - do: Offer single-pointer alternatives and support both orientations.
  - don't: Check WCAG exceptions that apply in context.

## auto-advance
- If a stepped showcase auto-advances, give it a pause control, stop it on keyboard focus and hover, and do not auto-advance copy-heavy walkthroughs at all. _(support 1, intermediate, R0780)_
  - values: 5 s threshold (WCAG 2.2.2); no recommended pause duration.
  - do: Add a play and pause button whose label toggles, pause on focus and hover, and set `aria-live="polite"` only while the visitor pages manually.
  - don't: Do not apply this to a scene that moves only with the visitor scroll; WCAG 2.2.2 covers automatic movement that lasts longer than five seconds next to other content.

## dropzone
- Pair every hero dropzone with a keyboard-focusable trigger and a native file input so the drag action has a single-pointer alternative. _(support 1, intermediate, R0701)_
  - values: WCAG 2.1.1 (Keyboard) and 2.5.7 (Dragging Movements, AA).
  - do: Make the dropzone a focusable button with Enter and Space handlers that open the file picker, and keep a visible label.
  - don't: Do not build a drag-only dropzone or hide or disable the file input.

## live-regions
- Announce try-it outcomes (file accepted, format error, playback state) with a polite live region and never attach one to a continuously updating timestamp. _(support 1, intermediate, R0702)_
  - values: polite for milestones; assertive only for critical failures.
  - do: Use `aria-live="polite"` with `aria-atomic="true"` for discrete messages; reserve `assertive` for critical upload failures.
  - don't: Do not announce a scrubber every second.

## mask-reveal
- Preserve screen-reader accessibility on split-text animations by setting aria-label with the full unfragmented sentence on the parent heading and aria-hidden="true" on all generated line/word/character span wrappers. _(support 1, intermediate, R1077)_
  - values: Parent: <h1 aria-label="Full original heading text">; Split children wrapper: <span aria-hidden="true"><span class="line"><span class="word">...</span></span></span>; Or include a visually-hidden (.sr-only) span alongside aria-hidden="true" visual spans. [Disagreement Note]: W3C/Deque accessibility practitioners note that aria-label on non-interactive headings can prevent some translation tools or braille displays from navigating word-by-word, so pairing a single <span class="sr-only">Full text</span> with <span aria-hidden="true">...split spans...</span> is preferred over aria-label on<h1> when machine-translation compatibility is required.
  - don't: Never split text into DOM <span> nodes per character or word without aria-hidden="true", which causes VoiceOver and NVDA to stutter letter-by-letter.

## media-controls
- Let visitors start sound and video themselves and give them native or keyboard-operable controls; the demo meaning must not depend on audio alone. _(support 1, intermediate, R0704)_
  - values: WCAG 1.4.2: 3 second limit for automatic audio.
  - do: Expose play and mute with `aria-pressed`, a keyboard-operable range seek, and a visible pause for anything that plays.
  - don't: Do not autoplay sound, and do not autoplay any audio longer than 3 seconds without an immediate pause control.

## pinned-fallback
- Keep the scene content reachable in ordinary document order and via keyboard, and never make a drag or trackpad gesture the only path to later states. _(support 1, intermediate, R0765)_
  - values: no source-backed scene-specific placement or paging values (codex).
  - do: Use real DOM text for every state, real buttons for step navigation, and normal tab order through the scene.
  - don't: Do not treat a skip link as a substitute for accessible content and controls inside the sequence.

## pinned-scroll-story
- Keep every step's content in the DOM as static, readable markup and disable the pinned/scrubbed layout under `prefers-reduced-motion: reduce`. Option: step labels may be buttons that scroll to the centre of that step's rest plateau (`trackTop + p_i * (trackH - stageH)`). _(support 1, intermediate, R0522)_
  - values: Step target offset `trackTop + p_i * (trackH - stageH)`; reduced-motion fallback converts the tall track to `height: auto` static grid or an instant opacity swap.
  - don't: Do not hide step content that is reachable only by scrolling a scrubbed animation.
  - caveat: evidence DIRECT for the reduced-motion half only; the step-button option is CANDIDATE

## redundant-entry
- Auto-populate previously entered data across multi-step workflows (WCAG 2.2 SC 3.3.7 Level A) and allow password-manager autofill, passkeys, and clipboard paste on all authentication and OTP fields without cognitive puzzle tests (WCAG 2.2 SC 3.3.8 Level AA). _(support 1, intermediate, R0875)_
  - values: WCAG 2.2 SC 3.3.7 Redundant Entry (Level A): Never force re-typing of shipping address, email, or user details within the same session; provide a "Same as shipping" checkbox or pre-filled value. WCAG 2.2 SC 3.3.8 Accessible Authentication (Minimum) (Level AA): Set proper autocomplete="username", autocomplete="current-password", autocomplete="one-time-code"; never block onpaste on password or 6-digit OTP inputs (automatically split pasted 6-digit strings across OTP boxes); never use object-recognition CAPTCHAs without a non-cognitive alternative.
  - do: Auto-populate previously entered data across multi-step workflows (WCAG 2.2 SC 3.3.7 Level A) and allow password-manager autofill, passkeys, and clipboard paste on all
  - don't: Re-entry of data is only permitted when essential for security verification (such as confirming a new password or when stored data has expired). | codex (CANDIDATE): keep entered values on error and explain the fix in text, not only by red colour/outline.

## screen-reader-specs
- Include screen-reader specifications in the handoff: VoiceOver (iOS), TalkBack (Android) and ARIA (web) annotations, e.g. for a checkout flow. _(support 1, intermediate, R0455)_

## text-animation
- When a headline is split into per-character or per-word spans for a staggered entrance, keep one complete accessible string and hide the animated fragments from assistive tech. _(support 1, intermediate, R0609)_
  - values: stagger about 50 ms per character/word step.
  - do: Prefer per-line or per-word wrappers that keep real text nodes (no ARIA needed). Only per-character splits need a visually hidden full string plus aria-hidden fragments.
  - don't: Do not leave split spans as the only readable text; do not add a redundant `aria-label` to a headline that is not split.

## touch-targets
- Size try-it controls (pills, sliders, toggles) at 24px or more on desktop and 44px or more on touch, with `touch-action: manipulation`. _(support 1, intermediate, R0705)_
  - values: 24px minimum (WCAG 2.5.8); 44px is a touch guideline.
  - do: Give every pill and thumb a hit area at that size, with label text at 13px or more.
  - don't: Do not rely on tiny pill hit areas.

## Checklist

- [ ] If a stepped showcase auto-advances, give it a pause control, stop it on keyboard focus and hover, and do not auto-advance copy-heavy walkthroughs at all.
- [ ] Provide a single-pointer click or tap alternative for every drag-and-drop interaction, slider, Kanban card move, or sortable list so users with motor tremors or eye-gaze pointers never have to press-and-drag (WCAG 2.2 SC 2.5.7 Level AA).
- [ ] Provide alternatives to complex path-based or multipoint gestures and do not require a specific screen orientation unless essential.
- [ ] Pair every hero dropzone with a keyboard-focusable trigger and a native file input so the drag action has a single-pointer alternative.
- [ ] Announce try-it outcomes (file accepted, format error, playback state) with a polite live region and never attach one to a continuously updating timestamp.
- [ ] Preserve screen-reader accessibility on split-text animations by setting aria-label with the full unfragmented sentence on the parent heading and aria-hidden="true" on all generated line/word/character span wrappers.
- [ ] Let visitors start sound and video themselves and give them native or keyboard-operable controls; the demo meaning must not depend on audio alone.
- [ ] Keep the scene content reachable in ordinary document order and via keyboard, and never make a drag or trackpad gesture the only path to later states.
