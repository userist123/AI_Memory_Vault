---
id: manual-layout-spacing
title: Layout spacing, section boundaries and alignment (skill default)
tier_min: T1
page_types: ["any"]
topics: ["section-spacing", "section-boundary", "pinned-stage-padding", "dead-band", "header-block", "shared-rows", "alignment"]
provenance: Created 2026-11-23 from the fable-consultant iter2 corrections (rules AR10, AR16) and the Fluxa v6 review.
  Values are HEURISTIC project conventions, not sourced facts.
---

# Layout spacing

| Token | 1440px | 390px |
|---|---|---|
| section gap small / standard / large | 64 / 96 / 128 | 48 / 64 / 80 |

## Section boundaries (AR10)
Set spacing once per boundary, never per section.
| Boundary | Spacing |
|---|---|
| hairline or surface change | 96px on each side (48-64 at 390) |
| neither (same background, no rule) | one 96-128px token in total (64-80 at 390) |
| section that opens with a rule | no top padding |
Audit heuristic: no ink-free band above 160px at 1440x900 or 140px at 390x844 at any scroll position.

## Pinned-stage padding
The stage's internal empty space counts toward the boundary: external padding = max(24px, target gap 96px - internal
buffer B), B = (stageHeight - contentHeight) / 2. If the stage background is a visible edge (panel or colour), use the
full 80-96px clearance instead. After a tall pinned stage the next heading appears within 64-96px of the unpin point.

## Dead-band measurement
Capture at 1440x900 and 390x844 at several scroll offsets (every 100px through each boundary); for each frame find the
longest continuous full-width horizontal slice with no foreground ink, hairline or texture; flag any above the audit limit.

## Header block and alignment
- Section header block (eyebrow + h2 + lead) at most 640px wide (56-68ch), at most 2 lines h2, 2-3 lines lead.
- One alignment axis per page: pick left-aligned or centred header blocks and keep it.
- AR16: anything that must align across sibling cards gets its own shared grid row (`grid-template-rows: subgrid`);
  `margin-top: auto` aligns only the last element, so nothing of variable length may follow it.

## Iteration 3 lessons (DIRECT failures in v7 unless marked)
- AR26 connector lane: reserved empty lane (28px in the hero, 16px in panels); target columns ordered like sources; each
  source x inside its target's column; straight drops; jogs share one lane with disjoint x ranges.
- AR33: only elements whose meaning is extent (timeline, to-scale bar) leave the grid; nothing overhangs the wrap by a few px.
- Hero fold budget: write one per viewport and check it at 1440x900, 1366x768, 1280x720 and 1024x800.
- AR36: a tall bento cell holds a vertical artifact of >= 60% of its height; `margin-top: auto` never fills a cell.
- The final card's right column carries "what happens next" above the form.

## Iteration 4 lessons (CANDIDATE; UNPROVEN until v9 is scored)
- AR37 spine geometry: trunk on the wrap's left edge, entry heads at +36px, entry bodies in column 3; sticky stamps from 1200.
- Hero 1024-1199: h1 full width, lead columns 1-8, CTA row left under it. From 1200: h1 columns 1-8, side 9-12, side height <= h1 height.
- AR46: no flex auto-margin fills a column.

## Iteration 5 lessons (CANDIDATE; UNPROVEN until v10 is scored)
- AR49 panels sized by content: free space <= 16px, no viewport-fitted height; AR50 eyebrow above the h1 flush left, note directly under the CTA.
- Hero 1024-1199 rows: label, h1, lead, CTA, note; budget them against the viewport height before sizing.
- Reserved 216px zone for the stand-out moment; text starts at x0 + 96px. The spine starts at the turn (AR37 amended), not at 00:00.
