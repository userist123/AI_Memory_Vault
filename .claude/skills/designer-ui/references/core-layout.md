---
id: core-layout
title: Spacing & layout
tier_min: T1
page_types: ["any"]
topics: ["8pt-grid", "mobile-grid", "section-rhythm", "spacing-scale", "css-grid", "desktop-grid", "proximity-spacing", "8pt-spacing-scale", "bento-layout", "card-aspect-ratio", "card-padding", "millers-law-chunking", "proximity-chunking", "soft-grid", "4pt-grid"]
rules: 88
---

# Spacing & layout

Grid, spacing scale, alignment, hierarchy.
Support = number of independent educators who teach the rule.

## 8pt-grid
- Size and position containers on the 8-point grid because most popular screen sizes are divisible by 8, so layouts can be subdivided without decimals. _(support 2, basic, R0467)_
  - values: 1280 width, 12 columns, 74pt column, 32pt gutter, 16pt margin + 4pt flexible margin
- Align all spacing and sizes to an 8pt grid. _(support 1, basic, R0145)_
  - values: 8px
  - do: Figma Layout Grid (Shift+G) set to 8px
- Use an 8-point grid for component sizes and for both vertical and horizontal alignment. _(support 1, basic, R0222)_
  - values: 8pt
- Make all spacing, margins and element dimensions multiples of 8; drop to 4pt increments when more precision is needed. _(support 1, basic, R0227)_
  - values: 8-64px in steps of 8; 4pt half-step
  - do: 8, 16, 24, 32, 40, 48, 56, 64px
- Space form fields on multiples of 8 (e.g. 16 side padding, 16 between label groups, 24 from header). _(support 1, basic, R0232)_
  - values: 24, 16
  - do: Edit Profile form: 24 top, 16 sides, 16 between fields
- Use multiples of 8 (8, 16, 24, 32, 40, 48, 56...) for layout, dimensions, padding and margin so designs hand off consistently to development and scale across devices. _(support 1, basic, R0235)_
  - values: 8, 16, 24, 32, 40, 48, 56
- Use 8pt increments for padding inside components so spacing stays consistent with the grid. _(support 1, basic, R0303)_
  - values: 8pt padding
- Make the heights of vertically stacked components multiples of 8pt as well, not just their padding. _(support 1, basic, R0304)_
- Replace inconsistent arbitrary gaps (12, 27, 9px) with 8-pixel steps for all paddings and margins so hierarchy reads clearly. _(support 1, basic, R0326)_
  - values: 8, 8, 16px
  - don't: 12px / 27px / 9px gaps in one card
- Use an 8-point grid for margins, padding and dimensions because 8 divides cleanly into 4, 2 and 1 without decimals. _(support 1, basic, R0387)_
  - values: 8, 4, 2, 1
  - don't: A 10-point grid that fractions into 5, 2.5 and 1.25 and makes elements look hazy at 1x
- Make every padding and margin an 8-px multiple; random gaps (12, 27, 9 px) confuse hierarchy. _(support 1, basic, R0475)_
  - values: 8 px steps
  - don't: 12 px / 27 px / 9 px gaps inside one card

## mobile-grid
- For mobile layouts use a 6-column grid with 16 margin and 24 gutter. _(support 1, basic, R0231)_
  - values: count 6, margin 16, gutter 24
- Start mobile app design on the smallest iPhone frame with at least 16px side margins, 8px gutters and a 4, 5 or 6 column grid. _(support 1, basic, R0307)_
  - values: 375px frame, 16+px margins, 8px gutter, 335+px content width
- Set up the mobile layout grid as 2 stretch columns with 16px margin and 16px gutter on a 320px frame. _(support 1, basic, R0313)_
  - values: Frame W 320, Count 2, Type Stretch, Width Auto, Margin 16, Gutter 16
- Lay out a mobile dashboard on a 4-column stretch grid with 20px margins and 20px gutters on a 390px frame. _(support 1, basic, R0330)_
  - values: 390x844px frame, 4 columns, margin 20, gutter 20; header 60px, carousel 150px, chips 36px, device cards 200px
- Mobile column grid: 4 columns, 20px gutter, 24px margin, column width auto (fluid). _(support 1, basic, R0397)_
  - values: 4 cols, gutter 20px, margin 24px, column ~80px on iPhone 13 Pro Max

## section-rhythm
- Option: When a `1px` horizontal hairline divider (`border-top: 1px solid rgba(255,255,255,0.08)`) or structural blueprint grid line separates two sections, apply symmetric padding of `80px-112px` above AND `80px-112px` below the line at `1440px` (`56px-64px` above and below at `390px`), because the visible hairline resets optical distance calculation so the eye measures two separate `96px` margins rather than one `192px` void. _(support 1, basic, R0536)_
  - values: Option values: With hairline divider at 1440px: `96px` above line + `1px` border + `96px` below line (`193px` total span, max gap without ink `96px`). At 390px: `56px` above + `1px` border + `56px` below.
  - don't: Do not place a `1px` hairline divider immediately above or below a section that already has a full-width boxed container card within `32px` of the divider, which creates double-line visual clutter; merge the card border with the section grid line (Vercel/Clerk cross-grid style).
  - caveat: the "observed on <sites>" claim was not independently verified (codex: no live computed-style inspection of the named sites was performed).
  - group: section-gap (pick one per boundary)
- Maintain an internal vertical spacing hierarchy within every landing page section that strictly obeys ` Gap(element -> element) < Gap(group -> group) < Gap(header -> grid) < Gap(section -> section) ` by at least a `1.5x` to `2x` multiplier at each level. _(support 1, basic, R0541)_
  - values: Desktop 1440px: Eyebrow-to-H2 `12px-16px`; H2-to-Subtitle `16px-20px`; Section Header Block-to-Content Grid `48px-64px`; Card internal padding `24px-32px`; Grid gap `16px-24px`; Section-to-Section `96px-128px`. Mobile 390px: Eyebrow-to-H2 `8px-12px`; H2-to-Subtitle `12px-16px`; Header Block-to-Grid `32px-40px`; Card padding `20px-24px`; Section-to-Section `64px-80px`.
  - don't: Do not use `64px` between a section header and its content grid when the section is a compact social-proof logo strip, where header-to-logos gap must be `24px-32px`.
- Constrain section header blocks (Eyebrow + H2 + Lead Paragraph) to a maximum measure of `56ch-68ch` (`580px-680px` at `16px-18px` body size) and cap their total vertical height at `<= 220px` on `1440px` desktop (`<= 260px` on `390px` mobile), leaving at least `60%` of the viewport height for the section's visual/interactive payload. _(support 1, basic, R0573)_
  - values: Header block max-width `640px` (`60ch-65ch`); H2 max lines `2` on desktop (`3` on mobile); Lead copy max lines `2-3` (`<= 68ch`); Header block total height `140px-220px`; gap to content grid `48px-64px` (desktop), `32px-40px` (mobile).
  - don't: Do not let a section H2 and lead paragraph stretch across the full `1200px` container width; lines wider than `75ch-80ch` degrade reading rhythm and leave awkward asymmetric whitespace on the right side of the section.
  - caveat: evidence DIRECT for the measure only; the 220px height cap and 60% payload share are HEURISTIC
- Give adjacent sections different archetypes (e.g. split, strip, pinned scene, asymmetric bento, inverted band, list, statement), with at most 1 card grid and 1 inverted band per page and section padding from 3 tokens only (128/96/72), symmetric. _(support 1, basic, R1123)_
  - values: Section padding tokens: 128 / 96 / 72 px, symmetric top and bottom; card grids <= 1; inverted bands <= 1.
  - don't: Never let adjacent sections share an archetype, and do not use more than 1 card grid or 1 inverted band per page.

## spacing-scale
- Avoid random spacing values (10, 14, 23, 31) and inconsistent card padding; use one scale such as 4/8/16/24/32/48. _(support 1, basic, R0283)_
  - values: 4 / 8 / 16 / 24 / 32 / 48
  - do: consistent 16px padding on all cards
  - don't: mixing 10, 14, 23, 31 for no reason
- Use 8, 16, 24 and 32px for common layout spacing and 4px for finer adjustments; group related elements with smaller gaps. _(support 1, basic, R0291)_
  - values: 8 / 16 / 24 / 32; 4
- Treat 8px as a base unit for a spacing scale where each step has a job, rather than applying 8px to every gap. _(support 1, basic, R0339)_
  - values: 4px tight internal gaps; 8px icon-to-label, inset; 12px related items, list rows; 16px card padding; 24px between card groups; 32px section breathing room; 64px page-level sections
  - don't: All gaps = 8px: icon padding, section gap and list gaps identical, so there is no hierarchy
- Use one spacing scale for all margins, padding, layouts and components so everything snaps to the grid. _(support 1, basic, R0348)_
  - values: 4, 8, 12, 16, 24, 32, 48, 64; example card padding 16, gaps 24

## proximity-spacing
- Put the title and subtitle 8px apart and the subtitle and CTA 16px apart, so related text groups tighter than the action. _(support 2, basic, R0328)_
  - values: title-subtitle 8px, subtitle-CTA 16px
- Do not make all gaps equal: keep related elements closer and give unrelated groups more room. _(support 1, basic, R0501)_
  - values: 8 / 16 / 24 / 32 px
  - do: 8px title and description; 16px fields in the same form; 24px form content and action buttons; 32px between distinct sections

## css-grid
- Use CSS Grid (display: grid) for full-page layouts and complex sections where you need control over both rows and columns. _(support 1, basic, R0260)_
  - do: header / sidebar + content / footer
- Define equal flexible columns with fr units and space items with gap rather than margins. _(support 1, basic, R0261)_
  - values: gap: 20px; 1fr 1fr 1fr
  - do: .container{display:grid;grid-template-columns:1fr 1fr 1fr;gap:20px}
- Let featured items span multiple columns (grid-column: span 2) for hero or featured content inside a grid. _(support 1, basic, R0262)_
  - values: span 2, span 3
  - do: .item-large{grid-column:span 2}

## desktop-grid
- Set the desktop layout grid as a 1440px frame, 12 stretch columns, 96 margin and 24 gutter. _(support 1, basic, R0236)_
  - values: W 1440, 12 columns, stretch, margin 96, gutter 24
- Design desktop websites on a 1440px frame with side margins of at least 5% (typically 100px) and add padding inside the main container. _(support 1, basic, R0306)_
  - values: 1440px frame, 100px margins, 1240px safe area, 20px inner padding
- Desktop column grid on a 1440 frame: 12 columns, 20px gutter, 100px margin. _(support 1, basic, R0398)_
  - values: 12 cols, gutter 20px, margin 100px, column ~85px, 1440x1080

## proximity-chunking
- Make every gap an 8pt multiple, use a bigger gap to start a new content chunk, and align edges and text baselines. _(support 2, basic, R0247)_
  - values: 8pt multiples; 16pt padding; 4pt baseline (caption)
  - do: card: icon, title, body lines, full-width CTA on shared left edge

## soft-grid
- Prefer a soft point grid (spacing measured between elements) over a rigid hard grid, because it mirrors how elements are positioned in code. _(support 2, basic, R0466)_
  - values: 360 DP / 375 Pt width; 16 DP / 16 Pt margins
  - do: Android 360dp and iOS 375pt screens with 16 margins

## 8pt-spacing-scale
- Use a small, repeated spacing scale in multiples of 8 (with 4 for micro tweaks) instead of random values like 11, 19, 27 px. _(support 1, basic, R0496)_
  - values: 4 / 8 / 16 / 24 / 32 / 40 / 48 px
  - do: 4 micro tweak, 8 base unit, 16 small gap, 24 medium gap, 32 large gap, 48 section spacing
  - don't: 11px 19px 27px
- Pick spacing from an 8-point cheat sheet by role, using 4px as a half-step for fine adjustments. _(support 1, basic, R0499)_
  - values: 8 16 24 32 40 48 56 64; 4px half-step
  - do: 4px icon gaps; 8px closely related elements; 16px standard spacing; 24px card padding/content groups; 32px larger blocks; 40px generous layout spacing; 48px section spacing; 64px major section separation

## bento-layout
- Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead zones appear. _(support 1, basic, R0846)_
  - values: Complete row math examples: 4-column x 3-row matrix (12 units) = one 2x2 (4) + two 2x1 (4) + four 1x1 (4) = 12 units; 3-column x 2-row matrix (6 units) = one 2x1 (2) + one 1x1 (1) on Row 1, one 1x1 (1) + one 2x1 (2) on Row 2 (alternating 66%/33% and 33%/66% asymmetric zigzag). At tablet (768px–1023px), step down to a 2-column grid where 2x2/2x1 cells span 2 columns and 1x1 cells pair side-by-side.
  - do: Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead
  - don't: Do not use Pinterest-style waterfall masonry (columns: 3) for functional product bento grids or dashboard KPIs, as ragged bottom edges destroy horizontal scannability.
- Collapse multi-span bento grids into a single-column vertical stack below 768px while preserving the exact HTML DOM source order for keyboard focus and screen readers. _(support 1, basic, R0847)_
  - values: Mobile breakpoint rule (< 768px): grid-template-columns: 1fr; with all child cells reset to grid-column: span 1 !important; grid-row: span 1 !important; without using CSS order or grid-auto-flow: dense rearrangements that violate WCAG 1.3.2 Meaningful Sequence and 2.4.3 Focus Order.
  - don't: Never use CSS order or explicit grid-row/grid-column placement to visually move a bottom-of-DOM element to the top-left visual slot if it contains interactive focusable controls.

## card-aspect-ratio
- Use one aspect ratio for media in a card grid so rows line up. _(support 1, basic, R0155)_
  - values: 1:1 / 16:9 / 4:3
  - don't: Mixing 1:1, 16:9 and 4:3 cards in one row
- Use one consistent media aspect ratio across cards in a row so the rows line up. _(support 1, basic, R0196)_
  - values: 1:1, 16:9, 4:3 (mixed = wrong)
  - don't: Mixing 1:1, 16:9 and 4:3 cards in the same grid

## card-padding
- Wrap card-like components in 16px inner padding. _(support 1, basic, R0327)_
  - values: 16px padding
- Wrap compact components such as cards in 16 px padding for breathing room. _(support 1, basic, R0476)_
  - values: 16 px padding

## millers-law-chunking
- Break long content into small chunks (overview, key points, examples, takeaway) to reduce memory load. _(support 1, basic, R0028)_
  - don't: One long undivided text block
- Break content into smaller chunks, group related information and use short sections and steps to reduce memory load. _(support 1, basic, R0111)_
  - do: Overview / Key points / Examples / Takeaway blocks
  - don't: One long wall of text at once

## 4pt-grid
- Stick to a 4pt grid: if a spacing number is not divisible by 4, it does not ship. _(support 1, basic, R0273)_
  - values: divisible by 4

## 4pt-half-step
- Use 8pt as the main system and 4pt for tiny adjustments, composing values like 12 = 8 + 4 and 20 = 16 + 4. _(support 1, basic, R0281)_
  - values: 12 = 8+4, 20 = 16+4

## 4px-half-step
- Use multiples of 4, with 4px as a half-step for tight elements like icon plus label. _(support 1, basic, R0342)_
  - values: 4, 8, 12, 16, 24, 32, 48, 64; 4px = micro, 8px = base, 16px = card

## 4px-spacing-scale
- Make every padding, gap and margin a multiple of one 4px base unit, picked from a fixed spacing scale. _(support 1, basic, R0159)_
  - values: 4, 8, 12, 16, 24, 32, 48, 64 px

## 8pt-exceptions
- Treat the 8pt grid as a guideline: allow 4px between icons and labels, font sizes like 14/18/20px, 1-2px borders and fluid responsive widths that do not follow multiples of 8. _(support 1, basic, R0289)_
  - values: 4px, 14/18/20px type, 1px/2px borders, width 100%
  - don't: forcing every measurement onto 8

## balance
- Choose symmetry for an orderly feel or asymmetry for a dynamic feel, keeping visual weight balanced either way. _(support 1, basic, R0411)_

## bleed-needs-reason
- Only an element whose meaning is extent (a timeline, a to-scale bar) leaves the grid (DIRECT failure v7 L142); nothing inside the wrap overhangs it by a few px. _(support 1, basic, R0675)_
  - values: Overhang is 0px except for extent elements.
  - do: Let only extent-bearing elements (a timeline, a to-scale bar) leave the grid and keep everything else inside the wrap.
  - don't: Do not let an element overhang the wrap by a few px.

## breakpoints
- Design responsive layouts against standard breakpoints: mobile <=480px and <=768px, tablet <=834px and <=1024px, laptop/desktop <=1440px. _(support 1, basic, R0246)_
  - values: 480, 768, 834, 1024, 1440px

## column-count
- Keep the column count even (12, 8 or 4) and define columns in percentages so content adapts to any screen. _(support 1, basic, R0223)_
  - values: 12, 8 or 4 columns

## consistent-aspect-ratios
- Use one aspect ratio for all cards in a row so images and text baselines line up. _(support 1, basic, R0008)_
  - values: 1:1, 16:9, 4:3
  - don't: Mixing 1:1, 16:9 and 4:3 cards in one row

## container-alignment
- Make outer-most containers span whole column widths aligned to the grid, and add padding inside them. _(support 1, basic, R0402)_

## css-grid-alignment
- Use place-items (shorthand for align-items + justify-items) to align content inside CSS grid cells on both axes in one line. _(support 1, basic, R0337)_
  - do: display:grid; place-items:center;

## css-grid-self-alignment
- Align individual CSS grid items inside their cell with align-self (block axis) and justify-self, or both at once with the place-self shorthand. _(support 1, basic, R0473)_
  - do: align-self: start|end|center|stretch; place-self: center stretch;

## declutter-mobile-app
- Simplify cluttered app screens: fewer competing banners and cards, clear visual hierarchy, prominent search and navigation, large imagery and legible typography with ample spacing. _(support 1, basic, R0019)_
  - don't: Too many competing banners, text overlaps, hidden navigation/search, too many tiny elements

## device-width
- When a device width is not divisible by 8, pick a sensible middle ground rather than forcing the grid. _(support 1, basic, R0233)_

## fixed-grid
- For fixed desktop grids use 12 columns of 65-80px with 15-30px gutters, giving about a 1100px box on a 1440px frame. _(support 1, basic, R0318)_
  - values: 12 columns, 65-80px columns, 15-30px gutters, 1100px box width, 1440px frame

## fixed-vs-fluid-grid
- Use a fixed grid on desktop and a fluid grid on tablets and phones. _(support 1, basic, R0396)_

## fluid-grid
- For fluid desktop grids use a 1440px frame, 96px side margins and a 12-column grid with 24px gutters. _(support 1, basic, R0319)_
  - values: 1440px frame, 96px margins, 1248px safe area, 12 columns, 24px gutters

## form-spacing
- Keep helper/acceptance text close to the button it belongs to so the relation is clear, and leave space above the keyboard. _(support 1, basic, R0314)_
  - values: 24px text-to-button, 16px button-to-keyboard, 56px button height, 343px button width on 375px frame, 80px gap above

## four-fixes
- Fix a generic-looking site with four basics: one headline per view (cut noise), generous spacing, everything aligned to one edge, and one primary button instead of many. _(support 1, basic, R0211)_
  - don't: Eight competing buttons

## gap-encodes-hierarchy
- Use small gaps for the same idea and larger gaps for a new section, and never use the same spacing for different relationships. _(support 1, basic, R0340)_
  - values: gaps scale 4 -> 8 -> 16 -> 32

## gutter-width
- Keep gutter width fixed while column width flexes; on an 8pt system use a 32px gutter (16px each side). _(support 1, basic, R0224)_
  - values: 32px gutter (16px each side); libraries typically 30px

## hero-fold
- If the hero product image is the LCP candidate, include it in the initial HTML (or preload it when referenced only from CSS), use fetchpriority=high and never lazy-load it. _(support 1, basic, R1118)_
  - values: fetchpriority="high"
  - don't: Do not raise priority on several images without reason; that reduces the value of prioritisation.

## inner-padding
- Inside containers you need not snap elements to columns, but use round numbers and at least 20-40px of inner padding so elements do not overlap when resized. _(support 1, basic, R0403)_
  - values: 20-40px inner padding

## minimalism
- Keep layouts minimal: simple structure, limited colour palette, clear typography and strategic whitespace. _(support 1, basic, R0245)_

## mobile-first
- Design the mobile version first with a single column, larger tap targets, readable type and one clear CTA, and test at 375px width. _(support 1, basic, R0117)_
  - values: 375px test width
  - don't: Desktop layout squeezed onto a phone

## mobile-safe-area
- Reserve at least 44px at the top for the iOS status bar and place nothing else in that zone. _(support 1, basic, R0237)_
  - values: 44px

## no-auto-margin-fill
- Extends AR36 (DIRECT, v8 L490): no flex auto-margin fills a column. _(support 1, basic, R0755)_
  - values: No `margin: auto` fill in columns.
  - do: Size column content explicitly instead of letting a flex auto-margin fill the column.
  - don't: Do not use a flex auto-margin to fill a column.

## proportional-spacing
- Give outer containers proportionally more spacing than inner elements, in 8px/4px increments taken from the spacing variables, and replace arbitrary spacing values with tokens. _(support 1, basic, R0454)_
  - values: 8px/4px increments

## proximity-grouping
- Group form labels with their fields and separate unrelated groups with extra space so layout communicates structure. _(support 1, basic, R0029)_
  - do: Separate 'Personal information' and 'Billing address' blocks with clear gaps
  - don't: Uniform spacing between all fields

## responsive-columns
- Define columns, gutters, margins, breakpoints and container widths as system rules: 4 columns on mobile, 8 on tablet, 12 on desktop. _(support 1, basic, R0422)_
  - values: Mobile 4, Tablet 8, Desktop 12 columns

## safe-area
- Use 100dvh (not 100vh) for full-height mobile layouts. _(support 1, basic, R0931)_
  - values: min-height: 100vh; min-height: 100dvh; (dvh as the enhancement; vh as the fallback for older browsers).
  - don't: Do not size full-height mobile layouts with 100vh, which ignores the dynamic browser toolbars and can clip bottom controls.

## scale-priority
- Use size to signal priority: the biggest element is where users look first. _(support 1, basic, R0410)_

## scanning-patterns
- Lay out pages for scanning in predictable F and Z patterns, since people scan rather than read. _(support 1, basic, R0263)_

## scanning-whitespace
- Design for scanning (F and Z patterns) so key information stands out in seconds, and use whitespace intentionally as structure. _(support 1, basic, R0488)_

## section-boundary
- Set spacing once per boundary, not per section: 96px on each side of a hairline or surface change; one token (96-128px) in total where there is neither. A section that opens with a rule has no top padding. _(support 1, basic, R0588)_
  - values: Audit heuristic: no ink-free band above 160px at 1440x900 or 140px at 390x844 at any scroll position.
  - do: Set spacing once per boundary: 96px on each side of a hairline or surface change, one 96-128px token in total where there is neither, and no top padding on a section that opens with a rule.
  - don't: Do not stack bottom padding of one section on top padding of the next.

## selective-attention
- Use clear hierarchy and avoid clutter; put the element tied to the user goal in the most prominent spot because users miss what seems irrelevant. _(support 1, basic, R0461)_
  - do: Netflix autoplay hero at top; Amazon trending banner below nav

## sticky-header
- Offset every full-viewport pinned stage by the sticky header height (`top: var(--header-height); height: calc(100dvh - var(--header-height));`) and set `scroll-padding-top: var(--header-height)` on `html` so the visual center of the pinned stage aligns with the optical center of the remaining viewport rather than centering behind the translucent nav bar. _(support 1, basic, R0533)_
  - values: `top: 64px; height: calc(100dvh - 64px);` on desktop; `top: 56px; height: calc(100dvh - 56px);` on mobile; `html { scroll-padding-top: var(--header-height); }`. codex: offset the stage sticky top by the real header height; sticky is relative to the nearest ancestor with a scrolling mechanism, so measure header and stage bounding boxes at the target viewport.
  - don't: Do not offset `top: var(--header-height)` when the sticky stage contains a full-bleed background canvas/shader that must show through a frosted-glass header (`backdrop-filter: blur(12px)`); in that case, keep the stage at `top: 0; height: 100dvh` and apply `padding-top: var(--header-height)` inside the stage's inner content grid so the canvas bleeds behind the header while content centers in the safe zone.
  - group: pinned-scene-always-on

## tablet-grid
- Tablet column grid: 8 columns, 24px gutter, 88px margin. _(support 1, basic, R0399)_
  - values: 8 cols, gutter 24px, margin 88px, column ~85px, iPad Pro 12.9

## text-container
- For a screen title use about 28px font with 8px between title and description, and start the text block about 96px from the top. _(support 1, basic, R0239)_
  - values: 28px title, 8px gap, ~80px block, 96px from top

## uniform-grid
- Use a uniform grid of evenly spaced rows and columns to keep spacing and alignment consistent across the design. _(support 1, basic, R0471)_

## web-column-grid
- Start desktop web designs on a 12-column grid with 100px side margins and 20px gutters in a 1440px frame (about 1240px content width). _(support 1, basic, R0229)_
  - values: 12 columns, 100px margins, 20px gutters, 1440px frame, ~1240px content

## white-space
- Use generous white space so content can breathe and the eye is guided to what matters. _(support 1, basic, R0385)_
  - values: 32px, 40px, 86px
  - do: Mobile hero: 32px and 40px vertical gaps between icon, headline and input, 86px input zone

## whitespace
- Give content room to breathe; if a layout feels crowded, remove something rather than shrink spacing. _(support 1, basic, R0148)_

## Checklist

- [ ] Size and position containers on the 8-point grid because most popular screen sizes are divisible by 8, so layouts can be subdivided without decimals.
- [ ] Make every gap an 8pt multiple, use a bigger gap to start a new content chunk, and align edges and text baselines.
- [ ] Put the title and subtitle 8px apart and the subtitle and CTA 16px apart, so related text groups tighter than the action.
- [ ] Prefer a soft point grid (spacing measured between elements) over a rigid hard grid, because it mirrors how elements are positioned in code.
