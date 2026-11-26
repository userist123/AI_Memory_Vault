---
id: core-layout-more
title: Spacing & layout (intermediate)
tier_min: T3
page_types: ["any"]
topics: ["bento-grid", "baseline-grid", "grid-types", "mobile-layout", "soft-vs-hard-grid", "4pt-subgrid", "5pt-vs-8pt-grid", "8pt-grid", "8pt-vs-5pt", "anchor-positioning", "button-spacing", "connector-lanes", "content-sized-panels", "css-grid-alignment", "css-grid-areas"]
rules: 34
---

# Spacing & layout (intermediate)

Detailed grids, breakpoints, density.
Support = number of independent educators who teach the rule.

## baseline-grid
- Align text to a 4pt baseline for vertical rhythm; text centred inside a button or list row does not need to sit on the baseline. _(support 2, intermediate, R0225)_
  - values: 4pt baseline
  - do: label-to-field gap 4
- Use a baseline (row) grid so typography and other elements rest on consistent horizontal alignment and spacing. _(support 1, intermediate, R0472)_

## bento-grid
- Use bento grids (lunch-box style tiles of varied size) for responsive feature sections that stay consistent across devices. _(support 1, intermediate, R0243)_
- Use bento feature grids deliberately: modular card grids for long feature lists are now the SaaS default and edging into cliché, so vary them to avoid a template look. _(support 1, intermediate, R0300)_
- Build a bento grid by first laying a column/row layout grid on a rounded frame, then merging cells into tiles of different sizes so the overall shape stays a clean rectangle or square. _(support 1, intermediate, R0474)_
  - values: 1080x1080 px frame; rows 6, columns 4; margin 40; gutter 24
  - do: 1080x1080 rounded frame; 6 rows + 4 columns, stretch, margin 40, gutter 24; fill each tile with an image, type specimen or component

## grid-types
- Pick the grid type for the job: radial grid for a single focal point, modular grid for consistent but flexible tiles, column grid for readable cross-screen layouts, hierarchical grid to steer visual flow, baseline grid for typographic rhythm. _(support 1, intermediate, R0253)_
- Choose among radial (focal point), modular (consistent tiles with freedom), axial (structure along one clear axis), column (readability across screens) and hierarchical (guided visual flow) grids according to the layout's goal. _(support 1, intermediate, R0285)_

## mobile-layout
- Set an eligible query container and size internal scene offsets with container units (`cqw`, `cqi`) so they follow the component width without JS resize listeners, with a grid/flex or media-query fallback. _(support 1, intermediate, R0633)_
  - values: 1cqw = 1% of the query container width; support starts at Chrome 105, Safari 16, Firefox 110.
  - do: Use `container-type: inline-size` on the scene wrapper and express offsets in `cqi`; add an `@supports not (width: 1cqi)` fallback.
  - don't: Do not rely on `cqh` unless the container is `container-type: size` with an explicit height; do not use it when the offset depends on auto-growing text height.
  - disagrees: gemini-hydrogen says `cqh` computes to 0 without `container-type: size`; the container-query units fall back to small viewport units when no eligible container exists, so the failure is a wrong size rather than 0; verify on the target browsers.
- Flatten nested container hierarchies on mobile viewports (< 640px) by using full-width grouped sections or single-level inset cards with a 16px screen margin instead of nesting boxed cards inside boxed cards. _(support 1, intermediate, R0900)_
  - values: Mobile horizontal screen budget (375px–393px viewport width): Outer screen margin = 16px left/right (leaving 343px–361px content width); If a desktop view has an outer panel (24px padding) containing inner cards (24px padding), collapse the outer box on mobile so only one level of 16px padding separates text from the viewport edge (recovering 64px of lost horizontal text width).
  - do: Flatten nested container hierarchies on mobile viewports (< 640px) by using full-width grouped sections or single-level inset cards with a 16px screen margin instead of
  - don't: Never nest 3 levels of bordered/padded cards on a 375px mobile screen, which squeezes inner text columns down to < 220px (~22 characters per line).

## soft-vs-hard-grid
- Use vertical columns for horizontal placement and 4-8px spacing increments (soft grid) for vertical spacing instead of strict baseline grids. _(support 1, intermediate, R0308)_
  - values: 4-8px increments
- Skip row/baseline grids; fit elements into vertical columns and space them in consistent 8px or 5px increments. _(support 1, intermediate, R0320)_

## 4pt-subgrid
- Align icons, type and small elements to a 4-point grid for finer control while layout stays on 8. _(support 1, intermediate, R0388)_
  - values: 16, 72x72, 24x24, 16x16, 8
  - do: Card: 16px padding, 72x72 avatar, 24x24 icon, 16x16 badge, 8px gap between avatar and text

## 5pt-vs-8pt-grid
- Choose a 5px grid for compact fixed-column layouts and an 8px grid for fluid layouts that need more breathing room. _(support 1, intermediate, R0317)_
  - values: 8px: 8,16,24,32,40,48,56,64; 5px: 5,10,15,20,30,40,50,60,70

## 8pt-grid
- Size component widths as a count of grid increments times the increment. _(support 1, intermediate, R0305)_
  - values: 384px = 24x16px
  - do: A card 24 increments of 16px = 384px wide

## 8pt-vs-5pt
- Use a 5px grid when working with fixed Bootstrap column layouts that need compact spacing; use 8px for fluid layouts that benefit from more breathing room. _(support 1, intermediate, R0228)_
  - values: 5px vs 8px

## anchor-positioning
- Use CSS anchor positioning for HTML elements tethered to a named anchor (callouts, labels, popovers), with `@position-try` fallbacks for overflow, and keep a feature-query or JS fallback until the target browser matrix is confirmed. _(support 1, intermediate, R0635)_
  - values: support dates reported by gemini-hydrogen (Chrome/Edge 125, Safari 26, Firefox 147, newly available baseline Jan 2026) are unverified.
  - do: Declare `anchor-name` and `position-anchor`, position with `anchor()`, add `position-try-fallbacks`, and wrap in `@supports (anchor-name: --a)`.
  - don't: Do not state a browser-support percentage from these results; do not use it for SVG path routing.
  - caveat: codex: MDN documents usage but no support percentage or full browser matrix was verified; check current compatibility data before relying on the dates.

## button-spacing
- Space adjacent buttons 12-48px apart, using more space for small buttons and less for large ones. _(support 1, intermediate, R0311)_
  - values: small ~42px buttons: 36-48px gap; medium ~60px: 25-36px; large ~72px: 15-24px

## connector-lanes
- Connectors live in a reserved empty lane between source and targets (DIRECT failure v7 L273-276); targets sit in columns ordered like their sources; each source x lies inside its target's column; drops are straight; jogs share one lane with disjoint x ranges. _(support 1, intermediate, R0668)_
  - values: Lane 28px in the hero, 16px in panels.
  - do: Reserve an empty lane for connectors, order target columns like their sources, keep each source x inside its target's column, draw straight drops and give jogs disjoint x ranges in one lane.
  - don't: Do not route a connector across any card but its target. Test: connector rects intersect no card except the target.

## content-sized-panels
- Panels are sized by content (DIRECT, L803, L806, L837, L842): no viewport-fitted height on a fixed panel; free space <= 16px; every DOM row shows at the top tier; slack goes to content, never to padding. _(support 1, intermediate, R0800)_
  - values: Free space <= 16px per panel state.
  - do: Size fixed panels by their content, keep free space at 16px or less, show every DOM row at the top tier and spend slack on content.
  - don't: Do not fit a fixed panel's height to the viewport or absorb slack in padding.

## css-grid-alignment
- Use CSS Grid alignment properties instead of floats/positioning: justify-items/align-items align items in their cells, justify-content/align-content align the whole grid in its container, justify-self/align-self override per item. _(support 1, intermediate, R0400)_
  - values: start | end | center | stretch | space-around | space-between | space-evenly

## css-grid-areas
- Name page regions with grid-template-areas (header, sidebar, main, ads, footer) so complex layouts are readable and can be reordered without touching the HTML. _(support 1, intermediate, R0338)_
  - values: grid-template-columns: 200px 1fr 200px; grid-template-rows: 80px 1fr 80px

## css-grid-repeat
- Simplify grid templates with repeat() and fr units, and set gaps with the gap shorthand. _(support 1, intermediate, R0401)_
  - values: repeat(3, 1fr)
  - do: grid-template-columns: repeat(3, 1fr); gap: 15px 10px;

## hard-vs-soft-grid
- Prefer the soft grid method (space components relative to each other in 8pt increments) for flexible UIs; use the hard grid (snap everything to predefined 8pt cells) when strict rhythm is required. _(support 1, intermediate, R0389)_
  - values: 8 Pt, 16 Pt

## hero-fold
- Reserve the display box of every hero image, video, canvas and interactive widget before it loads, with `width`/`height` attributes, `aspect-ratio` or `min-height`, so state toggles and late loads cause zero layout shift. _(support 1, intermediate, R0602)_
  - values: CLS <= 0.1 at p75 (web.dev threshold), target 0.0 in the hero.
  - do: Set `aspect-ratio` (or width and height) on the hero media and on the demo widget wrapper, and keep tooltips/dropdowns in absolutely positioned layers.
  - don't: Do not let hero media dimensions be decided only after a late load; do not animate width, height, top or left to open hero widgets.

## icon-text-gap
- Keep the gap between an avatar/icon and its text consistent with the padding around the icon instead of squeezing it. _(support 1, intermediate, R0221)_
  - values: 11.2 vs 6
  - do: 11.2 padding around icon and matching gap
  - don't: 11.2 outer padding with a 6 gap to the text

## keylines
- Establish consistent keylines across screens as a simpler alternative to nesting columns. _(support 1, intermediate, R0226)_

## optical-button-padding
- Make button horizontal padding optically equal to vertical padding by compensating for line-height vs cap height. _(support 1, intermediate, R0218)_
  - values: visible 14 = 14
  - do: font-size: 16px; line-height: 1.5; --py: 0.5em; --px: calc(var(--py) + (1lh - 1cap) / 2); padding: var(--py) var(--px);

## optical-compensation
- Optically compensate padding around text and icons when equal pixel values look unbalanced. _(support 1, intermediate, R0341)_
  - values: 14px/18px vs 16px/16px
  - do: 14px top / 18px bottom so the content feels centered
  - don't: 16px on all sides that looks top-heavy

## pinned-scroll-story
- Eliminate the entry/exit dead band around a pinned stage (`height: calc(100dvh - var(--header-height))`) by factoring the internal whitespace of the pinned stage into the outer section padding: if the pinned stage internally centers a `520px`-tall visual inside an `836px` stage (leaving `158px` internal top and bottom empty buffer), reduce the external margin/padding of the preceding and following sections to `0px-32px`. _(support 1, intermediate, R0537)_
  - values: Let `B_internal = (StageHeight - ContentHeight) / 2`. Set `Padding_external = max(24px, TargetGap_96px - B_internal)`. For a `900px` viewport, `64px` header, `836px` stage, and `540px` card (`B_internal = 148px`), `Padding_external = 24px`.
  - don't: Do not reduce external padding to `24px` when the pinned stage's background fills `100%` of the `836px` frame edge-to-edge with a bordered panel or distinct background color; in that case the stage boundary itself is a visible edge and requires full `80px-96px` external clearance.
  - caveat: correlated with the v6 review (~180px dead band)

## row-grid
- Use row grids (e.g. 8px height, 8px gutter, 8px offset, high count) for structured vertical layout in complex web and mobile dashboard views. _(support 1, intermediate, R0230)_
  - values: row height 8px, gutter 8px, offset 8px, count 800

## safe-area
- Size the pinned stage in `100svh` (with a `vh` fallback) so scrub progress does not jump when the mobile URL bar moves; use `dvh` only on a stage that must fill the screen edge to edge. _(support 1, intermediate, R0634)_
  - values: viewport units: Chrome 108+, Safari 15.4+, Firefox 101+.
  - do: Set the stage `height: 100vh; height: 100svh;` and keep the track in `svh` or `vh`.
  - don't: Do not put `dvh` on the track length; do not use `svh` on non-scrubbed full-screen modals where resizing with the URL bar is wanted.
  - disagrees: dvh fills the space as the URL bar retracts but resizes during scroll; whether that destabilises scrubbed offsets is a practitioner judgement, not stated on the web.dev page.

## section-rhythm
- When transitioning out of a tall pinned stage (`height: 300vh-400vh`) into the next normal-flow section, insert a visual horizon anchor—either a `1px` full-width architectural hairline with a centered or left-aligned section index pill (`[03 / ARCHITECTURE]`) overlapping or sitting `48px` below the line, or a surface luminance shift (`#12100E` to `#1A1714`)—so the user immediately registers that vertical scroll has resumed normal 1:1 document flow. _(support 1, intermediate, R0574)_
  - values: Horizon divider `1px solid var(--border-subtle)`; index pill offset `32px-48px` below unpin boundary; first normal-flow heading appears within `64px-96px` of the unpin boundary.
  - don't: Do not leave `>128px` of blank canvas below the bottom of a pinned stage before the next section's border or heading enters; after `300vh` of pinned scrolling where vertical page movement was suspended, a `160px+` empty gap makes users think the page is broken or still pinned.

## shared-rows
- Anything that must align across sibling cards gets its own shared grid row (subgrid). `margin-top:auto` aligns only the last element, so nothing of variable length may follow it. _(support 1, intermediate, R0594)_
  - values: grid-template-rows: subgrid; one shared row per aligned element.
  - do: Give every element that must align across sibling cards its own shared subgrid row.
  - don't: Do not rely on `margin-top: auto` for alignment when anything of variable length follows the element.

## tall-bento-cell
- A tall cell holds a vertical artifact of at least 60% of its height (DIRECT failure v7 L318/L326); `margin-top:auto` never fills a cell. _(support 1, intermediate, R0678)_
  - values: Artifact >= 60% of cell height.
  - do: Fill a tall bento cell with a vertical artifact of at least 60% of the cell height.
  - don't: Do not use `margin-top: auto` to fill a tall cell.

## Checklist

- [ ] Align text to a 4pt baseline for vertical rhythm; text centred inside a button or list row does not need to sit on the baseline.
