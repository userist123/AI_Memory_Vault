---
id: manual-qa-checks
title: QA checks before delivering any page (learned from the A/B/C direction audits, 2026-11-23)
tier_min: T1
page_types: ["any"]
topics: ["qa", "text-fit", "width-sweep", "overflow", "reduced-motion", "nav-layout", "verification"]
provenance: Lessons from three independent audits + final reviews of three landing directions (81 defects found in pages that
  had already "passed" their builders' own checks). Every item below was a real, repeated failure.
---

# QA checks (run all before saying done; T1 = items 1-6, T2+ = all)

1. **Width sweep, not fixed sizes.** Test every width from 320 to 1440 px in 20 px steps (plus 1280x720, 1366x768, 1024x640 for
   short laptops). Defects hid BETWEEN the usual 390/768/1024/1440 sizes (e.g. a signup row breaking only at 900-1060 px).
2. **Text-in-box check.** For every element with text, its text rects must stay inside the nearest ancestor that paints a box
   (background, border or radius) with >= 8 px inner clearance. Never give a text container a fixed height: use min-height + padding.
3. **No 2-line labels in pills/buttons.** Shorten copy ("Talk to sales", not "Talk to sales about Business"), use white-space:nowrap
   for chips under ~24 chars, or let the button grow with centred text and padding-block. Non-breaking spaces between numbers and
   units ("5&nbsp;h", "20&nbsp;min", "Oct&nbsp;2026") and U+2011 in hyphenated words.
4. **Header layout.** Put the auto margin on the flex ITEM, not its child: `.nav > nav{margin-inline:auto}` (a `margin:0 auto` on an inner
   `ul` centres nothing; the CTA bunches next to the links). Check the CTA's right edge equals the content edge at >= 861 px.
5. **Mobile essentials.** Sign-up CTA visible in the phone header or menu; menu has backdrop, Esc and outside-click close, Menu/Close label;
   comparison tables become stacked cards (data-label) under ~640 px, never a silent sideways scroll; avatars use aspect-ratio:1 + flex:none.
6. **Overlap and contrast on decoration.** No text over blobs/stickers/glows below 4.5:1; floating chips must not cover text at any width.
7. **State sync.** Billing toggle updates every price on the page (cards AND comparison table header); collapsed FAQ panels are hidden from
   assistive tech (hidden/display:none), not just visually.
8. **Reduced motion is a separate layout to test.** Every element hidden for an animation (opacity 0, translate) must have a reset in the
   prefers-reduced-motion path and in the static fallback; the CSS static breakpoint and the JS one must use the same conditions.
9. **Pinned scenes.** One scroll driver controls the step list, the visual layers and the rail together (no two independent triggers);
   sweep the scroll (~130 samples) and assert step == layer; fall back to static below ~700 px viewport height.
10. **Charts keep true proportions** at every width (no `max(12%, 84px)` fills that turn 6% into 45-70%); stack bar rows under ~420 px;
    use a shared value column so 100% bars end at the same x.
11. **Gradient/clip text.** line-height >= 1.08 and padding-bottom ~.14em so descenders are not cut.
12. **Screenshots for review.** Full-page captures do NOT trigger scroll reveals or pinned states: take viewport shots while scrolling
    (every ~0.9 viewport after one full slow scroll), plus open states (menu, yearly, FAQ open, table open). Look at every image.
13. **Independent reviewer.** The builder's own checks missed 81 defects; always get a fresh reviewer (did not build it) to look at the
    screenshots before delivery. Max 2 fix rounds, then decide.

14. **3D/WebGL in a hero (T4).** Shrink or side-place the 3D; never push the product frame down: it must stay >= 90% as visible
    above the fold as the 2D version at 1440x900, 1366x768 and 390x844. Reserve the slot and show a static poster of the final
    pose when the CDN/WebGL fails (CLS < 0.1). Avoid full spins that show backfaces; nothing may be hard-cut by the canvas edge;
    play once and hold the final state (no vanish/pop loops); init after LCP, pause off-screen, static frame under reduced motion.

## Checklist
- [ ] Width sweep 320-1440/20 + short laptops: 0 overflow, 0 text-out-of-box, 0 two-line labels, 0 overlaps.
- [ ] Header CTA at content edge; mobile CTA + menu behaviour.
- [ ] Tables stack on phones; billing toggle syncs everywhere; FAQ panels hidden when collapsed.
- [ ] Reduced-motion and no-JS paths show everything.
- [ ] Viewport screenshots while scrolling, reviewed by a fresh reviewer.
- [ ] Counted-copy check per breakpoint (AR29): every number in copy equals the visible DOM count.
- [ ] Class-collision grep (AR27): no short class carries two meanings.
- [ ] Computed transform is `none` outside the variant that declares it (AR28).
- [ ] Connector intersection test across widths: connector rects intersect no card but the target (AR26).
- [ ] Offer-string count: one distinct free-offer sentence on the page (AR30).
- [ ] A single wheel notch never spans a handoff (AR24).
- [ ] AR34 pixel-snap list: fixed alignments within 1px and fractional hairline positions at DPR 1.

## Iteration 4 checks
- [ ] Trunk continuity: the spine axis is unbroken from hero through every lower section (AR37).
- [ ] Clock-table string check: every time string on the page appears in the one clock table (AR37).
- [ ] Hold constancy: element rects identical across 20 samples inside a hold (AR38).
- [ ] Passive-pointer autoplay test: hover and pointer moves do not cancel autoplay; click, drag and keys do (AR44).
- [ ] Audience grep: hero range equals the union of plan ranges (AR43).
- [ ] Truncation check on one-line titles: `scrollWidth <= clientWidth`.

## Iteration 5 checks (CANDIDATE)
- [ ] Both-opacities-positive sampling across every handoff: never two states with opacity > 0 at one scrollY (AR47).
- [ ] Tick-source formula: position = seconds / total, instances within half a pitch (AR48).
- [ ] Free space per panel state <= 16px (AR49).
- [ ] Sweep rect vs text rects: no overlap for the stand-out moment (AR51).
- [ ] Frame 0 at scrollY 0 on tall viewports is a clean rest (AR51).
- [ ] Forbidden-claims grep: no audit, cipher, customer or changelog claims on a concept page (AR52).
- [ ] Real Firefox pass for pins >= 200svh (AR53).
- [ ] Reviewer screenshots include gap centres and moment frames.
