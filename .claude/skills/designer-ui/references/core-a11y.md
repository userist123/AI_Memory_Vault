---
id: core-a11y
title: Accessibility basics
tier_min: T1
page_types: ["any"]
topics: ["reduced-motion", "aria-patterns", "target-size", "wcag-motion", "focus-states", "reflow", "text-contrast", "a11y-minimums", "colour-not-only", "fitts-law-target-size", "fitts-law-touch-targets", "focus-ring", "hit-targets", "hover-focus-content", "icon-button-labels"]
rules: 40
---

# Accessibility basics

Minimum accessibility every page needs.
Support = number of independent educators who teach the rule.

## reduced-motion
- Respect the user's reduced-motion setting for drawers, transitions and other animations. _(support 1, basic, R0206)_
- Honor prefers-reduced-motion: reduce by removing nonessential scroll-linked movement or replacing it with a stable state, and never make scroll motion the only way to reveal necessary information. _(support 1, basic, R0586)_
  - values: prefers-reduced-motion: reduce -> remove nonessential movement or show a stable end state.
  - don't: Do not rely on a scroll-scrubbed animation as the only way to access required content; no universal duration is stated by MDN.
  - group: pinned-scene-always-on
- Disable or replace spatial translation, parallax and spring bounce when prefers-reduced-motion: reduce is active; keep high-frequency keyboard actions instant (heuristic). _(support 1, basic, R0870)_
  - values: Preferred: inside @media (prefers-reduced-motion: reduce) remove or swap specific spatial animations for opacity cross-fades. Fallback only (not the default): a global '*, *::before, *::after' reset with animation-duration/transition-duration 0.01ms and scroll-behavior: auto. Heuristic: instant response for keyboard actions used very often (e.g. command palette arrows).
  - do: Disable or replace spatial translation, parallax and spring bounce when prefers-reduced-motion: reduce is active; keep high-frequency keyboard actions instant (heuri
  - don't: Avoid long transitions on everyday menus, tooltips and modals (heuristic: not above 300ms). Note: WCAG 2.2 SC 2.3.3 Animation from Interactions is Level AAA; honouring the media query is the technique, not an AA requirement. No universal numeric durations are specified by the source.
- Avoid flashes and abrupt motion that can trigger seizures and communicate animated changes through an accessible channel such as ARIA live regions. _(support 1, basic, R0917)_
  - do: Convey state changes by text/live region as well as animation.
  - don't: Do not convey information only by animation.
- Honor prefers-reduced-motion: reduce by removing, reducing or replacing nonessential spatial movement with short cross-fades, not by disabling every animation. _(support 1, basic, R0997)_
  - values: 100ms-200ms opacity cross-fade (gemini synthesis: 150ms); SC 2.3.3 Level AAA
  - do: Replace translate/scale/bounce with 100ms-200ms opacity/color fades; in Motion wrap the app in <MotionConfig reducedMotion="user">. Avoid large-scale panning and scaling and test the adapted experience with the OS setting enabled; do not presume that shortening every duration addresses vestibular triggers. Allow interaction-triggered motion to be disabled via prefers-reduced-motion and/or a site setting unless essential (WCAG 2.2 SC 2.3.3, Level AAA), covering parallax, page morphs and scroll-jacked sequences.
  - don't: Never use the global * { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; } reset without testing. Do not confuse the AAA criterion 2.3.3 with the A-level flash criteria.
- Keep essential status and hierarchy signals under reduced motion by swapping to a dissolve, highlight fade, color shift or slower pulse. _(support 1, basic, R0998)_
  - values: spinner 0.6s -> 1.5s-2.0s opacity pulse
  - do: Keep progress bars updating; replace a rapid 0.6s spinner with a 1.5s-2.0s opacity pulse or stepped indicator.
  - don't: Do not remove the only signal that an action succeeded and do not hide or freeze loading spinners.
- Put large or decorative animations inside a no-motion-first @media (prefers-reduced-motion: no-preference) block. _(support 1, basic, R0999)_
  - values: @media (prefers-reduced-motion: no-preference); 100ms color transitions stay
  - do: Scroll-driven, parallax and decorative keyframes live inside no-preference.
  - don't: Do not gate basic 100ms hover color transitions or focus outlines behind no-preference.

## aria-patterns
- Build the Monthly/Annual toggle from native radio inputs in a `<fieldset>` with a `<legend>` (segmented control), or a native checkbox / `role="switch"` with a static label (sliding switch); show the concrete saving next to the Annual label and announce the price change in a polite live region. _(support 1, basic, R0553)_
  - values: Toggle height `36px-40px`, inner pill padding `4px`, saving badge text at least 4.5:1 and 12px; arrow keys move between segmented radios.
  - don't: Never dynamically mutate the accessible label of a `role="switch"` button when toggled (e.g., changing the label from 'Switch to Annual' to 'Switch to Monthly' while also flipping `aria-checked`), which causes double-negation confusion in screen readers per WAI-ARIA APG.
- Implement the APG Tabs pattern with roving tabindex; use automatic activation for static panels and manual activation when switching has latency or layout shift. _(support 1, basic, R0873)_
  - values: APG Tabs contract: role='tablist' (aria-orientation='vertical' if vertical); each tab role='tab', aria-selected true/false, tabindex 0 for active and -1 for inactive, aria-controls=panel id; panel role='tabpanel', aria-labelledby=tab id. Left/Right arrows move between tabs, Home/End jump to first/last. Automatic: arrow focuses and selects. Manual: arrow focuses, Enter/Space selects.
  - don't: Do not use the tabs pattern for site navigation to separate URLs (use nav with links and aria-current='page'). context: automatic activation fits instantly available panels; manual activation fits panels that load or shift layout. Verify the behaviour of the primitive and version in use.
- Build accordions with a native <button> nested inside a semantic heading (<h2>–<h6>) using aria-expanded="true|false" and aria-controls, and build modal dialogs with role="dialog", aria-modal="true", a strict Tab focus trap, and Escape dismissal that restores focus to the trigger. _(support 1, basic, R0874)_
  - values: WAI-ARIA APG Accordion contract: <h3><button type="button" aria-expanded="false" aria-controls="sect1" id="acc1">Title</button></h3> with panel <div id="sect1" role="region" aria-labelledby="acc1" hidden>; Enter/Space toggles expansion. WAI-ARIA APG Dialog (Modal) contract: <dialog> or <div role="dialog" aria-modal="true" aria-labelledby="dialog-title">; On open: move focus to first interactive input or primary action inside dialog; Tab/Shift+Tab wraps cyclically inside dialog boundary (background set to inert); Escape closes modal and returns focus to invoking trigger element.
  - do: Build accordions with a native <button> nested inside a semantic heading (<h2>–<h6>) using aria-expanded="true|false" and aria-controls, and build modal dialogs with
  - don't: Do not apply role="region" to every accordion panel if an accordion has > 6 panels on one page, as it clutters screen-reader landmark navigation list. | codex: avoid role=region on many panels (APG about 6 landmarks as orientative threshold).
- For a modal dialog keep Tab/Shift+Tab within the dialog, provide a visible close button, and restore focus to the trigger on close. _(support 1, basic, R0928)_
  - do: Trap focus, show close button, return focus to the trigger.
  - don't: Do not allow interaction with the background while the modal is open.

## target-size
- Enforce a minimum tap target of `44px x 44px` (`48px` height preferred for primary hero and pricing CTAs) with at least `8px-12px` clearance between adjacent interactive buttons on mobile (`390px`), and ensure desktop horizontal button pairs maintain `12px-16px` gap so cursor overshoot never triggers the adjacent link. _(support 1, basic, R0575)_
  - values: Touch target min `44px x 44px` (WCAG 2.5.5 AAA / Apple HIG) and `>= 24px x 24px` (WCAG 2.5.8 AA); hero button height `44px-48px`; gap between Primary and Secondary hero buttons `12px-16px`.
  - don't: Do not stack two full-width (`100%`) buttons vertically on `390px` mobile with `< 10px` gap or identical visual weight; place the Primary solid CTA on top (`height: 48px`) and the Secondary ghost/outline CTA below (`height: 44px`) with `12px` vertical gap.
- Size every pointer target to at least 24x24 CSS pixels on all web (WCAG 2.2 SC 2.5.8 Level AA legal floor), or space undersized targets with a 24px clearance circle. _(support 1, basic, R0866)_
  - values: WCAG 2.2 SC 2.5.8 (AA): minimum 24x24 CSS px (expand compact icons via padding or a ::before hit area); spacing exception: a 24px circle centred on an undersized target must not intersect another target. Enhanced WCAG 2.5.5 (AAA) is 44x44.
  - don't: The 24x24 box does not apply to inline text links within a sentence. context: 24px is the floor everywhere; use 44px as the default for anything touch-reachable on web and 48dp for Android native (see touch-target rule).
- Make anything touch-reachable at least 44x44 px by default on web (24px is the WCAG legal floor), 48x48dp on Android native and 44x44pt on iOS native, with spacing between adjacent targets. _(support 1, basic, R0894)_
  - values: Defaults: web touch 44x44px, Android native 48x48dp, iOS native 44x44pt; legal floor 24x24 CSS px. Expand visual 20-24px icon buttons via padding or a pseudo-element: .icon-btn { position: relative; } .icon-btn::after { content: ''; position: absolute; inset: -10px; }
  - do: Make anything touch-reachable at least 44x44 px by default on web (24px is the WCAG legal floor), 48x48dp on Android native and 44x44pt on iOS native, with spacing b
  - don't: Never pack several small action icons close together on a mobile list row; move secondary actions to a sheet or overflow menu. context: 24 CSS px is the legal floor (WCAG 2.5.8) for desktop pointer UIs.
- Size interactive targets to at least 24 by 24 CSS pixels or meet the spacing exception, including while scaled during animation (WCAG 2.2 SC 2.5.8, Level AA). _(support 1, basic, R1004)_
  - values: 24 × 24 CSS px (SC 2.5.8 AA); 44 × 44 CSS px (SC 2.5.5 AAA); Apple 44x44pt; Material 48x48dp
  - do: Aim for 44 x 44 where possible (SC 2.5.5 AAA, Apple 44x44pt, Material 48x48dp).
  - don't: Do not shrink an interactive target to make room for motion decoration.

## wcag-motion
- Provide pause, stop or hide controls for moving, blinking or scrolling content that starts automatically, lasts more than 5 seconds and runs alongside other content (WCAG 2.2 SC 2.2.2, Level A). _(support 1, basic, R1001)_
  - values: > 5 seconds; SC 2.2.2 Level A
  - do: Applies to carousels, marquees and background video.
  - don't: Do not apply the threshold to every animation indiscriminately; exempt only essential movement.
- Ensure nothing flashes more than 3 times in any one-second period unless below the general and red flash thresholds (WCAG 2.2 SC 2.3.1, Level A). _(support 1, basic, R1002)_
  - values: <= 3 flashes per 1 second
  - do: Check strobe effects and video.
  - don't: Do not confuse SC 2.3.3 with this flash threshold; no exceptions.
- Keep the focused element from being entirely hidden by sticky headers, floating bars, bottom sheets or toasts (WCAG 2.2 SC 2.4.11, Level AA). _(support 1, basic, R1003)_
  - values: SC 2.4.11 Level AA (part visible); SC 2.4.12 Level AAA (100% visible); 72px / 64px example paddings
  - do: html { scroll-padding-top: var(--sticky-header-height, 72px); scroll-padding-bottom: var(--sticky-cta-height, 64px); }; trap focus inside intentional modals.
  - don't: Do not treat a motion effect that obscures focus as harmless decoration.
- Keep animation supplemental to other feedback and never the only way to communicate important information. _(support 1, basic, R1005)_
  - do: Pair motion with text, icon or color change.
  - don't: Do not rely on a visual transition alone to confirm an action.

## focus-states
- Ensure that no keyboard-focused element is ever completely hidden behind sticky headers, fixed footers, floating bottom bars, or cookie banners by setting CSS scroll-padding matching the sticky overlay height (WCAG 2.2 SC 2.4.11 Level AA). _(support 1, basic, R0864)_
  - values: WCAG 2.2 SC 2.4.11 Focus Not Obscured (Minimum) (Level AA): At least some part of the focused component must remain visible; SC 2.4.12 (Level AAA): No part of the focused component is obscured. CSS remediation: html { scroll-padding-top: calc(var(--sticky-header-height, 64px) + 16px); scroll-padding-bottom: calc(var(--sticky-footer-height, 56px) + 16px); }.
  - do: Ensure that no keyboard-focused element is ever completely hidden behind sticky headers, fixed footers, floating bottom bars, or cookie banners by setting CSS
  - don't: Do not omit scroll-padding Adjustment when adding a new sticky banner or floating bottom CTA bar; dynamically update the CSS variable whenever sticky bar height changes. | codex: AA requires the focused element not be entirely hidden; avoiding partial obstruction is better.
- Render keyboard focus indicators via :focus-visible using a 2px thick solid ring with at least 3:1 contrast against adjacent colors and a 2px offset (or two-tone inner/outer ring) so focus is unmistakable on any background (WCAG 2.2 SC 2.4.13 Level AAA). _(support 1, basic, R0865)_
  - values: WCAG 2.2 SC 2.4.13 Focus Appearance: Area >= 2 CSS px thick perimeter around the component; Contrast change between focused and unfocused states >= 3:1; Adjacent contrast >= 3:1 or >= 2px offset. Universal two-tone CSS recipe: :focus-visible { outline: 2px solid var(--focus-ring-outer, #2563EB); outline-offset: 2px; box-shadow: 0 0 0 2px var(--bg-canvas, #FFFFFF); } (never use outline: none without a high-contrast replacement).
  - do: Render keyboard focus indicators via :focus-visible using a 2px thick solid ring with at least 3:1 contrast against adjacent colors and a 2px offset (or two-tone
  - don't: Do not show heavy focus rings on mouse click of non-text buttons—use :focus-visible rather than :focus so rings appear for keyboard navigation and text inputs without cluttering pointer clicks. | codex: WCAG 2.4.13 (2 CSS px perimeter, 3:1) is AAA, not AA.
- Differentiate focus with a clear contour or stroke, not colour change alone. _(support 1, basic, R0916)_
  - do: Use a thicker-stroke focus container as in Fluent.
  - don't: Do not remove the focus indicator for a minimalist look.

## reflow
- Ensure page layouts reflow cleanly down to a 320 CSS pixel viewport width (equivalent to 400% browser zoom on a 1280px screen) without horizontal overflow or two-dimensional scrolling (WCAG 2.2 SC 1.4.10 Level AA). _(support 1, basic, R0868)_
  - values: WCAG 2.2 SC 1.4.10 Reflow (Level AA): Vertical scrolling content must fit within 320 CSS pixels width without horizontal scrollbars; Horizontal scrolling content within 256 CSS pixels height. Use min(100%, 420px) instead of fixed width: 420px, flex-wrap: wrap on toolbars, and minmax(min(100%, 280px), 1fr) on CSS grids.
  - do: Ensure page layouts reflow cleanly down to a 320 CSS pixel viewport width (equivalent to 400% browser zoom on a 1280px screen) without horizontal overflow or
  - don't: Two-dimensional scrolling at 320px is permitted only for content where 2D spatial layout is essential to meaning: data tables, geographic maps, complex diagrams, and video/game canvases (wrap data tables in an isolated overflow-x: auto container so the page itself never scrolls horizontally). | codex: do not apply the two-dimensional exception to a whole page just because it contains a table.
- Allow text enlargement up to 200% without loss of content or functionality. _(support 1, basic, R0913)_
  - values: 200% text resize
  - do: Use flexible heights and layouts that survive 200% text.
  - don't: Do not fix heights or clips that hide enlarged text.

## text-contrast
- Check the contrast of every text level with a contrast plugin; keep secondary body text well above the minimum, not just the title. _(support 1, basic, R0407)_
  - values: examples: title 18.52:1, body 9.5:1 on light; 21:1 and 16.94:1 on black
- Gate text contrast on WCAG 2.x ratios only (4.5:1 normal text, 3:1 large text >=18pt or >=14pt bold); treat APCA Lc as advisory, never as a pass/fail gate. _(support 1, basic, R0871)_
  - values: Gate: WCAG 2.x 4.5:1 (normal), 3:1 (large). Advisory only: APCA Lc ~90 long-form, ~75 UI body, ~60 large text; 7:1 (AAA) for long-form reading.
  - don't: Do not claim APCA compliance as a substitute for WCAG 2.x or require both. context: APCA is a WCAG 3.0 candidate, not a legal baseline; its Lc values and a 7:1 long-form target are advisory only.

## a11y-minimums
- Meet accessibility minimums per component: text contrast at least 4.5:1, a 2px focus ring, tap targets at least 44px. _(support 1, basic, R0279)_
  - values: contrast >= 4.5:1, focus ring 2px, tap target >= 44px

## colour-not-only
- Do not use colour as the only means of conveying meaning; add labels, shapes, patterns or descriptive text for signals and charts. _(support 1, basic, R0918)_
  - do: Add label/shape/pattern next to colour.
  - don't: Do not signal status only by colour.

## fitts-law-target-size
- Make key actions large and place primary CTAs inside the thumb zone; avoid tiny targets far from the thumb. _(support 1, basic, R0027)_
  - do: Full-width Continue button in lower screen area
  - don't: Small icon button in the top corner for the main action

## fitts-law-touch-targets
- Make key actions large enough, place CTAs within the thumb zone, separate important actions clearly and avoid tiny hard-to-hit targets. _(support 1, basic, R0110)_
  - do: Full-width Continue button in the lower thumb zone
  - don't: Tiny icon target far from the thumb

## focus-ring
- Keep a visible keyboard focus ring on interactive elements; never remove it without a replacement. _(support 1, basic, R0205)_

## hit-targets
- If a visual target is smaller than 24px, expand its hit area to at least 24px; on mobile the minimum target is 44px. _(support 1, basic, R0448)_
  - values: >=24px hit target; 44px mobile

## hover-focus-content
- Content shown on hover or focus must be dismissible, hoverable and persistent long enough (WCAG 1.4.13). _(support 1, basic, R0915)_
  - values: WCAG 1.4.13
  - do: Let users dismiss, hover over and read tooltips.
  - don't: Do not use tooltips that vanish before the user can read them.

## icon-button-labels
- Give every icon-only button an accessible label. _(support 1, basic, R0204)_

## keyboard-focus
- Make every flow keyboard-operable following WAI-ARIA patterns, and give every focusable element a visible focus ring (prefer :focus-visible over :focus). _(support 1, basic, R0447)_

## mask-reveal
- Short-circuit all mask, translate, and blur text reveal animations immediately under @media (prefers-reduced-motion: reduce), rendering text at final position and full opacity on frame 0. _(support 1, basic, R1080)_
  - values: @media (prefers-reduced-motion: reduce) { .reveal-line, .reveal-word { animation: none !important; transition: none !important; transform: none !important; filter: none !important; opacity: 1 !important; clip-path: none !important; } }.
  - don't: If motion conveys essential state change (e.g., swapping two words in a rotating headline), replace the sliding translation with a gentle 150ms opacity crossfade rather than broken overlapping text.

## mobile-nav
- Use plain <nav><ul><li><a> link semantics for marketing site navigation (desktop or mobile) and reserve role="menu", role="menubar" and role="menuitem" for OS-style application menus. _(support 1, basic, R1099)_
  - values: Correct semantics: <nav aria-label="Main"><ul><li><a href="/pricing">Pricing</a></li></ul></nav>; Prohibited roles on marketing nav: role="menu", role="menubar", role="menuitem".
  - don't: Never apply role="menu", role="menubar", or role="menuitem" to marketing site navigation links, as those ARIA roles hijack Arrow keys and disable standard link interactions. | except: use role="menu" and role="menuitem" for in-app action menus (like a "More actions..." context menu or command menu widget) where ArrowUp/ArrowDown navigation and single-tab-stop composite widget behavior are expected. | codex (APG): for ordinary site navigation use disclosure buttons that show/hide link lists; do not use role=menu just because the UI is informally called a menu.

## non-text-contrast
- Maintain at least a 3:1 non-text contrast ratio on active form input borders, custom checkboxes/toggles, and data chart series against adjacent surfaces, and make hover/focus popovers dismissible via Escape, hoverable, and persistent (WCAG 2.2 SC 1.4.11 & 1.4.13 Level AA). _(support 1, basic, R0872)_
  - values: WCAG 2.2 SC 1.4.11 Non-text Contrast (Level AA): >= 3:1 contrast ratio for UI component boundary cues and graphical objects; Note: If an input has a subtle < 3:1 resting border (e.g., 1.5:1), it must either have a distinct background fill with >= 3:1 contrast or clearly indicate interactivity via high-contrast label/placeholder + >= 3:1 focus/hover border. WCAG 2.2 SC 1.4.13 Content on Hover or Focus (Level AA): Tooltips/popovers must be: 1. Dismissible (Escape key closes without moving focus), 2. Hoverable (cursor can move onto tooltip without it closing, using a 4px–8px invisible hover bridge), 3. Persistent (stays open until user moves focus/hover away or dismisses).
  - do: Maintain at least a 3:1 non-text contrast ratio on active form input borders, custom checkboxes/toggles, and data chart series against adjacent surfaces, and make
  - don't: Disabled/inactive controls and purely decorative background illustrations are exempt from the 3:1 non-text contrast rule.

## text-spacing
- Build all text containers with flexible heights and relative padding so user-injected text-spacing overrides never clip, overlap, or truncate copy (WCAG 2.2 SC 1.4.12 Level AA). _(support 1, basic, R0869)_
  - values: WCAG 2.2 SC 1.4.12 Text Spacing (Level AA) test thresholds: 1. Line height (line spacing) to at least 1.5 times the font size; 2. Spacing following paragraphs to at least 2 times the font size; 3. Letter spacing (tracking) to at least 0.12 times the font size (0.12em); 4. Word spacing to at least 0.16 times the font size (0.16em). Use min-height instead of fixed height on buttons/cards/badges and avoid overflow: hidden on text wrappers without expandable/tooltip recovery.
  - do: Build all text containers with flexible heights and relative padding so user-injected text-spacing overrides never clip, overlap, or truncate copy (WCAG 2.2 SC 1.4.12
  - don't: Do not hardcode fixed pixel heights (e.g., height: 32px; overflow: hidden; white-space: nowrap) on multi-word cards, alert banners, or navigation items that will clip when letter-spacing expands to 0.12em. | codex: WCAG 1.4.12 values are tolerance targets (1.5, 2, 0.12, 0.16), not required design values.

## touch-target
- Use a 48px touch target as a default for touch interfaces, checking the exact platform minimum. _(support 1, basic, R0288)_
  - values: 48px

## Checklist

- [ ] Meet accessibility minimums per component: text contrast at least 4.5:1, a 2px focus ring, tap targets at least 44px.
- [ ] Build the Monthly/Annual toggle from native radio inputs in a `<fieldset>` with a `<legend>` (segmented control), or a native checkbox / `role="switch"` with a static label (sliding switch); show the concrete saving next to the Annual label and announce the price change in a polite live region.
- [ ] Implement the APG Tabs pattern with roving tabindex; use automatic activation for static panels and manual activation when switching has latency or layout shift.
- [ ] Build accordions with a native <button> nested inside a semantic heading (<h2>–<h6>) using aria-expanded="true|false" and aria-controls, and build modal dialogs with role="dialog", aria-modal="true", a strict Tab focus trap, and Escape dismissal that restores focus to the trigger.
- [ ] For a modal dialog keep Tab/Shift+Tab within the dialog, provide a visible close button, and restore focus to the trigger on close.
- [ ] Do not use colour as the only means of conveying meaning; add labels, shapes, patterns or descriptive text for signals and charts.
- [ ] Make key actions large and place primary CTAs inside the thumb zone; avoid tiny targets far from the thumb.
- [ ] Make key actions large enough, place CTAs within the thumb zone, separate important actions clearly and avoid tiny hard-to-hit targets.
